using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Runtime;

internal sealed class ShellBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Shell, StatusLineAlignment.Left, ("\uF489", "❯", "SH"));

    public BlockContent? Evaluate(BlockContext context) {
        string? identity = context.ShellIdentity;
        if (string.IsNullOrWhiteSpace(identity) && !string.IsNullOrWhiteSpace(context.PowerShellVersion)) {
            identity = "pwsh " + context.PowerShellVersion;
        }
        if (string.IsNullOrWhiteSpace(identity)) { return null; }
        return (Metadata.Icon(context.IconSet), TerminalText.Sanitize(identity, keepStyles: false), Metadata.Alignment, Cyan);
    }
}
