using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Machine;

internal sealed class UserBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.User, StatusLineAlignment.Right, ("\uF007", "👤", ""));

    public BlockContent? Evaluate(BlockContext context) => (Metadata.Icon(context.IconSet), UserIdentity.CurrentName, Metadata.Alignment, Yellow);
}
