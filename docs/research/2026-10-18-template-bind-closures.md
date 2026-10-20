# Template binding: closures that retain the state array alone — 2026-10-18

## Environment and commits

- Machine: AMD Ryzen 9 9900X (12 cores, 62 GB), Windows 11 10.0.26200. Rider open and idle; timings ran one at a time, with no build running alongside.
- Chromium 153.0.8010.12 (Playwright 1.63.0), Node 26.7.0, Fable 5.18.0 (`--noCache`, Release). Solid 2: `solid-js`, `@solidjs/web`, `@solidjs/compiler` 2.0.0-rc.14.
- Before: `7478d33` (master at `32c3a60`, #93, merged into `poc/fable-ranvier-dom`), bundle `generated-keyed-ctx0`. After: the change on top of `7478d33`, bundle `generated-keyed-ctx1`. Every JSON records `commit: 7478d33`; the after bundle was built from the uncommitted change.

Raw data in [`framework-performance/investigation/template-bind-closures/`](https://github.com/shayanhabibi/Ranvier/tree/d784929cd9c67eed51bb319d1f3c2b993dcd9b54/docs/research/framework-performance/investigation/template-bind-closures): `heap-ctx0.json` and `heap-ctx1.json` (`keyed-heap.mjs`, objects new after `create(rows)` by kind and by holding object), `ctx0-1..3.json` and `ctx1-1..3.json` (timings, one process each, alternating).

## What a row retained

`bind` was inlined into `materialise`, so the render effect's body shared one V8 function context with every closure `materialise` creates. Each row's effect retained, through that context:

| Holder | Bytes per row |
|---|---|
| the effect body (closure) | 28 |
| its block context (`failed`) | 20 |
| `materialise`'s function context (`closure.context > previous`) | 64 |
| the row's props closure, held by that context (`Context.props`) | 28 |
| the props closure's context | 36 |

The [previous note](2026-10-18-owner-first-cleanup.md) attributed the 64 B context to the row's cleanup. That cleanup, registered by `buildRow`, has a context that holds `row` alone, and its parent is the region's context, one per list.

## The change

In `TemplateDom.fs`, the render effect, each isolated effect and each direct listener are created by their own functions, whose closures capture the services, the state array and the closure's own index, flag or handler. The effects read the compiled tables and the instance from the state array (`StateCompiled`, `StateInstance`). The setups of a template and of an inlined child run through `setUp` and `setUpChild`, so `materialise` allocates no function context. `bind` is no longer inline.

## Results

Retained heap after `create(rows)`, median of the six timing processes (bytes per row):

| Rows | Before | After | Solid 2 |
|---|---|---|---|
| 1,000 | 1,180 | 1,039 | 1,049 |
| 10,000 | 1,073 | 950 | 1,053 |

JS heap by kind (`keyed-heap.mjs`): 964 → 783 B per row at 1,000 rows, 968 → 848 B at 10,000; Solid 2 905 and 936 B.

Timings: three processes per build, alternating, 3 warmups and 21 shuffled rounds, a forced GC before every sample. Ranvier's median ms `[per-process medians]` and the median ratio to Solid 2:

| Operation | Before | After |
|---|---|---|
| create1k | 6.610 [6.21, 6.61, 7.37] 1.61× | 6.240 [5.50, 7.42, 6.24] 1.50× |
| create10k | 52.990 [52.27, 52.99, 58.46] 1.39× | 53.055 [51.20, 57.87, 53.06] 1.39× |
| replace1k | 5.480 [5.48, 5.33, 6.02] 1.34× | 5.530 [5.05, 6.72, 5.53] 1.36× |
| append1k | 3.685 [3.43, 5.54, 3.69] 1.19× | 3.715 [3.46, 3.80, 3.72] 1.04× |
| clear1k | 1.070 [0.96, 1.07, 1.15] 1.64× | 1.115 [0.96, 1.40, 1.12] 1.71× |
| clear10k | 10.295 [9.41, 10.30, 10.76] 1.89× | 10.365 [7.27, 10.36, 10.42] 1.84× |
| swap1k | 0.165 [0.17, 0.15, 0.19] 1.29× | 0.170 [0.15, 0.21, 0.17] 1.29× |
| remove1k | 0.170 [0.15, 0.17, 0.18] 1.36× | 0.175 [0.16, 0.25, 0.18] 1.50× |
| update10th1k | 0.075 0.43× | 0.075 0.45× |
| armsCreate1k | 11.075 [10.21, 13.19, 11.07] 1.57× | 11.475 [10.25, 11.51, 11.48] 1.54× |
| armsClear1k | 1.975 [1.66, 1.97, 2.10] 1.74× | 1.875 [1.66, 2.04, 1.88] 1.58× |

- Retained heap per row is below Solid 2's at both sizes, and below the 1,300 B target.
- Every timing's per-process ranges overlap; the change shows no timing effect beyond noise in this run.
- This run's Solid 2 swap1k ran its fast mode in all six processes (0.120–0.125 ms).

Tests: 379 Node DOM tests. New: two isolated slots each compare against their own previous value. Two of three mutants are killed: the isolated slot's previous-value index and the render effect's pending base. The direct listener's disposed-owner check, moved unchanged, survives: the mount's cleanup removes the listener at disposal, and no test dispatches the event during teardown.

## Remaining work

- Creation and clear ratios (create1k 1.50×, clear1k 1.71×, clear10k 1.84×) with retained bytes now below Solid 2's: the remaining create cost is time, not retained heap.
- The two signals' observer sets (48 B by `Signal$1.observers`, 32 B by their item arrays) and the `<a>`'s delegation list (49 B).

## Reproduction

From `examples/Fable.Ranvier.Playground`, one at a time:

```sh
RanvierCounters=false RANVIER_PERF_OUTPUT=generated-keyed-<bundle> dotnet fable tests/DomTests.fsproj --outDir generated-tests --configuration Release --noCache
RANVIER_PERF_OUTPUT=generated-keyed-<bundle> node node_modules/vite/bin/vite.js build --config benchmarks/frameworks/vite.config.mjs
node benchmarks/frameworks/keyed-heap.mjs --counts=1000,10000 --processes=1 --label=heap-<bundle> --out=<dir>
node benchmarks/frameworks/keyed-rows.mjs --dist=generated-keyed-<bundle> --processes=1 --rounds=21 --shuffle --no-alloc --pairs=ranvier:solid --ops=create1k,create10k,replace1k,append1k,clear1k,clear10k,swap1k,remove1k,update10th1k,armsCreate1k,armsClear1k --label=<bundle>-<n> --out=<dir>
```
