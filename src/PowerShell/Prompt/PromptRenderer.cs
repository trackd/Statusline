using System.Text;

namespace StatusLine;

internal static class PromptRenderer {
    /// <summary>Cells kept free for typing after a single-row prompt.</summary>
    internal const int MinimumInputCells = 20;

    public static string Render(PromptLayout layout, BlockContext context, int width, bool transient, bool includeTransientBlocks = true) {
        PromptConfiguration configuration = layout.Configuration;
        if (context.BlockTimeoutMilliseconds != configuration.BlockTimeoutMilliseconds ||
            context.DurationWarning != configuration.DurationWarning || context.WeatherLocation != configuration.WeatherLocation) {
            context = context with {
                BlockTimeoutMilliseconds = configuration.BlockTimeoutMilliseconds,
                DurationWarning = configuration.DurationWarning,
                WeatherLocation = configuration.WeatherLocation
            };
        }

        string symbol = transient ? layout.TransientSymbol : layout.Symbol;
        symbol = TerminalText.Clip(symbol, Math.Max(1, width - 1)).Sequence;
        if (transient) {
            string sequence = string.Empty;
            if (includeTransientBlocks && layout.Transient.Length > 0) {
                var selected = new List<StatusLineBlock>(layout.Transient.Length);
                BlockEvaluator.AppendTo(selected, layout.Transient, context, layout.LeftOverrides, StatusLineAlignment.Left);
                IReadOnlyList<StatusLineBlock> colors = layout.TransientGradient.Apply(selected);
                (sequence, _) = StatusLineRenderer.RenderGroup(colors, StatusLineAlignment.Left,
                    Math.Max(0, width - 1 - TerminalText.Measure(symbol)), configuration.BlockSeparator,
                    configuration.Style, textContrast: configuration.TextContrast, roundOuterEdges: true);
            }

            return sequence + Vt.Sgr(layout.TransientAccent, System.Drawing.Color.Empty) + symbol + Vt.Reset;
        }

        var blocks = new List<StatusLineBlock>(layout.Left.Length + layout.Right.Length + configuration.Block.Length);
        BlockEvaluator.AppendTo(blocks, layout.Left, context, layout.LeftOverrides, StatusLineAlignment.Left);
        BlockEvaluator.AppendTo(blocks, layout.Right, context, layout.RightOverrides, StatusLineAlignment.Right);
        BlockEvaluator.AppendTo(blocks, layout.AdditionalBlocks, context, layout.AdditionalOverrides);
        IReadOnlyList<StatusLineBlock> colored = layout.ContextGradient.Apply(blocks);
        if (colored.Count > 0) {
            StatusLineBlock first = colored[0];
            layout.TransientAccent = first.Background.IsEmpty ? first.Foreground : first.Background;
        }

        int available = Math.Max(0, width - 1);
        string input = RenderInput(layout, context, available, symbol);
        if (layout.Rows == 2) {
            string prefix = configuration.ConnectRows ? TerminalText.Clip(layout.ContextSymbol, available).Sequence : string.Empty;
            string contextRow = StatusLineRenderer.RenderBlocks(colored, available - TerminalText.Measure(prefix), configuration.BlockSeparator, configuration.Style, useCursorPositioning: false, textContrast: configuration.TextContrast, roundOuterEdges: true);
            StringBuilder output = StringBuilderCache.Rent();
            try {
                AppendAccent(output, layout, prefix);
                output.Append(contextRow).Append(Vt.Reset).Append('\n').Append(input);
                return output.ToString();
            }
            finally {
                StringBuilderCache.Return(output);
            }
        }

        // Reserve room to type so a crowded context row cannot push input onto the next line.
        int typing = Math.Min(MinimumInputCells, available / 2);
        int leftWidth = Math.Max(0, available - TerminalText.Measure(input) - typing);
        (string Sequence, int Cells) = StatusLineRenderer.RenderGroup(colored, StatusLineAlignment.Left, leftWidth, configuration.BlockSeparator, configuration.Style, textContrast: configuration.TextContrast, roundOuterEdges: true);
        return Sequence + Vt.Reset + input;
    }

    private static string RenderInput(PromptLayout layout, BlockContext context, int available, string symbol) {
        PromptConfiguration configuration = layout.Configuration;
        List<StatusLineBlock>? blocks = null;
        if (layout.Input.Length > 0) {
            blocks = new(layout.Input.Length);
            BlockEvaluator.AppendTo(blocks, layout.Input, context, layout.LeftOverrides, StatusLineAlignment.Left);
        }

        if (layout.Rows == 2 && (blocks is null || blocks.Count == 0) && string.IsNullOrWhiteSpace(symbol)) {
            symbol = TerminalText.Clip(layout.TransientSymbol, available).Sequence;
        }

        string prefix = layout.Rows == 2 && configuration.ConnectRows ? layout.InputLinePrefix : string.Empty;
        prefix = TerminalText.Clip(prefix, Math.Max(0, available - TerminalText.Measure(symbol))).Sequence;
        StringBuilder output = StringBuilderCache.Rent();
        try {
            if (blocks is null || blocks.Count == 0) {
                AppendAccent(output, layout, prefix, symbol);
            }
            else {
                int blockWidth = Math.Max(0, available - TerminalText.Measure(prefix) - TerminalText.Measure(symbol));
                IReadOnlyList<StatusLineBlock> colored = layout.InputGradient.Apply(blocks);
                (string sequence, _) = StatusLineRenderer.RenderGroup(colored, StatusLineAlignment.Left, blockWidth,
                    configuration.BlockSeparator, configuration.Style, textContrast: configuration.TextContrast, roundOuterEdges: true);
                AppendAccent(output, layout, prefix);
                output.Append(sequence);
                AppendAccent(output, layout, symbol);
            }

            return output.ToString();
        }
        finally {
            StringBuilderCache.Return(output);
        }
    }

    private static void AppendAccent(StringBuilder output, PromptLayout layout, string text, string suffix = "") {
        if (text.Length > 0 || suffix.Length > 0) {
            Vt.AppendSgr(output, layout.TransientAccent, System.Drawing.Color.Empty);
            output.Append(text).Append(suffix).Append(Vt.Reset);
        }
    }
}
