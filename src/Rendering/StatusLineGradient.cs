using System.Drawing;
using System.Globalization;
using System.Text;

namespace StatusLine;

/// <summary>
/// Measures groups, then emits their final colors at absolute terminal columns. The row's colors only
/// change with width or endpoints, so each column's SGR parameters are formatted once and reused.
/// </summary>
internal static class StatusLineGradient {
    [ThreadStatic] private static Ramp? ramp;

    internal static string Render(IReadOnlyList<StatusLineBlock> blocks, int width, SegmentStyle style,
        TerminalColor start, TerminalColor end, TextContrastMode contrast, bool roundOuterEdges,
        StatusLineFraming framing = StatusLineFraming.None) {
        StringBuilder output = StringBuilderCache.Rent();
        try {
            Append(output, blocks, width, style, start, end, contrast, roundOuterEdges, framing);
            return output.ToString();
        }
        finally {
            StringBuilderCache.Return(output);
        }
    }

    internal static void Append(StringBuilder output, IReadOnlyList<StatusLineBlock> blocks, int width, SegmentStyle style,
        TerminalColor start, TerminalColor end, TextContrastMode contrast, bool roundOuterEdges,
        StatusLineFraming framing = StatusLineFraming.None) {
        var colors = Ramp.For(ref ramp, width, start.Rgb, end.Rgb);
        int centerCells = MeasureGroup(blocks, StatusLineAlignment.Center, width, style, roundOuterEdges);
        var layout = new GroupLayout(width, centerCells);
        int centerColumn = layout.CenterColumn;
        int leftCells = MeasureGroup(blocks, StatusLineAlignment.Left,
            layout.LeftBudget, style, roundOuterEdges);
        int rightCells = MeasureGroup(blocks, StatusLineAlignment.Right,
            layout.RightBudget(leftCells), style, roundOuterEdges);

        var paint = new PaintState(colors, contrast);
        Vt.AppendFrameStart(output, framing);
        int column = 0;
        AppendGroup(output, blocks, StatusLineAlignment.Left, ref column, leftCells, style, roundOuterEdges, ref paint);
        if (centerCells > 0) {
            AppendGap(output, ref column, centerColumn, ref paint);
            AppendGroup(output, blocks, StatusLineAlignment.Center, ref column, centerColumn + centerCells, style, roundOuterEdges, ref paint);
        }

        if (rightCells > 0) {
            AppendGap(output, ref column, width - rightCells, ref paint);
            AppendGroup(output, blocks, StatusLineAlignment.Right, ref column, width, style, roundOuterEdges, ref paint);
        }

        AppendGap(output, ref column, width, ref paint);
        output.Append(Vt.Reset);
        Vt.AppendFrameEnd(output, framing);
    }

    /// <summary>Per-width colors plus each column's preformatted "r;g;b" SGR parameters.</summary>
    private sealed class Ramp {
        private const string Black = "0;0;0";
        private const string White = "255;255;255";
        private readonly int start;
        private readonly int end;
        internal readonly Color[] Background;
        internal readonly bool[] DarkText;
        private readonly char[] parameters;
        private readonly int[] offsets;

        private Ramp(int width, Color from, Color to) {
            start = from.ToArgb();
            end = to.ToArgb();
            Background = new Color[width];
            DarkText = new bool[width];
            offsets = new int[width + 1];
            parameters = new char[width * White.Length];
            int position = 0;
            for (int column = 0; column < width; column++) {
                Color background = BlockGradient.Interpolate(from, to, column, width);
                Background[column] = background;
                DarkText[column] = TextContrast.Select(background, Color.Empty, TextContrastMode.Automatic).Rgb.ToArgb() == Color.Black.ToArgb();
                offsets[column] = position;
                Write(background.R, ref position);
                parameters[position++] = ';';
                Write(background.G, ref position);
                parameters[position++] = ';';
                Write(background.B, ref position);
            }

            offsets[width] = position;
        }

        internal static Ramp For(ref Ramp? cached, int width, Color from, Color to) {
            if (cached is { } current && current.Background.Length == width &&
                current.start == from.ToArgb() && current.end == to.ToArgb()) {
                return current;
            }

            return cached = new Ramp(width, from, to);
        }

        private void Write(byte value, ref int position) {
            value.TryFormat(parameters.AsSpan(position), out int written, provider: CultureInfo.InvariantCulture);
            position += written;
        }

        internal ReadOnlySpan<char> Rgb(int column) => parameters.AsSpan(offsets[column], offsets[column + 1] - offsets[column]);

        internal static string Text(bool dark) => dark ? Black : White;
    }

    private static int MeasureGroup(IReadOnlyList<StatusLineBlock> blocks, StatusLineAlignment alignment, int width,
        SegmentStyle style, bool roundOuterEdges) {
        int used = 0;
        bool first = true;
        bool arrows = Glyphs.UsesJoinedSegments(style, alignment);
        bool right = alignment == StatusLineAlignment.Right;
        for (int index = 0; index < blocks.Count; index++) {
            StatusLineBlock block = blocks[index];
            if (block.Alignment != alignment) {
                continue;
            }

            int before = 1;
            if (arrows && !right && first && !roundOuterEdges) {
                before = 0;
            }

            int after = arrows ? 0 : 1;
            if (used + before + block.Cells + after <= width) {
                used += before + block.Cells + after;
            }
            else {
                used = Math.Min(width, used + before);
                if (!MeasureText(block.LeadingPadding, width, ref used) ||
                    !MeasureText(block.Content, width, ref used) ||
                    !MeasureText(block.Padding, width, ref used)) {
                    return used;
                }

                return Math.Min(width, used + after);
            }

            first = false;
        }

        return !first && arrows && (!right || roundOuterEdges) ? Math.Min(width, used + 1) : used;
    }

    private static bool MeasureText(ReadOnlySpan<char> text, int width, ref int used) {
        (int length, int cells) = TerminalText.ClipLength(text, width - used);
        used += cells;
        return length == text.Length;
    }
    // Cap and separator cells keep their style's layout but are drawn as background-only spaces:
    // over a continuous gradient a glyph colored like its own background is invisible.
    private static void AppendGroup(StringBuilder output, IReadOnlyList<StatusLineBlock> blocks,
        StatusLineAlignment alignment, ref int column, int end, SegmentStyle style, bool roundOuterEdges,
        ref PaintState paint) {
        bool first = true;
        bool arrows = Glyphs.UsesJoinedSegments(style, alignment);
        bool right = alignment == StatusLineAlignment.Right;
        TextContrastMode contrast = paint.Contrast;
        for (int index = 0; index < blocks.Count; index++) {
            StatusLineBlock block = blocks[index];
            if (block.Alignment != alignment) {
                continue;
            }

            if (column >= end) {
                return;
            }

            if ((!arrows || right || !first || roundOuterEdges) && !AppendCap(output, ref column, end, ref paint)) {
                return;
            }

            TerminalColor foreground = block.Background.IsEmpty
                ? TextContrast.Select(block.Foreground, Color.Empty, contrast)
                : TextContrast.Select(block.Background, block.Foreground, contrast);
            paint.SetBlockStyle(output, block, foreground, column);
            bool customForeground = false;
            if (!AppendText(output, block.LeadingPadding, ref column, end, ref customForeground, ref paint) ||
                !AppendText(output, block.Content, ref column, end, ref customForeground, ref paint)) {
                return;
            }

            if (block.Padding.Length > 0 && column < end) {
                paint.SetBlockStyle(output, block, foreground, column);
                customForeground = false;
                if (!AppendText(output, block.Padding, ref column, end, ref customForeground, ref paint)) {
                    return;
                }
            }

            if (!arrows && !AppendCap(output, ref column, end, ref paint)) {
                return;
            }

            first = false;
        }

        if (!first && arrows && (!right || roundOuterEdges)) {
            _ = AppendCap(output, ref column, end, ref paint);
        }
    }

    private static bool AppendText(StringBuilder output, ReadOnlySpan<char> text, ref int column, int end,
        ref bool customForeground, ref PaintState paint) {
        TextContrastMode contrast = paint.Contrast;
        int offset = 0;
        int numberEnd = 0;
        int contrastColumn = -1;
        while (offset < text.Length) {
            if (text[offset] == Vt.Escape) {
                // Only caller-supplied content contains VT here; generated output is never reparsed.
                int length = TerminalText.EscapeLength(text[offset..], out bool sgr);
                ReadOnlySpan<char> sequence = text.Slice(offset, length);
                if (sgr) {
                    TrackForeground(sequence[2..^1], ref customForeground);
                    output.Append(sequence);
                    paint.Invalidate();
                }

                offset += length;
                continue;
            }

            int next, cells;
            if (TextMeasurement.UseUnicode) {
                Unicode.MeasurementResult cluster = Unicode.Measure(text, offset, maxCursorMovements: 1, skipVT: false);
                next = cluster.Offset;
                cells = cluster.Columns;
            }
            else {
                int length = StringInfo.GetNextTextElementLength(text[offset..]);
                next = offset + length;
                cells = CellWidth.Of(text.Slice(offset, length));
            }

            if (next <= offset || column + cells > end) {
                return false;
            }

            if (cells > 0) {
                if (offset >= numberEnd) {
                    contrastColumn = -1;
                    if (!customForeground && contrast == TextContrastMode.Automatic &&
                        (char.IsDigit(text[offset]) || text[offset] is '+' or '-' or '−')) {
                        int length = NumericLength(text[offset..]);
                        if (length > 0) {
                            numberEnd = offset + length;
                            (_, int visibleCells) = TerminalText.ClipLength(text.Slice(offset, length), end - column);
                            // Choose once from the visible number's midpoint; backgrounds still vary per cell.
                            contrastColumn = column + (Math.Max(0, visibleCells - 1) / 2);
                        }
                    }
                }

                paint.PaintText(output, column, customForeground, contrastColumn);
            }

            output.Append(text[offset..next]);
            column += cells;
            offset = next;
        }

        return true;
    }

    private static int NumericLength(ReadOnlySpan<char> text) {
        int offset = 0;
        if (text.Length > 1 && text[0] is '+' or '-' or '−') {
            offset++;
        }

        if (!char.IsDigit(text[offset])) {
            return 0;
        }

        offset++;
        while (offset < text.Length) {
            if (char.IsDigit(text[offset])) {
                offset++;
                continue;
            }

            if (text[offset] is '.' or ',' or '\u066b' or '\u066c' or '\u00a0' or '\u202f' or '\u2009' &&
                offset + 1 < text.Length && char.IsDigit(text[offset + 1])) {
                offset += 2;
                continue;
            }

            if (text[offset] is 'e' or 'E') {
                int exponent = offset + 1;
                if (exponent < text.Length && text[exponent] is '+' or '-') {
                    exponent++;
                }

                if (exponent < text.Length && char.IsDigit(text[exponent])) {
                    offset = exponent + 1;
                    continue;
                }
            }

            break;
        }

        if (offset < text.Length && text[offset] is '%' or '\u066a' or '‰') {
            offset++;
        }

        return offset;
    }

    private static void AppendGap(StringBuilder output, ref int column, int end, ref PaintState paint) {
        paint.ResetStyle(output);
        while (column < end) {
            paint.PaintBackground(output, column++);
            output.Append(' ');
        }
    }

    private static bool AppendCap(StringBuilder output, ref int column, int end, ref PaintState paint) {
        if (column >= end) {
            return false;
        }

        paint.ResetStyle(output);
        paint.PaintBackground(output, column++);
        output.Append(' ');
        return true;
    }

    /// <summary>Tracks emitted colors so a cell only receives the SGR parameters that changed.</summary>
    private struct PaintState {
        private const long Unknown = long.MinValue;
        private static readonly int BlackArgb = Color.Black.ToArgb();
        private static readonly int WhiteArgb = Color.White.ToArgb();
        private readonly Ramp ramp;
        internal readonly TextContrastMode Contrast;
        private long lastForeground;
        private long lastBackground;
        // Underline or caller SGR may be active; a space would otherwise show it.
        private bool styled;

        internal PaintState(Ramp ramp, TextContrastMode contrast) {
            this.ramp = ramp;
            Contrast = contrast;
            lastForeground = Unknown;
            lastBackground = Unknown;
            styled = true;
        }

        internal void Invalidate() {
            lastForeground = Unknown;
            lastBackground = Unknown;
            styled = true;
        }

        internal void ResetStyle(StringBuilder output) {
            if (!styled) {
                return;
            }

            output.Append(Vt.Reset);
            lastForeground = Unknown;
            lastBackground = Unknown;
            styled = false;
        }

        internal void SetBlockStyle(StringBuilder output, StatusLineBlock block, TerminalColor foreground, int column) {
            Color background = ramp.Background[Math.Min(column, ramp.Background.Length - 1)];
            Vt.AppendSgr(output, foreground, background, block.Bold, block.Italic, block.Underline);
            lastForeground = !foreground.IsEmpty && foreground.Palette is null ? foreground.Rgb.ToArgb() : Unknown;
            lastBackground = background.ToArgb();
            styled = block.Underline;
        }

        internal void PaintText(StringBuilder output, int column, bool customForeground, int contrastColumn) {
            if (!customForeground && Contrast != TextContrastMode.Preserve) {
                bool dark = Contrast switch {
                    TextContrastMode.Black => true,
                    TextContrastMode.White => false,
                    _ => ramp.DarkText[contrastColumn < 0 ? column : contrastColumn],
                };
                int foreground = dark ? BlackArgb : WhiteArgb;
                if (lastForeground != foreground) {
                    output.Append("\e[38;2;").Append(Ramp.Text(dark)).Append(";48;2;").Append(ramp.Rgb(column)).Append('m');
                    lastForeground = foreground;
                    lastBackground = ramp.Background[column].ToArgb();
                    return;
                }
            }

            PaintBackground(output, column);
        }

        internal void PaintBackground(StringBuilder output, int column) {
            int background = ramp.Background[column].ToArgb();
            if (lastBackground != background) {
                output.Append("\e[48;2;").Append(ramp.Rgb(column)).Append('m');
                lastBackground = background;
            }
        }
    }

    private static void TrackForeground(ReadOnlySpan<char> parameters, ref bool custom) {
        int offset = 0;
        while (offset <= parameters.Length) {
            ReadOnlySpan<char> token = Next(parameters, ref offset);
            int code = ParseCode(token);
            if (code is Vt.ResetAttributesCode or Vt.DefaultForegroundCode) {
                custom = false;
            }
            else if (code is (>= Vt.StandardForegroundFirst and <= Vt.StandardForegroundLast) or
                (>= Vt.BrightForegroundFirst and <= Vt.BrightForegroundLast)) {
                custom = true;
            }
            else if (code is Vt.ExtendedForegroundCode or Vt.ExtendedBackgroundCode or Vt.ExtendedUnderlineColorCode) {
                int mode = ParseCode(Next(parameters, ref offset));
                if (mode == Vt.RgbColorMode) {
                    for (int channel = 0; channel < 3; channel++) {
                        _ = Next(parameters, ref offset);
                    }
                }
                else if (mode == Vt.IndexedColorMode) {
                    _ = Next(parameters, ref offset);
                }

                if (code == Vt.ExtendedForegroundCode) {
                    custom = true;
                }
            }
        }
    }

    private static ReadOnlySpan<char> Next(ReadOnlySpan<char> parameters, ref int offset) {
        if (offset > parameters.Length) {
            return [];
        }

        ReadOnlySpan<char> remaining = parameters[offset..];
        int separator = remaining.IndexOf(';');
        ReadOnlySpan<char> token = separator < 0 ? remaining : remaining[..separator];
        offset += token.Length + 1;
        return token;
    }

    private static int ParseCode(ReadOnlySpan<char> token)
        => int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : 0;
}
