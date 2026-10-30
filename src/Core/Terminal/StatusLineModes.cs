namespace StatusLine;

/// <summary>DEC DECSSDT status-display modes.</summary>
public enum StatusLineMode {
    Hidden = 0,
    Indicator = 1,
    HostWritable = 2,
}

/// <summary>Where Enable-StatusLine sends each refresh.</summary>
public enum StatusLineTarget {
    /// <summary>The DEC host-writable status line (DECSSDT/DECSASD escape sequences).</summary>
    HostWritable = 1,

    /// <summary>A text file, e.g. for Contour's indicator <c>{Command:Program=...}</c> placeholder.</summary>
    File = 2,
}
