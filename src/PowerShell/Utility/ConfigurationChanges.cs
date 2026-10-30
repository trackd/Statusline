using System.Collections;
using System.Management.Automation;
using System.Reflection;

namespace StatusLine.Configuration;

/// <summary>Common validation and block-list edits, used only when configuration changes.</summary>
internal static class ConfigurationChanges {
    internal static T Apply<T>(T snapshot, IDictionary changes) where T : class {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (DictionaryEntry entry in changes) {
            string name = LanguagePrimitives.ConvertTo<string>(entry.Key);
            PropertyInfo? property = typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property is null || property.SetMethod is null) {
                throw new ArgumentException($"Unknown configuration setting '{name}'.", nameof(changes));
            }

            object? value = entry.Value is PSObject wrapped ? wrapped.BaseObject : entry.Value;
            property.SetValue(snapshot, property.PropertyType == typeof(object) ? value :
                LanguagePrimitives.ConvertTo(value, property.PropertyType, System.Globalization.CultureInfo.InvariantCulture));
        }

        return snapshot;
    }

    internal static string Group(string group, bool prompt) {
        ArgumentNullException.ThrowIfNull(group);
        return group.ToLowerInvariant() switch {
            "left" => "Left",
            "right" => "Right",
            "center" when !prompt => "Center",
            "input" when prompt => "Input",
            "transient" when prompt => "TransientBlock",
            "block" => "Block",
            _ => throw new ArgumentException($"Unsupported block group '{group}'.", nameof(group)),
        };
    }

    internal static object[] Edit(object configuration, string property, object block, bool remove) {
        ArgumentNullException.ThrowIfNull(block);
        object[] values = (object[])configuration.GetType().GetProperty(property)!.GetValue(configuration)!;
        if (!remove) {
            return [.. values, block];
        }

        object target = Unwrap(block);
        return [.. values.Where(value => !Matches(Unwrap(value), target))];
    }

    private static object Unwrap(object value) => value is PSObject wrapped ? wrapped.BaseObject : value;

    private static bool Matches(object value, object target) =>
        ReferenceEquals(value, target) || value.Equals(target) ||
        ((value is string or StatusLineBlockKind) && (target is string or StatusLineBlockKind) &&
            string.Equals(value.ToString(), target.ToString(), StringComparison.OrdinalIgnoreCase));
}
