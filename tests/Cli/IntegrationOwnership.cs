namespace StatusLine.Cli.Tests;

internal static partial class Program {
    private static void IntegrationOwnership() {
        using var fixture = new Fixture();
        string? previous = Environment.GetEnvironmentVariable("TERM_PROGRAM");
        try {
            foreach (string terminal in new[] { "contour", "ghostty" }) {
                Environment.SetEnvironmentVariable("TERM_PROGRAM", terminal);
                fixture.Configure(new { Left = new[] { "Status" }, IconSet = "None", Style = "Plain" });
                string session = Start(fixture);
                string RenderOwned(string flags) => Success("prompt", "--session", session, "--cwd", fixture.Path, "--integration-owner", flags);
                string ExecuteOwned(string flags) => Success("command-start", "--session", session, "--integration-owner", flags, "--command", "echo test");
                _ = ExecuteOwned("63");
                string output = RenderOwned("63");
                Check(!output.Contains("\x1b]133;", StringComparison.Ordinal) && !output.Contains("\x1b]7;", StringComparison.Ordinal) &&
                    !output.Contains("2028", StringComparison.Ordinal) && Plain(output).Contains("OK", StringComparison.Ordinal), output);
                Check(ExecuteOwned("63").Length == 0, "Externally owned command marker was duplicated.");
                output = RenderOwned("29");
                Check(!output.Contains("\x1b]133;A", StringComparison.Ordinal) && output.Contains("\x1b]133;B", StringComparison.Ordinal) &&
                    !output.Contains("\x1b]133;D", StringComparison.Ordinal), "Missing-marker supplement was incorrect: " + output);
                if (terminal == "contour") {
                    Check(output.Contains("\x1b[?2028l", StringComparison.Ordinal), "Native Fish markers disabled Contour prompt reflow control.");
                    Check(ExecuteOwned("31") == "\x1b[?2028h", "Native Fish markers disabled Contour command reflow control.");
                }
                output = RenderOwned("0");
                Check(output.Contains("\x1b]133;A", StringComparison.Ordinal) && output.Contains("\x1b]133;B", StringComparison.Ordinal), "Standalone Auto lost its markers.");
                foreach (string flags in new[] { "-1", "64" }) {
                    var invalid = Cli("prompt", "--integration-owner", flags);
                    Check(invalid.Code == 2 && invalid.Out.Length == 0, "Invalid ownership mask accepted.");
                }
                fixture.Configure(new { Left = new[] { "Status" }, IconSet = "None", Style = "Plain", ShellIntegration = "Osc133" });
                session = Start(fixture);
                output = RenderOwned("63");
                Check(output.Contains("\x1b]133;A", StringComparison.Ordinal) && output.Contains("\x1b]133;B", StringComparison.Ordinal), "Explicit protocol did not override Auto ownership.");
            }
        }
        finally { Environment.SetEnvironmentVariable("TERM_PROGRAM", previous); }
    }

    private static void FishOwnershipVersions() {
        using var fixture = new Fixture();
        fixture.Configure(new { Left = new[] { "Status" }, IconSet = "None", Style = "Plain", Symbol = " __STATUSLINE_READY__ " });
        using var terminal = new LinuxTerminal("fish", fixture.Path, "ghostty");
        _ = terminal.Command(Quote(binary) + " init fish --config " + Quote(fixture.Config) + " | source");
        foreach ((string version, int state, int expected) in new[] {
            ("3.7.1", 2, 0), ("4.0.0", 2, 29), ("4.1.0", 0, 29), ("4.2.0", 0, 29),
            ("4.3.0", 0, 31), ("4.9.3", 0, 31), ("4.3.0", 1, 16), ("5.0.0", 0, 31),
        }) {
            string output = terminal.Command($"_statusline_native_ownership {version} {state}; printf 'FLAGS:%s\\n' $_statusline_external");
            Check(output.Contains("FLAGS:" + expected, StringComparison.Ordinal), $"Fish {version}/{state}: {output}");
        }
    }

    private static void GhosttyRedraw() {
        foreach (string shell in new[] { "bash", "zsh" }) {
            using var fixture = new Fixture();
            fixture.Configure(new { Left = new[] { "Status" }, Rows = 2, Style = "Plain", IconSet = "None", Symbol = " __STATUSLINE_READY__ " });
            using var screen = new LinuxTerminal(shell, fixture.Path, "ghostty");
            string initial = screen.Command("eval \"$(" + Quote(binary) + " init " + shell + " --config " + Quote(fixture.Config) + ")\"");
            Check(initial.Contains("\x1b]133;A", StringComparison.Ordinal), shell + " lost its fresh-prompt marker.");
            _ = screen.ReadFor(60);
            screen.Send("\x0c");
            string redraw = screen.ReadFor(200);
            Check(!redraw.Contains("\x1b]133;A", StringComparison.Ordinal) && redraw.Contains("\x1b]133;P;k=i", StringComparison.Ordinal) &&
                redraw.Contains("\x1b]133;P;k=s", StringComparison.Ordinal), shell + " redraw issued a fresh line or lost multiline grouping: " + redraw);
            Check(Plain(screen.Command("false")).Contains("ERR 1", StringComparison.Ordinal), shell + " lost status after redraw.");
        }
    }
}
