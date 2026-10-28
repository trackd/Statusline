using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace StatusLine.Cli;

internal sealed class SessionWorker {
    private readonly string session;
    private readonly int parentPid;
    private readonly string? config;
    private readonly string? statusFile;
    private readonly Lock gate = new();
    private readonly PromptOptions options;
    private readonly PromptOptions lineOptions;
    private readonly string environmentKey;
    private readonly string configKey;
    private readonly Refreshing.RefreshActivity activity;
    private SessionRequest? snapshot;
    private bool executing;
    private string? fileError;
    private string? terminalError;
    private HostWritableOutput? hostOutput;

    internal SessionWorker(string session, int parentPid, string? config, string? statusFile) {
        this.session = session;
        this.parentPid = parentPid;
        this.config = config;
        this.statusFile = statusFile;
        options = Program.LoadOptions(config);
        activity = new Refreshing.RefreshActivity(TimeSpan.FromSeconds(options.IdleTimeoutSeconds));
        lineOptions = options.StatusLine ?? new PromptOptions {
            Left = ["Git"], Center = ["Uptime"], Right = ["Cpu", "SystemMemory", "Clock"],
            StartColor = options.StartColor, EndColor = options.EndColor, IconSet = options.IconSet,
            Style = options.Style, BlockSeparator = options.BlockSeparator, Padding = options.Padding,
            BlockTimeoutMilliseconds = options.BlockTimeoutMilliseconds, WeatherLocation = options.WeatherLocation,
        };
        environmentKey = SessionProtocol.EnvironmentKey();
        configKey = SessionProtocol.ConfigKey(config);
    }

    internal int Run() {
        using var parent = Process.GetProcessById(parentPid);
        using var terminal = options.Target == StatusLineTarget.HostWritable ? new HostWritableOutput() : null;
        hostOutput = terminal;
        if (options.Target == StatusLineTarget.File && statusFile is null) {
            throw new ArgumentException("Target File requires --status-file.");
        }
        using var lifetime = new CancellationTokenSource();
        using PosixSignalRegistration? terminate = OperatingSystem.IsWindows() ? null :
            PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; lifetime.Cancel(); });
        using PosixSignalRegistration? hangup = OperatingSystem.IsWindows() ? null :
            PosixSignalRegistration.Create(PosixSignal.SIGHUP, context => { context.Cancel = true; lifetime.Cancel(); });
        Task ticking = Tick(parent, lifetime);
        try {
            while (!lifetime.IsCancellationRequested) {
                using var pipe = new NamedPipeServerStream(SessionProtocol.PipeName(session), PipeDirection.InOut,
                    1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                pipe.WaitForConnectionAsync(lifetime.Token).GetAwaiter().GetResult();
                using var clientTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                clientTimeout.CancelAfter(3000);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                try {
                    string line = SessionProtocol.ReadLine(reader, clientTimeout.Token).GetAwaiter().GetResult();
                    SessionRequest request = JsonSerializer.Deserialize(line, ConfigurationJson.Default.SessionRequest)
                        ?? throw new ArgumentException("Empty request.");
                    SessionResponse response;
                    lock (gate) {
                        response = Handle(request);
                    }
                    writer.WriteLine(JsonSerializer.Serialize(response, ConfigurationJson.Default.SessionResponse));
                    if (request.Command == "stop") {
                        break;
                    }
                }
                catch (Exception exception) when (exception is ArgumentException or IOException or JsonException or OperationCanceledException) {
                    if (pipe.IsConnected && !lifetime.IsCancellationRequested) {
                        try {
                            writer.WriteLine(JsonSerializer.Serialize(new SessionResponse { ParentPid = parentPid, Error = exception.Message }, ConfigurationJson.Default.SessionResponse));
                        }
                        catch (IOException) { }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        finally {
            lifetime.Cancel();
            ticking.GetAwaiter().GetResult();
            SessionProtocol.DeleteOwner(session);
        }
        return 0;
    }

    private SessionResponse Handle(SessionRequest request) {
        var response = new SessionResponse { ParentPid = parentPid, ConfigPath = config, StatusFile = statusFile, StatusFileError = fileError,
            HostWritableError = terminalError,
            IsIdle = activity.CheckIdle(), IdleSeconds = activity.InactiveFor.TotalSeconds,
            IsCommandExecuting = executing, RefreshPaused = activity.IsIdle || (executing && !options.UpdateDuringCommand),
            ProviderErrors = [.. BuiltInRefresh.Diagnostics().Where(entry => entry.IsStale).Select(entry => entry.Name + ": " + entry.LastError)] };
        if (request.Command is "ping" or "stop") {
            return response;
        }
        if (request.EnvironmentKey != environmentKey || SessionProtocol.ConfigKey(config) != configKey ||
            (request.ConfigPath is not null && request.ConfigPath != config) ||
            (request.StatusFile is not null && request.StatusFile != statusFile)) {
            response.RestartRequired = true;
            return response;
        }
        Program.ValidateRequest(request);
        ShellIntegrationProfile profile = ShellIntegrationProfile.Resolve(options.ShellIntegration);
        if (request.Command == "command-start") {
            activity.Touch();
            executing = true;
            response.Output = Program.CommandStart(profile, request.CommandLine);
            return response;
        }
        if (request.Command == "transient") {
            response.Output = PromptOutput.ForShell(TerminalText.Sanitize(options.TransientSymbol, false) + Vt.Reset, request.Shell);
            return response;
        }
        if (request.Command is not ("prompt" or "statusline")) {
            throw new ArgumentException("Unknown worker command.");
        }
        snapshot = request;
        if (request.Command == "prompt") { activity.Touch(); }
        string text = PromptOutput.Render(options, request.Path, request.Width, request.Status, request.Duration,
            request.Command == "statusline", wait: false);
        if (request.Command == "prompt") {
            text = ShellMarkers.Begin(profile, executing ? request.Status : null, request.Path) + text + profile.CommandStart;
            executing = false;
        }
        response.Output = PromptOutput.ForShell(text, request.Shell);
        return response;
    }

    private async Task Tick(Process parent, CancellationTokenSource lifetime) {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try {
            while (await timer.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false)) {
                if (parent.HasExited) {
                    lifetime.Cancel();
                    break;
                }
                lock (gate) {
                    if (activity.CheckIdle() || (executing && !options.UpdateDuringCommand)) { continue; }
                    if (snapshot is not { } current) {
                        continue;
                    }
                    // Drive the same asynchronous caches even while the shell is idle.
                    _ = PromptOutput.Render(options, current.Path, current.Width, current.Status, current.Duration, false, wait: false);
                    if (statusFile is not null) {
                        string text = PromptOutput.Render(lineOptions, current.Path, current.Width, current.Status, current.Duration, true, wait: false);
                        StatusFile.TryWrite(statusFile, text);
                        fileError = StatusFile.LastError;
                    }
                    if (hostOutput is not null && terminalError is null) {
                        try {
                            int width = hostOutput.Width(current.Width);
                            string text = PromptOutput.Render(lineOptions, current.Path, width, current.Status, current.Duration, true, wait: false);
                            hostOutput.Write(text, width);
                        }
                        catch (IOException exception) { terminalError = exception.Message; }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException) {
            fileError = exception.Message;
            lifetime.Cancel();
        }
    }
}
