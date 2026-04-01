```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8037)
AMD Ryzen 9 3900X, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.100-rc.2.25502.107
  [Host]     : .NET 10.0.0 (10.0.25.50307), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.0 (10.0.25.50307), X64 RyuJIT AVX2


```
| Method               | Mean     | Error     | StdDev    | Gen0   | Gen1   | Allocated |
|--------------------- |---------:|----------:|----------:|-------:|-------:|----------:|
| RunParallelSubAgents | 3.689 μs | 0.0675 μs | 0.0632 μs | 0.9995 | 0.0153 |    8.2 KB |
