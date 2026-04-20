using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Lookout.Core;
using Lookout.Core.Schemas;
using Lookout.Storage.Sqlite;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lookout.AspNetCore.Tests.Http;

public sealed class LookoutRequestMiddlewareTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _tempFiles)
            foreach (var f in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(f)) File.Delete(f);
    }

    [Fact]
    public async Task SuccessfulRequest_ProducesOneHttpEntry_WithExpectedTagsAndContent()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/ping");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var entry = (await PollForEntriesAsync(dbPath, expected: 1)).Single();
        await app.StopAsync();

        entry.Type.Should().Be("http");
        entry.Tags["http.method"].Should().Be("GET");
        entry.Tags["http.path"].Should().Be("/ping");
        entry.Tags["http.status"].Should().Be("200");
        entry.DurationMs.Should().BeGreaterThan(0);

        var content = DeserializeContent(entry);
        content.Method.Should().Be("GET");
        content.Path.Should().Be("/ping");
        content.StatusCode.Should().Be(200);
        content.DurationMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SkipPathsEntry_ProducesZeroRows()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath, o => o.SkipPaths.Add("/skip-me"));
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/skip-me");
        response.IsSuccessStatusCode.Should().BeTrue();

        // Give the flusher a moment to confirm nothing landed.
        await Task.Delay(300);
        var rows = await ReadAllAsync(dbPath);
        await app.StopAsync();

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task DefaultSkipPaths_HealthzAndFavicon_AreNotCaptured()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var client = app.GetTestClient();
        (await client.GetAsync("/healthz")).IsSuccessStatusCode.Should().BeTrue();
        (await client.GetAsync("/favicon.ico")).IsSuccessStatusCode.Should().BeTrue();

        await Task.Delay(300);
        var rows = await ReadAllAsync(dbPath);
        await app.StopAsync();

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestsUnderLookoutPrefix_AreNeverSelfCaptured()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var client = app.GetTestClient();
        (await client.GetAsync("/lookout")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/lookout/nested/path")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await Task.Delay(300);
        var rows = await ReadAllAsync(dbPath);
        await app.StopAsync();

        rows.Should().BeEmpty("/lookout and any path under it must self-skip");
    }

    [Fact]
    public async Task ActivityRootId_IsStable_AndMatchesStoredRequestId()
    {
        string? observedRootId = null;
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath, endpointMap: endpoints =>
        {
            endpoints.MapGet("/trace", (HttpContext ctx) =>
            {
                observedRootId = Activity.Current?.RootId;
                return Results.Ok();
            });
        });
        await app.StartAsync();

        (await app.GetTestClient().GetAsync("/trace")).StatusCode.Should().Be(HttpStatusCode.OK);

        var entry = (await PollForEntriesAsync(dbPath, expected: 1)).Single();
        await app.StopAsync();

        observedRootId.Should().NotBeNullOrEmpty();
        entry.RequestId.Should().Be(observedRootId);
    }

    [Fact]
    public async Task Response500_IsStillCaptured_WithStatusAndDuration()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath, endpointMap: endpoints =>
        {
            endpoints.MapGet("/boom", (HttpContext ctx) =>
            {
                ctx.Response.StatusCode = 500;
                return Results.StatusCode(500);
            });
        });
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/boom");
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var entry = (await PollForEntriesAsync(dbPath, expected: 1)).Single();
        await app.StopAsync();

        entry.Tags["http.status"].Should().Be("500");
        entry.DurationMs.Should().BeGreaterThan(0);
        DeserializeContent(entry).StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task AuthenticatedRequest_StoresHttpUserTag()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath, configureServices: services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        }, configurePipeline: app =>
        {
            app.UseAuthentication();
        });
        await app.StartAsync();

        var client = app.GetTestClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/ping");
        req.Headers.Authorization = new AuthenticationHeaderValue(TestAuthHandler.SchemeName, "ok");
        (await client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.OK);

        var entry = (await PollForEntriesAsync(dbPath, expected: 1)).Single();
        await app.StopAsync();

        entry.Tags.Should().ContainKey("http.user").WhoseValue.Should().Be("alice");
        DeserializeContent(entry).User.Should().Be("alice");
    }

    [Fact]
    public async Task AnonymousRequest_OmitsHttpUserTag()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        (await app.GetTestClient().GetAsync("/ping")).StatusCode.Should().Be(HttpStatusCode.OK);

        var entry = (await PollForEntriesAsync(dbPath, expected: 1)).Single();
        await app.StopAsync();

        entry.Tags.Should().NotContainKey("http.user");
        DeserializeContent(entry).User.Should().BeNull();
    }

    [Fact]
    public async Task SensitiveHeaders_AreRedacted_InContentJson()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var client = app.GetTestClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/ping");
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer super-secret-token");
        req.Headers.TryAddWithoutValidation("Cookie", "session=abc123");
        req.Headers.TryAddWithoutValidation("X-Safe", "visible");
        (await client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.OK);

        var entry = (await PollForEntriesAsync(dbPath, expected: 1)).Single();
        await app.StopAsync();

        var content = DeserializeContent(entry);
        content.RequestHeaders["Authorization"].Should().Be("***");
        content.RequestHeaders["Cookie"].Should().Be("***");
        content.RequestHeaders["X-Safe"].Should().Be("visible");
        entry.Content.Should().NotContain("super-secret-token");
        entry.Content.Should().NotContain("session=abc123");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private string TempDbPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lookout_http_{Guid.NewGuid():N}.db");
        _tempFiles.Add(path);
        return path;
    }

    private static WebApplication BuildApp(
        string dbPath,
        Action<LookoutOptions>? configure = null,
        Action<IServiceCollection>? configureServices = null,
        Action<WebApplication>? configurePipeline = null,
        Action<IEndpointRouteBuilder>? endpointMap = null)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddLookout(o =>
        {
            o.StoragePath = dbPath;
            configure?.Invoke(o);
        });
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        configurePipeline?.Invoke(app);
        app.UseLookout();
        app.MapLookout();

        if (endpointMap is not null)
        {
            endpointMap(app);
        }
        else
        {
            app.MapGet("/ping", () => Results.Text("pong"));
        }
        app.MapGet("/skip-me", () => Results.NoContent());
        app.MapGet("/healthz", () => Results.NoContent());
        app.MapGet("/favicon.ico", () => Results.NoContent());
        return app;
    }

    private static SqliteLookoutStorage OpenReadStorage(string dbPath) =>
        new(new LookoutOptions { StoragePath = dbPath });

    private static async Task<IReadOnlyList<LookoutEntry>> ReadAllAsync(string dbPath)
    {
        using var storage = OpenReadStorage(dbPath);
        return await storage.ReadRecentAsync(100);
    }

    private static async Task<IReadOnlyList<LookoutEntry>> PollForEntriesAsync(
        string dbPath, int expected, int timeoutSeconds = 5)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        IReadOnlyList<LookoutEntry> rows = Array.Empty<LookoutEntry>();
        while (rows.Count < expected && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
            rows = await ReadAllAsync(dbPath);
        }
        return rows;
    }

    private static HttpEntryContent DeserializeContent(LookoutEntry entry)
    {
        var content = JsonSerializer.Deserialize<HttpEntryContent>(entry.Content, LookoutJson.Options);
        content.Should().NotBeNull();
        return content!;
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Authorization", out var header) ||
                !header.ToString().StartsWith(SchemeName, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "alice") }, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
