using System.Globalization;

using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Machine;

internal sealed class CpuBlock : IRefreshingBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Cpu, StatusLineAlignment.Right, ("\uF4BC", "⚙", "CPU"));

    public TimeSpan Interval => TimeSpan.FromSeconds(1);
    public CollectionScope Scope(BlockContext context) => new(string.Empty, string.Empty);
    public Task<CollectedBlock?> CollectAsync(BlockContext context, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        double? sample = CpuSampler.Sample();
        return Task.FromResult<CollectedBlock?>(new CollectedBlock(set => {
            string icon = Metadata.Icon(set);
            if (sample is not { } cpu) {
                return null;
            }

            return (icon, string.Create(CultureInfo.InvariantCulture, $"{cpu,3:0}%"), Metadata.Alignment, LoadColor(cpu));
        }));
    }
}
