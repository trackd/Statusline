
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Session;

internal sealed class DurationBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Duration, StatusLineAlignment.Right, ("\u23F1", "⧗", "took"), OrdinaryLeadingSpace: true, Facts: SessionFacts.ExitCode);

    public BlockContent? Evaluate(BlockContext context) {
        string icon = Metadata.Icon(context.IconSet);
        if (context.State.LastDuration is not { } duration) {
            return null;
        }

        string text = FormatDuration(duration);
        if (context.DurationWarning && context.State.LastExitCode is { } code && code != 0) {
            text += " " + Vt.RedForeground + "⚠" + Vt.DefaultForeground;
        }

        return (icon, text, Metadata.Alignment, Yellow);
    }
}
