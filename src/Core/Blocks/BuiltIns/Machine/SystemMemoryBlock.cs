using System.Globalization;

using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Machine;

internal sealed class SystemMemoryBlock : IRefreshingBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.SystemMemory, StatusLineAlignment.Right, ("🧠", "🧠", "RAM"));

    public TimeSpan Interval => TimeSpan.FromSeconds(1);
    public CollectionScope Scope(BlockContext context) => new(string.Empty, string.Empty);
    public Task<CollectedBlock?> CollectAsync(BlockContext context, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        (ulong Used, ulong Total)? sample = SystemMemoryReader.Read();
        return Task.FromResult<CollectedBlock?>(new CollectedBlock(set => {
            string icon = Metadata.Icon(set);
            if (sample is not { } memory) {
                return null;
            }

            return (icon, string.Create(CultureInfo.InvariantCulture, $"{memory.Used / (1024.0 * 1024 * 1024):0.0}/{memory.Total / (1024.0 * 1024 * 1024):0.0} GiB ({100.0 * memory.Used / memory.Total:0}%)"), Metadata.Alignment, LoadColor(100.0 * memory.Used / memory.Total));
        }));
    }
}
