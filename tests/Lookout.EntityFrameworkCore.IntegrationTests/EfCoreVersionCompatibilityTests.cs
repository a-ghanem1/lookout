using FluentAssertions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Lookout.EntityFrameworkCore.IntegrationTests;

/// <summary>
/// Verifies that the EF Core version resolved at runtime matches the target framework,
/// confirming the TFM-conditional package references in Lookout.EntityFrameworkCore.csproj
/// are respected and no cross-version conflict is introduced.
/// </summary>
public sealed class EfCoreVersionCompatibilityTests
{
    [Fact]
    public void EfCoreRelational_AssemblyVersion_MatchesTargetFramework()
    {
        var assembly = typeof(DatabaseFacade).Assembly;
        var version = assembly.GetName().Version!;

#if NET8_0
        version.Major.Should().Be(8,
            "net8.0 TFM must resolve EF Core 8 — a version mismatch indicates a broken TFM-conditional reference");
#else
        version.Major.Should().Be(10,
            "net10.0 TFM must resolve EF Core 10 — a version mismatch indicates a broken TFM-conditional reference");
#endif
    }
}
