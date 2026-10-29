using System.Collections.Concurrent;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Microsoft.PowerShell.Commands;

namespace StatusLine;

/// <summary>Owns an explicitly enabled prompt and restores only shell functions it still owns.</summary>
public sealed class PromptSession {
    private static readonly ConcurrentDictionary<Guid, PromptSession> Sessions = new();
    /// <summary>The most recent optional terminal-marker or cleanup write failure.</summary>
    public static string? LastTerminalError { get; private set; }
    private PromptLayout layout;
    private readonly string? version;
    private readonly SessionState sessionState;
    private readonly ShellIntegrationProfile profile;
    private ShellIntegration Integration => profile.Protocol;
    private PSObject? installation;
    private long historyId = -1;
    private readonly ContourContext? contourContext;
    private bool commandPending;
    private bool promptWritten;
    private bool transient;
    private string? transientPrompt;
    private int? drawnRows;
    private volatile StatusLineState snapshot = new();

    private PromptSession(PromptConfiguration configuration, string? version, SessionState sessionState) {
        layout = new PromptLayout(configuration);
        this.version = version;
        this.sessionState = sessionState;
        profile = ShellIntegrationProfile.Resolve(configuration.ShellIntegration);
        if (profile.Context && configuration.ShellContext) {
            contourContext = new ContourContext();
        }
    }

    internal static void Install(Runspace runspace, PromptConfiguration configuration, string? version, SessionState sessionState) {
        var session = new PromptSession(configuration, version, sessionState);
        Disable(runspace);
        LastTerminalError = null;
        Sessions[runspace.InstanceId] = session;
        try {
            session.installation = ScriptBlock.Create(InstallScript).Invoke(configuration.Transient)[0];
        }
        catch {
            Sessions.TryRemove(runspace.InstanceId, out _);
            throw;
        }
    }

    internal static PromptConfiguration? GetConfiguration() =>
        Runspace.DefaultRunspace is { } runspace && Sessions.TryGetValue(runspace.InstanceId, out PromptSession? session)
            ? session.layout.Configuration.Copy() : null;

    internal static bool IsEnabled(Guid owner) => Sessions.ContainsKey(owner);

    // The owner has closed: release captured SessionState/closures without executing its shell.
    internal static void ReleaseOwner(Guid owner) => Sessions.TryRemove(owner, out _);

    internal static void Configure(PromptConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);
        if (Runspace.DefaultRunspace is not { } runspace || !Sessions.TryGetValue(runspace.InstanceId, out PromptSession? session)) {
            throw new InvalidOperationException("Enable-Prompt must be called before modifying its configuration.");
        }

        var replacement = new PromptLayout(configuration);
        PromptConfiguration current = session.layout.Configuration;
        if (configuration.ShellIntegration != current.ShellIntegration || configuration.ShellContext != current.ShellContext
            || configuration.TerminalProgress != current.TerminalProgress) {
            throw new ArgumentException("Changing ShellIntegration, ShellContext or TerminalProgress requires Enable-Prompt.");
        }

        session.layout = replacement;
        // Keep transientPrompt: it belongs to the prompt already drawn, not to these new settings.
    }

    /// <summary>Used by the existing reader wrapper so transient mode can change without reinstalling it.</summary>
    public static bool IsTransientEnabled(Runspace runspace) =>
        Sessions.TryGetValue(runspace.InstanceId, out PromptSession? session) && session.layout.Configuration.Transient;
    internal static ShellIntegrationFeatures IntegrationFeatures() {
        if (Runspace.DefaultRunspace is not { } runspace || !Sessions.TryGetValue(runspace.InstanceId, out PromptSession? session)) {
            return new();
        }

        return new() {
            Terminal = session.profile.Terminal,
            PromptStart = session.Integration == ShellIntegration.None ? "None" : $"OSC {session.profile.MarkerNumber};A",
            CommandStart = session.Integration == ShellIntegration.None ? "None" : $"OSC {session.profile.MarkerNumber};B",
            OutputStart = session.Integration == ShellIntegration.None ? "None" : $"OSC {session.profile.MarkerNumber};C",
            CommandFinished = session.Integration == ShellIntegration.None ? "None" : $"OSC {session.profile.MarkerNumber};D[;<exit-code>]",
            CommandTextFormat = session.profile.Metadata switch {
                CommandMetadata.VsCode => "OSC 633;E;<escaped-command>[;<nonce>]",
                CommandMetadata.Contour => "OSC 133;C;cmdline_url=<percent-encoded-command>",
                _ => "None",
            },
            TransientRedrawMarks = session.layout.Configuration.Transient && session.Integration != ShellIntegration.None,
            PromptInstalled = true,
            RequestedProtocol = session.layout.Configuration.ShellIntegration,
            Protocol = session.Integration,
            CommandBoundaries = session.Integration != ShellIntegration.None,
            WorkingDirectory = session.profile.WorkingDirectory,
            CommandText = session.profile.Metadata != CommandMetadata.None,
            ReflowControl = session.profile.ReflowControl,
            ShellContext = session.contourContext is not null,
        };
    }
    /// <summary>Restores the previous prompt and ReadLine function for this runspace.</summary>
    public static void Disable(Runspace runspace) {
        if (Sessions.TryGetValue(runspace.InstanceId, out PromptSession? session) && session.installation is not null) {
            string close = session.contourContext?.Close() ?? string.Empty;
            if (session.commandPending && session.layout.Configuration.TerminalProgress) {
                close += Vt.ClearProgress;
            }

            if (session.profile.ReflowControl) {
                close += Vt.EnableReflow;
            }

            if (close.Length > 0) {
                WriteOptionalTerminal(close);
            }

            StatusLineActivity.DetachPromptHooks(session.installation);
            ScriptBlock.Create(UninstallScript).Invoke(session.installation);
            Sessions.TryRemove(runspace.InstanceId, out _);
        }
    }

    internal static void DetachStatusHooks(Guid runspaceId, PSObject activityInstallation) {
        if (!Sessions.TryGetValue(runspaceId, out PromptSession? session) || session.installation is null) {
            return;
        }

        StatusLineActivity.ReplaceWrappedFunction(session.installation, "OldPrompt", activityInstallation, "Prompt", "OldPrompt");
        StatusLineActivity.ReplaceWrappedFunction(session.installation, "OldReadLine", activityInstallation, "ReadLine", "OldReadLine");
    }

    /// <summary>Renders the installed prompt from caller-captured success, location, and history.</summary>
    public static string Render(Runspace runspace, bool success, object? exitCode, PathInfo location, HistoryInfo? history, int? errorCount = null, long? invocationHistoryId = null) {
        StatusLineActivity.BeginPrompt(runspace.InstanceId);
        StatusLineActivity.PromptReady(runspace.InstanceId);
        if (!Sessions.TryGetValue(runspace.InstanceId, out PromptSession? session)) {
            return "> ";
        }

        if (session.transient) {
            return session.profile.PromptStart + (session.transientPrompt ?? PromptRenderer.Render(session.layout, new BlockContext(), Terminal.Width, transient: true, includeTransientBlocks: false)) + session.profile.CommandStart;
        }

        int? completedCode = null;
        // The prompt wrapper already supplies the location below. Capture only the remaining
        // facts needed by either surface, rather than resolving the same provider path twice.
        SessionFacts facts = (session.layout.RequiredFacts | StatusLineUpdater.RequiredFacts(runspace.InstanceId)) & ~SessionFacts.Location;
        var state = StatusLineState.Capture(session.sessionState, facts);
        state = state with {
            LastExitCode = session.snapshot.LastExitCode,
            LastDuration = session.snapshot.LastDuration,
            ErrorCount = errorCount ?? state.ErrorCount,
            HistoryId = invocationHistoryId,
            Location = location.Path,
            FileSystemPath = location.Provider.Name == "FileSystem" ? location.ProviderPath : null,
        };
        if (history is not null && history.Id != session.historyId) {
            session.historyId = history.Id;
            int code = ResolveExitCode(success, exitCode);
            state = state with {
                LastExitCode = code,
                LastDuration = history.Duration
            };
            if (session.commandPending) {
                completedCode = code;
            }
        }
        else if (session.commandPending) {
            completedCode = success ? 0 : 1;
        }

        session.commandPending = false;
        session.snapshot = state;
        StatusLineUpdater.UpdateSnapshot(runspace.InstanceId, state);
        StatusLineUpdater.Wake();
        var context = new BlockContext { State = state, IconSet = session.layout.Configuration.IconSet, PowerShellVersion = session.version, SlowProviderWait = TimeSpan.Zero };
        string prompt = PromptRenderer.Render(session.layout, context, Terminal.Width, transient: false);
        session.drawnRows = session.layout.Rows;
        if (session.layout.Configuration.Transient) {
            session.transientPrompt = PromptRenderer.Render(session.layout, context, Terminal.Width, transient: true);
        }

        string begin = ShellMarkers.Begin(session.profile, completedCode, state.FileSystemPath, session.promptWritten, session.layout.Configuration.TerminalProgress);
        session.promptWritten = true;
        return (session.contourContext?.Prompt(state.FileSystemPath, completedCode) ?? string.Empty) +
            begin +
            prompt + session.profile.CommandStart;
    }

    /// <summary>Selects the compact rendering during PSReadLine's InvokePrompt callback.</summary>
    public static void BeginTransient(Runspace runspace) {
        if (Sessions.TryGetValue(runspace.InstanceId, out PromptSession? session)) {
            session.transient = true;
        }
    }

    /// <summary>Redraws accepted input, including blank lines; null means the reader was cancelled.</summary>
    public static bool TryBeginTransient(Runspace runspace, string? line) {
        if (line is null || !Sessions.TryGetValue(runspace.InstanceId, out PromptSession? session)) {
            return false;
        }

        session.transient = true;
        return true;
    }

    /// <summary>Ends a transient redraw without changing command status.</summary>
    public static void EndTransient(Runspace runspace) {
        if (Sessions.TryGetValue(runspace.InstanceId, out PromptSession? session)) {
            session.transient = false;
        }
    }

    /// <summary>Returns a safe redraw row after acceptance, or null when layout is uncertain.</summary>
    public static int? GetTransientRow(Runspace runspace, string line, int inputColumn, int width, int endRow, string continuation) {
        if (!Sessions.TryGetValue(runspace.InstanceId, out PromptSession? session) || !session.layout.Configuration.Transient) {
            return null;
        }

        return TransientPromptLayout.GetRow(line, inputColumn, width, endRow,
            (session.drawnRows ?? session.layout.Rows) - 1, continuation);
    }

    /// <summary>Marks command execution after ReadLine has accepted a complete line.</summary>
    public static void CommandAccepted(Runspace runspace, string? line) {
        StatusLineActivity.CommandAccepted(runspace.InstanceId, line);
        if (string.IsNullOrWhiteSpace(line) || !Sessions.TryGetValue(runspace.InstanceId, out PromptSession? session)) {
            return;
        }

        session.commandPending = true;
        string sequence = ShellMarkers.Execute(session.profile, line, session.layout.Configuration.TerminalProgress) +
            (session.contourContext?.Execute(line, session.snapshot.FileSystemPath) ?? string.Empty);
        if (sequence.Length > 0) {
            WriteOptionalTerminal(sequence);
        }
    }

    private static void WriteOptionalTerminal(string sequence) {
        try {
            Terminal.Write(sequence);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or PlatformNotSupportedException) {
            LastTerminalError = exception.Message;
        }
    }

    private static int ResolveExitCode(bool success, object? exitCode) {
        if (success) {
            return 0;
        }

        if (exitCode is not null && LanguagePrimitives.TryConvertTo(exitCode, out int native) && native != 0) {
            return native;
        }

        return 1;
    }

    private const string InstallScript = """
        param($transient)
        $oldPrompt = $function:global:prompt
        $oldReadLine = $function:global:PSConsoleHostReadLine
        $options = $null
        $oldPromptText = $null
        $emptyPromptText = $null
        if (Get-Module PSReadLine) {
            $options = Get-PSReadLineOption
            $oldPromptText = $options.PromptText
            # An explicit empty suffix also prevents first-read automatic inference.
            $emptyPromptText = [string[]]@('')
            $options.PromptText = $emptyPromptText
        }
        elseif ($transient) { Write-Warning 'Transient prompt requires PSReadLine to be loaded.' }
        $prompt = {
            $previousSuccess = $?
            $previousExit = $global:LASTEXITCODE
            $sessionErrors = $global:Error.Count
            [StatusLine.PromptSession]::Render(
                [System.Management.Automation.Runspaces.Runspace]::DefaultRunspace,
                $previousSuccess, $previousExit, $executionContext.SessionState.Path.CurrentLocation,
                (Microsoft.PowerShell.Core\Get-History -Count 1), $sessionErrors, $MyInvocation.HistoryId)
        }
        Set-Item function:global:prompt $prompt
        $readLine = $null
        if ($oldReadLine) {
            $readLine = {
                $runspace = [System.Management.Automation.Runspaces.Runspace]::DefaultRunspace
                $transient = [StatusLine.PromptSession]::IsTransientEnabled($runspace)
                [StatusLine.StatusLineActivity]::BeginReadLine($runspace.InstanceId)
                $position = $null
                $width = 0
                if ($transient -and $options -and -not $options.ScreenReaderModeEnabled -and -not [Console]::IsInputRedirected -and -not [Console]::IsOutputRedirected) {
                    try {
                        $position = [Console]::GetCursorPosition()
                        $width = [Console]::BufferWidth
                    }
                    catch { $position = $null }
                }
                try {
                    $line = & $oldReadLine
                }
                finally {
                    [StatusLine.StatusLineActivity]::EndReadLine($runspace.InstanceId)
                }
                if ($null -ne $position -and $null -ne $line -and [Console]::BufferWidth -eq $width) {
                    # A failed cosmetic redraw must never lose the accepted line; the error stays in $Error.
                    try {
                        $endPosition = [Console]::GetCursorPosition()
                        $editorLine = $null
                        $editorCursor = 0
                        [Microsoft.PowerShell.PSConsoleReadLine]::GetBufferState([ref]$editorLine, [ref]$editorCursor)
                        if ($endPosition.Item1 -eq 0 -and $editorLine -ceq $line -and $editorCursor -eq $line.Length) {
                            $row = [StatusLine.PromptSession]::GetTransientRow($runspace, $line, $position.Item1, $width, $endPosition.Item2, $options.ContinuationPrompt)
                            if ($null -ne $row) {
                                [StatusLine.PromptSession]::BeginTransient($runspace)
                                try {
                                    $rowsUp = $endPosition.Item2 - $row
                                    [Console]::Write("`r`e[${rowsUp}A`e[0J")
                                    [Microsoft.PowerShell.PSConsoleReadLine]::InvokePrompt($null, [int]$row)
                                    # Acceptance already happened. Finish the redraw without accepting again.
                                    [Console]::Write("`e[0J`r`n")
                                }
                                finally { [StatusLine.PromptSession]::EndTransient($runspace) }
                            }
                        }
                    }
                    catch { }
                }
                [StatusLine.PromptSession]::CommandAccepted([System.Management.Automation.Runspaces.Runspace]::DefaultRunspace, $line)
                $line
            }.GetNewClosure()
            Set-Item function:global:PSConsoleHostReadLine $readLine
        }
        [pscustomobject]@{ OldPrompt = $oldPrompt; Prompt = $prompt; OldReadLine = $oldReadLine; ReadLine = $readLine; Options = $options; OldPromptText = $oldPromptText; EmptyPromptText = $emptyPromptText }
        """;

    private const string UninstallScript = """
        param($installation)
        $ownsPrompt = $function:global:prompt -eq $installation.Prompt
        if ($ownsPrompt -and $installation.Options -and
            [object]::ReferenceEquals($installation.Options.PromptText, $installation.EmptyPromptText)) {
            $installation.Options.PromptText = $installation.OldPromptText
        }
        if ($ownsPrompt) {
            if ($installation.OldPrompt) { Set-Item function:global:prompt $installation.OldPrompt }
            else { Remove-Item function:global:prompt -ErrorAction Ignore }
        }
        if ($installation.ReadLine -and $function:global:PSConsoleHostReadLine -eq $installation.ReadLine) {
            Set-Item function:global:PSConsoleHostReadLine $installation.OldReadLine
        }
        """;
}
