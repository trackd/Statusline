using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace StatusLine;

/// <summary>Tracks command boundaries without replacing PSReadLine key bindings.</summary>
public static class StatusLineActivity {
    private static PSObject? installation;
    private static PSEventManager? eventManager;
    private static PSEventSubscriber? idleSubscription;
    private static PSEventSubscriber? exitSubscription;
    private static PSEventSubscriber? renderSubscription;
    private static Guid readerOwner;
    private static System.Timers.Timer? refreshTimer;
    internal static bool HasReadLineHook => installation?.Properties["ReadLine"]?.Value is not null;

    internal static void Install(Guid owner, PSEventManager events) {
        installation = ScriptBlock.Create(InstallScript).Invoke(owner)[0];
        eventManager = events;
        idleSubscription = events.SubscribeEvent(null, null, "PowerShell.OnIdle", null,
            (sender, arguments) => StatusLineUpdater.OnIdle(owner), true, false);
        // Script actions run on the subscribing runspace. Delegate handlers can run on worker threads.
        refreshTimer = new System.Timers.Timer(300);
        renderSubscription = events.SubscribeEvent(refreshTimer, "Elapsed", "StatusLine.Refresh", null,
                ScriptBlock.Create($$"""
                    if ([StatusLine.StatusLineActivity]::NeedsObservation([guid]'{{owner}}') -and (Get-Module PSReadLine) -and
                        [StatusLine.PromptSession]::CanRefreshPrompt([Microsoft.PowerShell.PSConsoleReadLine])) {
                        $editorMode = $null
                        if ((Get-PSReadLineOption).EditMode -eq 'Vi') {
                            $editorMode = if ([Microsoft.PowerShell.PSConsoleReadLine]::InViCommandMode()) { 'NORMAL' } else { 'INSERT' }
                        }
                        [StatusLine.StatusLineActivity]::ObserveEditorMode([guid]'{{owner}}', $editorMode, $ExecutionContext.SessionState)
                    }
                    [StatusLine.StatusLineActivity]::FlushHost([guid]'{{owner}}')
                    """), true, false);

        exitSubscription = events.SubscribeEvent(null, null, "PowerShell.Exiting", null,
            (sender, arguments) => StatusLineUpdater.OnExit(owner), true, false);
    }

    internal static void Uninstall(bool restoreFunctions = true) {
        readerOwner = Guid.Empty;
        refreshTimer?.Stop();
        refreshTimer?.Dispose();
        refreshTimer = null;
        if (idleSubscription is not null) {
            eventManager?.UnsubscribeEvent(idleSubscription);
        }

        if (renderSubscription is not null) {
            eventManager?.UnsubscribeEvent(renderSubscription);
        }

        renderSubscription = null;
        if (exitSubscription is not null) {
            eventManager?.UnsubscribeEvent(exitSubscription);
        }

        idleSubscription = exitSubscription = null;
        eventManager = null;
        if (installation is null) {
            return;
        }

        if (restoreFunctions) {
            if (Runspace.DefaultRunspace is { } runspace) {
                PromptSession.DetachStatusHooks(runspace.InstanceId, installation);
            }

            ScriptBlock.Create(UninstallScript).Invoke(installation);
        }

        installation = null;
    }

    internal static bool DetachPromptHooks(PSObject promptInstallation) {
        if (installation is null) {
            return false;
        }

        bool prompt = ReplaceWrappedFunction(installation, "OldPrompt", promptInstallation, "Prompt", "OldPrompt");
        ReplaceWrappedFunction(installation, "OldReadLine", promptInstallation, "ReadLine", "OldReadLine");
        return prompt;
    }

    internal static bool ReplaceWrappedFunction(PSObject wrapper, string property, PSObject removed, string installed, string original) {
        if (removed.Properties[installed]?.Value is not { } function ||
            !Equals(wrapper.Properties[property]?.Value, function)) {
            return false;
        }

        wrapper.Properties[property].Value = removed.Properties[original].Value;
        return true;
    }

    /// <summary>Protects foreground prompt output until the next reader starts.</summary>
    public static void BeginPrompt(Guid runspaceId) {
        // PSReadLine can invoke prompt again while the same reader is editing.
        // There is no subsequent BeginReadLine to release a command-boundary guard.
        if (readerOwner == runspaceId) { return; }
        if (installation?.Properties["ReadLine"]?.Value is not null) {
            StatusLineUpdater.SetForegroundOutput(runspaceId, true);
        }
    }

    /// <summary>Allows background frames while the reader waits for input.</summary>
    public static void BeginReadLine(Guid runspaceId) {
        readerOwner = runspaceId;
        refreshTimer?.Start();
        EditorContext.Clear(runspaceId);
        StatusLineUpdater.SetExecuting(runspaceId, false);
        StatusLineUpdater.SetForegroundOutput(runspaceId, false);
        // PSReadLine has not captured its prompt anchor yet. Commit queued frames on idle.
    }

    /// <summary>Commits the latest status frame from a foreground idle event.</summary>
    public static void FlushHost(Guid runspaceId) => StatusLineUpdater.FlushHost(runspaceId);

    /// <summary>Waits for any frame in progress before a foreground redraw begins.</summary>
    public static void EndReadLine(Guid runspaceId) {
        if (readerOwner == runspaceId) { readerOwner = Guid.Empty; }
        refreshTimer?.Stop();
        StatusLineUpdater.SetForegroundOutput(runspaceId, true);
    }

    public static void CommandAccepted(Guid runspaceId, string? line) {
        EditorContext.Clear(runspaceId);
        StatusLineUpdater.CommandAccepted(runspaceId, line);
    }

    /// <summary>Whether the active surfaces need editor context or foreground job updates.</summary>
    public static bool NeedsObservation(Guid runspaceId) => readerOwner == runspaceId &&
        (PromptSession.NeedsEditorContext(runspaceId) || StatusLineUpdater.NeedsObservation(runspaceId));

    /// <summary>Publishes Vi mode and job counts from the foreground editor without changing keybindings.</summary>
    public static void ObserveEditorMode(Guid runspaceId, string? mode, SessionState sessionState) {
        if (EditorContext.Update(runspaceId, mode)) {
            StatusLineUpdater.EditorModeChanged(runspaceId);
        }
        if (StatusLineUpdater.RequiredFacts(runspaceId).HasFlag(SessionFacts.Jobs)) {
            StatusLineUpdater.UpdateJobs(runspaceId, StatusLineState.Capture(sessionState, SessionFacts.Jobs).RunningJobCount);
        }
    }

    public static void PromptReady(Guid runspaceId, SessionState? sessionState = null) {
        if (readerOwner == runspaceId) { return; }
        // The integrated prompt already published resolved exit status and history duration.
        // A generic session capture here would replace those with raw LASTEXITCODE/null.
        if (sessionState is not null && !PromptSession.IsEnabled(runspaceId)) {
            StatusLineState state = StatusLineState.Capture(sessionState, StatusLineUpdater.RequiredFacts(runspaceId));
            StatusLineUpdater.UpdateSnapshot(runspaceId, state);
        }

        EditorContext.Clear(runspaceId);
        StatusLineUpdater.SetExecuting(runspaceId, false);
    }

    private const string InstallScript = """
        param($owner)
        $oldPrompt = $function:global:prompt
        if (-not $oldPrompt) { $oldPrompt = { '> ' } }
        $oldReadLine = $function:global:PSConsoleHostReadLine
        $hooks = [pscustomobject]@{ Prompt = $null; OldPrompt = $oldPrompt; ReadLine = $null; OldReadLine = $oldReadLine }
        $prompt = {
            $result = & $hooks.OldPrompt
            [StatusLine.StatusLineActivity]::BeginPrompt($owner)
            [StatusLine.StatusLineActivity]::PromptReady($owner, $executionContext.SessionState)
            $result
        }.GetNewClosure()
        $readLine = $null
        if ($oldReadLine) {
            $readLine = {
                [StatusLine.StatusLineActivity]::BeginReadLine($owner)
                try { $line = & $hooks.OldReadLine }
                finally { [StatusLine.StatusLineActivity]::EndReadLine($owner) }
                [StatusLine.StatusLineActivity]::CommandAccepted($owner, $line)
                $line
            }.GetNewClosure()
            Set-Item function:global:PSConsoleHostReadLine $readLine
        }
        Set-Item function:global:prompt $prompt
        $hooks.Prompt = $prompt
        $hooks.ReadLine = $readLine
        $hooks
        """;

    private const string UninstallScript = """
        param($installation)
        if ($function:global:prompt -eq $installation.Prompt) {
            if ($installation.OldPrompt) { Set-Item function:global:prompt $installation.OldPrompt }
            else { Remove-Item function:global:prompt -ErrorAction Ignore }
        }
        if ($installation.ReadLine -and $function:global:PSConsoleHostReadLine -eq $installation.ReadLine) {
            Set-Item function:global:PSConsoleHostReadLine $installation.OldReadLine
        }
        """;
}
