using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace StatusLine.Cli;

// The worker's stdout belongs to the IPC launcher, not to the terminal.
// Never read /dev/tty: terminal input remains exclusively owned by the shell.
internal sealed partial class HostWritableOutput : IDisposable {
    private readonly FileStream terminal;
    private bool active;
    private string? previous;
    private int previousWidth;

    internal HostWritableOutput() {
        if (OperatingSystem.IsWindows()) {
            throw new ArgumentException("CLI HostWritable currently requires a Unix controlling terminal; use file output on Windows.");
        }
        if (!TerminalCapabilities.Detect().HostWritableStatusLine) {
            throw new ArgumentException("This terminal is not known to support HostWritable status lines.");
        }
        terminal = new FileStream("/dev/tty", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, bufferSize: 1);
    }

    internal int Width(int fallback) {
        nuint request = OperatingSystem.IsMacOS() ? 0x40087468u : 0x5413u;
        return Ioctl(terminal.SafeFileHandle, request, out WindowSize size) == 0 && size.Columns >= 2
            ? Math.Min(10000, (int)size.Columns) : fallback;
    }

    internal void Write(string text, int width) {
        if (active && text == previous && width == previousWidth) { return; }
        // One write keeps display selection, content and restoration together.
        byte[] frame = Encoding.UTF8.GetBytes(Vt.StatusText(text, initialize: !active));
        terminal.Write(frame);
        active = true;
        previous = text;
        previousWidth = width;
    }

    public void Dispose() {
        try {
            if (active) {
                terminal.Write(Encoding.UTF8.GetBytes(Vt.StatusText(string.Empty, false) + Vt.StatusLineMode(StatusLineMode.Hidden)));
            }
        }
        catch (IOException) { /* A closed terminal needs no restoration. */ }
        finally { terminal.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowSize {
        public ushort Rows;
        public ushort Columns;
        public ushort PixelWidth;
        public ushort PixelHeight;
    }

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int Ioctl(SafeFileHandle descriptor, nuint request, out WindowSize size);
}
