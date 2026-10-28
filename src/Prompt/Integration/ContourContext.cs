using System.Globalization;

namespace StatusLine;

/// <summary>Runspace-owned metadata; OSC 133 continues to own semantic marks.</summary>
internal sealed class ContourContext {
    private readonly string shellId = Guid.NewGuid().ToString("N");
    private readonly string identity = $";hostname={Escape(System.Net.Dns.GetHostName())};user={Escape(Environment.UserName)};pid={Environment.ProcessId}";
    private string? commandId;
    private long commandNumber;
    private bool opened;

    internal string Prompt(string? path, int? exitCode) {
        string ended = string.Empty;
        if (commandId is not null) {
            ended = Vt.ContextEnd(commandId, exitCode);
        }

        commandId = null;
        opened = true;
        return ended + Vt.ContextShellStart(shellId, identity, Directory(path));
    }

    internal string Execute(string line, string? path) {
        commandId = shellId + "-" + (++commandNumber).ToString(CultureInfo.InvariantCulture);
        return Vt.ContextCommandStart(commandId, identity, Directory(path), Escape(line));
    }

    internal string Close() {
        string result = commandId is null ? string.Empty : Vt.ContextEnd(commandId);
        commandId = null;
        if (opened) {
            result += Vt.ContextEnd(shellId);
            opened = false;
        }

        return result;
    }

    private static string Directory(string? path) => path is null ? string.Empty : ";cwd=" + Escape(path);
    private static string Escape(string value) => TerminalText.Sanitize(value, keepStyles: false)
        .Replace("\\", "\\x5c", StringComparison.Ordinal).Replace(";", "\\x3b", StringComparison.Ordinal);
}
