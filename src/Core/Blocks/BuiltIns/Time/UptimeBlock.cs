
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Time;

internal sealed class UptimeBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Uptime, StatusLineAlignment.Right, ("\uF011", "⏻", "up"));

    public BlockContent? Evaluate(BlockContext context) {
        IconSet set = context.IconSet;
        string icon = Metadata.Icon(set);
        return (icon, FormatUptime(TimeSpan.FromMilliseconds(Environment.TickCount64)), Metadata.Alignment, Gray);
    }
}
