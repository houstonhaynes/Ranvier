```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.402
  [Host]   : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4 DEBUG
  Traced   : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4
  Untraced : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4

UnrollFactor=16  Categories=Trace  

```
| Method      | Job      | Arguments             | Mean           | Error         | StdDev        | Median         | Op/s        | Ratio          | RatioSD | Gen0      | Gen1     | Gen2     | Allocated   | Alloc Ratio |
|------------ |--------- |---------------------- |---------------:|--------------:|--------------:|---------------:|------------:|---------------:|--------:|----------:|---------:|---------:|------------:|------------:|
| Signal      | Traced   | /p:RanvierTrace=true  |    18,318.2 ns |     583.08 ns |   1,719.23 ns |    17,937.1 ns |    54,590.5 | 126.45x slower |  14.09x |    2.5024 |   0.1221 |        - |     41.6 KB | 23.36x more |
| Signal      | Untraced | /p:RanvierTrace=false |       145.4 ns |       3.22 ns |       9.14 ns |       144.1 ns | 6,876,660.7 |       baseline |         |    0.1090 |   0.0005 |        - |     1.78 KB |             |
|             |          |                       |                |               |               |                |             |                |         |           |          |          |             |             |
| Memo        | Traced   | /p:RanvierTrace=true  |    27,086.4 ns |     977.45 ns |   2,835.76 ns |    26,220.3 ns |    36,918.9 | 117.33x slower |  17.36x |    3.7842 |   0.1221 |        - |    62.58 KB | 28.61x more |
| Memo        | Untraced | /p:RanvierTrace=false |       233.5 ns |       8.62 ns |      25.27 ns |       229.4 ns | 4,283,401.0 |       baseline |         |    0.1338 |   0.0007 |        - |     2.19 KB |             |
|             |          |                       |                |               |               |                |             |                |         |           |          |          |             |             |
| Chain       | Traced   | /p:RanvierTrace=true  |    54,663.4 ns |   1,846.22 ns |   5,443.61 ns |    52,575.3 ns |    18,293.8 | 115.94x slower |  17.85x |    7.5684 |   0.4883 |        - |    124.9 KB | 34.53x more |
| Chain       | Untraced | /p:RanvierTrace=false |       478.1 ns |      19.46 ns |      57.37 ns |       470.2 ns | 2,091,488.8 |       baseline |         |    0.2213 |   0.0019 |        - |     3.62 KB |             |
|             |          |                       |                |               |               |                |             |                |         |           |          |          |             |             |
| Fanout      | Traced   | /p:RanvierTrace=true  |   589,193.5 ns |  17,448.35 ns |  51,446.85 ns |   579,045.0 ns |     1,697.2 | 121.09x slower |  12.87x |   80.0781 |  19.5313 |        - |  1335.93 KB | 38.82x more |
| Fanout      | Untraced | /p:RanvierTrace=false |     4,884.2 ns |     106.55 ns |     307.43 ns |     4,856.4 ns |   204,743.3 |       baseline |         |    2.1057 |   0.2136 |        - |    34.41 KB |             |
|             |          |                       |                |               |               |                |             |                |         |           |          |          |             |             |
| DisposeRows | Traced   | /p:RanvierTrace=true  | 9,385,594.8 ns | 184,368.42 ns | 498,450.77 ns | 9,312,845.3 ns |       106.5 | 189.24x slower |  10.35x | 1281.2500 | 656.2500 | 312.5000 | 19786.51 KB | 53.50x more |
| DisposeRows | Untraced | /p:RanvierTrace=false |    49,605.8 ns |     779.55 ns |     729.19 ns |    49,593.9 ns |    20,158.9 |       baseline |         |   22.5830 |  13.1836 |        - |   369.84 KB |             |
