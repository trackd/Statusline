using System.Collections;
using StatusLine.Configuration;

namespace StatusLine;

/// <summary>Read or update the running statusline without restarting its worker.</summary>
public static class StatusBar {
    public static StatusBarConfiguration? Configuration => StatusLineUpdater.GetConfiguration();
    /// <summary>Live PowerShell property view; target/path remain lifecycle settings.</summary>
    public static System.Management.Automation.PSObject? Settings {
        get {
            if (Configuration is null) {
                return null;
            }

            return LiveSettings.Create(typeof(StatusBar), typeof(StatusBarConfiguration),
                nameof(StatusBarConfiguration.Target), nameof(StatusBarConfiguration.OutputPath));
        }
    }

    public static void Configure(IDictionary changes) =>
        Configure(ConfigurationChanges.Apply(Required(), changes));

    public static void Configure(StatusBarConfiguration configuration) => StatusLineUpdater.Configure(configuration);

    public static void SetColors(object? startColor, object? endColor) =>
        Configure(new Hashtable { [nameof(StatusBarConfiguration.StartColor)] = startColor, [nameof(StatusBarConfiguration.EndColor)] = endColor });

    public static void SetBlocks(string group, object[] blocks) =>
        Configure(new Hashtable { [ConfigurationChanges.Group(group, false)] = blocks });

    public static void AddBlock(string group, object block) => Edit(group, block, false);
    public static void RemoveBlock(string group, object block) => Edit(group, block, true);

    private static void Edit(string group, object block, bool remove) {
        StatusBarConfiguration configuration = Required();
        string property = ConfigurationChanges.Group(group, false);
        Configure(new Hashtable { [property] = ConfigurationChanges.Edit(configuration, property, block, remove) });
    }

    private static StatusBarConfiguration Required() =>
        Configuration ?? throw new InvalidOperationException("No running statusline is owned by the calling runspace.");
}
