using System.Text;

namespace StatusLine.Cli;

internal static class ProfileHooks {
    private const string Begin = "# >>> StatusLine >>>";
    private const string End = "# <<< StatusLine <<<";

    internal static string DefaultPath(string shell) {
        string home = ShellEnvironment.Home;
        return shell switch {
            "bash" => Path.Combine(home, ".bashrc"),
            "zsh" => Path.Combine(Environment.GetEnvironmentVariable("ZDOTDIR") ?? home, ".zshrc"),
            "fish" => Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config"), "fish", "conf.d", "statusline.fish"),
            _ => throw new ArgumentException("Unsupported shell."),
        };
    }

    internal static bool Update(string shell, string? config, string? statusFile, string profile, bool install) {
        string target = Path.GetFullPath(profile);
        var info = new FileInfo(target);
        if (info.LinkTarget is not null) {
            target = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                ?? throw new IOException("Profile link has no target.");
            info = new FileInfo(target);
            if (!info.Exists) { throw new IOException("Profile link has no existing target."); }
        }
        string original = string.Empty;
        Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        if (info.Exists) {
            using var reader = new StreamReader(target, encoding, detectEncodingFromByteOrderMarks: true);
            original = reader.ReadToEnd();
            encoding = reader.CurrentEncoding;
        }
        string newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        int begin = Marker(original, Begin);
        int end = Marker(original, End);
        if ((begin < 0) != (end < 0) || (end >= 0 && end < begin)) {
            throw new IOException("Incomplete StatusLine hook markers; profile was not changed.");
        }
        string block = install ? Block(shell, config, statusFile, newline) : string.Empty;
        string changed;
        if (begin >= 0) {
            int after = end + End.Length;
            if (after < original.Length && original[after] == '\r') { after++; }
            if (after < original.Length && original[after] == '\n') { after++; }
            changed = string.Concat(original.AsSpan(0, begin), block, original.AsSpan(after));
        }
        else if (install) {
            changed = original + (original.Length > 0 && original[^1] != '\n' ? newline : string.Empty) + block;
        }
        else { return false; }
        if (changed == original) { return false; }
        if (info.Exists && info.IsReadOnly) { throw new IOException("Profile is read-only."); }
        string? directory = Path.GetDirectoryName(target);
        if (directory is null) { throw new IOException("Profile requires a parent directory."); }
        Directory.CreateDirectory(directory);
        if (install && info.Exists && !File.Exists(target + ".statusline.bak")) {
            File.Copy(target, target + ".statusline.bak");
        }
        string temporary = Path.Combine(directory, ".statusline-" + Guid.NewGuid().ToString("N"));
        UnixFileMode? originalMode = OperatingSystem.IsWindows() ? null : info.Exists
            ? File.GetUnixFileMode(target) : UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }
            using (var file = new FileStream(temporary, options)) {
                file.Write(encoding.GetPreamble());
                file.Write(encoding.GetBytes(changed));
                file.Flush(flushToDisk: true);
            }
            // Creation permissions are masked by umask; restore the exact original
            // mode after writing the private temporary file, before replacing it.
            if (!OperatingSystem.IsWindows() && originalMode is { } mode) { File.SetUnixFileMode(temporary, mode); }
            if (info.Exists) { File.Replace(temporary, target, destinationBackupFileName: null); }
            else { File.Move(temporary, target); }
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
        return true;
    }

    private static int Marker(string text, string marker) {
        int found = -1;
        int offset = 0;
        foreach (ReadOnlySpan<char> line in text.AsSpan().EnumerateLines()) {
            if (line.SequenceEqual(marker)) {
                if (found >= 0) { throw new IOException("Multiple StatusLine hook blocks; profile was not changed."); }
                found = offset;
            }
            offset += line.Length;
            if (offset < text.Length && text[offset] == '\r') { offset++; }
            if (offset < text.Length && text[offset] == '\n') { offset++; }
        }
        return found;
    }

    private static string Block(string shell, string? config, string? statusFile, string newline) {
        // Resolve through PATH at shell startup, rather than persisting a versioned
        // Homebrew Cellar or package-manager directory in the user's profile.
        string command = "command statusline init " + shell;
        if (config is not null) { command += " --config " + Quote(config, shell); }
        if (statusFile is not null) { command += " --status-file " + Quote(statusFile, shell); }
        string[] lines = shell == "fish"
            ? [Begin, "if status is-interactive", "    " + command + " | source", "end", End, ""]
            : [Begin, "if [[ $- == *i* ]]; then", "    eval \"$(" + command + ")\"", "fi", End, ""];
        return string.Join(newline, lines);
    }

    private static string Quote(string value, string shell) => "'" + (shell == "fish"
        ? value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)
        : value.Replace("'", "'\\''", StringComparison.Ordinal)) + "'";
}
