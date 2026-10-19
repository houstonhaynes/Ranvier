# Observer removal benchmark

Can `ObserverSet.Remove` get cheaper by tombstoning slots instead of swap-removing them? The motivation was a Chromium
profile of disposing 128 view rows (Fable build), where `ObserverSet.Remove` took 49.6% of teardown, most of it in
`refIndexRemove` and `refIndexSet` on the `RefIndex` kept above 8 observers.

**Outcome: rejected.** On .NET the tombstone design made disposal 22-31% faster at 64 and 1024 observers. Under
Fable/V8 it made root disposal about 30% slower, and Fable is the build the profile came from. Master's swap-remove
stays. The benchmarks and tests from this work are committed. The engine change is not.

## Setup

- Machine: AMD Ryzen 9 9900X 4.40GHz, 12 cores / 24 threads; Windows 11 (10.0.26200.9457, 25H2); High performance plan
- .NET: SDK 10.0.402, runtime 10.0.13, X64 RyuJIT x86-64-v4; BenchmarkDotNet v0.15.8, job `UnrollFactor=16`
- JS: Node v26.7.0 running `dotnet fable fable/Ranvier.Tests.Fable -c Release` output; scripts are in the session
  scratchpad and are not committed
- Base: master `e2e0f95`. Benchmarks: commit `75c9296` (`bench/Ranvier.Benchmarks/Lifetimes.fs`,
  `ObserverRemovalBenchmarks` and `ObserverChurnBenchmarks`)
- Command: `dotnet run --project bench/Ranvier.Benchmarks -c Release -- --anyCategories ObserverRemoval`
- Raw base reports: `docs/.ai/benchmarks/results/Ranvier.Benchmarks.Lifetimes.Observer*-report-github.md`

Cases (`Nodes` = N effects on one source):

- `DisposeRoot`: create N effects under a root, then dispose the root. Removal runs newest first.
- `DisposeInCreationOrder`: create N effects, then dispose them oldest first. This is swap-remove's worst order.
- `RelinkAll`: every effect drops its edge to the wide source, then every effect links it again.
- `RelinkOne`: one effect drops its edge and links it again, cycling through the effects.
- `WriteWide`: one write that notifies all N effects. Setup has already relinked half of them.

## Design measured

- **Vacant slots.** `Remove` blanks the slot and moves no other observer. The live observers sit in a window
  `[head, length)`. Removing the first or last live observer shrinks the window and trims any vacant slots it exposes.
- **Compaction.** The live observers are moved to the front once vacant slots outnumber them 2:1. Each moved entry
  gets an index overwrite. A walk can trigger compaction, and that is safe: entries only move towards the front, so
  no unvisited observer is skipped.
- **Lazy end removal.** Removing an end observer leaves its index entry stale. A lookup checks the hit against the
  slot, so a stale entry reads as a miss. The index is cleared once stale entries exceed half the live count, and it
  is rebuilt when a lookup next needs it.
- **Eager middle removal.** Removing a middle observer deletes its index entry immediately.
- **Count zero.** When the live count reaches 0, the window resets and the index is cleared.

All existing tests passed with this design, including the traced and Fable suites, plus 21 new ones.

## .NET (BenchmarkDotNet, back-to-back runs, StdDev 1-3%)

| Case | N | master | tombstone | ratio |
| --- | ---: | ---: | ---: | ---: |
| DisposeRoot | 8 | 438.6 ns | 473.3 ns | 1.08 |
| DisposeRoot | 64 | 6,159.5 ns | 4,741.6 ns | 0.77 |
| DisposeRoot | 1024 | 101,567 ns | 79,234 ns | 0.78 |
| DisposeInCreationOrder | 8 | 649.8 ns | 453.4 ns | 0.70 |
| DisposeInCreationOrder | 64 | 6,976.0 ns | 4,795.3 ns | 0.69 |
| DisposeInCreationOrder | 1024 | 118,984 ns | 105,785 ns | 0.89 |
| RelinkAll | 8 | 378.0 ns | 410.1 ns | 1.08 |
| RelinkAll | 64 | 3,927.7 ns | 3,640.4 ns | 0.93 |
| RelinkAll | 1024 | 74,813 ns | 62,853 ns | 0.84 |
| RelinkOne | 8 | 60.95 ns | 60.15 ns | 0.99 |
| RelinkOne | 64 | 72.26 ns | 69.26 ns | 0.96 |
| RelinkOne | 1024 | 83.41 ns | 76.98 ns | 0.92 |
| WriteWide | 8 | 132.9 ns | 135.3 ns | 1.02 |
| WriteWide | 64 | 1,008.7 ns | 1,021.9 ns | 1.01 |
| WriteWide | 1024 | 17,975 ns | 16,954 ns | 0.94 |

The tombstone column clears stale entries above 16 rather than above half the live count. That threshold changes
only how many stale entries the index holds; the removal paths are the same. Allocation is identical in every case.
A first master run, made while the machine was busy, was about 30% slower across the board, including cases that
never touch removal. It is discarded.

## Fable / V8

These numbers come from the same cases written against the compiled graph, with master and the tombstone build
interleaved in one process, median of 11.

- **The isolated `ObserverSet` won where the profile pointed.** Adding N plain observers and removing them, per
  observer at N = 1024:

  | Removal order | master | tombstone |
  | --- | ---: | ---: |
  | Reverse | 41.3 ns | 33.3 ns |
  | Forward | 47.3 ns | 33.5 ns |
  | Scattered | 55.2 ns | 55.5 ns |

  At N = 64, reverse went from 34.2 ns to 28.9 ns and forward from 40.3 ns to 29.7 ns.
- **Through the graph, root disposal got slower.** `DisposeRoot` was measured in separate processes, 40,000
  iterations each:

  | N | master | tombstone |
  | ---: | ---: | ---: |
  | 64 | 11.9-12.0 µs | 15.7-16.3 µs |
  | 1024 | 192 µs | 244 µs |

  The `--trace-gc` counts over the same run explain it. Master did 162 scavenges and 30 mark-compacts. The tombstone
  build did 179 scavenges and 42 mark-compacts. The CPU profile agrees: `ObserverSet.Remove` drops from 7.4% of
  samples to under 1%, but the time moves into GC. Two causes are likely:
  - Removed effects stay reachable from stale `Map` entries until the clear, so more of them survive a scavenge.
  - `Map.clear()` followed by a rebuild reallocates the backing table, where master's progressive `delete` does not.
- **Other cases did not move clearly.** The interleaved runs gave these ratios (tombstone / master):

  | Case | Ratio range |
  | --- | --- |
  | `DisposeInCreationOrder` | 0.91-0.98 |
  | `RelinkAll` | 0.81-1.03 |
  | `RelinkOne` | 0.83-1.04 |
  | `WriteWide` | 0.97-1.35 |
  | `DisposeRoot` at 64 | 1.08-1.15 |
- **Other variants did no better.**
  - Deleting index entries eagerly on every removal keeps the window and compaction and has no retention. It was a
    wash: `DisposeRoot` 64 measured 12.1 µs against master's 12.0 µs, and 1024 measured 201 µs against 192 µs.
    Without the lazy deletion there is little left to save.
  - Clearing stale entries above the live count, not half of it, was slower still.

## What remains

- The cost the profile shows is hashing, and only removing the hash removes it. Store each edge's slot position on
  both sides: the `SourceList` entry and the `ObserverSet` slot. Removal then becomes an O(1) unlink with no index
  operation. This is the doubly-linked `Link` model the `SourceList` remarks already name as the end state. It
  changes `ISource.AddObserver` and `RemoveObserver` and their 12 pass-through implementers.
- Under Fable, `ObserverSlot` compiles to a heap object, so every `Add` allocates. The profile shows it as
  `ObserverSlot_$ctor`, at about 3% of a teardown-heavy run. A Fable-only representation that stores the observer
  directly would remove that allocation. The struct exists only for .NET's covariant-store check.
- The new tests (`Observers.fs` `ObserverSet`, and `Retention.fs`) pin the behaviour any future design must keep:
  - removal and compaction during a notification walk;
  - observers added mid-walk;
  - an exact `Count` under out-of-order and repeated removal;
  - re-adding after removal on a wide list;
  - wide-source retention.

## 2026-10-19: hash-free observer removal

Step 1 of the revised direction in `docs/.ai/superpowers/specs/2026-10-14-indexed-edges-design.md`. `ObserverSet`
drops `RefIndex`, and `SourceList` takes over duplicate-read detection. `ISource` is unchanged.

**Outcome: dispose wins on both runtimes and closes most of the browser gap, but scattered removal is not the only
loss.** A reader of many sources that changes its read order is 1.1-1.4× slower at 8 sources and 2.4-2.9× slower at
64 and 1024. Relinking one observer of 1024 is 2.8× slower on .NET and 4.5× on V8. By the design's rule, this does not
become the single model.

### Variants

All three keep swap-remove, the backwards re-clamped notification walk and `Add`'s check of the last slot. They differ
only in where removal looks for the observer.

- **A** (`c121218`) checks the last slot, then index 0, then scans from 1. The design expected teardown in either
  order to be O(1). It is not: after index 0 is removed, the last observer moves into 0, so the next oldest sits at 1,
  then 2, and so on. Creation-order teardown costs about n/8 compares per removal (129 at n = 1024, simulated) until
  the second half, which hits the last slot.
- **B** (`1d89caf`) checks the last slot, then scans from the position after the previous middle removal, wrapping
  round. Creation order costs about 1.5 compares per removal at any n.
- **C** (`6c05004`) is B with the wrapped scan written as two plain loops (`hint..last-1`, then `0..hint-1`). Its
  compare count matches B's, and V8 runs it faster.

The reader side is shared by all three. On an append, `SourceList` asks whether the source already holds a slot: a scan
up to 8 slots, and above that a reference index built on the first append of a run and dropped at `EndRun` or on a
trim (finding 6). A held source is not passed to `AddObserver`. Finding 1's handle-only check needs edge handles, so
it does not apply here.

### Setup

- Machine, OS, .NET and BenchmarkDotNet as above; Node v26.7.0; Chromium 153.0.8010.12; Solid 2.0.0-rc.14.
- Base: `d1121c0`, which is master `305d009`'s `src/` plus the new benchmarks. Each variant was built in its own
  worktree at the commit listed.
- .NET: `dotnet run --project bench/Ranvier.Benchmarks -c Release -- --anyCategories ObserverRemoval`, one process per
  run, interleaved base, A, B, base, A, B, then base, C, base, C. Each cell is the mean of two process means, with the
  per-process values in brackets.
- Fable/V8: each variant's `src/Ranvier` compiled with `dotnet fable -c Release`, all four loaded into one Node process
  and interleaved, 11 rounds, median per process. Three processes; each cell is the median of the process medians, with
  their range. `--trace-gc` counts come from separate processes, one variant and one case each, 2.56 million effects
  created and disposed. Scripts are in the session scratchpad and are not committed.
- Browser: `poc/fable-ranvier-dom` at `29f8505`, whose `src/Ranvier/Core.fs` is identical to master's. Each variant's
  `Core.fs` was copied in, `node benchmarks/frameworks/run.mjs --build --template-comparison` was run from
  `examples/Fable.Ranvier.Playground`, and the file was restored. Run order: base, B, A, base, B, base, C, A, base, C.
- New cases (`d1121c0`): `DisposeScattered` disposes in one fixed shuffled order. `OutOfOrderReadBenchmarks.RotateReads`
  is one effect reading N signals, each run rotated by one from the last, so every read after the first misses its
  slot.

### .NET

| Case | N | base | C | C/base | A/base | B/base |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| DisposeRoot | 8 | 415 ns (411, 419) | 413 ns (402, 425) | 1.00 | 0.99 | 1.00 |
| DisposeRoot | 64 | 4,275 ns (4,292, 4,258) | 3,086 ns (3,221, 2,951) | 0.72 | 0.69 | 0.67 |
| DisposeRoot | 1024 | 73.2 µs (73.2, 73.1) | 50.8 µs (50.1, 51.5) | 0.69 | 0.67 | 0.70 |
| DisposeInCreationOrder | 8 | 417 ns (417, 418) | 403 ns (403, 403) | 0.97 | 0.96 | 0.90 |
| DisposeInCreationOrder | 64 | 4,869 ns (4,812, 4,926) | 3,130 ns (3,128, 3,132) | 0.64 | 0.67 | 0.61 |
| DisposeInCreationOrder | 1024 | 87.0 µs (87.6, 86.4) | 53.6 µs (54.2, 53.0) | 0.62 | 1.29 | 0.60 |
| DisposeScattered | 8 | 419 ns (420, 418) | 432 ns (436, 429) | 1.03 | 0.98 | 0.94 |
| DisposeScattered | 64 | 4,979 ns (5,001, 4,957) | 3,455 ns (3,438, 3,472) | 0.69 | 0.68 | 0.68 |
| DisposeScattered | 1024 | 93.7 µs (95.0, 92.4) | 145.1 µs (139.3, 151.0) | 1.55 | 1.41 | 1.52 |
| RelinkOne | 8 | 59.6 ns (59.9, 59.2) | 54.3 ns (54.3, 54.4) | 0.91 | 0.94 | 0.90 |
| RelinkOne | 64 | 67.3 ns (68.0, 66.7) | 64.1 ns (64.0, 64.3) | 0.95 | 0.98 | 0.92 |
| RelinkOne | 1024 | 76.5 ns (76.7, 76.3) | 214.0 ns (217.3, 210.7) | 2.80 | 2.70 | 2.74 |
| RelinkAll | 8 | 360 ns (367, 353) | 404 ns (403, 405) | 1.12 | 1.02 | 1.05 |
| RelinkAll | 64 | 3,688 ns (3,680, 3,696) | 3,012 ns (3,023, 3,002) | 0.82 | 0.84 | 0.81 |
| RelinkAll | 1024 | 71.4 µs (72.1, 70.8) | 49.1 µs (49.1, 49.1) | 0.69 | 1.49 | 0.67 |
| WriteWide | 8 | 129 ns (128, 130) | 127 ns (129, 126) | 0.99 | 0.99 | 0.99 |
| WriteWide | 64 | 964 ns (935, 994) | 949 ns (961, 938) | 0.98 | 0.96 | 0.98 |
| WriteWide | 1024 | 17.5 µs (17.5, 17.5) | 17.4 µs (17.9, 16.9) | 1.00 | 1.02 | 0.99 |
| RotateReads | 8 | 79.2 ns (78.5, 79.9) | 107.8 ns (108.6, 106.9) | 1.36 | 1.30 | 1.29 |
| RotateReads | 64 | 467 ns (467, 467) | 1,287 ns (1,249, 1,325) | 2.76 | 2.73 | 2.80 |
| RotateReads | 1024 | 7.05 µs (7.41, 6.69) | 20.7 µs (21.0, 20.5) | 2.94 | 2.83 | 2.71 |

The A and B ratios are against the base processes of the first pass, which ran 2-6% slower than the second (base
`DisposeRoot` 64: 4,545 ns; 1024: 75.5 µs).

Allocation: each effect is 8 bytes larger, the `held` field on its `SourceList` (`DisposeRoot` 64: 21.3 KB against
20.8 KB). `RotateReads` allocates its per-run index: 5,216 B per run at 64 sources and 57,536 B at 1024, against none.
Every other case allocates nothing, as before.

### Fable / V8

| Case | N | base | C | C/base | A/base | B/base |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| DisposeRoot | 8 | 573 ns (552-574) | 498 ns (489-503) | 0.87 | 0.87 | 0.88 |
| DisposeRoot | 64 | 9.82 µs (9.33-10.3) | 3.73 µs (3.71-3.76) | 0.38 | 0.46 | 0.38 |
| DisposeRoot | 1024 | 140 µs (140-155) | 59.2 µs (58.7-59.4) | 0.42 | 0.48 | 0.43 |
| DisposeInCreationOrder | 8 | 654 ns (649-655) | 595 ns (592-610) | 0.91 | 0.90 | 0.92 |
| DisposeInCreationOrder | 64 | 11.9 µs (11.3-12.8) | 4.74 µs (4.71-4.75) | 0.40 | 0.48 | 0.40 |
| DisposeInCreationOrder | 1024 | 184 µs (180-200) | 75.5 µs (74.6-76.8) | 0.41 | 0.80 | 0.42 |
| DisposeScattered | 8 | 636 ns (628-640) | 558 ns (556-576) | 0.88 | 0.88 | 0.89 |
| DisposeScattered | 64 | 11.9 µs (11.8-12.4) | 5.11 µs (5.10-5.12) | 0.43 | 0.49 | 0.45 |
| DisposeScattered | 1024 | 197 µs (191-205) | 211 µs (209-212) | 1.07 | 1.00 | 1.34 |
| RelinkOne | 8 | 103 ns (103-103) | 95 ns (94-96) | 0.92 | 0.93 | 0.96 |
| RelinkOne | 64 | 132 ns (131-136) | 124 ns (123-126) | 0.94 | 0.84 | 1.04 |
| RelinkOne | 1024 | 139 ns (138-145) | 624 ns (598-651) | 4.49 | 2.62 | 5.42 |
| RelinkAll | 8 | 629 ns (625-636) | 633 ns (632-634) | 1.01 | 1.03 | 1.03 |
| RelinkAll | 64 | 7.58 µs (7.48-7.64) | 5.07 µs (5.06-5.15) | 0.67 | 0.70 | 0.69 |
| RelinkAll | 1024 | 130 µs (126-132) | 89.9 µs (87.7-94.5) | 0.69 | 1.18 | 0.67 |
| WriteWide | 8 | 311 ns (310-314) | 272 ns (270-274) | 0.87 | 0.90 | 0.89 |
| WriteWide | 64 | 2.41 µs (2.40-2.47) | 2.11 µs (2.08-2.18) | 0.87 | 0.89 | 0.90 |
| WriteWide | 1024 | 39.7 µs (38.9-41.8) | 35.9 µs (34.7-36.4) | 0.90 | 0.90 | 0.87 |
| RotateReads | 8 | 200 ns (200-201) | 228 ns (228-232) | 1.14 | 1.19 | 1.14 |
| RotateReads | 64 | 1.26 µs (1.25-1.26) | 3.12 µs (3.09-3.17) | 2.48 | 2.47 | 2.41 |
| RotateReads | 1024 | 19.8 µs (19.6-20.0) | 50.5 µs (50.4-51.3) | 2.55 | 2.55 | 2.56 |

`--trace-gc` counts over 2.56 million effects, three processes per cell:

| Case | N | base: scavenges / mark-compacts | A, B, C: scavenges / mark-compacts |
| --- | ---: | --- | --- |
| DisposeRoot | 64 | 110 / 1, 110 / 1, 60 / 10 | 96-97 / 1 in every process |
| DisposeRoot | 1024 | 61 / 10, 62 / 10, 103 / 1 | 76-85 / 1 in every process |
| DisposeInCreationOrder | 64 | 110-131 / 1 | 96-97 / 1 |
| DisposeInCreationOrder | 1024 | 85-111 / 1 | A 96-123 / 1; B and C 83-97 / 1 |
| DisposeScattered | 64 | 110-131 / 1 | 96-97 / 1 |
| DisposeScattered | 1024 | 91-100 / 1 | 84-128 / 1 |

Unlike tombstoning, removing the hash adds no GC work. The base build alone shows processes with 10 mark-compacts.
Within the interleaved runs, base `DisposeRoot` 1024 ranged 99-201 µs per process round, and C 56-76 µs.

### Browser: template component, Chromium

`ranvier-template`, µs per operation, median of three Chromium processes (process medians in brackets), and the ratio
to Solid 2 in the same run.

| Run | Variant | Dispose 128 | /Solid | Dispose 16 /Solid | Mount 128 /Solid | Update 128 /Solid |
| ---: | --- | ---: | ---: | ---: | ---: | ---: |
| 1 | base | 35.9 (35.9, 35.9, 36.1) | 1.39 | 1.35 | 1.11 | 1.05 |
| 4 | base | 36.7 (37.3, 32.3, 36.6) | 1.44 | 1.25 | 1.13 | 1.05 |
| 11 | base | 36.7 (37.0, 36.4, 36.7) | 1.42 | 1.23 | 1.10 | 1.06 |
| 14 | base | 35.6 (35.9, 35.6, 34.4) | 1.45 | 1.31 | 1.10 | 1.06 |
| 3 | A | 29.4 (29.4, 30.3, 26.6) | 1.13 | 1.01 | 1.09 | 1.07 |
| 13 | A | 28.1 (25.3, 29.8, 27.7) | 1.14 | 1.08 | 1.06 | 1.04 |
| 2 | B | 29.4 (30.0, 29.8, 26.4) | 1.14 | 1.13 | 1.13 | 1.05 |
| 5 | B | 26.9 (30.2, 26.6, 26.4) | 1.06 | 1.10 | 1.14 | 1.05 |
| 12 | C | 28.0 (28.9, 28.3, 27.2) | 1.15 | 1.02 | 1.07 | 1.05 |
| 15 | C | 29.2 (29.4, 29.2, 25.3) | 1.19 | 1.12 | 1.07 | 1.05 |

128-row dispose drops from 35.6-36.7 µs to 26.9-29.4 µs, and from 1.39-1.45× Solid to 1.06-1.19×. The three variants
cannot be told apart here. Mount and update stay within run-to-run noise.

### What won and what lost

- **Won.** Dispose in every order at 64 observers, and root and creation-order dispose at 1024: 0.62-0.72× on .NET and
  0.38-0.43× on V8 (C). Wide relinking is 0.67-0.82× (C). `WriteWide` is 0.87-0.90× on V8. The browser dispose gap to
  Solid shrinks by roughly two thirds.
- **Lost: scattered removal on a wide source.** `DisposeScattered` 1024 is 1.55× on .NET and 1.07× on V8 (C). B's
  wrapped loop was 1.34× on V8, and C's plain loops bring that down. `RelinkOne` 1024 pays the same scan on every
  relink: 2.8× on .NET and 4.5× on V8, where A's loop from index 1 is 2.6×.
- **Lost: readers of many sources that change their read order.** `RotateReads` is 1.14-1.36× at 8 sources (a scan per
  append) and 2.4-2.9× at 64 and 1024 (an index allocated and filled each run, 56 B per source on .NET) on both
  runtimes. The cost comes from
  moving duplicate detection to the reader, and the removal variant does not change it. Master pays nothing here: each
  source's `Add` finds an empty list.
- **Small fan-out.** At 8 observers every case is within 0.87-1.05×, except `RelinkAll` 8 on .NET in the second pass
  (1.12×; A and B measured 1.02-1.05× in the first) and `RotateReads` 8.

### Recommendation

Scattered removal is not the only loss, so by the design's rule this does not become the single model. Proceed to step
2, indexed handles with findings 1-8 applied. Handles address both losses: removal by position makes scattered removal
and `RelinkOne` O(1), and the handle-only record answers a repeat read at the source in O(1), so the reader's scan or
index is needed only on a miss. Measure step 2 against C as well as master: C sets the bar for the dispose cases. C is
the fallback if step 2 stalls. Its losses are confined to a wide source losing one observer from the middle, and to a
reader of many sources that changes its order every run.

### Not verified

- The browser comparison covers the template component only.
- The `--trace-gc` processes report counts only. Their timing lines interleaved with GC output and were not parsed; the
  interleaved runs give the timings.
- Retention under Fable: the wide-source retention test is excluded under Fable, as before.

## 2026-10-19: indexed edge handles

Step 2 of the revised direction in `docs/.ai/superpowers/specs/2026-10-14-indexed-edges-design.md`: each edge's
position is stored at both ends, so removing an edge is a fixed number of array writes. Findings 1-8 are applied, with
no compile-time switch. This is the one model; master and variant C are the comparison builds.

**Outcome: indexed handles match or beat master on every .NET and V8 case (at most 1.02× at 8 observers) except a reader that changes its read order (1.12-1.20×)
and `RelinkAll` at 8 (1.07-1.10×). They remove each of C's losses (scattered removal, `RelinkOne` and `RotateReads` at
64 and 1024), but lose to C on wide relinking (1.05-1.16×). In the browser, 128-row template dispose is 1.20-1.27× Solid,
against master's 1.27-1.48× and C's 1.12-1.16×.** Mount and update match master.

### Design as built

- **`ISource`** has three edge members:
  - `AddObserver(observer, slot)` links unconditionally and returns the handle (the observer's position).
  - `TryAddObserver(observer, slot)` returns the handle when it links, `EdgeHandle.Unlinked` (-1) when the observer is
    linked already, and `EdgeHandle.Unknown` (-2), linking nothing, when the answer would take a scan of more than 8
    observers.
  - `RemoveObserverAt(observer, handle)` swap-removes by handle.
- **`IComputation.MovedIn(slot, handle)`** receives the new handle of the observer a swap-remove moved. It sits on
  `IComputation` rather than on a narrower interface: a narrower one needs a type test, which Fable does not support for
  interfaces, or an extra field. Readers that cannot be moved never receive it.
- **Duplicate reads (findings 1 and 4).** `ObserverSet` keeps the handle of its latest add, an `int` with no reference. A
  hit requires the observer at that handle to be the adding reader, so the record validates itself after any removal or
  swap. A swap that moves the latest observer moves the record with it. A list of at most 8 observers is scanned. Only
  on `Unknown` does `SourceList` check its own slots: a scan up to 8 slots, and above that a reference index built on the
  first such check of a run and dropped at `EndRun` or a trim (finding 6).
- **Removal (finding 2).** `RemoveAt` raises `InvalidOperationException` when the handle holds another observer, in
  every build. -1 does nothing. Every reader writes -1 back after removing. A source read in several slots has one edge,
  owned by its first slot; the later slots hold -1, so `trimFrom` no longer checks whether a kept slot holds the source.
- **Direct linkers (finding 5)** were migrated before any measurement: `Binding` (a field), `FoldReads` (a handle list
  beside `order`), `FoldRow` and `ValueRow` (a field on the row), and the projection summary watch.
- **Summary watch (finding 3).** Each pending row links through a `ForwardingEdge`, which forwards marks to the
  `RowWatch` and holds its handle. It is created by the row's first pending report and reused, and it reports the
  watch's id, so traces are unchanged.
- **Fable (finding 7).** `ObserverSet` and `SourceList` keep parallel arrays (observers and slots, sources and handles),
  grown by one helper per class and blanked together. On .NET the slot structs gain the `int`. The first measured build
  (`5882947`) used `int[]`, which Fable compiles to `Int32Array`. Its browser dispose matched master's (1.39-1.41× Solid)
  and its mount was 6-10% slower: the profile showed fable-library's `copyTo` on typed arrays, a `subarray` view per
  grow, at 2.2% of samples, and `ObserverSet.grow` at 1.9%. `d0174cc` uses plain JavaScript arrays
  (`Platform.positions`) with an element copy. The V8 microbenchmarks did not distinguish the two builds.

### Tests

- Every existing test passes: `dotnet fsi build.fsx -- test` (1,250 `Ranvier.Tests`, 86 C#), traced (1,377 passed, 12
  skipped), and `dotnet fsi build.fsx -- test-fable` (untraced inline 1,064/1,082 and promise 1,000/1,082, traced inline
  1,196/1,219 and promise 1,122/1,219). The Fable failure counts are those of step 1.
- New tests:
  - Each direct-linking reader disposed in reverse and in creation order: bindings of one signal, folds of one
    projection (`FoldRow` and `FoldReads`), and pending rows settled while folds and a value reader observe them. They
    pass on master and C.
  - A pending row's edge to the summary watch, moved by a swap, then removed on settle.
  - A, B, A on a source with 10 other readers, after 0 and 10 other reads, which reaches the reader-side fallback.
  - On `ObserverSet`: a handle naming another observer raises; `TryAdd` finds the latest add and leaves a wide miss to
    the caller; the latest-add record follows a swap; every survivor's handle equals its position after removals in any
    order and after a walk that removes the walking observer.
- Mutation checks, each applied alone against `Ranvier.Tests`:

  | Mutation | Failing tests |
  | --- | ---: |
  | `ObserverSet.RemoveAt` skips `MovedIn` | 70 |
  | `SourceList.MovedIn` ignored | 25 |
  | `MovedIn` ignored by `Binding` / `FoldRow` / `FoldReads` / `ValueRow` / `ForwardingEdge` | 2 / 2 / 4 / 4 / 1 |
  | Identity check removed | 5 |
  | `RemoveAt` acts on -1 | 25 |
  | Latest-add record hit without the identity check (finding 1) | 74 |
  | Reader-side fallback never finds | 2 |
  | A repeated slot links a second edge | 4 |
  | -1 write-back removed in `Binding`, `FoldRow.Detach` or the watch edge | 0 |

  The `ForwardingEdge` mutation survived the first run, and the watch swap test was added for it. The three write-back
  mutations survive because no path removes the same reader's edge twice: `Binding.Dispose` is guarded by `disposed`,
  and a detached `FoldRow`, `ValueRow` or watch edge leaves its map or flag before any later removal. The write-back is
  defensive. The reachable half of finding 2, -1 as a no-op and the identity check, is covered.

### Setup

- Machine, OS, .NET, BenchmarkDotNet, Node and Chromium as in step 1. CPU load was 1-7% when each batch took the bench
  lock. Builds from other agents may have run concurrently in other worktrees; none was observed.
- Commits: base `d1121c0` (master `305d009`'s `src/` plus step 1's benchmarks), C `b420d43`, indexed `d0174cc` (Fable
  fix) on top of `5882947`. .NET ran `5882947`; `d0174cc` changes only Fable branches.
- .NET: `dotnet run --project bench/Ranvier.Benchmarks -c Release -- --anyCategories ObserverRemoval`, one process per
  run, interleaved base, C, indexed, base, C, indexed. Each cell is the mean of two process means, with the per-process
  values in brackets.
- Fable/V8: each build's `src/Ranvier` compiled with `dotnet fable -c Release`; base and C reproduce step 1's output
  byte for byte. All three were loaded into one Node process and interleaved, 11 rounds, median per process. Three
  processes; each cell is the median of the process medians, with their range. `--trace-gc` counts come from separate
  processes, one build and one case each, 2.56 million effects created and disposed.
- Browser: `poc/fable-ranvier-dom` at `29f8505`, whose `src/Ranvier` equals master's. Each build's changed files were
  copied in, `node benchmarks/frameworks/run.mjs --build --template-comparison` was run from
  `examples/Fable.Ranvier.Playground`, and the files were restored. Run order: base, indexed, C, indexed, base, C,
  indexed, base, then C, indexed, C, indexed, base. The first C run is discarded: its `Core.fs` copy failed (file busy),
  so it measured master's. Profiles came from unminified bundles built into the session scratchpad.

### .NET

| Case | N | base | C | indexed | indexed/base | indexed/C | C/base |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| DisposeRoot | 8 | 412 ns (410, 414) | 409 ns (404, 414) | 420 ns (413, 428) | 1.02 | 1.03 | 0.99 |
| DisposeRoot | 64 | 4,266 ns (4,272, 4,260) | 2,981 ns (2,982, 2,981) | 3,081 ns (3,084, 3,078) | 0.72 | 1.03 | 0.70 |
| DisposeRoot | 1024 | 72.8 µs (72.7, 73.0) | 49.7 µs (49.2, 50.2) | 50.3 µs (50.6, 50.0) | 0.69 | 1.01 | 0.68 |
| DisposeInCreationOrder | 8 | 410 ns (412, 409) | 403 ns (404, 402) | 410 ns (416, 404) | 1.00 | 1.02 | 0.98 |
| DisposeInCreationOrder | 64 | 4,930 ns (4,901, 4,959) | 3,221 ns (3,179, 3,263) | 3,174 ns (3,191, 3,158) | 0.64 | 0.99 | 0.65 |
| DisposeInCreationOrder | 1024 | 87.1 µs (85.9, 88.3) | 53.2 µs (53.0, 53.5) | 54.2 µs (54.1, 54.4) | 0.62 | 1.02 | 0.61 |
| DisposeScattered | 8 | 418 ns (413, 424) | 402 ns (402, 402) | 415 ns (418, 412) | 0.99 | 1.03 | 0.96 |
| DisposeScattered | 64 | 5,022 ns (5,115, 4,930) | 3,479 ns (3,430, 3,529) | 3,228 ns (3,234, 3,222) | 0.64 | 0.93 | 0.69 |
| DisposeScattered | 1024 | 93.9 µs (93.7, 94.1) | 150.9 µs (149.4, 152.3) | 57.2 µs (57.2, 57.2) | 0.61 | 0.38 | 1.61 |
| RelinkOne | 8 | 58.3 ns (59.3, 57.3) | 54.4 ns (54.3, 54.6) | 52.8 ns (52.6, 52.9) | 0.90 | 0.97 | 0.93 |
| RelinkOne | 64 | 66.5 ns (66.9, 66.2) | 63.8 ns (63.7, 63.9) | 55.1 ns (55.7, 54.6) | 0.83 | 0.86 | 0.96 |
| RelinkOne | 1024 | 77.8 ns (79.4, 76.2) | 212 ns (211, 212) | 57.1 ns (57.0, 57.2) | 0.73 | 0.27 | 2.72 |
| RelinkAll | 8 | 366 ns (367, 364) | 405 ns (403, 407) | 402 ns (424, 381) | 1.10 | 0.99 | 1.11 |
| RelinkAll | 64 | 3,701 ns (3,698, 3,703) | 3,021 ns (2,991, 3,052) | 3,165 ns (3,253, 3,077) | 0.86 | 1.05 | 0.82 |
| RelinkAll | 1024 | 71.1 µs (70.4, 71.7) | 48.9 µs (47.9, 49.9) | 51.3 µs (52.4, 50.2) | 0.72 | 1.05 | 0.69 |
| WriteWide | 8 | 128 ns (126, 129) | 127 ns (124, 129) | 126 ns (126, 127) | 0.99 | 0.99 | 0.99 |
| WriteWide | 64 | 963 ns (948, 978) | 937 ns (931, 942) | 943 ns (946, 941) | 0.98 | 1.01 | 0.97 |
| WriteWide | 1024 | 17.1 µs (16.9, 17.4) | 17.0 µs (17.1, 17.0) | 17.0 µs (17.1, 17.0) | 0.99 | 1.00 | 0.99 |
| RotateReads | 8 | 78.5 ns (78.8, 78.2) | 105 ns (106, 105) | 87.8 ns (87.7, 87.8) | 1.12 | 0.83 | 1.34 |
| RotateReads | 64 | 467 ns (463, 471) | 1,298 ns (1,321, 1,276) | 562 ns (559, 565) | 1.20 | 0.43 | 2.78 |
| RotateReads | 1024 | 7,263 ns (7,262, 7,265) | 20.9 µs (20.9, 20.8) | 8,589 ns (8,646, 8,532) | 1.18 | 0.41 | 2.88 |

Allocation: each effect is 8 bytes larger than in C, the handle of its first source, and 16 bytes larger than in master,
with C's per-run index field (`DisposeRoot` 64: 21.8 KB against C's 21.3 KB and master's 20.8 KB; 1024: 344.3 KB,
336.3 KB, 328.3 KB). An observer slot and a source slot are 16 bytes each on .NET, against 8, so observer and source
arrays double in size. `RotateReads` allocates nothing per run, against C's 5,216 B at 64 sources and 57,536 B at 1024.
Every other case allocates nothing, as before.

### Fable / V8

| Case | N | base | C | indexed | indexed/base | indexed/C | C/base |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| DisposeRoot | 8 | 566 ns (547-578) | 498 ns (482-526) | 509 ns (502-519) | 0.90 | 1.02 | 0.88 |
| DisposeRoot | 64 | 9.50 µs (9.50-10.0) | 4.35 µs (4.23-4.36) | 3.87 µs (3.83-3.92) | 0.41 | 0.89 | 0.46 |
| DisposeRoot | 1024 | 138 µs (134-139) | 65.6 µs (65.5-71.1) | 60.8 µs (60.7-60.8) | 0.44 | 0.93 | 0.48 |
| DisposeInCreationOrder | 8 | 645 ns (641-646) | 596 ns (593-601) | 604 ns (604-616) | 0.94 | 1.01 | 0.92 |
| DisposeInCreationOrder | 64 | 11.8 µs (11.7-12.2) | 5.56 µs (5.22-5.58) | 4.75 µs (4.74-4.78) | 0.40 | 0.85 | 0.47 |
| DisposeInCreationOrder | 1024 | 180 µs (177-184) | 83.5 µs (82.0-86.3) | 77.1 µs (75.4-80.0) | 0.43 | 0.92 | 0.46 |
| DisposeScattered | 8 | 632 ns (623-636) | 560 ns (556-574) | 564 ns (557-568) | 0.89 | 1.01 | 0.89 |
| DisposeScattered | 64 | 11.4 µs (11.3-11.7) | 5.87 µs (5.87-5.90) | 4.82 µs (4.79-4.83) | 0.42 | 0.82 | 0.52 |
| DisposeScattered | 1024 | 195 µs (189-197) | 217 µs (217-218) | 82.8 µs (82.4-84.8) | 0.42 | 0.38 | 1.11 |
| RelinkOne | 8 | 103 ns (101-104) | 96 ns (94-98) | 93 ns (92-96) | 0.90 | 0.97 | 0.93 |
| RelinkOne | 64 | 133 ns (127-136) | 124 ns (123-125) | 100 ns (98-105) | 0.75 | 0.81 | 0.93 |
| RelinkOne | 1024 | 139 ns (135-145) | 632 ns (595-636) | 108 ns (105-123) | 0.78 | 0.17 | 4.55 |
| RelinkAll | 8 | 625 ns (612-627) | 635 ns (628-648) | 666 ns (665-677) | 1.07 | 1.05 | 1.02 |
| RelinkAll | 64 | 7.37 µs (7.12-7.40) | 5.04 µs (5.03-5.17) | 5.58 µs (5.42-5.92) | 0.76 | 1.11 | 0.68 |
| RelinkAll | 1024 | 130 µs (127-137) | 87.8 µs (83.2-94.2) | 102 µs (92.0-124) | 0.78 | 1.16 | 0.68 |
| WriteWide | 8 | 309 ns (300-312) | 272 ns (268-278) | 278 ns (269-293) | 0.90 | 1.02 | 0.88 |
| WriteWide | 64 | 2.39 µs (2.12-2.39) | 2.05 µs (2.05-2.14) | 2.19 µs (2.08-2.20) | 0.91 | 1.06 | 0.86 |
| WriteWide | 1024 | 39.8 µs (36.6-40.9) | 35.6 µs (34.2-35.8) | 35.5 µs (34.8-41.4) | 0.89 | 1.00 | 0.89 |
| RotateReads | 8 | 201 ns (197-202) | 230 ns (227-234) | 233 ns (230-237) | 1.16 | 1.01 | 1.14 |
| RotateReads | 64 | 1.25 µs (1.24-1.28) | 3.03 µs (3.03-3.08) | 1.43 µs (1.42-1.55) | 1.14 | 0.47 | 2.43 |
| RotateReads | 1024 | 19.9 µs (19.6-20.9) | 51.2 µs (50.3-54.7) | 22.8 µs (22.7-25.9) | 1.15 | 0.45 | 2.57 |

`--trace-gc` counts over 2.56 million effects, three processes per cell (scavenges / mark-compacts):

| Case | N | base | C | indexed |
| --- | ---: | --- | --- | --- |
| DisposeRoot | 64 | 61/10, 110/1, 110/1 | 96-97/1 | 99/1 |
| DisposeRoot | 1024 | 105/1, 67/9, 61/10 | 83-87/1 | 89-104/1 |
| DisposeInCreationOrder | 64 | 110/1 | 97/1 | 100/1 |
| DisposeInCreationOrder | 1024 | 89-108/1 | 88-106/1 | 82-89/1 |
| DisposeScattered | 64 | 110/1 | 97/1 | 100/1 |
| DisposeScattered | 1024 | 88-102/1 | 84-124/1 | 82-83/1 |

Handles add no GC work. The extra slot array gives up to 3 more scavenges than C at 64 observers; at 1024 the counts
overlap.

### Browser: template component, Chromium

`ranvier-template`, µs per operation (median of three Chromium processes), with the ratio to Solid 2 in the same run.

| Build | Runs | Dispose 128 | /Solid | Dispose 16 /Solid | Mount 128 /Solid | Update 128 /Solid |
| --- | ---: | --- | --- | --- | --- | --- |
| base | 4 | 32.0-36.4 | 1.27-1.48 | 1.23-1.39 | 1.13-1.21 | 1.02-1.06 |
| C | 3 | 27.2-29.4 | 1.12-1.16 | 1.07-1.14 | 1.05-1.12 | 1.05-1.07 |
| indexed | 5 | 30.0-31.4 | 1.20-1.27 | 1.07-1.19 | 1.10-1.14 | 1.05-1.06 |
| indexed, `Int32Array` (`5882947`) | 3 | 33.9-35.5 | 1.39-1.41 | 1.27-1.35 | 1.21-1.25 | 1.05-1.07 |

By medians, indexed handles take 128-row dispose 3.9 µs below master (30.5 µs against 34.4 µs) and 2.1 µs above C (28.4 µs), 1.07× C. The template's teardown removes rows
newest first, which C already answers at its last-slot check, so C pays no scan here. CPU profiles of the two builds
spend most samples in DOM calls and the view layer and at most 1.1% in any one edge function, and they do not single
out where indexed handles spend the extra 2 µs.

### What won and what lost

- **Won against master:** every dispose order at 64 and 1024 (0.61-0.72× .NET, 0.41-0.44× V8), `RelinkOne` (0.73-0.90×),
  wide `RelinkAll` (0.72-0.86× .NET, 0.76-0.78× V8), `WriteWide` on V8 (0.89-0.91×), and browser dispose.
- **Won against C:** the three cases where C loses to master. `DisposeScattered` 1024 is 0.38× C (0.61× master on
  .NET, 0.42× on V8). `RelinkOne` 1024 is 0.27× C on .NET and 0.17× on V8. `RotateReads` at 64 and 1024 is 0.41-0.47× C,
  with no per-run allocation. Dispose at 64 and 1024 on V8 is 0.82-0.93× C.
- **Lost against master:** `RotateReads` at 1.12-1.20× on both runtimes. Every run removes and re-adds every edge, and
  each add and removal carries the handle write, the identity check and, for a reader past its first slot, the
  `TryAddObserver` call. `RelinkAll` at 8 is 1.07-1.10×, as C's is.
- **Lost against C:** `RelinkAll` at 64 and 1024, 1.05× on .NET and 1.11-1.16× on V8. An effect re-reading a wide source
  after another read gets `Unknown` and then makes a second interface call, `AddObserver`, after its own slot scan.
  `WriteWide` 64 on V8 is 1.06×. Browser 128-row dispose is 1.07× C by medians.
- **Small fan-out (finding 8):** at 8 observers every case is within 0.90-1.03× of master, except `RelinkAll` and
  `RotateReads` above.

### Recommendation

Indexed handles win on Fable against master in every browser phase and in every V8 case but one, so by the design's
rule they replace the current model, and the `Link` model is not the next step. They also remove C's three wide-source
losses of 2.5-4.5×, where C is a fallback with known pathologies. C remains ahead on the measured browser workload by
about 2 µs at 128 rows, because that workload tears down in reverse order. Two follow-ups would target the remaining gaps:

- `RelinkAll`: a reader past its first slot could scan its own slots before calling the source, saving the second
  interface call on a wide source, at a cost to narrow readers.
- Browser dispose against C: a profile restricted to the dispose window, which the profiles here do not isolate.

### Not verified

- The browser comparison covers the template component only, and the profiles mix mount and dispose samples.
- `counters.ps1` was not run, and no CI job was changed.
- Retention under Fable: the wide-source retention test is still excluded under Fable.
- No current path removes a reader's edge twice, so the -1 write-backs have no failing test.
