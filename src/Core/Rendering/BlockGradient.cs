using System.Drawing;

namespace StatusLine;

internal static class BlockGradient {
    // Keep context/input and statusline gradients reusable without caching dynamic block text.
    [ThreadStatic] private static Dictionary<(int Start, int End, int Count), Color[]>? gradients;

    public static void Validate(Color start, Color end) {
        if (start.IsEmpty != end.IsEmpty) {
            throw new ArgumentException("Specify both StartColor and EndColor, or neither.");
        }
    }

    public static IReadOnlyList<StatusLineBlock> Apply(IReadOnlyList<StatusLineBlock> blocks, Color start, Color end, SegmentStyle style) {
        if (start.IsEmpty || blocks.Count == 0) {
            return blocks;
        }

        ReadOnlySpan<Color> colors = Colors(start, end, blocks.Count);
        var result = new StatusLineBlock[blocks.Count];
        for (int index = 0; index < blocks.Count; index++) {
            result[index] = Apply(blocks[index], colors[index], style);
        }

        return result;
    }

    /// <summary>Returns the cached ramp of <paramref name="count"/> colors from start to end.</summary>
    internal static ReadOnlySpan<Color> Colors(Color start, Color end, int count) {
        (int, int, int Count) key = (start.ToArgb(), end.ToArgb(), count);
        Dictionary<(int Start, int End, int Count), Color[]> cache = gradients ??= [];
        if (!cache.TryGetValue(key, out Color[]? colors)) {
            colors = new Color[count];
            for (int index = 0; index < colors.Length; index++) {
                colors[index] = Interpolate(start, end, index, colors.Length);
            }

            if (cache.Count >= 8) {
                cache.Clear();
            }

            cache.Add(key, colors);
        }

        return colors;
    }

    /// <summary>Plain style colors the text; filled styles color the segment.</summary>
    internal static StatusLineBlock Apply(StatusLineBlock block, Color color, SegmentStyle style) =>
        block.WithAppearance(block.Alignment, block.PreserveForeground ? block.Foreground : style == SegmentStyle.Plain ? color : Color.Empty,
            block.PreserveBackground ? block.Background : style == SegmentStyle.Plain ? block.Background : color);

    internal static Color Interpolate(Color start, Color end, int index, int count) =>
        Color.FromArgb(Step(start.R, end.R, index, count), Step(start.G, end.G, index, count), Step(start.B, end.B, index, count));

    internal static TerminalColor Interpolate(TerminalColor start, TerminalColor end, int index, int count) =>
        Interpolate(start.Rgb, end.Rgb, index, count);

    private static int Step(int start, int end, int index, int count) => count <= 1 ? start :
        ((start * (count - 1 - index)) + (end * index) + ((count - 1) / 2)) / (count - 1);
}
