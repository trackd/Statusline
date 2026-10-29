using System.Diagnostics;

namespace StatusLine.Cli.Tests;

internal static partial class Program {
    private static readonly string[] ShellVariables = ["PATH", "VIRTUAL_ENV", "CONDA_PREFIX", "CONDA_DEFAULT_ENV", "KUBECONFIG", "DOCKER_CONTEXT",
        "HOME", "USERPROFILE", "DOTNET_ROOT", "RUSTUP_TOOLCHAIN", "JAVA_HOME", "PYENV_VERSION", "RBENV_VERSION", "TERM_PROGRAM", "TERM",
        "WT_SESSION", "VSCODE_SHELL_INTEGRATION", "AWS_PROFILE", "AWS_DEFAULT_PROFILE", "AZURE_SUBSCRIPTION_ID"];

    private static string Field(StreamReader reader) {
        var text = new System.Text.StringBuilder();
        char[] value = new char[1];
        while (reader.ReadAsync(value.AsMemory()).AsTask().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult() != 0) {
            if (value[0] == '\0') { return text.ToString(); }
            text.Append(value[0]);
        }
        throw new EndOfStreamException(text.ToString());
    }

    private static (string Result, string Output) ShellRequest(Process worker, string command, string path, int status = 0, string? changedProfile = null) {
        string[] fields = [command, path, "120", status.ToString(System.Globalization.CultureInfo.InvariantCulture), "1200", "bash", "", "0",
            .. ShellVariables.Select(name => name == "AWS_PROFILE" && changedProfile is not null ? changedProfile : Environment.GetEnvironmentVariable(name) ?? "")];
        worker.StandardInput.Write(string.Join('\0', fields) + '\0');
        worker.StandardInput.Flush();
        string result = Field(worker.StandardOutput);
        string prefix = Field(worker.StandardOutput);
        return (result, prefix + Field(worker.StandardOutput));
    }

    private static void PersistentTransport() {
        using var fixture = new Fixture();
        fixture.Configure(new { Left = new[] { "Directory", "Status", "Duration" }, Style = "Plain", IconSet = "None", DirectoryStyle = "Leaf" });
        var start = new ProcessStartInfo(binary) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in new[] { "serve", "--parent-pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), "--config", fixture.Config }) {
            start.ArgumentList.Add(argument);
        }
        using Process worker = Process.Start(start) ?? throw new IOException("No worker.");
        try {
            string token = Field(worker.StandardOutput);
            Check(token.Length == 32 && WorkerPid(token) == worker.Id, "Stdio and control IPC do not share one worker.");
            string directory = Path.Combine(fixture.Path, OperatingSystem.IsWindows() ? "literal_$(unchanged)" : "literal_$(unchanged)\nsecond line");
            Directory.CreateDirectory(directory);
            var response = ShellRequest(worker, "command-start", directory);
            Check(response.Result == "ok", response.Result);
            Check(Info(token).GetProperty("IsCommandExecuting").GetBoolean(), "Execution notification was lost.");
            response = ShellRequest(worker, "prompt", directory, 17);
            Check(response.Result == "ok" && Plain(response.Output).Contains("ERR 17", StringComparison.Ordinal) &&
                Plain(response.Output).Contains("1.2s", StringComparison.Ordinal) && response.Output.Contains("$(unchanged)", StringComparison.Ordinal), response.Output);
            Check(!Info(token).GetProperty("IsCommandExecuting").GetBoolean(), "Prompt did not finish execution.");
            response = ShellRequest(worker, "prompt", directory, changedProfile: "new-profile");
            Check(response.Result == "restart", "Changed provider environment retained old caches.");
            Check(worker.WaitForExit(5000), "Worker did not close after requesting restart.");
            Check(worker.ExitCode == 0, worker.StandardError.ReadToEnd());
        }
        finally { if (!worker.HasExited) { worker.Kill(entireProcessTree: true); worker.WaitForExit(); } }
    }

    private static void PathsAndCompletion() {
        using var fixture = new Fixture();
        string? previous = Environment.GetEnvironmentVariable("HOME");
        try {
            Environment.SetEnvironmentVariable("HOME", fixture.Path);
            fixture.Configure(new { Left = new[] { "Status" }, Style = "Plain", IconSet = "None" });
            Check(Plain(Success("prompt", "--config", "~/" + Path.GetFileName(fixture.Config))).Contains("OK", StringComparison.Ordinal), "Quoted tilde configuration was not expanded.");
            string help = Success("init", "--help");
            Check(help.Contains("--install", StringComparison.Ordinal) && help.Contains("--profile", StringComparison.Ordinal), help);
            string candidates = Success("complete", "--line", "statusline prompt --s");
            Check(candidates.Split('\n').Select(value => value.Trim()).Contains("--shell", StringComparer.Ordinal) &&
                !candidates.Contains("--install", StringComparison.Ordinal), candidates);
            Check(Success("complete", "--line", "statusline init f").Contains("fish", StringComparison.Ordinal), "Shell argument completion missing.");
        }
        finally { Environment.SetEnvironmentVariable("HOME", previous); }
    }

    private static void BashPersistent() {
        if (!OperatingSystem.IsLinux()) { return; }
        using var fixture = new Fixture();
        fixture.Configure(new { Left = new[] { "Directory", "Status" }, DirectoryStyle = "Leaf", IconSet = "None", Style = "Plain", Symbol = " __STATUSLINE_READY__ " });
        string copy = Path.Combine(fixture.Path, "statusline");
        File.Copy(binary, copy);
        UnixFileMode mode = File.GetUnixFileMode(binary);
        File.SetUnixFileMode(copy, mode);
        using var terminal = new LinuxTerminal("bash", fixture.Path);
        _ = terminal.Command("eval \"$(" + Quote(copy) + " init bash --config " + Quote(fixture.Config) + ")\"");
        string fromHook = Path.Combine(fixture.Path, "from-existing-hook");
        Directory.CreateDirectory(fromHook);
        string changed = terminal.Command("_test_hook() { cd " + Quote(fromHook) + "; }; PROMPT_COMMAND=(_statusline_precmd _test_hook _statusline_refresh)");
        Check(Plain(changed).Contains("from-existing-hook", StringComparison.Ordinal), "Render ran before the existing directory-changing hook.");
        File.SetUnixFileMode(copy, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        try {
            Check(Plain(terminal.Command("false")).Contains("ERR 1", StringComparison.Ordinal), "Prompt needs an executable client after initialization.");
            Check(Plain(terminal.Command("true")).Contains("OK", StringComparison.Ordinal), "Success notification needs an executable client.");
        }
        finally { File.SetUnixFileMode(copy, mode); }
        string recovered = terminal.Command("kill -KILL \"$_statusline_worker\"");
        Check(recovered.Contains("__STATUSLINE_READY__", StringComparison.Ordinal), "Prompt did not recover from worker death.");
        string exported = terminal.Command("export AWS_PROFILE=statusline-test-profile");
        Check(exported.Contains("__STATUSLINE_READY__", StringComparison.Ordinal), "Environment change broke persistent connection.");
        Check(Plain(terminal.Command("false")).Contains("ERR 1", StringComparison.Ordinal), "Replacement connection did not survive in the parent shell.");
    }

    private static void ShellDetection() {
        using var fixture = new Fixture();
        foreach (string name in new[] { ".bashrc", ".zshrc", ".config/fish/config.fish" }) {
            string path = Path.Combine(fixture.Path, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "# Another shell is installed.\n");
        }
        foreach (string shell in new[] { "bash", "zsh", "fish" }) {
            var result = Run(shell, fixture.Path, shell == "bash" ? "--noprofile" : shell == "zsh" ? "-f" : "--no-config", "-c",
                "HOME=" + Quote(fixture.Path) + " SHELL=/bin/not-the-calling-shell " + Quote(binary) + " init; true");
            Check(result.Code == 0 && result.Out.Contains(shell == "fish" ? "fish_prompt" : shell == "zsh" ? "precmd_functions" : "PROMPT_COMMAND", StringComparison.Ordinal), shell + ": " + result.Error);
        }
    }

    private static void OtherShellsPersistent() {
        if (!OperatingSystem.IsLinux()) { return; }
        foreach (string shell in new[] { "zsh", "fish" }) {
            using var fixture = new Fixture();
            fixture.Configure(new { Left = new[] { "Status" }, IconSet = "None", Style = "Plain", Symbol = " __STATUSLINE_READY__ " });
            string copy = Path.Combine(fixture.Path, "statusline");
            File.Copy(binary, copy);
            UnixFileMode mode = File.GetUnixFileMode(binary);
            File.SetUnixFileMode(copy, mode);
            using var terminal = new LinuxTerminal(shell, fixture.Path);
            string init = Quote(copy) + " init " + shell + " --config " + Quote(fixture.Config);
            _ = terminal.Command(shell == "fish" ? init + " | source" : "eval \"$(" + init + ")\"");
            File.SetUnixFileMode(copy, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            try {
                Check(Plain(terminal.Command("false")).Contains("ERR 1", StringComparison.Ordinal), shell + " still needs a per-prompt client.");
                Check(Plain(terminal.Command("true")).Contains("OK", StringComparison.Ordinal), shell + " lost success status.");
            }
            finally { File.SetUnixFileMode(copy, mode); }
            string tokenOutput = terminal.Command("printf 'TOKEN:%s\\n' \"$_statusline_session\"");
            var token = SessionTokenPattern().Match(tokenOutput);
            Check(token.Success, tokenOutput);
            using (Process worker = Process.GetProcessById(WorkerPid(token.Groups[1].Value))) { worker.Kill(); worker.WaitForExit(); }
            Check(Plain(terminal.Command("false")).Contains("ERR 1", StringComparison.Ordinal), shell + " did not recover its channel.");
            _ = terminal.Command("export AWS_PROFILE=statusline-test-profile");
            Check(Plain(terminal.Command("false")).Contains("ERR 1", StringComparison.Ordinal), shell + " failed after provider-environment change.");
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex("TOKEN:([a-f0-9]{32})")]
    private static partial System.Text.RegularExpressions.Regex SessionTokenPattern();

    private static void SuIdentity() {
        if (!OperatingSystem.IsLinux()) { return; }
        using var fixture = new Fixture();
        fixture.Configure(new { Left = new[] { "User", "Elevation" }, Style = "Plain", IconSet = "None" });
        File.SetUnixFileMode(fixture.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        File.SetUnixFileMode(fixture.Config, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        Check(Plain(Success("prompt", "--config", fixture.Config, "--cwd", "/tmp")).Contains("root", StringComparison.Ordinal), "Parent identity missing.");
        string session = Start(fixture);
        int before = WorkerPid(session);
        string command = Quote(binary) + " prompt --config " + Quote(fixture.Config) + " --cwd /tmp";
        var other = Run("su", "/tmp", "-s", "/bin/bash", "nobody", "-c", command);
        Check(other.Code == 0 && Plain(other.Out).Contains("nobody", StringComparison.Ordinal), other.Error);
        var denied = Run("su", "/tmp", "-s", "/bin/bash", "nobody", "-c", Quote(binary) + " info --session " + session);
        Check(denied.Code == 2 && denied.Out.Length == 0 && denied.Error.Contains("statusline:", StringComparison.Ordinal), denied.Error);
        Check(WorkerPid(session) == before, "Child user replaced the parent shell worker.");
    }
}
