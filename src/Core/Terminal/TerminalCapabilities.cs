using System.Collections.Concurrent;

namespace StatusLine;

/// <summary>Shell integration protocol selected independently of status-line support.</summary>
public enum ShellIntegration {
    Auto,
    None,
    Osc133,
    Osc633,
    ITerm2
}

/// <summary>Terminal features and the evidence used to select them.</summary>
public sealed record TerminalCapability(string Name, bool HostWritableStatusLine, ShellIntegration Integration, string Source);

internal static class TerminalCapabilities {
    private static readonly ConcurrentDictionary<string, bool> Responses = new(StringComparer.OrdinalIgnoreCase);
    public const string Query = Vt.QueryStatusLineMode;

    public static TerminalCapability Detect() {
        string name = Environment.GetEnvironmentVariable("TERM_PROGRAM")?.Trim() ?? string.Empty;
        // The user's terminal detector includes the release channel in TERM_PROGRAM.
        if (name.StartsWith("WindowsTerminal", StringComparison.OrdinalIgnoreCase)) {
            name = "WindowsTerminal";
        }

        bool knownProgram = name.ToLowerInvariant() is "vscode" or "iterm.app" or "contour" or "wezterm" or "kitty" or "windowsterminal" or "mintty" or "ghostty" or "rio" or "foot" or "mlterm";
        bool windowsTerminalSession = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WT_SESSION"));
        string source = "Environment fallback";
        if (!knownProgram && windowsTerminalSession) {
            name = "WindowsTerminal";
            source = "WT_SESSION";
        }
        else if (name.Length == 0) {
            name =
                Environment.GetEnvironmentVariable("GHOSTTY_RESOURCES_DIR") is not null ? "ghostty" :
                Environment.GetEnvironmentVariable("KITTY_PID") is not null ? "kitty" :
                Environment.GetEnvironmentVariable("TERM") ?? "unknown";
        }

        if (name == "unknown" && TerminalProcessDetector.Detect() is { } process) {
            name = process.Name.ToLowerInvariant() is "code" or "code - insiders" ? "vscode" : process.Name;
            source = "Process ancestry";
        }

        name = name.ToLowerInvariant() switch {
            "xterm-kitty" => "kitty",
            "xterm-ghostty" => "ghostty",
            "foot-extra" => "foot",
            _ => name,
        };
        ShellIntegration integration = name.ToLowerInvariant() switch {
            "vscode" => ShellIntegration.Osc633,
            "iterm.app" => ShellIntegration.ITerm2,
            "contour" or "wezterm" or "kitty" or "windowsterminal" or "ghostty" or "rio" or "foot" => ShellIntegration.Osc133,
            _ => ShellIntegration.None,
        };
        if (Responses.TryGetValue(ConnectionKey(name), out bool supported)) {
            return new(name, supported, integration, "DECRQSS response");
        }

        bool fallback = name.Equals("contour", StringComparison.OrdinalIgnoreCase) || name.Equals("mintty", StringComparison.OrdinalIgnoreCase);
        return new(name, fallback, integration, source);
    }

    public static void AcceptResponse(string response) {
        ReadOnlySpan<char> value = response.AsSpan();
        int start = 0;
        if (value.StartsWith(Vt.Dcs, StringComparison.Ordinal)) {
            start = Vt.Dcs.Length;
        }
        else if (value.Length > 0 && value[0] == Vt.DeviceControlString8Bit) {
            start = 1;
        }

        int end = 0;
        if (value.EndsWith(Vt.StringTerminator, StringComparison.Ordinal)) {
            end = Vt.StringTerminator.Length;
        }
        else if (value.Length > 0 && value[^1] == Vt.StringTerminator8Bit) {
            end = 1;
        }

        if (start == 0 || end == 0 || value.Length < start + end) {
            throw new ArgumentException("Expected a complete DECRQSS DCS response.", nameof(response));
        }

        value = value[start..^end];
        bool supported;
        if (value is "1$r0$~" or "1$r1$~" or "1$r2$~") {
            supported = true;
        }
        else if (value is "0$r" or "0$r$~") {
            supported = false;
        }
        else {
            throw new ArgumentException("The response does not describe DECSSDT.", nameof(response));
        }

        if (Responses.Count >= 128) {
            Responses.Clear();
        }

        Responses[ConnectionKey(Detect().Name)] = supported;
    }

    private static string ConnectionKey(string name)
        => string.Join('\0', name,
            Environment.GetEnvironmentVariable("TERM"),
            Environment.GetEnvironmentVariable("WT_SESSION"),
            Environment.GetEnvironmentVariable("KITTY_PID"),
            Environment.GetEnvironmentVariable("TMUX"),
            Environment.GetEnvironmentVariable("STY")
        );

    public static ShellIntegration Resolve(ShellIntegration integration)
        => integration == ShellIntegration.Auto ? Detect().Integration : integration;
}
