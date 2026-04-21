using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lookout.AspNetCore.Api;
using Lookout.Core;
using Lookout.Core.Schemas;
using Lookout.EntityFrameworkCore;
using Lookout.Storage.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lookout.AspNetCore.Tests.EfCore;

public sealed class EfCorePipelineTests : IDisposable
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
    public async Task Request_WithEfQueries_SharesRequestId()
    {
        var lookoutDb = TempDbPath();
        var efDb = TempDbPath();

        await using var app = BuildApp(lookoutDb, efDb);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync("/widgets");
        if (resp.StatusCode != HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadAsStringAsync();
            resp.StatusCode.Should().Be(HttpStatusCode.OK, because: body);
        }

        await WaitForCountAsync(lookoutDb, minimum: 2, timeoutSeconds: 5);

        using var storage = new SqliteLookoutStorage(new LookoutOptions { StoragePath = lookoutDb });
        var entries = await storage.ReadRecentAsync(50);
        await app.StopAsync();

        var httpEntries = entries.Where(e => e.Type == "http").ToList();
        var efEntries = entries.Where(e => e.Type == "ef").ToList();

        httpEntries.Should().HaveCount(1);
        efEntries.Should().NotBeEmpty();

        var requestId = httpEntries[0].RequestId;
        requestId.Should().NotBeNullOrEmpty("HTTP middleware must assign an Activity-based RequestId");

        // Pre-startup EnsureCreated/seed entries have null RequestId (no Activity then).
        // Only entries captured during the HTTP request carry the requestId.
        var efEntriesWithRequestId = efEntries.Where(e => e.RequestId != null).ToList();
        efEntriesWithRequestId.Should().NotBeEmpty(
            "EF queries during the HTTP request must carry the request's RequestId");
        efEntriesWithRequestId.Should().OnlyContain(e => e.RequestId == requestId,
            "all EF entries captured inside the HTTP request must share its correlation id");
    }

    [Fact]
    public async Task GetRequestEntries_ReturnsMixedHttpAndEfEntries_InTimestampOrder()
    {
        var lookoutDb = TempDbPath();
        var efDb = TempDbPath();

        await using var app = BuildApp(lookoutDb, efDb);
        await app.StartAsync();
        var client = app.GetTestClient();

        await client.GetAsync("/widgets");
        await WaitForHttpEntryAsync(lookoutDb, timeoutSeconds: 5);

        using var storage = new SqliteLookoutStorage(new LookoutOptions { StoragePath = lookoutDb });
        var allEntries = await storage.ReadRecentAsync(50);
        var requestId = allEntries.First(e => e.Type == "http").RequestId!;

        var resp = await client.GetAsync($"/lookout/api/requests/{requestId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dtos = await resp.Content.ReadFromJsonAsync<EntryDto[]>(LookoutJson.Options);
        await app.StopAsync();

        dtos.Should().NotBeNull();
        var list = dtos!;
        list.Should().HaveCountGreaterThanOrEqualTo(2,
            "at least one http and one ef entry share this requestId");
        list.Select(e => e.Type).Should().Contain("http");
        list.Select(e => e.Type).Should().Contain("ef");
        list.Select(e => e.Timestamp).Should().BeInAscendingOrder(
            "GetByRequestId returns entries in chronological order");
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private string TempDbPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lookout_efpipe_{Guid.NewGuid():N}.db");
        _tempFiles.Add(path);
        return path;
    }

    private static WebApplication BuildApp(string lookoutDbPath, string efDbPath)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddLookout(o => o.StoragePath = lookoutDbPath);
        builder.Services.AddEntityFrameworkCore();
        builder.Services.AddDbContext<EfPipelineDbContext>((sp, opts) =>
        {
            opts.UseSqlite($"Data Source={efDbPath}");
            opts.UseLookout(sp);
        });

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<EfPipelineDbContext>();
            ctx.Database.EnsureCreated();
            ctx.Widgets.Add(new EfPipelineWidget { Name = "Seed" });
            ctx.SaveChanges();
        }

        app.UseLookout();
        app.MapLookout();
        app.MapGet("/widgets", async (EfPipelineDbContext ctx) =>
        {
            var widgets = await ctx.Widgets.ToListAsync();
            return Results.Ok(widgets);
        });

        return app;
    }

    private static async Task WaitForCountAsync(string dbPath, int minimum, int timeoutSeconds)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var storage = new SqliteLookoutStorage(new LookoutOptions { StoragePath = dbPath });
                var rows = await storage.ReadRecentAsync(100);
                if (rows.Count >= minimum) return;
            }
            catch { }
            await Task.Delay(50);
        }
    }

    private static async Task WaitForHttpEntryAsync(string dbPath, int timeoutSeconds)
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
            catch { }
            await Task.Delay(50);
        }
    }
}

// Test-scoped EF entities — unique names avoid collision with other test assemblies.
internal sealed class EfPipelineWidget { public int Id { get; set; } public string Name { get; set; } = ""; }

internal sealed class EfPipelineDbContext : DbContext
{
    public EfPipelineDbContext(DbContextOptions<EfPipelineDbContext> options) : base(options) { }
    public DbSet<EfPipelineWidget> Widgets => Set<EfPipelineWidget>();
}
