using System.Globalization;
using System.Text;
namespace StatusLine;

/// <summary>Directory reporting is independent of OSC 133 command-boundary support.</summary>
internal static class WorkingDirectoryMarkers {
    public static string Create(ShellIntegrationProfile profile, string path) {
        return profile.WorkingDirectory switch {
            "OSC 633" => Vt.VsCodeDirectory(EscapeProperty(path)),
            "OSC 1337" => Vt.ITermDirectory(TerminalText.Sanitize(path, keepStyles: false)),
            "OSC 9;9" => Vt.WindowsTerminalDirectory(TerminalText.Sanitize(path, keepStyles: false)),
            "OSC 7" => Vt.FileUrlDirectory(FileUrl(path)),
            _ => string.Empty,
        };
    }

    private static string FileUrl(string path) {
        var uri = new Uri(Path.GetFullPath(path));
        return uri.IsUnc ? uri.AbsoluteUri : new UriBuilder(uri) { Host = System.Net.Dns.GetHostName() }.Uri.AbsoluteUri;
    }

    internal static string EscapeProperty(ReadOnlySpan<char> value) {
        StringBuilder builder = StringBuilderCache.Rent(value.Length);
        foreach (char character in value) {
            if (character == '\\' || character == ';' || character <= ' ' || char.IsControl(character)) {
                builder.Append("\\x").Append(((int)character).ToString("x2", CultureInfo.InvariantCulture));
            }
            else {
                builder.Append(character);
            }
        }

        return StringBuilderCache.Complete(builder);
    }
}
