using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace StatusLine.Cli;

internal static class Program {
    public static int Main(string[] args) {
        try { return Run(args); }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or JsonException
            or OverflowException or FormatException or TimeoutException or OperationCanceledException or InvalidOperationException) {
            Console.Error.WriteLine("statusline: " + exception.Message);
            return 2;
        }
    }

    private static int Run(string[] args) {
        if (args.Length == 0 || args[0] is "--help" or "help") {
            Console.WriteLine("statusline prompt|statusline|transient|command-start [--session TOKEN] [--parent-pid PID] [--config FILE] [--cwd PATH] [--width N] [--status N] [--duration-ms N] [--shell raw|bash|zsh|fish]\nstatusline init bash|zsh|fish [--config FILE] [--status-file FILE] [--install|--uninstall] [--profile FILE]\nstatusline start --parent-pid PID [--session TOKEN] [--config FILE] [--status-file FILE]\nstatusline info|stop --session TOKEN");
            return 0;
        }
        string command = args[0];
        int offset = command == "init" ? 2 : 1;
        if (command is not ("prompt" or "statusline" or "init" or "start" or "stop" or "info" or "worker" or "transient" or "command-start") ||
            args.Length < offset) {
            throw new ArgumentException("Unknown command or missing option value. See --help.");
        }
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = offset; index < args.Length; index++) {
            string option = args[index];
            if (command == "init" && option is "--install" or "--uninstall") {
                if (!values.TryAdd(option, "true")) { throw new ArgumentException($"Repeated option: {option}."); }
                continue;
            }
            bool allowed = command switch {
                "init" => option is "--config" or "--status-file" or "--profile",
                "start" or "worker" => option is "--config" or "--status-file" or "--parent-pid" or "--session",
                "info" or "stop" => option == "--session",
                _ => option is "--config" or "--cwd" or "--width" or "--status" or "--duration-ms" or "--shell" or "--session" or "--parent-pid" or "--status-file" ||
                    (command == "command-start" && option == "--command"),
            };
            if (!allowed || index + 1 >= args.Length || !values.TryAdd(option, args[++index])) {
                throw new ArgumentException($"Unknown, repeated, or incomplete option: {option}.");
            }
        }
        string? configPath = FullPath(values.GetValueOrDefault("--config"));
        string? statusFile = FullPath(values.GetValueOrDefault("--status-file"));
        string? session = values.GetValueOrDefault("--session");
        int parentPid = values.TryGetValue("--parent-pid", out string? owner) ? int.Parse(owner, CultureInfo.InvariantCulture) : 0;
        if (command == "worker") {
            return new SessionWorker(session ?? throw new ArgumentException("Worker requires a session."), parentPid, configPath, statusFile).Run();
        }
        if (command == "init") {
            string shell = args[1];
            if (shell is not ("bash" or "zsh" or "fish")) { throw new ArgumentException("Init requires bash, zsh, or fish."); }
            bool install = values.ContainsKey("--install");
            bool uninstall = values.ContainsKey("--uninstall");
            if (install && uninstall) { throw new ArgumentException("Choose --install or --uninstall."); }
            if (install || uninstall) {
                if (install) { _ = LoadOptions(configPath); }
                string profilePath = FullPath(values.GetValueOrDefault("--profile")) ?? ProfileHooks.DefaultPath(shell);
                bool changed = ProfileHooks.Update(shell, configPath, statusFile, profilePath, install);
                Console.WriteLine((changed ? install ? "Installed hook: " : "Removed hook: " : "Hook already in requested state: ") + profilePath);
                return 0;
            }
            if (values.ContainsKey("--profile")) { throw new ArgumentException("--profile requires --install or --uninstall."); }
            PromptOptions options = LoadOptions(configPath);
            using Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("StatusLine.Shells." + shell)
                ?? throw new IOException("Shell adapter is missing.");
            using var reader = new StreamReader(source);
            string script = reader.ReadToEnd()
                .Replace("__STATUSLINE_EXECUTABLE__", Quote(Environment.ProcessPath ?? throw new IOException("Executable path unavailable."), shell), StringComparison.Ordinal)
                .Replace("__STATUSLINE_CONFIG__", Quote(configPath ?? string.Empty, shell), StringComparison.Ordinal)
                .Replace("__STATUSLINE_STATUS_FILE__", Quote(statusFile ?? string.Empty, shell), StringComparison.Ordinal)
                .Replace("__STATUSLINE_TRANSIENT__", options.Transient ? "1" : "0", StringComparison.Ordinal);
            Console.Write(script);
            return 0;
        }
        if (command == "start") {
            _ = LoadOptions(configPath);
            session ??= Guid.NewGuid().ToString("N");
            SessionProtocol.Start(session, parentPid, configPath, statusFile);
            Console.Write(session);
            return 0;
        }
        if (command is "stop" or "info") {
            SessionResponse reply = SessionProtocol.Send(session ?? throw new ArgumentException("A session token is required."), new() { Command = command == "info" ? "ping" : "stop" });
            if (command == "info") { Console.Write(JsonSerializer.Serialize(reply, ConfigurationJson.Default.SessionResponse)); }
            return 0;
        }
        var request = new SessionRequest {
            Command = command,
            Path = Path.GetFullPath(values.GetValueOrDefault("--cwd") ?? Environment.CurrentDirectory),
            Width = values.TryGetValue("--width", out string? width) ? int.Parse(width, CultureInfo.InvariantCulture) : 120,
            Status = values.TryGetValue("--status", out string? status) ? int.Parse(status, CultureInfo.InvariantCulture) : 0,
            Duration = values.TryGetValue("--duration-ms", out string? duration) ? double.Parse(duration, CultureInfo.InvariantCulture) : null,
            Shell = values.GetValueOrDefault("--shell") ?? "raw",
            EnvironmentKey = SessionProtocol.EnvironmentKey(),
            ConfigPath = configPath, StatusFile = statusFile,
            CommandLine = values.GetValueOrDefault("--command"),
        };
        ValidateRequest(request);
        if (session is not null) {
            SessionResponse response;
            try {
                response = SessionProtocol.Send(session, request);
                if (response.ParentPid != parentPid && parentPid != 0) { throw new ArgumentException("This worker belongs to another shell."); }
                if (response.RestartRequired) {
                    if (parentPid == 0) { throw new ArgumentException("Configuration/environment changed; supply --parent-pid to restart the worker."); }
                    configPath ??= response.ConfigPath;
                    statusFile ??= response.StatusFile;
                    _ = LoadOptions(configPath);
                    using Process oldWorker = Process.GetProcessById(response.WorkerPid);
                    SessionProtocol.Send(session, new() { Command = "stop" });
                    if (!oldWorker.WaitForExit(3000)) { throw new IOException("Previous worker did not exit."); }
                    SessionProtocol.Start(session, parentPid, configPath, statusFile);
                    response = SessionProtocol.Send(session, request);
                }
            }
            catch (Exception exception) when (parentPid > 0 && exception is IOException or TimeoutException) {
                SessionOwner ownerInfo = SessionProtocol.LoadOwner(session, parentPid);
                configPath ??= ownerInfo.ConfigPath;
                statusFile ??= ownerInfo.StatusFile;
                SessionProtocol.Start(session, parentPid, configPath, statusFile);
                response = SessionProtocol.Send(session, request);
            }
            if (response.Error is not null) { throw new IOException(response.Error); }
            Console.Write(response.Output);
            return 0;
        }
        PromptOptions oneShot = LoadOptions(configPath);
        ShellIntegrationProfile profile = ShellIntegrationProfile.Resolve(oneShot.ShellIntegration);
        string output = command switch {
            "command-start" => CommandStart(profile, request.CommandLine),
            "transient" => TerminalText.Sanitize(oneShot.TransientSymbol, false) + Vt.Reset,
            _ => PromptOutput.Render(oneShot, request.Path, request.Width, request.Status, request.Duration, command == "statusline"),
        };
        Console.Write(command == "command-start" ? output : PromptOutput.ForShell(output, request.Shell));
        return 0;
    }

    internal static PromptOptions LoadOptions(string? path) {
        PromptOptions options = path is null ? new() :
            JsonSerializer.Deserialize(File.ReadAllText(path), ConfigurationJson.Default.PromptOptions)
            ?? throw new ArgumentException("Configuration cannot be null.");
        PromptOutput.Validate(options);
        if (options.IdleTimeoutSeconds is < 0 or > 86400) { throw new ArgumentException("IdleTimeoutSeconds must be between 0 and 86400."); }
        if (options.Target is { } target && !Enum.IsDefined(target)) { throw new ArgumentException("Unknown status-line target."); }
        if (options.StatusLine is { } line) { PromptOutput.Validate(line); }
        return options;
    }

    internal static void ValidateRequest(SessionRequest request) {
        if (request.Width is < 2 or > 10000 || request.Shell is not ("raw" or "bash" or "zsh" or "fish")) {
            throw new ArgumentException("Width must be 2–10000; shell must be raw, bash, zsh, or fish.");
        }
        if (request.Duration is { } milliseconds && (!double.IsFinite(milliseconds) || milliseconds < 0 || milliseconds > TimeSpan.MaxValue.TotalMilliseconds)) {
            throw new ArgumentException("Duration must be finite and nonnegative.");
        }
        if (!Path.IsPathFullyQualified(request.Path) || !Directory.Exists(request.Path)) { throw new DirectoryNotFoundException(request.Path); }
    }

    internal static string CommandStart(ShellIntegrationProfile profile, string? command) =>
        (profile.ReflowControl && command is null ? Vt.EnableReflow : string.Empty) + ShellMarkers.Execute(profile, command);

    private static string? FullPath(string? path) => path is null ? null : Path.GetFullPath(path);
    private static string Quote(string value, string shell) => "'" + (shell == "fish"
        ? value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)
        : value.Replace("'", "'\\''", StringComparison.Ordinal)) + "'";
}
