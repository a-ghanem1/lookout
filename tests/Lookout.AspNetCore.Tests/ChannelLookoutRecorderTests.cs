using FluentAssertions;
using Lookout.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lookout.AspNetCore.Tests;

public sealed class ChannelLookoutRecorderTests : IDisposable
{
    private readonly ChannelLookoutRecorder _sut;

    public ChannelLookoutRecorderTests()
    {
        _sut = Build(capacity: 3);
    }

    public void Dispose() => _sut.Dispose();

    // ── helpers ──────────────────────────────────────────────────────────────

    private static ChannelLookoutRecorder Build(int capacity, ILogger<ChannelLookoutRecorder>? logger = null) =>
        new(Options.Create(new LookoutOptions { ChannelCapacity = capacity }),
            logger ?? NullLogger<ChannelLookoutRecorder>.Instance);

    private static LookoutEntry MakeEntry(string type = "test") =>
        new(Guid.NewGuid(), type, DateTimeOffset.UtcNow, null, null,
            new Dictionary<string, string>(), "{}");

    // ── enqueue ───────────────────────────────────────────────────────────────

    [Fact]
    public void Record_EnqueuesEntry_ReaderSeesIt()
    {
        var entry = MakeEntry();

        _sut.Record(entry);

        _sut.Reader.TryRead(out var read).Should().BeTrue();
        read.Should().Be(entry);
    }

    // ── FIFO order ────────────────────────────────────────────────────────────

    [Fact]
    public void Record_MultipleEntries_ReaderSeesThemInFifoOrder()
    {
        var e1 = MakeEntry("a");
        var e2 = MakeEntry("b");
        var e3 = MakeEntry("c");

        _sut.Record(e1);
        _sut.Record(e2);
        _sut.Record(e3);

        _sut.Reader.TryRead(out var r1);
        _sut.Reader.TryRead(out var r2);
        _sut.Reader.TryRead(out var r3);

        r1.Should().Be(e1);
        r2.Should().Be(e2);
        r3.Should().Be(e3);
    }

    // ── drop-oldest under pressure ────────────────────────────────────────────

    [Fact]
    public void Record_WhenChannelFull_DropsOldestEntry()
    {
        // Fill to capacity (3) with e1..e3, then push e4.
        // e1 (oldest) should be evicted; channel should contain e2, e3, e4.
        var e1 = MakeEntry("oldest");
        var e2 = MakeEntry("middle");
        var e3 = MakeEntry("newest-before-overflow");
        var e4 = MakeEntry("overflow");

        _sut.Record(e1);
        _sut.Record(e2);
        _sut.Record(e3);
        _sut.Record(e4); // triggers DropOldest → e1 evicted

        var drained = new List<LookoutEntry>();
        while (_sut.Reader.TryRead(out var item))
            drained.Add(item);

        drained.Should().HaveCount(3);
        drained.Should().NotContain(e1, "oldest entry should have been evicted");
        drained.Should().ContainInOrder(e2, e3, e4);
    }

    // ── drop counter ──────────────────────────────────────────────────────────

    [Fact]
    public void Record_WhenChannelFull_IncrementsDropCount()
    {
        // Fill to capacity
        _sut.Record(MakeEntry());
        _sut.Record(MakeEntry());
        _sut.Record(MakeEntry());

        _sut.DropCount.Should().Be(0);

        // One more triggers a drop
        _sut.Record(MakeEntry());

        _sut.DropCount.Should().Be(1);
    }

    [Fact]
    public void Record_MultipleOverflows_DropCountAccumulates()
    {
        using var recorder = Build(capacity: 1);

        recorder.Record(MakeEntry()); // channel: [e1]
        recorder.Record(MakeEntry()); // channel full → drop e1, count=1, channel: [e2]
        recorder.Record(MakeEntry()); // channel full → drop e2, count=2, channel: [e3]

        recorder.DropCount.Should().Be(2);
    }

    // ── warning throttle ──────────────────────────────────────────────────────

    [Fact]
    public void Record_SustainedOverflow_EmitsAtMostOneWarningWithinOneSecond()
    {
        // All 50 drops happen in <<1 ms — only one warning should fire.
        var logger = new CapturingLogger<ChannelLookoutRecorder>();
        using var recorder = Build(capacity: 1, logger);

        recorder.Record(MakeEntry()); // fills to capacity

        for (var i = 0; i < 50; i++)
            recorder.Record(MakeEntry()); // every call is a drop

        recorder.DropCount.Should().Be(50);

        logger.Warnings.Should().HaveCount(1,
            "the Stopwatch gate must suppress repeated warnings within a 1-second window");
        logger.Warnings[0].Should().Contain("capture pipeline is behind");
    }
}

/// <summary>Captures log messages for assertion without pulling in a test-logging package.</summary>
file sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Warnings { get; } = [];

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Warning)
            Warnings.Add(formatter(state, exception));
    }

    public bool IsEnabled(LogLevel logLevel) => true;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}
