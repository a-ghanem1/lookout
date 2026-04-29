using System.Text.Json;
using FluentAssertions;
using Lookout.Core;
using Lookout.Core.Schemas;
using Lookout.Storage.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lookout.AspNetCore.Tests.Logging;

public sealed class LogCaptureIntegrationTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _tempFiles)
            foreach (var f in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(f)) File.Delete(f);
    }

    // ── endpoint logs → entries correlated to request ─────────────────────────

    [Fact]
    public async Task EndpointThatLogs_ProducesLogEntries_CorrelatedToRequest()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath, endpoints =>
        {
            endpoints.MapGet("/log", (ILogger<LogCaptureIntegrationTests> logger) =>
            {
                logger.LogInformation("hello from endpoint");
                return Results.Ok();
            });
        });
        await app.StartAsync();

        await app.GetTestClient().GetAsync("/log");

        var entries = await PollForEntriesAsync(dbPath, expectedMinimum: 2);
        await app.StopAsync();

        var logEntries = entries.Where(e => e.Type == "log").ToList();
        logEntries.Should().NotBeEmpty("at least one log entry should be captured");

        var httpEntry = entries.Single(e => e.Type == "http");
        logEntries[0].RequestId.Should().Be(httpEntry.RequestId,
            "log entry must share the request correlation id");
    }

    [Fact]
    public async Task EndpointThatLogs_LogEntries_HaveCorrectLevels()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath, endpoints =>
        {
            endpoints.MapGet("/levels", (ILogger<LogCaptureIntegrationTests> logger) =>
            {
                logger.LogInformation("info message");
                logger.LogWarning("warn message");
                logger.LogError("error message");
                return Results.Ok();
            });
        });
        await app.StartAsync();

        await app.GetTestClient().GetAsync("/levels");

        var entries = await PollForEntriesAsync(dbPath, expectedMinimum: 4);
        await app.StopAsync();

        var logEntries = entries.Where(e => e.Type == "log").ToList();
        logEntries.Should().HaveCount(3, "three log calls must produce three entries");

        var levels = logEntries.Select(e => Deserialize(e).Level).ToHashSet();
        levels.Should().Contain("Information").And.Contain("Warning").And.Contain("Error");
    }

    [Fact]
    public async Task EndpointThatLogs_ParentHttpEntry_HasLogCountTag()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath, endpoints =>
        {
            endpoints.MapGet("/count", (ILogger<LogCaptureIntegrationTests> logger) =>
            {
                logger.LogInformation("one");
                logger.LogInformation("two");
                return Results.Ok();
            });
        });
        await app.StartAsync();

        await app.GetTestClient().GetAsync("/count");

        var entries = await PollForEntriesAsync(dbPath, expectedMinimum: 3);
        await app.StopAsync();

        var httpEntry = entries.Single(e => e.Type == "http");
        httpEntry.Tags.Should().ContainKey("log.count").WhoseValue.Should().Be("2");
    }

    [Fact]
    public async Task EndpointThatLogs_ParentHttpEntry_HasMaxLogLevelTag_WhenWarningOrAbove()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath, endpoints =>
        {
            endpoints.MapGet("/maxlevel", (ILogger<LogCaptureIntegrationTests> logger) =>
            {
                logger.LogInformation("info");
                logger.LogWarning("warn");
                return Results.Ok();
            });
        });
        await app.StartAsync();

        await app.GetTestClient().GetAsync("/maxlevel");

        var entries = await PollForEntriesAsync(dbPath, expectedMinimum: 3);
        await app.StopAsync();

        var httpEntry = entries.Single(e => e.Type == "http");
        httpEntry.Tags.Should().ContainKey("log.maxLevel").WhoseValue.Should().Be("Warning");
    }

    [Fact]
    public async Task EndpointThatLogs_ParentHttpEntry_NoMaxLogLevelTag_WhenOnlyInformation()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath, endpoints =>
        {
            endpoints.MapGet("/infoonly", (ILogger<LogCaptureIntegrationTests> logger) =>
            {
                logger.LogInformation("just info");
                return Results.Ok();
            });
        });
        await app.StartAsync();

        await app.GetTestClient().GetAsync("/infoonly");

        var entries = await PollForEntriesAsync(dbPath, expectedMinimum: 2);
        await app.StopAsync();

        var httpEntry = entries.Single(e => e.Type == "http");
        httpEntry.Tags.Should().NotContainKey("log.maxLevel",
            "maxLevel is only written when max level > Information");
    }

    // ── IgnoreCategories ──────────────────────────────────────────────────────

    [Fact]
    public async Task MicrosoftFrameworkLogs_DoNotAppear_WithDefaultIgnoreCategories()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath, endpoints =>
            endpoints.MapGet("/ping", () => Results.Text("pong")));
        await app.StartAsync();

        await app.GetTestClient().GetAsync("/ping");

        var entries = await PollForEntriesAsync(dbPath, expectedMinimum: 1);
        await app.StopAsync();

        entries.Where(e => e.Type == "log").Should().NotContain(
            e => Deserialize(e).Category.StartsWith("Microsoft.",
                StringComparison.OrdinalIgnoreCase)
              || Deserialize(e).Category.StartsWith("System.",
                StringComparison.OrdinalIgnoreCase),
            "Microsoft.* and System.* categories are blocked by default IgnoreCategories");
    }

    // ── Capture disabled ──────────────────────────────────────────────────────

    [Fact]
    public async Task LogCapture_Disabled_ProducesNoLogEntries()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath,
            configure: o => o.Logging.Capture = false,
            endpointMap: endpoints =>
            {
                endpoints.MapGet("/log", (ILogger<LogCaptureIntegrationTests> logger) =>
                {
                    logger.LogInformation("should not be captured");
                    return Results.Ok();
                });
            });
        await app.StartAsync();

        await app.GetTestClient().GetAsync("/log");

        var entries = await PollForEntriesAsync(dbPath, expectedMinimum: 1);
        await app.StopAsync();

        entries.Should().NotContain(e => e.Type == "log",
            "log capture disabled must produce no log entries");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private string TempDbPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lookout_log_{Guid.NewGuid():N}.db");
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

    private static LogEntryContent Deserialize(LookoutEntry entry) =>
        JsonSerializer.Deserialize<LogEntryContent>(entry.Content, LookoutJson.Options)!;
}
