using System.Globalization;

using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Time;

internal sealed class DateBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Date, StatusLineAlignment.Right, ("\uF073", "📅", ""));

    public BlockContent? Evaluate(BlockContext context) {
        IconSet set = context.IconSet;
        string icon = Metadata.Icon(set);
        return (icon, context.Timestamp.LocalDateTime.ToString("ddd dd MMM", CultureInfo.CurrentCulture), Metadata.Alignment, Gray);
    }
}
