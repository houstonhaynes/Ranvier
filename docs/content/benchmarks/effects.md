---
title: Effects
order: 4
---

> **Preview** — Ranvier is pre-release; its APIs may change.

## CI instruction counts
**Library comparison** shows the latest recorded engine measurements; **Trends** follows scenarios
across commits. Choose a runtime, metric or commit, filter scenarios, and toggle libraries in the
legend. The site remembers your tab; runtime and metric selections carry between tabs.
See [chart controls and uncertainty](counters.md#interactive-chart-controls) for bands, relative values
and library-counter markers.

<div data-perf-tabs data-perf-data="effects"><p role="status">Loading recorded measurements…</p></div>
<link rel="stylesheet" href="/Ranvier/benchmarks/perf-charts.css">
<script src="/Ranvier/benchmarks/perf-charts.js"></script>

Effects are the push side of the graph: nothing reads them, so the scheduler runs them. A write that reaches an effect therefore pays for queueing and flushing on top of notification. See [Benchmarks](index.md) for configuration, environment and caveats.

## EffectBenchmarks

One signal observed by `Effects` effects (1, 8 or 64).

| Method | What it measures |
| --- | --- |
| `WriteAndFlush` | One write, then the flush that runs every effect the write invalidated. |
| `BatchOfTenWrites` | Ten writes inside one `batch`, followed by a single flush that runs each effect once. |

Recorded 2026-09-28. Source: `Ranvier.Benchmarks.Effects.EffectBenchmarks` at commit `d87920f`.

| Method           | Effects | Mean        | Error     | StdDev    | Op/s         | Allocated |
|----------------- |-------- |------------:|----------:|----------:|-------------:|----------:|
| **WriteAndFlush**    | **1**       |    **17.81 ns** |  **0.184 ns** |  **0.154 ns** | **56,133,012.1** |         **-** |
| BatchOfTenWrites | 1       |    36.89 ns |  0.311 ns |  0.276 ns | 27,104,757.4 |         - |
| **WriteAndFlush**    | **8**       |   **114.76 ns** |  **1.007 ns** |  **0.841 ns** |  **8,713,660.1** |         **-** |
| BatchOfTenWrites | 8       |   179.76 ns |  2.292 ns |  2.032 ns |  5,562,912.3 |         - |
| **WriteAndFlush**    | **64**      |   **835.32 ns** |  **6.463 ns** |  **5.729 ns** |  **1,197,153.0** |         **-** |
| BatchOfTenWrites | 64      | 1,298.42 ns | 13.692 ns | 12.138 ns |    770,167.6 |         - |

`BatchOfTenWrites` performs ten writes per operation where `WriteAndFlush` performs one, so the rows are not comparable per operation. The batch row shows the cost of ten writes that share one flush.

## FieldWriteBenchmarks

One field of a `Fields`-field model (8, 64 or 256) changed per write, with one effect per field. The model is an `int[]`, so a copy costs what copying a record of `Fields` fields costs. See [Elmish](../guide/elmish.md) for the patterns compared.

| Method | What it measures |
| --- | --- |
| `FieldSignalWrite` | Baseline. A write to one field's own signal, waking its one effect. Flat in `Fields`. |
| `SelectorMemoWrite` | A copy of a root signal's model with one field changed, re-running all `Fields` selector memos and waking one effect. |
| `MvuDispatch` | `SelectorMemoWrite` through `Mvu.Dispatch` and `Mvu.Select`. |
| `ModelCopyOnly` | The model copy alone: the part of `SelectorMemoWrite` spent outside the graph. |

The counter scenario `mvu-dispatch` compares a signal per field (`FieldSignalWrite`) with `Mvu.Dispatch` (`MvuDispatch`) at 64 fields. Figures are retired instructions per operation at commit `e13f159`, .NET 10 with tiered compilation and PGO off (see [Instruction counts](counters.md)):

| Variant | instr/op | bytes/op | Library counters/op |
| --- | ---: | ---: | --- |
| A signal per field | 607 | 0 | EffectRuns 1, Flushes 1 |
| `Mvu.Dispatch` | 48,133 | 400 | MemoRecomputes 64, EffectRuns 1, Flushes 1 |

A dispatch costs about 80 times a field signal write: every observed selector re-runs, 64 recomputes per dispatch. `SelectorMemoWrite`, `ModelCopyOnly` and the 8- and 256-field cases are in the suite for local runs (`dotnet run --project bench/Ranvier.Benchmarks -c Release -- --filter "*FieldWriteBenchmarks*"`) and have no published figure yet. The full report is [`e13f159.md`](https://github.com/ranvier-org/Ranvier/blob/master/docs/.ai/benchmarks/counters/e13f159.md).

## EditableBenchmarks

A writable derived value (`createEditable`) against a plain signal, each read by one effect.

| Method | What it measures |
| --- | --- |
| `PlainSignalWrite` | Baseline. One signal write, waking one effect. |
| `LocalEdit` | A local edit, waking the effect that reads the editable. |
| `UpstreamChange` | An upstream write propagating through the source, the seed, the editable and the effect. |

The counter scenarios `editable-edit` and `editable-upstream` measure the same two writes over 1000 editables, each read by one effect. One operation writes every 10th editable, 100 writes in all. Figures are retired instructions per operation at commit `e13f159`, .NET 10 with tiered compilation and PGO off (see [Instruction counts](counters.md)):

| Scenario | instr/op | bytes/op | Library counters/op |
| --- | ---: | ---: | --- |
| `editable-edit` | 145,006 | 0 | MemoRecomputes 100, EffectRuns 100, Flushes 100 |
| `editable-upstream` | 194,449 | 2,400 | MemoRecomputes 200, EffectRuns 100, Flushes 100 |

A local edit costs about 1,450 instructions and allocates nothing; an upstream write costs about 1,940 instructions and 24 bytes, and recomputes two memos per write. `PlainSignalWrite` has no counter scenario of its own and no published figure yet; the timing cases are in the suite for local runs (`dotnet run --project bench/Ranvier.Benchmarks -c Release -- --filter "*EditableBenchmarks*"`). The full report is [`e13f159.md`](https://github.com/ranvier-org/Ranvier/blob/master/docs/.ai/benchmarks/counters/e13f159.md).

## BindingBenchmarks

A fixed-dependency binding (`bindSignal`, `bindMemo`) against the effect that does the same copy: one consumer of one source, writing that source's value. `Nodes` is the consumer count (1, 64 or 1024).

### BindingLifecycleBenchmarks

| Method | What it measures |
| --- | --- |
| `SignalEffects` | Creating and disposing `Nodes` effects under a root, each copying a signal's value. Baseline. |
| `SignalBindings` | The same with `bindSignal`. |
| `MemoEffects` | Creating and disposing `Nodes` effects under a root, each copying a memo's value. |
| `MemoBindings` | The same with `bindMemo`. |

Recorded 2026-10-13. Source: `Ranvier.Benchmarks.Bindings.BindingLifecycleBenchmarks` at commit `70291a5`.

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

Binding / effect, each binding against the effect of its own source kind:

| Nodes | Kind | Mean ratio | Alloc ratio |
| --- | --- | ---: | ---: |
| 1 | signal | 0.849 | 0.756 |
| 1 | memo | 0.798 | 0.756 |
| 64 | signal | 0.722 | 0.543 |
| 64 | memo | 0.745 | 0.543 |
| 1024 | signal | 0.693 | 0.537 |
| 1024 | memo | 0.774 | 0.537 |

### BindingUpdateBenchmarks

| Method | What it measures |
| --- | --- |
| `WriteAndFlush` | One write to the source, then the flush that runs `Nodes` consumers. `Kind` selects `signal-effect`, `signal-binding`, `memo-effect` or `memo-binding`. |

Recorded 2026-10-13. Source: `Ranvier.Benchmarks.Bindings.BindingUpdateBenchmarks` at commit `70291a5`.

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

Binding / effect, each binding against the effect of its own source kind:

| Nodes | Kind | Mean ratio |
| --- | --- | ---: |
| 1 | signal | 0.792 |
| 1 | memo | 0.854 |
| 64 | signal | 0.686 |
| 64 | memo | 0.755 |
| 1024 | signal | 0.694 |
| 1024 | memo | 0.764 |

No kind allocates per write. Both bindings are at most 0.854x their effect at every `Nodes`.

Both reports are a single run on the recording machine: AMD Ryzen 9 9900X, Windows 11, .NET 10.0.12, BenchmarkDotNet v0.15.8. Commit `16d2e63` later reordered owner attachment in `Start` and added trace edges that compile out of Release builds. The figures were not re-recorded after it. Reproduce with `dotnet run --project bench/Ranvier.Benchmarks -c Release -- --anyCategories Binding`.
