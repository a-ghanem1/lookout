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
using Xunit;

namespace Lookout.AspNetCore.Tests.Exceptions;

public sealed class ExceptionCaptureIntegrationTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _tempFiles)
            foreach (var f in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(f)) File.Delete(f);
    }

    // ── endpoint throw → exception.handled=true ───────────────────────────────

    [Fact]
    public async Task EndpointThatThrows_ProducesExactlyOneExceptionEntry_HandledTrue()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath, endpointMap: endpoints =>
        {
            endpoints.MapGet("/throw", () =>
            {
                throw new InvalidOperationException("boom");
#pragma warning disable CS0162
                return Results.Ok();
#pragma warning restore CS0162
            });
        });
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/throw");
        // Error page served by UseExceptionHandler — any non-success is fine.

        var entries = await PollForEntriesAsync(dbPath, expectedMinimum: 2, timeoutSeconds: 5);
        await app.StopAsync();

        var exceptionEntry = entries.Where(e => e.Type == "exception").ToList();
        exceptionEntry.Should().ContainSingle("exactly one exception entry per throw");

        var content = Deserialize(exceptionEntry.Single());
        content.ExceptionType.Should().Be(typeof(InvalidOperationException).FullName);
        content.Handled.Should().BeTrue();
        content.Message.Should().Be("boom");
        exceptionEntry.Single().Tags["exception.handled"].Should().Be("true");
    }

    [Fact]
    public async Task EndpointThatThrows_ExceptionEntry_CorrelatedToRequest()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath, endpointMap: endpoints =>
        {
            endpoints.MapGet("/throw", () =>
            {
                throw new InvalidOperationException("correlation-test");
#pragma warning disable CS0162
                return Results.Ok();
#pragma warning restore CS0162
            });
        });
        await app.StartAsync();

        await app.GetTestClient().GetAsync("/throw");

        var entries = await PollForEntriesAsync(dbPath, expectedMinimum: 2, timeoutSeconds: 5);
        await app.StopAsync();

        var httpEntry = entries.Single(e => e.Type == "http");
        var exEntry = entries.Single(e => e.Type == "exception");

        exEntry.RequestId.Should().NotBeNullOrEmpty();
        exEntry.RequestId.Should().Be(httpEntry.RequestId,
            "exception entry must share the request correlation id");
    }

    [Fact]
    public async Task EndpointThatThrows_ParentHttpEntry_HasExceptionTags()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath, endpointMap: endpoints =>
        {
            endpoints.MapGet("/throw", () =>
            {
                throw new InvalidOperationException();
#pragma warning disable CS0162
                return Results.Ok();
#pragma warning restore CS0162
            });
        });
        await app.StartAsync();

        await app.GetTestClient().GetAsync("/throw");

        var entries = await PollForEntriesAsync(dbPath, expectedMinimum: 2, timeoutSeconds: 5);
        await app.StopAsync();

        var httpEntry = entries.Single(e => e.Type == "http");
        httpEntry.Tags.Should().ContainKey("exception").WhoseValue.Should().Be("true");
        httpEntry.Tags.Should().ContainKey("exception.count").WhoseValue.Should().Be("1");
    }

    // ── IgnoreExceptionTypes ──────────────────────────────────────────────────

    [Fact]
    public async Task IgnoredExceptionType_ProducesNoExceptionEntry()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(
            dbPath,
            configure: o => o.Exceptions.IgnoreExceptionTypes = ["System.InvalidOperationException"],
            endpointMap: endpoints =>
            {
                endpoints.MapGet("/throw", () =>
                {
                    throw new InvalidOperationException("should be ignored");
#pragma warning disable CS0162
                    return Results.Ok();
#pragma warning restore CS0162
                });
            });
        await app.StartAsync();

        await app.GetTestClient().GetAsync("/throw");

        var entries = await PollForEntriesAsync(dbPath, expectedMinimum: 1, timeoutSeconds: 5);
        await app.StopAsync();

        entries.Should().NotContain(e => e.Type == "exception",
            "ignored exception type must not produce an entry");
    }

    // ── dedup: rethrown exception only produces one entry ─────────────────────

    [Fact]
    public async Task RethrowException_ProducesExactlyOneEntry_NotTwo()
    {
        var dbPath = TempDbPath();
        await using var app = BuildApp(dbPath, endpointMap: endpoints =>
        {
            endpoints.MapGet("/rethrow", () =>
            {
                try { throw new InvalidOperationException("original"); }
                catch { throw; } // rethrow — same exception object
#pragma warning disable CS0162
                return Results.Ok();
#pragma warning restore CS0162
            });
        });
        await app.StartAsync();

        await app.GetTestClient().GetAsync("/rethrow");

        var entries = await PollForEntriesAsync(dbPath, expectedMinimum: 2, timeoutSeconds: 5);
        await app.StopAsync();

        entries.Where(e => e.Type == "exception").Should().ContainSingle(
            "registry dedup must prevent double-recording the same exception object");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private string TempDbPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lookout_exc_{Guid.NewGuid():N}.db");
        _tempFiles.Add(path);
        return path;
    }

    private static WebApplication BuildApp(
        string dbPath,
        Action<LookoutOptions>? configure = null,
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

        var app = builder.Build();

        // UseLookout must be outermost so the N+1/exception scope remains active while
        // UseExceptionHandler (downstream) calls LookoutExceptionHandler.TryHandleAsync.
        app.UseLookout();
        app.MapLookout();

        // Inline error handler avoids a second HTTP entry for the /error re-execute path.
        app.UseExceptionHandler(eh => eh.Run(async ctx =>
        {
            ctx.Response.StatusCode = 500;
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync("An error occurred");
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

    private static ExceptionEntryContent Deserialize(LookoutEntry entry) =>
        JsonSerializer.Deserialize<ExceptionEntryContent>(entry.Content, LookoutJson.Options)!;
}
