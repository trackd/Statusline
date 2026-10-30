namespace StatusLine.Cli.Tests;

internal static partial class Program {
    private static void Terminfo() {
        using var fixture = new Fixture();
        fixture.Configure(new { Left = new[] { "Status" }, Style = "Plain", IconSet = "None", Symbol = " __STATUSLINE_READY__ ", Target = "HostWritable" });
        string source = Path.Combine(fixture.Path, "terminfo.src");
        string database = Path.Combine(fixture.Path, "terminfo");
        File.WriteAllText(source, """
            statusline-test-dec|StatusLine DEC status fixture,
                eslok, hs, colors#256, cols#120, lines#30,
                tsl=\E[2$~\E[1$}\E[%i%p1%d`, fsl=\E[0$}, dsl=\E[0$~,
            statusline-test-title|StatusLine unrelated status fixture,
                hs, colors#256, cols#120, lines#30,
                tsl=\E]0;, fsl=^G, dsl=\E]0;^G,
            """);
        var compiled = Run("tic", fixture.Path, "-x", "-o", database, source);
        Check(compiled.Code == 0, compiled.Error);
        string? previous = Environment.GetEnvironmentVariable("TERMINFO");
        Environment.SetEnvironmentVariable("TERMINFO", database);
        try {
            using (var terminal = new LinuxTerminal("bash", fixture.Path, term: "statusline-test-dec")) {
                string output = terminal.Command("eval \"$(" + Quote(binary) + " init bash --config " + Quote(fixture.Config) + ")\"") + terminal.ReadFor(700);
                Check(StatusFrames(output).Length > 0, "DEC terminfo entry did not enable status display: " + output);
                output = terminal.Command("printf '\\e[0$~'") + terminal.ReadFor(700);
                Check(output.Contains("\x1b[2$~", StringComparison.Ordinal), "Status display was not restored after a command changed its mode.");
            }
            foreach ((string program, string term) in new[] { ("", "statusline-test-title"), ("ghostty", "statusline-test-dec") }) {
                using var terminal = new LinuxTerminal("bash", fixture.Path, program, term);
                terminal.Send(Quote(binary) + " start --parent-pid $$ --config " + Quote(fixture.Config) + "; printf 'REJECT:%s\\n' $?\n");
                string output = terminal.ReadFor(3000);
                Check(output.Contains("REJECT:2", StringComparison.Ordinal) && StatusFrames(output).Length == 0, output);
            }
        }
        finally { Environment.SetEnvironmentVariable("TERMINFO", previous); }
    }
}
