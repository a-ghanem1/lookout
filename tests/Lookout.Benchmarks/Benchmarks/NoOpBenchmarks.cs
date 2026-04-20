using BenchmarkDotNet.Attributes;

namespace Lookout.Benchmarks;

/// <summary>
/// Baseline no-op benchmark. Proves the harness builds and runs.
/// Real recorder benchmarks (RecordOnly_10Entries, Pipeline_EndToEnd) added in M2.2+.
/// </summary>
[MemoryDiagnoser]
public class NoOpBenchmarks
{
    [Benchmark(Baseline = true)]
    public void NoOp_Record()
    {
        // Intentionally empty baseline. Replaced with recorder call in M2.2.
    }
}
