using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Lookout.AspNetCore.Capture.Cache;

/// <summary>
/// Wraps an <see cref="ICacheEntry"/> and invokes a callback on <see cref="Dispose"/>
/// (the moment the entry is committed to the cache) so the decorator can record a Set entry.
/// </summary>
internal sealed class LookoutCacheEntryProxy : ICacheEntry
{
    private readonly ICacheEntry _inner;
    private readonly long _startTimestamp;
    private readonly Action<double, string?> _onCommit;
    private bool _disposed;

    internal LookoutCacheEntryProxy(ICacheEntry inner, long startTimestamp, Action<double, string?> onCommit)
    {
        _inner = inner;
        _startTimestamp = startTimestamp;
        _onCommit = onCommit;
    }

    public object Key => _inner.Key;

    public object? Value
    {
        get => _inner.Value;
        set => _inner.Value = value;
    }

    public DateTimeOffset? AbsoluteExpiration
    {
        get => _inner.AbsoluteExpiration;
        set => _inner.AbsoluteExpiration = value;
    }

    public TimeSpan? AbsoluteExpirationRelativeToNow
    {
        get => _inner.AbsoluteExpirationRelativeToNow;
        set => _inner.AbsoluteExpirationRelativeToNow = value;
    }

    public TimeSpan? SlidingExpiration
    {
        get => _inner.SlidingExpiration;
        set => _inner.SlidingExpiration = value;
    }

    public IList<IChangeToken> ExpirationTokens => _inner.ExpirationTokens;

    public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks => _inner.PostEvictionCallbacks;

    public CacheItemPriority Priority
    {
        get => _inner.Priority;
        set => _inner.Priority = value;
    }

    public long? Size
    {
        get => _inner.Size;
        set => _inner.Size = value;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var valueType = _inner.Value?.GetType().FullName;
        _inner.Dispose();
        var elapsed = ElapsedMs(_startTimestamp);
        _onCommit(elapsed, valueType);
    }

    private static double ElapsedMs(long startTimestamp)
    {
        var ticks = System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp;
        return ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }
}
