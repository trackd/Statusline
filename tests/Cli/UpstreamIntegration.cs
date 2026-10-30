namespace StatusLine.Cli.Tests;

internal static partial class Program {
    private static void UpstreamIntegration() {
        string root = Environment.GetEnvironmentVariable("STATUSLINE_UPSTREAM_INTEGRATION_ROOT")!;
        foreach (string terminalName in new[] { "contour", "ghostty" }) {
            foreach (string shell in new[] { "bash", "zsh", "fish" }) {
                foreach (bool upstreamFirst in new[] { true, false }) {
                    using var fixture = new Fixture();
                    fixture.Configure(new { Left = new[] { "Directory", "Status", "Duration" }, Style = "Plain", IconSet = "None", Rows = 2,
                        Symbol = " __STATUSLINE_READY__ ", DirectoryStyle = "Leaf" });
                    string upstream = Path.Combine(root, terminalName, terminalName == "contour" ? "shell-integration." + shell : shell switch {
                        "bash" => "bash--ghostty.bash", "zsh" => "zsh--ghostty-integration", _ => "fish--vendor_conf.d--ghostty-shell-integration.fish",
                    });
                    Check(File.Exists(upstream), upstream);
                    using var screen = new LinuxTerminal(shell, fixture.Path, terminalName);
                    if (terminalName == "ghostty") {
                        screen.Send("export GHOSTTY_SHELL_FEATURES=cursor\n");
                        _ = screen.ReadFor(60);
                    }
                    string init = Quote(binary) + " init " + shell + " --config " + Quote(fixture.Config);
                    init = shell == "fish" ? init + " | source" : "eval \"$(" + init + ")\"";
                    if (upstreamFirst) {
                        screen.Send("source " + Quote(upstream) + "\n");
                        _ = screen.ReadFor(200);
                        _ = screen.Command(init);
                    }
                    else {
                        _ = screen.Command(init);
                        _ = screen.Command("source " + Quote(upstream));
                    }
                    _ = screen.Command("true");
                    string output = screen.Command("false");
                    string label = $"{terminalName}/{shell}/{(upstreamFirst ? "upstream-first" : "statusline-first")}";
                    Check(Plain(output).Contains("ERR 1", StringComparison.Ordinal), label + " lost status: " + output);
                    Check(output.Contains("\x1b]133;B", StringComparison.Ordinal), label + " lost the input boundary: " + output);
                    if (shell == "bash" && terminalName == "ghostty") {
                        Check(output.Contains("\x1b[5 q", StringComparison.Ordinal), label + " lost Ghostty's cursor helper.");
                    }
                    if (shell != "fish") {
                        foreach (string marker in new[] { "A", "C", "D" }) {
                            int count = output.Split("\x1b]133;" + marker, StringSplitOptions.None).Length - 1;
                            Check(count == 1, label + " duplicated/lost " + marker + ": " + output);
                        }
                    }
                    // Ownership is observed in the active shell, including scripts loaded later.
                    string ownership = screen.Command("_statusline_ownership; printf 'OWNED:%s\\n' \"$_statusline_external\"");
                    string expected = shell == "fish" && terminalName == "ghostty" ? "31" : "63";
                    Check(ownership.Contains("OWNED:" + expected, StringComparison.Ordinal) ||
                        (shell == "bash" && terminalName == "ghostty" && ownership.Contains("OWNED:61", StringComparison.Ordinal)) ||
                        (shell == "fish" && terminalName == "ghostty" && ownership.Contains("OWNED:29", StringComparison.Ordinal)), label + ": " + ownership);
                    screen.Send("\x0c");
                    _ = screen.ReadFor(100);
                    Check(Plain(screen.Command("true")).Contains("OK", StringComparison.Ordinal), label + " failed after redraw.");
                    if (shell == "bash" && terminalName == "contour") {
                        _ = screen.Command("unset PROMPT_COMMAND; PROMPT_COMMAND=$'__bp_precmd_invoke_cmd\\n_statusline_precmd\\n__bp_interactive_mode'; _statusline_preserve_dispatcher");
                        string scalar = screen.Command("false");
                        Check(Plain(scalar).Contains("ERR 1", StringComparison.Ordinal) && scalar.Contains("\x1b]133;C", StringComparison.Ordinal) &&
                            scalar.Contains("\x1b]133;D;1", StringComparison.Ordinal), "Scalar dispatcher lost command hooks: " + scalar);
                    }
                    Console.WriteLine("UPSTREAM " + label);
                }
            }
        }
    }
}
