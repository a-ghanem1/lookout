using FluentAssertions;
using Lookout.AspNetCore;
using Xunit;

namespace Lookout.AspNetCore.Tests.Hangfire;

public sealed class LookoutAspNetCoreHangfireRegressionTests
{
    [Fact]
    public void LookoutAspNetCoreAssembly_DoesNotReferenceHangfire()
    {
        var refs = typeof(LookoutRequestMiddleware).Assembly.GetReferencedAssemblies();

        refs.Should().NotContain(
            r => r.Name!.StartsWith("Hangfire", StringComparison.OrdinalIgnoreCase),
            "Lookout.AspNetCore must not pull Hangfire into its dependency graph");
    }
}
