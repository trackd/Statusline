using System.Drawing;

namespace StatusLine;

/// <summary>Foreground selection for text inside filled prompt/statusline blocks.</summary>
public enum TextContrastMode {
    Automatic,
    Preserve,
    Black,
    White,
}

internal static class TextContrast {
    // Renderers run on separate threads; avoid locks and bound retained custom colors.
    [ThreadStatic] private static Dictionary<int, TerminalColor>? automaticColors;

    private static readonly double[] Linear = [.. Enumerable.Range(0, 256).Select(value => {
        double channel = value / 255.0;
        return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    })];

    internal static TerminalColor Select(TerminalColor background, TerminalColor foreground, TextContrastMode mode) {
        if (mode == TextContrastMode.Preserve) {
            return foreground;
        }

        if (mode == TextContrastMode.Black) {
            return Color.Black;
        }

        if (mode == TextContrastMode.White) {
            return Color.White;
        }
        // Terminal palette RGB is unknown; retain the supplied foreground/default in that case.
        if (background.Palette is not null || background.IsEmpty) {
            return foreground;
        }

        Color rgb = background.Rgb;
        int key = rgb.ToArgb();
        Dictionary<int, TerminalColor> cache = automaticColors ??= [];
        if (cache.TryGetValue(key, out TerminalColor cached)) {
            return cached;
        }

        double luminance = (0.2126 * Linear[rgb.R]) + (0.7152 * Linear[rgb.G]) + (0.0722 * Linear[rgb.B]);
        TerminalColor selected = luminance >= 0.1791287847 ? Color.Black : Color.White;
        if (cache.Count >= 256) {
            cache.Clear();
        }

        cache.Add(key, selected);
        return selected;
    }
}
