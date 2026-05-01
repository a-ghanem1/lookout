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
    public async Task GetRequestEntries_ByEntryId_ReturnsAllCorrelatedEntries()
    {
        // Regression: /orders/1 makes an HttpClient call to /payments/check/1 on the same
        // server. Both get the same requestId (Activity.RootId propagates via W3C headers).
        // The frontend now navigates with the specific entry's own Guid id so that the detail
        // view can identify the correct root HTTP entry. The backend must resolve that entry id
        // back to its requestId and return all correlated entries.
        var dbPath = TempDbPath();
        var sharedRequestId = "trace-abc-shared";
        var paymentsEntry = MakeHttp("GET", "/payments/check/1", 200, offsetMs: -2000, requestId: sharedRequestId);
        var ordersEntry = MakeHttp("GET", "/orders/1", 200, offsetMs: -1000, requestId: sharedRequestId);
        await SeedAsync(dbPath, paymentsEntry, ordersEntry);

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        // Navigate using orders entry's own id (what the frontend now passes)
        var resp = await app.GetTestClient().GetAsync($"/lookout/api/requests/{ordersEntry.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dtos = await resp.Content.ReadFromJsonAsync<EntryDto[]>(LookoutJson.Options);
        await app.StopAsync();

        dtos.Should().NotBeNull().And.HaveCount(2, "both correlated entries should be returned");
        var list = dtos!;
        list.All(e => e.RequestId == sharedRequestId).Should().BeTrue();
        // Both entries are returned — frontend uses the id to pick the right one
        list.Select(e => e.Tags["http.path"]).Should().Contain("/orders/1").And.Contain("/payments/check/1");
    }

    [Fact]
    public async Task GetRequestEntries_ByEntryId_NoRequestId_ReturnsSingleEntry()
    {
        // If an http entry has no requestId, resolving by entry id still returns that entry.
        var dbPath = TempDbPath();
        var orphan = MakeHttp("GET", "/standalone", 200, offsetMs: -500, requestId: null);
        await SeedAsync(dbPath, orphan);

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync($"/lookout/api/requests/{orphan.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dtos = await resp.Content.ReadFromJsonAsync<EntryDto[]>(LookoutJson.Options);
        await app.StopAsync();

        dtos.Should().ContainSingle();
        dtos![0].Tags["http.path"].Should().Be("/standalone");
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

    [Fact]
    public async Task GetEntryCounts_ReturnsZerosForEmptyDb()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync("/lookout/api/entries/counts");
        await app.StopAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var counts = await resp.Content.ReadFromJsonAsync<EntryCounts>(LookoutJson.Options);
        counts.Should().NotBeNull();
        counts!.Requests.Should().Be(0);
        counts.Queries.Should().Be(0);
        counts.Exceptions.Should().Be(0);
        counts.Logs.Should().Be(0);
        counts.Cache.Should().Be(0);
        counts.HttpClients.Should().Be(0);
        counts.Jobs.Should().Be(0);
        counts.Dump.Should().Be(0);
    }

    [Fact]
    public async Task GetEntryCounts_CorrectlyGroupsByType()
    {
        var dbPath = TempDbPath();
        var rid = "req-counts";
        await SeedAsync(dbPath,
            MakeHttp("GET", "/a", 200, offsetMs: -6000, requestId: rid),
            MakeHttp("POST", "/b", 201, offsetMs: -5000),
            MakeEf("SELECT 1", offsetMs: -4000, requestId: rid, rowsAffected: 1),
            MakeEntry("sql", rid, offsetMs: -3500),
            MakeEntry("exception", rid, offsetMs: -3000),
            MakeEntry("log", null, offsetMs: -2500),
            MakeEntry("cache", rid, offsetMs: -2000),
            MakeEntry("http-out", null, offsetMs: -1500),
            MakeEntry("job-enqueue", rid, offsetMs: -1000),
            MakeEntry("job-execution", null, offsetMs: -500),
            MakeEntry("dump", rid, offsetMs: 0));

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync("/lookout/api/entries/counts");
        await app.StopAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var counts = await resp.Content.ReadFromJsonAsync<EntryCounts>(LookoutJson.Options);
        counts.Should().NotBeNull();
        counts!.Requests.Should().Be(2, "two http entries");
        counts.Queries.Should().Be(2, "one ef + one sql");
        counts.Exceptions.Should().Be(1);
        counts.Logs.Should().Be(1);
        counts.Cache.Should().Be(1);
        counts.HttpClients.Should().Be(1);
        counts.Jobs.Should().Be(2, "one job-enqueue + one job-execution");
        counts.Dump.Should().Be(1);
    }

    [Fact]
    public async Task GetEntryCounts_ResponseHasNoCacheHeader()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync("/lookout/api/entries/counts");
        await app.StopAsync();

        resp.Headers.CacheControl.Should().NotBeNull();
        resp.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task ListEntries_SortByDuration_ReturnsSlowestFirst()
    {
        var dbPath = TempDbPath();
        await SeedAsync(dbPath,
            MakeEfWithDuration("SELECT fast", durationMs: 5.0, offsetMs: -3000),
            MakeEfWithDuration("SELECT medium", durationMs: 150.0, offsetMs: -2000),
            MakeEfWithDuration("SELECT slow", durationMs: 800.0, offsetMs: -1000));

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync("/lookout/api/entries?sort=duration");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await ReadListAsync(resp);
        await app.StopAsync();

        payload.Entries.Should().HaveCount(3);
        payload.Entries[0].DurationMs.Should().Be(800.0, "slowest first");
        payload.Entries[1].DurationMs.Should().Be(150.0);
        payload.Entries[2].DurationMs.Should().Be(5.0);
    }

    [Fact]
    public async Task ListEntries_MinDurationMs_FiltersOutShortEntries()
    {
        var dbPath = TempDbPath();
        await SeedAsync(dbPath,
            MakeEfWithDuration("SELECT fast", durationMs: 5.0, offsetMs: -3000),
            MakeEfWithDuration("SELECT medium", durationMs: 150.0, offsetMs: -2000),
            MakeEfWithDuration("SELECT slow", durationMs: 800.0, offsetMs: -1000));

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync("/lookout/api/entries?min_duration_ms=100");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await ReadListAsync(resp);
        await app.StopAsync();

        payload.Entries.Should().HaveCount(2, "only medium and slow qualify");
        payload.Entries.Should().AllSatisfy(e => e.DurationMs.Should().BeGreaterThanOrEqualTo(100.0));
    }

    [Fact]
    public async Task ListEntries_MultiType_ReturnsBothEfAndSql()
    {
        var dbPath = TempDbPath();
        await SeedAsync(dbPath,
            MakeEfWithDuration("SELECT ef query", durationMs: 10.0, offsetMs: -2000),
            MakeEntry("sql", null, offsetMs: -1000),
            MakeHttp("GET", "/ignored", 200, offsetMs: -500));

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync("/lookout/api/entries?type=ef%2Csql");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await ReadListAsync(resp);
        await app.StopAsync();

        payload.Entries.Should().HaveCount(2, "http entry excluded; both ef and sql included");
        payload.Entries.Select(e => e.Type).Should().BeEquivalentTo(["ef", "sql"]);
    }

    [Fact]
    public async Task ListEntries_UrlHost_FiltersHttpOutByHost()
    {
        var dbPath = TempDbPath();
        await SeedAsync(dbPath,
            MakeHttpOut("GET", "https://api.example.com/users", 200, offsetMs: -3000),
            MakeHttpOut("GET", "https://payments.io/charge", 201, offsetMs: -2000),
            MakeHttpOut("GET", "https://api.example.com/orders", 200, offsetMs: -1000));

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync("/lookout/api/entries?host=api.example.com");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await ReadListAsync(resp);
        await app.StopAsync();

        payload.Entries.Should().HaveCount(2, "only api.example.com entries");
        payload.Entries.Should().AllSatisfy(e => e.Tags["http.url.host"].Should().Be("api.example.com"));
    }

    [Fact]
    public async Task ListEntries_ErrorsOnly_FiltersToNetworkErrors()
    {
        var dbPath = TempDbPath();
        await SeedAsync(dbPath,
            MakeHttpOut("GET", "https://ok.io/path", 200, offsetMs: -3000),
            MakeHttpOutError("GET", "https://fail.io/path", "System.Net.Http.HttpRequestException", offsetMs: -2000),
            MakeHttpOut("POST", "https://ok.io/create", 201, offsetMs: -1000));

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync("/lookout/api/entries?errors_only=true");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await ReadListAsync(resp);
        await app.StopAsync();

        payload.Entries.Should().ContainSingle("only the network-error entry qualifies");
        payload.Entries[0].Tags.Should().ContainKey("http.error");
    }

    private static LookoutEntry MakeEfWithDuration(string sql, double durationMs, long offsetMs)
    {
        var ts = DateTimeOffset.UtcNow.AddMilliseconds(offsetMs);
        var content = new EfEntryContent(
            CommandText: sql,
            Parameters: [],
            DurationMs: durationMs,
            RowsAffected: null,
            DbContextType: null,
            CommandType: EfCommandType.Reader,
            Stack: []);

        return new LookoutEntry(
            Id: Guid.NewGuid(),
            Type: "ef",
            Timestamp: ts,
            RequestId: null,
            DurationMs: durationMs,
            Tags: new Dictionary<string, string> { ["db.system"] = "ef" },
            Content: JsonSerializer.Serialize(content, LookoutJson.Options));
    }

    private static LookoutEntry MakeHttpOut(string method, string url, int status, long offsetMs)
    {
        var ts = DateTimeOffset.UtcNow.AddMilliseconds(offsetMs);
        var uri = new Uri(url);
        var content = new OutboundHttpEntryContent(
            Method: method,
            Url: url,
            StatusCode: status,
            DurationMs: 10.0,
            RequestHeaders: new Dictionary<string, string>(),
            ResponseHeaders: new Dictionary<string, string>(),
            RequestBody: null,
            ResponseBody: null,
            ErrorType: null,
            ErrorMessage: null);

        return new LookoutEntry(
            Id: Guid.NewGuid(),
            Type: "http-out",
            Timestamp: ts,
            RequestId: null,
            DurationMs: 10.0,
            Tags: new Dictionary<string, string>
            {
                ["http.method"] = method,
                ["http.out"] = "true",
                ["http.url.host"] = uri.Host,
                ["http.url.path"] = uri.AbsolutePath,
                ["http.status"] = status.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            Content: JsonSerializer.Serialize(content, LookoutJson.Options));
    }

    private static LookoutEntry MakeHttpOutError(string method, string url, string errorType, long offsetMs)
    {
        var ts = DateTimeOffset.UtcNow.AddMilliseconds(offsetMs);
        var uri = new Uri(url);
        var content = new OutboundHttpEntryContent(
            Method: method,
            Url: url,
            StatusCode: null,
            DurationMs: 5.0,
            RequestHeaders: new Dictionary<string, string>(),
            ResponseHeaders: new Dictionary<string, string>(),
            RequestBody: null,
            ResponseBody: null,
            ErrorType: errorType,
            ErrorMessage: "Connection refused");

        return new LookoutEntry(
            Id: Guid.NewGuid(),
            Type: "http-out",
            Timestamp: ts,
            RequestId: null,
            DurationMs: 5.0,
            Tags: new Dictionary<string, string>
            {
                ["http.method"] = method,
                ["http.out"] = "true",
                ["http.url.host"] = uri.Host,
                ["http.url.path"] = uri.AbsolutePath,
                ["http.error"] = errorType,
            },
            Content: JsonSerializer.Serialize(content, LookoutJson.Options));
    }

    private static LookoutEntry MakeEntry(string type, string? requestId, long offsetMs)
    {
        var ts = DateTimeOffset.UtcNow.AddMilliseconds(offsetMs);
        return new LookoutEntry(
            Id: Guid.NewGuid(),
            Type: type,
            Timestamp: ts,
            RequestId: requestId,
            DurationMs: 1.0,
            Tags: new Dictionary<string, string> { ["entry.type"] = type },
            Content: "{}");
    }

    [Fact]
    public async Task CacheSummary_ReturnsZerosForEmptyDb()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync("/lookout/api/entries/cache/summary");
        await app.StopAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await resp.Content.ReadFromJsonAsync<CacheSummary>(LookoutJson.Options);
        summary.Should().NotBeNull();
        summary!.Hits.Should().Be(0);
        summary.Misses.Should().Be(0);
        summary.Sets.Should().Be(0);
        summary.Removes.Should().Be(0);
        summary.HitRatio.Should().Be(0.0);
    }

    [Fact]
    public async Task CacheSummary_ReturnsCorrectCountsAcrossMixedEntries()
    {
        var dbPath = TempDbPath();
        await SeedAsync(dbPath,
            MakeCache("Get", "user:1", hit: true, system: "memory", offsetMs: -5000),
            MakeCache("Get", "user:2", hit: true, system: "memory", offsetMs: -4000),
            MakeCache("Get", "user:3", hit: false, system: "distributed", offsetMs: -3000),
            MakeCache("Set", "user:4", hit: null, system: "memory", offsetMs: -2000),
            MakeCache("Remove", "user:5", hit: null, system: "memory", offsetMs: -1000));

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync("/lookout/api/entries/cache/summary");
        await app.StopAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await resp.Content.ReadFromJsonAsync<CacheSummary>(LookoutJson.Options);
        summary.Should().NotBeNull();
        summary!.Hits.Should().Be(2, "two successful gets");
        summary.Misses.Should().Be(1, "one cache miss");
        summary.Sets.Should().Be(1);
        summary.Removes.Should().Be(1);
        summary.HitRatio.Should().BeApproximately(2.0 / 3.0, precision: 0.001);
    }

    [Fact]
    public async Task CacheSummary_HitRatioIsZeroWhenNoGetOperations()
    {
        var dbPath = TempDbPath();
        await SeedAsync(dbPath,
            MakeCache("Set", "key:a", hit: null, system: "memory", offsetMs: -2000),
            MakeCache("Remove", "key:b", hit: null, system: "memory", offsetMs: -1000));

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync("/lookout/api/entries/cache/summary");
        await app.StopAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await resp.Content.ReadFromJsonAsync<CacheSummary>(LookoutJson.Options);
        summary.Should().NotBeNull();
        summary!.Hits.Should().Be(0);
        summary.Misses.Should().Be(0);
        summary.Sets.Should().Be(1);
        summary.Removes.Should().Be(1);
        summary.HitRatio.Should().Be(0.0, "no get operations → ratio is 0");
    }

    [Fact]
    public async Task CacheSummary_ResponseHasNoCacheHeader()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var resp = await app.GetTestClient().GetAsync("/lookout/api/entries/cache/summary");
        await app.StopAsync();

        resp.Headers.CacheControl.Should().NotBeNull();
        resp.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    private static LookoutEntry MakeCache(string operation, string key, bool? hit, string system, long offsetMs)
    {
        var ts = DateTimeOffset.UtcNow.AddMilliseconds(offsetMs);
        var content = new CacheEntryContent(
            Operation: operation,
            Key: key,
            Hit: hit,
            DurationMs: 0.5,
            ValueType: null,
            ValueBytes: null);

        var tags = new Dictionary<string, string>
        {
            ["cache.system"] = system,
            ["cache.key"] = key,
        };
        if (hit.HasValue)
            tags["cache.hit"] = hit.Value ? "true" : "false";

        return new LookoutEntry(
            Id: Guid.NewGuid(),
            Type: "cache",
            Timestamp: ts,
            RequestId: null,
            DurationMs: 0.5,
            Tags: tags,
            Content: JsonSerializer.Serialize(content, LookoutJson.Options));
    }

    private static string PathOf(EntryDto dto) => dto.Tags["http.path"];

    [Fact]
    public async Task MinLevel_Warning_ExcludesLowerLevels()
    {
        var dbPath = TempDbPath();
        await SeedAsync(dbPath,
            MakeLog("Trace", "App", "trace msg", offsetMs: -5000),
            MakeLog("Debug", "App", "debug msg", offsetMs: -4000),
            MakeLog("Information", "App", "info msg", offsetMs: -3000),
            MakeLog("Warning", "App", "warn msg", offsetMs: -2000),
            MakeLog("Error", "App", "error msg", offsetMs: -1000));

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var payload = await ReadListAsync(
            await app.GetTestClient().GetAsync("/lookout/api/entries?type=log&min_level=Warning"));
        await app.StopAsync();

        payload.Entries.Should().HaveCount(2);
        payload.Entries.Select(e => e.Tags["log.level"]).Should().BeEquivalentTo(["Error", "Warning"]);
    }

    [Fact]
    public async Task MinLevel_Information_IncludesInformationAndAbove()
    {
        var dbPath = TempDbPath();
        await SeedAsync(dbPath,
            MakeLog("Trace", "App", "trace msg", offsetMs: -4000),
            MakeLog("Debug", "App", "debug msg", offsetMs: -3000),
            MakeLog("Information", "App", "info msg", offsetMs: -2000),
            MakeLog("Warning", "App", "warn msg", offsetMs: -1000));

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var payload = await ReadListAsync(
            await app.GetTestClient().GetAsync("/lookout/api/entries?type=log&min_level=Information"));
        await app.StopAsync();

        payload.Entries.Should().HaveCount(2);
        payload.Entries.Select(e => e.Tags["log.level"]).Should().BeEquivalentTo(["Warning", "Information"]);
    }

    [Fact]
    public async Task Handled_True_ReturnsOnlyHandledExceptions()
    {
        var dbPath = TempDbPath();
        await SeedAsync(dbPath,
            MakeException("System.InvalidOperationException", "handled ex", handled: true, offsetMs: -3000),
            MakeException("System.NullReferenceException", "unhandled ex", handled: false, offsetMs: -2000),
            MakeException("System.ArgumentException", "also handled", handled: true, offsetMs: -1000));

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var payload = await ReadListAsync(
            await app.GetTestClient().GetAsync("/lookout/api/entries?type=exception&handled=true"));
        await app.StopAsync();

        payload.Entries.Should().HaveCount(2);
        payload.Entries.Should().AllSatisfy(e => e.Tags["exception.handled"].Should().Be("true"));
    }

    [Fact]
    public async Task Handled_False_ReturnsOnlyUnhandledExceptions()
    {
        var dbPath = TempDbPath();
        await SeedAsync(dbPath,
            MakeException("System.InvalidOperationException", "handled ex", handled: true, offsetMs: -2000),
            MakeException("System.NullReferenceException", "unhandled ex", handled: false, offsetMs: -1000));

        await using var app = BuildApp(dbPath);
        await app.StartAsync();

        var payload = await ReadListAsync(
            await app.GetTestClient().GetAsync("/lookout/api/entries?type=exception&handled=false"));
        await app.StopAsync();

        payload.Entries.Should().ContainSingle();
        payload.Entries[0].Tags["exception.handled"].Should().Be("false");
        payload.Entries[0].Tags["exception.type"].Should().Be("System.NullReferenceException");
    }

    private static LookoutEntry MakeLog(string level, string category, string message, long offsetMs)
    {
        var ts = DateTimeOffset.UtcNow.AddMilliseconds(offsetMs);
        var content = new LogEntryContent(
            Level: level,
            Category: category,
            Message: message,
            EventId: new LogEventId(0, null),
            Scopes: [],
            ExceptionType: null,
            ExceptionMessage: null);

        return new LookoutEntry(
            Id: Guid.NewGuid(),
            Type: "log",
            Timestamp: ts,
            RequestId: null,
            DurationMs: 0,
            Tags: new Dictionary<string, string>
            {
                ["log.level"] = level,
                ["log.category"] = category,
            },
            Content: JsonSerializer.Serialize(content, LookoutJson.Options));
    }

    private static LookoutEntry MakeException(string exType, string message, bool handled, long offsetMs)
    {
        var ts = DateTimeOffset.UtcNow.AddMilliseconds(offsetMs);
        var content = new ExceptionEntryContent(
            ExceptionType: exType,
            Message: message,
            Stack: [],
            InnerExceptions: [],
            Source: null,
            HResult: -2146233088,
            Handled: handled);

        return new LookoutEntry(
            Id: Guid.NewGuid(),
            Type: "exception",
            Timestamp: ts,
            RequestId: null,
            DurationMs: 0,
            Tags: new Dictionary<string, string>
            {
                ["exception.type"] = exType,
                ["exception.handled"] = handled ? "true" : "false",
            },
            Content: JsonSerializer.Serialize(content, LookoutJson.Options));
    }
}
