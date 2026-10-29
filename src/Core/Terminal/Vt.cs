using System.Drawing;
using System.Globalization;
using System.Text;

namespace StatusLine;

internal enum StatusLineFraming { None, Reset, HostWritable, InitializeHostWritable }

/// <summary>
/// Wire syntax for terminal output. Callers own sanitization, layout and lifecycle ordering.
/// DEC display selection is separate from activation so routine redraws do not resize the screen.
/// </summary>
internal static partial class Vt {
    internal static void AppendFrameStart(StringBuilder output, StatusLineFraming framing) {
        if (framing == StatusLineFraming.InitializeHostWritable) {
            output.Append(HostWritableStatusLine);
        }

        if (framing is StatusLineFraming.HostWritable or StatusLineFraming.InitializeHostWritable) {
            output.Append(SelectStatusDisplay).Append(Reset).Append('\r').Append(ClearLine);
        }
    }

    internal static void AppendFrameEnd(StringBuilder output, StatusLineFraming framing) {
        if (framing != StatusLineFraming.None) {
            output.Append(Reset);
        }

        if (framing is StatusLineFraming.HostWritable or StatusLineFraming.InitializeHostWritable) {
            output.Append(SelectMainDisplay);
        }
    }
    internal static string StatusLineMode(StatusLineMode mode) => $"{SelectMainDisplay}{Csi}{(int)mode}$~";
    internal static string Title(string text) => $"\e]2;{text}{StringTerminator}";

    internal static string StatusText(string text, bool initialize) {
        string activation = string.Empty;
        if (initialize) {
            activation = HostWritableStatusLine;
        }

        return string.Concat(activation, SelectStatusDisplay + Reset + "\r" + ClearLine,
            text, Reset + SelectMainDisplay);
    }

    internal static void AppendCursorPosition(StringBuilder output, int row, int column) => output.Append(CultureInfo.InvariantCulture, $"\e[{row};{column}H");

    internal static string RgbForeground(Color color, string text) =>
        $"{Csi}38;2;{color.R};{color.G};{color.B}m{text}{Reset}";

    internal static string PromptStart(int protocol) => $"\e]{protocol};A{Bell}";
    internal static string InputStart(int protocol) => $"\e]{protocol};B{Bell}";
    internal const string InputPrompt = "\e]133;P;k=i\a";
    internal const string SecondaryPrompt = "\e]133;P;k=s\a";
    internal static string OutputStart(int protocol) => $"\e]{protocol};C{Bell}";
    internal static string OutputEnd(int protocol) => $"\e]{protocol};D{Bell}";
    internal static string OutputEnd(int protocol, int exitCode) => $"\e]{protocol};D;{exitCode}{Bell}";
    internal static string VsCodeWindowsProperty(bool windows) => $"\e]633;P;IsWindows={windows}{Bell}";
    internal static string VsCodeCommand(string escapedCommand, string escapedNonce) =>
        $"\e]633;E;{escapedCommand}{escapedNonce}{Bell}";
    internal static string ContourCommand(string commandUrl) => $"\e]133;C;cmdline_url={commandUrl}{Bell}";
    internal static string VsCodeDirectory(string escapedPath) => $"\e]633;P;Cwd={escapedPath}{Bell}";
    internal static string ITermDirectory(string path) => $"\e]1337;CurrentDir={path}{Bell}";
    internal static string WindowsTerminalDirectory(string path) => $"\e]9;9;\"{path}\"{Bell}";
    internal static string FileUrlDirectory(string url) => $"\e]7;{url}{StringTerminator}";
    internal static string ContextShellStart(string id, string identity, string directory) =>
        $"\e]3008;start={id};type=shell{identity}{directory}{StringTerminator}";
    internal static string ContextCommandStart(string id, string identity, string directory, string command) =>
        $"\e]3008;start={id};type=command{identity}{directory};cmdline={command}{StringTerminator}";
    internal static string ContextEnd(string id) => $"\e]3008;end={id}{StringTerminator}";
    internal static string ContextEnd(string id, int? exitCode) {
        string outcome = "failure";
        if (exitCode == 0) {
            outcome = "success";
        }

        return $"\e]3008;end={id};exit={outcome};status={(exitCode ?? 1).ToString(CultureInfo.InvariantCulture)}{StringTerminator}";
    }
}
