using System.Buffers;
using System.Drawing;
using System.Text;

namespace StatusLine;

/// <summary>Builds DEC host-writable status line sequences and lays out shared blocks.</summary>
internal static class StatusLineRenderer {

    /// <summary>
    /// Writes the single-row status display without an absolute main-screen cursor movement.
    /// CR rewinds the status row; Contour keeps the main display's cursor in its separate screen state.
    /// </summary>
    public static string WrapStatusText(string statusText, bool initialize = true) =>
        Vt.StatusText(statusText, initialize);

    /// <summary>Selects the status line display type and returns output to the main display.</summary>
    public static string ModeSequence(StatusLineMode mode) =>
        Vt.StatusLineMode(mode);

    /// <summary>OSC 2 window title.</summary>
    public static string TitleSequence(string text) =>
        Vt.Title(TerminalText.Sanitize(text, keepStyles: false));

    /// <summary>
    /// Centers the center group independently, clipping side groups to their remaining space. Groups are positioned
    /// with CUP, or with spaces when <paramref name="useCursorPositioning"/> is false (single-line text
    /// for consumers such as Contour's indicator {Command} placeholder).
    /// </summary>
    public static string RenderBlocks(
        IReadOnlyList<StatusLineBlock> blocks,
        int width,
        bool blockSeparator,
        SegmentStyle style = SegmentStyle.Plain,
        bool useCursorPositioning = true,
        bool neutralBackground = false,
        bool blendBackground = false,
        TextContrastMode textContrast = TextContrastMode.Automatic, bool roundOuterEdges = false,
        Color startColor = default, Color endColor = default,
        StatusLineFraming framing = StatusLineFraming.None
    ) {
        StringBuilder output = StringBuilderCache.Rent();
        try {
            AppendBlocks(output, blocks, width, blockSeparator, style, useCursorPositioning, neutralBackground,
                blendBackground, textContrast, roundOuterEdges, startColor, endColor, framing);
            return output.ToString();
        }
        finally {
            StringBuilderCache.Return(output);
        }
    }

    /// <summary>Appends a rendered row to a caller-owned builder, e.g. a reusable frame buffer.</summary>
    internal static void AppendBlocks(
        StringBuilder output,
        IReadOnlyList<StatusLineBlock> blocks,
        int width,
        bool blockSeparator,
        SegmentStyle style = SegmentStyle.Plain,
        bool useCursorPositioning = true,
        bool neutralBackground = false,
        bool blendBackground = false,
        TextContrastMode textContrast = TextContrastMode.Automatic, bool roundOuterEdges = false,
        Color startColor = default, Color endColor = default,
        StatusLineFraming framing = StatusLineFraming.None,
        BlockGradientCache? gradientCache = null
    ) {
        // Palette RGB is controlled by the terminal theme and cannot be interpolated locally.
        bool fullRowGradient = blendBackground && style != SegmentStyle.Plain && !blockSeparator &&
            CanBlend(blocks, !startColor.IsEmpty);
        if (fullRowGradient && blocks.Count > 0) {
            TerminalColor start = GroupFill(blocks, StatusLineAlignment.Left, startColor, endColor);
            if (start.IsEmpty) {
                start = GroupFill(blocks, StatusLineAlignment.Center, startColor, endColor);
            }

            if (start.IsEmpty) {
                start = GroupFill(blocks, StatusLineAlignment.Right, startColor, endColor);
            }

            TerminalColor end = GroupFill(blocks, StatusLineAlignment.Right, startColor, endColor, last: true);
            if (end.IsEmpty) {
                end = GroupFill(blocks, StatusLineAlignment.Center, startColor, endColor, last: true);
            }

            if (end.IsEmpty) {
                end = GroupFill(blocks, StatusLineAlignment.Left, startColor, endColor, last: true);
            }

            StatusLineGradient.Append(output, blocks, width, style, start, end, textContrast, roundOuterEdges, framing);
            return;
        }

        blocks = gradientCache is null ? BlockGradient.Apply(blocks, startColor, endColor, style) : gradientCache.Apply(blocks);
        (StringBuilder? Builder, int Cells) center = default, right = default;
        StringBuilder? left = null;
        StringBuilder builder = output;
        int leftCells = 0;
        List<StatusLineBlock> scratch = rowScratch ??= [];
        try {
            center = PrepareGroup(blocks, StatusLineAlignment.Center, width, blockSeparator, style, neutralBackground, textContrast, roundOuterEdges);
            var layout = new GroupLayout(width, center.Cells);
            int centerColumn = layout.CenterColumn;
            if (center.Cells == 0) {
                // Left and right share the row: fit them together so neither is clipped into the other.
                blocks = Fit(blocks, scratch, width, StatusLineAlignment.Left, StatusLineAlignment.Right,
                    GroupLayout.Gap, style, blockSeparator, roundOuterEdges);
            }

            (left, leftCells) = PrepareGroup(blocks, StatusLineAlignment.Left, layout.LeftBudget, blockSeparator, style, neutralBackground, textContrast, roundOuterEdges);
            right = PrepareGroup(blocks, StatusLineAlignment.Right,
                layout.RightBudget(leftCells), blockSeparator, style, neutralBackground, textContrast, roundOuterEdges);
            Vt.AppendFrameStart(builder, framing);
            builder.Append(left);
            int column = leftCells;
            if (center.Cells > 0) {
                MoveTo(builder, ref column, centerColumn, useCursorPositioning, neutralBackground);
                builder.Append(center.Builder);
                column += center.Cells;
            }

            if (right.Cells > 0) {
                MoveTo(builder, ref column, width - right.Cells, useCursorPositioning, neutralBackground);
                builder.Append(right.Builder);
                column = width;
            }

            if (neutralBackground && !useCursorPositioning && column < width) {
                MoveTo(builder, ref column, width, false, neutralBackground);
            }

            Vt.AppendFrameEnd(builder, framing);
        }
        finally {
            scratch.Clear();
            if (left is not null) {
                StringBuilderCache.Return(left);
            }

            if (center.Builder is not null) {
                StringBuilderCache.Return(center.Builder);
            }

            if (right.Builder is not null) {
                StringBuilderCache.Return(right.Builder);
            }
        }
    }
    internal static (string Sequence, int Cells) RenderGroup(
        IReadOnlyList<StatusLineBlock> blocks, StatusLineAlignment alignment, int width,
        bool blockSeparator, SegmentStyle style, bool neutralBackground = false, TextContrastMode textContrast = TextContrastMode.Automatic, bool roundOuterEdges = false) {
        (StringBuilder? builder, int cells) = PrepareGroup(blocks, alignment, width,
            blockSeparator, style, neutralBackground, textContrast, roundOuterEdges);
        return (builder is null ? string.Empty : StringBuilderCache.Complete(builder), cells);
    }

    private static (StringBuilder? Builder, int Cells) PrepareGroup(
        IReadOnlyList<StatusLineBlock> blocks, StatusLineAlignment alignment, int width,
        bool blockSeparator, SegmentStyle style, bool neutralBackground, TextContrastMode textContrast, bool roundOuterEdges) {
        List<StatusLineBlock> scratch = groupScratch ??= [];
        try {
            blocks = Fit(blocks, scratch, Math.Max(0, width), alignment, null, 0, style, blockSeparator, roundOuterEdges);
            return Clip(blocks, alignment, width, blockSeparator, style, neutralBackground, textContrast, roundOuterEdges);
        }
        finally {
            scratch.Clear();
        }
    }

    private static (StringBuilder? Builder, int Cells) Clip(
        IReadOnlyList<StatusLineBlock> blocks, StatusLineAlignment alignment, int width,
        bool blockSeparator, SegmentStyle style, bool neutralBackground, TextContrastMode textContrast, bool roundOuterEdges) {
        (StringBuilder? builder, int measuredCells) = Compose(blocks, alignment, style, blockSeparator,
            Math.Max(0, width), neutralBackground, textContrast, roundOuterEdges);
        if (builder is null) {
            return (null, 0);
        }
        // Fitting leaves overflow only when a single block cannot shrink enough.
        if (measuredCells <= width) {
            return (builder, measuredCells);
        }

        char[]? buffer = null;
        try {
            StringBuilder.ChunkEnumerator chunks = builder.GetChunks().GetEnumerator();
            _ = chunks.MoveNext();
            ReadOnlySpan<char> text = chunks.Current.Span;
            if (chunks.MoveNext()) {
                buffer = ArrayPool<char>.Shared.Rent(builder.Length);
                builder.CopyTo(0, buffer.AsSpan(0, builder.Length), builder.Length);
                text = buffer.AsSpan(0, builder.Length);
            }

            (int length, int cells) = TerminalText.ClipLength(text, Math.Max(0, width));
            if (length < builder.Length) {
                builder.Length = length;
                builder.Append(Reset);
            }

            return (builder, cells);
        }
        catch {
            StringBuilderCache.Return(builder);
            throw;
        }
        finally {
            if (buffer is not null) {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }
    }
    private static void MoveTo(StringBuilder builder, ref int column, int target, bool useCursorPositioning, bool neutralBackground) {
        if (useCursorPositioning) {
            Vt.AppendCursorPosition(builder, 1, target + 1);
        }
        else {
            if (neutralBackground) {
                builder.Append(Vt.ResetReverse);
            }

            builder.Append(' ', Math.Max(0, target - column));
            if (neutralBackground) {
                builder.Append(Reset);
            }
        }

        column = target;
    }
    private static bool CanBlend(IReadOnlyList<StatusLineBlock> blocks, bool rgbGradient) {
        for (int index = 0; index < blocks.Count; index++) {
            StatusLineBlock block = blocks[index];
            if (block.PreserveBackground || block.PreserveForeground) {
                return false;
            }

            if (!rgbGradient && (block.Background.IsEmpty ? block.Foreground : block.Background).Palette is not null) {
                return false;
            }
        }

        return true;
    }

    private static TerminalColor GroupFill(IReadOnlyList<StatusLineBlock> blocks, StatusLineAlignment alignment,
        Color start, Color end, bool last = false) {
        TerminalColor fill = Color.Empty;
        for (int index = 0; index < blocks.Count; index++) {
            StatusLineBlock block = blocks[index];
            if (block.Alignment != alignment) {
                continue;
            }

            fill = block.Background.IsEmpty ? (block.Foreground.IsEmpty ? DefaultSegmentFill : block.Foreground) : block.Background;
            if (!start.IsEmpty) {
                fill = BlockGradient.Interpolate(start, end, index, blocks.Count);
            }

            if (!last) {
                break;
            }
        }

        return fill;
    }

    private static (StringBuilder? Builder, int Cells) Compose(IReadOnlyList<StatusLineBlock> blocks, StatusLineAlignment alignment,
        SegmentStyle style, bool blockSeparator, int width, bool neutralBackground, TextContrastMode textContrast, bool roundOuterEdges) {
        if (width <= 0) {
            return (null, 0);
        }

        StringBuilder? builder = null;
        int used = 0;
        bool first = true;
        TerminalColor previous = Color.Empty;
        bool right = alignment == StatusLineAlignment.Right;
        bool arrows = Glyphs.UsesJoinedSegments(style, alignment);
        bool separated = blockSeparator && arrows && Glyphs.SupportsBlockSeparator(style);
        (char leftSeparator, char rightSeparator) = Glyphs.Separators(style);
        char arrow = right ? leftSeparator : rightSeparator;
        for (int index = 0; index < blocks.Count; index++) {
            StatusLineBlock block = blocks[index];
            if (block.Alignment != alignment) {
                continue;
            }

            if (used >= width) {
                break;
            }

            builder ??= StringBuilderCache.Rent();
            if (style == SegmentStyle.Plain) {
                TerminalColor foreground = block.Foreground;
                if (!block.Background.IsEmpty && !block.PreserveForeground) {
                    foreground = TextContrast.Select(block.Background, foreground, textContrast);
                }

                AppendSegmentBody(builder, block, (foreground, block.Background));
                builder.Append(Reset);
            }
            else {
                (TerminalColor Foreground, TerminalColor Background) colors = SegmentColors(block, textContrast);
                if (first && roundOuterEdges && arrows && !right) {
                    AppendCapStyle(builder, colors.Background, neutralBackground);
                    builder.Append(Glyphs.LeftCap);
                }
                if (separated) {
                    if (!first) {
                        if (right) {
                            AppendTransparentEdge(builder, previous, arrow);
                        }
                        else {
                            AppendCapStyle(builder, previous, neutralBackground);
                            builder.Append(arrow);
                        }

                        builder.Append(Reset);
                    }

                    if (right || !first) {
                        if (right) {
                            AppendCapStyle(builder, colors.Background, neutralBackground);
                            builder.Append(arrow);
                        }
                        else {
                            AppendTransparentEdge(builder, colors.Background, arrow);
                        }
                    }
                    AppendSegmentBody(builder, block, colors);
                }
                else if (!arrows) {
                    if (!first) {
                        AppendGap(builder, neutralBackground);
                    }
                    AppendCapStyle(builder, colors.Background, neutralBackground);
                    builder.Append(Glyphs.LeftCap);
                    AppendSegmentBody(builder, block, colors);
                    AppendCapStyle(builder, colors.Background, neutralBackground);
                    builder.Append(Glyphs.RightCap);
                }
                else {
                    if (right || !first) {
                        if (first && neutralBackground) {
                            AppendCapStyle(builder, colors.Background, neutralBackground);
                        }
                        else {
                            Vt.AppendSgr(builder, right ? colors.Background : previous,
                                right ? previous : colors.Background);
                        }

                        builder.Append(arrow);
                    }

                    AppendSegmentBody(builder, block, colors);
                }

                previous = colors.Background;
            }

            used += block.Cells + Overhead(style, arrows, blockSeparator, right, first, roundOuterEdges);
            first = false;
        }

        if (builder is null) {
            return (null, 0);
        }

        if (style != SegmentStyle.Plain) {
            if (!right && arrows) {
                AppendCapStyle(builder, previous, neutralBackground);
                builder.Append(arrow);
            }
            if (right && roundOuterEdges && arrows) {
                AppendCapStyle(builder, previous, neutralBackground);
                builder.Append(Glyphs.RightCap);
            }

            builder.Append(Reset);
        }

        return (builder, used + Trailing(style, arrows, blockSeparator, right, roundOuterEdges));
    }

    // Cells drawn around a block's body; Compose and Measure must agree.
    private static int Overhead(SegmentStyle style, bool arrows, bool blockSeparator, bool right, bool first, bool roundOuterEdges) {
        if (style == SegmentStyle.Plain) {
            return 0;
        }

        int cells = first && roundOuterEdges && arrows && !right ? 1 : 0;
        if (blockSeparator && arrows && Glyphs.SupportsBlockSeparator(style)) {
            cells += first ? (right ? 1 : 0) : 2;
        }
        else if (!arrows) {
            // Two caps, plus the gap before every pill but the first.
            cells += first ? 2 : 3;
        }
        else if (right || !first) {
            cells++;
        }

        return cells;
    }

    private static int Trailing(SegmentStyle style, bool arrows, bool blockSeparator, bool right, bool roundOuterEdges) {
        if (style == SegmentStyle.Plain || !arrows) {
            return 0;
        }

        return !right || roundOuterEdges ? 1 : 0;
    }

    /// <summary>Cells a group would occupy without clipping.</summary>
    private static int Measure(IReadOnlyList<StatusLineBlock> blocks, StatusLineAlignment alignment,
        SegmentStyle style, bool blockSeparator, bool roundOuterEdges) {
        bool right = alignment == StatusLineAlignment.Right;
        bool arrows = Glyphs.UsesJoinedSegments(style, alignment);
        int used = 0;
        bool first = true;
        for (int index = 0; index < blocks.Count; index++) {
            StatusLineBlock block = blocks[index];
            if (block.Alignment != alignment) {
                continue;
            }

            used += block.Cells + Overhead(style, arrows, blockSeparator, right, first, roundOuterEdges);
            first = false;
        }

        return first ? 0 : used + Trailing(style, arrows, blockSeparator, right, roundOuterEdges);
    }

    // Shrinkable text keeps at least this many cells before whole blocks are dropped.
    internal const int MinimumShrinkCells = 12;
    [ThreadStatic] private static List<StatusLineBlock>? rowScratch;
    [ThreadStatic] private static List<StatusLineBlock>? groupScratch;

    /// <summary>
    /// Fits one group, or a left and right group sharing a row, into <paramref name="width"/>:
    /// shrinkable blocks get a middle ellipsis first, then whole blocks are dropped from the end of the row.
    /// Returns <paramref name="blocks"/> unchanged when it already fits; otherwise <paramref name="scratch"/>.
    /// </summary>
    private static IReadOnlyList<StatusLineBlock> Fit(IReadOnlyList<StatusLineBlock> blocks, List<StatusLineBlock> scratch,
        int width, StatusLineAlignment primary, StatusLineAlignment? secondary, int gap,
        SegmentStyle style, bool blockSeparator, bool roundOuterEdges) {
        if (Total(blocks) <= width) {
            return blocks;
        }

        scratch.Clear();
        for (int index = 0; index < blocks.Count; index++) {
            StatusLineAlignment alignment = blocks[index].Alignment;
            if (alignment == primary || alignment == secondary) {
                scratch.Add(blocks[index]);
            }
        }

        while (true) {
            int excess = Total(scratch) - width;
            if (excess <= 0) {
                return scratch;
            }

            int reducible = 0;
            int drop = -1;
            for (int index = 0; index < scratch.Count; index++) {
                StatusLineBlock block = scratch[index];
                if (block.Shrinkable) {
                    reducible += Math.Max(0, TerminalText.Measure(block.Text) - MinimumShrinkCells);
                }
                // Prefer the last secondary block, then the last primary block; shrinkable blocks go last.
                else if (drop < 0 || scratch[drop].Alignment != secondary || block.Alignment == secondary) {
                    drop = index;
                }
            }

            if (excess <= reducible || scratch.Count <= 1) {
                Shrink(scratch, excess);
                return scratch;
            }

            scratch.RemoveAt(drop >= 0 ? drop : scratch.Count - 1);
        }

        int Total(IReadOnlyList<StatusLineBlock> list) {
            int cells = Measure(list, primary, style, blockSeparator, roundOuterEdges);
            if (secondary is { } other) {
                int second = Measure(list, other, style, blockSeparator, roundOuterEdges);
                cells += second > 0 && cells > 0 ? second + gap : second;
            }

            return cells;
        }
    }

    private static void Shrink(List<StatusLineBlock> blocks, int excess) {
        for (int index = 0; index < blocks.Count && excess > 0; index++) {
            StatusLineBlock block = blocks[index];
            if (!block.Shrinkable) {
                continue;
            }

            int text = TerminalText.Measure(block.Text);
            int take = Math.Min(excess, Math.Max(0, text - MinimumShrinkCells));
            if (take > 0) {
                blocks[index] = block.Shorten(text - take);
                excess -= take;
            }
        }
    }

    // Paint the complementary shape; reverse video paints an opaque default-color triangle.
    private static void AppendTransparentEdge(StringBuilder builder, TerminalColor fill, char glyph) {
        Vt.AppendSgr(builder, fill, Color.Empty);
        builder.Append(Glyphs.Complement(glyph));
    }
    private static void AppendGap(StringBuilder builder, bool neutralBackground) {
        builder.Append(neutralBackground ? Vt.ResetReverse : Reset);
        builder.Append(' ');
        if (neutralBackground) {
            builder.Append(Reset);
        }
    }
    private static void AppendCapStyle(StringBuilder builder, TerminalColor fill, bool neutralBackground) {
        // Contour reverses default status-line colors. Inverse keeps the cap on the page background.
        Vt.AppendSgr(builder, neutralBackground ? Color.Empty : fill, neutralBackground ? fill : Color.Empty);
        if (neutralBackground) {
            builder.Append(Vt.Reverse);
        }
    }
    private static void AppendSegmentBody(StringBuilder builder, StatusLineBlock block, (TerminalColor Foreground, TerminalColor Background) colors) {
        Vt.AppendSgr(builder, colors.Foreground, colors.Background, block.Bold, block.Italic, block.Underline);
        builder.Append(block.LeadingPadding);
        builder.Append(block.Content);
        if (block.Padding.Length > 0) {
            Vt.AppendSgr(builder, colors.Foreground, colors.Background, block.Bold, block.Italic, block.Underline);
            builder.Append(block.Padding);
        }
    }
    /// <summary>Filled segments use the background, or the foreground as the fill when no background is set.</summary>
    private static (TerminalColor Foreground, TerminalColor Background) SegmentColors(StatusLineBlock block, TextContrastMode textContrast) {
        if (!block.Background.IsEmpty) {
            return (block.PreserveForeground ? block.Foreground : TextContrast.Select(block.Background, block.Foreground, textContrast), block.Background);
        }

        TerminalColor fill = block.Foreground.IsEmpty ? DefaultSegmentFill : block.Foreground;
        return (block.PreserveForeground ? block.Foreground : TextContrast.Select(fill, Color.Empty, textContrast), fill);
    }

    private const string Reset = Vt.Reset;
    private static readonly TerminalColor DefaultSegmentFill = Color.FromArgb(0x3E, 0x44, 0x52);
}
