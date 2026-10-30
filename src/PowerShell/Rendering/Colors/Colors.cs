using System.Drawing;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace StatusLine;

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
