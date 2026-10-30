
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Machine;

internal sealed partial class WeatherBlock : IRefreshingBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Weather, StatusLineAlignment.Right, ("", "", ""));

    public TimeSpan Interval => TimeSpan.FromSeconds(900);
    public CollectionScope Scope(BlockContext context) => new(context.WeatherLocation?.Trim() ?? string.Empty, string.Empty);
    public async Task<CollectedBlock?> CollectAsync(BlockContext context, CancellationToken token) {
        string? text = await ReadAsync(context.WeatherLocation, token).ConfigureAwait(false);
        if (string.IsNullOrEmpty(text)) {
            return null;
        }

        return new CollectedBlock(set => (Metadata.Icon(set), text, Metadata.Alignment, Cyan));
    }
}
