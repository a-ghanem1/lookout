using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using FluentAssertions;
using Lookout.Core;
using Lookout.Storage.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lookout.AspNetCore.Tests.Storage;

/// <summary>
/// Verifies that WAL mode allows concurrent readers and writers without <see cref="SqliteException"/>.
/// The cross-platform test runs on all CI legs; the Windows-only test specifically exercises
/// the Windows file-locking behaviour that WAL mode resolves (SQLITE_BUSY in rollback-journal mode).
/// </summary>
public sealed class LookoutStorageWalTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _tempFiles)
            foreach (var f in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(f)) File.Delete(f);
    }

    // ── cross-platform WAL concurrent test ───────────────────────────────────

    [Fact]
    public async Task WalMode_ConcurrentReaderAndWriter_NoSqliteBusyException()
    {
        // Two separate storage instances share the same on-disk file.
        // The writer inserts 50 entries; the reader runs 100 ReadRecentAsync queries.
        // WAL mode must allow these to interleave without any SqliteException.
        var dbPath = TempDbPath();
        using var writer = new SqliteLookoutStorage(new LookoutOptions { StoragePath = dbPath });
        using var reader = new SqliteLookoutStorage(new LookoutOptions { StoragePath = dbPath });

        // Prime the schema.
        await writer.WriteAsync([MakeEntry()]);

        var exceptions = new ConcurrentBag<Exception>();

        var writerTask = Task.Run(async () =>
        {
            for (var i = 0; i < 50; i++)
            {
                try { await writer.WriteAsync([MakeEntry()]); }
                catch (Exception ex) { exceptions.Add(ex); }
            }
        });

        var readerTask = Task.Run(async () =>
        {
            for (var i = 0; i < 100; i++)
            {
                try { await reader.ReadRecentAsync(10); }
                catch (Exception ex) { exceptions.Add(ex); }
            }
        });

        await Task.WhenAll(writerTask, readerTask);

        exceptions.Should().BeEmpty(
            "WAL mode must allow concurrent reads and writes without locking errors");
    }

    // ── Windows file-locking test ─────────────────────────────────────────────

    [WindowsOnlyFact]
    public async Task Windows_WalMode_ConcurrentConnections_NoFileLockException()
    {
        // Uses a real ASP.NET Core TestServer so the flusher writes to SQLite while
        // a second raw SqliteConnection reads — the scenario that raised SQLITE_BUSY
        // in rollback-journal mode on Windows.
        var dbPath = TempDbPath();
        await using var app = BuildTestApp(dbPath);
        await app.StartAsync();

        var recorder = app.Services.GetRequiredService<ILookoutRecorder>();

        var exceptions = new ConcurrentBag<Exception>();

        // Writer side: fire 50 requests through the test server.
        // Each request is captured by Lookout and flushed to SQLite by the background flusher.
        var requestTask = Task.Run(async () =>
        {
            var client = app.GetTestClient();
            for (var i = 0; i < 50; i++)
            {
                try
                {
                    recorder.Record(MakeEntry());
                    await client.GetAsync("/lookout");
                }
                catch (Exception ex) { exceptions.Add(ex); }
            }
        });

        // Reader side: open a second connection directly and run SELECT COUNT(*) 100 times.
        var readTask = Task.Run(async () =>
        {
            // Allow the schema to be created by the first write before reading.
            await Task.Delay(50);
            var cs = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
            for (var i = 0; i < 100; i++)
            {
                try
                {
                    await using var conn = new SqliteConnection(cs);
                    await conn.OpenAsync();
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = "PRAGMA journal_mode = WAL; SELECT COUNT(*) FROM entries";
                    await cmd.ExecuteScalarAsync();
                }
                catch (Exception ex) { exceptions.Add(ex); }
            }
        });

        await Task.WhenAll(requestTask, readTask);
        await app.StopAsync();

        exceptions.Should().BeEmpty(
            "WAL mode must resolve Windows SQLITE_BUSY on concurrent file opens");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private string TempDbPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lookout_wal_{Guid.NewGuid():N}.db");
        _tempFiles.Add(path);
        return path;
    }

    private static WebApplication BuildTestApp(string dbPath)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddLookout(o => o.StoragePath = dbPath);
        var app = builder.Build();
        app.UseLookout();
        app.MapLookout();
        return app;
    }

    private static LookoutEntry MakeEntry() => new(
        Id: Guid.NewGuid(),
        Type: "test",
        Timestamp: DateTimeOffset.UtcNow,
        RequestId: null,
        DurationMs: null,
        Tags: new Dictionary<string, string>(),
        Content: "{}");
}

/// <summary>Skips on non-Windows platforms; runs normally on Windows.</summary>
[AttributeUsage(AttributeTargets.Method)]
file sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Skip = "Windows only — validates WAL mode resolves SQLITE_BUSY on concurrent file opens.";
    }
}
