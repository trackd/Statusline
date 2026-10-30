using System.Drawing;

namespace StatusLine;

/// <summary>Detached settings for the active statusline. Target and OutputPath require re-enabling to change.</summary>
public sealed record StatusBarConfiguration {

    public object[] Left { get; init; } = [];
    public object[] Center { get; init; } = [];
    public object[] Right { get; init; } = [];
    public object[] Block { get; init; } = [];
    public object? StartColor { get; init; }
    public object? EndColor { get; init; }
    public string Padding { get; init; } = " ";
    public bool BlockSeparator { get; init; }
    public bool BlendBackground { get; init; }
    public SegmentStyle Style { get; init; } = SegmentStyle.Powerline;
    public IconSet IconSet { get; init; } = IconSet.NerdFont;
    public TextContrastMode TextContrast { get; init; } = TextContrastMode.Automatic;
    public bool DurationWarning { get; init; }
    public string? WeatherLocation { get; init; }
    public int IntervalMilliseconds { get; init; } = 1000;
    public int BlockTimeoutMilliseconds { get; init; } = 2000;
    public int IdleTimeoutSeconds { get; init; } = 60;
    public bool UpdateDuringCommand { get; init; }
    public StatusLineTarget Target { get; init; }
    public string OutputPath { get; init; } = string.Empty;

    internal StatusBarConfiguration Copy() => this with {

        Left = [.. Left],
        Center = [.. Center],
        Right = [.. Right],
        Block = [.. Block],
    };

    internal UpdaterOptions Compile(UpdaterOptions current) {
        (Color start, Color end) = Validate();
        if (Target != current.Target || OutputPath != current.OutputPath) {
            throw new ArgumentException("Changing Target or OutputPath requires Enable-StatusLine.");
        }

        return current with {
            Selection = SelectBlocks(),
            Overrides = current.Overrides with { Padding = Padding },
            StartColor = start,
            EndColor = end,
            Style = Style,
            IconSet = IconSet,
            TextContrast = TextContrast,
            BlockSeparator = BlockSeparator,
            BlendBackground = BlendBackground,
            DurationWarning = DurationWarning,
            WeatherLocation = WeatherLocation,
            Interval = TimeSpan.FromMilliseconds(IntervalMilliseconds),
            BlockTimeoutMilliseconds = BlockTimeoutMilliseconds,
            IdleTimeoutSeconds = IdleTimeoutSeconds,
            UpdateDuringCommand = UpdateDuringCommand,
        };
    }

    internal (Color Start, Color End) Validate() {
        ArgumentNullException.ThrowIfNull(Padding);

        ArgumentNullException.ThrowIfNull(Left);
        ArgumentNullException.ThrowIfNull(Center);
        ArgumentNullException.ThrowIfNull(Right);
        ArgumentNullException.ThrowIfNull(Block);
        if (!Enum.IsDefined(Style) || !Enum.IsDefined(IconSet) || !Enum.IsDefined(TextContrast)) {
            throw new ArgumentException("Undefined statusline style, icon set or contrast.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(IntervalMilliseconds, 100);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(IntervalMilliseconds, 60000);
        ArgumentOutOfRangeException.ThrowIfLessThan(BlockTimeoutMilliseconds, 50);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(BlockTimeoutMilliseconds, 60000);
        ArgumentOutOfRangeException.ThrowIfLessThan(IdleTimeoutSeconds, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(IdleTimeoutSeconds, 86400);
        Color start = BlockSnapshots.ColorValue(StartColor), end = BlockSnapshots.ColorValue(EndColor);
        BlockGradient.Validate(start, end);
        return (start, end);
    }

    internal BlockSelector[] SelectBlocks() {
        var selection = new List<BlockSelector>(BlockSelector.Parse(Block));
        Add(Left, StatusLineAlignment.Left);
        Add(Center, StatusLineAlignment.Center);
        Add(Right, StatusLineAlignment.Right);

        return [.. selection];

        void Add(object[] values, StatusLineAlignment alignment) => selection.AddRange(BlockSelector.Parse(values).Select(selector => selector with { Alignment = alignment }));
    }

    internal static StatusBarConfiguration From(UpdaterOptions options) {
        object[] Group(StatusLineAlignment? alignment) => [.. options.Selection.Where(selector => selector.Alignment == alignment)
            .Select(selector => (object?)selector.Definition ?? selector.Value ?? (object)selector.Kind!.Value)];
        return new() {
            Block = Group(null),

            Left = Group(StatusLineAlignment.Left),
            Center = Group(StatusLineAlignment.Center),
            Right = Group(StatusLineAlignment.Right),
            StartColor = options.StartColor,
            EndColor = options.EndColor,
            Padding = options.Overrides.Padding,
            BlockSeparator = options.BlockSeparator,
            BlendBackground = options.BlendBackground,
            Style = options.Style,
            IconSet = options.IconSet,
            TextContrast = options.TextContrast,
            DurationWarning = options.DurationWarning,
            WeatherLocation = options.WeatherLocation,
            IntervalMilliseconds = (int)options.Interval.TotalMilliseconds,
            BlockTimeoutMilliseconds = options.BlockTimeoutMilliseconds,
            IdleTimeoutSeconds = options.IdleTimeoutSeconds,
            UpdateDuringCommand = options.UpdateDuringCommand,
            Target = options.Target,
            OutputPath = options.OutputPath,
        };
    }
}
