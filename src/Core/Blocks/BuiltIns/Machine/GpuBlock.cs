using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Machine;

internal sealed class GpuBlock : IRefreshingBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Gpu, StatusLineAlignment.Right, ("GPU", "GPU", "GPU"));
    public TimeSpan Interval => TimeSpan.FromSeconds(1);
    public CollectionScope Scope(BlockContext context) => new(string.Empty, string.Empty);

    public Task<CollectedBlock?> CollectAsync(BlockContext context, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        GpuReading[] readings = GpuSampler.Shared.Sample();
        CollectedBlock? result = null;
        if (readings.Length > 0) {
            string text = GpuSampler.Format(readings);
            double busiest = readings.Max(reading => reading.Utilization);
            result = new CollectedBlock(set => (Metadata.Icon(set), text, Metadata.Alignment, LoadColor(busiest)));
        }

        return Task.FromResult(result);
    }
}
