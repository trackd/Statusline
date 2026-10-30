namespace StatusLine.Utility;

internal static class ProjectReader {
    internal static bool HasProject(string path, string[] markers, string[] patterns) =>
        FindProject(path, markers, patterns) is not null;

    internal static string? FindProject(string path, string[] markers, string[] patterns) {
        if (path.Length == 0) {
            return null;
        }

        DirectoryInfo? directory = new(path);
        for (int depth = 0; directory is not null && depth < 8; depth++, directory = directory.Parent) {
            foreach (string marker in markers) {
                if (File.Exists(Path.Combine(directory.FullName, marker))) {
                    return directory.FullName;
                }
            }

            foreach (string pattern in patterns) {
                if (Directory.EnumerateFiles(directory.FullName, pattern).Any()) {
                    return directory.FullName;
                }
            }

            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git"))) {
                break;
            }
        }

        return null;
    }

    internal static async Task<string?> RunAsync(string program, string[] arguments, string path, CancellationToken token) {
        (int ExitCode, string Output, string Error) = await ProcessRunner.RunAsync(program, arguments, path, token).ConfigureAwait(false);
        if (ExitCode != 0) {
            throw new IOException($"{program} exited with code {ExitCode}.");
        }

        ReadOnlyMemory<char> raw = (Output.Length == 0 ? Error : Output).AsMemory();
        int line = raw.Span.IndexOfAny('\r', '\n');
        string text = TerminalText.Sanitize((line >= 0 ? raw[..line] : raw).ToString().Trim(), keepStyles: false);
        return TerminalText.Clip(text, 80).Sequence;
    }
}
