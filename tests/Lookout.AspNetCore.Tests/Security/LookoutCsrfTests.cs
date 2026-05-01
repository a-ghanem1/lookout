using System.Net;
using FluentAssertions;
using Lookout.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lookout.AspNetCore.Tests.Security;

public sealed class LookoutCsrfTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _tempFiles)
            foreach (var f in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(f)) File.Delete(f);
    }

    // ── CSRF token validation ────────────────────────────────────────────────

    [Fact]
    public async Task Delete_CorrectCsrfTokenAndSameOrigin_Returns200()
    {
        await using var app = BuildApp();
        await app.StartAsync();

        var mount = app.Services.GetRequiredService<LookoutMountInfo>();
        var request = new HttpRequestMessage(HttpMethod.Delete, "/lookout/api/entries");
        request.Headers.Add("Origin", "http://localhost");
        request.Headers.Add("X-Lookout-Csrf-Token", mount.CsrfToken);

        var resp = await app.GetTestClient().SendAsync(request);
        await app.StopAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_MissingCsrfToken_Returns403WithCsrfBody()
    {
        await using var app = BuildApp();
        await app.StartAsync();

        var request = new HttpRequestMessage(HttpMethod.Delete, "/lookout/api/entries");
        request.Headers.Add("Origin", "http://localhost");
        // No X-Lookout-Csrf-Token

        var resp = await app.GetTestClient().SendAsync(request);
        await app.StopAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("csrf");
    }

    [Fact]
    public async Task Delete_WrongCsrfToken_Returns403()
    {
        await using var app = BuildApp();
        await app.StartAsync();

        var request = new HttpRequestMessage(HttpMethod.Delete, "/lookout/api/entries");
        request.Headers.Add("Origin", "http://localhost");
        request.Headers.Add("X-Lookout-Csrf-Token", "totally-wrong-token");

        var resp = await app.GetTestClient().SendAsync(request);
        await app.StopAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Delete_CorrectCsrfTokenButCrossOrigin_Returns403()
    {
        await using var app = BuildApp();
        await app.StartAsync();

        var mount = app.Services.GetRequiredService<LookoutMountInfo>();
        var request = new HttpRequestMessage(HttpMethod.Delete, "/lookout/api/entries");
        request.Headers.Add("Origin", "http://evil.example.com"); // cross-origin
        request.Headers.Add("X-Lookout-Csrf-Token", mount.CsrfToken);

        var resp = await app.GetTestClient().SendAsync(request);
        await app.StopAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── CSRF cookie is set on dashboard load ─────────────────────────────────

    [Fact]
    public async Task GetDashboard_SetsCsrfCookie()
    {
        await using var app = BuildApp();
        await app.StartAsync();

        var mount = app.Services.GetRequiredService<LookoutMountInfo>();
        var resp = await app.GetTestClient().GetAsync("/lookout");
        await app.StopAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var setCookie = resp.Headers.GetValues("Set-Cookie").FirstOrDefault(v => v.Contains("__lookout-csrf"));
        setCookie.Should().NotBeNull("the server must set the __lookout-csrf cookie on dashboard load");
        setCookie.Should().Contain(mount.CsrfToken);
        setCookie!.ToLowerInvariant().Should().Contain("samesite=strict");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private WebApplication BuildApp()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"lookout_csrf_{Guid.NewGuid():N}.db");
        _tempFiles.Add(dbPath);

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddLookout(o => o.StoragePath = dbPath);

        var app = builder.Build();
        app.UseLookout();
        app.MapLookout();
        return app;
    }
}
