using System.Text.Json;
using FluentAssertions;
using Lookout.AspNetCore.Capture.Exceptions;
using Lookout.Core;
using Lookout.Core.Schemas;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lookout.AspNetCore.Tests.Exceptions;

public sealed class LookoutExceptionHandlerTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private sealed class CapturingRecorder : ILookoutRecorder
    {
        private readonly List<LookoutEntry> _entries = [];
        public IReadOnlyList<LookoutEntry> Entries => _entries;
        public void Record(LookoutEntry entry) => _entries.Add(entry);
    }

    private static (LookoutExceptionHandler Handler, CapturingRecorder Recorder)
        Build(Action<LookoutOptions>? configure = null)
    {
        var opts = new LookoutOptions();
        configure?.Invoke(opts);
        var recorder = new CapturingRecorder();
        var handler = new LookoutExceptionHandler(
            recorder,
            Options.Create(opts),
            NullLogger<LookoutExceptionHandler>.Instance);
        return (handler, recorder);
    }

    private static ExceptionEntryContent Deserialize(LookoutEntry entry) =>
        JsonSerializer.Deserialize<ExceptionEntryContent>(entry.Content, LookoutJson.Options)!;

    private static async Task<bool> Handle(LookoutExceptionHandler handler, Exception exception)
    {
        var ctx = new DefaultHttpContext();
        return await handler.TryHandleAsync(ctx, exception, CancellationToken.None);
    }

    // ── always returns false ──────────────────────────────────────────────────

    [Fact]
    public async Task TryHandleAsync_AlwaysReturnsFalse_SoHostHandlingContinues()
    {
        var (handler, _) = Build();

        var result = await Handle(handler, new InvalidOperationException("test"));

        result.Should().BeFalse("Lookout must not suppress the exception");
    }

    // ── recording ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task TryHandleAsync_RecordsEntry_WithExceptionType()
    {
        var (handler, recorder) = Build();
        var ex = new InvalidOperationException("boom");

        await Handle(handler, ex);

        recorder.Entries.Should().ContainSingle(e => e.Type == "exception");
        Deserialize(recorder.Entries.Single()).ExceptionType
            .Should().Be(typeof(InvalidOperationException).FullName);
    }

    [Fact]
    public async Task TryHandleAsync_RecordsEntry_WithMessage()
    {
        var (handler, recorder) = Build();
        var ex = new InvalidOperationException("something went wrong");

        await Handle(handler, ex);

        Deserialize(recorder.Entries.Single()).Message.Should().Be("something went wrong");
    }

    [Fact]
    public async Task TryHandleAsync_RecordsEntry_HandledTrue()
    {
        var (handler, recorder) = Build();

        await Handle(handler, new InvalidOperationException());

        var content = Deserialize(recorder.Entries.Single());
        content.Handled.Should().BeTrue();
        recorder.Entries.Single().Tags["exception.handled"].Should().Be("true");
    }

    [Fact]
    public async Task TryHandleAsync_Tags_ExceptionTypePresent()
    {
        var (handler, recorder) = Build();
        var ex = new ArgumentNullException("param");

        await Handle(handler, ex);

        recorder.Entries.Single().Tags["exception.type"]
            .Should().Be(typeof(ArgumentNullException).FullName);
    }

    [Fact]
    public async Task TryHandleAsync_RecordsFilteredStackTrace()
    {
        var (handler, recorder) = Build();
        Exception ex;
        try { throw new InvalidOperationException("stack test"); }
        catch (InvalidOperationException e) { ex = e; }

        await Handle(handler, ex);

        var content = Deserialize(recorder.Entries.Single());
        content.Stack.Should().NotBeEmpty("there must be at least one user-code frame");
        content.Stack.Should().Contain(
            f => f.Method.Contains(nameof(TryHandleAsync_RecordsFilteredStackTrace)),
            "the test method must appear in the filtered stack");
    }

    [Fact]
    public async Task TryHandleAsync_StackTrace_ContainsUserCodeFrame_AndStripsFrameworkFrames()
    {
        var (handler, recorder) = Build();
        Exception ex;
        try { throw new InvalidOperationException(); }
        catch (InvalidOperationException e) { ex = e; }

        await Handle(handler, ex);

        var content = Deserialize(recorder.Entries.Single());
        // User-code frame (the test method) must be present.
        content.Stack.Should().Contain(
            f => f.Method.Contains(
                nameof(TryHandleAsync_StackTrace_ContainsUserCodeFrame_AndStripsFrameworkFrames)),
            "the test method must appear in the filtered stack");
        // Framework frames must be stripped.
        content.Stack.Should().NotContain(
            f => f.Method.StartsWith("System.", StringComparison.Ordinal)
              || f.Method.StartsWith("Microsoft.", StringComparison.Ordinal),
            "System.* and Microsoft.* framework frames must be stripped");
    }

    [Fact]
    public async Task TryHandleAsync_CapturesInnerException_FirstLevelOnly()
    {
        var (handler, recorder) = Build();
        var inner = new ArgumentException("inner");
        var outer = new InvalidOperationException("outer", inner);

        await Handle(handler, outer);

        var content = Deserialize(recorder.Entries.Single());
        content.InnerExceptions.Should().ContainSingle();
        content.InnerExceptions[0].Type.Should().Be(typeof(ArgumentException).FullName);
        content.InnerExceptions[0].Message.Should().Be("inner");
    }

    [Fact]
    public async Task TryHandleAsync_InnerExceptions_EmptyWhenNoInner()
    {
        var (handler, recorder) = Build();

        await Handle(handler, new InvalidOperationException("no inner"));

        Deserialize(recorder.Entries.Single()).InnerExceptions.Should().BeEmpty();
    }

    // ── stamps registry ───────────────────────────────────────────────────────

    [Fact]
    public async Task TryHandleAsync_StampsExceptionInRegistry()
    {
        var (handler, _) = Build();
        var ex = new InvalidOperationException();

        await Handle(handler, ex);

        ExceptionRegistry.IsStamped(ex).Should().BeTrue();
    }

    // ── IgnoreExceptionTypes ──────────────────────────────────────────────────

    [Fact]
    public async Task TryHandleAsync_DropsEntry_WhenExceptionTypeIsIgnored()
    {
        var (handler, recorder) = Build(o =>
            o.Exceptions.IgnoreExceptionTypes = ["System.OperationCanceledException"]);

        await Handle(handler, new OperationCanceledException());

        recorder.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task TryHandleAsync_DropsEntry_WhenSubclassOfIgnoredType()
    {
        // TaskCanceledException is a subclass of OperationCanceledException.
        var (handler, recorder) = Build(o =>
            o.Exceptions.IgnoreExceptionTypes = ["System.OperationCanceledException"]);

        await Handle(handler, new TaskCanceledException());

        recorder.Entries.Should().BeEmpty("subclasses of ignored types must also be dropped");
    }

    [Fact]
    public async Task TryHandleAsync_RecordsEntry_WhenExceptionTypeNotIgnored()
    {
        var (handler, recorder) = Build(o =>
            o.Exceptions.IgnoreExceptionTypes = ["System.OperationCanceledException"]);

        await Handle(handler, new InvalidOperationException());

        recorder.Entries.Should().ContainSingle();
    }

    // ── Capture = false ───────────────────────────────────────────────────────

    [Fact]
    public async Task TryHandleAsync_NoEntry_WhenCaptureDisabled()
    {
        var (handler, recorder) = Build(o => o.Exceptions.Capture = false);

        await Handle(handler, new InvalidOperationException());

        recorder.Entries.Should().BeEmpty();
    }

    // ── failure safety ────────────────────────────────────────────────────────

    [Fact]
    public async Task TryHandleAsync_DoesNotThrow_WhenRecorderFails()
    {
        var opts = new LookoutOptions();
        var badRecorder = new ThrowingRecorder();
        var handler = new LookoutExceptionHandler(
            badRecorder,
            Options.Create(opts),
            NullLogger<LookoutExceptionHandler>.Instance);

        var act = async () => await handler.TryHandleAsync(
            new DefaultHttpContext(), new InvalidOperationException(), CancellationToken.None);

        await act.Should().NotThrowAsync("capture failures must be swallowed");
    }

    private sealed class ThrowingRecorder : ILookoutRecorder
    {
        public void Record(LookoutEntry entry) => throw new Exception("recorder exploded");
    }
}
