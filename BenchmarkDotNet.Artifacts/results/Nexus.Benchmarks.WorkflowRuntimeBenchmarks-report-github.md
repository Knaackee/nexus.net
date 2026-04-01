```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8037)
AMD Ryzen 9 3900X, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.100-rc.2.25502.107
  [Host]     : .NET 10.0.0 (10.0.25.50307), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.0 (10.0.25.50307), X64 RyuJIT AVX2


```
| Method          | Mean      | Error     | StdDev     | Median    | Gen0   | Gen1   | Allocated |
|---------------- |----------:|----------:|-----------:|----------:|-------:|-------:|----------:|
| CompileWorkflow |  1.961 μs | 0.1076 μs |  0.3174 μs |  1.878 μs | 0.6294 | 0.0076 |   5.15 KB |
| ExecuteWorkflow | 82.142 μs | 3.6792 μs | 10.6154 μs | 80.334 μs | 4.6387 | 0.9766 |  39.24 KB |
