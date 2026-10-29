using System.CommandLine;

namespace StatusLine.Cli;

internal static class CommandLine {
    internal static int Invoke(string[] args) {
        var root = new RootCommand("StatusLine prompt and asynchronous status bar. Initialize with statusline init; render the bar with statusline statusline.");
        root.Directives.Add(new System.CommandLine.Completions.SuggestDirective());
        bool statusBar = args.FirstOrDefault() == "statusline";
        foreach (string name in new[] { "prompt", "statusline", "transient", "command-start", "init", "start", "serve", "worker", "worker-shell", "channel", "info", "stop" }) {
            // System.CommandLine rejects a child named identically to the executable.
            // Parse the existing 'statusline statusline' command as a standalone root.
            if (statusBar ? name != "statusline" : name == "statusline") { continue; }
            Command command = statusBar ? root : new Command(name, Description(name)) { Hidden = name is "worker" or "worker-shell" or "serve" or "channel" };
            var options = new List<Option<string>>();
            string[] names = name switch {
                "init" => ["--config", "--status-file", "--profile"],
                "start" or "serve" or "worker" or "worker-shell" or "channel" => ["--config", "--status-file", "--parent-pid", "--session"],
                "info" or "stop" => ["--session"],
                _ => ["--config", "--cwd", "--width", "--status", "--duration-ms", "--shell", "--session", "--parent-pid", "--status-file", "--integration-owner"],
            };
            foreach (string optionName in names.Concat(name == "command-start" ? ["--command"] : Array.Empty<string>())) {
                var option = new Option<string>(optionName) { Arity = ArgumentArity.ExactlyOne, Description = OptionDescription(optionName) };
                option.Hidden = optionName == "--integration-owner";
                option.Validators.Add(result => {
                    if (result.Tokens.Count > 1) { result.AddError($"Repeated option: {optionName}."); }
                });
                if (optionName == "--shell") { option.AcceptOnlyFromAmong("raw", "bash", "zsh", "fish"); }
                command.Options.Add(option);
                options.Add(option);
            }
            var shell = new Argument<string>("shell") { Arity = ArgumentArity.ZeroOrOne, Description = "Shell adapter; inferred from the calling shell when omitted." };
            var install = new Option<bool>("--install") { Description = "Install a managed hook in the shell profile (opt in)." };
            var uninstall = new Option<bool>("--uninstall") { Description = "Remove the managed hook from the shell profile." };
            if (name == "init") {
                shell.AcceptOnlyFromAmong("bash", "zsh", "fish");
                command.Arguments.Add(shell);
                command.Options.Add(install);
                command.Options.Add(uninstall);
            }
            command.SetAction(result => {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Option<string> option in options) {
                    if (result.GetValue(option) is { } value) { values.Add(option.Name, value); }
                }
                if (name == "init") {
                    if (result.GetValue(install)) { values.Add("--install", "true"); }
                    if (result.GetValue(uninstall)) { values.Add("--uninstall", "true"); }
                }
                return Program.Run(name, name == "init" ? result.GetValue(shell) : null, values);
            });
            if (!statusBar) { root.Subcommands.Add(command); }
        }
        var complete = new Command("complete", "Return completion candidates for shell adapters.");
        var line = new Option<string>("--line") { Required = true, Description = "Command line to complete." };
        var position = new Option<int?>("--position") { Description = "Cursor position; defaults to the end of the command line." };
        complete.Options.Add(line);
        complete.Options.Add(position);
        complete.SetAction(result => {
            string text = result.GetValue(line)!;
            int cursor = result.GetValue(position) ?? text.Length;
            if (cursor < 0 || cursor > text.Length) { throw new ArgumentException("Completion cursor is outside the command line."); }
            foreach (var candidate in root.Parse(text).GetCompletions(cursor)) { Console.WriteLine(candidate.InsertText); }
            return 0;
        });
        if (!statusBar) {
            root.Subcommands.Add(complete);
        }
        if (args.Length == 0 || args is ["help"]) { args = ["--help"]; }
        var parsed = root.Parse(statusBar ? args[1..] : args);
        if (parsed.Errors.Count > 0) {
            foreach (var error in parsed.Errors) { Console.Error.WriteLine("statusline: " + error.Message); }
            return 2;
        }
        return parsed.Invoke(new InvocationConfiguration { EnableDefaultExceptionHandler = false });
    }

    private static string Description(string name) => name switch {
        "init" => "Generate a Bash, Zsh or Fish adapter; optionally install it.",
        "start" => "Start a background worker owned by a shell process.",
        "info" => "Show worker state and provider diagnostics.",
        "stop" => "Stop a session worker and restore terminal state.",
        "statusline" => "Render the status bar.",
        "transient" => "Render the transient prompt.",
        "command-start" => "Notify the worker that command execution is starting.",
        _ => "Render the prompt.",
    };

    private static string OptionDescription(string name) => name switch {
        "--config" => "JSON configuration file.", "--cwd" => "Absolute working directory.",
        "--width" => "Terminal width in cells (2–10000).", "--status" => "Previous command exit status.",
        "--duration-ms" => "Previous command duration in milliseconds.", "--shell" => "Output escaping convention.",
        "--session" => "Worker session token.", "--parent-pid" => "Owning shell process ID.",
        "--profile" => "Profile file to update with --install or --uninstall.",
        "--command" => "Command text for shell integration.", _ => "Status bar output file.",
    };
}
