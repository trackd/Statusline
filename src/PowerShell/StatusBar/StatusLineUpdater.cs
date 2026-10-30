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
    TextContrastMode TextContrast = TextContrastMode.Automatic, bool DurationWarning = false);

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

    internal static SessionFacts RequiredFacts(Guid runspaceId) {
        lock (OutputGate) {
            return owner == runspaceId ? requiredFacts : SessionFacts.None;
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
            StatusLineActivity.Install(owner, events, options.Target == StatusLineTarget.HostWritable);
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
                activity.CheckIdle();
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
        !token.IsCancellationRequested && !foregroundOutput && !activity.IsIdle && (!IsCommandExecuting || options.UpdateDuringCommand);

    internal static void ReportError(string? error) =>
        LastError = error;

    private static List<StatusLineBlock> CreateBlocks(UpdaterOptions options) {
        var context = new BlockContext {
            State = initialState!,
            PowerShellVersion = options.PowerShellVersion,
            IconSet = options.IconSet,
            SlowProviderWait = TimeSpan.Zero,
            RunspaceId = options.RunspaceId,
            BlockTimeoutMilliseconds = options.BlockTimeoutMilliseconds,
            WeatherLocation = options.WeatherLocation,
            DurationWarning = options.DurationWarning,
        };

        return BlockEvaluator.Create(options.Selection, context, options.Overrides);
    }

    private static volatile StatusLineState? initialState;
}

/// <summary>The terminal status line is owned by a different, still-running runspace.</summary>
public sealed class StatusLineOwnershipException() : InvalidOperationException("The terminal status line is owned by another runspace; disable it there first.");
