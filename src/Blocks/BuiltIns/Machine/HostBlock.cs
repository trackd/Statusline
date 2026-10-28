using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Machine;

internal sealed class HostBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Host, StatusLineAlignment.Right, ("\uF108", "💻", ""));

    public BlockContent? Evaluate(BlockContext context) => (Metadata.Icon(context.IconSet), Environment.MachineName, Metadata.Alignment, Gray);
}
