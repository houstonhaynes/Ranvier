```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Binding  

```
| Method         | Nodes | Mean         | Error       | StdDev      | Op/s        | Ratio        | RatioSD | Gen0    | Gen1    | Allocated | Alloc Ratio |
|--------------- |------ |-------------:|------------:|------------:|------------:|-------------:|--------:|--------:|--------:|----------:|------------:|
| **SignalEffects**  | **1**     |     **146.8 ns** |     **3.22 ns** |     **9.43 ns** | **6,810,474.5** |     **baseline** |        **** |  **0.0372** |       **-** |     **624 B** |            **** |
| SignalBindings | 1     |     124.7 ns |     2.45 ns |     2.52 ns | 8,019,152.2 | 1.18x faster |   0.08x |  0.0281 |       - |     472 B |  1.32x less |
| MemoEffects    | 1     |     155.1 ns |     3.39 ns |    10.00 ns | 6,445,906.9 | 1.06x slower |   0.10x |  0.0372 |       - |     624 B |  1.00x more |
| MemoBindings   | 1     |     123.8 ns |     2.51 ns |     5.02 ns | 8,075,577.5 | 1.19x faster |   0.09x |  0.0281 |       - |     472 B |  1.32x less |
|                |       |              |             |             |             |              |         |         |         |           |             |
| **SignalEffects**  | **64**    |   **7,736.2 ns** |   **152.72 ns** |   **344.71 ns** |   **129,262.9** |     **baseline** |        **** |  **1.2665** |  **0.0687** |   **21288 B** |            **** |
| SignalBindings | 64    |   5,588.6 ns |   102.30 ns |   208.97 ns |   178,934.8 | 1.39x faster |   0.08x |  0.6866 |  0.0229 |   11560 B |  1.84x less |
| MemoEffects    | 64    |   7,386.9 ns |   145.74 ns |   280.79 ns |   135,374.3 | 1.05x faster |   0.06x |  1.2665 |  0.0687 |   21288 B |  1.00x more |
| MemoBindings   | 64    |   5,506.9 ns |   110.00 ns |   227.16 ns |   181,590.2 | 1.41x faster |   0.08x |  0.6866 |  0.0229 |   11560 B |  1.84x less |
|                |       |              |             |             |             |              |         |         |         |           |             |
| **SignalEffects**  | **1024**  | **120,896.0 ns** | **2,408.23 ns** | **4,089.36 ns** |     **8,271.6** |     **baseline** |        **** | **20.0195** | **10.3760** |  **336168 B** |            **** |
| SignalBindings | 1024  |  83,838.6 ns | 1,662.03 ns | 2,822.26 ns |    11,927.7 | 1.44x faster |   0.07x | 10.7422 |  3.5400 |  180520 B |  1.86x less |
| MemoEffects    | 1024  | 120,891.7 ns | 2,415.94 ns | 5,200.56 ns |     8,271.9 | 1.00x slower |   0.06x | 20.0195 | 10.3760 |  336168 B |  1.00x more |
| MemoBindings   | 1024  |  93,561.4 ns | 2,235.33 ns | 6,590.92 ns |    10,688.2 | 1.30x faster |   0.10x | 10.7422 |  3.5400 |  180520 B |  1.86x less |
