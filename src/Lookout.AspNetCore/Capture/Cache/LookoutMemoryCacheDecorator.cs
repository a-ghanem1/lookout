using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lookout.Core;
using Lookout.Core.Schemas;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Lookout.AspNetCore.Capture.Cache;

/// <summary>
/// Decorates <see cref="IMemoryCache"/> to capture every Get / Set / Remove operation
/// as a <c>cache</c> <see cref="LookoutEntry"/>. Registered automatically by
/// <c>AddLookout()</c> when an <c>IMemoryCache</c> registration is found — requires that
/// <c>AddMemoryCache()</c> is called <em>before</em> <c>AddLookout()</c>.
/// </summary>
public sealed class LookoutMemoryCacheDecorator : IMemoryCache
{
    private readonly IMemoryCache _inner;
    private readonly ILookoutRecorder _recorder;
    private readonly LookoutOptions _options;

    public LookoutMemoryCacheDecorator(
        IMemoryCache inner,
        ILookoutRecorder recorder,
        IOptions<LookoutOptions> options)
    {
        _inner = inner;
        _recorder = recorder;
        _options = options.Value;
    }

    public bool TryGetValue(object key, out object? value)
    {
        if (!_options.Cache.CaptureMemoryCache)
            return _inner.TryGetValue(key, out value);

        var start = Stopwatch.GetTimestamp();
        var hit = _inner.TryGetValue(key, out value);
        RecordEntry("Get", key, ElapsedMs(start), hit: hit, valueType: value?.GetType().FullName);
        return hit;
    }

    public ICacheEntry CreateEntry(object key)
    {
        var start = Stopwatch.GetTimestamp();
        var entry = _inner.CreateEntry(key);

        if (!_options.Cache.CaptureMemoryCache)
            return entry;

        var redactedKey = RedactKey(key, _options.Cache.SensitiveKeyPatterns);

        return new LookoutCacheEntryProxy(entry, start, (durationMs, valueType) =>
            RecordEntryRaw("Set", redactedKey, durationMs, hit: null, valueType, valueBytes: null));
    }

    public void Remove(object key)
    {
        if (!_options.Cache.CaptureMemoryCache)
        {
            _inner.Remove(key);
            return;
        }

        var start = Stopwatch.GetTimestamp();
        _inner.Remove(key);
        RecordEntry("Remove", key, ElapsedMs(start), hit: null, valueType: null);
    }

    public void Dispose() => _inner.Dispose();

    private void RecordEntry(string operation, object key, double durationMs, bool? hit, string? valueType)
    {
        var redactedKey = RedactKey(key, _options.Cache.SensitiveKeyPatterns);
        RecordEntryRaw(operation, redactedKey, durationMs, hit, valueType, valueBytes: null);
    }

    private void RecordEntryRaw(string operation, string redactedKey, double durationMs, bool? hit, string? valueType, int? valueBytes)
    {
        var content = new CacheEntryContent(
            Operation: operation,
            Key: redactedKey,
            Hit: hit,
            DurationMs: durationMs,
            ValueType: valueType,
            ValueBytes: valueBytes);

        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cache.system"] = "memory",
            ["cache.key"] = redactedKey,
        };

        if (hit.HasValue)
            tags["cache.hit"] = hit.Value ? "true" : "false";

        if (valueType is not null)
            tags["cache.value-type"] = valueType;

        var entry = new LookoutEntry(
            Id: Guid.NewGuid(),
            Type: "cache",
            Timestamp: DateTimeOffset.UtcNow,
            RequestId: Activity.Current?.RootId,
            DurationMs: durationMs,
            Tags: tags,
            Content: JsonSerializer.Serialize(content, LookoutJson.Options));

        _recorder.Record(entry);
    }

    private static string RedactKey(object key, IList<Regex> patterns)
    {
        var keyStr = key?.ToString() ?? string.Empty;
        foreach (var pattern in patterns)
            if (pattern.IsMatch(keyStr))
                return "***";
        return keyStr;
    }

    private static double ElapsedMs(long startTimestamp)
    {
        var ticks = Stopwatch.GetTimestamp() - startTimestamp;
        return ticks * 1000.0 / Stopwatch.Frequency;
    }
}
