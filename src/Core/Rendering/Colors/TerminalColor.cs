using System.Drawing;

namespace StatusLine;

/// <summary>A terminal default, explicit RGB color, or theme-resolved console palette color.</summary>
public readonly record struct TerminalColor {
    private readonly Color color;
    public ConsoleColor? Palette { get; }
    public bool IsEmpty => Palette is null && color.IsEmpty;
    public Color Rgb => Palette is null ? color : throw new InvalidOperationException("A console palette color has no known RGB value.");

    public TerminalColor(Color value) {
        color = Validate(value);
    }

    public TerminalColor(ConsoleColor value) {
        if (!Enum.IsDefined(value)) {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Palette = value;
    }

    internal static Color Validate(Color value) {
        if (value.IsNamedColor && !value.IsKnownColor && !value.IsEmpty) {
            throw new ArgumentException($"Unknown drawing color: {value.Name}.", nameof(value));
        }

        return value;
    }

    // ConsoleColor uses BGR bit order; ANSI uses RGB bit order, with brightness in bit 3.
    internal int PaletteIndex => Palette is { } value
        ? ((int)value & 10) | (((int)value & 1) << 2) | (((int)value & 4) >> 2) : -1;

    public static implicit operator TerminalColor(Color value) => new(value);

    public static implicit operator TerminalColor(ConsoleColor value) => new(value);
    public override string ToString() => Palette?.ToString() ?? color.ToString();
}
