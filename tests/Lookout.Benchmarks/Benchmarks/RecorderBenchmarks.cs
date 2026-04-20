using BenchmarkDotNet.Attributes;
using Lookout.AspNetCore;
using Lookout.Core;
using Lookout.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lookout.Benchmarks;

/// <summary>
/// Measures request-path overhead of the Lookout capture pipeline.
/// Target: &lt;5ms p99 for 10 DB + 2 HTTP captures.
/// </summary>
[MemoryDiagnoser]
public class RecorderBenchmarks
{
    private ChannelLookoutRecorder? _standaloneRecorder;

    private string? _dbPath;
    private IHost? _host;
    private ILookoutRecorder? _pipelineRecorder;
    private SqliteLookoutStorage? _readStorage;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _standaloneRecorder = new ChannelLookoutRecorder(
            Options.Create(new LookoutOptions()),
            NullLogger<ChannelLookoutRecorder>.Instance);

        _dbPath = Path.Combine(Path.GetTempPath(), $"lookout_bench_{Guid.NewGuid():N}.db");

        _host = Host.CreateDefaultBuilder()
            .UseEnvironment("Development")
            .ConfigureLogging(b => b.ClearProviders())
            .ConfigureServices(services =>
                services.AddLookout(o => o.StoragePath = _dbPath!))
            .Build();

        await _host.StartAsync().ConfigureAwait(false);

        _pipelineRecorder = _host.Services.GetRequiredService<ILookoutRecorder>();
        _readStorage = new SqliteLookoutStorage(new LookoutOptions { StoragePath = _dbPath });
    }

    /// <summary>
    /// Raw recorder overhead: 10 <c>Record()</c> calls with no storage involved.
    /// Represents the latency impact on the hot request path.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void RecordOnly_10Entries()
    {
        for (var i = 0; i < 10; i++)
            _standaloneRecorder!.Record(MakeEntry(i < 8 ? "ef" : "http"));
    }

    [IterationCleanup(Target = nameof(RecordOnly_10Entries))]
    public void DrainStandaloneChannel()
    {
        while (_standaloneRecorder!.Reader.TryRead(out _)) { }
    }

    /// <summary>
    /// End-to-end wall time: record 10 EF-shaped + 2 HTTP-shaped entries and wait for
    /// all 12 to be flushed to SQLite. Represents full pipeline latency (off request path).
    /// </summary>
    [Benchmark]
    public async Task Pipeline_EndToEnd()
    {
        for (var i = 0; i < 12; i++)
            _pipelineRecorder!.Record(MakeEntry(i < 10 ? "ef" : "http"));

        await WaitForFlushAsync(12).ConfigureAwait(false);
    }

    [IterationCleanup(Target = nameof(Pipeline_EndToEnd))]
    public void PrunePipelineEntries()
    {
        _readStorage!.PruneOlderThanAsync(DateTimeOffset.UtcNow.AddMinutes(1))
            .GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        _standaloneRecorder?.Dispose();
        _readStorage?.Dispose();
        if (_host != null) await _host.StopAsync().ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        if (_dbPath != null)
            foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
                if (File.Exists(f)) File.Delete(f);
    }

    private async Task WaitForFlushAsync(int expected, int timeoutMs = 5_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var rows = await _readStorage!.ReadRecentAsync(expected + 5).ConfigureAwait(false);
            if (rows.Count >= expected) return;
            await Task.Delay(5).ConfigureAwait(false);
        }
    }

    private static LookoutEntry MakeEntry(string type) =>
        new(
            Id: Guid.NewGuid(),
            Type: type,
            Timestamp: DateTimeOffset.UtcNow,
            RequestId: "req-bench-001",
            DurationMs: 1.5,
            Tags: new Dictionary<string, string> { ["source"] = "benchmark" },
            Content: type == "ef"
                ? """{"sql":"SELECT * FROM Products","rows":10}"""
                : """{"method":"GET","path":"/api/items","status":200}""");
}
