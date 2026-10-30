using System.Drawing;
namespace StatusLine;

internal sealed class PromptLayout {
    private readonly StatusLineBlockDefinition[] alignmentDefinitions;
    private readonly Lock accentGate = new();
    internal TerminalColor TransientAccent {
        get {
            lock (accentGate) {
                return StartColor.IsEmpty ? field : StartColor;
            }
        }
        set {
            if (!value.IsEmpty) {
                lock (accentGate) {
                    field = value;
                }
            }
        }
    } = ConsoleColor.DarkBlue;
    public PromptLayout(PromptConfiguration configuration) : this(configuration, Privilege.IsElevated) {
    }

    internal PromptLayout(PromptConfiguration configuration, bool elevated) {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!Enum.IsDefined(configuration.TextContrast)) {
            throw new ArgumentOutOfRangeException(nameof(configuration));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(configuration.Rows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(configuration.Rows, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(configuration.BlockTimeoutMilliseconds, 50);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(configuration.BlockTimeoutMilliseconds, 60000);
        ArgumentNullException.ThrowIfNull(configuration.Left);
        ArgumentNullException.ThrowIfNull(configuration.Right);
        ArgumentNullException.ThrowIfNull(configuration.Block);
        ArgumentNullException.ThrowIfNull(configuration.Input);
        ArgumentNullException.ThrowIfNull(configuration.Symbol);
        ArgumentNullException.ThrowIfNull(configuration.ContextSymbol);
        ArgumentNullException.ThrowIfNull(configuration.InputLinePrefix);
        ArgumentNullException.ThrowIfNull(configuration.TransientBlock);
        ArgumentNullException.ThrowIfNull(configuration.TransientSymbol);
        ArgumentNullException.ThrowIfNull(configuration.Padding);
        if (!Enum.IsDefined(configuration.Style) || !Enum.IsDefined(configuration.IconSet) || !Enum.IsDefined(configuration.ShellIntegration)) {
            throw new ArgumentException("Prompt configuration contains an undefined style, icon set, or protocol.", nameof(configuration));
        }

        Configuration = configuration.Copy();
        Symbol = TerminalText.Sanitize(Configuration.Symbol, keepStyles: false);
        TransientSymbol = TerminalText.Sanitize(Configuration.TransientSymbol, keepStyles: false);
        ContextSymbol = TerminalText.Sanitize(Configuration.ContextSymbol, keepStyles: false);
        InputLinePrefix = TerminalText.Sanitize(Configuration.InputLinePrefix, keepStyles: false);
        StartColor = BlockSnapshots.ColorValue(configuration.StartColor);
        EndColor = BlockSnapshots.ColorValue(configuration.EndColor);
        BlockGradient.Validate(StartColor, EndColor);
        Color elevatedStart = BlockSnapshots.ColorValue(configuration.ElevatedColorStart);
        Color elevatedEnd = BlockSnapshots.ColorValue(configuration.ElevatedColorEnd);
        if (elevatedStart.IsEmpty != elevatedEnd.IsEmpty) {
            throw new ArgumentException("Specify both ElevatedColorStart and ElevatedColorEnd, or neither.", nameof(configuration));
        }

        if (elevated && !elevatedStart.IsEmpty) {
            StartColor = elevatedStart;
            EndColor = elevatedEnd;
        }

        Left = BlockSelector.Parse(configuration.Left);
        Right = BlockSelector.Parse(configuration.Right);
        Input = BlockSelector.Parse(configuration.Input);
        Transient = BlockSelector.Parse(configuration.TransientBlock);
        AdditionalBlocks = BlockSelector.Parse(Configuration.Block);
        RequiredFacts = BlockSelector.RequiredFacts(Left, Right, Input, Transient, AdditionalBlocks);
        LeftOverrides = new(StatusLineAlignment.Left, null, null, Configuration.Padding);
        RightOverrides = new(StatusLineAlignment.Right, null, null, Configuration.Padding);
        AdditionalOverrides = new(null, null, null, Configuration.Padding);
        ContextGradient = new(StartColor, EndColor, Configuration.Style);
        InputGradient = new(StartColor, EndColor, Configuration.Style);
        TransientGradient = new(StartColor, EndColor, Configuration.Style);
        alignmentDefinitions = [.. AdditionalBlocks.Select(block => block.Definition).OfType<StatusLineBlockDefinition>()];
        Rows = Right.Length > 0 || Input.Length > 0 || AdditionalBlocks.Any(block => block.Definition is null && block.EffectiveAlignment != StatusLineAlignment.Left)
            ? 2 : Configuration.Rows;
    }

    public PromptConfiguration Configuration { get; }
    internal string Symbol { get; }
    internal string TransientSymbol { get; }
    internal string ContextSymbol { get; }
    internal string InputLinePrefix { get; }
    public BlockSelector[] Left { get; }
    public BlockSelector[] Right { get; }
    public BlockSelector[] Input { get; }
    public BlockSelector[] Transient { get; }
    public BlockSelector[] AdditionalBlocks { get; }
    public int Rows {
        get {
            if (field == 2) {
                return 2;
            }

            foreach (StatusLineBlockDefinition definition in alignmentDefinitions) {
                if (definition.EffectiveAlignment != StatusLineAlignment.Left) {
                    return 2;
                }
            }

            return field;
        }
    }
    internal SessionFacts RequiredFacts { get; }
    public Color StartColor { get; }
    public Color EndColor { get; }
    internal BlockOverrides LeftOverrides { get; }
    internal BlockOverrides RightOverrides { get; }
    internal BlockOverrides AdditionalOverrides { get; }
    internal BlockGradientCache ContextGradient { get; }
    internal BlockGradientCache InputGradient { get; }
    internal BlockGradientCache TransientGradient { get; }
}
