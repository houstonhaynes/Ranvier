# armsClear1k and component dispose: where the remaining ratio comes from — 2026-10-19

The probe scripts this note runs were removed from the branch; they are at commit [d784929](https://github.com/ranvier-org/Ranvier/tree/d784929cd9c67eed51bb319d1f3c2b993dcd9b54).

## Environment and commits

- Machine: AMD Ryzen 9 9900X (12 cores, 62 GB), Windows 11 10.0.26200. Rider open and idle; profiles and timings ran one at a time, with no build running alongside.
- Chromium 153.0.8010.12 (Playwright 1.63.0), Node 26.7.0, Fable 5.18.0 (`--noCache`, Release). Solid 2: `solid-js`, `@solidjs/web`, `@solidjs/compiler` 2.0.0-rc.14.
- Code: `31fc732` (`poc/fable-ranvier-dom`). Profiles use the unminified bundle `generated-keyed-profile`; the template timings use `run.mjs --build`'s minified bundle.

Raw data in [`framework-performance/investigation/arms-and-dispose-profile/`](https://github.com/ranvier-org/Ranvier/tree/d784929cd9c67eed51bb319d1f3c2b993dcd9b54/docs/research/framework-performance/investigation/arms-and-dispose-profile):

- `keyed-profile-buckets.json`: armsClear1k, clear1k and armsCreate1k, loop protocol (`keyed-profile-buckets.mjs`).
- `template-profile-buckets.json`: template-component mount and dispose at 128 rows (`template-profile.mjs`, then `keyed-profile-buckets.mjs`).
- `template-framework-results.json`: `run.mjs --template-comparison`, three processes.

## armsClear1k

Loop protocol, 200 fresh lists per operation, 50 µs sampling, the samples inside `measuredRun`. Sampled ms per operation:

| Operation | Ranvier | Solid 2 | Ratio |
|---|---|---|---|
| armsClear1k | 0.976 | 0.950 | 1.03 |
| clear1k | 0.426 | 0.323 | 1.32 |
| armsCreate1k | 4.829 | 3.373 | 1.43 |

Ranvier's armsClear1k by bucket: row teardown 0.604 ms, GC 0.175, view list region 0.115, core graph 0.054, core projection pass 0.026. Solid 2: its own code 0.900 ms, GC 0.045.

- In steady state, armsClear1k costs what Solid 2's costs. The 1.96× in the timing protocol comes from the first operation after a forced full GC. That is the effect recorded in [the post-GC note](2026-10-17-arms-clear-and-post-gc.md#the-first-operation-after-a-forced-gc), where armsClear1k was 0.67× Solid 2 with live instances of the row shapes kept mounted.
- Ranvier's GC share (0.175 ms against 0.045 ms) is the largest single difference left.

## Component dispose

`run.mjs --template-comparison`, three processes, 3 warmups and 11 rounds each. The template component's median µs per root, against Solid 2:

| Phase, rows | Template | Solid 2 | × Solid 2 |
|---|---|---|---|
| Mount, 1 | 7.21 | 6.21 | 1.16 |
| Mount, 16 | 52.77 | 47.38 | 1.11 |
| Mount, 128 | 368.59 | 329.38 | 1.12 |
| Update, 1 | 1.13 | 1.56 | 0.73 |
| Update, 16 | 16.38 | 15.62 | 1.05 |
| Update, 128 | 128.38 | 119.35 | 1.08 |
| Dispose, 1 | 2.64 | 1.99 | 1.32 |
| Dispose, 16 | 9.88 | 7.50 | 1.32 |
| Dispose, 128 | 44.84 | 31.41 | 1.43 |

The dispose ratios match the 2026-10-14 measurements, so the owner and observer-slot changes since then left them unchanged.

Profile of 300 rounds of 32 roots at 128 rows, µs per root:

| Function | Template | Solid 2 |
|---|---|---|
| Removing the row nodes (native `remove`; Solid 2's `web.js` removal) | 23.4 | 22.8 |
| Removing each binding from its source's observers (`ObserverSet.Remove`; Solid 2's `unlinkSubs`) | 10.4 | 3.1 |
| Trimming each binding's sources (`SourceList.trimFrom`, `sourceAt`; Solid 2's `clearDeps`) | 7.3 | 1.2 |
| The rest of row teardown: owners, effects, the mount (Solid 2's `disposeChildren`) | 5.2 | 9.0 |
| Whole dispose | 47.0 | 39.4 |

- Node removal costs the same in both.
- The difference is in removing edges. Every row's bindings read the same shared state, so each source has hundreds of observers. Above 8 observers, `ObserverSet.Remove` deletes the observer from the position index and rewrites the index entry for the observer that fills the gap. Solid 2 unlinks each edge in constant time from a linked list.
- This is the cost [#54](https://github.com/ranvier-org/Ranvier/pull/54) measured, and its deferred design (indexed edge handles: each edge stores its position on both sides) removes it. A view-side change cannot remove it.

## Reproduction

From `examples/Fable.Ranvier.Playground`, one at a time:

```sh
node benchmarks/frameworks/keyed-rows.mjs --build --profile --repeats=200 --ops=armsClear1k,clear1k,armsCreate1k --pairs=ranvier:solid --label=prof-x200 --save-cpu=<dir>/cpu --out=<dir>
node benchmarks/frameworks/keyed-profile-buckets.mjs <dir>/cpu generated-keyed-profile/assets/<bundle>.js.map --out=<dir>/buckets.json
node benchmarks/frameworks/template-profile.mjs "$PWD" <dir2>/cpu 128 300
node benchmarks/frameworks/keyed-profile-buckets.mjs <dir2>/cpu generated-keyed-profile/assets/<bundle>.js.map --out=<dir2>/buckets.json
node benchmarks/frameworks/run.mjs --build --template-comparison   # writes investigation/template-framework-results.json
```

## After indexed edge handles (#98)

[#98](https://github.com/ranvier-org/Ranvier/pull/98) removes edges by handle: each end of an edge stores its position in the other, and `ObserverSet` has no hash index. Master `168f44e`, merged into `poc/fable-ranvier-dom` as `e102216`. Two runs of `run.mjs --template-comparison` (`template-after-98-1.json`, `template-after-98-2.json`), the template component's median µs per root and its ratio to Solid 2:

| Phase, rows | Run 1 | Run 2 | Before #98 |
|---|---|---|---|
| Mount, 1 | 5.74 (1.09) | 5.62 (1.10) | 1.16 |
| Mount, 16 | 43.83 (1.15) | 43.52 (1.15) | 1.11 |
| Mount, 128 | 307.81 (1.09) | 326.56 (1.13) | 1.12 |
| Update, 1 | 1.05 (0.77) | 1.07 (0.81) | 0.73 |
| Update, 16 | 14.72 (1.02) | 15.03 (1.04) | 1.05 |
| Update, 128 | 117.20 (1.06) | 118.53 (1.07) | 1.08 |
| Dispose, 1 | 1.45 (1.35) | 1.39 (1.39) | 1.32 |
| Dispose, 16 | 6.02 (1.22) | 5.23 (1.17) | 1.32 |
| Dispose, 128 | 29.22 (1.14) | 28.13 (1.03) | 1.43 |

- Dispose at 128 rows falls from 1.43× to 1.03–1.14× Solid 2, and at 16 rows from 1.32× to 1.17–1.22×. Mount and update stay within their earlier ranges.
- A one-row root's dispose is unchanged at 1.35–1.39× (0.4 µs per root): its sources have one observer each, so there was no hash to remove.
