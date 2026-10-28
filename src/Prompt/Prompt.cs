using System.Collections;
using StatusLine.Configuration;

namespace StatusLine;

/// <summary>Read or update the installed prompt in the calling runspace.</summary>
public static class Prompt {
    public static PromptConfiguration? Configuration => PromptSession.GetConfiguration();
    /// <summary>Live PowerShell property view; configuration snapshots remain detached.</summary>
    public static System.Management.Automation.PSObject? Settings {
        get {
            if (Configuration is null) {
                return null;
            }

            return LiveSettings.Create(typeof(Prompt), typeof(PromptConfiguration),
                nameof(PromptConfiguration.ShellIntegration), nameof(PromptConfiguration.ShellContext), nameof(PromptConfiguration.TerminalProgress));
        }
    }
    public static void Configure(IDictionary changes) {
        PromptConfiguration replacement = ConfigurationChanges.Apply(Required(), changes);
        bool Changed(string name) => changes.Keys.Cast<object>().Any(key => string.Equals(key.ToString(), name, StringComparison.OrdinalIgnoreCase));
        if (Changed(nameof(PromptConfiguration.TransientBlock)) && !Changed(nameof(PromptConfiguration.Transient)) && replacement.TransientBlock is { Length: > 0 }) {
            replacement = replacement with { Transient = true };
        }

        Configure(replacement);
    }
    public static void Configure(PromptConfiguration configuration) => PromptSession.Configure(configuration);

    public static void SetColors(object? startColor, object? endColor) =>
        Configure(new Hashtable { [nameof(PromptConfiguration.StartColor)] = startColor, [nameof(PromptConfiguration.EndColor)] = endColor });

    public static void SetBlocks(string group, object[] blocks) =>
        Configure(new Hashtable { [ConfigurationChanges.Group(group, true)] = blocks });

    public static void AddBlock(string group, object block) => Edit(group, block, false);
    public static void RemoveBlock(string group, object block) => Edit(group, block, true);

    private static void Edit(string group, object block, bool remove) {
        PromptConfiguration configuration = Required();
        string property = ConfigurationChanges.Group(group, true);
        Configure(new Hashtable { [property] = ConfigurationChanges.Edit(configuration, property, block, remove) });
    }

    private static PromptConfiguration Required() =>
        Configuration ?? throw new InvalidOperationException("Enable-Prompt must be called before modifying its configuration.");
}
