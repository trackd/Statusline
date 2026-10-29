using System.Drawing;
using System.Globalization;
using System.Text;

namespace StatusLine;

internal static partial class Vt {
    /// <summary>Builds a reset-prefixed SGR sequence; empty colors select the terminal defaults.</summary>
    internal static string Sgr(TerminalColor foreground, TerminalColor background, bool bold = false, bool italic = false, bool underline = false) {
        Span<char> buffer = stackalloc char[96];
        return new string(buffer[..WriteSgr(buffer, foreground, background, bold, italic, underline)]);
    }

    internal static void AppendSgr(StringBuilder builder, TerminalColor foreground, TerminalColor background, bool bold = false, bool italic = false, bool underline = false) {
        Span<char> buffer = stackalloc char[96];
        int length = WriteSgr(buffer, foreground, background, bold, italic, underline);
        builder.Append(buffer[..length]);
    }

    private static int WriteSgr(Span<char> buffer, TerminalColor foreground, TerminalColor background, bool bold, bool italic, bool underline) {
        "\e[0".AsSpan().CopyTo(buffer);
        int position = 3;
        AppendColor(buffer, ref position, foreground, ExtendedForegroundCode, DefaultForegroundCode);
        AppendColor(buffer, ref position, background, ExtendedBackgroundCode, DefaultBackgroundCode);
        if (bold) {
            buffer[position++] = ';';
            buffer[position++] = '1';
        }

        if (italic) {
            buffer[position++] = ';';
            buffer[position++] = '3';
        }

        if (underline) {
            buffer[position++] = ';';
            buffer[position++] = '4';
        }

        buffer[position++] = 'm';
        return position;
    }

    private static void AppendColor(Span<char> buffer, ref int position, TerminalColor color, int rgbCode, int defaultCode) {
        buffer[position++] = ';';
        if (color.IsEmpty) {
            AppendNumber(buffer, ref position, defaultCode);
            return;
        }

        int palette = color.PaletteIndex;
        if (palette >= 0) {
            AppendNumber(buffer, ref position, (rgbCode == ExtendedForegroundCode ? 30 : 40) + (palette & 7) + (palette >= 8 ? 60 : 0));
            return;
        }

        Color rgb = color.Rgb;
        AppendNumber(buffer, ref position, rgbCode);
        ";2;".AsSpan().CopyTo(buffer[position..]);
        position += 3;
        AppendNumber(buffer, ref position, rgb.R);
        buffer[position++] = ';';
        AppendNumber(buffer, ref position, rgb.G);
        buffer[position++] = ';';
        AppendNumber(buffer, ref position, rgb.B);
    }

    private static void AppendNumber(Span<char> buffer, ref int position, int value) {
        value.TryFormat(buffer[position..], out int written, provider: CultureInfo.InvariantCulture);
        position += written;
    }
}
