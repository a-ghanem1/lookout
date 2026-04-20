using System.Text.Json;
using FluentAssertions;
using Lookout.Core.Schemas;
using Xunit;

namespace Lookout.AspNetCore.Tests.Schemas;

public sealed class HttpEntryContentTests
{
    [Fact]
    public void RoundTripsThroughSharedSerializer()
    {
        var original = new HttpEntryContent(
            Method: "POST",
            Path: "/orders",
            QueryString: "?debug=1",
            StatusCode: 201,
            DurationMs: 12.5,
            RequestHeaders: new Dictionary<string, string> { ["Accept"] = "application/json" },
            ResponseHeaders: new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            RequestBody: "{\"id\":1}",
            ResponseBody: "{\"ok\":true}",
            User: "alice");

        var json = JsonSerializer.Serialize(original, LookoutJson.Options);
        var round = JsonSerializer.Deserialize<HttpEntryContent>(json, LookoutJson.Options);

        round.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Serializes_UsingCamelCase_PropertyNames()
    {
        var content = new HttpEntryContent(
            "GET", "/x", string.Empty, 200, 1.0,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            null, null, null);

        var json = JsonSerializer.Serialize(content, LookoutJson.Options);

        json.Should().Contain("\"method\":\"GET\"");
        json.Should().Contain("\"statusCode\":200");
        json.Should().Contain("\"durationMs\":1");
    }

    [Fact]
    public void Serializes_OmittingNullOptionalFields()
    {
        var content = new HttpEntryContent(
            "GET", "/x", string.Empty, 200, 1.0,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            RequestBody: null,
            ResponseBody: null,
            User: null);

        var json = JsonSerializer.Serialize(content, LookoutJson.Options);

        json.Should().NotContain("\"requestBody\"");
        json.Should().NotContain("\"responseBody\"");
        json.Should().NotContain("\"user\"");
    }

    [Fact]
    public void Deserializes_JsonFromEquivalentCamelCasePayload()
    {
        const string json = """
        {
          "method":"GET",
          "path":"/ping",
          "queryString":"",
          "statusCode":200,
          "durationMs":3.14,
          "requestHeaders":{"X-Req":"a"},
          "responseHeaders":{"X-Res":"b"}
        }
        """;

        var content = JsonSerializer.Deserialize<HttpEntryContent>(json, LookoutJson.Options);

        content.Should().NotBeNull();
        content!.Method.Should().Be("GET");
        content.Path.Should().Be("/ping");
        content.StatusCode.Should().Be(200);
        content.DurationMs.Should().BeApproximately(3.14, 1e-9);
        content.RequestHeaders.Should().ContainKey("X-Req").WhoseValue.Should().Be("a");
        content.RequestBody.Should().BeNull();
        content.User.Should().BeNull();
    }
}
