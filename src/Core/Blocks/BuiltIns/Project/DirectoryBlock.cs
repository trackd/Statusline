
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Project;

internal sealed class DirectoryBlock : IImmediateBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Directory, StatusLineAlignment.Left, ("\uF07C", "📂", ""), Facts: SessionFacts.Location);

    public BlockContent? Evaluate(BlockContext context) {
        string icon = Metadata.Icon(context.IconSet);
        if (context.State.Location is not { } location) {
            return null;
        }

        if (context.State.FileSystemPath is not { } path) {
            return (icon, location, Metadata.Alignment, Blue);
        }

        string text = DirectoryDisplay.Format(path);
        if (DirectoryDisplay.Style == DirectoryDisplayStyle.Relative && DirectoryDisplay.BasePath is null) {
            string relative = Path.GetRelativePath(DirectoryDisplay.StartupDirectory, path);
            if (relative == ".") {
                return context.IconSet == IconSet.None
                    ? (string.Empty, "~", Metadata.Alignment, Blue)
                    : (Home(context.IconSet), string.Empty, Metadata.Alignment, Blue);
            }
        }

        return (icon, text, Metadata.Alignment, Blue);
    }

    private static string Home(IconSet set) => Pick(("\uE617", "⌂", ""), set);
}
