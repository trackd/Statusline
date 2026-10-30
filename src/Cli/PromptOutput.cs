using System.Drawing;
using System.Text;

namespace StatusLine.Cli;

internal static class PromptOutput {
    private static readonly HashSet<StatusLineBlockKind> PowerShellOnly = [
        StatusLineBlockKind.PowerShell, StatusLineBlockKind.Errors, StatusLineBlockKind.StackCount,
        StatusLineBlockKind.NestedPrompt, StatusLineBlockKind.Runspaces, StatusLineBlockKind.HistoryId,
        // This provider measures the renderer process, not its parent shell.
        StatusLineBlockKind.ProcessMemory,
    ];

    internal static string Render(PromptOptions options, string path, int width, int status,
        double? durationMilliseconds, bool statusLine, bool wait = true) =>
        new PreparedPrompt(options).Render(path, width, status, durationMilliseconds, statusLine, wait);

    internal static void Validate(PromptOptions options) {
        if (options.Rows is not (1 or 2) || options.BlockTimeoutMilliseconds is < 1 or > 60000) {
            throw new ArgumentException("Rows must be 1 or 2; BlockTimeoutMilliseconds must be between 1 and 60000.");
        }

        if (!Enum.IsDefined(options.Style) || !Enum.IsDefined(options.IconSet) || !Enum.IsDefined(options.DirectoryStyle) ||
            !Enum.IsDefined(options.ShellIntegration) || !Enum.IsDefined(options.TextContrast)) {
            throw new ArgumentException("Style, IconSet, DirectoryStyle, ShellIntegration, and TextContrast must be defined values.");
        }

        ArgumentNullException.ThrowIfNull(options.Left);
        ArgumentNullException.ThrowIfNull(options.Right);
        ArgumentNullException.ThrowIfNull(options.Center);
        ArgumentNullException.ThrowIfNull(options.Padding);
        ArgumentNullException.ThrowIfNull(options.Symbol);
        ArgumentNullException.ThrowIfNull(options.TransientSymbol);
        foreach (string name in options.Left.Concat(options.Center).Concat(options.Right)) { _ = Kind(name); }

        Color start = options.StartColor is { } first ? NamedColors.Parse(first) : Color.Empty;
        Color end = options.EndColor is { } last ? NamedColors.Parse(last) : Color.Empty;
        BlockGradient.Validate(start, end);
    }

    internal static StatusLineBlockKind Kind(string name) {
        if (!Enum.TryParse(name, ignoreCase: true, out StatusLineBlockKind kind) || !Enum.IsDefined(kind) || PowerShellOnly.Contains(kind)) {
            throw new ArgumentException($"Unsupported shell block: {name}.");
        }

        return kind;
    }

    internal static string ForShell(string text, string shell) {
        if (shell is "raw" or "fish") {
            return text;
        }

        if (shell is not ("bash" or "zsh")) {
            throw new ArgumentException("Shell must be raw, bash, zsh, or fish.");
        }

        var output = new StringBuilder(text.Length);
        for (int index = 0; index < text.Length;) {
            if (TerminalSequence.IsEscape(text[index])) {
                int length = TerminalSequence.Length(text.AsSpan(index), out _);
                output.Append(shell == "bash" ? "\x01" : "%{");
                output.Append(text.AsSpan(index, length));
                output.Append(shell == "bash" ? "\x02" : "%}");
                index += length;
            }
            else {
                char character = text[index++];
                if (shell == "zsh" && character == '%') {
                    output.Append('%');
                }

                output.Append(character);
            }
        }

        return output.ToString();
    }
}
