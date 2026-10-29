namespace StatusLine;

/// <summary>
/// Snapshot of the PowerShell session that blocks are built from.
/// Passed as the first argument to script definition script blocks.
/// </summary>
public sealed partial record StatusLineState {
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

}
