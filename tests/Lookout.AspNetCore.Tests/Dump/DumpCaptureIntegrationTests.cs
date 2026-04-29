using System.Text.Json;
using FluentAssertions;
using Lookout.Core;
using Lookout.Core.Schemas;
using static Lookout.Core.Lookout;
using Lookout.Storage.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lookout.AspNetCore.Tests.DumpTests;

public sealed class DumpCaptureIntegrationTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _tempFiles)
            foreach (var f in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(f)) File.Delete(f);
    }

    // ── endpoint dump → entry correlated to request ───────────────────────────

    [Fact]
    public async Task EndpointThatDumps_ProducesDumpEntry_CorrelatedToRequest()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath, endpoints =>
        {
            endpoints.MapGet("/dump", () =>
            {
                Dump(new { Value = 42 }, label: "test-dump");
                return Results.Ok();
            });
        });
        await app.StartAsync();

        await app.GetTestClient().GetAsync("/dump");

        var entries = await PollForEntriesAsync(dbPath, expectedMinimum: 2);
        await app.StopAsync();

        var dumpEntries = entries.Where(e => e.Type == "dump").ToList();
        dumpEntries.Should().ContainSingle("exactly one dump call produces one entry");

        var httpEntry = entries.Single(e => e.Type == "http");
        dumpEntries[0].RequestId.Should().Be(httpEntry.RequestId,
            "dump entry must share the request correlation id");
    }

    [Fact]
    public async Task EndpointThatDumps_DumpEntry_HasLabel()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath, endpoints =>
        {
            endpoints.MapGet("/dump", () =>
            {
                Dump("payload", label: "after-lookup");
                return Results.Ok();
            });
        });
        await app.StartAsync();

        await app.GetTestClient().GetAsync("/dump");

        var entries = await PollForEntriesAsync(dbPath, expectedMinimum: 2);
        await app.StopAsync();

        var content = Deserialize(entries.Single(e => e.Type == "dump"));
        content.Label.Should().Be("after-lookup");
    }

    [Fact]
    public async Task EndpointThatDumps_DumpEntry_HasJsonAndType()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath, endpoints =>
        {
            endpoints.MapGet("/dump", () =>
            {
                Dump(new { OrderId = 7, Status = "pending" });
                return Results.Ok();
            });
        });
        await app.StartAsync();

        await app.GetTestClient().GetAsync("/dump");

        var entries = await PollForEntriesAsync(dbPath, expectedMinimum: 2);
        await app.StopAsync();

        var content = Deserialize(entries.Single(e => e.Type == "dump"));
        content.Json.Should().Contain("7");
        content.ValueType.Should().NotBeNullOrEmpty();
        content.JsonTruncated.Should().BeFalse();
    }

    // ── parent HTTP entry gets dump.count tag ─────────────────────────────────

    [Fact]
    public async Task EndpointThatDumps_ParentHttpEntry_HasDumpCountTag()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath, endpoints =>
        {
            endpoints.MapGet("/dump", () =>
            {
                Dump("first");
                Dump("second");
                return Results.Ok();
            });
        });
        await app.StartAsync();

        await app.GetTestClient().GetAsync("/dump");

        var entries = await PollForEntriesAsync(dbPath, expectedMinimum: 3);
        await app.StopAsync();

        var httpEntry = entries.Single(e => e.Type == "http");
        httpEntry.Tags.Should().ContainKey("dump.count").WhoseValue.Should().Be("2");
    }

    [Fact]
    public async Task EndpointWithNoDump_ParentHttpEntry_HasNoDumpCountTag()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath, endpoints =>
            endpoints.MapGet("/ping", () => Results.Text("pong")));
        await app.StartAsync();

        await app.GetTestClient().GetAsync("/ping");

        var entries = await PollForEntriesAsync(dbPath, expectedMinimum: 1);
        await app.StopAsync();

        var httpEntry = entries.Single(e => e.Type == "http");
        httpEntry.Tags.Should().NotContainKey("dump.count");
    }

    // ── Capture disabled ──────────────────────────────────────────────────────

    [Fact]
    public async Task DumpCapture_Disabled_ProducesNoDumpEntries()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath,
            configure: o => o.Dump.Capture = false,
            endpointMap: endpoints =>
            {
                endpoints.MapGet("/dump", () =>
                {
                    Dump("should not appear");
                    return Results.Ok();
                });
            });
        await app.StartAsync();

        await app.GetTestClient().GetAsync("/dump");

        var entries = await PollForEntriesAsync(dbPath, expectedMinimum: 1);
        await app.StopAsync();

        entries.Should().NotContain(e => e.Type == "dump",
            "dump capture disabled must produce no dump entries");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private string TempDbPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lookout_dump_{Guid.NewGuid():N}.db");
        _tempFiles.Add(path);
        return path;
    }

    private static WebApplication BuildApp(
        string dbPath,
        Action<IEndpointRouteBuilder>? endpointMap = null,
        Action<LookoutOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddLookout(o =>
        {
            o.StoragePath = dbPath;
            configure?.Invoke(o);
        });

        var app = builder.Build();
        app.UseLookout();
        app.MapLookout();
        app.UseExceptionHandler(eh => eh.Run(async ctx =>
        {
            ctx.Response.StatusCode = 500;
            await ctx.Response.WriteAsync("error");
        }));

        if (endpointMap is not null)
            endpointMap(app);
        else
            app.MapGet("/ping", () => Results.Text("pong"));

        return app;
    }

    private static SqliteLookoutStorage OpenReadStorage(string dbPath) =>
        new(new LookoutOptions { StoragePath = dbPath });

    private static async Task<IReadOnlyList<LookoutEntry>> ReadAllAsync(string dbPath)
    {
        using var storage = OpenReadStorage(dbPath);
        return await storage.ReadRecentAsync(200);
    }

    private static async Task<IReadOnlyList<LookoutEntry>> PollForEntriesAsync(
        string dbPath, int expectedMinimum, int timeoutSeconds = 5)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        IReadOnlyList<LookoutEntry> rows = [];
        while (rows.Count < expectedMinimum && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
            rows = await ReadAllAsync(dbPath);
        }
        return rows;
    }

    private static DumpEntryContent Deserialize(LookoutEntry entry) =>
        JsonSerializer.Deserialize<DumpEntryContent>(entry.Content, LookoutJson.Options)!;
}
