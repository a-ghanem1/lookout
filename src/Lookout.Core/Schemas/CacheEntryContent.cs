namespace Lookout.Core.Schemas;

/// <summary>
/// Serialized body for <c>cache</c> entries produced by the memory-cache and
/// distributed-cache decorators.
/// </summary>
public sealed record CacheEntryContent(
    string Operation,
    string Key,
    bool? Hit,
    double DurationMs,
    string? ValueType,
    int? ValueBytes);
