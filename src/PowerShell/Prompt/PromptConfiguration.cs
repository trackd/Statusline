namespace StatusLine;

/// <summary>Reusable prompt settings; kind names resolve through the shared block registry.</summary>
public sealed record PromptConfiguration {
    public object[] Left { get; init; } = ["Directory", "Git", "Status", "Duration"];
    public object[] Right { get; init; } = [];
    public object[] Block { get; init; } = [];
    public object[] Input { get; init; } = [];
    public TextContrastMode TextContrast { get; init; } = TextContrastMode.Automatic;
    public SegmentStyle Style { get; init; } = SegmentStyle.Powerline;
    public IconSet IconSet { get; init; } = IconSet.NerdFont;
    public ShellIntegration ShellIntegration { get; init; } = ShellIntegration.Auto;
    /// <summary>Report command execution using OSC 9;4 indeterminate progress.</summary>
    public bool TerminalProgress { get; init; }
    public string Symbol { get; init; } = " ";
    public bool ConnectRows { get; init; }
    public string ContextSymbol { get; init; } = "╭─ ";
    public string InputLinePrefix { get; init; } = "╰─ ";
    public object[] TransientBlock { get; init; } = [];
    public string TransientSymbol { get; init; } = "  ";
    /// <summary>Transparent Powerline breaks between blocks.</summary>
    public bool BlockSeparator { get; init; }
    public bool DurationWarning { get; init; }
    /// <summary>Text inside both edges of each block.</summary>
    public string Padding { get; init; } = " ";
    public bool Transient { get; init; }
    public int Rows { get; init; } = 1;
    public int BlockTimeoutMilliseconds { get; init; } = 2000;
    public string? WeatherLocation { get; init; }
    public object? StartColor { get; init; }
    public object? EndColor { get; init; }
    /// <summary>Optional gradient start used in elevated sessions.</summary>
    public object? ElevatedColorStart { get; init; }
    /// <summary>Optional gradient end used in elevated sessions.</summary>
    public object? ElevatedColorEnd { get; init; }
    /// <summary>Emit hierarchical context metadata in Contour, alongside OSC 133 marks.</summary>
    public bool ShellContext { get; init; } = true;
    internal PromptConfiguration Copy() => this with {
        Left = [.. Left],
        Right = [.. Right],
        Block = [.. Block],
        Input = [.. Input],
        TransientBlock = [.. TransientBlock],
    };
}
