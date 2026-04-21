```

BenchmarkDotNet v0.14.0, macOS 26.3 (25D125) [Darwin 25.3.0]
Apple M1, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.201
  [Host]   : .NET 8.0.0 (8.0.23.53103), Arm64 RyuJIT AdvSIMD
  ShortRun : .NET 8.0.0 (8.0.23.53103), Arm64 RyuJIT AdvSIMD

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                                      | Mean      | Error     | StdDev   | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|-------------------------------------------- |----------:|----------:|---------:|------:|--------:|--------:|-------:|----------:|------------:|
| &#39;No interceptor&#39;                            |  51.93 μs |  52.46 μs | 2.876 μs |  1.00 |    0.07 |  7.3242 | 0.4883 |  46.01 KB |        1.00 |
| &#39;Interceptor, stack off (MaxStackFrames=0)&#39; |  58.33 μs |  12.28 μs | 0.673 μs |  1.13 |    0.05 |  8.7891 | 0.4883 |  54.26 KB |        1.18 |
| &#39;Interceptor, stack on (MaxStackFrames=20)&#39; | 121.43 μs | 131.83 μs | 7.226 μs |  2.34 |    0.16 | 16.1133 | 0.9766 |  98.81 KB |        2.15 |
