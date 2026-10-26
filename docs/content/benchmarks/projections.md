---
title: Projections
order: 6
---

> **Preview** — Ranvier is pre-release; its APIs may change.

## CI instruction counts
**Library comparison** shows the latest recorded engine measurements; **Trends** follows scenarios
across commits. Choose a runtime, metric or commit, filter scenarios, and toggle libraries in the
legend. The site remembers your tab; runtime and metric selections carry between tabs.
See [chart controls and uncertainty](counters.md#interactive-chart-controls) for bands, relative values
and library-counter markers.

<div data-perf-tabs data-perf-data="projections"><p role="status">Loading recorded measurements…</p></div>
<link rel="stylesheet" href="/Ranvier/benchmarks/perf-charts.css">
<script src="/Ranvier/benchmarks/perf-charts.js"></script>

Projections keep keyed, per-row reactive state over a collection source. Selectors (`Lookup`) track, per key, whether that key matches a source value. See [Benchmarks](index.md) for configuration, environment and caveats.

## Keyed-pass follow-ups

`master` at `2ce4b7d` includes the two changes below after the primitive tables on this page. Later projection work ([#78](https://github.com/shayanhabibi/Ranvier/pull/78), [#79](https://github.com/shayanhabibi/Ranvier/pull/79), [#84](https://github.com/shayanhabibi/Ranvier/pull/84)) is measured by the CI charts above, including the memo-free row variant of the `keyed-*` scenarios. The tables retain their original commits and must not be read as current timings.

[#55](https://github.com/shayanhabibi/Ranvier/pull/55) trims unchanged prefixes and suffixes under the projection's item equality policy, visits the changed middle, and handles nested passes and construction rollback. This reduces reconciliation work when item identities are retained; it does not make an arbitrary snapshot update proportional to its delta. A layout effect that reads every row still does linear work.

The [2026-10-14 keyed-list report](https://github.com/shayanhabibi/Ranvier/blob/master/docs/research/2026-10-14-core-trimmed-pass.md) compares the earlier implementation with the branch that became #55. In the final paired Node rerun, swap/remove elapsed-time medians were 0.45–0.51 of baseline, with all reported interquartile intervals below 1. Creation/clear medians were 1.07–1.20 of baseline; creation intervals crossed 1 and the 1,000-row clear interval did not. The after run used three processes with five warmups and fifteen timed rounds. Its .NET after measurements used BenchmarkDotNet's short job, so they are diagnostic rather than full-run replacements for the tables below.

[#56](https://github.com/shayanhabibi/Ranvier/pull/56) takes the full visit when no prefix or suffix can trim, avoiding the trimmed path's per-position hash-set bookkeeping. Its [matched instruction-count report](https://github.com/shayanhabibi/Ranvier/blob/master/docs/.ai/benchmarks/counters/6d496e7-untrimmable-pass.md) uses 1,000 rows, alternating base/change runs, five processes per round and scale 8 (N = 400, 2N = 800). With fresh item records in every position, remove/swap/insert retired 19% fewer instructions on .NET and 30–31% fewer under Node. Library operation counts were identical. Stable-item .NET remove/insert stayed within noise; Node stable remove/insert still included compilation effects and establish no measured speedup. Node allocation rows with a collection inside the region understate allocation.

These are two different comparisons with different baselines. Their ratios must not be multiplied into a claimed speedup for current `master`, which also includes later changes such as the Fable core changes in #57 and the projection work above.

## ProjectionBenchmarks

A projection over `Items` rows (8, 64 or 512), observed by one effect so that the projection stays scheduled.

| Method | What it measures |
| --- | --- |
| `ReadRow` | A tracked read of one cached row. |
| `EditOneItem` | A write that changes the value of the first row, re-running that row's effect. |
| `Reorder` | A write of the same keys and values in reverse order: the key order changes and every row keeps its value. |

Recorded 2026-09-28. Source: `Ranvier.Benchmarks.Projections.ProjectionBenchmarks` at commit `d87920f`.

| Method      | Items | Mean         | Error       | StdDev     | Op/s          | Gen0   | Gen1   | Allocated |
|------------ |------ |-------------:|------------:|-----------:|--------------:|-------:|-------:|----------:|
| **ReadRow**     | **8**     |     **6.184 ns** |   **0.0715 ns** |  **0.0669 ns** | **161,707,225.4** |      **-** |      **-** |         **-** |
| EditOneItem | 8     |   214.629 ns |   2.3013 ns |  2.1527 ns |   4,659,197.7 | 0.0057 |      - |      96 B |
| Reorder     | 8     |   183.185 ns |   2.5630 ns |  2.1403 ns |   5,458,957.0 | 0.0210 |      - |     352 B |
| **ReadRow**     | **64**    |     **6.013 ns** |   **0.0744 ns** |  **0.0696 ns** | **166,315,149.2** |      **-** |      **-** |         **-** |
| EditOneItem | 64    |   925.895 ns |  11.0672 ns | 10.3522 ns |   1,080,035.8 | 0.0057 |      - |      96 B |
| Reorder     | 64    | 1,140.921 ns |  10.3918 ns |  9.2121 ns |     876,485.0 | 0.1411 |      - |    2368 B |
| **ReadRow**     | **512**   |     **6.005 ns** |   **0.0735 ns** |  **0.0688 ns** | **166,522,611.8** |      **-** |      **-** |         **-** |
| EditOneItem | 512   | 6,653.607 ns |  45.0946 ns | 35.2069 ns |     150,294.4 |      - |      - |      96 B |
| Reorder     | 512   | 8,307.363 ns | 104.3172 ns | 92.4745 ns |     120,375.1 | 1.0986 | 0.0916 |   18496 B |

## Key churn and key readers

A key reader (`Projection.NewKeyReader`) reports the keys added, removed and moved since its previous read. These cases measure a write that removes one key and adds another, and what a reader adds to it.

`ProjectionBenchmarks.ChurnOneKey` runs on the fixture above's `Items`: one effect observes each row except the last, and one observes `Keys`.

| Method | What it measures |
| --- | --- |
| `ChurnOneKey` | A write removing the source's last key and adding a key the source has never held, with no key reader. |

`DeltaReaderChurnBenchmarks` is `ChurnOneKey` with `Readers` key readers (1 or 4), each read by its own effect after every write, at `Items` 8, 64 or 512.

| Method | What it measures |
| --- | --- |
| `ChurnOneKey` | The same write, then one read per reader. The difference from `ProjectionBenchmarks.ChurnOneKey` is the readers' cost. |

`DeltaReaderBenchmarks` compares two ways of finding one change. The source toggles key `Items / 2` out and back in on every write, at `Items` 64, 512 or 10 000.

| Method | What it measures |
| --- | --- |
| `SetDiffAfterOneRemoval` | Baseline. The toggle, then a set difference of the previous and current `Keys`: linear in `Items`. |
| `ReadAfterOneRemoval` | The toggle, then the change read from the key reader. |
| `ReadIdle` | A read of the key reader with nothing changed since the previous read. |

The counter scenario `project-churn` measures the same write over 1000 rows, with no key reader and with one read by an effect. Figures are retired instructions per operation at commit `e13f159`, .NET 10 with tiered compilation and PGO off (see [Instruction counts](counters.md)):

| Variant | instr/op | bytes/op | Library counters/op |
| --- | ---: | ---: | --- |
| No key reader | 539,775 | 4,592 | SignalsCreated 1, MemosCreated 1, EffectRuns 1, Flushes 1 |
| One key reader | 541,276 | 4,992 | SignalsCreated 1, MemosCreated 1, EffectRuns 2, Flushes 1 |

A key reader adds about 1,500 instructions and 400 bytes to the churn pass, about 0.3 %. The BenchmarkDotNet cases, including `DeltaReaderBenchmarks` and four readers, are in the suite for local runs (`dotnet run --project bench/Ranvier.Benchmarks -c Release -- --filter "*ChurnOneKey*" "*DeltaReader*"`) and have no published figure yet. The full report is [`e13f159.md`](https://github.com/shayanhabibi/Ranvier/blob/master/docs/.ai/benchmarks/counters/e13f159.md).

## LookupBenchmarks

A selector created with `createSelector`, whose key 7 loses its last observer and is read again before the next transition. Each case should reuse the key's cell; a regression would rebuild it.

| Method | What it measures |
| --- | --- |
| `RemountKey` | Mounting and disposing the only reader of a key. |
| `ReadAheadOfGet` | Re-running a reader that inserts or drops a read ahead of its `Get`. |
| `UntrackedGet` | An untracked read of an unobserved key. |

Recorded 2026-09-28. Source: `Ranvier.Benchmarks.Projections.LookupBenchmarks` at commit `d87920f`.

| Method         | Mean      | Error    | StdDev   | Op/s         | Gen0   | Allocated |
|--------------- |----------:|---------:|---------:|-------------:|-------:|----------:|
| RemountKey     | 101.88 ns | 1.721 ns | 1.525 ns |  9,815,568.7 | 0.0257 |     432 B |
| ReadAheadOfGet |  54.37 ns | 0.434 ns | 0.406 ns | 18,392,960.7 | 0.0014 |      24 B |
| UntrackedGet   |  21.15 ns | 0.170 ns | 0.159 ns | 47,272,722.8 | 0.0014 |      24 B |
