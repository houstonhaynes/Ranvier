# Observer sets and source lists: a shared empty array and a one-slot first growth — 2026-10-18

## Environment and commits

- Machine: AMD Ryzen 9 9900X (12 cores, 62 GB), Windows 11 10.0.26200. Rider open and idle; timings ran one at a time, with no build running alongside.
- Chromium 153.0.8010.12 (Playwright 1.63.0), Node 26.7.0, Fable 5.18.0 (`--noCache`, Release). Solid 2: `solid-js`, `@solidjs/web`, `@solidjs/compiler` 2.0.0-rc.14.
- Before: `5c987ea` (`poc/fable-ranvier-dom`), bundle `generated-keyed-slots0`. After: `5c987ea` with `src/Ranvier/Core.fs` from `7a8e68c` (`perf/observer-set-slots`, from master `32c3a60`), bundle `generated-keyed-slots1`. Every JSON records `commit: 5c987ea`.

Raw data in [`framework-performance/investigation/observer-set-slots/`](https://github.com/ranvier-org/Ranvier/tree/d784929cd9c67eed51bb319d1f3c2b993dcd9b54/docs/research/framework-performance/investigation/observer-set-slots): `heap-slots0.json` and `heap-slots1.json` (`keyed-heap.mjs`), `slots0-1..3.json` and `slots1-1..3.json` (all operations, one process each, alternating), `rerun-slots0-1..4.json` and `rerun-slots1-1..4.json` (create1k, replace1k and update10th1k, 31 rounds).

## What a row retained

Under Fable, `Array.empty` compiles to `new Array(0)`, a fresh 16 B array per call. Each row held three of them, plus a four-slot backing store for the single observer of one of them:

| Holder | Bytes per row |
|---|---|
| `ObserverSet.items`, one per signal, two signals | 32 |
| the observed signal's backing store (four slots, one used) | 24 |
| `SourceList.rest` of the row's effect (one source, held in `first`) | 16 |

## The change

`ObserverSlots.none` and `SourceSlots.none` are module-level zero-length arrays. An observer set holds `ObserverSlots.none` until its first observer, and a source list holds `SourceSlots.none` until its second source. The first growth of an observer set allocates one slot; later growths keep `max 4 (count * 2)`. On .NET, `Array.empty` was already shared, so the .NET change is the one-slot first growth.

## Results

JS heap by kind (`keyed-heap.mjs`), bytes per row: 783 → 739 at 1,000 rows, 848 → 804 at 10,000; Solid 2 905 and 936. Objects per row: 38.6 → 36.6 at 1,000, 28.2 → 26.2 at 10,000. The observer and source arrays now come to 28 B per row: the observed signal's `items` (16 B) and its one-slot backing store (12 B).

Retained heap after `create(rows)`, median of the three processes per build (bytes per row):

| Rows | Before | After | Solid 2 |
|---|---|---|---|
| 1,000 | 1,046 | 994 | 1,048 |
| 10,000 | 950 | 905 | 1,053 |

Timings: three processes per build, alternating, 3 warmups and 21 shuffled rounds, a forced GC before every sample. Ranvier's median ms `[per-process medians]` and the median ratio to Solid 2:

| Operation | Before | After |
|---|---|---|
| create1k | 6.225 [6.17, 6.23, 6.28] 1.50× | 6.035 [6.09, 5.97, 6.03] 1.42× |
| create10k | 52.425 [51.53, 52.42, 52.97] 1.39× | 51.935 [51.94, 52.38, 51.56] 1.34× |
| replace1k | 5.425 [5.19, 5.43, 5.43] 1.42× | 5.735 [5.65, 5.84, 5.74] 1.39× |
| append1k | 4.040 [3.85, 4.16, 4.04] 1.16× | 3.930 [3.99, 3.93, 3.91] 1.10× |
| clear1k | 1.085 [1.08, 1.10, 1.06] 1.69× | 1.110 [1.11, 1.17, 1.08] 1.72× |
| clear10k | 10.395 [10.24, 10.39, 10.76] 1.92× | 10.055 [10.05, 10.18, 10.02] 1.89× |
| swap1k | 0.160 [0.16, 0.16, 0.18] 1.23× | 0.165 [0.16, 0.19, 0.17] 1.32× |
| remove1k | 0.180 [0.18, 0.19, 0.18] 1.55× | 0.185 [0.18, 0.20, 0.19] 1.61× |
| update10th1k | 0.070 [0.07, 0.07, 0.09] 0.43× | 0.080 [0.08, 0.08, 0.08] 0.47× |
| armsCreate1k | 11.970 [12.09, 11.89, 11.97] 1.63× | 11.175 [11.18, 11.19, 11.09] 1.60× |
| armsClear1k | 2.165 [2.17, 2.18, 2.13] 1.66× | 1.925 [1.93, 2.02, 1.92] 1.58× |

replace1k's per-process ranges were disjoint in this run, so it was re-run with create1k and update10th1k, four processes per build, alternating, 31 rounds:

| Operation | Before | After |
|---|---|---|
| create1k | 6.440 [6.66, 6.20, 6.32, 6.56] 1.54× | 6.515 [6.16, 6.54, 6.49, 6.56] 1.56× |
| replace1k | 5.537 [5.68, 5.48, 5.43, 5.59] 1.37× | 5.710 [5.84, 5.54, 5.73, 5.69] 1.30× |
| update10th1k | 0.070 [0.07, 0.07, 0.06, 0.07] 0.40× | 0.070 [0.09, 0.07, 0.07, 0.06] 0.41× |

- In the re-run every range overlaps, and replace1k's ratio to Solid 2 is lower after the change than before.
- armsCreate1k and armsClear1k have disjoint ranges in the first run, both faster after the change; neither was re-run.

Tests on `perf/observer-set-slots`: 1,171 .NET and 86 C# tests, `test-fable`. The change alters capacity and allocation only, so it has no behavioural mutant.

## Remaining work

- The `<a>`'s delegation list (49 B per row) and the two strings per row held by the template state array.
- Creation and clear time: create1k 1.42–1.56× and clear1k 1.72× with retained bytes below Solid 2's.

## Reproduction

From `examples/Fable.Ranvier.Playground`, one at a time:

```sh
RanvierCounters=false RANVIER_PERF_OUTPUT=generated-keyed-<bundle> dotnet fable tests/DomTests.fsproj --outDir generated-tests --configuration Release --noCache
RANVIER_PERF_OUTPUT=generated-keyed-<bundle> node node_modules/vite/bin/vite.js build --config benchmarks/frameworks/vite.config.mjs
node benchmarks/frameworks/keyed-heap.mjs --counts=1000,10000 --processes=1 --label=heap-<bundle> --out=<dir>
node benchmarks/frameworks/keyed-rows.mjs --dist=generated-keyed-<bundle> --processes=1 --rounds=21 --shuffle --no-alloc --pairs=ranvier:solid --ops=create1k,create10k,replace1k,append1k,clear1k,clear10k,swap1k,remove1k,update10th1k,armsCreate1k,armsClear1k --label=<bundle>-<n> --out=<dir>
```
