namespace StatusLine;

/// <summary>Last refresh outcome; IsStale means the last attempt failed or exceeded its deadline.</summary>
public sealed record SegmentRefreshStatus(string Name, string Path, DateTime? LastUpdated, string? LastError,
    bool IsStale, bool IsRefreshing, long CollectionId, int TimeoutMilliseconds, Guid? Owner);
