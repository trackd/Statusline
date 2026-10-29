namespace StatusLine.Blocks.Execution;

/// <summary>Session facts that built-in blocks are created from.</summary>
internal sealed partial record BlockContext {
    public DateTimeOffset Timestamp { get; init; } = TimeProvider.System.GetLocalNow();
    public StatusLineState State { get; init; } = new();

    public string? PowerShellVersion { get; init; }
    public string? ShellIdentity { get; init; }

    public IconSet IconSet { get; init; } = IconSet.NerdFont;

    /// <summary>How long slow providers (git, weather, script definitions) may block before returning cached data.</summary>
    public TimeSpan SlowProviderWait { get; init; }

    public int BlockTimeoutMilliseconds { get; init; } = 2000;
    public bool DurationWarning { get; init; }
    public string? WeatherLocation { get; init; }
}

/// <summary>Caller-supplied replacements for each kind's default layout and colors.</summary>
internal sealed record BlockOverrides(StatusLineAlignment? Alignment, TerminalColor? Foreground, TerminalColor? Background, string Padding);
