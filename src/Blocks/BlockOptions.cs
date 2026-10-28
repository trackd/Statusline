namespace StatusLine;

/// <summary>Horizontal group a block is placed in.</summary>
public enum StatusLineAlignment {
    Left,
    Center,
    Right,
}

/// <summary>How blocks are drawn.</summary>
public enum SegmentStyle {
    /// <summary>Colored text separated by the separator string.</summary>
    Plain,

    /// <summary>Filled segments joined by powerline arrows; Center blocks use diamond caps.</summary>
    Powerline,

    /// <summary>Separate filled pills with rounded caps on both ends and a gap between blocks.</summary>
    Diamond,

    /// <summary>Filled segments joined by rounded Powerline separators.</summary>
    Rounded,
}

/// <summary>Glyph set used for built-in block icons.</summary>
public enum IconSet {
    /// <summary>Nerd Font glyphs; requires a patched font.</summary>
    NerdFont,

    /// <summary>Standard Unicode symbols that render in most fonts.</summary>
    Unicode,

    /// <summary>Short ASCII labels.</summary>
    None,
}

/// <summary>Built-in block kinds shared by the prompt, updater, and BlockSnapshots.Snapshot.</summary>
public enum StatusLineBlockKind {
    Directory,
    Git,
    Status,
    Duration,
    PowerShell,
    SystemMemory,
    ProcessMemory,
    Cpu,
    Gpu,
    Network,
    Battery,
    Weather,
    Uptime,
    Clock,
    Date,
    User,
    Host,
    Node,
    Python,
    Dotnet,
    Go,
    Rust,
    Java,
    Ruby,
    Package,
    VirtualEnv,
    Aws,
    Azure,
    Kubernetes,
    Docker,
    Os,
    Elevation,
    Errors,
    StackCount,
    NestedPrompt,
    Runspaces,
    HistoryId,
}
