namespace Lookout.AspNetCore.Api;

/// <summary>Aggregate hit/miss/set/remove counts returned by <c>GET /lookout/api/entries/cache/summary</c>.</summary>
public sealed record CacheSummary(long Hits, long Misses, long Sets, long Removes, double HitRatio);
