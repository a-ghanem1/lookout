# Lookout Benchmark Results

All benchmarks run with `dotnet run -c Release --project tests/Lookout.Benchmarks` on the target machine.
Results are from the BenchmarkDotNet summary table printed at the end of each run.

---

## How to run

```bash
# Full run (all benchmark classes, takes ~10 min)
dotnet run -c Release --project tests/Lookout.Benchmarks

# Single class
dotnet run -c Release --project tests/Lookout.Benchmarks -- --filter "*EfCapture*"
dotnet run -c Release --project tests/Lookout.Benchmarks -- --filter "*StorageFlush*"
```

---

## EfCaptureBenchmarks — Week 11 (M11.1)

**Budget:** interceptor with stack capture ≤70 µs per query; no-stack ≤10 µs overhead.

| Method | Mean | Error | StdDev | Ratio | Alloc |
|---|---|---|---|---|---|
| EfQuery_NoInterceptor | — | — | — | 1.00 (baseline) | — |
| EfQuery_Interceptor_NoStack | — | — | — | — | — |
| EfQuery_Interceptor_WithStack | — | — | — | — | — |

> Run `dotnet run -c Release --project tests/Lookout.Benchmarks -- --filter "*EfCapture*"` and paste the table here.

---

## StorageFlushBenchmarks — Week 11 (M11.1)

**Budget:** `BatchedFlush_50Entries` must be ≥5× faster per-entry than `SingleFlush_50Entries`.

| Method | Mean | Error | StdDev | Ratio | Alloc |
|---|---|---|---|---|---|
| BatchedFlush_50Entries | — | — | — | 1.00 (baseline) | — |
| SingleFlush_50Entries | — | — | — | ≥5× slower expected | — |

> Run `dotnet run -c Release --project tests/Lookout.Benchmarks -- --filter "*StorageFlush*"` and paste the table here.

---

## k6 Load Test — Week 11 (M11.1)

**Budget:** Lookout-enabled p99 ≤ baseline p99 × 1.05 (≤5% regression at 1,000 VUs, 60 s).

```bash
# 1. Start sample app WITHOUT Lookout:
dotnet run --project samples/WebApp --urls http://localhost:5080
k6 run tests/load/k6-baseline.js --env LOOKOUT=off

# 2. Start sample app WITH Lookout:
k6 run tests/load/k6-baseline.js --env LOOKOUT=on
```

| Scenario | p50 (ms) | p95 (ms) | p99 (ms) |
|---|---|---|---|
| Baseline (Lookout off) | — | — | — |
| Lookout enabled | — | — | — |
| Overhead (p99) | | | — % |

---

## One-Hour Soak Test — Week 11 (M11.5)

**Budget:** No `LookoutChannelDropped` warnings at 50 VUs. SQLite file size stable. GC heap stable.

```bash
# Run the soak script (60 min, 50 VUs):
dotnet run --project samples/WebApp --urls http://localhost:5080

# In a second terminal:
k6 run tests/load/k6-soak.js

# In a third terminal (monitor GC/heap):
dotnet-counters monitor --process-id $(pgrep -f samples/WebApp) System.Runtime
```

| Time | SQLite size | GC heap (MB) | /api/entries p50 (ms) |
|---|---|---|---|
| t=0 | — | — | — |
| t=30m | — | — | — |
| t=60m | — | — | — |

Pass criteria:
- [ ] No `LookoutChannelDropped` in application logs
- [ ] SQLite file size ≤ configured cap (default: 50k entries × ~1 KB/entry ≈ 50 MB)
- [ ] GC heap shows no upward trend from t=30m to t=60m
- [ ] `GET /lookout/api/entries` p50 stays under 20 ms throughout
