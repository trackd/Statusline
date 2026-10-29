using System.Drawing;
using System.Management.Automation;

namespace StatusLine.Commands;

/// <summary>Starts refreshing the status line from a background thread.</summary>
[Cmdlet(VerbsLifecycle.Enable, "StatusLine", SupportsShouldProcess = true)]
public sealed class EnableStatusLineCommand : PSCmdlet {
    /// <summary>Built-in kinds or reusable block definitions to render and refresh.</summary>
    [Parameter(Position = 0)]
    [AllowEmptyCollection]
    [ArgumentCompleter(typeof(BlockKindCompleter))]
    public object[] Block { get; set; } = ["Cpu", "ProcessMemory", "Clock"];

    [Parameter]
    [AllowEmptyCollection]
    [ArgumentCompleter(typeof(BlockKindCompleter))]
    public object[]? Left { get; set; }

    [Parameter]
    [AllowEmptyCollection]
    [ArgumentCompleter(typeof(BlockKindCompleter))]
    public object[]? Center { get; set; }

    [Parameter]
    [AllowEmptyCollection]
    [ArgumentCompleter(typeof(BlockKindCompleter))]
    public object[]? Right { get; set; }

    [Parameter]
    [ColorTransform]
    [ArgumentCompleter(typeof(ColorCompleter))]
    public Color StartColor { get; set; }

    [Parameter]
    [ColorTransform]
    [ArgumentCompleter(typeof(ColorCompleter))]
    public Color EndColor { get; set; }

    [Parameter]
    [ValidateRange(100, 60000)]
    public int IntervalMilliseconds { get; set; } = 1000;

    /// <summary>Pause refresh work after this many seconds without shell activity; zero disables idle pausing.</summary>
    [Parameter]
    [ValidateRange(0, 86400)]
    public int IdleTimeoutSeconds { get; set; } = 60;

    [Parameter]
    [ValidateRange(50, 60000)]
    public int BlockTimeoutMilliseconds { get; set; } = 2000;

    [Parameter]
    public SwitchParameter UpdateDuringCommand { get; set; }

    [Parameter]
    public TextContrastMode TextContrast { get; set; } = TextContrastMode.Automatic;

    [Parameter]
    public SegmentStyle Style { get; set; } = SegmentStyle.Powerline;

    [Parameter]
    public IconSet IconSet { get; set; } = IconSet.NerdFont;

    [Parameter]
    public SwitchParameter BlockSeparator { get; set; }

    [Parameter]
    public SwitchParameter DurationWarning { get; set; }

    [Parameter]
    [AllowEmptyString]
    [ValidateNotNull]
    public string Padding { get; set; } = " ";

    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Where each refresh goes: the host-writable status line or a file.</summary>
    [Parameter]
    public StatusLineTarget Target { get; set; } = StatusLineTarget.HostWritable;

    /// <summary>File written when Target is File.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string OutputPath { get; set; } = Path.Combine(Path.GetTempPath(), $"statusline-{Environment.ProcessId}-{System.Management.Automation.Runspaces.Runspace.DefaultRunspace?.InstanceId ?? Guid.Empty:N}.txt");

    [Parameter]
    public string? WeatherLocation { get; set; }

    [Parameter]
    public SwitchParameter BlendBackground { get; set; }

    /// <summary>Process-wide relative directory depth (default 3); zero keeps the complete relative path.</summary>
    [Parameter]
    [ValidateRange(0, int.MaxValue)]
    public int DirectoryDepth { get; set; } = 3;

    protected override void EndProcessing() {
        BlockGradient.Validate(StartColor, EndColor);
        BlockSelector[] selection = CommandHelpers.ParseGroups(this, Block, Left, Center, Right);
        if (Target == StatusLineTarget.HostWritable && !Terminal.CanWrite(Force)) {
            WriteWarning("Host-writable status-line support was not detected or output is redirected; use -Force to override.");
            return;
        }

        string outputPath = OutputPath;
        if (Target == StatusLineTarget.File) {
            outputPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(OutputPath, out ProviderInfo provider, out _);
            if (!provider.Name.Equals("FileSystem", StringComparison.OrdinalIgnoreCase)) {
                ThrowTerminatingError(new ErrorRecord(new ArgumentException("OutputPath must use the FileSystem provider."),
                    "StatusLineFileSystemRequired", ErrorCategory.InvalidArgument, OutputPath));
            }
        }

        if (!ShouldProcess(Target == StatusLineTarget.File ? outputPath : "Current terminal", "Enable status line")) {
            return;
        }

        if (Target == StatusLineTarget.File) {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
                ThrowTerminatingError(new ErrorRecord(exception, "StatusLineOutputPathUnavailable", ErrorCategory.WriteError, outputPath));
            }

            string program = OperatingSystem.IsWindows() ? $"type \"{outputPath}\"" : $"cat '{outputPath.Replace("'", "'\\''", StringComparison.Ordinal)}'";
            string value = $"{{Command:Program={program},Interval=1}}".Replace("'", "''", StringComparison.Ordinal);
            WriteVerbose($"Contour profile: status_line.indicator.left: '{value}'");
        }

        var options = new UpdaterOptions(
            selection,
            new BlockOverrides(null, null, null, Padding),
            TimeSpan.FromMilliseconds(IntervalMilliseconds),
            BlockSeparator.IsPresent,
            Style,
            IconSet,
            SessionState,
            CommandHelpers.GetPowerShellVersion(SessionState),
            Target,
            outputPath,
            System.Management.Automation.Runspaces.Runspace.DefaultRunspace.InstanceId,
            StartColor, EndColor, BlockTimeoutMilliseconds, UpdateDuringCommand.IsPresent, IdleTimeoutSeconds, WeatherLocation, BlendBackground.IsPresent, TextContrast, DurationWarning.IsPresent);
        int previousDepth = DirectoryDisplay.MaximumComponents;
        bool started = false;
        try {
            if (MyInvocation.BoundParameters.ContainsKey(nameof(DirectoryDepth))) {
                DirectoryDisplay.MaximumComponents = DirectoryDepth;
            }

            StatusLineUpdater.Start(options, Events);
            started = true;
        }
        catch (StatusLineOwnershipException exception) {
            ThrowTerminatingError(new ErrorRecord(exception, "StatusLineOwnedByAnotherRunspace", ErrorCategory.ResourceUnavailable, null));
        }
        catch (TimeoutException exception) {
            ThrowTerminatingError(new ErrorRecord(exception, "StatusLineStopTimeout", ErrorCategory.OperationTimeout, null));
        }
        finally {
            if (!started && MyInvocation.BoundParameters.ContainsKey(nameof(DirectoryDepth))) {
                DirectoryDisplay.MaximumComponents = previousDepth;
            }
        }
    }
}
