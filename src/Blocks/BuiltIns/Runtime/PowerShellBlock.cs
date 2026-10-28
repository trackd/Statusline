
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Runtime;

internal sealed class PowerShellBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.PowerShell, StatusLineAlignment.Center, ("\U000F0A0A", "❯", "PS"));

    public BlockContent? Evaluate(BlockContext context) {
        IconSet set = context.IconSet;
        string icon = Metadata.Icon(set);
        if (context.PowerShellVersion is not { } version) {
            return null;
        }

        return (icon, version, Metadata.Alignment, Cyan);
    }
}
