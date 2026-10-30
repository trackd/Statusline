
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Session;

internal sealed class StackCountBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.StackCount, StatusLineAlignment.Left, ("\uf115", "↥", "STACK"), SessionFacts.Stack);

    public BlockContent? Evaluate(BlockContext context) {
        IconSet set = context.IconSet;
        string icon = Metadata.Icon(set);
        StatusLineState state = context.State;
        return CountBlock(icon, state.StackCount, Yellow);
    }
}
