namespace StatusLine;

/// <summary>Terminal detection and output shared by the cmdlets and the background updater.</summary>
internal static class Terminal {
    private static readonly Lock WriteLock = new();

    public static bool IsContour =>
        TerminalCapabilities.Detect().Name.Equals("contour", StringComparison.OrdinalIgnoreCase);

    public static bool CanWrite(bool force) =>
        force || (TerminalCapabilities.Detect().HostWritableStatusLine && !Console.IsOutputRedirected);

    public static int Width {
        get {
            try {
                int width = Console.WindowWidth;
                return width > 0 ? width : 120;
            }
            catch (Exception exception) when (exception is IOException or PlatformNotSupportedException) {
                return 120;
            }
        }
    }

    /// <summary>Writes the whole sequence in one call so the terminal sees it as a single chunk.</summary>
    public static void Write(string sequence) => Write(sequence.AsSpan());

    public static void Write(ReadOnlySpan<char> sequence) {
        lock (WriteLock) {
            Console.Out.Write(sequence);
            Console.Out.Flush();
        }
    }
}
