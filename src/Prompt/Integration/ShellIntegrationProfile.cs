namespace StatusLine;

internal enum CommandMetadata {
    None,
    VsCode,
    Contour,
}

/// <summary>Terminal-specific syntax, resolved once for an installed prompt rather than inferred from an OSC number.</summary>
internal readonly record struct ShellIntegrationProfile {
    public ShellIntegrationProfile(string terminal, ShellIntegration protocol, string workingDirectory,
        CommandMetadata metadata, bool reflowControl, bool context) {
        Terminal = terminal;
        Protocol = protocol;
        WorkingDirectory = workingDirectory;
        Metadata = metadata;
        ReflowControl = reflowControl;
        Context = context;
        PromptStart = string.Empty;
        CommandStart = string.Empty;
        if (protocol != ShellIntegration.None) {
            PromptStart = Vt.PromptStart(MarkerNumber);
            CommandStart = Vt.InputStart(MarkerNumber);
        }
    }

    public string Terminal { get; }
    public ShellIntegration Protocol { get; }
    public string WorkingDirectory { get; }
    public CommandMetadata Metadata { get; }
    public bool ReflowControl { get; }
    public bool Context { get; }
    public int MarkerNumber => Protocol == ShellIntegration.Osc633 ? 633 : 133;
    public string PromptStart { get; }
    public string CommandStart { get; }
    public static ShellIntegrationProfile Resolve(ShellIntegration requested) {
        string terminal = TerminalCapabilities.Detect().Name.ToLowerInvariant();
        ShellIntegration protocol = TerminalCapabilities.Resolve(requested);
        if (requested == ShellIntegration.Auto && terminal == "vscode" &&
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VSCODE_SHELL_INTEGRATION"))) {
            protocol = ShellIntegration.None;
        }

        if (protocol == ShellIntegration.None) {
            return new(terminal, protocol, "None", CommandMetadata.None, false, false);
        }

        if (protocol == ShellIntegration.Osc633) {
            return new(terminal, protocol, "OSC 633", CommandMetadata.VsCode, false, false);
        }

        if (protocol == ShellIntegration.ITerm2) {
            return new(terminal, protocol, "OSC 1337", CommandMetadata.None, false, false);
        }
        // OSC 133 boundaries are shared. Directory and command metadata extensions are not.
        return terminal switch {
            "windowsterminal" => new(terminal, protocol, "OSC 9;9", CommandMetadata.None, false, false),
            "contour" => new(terminal, protocol, "OSC 7", CommandMetadata.Contour, true, true),
            "wezterm" or "ghostty" or "kitty" or "rio" or "foot" or "mintty" =>
                new(terminal, protocol, "OSC 7", CommandMetadata.None, false, false),
            _ => new(terminal, protocol, "None", CommandMetadata.None, false, false),
        };
    }
}
