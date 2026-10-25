# Input journal budget (#184, criterion 5)

Baseline: `aae54f2e`, the fork point of `issue/184`. Head: `issue/184` with the out-of-line signal hook
(`Signal.RecordWrite`). Both are untraced Release builds of `src/Ranvier` for `net10.0`, with
`Journal = None`. Machine: AMD64 Family 26 Model 68, 24 logical processors, Windows 10.0.26200,
.NET 10.0.13, a non-elevated shell, shared with up to two other lanes.

The baseline tree is a `git archive` export, not a worktree. `build.fsx -- perf build` and
`tools/verify-trace.fsx` add git worktrees, which this lane does not create, and the counter A/B
harness (`Ranvier.Counters ab`) reads Linux thread counters only. The measurements below replace
them with the same comparisons run by hand.

## IL, method by method

`il-diff.txt` lists every merge-base method whose untraced body changed, from the
`methodBodies` comparison of `tools/verify-trace.fsx` gate 1:

| Method | Change |
| --- | --- |
| `Signal.set_Value` | The guarded hook: `ldfld graph`, `ldfld journal`, `brfalse`, and a call to `RecordWrite` that runs only with a journal. Once per copy of the body (the fast path and the guarded path). |
| `AsyncSource.Settle` and `Fail` closures | The guarded hook, as in the setter. |
| `Graph.RunFlush` | The guarded `Boundary` call after a flush pass. |
| `Graph.RunUntrackedHosted`, `RunUntrackedHostedWith` | The guarded `EnterDerived` / `LeaveDerived` pair. |
| `Graph.EnterContinuation`, `LeaveContinuation` | The guarded `EnterDerived` / `LeaveDerived` pair for an async-memo continuation. |
| `Effect.RunBody`, `EffectOn.Act` | Added by the second review fix of PR #188: `ldfld graph`, `ldfld journal`, `brfalse` before the body runs; the work numbering and its closure run only with a journal. |
| `Graph.Post` | Added by the same fix: on the queued, off-thread path only, a `journal` null check before the inbox enqueue. |
| `Graph..ctor` | Resolving `options.Journal` into the `journal` field. |
| `GraphOptions` members | The record's fifth field, `Journal`. |
| `AsyncMemo.applyResult` | No instruction change: the `ldstr` source path and line literal of a raise site differ between the two trees. |

## Counters, every scenario

The counters below predate the second review fix. Its journal-off additions are the null checks in the table above and allocate nothing; the counters were not rerun for it.

`counters-base-aae54f2e.md` and `counters-head.md`: `Ranvier.Counters --no-pmc` with a
`RanvierCounters` counters worker, base then head, under `gate-lock.ps1 bench`. CPU load 63 % before,
68 % after. Allocations and library counters are deterministic.

Every Ranvier row is identical except `server-render`, which builds a graph per request: 200,560 B/op
at the baseline and 200,568 B/op at head, the same 103 objects/op. The 8 bytes are the `journal`
reference field on `Graph`. Instruction counts are not collected: processor counters need an
elevated shell.

An earlier run of `update`, `chain` and `cutoff`, base and head interleaved twice, gave identical
rows (CPU load 85 % before, 77 % after).

## Time, paired and interleaved

`timing-harness.fs` is compiled once against each `Ranvier.dll`. Each process reports the best of
five passes of 200,000 operations:

- signal write: a signal, a memo over it and an effect over the memo; one write per operation;
- memo chain: a signal and a chain of four memos; one write and a read of the last memo per
  operation.

`paired.ps1` runs the two processes alternately for 20 rounds, flipping the order each round, and
reports the geometric mean of the head/base ratios with a 95 % t interval.

| Run | CPU load | Signal write, head/base | Memo chain, head/base |
| --- | --- | --- | --- |
| A/B (`paired-timing.csv`) | 70 % before, 62 % after | 1.026 [0.954, 1.105] | 0.952 [0.892, 1.017] |
| A/A, the baseline against itself (`aa-timing.csv`) | 82 % before, 66 % after | 1.017 [0.946, 1.094] | 0.965 [0.884, 1.054] |

Both A/B intervals contain 1 and lie within the A/A interval of the same harness: no difference is
detected at this machine's noise. The intervals are about ±8 % wide, so a cost under that is not
ruled out.

An earlier A/B run, with the hook inline in the setter (`graph.RecordInput (JournalEntryKind.Write, id,
box v)` behind the null check), gave a signal write ratio of 1.046 [1.004, 1.090]. Moving the call into
the `NoInlining` `RecordWrite` leaves the field load and null check in the setter.

## Async and effect scenarios (#205)

The budget above measured the signal and memo steady state only. The CI Perf gate on master PR #204
(run 40173180229, base `27c6cc2d`, head `dfcb336f`) measured every scenario and rejected the
journal-off cost on the async and effect paths. A gate run at `64aecdcd`, the commit before #184,
against the same base passed with no regression (run 40174761989), and `db599180..dfcb336f` changes
no file of `src/Ranvier`, so every row below comes from #184.

### Where the cost came from

| Hook | .NET (x64 JIT disassembly) | Fable (`node --jitless` bytecode) |
| --- | --- | --- |
| `Graph.Journal` | Inlined by the JIT. | A call to `Graph__get_Journal` at every guard. |
| `Signal.set_Value` | `mov`, `cmp`, `je`: the field load and null check. | The getter call, and a ternary for the `\|\|` guard (8 bytecodes). |
| `AsyncSource` settle and fail closures | Boxing and the `RecordInput` call inline in the closure: two more saved registers. | The getter call. |
| `Graph.Post` | The queued path grew past the inlining budget: Post became a call with a five-register prologue on the on-thread path. | — (Post is `work ()`). |
| `Effect.RunBody`, `EffectOn.Act` | One field load and null check. | The getter call, and a closure context allocated on every run, because the journaled closure sat in the hot function. |
| `Graph.RunUntrackedHostedWith` | A `journaled` local kept alive across the `finally`: about 11 instructions. | A local and two tests. |

### The fix

`Graph.Journal` is inline; the journaled branches of `RunBody` and `Act` are out of line
(`RunBodyJournaled`, `ActJournaled`); the settle and fail closures call `RecordSettle` / `RecordFail`
out of line; `Post`'s queued path is `PostQueued`, out of line, so `Post` inlines into its callers; the
setter, settle and fail guards use `&&`, which Fable emits as a short-circuit (5 bytecodes); and
`ActJournaled` marks the derived section around `RunUntrackedHostedWith`, whose body returns to its
pre-#184 IL. `RunUntrackedHosted` keeps its two field tests.

### Gate, before and after

Instructions are the arm64 counter medians; `cpu_ns` is x64 Node.js timing. The base is `27c6cc2d` in
both runs; timing bases differ between runs.

| Runtime | Scenario | Engine | Metric | Base | `dfcb336f` | Change | Base | `56af35da` | Change |
|---|---|---|---|---:|---:|---:|---:|---:|---:|
| dotnet | stream-burst-16 | AsyncSource, batched | instructions | 5630.9 | 5803.9 | +3.07% | 5630.9 | 5021.9 | −10.82% |
| dotnet | stream-burst-256 | AsyncSource, batched | instructions | 64131.4 | 66704.4 | +4.01% | 64131.4 | 54162.4 | −15.54% |
| dotnet | update-on | Ranvier | instructions | 93005.0 | 95205.0 | +2.37% | 93005.0 | 94505.0 | +1.61% |
| node-jitless | cutoff | Ranvier | instructions | 874.9 | 1244.2 | +42.21% | 874.9 | 1005.9 | +14.97% |
| node-jitless | derive-effect | Ranvier | instructions | 16108.1 | 17271.8 | +7.22% | 16108.1 | 16448.1 | +2.11% |
| node-jitless | derive-on | Ranvier | instructions | 16267.2 | 16709.5 | +2.72% | 16267.2 | 16471.2 | +1.25% |
| node-jitless | shape-dynamic | Ranvier | instructions | 2006440.8 | 2100924.6 | +4.71% | 2006440.7 | 2030349.7 | +1.19% |
| node-jitless | stream-burst-1 | AsyncSource, batched | instructions | 47213.5 | 48408.8 | +2.53% | 47213.5 | 47557.6 | +0.73% |
| node-jitless | stream-burst-1 | AsyncSource, unbatched | instructions | 45422.9 | 46648.5 | +2.70% | 45423.0 | 45789.8 | +0.81% |
| node-jitless | stream-burst-16 | AsyncSource, batched | instructions | 121075.2 | 128359.6 | +6.02% | 121072.4 | 123780.8 | +2.24% |
| node-jitless | stream-burst-16 | AsyncSource, unbatched | instructions | 714275.6 | 733872.3 | +2.74% | 714275.6 | 720147.5 | +0.82% |
| node-jitless | stream-burst-256 | AsyncSource, batched | instructions | 1303120.8 | 1407048.0 | +7.98% | 1303062.3 | 1343330.9 | +3.09% |
| node-jitless | stream-burst-256 | AsyncSource, unbatched | instructions | 11414824.0 | 11728329.0 | +2.75% | 11414822.0 | 11508770.0 | +0.82% |
| node-jitless | stream-restart | async memo, CancelPrevious | instructions | 42496.3 | 43660.8 | +2.74% | 42492.8 | 42833.0 | +0.80% |
| node-jitless | stream-restart | stream | instructions | 71408.9 | 73362.0 | +2.74% | 71401.1 | 71951.9 | +0.77% |
| node-jitless | timed-admit-first | Ranvier | instructions | 1611697.7 | 1658310.3 | +2.89% | 1611697.7 | 1620605.7 | +0.55% |
| node-jitless | update | Ranvier | instructions | 2503269.7 | 2619673.3 | +4.65% | 2503400.8 | 2537437.7 | +1.36% |
| node-jitless | update-on | Ranvier | instructions | 2830985.6 | 2960277.2 | +4.57% | 2830966.6 | 2865013.0 | +1.20% |
| node-jitless | work-form | createEffect | instructions | 5349980.7 | 5542273.6 | +3.59% | 5350002.9 | 5409038.2 | +1.10% |
| node-jitless | work-status | createEffect | instructions | 2611609.5 | 2727990.1 | +4.46% | 2611573.7 | 2645665.1 | +1.31% |
| node-jitless | work-status | createEffectOn | instructions | 2361431.7 | 2413963.4 | +2.22% | 2361454.7 | 2383108.5 | +0.92% |
| node-jitless | work-value | createEffect | instructions | 2596238.0 | 2712564.5 | +4.48% | 2596308.1 | 2630172.3 | +1.30% |
| node-jitless | work-value | createEffectOn | instructions | 2920832.7 | 3049866.9 | +4.42% | 2920784.4 | 2954783.3 | +1.16% |
| node | stream-burst-1 | AsyncSource, unbatched | cpu_ns | 113.7 | 408.6 | +259.19% | 191.4 | 183.4 | −4.14% |
| node | stream-burst-256 | AsyncSource, unbatched | cpu_ns | 29538.5 | 34304.8 | +16.14% | 43978.3 | 44312.8 | +0.76% |
| node | stream-restart | async memo, CancelPrevious | cpu_ns | 286.2 | 361.2 | +26.22% | 469.4 | 440.7 | −6.11% |
| node | timed-admit-both | Ranvier | cpu_ns | 4726.4 | 5801.4 | +22.74% | 7194.1 | 6792.2 | −5.59% |
| node | cutoff | Ranvier | cpu_ns | 1.04 | 1.59 | +52.56% | 1.45 | 2.41 | +66.86% |

### Accepted remainder

Four scenarios stay above the gate's 2 % instruction threshold (and cutoff's x64 Node.js timing above
15 %). Each is the cost of the guards alone: a property load of `graph`, of `journal`, and a null test,
5 bytecodes and about 131 instructions per guard under `--jitless`.

- **cutoff:** one setter guard per write: +131 instructions on 875 (the setter's work is an equality
  test), +0.97 ns per write on x64 Node.js.
- **derive-effect:** the setter, `Effect.RunBody` and `RunFlush` guards, one each: +340 instructions.
- **stream-burst-16 / stream-burst-256 (batched):** one settle-closure guard per item, about 157
  instructions each; on .NET the same scenarios improve by 10.8 % and 15.5 %.

Removing the per-node guard needs a change to #184's design, such as caching the recorder on each
node (a field per node, one load instead of two) or a journaled node type chosen at construction.
The remainder is accepted with `Perf-Accepted` trailers instead.
