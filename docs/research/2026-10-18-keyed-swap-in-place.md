# Keyed swap: rows kept in place inside a long window — 2026-10-18

## Environment and commits

- Machine: AMD Ryzen 9 9900X (12 cores, 62 GB), Windows 11 10.0.26200. Rider open and idle; timings ran one at a time, with no build running alongside.
- Chromium 153.0.8010.12 (Playwright 1.63.0), Node 26.7.0, Fable 5.18.0 (`--noCache`, Release). Solid 2: `solid-js`, `@solidjs/web`, `@solidjs/compiler` 2.0.0-rc.14.
- Before: `f9f6b97` (master at `2650152` merged into `poc/fable-ranvier-dom`), bundle `generated-keyed-inplace0`. After: the in-place change on top of `f9f6b97`, bundle `generated-keyed-inplace1`. Every timing JSON records `commit: f9f6b97`; the after bundle was built from the uncommitted change.

Raw data in [`framework-performance/investigation/keyed-swap-in-place/`](https://github.com/shayanhabibi/Ranvier/tree/d784929cd9c67eed51bb319d1f3c2b993dcd9b54/docs/research/framework-performance/investigation/keyed-swap-in-place): `inplace0-1..3.json` and `inplace1-1..3.json` (one process each, alternating), and `profile-loop-before.json` (swap1k, 100 prepared steps per mode in one `evaluate`, no forced GC). The status run at `f9f6b97` with every operation is [`poc-status-2026-10-18/status.json`](https://github.com/shayanhabibi/Ranvier/blob/d784929cd9c67eed51bb319d1f3c2b993dcd9b54/docs/research/framework-performance/investigation/poc-status-2026-10-18/status.json).

## Where swap1k stood

The status run at `f9f6b97` (three processes, 15 rounds, a forced GC before every sample): swap1k 2.61× Solid 2 (0.460 against 0.175 ms), remove1k 1.55×, remove10k 0.89×, clear1k 1.89×, create1k 1.67×, armsClear1k 1.96×.

The benchmark swaps rows 1 and 998 of 1,000. The projection's trimmed pass visits only the two changed positions, but its window spans positions 1 to 998. The template list then:

- validated every laid-out row from 0 to 999, and
- looked up the row of every key from 1 to 998 to build the next rows.

Loop profile of swap1k at `f9f6b97`, self time inside the timed step (Ranvier 0.199 ms, Solid 2 0.104 ms per operation):

| Code | Share |
|---|---|
| `Row.ValidateWindow` | 17.8% |
| the region's reconcile closure | 9.5% |
| `Projection.Get` | 9.3% |
| `RowsOf.Enumerate` | 6.1% |
| `RowEntry.Row` | 5.1% |
| `RegionSite.Settle` | 5.0% |
| `KeyMap.Find` | 4.5% |

Under the timing protocol, Ranvier's swap1k is about 0.26 ms slower than in the loop, Solid 2's about 0.07 ms. That part is the first operation after a forced GC ([post-GC note](2026-10-17-arms-clear-and-post-gc.md#the-first-operation-after-a-forced-gc)); the timing protocol's profile attributes most of it to `RowsOf.Stage`, which runs twice per swap.

## The change

When a pass's window keeps its length and spans more than 64 positions (`smallWindow`, the largest window spliced into a copy of the previous rows), the list region compares the previous and current key at each window position:

- The next rows start as a copy of the previous rows; only the positions whose key changed are looked up.
- A template list validates the laid-out rows from one before to one after each run of changed positions, instead of the whole window. Authored lists still validate their item window before the pass.
- Kept rows released since the last layout are replaced at any position, as the guard for rows outside the window does.

Recorded: inside such a window, rows whose key stays at its position are not validated on that pass, in the same way as rows outside the window. A foreign node between two of them raises no error on that pass. Accepted on 2026-10-18.

## Results

Three processes per build, alternating, 3 warmups and 21 shuffled rounds, a forced GC before every sample. Ranvier's median ms `[per-process medians]`:

| Operation | Before | After |
|---|---|---|
| swap1k | 0.490 [0.520, 0.445, 0.490] | 0.245 [0.225, 0.255, 0.245] |
| remove1k | 0.285 [0.285, 0.290, 0.250] | 0.295 [0.295, 0.325, 0.265] |
| remove10k | 0.710 | 0.700 |
| clear1k | 1.805 [2.430, 1.700, 1.805] | 1.910 [1.910, 1.675, 1.990] |
| create1k | 9.220 [9.690, 8.965, 9.220] | 9.350 [10.335, 8.715, 9.350] |
| append1k | 6.545 | 5.450 |
| replace1k | 8.760 | 8.805 |
| update10th1k | 0.100 | 0.105 |

- swap1k halves; the per-process ranges do not overlap. The other operations take paths the change leaves alone, and their ranges overlap.
- Solid 2's swap1k ran its slow mode in four of the six processes (0.695–0.845 ms; 0.210 and 0.260 ms in the other two, 0.175 ms in the status run). The post-GC note found a young collection inside 97% of Solid 2's slow samples; this run did not trace collections. The swap ratios of this run (0.774 → 0.329) are therefore not comparable with the status run's 2.61×. Against the status run's Solid 2 median, after is 1.40×.
- This machine ran slower than during the status run (create1k 8.25 ms there, 9.2 ms here before the change).

Tests: 378 Node DOM tests. New: a two-row swap of 1,000 rows validates at most 6 rows and looks up at most 2 (template list), and looks up at most 2 (authored list); a foreign row before a swapped row, before the row preceding it, after it and after the first swapped row is reported; a 98-position window with a swap and a replacement keeps the unchanged rows' nodes, for both lists. Six mutants of the change are killed: the 64-position threshold, the key comparison, the lookup of changed positions, the validation bounds on each side, and the authored list's comparison.

## Remaining work

- swap1k's post-GC cost: Ranvier pays about 0.26 ms more after a forced GC, Solid 2 about 0.07 ms.
- Creation ratios (create1k 1.67×, append1k 1.55× in the status run) and clear1k (1.89×) need fewer objects and bytes per row.
- armsClear1k (1.96×) and component dispose (1.33–1.43×).

## Reproduction

From `examples/Fable.Ranvier.Playground`, one at a time:

```sh
RanvierCounters=false RANVIER_PERF_OUTPUT=generated-keyed-<bundle> dotnet fable tests/DomTests.fsproj --outDir generated-tests --configuration Release --noCache
RANVIER_PERF_OUTPUT=generated-keyed-<bundle> node node_modules/vite/bin/vite.js build --config benchmarks/frameworks/vite.config.mjs
node benchmarks/frameworks/keyed-rows.mjs --dist=generated-keyed-<bundle> --processes=1 --rounds=21 --shuffle --no-alloc --pairs=ranvier:solid --ops=swap1k,remove1k,remove10k,clear1k,append1k,create1k,replace1k,update10th1k --label=<bundle>-<n> --out=<dir>
node benchmarks/frameworks/keyed-rows.mjs --bundle --profile --repeats=100 --ops=swap1k --modes=ranvier,solid --save-cpu=<dir> --out=<dir>
node benchmarks/frameworks/keyed-rows.mjs --build --processes=3 --rounds=15 --shuffle --label=status --out=<dir>
```
