using System.Collections.Frozen;
using System.Drawing;
using System.Globalization;
using System.Reflection;

namespace StatusLine;

/// <summary>Named colors exposed as static properties on <see cref="Color"/> (system colors excluded).</summary>
internal static class NamedColors {
    public static readonly FrozenDictionary<string, Color> ByName = typeof(Color)
        .GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Where(property => property.PropertyType == typeof(Color))
        .ToFrozenDictionary(property => property.Name, property => (Color)property.GetValue(null)!, StringComparer.OrdinalIgnoreCase);

    public static readonly string[] Names = [.. ByName.Keys.Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Parses hex colors or a named color.</summary>
    public static Color Parse(string value) {
        if (ByName.TryGetValue(value, out Color named)) {
            return named;
        }

        scoped ReadOnlySpan<char> hex = value.AsSpan();
        if (hex.StartsWith("#")) {
            hex = hex[1..];
        }
        else if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            hex = hex[2..];
        }

        Span<char> expanded = stackalloc char[8];
        if (hex.Length is 3 or 4) {
            for (int index = 0; index < hex.Length; index++) {
                expanded[index * 2] = expanded[(index * 2) + 1] = hex[index];
            }

            hex = expanded[..(hex.Length * 2)];
        }

        if (hex.Length < 6) {
            throw new ArgumentException("A hex color must supply red, green and blue components.", nameof(value));
        }

        if (hex.Length % 2 != 0) {
            throw new ArgumentException($"Invalid color value: {value}.", nameof(value));
        }

        foreach (char character in hex) {
            if (!char.IsAsciiHexDigit(character)) {
                throw new ArgumentException($"Invalid color value: {value}.", nameof(value));
            }
        }
        // SGR uses RGB. Preserve existing handling of additional bytes (e.g. RGBA).
        return Color.FromArgb(
            byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }
}
