using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lookout.Core;
using Lookout.EntityFrameworkCore;
using Lookout.Storage.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lookout.AspNetCore.Tests.N1Detection;

/// <summary>
/// End-to-end tests for N+1 detection wired through the full middleware → EF interceptor →
/// N1RequestScope → HTTP entry tags pipeline.
/// </summary>
public sealed class N1DetectionIntegrationTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _tempFiles)
            foreach (var f in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(f)) File.Delete(f);
    }

    // ── N+1 detection: EF entries ─────────────────────────────────────────────

    [Fact]
    public async Task FiveIdenticalEfQueries_TaggedWithN1GroupAndCount()
    {
        var lookoutDb = TempDbPath();
        var efDb = TempDbPath();

        // Endpoint issues 5 identical-shape EF queries (classic N+1 pattern).
        await using var app = BuildApp(lookoutDb, efDb,
            endpoint: "/n1",
            handler: async (N1DbContext ctx) =>
            {
                for (var i = 0; i < 5; i++)
                    await ctx.Widgets.Where(w => w.Id == i).ToListAsync();
                return Results.Ok();
            });

        await app.StartAsync();
        var resp = await app.GetTestClient().GetAsync("/n1");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        await WaitForHttpEntryAsync(lookoutDb);
        using var storage = new SqliteLookoutStorage(new LookoutOptions { StoragePath = lookoutDb });
        var entries = await storage.ReadRecentAsync(50);
        await app.StopAsync();

        var efEntries = entries.Where(e => e.Type == "ef" && e.RequestId != null).ToList();
        efEntries.Should().NotBeEmpty("EF queries inside the request must be captured");

        // All 5 query entries should carry N+1 tags.
        var n1Tagged = efEntries.Where(e => e.Tags.ContainsKey("n1.group")).ToList();
        n1Tagged.Should().HaveCount(5, "5 identical-shape queries → all tagged");
        n1Tagged.Should().OnlyContain(e => e.Tags["n1.count"] == "5",
            "n1.count must equal the group size");

        // All 5 must share the same n1.group hash.
        n1Tagged.Select(e => e.Tags["n1.group"]).Distinct()
            .Should().ContainSingle("all entries in the same group share one shape hash");
    }

    [Fact]
    public async Task TwoIdenticalEfQueries_BelowThreshold_NotTagged()
    {
        var lookoutDb = TempDbPath();
        var efDb = TempDbPath();

        await using var app = BuildApp(lookoutDb, efDb,
            endpoint: "/no-n1",
            handler: async (N1DbContext ctx) =>
            {
                for (var i = 0; i < 2; i++)
                    await ctx.Widgets.Where(w => w.Id == i).ToListAsync();
                return Results.Ok();
            });

        await app.StartAsync();
        await app.GetTestClient().GetAsync("/no-n1");
        await WaitForHttpEntryAsync(lookoutDb);
        using var storage = new SqliteLookoutStorage(new LookoutOptions { StoragePath = lookoutDb });
        var entries = await storage.ReadRecentAsync(50);
        await app.StopAsync();

        var efEntries = entries.Where(e => e.Type == "ef" && e.RequestId != null).ToList();
        efEntries.Should().NotContain(e => e.Tags.ContainsKey("n1.group"),
            "2 queries is below the default threshold of 3");
    }

    [Fact]
    public async Task TwoGroupsOfThree_BothDetected()
    {
        var lookoutDb = TempDbPath();
        var efDb = TempDbPath();

        await using var app = BuildApp(lookoutDb, efDb,
            endpoint: "/two-groups",
            handler: async (N1DbContext ctx) =>
            {
                // Group A: 3 queries on Widgets
                for (var i = 0; i < 3; i++)
                    await ctx.Widgets.Where(w => w.Id == i).ToListAsync();
                // Group B: 3 queries on Tags (different shape)
                for (var j = 0; j < 3; j++)
                    await ctx.Tags.Where(t => t.Id == j).ToListAsync();
                return Results.Ok();
            });

        await app.StartAsync();
        await app.GetTestClient().GetAsync("/two-groups");
        await WaitForHttpEntryAsync(lookoutDb);
        using var storage = new SqliteLookoutStorage(new LookoutOptions { StoragePath = lookoutDb });
        var entries = await storage.ReadRecentAsync(100);
        await app.StopAsync();

        var efEntries = entries.Where(e => e.Type == "ef" && e.RequestId != null).ToList();
        var n1Tagged = efEntries.Where(e => e.Tags.ContainsKey("n1.group")).ToList();

        n1Tagged.Select(e => e.Tags["n1.group"]).Distinct()
            .Should().HaveCount(2, "two distinct SQL shapes → two N+1 groups");
    }

    // ── HTTP entry tags ───────────────────────────────────────────────────────

    [Fact]
    public async Task HttpEntry_HasDbCountTag_EqualToTotalEfEntries()
    {
        var lookoutDb = TempDbPath();
        var efDb = TempDbPath();

        await using var app = BuildApp(lookoutDb, efDb,
            endpoint: "/db-count",
            handler: async (N1DbContext ctx) =>
            {
                // Issue exactly 2 EF queries during the request.
                await ctx.Widgets.Where(w => w.Id == 1).ToListAsync();
                await ctx.Widgets.Where(w => w.Id == 2).ToListAsync();
                return Results.Ok();
            });

        await app.StartAsync();
        await app.GetTestClient().GetAsync("/db-count");
        await WaitForHttpEntryAsync(lookoutDb);
        using var storage = new SqliteLookoutStorage(new LookoutOptions { StoragePath = lookoutDb });
        var entries = await storage.ReadRecentAsync(50);
        await app.StopAsync();

        var httpEntry = entries.Single(e => e.Type == "http");
        httpEntry.Tags.Should().ContainKey("db.count",
            "HTTP entry must carry the total DB query count");
        var dbCount = int.Parse(httpEntry.Tags["db.count"]);
        dbCount.Should().BeGreaterThanOrEqualTo(2,
            "at least the 2 explicit queries must be counted; EF may also emit internal queries");
    }

    [Fact]
    public async Task HttpEntry_HasN1DetectedTag_WhenN1Detected()
    {
        var lookoutDb = TempDbPath();
        var efDb = TempDbPath();

        await using var app = BuildApp(lookoutDb, efDb,
            endpoint: "/http-n1",
            handler: async (N1DbContext ctx) =>
            {
                for (var i = 0; i < 3; i++)
                    await ctx.Widgets.Where(w => w.Id == i).ToListAsync();
                return Results.Ok();
            });

        await app.StartAsync();
        await app.GetTestClient().GetAsync("/http-n1");
        await WaitForHttpEntryAsync(lookoutDb);
        using var storage = new SqliteLookoutStorage(new LookoutOptions { StoragePath = lookoutDb });
        var entries = await storage.ReadRecentAsync(50);
        await app.StopAsync();

        var httpEntry = entries.Single(e => e.Type == "http");
        httpEntry.Tags.Should().ContainKey("n1.detected",
            "HTTP entry must carry n1.detected=true when N+1 is found");
        httpEntry.Tags["n1.detected"].Should().Be("true");
        httpEntry.Tags.Should().ContainKey("n1.groups");
        int.Parse(httpEntry.Tags["n1.groups"]).Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task HttpEntry_NoN1Tag_WhenBelowThreshold()
    {
        var lookoutDb = TempDbPath();
        var efDb = TempDbPath();

        await using var app = BuildApp(lookoutDb, efDb,
            endpoint: "/http-no-n1",
            handler: async (N1DbContext ctx) =>
            {
                await ctx.Widgets.Where(w => w.Id == 1).ToListAsync();
                return Results.Ok();
            });

        await app.StartAsync();
        await app.GetTestClient().GetAsync("/http-no-n1");
        await WaitForHttpEntryAsync(lookoutDb);
        using var storage = new SqliteLookoutStorage(new LookoutOptions { StoragePath = lookoutDb });
        var entries = await storage.ReadRecentAsync(50);
        await app.StopAsync();

        var httpEntry = entries.Single(e => e.Type == "http");
        httpEntry.Tags.Should().NotContainKey("n1.detected",
            "n1.detected must not appear when no N+1 is found");
    }

    // ── N1IgnorePatterns ──────────────────────────────────────────────────────

    [Fact]
    public async Task N1IgnorePatterns_SuppressKnownShape()
    {
        var lookoutDb = TempDbPath();
        var efDb = TempDbPath();

        await using var app = BuildApp(lookoutDb, efDb,
            endpoint: "/ignored",
            handler: async (N1DbContext ctx) =>
            {
                for (var i = 0; i < 5; i++)
                    await ctx.Widgets.Where(w => w.Id == i).ToListAsync();
                return Results.Ok();
            },
            configure: o => o.Ef.N1IgnorePatterns.Add(new Regex("Widgets", RegexOptions.IgnoreCase)));

        await app.StartAsync();
        await app.GetTestClient().GetAsync("/ignored");
        await WaitForHttpEntryAsync(lookoutDb);
        using var storage = new SqliteLookoutStorage(new LookoutOptions { StoragePath = lookoutDb });
        var entries = await storage.ReadRecentAsync(50);
        await app.StopAsync();

        var httpEntry = entries.Single(e => e.Type == "http");
        httpEntry.Tags.Should().NotContainKey("n1.detected",
            "Widgets shape is suppressed by N1IgnorePatterns");

        var efEntries = entries.Where(e => e.Type == "ef" && e.RequestId != null).ToList();
        efEntries.Should().NotContain(e => e.Tags.ContainsKey("n1.group"),
            "ignored shape entries must not be tagged");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private string TempDbPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lookout_n1_{Guid.NewGuid():N}.db");
        _tempFiles.Add(path);
        return path;
    }

    private static WebApplication BuildApp(
        string lookoutDbPath,
        string efDbPath,
        string endpoint,
        Delegate handler,
        Action<LookoutOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddLookout(o =>
        {
            o.StoragePath = lookoutDbPath;
            configure?.Invoke(o);
        });
        builder.Services.AddEntityFrameworkCore();
        builder.Services.AddDbContext<N1DbContext>((sp, opts) =>
        {
            opts.UseSqlite($"Data Source={efDbPath}");
            opts.UseLookout(sp);
        });

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<N1DbContext>();
            ctx.Database.EnsureCreated();
        }

        app.UseLookout();
        app.MapLookout();
        app.MapGet(endpoint, handler);

        return app;
    }

    private static async Task WaitForHttpEntryAsync(string dbPath, int timeoutSeconds = 5)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var storage = new SqliteLookoutStorage(new LookoutOptions { StoragePath = dbPath });
                var rows = await storage.ReadRecentAsync(100);
                if (rows.Any(e => e.Type == "http")) return;
            }
            catch { /* storage not ready yet */ }
            await Task.Delay(50);
        }
    }
}

// Test-scoped EF types — distinct names prevent collision with other test classes.
internal sealed class N1Widget { public int Id { get; set; } public string Name { get; set; } = ""; }
internal sealed class N1Tag { public int Id { get; set; } public string Value { get; set; } = ""; }

internal sealed class N1DbContext : DbContext
{
    public N1DbContext(DbContextOptions<N1DbContext> options) : base(options) { }
    public DbSet<N1Widget> Widgets => Set<N1Widget>();
    public DbSet<N1Tag> Tags => Set<N1Tag>();
}
