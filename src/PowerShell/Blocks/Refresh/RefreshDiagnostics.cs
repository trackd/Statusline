using System.Management.Automation.Runspaces;

namespace StatusLine;

/// <summary>Read-only diagnostics for shared built-in caches and this runspace's script definitions.</summary>
public static class RefreshDiagnostics {
    public static string? LastStatusLineError => StatusLineUpdater.LastError;
    public static bool IsIdle => StatusLineUpdater.IsIdle;

    /// <summary>Status of a script definition's execution in the calling runspace.</summary>
    public static SegmentRefreshStatus GetScriptStatus(StatusLineBlockDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Kind is not null) {
            throw new ArgumentException("Built-in collections are listed by GetSegments; script status requires a script definition.", nameof(definition));
        }

        return definition.GetDiagnostics(Runspace.DefaultRunspace?.InstanceId ?? Guid.Empty);
    }

    public static SegmentRefreshStatus[] GetSegments() =>
    [.. BuiltInRefresh.Diagnostics(),
        .. ScriptBlockRuntime.ForRunspace(Runspace.DefaultRunspace?.InstanceId ?? Guid.Empty).Select(GetScriptStatus)];
}
