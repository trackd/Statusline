using System.Diagnostics;
using System.Buffers;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StatusLine.Cli;

internal sealed class SessionRequest {
    public string Command { get; set; } = "ping";
    public string Path { get; set; } = Environment.CurrentDirectory;
    public int Width { get; set; } = 120;
    public int Status { get; set; }
    public double? Duration { get; set; }
    public string Shell { get; set; } = "raw";
    public string EnvironmentKey { get; set; } = string.Empty;
    public string? CommandLine { get; set; }
    public string? ConfigPath { get; set; }
    public string? StatusFile { get; set; }
    public ExternalIntegration ExternalIntegration { get; set; }
}

internal sealed class SessionResponse {
    public int WorkerPid { get; set; } = Environment.ProcessId;
    public int ParentPid { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Prelude { get; set; } = string.Empty;
    public string? Error { get; set; }
    public bool RestartRequired { get; set; }
    public string? StatusFile { get; set; }
    public string? StatusFileError { get; set; }
    public string? HostWritableError { get; set; }
    public bool IsIdle { get; set; }
    public double IdleSeconds { get; set; }
    public bool IsCommandExecuting { get; set; }
    public bool RefreshPaused { get; set; }
    public string? ConfigPath { get; set; }
    public string[] ProviderErrors { get; set; } = [];
    public string? ShellInput { get; set; }
    public string? ShellOutput { get; set; }
}

internal sealed class SessionOwner {
    public int ParentPid { get; set; }
    public long ParentStarted { get; set; }
    public string? ConfigPath { get; set; }
    public string? StatusFile { get; set; }
}

internal static class SessionProtocol {
    internal const int MaximumMessage = 65536;
    internal static readonly string[] ShellVariables = ["PATH", "VIRTUAL_ENV", "CONDA_PREFIX", "CONDA_DEFAULT_ENV", "KUBECONFIG", "DOCKER_CONTEXT",
        "HOME", "USERPROFILE", "DOTNET_ROOT", "RUSTUP_TOOLCHAIN", "JAVA_HOME", "PYENV_VERSION", "RBENV_VERSION", "TERM_PROGRAM", "TERM",
        "WT_SESSION", "VSCODE_SHELL_INTEGRATION", "AWS_PROFILE", "AWS_DEFAULT_PROFILE", "AZURE_SUBSCRIPTION_ID"];

    internal static void RegisterOwner(string session, int parentPid, string? config, string? statusFile) {
        using var parent = Process.GetProcessById(parentPid);
        if (parent.HasExited) { throw new IOException("The owning shell has exited."); }
        var owner = new SessionOwner { ParentPid = parentPid, ParentStarted = ParentIdentity(parent), ConfigPath = config, StatusFile = statusFile };
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) { options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; }
        using var file = new FileStream(OwnerFile(session), options);
        JsonSerializer.Serialize(file, owner, ConfigurationJson.Default.SessionOwner);
    }

    private static string OwnerFile(string session) {
        _ = PipeName(session);
        string root;
        if (OperatingSystem.IsWindows()) {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StatusLine", "Sessions");
            Directory.CreateDirectory(root);
        }
        else {
            string user = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName)))[..16];
            root = Path.Combine(Path.GetTempPath(), "statusline-" + user);
            Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if ((File.GetUnixFileMode(root) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0) {
                throw new IOException("Session metadata directory must be private to its owner.");
            }
        }
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) { throw new IOException("Session metadata directory cannot be a link."); }
        return Path.Combine(root, session + ".json");
    }

    internal static SessionOwner LoadOwner(string session, int parentPid) {
        string path = OwnerFile(session);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { throw new IOException("Session metadata cannot be a link."); }
        SessionOwner owner = JsonSerializer.Deserialize(File.ReadAllText(path), ConfigurationJson.Default.SessionOwner)
            ?? throw new IOException("Invalid session metadata.");
        using var parent = Process.GetProcessById(parentPid);
        if (owner.ParentPid != parentPid || owner.ParentStarted != ParentIdentity(parent)) {
            throw new ArgumentException("Session is owned by another shell.");
        }
        return owner;
    }

    internal static void DeleteOwner(string session) => File.Delete(OwnerFile(session));

    private static long ParentIdentity(Process process) {
        if (OperatingSystem.IsLinux()) {
            // Linux Process.StartTime is reconstructed from boot-time estimates and can
            // differ slightly across clients. The kernel's start tick is stable across them.
            string stat = File.ReadAllText("/proc/" + process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/stat");
            string[] fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return long.Parse(fields[19], System.Globalization.CultureInfo.InvariantCulture);
        }
        return process.StartTime.ToUniversalTime().Ticks;
    }

    internal static string PipeName(string session) {
        if (session.Length != 32 || !session.All(char.IsAsciiHexDigit)) {
            throw new ArgumentException("Session must be the token returned by start.");
        }
        return "statusline-" + session;
    }

    internal static string EnvironmentKey() => string.Join('\0',
        ProviderEnvironment.Fingerprint, Environment.GetEnvironmentVariable("TERM_PROGRAM"),
        Environment.GetEnvironmentVariable("TERM"), Environment.GetEnvironmentVariable("WT_SESSION"),
        Environment.GetEnvironmentVariable("VSCODE_SHELL_INTEGRATION"),
        Environment.GetEnvironmentVariable("AWS_PROFILE"), Environment.GetEnvironmentVariable("AWS_DEFAULT_PROFILE"),
        Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID"));

    internal static string ConfigKey(string? config) => config is null ? "" :
        File.GetLastWriteTimeUtc(config).Ticks + ":" + new FileInfo(config).Length;

    internal static async Task<string> ReadLine(StreamReader reader, CancellationToken token) {
        // Bound allocation even if a local client sends a malformed request without a newline.
        var text = new StringBuilder();
        char[] buffer = ArrayPool<char>.Shared.Rent(1024);
        try {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(0, 1024), token).ConfigureAwait(false)) != 0) {
                int newline = buffer.AsSpan(0, count).IndexOf('\n');
                int length = newline < 0 ? count : newline;
                if (text.Length + length > MaximumMessage) { throw new IOException("Session message exceeds 64 KiB."); }
                text.Append(buffer.AsSpan(0, length));
                if (newline >= 0) { return text.ToString(); }
            }
            throw new EndOfStreamException("Session connection closed.");
        }
        finally { ArrayPool<char>.Shared.Return(buffer); }
    }

    internal static SessionResponse Send(string session, SessionRequest request, int timeout = 3000) {
        using var cancellation = new CancellationTokenSource(timeout);
        using var pipe = new NamedPipeClientStream(".", PipeName(session), PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        pipe.Connect(timeout);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);
        string json = JsonSerializer.Serialize(request, ConfigurationJson.Default.SessionRequest);
        if (json.Length > MaximumMessage) {
            throw new ArgumentException("Session request exceeds 64 KiB.");
        }
        writer.WriteLine(json);
        string response = ReadLine(reader, cancellation.Token).GetAwaiter().GetResult();
        return JsonSerializer.Deserialize(response, ConfigurationJson.Default.SessionResponse)
            ?? throw new IOException("Empty worker response.");
    }

    internal static void Start(string session, int parentPid, string? config, string? statusFile, bool shellPipes = false) {
        _ = PipeName(session);
        using var parent = Process.GetProcessById(parentPid);
        if (parent.HasExited) {
            throw new ArgumentException("The owning shell has exited.");
        }
        if (File.Exists(OwnerFile(session))) {
            SessionOwner owner = LoadOwner(session, parentPid);
            config ??= owner.ConfigPath;
            statusFile ??= owner.StatusFile;
            try {
                SessionResponse running = Send(session, new(), 100);
                if (running.ParentPid == parentPid && running.ConfigPath == config && running.StatusFile == statusFile) { return; }
                throw new ArgumentException("Session is owned by another shell.");
            }
            catch (Exception exception) when (exception is TimeoutException or IOException) { }
        }
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new IOException("Executable path unavailable.")) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        // The worker must not inherit the launcher's caller-facing capture pipes.
        // On Windows, whitelist only its own redirected standard handles.
        if (OperatingSystem.IsWindows()) { start.InheritedHandles = []; }
        foreach (string argument in new[] { shellPipes ? "worker-shell" : "worker", "--session", session, "--parent-pid", parentPid.ToString(System.Globalization.CultureInfo.InvariantCulture) }) {
            start.ArgumentList.Add(argument);
        }
        if (config is not null) {
            start.ArgumentList.Add("--config"); start.ArgumentList.Add(config);
        }
        if (statusFile is not null) {
            start.ArgumentList.Add("--status-file"); start.ArgumentList.Add(statusFile);
        }
        using Process worker = Process.Start(start) ?? throw new IOException("Worker failed to start.");
        using var startup = new StartupCleanup(worker);
        worker.StandardInput.Close();
        // Drain both streams while waiting for readiness. They are never inherited by the
        // command substitution that launches us, so the shell cannot wait on worker EOF.
        _ = worker.StandardOutput.ReadToEndAsync();
        Task<string> errors = worker.StandardError.ReadToEndAsync();
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed.TotalSeconds < 5) {
            if (worker.HasExited) {
                throw new IOException("Worker exited during startup: " + errors.GetAwaiter().GetResult());
            }
            try {
                SessionResponse response = Send(session, new(), 100);
                if (response.WorkerPid != worker.Id || response.ParentPid != parentPid) {
                    throw new ArgumentException("Session token is already owned by another worker.");
                }
                var owner = new SessionOwner { ParentPid = parentPid, ParentStarted = ParentIdentity(parent), ConfigPath = config, StatusFile = statusFile };
                string ownerPath = OwnerFile(session);
                var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
                if (!OperatingSystem.IsWindows()) { fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; }
                // Replacing a same-user session's metadata uses an owner-validated caller.
                File.Delete(ownerPath);
                using (var file = new FileStream(ownerPath, fileOptions)) {
                    JsonSerializer.Serialize(file, owner, ConfigurationJson.Default.SessionOwner);
                }
                startup.Ready = true;
                return;
            }
            catch (Exception exception) when (exception is TimeoutException or IOException) {
                Thread.Sleep(20);
            }
        }
        throw new IOException("Worker did not become ready within five seconds.");
    }

    private sealed class StartupCleanup : IDisposable {
        private readonly Process process;
        internal bool Ready { get; set; }
        internal StartupCleanup(Process process) { this.process = process; }
        public void Dispose() {
            if (Ready) { return; }
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); } }
            catch (InvalidOperationException) { /* It exited between the check and termination. */ }
        }
    }
}
