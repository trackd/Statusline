
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Session;

internal sealed class ErrorsBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Errors, StatusLineAlignment.Left, ("\uF071", "⚠", "ERR"), SessionFacts.Errors);

    public BlockContent? Evaluate(BlockContext context) {
        IconSet set = context.IconSet;
        string icon = Metadata.Icon(set);
        StatusLineState state = context.State;
        return CountBlock(icon, state.ErrorCount, Red);
    }
}
