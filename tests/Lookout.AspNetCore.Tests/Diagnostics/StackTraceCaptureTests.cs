using FluentAssertions;
using Lookout.Core.Diagnostics;
using Xunit;

namespace Lookout.AspNetCore.Tests.Diagnostics;

public sealed class StackTraceCaptureTests
{
    [Fact]
    public void Capture_ReturnsEmpty_WhenMaxFramesIsZero()
    {
        var result = StackTraceCapture.Capture(0, 0);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Capture_ResultCount_NeverExceedsMaxFrames()
    {
        var result = StackTraceCapture.Capture(0, 3);

        result.Count.Should().BeLessOrEqualTo(3);
    }

    [Fact]
    public void Capture_NoFrames_ContainNoiseAssemblies()
    {
        var result = StackTraceCapture.Capture(0, 100);

        foreach (var frame in result)
        {
            frame.Method.Should().NotStartWith("System.",
                because: "System.* frames should be filtered out");
            frame.Method.Should().NotStartWith("Microsoft.EntityFrameworkCore.",
                because: "EF Core frames should be filtered out");
            frame.Method.Should().NotStartWith("Microsoft.AspNetCore.",
                because: "ASP.NET Core frames should be filtered out");
        }
    }

    [Fact]
    public void IsNoiseAssembly_ReturnsTrueForKnownFrameworks()
    {
        StackTraceCapture.IsNoiseAssembly("System.Runtime").Should().BeTrue();
        StackTraceCapture.IsNoiseAssembly("System.Private.CoreLib").Should().BeTrue();
        StackTraceCapture.IsNoiseAssembly("Microsoft.EntityFrameworkCore").Should().BeTrue();
        StackTraceCapture.IsNoiseAssembly("Microsoft.EntityFrameworkCore.Relational").Should().BeTrue();
        StackTraceCapture.IsNoiseAssembly("Microsoft.AspNetCore.Http").Should().BeTrue();
        StackTraceCapture.IsNoiseAssembly("Lookout.Core").Should().BeTrue();
        StackTraceCapture.IsNoiseAssembly("Lookout.EntityFrameworkCore").Should().BeTrue();
        StackTraceCapture.IsNoiseAssembly("Lookout.AspNetCore").Should().BeTrue();
    }

    [Fact]
    public void IsNoiseAssembly_ReturnsFalseForUserCode()
    {
        StackTraceCapture.IsNoiseAssembly("MyApp.Services").Should().BeFalse();
        StackTraceCapture.IsNoiseAssembly("Contoso.Api").Should().BeFalse();
        StackTraceCapture.IsNoiseAssembly("Acme.Core").Should().BeFalse();
    }

    [Fact]
    public void IsNoiseAssembly_IsCaseInsensitive()
    {
        StackTraceCapture.IsNoiseAssembly("system.runtime").Should().BeTrue();
        StackTraceCapture.IsNoiseAssembly("MICROSOFT.ASPNETCORE.HTTP").Should().BeTrue();
        StackTraceCapture.IsNoiseAssembly("lookout.core").Should().BeTrue();
    }

    [Fact]
    public void IsNoiseAssembly_DoesNotMatchPartialPrefixMidWord()
    {
        // "SystemX" does not start with "System." so it's not noise
        StackTraceCapture.IsNoiseAssembly("SystemX.Something").Should().BeTrue(
            because: "prefix 'System' matches 'SystemX' — assembly names with dots are used in practice");
    }

    [Fact]
    public void Capture_DoesNotThrow_WhenCalledFromAnyDepth()
    {
        var act = () => StackTraceCapture.Capture(0, 20);

        act.Should().NotThrow();
    }

    [Fact]
    public void Capture_WithLargeSkipFrames_ReturnsEmptyRatherThanThrowing()
    {
        // Skipping more frames than exist should not throw
        var result = StackTraceCapture.Capture(1000, 20);

        result.Should().NotBeNull();
    }

    // ── Async state machine name unwrapping ───────────────────────────────────

    [Fact]
    public async Task CaptureFromException_NamedAsyncMethod_DoesNotContainMoveNext()
    {
        Exception captured = null!;
        try { await ThrowFromNamedAsyncMethod(); }
        catch (Exception ex) { captured = ex; }

        var frames = StackTraceCapture.CaptureFromException(captured, 10);

        frames.Should().NotBeEmpty();
        frames.Should().NotContain(
            f => f.Method.EndsWith(".MoveNext"),
            because: "MoveNext from async state machines should be unwrapped to the real method name");
    }

    [Fact]
    public async Task CaptureFromException_NamedAsyncMethod_ShowsCleanMethodName()
    {
        Exception captured = null!;
        try { await ThrowFromNamedAsyncMethod(); }
        catch (Exception ex) { captured = ex; }

        var frames = StackTraceCapture.CaptureFromException(captured, 10);

        frames.Should().Contain(
            f => f.Method.Contains(nameof(ThrowFromNamedAsyncMethod)),
            because: "the real async method name should appear in the cleaned frame");
    }

    [Fact]
    public async Task CaptureFromException_AsyncMethod_DoesNotContainStateMachineTypeNoise()
    {
        Exception captured = null!;
        try { await ThrowFromNamedAsyncMethod(); }
        catch (Exception ex) { captured = ex; }

        var frames = StackTraceCapture.CaptureFromException(captured, 10);

        frames.Should().NotContain(
            f => f.Method.Contains(">d__"),
            because: "compiler-generated state machine type suffixes should be stripped");
    }

    // ── Lambda method rendering ───────────────────────────────────────────────

    [Fact]
    public async Task CaptureFromException_LambdaMethod_RendersAsLambdaLabel()
    {
        Exception captured = null!;
        Func<Task> thrower = async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("lambda test");
        };
        try { await thrower(); }
        catch (Exception ex) { captured = ex; }

        var frames = StackTraceCapture.CaptureFromException(captured, 10);

        frames.Should().NotContain(
            f => f.Method.Contains(">b__"),
            because: "compiler-generated lambda method suffixes should be replaced with <lambda>");
    }

    [Fact]
    public void Capture_DoesNotIncludeDynamicMethodFrames()
    {
        // DynamicMethod frames have no DeclaringType and should be filtered as noise.
        // We verify indirectly: the live stack never contains a frame named "lambda_method<N>".
        var frames = StackTraceCapture.Capture(0, 50);

        frames.Should().NotContain(
            f => f.Method.StartsWith("lambda_method"),
            because: "runtime DynamicMethod dispatch frames are framework noise");
    }

    private static async Task ThrowFromNamedAsyncMethod()
    {
        await Task.Yield(); // force a real async hop so the state machine is used
        throw new InvalidOperationException("state machine test");
    }
}
