using System.Globalization;
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Session;

internal sealed class JobsBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Jobs, StatusLineAlignment.Right, ("\uF0AE", "⚙", "jobs"), Facts: SessionFacts.Jobs);
    public BlockContent? Evaluate(BlockContext context) => context.State.RunningJobCount > 0
        ? (Metadata.Icon(context.IconSet), context.State.RunningJobCount.ToString(CultureInfo.InvariantCulture), Metadata.Alignment, Yellow)
        : null;
}

internal sealed class RunningCommandBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.RunningCommand, StatusLineAlignment.Left, ("\uF120", "▶", ""));
    public BlockContent? Evaluate(BlockContext context) => context.State.CommandStartedAt is { } started
        ? (Metadata.Icon(context.IconSet), (context.State.RunningCommand ?? "command") + " " +
            FormatDuration(context.Timestamp - started), Metadata.Alignment, Yellow)
        : null;
}

internal sealed class ViModeBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.ViMode, StatusLineAlignment.Left, ("", "", ""));
    public BlockContent? Evaluate(BlockContext context) => context.State.ViMode is { } mode
        ? (string.Empty, mode, Metadata.Alignment, Cyan) : null;
}
