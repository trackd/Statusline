using System.Text;

namespace StatusLine;

/// <summary>
/// Renders frames into reusable buffers. Unchanged inputs skip rendering, unchanged output skips
/// writing, and the steady state allocates nothing.
/// </summary>
internal sealed class StatusLineOutput {
    private UpdaterOptions options;
    private readonly CancellationToken token;
    private readonly bool deferHost = !Console.IsInputRedirected && StatusLineActivity.HasReadLineHook;
    private readonly StringBuilder scratch = new(1024);
    private readonly FrameBuffer frame = new();
    private BlockGradientCache gradient;
    private bool contour = Terminal.IsContour;
    private bool initialized;
    private bool hostPending;
    private UpdaterOptions? renderedOptions;
    private StatusLineBlock[] renderedBlocks = [];
    private int renderedWidth;
    private bool renderedUnicode;
    private StatusLineFraming renderedFraming;

    public StatusLineOutput(UpdaterOptions options, CancellationToken token) {
        this.options = options;
        this.token = token;
        gradient = new(options.StartColor, options.EndColor, options.Style);
    }

    /// <summary>The last rendered frame.</summary>
    internal ReadOnlySpan<char> Frame => frame.Text;

    // The caller holds OutputGate; never flush a queued frame from an older configuration.
    internal void UpdateOptions(UpdaterOptions replacement) {
        options = replacement;
        gradient = new(replacement.StartColor, replacement.EndColor, replacement.Style);
        contour = Terminal.IsContour;
        if (hostPending) {
            hostPending = false;
            frame.Invalidate();
        }
    }

    /// <summary>Draws <paramref name="blocks"/> unless <paramref name="source"/> has been replaced meanwhile.</summary>
    public void Draw(IReadOnlyList<StatusLineBlock> blocks, UpdaterOptions source) {
        int width = Terminal.Width;
        string path;
        lock (StatusLineUpdater.OutputGate) {
            // A configuration update may have overtaken provider collection.
            if (!StatusLineUpdater.IsLive(source)) {
                return;
            }

            if (options.Target == StatusLineTarget.HostWritable) {
                DrawHost(blocks, width);
                return;
            }

            if (options.Target != StatusLineTarget.File || !Render(blocks, width, StatusLineFraming.Reset)) {
                return;
            }

            path = options.OutputPath;
        }
        // Only this worker touches the frame on the file path, so the slow write runs outside the gate.
        if (token.IsCancellationRequested) {
            return;
        }

        if (!StatusFile.TryWrite(path, frame.Text)) {
            Invalidate();
            StatusLineUpdater.ReportError(StatusFile.LastError);
        }
    }

    private void DrawHost(IReadOnlyList<StatusLineBlock> blocks, int width) {
        if (!StatusLineUpdater.CanDrawHost(options, token)) {
            return;
        }

        if (Render(blocks, width, StatusLineFraming.HostWritable)) {
            hostPending = true;
        }

        if (hostPending && !(deferHost && !StatusLineUpdater.IsCommandExecuting)) {
            WriteHost();
        }
    }

    // Called on the foreground runspace, while PSReadLine is processing an idle event.
    internal void FlushPending() {
        lock (StatusLineUpdater.OutputGate) {
            if (hostPending && StatusLineUpdater.CanDrawHost(options, token)) {
                WriteHost();
            }
        }
    }

    private void WriteHost() {
        if (token.IsCancellationRequested) {
            return;
        }

        if (!initialized) {
            Terminal.Write(StatusLineRenderer.ModeSequence(StatusLineMode.HostWritable));
            initialized = true;
        }

        Terminal.Write(frame.Text);
        hostPending = false;
    }

    /// <summary>Renders into the frame buffer; returns whether the frame content changed.</summary>
    internal bool Render(IReadOnlyList<StatusLineBlock> blocks, int width, StatusLineFraming framing) {
        bool unicode = TextMeasurement.UseUnicode;
        if (IsRendered(blocks, width, unicode, framing)) {
            return false;
        }

        scratch.Clear();
        StatusLineRenderer.AppendBlocks(scratch, blocks, width, options.BlockSeparator, options.Style,
            useCursorPositioning: false, neutralBackground: contour,
            blendBackground: options.BlendBackground, textContrast: options.TextContrast,
            startColor: options.StartColor, endColor: options.EndColor, framing: framing, gradientCache: gradient);
        if (renderedBlocks.Length != blocks.Count) {
            renderedBlocks = new StatusLineBlock[blocks.Count];
        }

        for (int index = 0; index < blocks.Count; index++) {
            renderedBlocks[index] = blocks[index];
        }

        renderedOptions = options;
        renderedWidth = width;
        renderedUnicode = unicode;
        renderedFraming = framing;
        return frame.Update(scratch);
    }

    private bool IsRendered(IReadOnlyList<StatusLineBlock> blocks, int width, bool unicode, StatusLineFraming framing) {
        if (renderedOptions is null || !ReferenceEquals(renderedOptions, options) || renderedWidth != width ||
            renderedUnicode != unicode || renderedFraming != framing || renderedBlocks.Length != blocks.Count) {
            return false;
        }

        for (int index = 0; index < blocks.Count; index++) {
            if (!ReferenceEquals(renderedBlocks[index], blocks[index])) {
                return false;
            }
        }

        return true;
    }

    private void Invalidate() {
        renderedOptions = null;
        frame.Invalidate();
    }

    public void Clear() {
        if (options.Target == StatusLineTarget.File && !StatusFile.TryWrite(options.OutputPath, [])) {
            StatusLineUpdater.ReportError(StatusFile.LastError);
        }

        if (!initialized) {
            return;
        }

        try {
            Terminal.Write(StatusLineRenderer.ModeSequence(StatusLineMode.Indicator));
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException) {
            StatusLineUpdater.ReportError(exception.Message);
        }
    }
}
