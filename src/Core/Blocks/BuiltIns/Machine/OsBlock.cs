
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Machine;

internal sealed class OsBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Os, StatusLineAlignment.Left, ("\uF108", "OS", "OS"));

    public BlockContent? Evaluate(BlockContext context) {
        IconSet set = context.IconSet;
        string icon = Metadata.Icon(set);
        string name = OperatingSystemIdentity.Current.DisplayName;
        return (icon, name, Metadata.Alignment, Gray);
    }
}

