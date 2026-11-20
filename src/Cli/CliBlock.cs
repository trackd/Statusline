using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using StatusLine.Refreshing;

namespace StatusLine.Cli;

internal sealed class CliBlock : IDisposable {
    private static readonly string[] SshVariables = ["SSH_CONNECTION", "SSH_CLIENT", "SSH_TTY"];
    private readonly BlockSettings settings;
    private readonly StatusLineAlignment alignment;
    private readonly RefreshValue<string>? output;
    internal IBuiltInBlock? Implementation { get; }
    private BlockContent? previous;
    private string? previousError;
    private StatusLineBlock? block;
    private bool unicode;
    internal string? Error { get; private set; }
    internal string Label => settings.Name ?? settings.Kind ?? settings.Executable!;

    internal CliBlock(BlockSettings settings, StatusLineAlignment alignment) {
        Validate(settings);
        this.settings = settings;
        this.alignment = alignment;
        if (settings.Kind is { } kind) { Implementation = BlockCatalog.Get(PromptOutput.Kind(kind)); }
        else { output = new(); }
    }

    internal static void Validate(BlockSettings settings) {
        if ((settings.Kind is null) == (settings.Executable is null)) { throw new ArgumentException("Specify exactly one of Kind and Executable for a block."); }
        if (settings.Kind is { } kind) { _ = PromptOutput.Kind(kind); }
        if (settings.Executable is { Length: 0 } || settings.Arguments is null || settings.Commands is null || settings.DetectFiles is null ||
            settings.Commands.Any(string.IsNullOrWhiteSpace) || settings.Arguments.Any(argument => argument is null) ||
            settings.DetectFiles.Any(name => string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name || name is "." or "..")) {
            throw new ArgumentException("Invalid executable, arguments, commands or project file names.");
        }
        if (settings.MinimumWidth < 0 || settings.MaximumWidth < 0 || settings.MinimumDurationMilliseconds < 0 ||
            settings.IntervalMilliseconds is < 250 or > 86400000 || settings.TimeoutMilliseconds is < 50 or > 60000) {
            throw new ArgumentException("Invalid block width, duration, interval or timeout.");
        }
        if (settings.Foreground is { } foreground) { _ = NamedColors.Parse(foreground); }
        if (settings.Background is { } background) { _ = NamedColors.Parse(background); }
    }

    internal static IEnumerable<CliBlock> Hints(BlockSpec[] specs) {
        var kinds = new HashSet<StatusLineBlockKind>();
        foreach (BlockSpec spec in specs) {
            if (spec is null) { throw new ArgumentException("Hint lists cannot contain null."); }
            BlockSettings settings = spec.Settings;
            if (settings.Commands is null) { throw new ArgumentException("Hint Commands cannot be null."); }
            if (settings.Kind is { } preset && settings.Commands.Length == 0) {
                var hint = HintCatalog.Resolve(preset);
                if (!kinds.Add(hint.Kind)) { continue; }
                settings = settings with { Kind = hint.Kind.ToString(), Commands = hint.Commands };
            }
            if (settings.Commands.Length == 0) { throw new ArgumentException("Custom hints require Commands."); }
            yield return new(settings, StatusLineAlignment.Right);
        }
    }

    internal StatusLineBlock? Evaluate(BlockContext context, string padding, bool wait) {
        int width = context.Width ?? 120;
        if (width < settings.MinimumWidth || settings.MaximumWidth > 0 && width > settings.MaximumWidth ||
            settings.MinimumDurationMilliseconds > 0 && !(context.State.LastDuration?.TotalMilliseconds >= settings.MinimumDurationMilliseconds) ||
            settings.Commands.Length > 0 && !settings.Commands.Contains(context.State.InputCommand, StringComparer.OrdinalIgnoreCase) ||
            settings.SshOnly && !SshVariables.Any(name => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))) ||
            settings.DetectFiles.Length > 0 && !(context.State.FileSystemPath is { } path && settings.DetectFiles.Any(name => File.Exists(Path.Combine(path, name))))) {
            return null;
        }
        string? error;
        BlockContent? content;
        if (Implementation is { } implementation) {
            if (settings.TimeoutMilliseconds is { } timeout) { context = context with { BlockTimeoutMilliseconds = timeout }; }
            content = BuiltInRefresh.Evaluate(implementation, context, out error);
        }
        else {
            var sample = output!.Read(context.State, ReadAsync, TimeSpan.FromMilliseconds(settings.IntervalMilliseconds),
                TimeSpan.FromMilliseconds(settings.TimeoutMilliseconds ?? context.BlockTimeoutMilliseconds), wait ? context.SlowProviderWait : TimeSpan.Zero,
                scope: context.State);
            error = sample.Error;
            content = sample.HasValue ? new BlockContent("", sample.Value!, alignment, Color.Gray) : null;
        }
        Error = error;
        if (error is not null && wait) { Console.Error.WriteLine($"statusline: {settings.Name ?? settings.Kind ?? settings.Executable}: {error}"); }
        if (content is not { } value || value.Text.Length == 0 && value.Icon.Length == 0) { return null; }
        if (block is not null && previous == content && previousError == error && unicode == TextMeasurement.UseUnicode) { return block; }
        var next = new StatusLineBlock(value.Text,
            settings.Foreground is { } foreground ? NamedColors.Parse(foreground) : value.Accent,
            settings.Background is { } background ? NamedColors.Parse(background) : Color.Empty,
            alignment, settings.Padding ?? padding, false, false, false, settings.Icon ?? value.Icon, value.IconSeparator ?? " ") {
            Priority = settings.Priority,
            Shrinkable = Implementation?.Metadata.Kind == StatusLineBlockKind.Directory,
            PreserveForeground = settings.Foreground is not null,
            PreserveBackground = settings.Background is not null,
        };
        previous = content;
        previousError = error;
        unicode = TextMeasurement.UseUnicode;
        return block = error is null ? next : next.AsStale(error);
    }

    private async Task<string> ReadAsync(StatusLineState state, CancellationToken token) {
        var start = new ProcessStartInfo(settings.Executable!) { WorkingDirectory = state.FileSystemPath ?? Environment.CurrentDirectory };
        foreach (string argument in settings.Arguments) { start.ArgumentList.Add(argument); }
        var result = await ProcessRunner.RunAsync(start, JsonSerializer.Serialize(state, BlockConfigurationJson.Default.StatusLineState), token).ConfigureAwait(false);
        if (result.ExitCode != 0) { throw new IOException($"Custom block exited with code {result.ExitCode}: {result.Error.Trim()}"); }
        return result.Output.TrimEnd('\r', '\n');
    }

    public void Dispose() => output?.Dispose();
}
