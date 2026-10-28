# Keyed rows: an owner's first cleanup without arrays — 2026-10-18

## Environment and commits

- Machine: AMD Ryzen 9 9900X (12 cores, 62 GB), Windows 11 10.0.26200. Rider open and idle; timings ran one at a time, with no build running alongside.
- Chromium 153.0.8010.12 (Playwright 1.63.0), Node 26.7.0, Fable 5.18.0 (`--noCache`, Release). Solid 2: `solid-js`, `@solidjs/web`, `@solidjs/compiler` 2.0.0-rc.14.
- Before: `c948406`, bundle `generated-keyed-inplace1`. After: the core change of [#93](https://github.com/ranvier-org/Ranvier/pull/93) (`e6448b4`) applied uncommitted to `d2ec3c0`, bundle `generated-keyed-owner1`. The timing JSONs record the checked-out commit.

Raw data in [`framework-performance/investigation/owner-first-cleanup/`](https://github.com/ranvier-org/Ranvier/tree/d784929cd9c67eed51bb319d1f3c2b993dcd9b54/docs/research/framework-performance/investigation/owner-first-cleanup):

- `heap-before-snapshot.json`, `heap-before-holders.json`, `heap-after-holders.json`: `keyed-heap.mjs`, objects new after `create(rows)` by kind and by holding object.
- `alloc-before.json`: `keyed-rows.mjs --profile`, sampled allocation bytes of create1k and clear1k by allocating function.
- `inplace1-1..3.json`, `owner1-1..3.json`: one process each, alternating, 3 warmups and 21 shuffled rounds, a forced GC before every sample.

## What a row holds

Heap snapshot diff of 1,000 created rows: Ranvier's rows held 1,140 JS bytes each, Solid 2's 905. By holding object, the largest item was the row owner's cleanup storage:

| Holder | Objects per row | Bytes per row |
|---|---|---|
| `Owner.cleanups`, `Owner.cleanupOrder` (lists and element stores) | 4 | 184 |
| a closure context of the row's cleanup | 1 | 64 |
| `Signal.observers` (`ObserverSet`, its list and store), two signals | 6 | 104 |
| `$$ranvier` delegation list of the `<a>` | 2 | 49 |

A row's owner holds one cleanup, the release registered by the list region's `buildRow`. V8 gives an array's first push a 17-slot store, so each of the two lists carried a 76-byte store for that one entry.

## The change

[#93](https://github.com/ranvier-org/Ranvier/pull/93): an owner keeps its first cleanup since the last teardown in two fields; the lists are allocated by the second cleanup.

## Results

Ranvier's median ms `[per-process medians]`, and the median ratio to Solid 2:

| Operation | Before | After | × Solid 2 before → after |
|---|---|---|---|
| replace1k | 5.745 [5.735, 5.825, 5.745] | 4.885 [4.845, 4.895, 4.885] | 1.61 → 1.31 |
| append1k | 4.095 | 3.525 | 1.37 → 1.12 |
| clear10k | 10.505 [10.570, 10.505, 10.180] | 9.465 [7.085, 9.470, 9.465] | 2.08 → 1.66 |
| clear1k | 1.005 | 0.975 | 1.83 → 1.74 |
| create10k | 52.165 | 50.945 | 1.43 → 1.36 |
| create1k | 6.110 [6.110, 6.030, 6.205] | 5.905 [5.930, 5.825, 5.905] | 1.53 → 1.53 |
| armsClear1k | 1.580 | 1.600 | 1.64 → 1.51 |
| armsCreate1k | 10.000 [10.000, 10.065, 9.805] | 10.255 [10.255, 10.900, 10.055] | 1.54 → 1.58 |
| swap1k / remove1k / update10th1k | 0.140 / 0.150 / 0.055 | 0.135 / 0.150 / 0.060 | 1.22 / 1.35 / 0.41 → 1.17 / 1.38 / 0.43 |

Retained heap per row (`getHeapUsage`): 1,365 → 1,179 B at 1k rows and 1,228 → 1,073 B at 10k, against Solid 2's 1,058 and 1,051 B. The snapshot diff gives 964 JS bytes per row after, against 905.

- The machine ran faster than in the [10-18 status run](https://github.com/ranvier-org/Ranvier/blob/d784929cd9c67eed51bb319d1f3c2b993dcd9b54/docs/research/framework-performance/investigation/poc-status-2026-10-18/status.json) (create1k 6.1 ms here, 8.25 ms there); compare ratios across days, not times.
- armsCreate1k's per-process ranges overlap.

## Remaining per-row objects

After the change, by holding object: the closure context of the row's cleanup (64 B), the two signals' observer sets (104 B), the `<a>`'s delegation list (63 B), and two strings per row held by an array at indices 8 and 9 (93 B). In the create1k allocation sample (before the change), the template's `materialise` is the largest allocator (309 KB per 1,000 rows), then `AddCleanup` (201 KB, removed by #93), `Effect` (120 KB), `RowsOf.CreateOnce` (114 KB) and `collect` (112 KB). clear1k allocates 118 KB per 1,000 rows against Solid 2's 22 KB, mostly in native `push`, `add` and `delete` during `Owner.TearDown`.

## Reproduction

From `examples/Fable.Ranvier.Playground`, one at a time:

```sh
RanvierCounters=false dotnet fable tests/DomTests.fsproj --outDir generated-tests --configuration Release --noCache
node benchmarks/frameworks/keyed-heap.mjs --counts=1000,10000 --processes=1 --label=<label> --out=<dir>
RANVIER_PERF_OUTPUT=generated-keyed-<bundle> node node_modules/vite/bin/vite.js build --config benchmarks/frameworks/vite.config.mjs
node benchmarks/frameworks/keyed-rows.mjs --dist=generated-keyed-<bundle> --processes=1 --rounds=21 --shuffle --no-alloc --pairs=ranvier:solid --ops=create1k,create10k,replace1k,append1k,clear1k,clear10k,swap1k,remove1k,update10th1k,armsCreate1k,armsClear1k --label=<bundle>-<n> --out=<dir>
RANVIER_PERF_OUTPUT=generated-keyed-alloc RANVIER_PERF_PROFILE=true node node_modules/vite/bin/vite.js build --config benchmarks/frameworks/vite.config.mjs
node benchmarks/frameworks/keyed-rows.mjs --dist=generated-keyed-alloc --profile --repeats=40 --ops=create1k,clear1k --modes=ranvier,solid --label=alloc --out=<dir>
```
