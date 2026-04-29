using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lookout.Core;
using Lookout.Core.Schemas;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Lookout.AspNetCore.Capture.Cache;

/// <summary>
/// Decorates <see cref="IDistributedCache"/> to capture every Get / Set / Refresh / Remove
/// operation as a <c>cache</c> <see cref="LookoutEntry"/>. Registered automatically by
/// <c>AddLookout()</c> when an <c>IDistributedCache</c> registration is found — requires that
/// <c>AddDistributedMemoryCache()</c> (or equivalent) is called <em>before</em> <c>AddLookout()</c>.
/// </summary>
public sealed class LookoutDistributedCacheDecorator : IDistributedCache
{
    private readonly IDistributedCache _inner;
    private readonly ILookoutRecorder _recorder;
    private readonly LookoutOptions _options;
    private readonly string _providerType;

    public LookoutDistributedCacheDecorator(
        IDistributedCache inner,
        ILookoutRecorder recorder,
        IOptions<LookoutOptions> options)
    {
        _inner = inner;
        _recorder = recorder;
        _options = options.Value;
        _providerType = inner.GetType().Name;
    }

    public byte[]? Get(string key)
    {
        if (!_options.Cache.CaptureDistributedCache)
            return _inner.Get(key);

        var start = Stopwatch.GetTimestamp();
        var result = _inner.Get(key);
        RecordEntry("Get", key, ElapsedMs(start), hit: result != null, valueBytes: null);
        return result;
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        if (!_options.Cache.CaptureDistributedCache)
            return await _inner.GetAsync(key, token).ConfigureAwait(false);

        var start = Stopwatch.GetTimestamp();
        var result = await _inner.GetAsync(key, token).ConfigureAwait(false);
        RecordEntry("Get", key, ElapsedMs(start), hit: result != null, valueBytes: null);
        return result;
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        if (!_options.Cache.CaptureDistributedCache)
        {
            _inner.Set(key, value, options);
            return;
        }

        var start = Stopwatch.GetTimestamp();
        _inner.Set(key, value, options);
        RecordEntry("Set", key, ElapsedMs(start), hit: null, valueBytes: value.Length);
    }

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        if (!_options.Cache.CaptureDistributedCache)
        {
            await _inner.SetAsync(key, value, options, token).ConfigureAwait(false);
            return;
        }

        var start = Stopwatch.GetTimestamp();
        await _inner.SetAsync(key, value, options, token).ConfigureAwait(false);
        RecordEntry("Set", key, ElapsedMs(start), hit: null, valueBytes: value.Length);
    }

    public void Refresh(string key)
    {
        if (!_options.Cache.CaptureDistributedCache)
        {
            _inner.Refresh(key);
            return;
        }

        var start = Stopwatch.GetTimestamp();
        _inner.Refresh(key);
        RecordEntry("Refresh", key, ElapsedMs(start), hit: null, valueBytes: null);
    }

    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        if (!_options.Cache.CaptureDistributedCache)
        {
            await _inner.RefreshAsync(key, token).ConfigureAwait(false);
            return;
        }

        var start = Stopwatch.GetTimestamp();
        await _inner.RefreshAsync(key, token).ConfigureAwait(false);
        RecordEntry("Refresh", key, ElapsedMs(start), hit: null, valueBytes: null);
    }

    public void Remove(string key)
    {
        if (!_options.Cache.CaptureDistributedCache)
        {
            _inner.Remove(key);
            return;
        }

        var start = Stopwatch.GetTimestamp();
        _inner.Remove(key);
        RecordEntry("Remove", key, ElapsedMs(start), hit: null, valueBytes: null);
    }

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        if (!_options.Cache.CaptureDistributedCache)
        {
            await _inner.RemoveAsync(key, token).ConfigureAwait(false);
            return;
        }

        var start = Stopwatch.GetTimestamp();
        await _inner.RemoveAsync(key, token).ConfigureAwait(false);
        RecordEntry("Remove", key, ElapsedMs(start), hit: null, valueBytes: null);
    }

    private void RecordEntry(string operation, string key, double durationMs, bool? hit, int? valueBytes)
    {
        var redactedKey = RedactKey(key, _options.Cache.SensitiveKeyPatterns);

        var content = new CacheEntryContent(
            Operation: operation,
            Key: redactedKey,
            Hit: hit,
            DurationMs: durationMs,
            ValueType: null,
            ValueBytes: valueBytes);

        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cache.system"] = "distributed",
            ["cache.provider"] = _providerType,
            ["cache.key"] = redactedKey,
        };

        if (hit.HasValue)
            tags["cache.hit"] = hit.Value ? "true" : "false";

        if (valueBytes.HasValue)
            tags["cache.value-bytes"] = valueBytes.Value.ToString();

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

    private static string RedactKey(string key, IList<Regex> patterns)
    {
        foreach (var pattern in patterns)
            if (pattern.IsMatch(key))
                return "***";
        return key;
    }

    private static double ElapsedMs(long startTimestamp)
    {
        var ticks = Stopwatch.GetTimestamp() - startTimestamp;
        return ticks * 1000.0 / Stopwatch.Frequency;
    }
}
