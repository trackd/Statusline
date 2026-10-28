
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Machine;

internal sealed class ElevationBlock : IRefreshingBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Elevation, StatusLineAlignment.Left, ("\uF023", "!", "!"));

    public TimeSpan Interval => TimeSpan.FromSeconds(300);
    public CollectionScope Scope(BlockContext context) => new(string.Empty, string.Empty);
    public Task<CollectedBlock?> CollectAsync(BlockContext context, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        bool elevated = Privilege.IsElevated;
        return Task.FromResult<CollectedBlock?>(new CollectedBlock(set => {
            string icon = Metadata.Icon(set);
            return elevated ? (icon, "", Metadata.Alignment, Red) : null;
        }));
    }
}
