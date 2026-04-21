using FluentAssertions;
using Lookout.Core;
using Xunit;

namespace Lookout.AspNetCore.Tests.Redaction;

public sealed class RedactionSqlParametersTests
{
    private readonly RedactionOptions _options = new();

    [Fact]
    public void SqlParameters_DefaultsContainSensitiveNames()
    {
        _options.SqlParameters.Should().Contain(new[]
        {
            "password", "token", "access_token", "refresh_token",
            "secret", "api_key", "apikey",
        });
    }

    [Fact]
    public void SqlParameters_MatchesCaseInsensitively()
    {
        _options.SqlParameters.Contains("Password").Should().BeTrue();
        _options.SqlParameters.Contains("API_KEY").Should().BeTrue();
        _options.SqlParameters.Contains("TOKEN").Should().BeTrue();
    }

    [Fact]
    public void RedactSqlParameterValue_RedactsSensitiveParameter()
    {
        RedactionPipeline.RedactSqlParameterValue("password", "s3cr3t", _options)
            .Should().Be("***");
    }

    [Fact]
    public void RedactSqlParameterValue_PassesThroughSafeParameter()
    {
        RedactionPipeline.RedactSqlParameterValue("userId", "42", _options)
            .Should().Be("42");
    }

    [Fact]
    public void RedactSqlParameterValue_ReturnsNullForNullValue()
    {
        RedactionPipeline.RedactSqlParameterValue("password", null, _options)
            .Should().BeNull();
    }

    [Fact]
    public void RedactSqlParameterValue_CaseInsensitiveNameMatch()
    {
        RedactionPipeline.RedactSqlParameterValue("PASSWORD", "s3cr3t", _options)
            .Should().Be("***");

        RedactionPipeline.RedactSqlParameterValue("Api_Key", "mykey", _options)
            .Should().Be("***");
    }

    [Fact]
    public void RedactSqlParameterValue_CustomSensitiveName_IsRedacted()
    {
        var options = new RedactionOptions();
        options.SqlParameters.Add("ssn");

        RedactionPipeline.RedactSqlParameterValue("ssn", "123-45-6789", options)
            .Should().Be("***");
    }

    [Fact]
    public void RedactSqlParameterValue_RemovedName_IsNoLongerRedacted()
    {
        var options = new RedactionOptions();
        options.SqlParameters.Remove("password");

        RedactionPipeline.RedactSqlParameterValue("password", "visible", options)
            .Should().Be("visible");
    }
}
