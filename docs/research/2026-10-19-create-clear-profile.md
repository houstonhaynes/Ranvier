# Keyed create and clear: where the time goes, an ordered `ReleasedAll` and a clearing pass that owns its markers' contents — 2026-10-19

The probe scripts this note runs were removed from the branch; they are at commit [d784929](https://github.com/ranvier-org/Ranvier/tree/d784929cd9c67eed51bb319d1f3c2b993dcd9b54).

## Environment and commits

- Machine: AMD Ryzen 9 9900X (12 cores, 62 GB), Windows 11 10.0.26200. Rider open and idle; profiles and timings ran one at a time, with no build running alongside.
- Chromium 153.0.8010.12 (Playwright 1.63.0), Node 26.7.0, Fable 5.18.0 (`--noCache`, Release). Solid 2: `solid-js`, `@solidjs/web`, `@solidjs/compiler` 2.0.0-rc.14.
- Profile: `d264aed` (`poc/fable-ranvier-dom`, master `7853137` merged in), bundle `generated-keyed-profile` (unminified, `RANVIER_PERF_PROFILE`).
- Timings: before `d264aed`, bundle `generated-keyed-rel0`; after `d264aed` with the `ReleasedAll` change, bundle `generated-keyed-rel1`. Both bundles were built before the timings, and every JSON records `commit: d264aed` and the change as uncommitted.

Raw data in [`framework-performance/investigation/released-all/`](https://github.com/ranvier-org/Ranvier/tree/d784929cd9c67eed51bb319d1f3c2b993dcd9b54/docs/research/framework-performance/investigation/released-all): `profile-buckets.json` (`keyed-profile-buckets.mjs`: buckets, functions by self and by total time) and `rel0-1..3.json`, `rel1-1..3.json` (one process each, alternating).

## Where the time goes

Loop protocol, 200 fresh lists per operation, 50 µs sampling, the samples inside `measuredRun`. Sampled ms per operation:

| Operation | Ranvier | Solid 2 |
|---|---|---|
| create1k | 3.72 | 2.69 |
| replace1k | 4.91 | 3.36 |
| clear1k | 0.68 | 0.45 |
| clear10k | 7.12 | 5.62 |

create1k:

| Function | Ranvier | Solid 2 |
|---|---|---|
| `cloneNode` | 0.92 | 0.82 |
| `insertBefore` | 0.38 (the staging fragment, once per pass) | 0.37 (each row) |
| `appendChild` | 0.32 (each row, into the staging fragment) | — |
| the node walk (`collect`; Solid 2's row function) | 0.39 | 0.37 |
| GC | 0.48 | 0.33 |
| core graph and projection pass | 0.54 | — |

- Each row enters the DOM twice: `RowBuilder` appends it to the region's staging fragment, and `Row.Layout` inserts the fragment before the region's end. Solid 2 inserts each row once.

clear10k:

| Function | Ranvier | Solid 2 |
|---|---|---|
| removing the region's nodes (`Row.ClearBetween`; Solid 2's `cleanChildren`) | 3.83 | 3.62 |
| row and owner disposal | 1.01 | 1.32 |
| `Row.ValidateWindow` over every row before the clear | 0.57 | — |
| `RegionSite.ReleasedAll` | 0.39 | — |
| `KeyMap.Find` and `KeyMap.Remove` in `Projection.RetireRemoved` | 0.40 | — |

- The removal itself costs about as much as Solid 2's. The extra time is the validation before the clear, the `ReleasedAll` guard, and the projection's lookup and removal of each retired key.
- The validation reports a foreign node between the region's markers instead of deleting it with the region's contents.

## The change

`RegionSite.ReleasedAll` compares the held rows with the live laid-out rows in one walk when they are in the same order, which holds for a clear of a list in creation order. Any other order compares through a set, as before.

## Results

Ranvier's median ms `[per-process medians]` and the median ratio to Solid 2, three processes per build, 3 warmups and 21 shuffled rounds, a forced GC before every sample:

| Operation | Before | After |
|---|---|---|
| create1k | 8.150 [8.17, 8.15, 8.09] 1.67× | 8.175 [7.96, 8.18, 8.25] 1.56× |
| clear1k | 1.550 [1.52, 1.69, 1.55] 1.87× | 1.555 [1.56, 1.64, 1.48] 1.37× |
| clear10k | 8.980 [8.98, 9.28, 8.94] 1.61× | 8.730 [8.57, 9.02, 8.73] 1.53× |
| remove1k | 0.285 [0.28, 0.29, 0.29] 0.94× | 0.270 [0.26, 0.28, 0.27] 1.17× |
| armsClear1k | 3.175 [3.18, 3.05, 3.24] 1.81× | 3.240 [2.89, 3.31, 3.24] 1.88× |

- Absolute times in this run are about 35% above the 2026-10-18 runs (create1k 8.15 ms against 6.04 ms), with Solid 2's slower too, so only ratios compare across days.
- clear10k is 0.25 ms faster and every per-process median is lower, but the ranges overlap. The profile predicts 0.39 ms.
- clear1k's and remove1k's ratios move with Solid 2's medians; Ranvier's own ranges overlap.

Tests: 380 Node DOM tests. New: a clear after a swap removes the rows in one operation. Both mutants are killed: the set comparison returning false (the new test) and the ordered comparison returning false (four clear tests).

## A clearing pass owns every node between its markers

A pass that empties a template `each` region, with every live laid-out row held for release, skips the validation of the laid-out rows and removes every node between the markers in one operation, foreign nodes included, without a report. A marker moved to another parent is still reported: `RegionSite.Parent` raises "Keyed-list boundaries were externally mutated" at the start of every pass. `Keyed.array` regions keep their row-by-row release.

Before: `6e6c365` (the ordered `ReleasedAll`), bundle `generated-keyed-clr0`; after: `6e6c365` with the change, bundle `generated-keyed-clr1`. Raw data in [`framework-performance/investigation/clear-markers/`](https://github.com/ranvier-org/Ranvier/tree/d784929cd9c67eed51bb319d1f3c2b993dcd9b54/docs/research/framework-performance/investigation/clear-markers), `clr0-1..3.json` and `clr1-1..3.json`, same protocol as above:

| Operation | Before | After |
|---|---|---|
| clear1k | 1.600 [1.58, 1.67, 1.60] 1.54× | 1.615 [1.47, 1.62, 1.62] 1.84× |
| clear10k | 9.350 [9.48, 9.04, 9.35] 1.50× | 8.425 [8.63, 8.33, 8.43] 1.30× |
| remove1k | 0.270 [0.26, 0.27, 0.28] 1.02× | 0.265 [0.28, 0.27, 0.25] 1.06× |
| armsClear1k | 3.345 [3.35, 3.40, 3.34] 1.90× | 3.555 [3.56, 2.94, 3.78] 1.76× |

- clear10k is 0.93 ms faster, with disjoint per-process ranges; the profile predicts 0.57 ms for the validation.
- clear1k's Ranvier ranges overlap; its ratio follows Solid 2's medians (1.04 → 0.81 ms).

Tests: 383 Node DOM tests. New: a clear deletes a foreign node between the markers and reports nothing, in a list alone in its parent and between siblings; a clear after the end marker moves to another parent reports the boundaries. Restoring the row validation fails both foreign-node tests.

## Remaining work

- The staging fragment's second insertion on create (0.32 ms of 3.72). Building a pass into an empty region straight between its markers, with a failed pass releasing its rows there, saves nothing: create1k 5.450 [5.37, 5.45, 5.48] → 5.425 [5.56, 5.43, 5.38] ms, every operation's ranges overlapping (`b6f35f3` with and without the change, bundles `generated-keyed-dir0`, `generated-keyed-dir1`; [`investigation/direct-create/`](https://github.com/ranvier-org/Ranvier/tree/d784929cd9c67eed51bb319d1f3c2b993dcd9b54/docs/research/framework-performance/investigation/direct-create), create1k, replace1k, clear1k, remove1k and armsClear1k). Each row's insertion into the live parent costs what the fragment append and the single insertion cost. The change is not kept.
- `Projection.RetireRemoved`'s lookup of each removed key (0.26 ms at 10,000 rows): [#96](https://github.com/ranvier-org/Ranvier/pull/96) retired the entries found by the diff's walk. Timed on `c7a8d93` with and without its `Projections.fs` (bundles `generated-keyed-ret0`, `generated-keyed-ret1`; [`investigation/retire-entries/`](https://github.com/ranvier-org/Ranvier/tree/d784929cd9c67eed51bb319d1f3c2b993dcd9b54/docs/research/framework-performance/investigation/retire-entries)): clear10k 8.485 [8.53, 8.49, 8.23] → 8.445 [8.45, 8.51, 8.42] ms, every operation's ranges overlapping. #96 was closed unmerged: its CI counters failed the gate, with Node keyed-insert (memo-free rows) at +2.08% instructions and keyed-remove unchanged on .NET and +0.9% on Node.

## Reproduction

From `examples/Fable.Ranvier.Playground`, one at a time:

```sh
node benchmarks/frameworks/keyed-rows.mjs --build --profile --repeats=200 --ops=create1k,replace1k,clear1k,clear10k --pairs=ranvier:solid --label=prof-x200 --save-cpu=<dir>/cpu --out=<dir>
node benchmarks/frameworks/keyed-profile-buckets.mjs <dir>/cpu generated-keyed-profile/assets/<bundle>.js.map --out=<dir>/buckets.json
RanvierCounters=false RANVIER_PERF_OUTPUT=generated-keyed-<bundle> dotnet fable tests/DomTests.fsproj --outDir generated-tests --configuration Release --noCache
RANVIER_PERF_OUTPUT=generated-keyed-<bundle> node node_modules/vite/bin/vite.js build --config benchmarks/frameworks/vite.config.mjs
node benchmarks/frameworks/keyed-rows.mjs --dist=generated-keyed-<bundle> --processes=1 --rounds=21 --shuffle --no-alloc --pairs=ranvier:solid --ops=create1k,clear1k,clear10k,remove1k,armsClear1k --label=<bundle>-<n> --out=<dir>
```
