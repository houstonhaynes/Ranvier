```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.402
  [Host]   : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4 DEBUG
  Traced   : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4
  Untraced : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4

InvocationCount=4096  UnrollFactor=16  Categories=Trace  

```
| Method | Job      | Arguments             | Mean         | Error        | StdDev       | Median       | Op/s         | Ratio         | RatioSD | Gen0   | Gen1   | Gen2   | Allocated | Alloc Ratio |
|------- |--------- |---------------------- |-------------:|-------------:|-------------:|-------------:|-------------:|--------------:|--------:|-------:|-------:|-------:|----------:|------------:|
| Signal | Traced   | /p:RanvierTrace=true  |  1,634.78 ns |    31.661 ns |    35.191 ns |  1,628.74 ns |    611,701.8 | 89.04x slower |   1.96x |      - |      - |      - |    1672 B |          NA |
| Signal | Untraced | /p:RanvierTrace=false |     18.36 ns |     0.154 ns |     0.129 ns |     18.33 ns | 54,462,514.1 |      baseline |         |      - |      - |      - |         - |          NA |
|        |          |                       |              |              |              |              |              |               |         |        |        |        |           |             |
| Memo   | Traced   | /p:RanvierTrace=true  |  1,332.72 ns |   324.852 ns |   952.735 ns |    770.83 ns |    750,344.6 | 37.31x slower |  26.55x |      - |      - |      - |    3472 B |          NA |
| Memo   | Untraced | /p:RanvierTrace=false |     35.73 ns |     0.320 ns |     0.284 ns |     35.73 ns | 27,990,433.0 |      baseline |         |      - |      - |      - |         - |          NA |
|        |          |                       |              |              |              |              |              |               |         |        |        |        |           |             |
| Chain  | Traced   | /p:RanvierTrace=true  |  6,151.13 ns |   111.623 ns |   104.412 ns |  6,138.09 ns |    162,571.7 | 70.31x slower |   1.34x | 0.4883 | 0.2441 | 0.2441 |   10791 B |          NA |
| Chain  | Untraced | /p:RanvierTrace=false |     87.50 ns |     1.109 ns |     0.866 ns |     87.71 ns | 11,428,571.4 |      baseline |         |      - |      - |      - |         - |          NA |
|        |          |                       |              |              |              |              |              |               |         |        |        |        |           |             |
| Fanout | Traced   | /p:RanvierTrace=true  | 47,614.40 ns | 1,709.493 ns | 5,040.479 ns | 45,799.01 ns |     21,002.1 | 57.20x slower |   6.27x | 4.8828 | 1.7090 | 0.9766 |  107646 B |          NA |
| Fanout | Untraced | /p:RanvierTrace=false |    833.21 ns |    16.275 ns |    25.814 ns |    826.93 ns |  1,200,184.3 |      baseline |         |      - |      - |      - |         - |          NA |
|        |          |                       |              |              |              |              |              |               |         |        |        |        |           |             |
| Cutoff | Traced   | /p:RanvierTrace=true  |    359.55 ns |     8.352 ns |    23.421 ns |    357.03 ns |  2,781,273.6 | 13.58x slower |   0.89x |      - |      - |      - |    2576 B |          NA |
| Cutoff | Untraced | /p:RanvierTrace=false |     26.48 ns |     0.361 ns |     0.320 ns |     26.38 ns | 37,768,556.9 |      baseline |         |      - |      - |      - |         - |          NA |
|        |          |                       |              |              |              |              |              |               |         |        |        |        |           |             |
| Batch  | Traced   | /p:RanvierTrace=true  |  2,911.54 ns |    64.543 ns |   188.274 ns |  2,926.54 ns |    343,460.4 | 67.74x slower |   4.37x | 0.4883 | 0.2441 | 0.2441 |    6464 B |          NA |
| Batch  | Untraced | /p:RanvierTrace=false |     42.98 ns |     0.222 ns |     0.185 ns |     42.99 ns | 23,266,625.9 |      baseline |         |      - |      - |      - |         - |          NA |
