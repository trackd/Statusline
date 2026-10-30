using System.Globalization;

using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Session;

internal sealed class StatusBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Status, StatusLineAlignment.Right, ("", "", ""), Facts: SessionFacts.ExitCode);

    public BlockContent? Evaluate(BlockContext context) {
        if (context.State.LastExitCode is not { } code) {
            return null;
        }

        if (code == 0) {
            return (Success(context.IconSet), context.IconSet == IconSet.None ? "OK" : string.Empty,
                Metadata.Alignment, Green);
        }

        return (Failure(context.IconSet), code.ToString(CultureInfo.InvariantCulture), Metadata.Alignment, Red);
    }

    private static string Success(IconSet set) =>
        Pick(("\uF00C", "✔", ""), set);

    private static string Failure(IconSet set) =>
        Pick(("\uF00D", "✘", "ERR"), set);
}
