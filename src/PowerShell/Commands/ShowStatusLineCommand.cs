using System.Management.Automation;

namespace StatusLine.Commands;

/// <summary>Previews a status row in ordinary output without starting a worker or selecting a display.</summary>
[Cmdlet(VerbsCommon.Show, "StatusLine")]
[OutputType(typeof(string))]
public sealed class ShowStatusLineCommand : PSCmdlet {
    [Parameter(Position = 0)]
    [ValidateNotNull]
    public StatusBarConfiguration Configuration { get; set; } = new() {
        Block = ["Cpu", "ProcessMemory", "Clock"],
    };

    [Parameter]
    [ValidateRange(1, 32767)]
    public int Width { get; set; } = 120;

    [Parameter]
    public int? ExitCode { get; set; }

    [Parameter]
    public TimeSpan? Duration { get; set; }

    protected override void EndProcessing() {
        StatusBarConfiguration settings = Configuration;
        (System.Drawing.Color start, System.Drawing.Color end) = settings.Validate();
        BlockSelector[] selection = settings.SelectBlocks();
        var context = new BlockContext {
            State = StatusLineState.Capture(SessionState, BlockSelector.RequiredFacts(selection)) with { LastExitCode = ExitCode, LastDuration = Duration },
            PowerShellVersion = CommandHelpers.GetPowerShellVersion(SessionState),
            IconSet = settings.IconSet,
            BlockTimeoutMilliseconds = settings.BlockTimeoutMilliseconds,
            DurationWarning = settings.DurationWarning,
            WeatherLocation = settings.WeatherLocation,
            SlowProviderWait = TimeSpan.Zero,
        };
        var overrides = new BlockOverrides(null, null, null, settings.Padding);
        // A preview is one-shot: start every refresh, then wait for them together.
        _ = BlockEvaluator.Create(selection, context, overrides);
        context = context with { SlowProviderWait = TimeSpan.FromMilliseconds(settings.BlockTimeoutMilliseconds) };
        List<StatusLineBlock> blocks = [.. BlockEvaluator.Create(selection, context, overrides)];
        WriteObject(StatusLineRenderer.RenderBlocks(blocks, Width, settings.BlockSeparator, settings.Style,
            useCursorPositioning: false, blendBackground: settings.BlendBackground, textContrast: settings.TextContrast,
            startColor: start, endColor: end) + Vt.Reset);

    }
}
