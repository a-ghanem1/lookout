using System.Net;
using System.Text.Json;
using FluentAssertions;
using Lookout.Core;
using Lookout.Core.Schemas;
using Lookout.Dashboard;
using Lookout.Storage.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lookout.AspNetCore.Tests.Dashboard;

/// <summary>
/// Validates that the embedded Vite bundle is served from MapLookout with the right
/// content types and that unknown sub-paths SPA-fallback to index.html.
/// </summary>
public sealed class DashboardEmbedTests : IDisposable
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
    public void DashboardAssets_ExposesAtLeastIndexHtml()
    {
        // Either the Vite build ran (common dev path) or the assembly was built with
        // SkipDashboardUiBuild=true and no assets are embedded. Both are valid states
        // for the library — the integration tests below exercise the wiring when at
        // least one asset is embedded.
        var names = DashboardAssets.Assembly
            .GetManifestResourceNames()
            .Where(n => n.StartsWith("LookoutUi/", StringComparison.Ordinal))
            .ToArray();

        if (names.Length == 0)
        {
            // Nothing was embedded (SkipDashboardUiBuild). Don't fail — just skip.
            return;
        }

        names.Should().Contain(n => n.EndsWith("/index.html", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetLookoutRoot_ReturnsEmbeddedIndexHtml_WithHtmlContentType()
    {
        if (!HasEmbeddedAssets()) return;

        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync("/lookout/");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        var body = await resp.Content.ReadAsStringAsync();
        await app.StopAsync();
        body.Should().Contain("<div id=\"root\">");
    }

    [Fact]
    public async Task GetLookoutRoot_WithoutTrailingSlash_AlsoServesIndex()
    {
        if (!HasEmbeddedAssets()) return;

        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync("/lookout");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        await app.StopAsync();
    }

    [Fact]
    public async Task GetJsAsset_ReturnsJavascriptContentType()
    {
        var jsAsset = FirstEmbeddedWithExtension(".js");
        if (jsAsset is null) return;

        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync($"/lookout/{jsAsset}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/javascript");
        await app.StopAsync();
    }

    [Fact]
    public async Task UnknownPathUnderLookout_SpaFallbacksToIndex()
    {
        if (!HasEmbeddedAssets()) return;

        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync("/lookout/requests/some-id");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        var body = await resp.Content.ReadAsStringAsync();
        await app.StopAsync();
        body.Should().Contain("<div id=\"root\">");
    }

    [Fact]
    public async Task IndexHtml_HasBaseHrefMatchingMountPrefix()
    {
        if (!HasEmbeddedAssets()) return;

        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var body = await app.GetTestClient().GetStringAsync("/lookout/");
        await app.StopAsync();

        body.Should().Contain("<base href=\"/lookout/\">");
    }

    [Fact]
    public async Task IndexHtml_UsesCustomMountPrefix_WhenProvided()
    {
        if (!HasEmbeddedAssets()) return;

        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath, mount: "/diag");
        await app.StartAsync();

        var body = await app.GetTestClient().GetStringAsync("/diag/");
        await app.StopAsync();

        body.Should().Contain("<base href=\"/diag/\">");
    }

    [Fact]
    public async Task DetailRoute_WithMixedHttpAndEfEntries_LoadsIndexAndReturnsAllEntries()
    {
        if (!HasEmbeddedAssets()) return;

        var dbPath = TempDbPath();
        const string rid = "req-mixed-embed";

        // Seed 1 HTTP + 2 EF entries sharing the same request id.
        using (var storage = new SqliteLookoutStorage(new LookoutOptions { StoragePath = dbPath }))
        {
            var now = DateTimeOffset.UtcNow;
            var http = new LookoutEntry(
                Id: Guid.NewGuid(), Type: "http", Timestamp: now.AddMilliseconds(-300),
                RequestId: rid, DurationMs: 1.0,
                Tags: new Dictionary<string, string> { ["http.path"] = "/orders" },
                Content: JsonSerializer.Serialize(
                    new HttpEntryContent("GET", "/orders", "", 200, 1.0,
                        new Dictionary<string, string>(), new Dictionary<string, string>(),
                        null, null, null),
                    LookoutJson.Options));
            var ef1 = new LookoutEntry(
                Id: Guid.NewGuid(), Type: "ef", Timestamp: now.AddMilliseconds(-200),
                RequestId: rid, DurationMs: 0.5,
                Tags: new Dictionary<string, string> { ["db.system"] = "ef" },
                Content: JsonSerializer.Serialize(
                    new EfEntryContent("SELECT 1",
                        new List<EfParameter>(), 0.5, 1, "Db", EfCommandType.Scalar,
                        new List<EfStackFrame>()),
                    LookoutJson.Options));
            var ef2 = new LookoutEntry(
                Id: Guid.NewGuid(), Type: "ef", Timestamp: now.AddMilliseconds(-100),
                RequestId: rid, DurationMs: 0.8,
                Tags: new Dictionary<string, string> { ["db.system"] = "ef" },
                Content: JsonSerializer.Serialize(
                    new EfEntryContent("SELECT * FROM orders",
                        new List<EfParameter>(), 0.8, 3, "Db", EfCommandType.Reader,
                        new List<EfStackFrame>()),
                    LookoutJson.Options));
            await storage.WriteAsync(new[] { http, ef1, ef2 });
        }

        await using var app = BuildApp(dbPath);
        await app.StartAsync();
        var client = app.GetTestClient();

        // SPA fallback serves index.html for the client-side detail route.
        var html = await client.GetAsync($"/lookout/requests/{rid}");
        html.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await html.Content.ReadAsStringAsync()).Should().Contain("<div id=\"root\">");

        // Bundle assets are discoverable.
        var jsAsset = FirstEmbeddedWithExtension(".js");
        jsAsset.Should().NotBeNull();
        var js = await client.GetAsync($"/lookout/{jsAsset}");
        js.StatusCode.Should().Be(HttpStatusCode.OK);

        // API returns all three entries correlated by requestId.
        var api = await client.GetAsync($"/lookout/api/requests/{rid}");
        api.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await api.Content.ReadAsStringAsync();
        await app.StopAsync();

        body.Should().Contain("\"http\"");
        body.Should().Contain("\"ef\"");
        body.Should().Contain("SELECT * FROM orders");
    }

    [Fact]
    public async Task ApiRoute_StillMatches_EvenWithCatchAllMounted()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync("/lookout/api/entries");
        await app.StopAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    // ── cross-platform embedded asset tests ───────────────────────────────────

    [Fact]
    public void EmbeddedResourcePaths_NeverContainBackslash()
    {
        // DashboardAssets normalizes Windows MSBuild %(RecursiveDir) backslashes to '/'
        // so URL construction is always forward-slash regardless of build platform.
        var paths = DashboardAssets.EnumerateAssetPaths().ToList();
        if (paths.Count == 0) return; // SkipDashboardUiBuild — no assets embedded

        paths.Should().NotContain(
            p => p.Contains('\\'),
            because: "embedded asset paths must use '/' separators on all platforms");
    }

    [Theory]
    [InlineData("/lookout/", "text/html")]
    [InlineData("/lookout/index.html", "text/html")]
    public async Task GetStaticDashboardPath_Returns200_WithCorrectContentType(
        string requestPath, string expectedContentType)
    {
        if (!HasEmbeddedAssets()) return;

        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync(requestPath);
        await app.StopAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be(expectedContentType);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static bool HasEmbeddedAssets() =>
        DashboardAssets.Assembly
            .GetManifestResourceNames()
            .Any(n => n.StartsWith("LookoutUi/", StringComparison.Ordinal));

    private static string? FirstEmbeddedWithExtension(string ext)
    {
        const string prefix = "LookoutUi/";
        foreach (var name in DashboardAssets.Assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return name.Substring(prefix.Length);
        }
        return null;
    }

    private string TempDbPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lookout_embed_{Guid.NewGuid():N}.db");
        _tempFiles.Add(path);
        return path;
    }

    private static WebApplication BuildApp(string dbPath, string mount = "/lookout")
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddLookout(o => o.StoragePath = dbPath);

        var app = builder.Build();
        app.UseLookout();
        app.MapLookout(mount);
        return app;
    }

}
