using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StatusLine.Cli.Tests;

internal static partial class Program {
    private static string binary = "";
    private static int passed;
    private static int failed;
    private static readonly List<TestResult> Results = [];
    private sealed record TestResult(string Name, bool Passed, string? Error);

    public static int Main(string[] args) {
        if (args is ["--shell-test", string path, string shell]) { binary = Path.GetFullPath(path); Case(shell + "Pty", () => Shell(shell)); return failed == 0 ? 0 : 1; }
        if (args is ["--wait"]) { Thread.Sleep(Timeout.Infinite); return 0; }
        if (args is ["--version"]) {
            string counter = Environment.GetEnvironmentVariable("STATUSLINE_TEST_COUNTER") ?? throw new IOException("Missing counter.");
            int count = File.Exists(counter) ? int.Parse(File.ReadAllText(counter), CultureInfo.InvariantCulture) : 0;
            File.WriteAllText(counter, (count + 1).ToString(CultureInfo.InvariantCulture));
            if (File.Exists(counter + ".fail")) { Console.Error.WriteLine("Provider failure fixture."); return 13; }
            Console.WriteLine("v20.1.0");
            return 0;
        }
        binary = Path.GetFullPath(args[0]);
        Case("StandaloneNativeExecutable", Standalone);
        Case("ArgumentAndConfigurationErrors", InvalidInput);
        Case("SharedGitStatusAndDuration", GitData);
        Case("ShellMarkersAndGroupedRows", Framing);
        Case("WorkerCachesIdleRefreshAndSessionIsolation", Worker);
        Case("WorkerRecoveryAndConfigurationReload", Recovery);
        Case("WorkerStopsWhenParentExits", ParentExit);
        Case("OptInHookInstallUpdateAndRemoval", Hooks);
        Case("HookDefaultPathsAndSymlink", HookPaths);
        Case("IdleTimeoutPausesAndActivityResumes", IdleTimeout);
        Case("CommandRefreshPolicyAndDisabledIdleTimeout", CommandRefresh);
        if (OperatingSystem.IsLinux() && Environment.Is64BitProcess) {
            Case("BashPty", () => Shell("bash"));
            Case("ZshPtyAndTransient", () => Shell("zsh"));
            Case("FishPty", () => Shell("fish"));
            Case("InstalledHooksInRealShells", InstalledHooks);
            Case("HostWritableIdleResizeAndStop", HostWritable);
            Case("HostWritableRejectsUnsupportedTerminal", HostWritableUnsupported);
            Case("HostWritableParentExitCleanup", HostWritableParentExit);
        }
        else {
            Console.WriteLine("Linux/glibc PTY cases are not run on this host; CLI/worker cases run on every OS.");
        }
        Console.WriteLine($"Passed: {passed}, Failed: {failed}");
        string report = Path.GetFullPath(args.Length > 1 ? args[1] : "artifacts/native-test-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        File.WriteAllText(report, JsonSerializer.Serialize(Results));
        return failed == 0 ? 0 : 1;
    }

    private static void Case(string name, Action test) {
        try { test(); passed++; Results.Add(new(name, true, null)); Console.WriteLine("PASS " + name); }
        catch (Exception exception) { failed++; Results.Add(new(name, false, exception.ToString())); Console.Error.WriteLine("FAIL " + name + ": " + exception); }
    }

    private static void Check(bool condition, string message) {
        if (!condition) { throw new InvalidOperationException(message); }
    }

    private static (int Code, string Out, string Error) Cli(params string[] args) => Run(binary, null, args);

    private static (int Code, string Out, string Error) Run(string executable, string? directory, params string[] args) {
        var start = new ProcessStartInfo(executable) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = directory ?? Environment.CurrentDirectory,
        };
        foreach (string argument in args) { start.ArgumentList.Add(argument); }
        using Process process = Process.Start(start) ?? throw new IOException("Process did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000)) { process.Kill(entireProcessTree: true); throw new IOException("Process timeout: " + executable); }
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    private static string Success(params string[] args) {
        var result = Cli(args);
        Check(result.Code == 0, result.Error);
        return result.Out;
    }

    private static string Plain(string value) => VtPattern().Replace(value, "");
    [GeneratedRegex(@"\x1b\[[0-?]*[ -/]*[@-~]|\x1b\](?:(?!\x07|\x1b\\).)*(?:\x07|\x1b\\)", RegexOptions.Singleline)]
    private static partial Regex VtPattern();
    [GeneratedRegex("0\\.[2-9]s", RegexOptions.CultureInvariant)]
    private static partial Regex DurationPattern();

    private static void Standalone() {
        using var fixture = new Fixture();
        string copy = Path.Combine(fixture.Path, Path.GetFileName(binary));
        File.Copy(binary, copy);
        if (!OperatingSystem.IsWindows()) { File.SetUnixFileMode(copy, File.GetUnixFileMode(binary)); }
        var result = Run(copy, fixture.Path, "--help");
        Check(result.Code == 0 && result.Out.Contains("statusline init", StringComparison.Ordinal), result.Error);
        Check(Directory.GetFiles(fixture.Path).Length == 1, "Standalone fixture unexpectedly contains dependencies.");
    }

    private static void InvalidInput() {
        foreach (string[] arguments in new[] {
            new[] { "prompt", "--width", "1" }, new[] { "prompt", "--width", "80", "--width", "90" },
            new[] { "missing" }, new[] { "prompt", "--duration-ms", "NaN" }, new[] { "init", "missing" },
            new[] { "info", "--session", "not-a-token" },
        }) {
            var result = Cli(arguments);
            Check(result.Code == 2 && result.Out.Length == 0 && result.Error.Contains("statusline:", StringComparison.Ordinal), string.Join(' ', arguments));
        }
        using var fixture = new Fixture();
        foreach (string json in new[] { "{\"Left\":[\"Runspaces\"]}", "{\"Style\":99}", "{\"StartColor\":\"red\"}", "{\"Left\":null}", "{\"Unknown\":true}", "{\"IdleTimeoutSeconds\":-1}", "{\"IdleTimeoutSeconds\":86401}" }) {
            File.WriteAllText(fixture.Config, json);
            var result = Cli("prompt", "--config", fixture.Config);
            Check(result.Code == 2 && result.Out.Length == 0, json + ": " + result.Error);
        }
    }

    private static void GitData() {
        using var fixture = new Fixture();
        fixture.Git();
        File.WriteAllText(Path.Combine(fixture.Path, "tracked"), "before");
        Check(Run("git", fixture.Path, "add", "tracked").Code == 0, "git add");
        Check(Run("git", fixture.Path, "commit", "-qm", "fixture").Code == 0, "git commit");
        File.WriteAllText(Path.Combine(fixture.Path, "tracked"), "after");
        File.WriteAllText(Path.Combine(fixture.Path, "untracked"), "new");
        fixture.Configure(new { Left = new[] { "Git", "Status", "Duration" }, IconSet = "None", Style = "Plain" });
        string output = Plain(Success("prompt", "--config", fixture.Config, "--cwd", fixture.Path, "--status", "17", "--duration-ms", "1200"));
        Check(output.Contains("topic", StringComparison.Ordinal) && output.Contains("~1", StringComparison.Ordinal) && output.Contains("?2", StringComparison.Ordinal), output);
        Check(output.Contains("ERR 17", StringComparison.Ordinal) && output.Contains("1.2s", StringComparison.Ordinal), output);
    }

    private static void Framing() {
        using var fixture = new Fixture();
        fixture.Configure(new { Left = new[] { "Status" }, IconSet = "None", Style = "Powerline", StartColor = "#0400fdff", EndColor = "#ff7b00ff" });
        foreach (string shell in new[] { "raw", "bash", "zsh", "fish" }) {
            string output = Success("prompt", "--config", fixture.Config, "--shell", shell, "--status", "17");
            Check(output.Contains("ERR 17", StringComparison.Ordinal), output);
            if (shell == "bash") { Check(output.Contains("\x01\x1b[", StringComparison.Ordinal) && output.Count(c => c == '\x01') == output.Count(c => c == '\x02'), output); }
            if (shell == "zsh") { Check(output.Contains("%{\x1b[", StringComparison.Ordinal), output); }
        }
        fixture.Configure(new { Left = new[] { "User" }, Right = new[] { "Status" }, Rows = 2, Style = "Plain", IconSet = "None", Symbol = " READY " });
        string prompt = Plain(Success("prompt", "--config", fixture.Config, "--width", "120", "--status", "17"));
        string line = Plain(Success("statusline", "--config", fixture.Config, "--width", "119", "--status", "17"));
        Check(prompt.Split('\n').Length == 2 && prompt.Split('\n')[0] == line, prompt);
        string? terminal = Environment.GetEnvironmentVariable("TERM_PROGRAM");
        try {
            Environment.SetEnvironmentVariable("TERM_PROGRAM", "contour");
            string session = Start(fixture);
            string boundary = Success("command-start", "--session", session);
            Check(boundary.StartsWith("\x1b[?2028h", StringComparison.Ordinal), "Bash-style boundary failed to restore Contour reflow.");
            string marked = Success("prompt", "--session", session, "--cwd", fixture.Path, "--status", "17");
            Check(marked.Contains("\x1b]7;", StringComparison.Ordinal) && marked.Contains("\x1b]133;D;17", StringComparison.Ordinal), "Contour working-directory/exit metadata missing.");
        }
        finally { Environment.SetEnvironmentVariable("TERM_PROGRAM", terminal); }
    }

    private static string Start(Fixture fixture, int? parent = null, string? statusFile = null) {
        var args = new List<string> { "start", "--parent-pid", (parent ?? Environment.ProcessId).ToString(CultureInfo.InvariantCulture), "--config", fixture.Config };
        if (statusFile is not null) { args.AddRange(["--status-file", statusFile]); }
        string session = Success([.. args]);
        fixture.Sessions.Add(session);
        Check(session.Length == 32, "Invalid session token: " + session);
        return session;
    }

    private static int WorkerPid(string session) {
        using JsonDocument document = JsonDocument.Parse(Success("info", "--session", session));
        return document.RootElement.GetProperty("WorkerPid").GetInt32();
    }

    private static JsonElement Info(string session) {
        using JsonDocument document = JsonDocument.Parse(Success("info", "--session", session));
        return document.RootElement.Clone();
    }

    private static void IdleTimeout() {
        using var fixture = new Fixture();
        fixture.Configure(new { IdleTimeoutSeconds = 1, Left = new[] { "Status" },
            StatusLine = new { Left = new[] { "Clock" }, Style = "Plain", IconSet = "None" } });
        string file = Path.Combine(fixture.Path, "idle.txt");
        string session = Start(fixture, statusFile: file);
        _ = Render(session, fixture.Path);
        Until(() => File.Exists(file), "No initial status frame.");
        Until(() => Info(session).GetProperty("IsIdle").GetBoolean(), "Worker did not become idle.");
        string frozen = File.ReadAllText(file);
        double before = Info(session).GetProperty("IdleSeconds").GetDouble();
        Thread.Sleep(1400);
        JsonElement idle = Info(session);
        Check(idle.GetProperty("RefreshPaused").GetBoolean() && idle.GetProperty("IdleSeconds").GetDouble() > before + 1,
            "Diagnostic polling reset idle time or failed to report the pause.");
        Check(File.ReadAllText(file) == frozen, "Idle worker kept refreshing status output.");
        _ = Render(session, fixture.Path);
        Check(!Info(session).GetProperty("IsIdle").GetBoolean(), "Prompt readiness did not resume activity.");
        Until(() => File.ReadAllText(file) != frozen, "Status output did not resume after prompt readiness.");
        Until(() => Info(session).GetProperty("IsIdle").GetBoolean(), "Worker did not idle again.");
        _ = Success("command-start", "--session", session);
        JsonElement executing = Info(session);
        Check(!executing.GetProperty("IsIdle").GetBoolean() && executing.GetProperty("IsCommandExecuting").GetBoolean(),
            "Command acceptance did not reset idle state.");
    }

    private static void CommandRefresh() {
        using var fixture = new Fixture();
        foreach (bool update in new[] { false, true }) {
            fixture.Configure(new { IdleTimeoutSeconds = 0, UpdateDuringCommand = update, Left = new[] { "Status" },
                StatusLine = new { Left = new[] { "Clock" }, Style = "Plain", IconSet = "None" } });
            string file = Path.Combine(fixture.Path, update ? "during.txt" : "paused.txt");
            string session = Start(fixture, statusFile: file);
            _ = Render(session, fixture.Path);
            Until(() => File.Exists(file), "Status file was not initialized.");
            _ = Success("command-start", "--session", session);
            string first = File.ReadAllText(file);
            Thread.Sleep(1600);
            JsonElement state = Info(session);
            Check(!state.GetProperty("IsIdle").GetBoolean() && state.GetProperty("IsCommandExecuting").GetBoolean(), "Zero timeout did not disable idle pausing.");
            Check(state.GetProperty("RefreshPaused").GetBoolean() == !update, "Wrong command refresh policy.");
            Check((File.ReadAllText(file) != first) == update, "Command refresh output disagrees with policy.");
            _ = Render(session, fixture.Path);
            Check(!Info(session).GetProperty("IsCommandExecuting").GetBoolean(), "Prompt did not end command execution.");
            if (!update) { Until(() => File.ReadAllText(file) != first, "Output did not resume after command completion."); }
            _ = Success("stop", "--session", session);
            fixture.Sessions.Remove(session);
        }
    }

    private static string Render(string session, string path) => Plain(Success("prompt", "--session", session,
        "--parent-pid", Environment.ProcessId.ToString(CultureInfo.InvariantCulture), "--cwd", path));

    private static void Until(Func<bool> condition, string message, int milliseconds = 6000) {
        var watch = Stopwatch.StartNew();
        while (!condition()) {
            if (watch.ElapsedMilliseconds > milliseconds) { throw new IOException(message); }
            Thread.Sleep(30);
        }
    }

    private static void Worker() {
        using var fixture = new Fixture();
        string fakeDirectory = Path.Combine(fixture.Path, "tools");
        Directory.CreateDirectory(fakeDirectory);
        string host = Environment.ProcessPath ?? throw new IOException("Test apphost unavailable.");
        string fakeName = OperatingSystem.IsWindows() ? "node.exe" : "node";
        File.Copy(host, Path.Combine(fakeDirectory, fakeName));
        if (!OperatingSystem.IsWindows()) { File.SetUnixFileMode(Path.Combine(fakeDirectory, fakeName), File.GetUnixFileMode(host)); }
        foreach (string name in new[] { "NativeCli.Tests.dll", "NativeCli.Tests.deps.json", "NativeCli.Tests.runtimeconfig.json" }) {
            File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(fakeDirectory, name));
        }
        string counter = Path.Combine(fixture.Path, "count");
        string? originalPath = Environment.GetEnvironmentVariable("PATH");
        string? originalCounter = Environment.GetEnvironmentVariable("STATUSLINE_TEST_COUNTER");
        Environment.SetEnvironmentVariable("PATH", fakeDirectory + Path.PathSeparator + originalPath);
        Environment.SetEnvironmentVariable("STATUSLINE_TEST_COUNTER", counter);
        try {
            File.WriteAllText(Path.Combine(fixture.Path, "package.json"), "{\"name\":\"sample\",\"version\":\"1.2.3\"}");
            fixture.Configure(new { Left = new[] { "Node", "Status" }, IconSet = "None", Style = "Plain", ShellIntegration = "Osc133",
                StatusLine = new { Left = new[] { "Clock" }, Right = Array.Empty<string>(), Style = "Plain", IconSet = "None" } });
            string file = Path.Combine(fixture.Path, "status.txt");
            string session = Start(fixture, statusFile: file);
            int pid = WorkerPid(session);
            Until(() => Render(session, fixture.Path).Contains("20.1.0", StringComparison.Ordinal), "Node collection incomplete.");
            for (int index = 0; index < 20; index++) { Check(Render(session, fixture.Path).Contains("20.1.0", StringComparison.Ordinal), "Lost cached Node value."); }
            Check(File.ReadAllText(counter) == "1", "Node recollected during cached render calls.");
            Check(WorkerPid(session) == pid, "Worker restarted between renders.");
            Until(() => File.Exists(file) && File.ReadAllText(file).Length > 0, "Status file not created.");
            string first = File.ReadAllText(file);
            Until(() => File.ReadAllText(file) != first, "Status file did not refresh while shell was idle.");
            string execute = Success("command-start", "--session", session);
            Check(execute.Contains("\x1b]133;C", StringComparison.Ordinal), "Missing execution marker.");
            string prompt = Success("prompt", "--session", session, "--cwd", fixture.Path, "--status", "17");
            Check(prompt.Contains("\x1b]133;D;17", StringComparison.Ordinal) && prompt.Contains("\x1b]133;A", StringComparison.Ordinal) && prompt.Contains("\x1b]133;B", StringComparison.Ordinal), "Missing prompt lifecycle markers.");
            Check(WorkerPid(Start(fixture)) != pid, "Sessions share one worker.");
            File.WriteAllText(counter + ".fail", "fail");
            Until(() => Success("info", "--session", session).Contains("Node:", StringComparison.Ordinal), "Provider refresh failure was not reported.", 15000);
            string stale = Render(session, fixture.Path);
            Check(stale.Contains("20.1.0", StringComparison.Ordinal) && stale.Contains("[stale]", StringComparison.Ordinal), "Provider failure discarded usable cached data: " + stale);
        }
        finally {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Environment.SetEnvironmentVariable("STATUSLINE_TEST_COUNTER", originalCounter);
        }
    }

    private static void Recovery() {
        using var fixture = new Fixture();
        fixture.Configure(new { Left = new[] { "Status" }, Style = "Plain", IconSet = "None", Symbol = " ORIGINAL " });
        string session = Start(fixture);
        int first = WorkerPid(session);
        using (Process worker = Process.GetProcessById(first)) {
            worker.Kill();
            Check(worker.WaitForExit(3000), "Killed worker was not reaped; run container tests with --init.");
        }
        Check(Render(session, fixture.Path).Contains("ORIGINAL", StringComparison.Ordinal), "Worker did not recover.");
        Check(WorkerPid(session) != first, "Worker PID did not change.");
        fixture.Configure(new { Left = new[] { "Status" }, Style = "Plain", IconSet = "None", Symbol = " RELOADED_CONFIGURATION " });
        Check(Render(session, fixture.Path).Contains("RELOADED_CONFIGURATION", StringComparison.Ordinal), "Configuration not reloaded.");
        int old = WorkerPid(session);
        string? before = Environment.GetEnvironmentVariable("VIRTUAL_ENV");
        try {
            Environment.SetEnvironmentVariable("VIRTUAL_ENV", fixture.Path);
            _ = Render(session, fixture.Path);
            Check(WorkerPid(session) != old, "Provider environment did not invalidate worker caches.");
        }
        finally { Environment.SetEnvironmentVariable("VIRTUAL_ENV", before); }
    }

    private static void ParentExit() {
        using var fixture = new Fixture();
        fixture.Configure(new { Left = new[] { "Status" } });
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new IOException("No apphost.")) { UseShellExecute = false, CreateNoWindow = true };
        // A dedicated managed test host waits without using an external scripting language.
        start.ArgumentList.Add("--wait");
        using Process parent = Process.Start(start) ?? throw new IOException("No parent.");
        try {
            int worker = WorkerPid(Start(fixture, parent.Id));
            parent.Kill(); parent.WaitForExit();
            Until(() => { try { using Process process = Process.GetProcessById(worker); return process.HasExited; } catch (ArgumentException) { return true; } }, "Worker survived its owning parent.");
        }
        finally { if (!parent.HasExited) { parent.Kill(); parent.WaitForExit(); } }
    }

    private static void Shell(string shell) {
        using var fixture = new Fixture();
        string shellVersion = shell == "fish" ? Run("fish", null, "--version").Out : "";
        bool transient = shell == "zsh" || (shell == "fish" && Version.TryParse(shellVersion.Trim().Split(' ')[^1], out Version? fishVersion) && fishVersion >= new Version(4, 1));
        fixture.Configure(new { Left = new[] { "Directory", "Status", "Duration" }, Style = "Plain", IconSet = "None", DirectoryStyle = "Leaf",
            Symbol = " __STATUSLINE_READY__ ", Transient = transient, TransientSymbol = " SHORT_PROMPT " });
        string directory = Path.Combine(fixture.Path, "literal_%F{red}_$(touch injected)");
        Directory.CreateDirectory(directory);
        using var terminal = new LinuxTerminal(shell, fixture.Path);
        if (shell == "bash") { terminal.Send("PROMPT_COMMAND='printf EXISTING_HOOK'\n"); _ = terminal.ReadFor(50); }
        if (shell == "zsh") { terminal.Send("_existing() { print -n EXISTING_HOOK; }; precmd_functions=(_existing)\n"); _ = terminal.ReadFor(50); }
        string invocation = Quote(binary) + " init " + shell + " --config " + Quote(fixture.Config);
        string initialize = shell == "fish" ? invocation + " | source" : "eval \"$(" + invocation + ")\"";
        _ = terminal.Command(initialize);
        string failure = Plain(terminal.Command("false"));
        Check(failure.Contains("ERR 1", StringComparison.Ordinal), failure);
        if (shell != "fish") { Check(failure.Contains("EXISTING_HOOK", StringComparison.Ordinal), failure); }
        Check(Plain(terminal.Command("true")).Contains("OK", StringComparison.Ordinal), "Successful command status lost.");
        string moved = Plain(terminal.Command("cd " + Quote(directory)));
        Check(moved.Contains(Path.GetFileName(directory), StringComparison.Ordinal) && !File.Exists(Path.Combine(directory, "injected")), moved);
        string timed = Plain(terminal.Command("sleep 0.2"));
        Check(DurationPattern().IsMatch(timed), timed);
        if (transient) { Check(timed.Contains("SHORT_PROMPT", StringComparison.Ordinal), "Transient prompt not redrawn: " + timed); }
        _ = terminal.Command(initialize);
        if (shell != "fish") {
            string count = shell == "bash" ? "printf 'HOOKS:%s\\n' ${#PROMPT_COMMAND[@]}" : "print HOOKS:${#precmd_functions[@]}";
            Check(Plain(terminal.Command(count)).Contains("HOOKS:2", StringComparison.Ordinal), "Prompt hooks duplicated.");
        }
        terminal.Resize(80);
        Check(Plain(terminal.Command("true")).Contains("OK", StringComparison.Ordinal), "Prompt failed after resize.");
        terminal.Send("sleep 20\n"); _ = terminal.ReadFor(150); terminal.Send("\x03");
        string cancelled = terminal.Command("true");
        Check(cancelled.Contains("__STATUSLINE_READY__", StringComparison.Ordinal), "Prompt failed after Ctrl+C.");
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static void Hooks() {
        using var fixture = new Fixture();
        foreach (string shell in new[] { "bash", "zsh", "fish" }) {
            fixture.Configure(new { Left = new[] { "Status" } });
            string profile = Path.Combine(fixture.Path, shell + "-profile");
            const string original = "# user setting ✓\r\n# preserve me\r\n";
            File.WriteAllText(profile, original, new System.Text.UTF8Encoding(true));
            byte[] bytes = File.ReadAllBytes(profile);
            _ = Success("init", shell, "--config", fixture.Config);
            Check(File.ReadAllBytes(profile).SequenceEqual(bytes), "Plain init modified profile.");
            Check(!File.Exists(profile + ".statusline.bak"), "Plain init created a backup.");
            _ = Success("init", shell, "--install", "--profile", profile, "--config", fixture.Config);
            string installed = File.ReadAllText(profile);
            Check(installed.StartsWith(original, StringComparison.Ordinal), "User content was changed.");
            Check(installed.Contains("command statusline init " + shell, StringComparison.Ordinal) && !installed.Contains("--install", StringComparison.Ordinal), "Hook is recursive or missing.");
            Check(File.ReadAllBytes(profile + ".statusline.bak").SequenceEqual(bytes), "Original backup changed.");
            byte[] firstInstall = File.ReadAllBytes(profile);
            _ = Success("init", shell, "--config", fixture.Config, "--profile", profile, "--install");
            Check(File.ReadAllBytes(profile).SequenceEqual(firstInstall), "Repeated installation was not idempotent.");
            string statusFile = Path.Combine(fixture.Path, "line 'quoted'.txt");
            _ = Success("init", shell, "--install", "--profile", profile, "--config", fixture.Config, "--status-file", statusFile);
            string updated = File.ReadAllText(profile);
            Check(updated.Split("# >>> StatusLine >>>", StringSplitOptions.None).Length == 2, "Duplicate managed blocks.");
            Check(updated.Contains("--status-file", StringComparison.Ordinal) && !File.Exists(statusFile), "Installer executed its hook.");
            File.AppendAllText(profile, "# user later\r\n");
            File.Delete(fixture.Config);
            _ = Success("init", shell, "--uninstall", "--profile", profile, "--config", fixture.Config);
            Check(File.ReadAllText(profile) == original + "# user later\r\n", "Uninstall changed unrelated content.");
            Check(File.ReadAllBytes(profile).AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }), "UTF-8 BOM was lost.");
            _ = Success("init", shell, "--uninstall", "--profile", profile);
            Check(File.ReadAllBytes(profile + ".statusline.bak").SequenceEqual(bytes), "Backup overwritten.");
            File.WriteAllText(profile, "# >>> StatusLine >>>\nunterminated\n");
            byte[] incomplete = File.ReadAllBytes(profile);
            var result = Cli("init", shell, "--install", "--profile", profile);
            Check(result.Code == 2 && File.ReadAllBytes(profile).SequenceEqual(incomplete), "Malformed block was modified.");
            result = Cli("init", shell, "--install", "--uninstall", "--profile", profile);
            Check(result.Code == 2, "Conflicting flags accepted.");
        }
    }

    private static void HookPaths() {
        using var fixture = new Fixture();
        string? home = Environment.GetEnvironmentVariable("HOME");
        string? zdir = Environment.GetEnvironmentVariable("ZDOTDIR");
        string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("HOME", fixture.Path);
        Environment.SetEnvironmentVariable("ZDOTDIR", Path.Combine(fixture.Path, "z-conf"));
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(fixture.Path, "xdg"));
        try {
            var profiles = new Dictionary<string, string> {
                ["bash"] = Path.Combine(fixture.Path, ".bashrc"),
                ["zsh"] = Path.Combine(fixture.Path, "z-conf", ".zshrc"),
                ["fish"] = Path.Combine(fixture.Path, "xdg", "fish", "conf.d", "statusline.fish"),
            };
            foreach ((string shell, string path) in profiles) {
                _ = Success("init", shell);
                Check(!File.Exists(path), "Default init installed a hook.");
                _ = Success("init", shell, "--install");
                Check(File.Exists(path), "Wrong default profile path.");
                _ = Success("init", shell, "--uninstall");
                Check(File.ReadAllText(path) == "", "Owned hook was not removed.");
            }
        }
        finally {
            Environment.SetEnvironmentVariable("HOME", home);
            Environment.SetEnvironmentVariable("ZDOTDIR", zdir);
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", xdg);
        }
        if (!OperatingSystem.IsWindows()) {
            string target = Path.Combine(fixture.Path, "real-profile");
            string link = Path.Combine(fixture.Path, "profile-link");
            File.WriteAllText(target, "# user\n");
            File.CreateSymbolicLink(link, target);
            _ = Success("init", "bash", "--install", "--profile", link);
            Check(new FileInfo(link).LinkTarget is not null && File.ReadAllText(target).Contains("# >>> StatusLine >>>", StringComparison.Ordinal), "Profile symlink was replaced.");
            _ = Success("init", "bash", "--uninstall", "--profile", link);
            Check(File.ReadAllText(target) == "# user\n", "Symlink target content was not preserved.");
        }
    }

    private static void InstalledHooks() {
        using var fixture = new Fixture();
        fixture.Configure(new { Left = new[] { "Status" }, Style = "Plain", IconSet = "None", Symbol = " __STATUSLINE_READY__ " });
        string? previous = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(binary) + Path.PathSeparator + previous);
        try {
            foreach (string shell in new[] { "bash", "zsh", "fish" }) {
                string profile = Path.Combine(fixture.Path, shell + "-startup");
                _ = Success("init", shell, "--install", "--profile", profile, "--config", fixture.Config);
                using var terminal = new LinuxTerminal(shell, fixture.Path);
                _ = terminal.Command("source " + Quote(profile));
                Check(Plain(terminal.Command("false")).Contains("ERR 1", StringComparison.Ordinal), "Installed hook failed in " + shell);
            }
        }
        finally { Environment.SetEnvironmentVariable("PATH", previous); }
    }

    private static void HostWritable() {
        foreach (string shell in new[] { "bash", "zsh", "fish" }) {
            using var fixture = new Fixture();
            fixture.Configure(new {
                Target = "HostWritable", Left = new[] { "Status" }, Style = "Plain", IconSet = "None",
                Symbol = " __STATUSLINE_READY__ ", ShellIntegration = "None",
                StatusLine = new { Left = new[] { "Status" }, Right = new[] { "Clock" }, Style = "Plain", IconSet = "None" },
            });
            using var terminal = new LinuxTerminal(shell, fixture.Path, "contour");
            string invocation = Quote(binary) + " init " + shell + " --config " + Quote(fixture.Config);
            string initial = terminal.Command(shell == "fish" ? invocation + " | source" : "eval \"$(" + invocation + ")\"") + terminal.ReadFor(1500);
            Check(initial.Contains("\x1b[2$~", StringComparison.Ordinal), "Missing HostWritable activation in " + shell + ": " + initial);
            string[] frames = StatusFrames(initial);
            Check(frames.Length > 0 && Plain(frames[^1]).Contains("OK", StringComparison.Ordinal), "Missing idle status frame: " + initial);
            string idle = terminal.ReadFor(1600);
            Check(StatusFrames(idle).Length > 0, "Clock did not refresh while idle in " + shell);
            Check(!idle.Contains("\x1b[2$~", StringComparison.Ordinal), "Routine redraw reactivated/resized status area.");
            int originalLength = Plain(frames[^1]).Trim('\r', '\n').Length;
            terminal.Resize(80);
            string resized = terminal.ReadFor(1200);
            string[] smaller = StatusFrames(resized);
            Check(smaller.Length > 0 && Plain(smaller[^1]).Trim('\r', '\n').Length < originalLength,
                "Idle resize did not use controlling-terminal width: " + resized);
            string failure = terminal.Command("false") + terminal.ReadFor(500);
            Check(StatusFrames(failure).Any(frame => Plain(frame).Contains("ERR 1", StringComparison.Ordinal)), "Status line lost shell exit status.");
            string token = shell == "fish" ? "$_statusline_session" : "\"$_statusline_session\"";
            // Disable prompt hooks before stopping so the next prompt cannot recover the worker.
            string disable = shell switch {
                "bash" => "unset PROMPT_COMMAND; PS1='__STATUSLINE_READY__'; ",
                "zsh" => "precmd_functions=(); preexec_functions=(); PROMPT='__STATUSLINE_READY__'; ",
                _ => "functions --erase fish_prompt; function fish_prompt; echo __STATUSLINE_READY__; end; ",
            };
            string stopped = terminal.Command(disable + Quote(binary) + " stop --session " + token) + terminal.ReadFor(600);
            Check(stopped.Contains("\x1b[0$~", StringComparison.Ordinal) && stopped.Contains("\x1b[0$}", StringComparison.Ordinal),
                "Stopping did not restore main display and hide status area.");
        }
    }

    private static string[] StatusFrames(string output) => output.Split("\x1b[1$}", StringSplitOptions.None).Skip(1)
        .Where(part => part.Contains("\x1b[0$}", StringComparison.Ordinal))
        .Select(part => part[..part.IndexOf("\x1b[0$}", StringComparison.Ordinal)]).ToArray();

    private static void HostWritableUnsupported() {
        using var fixture = new Fixture();
        fixture.Configure(new { Target = "HostWritable" });
        using var terminal = new LinuxTerminal("bash", fixture.Path, "kitty");
        terminal.Send(Quote(binary) + " start --parent-pid $$ --config " + Quote(fixture.Config) + "; printf 'REJECT:%s\\n' $?\n");
        string rejected = terminal.ReadFor(1500);
        Check(rejected.Contains("REJECT:2", StringComparison.Ordinal) && rejected.Contains("not known to support", StringComparison.Ordinal), rejected);
        Check(!rejected.Contains("\x1b[2$~", StringComparison.Ordinal), "Unsupported terminal received activation.");
    }

    private static void HostWritableParentExit() {
        using var fixture = new Fixture();
        fixture.Configure(new { Target = "HostWritable", Left = new[] { "Status" }, Symbol = " __STATUSLINE_READY__ " });
        using var terminal = new LinuxTerminal("bash", fixture.Path, "contour");
        string invocation = Quote(binary) + " init bash --config " + Quote(fixture.Config);
        string started = terminal.Command("eval \"$(" + invocation + ")\"") + terminal.ReadFor(700);
        Check(started.Contains("\x1b[2$~", StringComparison.Ordinal), "Parent exit fixture never activated status area.");
        terminal.Send("exit\n");
        string ended = terminal.ReadFor(1300);
        Check(ended.Contains("\x1b[0$~", StringComparison.Ordinal), "Parent exit did not hide status area: " + ended);
    }

    private sealed class Fixture : IDisposable {
        internal string Path { get; } = Directory.CreateTempSubdirectory("statusline-cli-").FullName;
        internal string Config => System.IO.Path.Combine(Path, "config 'quoted'.json");
        internal List<string> Sessions { get; } = [];
        internal void Configure(object configuration) => File.WriteAllText(Config, JsonSerializer.Serialize(configuration));
        internal void Git() {
            foreach (string[] command in new[] { new[] { "init", "-q", "-b", "topic" }, new[] { "config", "user.name", "Test" }, new[] { "config", "user.email", "test@example.invalid" } }) {
                Check(Run("git", Path, command).Code == 0, "Git fixture initialization failed.");
            }
        }
        public void Dispose() {
            foreach (string session in Sessions) { _ = Cli("stop", "--session", session); }
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }
}
