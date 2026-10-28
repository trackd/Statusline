using System.Collections.ObjectModel;
using System.Drawing;
using System.Management.Automation;
using Microsoft.PowerShell.Commands;
using StatusLine.Commands;

namespace StatusLine;

/// <summary>Optional selection and appearance for an advanced block snapshot.</summary>
public sealed record BlockSnapshotOptions {
    public string? WeatherLocation { get; init; }
    public object[]? Block { get; init; }
    public object[]? Left { get; init; }
    public object[]? Center { get; init; }
    public object[]? Right { get; init; }
    public StatusLineAlignment? Alignment { get; init; }
    public object? Foreground { get; init; }
    public object? Background { get; init; }
    public object? StartColor { get; init; }
    public object? EndColor { get; init; }
    public string Padding { get; init; } = " ";
    public IconSet IconSet { get; init; } = IconSet.NerdFont;
    public int BlockTimeoutMilliseconds { get; init; } = 2000;
    public int? ExitCode { get; init; }
    public TimeSpan? Duration { get; init; }
    public bool DurationWarning { get; init; }
    public StatusLineState? State { get; init; }
}

/// <summary>Advanced block construction and snapshots; normal setup uses the Enable cmdlets.</summary>
public static class BlockSnapshots {
    public static StatusLineBlock Create(
        string text,
        object? foreground = null,
        object? background = null,
        StatusLineAlignment alignment = StatusLineAlignment.Left,
        string padding = " ",
        bool bold = false,
        bool italic = false,
        bool underline = false,
        string icon = ""
    ) => new(text, TerminalColorValue(foreground), TerminalColorValue(background), alignment, padding, bold, italic, underline, icon);

    public static StatusLineBlock[] Snapshot(BlockSnapshotOptions? options = null) {
        options ??= new();
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BlockTimeoutMilliseconds, 50);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.BlockTimeoutMilliseconds, 60000);
        if (!Enum.IsDefined(options.IconSet) || (options.Alignment is { } alignment && !Enum.IsDefined(alignment))) {
            throw new ArgumentException("Undefined icon set or alignment.", nameof(options));
        }

        Color start = ColorValue(options.StartColor), end = ColorValue(options.EndColor);
        BlockGradient.Validate(start, end);
        var selection = new List<BlockSelector>();
        if (options.Block is not null || (options.Left is null && options.Center is null && options.Right is null)) {
            selection.AddRange(BlockSelector.Parse(options.Block ?? ["Directory", "Git", "PowerShell", "ProcessMemory", "Clock"]));
        }

        Add(options.Left, StatusLineAlignment.Left);
        Add(options.Center, StatusLineAlignment.Center);
        Add(options.Right, StatusLineAlignment.Right);
        // A parameterless SessionState creates a new state, rather than the caller's state.
        // Resolve the foreground execution context while still on the calling runspace thread.
        SessionState sessionState = LanguagePrimitives.ConvertTo<SessionState>(ScriptBlock.Create("$ExecutionContext.SessionState").InvokeReturnAsIs());
        TimeSpan? duration = options.Duration ?? options.State?.LastDuration;
        if (duration is null && selection.Any(selector => (selector.Kind ?? selector.Definition?.Kind) == StatusLineBlockKind.Duration)) {
            Collection<PSObject> history = sessionState.InvokeCommand.InvokeScript("Microsoft.PowerShell.Core\\Get-History -Count 1");
            if (history.Count > 0 && history[0].BaseObject is HistoryInfo entry) {
                duration = entry.Duration;
            }
        }

        var context = new BlockContext {
            State = (options.State ?? StatusLineState.Capture(sessionState, BlockSelector.RequiredFacts([.. selection]))) with { LastExitCode = options.ExitCode ?? options.State?.LastExitCode, LastDuration = duration },
            PowerShellVersion = CommandHelpers.GetPowerShellVersion(sessionState) ?? PSVersionInfo.PSVersion.ToString(),
            IconSet = options.IconSet,
            DurationWarning = options.DurationWarning,
            WeatherLocation = options.WeatherLocation,
            SlowProviderWait = TimeSpan.FromSeconds(1.5),
            BlockTimeoutMilliseconds = options.BlockTimeoutMilliseconds,
        };
        var appearance = new BlockOverrides(options.Alignment,
            options.Foreground is null ? null : TerminalColorValue(options.Foreground),
            options.Background is null ? null : TerminalColorValue(options.Background), options.Padding);
        List<StatusLineBlock> blocks = [.. BlockEvaluator.Create(selection, context, appearance)];
        return [.. BlockGradient.Apply(blocks, start, end, SegmentStyle.Plain)];

        void Add(object[]? names, StatusLineAlignment alignment) {
            if (names is null) {
                return;
            }

            foreach (BlockSelector selector in BlockSelector.Parse(names)) {
                selection.Add(selector with {
                    Alignment = alignment
                });
            }
        }
    }

    internal static TerminalColor TerminalColorValue(object? value) =>
        (value is PSObject wrapped ? wrapped.BaseObject : value) switch {
            null => default,
            Color rgb => new TerminalColor(rgb),
            TerminalColor color => color,
            ConsoleColor palette => new TerminalColor(palette),
            var other => new TerminalColor(ColorValue(other)),
        };

    internal static Color ColorValue(object? value) =>
        (value is PSObject wrapped ? wrapped.BaseObject : value) switch {
            null => Color.Empty,
            Color color => TerminalColor.Validate(color),
            string text => NamedColors.Parse(text),
            _ => throw new ArgumentException("Expected a color name, hex #, or System.Drawing.Color.", nameof(value)),
        };
}
