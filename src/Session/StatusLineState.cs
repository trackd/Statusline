using System.Management.Automation;

namespace StatusLine;

/// <summary>
/// Snapshot of the PowerShell session that blocks are built from.
/// Passed as the first argument to script definition script blocks.
/// </summary>
public sealed record StatusLineState {
    // PowerShell exposes local enumeration through Get-Runspace, but its SDK GetRunspaces
    // overloads enumerate remote runspaces. Cache the same local snapshot getter the cmdlet uses.
    private static readonly Func<IReadOnlyList<System.Management.Automation.Runspaces.Runspace>>? LocalRunspaces =
        typeof(System.Management.Automation.Runspaces.Runspace).GetProperty("RunspaceList", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)?
            .GetMethod?.CreateDelegate<Func<IReadOnlyList<System.Management.Automation.Runspaces.Runspace>>>();
    /// <summary>PowerShell path of the current location, e.g. <c>C:\src</c> or <c>HKLM:\Software</c>.</summary>
    public string? Location { get; init; }

    /// <summary>Provider path when the current location is in the FileSystem provider.</summary>
    public string? FileSystemPath { get; init; }

    /// <summary>Global $LASTEXITCODE, i.e. the exit code of the last native command.</summary>
    public int? LastExitCode { get; init; }

    /// <summary>Duration captured at a prompt boundary or supplied to a snapshot.</summary>
    public TimeSpan? LastDuration { get; init; }

    /// <summary>Number of retained records in the current runspace's global $Error collection.</summary>
    public int ErrorCount { get; init; }
    /// <summary>Number of locations in the current default location stack.</summary>
    public int StackCount { get; init; }
    /// <summary>Current PowerShell nested prompt depth.</summary>
    public int NestedPromptLevel { get; init; }
    /// <summary>Number of runspaces in this process, including provider workers.</summary>
    public int RunspaceCount { get; init; }
    /// <summary>History ID reported by the current prompt invocation; null outside a captured prompt.</summary>
    public long? HistoryId { get; init; }

    /// <summary>
    /// Captures session facts on the calling runspace thread. Background providers consume the
    /// resulting immutable snapshot instead of accessing live PowerShell session state.
    /// </summary>
    internal static StatusLineState Capture(SessionState sessionState, SessionFacts facts = SessionFacts.All) {
        try {
            PathInfo? location = facts.HasFlag(SessionFacts.Location) ? sessionState.Path.CurrentLocation : null;
            return new StatusLineState {
                Location = location?.Path,
                ErrorCount = facts.HasFlag(SessionFacts.Errors) ? (sessionState.PSVariable.GetValue("global:Error") as System.Collections.ICollection)?.Count ?? 0 : 0,
                StackCount = facts.HasFlag(SessionFacts.Stack) ? sessionState.Path.LocationStack(string.Empty).Count : 0,
                NestedPromptLevel = facts.HasFlag(SessionFacts.NestedPrompt) && LanguagePrimitives.TryConvertTo(sessionState.PSVariable.GetValue("NestedPromptLevel"), out int depth) ? depth : 0,
                RunspaceCount = facts.HasFlag(SessionFacts.Runspaces) ? LocalRunspaces?.Invoke().Count ?? 0 : 0,
                FileSystemPath = location?.Provider.Name == "FileSystem" ? location.ProviderPath : null,
                LastExitCode = facts.HasFlag(SessionFacts.ExitCode) && sessionState.PSVariable.GetValue("global:LASTEXITCODE") is { } raw && LanguagePrimitives.TryConvertTo(raw, out int code) ? code : null,
            };
        }
        catch (InvalidOperationException) {
            // The session is mid-change (e.g. switching drives); the next tick will read it again.
            return new StatusLineState();
        }
    }
}
