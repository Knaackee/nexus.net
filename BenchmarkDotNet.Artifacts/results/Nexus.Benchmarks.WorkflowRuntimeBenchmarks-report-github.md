```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8037)
AMD Ryzen 9 3900X, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.100-rc.2.25502.107
  [Host]     : .NET 10.0.0 (10.0.25.50307), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.0 (10.0.25.50307), X64 RyuJIT AVX2


```
| Method          | Mean      | Error     | StdDev     | Gen0   | Gen1   | Allocated |
|---------------- |----------:|----------:|-----------:|-------:|-------:|----------:|
| CompileWorkflow |  1.788 μs | 0.0746 μs |  0.2199 μs | 0.6294 | 0.0095 |   5.15 KB |
| ExecuteWorkflow | 92.894 μs | 3.4487 μs | 10.0601 μs | 4.6387 | 0.9766 |  39.17 KB |
