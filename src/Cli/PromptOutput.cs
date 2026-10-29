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
        double? durationMilliseconds, bool statusLine, bool wait = true) {
        Validate(options);
        if (!statusLine && options.Rows == 1 && (options.Right.Length != 0 || options.Center.Length != 0)) {
            throw new ArgumentException("Right and Center prompt groups require Rows = 2.");
        }
        DirectoryDisplay.Style = options.DirectoryStyle;
        var context = new BlockContext {
            State = new StatusLineState {
                Location = path, FileSystemPath = path, LastExitCode = status,
                LastDuration = durationMilliseconds is { } milliseconds ? TimeSpan.FromMilliseconds(milliseconds) : null,
            },
            IconSet = options.IconSet,
            ShellIdentity = Environment.GetEnvironmentVariable("STATUSLINE_SHELL"),
            BlockTimeoutMilliseconds = options.BlockTimeoutMilliseconds,
            SlowProviderWait = wait ? TimeSpan.FromMilliseconds(options.BlockTimeoutMilliseconds) : TimeSpan.Zero,
            WeatherLocation = options.WeatherLocation,
        };
        var blocks = new List<StatusLineBlock>();
        Append(options.Left, StatusLineAlignment.Left);
        Append(options.Center, StatusLineAlignment.Center);
        Append(options.Right, StatusLineAlignment.Right);
        Color start = options.StartColor is { } first ? NamedColors.Parse(first) : Color.Empty;
        Color end = options.EndColor is { } last ? NamedColors.Parse(last) : Color.Empty;
        if (statusLine) {
            return StatusLineRenderer.RenderBlocks(blocks, width, options.BlockSeparator, options.Style,
                useCursorPositioning: false, blendBackground: options.BlendBackground,
                textContrast: options.TextContrast, roundOuterEdges: true, startColor: start, endColor: end);
        }
        string symbol = TerminalText.Clip(TerminalText.Sanitize(options.Symbol, keepStyles: false), width - 1).Sequence;
        if (options.Rows == 2) {
            return StatusLineRenderer.RenderBlocks(blocks, width - 1, options.BlockSeparator, options.Style,
                useCursorPositioning: false, blendBackground: options.BlendBackground,
                textContrast: options.TextContrast, roundOuterEdges: true, startColor: start, endColor: end) + Vt.Reset + "\n" + symbol;
        }
        int budget = Math.Max(0, width - 1 - TerminalText.Measure(symbol) - Math.Min(20, (width - 1) / 2));
        if (options.BlendBackground) {
            return StatusLineRenderer.RenderBlocks(blocks, budget, options.BlockSeparator, options.Style,
                useCursorPositioning: false, blendBackground: true, textContrast: options.TextContrast,
                roundOuterEdges: true, startColor: start, endColor: end) + Vt.Reset + symbol;
        }
        IReadOnlyList<StatusLineBlock> colored = BlockGradient.Apply(blocks, start, end, options.Style);
        return StatusLineRenderer.RenderGroup(colored, StatusLineAlignment.Left, budget,
            options.BlockSeparator, options.Style, textContrast: options.TextContrast, roundOuterEdges: true).Sequence + Vt.Reset + symbol;

        void Append(IEnumerable<string> names, StatusLineAlignment alignment) {
            foreach (string name in names) {
                StatusLineBlockKind kind = Kind(name);
                BlockContent? content = BuiltInRefresh.Evaluate(BlockCatalog.Get(kind), context, out string? error);
                if (error is not null && wait) { Console.Error.WriteLine($"statusline: {name}: {error}"); }
                if (content is { } value) {
                    var block = new StatusLineBlock(value.Text, value.Accent, Color.Empty, alignment,
                        options.Padding, icon: value.Icon) { Shrinkable = kind == StatusLineBlockKind.Directory };
                    blocks.Add(error is null ? block : block.AsStale(error));
                }
            }
        }
    }

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

    private static StatusLineBlockKind Kind(string name) {
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
