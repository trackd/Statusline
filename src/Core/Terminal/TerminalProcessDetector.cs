using System.Diagnostics;
using System.Runtime.InteropServices;
using StatusLine.Interop;

namespace StatusLine;

/// <summary>Terminal executable metadata inferred from process ancestry.</summary>
public sealed record TerminalProcessInfo(string Name, string? Edition, string? Version, int ProcessId);

internal static class TerminalProcessDetector {
    private static readonly string[] Channels = ["Preview", "Canary", "Dev"];
    private static readonly Lazy<TerminalProcessInfo?> Current = new(Read);
    public static TerminalProcessInfo? Detect() => Current.Value;

    private static TerminalProcessInfo? Read() {
        if (!OperatingSystem.IsWindows()) {
            return null;
        }

        nint snapshot = NativeMethods.CreateToolhelp32Snapshot(2, 0);
        if (snapshot == -1) {
            return null;
        }

        var processes = new Dictionary<int, (int Parent, string Name)>();
        try {
            var entry = new NativeMethods.ProcessEntry { Size = (uint)Marshal.SizeOf<NativeMethods.ProcessEntry>(), Executable = string.Empty };
            if (!NativeMethods.Process32First(snapshot, ref entry)) {
                return null;
            }

            do {
                processes[(int)entry.Id] = ((int)entry.ParentId, Path.GetFileNameWithoutExtension(entry.Executable));
            }
            while (NativeMethods.Process32Next(snapshot, ref entry));
        }
        finally {
            NativeMethods.CloseHandle(snapshot);
        }

        int id = FindAncestor(Environment.ProcessId, processes);
        if (id == 0) {
            return null;
        }

        string name = processes[id].Name;
        string? path = null;
        string? version = null;
        try {
            using var process = Process.GetProcessById(id);
            path = process.MainModule?.FileName;
            if (path is not null) {
                version = FileVersionInfo.GetVersionInfo(path).FileVersion;
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or NotSupportedException) { }

        string? edition = name.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase)
            ? Channels.FirstOrDefault(channel => path?.Contains(channel, StringComparison.OrdinalIgnoreCase) == true) ?? "Stable" : null;
        return new(name, edition, version, id);
    }

    internal static int FindAncestor(int id, IReadOnlyDictionary<int, (int Parent, string Name)> processes) {
        var visited = new HashSet<int>();
        int consoleHost = 0;
        for (int depth = 0; depth < 32 && id > 0 && visited.Add(id) && processes.TryGetValue(id, out (int Parent, string Name) entry); depth++) {
            string name = entry.Name.ToLowerInvariant();
            if (name is "windowsterminal" or "code" or "code - insiders" or "alacritty" or "hyper" or "terminus" or "wezterm" or "wezterm-gui" or "rio" or "contour" or "mintty") {
                return id;
            }

            if (name == "conhost") {
                consoleHost = id;
            }

            if (name == "explorer") {
                break;
            }

            id = entry.Parent;
        }

        return consoleHost;
    }
}
