namespace StatusLine;

/// <summary>Advanced one-shot terminal output, capabilities, and diagnostics outside the cmdlet surface.</summary>
public static class TerminalControl {
    public static string TextSequence(string text) => StatusLineRenderer.WrapStatusText(TerminalText.Sanitize(text, keepStyles: true));
    public static string ModeSequence(StatusLineMode mode) {
        if (!Enum.IsDefined(mode)) {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return StatusLineRenderer.ModeSequence(mode);
    }
    public static string BlocksSequence(StatusLineBlock[] blocks, BlockRenderOptions? options = null) {
        ArgumentNullException.ThrowIfNull(blocks);
        options ??= new();
        int width = options.Width ?? Terminal.Width;
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 32767);
        if (!Enum.IsDefined(options.TextContrast)) {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (!Enum.IsDefined(options.Style)) {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        System.Drawing.Color start = BlockSnapshots.ColorValue(options.StartColor), end = BlockSnapshots.ColorValue(options.EndColor);
        BlockGradient.Validate(start, end);
        bool contour = Terminal.IsContour;
        return StatusLineRenderer.RenderBlocks(
            blocks,
            width, options.BlockSeparator, options.Style, useCursorPositioning: !contour,
            neutralBackground: contour, blendBackground: options.BlendBackground, textContrast: options.TextContrast,
            startColor: start, endColor: end, framing: StatusLineFraming.InitializeHostWritable);
    }
    public static void WriteText(string text, bool force = false) => WriteSequence(TextSequence(text), force);
    public static void WriteBlocks(StatusLineBlock[] blocks, BlockRenderOptions? options = null, bool force = false) => WriteSequence(BlocksSequence(blocks, options), force);
    public static void SetMode(StatusLineMode mode, bool force = false) => WriteSequence(ModeSequence(mode), force);
    private static void WriteSequence(string sequence, bool force) {
        if (Terminal.CanWrite(force)) {
            Terminal.Write(sequence);
        }
    }
    /// <summary>Cached Windows process ancestry fallback; null when no terminal ancestor is identifiable.</summary>
    public static TerminalProcessInfo? DetectProcess() => TerminalProcessDetector.Detect();
    public static TerminalCapability Detect() => TerminalCapabilities.Detect();
    /// <summary>Detected capabilities and StatusLine integration enabled in the calling runspace.</summary>
    public static ShellIntegrationDiagnostics GetShellIntegration() => new() {
        Detected = Detect(),
        Enabled = PromptSession.IntegrationFeatures(),
    };
    public static string StatusLineQuery => TerminalCapabilities.Query;
    public static TerminalCapability AcceptResponse(string response) {
        ArgumentNullException.ThrowIfNull(response);
        TerminalCapabilities.AcceptResponse(response);
        return Detect();
    }
    public static void QueryStatusLine() {
        if (Console.IsOutputRedirected) {
            throw new InvalidOperationException("Cannot query a redirected terminal.");
        }

        Terminal.Write(StatusLineQuery);
    }
    public static string TitleSequence(string text) {
        ArgumentNullException.ThrowIfNull(text);
        return StatusLineRenderer.TitleSequence(text);
    }
    public static void SetTitle(string text, bool force = false) {
        string sequence = TitleSequence(text);
        if (force || !Console.IsOutputRedirected) {
            Terminal.Write(sequence);
        }
    }
}

/// <summary>Advanced one-shot layout settings.</summary>
public sealed record BlockRenderOptions {
    public TextContrastMode TextContrast { get; init; } = TextContrastMode.Automatic;
    public bool BlendBackground { get; init; }
    public int? Width { get; init; }
    public bool BlockSeparator { get; init; }
    public SegmentStyle Style { get; init; } = SegmentStyle.Plain;
    public object? StartColor { get; init; }
    public object? EndColor { get; init; }
}
