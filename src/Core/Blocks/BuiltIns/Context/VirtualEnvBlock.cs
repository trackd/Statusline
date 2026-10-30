
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Context;

internal sealed class VirtualEnvBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.VirtualEnv, StatusLineAlignment.Left, ("\uE73C", "venv", "venv"));

    public BlockContent? Evaluate(BlockContext context) {
        IconSet set = context.IconSet;
        string icon = Metadata.Icon(set);
        if ((Environment.GetEnvironmentVariable("VIRTUAL_ENV") ?? Environment.GetEnvironmentVariable("CONDA_DEFAULT_ENV")) is not { Length: > 0 } virtualEnv) {
            return null;
        }

        return (icon, Path.GetFileName(virtualEnv.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), Metadata.Alignment, Yellow);
    }
}
