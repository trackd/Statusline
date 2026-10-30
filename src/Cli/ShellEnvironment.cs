using System.Diagnostics;
using System.Globalization;

namespace StatusLine.Cli;

internal static class ShellEnvironment {
    internal static string Home {
        get {
            string? home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrWhiteSpace(home) || !Path.IsPathFullyQualified(home)) {
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            if (string.IsNullOrEmpty(home) || !Path.IsPathFullyQualified(home)) {
                throw new IOException("User home directory is unavailable; supply an absolute path.");
            }

            return home;
        }
    }

    internal static string FullPath(string path) {
        if (path == "~") { return Home; }

        if (path.StartsWith("~/", StringComparison.Ordinal) || (OperatingSystem.IsWindows() && path.StartsWith("~\\", StringComparison.Ordinal))) {
            path = Path.Combine(Home, path[2..]);
        }

        return Path.GetFullPath(path);
    }

    internal static string DetectShell() {
        // SHELL is the login shell, not necessarily the shell evaluating init.
        // Look through command substitutions and sudo/su wrappers on Linux first.
        if (OperatingSystem.IsLinux()) {
            int processId = Environment.ProcessId;
            for (int depth = 0; depth < 16 && processId > 1; depth++) {
                try {
                    string stat = File.ReadAllText($"/proc/{processId}/stat");
                    int end = stat.LastIndexOf(')');
                    string name = stat[(stat.IndexOf('(') + 1)..end].TrimStart('-');
                    if (depth > 0 && name is "bash" or "zsh" or "fish") { return name; }

                    processId = int.Parse(stat[(end + 2)..].Split(' ', 3)[1], CultureInfo.InvariantCulture);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException) { break; }
            }
        }

        string? loginShell = Path.GetFileName(Environment.GetEnvironmentVariable("SHELL"));
        if (loginShell is "bash" or "zsh" or "fish") { return loginShell; }

        throw new ArgumentException("Cannot identify the calling shell. Specify init bash, init zsh, or init fish.");
    }
}
