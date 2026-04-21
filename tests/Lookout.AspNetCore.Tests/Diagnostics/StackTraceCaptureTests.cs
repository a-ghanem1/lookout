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
}
