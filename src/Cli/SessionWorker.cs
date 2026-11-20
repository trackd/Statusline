using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;

namespace StatusLine.Cli;

internal sealed class SessionWorker : IDisposable {
    private readonly string session;
    private readonly int parentPid;
    private readonly string? config;
    private readonly string? statusFile;
    private readonly Lock gate = new();
    private readonly PromptOptions options;
    private readonly PromptOptions lineOptions;
    private readonly PreparedPrompt prompt;
    private readonly PreparedPrompt statusLine;
    private readonly PreparedPrompt transientPrompt;
    private string? runningCommand;
    private DateTimeOffset? commandStarted;
    private int jobCount;
    private long generation;
    private readonly string environmentKey;
    private readonly string configKey;
    private readonly Refreshing.RefreshActivity activity;
    private SessionRequest? snapshot;
    private bool executing;
    private string? fileError;
    private string? terminalError;
    private HostWritableOutput? hostOutput;
    private readonly bool shellPipes;
    private string? shellInput;
    private string? shellOutput;
    private SessionRequest? editorSnapshot;
    private string? previousEditorFrame;
    private long previousEditorRevision = -1;
    private StreamWriter? shellWriter;
    private readonly SemaphoreSlim shellWriteGate = new(1, 1);

    internal SessionWorker(string session, int parentPid, string? config, string? statusFile, bool shellPipes = false) {
        if (shellPipes && !OperatingSystem.IsLinux()) { throw new ArgumentException("Shell pipe channels currently require Linux."); }

        this.session = session;
        this.parentPid = parentPid;
        this.config = config;
        this.statusFile = statusFile;
        this.shellPipes = shellPipes;
        options = Program.LoadOptions(config);
        activity = new Refreshing.RefreshActivity(TimeSpan.FromSeconds(options.IdleTimeoutSeconds));
        lineOptions = options.StatusLine ?? new PromptOptions {
            Left = ["Git"],
            Center = ["Uptime"],
            Right = ["Cpu", "SystemMemory", "Clock"],
            StartColor = options.StartColor,
            EndColor = options.EndColor,
            IconSet = options.IconSet,
            Style = options.Style,
            BlockSeparator = options.BlockSeparator,
            Padding = options.Padding,
            BlendBackground = options.BlendBackground,
            TextContrast = options.TextContrast,
            BlockTimeoutMilliseconds = options.BlockTimeoutMilliseconds,
            WeatherLocation = options.WeatherLocation,
        };
        environmentKey = SessionProtocol.EnvironmentKey();
        configKey = SessionProtocol.ConfigKey(config);
        prompt = new(options);
        statusLine = new(lineOptions);
        transientPrompt = new(options.TransientView());
    }

    internal int Run(bool standardInput = false) {
        // The worker may share the shell's process group during interactive editing.
        // Ctrl+C cancels input or the foreground command, not this session service.
        using PosixSignalRegistration? interrupt = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGINT, static context => context.Cancel = true);
        using PosixSignalRegistration? quit = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGQUIT, static context => context.Cancel = true);
        using var parent = Process.GetProcessById(parentPid);
        using HostWritableOutput? terminal = options.Target == StatusLineTarget.HostWritable ? new HostWritableOutput() : null;
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
        using var pipe = new NamedPipeServerStream(SessionProtocol.PipeName(session), PipeDirection.InOut,
            1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using AnonymousPipeServerStream? requests = shellPipes ? new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None) : null;
        using AnonymousPipeServerStream? responses = shellPipes ? new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None) : null;
        if (requests is not null && responses is not null) {
            // Keep both client handles alive: opening these anonymous pipes never
            // waits for a FIFO peer. A dead worker removes the paths and yields EOF.
            shellInput = $"/proc/{Environment.ProcessId}/fd/{requests.ClientSafePipeHandle.DangerousGetHandle()}";
            shellOutput = $"/proc/{Environment.ProcessId}/fd/{responses.ClientSafePipeHandle.DangerousGetHandle()}";
        }

        Task input = requests is not null && responses is not null ? ReadShell(lifetime, requests, responses, announce: false) :
            standardInput ? ReadShell(lifetime, Console.OpenStandardInput(), Console.OpenStandardOutput(), announce: true) : Task.CompletedTask;
        try {
            while (!lifetime.IsCancellationRequested) {
                // Keep the listener alive while replacing accepted connections.
                if (pipe.IsConnected) { pipe.Disconnect(); }

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
            // Console stdin can be a blocking pipe whose read cannot be cancelled.
            // It is process-owned; do not delay terminal restoration waiting for EOF
            // when a control client or the parent monitor stops the worker.
            if (input.IsCompleted) { input.GetAwaiter().GetResult(); }

            SessionProtocol.DeleteOwner(session);
        }

        return 0;
    }

    private async Task ReadShell(CancellationTokenSource lifetime, Stream source, Stream destination, bool announce) {
        // NUL framing preserves multiline paths and output. Only the inherited
        // private pipes carry requests; terminal input is never consumed here.
        using var reader = new ShellFieldReader(source, SessionProtocol.MaximumMessage);
        using var writer = new StreamWriter(destination, leaveOpen: true) { AutoFlush = true };
        try {
            if (announce) { await writer.WriteAsync(session + "\0").ConfigureAwait(false); }
            shellWriter = writer;

            string[] expectedEnvironment = [.. SessionProtocol.ShellVariables.Select(name => Environment.GetEnvironmentVariable(name) ?? "")];
            while (!lifetime.IsCancellationRequested) {
                string command = await reader.ReadStringAsync(lifetime.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                timeout.CancelAfter(3000);
                var request = new SessionRequest {
                    Command = command,
                    Path = await reader.ReadStringAsync(timeout.Token).ConfigureAwait(false),
                    Width = int.Parse((await reader.ReadAsync(timeout.Token).ConfigureAwait(false)).Span, System.Globalization.CultureInfo.InvariantCulture),
                    Status = int.Parse((await reader.ReadAsync(timeout.Token).ConfigureAwait(false)).Span, System.Globalization.CultureInfo.InvariantCulture),
                };
                ReadOnlyMemory<char> duration = await reader.ReadAsync(timeout.Token).ConfigureAwait(false);
                request.Duration = duration.Length == 0 ? null : double.Parse(duration.Span, System.Globalization.CultureInfo.InvariantCulture);
                request.Shell = await reader.ReadStringAsync(timeout.Token).ConfigureAwait(false);
                request.CommandLine = await reader.ReadStringAsync(timeout.Token).ConfigureAwait(false);
                if (request.CommandLine.Length == 0) { request.CommandLine = null; }

                request.ExternalIntegration = (ExternalIntegration)int.Parse((await reader.ReadAsync(timeout.Token).ConfigureAwait(false)).Span, System.Globalization.CultureInfo.InvariantCulture);
                if (command == "edit") {
                    request.InputCursor = int.Parse((await reader.ReadAsync(timeout.Token).ConfigureAwait(false)).Span, System.Globalization.CultureInfo.InvariantCulture);
                    request.InputRevision = long.Parse((await reader.ReadAsync(timeout.Token).ConfigureAwait(false)).Span, System.Globalization.CultureInfo.InvariantCulture);
                    request.InputMode = await reader.ReadStringAsync(timeout.Token).ConfigureAwait(false);
                    request.JobCount = int.Parse((await reader.ReadAsync(timeout.Token).ConfigureAwait(false)).Span, System.Globalization.CultureInfo.InvariantCulture);
                    request.InputCommand = await reader.ReadStringAsync(timeout.Token).ConfigureAwait(false);
                }
                bool environmentChanged = false;
                foreach (string expected in expectedEnvironment) {
                    environmentChanged |= !await reader.MatchesAsync(expected, timeout.Token).ConfigureAwait(false);
                }

                request.EnvironmentKey = environmentKey;
                SessionResponse response;
                lock (gate) {
                    response = environmentChanged ? new SessionResponse { RestartRequired = true } : Handle(request, cachedPrompt: true);
                }

                await WriteShellResponse(response, timeout.Token).ConfigureAwait(false);
                if (response.RestartRequired || command == "stop") { break; }
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ArgumentException or FormatException or OverflowException) {
            if (!lifetime.IsCancellationRequested && exception is not EndOfStreamException) { Console.Error.WriteLine("statusline: " + exception.Message); }
        }
        finally { shellWriter = null; lifetime.Cancel(); }
    }

    private async Task WriteShellResponse(SessionResponse response, CancellationToken token) {
        await shellWriteGate.WaitAsync(token).ConfigureAwait(false);
        try {
            if (shellWriter is not { } writer) { return; }
            string result = response.RestartRequired ? "restart" : response.Error is not null ? "error" : response.InputRevision is not null ? "update" : "ok";
            string prefix = response.InputRevision?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? response.Prelude;
            await writer.WriteAsync((result + "\0" + prefix + "\0" + (response.Error ?? response.Output) + "\0").AsMemory(), token).ConfigureAwait(false);
        }
        finally { shellWriteGate.Release(); }
    }

    private static StatusLineState EditorState(SessionRequest request) {
        string line = request.CommandLine ?? string.Empty;
        int cursor = 0, characters = 0;
        foreach (var rune in line.EnumerateRunes()) {
            if (characters++ >= request.InputCursor) { break; }
            cursor += rune.Utf16SequenceLength;
        }
        return new() { InputLine = line, InputCursor = cursor, InputCommand = request.InputCommand,
            ViMode = request.InputMode, RunningJobCount = request.JobCount };
    }

    private string EditorOutput(string text, SessionRequest request) {
        var profile = ShellIntegrationProfile.ResolveForShell(options.ShellIntegration, request.ExternalIntegration);
        if (profile.Terminal == "ghostty" && profile.Protocol == ShellIntegration.Osc133) {
            text = text.Replace("\n", "\n" + Vt.SecondaryPrompt, StringComparison.Ordinal);
            text = (profile.IsExternal(ExternalIntegration.PromptStart) ? "" : Vt.InputPrompt) + text + profile.CommandStart;
        }
        return PromptOutput.ForShell(text, request.Shell);
    }

    private SessionResponse Handle(SessionRequest request, bool cachedPrompt = false) {
        var response = new SessionResponse {
            ParentPid = parentPid,
            ConfigPath = config,
            StatusFile = statusFile,
            StatusFileError = fileError,
            ShellInput = shellInput,
            ShellOutput = shellOutput,
            HostWritableError = terminalError,
            IsIdle = activity.CheckIdle(),
            IdleSeconds = activity.InactiveFor.TotalSeconds,
            IsCommandExecuting = executing,
            RefreshPaused = activity.IsIdle || (executing && !options.UpdateDuringCommand),
        };
        if (request.Command is "ping" or "stop") {
            response.ProviderErrors = [.. BuiltInRefresh.Diagnostics().Where(entry => entry.IsStale).Select(entry => entry.Name + ": " + entry.LastError),
                .. prompt.Errors, .. statusLine.Errors, .. transientPrompt.Errors];
            return response;
        }

        if (request.EnvironmentKey != environmentKey || SessionProtocol.ConfigKey(config) != configKey ||
            (request.ConfigPath is not null && request.ConfigPath != config) ||
            (request.StatusFile is not null && request.StatusFile != statusFile)) {
            response.RestartRequired = true;
            return response;
        }

        Program.ValidateRequest(request);
        if (request.Command == "edit") {
            if (!cachedPrompt || request.Shell != "zsh" || request.InputRevision < 0 || request.InputCursor < 0 || request.InputCursor > (request.CommandLine?.Length ?? 0)) {
                throw new ArgumentException("Invalid editor snapshot or unsupported transport.");
            }
            editorSnapshot = request;
            generation++;
            jobCount = request.JobCount;
            if (snapshot is not null) { snapshot.Width = request.Width; }
            activity.Touch();
            string rendered = prompt.Render(request.Path, request.Width, request.Status, request.Duration, false, wait: false, editor: EditorState(request));
            previousEditorFrame = rendered;
            previousEditorRevision = request.InputRevision;
            response.InputRevision = request.InputRevision;
            response.Output = EditorOutput(rendered, request);
            return response;
        }
        if (request.Command is "prompt" or "command-start" or "transient") { editorSnapshot = null; }
        var profile = ShellIntegrationProfile.ResolveForShell(options.ShellIntegration, request.ExternalIntegration);
        if (request.Command == "command-start") {
            generation++;
            activity.Touch();
            executing = true;
            runningCommand = request.CommandLine;
            commandStarted = DateTimeOffset.UtcNow;
            response.Output = Program.CommandStart(profile, request.CommandLine);
            return response;
        }

        if (request.Command == "transient") {
            SessionRequest current = snapshot ?? request;
            response.Output = PromptOutput.ForShell(transientPrompt.Render(current.Path, request.Width, current.Status, current.Duration, false, wait: false), request.Shell);
            return response;
        }

        if (request.Command is not ("prompt" or "statusline")) {
            throw new ArgumentException("Unknown worker command.");
        }

        if (request.Command == "statusline") {
            SessionRequest current = snapshot ?? request;
            response.Output = PromptOutput.ForShell(statusLine.Render(current.Path, request.Width,
                current.Status, current.Duration, true, wait: false,
                editor: editorSnapshot is { } statusEdit ? EditorState(statusEdit) : null), request.Shell);
            return response;
        }

        snapshot = request;
        generation++;
        activity.Touch();
        string text = prompt.Render(request.Path, request.Width, request.Status, request.Duration, false, wait: false);
        if (request.Command == "prompt") {
            string begin = ShellMarkers.Begin(profile, executing ? request.Status : null, request.Path);
            if (cachedPrompt && (request.Shell is "bash" or "zsh") && profile.Terminal == "ghostty" && profile.Protocol == ShellIntegration.Osc133) {
                // A requests a fresh terminal line. Emit it once from precmd;
                // cached PS1 redraws use P and keep each continuation in the group.
                text = text.Replace("\n", "\n" + Vt.SecondaryPrompt, StringComparison.Ordinal);
                if (!profile.IsExternal(ExternalIntegration.PromptStart)) {
                    response.Prelude = begin;
                    begin = Vt.InputPrompt;
                }
            }

            text = begin + text + profile.CommandStart;
            if (executing) { hostOutput?.Invalidate(); }

            executing = false;
            runningCommand = null;
            commandStarted = null;
        }

        response.Output = PromptOutput.ForShell(text, request.Shell);
        return response;
    }

    private async Task Tick(Process parent, CancellationTokenSource lifetime) {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        string? previousFileFrame = null;
        try {
            while (await timer.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false)) {
                if (parent.HasExited) {
                    lifetime.Cancel();
                    break;
                }

                string? fileFrame = null;
                SessionResponse? editorUpdate = null;
                long frameGeneration;
                lock (gate) {
                    if (snapshot is { } observed && hostOutput is not null && terminalError is null) {
                        int width = hostOutput.Width(observed.Width);
                        if (width != observed.Width) {
                            observed.Width = width;
                            hostOutput.Invalidate();
                            activity.Touch();
                        }
                    }
                    if (activity.CheckIdle() || (executing && !options.UpdateDuringCommand)) { continue; }

                    if (snapshot is not { } current) {
                        continue;
                    }
                    StatusLineState statusState = (editorSnapshot is { } stateEdit ? EditorState(stateEdit) : new()) with {
                        RunningCommand = runningCommand, CommandStartedAt = commandStarted, RunningJobCount = jobCount,
                    };
                    // Warm slow providers without allocating and discarding a prompt frame.
                    prompt.Refresh(current.Path, current.Status, current.Duration);
                    if (editorSnapshot is { } edit) {
                        string rendered = prompt.Render(edit.Path, edit.Width, edit.Status, edit.Duration, false, wait: false, editor: EditorState(edit));
                        if (rendered != previousEditorFrame || edit.InputRevision != previousEditorRevision) {
                            previousEditorFrame = rendered;
                            previousEditorRevision = edit.InputRevision;
                            editorUpdate = new() { InputRevision = edit.InputRevision, Output = EditorOutput(rendered, edit) };
                        }
                    }
                    frameGeneration = generation;
                    if (statusFile is not null) {
                        fileFrame = statusLine.Render(current.Path, current.Width, current.Status, current.Duration, true, wait: false,
                            editor: statusState);
                    }

                    if (hostOutput is not null && terminalError is null) {
                        try {
                            int width = hostOutput.Width(current.Width);
                            string text = fileFrame is not null && width == current.Width ? fileFrame :
                                statusLine.Render(current.Path, width, current.Status, current.Duration, true, wait: false,
                                    editor: statusState);
                            // Keep the atomic terminal write ordered with command-start and
                            // prompt restoration; only filesystem preparation happens outside gate.
                            hostOutput.Write(text, width);
                        }
                        catch (IOException exception) { terminalError = exception.Message; }
                    }
                }

                if (editorUpdate is not null) { await WriteShellResponse(editorUpdate, lifetime.Token).ConfigureAwait(false); }

                if (statusFile is not null && fileFrame is not null &&
                    (fileFrame != previousFileFrame || fileError is not null || !File.Exists(statusFile))) {
                    bool written = StatusFile.TryWriteIfCurrent(statusFile, fileFrame, (temporary, path) => {
                        lock (gate) {
                            if (lifetime.IsCancellationRequested || generation != frameGeneration || activity.CheckIdle() ||
                                (executing && !options.UpdateDuringCommand)) { return false; }

                            File.Move(temporary, path, overwrite: true);
                            return true;
                        }
                    });
                    lock (gate) { fileError = StatusFile.LastError; }

                    if (written) { previousFileFrame = fileFrame; }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException) {
            fileError = exception.Message;
            lifetime.Cancel();
        }
    }

    public void Dispose() {
        prompt.Dispose();
        statusLine.Dispose();
        transientPrompt.Dispose();
        shellWriteGate.Dispose();
    }
}
