using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lookout.AspNetCore.Capture.Http;
using Lookout.Core;
using Lookout.Core.Schemas;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Lookout.AspNetCore.Tests.Http;

public sealed class LookoutHttpClientHandlerTests
{
    private static (LookoutHttpClientHandler handler, List<LookoutEntry> captured) Build(
        Action<LookoutOptions>? configure = null,
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var opts = new LookoutOptions();
        configure?.Invoke(opts);

        var options = Substitute.For<IOptions<LookoutOptions>>();
        options.Value.Returns(opts);

        var captured = new List<LookoutEntry>();
        var recorder = Substitute.For<ILookoutRecorder>();
        recorder.When(r => r.Record(Arg.Any<LookoutEntry>()))
            .Do(ci => captured.Add(ci.Arg<LookoutEntry>()));

        var inner = new StubHandler(respond ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var handler = new LookoutHttpClientHandler(recorder, options) { InnerHandler = inner };
        return (handler, captured);
    }

    private static HttpClient Client(LookoutHttpClientHandler handler) => new(handler);

    // ── success path ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Success_RecordsTypeMethodUrlStatusDuration()
    {
        var (handler, captured) = Build(
            respond: _ => new HttpResponseMessage(HttpStatusCode.Created));

        await Client(handler).GetAsync("http://example.com/api/items");

        captured.Should().HaveCount(1);
        var entry = captured[0];
        entry.Type.Should().Be("http-out");
        entry.Tags["http.method"].Should().Be("GET");
        entry.Tags["http.out"].Should().Be("true");
        entry.Tags["http.url.host"].Should().Be("example.com");
        entry.Tags["http.url.path"].Should().Be("/api/items");
        entry.Tags["http.status"].Should().Be("201");
        entry.DurationMs.Should().BeGreaterThan(0);

        var content = Deserialize(entry);
        content.Method.Should().Be("GET");
        content.Url.Should().Be("http://example.com/api/items");
        content.StatusCode.Should().Be(201);
        content.ErrorType.Should().BeNull();
        content.ErrorMessage.Should().BeNull();
    }

    // ── failure path ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Exception_RecordsErrorTypeAndMessageAndRethrows()
    {
        var (handler, captured) = Build(
            respond: _ => throw new HttpRequestException("connection refused"));

        var act = async () => await Client(handler).GetAsync("http://example.com/fail");
        await act.Should().ThrowAsync<Exception>();

        captured.Should().HaveCount(1);
        var entry = captured[0];
        entry.Tags.Should().ContainKey("http.error");
        entry.Tags.Should().NotContainKey("http.status");

        var content = Deserialize(entry);
        content.StatusCode.Should().BeNull();
        content.ErrorType.Should().NotBeNullOrEmpty();
        content.ErrorMessage.Should().Contain("connection refused");
    }

    // ── body capture ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BodyCapture_OffByDefault_NullBodies()
    {
        var (handler, captured) = Build(respond: _ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json");
            return r;
        });

        var req = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api")
        {
            Content = new StringContent("{\"x\":1}", Encoding.UTF8, "application/json"),
        };
        await Client(handler).SendAsync(req);

        var c = Deserialize(captured[0]);
        c.RequestBody.Should().BeNull();
        c.ResponseBody.Should().BeNull();
    }

    [Fact]
    public async Task BodyCapture_WhenEnabled_CapturesBothBodies()
    {
        var (handler, captured) = Build(
            configure: o =>
            {
                o.Http.CaptureOutboundRequestBody = true;
                o.Http.CaptureOutboundResponseBody = true;
            },
            respond: _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.OK);
                r.Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json");
                return r;
            });

        var req = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api")
        {
            Content = new StringContent("{\"x\":1}", Encoding.UTF8, "application/json"),
        };
        await Client(handler).SendAsync(req);

        var c = Deserialize(captured[0]);
        c.RequestBody.Should().Be("{\"x\":1}");
        c.ResponseBody.Should().Be("{\"ok\":true}");
    }

    [Fact]
    public async Task BodyCapture_TruncatedAtSizeCap_WithMarker()
    {
        var (handler, captured) = Build(
            configure: o =>
            {
                o.Http.CaptureOutboundRequestBody = true;
                o.Http.OutboundBodyMaxBytes = 16;
            },
            respond: _ => new HttpResponseMessage(HttpStatusCode.OK));

        var req = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api")
        {
            Content = new StringContent(new string('A', 256), Encoding.UTF8, "text/plain"),
        };
        await Client(handler).SendAsync(req);

        var c = Deserialize(captured[0]);
        c.RequestBody.Should().NotBeNull();
        c.RequestBody.Should().Contain("[lookout:truncated]");
        c.RequestBody!.Replace("[lookout:truncated]", "").TrimEnd('\n', '…')
            .Should().Be(new string('A', 16));
    }

    [Fact]
    public async Task BodyCapture_BinaryContentType_NotCaptured()
    {
        var (handler, captured) = Build(
            configure: o => { o.Http.CaptureOutboundResponseBody = true; },
            respond: _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.OK);
                r.Content = new ByteArrayContent(new byte[] { 0x89, 0x50 });
                r.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                return r;
            });

        await Client(handler).GetAsync("http://example.com/img");

        Deserialize(captured[0]).ResponseBody.Should().BeNull();
    }

    // ── header redaction ─────────────────────────────────────────────────────

    [Fact]
    public async Task SensitiveRequestHeaders_AreRedacted()
    {
        var (handler, captured) = Build(respond: _ => new HttpResponseMessage(HttpStatusCode.OK));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api");
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer super-secret");
        req.Headers.TryAddWithoutValidation("X-Safe", "visible");
        await Client(handler).SendAsync(req);

        var c = Deserialize(captured[0]);
        c.RequestHeaders["Authorization"].Should().Be("***");
        c.RequestHeaders["X-Safe"].Should().Be("visible");
        captured[0].Content.Should().NotContain("super-secret");
    }

    // ── query-param redaction ─────────────────────────────────────────────────

    [Fact]
    public async Task SensitiveQueryParams_AreRedactedInUrl()
    {
        var (handler, captured) = Build(respond: _ => new HttpResponseMessage(HttpStatusCode.OK));

        await Client(handler).GetAsync("http://example.com/api?user=alice&token=secret-value&page=1");

        var c = Deserialize(captured[0]);
        c.Url.Should().Contain("user=alice");
        c.Url.Should().Contain("token=***");
        c.Url.Should().NotContain("secret-value");
        c.Url.Should().Contain("page=1");
    }

    // ── correlation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RequestId_MatchesActivityRootId_WhenActivityPresent()
    {
        using var source = new ActivitySource("test.http");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        var (handler, captured) = Build(respond: _ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = Client(handler);

        string? rootId;
        using (var activity = source.StartActivity("parent"))
        {
            rootId = activity!.RootId;
            await client.GetAsync("http://example.com/api");
        }

        captured.Should().HaveCount(1);
        captured[0].RequestId.Should().Be(rootId);
    }

    [Fact]
    public async Task RequestId_Null_WhenNoActivityPresent()
    {
        Activity.Current = null;

        var (handler, captured) = Build(respond: _ => new HttpResponseMessage(HttpStatusCode.OK));
        await Client(handler).GetAsync("http://example.com/api");

        captured[0].RequestId.Should().BeNull();
    }

    // ── master switch ─────────────────────────────────────────────────────────

    [Fact]
    public async Task MasterSwitch_Off_DoesNotRecord()
    {
        var (handler, captured) = Build(configure: o => o.Http.CaptureOutbound = false);

        await Client(handler).GetAsync("http://example.com/api");

        captured.Should().BeEmpty();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static OutboundHttpEntryContent Deserialize(LookoutEntry entry)
    {
        var c = JsonSerializer.Deserialize<OutboundHttpEntryContent>(entry.Content, LookoutJson.Options);
        c.Should().NotBeNull();
        return c!;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }
}
