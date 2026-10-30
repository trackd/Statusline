using System.Globalization;

using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Machine;

internal sealed class BatteryBlock : IRefreshingBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Battery, StatusLineAlignment.Right, ("", "", ""));

    public TimeSpan Interval => TimeSpan.FromSeconds(1);
    public CollectionScope Scope(BlockContext context) => new(string.Empty, string.Empty);
    public Task<CollectedBlock?> CollectAsync(BlockContext context, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        (int Percent, bool Charging)? sample = BatteryReader.Read();
        return Task.FromResult<CollectedBlock?>(new CollectedBlock(set => {
            string icon = Metadata.Icon(set);
            if (sample is not { } battery) {
                return null;
            }

            return (BatteryIcon(set, battery.Percent, battery.Charging), string.Create(CultureInfo.InvariantCulture, $"{battery.Percent}%"), Metadata.Alignment, LoadColor(100 - battery.Percent));
        }));
    }

    private static readonly string[] NerdBattery = [
        "\uF244",
        "\uF243",
        "\uF242",
        "\uF241",
        "\uF240"
    ];

    private static string BatteryIcon(IconSet set, int percent, bool charging) =>
        set switch {
            IconSet.NerdFont => charging ? "\uF0E7" : NerdBattery[Math.Clamp(percent, 0, 100) * (NerdBattery.Length - 1) / 100],
            IconSet.Unicode => charging ? "⚡" : "🔋",
            _ => "BAT",
        };
}
