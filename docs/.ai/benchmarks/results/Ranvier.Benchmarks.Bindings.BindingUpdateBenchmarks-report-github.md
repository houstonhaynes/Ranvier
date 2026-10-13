```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Binding  

```
| Method        | Nodes | Kind           | Mean         | Error      | StdDev       | Median       | Op/s         | Allocated |
|-------------- |------ |--------------- |-------------:|-----------:|-------------:|-------------:|-------------:|----------:|
| **WriteAndFlush** | **1**     | **memo-binding**   |     **48.05 ns** |   **0.762 ns** |     **0.713 ns** |     **47.91 ns** | **20,812,758.1** |         **-** |
| **WriteAndFlush** | **1**     | **memo-effect**    |     **56.29 ns** |   **1.603 ns** |     **4.701 ns** |     **57.57 ns** | **17,766,508.5** |         **-** |
| **WriteAndFlush** | **1**     | **signal-binding** |     **21.73 ns** |   **0.383 ns** |     **0.339 ns** |     **21.79 ns** | **46,021,463.3** |         **-** |
| **WriteAndFlush** | **1**     | **signal-effect**  |     **27.45 ns** |   **0.464 ns** |     **0.411 ns** |     **27.54 ns** | **36,427,456.7** |         **-** |
| **WriteAndFlush** | **64**    | **memo-binding**   |  **1,123.43 ns** |  **21.877 ns** |    **29.206 ns** |  **1,126.71 ns** |    **890,133.8** |         **-** |
| **WriteAndFlush** | **64**    | **memo-effect**    |  **1,488.38 ns** |  **39.909 ns** |   **117.674 ns** |  **1,485.11 ns** |    **671,871.1** |         **-** |
| **WriteAndFlush** | **64**    | **signal-binding** |    **913.76 ns** |  **18.175 ns** |    **38.338 ns** |    **913.92 ns** |  **1,094,385.0** |         **-** |
| **WriteAndFlush** | **64**    | **signal-effect**  |  **1,331.63 ns** |  **26.623 ns** |    **49.347 ns** |  **1,331.78 ns** |    **750,961.9** |         **-** |
| **WriteAndFlush** | **1024**  | **memo-binding**   | **17,798.70 ns** | **355.752 ns** |   **668.188 ns** | **17,782.70 ns** |     **56,183.9** |         **-** |
| **WriteAndFlush** | **1024**  | **memo-effect**    | **23,302.72 ns** | **463.603 ns** | **1,352.351 ns** | **23,355.53 ns** |     **42,913.4** |         **-** |
| **WriteAndFlush** | **1024**  | **signal-binding** | **14,720.96 ns** | **286.281 ns** |   **401.326 ns** | **14,717.00 ns** |     **67,930.3** |         **-** |
| **WriteAndFlush** | **1024**  | **signal-effect**  | **21,198.52 ns** | **418.919 ns** |   **652.206 ns** | **21,073.43 ns** |     **47,173.1** |         **-** |
