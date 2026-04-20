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
    private readonly ILogger<ChannelLookoutRecorder> _logger;
    private long _dropCount;

    public ChannelLookoutRecorder(
        IOptions<LookoutOptions> options,
        ILogger<ChannelLookoutRecorder> logger)
    {
        _capacity = options.Value.ChannelCapacity;
        _logger = logger;
        _channel = Channel.CreateBounded<LookoutEntry>(new BoundedChannelOptions(_capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    /// <inheritdoc />
    public void Record(LookoutEntry entry)
    {
        // Pre-check fullness before write so we can count dropped entries.
        // DropOldest silently removes the oldest item and succeeds, so TryWrite always returns true.
        if (_channel.Reader.Count >= _capacity)
        {
            var drops = Interlocked.Increment(ref _dropCount);
            if (drops == 1 || drops % 1000 == 0)
                _logger.LogWarning(
                    "Lookout channel is full; {DropCount} {Noun} dropped.",
                    drops,
                    drops == 1 ? "entry" : "entries");
        }

        _channel.Writer.TryWrite(entry);
    }

    /// <summary>Channel reader consumed by the background flusher (M2.4).</summary>
    internal ChannelReader<LookoutEntry> Reader => _channel.Reader;

    /// <summary>Running count of entries dropped due to a full channel.</summary>
    internal long DropCount => Volatile.Read(ref _dropCount);

    /// <inheritdoc />
    public void Dispose() => _channel.Writer.TryComplete();
}
