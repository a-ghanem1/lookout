```

BenchmarkDotNet v0.14.0, macOS 26.3 (25D125) [Darwin 25.3.0]
Apple M1, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.201
  [Host]     : .NET 8.0.0 (8.0.23.53103), Arm64 RyuJIT AdvSIMD
  Job-OFCOOQ : .NET 8.0.0 (8.0.23.53103), Arm64 RyuJIT AdvSIMD

InvocationCount=1  UnrollFactor=1  

```
| Method               | Mean         | Error       | StdDev     | Median       | Ratio  | RatioSD | Allocated | Alloc Ratio |
|--------------------- |-------------:|------------:|-----------:|-------------:|-------:|--------:|----------:|------------:|
| RecordOnly_10Entries |     7.665 μs |   0.7492 μs |   2.101 μs |     7.083 μs |   1.06 |    0.38 |   3.77 KB |        1.00 |
| Pipeline_EndToEnd    | 6,381.594 μs | 126.3117 μs | 366.453 μs | 6,482.583 μs | 885.37 |  206.95 |   67.9 KB |       18.03 |
