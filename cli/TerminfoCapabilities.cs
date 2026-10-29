using System.Diagnostics;

namespace StatusLine.Cli;

internal static class TerminfoCapabilities {
    internal static bool HasDecStatusLine() {
        if (!OperatingSystem.IsLinux()) { return false; }
        string? term = Environment.GetEnvironmentVariable("TERM");
        if (string.IsNullOrEmpty(term) || term[0] == '-' || term.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '+' or '_' or '.'))) {
            return false;
        }
        try {
            // One startup probe, never a prompt-time subprocess. Match DEC's
            // actual controls: hs alone also describes xterm's window title.
            var result = Process.RunAndCaptureText("infocmp", ["-1", term], timeout: TimeSpan.FromSeconds(1));
            if (result.ExitStatus.ExitCode != 0) { return false; }
            string[] fields = result.StandardOutput.Split('\n').Select(line => line.Trim().TrimEnd(',')).ToArray();
            return fields.Contains("hs", StringComparer.Ordinal) && fields.Contains("eslok", StringComparer.Ordinal) &&
                fields.Contains(@"fsl=\E[0$}", StringComparer.Ordinal) &&
                fields.Any(field => field.StartsWith(@"tsl=\E[2$~\E[1$}", StringComparison.Ordinal));
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or TimeoutException) {
            return false;
        }
    }
}
