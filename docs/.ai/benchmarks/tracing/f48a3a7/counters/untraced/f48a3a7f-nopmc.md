# Counter bench, f48a3a7f

- Date: 2026-10-24 03:26:47Z
- Machine: AMD64 Family 26 Model 68 Stepping 0, AuthenticAMD, 24 logical processors, Microsoft Windows 10.0.26200, .NET 10.0.13
- Instructions: not collected (--no-pmc)
- Library counters: not collected (built without RanvierCounters)
- Versions: .NET 10.0.13, FSharp.Data.Adaptive 1.2.27, R3 1.3.1, Ranvier f48a3a7f
- Worker environment: DOTNET_TieredCompilation=0 DOTNET_TieredPGO=0 DOTNET_ReadyToRun=0 DOTNET_gcServer=0
- RanvierTrace: false
- Every figure is (m(2N) - m(N)) / N. Processor counters are the median over runs.
- Instruction counts differing by under 5 % are within run-to-run noise. Compare only reports taken at the same --scale.

## Engine differences

- R3 pushes each write straight to its subscribers, without batching or glitch-free ordering. Its chain is `Select` operators holding no cached value.
- FSharp.Data.Adaptive dispose removes the callback subscriptions only. The `AVal.map2` nodes stay in the weak output sets of their `cval`s until collected.

## timed-capture-debounce: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | n/a | n/a |

## timed-admit-debounce: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 1,536 | n/a | n/a |

## timed-capture-first: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | n/a | n/a |

## timed-admit-first: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | n/a | n/a |

## timed-capture-last: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | n/a | n/a |

## timed-admit-last: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 1,536 | n/a | n/a |

## timed-capture-both: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | n/a | n/a |

## timed-admit-both: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 1,536 | n/a | n/a |

## create: one root of 1000 rows (N = 8)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 577,376 | n/a | n/a |
| FSharp.Data.Adaptive 1.2.27 | n/a | 2,413,717 | n/a | n/a |
| R3 1.3.1 | n/a | 584,160 | n/a | n/a |

## update: write every 10th of 1000 rows (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | n/a | n/a |
| FSharp.Data.Adaptive 1.2.27 | n/a | 63,200 | n/a | n/a |
| R3 1.3.1 | n/a | 0 | n/a | n/a |

## chain: write the source of 4 memos, read the tail (N = 5000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | n/a | n/a |
| FSharp.Data.Adaptive 1.2.27 | n/a | 472 | n/a | n/a |
| R3 1.3.1 | n/a | 0 | n/a | n/a |

## cutoff: write an equal value to an observed source (N = 50000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | n/a | n/a |
| FSharp.Data.Adaptive 1.2.27 | n/a | 352 | n/a | n/a |
| R3 1.3.1 | n/a | 0 | n/a | n/a |

## dispose: dispose one root of 1000 rows (N = 8)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 56 | n/a | n/a |
| FSharp.Data.Adaptive 1.2.27 | n/a | 0 | n/a | n/a |
| R3 1.3.1 | n/a | 0 | n/a | n/a |

## create-on: one root of 1000 createEffectOn rows (N = 8)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 489,520 | n/a | n/a |

## update-on: write every 10th of 1000 createEffectOn rows (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | n/a | n/a |

## dispose-on: dispose one root of 1000 createEffectOn rows (N = 8)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 56 | n/a | n/a |

## derive-effect: write a new value whose derived value is unchanged, Effect (N = 50000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | n/a | n/a |

## derive-on: write a new value whose derived value is unchanged, createEffectOn (N = 50000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | n/a | n/a |

## work-status: write every 10th of 1000 rows; the act formats a label, 1 write in 10 changes it (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (createEffect) | n/a | 3,980 | n/a | n/a |
| Ranvier f48a3a7f (createEffectOn) | n/a | 398.40 | n/a | n/a |

## work-value: write every 10th of 1000 rows; the act formats a label, every write changes it (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (createEffect) | n/a | 6,467.20 | n/a | n/a |
| Ranvier f48a3a7f (createEffectOn) | n/a | 6,467.20 | n/a | n/a |

## work-form: write one field of each of 125 8-field forms; validity feeds a signal and an effect (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (createEffect) | n/a | 0 | n/a | n/a |
| Ranvier f48a3a7f (createEffectOn) | n/a | 0 | n/a | n/a |

## project-edit: change one row of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 112 | n/a | n/a |

## project-reorder: reverse a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 4,136 | n/a | n/a |

## keyed-remove: alternately remove and restore the middle row of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (fresh items) | n/a | 4,428 | n/a | n/a |
| Ranvier f48a3a7f (stable items) | n/a | 4,428 | n/a | n/a |
| Ranvier f48a3a7f (memo-free rows) | n/a | 4,500 | n/a | n/a |

## keyed-swap: alternately swap and restore the second and second-to-last rows of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (fresh items) | n/a | 4,136 | n/a | n/a |
| Ranvier f48a3a7f (stable items) | n/a | 4,136 | n/a | n/a |
| Ranvier f48a3a7f (memo-free rows) | n/a | 4,160 | n/a | n/a |

## keyed-insert: alternately prepend a row and restore of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (fresh items) | n/a | 4,432 | n/a | n/a |
| Ranvier f48a3a7f (stable items) | n/a | 4,432 | n/a | n/a |
| Ranvier f48a3a7f (memo-free rows) | n/a | 4,504 | n/a | n/a |

## server-render: render a 100-row keyed table as one request (N = 5)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 200,440 | n/a | n/a |

## project-chain: change one row of a 1000-row filter, sortBy, map chain (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 2,528 | n/a | n/a |

## app-table: alternate a filter query and a sort direction over 1000 rows (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 78,476 | n/a | n/a |

## app-detail: move the selection over 1000 rows; the detail rebuilds 20 memo and effect pairs (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 12,952 | n/a | n/a |

## shape-diamond: write a source read by 100 memos joined by one (N = 5000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | n/a | n/a |

## shape-dynamic: alternate a branch flip and a write of every active source of 100 effects (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | n/a | n/a |

## async-resolve: reload 10 suspense widgets of 10 sources, then settle each (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 68,608 | n/a | n/a |

## async-recover: fail one source of one error-boundary widget, then settle a replacement (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 180,374.29 | n/a | n/a |

## stream-burst-1: deliver 1 items per round into a stream fold read by a memo and an effect (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (stream, batched) | n/a | 120 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, batched) | n/a | 160 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, unbatched) | n/a | 32 | n/a | n/a |

## stream-burst-16: deliver 16 items per round into a stream fold read by a memo and an effect (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (stream, batched) | n/a | 120 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, batched) | n/a | 640 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, unbatched) | n/a | 512 | n/a | n/a |

## stream-burst-256: deliver 256 items per round into a stream fold read by a memo and an effect (N = 20)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (stream, batched) | n/a | 120 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, batched) | n/a | 8,320 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, unbatched) | n/a | 8,192 | n/a | n/a |

## stream-restart: write a dependency of a stream, restarting it, then deliver one item; or of an async memo, restarting its flight (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (stream) | n/a | 608 | n/a | n/a |
| Ranvier f48a3a7f (async memo, CancelPrevious) | n/a | 760 | n/a | n/a |

## chain-affinity: write the source of 4 memos, read the tail, per thread affinity (N = 5000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (Guarded) | n/a | 0 | n/a | n/a |
| Ranvier f48a3a7f (Unchecked) | n/a | 0 | n/a | n/a |
| Ranvier f48a3a7f (Serialised) | n/a | 0 | n/a | n/a |

## project-churn: replace the last key of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (no reader) | n/a | 4,616 | n/a | n/a |
| Ranvier f48a3a7f (key reader) | n/a | 5,016 | n/a | n/a |

## flight: write an async memo's trigger twice, then settle every flight the writes started (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (CancelPrevious) | n/a | 1,720 | n/a | n/a |
| Ranvier f48a3a7f (KeepLatest) | n/a | 1,672 | n/a | n/a |
| Ranvier f48a3a7f (Queue) | n/a | 3,184 | n/a | n/a |
| Ranvier f48a3a7f (FinishCurrent) | n/a | 1,744 | n/a | n/a |
| Ranvier f48a3a7f (Drop) | n/a | 872 | n/a | n/a |
| Ranvier f48a3a7f (DropWhilePending) | n/a | 872 | n/a | n/a |

## fail-recompute: re-run a memo and its one reader, then read the reader's failure origin (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (succeeding) | n/a | 0 | n/a | n/a |
| Ranvier f48a3a7f (failing) | n/a | 1,576 | n/a | n/a |

## editable-edit: edit every 10th of 1000 editables (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | n/a | n/a |

## editable-upstream: write the seed source of every 10th of 1000 editables (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 2,400 | n/a | n/a |

## mvu-dispatch: change one field of a 64-field model, one reader per field (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (signal per field) | n/a | 0 | n/a | n/a |
| Ranvier f48a3a7f (Mvu) | n/a | 400 | n/a | n/a |

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
| Ranvier f48a3a7f | n/a | -0.25 | 0 | -3.67 | n/a | n/a |

## timed-admit-debounce: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 3,584.66 | 0 | 3,581.48 | n/a | n/a |

## timed-capture-first: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | 0 | -3.21 | n/a | n/a |

## timed-admit-first: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | 0 | 0 | n/a | n/a |

## timed-capture-last: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0.24 | 0 | 0.24 | n/a | n/a |

## timed-admit-last: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 3,584.69 | 0 | 3,584.69 | n/a | n/a |

## timed-capture-both: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | 0 | 0 | n/a | n/a |

## timed-admit-both: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 3,584.58 | 0 | 3,584.58 | n/a | n/a |

## create: one root of 1000 rows (N = 8)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 1,026,871 | 0 | 1,026,871 | n/a | n/a |
| Fable.Ripple 1.0.0-beta.5 | n/a | 1,323,301 | 0 | 1,323,301 | n/a | n/a |

## update: write every 10th of 1000 rows (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 7,216.80 | 0 | 7,216.80 | n/a | n/a |
| Fable.Ripple 1.0.0-beta.5 | n/a | 22,418.24 | 0 | 22,418.24 | n/a | n/a |

## chain: write the source of 4 memos, read the tail (N = 5000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | 0 | 0 | n/a | n/a |
| Fable.Ripple 1.0.0-beta.5 | n/a | 0 | 0 | 0 | n/a | n/a |

## cutoff: write an equal value to an observed source (N = 50000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | 0 | 0 | n/a | n/a |
| Fable.Ripple 1.0.0-beta.5 | n/a | 0 | 0 | 0 | n/a | n/a |

## dispose: dispose one root of 1000 rows (N = 8)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 200 | 0 | 200 | n/a | n/a |
| Fable.Ripple 1.0.0-beta.5 | n/a | 29,503 | 0 | 29,503 | n/a | n/a |

## create-on: one root of 1000 createEffectOn rows (N = 8)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 899,173 | 0 | 899,173 | n/a | n/a |

## update-on: write every 10th of 1000 createEffectOn rows (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 7,216.80 | 0 | 7,216.80 | n/a | n/a |

## dispose-on: dispose one root of 1000 createEffectOn rows (N = 8)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 200 | 0 | 200 | n/a | n/a |

## derive-effect: write a new value whose derived value is unchanged, Effect (N = 50000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | 0 | 0 | n/a | n/a |

## derive-on: write a new value whose derived value is unchanged, createEffectOn (N = 50000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | 0 | 0 | n/a | n/a |

## work-status: write every 10th of 1000 rows; the act formats a label, 1 write in 10 changes it (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (createEffect) | n/a | 32,024.48 | 0 | 32,024.48 | n/a | n/a |
| Ranvier f48a3a7f (createEffectOn) | n/a | 9,697.76 | 0 | 9,697.76 | n/a | n/a |

## work-value: write every 10th of 1000 rows; the act formats a label, every write changes it (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (createEffect) | n/a | 32,024.48 | 0 | 32,024.48 | n/a | n/a |
| Ranvier f48a3a7f (createEffectOn) | n/a | 32,024.48 | 0 | 32,024.48 | n/a | n/a |

## work-form: write one field of each of 125 8-field forms; validity feeds a signal and an effect (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (createEffect) | n/a | 0 | 0 | 0 | n/a | n/a |
| Ranvier f48a3a7f (createEffectOn) | n/a | 0 | 0 | 0 | n/a | n/a |

## keyed-remove: alternately remove and restore the middle row of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (fresh items) | n/a | 333,404.48 | 0 | 333,404.48 | n/a | n/a |
| Ranvier f48a3a7f (stable items) | n/a | 10,484.16 | 0 | 10,484.16 | n/a | n/a |
| Ranvier f48a3a7f (memo-free rows) | n/a | 10,595.36 | 0 | 10,595.36 | n/a | n/a |

## keyed-swap: alternately swap and restore the second and second-to-last rows of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (fresh items) | n/a | 361,855.04 | 0 | 361,855.04 | n/a | n/a |
| Ranvier f48a3a7f (stable items) | n/a | 10,604.96 | 0 | 10,604.96 | n/a | n/a |
| Ranvier f48a3a7f (memo-free rows) | n/a | 10,604.32 | 0 | 10,604.32 | n/a | n/a |

## keyed-insert: alternately prepend a row and restore of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (fresh items) | n/a | 333,677.44 | 0 | 333,677.44 | n/a | n/a |
| Ranvier f48a3a7f (stable items) | n/a | 10,401.76 | 0 | 10,401.92 | n/a | n/a |
| Ranvier f48a3a7f (memo-free rows) | n/a | 10,536.96 | 0 | 10,536.96 | n/a | n/a |

## server-render: render a 100-row keyed table as one request (N = 5)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 196,283.20 | 0 | 196,283.20 | n/a | n/a |

## app-table: alternate a filter query and a sort direction over 1000 rows (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 258,203.04 (GC in region) | 0 | 258,203.04 | n/a | n/a |

## app-detail: move the selection over 1000 rows; the detail rebuilds 20 memo and effect pairs (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 27,168.80 | 0 | 27,168.80 | n/a | n/a |

## shape-diamond: write a source read by 100 memos joined by one (N = 5000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | 0 | 0 | n/a | n/a |

## shape-dynamic: alternate a branch flip and a write of every active source of 100 effects (N = 500)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 0 | 0 | 0 | n/a | n/a |

## async-resolve: reload 10 suspense widgets of 10 sources, then settle each (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 153,209.12 | 0 | 153,209.12 | n/a | n/a |

## async-recover: fail one source of one error-boundary widget, then settle a replacement (N = 500)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f | n/a | 4,224.75 | 0 | 4,224.75 | n/a | n/a |

## stream-burst-1: deliver 1 items per round into a stream fold read by a memo and an effect (N = 500)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (stream, batched) | n/a | 296.08 | 0 | 296.08 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, batched) | n/a | 400.05 | 0 | 400.05 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, unbatched) | n/a | 344.08 | 0 | 344.08 | n/a | n/a |

## stream-burst-16: deliver 16 items per round into a stream fold read by a memo and an effect (N = 500)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (stream, batched) | n/a | 296.08 | 0 | 296.08 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, batched) | n/a | 1,960.19 | 0 | 1,960.19 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, unbatched) | n/a | 5,504.13 | 0 | 5,504.13 | n/a | n/a |

## stream-burst-256: deliver 256 items per round into a stream fold read by a memo and an effect (N = 20)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (stream, batched) | n/a | 296 | 0 | 296 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, batched) | n/a | 26,923.20 | 0 | 26,923.20 | n/a | n/a |
| Ranvier f48a3a7f (AsyncSource, unbatched) | n/a | 88,065.20 | 0 | 88,065.20 | n/a | n/a |

## stream-restart: write a dependency of a stream, restarting it, then deliver one item; or of an async memo, restarting its flight (N = 500)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier f48a3a7f (stream) | n/a | 1,768.03 | 0 | 1,768.03 | n/a | n/a |
| Ranvier f48a3a7f (async memo, CancelPrevious) | n/a | 1,215.98 | 0 | 1,215.98 | n/a | n/a |

## control: refill a 256-entry closure table, sum it, and sort a copy of its array (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Control | n/a | 45,812.80 | 0 | 45,812.80 | n/a | n/a |


