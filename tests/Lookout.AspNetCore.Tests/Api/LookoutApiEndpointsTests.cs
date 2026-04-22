using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lookout.AspNetCore.Api;
using Lookout.Core;
using Lookout.Core.Schemas;
using Lookout.Storage.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lookout.AspNetCore.Tests.Api;

public sealed class LookoutApiEndpointsTests : IDisposable
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
    public async Task ListEntries_ReturnsNewestFirst()
    {
        var dbPath = TempDbPath();
        await SeedHttpAsync(dbPath,
            MakeHttp("GET", "/a", 200, offsetMs: -3000),
            MakeHttp("GET", "/b", 200, offsetMs: -2000),
            MakeHttp("GET", "/c", 200, offsetMs: -1000));

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync("/lookout/api/entries");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await ReadListAsync(resp);
        await app.StopAsync();

        payload.Entries.Select(e => PathOf(e)).Should().ContainInOrder("/c", "/b", "/a");
    }

    [Fact]
    public async Task ListEntries_PaginationViaBeforeCursor_IsStable()
    {
        var dbPath = TempDbPath();
        // 5 entries, 1s apart, oldest first
        var seeds = Enumerable.Range(0, 5)
            .Select(i => MakeHttp("GET", $"/p{i}", 200, offsetMs: -1000 * (5 - i)))
            .ToArray();
        await SeedHttpAsync(dbPath, seeds);

        await using var app = BuildApp(dbPath);
        await app.StartAsync();
        var client = app.GetTestClient();

        var page1 = await ReadListAsync(await client.GetAsync("/lookout/api/entries?limit=2"));
        page1.Entries.Should().HaveCount(2);
        page1.NextBefore.Should().NotBeNull();
        page1.Entries.Select(PathOf).Should().ContainInOrder("/p4", "/p3");

        var page2 = await ReadListAsync(
            await client.GetAsync($"/lookout/api/entries?limit=2&before={page1.NextBefore}"));
        page2.Entries.Should().HaveCount(2);
        page2.Entries.Select(PathOf).Should().ContainInOrder("/p2", "/p1");

        var page3 = await ReadListAsync(
            await client.GetAsync($"/lookout/api/entries?limit=2&before={page2.NextBefore}"));
        await app.StopAsync();

        page3.Entries.Should().ContainSingle();
        page3.Entries[0].Tags["http.path"].Should().Be("/p0");
        page3.NextBefore.Should().BeNull("last partial page should not advertise a cursor");
    }

    [Fact]
    public async Task ListEntries_CombinesStatusAndMethodFilters()
    {
        var dbPath = TempDbPath();
        await SeedHttpAsync(dbPath,
            MakeHttp("GET", "/a", 200, offsetMs: -4000),
            MakeHttp("POST", "/a", 500, offsetMs: -3000),  // match
            MakeHttp("GET", "/a", 500, offsetMs: -2000),   // status match, method no
            MakeHttp("POST", "/a", 400, offsetMs: -1000)); // method match, status no

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient()
            .GetAsync("/lookout/api/entries?method=POST&status=500");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await ReadListAsync(resp);
        await app.StopAsync();

        payload.Entries.Should().ContainSingle();
        payload.Entries[0].Tags["http.method"].Should().Be("POST");
        payload.Entries[0].Tags["http.status"].Should().Be("500");
    }

    [Fact]
    public async Task ListEntries_StatusRangeFilter_Works()
    {
        var dbPath = TempDbPath();
        await SeedHttpAsync(dbPath,
            MakeHttp("GET", "/a", 200, offsetMs: -3000),
            MakeHttp("GET", "/b", 404, offsetMs: -2000),
            MakeHttp("GET", "/c", 500, offsetMs: -1000));

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var payload = await ReadListAsync(
            await app.GetTestClient().GetAsync("/lookout/api/entries?status=400-499"));
        await app.StopAsync();

        payload.Entries.Should().ContainSingle();
        payload.Entries[0].Tags["http.path"].Should().Be("/b");
    }

    [Fact]
    public async Task ListEntries_PathSubstring_MatchesCaseInsensitively()
    {
        var dbPath = TempDbPath();
        await SeedHttpAsync(dbPath,
            MakeHttp("GET", "/api/Users/1", 200, offsetMs: -2000),
            MakeHttp("GET", "/api/orders/1", 200, offsetMs: -1000));

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var payload = await ReadListAsync(
            await app.GetTestClient().GetAsync("/lookout/api/entries?path=users"));
        await app.StopAsync();

        payload.Entries.Should().ContainSingle();
        payload.Entries[0].Tags["http.path"].Should().Be("/api/Users/1");
    }

    [Fact]
    public async Task ListEntries_Fts_MatchesAcrossTagsAndContent()
    {
        var dbPath = TempDbPath();
        await SeedHttpAsync(dbPath,
            MakeHttp("GET", "/weatherforecast", 200, offsetMs: -2000),
            MakeHttp("GET", "/orders", 200, offsetMs: -1000));

        await using var app = BuildApp(dbPath);
        await app.StartAsync();
        var client = app.GetTestClient();

        // FTS token from content/tags (path value)
        var byPath = await ReadListAsync(
            await client.GetAsync("/lookout/api/entries?q=weatherforecast"));
        byPath.Entries.Should().ContainSingle()
            .Which.Tags["http.path"].Should().Be("/weatherforecast");

        // FTS token from the json content (method is serialized inside content_json too)
        var byMethod = await ReadListAsync(await client.GetAsync("/lookout/api/entries?q=orders"));
        await app.StopAsync();
        byMethod.Entries.Should().ContainSingle()
            .Which.Tags["http.path"].Should().Be("/orders");
    }

    [Fact]
    public async Task ListEntries_InvalidStatus_Returns400()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync("/lookout/api/entries?status=abc");
        await app.StopAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetEntryById_Returns404_WhenUnknown()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient()
            .GetAsync($"/lookout/api/entries/{Guid.NewGuid()}");
        await app.StopAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetEntryById_ReturnsFullEntry_WhenPresent()
    {
        var dbPath = TempDbPath();
        var entry = MakeHttp("GET", "/thing", 201, offsetMs: 0);
        await SeedHttpAsync(dbPath, entry);

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync($"/lookout/api/entries/{entry.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resp.Content.ReadFromJsonAsync<EntryDto>(LookoutJson.Options);
        await app.StopAsync();

        dto.Should().NotBeNull();
        dto!.Id.Should().Be(entry.Id);
        dto.Type.Should().Be("http");
        dto.Content.GetProperty("statusCode").GetInt32().Should().Be(201);
    }

    [Fact]
    public async Task GetRequestEntries_Returns404_WhenNoMatches()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync("/lookout/api/requests/does-not-exist");
        await app.StopAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRequestEntries_ReturnsAllEntriesForRequestId_OrderedByTimestamp()
    {
        var dbPath = TempDbPath();
        var rid = "req-42";
        await SeedAsync(dbPath,
            MakeHttp("GET", "/x", 200, offsetMs: -2000, requestId: rid),
            MakeHttp("GET", "/x", 200, offsetMs: -1000, requestId: rid),
            MakeHttp("GET", "/y", 200, offsetMs: 0, requestId: "other"));

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync($"/lookout/api/requests/{rid}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dtos = await resp.Content.ReadFromJsonAsync<EntryDto[]>(LookoutJson.Options);
        await app.StopAsync();

        dtos.Should().NotBeNull();
        var list = dtos!;
        list.Should().HaveCount(2);
        list.All(e => e.RequestId == rid).Should().BeTrue();
        list.Select(e => e.Timestamp).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetRequestEntries_ReturnsMixedHttpAndEfEntries_InTimestampOrder()
    {
        var dbPath = TempDbPath();
        var rid = "req-mixed";
        await SeedAsync(dbPath,
            MakeHttp("GET", "/orders", 200, offsetMs: -3000, requestId: rid),
            MakeEf("SELECT * FROM products WHERE id = @p0", offsetMs: -2500, requestId: rid, rowsAffected: 1),
            MakeEf("SELECT * FROM orders", offsetMs: -2000, requestId: rid, rowsAffected: 3));

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync($"/lookout/api/requests/{rid}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dtos = await resp.Content.ReadFromJsonAsync<EntryDto[]>(LookoutJson.Options);
        await app.StopAsync();

        dtos.Should().NotBeNull();
        var list = dtos!;
        list.Should().HaveCount(3);
        list.Select(e => e.Type).Should().ContainInOrder("http", "ef", "ef");
        list.All(e => e.RequestId == rid).Should().BeTrue();
        list.Select(e => e.Timestamp).Should().BeInAscendingOrder();

        // EF content round-trips: commandText and parameters visible from the API.
        var firstEf = list[1];
        firstEf.Content.GetProperty("commandText").GetString()
            .Should().Contain("SELECT");
        firstEf.Content.GetProperty("parameters").GetArrayLength().Should().BeGreaterThan(0);
        firstEf.Content.GetProperty("rowsAffected").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ApiCalls_AreNeverSelfCaptured()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath);
        await app.StartAsync();
        var client = app.GetTestClient();

        // Generate one captured request first, then hit the API several times.
        (await client.GetAsync("/ping")).StatusCode.Should().Be(HttpStatusCode.OK);
        await WaitForCountAsync(dbPath, 1);

        for (var i = 0; i < 5; i++)
            await client.GetAsync("/lookout/api/entries");

        await Task.Delay(300);
        using var storage = new SqliteLookoutStorage(new LookoutOptions { StoragePath = dbPath });
        var rows = await storage.ReadRecentAsync(100);
        await app.StopAsync();

        rows.Should().ContainSingle("only the /ping call should be captured; /lookout/api/* must self-skip");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private string TempDbPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lookout_api_{Guid.NewGuid():N}.db");
        _tempFiles.Add(path);
        return path;
    }

    private static WebApplication BuildApp(string dbPath)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddLookout(o => o.StoragePath = dbPath);

        var app = builder.Build();
        app.UseLookout();
        app.MapLookout();
        app.MapGet("/ping", () => Results.Text("pong"));
        return app;
    }

    private static LookoutEntry MakeHttp(
        string method, string path, int status, long offsetMs, string? requestId = null)
    {
        var ts = DateTimeOffset.UtcNow.AddMilliseconds(offsetMs);
        var content = new HttpEntryContent(
            Method: method,
            Path: path,
            QueryString: string.Empty,
            StatusCode: status,
            DurationMs: 1.0,
            RequestHeaders: new Dictionary<string, string>(),
            ResponseHeaders: new Dictionary<string, string>(),
            RequestBody: null,
            ResponseBody: null,
            User: null);

        return new LookoutEntry(
            Id: Guid.NewGuid(),
            Type: "http",
            Timestamp: ts,
            RequestId: requestId,
            DurationMs: 1.0,
            Tags: new Dictionary<string, string>
            {
                ["http.method"] = method,
                ["http.path"] = path,
                ["http.status"] = status.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            Content: JsonSerializer.Serialize(content, LookoutJson.Options));
    }

    private static Task SeedHttpAsync(string dbPath, params LookoutEntry[] entries) =>
        SeedAsync(dbPath, entries);

    private static LookoutEntry MakeEf(
        string sql, long offsetMs, string? requestId, int? rowsAffected)
    {
        var ts = DateTimeOffset.UtcNow.AddMilliseconds(offsetMs);
        var content = new EfEntryContent(
            CommandText: sql,
            Parameters: new List<EfParameter> { new("@p0", "1", "Int32") },
            DurationMs: 1.0,
            RowsAffected: rowsAffected,
            DbContextType: "WebApp.SampleDbContext",
            CommandType: EfCommandType.Reader,
            Stack: new List<EfStackFrame>
            {
                new("WebApp.Program.<Main>$", "Program.cs", 42),
            });

        return new LookoutEntry(
            Id: Guid.NewGuid(),
            Type: "ef",
            Timestamp: ts,
            RequestId: requestId,
            DurationMs: 1.0,
            Tags: new Dictionary<string, string>
            {
                ["db.system"] = "ef",
            },
            Content: JsonSerializer.Serialize(content, LookoutJson.Options));
    }

    private static async Task SeedAsync(string dbPath, params LookoutEntry[] entries)
    {
        using var storage = new SqliteLookoutStorage(new LookoutOptions { StoragePath = dbPath });
        await storage.WriteAsync(entries);
    }

    private static async Task WaitForCountAsync(string dbPath, int expected, int timeoutSeconds = 5)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var storage = new SqliteLookoutStorage(new LookoutOptions { StoragePath = dbPath });
            var rows = await storage.ReadRecentAsync(100);
            if (rows.Count >= expected) return;
            await Task.Delay(50);
        }
    }

    private static async Task<EntryListResponse> ReadListAsync(HttpResponseMessage resp)
    {
        var payload = await resp.Content.ReadFromJsonAsync<EntryListResponse>(LookoutJson.Options);
        payload.Should().NotBeNull();
        return payload!;
    }

    private static string PathOf(EntryDto dto) => dto.Tags["http.path"];
}
