using System.Drawing;
using System.Text;

namespace StatusLine;

/// <summary>
/// Immutable, styled piece of status line text.
/// The SGR style is derived from validated inputs, so instances cannot carry arbitrary escape sequences.
/// </summary>
public sealed class StatusLineBlock {
    /// <summary>Creates a block; empty colors use the terminal defaults.</summary>
    public StatusLineBlock(
        string text,
        TerminalColor foreground,
        TerminalColor background,
        StatusLineAlignment alignment = StatusLineAlignment.Left,
        string padding = " ",
        bool bold = false,
        bool italic = false,
        bool underline = false,
        string icon = "") {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(icon);

        ArgumentNullException.ThrowIfNull(padding);

        if (!Enum.IsDefined(alignment)) {
            throw new ArgumentOutOfRangeException(nameof(alignment));
        }

        Text = text;
        Foreground = foreground;
        Background = background;
        Alignment = alignment;
        Padding = TerminalText.Sanitize(padding, keepStyles: false);

        Bold = bold;
        Italic = italic;
        Underline = underline;
        Icon = icon;

        if (Icon.Length == 0 || Text.Length == 0) {
            Content = TerminalText.Sanitize(Icon.Length == 0 ? Text : Icon, keepStyles: true);
        }
        else {
            StringBuilder builder = StringBuilderCache.Rent(Icon.Length + Text.Length + 1);
            try {
                TerminalText.AppendSanitized(builder, Icon, keepStyles: true);
                builder.Append(' ');
                TerminalText.AppendSanitized(builder, Text, keepStyles: true);
                Content = builder.ToString();
            }
            finally {
                StringBuilderCache.Return(builder);
            }
        }

        unicodeMeasurement = TextMeasurement.UseUnicode;
        contentCells = TerminalText.Measure(Content);
        paddingCells = TerminalText.Measure(Padding);
    }

    public string Text { get; }

    /// <summary>Glyph drawn before the text, separated by a space.</summary>
    public string Icon { get; }

    public TerminalColor Foreground { get; }

    public TerminalColor Background { get; }

    public StatusLineAlignment Alignment { get; }

    public string Padding { get; }

    public bool Bold { get; }

    public bool Italic { get; }

    public bool Underline { get; }

    /// <summary>SGR sequence applied before the block text; always starts with a reset.</summary>
    public string Style => style ??= Vt.Sgr(Foreground, Background, Bold, Italic, Underline);
    private string? style;

    /// <summary>Icon and text as displayed.</summary>
    public string Content { get; }
    // Definition colors override gradients; Duration retains its ordinary leading space.
    internal bool PreserveForeground { get; init; }
    internal bool PreserveBackground { get; init; }
    internal bool UseOrdinaryLeadingSpace { get; init; }
    /// <summary>Text may be shortened with a middle ellipsis when the row is too narrow.</summary>
    internal bool Shrinkable { get; init; }
    private Shortened? shortened;
    private sealed record Shortened(int TextCells, bool UnicodeMeasurement, StatusLineBlock Block);
    private readonly int contentCells;
    private readonly int paddingCells;
    private readonly bool unicodeMeasurement;
    internal string LeadingPadding => UseOrdinaryLeadingSpace && Padding.Length > 0 ? " " : Padding;
    internal int Cells {
        get {
            int content = contentCells;
            int padding = paddingCells;
            if (unicodeMeasurement != TextMeasurement.UseUnicode) {
                content = TerminalText.Measure(Content);
                padding = TerminalText.Measure(Padding);
            }

            return content + padding + (UseOrdinaryLeadingSpace && Padding.Length > 0 ? 1 : padding);
        }
    }

    public bool IsStale { get; private init; }
    public string? LastError { get; private init; }

    internal StatusLineBlock AsStale(string? error) => IsStale ? this : new StatusLineBlock(
        Text + " [stale]", Color.Goldenrod, Background, Alignment, Padding, Bold, Italic, Underline, Icon) {
        PreserveForeground = PreserveForeground,
        PreserveBackground = PreserveBackground,
        UseOrdinaryLeadingSpace = UseOrdinaryLeadingSpace,
        Shrinkable = Shrinkable,
        IsStale = true,
        LastError = error
    };

    /// <summary>Returns a copy whose text fits <paramref name="textCells"/>; the last result is cached for repeated renders.</summary>
    internal StatusLineBlock Shorten(int textCells) {
        bool unicode = TextMeasurement.UseUnicode;
        if (shortened is { } cached && cached.TextCells == textCells && cached.UnicodeMeasurement == unicode) {
            return cached.Block;
        }

        string text = TerminalText.Ellipsize(Text, textCells);
        if (ReferenceEquals(text, Text)) {
            return this;
        }

        var block = new StatusLineBlock(text, Foreground, Background, Alignment, Padding, Bold, Italic, Underline, Icon) {
            PreserveForeground = PreserveForeground,
            PreserveBackground = PreserveBackground,
            UseOrdinaryLeadingSpace = UseOrdinaryLeadingSpace,
            IsStale = IsStale,
            LastError = LastError
        };
        shortened = new Shortened(textCells, unicode, block);
        return block;
    }

    // Appearance changes do not change sanitized text or its measured width.
    private StatusLineBlock(StatusLineBlock source, StatusLineAlignment alignment, TerminalColor foreground,
        TerminalColor background, string padding, bool bold, bool italic, bool underline) {
        Text = source.Text;
        Icon = source.Icon;
        Content = source.Content;
        unicodeMeasurement = TextMeasurement.UseUnicode;
        bool sameMeasurement = unicodeMeasurement == source.unicodeMeasurement;
        contentCells = sameMeasurement ? source.contentCells : TerminalText.Measure(Content);
        Padding = padding;
        paddingCells = sameMeasurement && padding == source.Padding ? source.paddingCells : TerminalText.Measure(padding);
        Foreground = foreground;
        Background = background;
        Alignment = alignment;
        Bold = bold;
        Italic = italic;
        Underline = underline;
        if (foreground == source.Foreground && background == source.Background && bold == source.Bold &&
            italic == source.Italic && underline == source.Underline) {
            style = source.style;
        }

        PreserveForeground = source.PreserveForeground;
        PreserveBackground = source.PreserveBackground;
        UseOrdinaryLeadingSpace = source.UseOrdinaryLeadingSpace;
        Shrinkable = source.Shrinkable;
        IsStale = source.IsStale;
        LastError = source.LastError;
    }

    internal StatusLineBlock WithAppearance(StatusLineAlignment alignment, TerminalColor foreground, TerminalColor background) {
        if (alignment == Alignment && foreground == Foreground && background == Background) {
            return this;
        }

        return new StatusLineBlock(this, alignment, foreground, background, Padding, Bold, Italic, Underline);
    }

    internal StatusLineBlock WithPadding(string padding) {
        ArgumentNullException.ThrowIfNull(padding);
        string sanitized = TerminalText.Sanitize(padding, keepStyles: false);
        if (sanitized == Padding) {
            return this;
        }

        return new StatusLineBlock(this, Alignment, Foreground, Background, sanitized, Bold, Italic, Underline);
    }

    public override string ToString() => Content;

}
