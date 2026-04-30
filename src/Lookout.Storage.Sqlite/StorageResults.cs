namespace Lookout.Storage.Sqlite;

/// <summary>A ranked FTS5 search hit with a content snippet.</summary>
public sealed record SearchResult(
    Guid Id,
    string Type,
    DateTimeOffset Timestamp,
    string? RequestId,
    string Snippet);

/// <summary>One time bucket in the log-volume histogram.</summary>
public sealed record LogHistogramBucket(
    long From,
    long To,
    long Trace,
    long Debug,
    long Information,
    long Warning,
    long Error,
    long Critical);

/// <summary>Per-key cache access statistics.</summary>
public sealed record CacheKeyStats(
    string Key,
    long Hits,
    long Misses,
    long Sets,
    double HitRatio);
