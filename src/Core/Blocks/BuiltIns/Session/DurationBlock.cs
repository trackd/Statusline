
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Session;

internal sealed class DurationBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Duration, StatusLineAlignment.Right, ("\u23F1", "⧗", "took"), OrdinaryLeadingSpace: true);

    public BlockContent? Evaluate(BlockContext context) {
        string icon = Metadata.Icon(context.IconSet);
        if (context.State.LastDuration is not { } duration) {
            return null;
        }

        return (icon, FormatDuration(duration), Metadata.Alignment, Yellow);
    }
}
