using System.Management.Automation;

namespace StatusLine.Commands;

/// <summary>Stops the background updater.</summary>
[Cmdlet(VerbsLifecycle.Disable, "StatusLine", SupportsShouldProcess = true)]
public sealed class DisableStatusLineCommand : PSCmdlet {
    /// <summary>Status line mode to switch to after stopping.</summary>
    [Parameter(Position = 0)]
    public StatusLineMode Mode { get; set; } = StatusLineMode.Indicator;

    [Parameter]
    public SwitchParameter Force { get; set; }

    protected override void EndProcessing() {
        if (!ShouldProcess("Current status line", "Disable status line")) {
            return;
        }

        bool stopped;
        try {
            stopped = StatusLineUpdater.Stop();
        }
        catch (TimeoutException exception) {
            ThrowTerminatingError(new ErrorRecord(exception, "StatusLineStopTimeout", ErrorCategory.OperationTimeout, null));
            return;
        }

        if (!stopped) {
            WriteError(new ErrorRecord(new StatusLineOwnershipException(), "StatusLineOwnedByAnotherRunspace", ErrorCategory.ResourceUnavailable, null));
            return;
        }

        TerminalControl.SetMode(Mode, Force.IsPresent);
    }
}
