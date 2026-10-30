
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Context;

internal sealed class AzureBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Azure, StatusLineAlignment.Left, ("\U000F0805", "AZ", "AZ"));

    public BlockContent? Evaluate(BlockContext context) {
        IconSet set = context.IconSet;
        string icon = Metadata.Icon(set);
        if (Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID") is not { Length: > 0 } azure) {
            return null;
        }

        return (icon, azure, Metadata.Alignment, Blue);
    }
}
