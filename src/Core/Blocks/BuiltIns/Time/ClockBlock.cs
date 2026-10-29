using System.Globalization;

using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Time;

internal sealed class ClockBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Clock, StatusLineAlignment.Right, ("\uF017", "◷", ""));

    public BlockContent? Evaluate(BlockContext context) {
        IconSet set = context.IconSet;
        string icon = Metadata.Icon(set);
        return (icon, context.Timestamp.LocalDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture), Metadata.Alignment, Gray);
    }
}
