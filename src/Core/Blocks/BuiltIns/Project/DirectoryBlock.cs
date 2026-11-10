
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

        if (context.DirectorySeparator is { Length: > 0 } separator) {
            return new BlockContent(icon, SeparateComponents(text, separator), Metadata.Alignment, Blue, separator);
        }

        return (icon, text, Metadata.Alignment, Blue);
    }

    private static string SeparateComponents(string text, string separator) {
        var output = new System.Text.StringBuilder(text.Length);
        for (int index = 0; index < text.Length; index++) {
            char character = text[index];
            if (index > 0 && index + 1 < text.Length && IsSeparator(character) &&
                !IsSeparator(text[index - 1]) && !IsSeparator(text[index + 1])) {
                output.Append(separator);
            }
            else {
                output.Append(character);
            }
        }
        return output.ToString();
    }

    private static bool IsSeparator(char character) =>
        character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar;

    private static string Home(IconSet set) => Pick(("\uE617", "⌂", ""), set);
}
