using System.Text.Json;
using FluentAssertions;
using Lookout.Core;
using Xunit;

namespace Lookout.AspNetCore.Tests.Redaction;

public sealed class RedactionPipelineBodyTests
{
    private readonly RedactionOptions _options = new();

    [Fact]
    public void RedactJsonBody_RedactsTopLevelSensitiveValues()
    {
        var json = "{\"user\":\"alice\",\"password\":\"secret\"}";

        var redacted = RedactionPipeline.RedactJsonBody(json, _options);

        using var doc = JsonDocument.Parse(redacted);
        doc.RootElement.GetProperty("user").GetString().Should().Be("alice");
        doc.RootElement.GetProperty("password").GetString().Should().Be("***");
    }

    [Fact]
    public void RedactJsonBody_RedactsNestedSensitiveValues()
    {
        var json = "{\"outer\":{\"token\":\"abc\",\"safe\":1}}";

        var redacted = RedactionPipeline.RedactJsonBody(json, _options);

        using var doc = JsonDocument.Parse(redacted);
        var outer = doc.RootElement.GetProperty("outer");
        outer.GetProperty("token").GetString().Should().Be("***");
        outer.GetProperty("safe").GetInt32().Should().Be(1);
    }

    [Fact]
    public void RedactJsonBody_TraversesArraysOfObjects()
    {
        var json = "{\"items\":[{\"password\":\"x\"},{\"password\":\"y\"}]}";

        var redacted = RedactionPipeline.RedactJsonBody(json, _options);

        using var doc = JsonDocument.Parse(redacted);
        var items = doc.RootElement.GetProperty("items");
        items[0].GetProperty("password").GetString().Should().Be("***");
        items[1].GetProperty("password").GetString().Should().Be("***");
    }

    [Fact]
    public void RedactJsonBody_CaseInsensitiveMatch()
    {
        var json = "{\"Password\":\"secret\"}";

        var redacted = RedactionPipeline.RedactJsonBody(json, _options);

        using var doc = JsonDocument.Parse(redacted);
        doc.RootElement.GetProperty("Password").GetString().Should().Be("***");
    }

    [Fact]
    public void RedactJsonBody_ReturnsInputUnchangedWhenNotJson()
    {
        var text = "not-json-at-all";

        RedactionPipeline.RedactJsonBody(text, _options).Should().Be(text);
    }

    [Fact]
    public void RedactJsonBody_NoMatchingFields_ProducesEquivalentJson()
    {
        var json = "{\"a\":1,\"b\":\"two\"}";

        var redacted = RedactionPipeline.RedactJsonBody(json, _options);

        using var left = JsonDocument.Parse(json);
        using var right = JsonDocument.Parse(redacted);
        right.RootElement.GetProperty("a").GetInt32().Should().Be(1);
        right.RootElement.GetProperty("b").GetString().Should().Be("two");
    }

    [Fact]
    public void RedactFormBody_RedactsSensitiveFieldsOnly()
    {
        var form = "user=alice&password=secret&keep=1";

        RedactionPipeline.RedactFormBody(form, _options)
            .Should().Be("user=alice&password=***&keep=1");
    }

    [Fact]
    public void RedactFormBody_CaseInsensitiveMatch()
    {
        var form = "PASSWORD=s";

        RedactionPipeline.RedactFormBody(form, _options)
            .Should().Be("PASSWORD=***");
    }

    [Fact]
    public void RedactFormBody_IgnoresMalformedPairsWithoutEquals()
    {
        var form = "loneFlag&password=s";

        RedactionPipeline.RedactFormBody(form, _options)
            .Should().Be("loneFlag&password=***");
    }

    [Fact]
    public void RedactFormBody_PreservesEmptyString()
    {
        RedactionPipeline.RedactFormBody(string.Empty, _options).Should().Be(string.Empty);
    }
}
