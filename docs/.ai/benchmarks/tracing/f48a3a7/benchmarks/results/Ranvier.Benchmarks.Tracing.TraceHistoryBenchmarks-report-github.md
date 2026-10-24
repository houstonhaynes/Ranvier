```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.402
  [Host] : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4 DEBUG
  Traced : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4

Job=Traced  Arguments=/p:RanvierTrace=true  UnrollFactor=16  
Categories=Trace  

```
| Method  | Updates | Mean        | Error     | StdDev    | Op/s    | Gen0     | Gen1     | Gen2     | Allocated |
|-------- |-------- |------------:|----------:|----------:|--------:|---------:|---------:|---------:|----------:|
| **History** | **1000**    |    **11.51 ms** |  **0.131 ms** |  **0.109 ms** | **86.8724** | **156.2500** | **156.2500** | **156.2500** |   **1.57 MB** |
| **History** | **3000**    |   **103.84 ms** |  **2.057 ms** |  **2.020 ms** |  **9.6305** | **500.0000** | **437.5000** | **375.0000** |   **4.67 MB** |
| **History** | **10000**   | **4,168.83 ms** | **42.317 ms** | **39.583 ms** |  **0.2399** | **875.0000** | **812.5000** | **375.0000** |  **16.17 MB** |
