# Counter bench, b7863731-dirty

- Date: 2026-10-24 06:21:35Z
- Machine: AMD64 Family 26 Model 68 Stepping 0, AuthenticAMD, 24 logical processors, Microsoft Windows 10.0.26200, .NET 10.0.13
- Instructions: not collected (--no-pmc)
- Library counters: from the measured build
- Versions: .NET 10.0.13, FSharp.Data.Adaptive 1.2.27, R3 1.3.1, Ranvier b7863731-dirty
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
| Ranvier b7863731-dirty | n/a | 0 | 0 | MemoRecomputes 64, Flushes 1 |

## timed-admit-debounce: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 1,536 | 0 | MemoRecomputes 64, EffectRuns 64, Flushes 65 |

## timed-capture-first: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | MemoRecomputes 64, Flushes 1 |

## timed-admit-first: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | MemoRecomputes 64, EffectRuns 64, Flushes 1 |

## timed-capture-last: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | MemoRecomputes 64, Flushes 1 |

## timed-admit-last: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 1,536 | 0 | MemoRecomputes 64, EffectRuns 64, Flushes 65 |

## timed-capture-both: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | MemoRecomputes 64, Flushes 1 |

## timed-admit-both: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 1,536 | 0 | MemoRecomputes 64, EffectRuns 64, Flushes 65 |

## create: one root of 1000 rows (N = 8)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 577,376 | 2,002 | SignalsCreated 1,001, EffectsCreated 1,000, OwnersCreated 1, EdgesAdded 2,000, ObserverInserts 2,000, EffectRuns 1,000, Flushes 1,000 |
| FSharp.Data.Adaptive 1.2.27 | n/a | 2,413,717 | n/a | n/a |
| R3 1.3.1 | n/a | 584,160 | n/a | n/a |

## update: write every 10th of 1000 rows (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | EffectRuns 100, Flushes 100 |
| FSharp.Data.Adaptive 1.2.27 | n/a | 63,200 | n/a | n/a |
| R3 1.3.1 | n/a | 0 | n/a | n/a |

## chain: write the source of 4 memos, read the tail (N = 5000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | MemoRecomputes 4 |
| FSharp.Data.Adaptive 1.2.27 | n/a | 472 | n/a | n/a |
| R3 1.3.1 | n/a | 0 | n/a | n/a |

## cutoff: write an equal value to an observed source (N = 50000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | 0 |
| FSharp.Data.Adaptive 1.2.27 | n/a | 352 | n/a | n/a |
| R3 1.3.1 | n/a | 0 | n/a | n/a |

## dispose: dispose one root of 1000 rows (N = 8)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 56 | 0 | EdgesRemoved 2,000, ObserverRemoves 2,000 |
| FSharp.Data.Adaptive 1.2.27 | n/a | 0 | n/a | n/a |
| R3 1.3.1 | n/a | 0 | n/a | n/a |

## create-on: one root of 1000 createEffectOn rows (N = 8)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 489,520 | 2,002 | SignalsCreated 1,001, EffectsCreated 1,000, OwnersCreated 1, EdgesAdded 2,000, ObserverInserts 2,000, EffectRuns 1,000, Flushes 1,000 |

## update-on: write every 10th of 1000 createEffectOn rows (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | EffectRuns 100, Flushes 100 |

## dispose-on: dispose one root of 1000 createEffectOn rows (N = 8)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 56 | 0 | EdgesRemoved 2,000, ObserverRemoves 2,000 |

## derive-effect: write a new value whose derived value is unchanged, Effect (N = 50000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | EffectRuns 1, Flushes 1 |

## derive-on: write a new value whose derived value is unchanged, createEffectOn (N = 50000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | EffectRuns 1, Flushes 1 |

## work-status: write every 10th of 1000 rows; the act formats a label, 1 write in 10 changes it (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (createEffect) | n/a | 3,980 | 0 | EffectRuns 100, Flushes 100 |
| Ranvier b7863731-dirty (createEffectOn) | n/a | 398.40 | 0 | EffectRuns 100, Flushes 100 |

## work-value: write every 10th of 1000 rows; the act formats a label, every write changes it (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (createEffect) | n/a | 6,467.20 | 0 | EffectRuns 100, Flushes 100 |
| Ranvier b7863731-dirty (createEffectOn) | n/a | 6,467.20 | 0 | EffectRuns 100, Flushes 100 |

## work-form: write one field of each of 125 8-field forms; validity feeds a signal and an effect (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (createEffect) | n/a | 0 | 0 | EffectRuns 126.02, Flushes 125 |
| Ranvier b7863731-dirty (createEffectOn) | n/a | 0 | 0 | EffectRuns 126.02, Flushes 125 |

## project-edit: change one row of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 112 | 0 | MemoRecomputes 1, EffectRuns 1, Flushes 1 |

## project-reorder: reverse a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 4,136 | 0 | Flushes 1 |

## keyed-remove: alternately remove and restore the middle row of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (fresh items) | n/a | 4,428 | 1 | SignalsCreated 0.50, MemosCreated 0.50, EdgesAdded 1.50, EdgesRemoved 1.50, ObserverInserts 1.50, ObserverRemoves 1.50, MemoRecomputes 999.50, EffectRuns 3, Flushes 1 |
| Ranvier b7863731-dirty (stable items) | n/a | 4,428 | 1 | SignalsCreated 0.50, MemosCreated 0.50, EdgesAdded 1.50, EdgesRemoved 1.50, ObserverInserts 1.50, ObserverRemoves 1.50, MemoRecomputes 0.50, EffectRuns 3, Flushes 1 |
| Ranvier b7863731-dirty (memo-free rows) | n/a | 4,500 | 1.50 | SignalsCreated 0.50, EffectsCreated 0.50, OwnersCreated 0.50, EdgesAdded 0.50, EdgesRemoved 0.50, ObserverInserts 0.50, ObserverRemoves 0.50, EffectRuns 1.50, Flushes 1, OnceRowsCreated 0.50 |

## keyed-swap: alternately swap and restore the second and second-to-last rows of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (fresh items) | n/a | 4,136 | 0 | MemoRecomputes 1,000, EffectRuns 2, Flushes 1 |
| Ranvier b7863731-dirty (stable items) | n/a | 4,136 | 0 | EffectRuns 2, Flushes 1 |
| Ranvier b7863731-dirty (memo-free rows) | n/a | 4,160 | 0 | EffectRuns 1, Flushes 1 |

## keyed-insert: alternately prepend a row and restore of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (fresh items) | n/a | 4,432 | 1 | SignalsCreated 0.50, MemosCreated 0.50, EdgesAdded 1.50, EdgesRemoved 1.50, ObserverInserts 1.50, ObserverRemoves 1.50, MemoRecomputes 1,000.50, EffectRuns 2, Flushes 1 |
| Ranvier b7863731-dirty (stable items) | n/a | 4,432 | 1 | SignalsCreated 0.50, MemosCreated 0.50, EdgesAdded 1.50, EdgesRemoved 1.50, ObserverInserts 1.50, ObserverRemoves 1.50, MemoRecomputes 0.50, EffectRuns 2, Flushes 1 |
| Ranvier b7863731-dirty (memo-free rows) | n/a | 4,504 | 1.50 | SignalsCreated 0.50, EffectsCreated 0.50, OwnersCreated 0.50, EdgesAdded 0.50, EdgesRemoved 0.50, ObserverInserts 0.50, ObserverRemoves 0.50, EffectRuns 1.50, Flushes 1, OnceRowsCreated 0.50 |

## server-render: render a 100-row keyed table as one request (N = 5)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 200,440 | 103 | OwnersCreated 103 |

## project-chain: change one row of a 1000-row filter, sortBy, map chain (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 2,528 | 0 | MemoRecomputes 6, EffectRuns 2, Flushes 1 |

## app-table: alternate a filter query and a sort direction over 1000 rows (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 78,476 | 240 | SignalsCreated 120, MemosCreated 120, EdgesAdded 760.50, EdgesRemoved 760.50, ObserverInserts 760.50, ObserverRemoves 760.50, MemoRecomputes 980, EffectRuns 1, Flushes 1 |

## app-detail: move the selection over 1000 rows; the detail rebuilds 20 memo and effect pairs (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 12,952 | 40 | MemosCreated 20, EffectsCreated 20, EdgesAdded 41, EdgesRemoved 41, ObserverInserts 41, ObserverRemoves 41, MemoRecomputes 21, EffectRuns 24, Flushes 1 |

## shape-diamond: write a source read by 100 memos joined by one (N = 5000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | MemoRecomputes 101, EffectRuns 1, Flushes 1 |

## shape-dynamic: alternate a branch flip and a write of every active source of 100 effects (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | EdgesAdded 50, EdgesRemoved 50, ObserverInserts 50, ObserverRemoves 50, EffectRuns 100, Flushes 50.50 |

## async-resolve: reload 10 suspense widgets of 10 sources, then settle each (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 68,608 | 0 | EdgesAdded 190, EdgesRemoved 190, ObserverInserts 190, ObserverRemoves 190, EffectRuns 20, Flushes 101 |

## async-recover: fail one source of one error-boundary widget, then settle a replacement (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 180,374.29 | 0 | EdgesAdded 20, EdgesRemoved 20, ObserverInserts 20, ObserverRemoves 20, EffectRuns 4, Flushes 4 |

## stream-burst-1: deliver 1 items per round into a stream fold read by a memo and an effect (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (stream, batched) | n/a | 120 | 0 | MemoRecomputes 1, EffectRuns 1, Flushes 1 |
| Ranvier b7863731-dirty (AsyncSource, batched) | n/a | 160 | 0 | MemoRecomputes 1, EffectRuns 1, Flushes 1 |
| Ranvier b7863731-dirty (AsyncSource, unbatched) | n/a | 32 | 0 | MemoRecomputes 1, EffectRuns 1, Flushes 1 |

## stream-burst-16: deliver 16 items per round into a stream fold read by a memo and an effect (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (stream, batched) | n/a | 120 | 0 | MemoRecomputes 1, EffectRuns 1, Flushes 1 |
| Ranvier b7863731-dirty (AsyncSource, batched) | n/a | 640 | 0 | MemoRecomputes 1, EffectRuns 1, Flushes 1 |
| Ranvier b7863731-dirty (AsyncSource, unbatched) | n/a | 512 | 0 | MemoRecomputes 16, EffectRuns 16, Flushes 16 |

## stream-burst-256: deliver 256 items per round into a stream fold read by a memo and an effect (N = 20)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (stream, batched) | n/a | 120 | 0 | MemoRecomputes 1, EffectRuns 1, Flushes 1 |
| Ranvier b7863731-dirty (AsyncSource, batched) | n/a | 8,320 | 0 | MemoRecomputes 1, EffectRuns 1, Flushes 1 |
| Ranvier b7863731-dirty (AsyncSource, unbatched) | n/a | 8,192 | 0 | MemoRecomputes 256, EffectRuns 256, Flushes 256 |

## stream-restart: write a dependency of a stream, restarting it, then deliver one item; or of an async memo, restarting its flight (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (stream) | n/a | 608 | 0 | EffectRuns 2, Flushes 2 |
| Ranvier b7863731-dirty (async memo, CancelPrevious) | n/a | 760 | 0 | EffectRuns 1, Flushes 1 |

## chain-affinity: write the source of 4 memos, read the tail, per thread affinity (N = 5000)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (Guarded) | n/a | 0 | 0 | MemoRecomputes 4 |
| Ranvier b7863731-dirty (Unchecked) | n/a | 0 | 0 | MemoRecomputes 4 |
| Ranvier b7863731-dirty (Serialised) | n/a | 0 | 0 | MemoRecomputes 4 |

## project-churn: replace the last key of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (no reader) | n/a | 4,616 | 2 | SignalsCreated 1, MemosCreated 1, EffectRuns 1, Flushes 1 |
| Ranvier b7863731-dirty (key reader) | n/a | 5,016 | 2 | SignalsCreated 1, MemosCreated 1, EffectRuns 2, Flushes 1 |

## flight: write an async memo's trigger twice, then settle every flight the writes started (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (CancelPrevious) | n/a | 1,720 | 0 | 0 |
| Ranvier b7863731-dirty (KeepLatest) | n/a | 1,672 | 0 | 0 |
| Ranvier b7863731-dirty (Queue) | n/a | 3,184 | 0 | 0 |
| Ranvier b7863731-dirty (FinishCurrent) | n/a | 1,744 | 0 | 0 |
| Ranvier b7863731-dirty (Drop) | n/a | 872 | 0 | 0 |
| Ranvier b7863731-dirty (DropWhilePending) | n/a | 872 | 0 | 0 |

## fail-recompute: re-run a memo and its one reader, then read the reader's failure origin (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (succeeding) | n/a | 0 | 0 | MemoRecomputes 2 |
| Ranvier b7863731-dirty (failing) | n/a | 1,576 | 0 | MemoRecomputes 2 |

## editable-edit: edit every 10th of 1000 editables (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | MemoRecomputes 100, EffectRuns 100, Flushes 100 |

## editable-upstream: write the seed source of every 10th of 1000 editables (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 2,400 | 0 | MemoRecomputes 200, EffectRuns 100, Flushes 100 |

## mvu-dispatch: change one field of a 64-field model, one reader per field (N = 500)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (signal per field) | n/a | 0 | 0 | EffectRuns 1, Flushes 1 |
| Ranvier b7863731-dirty (Mvu) | n/a | 400 | 0 | MemoRecomputes 64, EffectRuns 1, Flushes 1 |

## control: refill a 256-entry closure table, sum it, and sort a copy of its array (N = 50)

| Engine | instr/op | bytes/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | --- |
| Control | n/a | 1,048 | n/a | n/a |

## Calibration, 5 run(s)

Allocations and library counters identical in every run.

# Fable under Node.js v26.7.0

- Versions: Fable.Ripple 1.0.0-beta.5, Node.js v26.7.0, Ranvier b7863731-dirty, Xantham.Fable.Core 0.2.0, fable-library-js 5.23.0
- node flags: --expose-gc --single-threaded --min-semi-space-size=32 --max-semi-space-size=32
- Instructions: not collected (--no-pmc)
- bytes/op: growth of the new, old and large-object heap spaces; median of 5 processes. bytes range: minimum..maximum, 0 when every process agreed.
- heapUsed/op: growth of `process.memoryUsage().heapUsed`, which adds the code and trusted spaces.
- Every figure is (m(2N) - m(N)) / N. A figure followed by a bracketed range differed between processes.

## timed-capture-debounce: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | -0.25 | 0 | -3.67 | 0 | MemoRecomputes 64, Flushes 1 |

## timed-admit-debounce: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 3,584.66 | 0 | 3,581.48 | 0 | MemoRecomputes 64, EffectRuns 64, Flushes 65 |

## timed-capture-first: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | -3.21 | 0 | MemoRecomputes 64, Flushes 1 |

## timed-admit-first: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | 0 | 0 | MemoRecomputes 64, EffectRuns 64, Flushes 1 |

## timed-capture-last: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0.24 | 0 | 0.24 | 0 | MemoRecomputes 64, Flushes 1 |

## timed-admit-last: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 3,584.69 | 0 | 3,584.69 | 0 | MemoRecomputes 64, EffectRuns 64, Flushes 65 |

## timed-capture-both: capture a changed input in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | 0 | 0 | MemoRecomputes 64, Flushes 1 |

## timed-admit-both: capture and admit in 64 timed nodes (N = 1000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 3,584.58 | 0 | 3,584.58 | 0 | MemoRecomputes 64, EffectRuns 64, Flushes 65 |

## create: one root of 1000 rows (N = 8)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 1,026,871 | 0 | 1,026,871 | 2,002 | SignalsCreated 1,001, EffectsCreated 1,000, OwnersCreated 1, EdgesAdded 2,000, ObserverInserts 2,000, EffectRuns 1,000, Flushes 1,000 |
| Fable.Ripple 1.0.0-beta.5 | n/a | 1,323,301 | 0 | 1,323,301 | n/a | n/a |

## update: write every 10th of 1000 rows (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 7,216.80 | 0 | 7,216.80 | 0 | EffectRuns 100, Flushes 100 |
| Fable.Ripple 1.0.0-beta.5 | n/a | 22,418.24 | 0 | 22,418.24 | n/a | n/a |

## chain: write the source of 4 memos, read the tail (N = 5000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | 0 | 0 | MemoRecomputes 4 |
| Fable.Ripple 1.0.0-beta.5 | n/a | 0 | 0 | 0 | n/a | n/a |

## cutoff: write an equal value to an observed source (N = 50000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | 0 | 0 | 0 |
| Fable.Ripple 1.0.0-beta.5 | n/a | 0 | 0 | 0 | n/a | n/a |

## dispose: dispose one root of 1000 rows (N = 8)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 200 | 0 | 200 | 0 | EdgesRemoved 2,000, ObserverRemoves 2,000 |
| Fable.Ripple 1.0.0-beta.5 | n/a | 29,503 | 0 | 29,503 | n/a | n/a |

## create-on: one root of 1000 createEffectOn rows (N = 8)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 899,173 | 0 | 899,173 | 2,002 | SignalsCreated 1,001, EffectsCreated 1,000, OwnersCreated 1, EdgesAdded 2,000, ObserverInserts 2,000, EffectRuns 1,000, Flushes 1,000 |

## update-on: write every 10th of 1000 createEffectOn rows (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 7,216.80 | 0 | 7,216.80 | 0 | EffectRuns 100, Flushes 100 |

## dispose-on: dispose one root of 1000 createEffectOn rows (N = 8)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 200 | 0 | 200 | 0 | EdgesRemoved 2,000, ObserverRemoves 2,000 |

## derive-effect: write a new value whose derived value is unchanged, Effect (N = 50000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | 0 | 0 | EffectRuns 1, Flushes 1 |

## derive-on: write a new value whose derived value is unchanged, createEffectOn (N = 50000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | 0 | 0 | EffectRuns 1, Flushes 1 |

## work-status: write every 10th of 1000 rows; the act formats a label, 1 write in 10 changes it (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (createEffect) | n/a | 32,024.48 | 0 | 32,024.48 | 0 | EffectRuns 100, Flushes 100 |
| Ranvier b7863731-dirty (createEffectOn) | n/a | 9,697.76 | 0 | 9,697.76 | 0 | EffectRuns 100, Flushes 100 |

## work-value: write every 10th of 1000 rows; the act formats a label, every write changes it (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (createEffect) | n/a | 32,024.48 | 0 | 32,024.48 | 0 | EffectRuns 100, Flushes 100 |
| Ranvier b7863731-dirty (createEffectOn) | n/a | 32,024.48 | 0 | 32,024.48 | 0 | EffectRuns 100, Flushes 100 |

## work-form: write one field of each of 125 8-field forms; validity feeds a signal and an effect (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (createEffect) | n/a | 0 | 0 | 0 | 0 | EffectRuns 126.02, Flushes 125 |
| Ranvier b7863731-dirty (createEffectOn) | n/a | 0 | 0 | 0 | 0 | EffectRuns 126.02, Flushes 125 |

## keyed-remove: alternately remove and restore the middle row of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (fresh items) | n/a | 333,404.48 | 0 | 333,404.48 | 1 | SignalsCreated 0.50, MemosCreated 0.50, EdgesAdded 1.50, EdgesRemoved 1.50, ObserverInserts 1.50, ObserverRemoves 1.50, MemoRecomputes 999.50, EffectRuns 3, Flushes 1 |
| Ranvier b7863731-dirty (stable items) | n/a | 10,484.16 | 0 | 10,484.16 | 1 | SignalsCreated 0.50, MemosCreated 0.50, EdgesAdded 1.50, EdgesRemoved 1.50, ObserverInserts 1.50, ObserverRemoves 1.50, MemoRecomputes 0.50, EffectRuns 3, Flushes 1 |
| Ranvier b7863731-dirty (memo-free rows) | n/a | 10,595.36 | 0 | 10,595.36 | 1.50 | SignalsCreated 0.50, EffectsCreated 0.50, OwnersCreated 0.50, EdgesAdded 0.50, EdgesRemoved 0.50, ObserverInserts 0.50, ObserverRemoves 0.50, EffectRuns 1.50, Flushes 1, OnceRowsCreated 0.50 |

## keyed-swap: alternately swap and restore the second and second-to-last rows of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (fresh items) | n/a | 361,855.04 | 0 | 361,855.04 | 0 | MemoRecomputes 1,000, EffectRuns 2, Flushes 1 |
| Ranvier b7863731-dirty (stable items) | n/a | 10,604.96 | 0 | 10,604.96 | 0 | EffectRuns 2, Flushes 1 |
| Ranvier b7863731-dirty (memo-free rows) | n/a | 10,604.32 | 0 | 10,604.32 | 0 | EffectRuns 1, Flushes 1 |

## keyed-insert: alternately prepend a row and restore of a 1000-row projection (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (fresh items) | n/a | 333,677.44 | 0 | 333,677.44 | 1 | SignalsCreated 0.50, MemosCreated 0.50, EdgesAdded 1.50, EdgesRemoved 1.50, ObserverInserts 1.50, ObserverRemoves 1.50, MemoRecomputes 1,000.50, EffectRuns 2, Flushes 1 |
| Ranvier b7863731-dirty (stable items) | n/a | 10,401.76 | 0 | 10,401.92 | 1 | SignalsCreated 0.50, MemosCreated 0.50, EdgesAdded 1.50, EdgesRemoved 1.50, ObserverInserts 1.50, ObserverRemoves 1.50, MemoRecomputes 0.50, EffectRuns 2, Flushes 1 |
| Ranvier b7863731-dirty (memo-free rows) | n/a | 10,536.96 | 0 | 10,536.96 | 1.50 | SignalsCreated 0.50, EffectsCreated 0.50, OwnersCreated 0.50, EdgesAdded 0.50, EdgesRemoved 0.50, ObserverInserts 0.50, ObserverRemoves 0.50, EffectRuns 1.50, Flushes 1, OnceRowsCreated 0.50 |

## server-render: render a 100-row keyed table as one request (N = 5)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 196,283.20 | 0 | 196,283.20 | 103 | OwnersCreated 103 |

## app-table: alternate a filter query and a sort direction over 1000 rows (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 258,203.04 (GC in region) | 258,055.20..258,203.04 | 258,203.04 (258,055.20..258,203.04) | 240 | SignalsCreated 120, MemosCreated 120, EdgesAdded 760.50, EdgesRemoved 760.50, ObserverInserts 760.50, ObserverRemoves 760.50, MemoRecomputes 980, EffectRuns 1, Flushes 1 |

## app-detail: move the selection over 1000 rows; the detail rebuilds 20 memo and effect pairs (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 27,168.80 | 0 | 27,168.80 | 40 | MemosCreated 20, EffectsCreated 20, EdgesAdded 41, EdgesRemoved 41, ObserverInserts 41, ObserverRemoves 41, MemoRecomputes 21, EffectRuns 24, Flushes 1 |

## shape-diamond: write a source read by 100 memos joined by one (N = 5000)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | 0 | 0 | MemoRecomputes 101, EffectRuns 1, Flushes 1 |

## shape-dynamic: alternate a branch flip and a write of every active source of 100 effects (N = 500)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 0 | 0 | 0 | 0 | EdgesAdded 50, EdgesRemoved 50, ObserverInserts 50, ObserverRemoves 50, EffectRuns 100, Flushes 50.50 |

## async-resolve: reload 10 suspense widgets of 10 sources, then settle each (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 153,209.12 | 0 | 153,209.12 | 0 | EdgesAdded 190, EdgesRemoved 190, ObserverInserts 190, ObserverRemoves 190, EffectRuns 20, Flushes 101 |

## async-recover: fail one source of one error-boundary widget, then settle a replacement (N = 500)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty | n/a | 4,224.75 | 0 | 4,224.75 | 0 | EdgesAdded 20, EdgesRemoved 20, ObserverInserts 20, ObserverRemoves 20, EffectRuns 4, Flushes 4 |

## stream-burst-1: deliver 1 items per round into a stream fold read by a memo and an effect (N = 500)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (stream, batched) | n/a | 296.08 | 0 | 296.08 | 0 | MemoRecomputes 1, EffectRuns 1, Flushes 1 |
| Ranvier b7863731-dirty (AsyncSource, batched) | n/a | 400.05 | 0 | 400.05 | 0 | MemoRecomputes 1, EffectRuns 1, Flushes 1 |
| Ranvier b7863731-dirty (AsyncSource, unbatched) | n/a | 344.08 | 0 | 344.08 | 0 | MemoRecomputes 1, EffectRuns 1, Flushes 1 |

## stream-burst-16: deliver 16 items per round into a stream fold read by a memo and an effect (N = 500)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (stream, batched) | n/a | 296.08 | 0 | 296.08 | 0 | MemoRecomputes 1, EffectRuns 1, Flushes 1 |
| Ranvier b7863731-dirty (AsyncSource, batched) | n/a | 1,960.19 | 0 | 1,960.19 | 0 | MemoRecomputes 1, EffectRuns 1, Flushes 1 |
| Ranvier b7863731-dirty (AsyncSource, unbatched) | n/a | 5,504.13 | 0 | 5,504.13 | 0 | MemoRecomputes 16, EffectRuns 16, Flushes 16 |

## stream-burst-256: deliver 256 items per round into a stream fold read by a memo and an effect (N = 20)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (stream, batched) | n/a | 296 | 0 | 296 | 0 | MemoRecomputes 1, EffectRuns 1, Flushes 1 |
| Ranvier b7863731-dirty (AsyncSource, batched) | n/a | 26,923.20 | 0 | 26,923.20 | 0 | MemoRecomputes 1, EffectRuns 1, Flushes 1 |
| Ranvier b7863731-dirty (AsyncSource, unbatched) | n/a | 88,065.20 | 0 | 88,065.20 | 0 | MemoRecomputes 256, EffectRuns 256, Flushes 256 |

## stream-restart: write a dependency of a stream, restarting it, then deliver one item; or of an async memo, restarting its flight (N = 500)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Ranvier b7863731-dirty (stream) | n/a | 1,768.03 | 0 | 1,768.03 | 0 | EffectRuns 2, Flushes 2 |
| Ranvier b7863731-dirty (async memo, CancelPrevious) | n/a | 1,215.98 | 0 | 1,215.98 | 0 | EffectRuns 1, Flushes 1 |

## control: refill a 256-entry closure table, sum it, and sort a copy of its array (N = 50)

| Engine | instr/op | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Control | n/a | 45,812.80 | 0 | 45,812.80 | n/a | n/a |


