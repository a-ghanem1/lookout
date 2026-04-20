using FluentAssertions;
using Lookout.Core;
using Xunit;

namespace Lookout.AspNetCore.Tests;

public sealed class LookoutOptionsDefaultsTests
{
    private readonly LookoutOptions _sut = new();

    [Fact]
    public void AllowInEnvironments_DefaultsToSingleDevelopmentEntry()
    {
        _sut.AllowInEnvironments.Should().ContainSingle()
            .Which.Should().Be("Development");
    }

    [Fact]
    public void AllowInProduction_DefaultsToFalse()
    {
        _sut.AllowInProduction.Should().BeFalse();
    }

    [Fact]
    public void StoragePath_DefaultIsNonEmptyAndContainsLookout()
    {
        _sut.StoragePath.Should().NotBeNullOrWhiteSpace();
        _sut.StoragePath.Should().Contain("Lookout");
        _sut.StoragePath.Should().EndWith(".db");
    }

    [Fact]
    public void MaxAgeHours_DefaultIs24()
    {
        _sut.MaxAgeHours.Should().Be(24);
    }

    [Fact]
    public void MaxEntryCount_DefaultIs50000()
    {
        _sut.MaxEntryCount.Should().Be(50_000);
    }

    [Fact]
    public void ChannelCapacity_DefaultIs10000()
    {
        _sut.ChannelCapacity.Should().Be(10_000);
    }

    [Fact]
    public void Tag_DefaultsToNull()
    {
        _sut.Tag.Should().BeNull();
    }

    [Fact]
    public void Filter_DefaultsToNull()
    {
        _sut.Filter.Should().BeNull();
    }

    [Fact]
    public void FilterBatch_DefaultsToNull()
    {
        _sut.FilterBatch.Should().BeNull();
    }

    [Fact]
    public void Redaction_DefaultsToNonNullInstance()
    {
        _sut.Redaction.Should().NotBeNull();
    }

    [Fact]
    public void CaptureRequestBody_DefaultsToFalse()
    {
        _sut.CaptureRequestBody.Should().BeFalse();
    }

    [Fact]
    public void CaptureResponseBody_DefaultsToFalse()
    {
        _sut.CaptureResponseBody.Should().BeFalse();
    }

    [Fact]
    public void MaxBodyBytes_DefaultIs65536()
    {
        _sut.MaxBodyBytes.Should().Be(65_536);
    }

    [Fact]
    public void SkipPaths_DefaultsToKnownHealthAndFaviconPaths()
    {
        _sut.SkipPaths.Should().BeEquivalentTo(
            new[] { "/healthz", "/health", "/ready", "/favicon.ico" });
    }

    [Fact]
    public void SkipPaths_MatchesCaseInsensitively()
    {
        _sut.SkipPaths.Contains("/HEALTHZ").Should().BeTrue();
        _sut.SkipPaths.Contains("/Favicon.ICO").Should().BeTrue();
    }

    [Fact]
    public void CapturedContentTypes_DefaultsToJsonFormAndTextWildcard()
    {
        _sut.CapturedContentTypes.Should().BeEquivalentTo(
            new[] { "application/json", "application/x-www-form-urlencoded", "text/*" });
    }

    [Fact]
    public void CapturedContentTypes_MatchesCaseInsensitively()
    {
        _sut.CapturedContentTypes.Contains("Application/JSON").Should().BeTrue();
    }

    [Fact]
    public void Redaction_JsonFields_DefaultsToSensitiveNames()
    {
        _sut.Redaction.JsonFields.Should().Contain(
            new[] { "password", "token", "access_token", "refresh_token", "secret", "api_key", "apikey" });
    }

    [Fact]
    public void Redaction_JsonFields_MatchCaseInsensitively()
    {
        _sut.Redaction.JsonFields.Contains("Password").Should().BeTrue();
        _sut.Redaction.JsonFields.Contains("API_KEY").Should().BeTrue();
    }
}
