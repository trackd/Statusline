using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace StatusLine.Commands;

/// <summary>Restores the prompt and owned PSReadLine handlers that preceded Enable-Prompt.</summary>
[Cmdlet(VerbsLifecycle.Disable, "Prompt", SupportsShouldProcess = true)]
public sealed class DisablePromptCommand : PSCmdlet {
    protected override void EndProcessing() {
        if (ShouldProcess("Current runspace", "Disable prompt")) {
            PromptSession.Disable(Runspace.DefaultRunspace);
        }
    }
}
