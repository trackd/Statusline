using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Machine;

internal sealed class UserHostBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.UserHost, StatusLineAlignment.Right, ("\uF007", "👤", ""));

    public BlockContent? Evaluate(BlockContext context) =>
        (Metadata.Icon(context.IconSet), UserIdentity.CurrentName + "@" + Environment.MachineName, Metadata.Alignment, Yellow);
}
