using System.Management.Automation;

namespace StatusLine.Commands;

/// <summary>Previews a prompt without installing functions or changing PSReadLine.</summary>
[Cmdlet(VerbsCommon.Show, "Prompt")]
[OutputType(typeof(string))]
public sealed class ShowPromptCommand : PSCmdlet {
    [Parameter(Position = 0)]
    [ValidateNotNull]
    public PromptConfiguration Configuration { get; set; } = new();

    [Parameter]
    [ValidateRange(2, 32767)]
    public int Width { get; set; } = 120;

    [Parameter]
    public SwitchParameter Transient { get; set; }

    [Parameter]
    public int? ExitCode { get; set; }

    [Parameter]
    public TimeSpan? Duration { get; set; }

    protected override void EndProcessing() {
        WriteObject(PromptPreview.Render(Configuration, width: Width, exitCode: ExitCode,
            duration: Duration, transient: Transient.IsPresent, sessionState: SessionState));
    }
}
