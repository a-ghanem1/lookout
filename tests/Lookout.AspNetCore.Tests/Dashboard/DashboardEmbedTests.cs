using System.Net;
using FluentAssertions;
using Lookout.Dashboard;
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
