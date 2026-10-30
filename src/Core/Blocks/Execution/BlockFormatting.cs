using System.Globalization;
using System.Text;

namespace StatusLine.Blocks.Execution;

internal static class BlockFormatting {
    internal static readonly TerminalColor Blue = ConsoleColor.DarkBlue;
    internal static readonly TerminalColor Green = ConsoleColor.DarkGreen;
    internal static readonly TerminalColor Yellow = ConsoleColor.DarkYellow;
    internal static readonly TerminalColor Red = ConsoleColor.DarkRed;
    internal static readonly TerminalColor Purple = ConsoleColor.DarkMagenta;
    internal static readonly TerminalColor Cyan = ConsoleColor.DarkCyan;
    internal static readonly TerminalColor Gray = ConsoleColor.Gray;

    internal static BlockContent? CountBlock(string icon, int count, TerminalColor color) {
        if (count <= 0) {
            return null;
        }

        return (icon, count.ToString(CultureInfo.InvariantCulture), StatusLineAlignment.Left, color);
    }

    internal static TerminalColor LoadColor(double percent)
        => percent switch {
            < 50 => Green,
            < 80 => Yellow,
            _ => Red,
        };

    internal static string FormatDuration(TimeSpan duration) {
        if (duration.TotalMinutes >= 1) {
            return string.Create(CultureInfo.InvariantCulture, $"{(long)duration.TotalMinutes}m{duration.Seconds}s");
        }

        if (duration.TotalMilliseconds < 100) {
            return string.Create(CultureInfo.InvariantCulture, $"{duration.TotalMilliseconds:0}ms");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{duration.TotalSeconds:0.0}s");
    }

    internal static string FormatUptime(TimeSpan uptime) {
        if (uptime.TotalDays >= 1) {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)uptime.TotalDays}d {uptime.Hours}h");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{uptime.Hours}h {uptime.Minutes:00}m");
    }

    internal static string Pick((string NerdFont, string Unicode, string None) icons, IconSet set) =>
        set switch {
            IconSet.NerdFont => icons.NerdFont,
            IconSet.Unicode => icons.Unicode,
            _ => icons.None,
        };
}
