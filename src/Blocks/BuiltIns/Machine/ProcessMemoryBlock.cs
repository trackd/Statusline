using System.Globalization;

using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Machine;

internal sealed class ProcessMemoryBlock : IRefreshingBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.ProcessMemory, StatusLineAlignment.Right, ("🧠", "🧠", "PROC"));

    public TimeSpan Interval => TimeSpan.FromSeconds(1);
    public CollectionScope Scope(BlockContext context) => new(string.Empty, string.Empty);
    public Task<CollectedBlock?> CollectAsync(BlockContext context, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        long workingSet = Environment.WorkingSet;
        return Task.FromResult<CollectedBlock?>(new CollectedBlock(set => {
            string icon = Metadata.Icon(set);
            return (icon, string.Create(CultureInfo.InvariantCulture, $"{workingSet / (1024.0 * 1024.0):0} MiB"), Metadata.Alignment, Purple);
        }));
    }
}
