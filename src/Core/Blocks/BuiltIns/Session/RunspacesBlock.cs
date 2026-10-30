
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Session;

internal sealed class RunspacesBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Runspaces, StatusLineAlignment.Left, ("\ue683", "⚙", "RS"), SessionFacts.Runspaces);

    public BlockContent? Evaluate(BlockContext context) {
        IconSet set = context.IconSet;
        string icon = Metadata.Icon(set);
        StatusLineState state = context.State;
        return CountBlock(icon, state.RunspaceCount, Cyan);
    }
}
