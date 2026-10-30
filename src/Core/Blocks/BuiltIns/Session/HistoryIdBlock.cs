using System.Globalization;

using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Session;

internal sealed class HistoryIdBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.HistoryId, StatusLineAlignment.Left, ("\U000F060D", "↳", ""));

    public BlockContent? Evaluate(BlockContext context) {
        IconSet set = context.IconSet;
        string icon = Metadata.Icon(set);
        StatusLineState state = context.State;
        if (state.HistoryId is not { } historyId) {
            return null;
        }

        return (icon, historyId.ToString(CultureInfo.InvariantCulture), Metadata.Alignment, Cyan);
    }
}
