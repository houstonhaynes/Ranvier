# Cursor reads and checkpointed retention at `b786373`

Issue #165's measurements against the #164 baseline in [`../f48a3a7`](../f48a3a7/README.md): the
`Trace.eventsSince` cursor read beside the copy-and-slice tail read, and the retained heap of a
graph checkpointed at each read position. Same machine and versions as the baseline (AMD Ryzen 9
9900X, Windows 11 10.0.26200, .NET 10.0.13, Node.js 26.7.0, fickl 0.5.3), 2026-10-24.

This is a targeted run, not a full `perf trace`: other worktrees ran builds and tests on the
machine at the same time, so timings carry more noise than the baseline's.

## Files

| Files | Source | Command |
| --- | --- | --- |
| `benchmarks/results` | `cac74dc` | `dotnet run --project bench/Ranvier.Benchmarks -c Release -- --anyCategories Trace --filter *TraceQueryBenchmarks* --artifacts <dir>` (default job) |
| `growth/dotnet-traced.json`, `growth/node-*.json` | `b786373` | `Ranvier.Counters trace-growth` (`DOTNET_TieredCompilation=0`), `node --expose-gc --single-threaded Main.fs.js --trace-growth`, both built with `RanvierCounters=false` |
| `growth/dotnet-untraced.json` | `cac74dc` | as above, untraced |
| `counters/untraced` | `b786373` (docs-only edits uncommitted) | `./counters.ps1 -NoPmc -Out <dir>` |

`cac74dc` and `b786373` differ only in `TraceLog.Truncate` trimming the event list's capacity,
which no query benchmark reaches and the Fable build does not compile.

## Results

### Cursor read against tail read

BenchmarkDotNet, traced job, the events of the last 100 `memo` updates (1,400 events):

| Updates | Events held | `TailRead` (f48a3a7) | `TailRead` | `EventsSince` | Allocated (`EventsSince`) |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1,000 | 14,017 | 46.1 µs | 148.1 µs | 1.89 µs | 54.7 KB |
| 10,000 | 140,017 | 895 µs | 692 µs | 1.78 µs | 54.7 KB |
| 100,000 | 1,400,017 | 5,661 µs | 4,833 µs | 1.44 µs | 54.7 KB |

`EventsSince` stays flat from 1,000 to 100,000 updates and allocates the returned events alone;
the tail read grows with the log. The 1,000-update `Events` and `TailRead` rows run about three
times slower than the baseline's under this run's load; the other unchanged rows are within the
baseline's spread.

Growth probe, fastest of three, milliseconds:

| Runtime | Workload | Updates | Tail read | `Trace.eventsSince` |
| --- | --- | ---: | ---: | ---: |
| .NET | `memo` | 1,000 | 0.095 | 0.007 |
| .NET | `memo` | 100,000 | 5.546 | 0.002 |
| .NET | `fanout` | 10,000 | 14.139 | 0.282 |
| Node.js | `memo` | 1,000 | 0.009 | 0.001 |
| Node.js | `memo` | 100,000 | 1.613 | 0.002 |
| Node.js | `fanout` | 10,000 | 3.495 | 0.090 |

### Checkpointed retention

`memo-checkpointed` is `memo` with `Trace.checkpoint` at each read position, 100 updates before
each probe point. Retained heap after a full collection, minus the heap before the graph:

| Runtime | Updates | `memo` (f48a3a7) | `memo` | `memo-checkpointed` | Events retained |
| --- | ---: | ---: | ---: | ---: | ---: |
| .NET | 1,000 | 739,000 B | 739,168 B | 95,120 B | 1,400 |
| .NET | 10,000 | 11,070,760 B | 11,067,760 B | 156,064 B | 1,400 |
| .NET | 100,000 | 88,791,184 B | 88,788,168 B | 156,168 B | 1,400 |
| Node.js | 1,000 | 1,941,976 B | 1,941,488 B | 1,241,816 B | 1,400 |
| Node.js | 10,000 | 14,828,568 B | 14,840,288 B | 1,676,608 B | 1,400 |
| Node.js | 100,000 | 140,029,016 B | 140,039,504 B | 890,200 B | 1,400 |

The checkpointed graph's retained slope is 0.4 B per update on .NET and -5.7 B on Node.js,
against 879 and 1,393 B for `memo`. Events per update and bytes per event of `memo` and `fanout`
match the baseline exactly on .NET (14.0 and 392.1 events, 62.8 and 46.4 B per event).

### Untraced build

`counters/untraced` against `../f48a3a7/counters/untraced`: identical bytes/op in all 30 .NET and
25 Node.js Ranvier rows. The baseline was recorded with `RanvierCounters=false`, so it holds no
library counters to compare.
