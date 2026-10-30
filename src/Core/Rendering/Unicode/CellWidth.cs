using System.Globalization;
using System.Text;
namespace StatusLine;

/// <summary>
/// Approximate terminal cell width of a grapheme cluster. The background thread has no host
/// to ask, so this uses East Asian Wide/Fullwidth and emoji presentation ranges.
/// </summary>
internal static class CellWidth {
    public static int Of(ReadOnlySpan<char> textElement) {
        if (textElement.Length == 0) {
            return 0;
        }

        // VS16 forces emoji presentation, which terminals render two cells wide.
        if (textElement.Contains('\uFE0F')) {
            return 2;
        }

        Rune.DecodeFromUtf16(textElement, out Rune rune, out _);
        UnicodeCategory category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format) {
            return 0;
        }

        if (rune.Value is >= 0x1160 and <= 0x11FF) {
            return 0;
        }

        if (textElement.Contains('\uFE0E') && CellWidthData.IsEmoji(rune.Value)) {
            return 1;
        }

        return CellWidthData.IsWide(rune.Value) || CellWidthData.IsEmoji(rune.Value) ? 2 : 1;
    }
}
