using System.Drawing;
using System.Management.Automation;
using StatusLine.Refreshing;

namespace StatusLine;

/// <summary>Configuration for the background updater.</summary>
internal sealed record UpdaterOptions(
    BlockSelector[] Selection,
    BlockOverrides Overrides,
    TimeSpan Interval,
    bool BlockSeparator,
    SegmentStyle Style,
    IconSet IconSet,
    SessionState SessionState,
    string? PowerShellVersion,
    StatusLineTarget Target,
    string OutputPath,
    Guid RunspaceId,
    Color StartColor,
    Color EndColor,
    int BlockTimeoutMilliseconds,
    bool UpdateDuringCommand,
    int IdleTimeoutSeconds,
    string? WeatherLocation = null,
    bool BlendBackground = false,
    TextContrastMode TextContrast = TextContrastMode.Automatic,
    int CompletionMessageThresholdMilliseconds = 0);

/// <summary>
/// Renders immutable session snapshots on a dedicated thread. Script and built-in I/O
/// providers refresh independently, without blocking the renderer or reading live session state.
/// </summary>
internal static class StatusLineUpdater {
    private static readonly Lock Gate = new();
    internal static readonly object OutputGate = new();
    private static readonly AutoResetEvent WakeSignal = new(false);
    private static Thread? thread;
    private static CancellationTokenSource? cancellation;
    private static bool foregroundOutput;
    private static StatusLineOutput? currentOutput;
    private static Guid owner;
    private static UpdaterOptions? liveOptions;
    private static SessionFacts requiredFacts;
    private sealed record CommandActivity(string Name, DateTimeOffset Started);
    private sealed record StatusMessage(string Text, long Expires);
    private static volatile CommandActivity? commandActivity;
    private static volatile StatusMessage? message;

    internal static void CommandAccepted(Guid runspaceId, string? line) {
        lock (OutputGate) {
            if (owner != runspaceId) { return; }
            commandActivity = string.IsNullOrWhiteSpace(line) ? null :
                new(line.Trim().Split([' ', '\r', '\n', '\t'], 2)[0], DateTimeOffset.UtcNow);
        }
        SetExecuting(runspaceId, !string.IsNullOrWhiteSpace(line));
    }

    internal static void EditorModeChanged(Guid runspaceId) {
        lock (OutputGate) {
            if (owner != runspaceId) { return; }
            activity.Touch();
        }
        Wake();
    }

    internal static void UpdateJobs(Guid runspaceId, int count) {
        lock (OutputGate) {
            if (owner != runspaceId || initialState is not { } state || state.RunningJobCount == count) { return; }
            initialState = state with { RunningJobCount = count };
            activity.Touch();
        }
        Wake();
    }

    internal static void ShowMessage(string text, int durationMilliseconds) {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationMilliseconds);
        lock (OutputGate) {
            if (liveOptions is null || owner != System.Management.Automation.Runspaces.Runspace.DefaultRunspace?.InstanceId) {
                throw new InvalidOperationException("No running statusline is owned by the calling runspace.");
            }
            message = new(text, Environment.TickCount64 + durationMilliseconds);
            activity.Touch();
        }
        Wake();
    }

    internal static void ClearMessage() {
        lock (OutputGate) {
            if (owner != System.Management.Automation.Runspaces.Runspace.DefaultRunspace?.InstanceId) { return; }
            message = null;
            activity.Touch();
        }
        Wake();
    }

    internal static void CommandCompleted(Guid runspaceId, int code, TimeSpan? duration) {
        lock (OutputGate) {
            if (owner != runspaceId || liveOptions is not { CompletionMessageThresholdMilliseconds: > 0 } options ||
                duration is not { } elapsed || elapsed.TotalMilliseconds < options.CompletionMessageThresholdMilliseconds) { return; }
            string result = code == 0 ? "Completed" : $"Failed ({code})";
            message = new(result + " in " + BlockFormatting.FormatDuration(elapsed), Environment.TickCount64 + 4000);
            activity.Touch();
        }
        Wake();
    }

    internal static SessionFacts RequiredFacts(Guid runspaceId) {
        lock (OutputGate) {
            return owner == runspaceId ? requiredFacts : SessionFacts.None;
        }
    }
    internal static bool NeedsObservation(Guid runspaceId) {
        lock (OutputGate) {
            return owner == runspaceId && liveOptions is { } options &&
                (requiredFacts.HasFlag(SessionFacts.Jobs) || options.Selection.Any(block =>
                    block.Implementation?.Metadata.Kind == StatusLineBlockKind.ViMode));
        }
    }
    private static StatusBarConfiguration? configuration;
    private static RefreshActivity activity = new(TimeSpan.Zero);
    public static string? LastError { get; private set; }
    public static bool IsRunning {
        get {
            lock (Gate) {
                return thread is not null;
            }
        }
    }

    public static void Start(UpdaterOptions options, PSEventManager events) {
        lock (Gate) {
            if (thread is not null && owner != options.RunspaceId) {
                if (thread.IsAlive) {
                    throw new StatusLineOwnershipException();
                }

                StopCore(restoreFunctions: false);
            }

            StopCore();
            owner = options.RunspaceId;
            lock (OutputGate) {
                liveOptions = options;
                requiredFacts = BlockSelector.RequiredFacts(options.Selection);
                configuration = StatusBarConfiguration.From(options);
            }

            activity = new(TimeSpan.FromSeconds(options.IdleTimeoutSeconds));
            initialState = StatusLineState.Capture(options.SessionState, requiredFacts);
            lock (OutputGate) {
                IsCommandExecuting = false;
                foregroundOutput = false;
            }

            LastError = null;
            StatusLineActivity.Install(owner, events);
            cancellation = new CancellationTokenSource();
            CancellationToken token = cancellation.Token;
            var output = new StatusLineOutput(options, token);
            currentOutput = output;
            thread = new Thread(() =>
                Run(options, output, token)) {
                IsBackground = true,
                Name = "StatusLine",
                Priority = ThreadPriority.BelowNormal,
            };
            thread.Start();
        }
    }

    internal static StatusBarConfiguration? GetConfiguration() {
        lock (Gate) {
            return thread is not null && owner == System.Management.Automation.Runspaces.Runspace.DefaultRunspace?.InstanceId
                ? configuration!.Copy() : null;
        }
    }

    internal static void Configure(StatusBarConfiguration replacement) {
        ArgumentNullException.ThrowIfNull(replacement);
        lock (Gate) {
            if (thread is null || owner != System.Management.Automation.Runspaces.Runspace.DefaultRunspace?.InstanceId) {
                throw new InvalidOperationException("No running statusline is owned by the calling runspace.");
            }

            UpdaterOptions updated = replacement.Compile(liveOptions!);
            StatusBarConfiguration snapshot = replacement.Copy();
            lock (OutputGate) {
                liveOptions = updated;
                requiredFacts = BlockSelector.RequiredFacts(updated.Selection);
                configuration = snapshot;
                currentOutput!.UpdateOptions(updated);
                activity = new(TimeSpan.FromSeconds(updated.IdleTimeoutSeconds));
            }
        }

        Wake();
    }
    public static bool Stop() {
        lock (Gate) {
            if (thread is not null &&
                owner != System.Management.Automation.Runspaces.Runspace.DefaultRunspace?.InstanceId) {
                return false;
            }

            StopCore();
            return true;
        }
    }

    /// <summary>Renders early, e.g. when a script definition produced new output.</summary>
    public static void Wake() => WakeSignal.Set();

    internal static void SetExecuting(Guid runspaceId, bool value) {
        lock (OutputGate) {
            if (owner != runspaceId) {
                return;
            }

            IsCommandExecuting = value;
            if (!value) { commandActivity = null; }
            if (value) {
                // Acceptance and transient redraw are complete; command output may now begin.
                foregroundOutput = false;
            }

            activity.Touch();
        }

        Wake();
    }

    internal static void SetForegroundOutput(Guid runspaceId, bool value) {
        lock (OutputGate) {
            if (owner != runspaceId) {
                return;
            }

            foregroundOutput = value;
        }

        if (!value) {
            Wake();
        }
    }

    internal static void FlushHost(Guid runspaceId) {
        lock (OutputGate) {
            if (owner == runspaceId) {
                currentOutput?.FlushPending();
            }
        }
    }

    internal static void OnIdle(Guid runspaceId) {
        lock (OutputGate) {
            if (owner != runspaceId ||
                IsCommandExecuting ||
                activity.IsIdle ||
                !activity.CheckIdle()
            ) {
                return;
            }
        }

        Wake();
    }

    // Called under OutputGate when a frame no longer fits the terminal.
    internal static void RefreshAfterResize() {
        activity.Touch();
        Wake();
    }

    internal static void OnExit(Guid runspaceId) {
        lock (Gate) {
            if (owner == runspaceId) {
                cancellation?.Cancel();
                Wake();
            }
        }
    }

    internal static void UpdateSnapshot(Guid runspaceId, StatusLineState state) {
        lock (OutputGate) {
            if (owner == runspaceId) {
                initialState = state;
            }
        }
    }

    internal static void ReleaseOwner(Guid runspaceId) {
        lock (Gate) {
            if (owner == runspaceId) {
                StopCore(restoreFunctions: false);
            }
        }
    }

    private static void StopCore(bool restoreFunctions = true) {
        if (thread is null) {
            return;
        }

        cancellation!.Cancel();
        if (!thread.Join(TimeSpan.FromSeconds(5))) {
            throw new TimeoutException("Status-line worker has not stopped; its ownership is retained.");
        }

        StatusLineActivity.Uninstall(restoreFunctions);
        cancellation.Dispose();
        cancellation = null;
        thread = null;
        currentOutput = null;
        liveOptions = null;
        configuration = null;
        initialState = null;
        commandActivity = null;
        message = null;
        EditorContext.Clear(owner);
        requiredFacts = SessionFacts.None;
        owner = Guid.Empty;
    }

    private static void Run(UpdaterOptions options, StatusLineOutput output, CancellationToken token) {
        WaitHandle[] waitHandles = [WakeSignal, token.WaitHandle];
        while (!token.IsCancellationRequested) {
            options = Volatile.Read(ref liveOptions)!;
            bool paused;
            lock (OutputGate) {
                paused = !IsCommandExecuting &&
                activity.CheckIdle() && message is null;
            }

            if (paused) {
                WaitHandle.WaitAny(waitHandles);
                continue;
            }

            try {
                output.Draw(CreateBlocks(options), options);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException) {
                LastError = exception.Message;
            }

            WaitHandle.WaitAny(waitHandles, options.Interval);
        }

        output.Clear();
    }

    internal static bool IsCommandExecuting { get; private set; }
    internal static bool IsIdle => activity.IsIdle;

    // The caller holds OutputGate.
    internal static bool IsLive(UpdaterOptions options) => ReferenceEquals(options, liveOptions);

    internal static bool CanDrawHost(UpdaterOptions options, CancellationToken token) =>
        !token.IsCancellationRequested && !foregroundOutput && (!activity.IsIdle || message is not null) && (!IsCommandExecuting || options.UpdateDuringCommand);

    internal static void ReportError(string? error) =>
        LastError = error;

    private static List<StatusLineBlock> CreateBlocks(UpdaterOptions options) {
        if (message is { } current) {
            if (Environment.TickCount64 < current.Expires) {
                return [new StatusLineBlock(current.Text, Color.White, Color.Empty)];
            }
            lock (OutputGate) {
                if (ReferenceEquals(message, current)) {
                    message = null;
                    activity.Touch();
                }
            }
        }
        EditorContext? editor = EditorContext.Get(options.RunspaceId);
        CommandActivity? running = commandActivity;
        var context = new BlockContext {
            State = initialState! with {
                ViMode = editor?.ViMode,
                RunningCommand = running?.Name,
                CommandStartedAt = running?.Started,
            },
            PowerShellVersion = options.PowerShellVersion,
            IconSet = options.IconSet,
            SlowProviderWait = TimeSpan.Zero,
            RunspaceId = options.RunspaceId,
            BlockTimeoutMilliseconds = options.BlockTimeoutMilliseconds,
            WeatherLocation = options.WeatherLocation,
        };

        return BlockEvaluator.Create(options.Selection, context, options.Overrides);
    }

    private static volatile StatusLineState? initialState;
}

/// <summary>The terminal status line is owned by a different, still-running runspace.</summary>
public sealed class StatusLineOwnershipException() : InvalidOperationException("The terminal status line is owned by another runspace; disable it there first.");
