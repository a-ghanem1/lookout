using BenchmarkDotNet.Attributes;
using Lookout.Core;
using Lookout.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Lookout.Benchmarks;

/// <summary>
/// Compares writing 50 entries as a single batched transaction vs 50 individual transactions.
/// Target: batched is ≥5× faster per entry — single-row inserts pay BEGIN/COMMIT per row.
/// </summary>
[MemoryDiagnoser]
public class StorageFlushBenchmarks
{
    private SqliteLookoutStorage? _storage;
    private IReadOnlyList<LookoutEntry>? _batch;
    private string? _dbPath;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lookout_storage_bench_{Guid.NewGuid():N}.db");
        _storage = new SqliteLookoutStorage(new LookoutOptions { StoragePath = _dbPath });

        _batch = Enumerable.Range(0, 50).Select(i => new LookoutEntry(
            Id: Guid.NewGuid(),
            Type: "bench",
            Timestamp: DateTimeOffset.UtcNow,
            RequestId: $"req-{i}",
            DurationMs: 1.0,
            Tags: new Dictionary<string, string>(),
            Content: """{"sql":"SELECT 1"}""")).ToList();

        // Warm up the SQLite file (schema creation on first write).
        await _storage.WriteAsync([_batch[0]]).ConfigureAwait(false);
        await _storage.PruneOlderThanAsync(DateTimeOffset.UtcNow.AddMinutes(1)).ConfigureAwait(false);
    }

    /// <summary>
    /// All 50 entries in one <c>BEGIN/COMMIT</c> transaction — the current behaviour.
    /// </summary>
    [Benchmark(Baseline = true)]
    public async Task BatchedFlush_50Entries()
    {
        await _storage!.WriteAsync(_batch!).ConfigureAwait(false);
    }

    [IterationCleanup(Target = nameof(BatchedFlush_50Entries))]
    public void CleanupBatched()
    {
        _storage!.PruneOlderThanAsync(DateTimeOffset.UtcNow.AddMinutes(1)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// The same 50 entries written one at a time — each gets its own <c>BEGIN/COMMIT</c>.
    /// Demonstrates the overhead that batching eliminates.
    /// </summary>
    [Benchmark]
    public async Task SingleFlush_50Entries()
    {
        foreach (var entry in _batch!)
            await _storage!.WriteAsync([entry]).ConfigureAwait(false);
    }

    [IterationCleanup(Target = nameof(SingleFlush_50Entries))]
    public void CleanupSingle()
    {
        _storage!.PruneOlderThanAsync(DateTimeOffset.UtcNow.AddMinutes(1)).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _storage?.Dispose();
        SqliteConnection.ClearAllPools();
        if (_dbPath != null)
            foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
                if (File.Exists(f)) File.Delete(f);
    }
}
