using System.Drawing;
using System.Text;

namespace StatusLine.Cli;

// One instance per worker layout. Access is serialized by the session gate.
internal sealed class PreparedPrompt : IDisposable {
    private readonly PromptOptions options;
    private readonly CliBlock[] selection;
    private readonly IBuiltInBlock[] refreshing;
    private readonly List<StatusLineBlock> blocks;
    private readonly BlockGradientCache gradient;
    private readonly Color start;
    private readonly Color end;
    private readonly string symbol;
    private StatusLineBlock[] previous = [];
    private string? frame;
    private int previousWidth;
    private bool previousStatusLine;
    private bool previousUnicode;
    private int previousStatus;

    internal PreparedPrompt(PromptOptions options) {
        PromptOutput.Validate(options);
        this.options = options;
        start = options.StartColor is { } first ? NamedColors.Parse(first) : Color.Empty;
        end = options.EndColor is { } last ? NamedColors.Parse(last) : Color.Empty;
        symbol = TerminalText.Sanitize(options.Symbol, keepStyles: false);
        selection = [.. Compile(options.Left, StatusLineAlignment.Left),
            .. Compile(options.Center, StatusLineAlignment.Center), .. Compile(options.Right, StatusLineAlignment.Right), .. CliBlock.Hints(options.EnableHints)];
        blocks = new(selection.Length);
        refreshing = [.. selection.Select(item => item.Implementation).OfType<IRefreshingBlock>()];
        gradient = new(start, end, options.Style);

        static IEnumerable<CliBlock> Compile(BlockSpec[] specs, StatusLineAlignment alignment) {
            foreach (BlockSpec spec in specs) { yield return new(spec.Settings, alignment); }
        }
    }

    internal void Refresh(string path, int status, double? durationMilliseconds) {
        if (refreshing.Length == 0) { return; }

        BlockContext context = Context(path, status, durationMilliseconds, wait: false);
        foreach (IBuiltInBlock block in refreshing) {
            _ = BuiltInRefresh.Evaluate(block, context, out _);
        }
    }

    internal string Render(string path, int width, int status, double? durationMilliseconds, bool statusLine, bool wait = true, StatusLineState? editor = null) {
        if (!statusLine && options.Rows == 1 && (options.Right.Length != 0 || options.Center.Length != 0)) {
            throw new ArgumentException("Right and Center prompt groups require Rows = 2.");
        }

        DirectoryDisplay.Style = options.DirectoryStyle;
        BlockContext context = Context(path, status, durationMilliseconds, wait) with { Width = width };
        if (editor is not null) { context = context with { State = editor with { Location = path, FileSystemPath = path, LastExitCode = status, LastDuration = context.State.LastDuration } }; }
        blocks.Clear();
        foreach (CliBlock item in selection) {
            if (item.Evaluate(context, options.Padding, wait) is { } block) { blocks.Add(block); }
        }

        bool unicode = TextMeasurement.UseUnicode;
        if (frame is not null && previousWidth == width && previousStatusLine == statusLine && previousUnicode == unicode &&
            (!options.ColorSymbolByExitCode || previousStatus == status) && previous.Length == blocks.Count) {
            int index = 0;
            while (index < blocks.Count && ReferenceEquals(previous[index], blocks[index])) { index++; }

            if (index == blocks.Count) { return frame; }
        }

        string result = Draw(width, statusLine, status);
        if (previous.Length != blocks.Count) { previous = new StatusLineBlock[blocks.Count]; }

        blocks.CopyTo(previous);
        previousWidth = width;
        previousStatusLine = statusLine;
        previousUnicode = unicode;
        previousStatus = status;
        return frame = result;
    }

    private BlockContext Context(string path, int status, double? durationMilliseconds, bool wait) => new() {
        State = new StatusLineState {
            Location = path,
            FileSystemPath = path,
            LastExitCode = status,
            LastDuration = durationMilliseconds is { } milliseconds ? TimeSpan.FromMilliseconds(milliseconds) : null,
        },
        IconSet = options.IconSet,
        ShellIdentity = Environment.GetEnvironmentVariable("STATUSLINE_SHELL"),
        BlockTimeoutMilliseconds = options.BlockTimeoutMilliseconds,
        SlowProviderWait = wait ? TimeSpan.FromMilliseconds(options.BlockTimeoutMilliseconds) : TimeSpan.Zero,
        WeatherLocation = options.WeatherLocation,
        DirectorySeparator = options.DirectorySeparator,
    };

    private string Draw(int width, bool statusLine, int status) {
        if (statusLine) { return Row(width); }

        string marker = TerminalText.Clip(symbol, width - 1).Sequence;
        if (options.ColorSymbolByExitCode) { marker = Vt.Sgr(status == 0 ? ConsoleColor.DarkGreen : ConsoleColor.DarkRed, Color.Empty) + marker + Vt.Reset; }
        if (options.Rows == 2 || options.EnableHints.Length > 0) { return Row(width - 1) + Vt.Reset + "\n" + marker; }

        int budget = Math.Max(0, width - 1 - TerminalText.Measure(marker) - Math.Min(20, (width - 1) / 2));
        if (options.BlendBackground) { return Row(budget) + Vt.Reset + marker; }

        return StatusLineRenderer.RenderGroup(gradient.Apply(blocks), StatusLineAlignment.Left, budget,
            options.BlockSeparator, options.Style, textContrast: options.TextContrast, roundOuterEdges: true).Sequence + Vt.Reset + marker;
    }

    private string Row(int width) {
        StringBuilder output = StringBuilderCache.Rent();
        try {
            StatusLineRenderer.AppendBlocks(output, blocks, width, options.BlockSeparator, options.Style,
                useCursorPositioning: false, blendBackground: options.BlendBackground,
                textContrast: options.TextContrast, roundOuterEdges: true, startColor: start, endColor: end, gradientCache: gradient);
            return output.ToString();
        }
        finally { StringBuilderCache.Return(output); }
    }

    public void Dispose() {
        foreach (CliBlock item in selection) { item.Dispose(); }
    }

    internal IEnumerable<string> Errors => selection.Where(item => item.Error is not null).Select(item => item.Label + ": " + item.Error);

}
