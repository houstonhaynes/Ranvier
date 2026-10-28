---
title: Benchmarks
order: 1
---

> **Preview** — Ranvier is pre-release; its APIs may change.

These benchmarks track Ranvier across revisions. Compare runs from the same machine and
configuration; their absolute times do not predict your application's performance.

BenchmarkDotNet covers signals, memos, effects, lifetimes, projections and suspension. CI adds
instruction-count history, including streams and comparisons with other libraries. Each dated
table identifies its source commit; interactive charts identify the selected measurement.

| Area | What it covers |
| --- | --- |
| [Signals](signals.md) | Reads, writes with 0 to 64 observers, the equality cutoff, `StructuralPolicy` against identity comparison, and each `ThreadAffinity`. |
| [Memos](memos.md) | Cache hits, one recomputation under each `ThreadAffinity`, propagation through chains of 1 to 64 memos, and a diamond. |
| [Effects](effects.md) | Write-and-flush with 1 to 64 effects, a batch of ten writes, one-field writes to a model, and editable values. |
| [Lifetimes](lifetimes.md) | Construction and disposal of nodes, fan-out on one source, scopes with children, observer removal and relinking on a wide source, and the shallow size of each node type. |
| [Projections](projections.md) | Keyed projection reads, single-item edits, reorders, key churn with and without key readers, and selector (`Lookup`) reads. |
| [Suspension](suspension.md) | The cost of the pending and error channels: throwing through a chain, boundaries, settling a source, flights under each `FlightPolicy`, and failing recomputations. |
| [Streams](streams.md) | Delivering bursts of items into a stream fold, against async-source settles, and restarting a stream against restarting an async memo. CI instruction counts only. |
| [Instruction counts](counters.md) | Whole scenarios by instructions per operation, against FSharp.Data.Adaptive, R3 and Fable.Ripple, on .NET and Node.js. |
| [Tracing cost](tracing.md) | What the traced build (`RanvierTrace=true`) costs against the untraced build on .NET, Node.js and Chromium: events and retained bytes per update, time and allocation ratios, export and incremental-read costs, and the budget for instrumentation changes. |

## Results

[Query reconciliation](queries.md) compares the package's local save publication
with a hand-written update of the same three-page dictionary model. It is a
separate scenario from the primitive results below.

The benchmark suite lives in this repository as `bench/Ranvier.Benchmarks`. Most primitive timing tables come from the full run at commit `d87920f`, recorded on **2026-09-28**. Binding tables were recorded on **2026-10-13** at `70291a5`; query reconciliation has its own **2026-10-05** run. Each table states its source. Later cases use the [instruction-counter](counters.md) run at `e13f159` where a counter scenario covers them; other cases remain available for local runs without a published figure.

### Historical follow-ups at `2ce4b7d` (2026-10-16)

The dated tables below are historical measurements, not a rerun of current `master`. Since those runs:

- **Keyed projections** gained prefix/suffix trimming and nested-pass fixes in [#55](https://github.com/ranvier-org/Ranvier/pull/55), then a full-visit fallback for passes that cannot trim in [#56](https://github.com/ranvier-org/Ranvier/pull/56). [Projections](projections.md#keyed-pass-follow-ups) links the paired timing and hardware-counter evidence, including its limitations.
- **Fable core output** sheds redundant affinity/inbox work, slot wrappers, async continuation/lock callbacks and checked scheduler stores in [#57](https://github.com/ranvier-org/Ranvier/pull/57). The existing .NET executable implementation is preserved. [Instruction counts](counters.md#fable-core-follow-up) distinguishes the earlier experiment's timings from hardware-counter results; no fresh instruction-count acceptance run is available for this commit.

The static charts on [Instruction counts](counters.md) still show `e13f159`. The CI instruction-count charts on each benchmark page follow every measured `master` commit, including the later engine changes. The repository also retains the broader [2026-10-04 report at `878f294`](https://github.com/ranvier-org/Ranvier/blob/master/docs/.ai/benchmarks/counters/878f294.md), and the targeted [2026-10-15 keyed-pass comparison](https://github.com/ranvier-org/Ranvier/blob/master/docs/.ai/benchmarks/counters/6d496e7-untrimmable-pass.md). None is a whole-suite measurement of `2ce4b7d`.

The BenchmarkDotNet comparison runs against other .NET reactive libraries are not published here; [Instruction counts](counters.md) compares engines by instructions instead. The engine-internal diagnostic probes in the suite are not published either; they compare candidate implementations of internal data structures rather than measure a public operation.

## Configuration

Every run uses the same BenchmarkDotNet configuration, so that runs are comparable with each other:

- **Job**: BenchmarkDotNet's default job (`Job.Default`) with **`UnrollFactor = 16`**. The primitives are small enough that loop overhead would otherwise be a visible part of the measurement, so each measured call is repeated sixteen times in an unrolled loop.
- **Diagnoser**: `MemoryDiagnoser`, which adds the `Gen0`/`Gen1` and `Allocated` columns (bytes allocated per operation; `-` means none).
- **Columns**: the default column set plus `Categories` and `Op/s` (operations per second).
- **Ratio style**: `RatioStyle.Trend`, so ratios read as "_N_x slower/faster" relative to the class's baseline method.
- **Exporters**: GitHub-flavoured Markdown (the tables on these pages) and compressed full JSON (for diffing runs).
- A **short** configuration (`Job.ShortRun`, same unroll factor) exists for smoke runs. It catches order-of-magnitude regressions only. The primitive tables use the full configuration; the linked keyed-list research also reports explicitly labelled short runs, whose large errors limit timing conclusions.

## Environment

The September primitive tables were recorded on a single machine, as reported by BenchmarkDotNet. Later reports state their own SDK/runtime versions and settings:

```text
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
```

The `DEBUG` marker on the `[Host]` line refers to BenchmarkDotNet's host process. Measurements run in the separate `UnrollFactor=16` job process, which BenchmarkDotNet builds and launches on its own.

## Reproducing

Run these commands from the repository root, in Release configuration. A full run takes minutes.

```shell
# Everything
dotnet run --project bench/Ranvier.Benchmarks -c Release -- --filter "*"

# One area
dotnet run --project bench/Ranvier.Benchmarks -c Release -- --filter "*Memo*"

# By category: Signal, Memo, Effect, Lifetime, ObserverRemoval, Projection, Suspension, Commands, Model, Editable, Binding, Query
dotnet run --project bench/Ranvier.Benchmarks -c Release -- --anyCategories Memo

# Smoke run: short job, not for publishing
dotnet run --project bench/Ranvier.Benchmarks -c Release -- --short --filter "*"
```

The instruction-count bench runs through `counters.ps1` at the repository root, from an elevated
shell. BenchmarkDotNet writes one Markdown and one JSON report per benchmark class and overwrites them on the next run.

## Reading the results

- **Absolute times are not portable.** Many of these operations take a few nanoseconds. The machine, its power profile and whatever else is running can move them by more than most code changes do. Compare numbers only against numbers from the same machine and configuration.
- **Allocation is the steadier signal.** The `Allocated` column is deterministic where timing is not. If an operation that allocated nothing starts allocating, the column says exactly how much.
- **Ratios within a class travel better than means.** Several classes have a baseline method (for example `CachedRead` for memos, `RecomputeSettledChain` for suspension, `IdentityCutoff` for equality). The ratio against the baseline survives a change of machine better than the mean does.
- **Sub-nanosecond rows are at the resolution limit.** A result well under a nanosecond means the operation compiles to little more than a field read; treat it as negligible rather than as a precise figure, and expect a large `RatioSD` on ratios computed from it.
- **Microbenchmarks isolate one primitive.** They do not predict the cost of a real application, where graph shape, allocation pressure and the work inside computations dominate.
- **Regression sentinels.** `MemoBenchmarks.Recompute` and `ChainBenchmarks.WriteThenReadTail` are tagged `Sentinel` and are run against the parent commit on every engine change. Their cost depends on JIT tiering as well as on the algorithm, so a runtime update can move them with no code change.
