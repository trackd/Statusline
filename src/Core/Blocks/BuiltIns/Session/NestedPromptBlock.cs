
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Session;

internal sealed class NestedPromptBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.NestedPrompt, StatusLineAlignment.Left, ("\uf003d", "↪", "NEST"), SessionFacts.NestedPrompt);

    public BlockContent? Evaluate(BlockContext context) {
        IconSet set = context.IconSet;
        string icon = Metadata.Icon(set);
        StatusLineState state = context.State;
        return CountBlock(icon, state.NestedPromptLevel, Purple);
    }
}
