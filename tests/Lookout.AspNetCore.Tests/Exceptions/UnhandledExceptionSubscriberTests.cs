using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Lookout.AspNetCore.Capture.Exceptions;
using Lookout.Core;
using Lookout.Core.Schemas;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lookout.AspNetCore.Tests.Exceptions;

public sealed class UnhandledExceptionSubscriberTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private sealed class CapturingRecorder : ILookoutRecorder
    {
        private readonly List<LookoutEntry> _entries = [];
        public IReadOnlyList<LookoutEntry> Entries => _entries;
        public void Record(LookoutEntry entry) => _entries.Add(entry);
    }

    private static (UnhandledExceptionDiagnosticSubscriber Subscriber, CapturingRecorder Recorder)
        Build(Action<LookoutOptions>? configure = null)
    {
        var opts = new LookoutOptions();
        configure?.Invoke(opts);
        var recorder = new CapturingRecorder();
        var sub = new UnhandledExceptionDiagnosticSubscriber(
            recorder,
            Options.Create(opts),
            NullLogger<UnhandledExceptionDiagnosticSubscriber>.Instance);
        return (sub, recorder);
    }

    private static ExceptionEntryContent Deserialize(LookoutEntry entry) =>
        JsonSerializer.Deserialize<ExceptionEntryContent>(entry.Content, LookoutJson.Options)!;

    // ── basic recording ───────────────────────────────────────────────────────

    [Fact]
    public void OnUnhandledException_RecordsEntry_WithExceptionType()
    {
        var (sub, recorder) = Build();
        var ex = new InvalidOperationException("boom");

        sub.OnUnhandledException(ex);

        recorder.Entries.Should().ContainSingle(e => e.Type == "exception");
        Deserialize(recorder.Entries.Single()).ExceptionType
            .Should().Be(typeof(InvalidOperationException).FullName);
    }

    [Fact]
    public void OnUnhandledException_RecordsEntry_HandledFalse()
    {
        var (sub, recorder) = Build();

        sub.OnUnhandledException(new InvalidOperationException());

        var content = Deserialize(recorder.Entries.Single());
        content.Handled.Should().BeFalse();
        recorder.Entries.Single().Tags["exception.handled"].Should().Be("false");
    }

    [Fact]
    public void OnUnhandledException_RecordsFilteredStack()
    {
        var (sub, recorder) = Build();
        Exception ex;
        try { throw new InvalidOperationException("stack test"); }
        catch (InvalidOperationException e) { ex = e; }

        sub.OnUnhandledException(ex);

        var content = Deserialize(recorder.Entries.Single());
        content.Stack.Should().NotBeEmpty();
        content.Stack.Should().Contain(
            f => f.Method.Contains(nameof(OnUnhandledException_RecordsFilteredStack)),
            "the test method must appear in the captured stack");
    }

    // ── dedup via registry ────────────────────────────────────────────────────

    [Fact]
    public void OnUnhandledException_SkipsEntry_WhenAlreadyStampedByHandler()
    {
        var (sub, recorder) = Build();
        var ex = new InvalidOperationException();
        ExceptionRegistry.TryStamp(ex);

        sub.OnUnhandledException(ex);

        recorder.Entries.Should().BeEmpty("already-stamped exceptions must not produce a second entry");
    }

    [Fact]
    public void OnUnhandledException_RecordsEntry_WhenNotYetStamped()
    {
        var (sub, recorder) = Build();
        var ex = new InvalidOperationException();
        // No stamp — fresh exception

        sub.OnUnhandledException(ex);

        recorder.Entries.Should().ContainSingle();
    }

    // ── IgnoreExceptionTypes ──────────────────────────────────────────────────

    [Fact]
    public void OnUnhandledException_Drops_WhenTypeIsIgnored()
    {
        var (sub, recorder) = Build(o =>
            o.Exceptions.IgnoreExceptionTypes = ["System.OperationCanceledException"]);

        sub.OnUnhandledException(new OperationCanceledException());

        recorder.Entries.Should().BeEmpty();
    }

    [Fact]
    public void OnUnhandledException_Drops_WhenSubclassOfIgnoredType()
    {
        var (sub, recorder) = Build(o =>
            o.Exceptions.IgnoreExceptionTypes = ["System.OperationCanceledException"]);

        sub.OnUnhandledException(new TaskCanceledException());

        recorder.Entries.Should().BeEmpty("subclasses of ignored types must also be dropped");
    }

    // ── Capture = false ───────────────────────────────────────────────────────

    [Fact]
    public void OnUnhandledException_NoEntry_WhenCaptureDisabled()
    {
        var (sub, recorder) = Build(o => o.Exceptions.Capture = false);

        sub.OnUnhandledException(new InvalidOperationException());

        recorder.Entries.Should().BeEmpty();
    }

    // ── failure safety ────────────────────────────────────────────────────────

    [Fact]
    public void OnUnhandledException_DoesNotThrow_WhenRecorderFails()
    {
        var opts = new LookoutOptions();
        var badRecorder = new ThrowingRecorder();
        var sub = new UnhandledExceptionDiagnosticSubscriber(
            badRecorder,
            Options.Create(opts),
            NullLogger<UnhandledExceptionDiagnosticSubscriber>.Instance);

        var act = () => sub.OnUnhandledException(new InvalidOperationException());

        act.Should().NotThrow("capture failures must be swallowed");
    }

    private sealed class ThrowingRecorder : ILookoutRecorder
    {
        public void Record(LookoutEntry entry) => throw new Exception("recorder exploded");
    }

    // ── lifecycle ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_AndStopAsync_CompleteWithoutError()
    {
        var (sub, _) = Build();

        await sub.StartAsync(CancellationToken.None);
        await sub.StopAsync(CancellationToken.None);
    }

    // ── request correlation ───────────────────────────────────────────────────

    [Fact]
    public void OnUnhandledException_RequestId_IsNullOutsideActivity()
    {
        var (sub, recorder) = Build();

        var saved = Activity.Current;
        Activity.Current = null;
        try { sub.OnUnhandledException(new InvalidOperationException()); }
        finally { Activity.Current = saved; }

        recorder.Entries.Single().RequestId.Should().BeNull();
    }

    [Fact]
    public void OnUnhandledException_RequestId_UsesActivityRootIdInsideRequest()
    {
        var (sub, recorder) = Build();
        using var activity = new Activity("TestRequest").Start();

        sub.OnUnhandledException(new InvalidOperationException());

        recorder.Entries.Single().RequestId.Should().Be(activity.RootId);
    }
}
