namespace StatusLine;

/// <summary>Detection evidence and the features emitted by StatusLine, not a terminal acknowledgement.</summary>
public sealed record ShellIntegrationDiagnostics {
    public required TerminalCapability Detected { get; init; }
    public required ShellIntegrationFeatures Enabled { get; init; }
}

/// <summary>Integration owned by the installed StatusLine prompt in the calling runspace.</summary>
public sealed record ShellIntegrationFeatures {
    public string Terminal { get; init; } = "None";
    public string PromptStart { get; init; } = "None";
    public string CommandStart { get; init; } = "None";
    public string OutputStart { get; init; } = "None";
    public string CommandFinished { get; init; } = "None";
    public string CommandTextFormat { get; init; } = "None";
    public bool TransientRedrawMarks { get; init; }
    public bool PromptInstalled { get; init; }
    public ShellIntegration? RequestedProtocol { get; init; }
    public ShellIntegration Protocol { get; init; } = ShellIntegration.None;
    public bool CommandBoundaries { get; init; }
    public string WorkingDirectory { get; init; } = "None";
    public bool CommandText { get; init; }
    public bool ReflowControl { get; init; }
    public bool ShellContext { get; init; }
}
