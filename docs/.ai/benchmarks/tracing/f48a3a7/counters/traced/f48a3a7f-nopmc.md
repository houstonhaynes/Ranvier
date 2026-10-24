# Counter bench, f48a3a7f

- Date: 2026-10-24 03:27:31Z
- Machine: AMD64 Family 26 Model 68 Stepping 0, AuthenticAMD, 24 logical processors, Microsoft Windows 10.0.26200, .NET 10.0.13
- Instructions: not collected (--no-pmc)
- Library counters: not collected (built without RanvierCounters)
- Versions: .NET 10.0.13, FSharp.Data.Adaptive 1.2.27, R3 1.3.1, Ranvier f48a3a7f
- Worker environment: DOTNET_TieredCompilation=0 DOTNET_TieredPGO=0 DOTNET_ReadyToRun=0 DOTNET_gcServer=0
- RanvierTrace: true
- Every figure is (m(2N) - m(N)) / N. Processor counters are the median over runs.
- Instruction counts differing by under 5 % are within run-to-run noise. Compare only reports taken at the same --scale.

## Engine differences

- R3 pushes each write straight to its subscribers, without batching or glitch-free ordering. Its chain is `Select` operators holding no cached value.
- FSharp.Data.Adaptive dispose removes the callback subscriptions only. The `AVal.map2` nodes stay in the weak output sets of their `cval`s until collected.

## timed-capture-debounce: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 124,719.06 | n/a | n/a |

## timed-admit-debounce: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 363,188.22 (GC in region) | n/a | n/a |

## timed-capture-first: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 125,231.06 | n/a | n/a |

## timed-admit-first: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 233,734.10 (GC in region) | n/a | n/a |

## timed-capture-last: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 124,719.06 | n/a | n/a |

## timed-admit-last: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 363,188.20 (GC in region) | n/a | n/a |

## timed-capture-both: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 124,719.06 | n/a | n/a |

## timed-admit-both: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 363,188.21 (GC in region) | n/a | n/a |

## create: one root of 1000 rows (N = 8)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 37,774,055 (GC in region) | n/a | n/a |
| FSharp.Data.Adaptive 1.2.27 | n/a | 2,413,717 | n/a | n/a |
| R3 1.3.1 | n/a | 584,160 | n/a | n/a |

## update: write every 10th of 1000 rows (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 208,058.08 | n/a | n/a |
| FSharp.Data.Adaptive 1.2.27 | n/a | 63,200 | n/a | n/a |
| R3 1.3.1 | n/a | 0 | n/a | n/a |

## chain: write the source of 4 memos, read the tail (N = 5000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 6,569.16 | n/a | n/a |
| FSharp.Data.Adaptive 1.2.27 | n/a | 472 | n/a | n/a |
| R3 1.3.1 | n/a | 0 | n/a | n/a |

## cutoff: write an equal value to an observed source (N = 50000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 208.86 | n/a | n/a |
| FSharp.Data.Adaptive 1.2.27 | n/a | 352 | n/a | n/a |
| R3 1.3.1 | n/a | 0 | n/a | n/a |

## dispose: dispose one root of 1000 rows (N = 8)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 312,112 | n/a | n/a |
| FSharp.Data.Adaptive 1.2.27 | n/a | 0 | n/a | n/a |
| R3 1.3.1 | n/a | 0 | n/a | n/a |

## create-on: one root of 1000 createEffectOn rows (N = 8)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 36,757,938 (GC in region) | n/a | n/a |

## update-on: write every 10th of 1000 createEffectOn rows (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 211,258.08 | n/a | n/a |

## dispose-on: dispose one root of 1000 createEffectOn rows (N = 8)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 312,112 | n/a | n/a |

## derive-effect: write a new value whose derived value is unchanged, Effect (N = 50000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 1,870.86 | n/a | n/a |

## derive-on: write a new value whose derived value is unchanged, createEffectOn (N = 50000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 1,902.86 | n/a | n/a |

## work-status: write every 10th of 1000 rows; the act formats a label, 1 write in 10 changes it (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (createEffect) | n/a | 212,037.44 | n/a | n/a |
| Ranvier f48a3a7f (createEffectOn) | n/a | 211,656.48 | n/a | n/a |

## work-value: write every 10th of 1000 rows; the act formats a label, every write changes it (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (createEffect) | n/a | 214,525.28 | n/a | n/a |
| Ranvier f48a3a7f (createEffectOn) | n/a | 217,725.28 | n/a | n/a |

## work-form: write one field of each of 125 8-field forms; validity feeds a signal and an effect (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (createEffect) | n/a | 251,616.96 | n/a | n/a |
| Ranvier f48a3a7f (createEffectOn) | n/a | 238,755.68 | n/a | n/a |

## project-edit: change one row of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 3,312 | n/a | n/a |

## project-reorder: reverse a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 117,696 | n/a | n/a |

## keyed-remove: alternately remove and restore the middle row of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (fresh items) | n/a | 2,062,104.48 | n/a | n/a |
| Ranvier f48a3a7f (stable items) | n/a | 40,427.20 | n/a | n/a |
| Ranvier f48a3a7f (memo-free rows) | n/a | 65,080 | n/a | n/a |

## keyed-swap: alternately swap and restore the second and second-to-last rows of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (fresh items) | n/a | 2,029,933.28 | n/a | n/a |
| Ranvier f48a3a7f (stable items) | n/a | 7,296 | n/a | n/a |
| Ranvier f48a3a7f (memo-free rows) | n/a | 6,480 | n/a | n/a |

## keyed-insert: alternately prepend a row and restore of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (fresh items) | n/a | 2,062,604.48 | n/a | n/a |
| Ranvier f48a3a7f (stable items) | n/a | 39,743.20 | n/a | n/a |
| Ranvier f48a3a7f (memo-free rows) | n/a | 65,084 | n/a | n/a |

## server-render: render a 100-row keyed table as one request (N = 5)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 3,344,592 | n/a | n/a |

## project-chain: change one row of a 1000-row filter, sortBy, map chain (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 84,112 | n/a | n/a |

## app-table: alternate a filter query and a sort direction over 1000 rows (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 11,401,084.64 (GC in region) | n/a | n/a |

## app-detail: move the selection over 1000 rows; the detail rebuilds 20 memo and effect pairs (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 873,803.84 | n/a | n/a |

## shape-diamond: write a source read by 100 memos joined by one (N = 5000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 169,460.87 (GC in region) | n/a | n/a |

## shape-dynamic: alternate a branch flip and a write of every active source of 100 effects (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 182,858.13 | n/a | n/a |

## async-resolve: reload 10 suspense widgets of 10 sources, then settle each (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 2,268,441.60 | n/a | n/a |

## async-recover: fail one source of one error-boundary widget, then settle a replacement (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 245,427.44 | n/a | n/a |

## stream-burst-1: deliver 1 items per round into a stream fold read by a memo and an effect (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (stream, batched) | n/a | 5,173.49 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, batched) | n/a | 3,750.77 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, unbatched) | n/a | 3,510.77 | n/a | n/a |

## stream-burst-16: deliver 16 items per round into a stream fold read by a memo and an effect (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (stream, batched) | n/a | 5,173.49 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, batched) | n/a | 12,362.93 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, unbatched) | n/a | 56,171.57 | n/a | n/a |

## stream-burst-256: deliver 256 items per round into a stream fold read by a memo and an effect (N = 20)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (stream, batched) | n/a | 4,601.20 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, batched) | n/a | 147,537.20 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, unbatched) | n/a | 1,087,489.20 | n/a | n/a |

## stream-restart: write a dependency of a stream, restarting it, then deliver one item; or of an async memo, restarting its flight (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (stream) | n/a | 6,165.49 | n/a | n/a |
| Ranvier f48a3a7f (async memo, CancelPrevious) | n/a | 3,875.01 | n/a | n/a |

## chain-affinity: write the source of 4 memos, read the tail, per thread affinity (N = 5000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (Guarded) | n/a | 6,569.16 | n/a | n/a |
| Ranvier f48a3a7f (Unchecked) | n/a | 6,569.16 | n/a | n/a |
| Ranvier f48a3a7f (Serialised) | n/a | 6,569.16 | n/a | n/a |

## project-churn: replace the last key of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (no reader) | n/a | 67,560 | n/a | n/a |
| Ranvier f48a3a7f (key reader) | n/a | 68,648 | n/a | n/a |

## flight: write an async memo's trigger twice, then settle every flight the writes started (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (CancelPrevious) | n/a | 5,401.62 | n/a | n/a |
| Ranvier f48a3a7f (KeepLatest) | n/a | 5,353.62 | n/a | n/a |
| Ranvier f48a3a7f (Queue) | n/a | 7,185.62 | n/a | n/a |
| Ranvier f48a3a7f (FinishCurrent) | n/a | 5,737.62 | n/a | n/a |
| Ranvier f48a3a7f (Drop) | n/a | 3,995.01 | n/a | n/a |
| Ranvier f48a3a7f (DropWhilePending) | n/a | 3,995.01 | n/a | n/a |

## fail-recompute: re-run a memo and its one reader, then read the reader's failure origin (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (succeeding) | n/a | 3,462.77 | n/a | n/a |
| Ranvier f48a3a7f (failing) | n/a | 5,038.77 | n/a | n/a |

## editable-edit: edit every 10th of 1000 editables (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 429,715.68 | n/a | n/a |

## editable-upstream: write the seed source of every 10th of 1000 editables (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 544,915.68 | n/a | n/a |

## mvu-dispatch: change one field of a 64-field model, one reader per field (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (signal per field) | n/a | 1,687.41 | n/a | n/a |
| Ranvier f48a3a7f (Mvu) | n/a | 104,487.09 | n/a | n/a |

## control: refill a 256-entry closure table, sum it, and sort a copy of its array (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Control | n/a | 1,048 | n/a | n/a |

## Calibration, 1 run(s)

One run: nothing to compare.

# Fable under Node.js v26.7.0

- Versions: Fable.Ripple 1.0.0-beta.5, Node.js v26.7.0, Ranvier f48a3a7f, Xantham.Fable.Core 0.2.0, fable-library-js 5.23.0
- node flags: --expose-gc --single-threaded --min-semi-space-size=32 --max-semi-space-size=32
- Instructions: not collected (--no-pmc)
- bytes/op: growth of the new, old and large-object heap spaces; median of 1 processes. bytes range: minimum..maximum, 0 when every process agreed.
- heapUsed/op: growth of `process.memoryUsage().heapUsed`, which adds the code and trusted spaces.
- Every figure is (m(2N) - m(N)) / N. A figure followed by a bracketed range differed between processes.

## timed-capture-debounce: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | -232,882.75 (GC in region) | 0 | -232,957.48 | n/a | n/a |

## timed-admit-debounce: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | -1,341,753.52 (GC in region) | 0 | -1,341,814.64 | n/a | n/a |

## timed-capture-first: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 60,054.98 (GC in region) | 0 | 60,051.78 | n/a | n/a |

## timed-admit-first: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | -1,089,499.85 (GC in region) | 0 | -1,089,583.50 | n/a | n/a |

## timed-capture-last: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | -224,617.09 (GC in region) | 0 | -224,639.26 | n/a | n/a |

## timed-admit-last: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | -1,333,250.84 (GC in region) | 0 | -1,333,311.16 | n/a | n/a |

## timed-capture-both: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | -208,296.06 (GC in region) | 0 | -208,334.72 | n/a | n/a |

## timed-admit-both: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | -1,359,799.25 (GC in region) | 0 | -1,359,862.02 | n/a | n/a |

## create: one root of 1000 rows (N = 8)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 3,608,907 (GC in region) | 0 | 3,608,509 | n/a | n/a |
| Fable.Ripple 1.0.0-beta.5 | n/a | 1,323,472 | 0 | 1,323,071 | n/a | n/a |

## update: write every 10th of 1000 rows (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | -1,431,882.72 (GC in region) | 0 | -1,436,400.96 | n/a | n/a |
| Fable.Ripple 1.0.0-beta.5 | n/a | 22,426.08 | 0 | 22,426.08 | n/a | n/a |

## chain: write the source of 4 memos, read the tail (N = 5000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | -19,126.26 (GC in region) | 0 | -19,137.33 | n/a | n/a |
| Fable.Ripple 1.0.0-beta.5 | n/a | 0 | 0 | 0 | n/a | n/a |

## cutoff: write an equal value to an observed source (N = 50000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | -160.57 (GC in region) | 0 | -160.57 | n/a | n/a |
| Fable.Ripple 1.0.0-beta.5 | n/a | 0 | 0 | 0 | n/a | n/a |

## dispose: dispose one root of 1000 rows (N = 8)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 1,349,339 | 0 | 1,349,339 | n/a | n/a |
| Fable.Ripple 1.0.0-beta.5 | n/a | 29,519 | 0 | 29,519 | n/a | n/a |

## create-on: one root of 1000 createEffectOn rows (N = 8)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 3,927,455 (GC in region) | 0 | 3,927,455 | n/a | n/a |

## update-on: write every 10th of 1000 createEffectOn rows (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | -1,561,273.92 (GC in region) | 0 | -1,565,552.16 | n/a | n/a |

## dispose-on: dispose one root of 1000 createEffectOn rows (N = 8)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 1,349,279 | 0 | 1,349,279 | n/a | n/a |

## derive-effect: write a new value whose derived value is unchanged, Effect (N = 50000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | -5,687.51 (GC in region) | 0 | -5,688.42 | n/a | n/a |

## derive-on: write a new value whose derived value is unchanged, createEffectOn (N = 50000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | -4,974.14 (GC in region) | 0 | -4,975.13 | n/a | n/a |

## work-status: write every 10th of 1000 rows; the act formats a label, 1 write in 10 changes it (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (createEffect) | n/a | -1,593,614.56 (GC in region) | 0 | -1,597,149.44 | n/a | n/a |
| Ranvier f48a3a7f (createEffectOn) | n/a | -1,110,492.96 (GC in region) | 0 | -1,113,505.76 | n/a | n/a |

## work-value: write every 10th of 1000 rows; the act formats a label, every write changes it (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (createEffect) | n/a | -1,322,333.76 (GC in region) | 0 | -1,325,343.04 | n/a | n/a |
| Ranvier f48a3a7f (createEffectOn) | n/a | -1,171,082.56 (GC in region) | 0 | -1,174,136 | n/a | n/a |

## work-form: write one field of each of 125 8-field forms; validity feeds a signal and an effect (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (createEffect) | n/a | 184,953.12 (GC in region) | 0 | 184,953.12 | n/a | n/a |
| Ranvier f48a3a7f (createEffectOn) | n/a | 140,269.28 (GC in region) | 0 | 140,269.28 | n/a | n/a |

## keyed-remove: alternately remove and restore the middle row of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (fresh items) | n/a | 1,477,588.32 (GC in region) | 0 | 1,477,577.92 | n/a | n/a |
| Ranvier f48a3a7f (stable items) | n/a | 51,856.16 | 0 | 51,792.48 | n/a | n/a |
| Ranvier f48a3a7f (memo-free rows) | n/a | 34,287.52 | 0 | 34,287.52 | n/a | n/a |

## keyed-swap: alternately swap and restore the second and second-to-last rows of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (fresh items) | n/a | 1,488,506.72 (GC in region) | 0 | 1,488,442.56 | n/a | n/a |
| Ranvier f48a3a7f (stable items) | n/a | 36,354.56 | 0 | 36,354.56 | n/a | n/a |
| Ranvier f48a3a7f (memo-free rows) | n/a | 29,457.76 | 0 | 29,457.76 | n/a | n/a |

## keyed-insert: alternately prepend a row and restore of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (fresh items) | n/a | 1,475,110.24 (GC in region) | 0 | 1,475,099.84 | n/a | n/a |
| Ranvier f48a3a7f (stable items) | n/a | 48,393.92 | 0 | 48,330.24 | n/a | n/a |
| Ranvier f48a3a7f (memo-free rows) | n/a | 34,275.36 | 0 | 34,275.36 | n/a | n/a |

## server-render: render a 100-row keyed table as one request (N = 5)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 294,187.20 | 0 | 293,545.60 | n/a | n/a |

## app-table: alternate a filter query and a sort direction over 1000 rows (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | -9,552,223.84 (GC in region) | 0 | -9,553,440.96 | n/a | n/a |

## app-detail: move the selection over 1000 rows; the detail rebuilds 20 memo and effect pairs (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | -164,559.52 (GC in region) | 0 | -164,623.20 | n/a | n/a |

## shape-diamond: write a source read by 100 memos joined by one (N = 5000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | -190,644.24 (GC in region) | 0 | -190,648.51 | n/a | n/a |

## shape-dynamic: alternate a branch flip and a write of every active source of 100 effects (N = 500)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 155,551.31 (GC in region) | 0 | 155,544.94 | n/a | n/a |

## async-resolve: reload 10 suspense widgets of 10 sources, then settle each (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | -113,587.84 (GC in region) | 0 | -113,652 | n/a | n/a |

## async-recover: fail one source of one error-boundary widget, then settle a replacement (N = 500)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 3,443.90 (GC in region) | 0 | 3,443.90 | n/a | n/a |

## stream-burst-1: deliver 1 items per round into a stream fold read by a memo and an effect (N = 500)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (stream, batched) | n/a | 21,177.01 | 0 | 21,177.01 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, batched) | n/a | 20,168.90 | 0 | 20,168.90 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, unbatched) | n/a | 19,284.50 | 0 | 19,284.50 | n/a | n/a |

## stream-burst-16: deliver 16 items per round into a stream fold read by a memo and an effect (N = 500)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (stream, batched) | n/a | 21,177.07 | 0 | 21,177.07 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, batched) | n/a | -23,547.34 (GC in region) | 0 | -23,547.34 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, unbatched) | n/a | 20,451.84 (GC in region) | 0 | 20,451.84 | n/a | n/a |

## stream-burst-256: deliver 256 items per round into a stream fold read by a memo and an effect (N = 20)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (stream, batched) | n/a | 21,012.40 | 0 | 21,012.40 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, batched) | n/a | 308,247.20 | 0 | 308,247.20 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, unbatched) | n/a | -4,834,906.40 (GC in region) | 0 | -4,841,772.40 | n/a | n/a |

## stream-restart: write a dependency of a stream, restarting it, then deliver one item; or of an async memo, restarting its flight (N = 500)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (stream) | n/a | 29,418.38 | 0 | 29,418.38 | n/a | n/a |
| Ranvier f48a3a7f (async memo, CancelPrevious) | n/a | 17,633.06 | 0 | 17,633.06 | n/a | n/a |

## control: refill a 256-entry closure table, sum it, and sort a copy of its array (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Control | n/a | 45,812.80 | 0 | 45,812.80 | n/a | n/a |


