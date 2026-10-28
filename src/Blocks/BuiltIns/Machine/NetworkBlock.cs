
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Machine;

internal sealed class NetworkBlock : IRefreshingBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Network, StatusLineAlignment.Right, ("\uF0EC", "⇅", "NET"));

    public TimeSpan Interval => TimeSpan.FromSeconds(1);
    public CollectionScope Scope(BlockContext context) => new(string.Empty, string.Empty);
    public Task<CollectedBlock?> CollectAsync(BlockContext context, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        (double ReceivedPerSecond, double SentPerSecond)? sample = NetworkSampler.Sample();
        return Task.FromResult<CollectedBlock?>(new CollectedBlock(set => {
            string icon = Metadata.Icon(set);
            if (sample is not { } rate) {
                return null;
            }

            return (icon, FormatNetwork(rate, set), Metadata.Alignment, Cyan);
        }));
    }
    private static string FormatNetwork((double ReceivedPerSecond, double SentPerSecond) rate, IconSet set) {
        (string? down, string? up) = Markers(set);
        return $"{down}{NetworkSampler.FormatRate(rate.ReceivedPerSecond)} {up}{NetworkSampler.FormatRate(rate.SentPerSecond)}";
    }

    private static (string Down, string Up) Markers(IconSet set) =>
        set == IconSet.None ? ("rx ", "tx ") : ("⇣", "⇡");
}
