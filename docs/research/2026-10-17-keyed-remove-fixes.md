# Keyed remove fixes and the remaining remove10k gap — 2026-10-17

## Environment and commits

- Machine: AMD Ryzen 9 9900X (12 cores, 62 GB), Windows 11 10.0.26200. Timings ran one at a time, with no build running alongside.
- Chromium 153.0.8010.12 (Playwright 1.63.0), Node 26.7.0, Fable 5.18.0 (`--noCache`, Release). Solid 2: `solid-js`, `@solidjs/web`, `@solidjs/compiler` 2.0.0-rc.14.
- Branch `poc/fable-ranvier-dom`. Each step is one bundle. A step measured before its commit records its uncommitted `Mounting.fs` in `dirty` and its parent in `commit`.

| Bundle | Code | Change |
|---|---|---|
| base | `9c531c0` | the keyed rows harness on the PoC branch |
| fix1 | `a873c73` | `Row.Layout` writes the laid-out rows in place; unchecked array reads and writes in the region |
| fix3 | `f7fe47c` | the window scans use locals instead of `FSharpRef` cells |
| harness | `21d8ba6` | the harness removes a row with `slice` and `splice`, as the Solid 2 fixture does |
| core | `b6a5247` | cherry-pick of `1faeb63` (#78): memo-free projections skip the row observer scan |
| fix4 | `f4a8847` | removed rows stay in the previous layout instead of being compacted out |
| fix5 | `cafa799` | the next-row builder takes rows outside the changed window without reading `Row.Removed`, and replaces only the rows released since the last layout; `Row.Layout` starts after the unchanged prefix and suffix |
| fix6 | `f31aecf` | a changed window of at most 64 rows is spliced into a copy of the previous rows |
| fix7 | `8d72762` | `Row.Index` is gone: `Row.Layout` maps the previous rows of the changed window to their positions, and no longer writes an index into every row after the window |
| fix8 | `eb76a07` | a changed window of at most 64 rows is spliced into the laid-out rows |
| range | `a7e97a1` | cherry-pick of `7753f2f` (#89): `Projection.ChangedRange` returns the window of keys a trimmed pass changed |
| fix9 | `6d9f9d4` | a template list takes its changed window from `ChangedRange` instead of comparing every item of the previous and current arrays, and keeps no copy of its items; its validation runs after the pass, before the flush |
| version | `878ffe9` | cherry-pick of `ba65be1` (#89): `KeysVersion` and `ChangedSince` replace `ChangedRange`, which held its key arrays through JS `WeakRef`s; measured with the `Mounting.fs` of `a7e97a1` |
| fix10 | `ae0e16a` | the template list reads its window through `ChangedSince`, and compares the two key arrays when the projection holds no window |

Raw data in [`framework-performance/investigation/keyed-remove-fixes/`](https://github.com/ranvier-org/Ranvier/tree/d784929cd9c67eed51bb319d1f3c2b993dcd9b54/docs/research/framework-performance/investigation/keyed-remove-fixes):

- `<pair>/<bundle>-<n>.json`: three alternating processes per bundle, 3 warmups and 101 shuffled rounds, a forced CDP GC before every sample (the protocol of [the post-GC note](2026-10-17-arms-clear-and-post-gc.md)). `base-fix1` also holds `--pin` runs.
- `profile-*.json`: CPU profiles of 100 remove10k operations (`--protocol=sample`), 50 µs sampling, unminified bundle; `pinned` keeps one 10,000-row list mounted.
- `alloc.json`: sampled allocations of remove1k and remove10k at `f4a8847`.

## Each step against the previous one

Ranvier's median ms over all samples of three processes `[per-process medians]`, then Solid 2's remove10k median in the same runs. Each row compares a bundle with the one before it; absolute times are comparable within a row only.

| Pair | remove10k | remove1k | swap1k | clear1k | Solid 2 remove10k |
|---|---|---|---|---|---|
| base → fix1 | 2.975 [3.09, 2.96, 2.92] → 2.490 [2.52, 2.46, 2.48] | 0.350 → 0.315 | 0.340 → 0.310 | — | 0.635 / 0.635 |
| fix1 → fix3 | 2.505 [2.61, 2.49, 2.49] → 1.795 [1.82, 1.77, 1.80] | 0.335 → 0.245 | 0.320 → 0.310 | — | 0.660 / 0.665 |
| fix3 → harness | 1.775 [1.79, 1.75, 1.80] → 1.710 [1.81, 1.67, 1.68] | 0.245 → 0.225 | 0.300 → 0.295 | — | 0.630 / 0.645 |
| harness → core | 1.745 [1.78, 1.70, 1.75] → 1.505 [1.47, 1.51, 1.52] | 0.230 → 0.210 | 0.300 → 0.290 | — | 0.655 / 0.660 |
| core → fix4 | 1.495 [1.47, 1.53, 1.49] → 1.490 [1.51, 1.51, 1.45] | 0.205 → 0.200 | 0.290 → 0.275 | 1.080 → 1.050 | 0.660 / 0.655 |
| fix4 → fix5 | 1.460 [1.47, 1.49, 1.44] → 0.970 [0.96, 0.97, 0.99] | 0.200 → 0.160 | 0.275 → 0.265 | 1.040 → 1.050 | 0.665 / 0.665 |
| fix5 → fix6 | 0.955 [0.93, 0.98, 0.96] → 0.970 [1.07, 0.94, 0.90] | 0.165 → 0.155 | 0.270 → 0.275 | 1.050 → 1.045 | 0.645 / 0.665 |
| fix6 → fix7 | 1.070 [1.06, 1.10, 1.05] → 0.825 [0.83, 0.83, 0.81] | 0.145 → 0.155 | 0.270 → 0.270 | 1.015 → 1.035 | 0.660 / 0.655 |
| fix7 → fix8 | 0.830 [0.83, 0.84, 0.82] → 0.770 [0.78, 0.76, 0.77] | 0.150 → 0.140 | 0.265 → 0.270 | 1.040 → 1.025 | 0.660 / 0.645 |
| range → fix9 | 0.735 [0.77, 0.73, 0.73] → 0.645 [0.65, 0.65, 0.64] | 0.150 → 0.140 | 0.260 → 0.250 | 0.955 → 0.960 | 0.690 / 0.680 |
| version → fix10 | 0.710 [0.71, 0.73, 0.70] → 0.655 [0.67, 0.65, 0.65] | 0.155 → 0.140 | 0.255 → 0.225 | 0.995 → 1.010 | 0.705 / 0.675 |

append1k and create1k (measured from fix4 and fix6 on) stay within 1.5% at every step: append1k 4.615 → 4.335 ms, create1k 5.965 → 5.880 ms.

- remove10k falls from 2.98 to 0.77 ms against Solid 2's 0.65 (4.7× → 1.19×), remove1k from 0.35 to 0.14 ms (Solid 2 0.115–0.180 across these runs), swap1k from 0.34 to 0.27 ms.
- fix9 takes remove10k from 0.735 to 0.645 ms, below Solid 2's 0.680 in the same runs; every fix9 process is faster than every `range` process. append1k (4.215 → 4.145 ms) and create1k (5.760 → 5.760 ms) stay within 1.7%. fix9 was measured with the template path alone; the authored-row path, which keeps the item comparison and validates before the pass, was added before the commit and does not run in a template list.
- fix10, on the version-based core, takes remove10k from 0.710 to 0.655 ms and swap1k from 0.255 to 0.225 ms; for both, every fix10 process is faster than every `version` process. append1k (4.265 → 4.205 ms), create1k (5.910 → 5.985 ms) and clear1k (0.995 → 1.010 ms) stay within their process spreads.
- An authored row's nested mount removes its nodes inside the pass, so authored lists still compare their items and validate before the pass. A template list's retired rows keep their nodes until the flush, so its validation after the pass sees the same nodes. On a validation failure, a template list's pass has already run its retired rows' cleanups; their nodes stay until the next flush.
- fix3 and fix5 are the largest steps. fix5's builder had read `Row.Removed` from every kept row, 13% of the pinned profile at `b6a5247`.
- The harness change also applies to Solid 2's fixture, whose times are unchanged within 0.015 ms.
- The core fix removed an O(n) scan of the projection's entries per pass, about 0.24 ms of remove10k.
- fix6 moves the unpinned remove10k by less than its process spread; it is visible in the pinned profile (0.503 → 0.42–0.44 ms per operation). fix7's remove1k rise of 0.010 ms is consistent across its processes and reversed by fix8.
- No test reaches fix5's replacement of a released kept row: a keyed projection keeps the row of an item that keeps its reference and its offset from the nearer end, even after the item's key changes in place. The replacement is a guard, as the `Row.Removed` check it replaced was.

## Allocation

`alloc.json` (`f4a8847`, sampled allocations over 10 operations each, KiB per operation):

| Operation | Ranvier | Solid 2 |
|---|---|---|
| remove1k | 26.0 | 30.9 |
| remove10k | 238.0 | 273.3 |

Ranvier's remove10k allocations are `slice` (117 KiB: the harness's new array, the region's copy of the items and the projection's copy), `KeyedProjection.Trim` (39 KiB), `KeyedProjection.take` (39 KiB) and the next-row builder (39 KiB). Solid 2's are `flattenArray` (116 KiB), one closure (114 KiB) and `slice` (43 KiB). Ranvier allocates less per removal than Solid 2. fix6 replaces the builder's array with a copy of the previous rows of the same size.

## Where the remaining time goes

Retained JavaScript heap per row of a mounted list (`Runtime.getHeapUsage`, median of three processes): Ranvier 1,379 bytes at 1,000 rows and 1,232 at 10,000 at `eb76a07`; Solid 2 1,061 and 1,046. No step changed Ranvier's figure by more than 5 bytes per row.

remove10k CPU profiles, ms per operation:

| Profile | Ranvier | Ranvier GC | Solid 2 |
|---|---|---|---|
| unpinned, `f4a8847` | 3.245 | 0.651 | 0.981 |
| unpinned, `eb76a07` | 1.318 | 0.170 | 0.871 |
| pinned, `b6a5247` | 0.566 | — | not profiled |
| pinned, `cafa799` | 0.503 | — | not profiled |
| pinned, `8d72762` | 0.278 | — | not profiled |
| pinned, `eb76a07` | 0.297 | 0.026 | not profiled |

The unpinned operation follows the setup of a fresh 10,000-row list. At `f4a8847`, 20% of it was garbage collection; at `eb76a07` the collection takes 0.170 ms (13%). The kept-row reads and index writes that fix5 and fix7 remove touched every row object after the window. The pinned profiles vary by about 0.02 ms between runs.

Self time of the unpinned profile at `eb76a07`:

| Code | Share |
|---|---|
| `SourceList.EndRun` (V8's label for the region effect body) | 18.3% |
| `KeyedProjection.Trim` | 15.8% |
| garbage collector | 12.9% |
| `RowsOf.Enumerate` | 8.6% |
| `item` (bounds-checked array reads) | 6.8% |
| the item window scan | 5.5% |
| `Operators_IsNull` | 2.9% |

## Remaining work

- Core, each on its own branch from master: `KeyedProjection.Trim`, `take` and `RowsOf.Enumerate` on a pass that changes one key; `Operators_IsNull` calls (103 in the core bundle); bounds-checked `item` reads; ref cells in core loops.
- Fewer objects and bytes per row, for the creation ratios (create1k 1.66× and append1k 1.36× in the last pair) and clear1k (1.68–1.91×).
- swap1k stays at 0.27 ms against Solid 2's 0.11–0.17 ms.

## Reproduction

From `examples/Fable.Ranvier.Playground`, one at a time, alternating the two bundles of a pair:

```sh
RanvierCounters=false RANVIER_PERF_OUTPUT=generated-keyed-<bundle> dotnet fable tests/DomTests.fsproj --outDir generated-tests --configuration Release --noCache
RANVIER_PERF_OUTPUT=generated-keyed-<bundle> node node_modules/vite/bin/vite.js build --config benchmarks/frameworks/vite.config.mjs
node benchmarks/frameworks/keyed-rows.mjs --dist=generated-keyed-<bundle> --processes=1 --rounds=101 --shuffle --no-alloc --pairs=ranvier:solid --ops=swap1k,remove1k,remove10k,clear1k,append1k,create1k --label=<bundle><n> --out=<dir>
node benchmarks/frameworks/keyed-rows.mjs --build --profile --protocol=sample --repeats=100 --pin --ops=remove10k --modes=ranvier --out=<dir>
node benchmarks/frameworks/keyed-rows.mjs --build --profile --protocol=sample --repeats=100 --ops=remove10k --modes=ranvier,solid --out=<dir>
node benchmarks/frameworks/keyed-rows.mjs --bundle --profile --repeats=5 --ops=remove10k,remove1k --modes=ranvier,solid --out=<dir>
```
