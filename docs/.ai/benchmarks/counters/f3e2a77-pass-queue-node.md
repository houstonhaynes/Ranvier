# Counter bench, pass queue on Node.js (#123, PR #127)

- Date: 2026-10-20
- Change: the pass queue (`3dacd76` .. `f3e2a77`) against `e34da8c`
- Source: the CI `perf ab` arm64-counters leg of PR #127, runs 39439362437 (`3ea5e7b`) and 39448641935 (`0dca0ba`), plus local
  runs of the Fable counter harness under the CI V8 flags (`--expose-gc --single-threaded --min-semi-space-size=32
  --max-semi-space-size=32`), one scenario per process, from `perf build` outputs of `e34da8c` and `f3e2a77`

## Flagged rows (Node.js, instructions per operation)

| Scenario | Engine | Base | `3ea5e7b` | `0dca0ba` |
| --- | --- | ---: | ---: | ---: |
| stream-burst-16 | Ranvier (AsyncSource, batched) | 10,350 | 11,235 (+8.5%) | 10,631 (+2.7%) |
| stream-burst-256 | Ranvier (AsyncSource, batched) | 119,391 | 116,645 (-2.3%) | 123,050 (+3.1%) |
| work-form | Ranvier (createEffectOn) | 287,121 | 288,364 (+0.4%) | 293,062 (+2.1%) |

On .NET the same commits add exactly the work they contain: +3 instructions per effect mark and +2 per flush
(derive-effect 760 → 765, update 82,005 → 82,505, stream-burst-256 batched +5).

## Findings

- **The work per item is unchanged.** The Fable output of `e34da8c` and `f3e2a77` differs only in the `Graph`
  constructor (five new fields), the new `SchedulePass`, `QueueDrain` and `RunPasses`, one `passCount` test at the start of
  `RunFlush`, and the `publishes` test in `Effect.MarkDirty`/`MarkCheck`. An AsyncSource settle, `RequestFlush`,
  `Schedule` and the flush loop compile to identical JavaScript, and the pass functions run zero times in these
  scenarios. The library counters are identical in base and head:
  - The stream-burst rows have one flush, one effect run and one memo recompute per operation. The added work is one
    `passCount` test and one `publishes` test per operation, against about 14 instructions per item measured.
  - work-form has 125 flushes and 126 effect runs per operation. The added work for createEffectOn is 125 `passCount`
    tests; `EffectOn` has no `publishes` test. That is about 250 instructions, against +5,941 measured.
  - work-form createEffect does the same plus 126 `publishes` tests, and moves +1,868 (+0.6%), less than the
    createEffectOn row with less work.
- **Allocation and collections are identical.** Run alone, base and head allocate the same bytes. No collection and no
  compilation run inside the regions:
  - stream-burst-256 batched: 548,704 and 1,087,144 B at N and 2N
  - stream-burst-16 batched: 990,400 and 1,970,488 B
  - work-form createEffectOn: 10,280 B
- **Deoptimisation is identical.** `--trace-deopt` shows the same deoptimisations, with the same reasons, in both builds.
- **`Graph` stays in-object.** `%DebugPrint` shows all 26 `Graph` fields in-object; base has 21. Instance size grows from
  192 to 232 bytes, and no field moves to the out-of-object property store.
- **The cost moves against the added work.**
  - Between `3ea5e7b` and `0dca0ba`, the per-item `RequestFlush` lost a call and returned to base's code. Yet
    stream-burst-256 batched moved from -2.3% to +3.1%, and work-form createEffectOn from +0.4% to +2.1%.
  - stream-burst-16 batched moved from +8.5% to +2.7%.
  - The rows move independently of the work on their path, so they reflect V8's optimisation and inlining state for
    the changed `Graph` shape and code layout, not added work.

## Accepted

`Perf-Accepted: stream-burst-16, stream-burst-256, work-form` on the commit that adds this note.
