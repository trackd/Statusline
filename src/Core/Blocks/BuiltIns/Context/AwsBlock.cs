
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Context;

internal sealed class AwsBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Aws, StatusLineAlignment.Left, ("\uE7AD", "AWS", "AWS"));

    public BlockContent? Evaluate(BlockContext context) {
        IconSet set = context.IconSet;
        string icon = Metadata.Icon(set);
        if ((Environment.GetEnvironmentVariable("AWS_PROFILE") ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_PROFILE")) is not { Length: > 0 } aws) {
            return null;
        }

        return (icon, aws, Metadata.Alignment, Yellow);
    }
}
