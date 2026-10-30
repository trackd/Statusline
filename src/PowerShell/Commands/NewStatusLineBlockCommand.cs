using System.Drawing;
using System.Management.Automation;

namespace StatusLine.Commands;

/// <summary>Creates a reusable built-in or script-backed block definition without executing it.</summary>
[Cmdlet(VerbsCommon.New, "StatusLineBlock", DefaultParameterSetName = "Kind")]
[OutputType(typeof(StatusLineBlockDefinition))]
public sealed class NewStatusLineBlockCommand : PSCmdlet {
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Receives a StatusLineState as its first argument and returns strings or blocks.</summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = "Script")]
    public ScriptBlock ScriptBlock { get; set; } = null!;

    [Parameter]
    [ValidateRange(250, 86_400_000)]
    public int IntervalMilliseconds { get; set; } = 5000;

    [Parameter]
    [ValidateRange(50, 60000)]
    public int TimeoutMilliseconds { get; set; } = 2000;

    [Parameter]
    [AllowEmptyString]
    public string Icon { get; set; } = string.Empty;

    [Parameter]
    [Alias("ForegroundColor", "Color")]
    [ArgumentCompleter(typeof(ColorCompleter))]
    [TerminalColorTransform]
    public TerminalColor Foreground { get; set; } = Color.Empty;

    [Parameter]
    [Alias("BackgroundColor")]
    [ArgumentCompleter(typeof(ColorCompleter))]
    [TerminalColorTransform]
    public TerminalColor Background { get; set; } = Color.Empty;

    [Parameter]
    public StatusLineAlignment Alignment { get; set; } = StatusLineAlignment.Right;

    [Parameter]
    public SwitchParameter Bold { get; set; }

    [Parameter]
    public SwitchParameter Italic { get; set; }

    [Parameter]
    public SwitchParameter Underline { get; set; }

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = "Kind")]
    public StatusLineBlockKind Kind { get; set; }

    [Parameter]
    [AllowEmptyString]
    public string? Padding { get; set; }

    protected override void EndProcessing() {
        // The script is re-parsed in the background runspace pool; it cannot see this session's variables.
        string name = Name;
        if (name.Length == 0) {
            name = ParameterSetName == "Kind" ? Kind.ToString() : "Script";
        }

        var block = new StatusLineBlockDefinition(name, ScriptBlock?.ToString() ?? string.Empty,
            TimeSpan.FromMilliseconds(IntervalMilliseconds), ParameterSetName == "Kind" ? Kind : null, TimeoutMilliseconds) {
            Padding = Padding,
            Icon = MyInvocation.BoundParameters.ContainsKey(nameof(Icon)) ? Icon : null,
            Foreground = MyInvocation.BoundParameters.ContainsKey(nameof(Foreground)) ? Foreground : null,
            Background = MyInvocation.BoundParameters.ContainsKey(nameof(Background)) ? Background : null,
            Alignment = MyInvocation.BoundParameters.ContainsKey(nameof(Alignment)) ? Alignment : null,
            Bold = MyInvocation.BoundParameters.ContainsKey(nameof(Bold)) ? Bold.IsPresent : null,
            Italic = MyInvocation.BoundParameters.ContainsKey(nameof(Italic)) ? Italic.IsPresent : null,
            Underline = MyInvocation.BoundParameters.ContainsKey(nameof(Underline)) ? Underline.IsPresent : null,
        };
        WriteObject(block);
    }
}
