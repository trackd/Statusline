using System.Collections.Frozen;
using System.Drawing;
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Language;
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
/// <summary>Converts #RRGGBB strings and color names into <see cref="Color"/> values.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ColorTransformAttribute : ArgumentTransformationAttribute {
    public override object Transform(EngineIntrinsics engineIntrinsics, object inputData) {
        object value = inputData is PSObject wrapped ? wrapped.BaseObject : inputData;
        try {
            Color result = value switch {
                Color color => TerminalColor.Validate(color),
                TerminalColor color => color.Rgb,
                string text => NamedColors.Parse(text),
                _ => throw new ArgumentException($"Invalid color value: {value}. Expected a valid color name or hex code."),
            };
            return result;
        }
        catch (ArgumentException exception) {
            throw new ArgumentTransformationMetadataException(exception.Message, exception);
        }
    }
}

/// <summary>Converts block colors while retaining theme-resolved console palette entries.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class TerminalColorTransformAttribute : ArgumentTransformationAttribute {
    public override object Transform(EngineIntrinsics engineIntrinsics, object inputData) {
        try {
            return BlockSnapshots.TerminalColorValue(inputData);
        }
        catch (ArgumentException exception) {
            throw new ArgumentTransformationMetadataException(exception.Message, exception);
        }
    }
}
/// <summary>Completes color names, previewing each one in its own color.</summary>
public sealed class ColorCompleter : IArgumentCompleter {
    public IEnumerable<CompletionResult> CompleteArgument(
        string commandName,
        string parameterName,
        string wordToComplete,
        CommandAst commandAst,
        System.Collections.IDictionary fakeBoundParameters) {
        string word = wordToComplete.Trim('\'', '"');
        var pattern = new WildcardPattern(WildcardPattern.ContainsWildcardCharacters(word) ? word : word + "*", WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant);
        foreach (string name in NamedColors.Names) {
            if (!pattern.IsMatch(name)) {
                continue;
            }

            Color color = NamedColors.ByName[name];
            yield return new CompletionResult(
                name,
                Vt.RgbForeground(color, name),
                CompletionResultType.ParameterValue,
                $"{name} R: {color.R}, G: {color.G}, B: {color.B}");
        }
    }
}
