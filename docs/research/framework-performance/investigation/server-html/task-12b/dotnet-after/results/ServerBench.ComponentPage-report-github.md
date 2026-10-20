```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4 DEBUG
  DefaultJob : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4


```
| Method | Rows | Mean        | Error     | StdDev    | Gen0    | Gen1   | Allocated |
|------- |----- |------------:|----------:|----------:|--------:|-------:|----------:|
| **Render** | **1**    |    **994.0 ns** |  **12.07 ns** |  **11.29 ns** |  **0.3891** | **0.0019** |   **6.36 KB** |
| **Render** | **16**   |  **5,489.6 ns** |  **54.40 ns** |  **50.88 ns** |  **2.0370** | **0.0839** |  **33.38 KB** |
| **Render** | **128**  | **38,731.0 ns** | **621.79 ns** | **581.62 ns** | **14.5874** | **2.8687** | **238.91 KB** |
