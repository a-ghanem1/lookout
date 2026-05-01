using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lookout.Core;

/// <summary>
/// Fire-and-forget recorder backed by a bounded <see cref="Channel{T}"/>.
/// When the channel is full the oldest entry is dropped and a throttled warning is logged.
/// </summary>
public sealed class ChannelLookoutRecorder : ILookoutRecorder, IDisposable
{
    private readonly Channel<LookoutEntry> _channel;
    private readonly int _capacity;
    private readonly LookoutOptions _options;
    private readonly ILogger<ChannelLookoutRecorder> _logger;
    private long _dropCount;

    // Stopwatch gate: emit at most one warning per second under sustained overflow.
    // Initialised to -1_001 so the first drop always fires a warning immediately.
    private readonly Stopwatch _warningGate = Stopwatch.StartNew();
    private long _lastWarningMs = -1_001L;

    public ChannelLookoutRecorder(
        IOptions<LookoutOptions> options,
        ILogger<ChannelLookoutRecorder> logger)
    {
        _options = options.Value;
        _capacity = _options.ChannelCapacity;
        _logger = logger;
        _channel = Channel.CreateBounded<LookoutEntry>(new BoundedChannelOptions(_capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <inheritdoc />
    public void Record(LookoutEntry entry)
    {
        if (_options.Tag is { } tag)
        {
            var mutableTags = entry.Tags.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            tag(entry, mutableTags);
            entry = entry with { Tags = mutableTags };
        }

        if (_options.Filter is { } filter && !filter(entry))
            return;

        // Pre-check fullness before write so we can count dropped entries.
        // DropOldest silently removes the oldest item and succeeds, so TryWrite always returns true.
        if (_channel.Reader.Count >= _capacity)
        {
            Interlocked.Increment(ref _dropCount);

            // Emit at most one warning per second; CAS ensures only one thread wins the window.
            var nowMs = _warningGate.ElapsedMilliseconds;
            var lastMs = Volatile.Read(ref _lastWarningMs);
            if (nowMs - lastMs >= 1_000 &&
                Interlocked.CompareExchange(ref _lastWarningMs, nowMs, lastMs) == lastMs)
            {
                _logger.LogWarning(
                    "Lookout capture pipeline is behind; oldest entries are being dropped. " +
                    "Consider increasing ChannelCapacity or reducing capture scope.");
            }
        }

        _channel.Writer.TryWrite(entry);
    }

    /// <summary>Channel reader consumed by the background flusher.</summary>
    internal ChannelReader<LookoutEntry> Reader => _channel.Reader;

    /// <summary>Running count of entries dropped due to a full channel.</summary>
    internal long DropCount => Volatile.Read(ref _dropCount);

    /// <inheritdoc />
    public void Dispose() => _channel.Writer.TryComplete();
}
