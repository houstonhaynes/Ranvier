# Traced and untraced baseline at `f48a3a7`

The baseline for issue #164: the same workloads compiled with `RanvierTrace=false` and
`RanvierTrace=true`, on .NET, on Node.js through Fable, and in Chromium. Later instrumentation
changes (#165, #166) are measured against these files. The budget derived from them is in
[`docs/content/benchmarks/tracing.md`](../../../../content/benchmarks/tracing.md).

`report.md` is the summary; the other files are the raw results it is computed from.

## Reproduction

From the repository root, on a clean tree at the source revision:

```shell
dotnet fsi build.fsx -- perf trace <fresh-directory>
```

The command takes about 75 minutes on the machine below. `--short` runs BenchmarkDotNet's
short job and one Chromium process, a smoke run that is not comparable with this baseline.
`--no-chromium` skips the Chromium stage.

Then regenerate the summary from a result directory with
`dotnet <dir>/dotnet/untraced/Ranvier.Counters.dll trace-report --dir <dir>`.

## Machine and versions

| | |
| --- | --- |
| Source | `f48a3a7fdf37c6e936ab6237d971642fd012a8ce`, clean |
| CPU | AMD Ryzen 9 9900X, 12 cores, 24 logical processors |
| OS | Windows 11 Pro 10.0.26200 |
| .NET | SDK 10.0.402, runtime 10.0.13, x64 RyuJIT x86-64-v4 |
| BenchmarkDotNet | 0.15.8, default job, `UnrollFactor=16` |
| Fable | fickl 0.5.3, fable-library-js 2.9.0 |
| Node.js | 26.7.0 |
| Chromium | 156.0.8078.4 (Playwright 1.64.1), headless |
| Date | 2026-10-24 |

`versions.json` records the same values as the command wrote them.

## Methodology

| Files | Producer | What is measured |
| --- | --- | --- |
| `counters/untraced`, `counters/traced` | `Ranvier.Counters --fable ... --no-pmc` | Bytes per operation and gen-0 collections of every counter scenario on .NET and Node.js. No hardware counters on Windows, so no instruction or CPU-time columns. |
| `growth/<runtime>-<variant>.json` | `Ranvier.Counters trace-growth`, `Main.fs.js --trace-growth` | Sustained writes on one graph (`memo` at 1,000, 10,000 and 100,000 updates; `fanout` at 100, 1,000 and 10,000). At each checkpoint: event count, retained heap after a full collection, and the fastest of three timings of `Trace.events`, `Trace.snapshot`, `Trace.dumpText` and the tail read. |
| `benchmarks/results` | BenchmarkDotNet, category `Trace` | Time, throughput and allocation of the steady-state and construction workloads under an `Untraced` job (the ratio baseline) and a `Traced` job, and the trace queries at 1,000 to 100,000 updates on the traced job. |
| `chromium/rows-<variant>.json` | `keyed-rows.mjs --modes=ranvier,ranvier-sugar` | Median milliseconds per keyed-list operation over 3 browser processes of 15 rounds (3 warm-up rounds), and the retained V8 heap per row of mounted 1,000 and 10,000-row lists. |
| `chromium/heap-<variant>.json` | `keyed-heap.mjs` | Self size per row of the objects new between two heap snapshots of a mounted list, over 2 browser processes. |

Each variant is a separate build of the same source. The traced and untraced Chromium bundles
run in separate browser processes, so their ratios divide medians rather than paired samples.

## Reading the data

- A Node.js counter row with `GC-free = no` ran a collection inside a measured region. Its
  bytes/op understate the allocation and can be negative. Most traced Node.js rows are in this
  state; the growth probe, which reads the heap after `global.gc ()`, is the Node.js figure for
  the log's cost.
- The traced .NET counter rows record a growing log, so their bytes/op include the amortised
  growth of the log's backing array.
- `Trace.history` is quadratic in the run count: 11.5 ms at 1,000 updates, 104 ms at 3,000 and
  4.2 s at 10,000.
- An earlier run of the command on a dirty tree at `a33622e` (short job, same machine) reproduced the
  traced .NET bytes/op within 0.6 % and the log bytes per event exactly; export timings
  differed by up to 40 %.
