using System.Drawing;
using System.Text;

namespace StatusLine.Cli;

// One instance per worker layout. Access is serialized by the session gate.
internal sealed class PreparedPrompt {
    private readonly PromptOptions options;
    private readonly Selection[] selection;
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

    internal PreparedPrompt(PromptOptions options) {
        PromptOutput.Validate(options);
        this.options = options;
        start = options.StartColor is { } first ? NamedColors.Parse(first) : Color.Empty;
        end = options.EndColor is { } last ? NamedColors.Parse(last) : Color.Empty;
        symbol = TerminalText.Sanitize(options.Symbol, keepStyles: false);
        selection = [.. Compile(options.Left, StatusLineAlignment.Left),
            .. Compile(options.Center, StatusLineAlignment.Center), .. Compile(options.Right, StatusLineAlignment.Right)];
        blocks = new(selection.Length);
        refreshing = [.. selection.Select(item => item.Implementation).Where(block => block is IRefreshingBlock)];
        gradient = new(start, end, options.Style);

        static IEnumerable<Selection> Compile(string[] names, StatusLineAlignment alignment) {
            foreach (string name in names) { yield return new(name, PromptOutput.Kind(name), alignment); }
        }
    }

    internal void Refresh(string path, int status, double? durationMilliseconds) {
        if (refreshing.Length == 0) { return; }

        BlockContext context = Context(path, status, durationMilliseconds, wait: false);
        foreach (IBuiltInBlock block in refreshing) {
            _ = BuiltInRefresh.Evaluate(block, context, out _);
        }
    }

    internal string Render(string path, int width, int status, double? durationMilliseconds, bool statusLine, bool wait = true) {
        if (!statusLine && options.Rows == 1 && (options.Right.Length != 0 || options.Center.Length != 0)) {
            throw new ArgumentException("Right and Center prompt groups require Rows = 2.");
        }

        DirectoryDisplay.Style = options.DirectoryStyle;
        BlockContext context = Context(path, status, durationMilliseconds, wait);
        blocks.Clear();
        foreach (Selection item in selection) {
            if (item.Evaluate(context, options.Padding, wait) is { } block) { blocks.Add(block); }
        }

        bool unicode = TextMeasurement.UseUnicode;
        if (frame is not null && previousWidth == width && previousStatusLine == statusLine && previousUnicode == unicode && previous.Length == blocks.Count) {
            int index = 0;
            while (index < blocks.Count && ReferenceEquals(previous[index], blocks[index])) { index++; }

            if (index == blocks.Count) { return frame; }
        }

        string result = Draw(width, statusLine);
        if (previous.Length != blocks.Count) { previous = new StatusLineBlock[blocks.Count]; }

        blocks.CopyTo(previous);
        previousWidth = width;
        previousStatusLine = statusLine;
        previousUnicode = unicode;
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
    };

    private string Draw(int width, bool statusLine) {
        if (statusLine) { return Row(width); }

        string marker = TerminalText.Clip(symbol, width - 1).Sequence;
        if (options.Rows == 2) { return Row(width - 1) + Vt.Reset + "\n" + marker; }

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

    private sealed class Selection(string name, StatusLineBlockKind kind, StatusLineAlignment alignment) {
        internal IBuiltInBlock Implementation { get; } = BlockCatalog.Get(kind);
        private BlockContent? previousContent;
        private string? previousError;
        private StatusLineBlock? block;
        private bool unicode;

        internal StatusLineBlock? Evaluate(BlockContext context, string padding, bool wait) {
            BlockContent? content = BuiltInRefresh.Evaluate(Implementation, context, out string? error);
            if (error is not null && wait) { Console.Error.WriteLine($"statusline: {name}: {error}"); }

            if (content is not { } value) { return null; }

            if (block is not null && previousContent == content && previousError == error && unicode == TextMeasurement.UseUnicode) { return block; }

            var next = new StatusLineBlock(value.Text, value.Accent, Color.Empty, alignment, padding, icon: value.Icon) {
                Shrinkable = kind == StatusLineBlockKind.Directory,
            };
            previousContent = content;
            previousError = error;
            unicode = TextMeasurement.UseUnicode;
            return block = error is null ? next : next.AsStale(error);
        }
    }
}
