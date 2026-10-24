# Traced and untraced baseline

| Version | Value |
| --- | --- |
| BenchmarkJob | default |
| CPU | AMD64 Family 26 Model 68 Stepping 0, AuthenticAMD |
| Dotnet | 10.0.13 |
| Fable | fickl 0.5.3 |
| FableLibrary | 2.9.0 |
| LogicalProcessors | 24 |
| Node | v26.7.0 |
| OS | Microsoft Windows 10.0.26200 |
| Source | f48a3a7fdf37c6e936ab6237d971642fd012a8ce |

## Allocations per operation

Bytes per operation from the counter bench, and the traced build's ratio to the untraced build. A row that is
not GC-free ran a collection inside a measured region, and its figures understate the allocated bytes.

| Runtime | Scenario | Engine | Untraced B/op | Traced B/op | Traced - untraced | Ratio | GC-free |
| --- | --- | --- | ---: | ---: | ---: | ---: | --- |
| dotnet | timed-capture-debounce | Ranvier | 0 | 124719.1 | 124719.1 | n/a | yes |
| dotnet | timed-admit-debounce | Ranvier | 1536 | 363188.2 | 361652.2 | n/a | no |
| dotnet | timed-capture-first | Ranvier | 0 | 125231.1 | 125231.1 | n/a | yes |
| dotnet | timed-admit-first | Ranvier | 0 | 233734.1 | 233734.1 | n/a | no |
| dotnet | timed-capture-last | Ranvier | 0 | 124719.1 | 124719.1 | n/a | yes |
| dotnet | timed-admit-last | Ranvier | 1536 | 363188.2 | 361652.2 | n/a | no |
| dotnet | timed-capture-both | Ranvier | 0 | 124719.1 | 124719.1 | n/a | yes |
| dotnet | timed-admit-both | Ranvier | 1536 | 363188.2 | 361652.2 | n/a | no |
| dotnet | create | Ranvier | 577376 | 37774055 | 37196679 | n/a | no |
| dotnet | update | Ranvier | 0 | 208058.1 | 208058.1 | n/a | yes |
| dotnet | chain | Ranvier | 0 | 6569.2 | 6569.2 | n/a | yes |
| dotnet | cutoff | Ranvier | 0 | 208.9 | 208.9 | n/a | yes |
| dotnet | dispose | Ranvier | 56 | 312112 | 312056 | 5573.43 | yes |
| dotnet | create-on | Ranvier | 489520 | 36757938 | 36268418 | n/a | no |
| dotnet | update-on | Ranvier | 0 | 211258.1 | 211258.1 | n/a | yes |
| dotnet | dispose-on | Ranvier | 56 | 312112 | 312056 | 5573.43 | yes |
| dotnet | derive-effect | Ranvier | 0 | 1870.9 | 1870.9 | n/a | yes |
| dotnet | derive-on | Ranvier | 0 | 1902.9 | 1902.9 | n/a | yes |
| dotnet | work-status | Ranvier (createEffect) | 3980 | 212037.4 | 208057.4 | 53.28 | yes |
| dotnet | work-status | Ranvier (createEffectOn) | 398.4 | 211656.5 | 211258.1 | 531.27 | yes |
| dotnet | work-value | Ranvier (createEffect) | 6467.2 | 214525.3 | 208058.1 | 33.17 | yes |
| dotnet | work-value | Ranvier (createEffectOn) | 6467.2 | 217725.3 | 211258.1 | 33.67 | yes |
| dotnet | work-form | Ranvier (createEffect) | 0 | 251617 | 251617 | n/a | yes |
| dotnet | work-form | Ranvier (createEffectOn) | 0 | 238755.7 | 238755.7 | n/a | yes |
| dotnet | project-edit | Ranvier | 112 | 3312 | 3200 | 29.57 | yes |
| dotnet | project-reorder | Ranvier | 4136 | 117696 | 113560 | 28.46 | yes |
| dotnet | keyed-remove | Ranvier (fresh items) | 4428 | 2062104.5 | 2057676.5 | 465.70 | yes |
| dotnet | keyed-remove | Ranvier (stable items) | 4428 | 40427.2 | 35999.2 | 9.13 | yes |
| dotnet | keyed-remove | Ranvier (memo-free rows) | 4500 | 65080 | 60580 | 14.46 | yes |
| dotnet | keyed-swap | Ranvier (fresh items) | 4136 | 2029933.3 | 2025797.3 | 490.80 | yes |
| dotnet | keyed-swap | Ranvier (stable items) | 4136 | 7296 | 3160 | 1.76 | yes |
| dotnet | keyed-swap | Ranvier (memo-free rows) | 4160 | 6480 | 2320 | 1.56 | yes |
| dotnet | keyed-insert | Ranvier (fresh items) | 4432 | 2062604.5 | 2058172.5 | 465.39 | yes |
| dotnet | keyed-insert | Ranvier (stable items) | 4432 | 39743.2 | 35311.2 | 8.97 | yes |
| dotnet | keyed-insert | Ranvier (memo-free rows) | 4504 | 65084 | 60580 | 14.45 | yes |
| dotnet | server-render | Ranvier | 200440 | 3344592 | 3144152 | 16.69 | yes |
| dotnet | project-chain | Ranvier | 2528 | 84112 | 81584 | 33.27 | yes |
| dotnet | app-table | Ranvier | 78476 | 11401084.6 | 11322608.6 | n/a | no |
| dotnet | app-detail | Ranvier | 12952 | 873803.8 | 860851.8 | 67.46 | yes |
| dotnet | shape-diamond | Ranvier | 0 | 169460.9 | 169460.9 | n/a | no |
| dotnet | shape-dynamic | Ranvier | 0 | 182858.1 | 182858.1 | n/a | yes |
| dotnet | async-resolve | Ranvier | 68608 | 2268441.6 | 2199833.6 | 33.06 | yes |
| dotnet | async-recover | Ranvier | 180374.3 | 245427.4 | 65053.2 | 1.36 | yes |
| dotnet | stream-burst-1 | Ranvier (stream, batched) | 120 | 5173.5 | 5053.5 | 43.11 | yes |
| dotnet | stream-burst-1 | Ranvier (AsyncSource, batched) | 160 | 3750.8 | 3590.8 | 23.44 | yes |
| dotnet | stream-burst-1 | Ranvier (AsyncSource, unbatched) | 32 | 3510.8 | 3478.8 | 109.71 | yes |
| dotnet | stream-burst-16 | Ranvier (stream, batched) | 120 | 5173.5 | 5053.5 | 43.11 | yes |
| dotnet | stream-burst-16 | Ranvier (AsyncSource, batched) | 640 | 12362.9 | 11722.9 | 19.32 | yes |
| dotnet | stream-burst-16 | Ranvier (AsyncSource, unbatched) | 512 | 56171.6 | 55659.6 | 109.71 | yes |
| dotnet | stream-burst-256 | Ranvier (stream, batched) | 120 | 4601.2 | 4481.2 | 38.34 | yes |
| dotnet | stream-burst-256 | Ranvier (AsyncSource, batched) | 8320 | 147537.2 | 139217.2 | 17.73 | yes |
| dotnet | stream-burst-256 | Ranvier (AsyncSource, unbatched) | 8192 | 1087489.2 | 1079297.2 | 132.75 | yes |
| dotnet | stream-restart | Ranvier (stream) | 608 | 6165.5 | 5557.5 | 10.14 | yes |
| dotnet | stream-restart | Ranvier (async memo, CancelPrevious) | 760 | 3875 | 3115 | 5.10 | yes |
| dotnet | chain-affinity | Ranvier (Guarded) | 0 | 6569.2 | 6569.2 | n/a | yes |
| dotnet | chain-affinity | Ranvier (Unchecked) | 0 | 6569.2 | 6569.2 | n/a | yes |
| dotnet | chain-affinity | Ranvier (Serialised) | 0 | 6569.2 | 6569.2 | n/a | yes |
| dotnet | project-churn | Ranvier (no reader) | 4616 | 67560 | 62944 | 14.64 | yes |
| dotnet | project-churn | Ranvier (key reader) | 5016 | 68648 | 63632 | 13.69 | yes |
| dotnet | flight | Ranvier (CancelPrevious) | 1720 | 5401.6 | 3681.6 | 3.14 | yes |
| dotnet | flight | Ranvier (KeepLatest) | 1672 | 5353.6 | 3681.6 | 3.20 | yes |
| dotnet | flight | Ranvier (Queue) | 3184 | 7185.6 | 4001.6 | 2.26 | yes |
| dotnet | flight | Ranvier (FinishCurrent) | 1744 | 5737.6 | 3993.6 | 3.29 | yes |
| dotnet | flight | Ranvier (Drop) | 872 | 3995 | 3123 | 4.58 | yes |
| dotnet | flight | Ranvier (DropWhilePending) | 872 | 3995 | 3123 | 4.58 | yes |
| dotnet | fail-recompute | Ranvier (succeeding) | 0 | 3462.8 | 3462.8 | n/a | yes |
| dotnet | fail-recompute | Ranvier (failing) | 1576 | 5038.8 | 3462.8 | 3.20 | yes |
| dotnet | editable-edit | Ranvier | 0 | 429715.7 | 429715.7 | n/a | yes |
| dotnet | editable-upstream | Ranvier | 2400 | 544915.7 | 542515.7 | 227.05 | yes |
| dotnet | mvu-dispatch | Ranvier (signal per field) | 0 | 1687.4 | 1687.4 | n/a | yes |
| dotnet | mvu-dispatch | Ranvier (Mvu) | 400 | 104487.1 | 104087.1 | 261.22 | yes |
| node | timed-capture-debounce | Ranvier | -0.2 | -232882.8 | -232882.5 | n/a | no |
| node | timed-admit-debounce | Ranvier | 3584.7 | -1341753.5 | -1345338.2 | n/a | no |
| node | timed-capture-first | Ranvier | 0 | 60055 | 60055 | n/a | no |
| node | timed-admit-first | Ranvier | 0 | -1089499.8 | -1089499.8 | n/a | no |
| node | timed-capture-last | Ranvier | 0.2 | -224617.1 | -224617.3 | n/a | no |
| node | timed-admit-last | Ranvier | 3584.7 | -1333250.8 | -1336835.5 | n/a | no |
| node | timed-capture-both | Ranvier | 0 | -208296.1 | -208296.1 | n/a | no |
| node | timed-admit-both | Ranvier | 3584.6 | -1359799.2 | -1363383.8 | n/a | no |
| node | create | Ranvier | 1026871 | 3608907 | 2582036 | n/a | no |
| node | update | Ranvier | 7216.8 | -1431882.7 | -1439099.5 | n/a | no |
| node | chain | Ranvier | 0 | -19126.3 | -19126.3 | n/a | no |
| node | cutoff | Ranvier | 0 | -160.6 | -160.6 | n/a | no |
| node | dispose | Ranvier | 200 | 1349339 | 1349139 | 6746.70 | yes |
| node | create-on | Ranvier | 899173 | 3927455 | 3028282 | n/a | no |
| node | update-on | Ranvier | 7216.8 | -1561273.9 | -1568490.7 | n/a | no |
| node | dispose-on | Ranvier | 200 | 1349279 | 1349079 | 6746.40 | yes |
| node | derive-effect | Ranvier | 0 | -5687.5 | -5687.5 | n/a | no |
| node | derive-on | Ranvier | 0 | -4974.1 | -4974.1 | n/a | no |
| node | work-status | Ranvier (createEffect) | 32024.5 | -1593614.6 | -1625639 | n/a | no |
| node | work-status | Ranvier (createEffectOn) | 9697.8 | -1110493 | -1120190.7 | n/a | no |
| node | work-value | Ranvier (createEffect) | 32024.5 | -1322333.8 | -1354358.2 | n/a | no |
| node | work-value | Ranvier (createEffectOn) | 32024.5 | -1171082.6 | -1203107 | n/a | no |
| node | work-form | Ranvier (createEffect) | 0 | 184953.1 | 184953.1 | n/a | no |
| node | work-form | Ranvier (createEffectOn) | 0 | 140269.3 | 140269.3 | n/a | no |
| node | keyed-remove | Ranvier (fresh items) | 333404.5 | 1477588.3 | 1144183.8 | n/a | no |
| node | keyed-remove | Ranvier (stable items) | 10484.2 | 51856.2 | 41372 | 4.95 | yes |
| node | keyed-swap | Ranvier (fresh items) | 361855 | 1488506.7 | 1126651.7 | n/a | no |
| node | keyed-swap | Ranvier (stable items) | 10605 | 36354.6 | 25749.6 | 3.43 | yes |
| node | keyed-insert | Ranvier (fresh items) | 333677.4 | 1475110.2 | 1141432.8 | n/a | no |
| node | keyed-insert | Ranvier (stable items) | 10401.8 | 48393.9 | 37992.2 | 4.65 | yes |
| node | server-render | Ranvier | 196283.2 | 294187.2 | 97904 | 1.50 | yes |
| node | app-table | Ranvier | 258203 | -9552223.8 | -9810426.9 | n/a | no |
| node | app-detail | Ranvier | 27168.8 | -164559.5 | -191728.3 | n/a | no |
| node | shape-diamond | Ranvier | 0 | -190644.2 | -190644.2 | n/a | no |
| node | shape-dynamic | Ranvier | 0 | 155551.3 | 155551.3 | n/a | no |
| node | async-resolve | Ranvier | 153209.1 | -113587.8 | -266797 | n/a | no |
| node | async-recover | Ranvier | 4224.8 | 3443.9 | -780.8 | n/a | no |
| node | stream-burst-1 | Ranvier (stream, batched) | 296.1 | 21177 | 20880.9 | 71.52 | yes |
| node | stream-burst-1 | Ranvier (AsyncSource, batched) | 400 | 20168.9 | 19768.8 | 50.42 | yes |
| node | stream-burst-1 | Ranvier (AsyncSource, unbatched) | 344.1 | 19284.5 | 18940.4 | 56.05 | yes |
| node | stream-burst-16 | Ranvier (stream, batched) | 296.1 | 21177.1 | 20881 | 71.52 | yes |
| node | stream-burst-16 | Ranvier (AsyncSource, batched) | 1960.2 | -23547.3 | -25507.5 | n/a | no |
| node | stream-burst-16 | Ranvier (AsyncSource, unbatched) | 5504.1 | 20451.8 | 14947.7 | n/a | no |
| node | stream-burst-256 | Ranvier (stream, batched) | 296 | 21012.4 | 20716.4 | 70.99 | yes |
| node | stream-burst-256 | Ranvier (AsyncSource, batched) | 26923.2 | 308247.2 | 281324 | 11.45 | yes |
| node | stream-burst-256 | Ranvier (AsyncSource, unbatched) | 88065.2 | -4834906.4 | -4922971.6 | n/a | no |
| node | stream-restart | Ranvier (stream) | 1768 | 29418.4 | 27650.4 | 16.64 | yes |
| node | stream-restart | Ranvier (async memo, CancelPrevious) | 1216 | 17633.1 | 16417.1 | 14.50 | yes |
| node | keyed-remove | Ranvier (memo-free rows) | 10595.4 | 34287.5 | 23692.2 | 3.24 | yes |
| node | keyed-swap | Ranvier (memo-free rows) | 10604.3 | 29457.8 | 18853.4 | 2.78 | yes |
| node | keyed-insert | Ranvier (memo-free rows) | 10537 | 34275.4 | 23738.4 | 3.25 | yes |

## Trace history growth

Least-squares slopes against update count. The log's retained bytes per update are the traced slope minus the
untraced slope; its bytes per event divide that by the event slope.

| Runtime | Workload | Events/update | Retained B/update, untraced | Retained B/update, traced | Log B/update | Log B/event |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| dotnet | memo | 14.0 | 0.0 | 878.9 | 878.9 | 62.8 |
| dotnet | fanout | 392.1 | 0.0 | 18185.8 | 18185.8 | 46.4 |
| node | memo | 14.0 | 0.2 | 1393.3 | 1393.1 | 99.5 |
| node | fanout | 392.1 | 4.3 | 37903.7 | 37899.4 | 96.7 |

## Export and incremental reads

Fastest of three runs, in milliseconds, on the traced build. The tail read copies the full log and slices the
events of the last 100 updates, as the signal maps read it.

| Runtime | Workload | Updates | Events | Trace.events | Trace.snapshot | Trace.dumpText | Tail read | Tail events |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| dotnet | memo | 1000 | 14017 | 0.066 | 2.940 | 2.732 | 0.074 | 1400 |
| dotnet | memo | 10000 | 140017 | 0.302 | 20.800 | 35.302 | 0.532 | 1400 |
| dotnet | memo | 100000 | 1400017 | 7.281 | 83.890 | 217.842 | 5.303 | 1400 |
| dotnet | fanout | 100 | 39721 | 0.204 | 4.867 | 4.988 | 0.382 | 39200 |
| dotnet | fanout | 1000 | 392521 | 2.053 | 43.642 | 52.993 | 2.265 | 39200 |
| dotnet | fanout | 10000 | 3920521 | 17.455 | 283.085 | 371.839 | 12.439 | 39200 |
| node | memo | 1000 | 14017 | 0.002 | 7.756 | 6.337 | 0.003 | 1400 |
| node | memo | 10000 | 140017 | 0.112 | 19.860 | 66.333 | 0.039 | 1400 |
| node | memo | 100000 | 1400017 | 1.008 | 116.170 | 766.964 | 1.068 | 1400 |
| node | fanout | 100 | 39721 | 0.008 | 17.346 | 15.050 | 0.026 | 39200 |
| node | fanout | 1000 | 392521 | 0.297 | 84.264 | 205.989 | 0.170 | 39200 |
| node | fanout | 10000 | 3920521 | 3.076 | 708.219 | 2249.546 | 2.932 | 39200 |

## Chromium keyed rows

Chromium 156.0.8078.4, the Playground's framework harness. Each bundle runs in its own browser processes,
so a ratio divides the traced median by the untraced median, not paired samples.

| Operation | Mode | Untraced ms | Traced ms | Ratio |
| --- | --- | ---: | ---: | ---: |
| create1k | ranvier | 7.035 | 11.070 | 1.57 |
| create1k | ranvier-sugar | 7.005 | 11.085 | 1.58 |
| create10k | ranvier | 48.260 | 86.335 | 1.79 |
| create10k | ranvier-sugar | 48.150 | 85.900 | 1.78 |
| replace1k | ranvier | 5.965 | 12.480 | 2.09 |
| replace1k | ranvier-sugar | 5.865 | 12.400 | 2.11 |
| update10th1k | ranvier | 0.070 | 0.700 | 10.00 |
| update10th1k | ranvier-sugar | 0.075 | 0.730 | 9.73 |
| swap1k | ranvier | 0.150 | 0.270 | 1.80 |
| swap1k | ranvier-sugar | 0.145 | 0.270 | 1.86 |
| remove1k | ranvier | 0.170 | 0.275 | 1.62 |
| remove1k | ranvier-sugar | 0.170 | 0.270 | 1.59 |
| remove10k | ranvier | 0.580 | 0.720 | 1.24 |
| remove10k | ranvier-sugar | 0.510 | 0.725 | 1.42 |
| clear1k | ranvier | 1.115 | 2.370 | 2.13 |
| clear1k | ranvier-sugar | 1.075 | 2.455 | 2.28 |
| clear10k | ranvier | 8.230 | 10.555 | 1.28 |
| clear10k | ranvier-sugar | 7.790 | 10.225 | 1.31 |
| append1k | ranvier | 4.165 | 8.275 | 1.99 |
| append1k | ranvier-sugar | 4.130 | 8.145 | 1.97 |
| armsCreate1k | ranvier | 13.005 | 32.905 | 2.53 |
| armsCreate1k | ranvier-sugar | 13.545 | 33.390 | 2.47 |
| armsSwitch1k | ranvier | 8.350 | 25.635 | 3.07 |
| armsSwitch1k | ranvier-sugar | 8.545 | 24.840 | 2.91 |
| armsPayload1k | ranvier | 1.300 | 9.560 | 7.35 |
| armsPayload1k | ranvier-sugar | 1.150 | 9.475 | 8.24 |
| armsClear1k | ranvier | 2.710 | 7.305 | 2.70 |
| armsClear1k | ranvier-sugar | 2.735 | 7.420 | 2.71 |

| Measure | Mode | Rows | Untraced B/row | Traced B/row | Traced - untraced | Ratio |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| retained heap | ranvier | 1000 | 1111 | 1816 | 705 | 1.63 |
| retained heap | ranvier-sugar | 1000 | 1128 | 1827 | 699 | 1.62 |
| retained heap | ranvier | 10000 | 976 | 1584 | 609 | 1.62 |
| retained heap | ranvier-sugar | 10000 | 984 | 1587 | 603 | 1.61 |
| new JS objects | ranvier | 1000 | 879 | 1503 | 624 | 1.71 |
| new JS objects | ranvier-sugar | 1000 | 879 | 1503 | 624 | 1.71 |
| new JS objects | ranvier | 10000 | 881 | 1477 | 595 | 1.68 |
| new JS objects | ranvier-sugar | 10000 | 881 | 1477 | 595 | 1.68 |

## BenchmarkDotNet, category Trace

### TraceConstructionBenchmarks

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

### TraceCostBenchmarks

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

### TraceHistoryBenchmarks

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

### TraceQueryBenchmarks

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.402
  [Host] : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4 DEBUG
  Traced : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4

Job=Traced  Arguments=/p:RanvierTrace=true  UnrollFactor=16  
Categories=Trace  

```
| Method   | Updates | Mean          | Error        | StdDev        | Median        | Op/s       | Gen0       | Gen1       | Gen2      | Allocated    |
|--------- |-------- |--------------:|-------------:|--------------:|--------------:|-----------:|-----------:|-----------:|----------:|-------------:|
| **Events**   | **1000**    |      **42.46 μs** |     **2.600 μs** |      **7.376 μs** |      **39.39 μs** | **23,554.038** |    **91.3086** |    **91.2476** |   **90.3320** |     **548.3 KB** |
| Snapshot | 1000    |     734.55 μs |    13.765 μs |     24.821 μs |     728.73 μs |  1,361.384 |   333.0078 |   166.0156 |  166.0156 |   4712.31 KB |
| Why      | 1000    |      41.17 μs |     1.131 μs |      3.189 μs |      40.72 μs | 24,292.152 |    90.3931 |    90.2710 |   89.9658 |     548.7 KB |
| Origin   | 1000    |     919.22 μs |    31.179 μs |     90.951 μs |     919.60 μs |  1,087.884 |   333.0078 |   166.0156 |  166.0156 |   4712.46 KB |
| DumpText | 1000    |   2,143.57 μs |    90.247 μs |    263.254 μs |   2,140.57 μs |    466.510 |   839.8438 |   824.2188 |  640.6250 |   6122.42 KB |
| TailRead | 1000    |      46.08 μs |     1.064 μs |      3.138 μs |      46.02 μs | 21,702.822 |    90.8813 |    87.4634 |   87.1582 |    602.99 KB |
| **Events**   | **10000**   |   **1,001.39 μs** |    **19.006 μs** |     **20.336 μs** |   **1,008.93 μs** |    **998.614** |   **240.2344** |   **240.2344** |  **235.3516** |    **5469.6 KB** |
| Snapshot | 10000   |  13,097.94 μs |   275.004 μs |    802.200 μs |  13,072.51 μs |     76.348 |  2921.8750 |   375.0000 |  375.0000 |  47182.34 KB |
| Why      | 10000   |   1,008.69 μs |    35.306 μs |     94.238 μs |   1,002.40 μs |    991.386 |   242.1875 |   242.1875 |  237.3047 |   5470.54 KB |
| Origin   | 10000   |  13,479.77 μs |   402.605 μs |  1,187.089 μs |  13,104.38 μs |     74.185 |  2921.8750 |   375.0000 |  375.0000 |   47182.5 KB |
| DumpText | 10000   |  38,902.32 μs | 1,319.809 μs |  3,870.772 μs |  39,298.35 μs |     25.705 |  2750.0000 |  2687.5000 |  937.5000 |  62362.64 KB |
| TailRead | 10000   |     894.89 μs |    48.795 μs |    140.786 μs |     863.40 μs |  1,117.460 |   257.8125 |   255.8594 |  246.0938 |   5524.33 KB |
| **Events**   | **100000**  |   **6,414.56 μs** |   **446.380 μs** |  **1,214.410 μs** |   **5,973.93 μs** |    **155.895** |   **234.3750** |   **234.3750** |  **234.3750** |  **54689.24 KB** |
| Snapshot | 100000  |  74,182.14 μs | 1,546.310 μs |  4,535.060 μs |  73,259.24 μs |     13.480 | 25875.0000 |   375.0000 |  312.5000 | 472655.57 KB |
| Why      | 100000  |   5,843.78 μs |   161.284 μs |    465.340 μs |   5,721.19 μs |    171.122 |   242.1875 |   242.1875 |  242.1875 |  54690.27 KB |
| Origin   | 100000  |  75,132.70 μs | 1,483.792 μs |  3,438.915 μs |  74,761.27 μs |     13.310 | 25875.0000 |   375.0000 |  312.5000 | 472655.73 KB |
| DumpText | 100000  | 176,735.53 μs | 5,744.428 μs | 16,756.767 μs | 175,856.90 μs |      5.658 | 21937.5000 | 21875.0000 | 2937.5000 | 636037.57 KB |
| TailRead | 100000  |   5,660.69 μs |   156.382 μs |    458.642 μs |   5,709.01 μs |    176.657 |   234.3750 |   234.3750 |  234.3750 |  54744.55 KB |
