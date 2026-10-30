namespace StatusLine;

/// <summary>Powerline separator glyphs from the Nerd Fonts / Powerline Extra private use area.</summary>
internal static class Glyphs {
    public const char RightArrow = '\uE0B0';
    public const char RightArrowCutout = '\uE0D7';
    public const char LeftArrowCutout = '\uE0D6';
    public const char LeftArrow = '\uE0B2';
    public const char LeftCap = '\uE0B6';
    public const char RightCap = '\uE0B4';

    internal static bool UsesJoinedSegments(SegmentStyle style, StatusLineAlignment alignment) =>
        style is SegmentStyle.Rounded || (style is SegmentStyle.Powerline && alignment != StatusLineAlignment.Center);

    // Transparent block breaks need a complementary cutout glyph; only the arrows have one.
    internal static bool SupportsBlockSeparator(SegmentStyle style) => style is SegmentStyle.Powerline;

    internal static (char Left, char Right) Separators(SegmentStyle style) => style switch {
        SegmentStyle.Rounded => (LeftCap, RightCap),
        _ => (LeftArrow, RightArrow),
    };

    internal static char Complement(char glyph) => glyph switch {
        RightArrow => RightArrowCutout,
        LeftArrow => LeftArrowCutout,
        _ => throw new ArgumentOutOfRangeException(nameof(glyph)),
    };
}
