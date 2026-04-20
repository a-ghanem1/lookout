using System.Net;
using FluentAssertions;
using Lookout.Core;
using Lookout.Storage.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lookout.AspNetCore.Tests.Flusher;

public sealed class LookoutFlusherTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _tempFiles)
        {
            foreach (var f in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(f)) File.Delete(f);
        }
    }

    [Fact]
    public async Task Record100Entries_ResultsIn100RowsInSqlite()
    {
        var dbPath = TempDbPath();
        await using var app = BuildTestApp(dbPath);
        await app.StartAsync();

        var recorder = app.Services.GetRequiredService<ILookoutRecorder>();
        for (var i = 0; i < 100; i++)
            recorder.Record(MakeEntry());

        var rowCount = await PollForRowCountAsync(dbPath, expected: 100, timeoutSeconds: 5);

        await app.StopAsync();

        rowCount.Should().Be(100);
    }

    [Fact]
    public async Task ShutdownDrains_PendingEntries()
    {
        var dbPath = TempDbPath();
        await using var app = BuildTestApp(dbPath);
        await app.StartAsync();

        var recorder = app.Services.GetRequiredService<ILookoutRecorder>();
        for (var i = 0; i < 10; i++)
            recorder.Record(MakeEntry());

        await app.StopAsync();

        using var storage = OpenReadStorage(dbPath);
        var rows = await storage.ReadRecentAsync(20);
        rows.Should().HaveCount(10);
    }

    [Fact]
    public async Task StorageException_DoesNotCrashHost_SubsequentWritesStillFlow()
    {
        var dbPath = TempDbPath();
        var inner = new SqliteLookoutStorage(new LookoutOptions { StoragePath = dbPath });
        try
        {
            var faulting = new FaultOnceStorage(inner);

            await using var app = BuildTestApp(dbPath, services =>
                services.AddSingleton<ILookoutStorage>(faulting));
            await app.StartAsync();

            var recorder = app.Services.GetRequiredService<ILookoutRecorder>();

            // First entry — flusher will attempt a write and catch the thrown exception.
            recorder.Record(MakeEntry());
            await Task.Delay(300); // Allow the faulting batch to be processed.

            // Second entry — storage no longer faults; this batch should be persisted.
            recorder.Record(MakeEntry());

            var rowCount = await PollForRowCountAsync(dbPath, expected: 1, timeoutSeconds: 5);

            // Host must still be reachable after the storage exception.
            var response = await app.GetTestClient().GetAsync("/lookout");
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            await app.StopAsync();

            rowCount.Should().BeGreaterOrEqualTo(1, "at least the post-fault entry must be persisted");
        }
        finally
        {
            inner.Dispose();
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private string TempDbPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lookout_flusher_{Guid.NewGuid():N}.db");
        _tempFiles.Add(path);
        return path;
    }

    private static WebApplication BuildTestApp(
        string dbPath,
        Action<IServiceCollection>? extraServices = null)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddLookout(o => o.StoragePath = dbPath);
        extraServices?.Invoke(builder.Services);
        var app = builder.Build();
        app.UseLookout();
        app.MapLookout();
        return app;
    }

    private static SqliteLookoutStorage OpenReadStorage(string dbPath) =>
        new(new LookoutOptions { StoragePath = dbPath });

    private static async Task<int> PollForRowCountAsync(
        string dbPath, int expected, int timeoutSeconds)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        int count = 0;
        while (count < expected && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
            using var storage = OpenReadStorage(dbPath);
            count = (await storage.ReadRecentAsync(expected + 10)).Count;
        }
        return count;
    }

    private static LookoutEntry MakeEntry() => new(
        Id: Guid.NewGuid(),
        Type: "test",
        Timestamp: DateTimeOffset.UtcNow,
        RequestId: null,
        DurationMs: null,
        Tags: new Dictionary<string, string>(),
        Content: "{}");

    /// <summary>Throws on the first WriteAsync call; delegates all subsequent calls to inner.</summary>
    private sealed class FaultOnceStorage(ILookoutStorage inner) : ILookoutStorage
    {
        private int _calls;

        public Task WriteAsync(IReadOnlyList<LookoutEntry> entries, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                throw new InvalidOperationException("Simulated storage fault.");
            return inner.WriteAsync(entries, ct);
        }
    }
}
