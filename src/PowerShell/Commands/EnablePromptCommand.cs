using System.Drawing;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace StatusLine.Commands;

/// <summary>Installs a block-based prompt; installation is explicit and reversible.</summary>
[Cmdlet(VerbsLifecycle.Enable, "Prompt", SupportsShouldProcess = true)]
public sealed class EnablePromptCommand : PSCmdlet {
    [Parameter]
    [ValidateNotNull]
    public PromptConfiguration Configuration { get; set; } = new();

    [Parameter]
    [AllowEmptyCollection]
    [ArgumentCompleter(typeof(BlockKindCompleter))]
    public object[]? Left { get; set; }

    [Parameter]
    [AllowEmptyCollection]
    [ArgumentCompleter(typeof(BlockKindCompleter))]
    public object[]? Right { get; set; }

    [Parameter]
    [AllowEmptyCollection]
    [ArgumentCompleter(typeof(BlockKindCompleter))]
    public object[]? Block { get; set; }

    [Parameter]
    [AllowEmptyCollection]
    [ArgumentCompleter(typeof(BlockKindCompleter))]
    public object[]? Input { get; set; }

    [Parameter]
    public TextContrastMode TextContrast { get; set; }

    [Parameter]
    public SegmentStyle Style { get; set; }

    [Parameter]
    public IconSet IconSet { get; set; }

    [Parameter]
    public ShellIntegration ShellIntegration { get; set; }

    [Parameter]
    public SwitchParameter TerminalProgress { get; set; }

    [Parameter]
    public SwitchParameter Transient { get; set; }

    [Parameter]
    [AllowEmptyCollection]
    [ArgumentCompleter(typeof(BlockKindCompleter))]
    public object[]? TransientBlock { get; set; }

    [Parameter]
    public SwitchParameter ConnectRows { get; set; }

    [Parameter]
    [ValidateRange(1, 2)]
    public int Rows { get; set; } = 1;

    [Parameter]
    [ValidateRange(50, 60000)]
    public int BlockTimeoutMilliseconds { get; set; } = 2000;

    [Parameter]
    [ColorTransform]
    [ArgumentCompleter(typeof(ColorCompleter))]
    public Color StartColor { get; set; }

    [Parameter]
    [ColorTransform]
    [ArgumentCompleter(typeof(ColorCompleter))]
    public Color EndColor { get; set; }

    [Parameter]
    [ColorTransform]
    [ArgumentCompleter(typeof(ColorCompleter))]
    public Color ElevatedColorStart { get; set; }

    [Parameter]
    [ColorTransform]
    [ArgumentCompleter(typeof(ColorCompleter))]
    public Color ElevatedColorEnd { get; set; }

    [Parameter]
    public string? WeatherLocation { get; set; }

    /// <summary>Process-wide relative directory depth (default 3); zero keeps the complete relative path.</summary>
    [Parameter]
    [ValidateRange(0, int.MaxValue)]
    public int DirectoryDepth { get; set; } = 3;

    [Parameter]
    public SwitchParameter BlockSeparator { get; set; }

    [Parameter]
    public SwitchParameter DurationWarning { get; set; }

    [Parameter]
    public SwitchParameter ShellContext { get; set; }

    [Parameter]
    [AllowEmptyString]
    [ValidateNotNull]
    public string Padding { get; set; } = " ";

    protected override void EndProcessing() {
        Dictionary<string, object> bound = MyInvocation.BoundParameters;
        (object[] left, object[] right) = ResolveGroups(bound.ContainsKey(nameof(Configuration)));
        PromptConfiguration configuration = Configuration with {
            ConnectRows = bound.ContainsKey(nameof(ConnectRows)) ? ConnectRows.IsPresent : Configuration.ConnectRows,
            DurationWarning = bound.ContainsKey(nameof(DurationWarning)) ? DurationWarning.IsPresent : Configuration.DurationWarning,
            ShellContext = bound.ContainsKey(nameof(ShellContext)) ? ShellContext.IsPresent : Configuration.ShellContext,
            TerminalProgress = bound.ContainsKey(nameof(TerminalProgress)) ? TerminalProgress.IsPresent : Configuration.TerminalProgress,
            Padding = bound.ContainsKey(nameof(Padding)) ? Padding : Configuration.Padding,
            BlockSeparator = bound.ContainsKey(nameof(BlockSeparator)) ? BlockSeparator.IsPresent : Configuration.BlockSeparator,
            WeatherLocation = bound.ContainsKey(nameof(WeatherLocation)) ? WeatherLocation : Configuration.WeatherLocation,
            Left = left,
            Right = right,
            Block = Block ?? Configuration.Block,
            Input = Input ?? Configuration.Input,
            TransientBlock = TransientBlock ?? Configuration.TransientBlock,
            TextContrast = bound.ContainsKey(nameof(TextContrast)) ? TextContrast : Configuration.TextContrast,
            Style = bound.ContainsKey(nameof(Style)) ? Style : Configuration.Style,
            IconSet = bound.ContainsKey(nameof(IconSet)) ? IconSet : Configuration.IconSet,
            ShellIntegration = bound.ContainsKey(nameof(ShellIntegration)) ? ShellIntegration : Configuration.ShellIntegration,
            Transient = ResolveTransient(bound.ContainsKey(nameof(Transient))),
            Rows = bound.ContainsKey(nameof(Rows)) ? Rows : Configuration.Rows,
            BlockTimeoutMilliseconds = bound.ContainsKey(nameof(BlockTimeoutMilliseconds)) ? BlockTimeoutMilliseconds : Configuration.BlockTimeoutMilliseconds,
            StartColor = bound.ContainsKey(nameof(StartColor)) ? StartColor : Configuration.StartColor,
            EndColor = bound.ContainsKey(nameof(EndColor)) ? EndColor : Configuration.EndColor,
            ElevatedColorStart = bound.ContainsKey(nameof(ElevatedColorStart)) ? ElevatedColorStart : Configuration.ElevatedColorStart,
            ElevatedColorEnd = bound.ContainsKey(nameof(ElevatedColorEnd)) ? ElevatedColorEnd : Configuration.ElevatedColorEnd,
        };
        if (!ShouldProcess("Current runspace", "Enable prompt")) {
            return;
        }

        int previousDepth = DirectoryDisplay.MaximumComponents;
        bool installed = false;
        try {
            if (bound.ContainsKey(nameof(DirectoryDepth))) {
                DirectoryDisplay.MaximumComponents = DirectoryDepth;
            }

            PromptSession.Install(Runspace.DefaultRunspace, configuration, CommandHelpers.GetPowerShellVersion(SessionState), SessionState);
            installed = true;
        }
        catch (ArgumentException exception) {
            ThrowTerminatingError(new ErrorRecord(exception, "InvalidPromptConfiguration", ErrorCategory.InvalidArgument, Configuration));
        }
        finally {
            if (!installed && bound.ContainsKey(nameof(DirectoryDepth))) {
                DirectoryDisplay.MaximumComponents = previousDepth;
            }
        }
    }

    private (object[] Left, object[] Right) ResolveGroups(bool hasConfiguration) {
        object[] left = Left ?? Configuration.Left;
        object[] right = Right ?? Configuration.Right;
        if (!hasConfiguration) {
            if (Left is null && (Right is not null || Block is not null)) {
                left = [];
            }

            if (Right is null && (Left is not null || Block is not null)) {
                right = [];
            }
        }

        return (left, right);
    }

    private bool ResolveTransient(bool explicitlyBound) {
        if (explicitlyBound) {
            return Transient.IsPresent;
        }

        return Configuration.Transient || (TransientBlock ?? Configuration.TransientBlock).Length > 0;
    }
}
