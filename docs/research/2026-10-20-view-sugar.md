# View sugar measurements

The template sugar (`t.Div(...) { ... }`, `forEach`, `forEachFixed`, `armOf`, `show`, `showElse`) was measured against its explicit twins on 2026-10-20. Every number below comes from the JSON files linked in the last section. The design is in the [spec](../superpowers/specs/2026-10-19-view-sugar-design.md).

## Setup

| Item | Value |
|------|-------|
| Commit | `1fc1809` (branch `feat/view-sugar`) plus the measurement scripts committed with this note |
| CPU | AMD Ryzen 9 9900X, 24 threads, 62 GB |
| Browser | Chromium 153.0.8010.12 (Playwright) |
| Node | v26.7.0 |
| Solid | 2.0.0-rc.14 (`solid`, `solid-web`, compiler) |
| Build | Release Fable output, minified, production bundles |
| Locks | each timed run held the shared bench lock; no other benchmark ran |

`run.mjs --template-comparison` ran three times (11 rounds, 3 warmups, 3 independent Chromium processes each). Each run printed `Verified 6 adapters` for each of its three processes. The keyed run used 3 processes and the heap run 2; the allocation run used the harness default.

## Template comparison (criterion 3, first bullet)

Median of the three runs' medians, in microseconds, with the lowest and highest per-process value over the three runs in brackets.

| Phase | Width | `ranvier-template` | `ranvier-template-sugar` |
|-------|------:|-------------------:|-------------------------:|
| mount | 1 | 6.37 [5.90-7.54] | 7.15 [5.64-8.34] |
| update | 1 | 1.08 [1.04-1.56] | 1.08 [1.02-1.38] |
| dispose | 1 | 2.25 [1.39-2.99] | 2.17 [1.39-2.56] |
| mount | 16 | 47.8 [45.1-62.2] | 48.7 [43.5-60.7] |
| update | 16 | 15.4 [14.7-17.1] | 15.6 [14.6-16.5] |
| dispose | 16 | 8.05 [6.17-13.9] | 7.81 [5.70-15.2] |
| mount | 128 | 353.1 [339.1-454.1] | 364.8 [337.8-551.4] |
| update | 128 | 127.1 [122.8-140.8] | 127.8 [124.5-144.4] |
| dispose | 128 | 34.7 [30.5-42.2] | 34.7 [30.8-42.8] |

Per-run verdicts: all 9 phase-and-width rows overlap in runs 1 and 3. Run 2 printed one `DISJOINT` row on its first attempt:

- `update@128`: explicit `[121.050, 121.200]` against sugar `[121.400, 125.375]`. The explicit range is 0.15 us wide and sits 0.2 us below the sugared range's lower bound.

Run 2 was repeated once, as the plan prescribes, and all 9 rows overlapped. The first attempt is kept as `first-attempt-template-run-2.json`; the table above uses the repeat. The medians of the sugared adapter are within about 3 % of the explicit adapter's except `mount@1` (+12 %, 0.8 us, ranges overlapping) and `mount@128` (+3.3 %).

## Keyed rows (criterion 3, second bullet)

`keyed-rows.mjs --modes=ranvier,ranvier-sugar,solid`, 3 processes, 15 rounds, 3 warmups. Median in ms with the process range in brackets. `ranvier` is the explicit `t.each` fixture; `ranvier-sugar` is the sugared fixture.

| Operation | `ranvier` | `ranvier-sugar` | `solid` |
|-----------|----------:|----------------:|--------:|
| create1k | 7.68 [6.71-9.05] | 7.80 [7.21-9.35] | 4.63 [4.37-5.52] |
| create10k | 57.19 [54.12-58.45] | 55.21 [54.49-57.14] | 35.56 [34.24-38.38] |
| replace1k | 6.50 [5.56-7.32] | 6.36 [5.96-7.25] | 4.10 [3.93-4.47] |
| update10th1k | 0.075 [0.070-0.090] | 0.075 [0.070-0.085] | 0.220 [0.175-0.520] |
| swap1k | 0.170 [0.145-0.185] | 0.185 [0.150-0.195] | 0.160 [0.125-0.175] |
| remove1k | 0.210 [0.195-0.225] | 0.200 [0.185-0.205] | 0.150 [0.120-0.160] |
| remove10k | 0.710 [0.610-0.730] | 0.600 [0.560-0.680] | 0.735 [0.635-0.785] |
| clear1k | 1.15 [1.10-1.27] | 1.13 [1.11-1.19] | 0.770 [0.635-1.04] |
| clear10k | 9.94 [9.09-10.09] | 6.89 [6.48-8.27] | 5.52 [5.46-5.56] |
| append1k | 4.18 [3.88-5.36] | 4.04 [3.37-5.57] | 3.69 [3.06-4.72] |
| armsCreate1k | 13.34 [11.83-15.09] | 13.54 [12.16-15.17] | 8.64 [7.03-9.52] |
| armsSwitch1k | 6.49 [6.12-7.38] | 7.00 [6.45-7.91] | 4.33 [3.82-5.47] |
| armsPayload1k | 1.16 [0.950-1.23] | 1.14 [0.945-1.26] | 2.19 [1.76-2.52] |
| armsClear1k | 2.53 [2.01-3.16] | 2.61 [1.97-3.21] | 1.30 [1.25-1.56] |

13 of 14 operations overlap. `clear10k` printed `DISJOINT`, with the sugared range below the explicit range (sugar `[6.475, 8.270]` against explicit `[9.085, 10.090]`; the sugar is faster, not slower). A rerun (`keyed-sugar-rerun.json`, same modes) overlapped on all 14 operations, with `clear10k` explicit `[6.345, 10.020]` against sugar `[6.380, 7.760]`. The explicit adapter's `clear10k` ranged from 6.3 to 10.1 ms across the two runs, so the first-run gap is run-to-run variation of the explicit adapter, not an effect of the sugar.

Differences between the two fixtures that readers should not attribute to the sugar:

- The sugared fixtures use `forEachFixed`, which expands to `t.each key items (Component.define (fun _ c -> c ()) row)` (spec section 4 table). The explicit fixture's `keyedRow` is likewise a snapshot row. Both fixtures are the same region kind with the same row kind, so a timing difference does not mix fixed and live rows.
- Default `keyed-rows.mjs` runs (no `--modes`) now measure three modes, so the rotation of modes across rounds differs from earlier result files with the same label. A comparison against an earlier baseline needs `--modes=ranvier,solid`. The runs above name all three modes explicitly.

## Retained heap (criterion 3, third bullet)

`keyed-heap.mjs --counts=1000,10000 --processes=2`. JS bytes retained per row.

| Rows | `ranvier` (2 processes) | `ranvier-sugar` (2 processes) | `solid` |
|-----:|------------------------:|------------------------------:|--------:|
| 1,000 | 856.1, 856.1 | 795.3, 856.1 | 905.4, 905.4 |
| 10,000 | 839.3, 839.3 | 839.3, 839.3 | 936.0, 936.0 |

With 2 processes one sugared process retained 795.3 B/row, 61 B/row less than every other Ranvier sample, and the other retained 856.1 B/row. A median of two samples cannot establish equality, so the comparer now requires every sugared sample to lie within 0.5 % of the explicit median, and the heap run was repeated with 4 processes (`heap-sugar-4p.json`, commit `df3bb0b`):

| Rows | `ranvier` (4 processes) | `ranvier-sugar` (4 processes) |
|-----:|------------------------:|------------------------------:|
| 1,000 | 856.1, 856.1, 856.1, 856.1 | 795.3, 856.1, 795.3, 795.3 |
| 10,000 | 860.0, 839.3, 839.3, 843.6 | 839.3, 839.3, 839.3, 839.3 |

The comparer reports `same` at 10,000 rows and `LOWER` at 1,000 rows (exit 0; it reports `DIFFER` and exits 1 only when a sugared sample retains more). At 1,000 rows three of four sugared processes retain 795.3 B/row, 7.1 % below the explicit 856.1; no sugared sample retains more than the explicit adapter.

The gap is confined to one category of the retained-object breakdown: `string` is 34,416 B in a 795.3 process and 95,248 B in an 856.1 process (a difference of 60,832 B) with 18,311 and 18,251 strings. Every other JS category is identical. A constant 60.8 KB is 61 B/row at 1,000 rows and about 6 B/row at 10,000 rows, where the two adapters agree, so the difference is a fixed-size string present in some processes and not a per-row cost. Its origin is not identified. The spec's 994 B at 1k is superseded by 856 B in the current build.

## Allocation sample (criterion 3, fourth bullet)

Two samples, each over the whole profile: every allocating function is kept and searched by name for `ElementBuilder`, `Builders_`, `Sugar`, `TemplateBuilder$1_*` and `Delay`.

**128-row mount of the template row** (the sample the spec names). `sugar-alloc.mjs` mounts 10 hosts of 128 `perfRowSugar` rows (`ranvier-template-sugar`) and of the explicit `perfRow` (`ranvier-template`) through the same adapters as the template comparison, after one warm-up mount, with V8 sampling at 64 B in 4 Chromium processes (`alloc-template-row.json`, unminified build). The mode sampled first alternates by process (explicit first in processes 1 and 3, sugared first in 2 and 4).

| Process | First mode | Explicit bytes (10 mounts) | Sugared bytes (10 mounts) | Functions sampled (explicit / sugared) |
|--------:|------------|---------------------------:|--------------------------:|--------------------------------------:|
| 1 | explicit | 1,561,916 | 1,496,116 | 149 / 85 |
| 2 | sugared | 1,496,728 | 1,565,484 | 84 / 151 |
| 3 | explicit | 1,564,880 | 1,537,132 | 150 / 103 |
| 4 | sugared | 1,530,716 | 1,553,960 | 110 / 153 |
| mean | | 1,538,560 | 1,538,173 (-0.03 %) | |

That is about 154 KB per 128-row mount in both modes. In each process the mode sampled first allocates more (1.53 to 1.57 MB against 1.50 to 1.54 MB) and lists about 150 functions where the second lists 84 to 110, whichever mode it is: the difference between the modes follows the order inside the process (warmed code, inlined callees), not the sugar. No `ElementBuilder`, `TemplateBuilder$1_*`, `Builders_` or `Delay` function allocates in any sugared sample, and no sampled function name contains `Sugar` other than `mountTemplateSugar`, the adapter entry, which allocates 308 to 464 B per sample against 168 to 380 B for its counterpart `mountTemplate`. The largest functions are the same in both modes (`materialise`, `ObserverSet__grow`, `startRender`, `stateFor`, `Effect$1`).

**1,000-row keyed create** (`create1k`, `alloc-sugar-full.json`, 10 repeats, 1 process, commit `df3bb0b`): explicit 16,439,124 B, sugared 16,430,288 B (-0.05 %); `clean create1k` among 89 sugared functions. Top five of `ranvier-sugar`: `materialise` 2,676,784; `Effect$1` 1,810,740; `startRender` 1,296,556; `RowsOf$3__CreateOnce_5BDD81` 1,218,320; `Signal$1` constructor 1,200,900. The earlier file `alloc-sugar.json` kept only the largest 60 functions per sample and is superseded.

Per-function attribution differs between the modes while the totals agree. In the keyed sample `materialise` is 1.54 MB explicit and 2.68 MB sugared, `Effect$1` 0.88 and 1.81 MB, and `collect` (0.78 MB) and `ownSetup` (0.50 MB) are absent from the sugared profile. The template-row sample shows the same shift, and it follows the order in which the modes ran in a process rather than the mode (see the table above: the second mode sampled in a process lists 84 to 110 functions against about 150 for the first, for explicit and sugared alike). The keyed sample runs the explicit mode first and the sugared mode second in one process, which matches that pattern. The explanation is that V8 has inlined callees into their callers by the time the second mode is sampled, and the sampler attributes an allocation to the function it is inlined into, so the same bytes move among `materialise`, `Effect$1`, `collect` and `ownSetup`. This is inferred from the unchanged totals and the order dependence of the template-row sample; an inlining trace was not taken. The attribution difference is sampling, not a different allocation pattern.

## Bundle size (criterion 4)

`sugar-size.mjs` builds two Vite bundles from entries exporting the benchmark fixtures: explicit (`FrameworkPerf.mountTemplate`, `KeyedRowsPerf`) and sugared (`FrameworkSugarPerf.mountTemplateSugar`, `KeyedRowsSugarPerf`).

| Entry | Raw | Gzip | Brotli |
|-------|----:|-----:|-------:|
| explicit | 204,052 | 58,684 | 51,543 |
| sugared | 206,859 | 59,266 | 51,937 |
| delta | +2,807 | +582 | +394 |

The gzip delta is 582 B against the 1,024 B budget of open question 8, which is 57 % of the budget. The delta is net: the sugared bundle omits the explicit row definitions (`perfRow`, `keyedRow`, the arm rows) that the explicit bundle holds, so it measures the cost of choosing the sugar, not the gross size of the sugar code.

Emitted sugar modules contain 0 `curry`, `uncurry` and `partialApply` (criterion 4, second sentence): `grep -c "curry\|uncurry\|partialApply"` over `generated-tests/experiments/Ranvier.View.Portable/Generated/HtmlTemplateSugar.js`, `.../TemplateSugar.js`, `FrameworkSugarPerf.js`, `KeyedRowsSugarPerf.js` and `PlaygroundSugar.js` prints 0 for each.

`perfRowSugar` moved from `FrameworkPerf.fs` to `FrameworkSugarPerf.fs`: its top-level `Component.define` call cannot be tree-shaken, so with the row in `FrameworkPerf.fs` the explicit entry already held sugar and the delta undercounted. The explicit entry is checked on an unminified build: `explicitHoldsSugar` is `false`, and the same probe finds sugar in the unminified sugared build (`sugaredHoldsSugar` is `true`).

Playground bundle: `dist/assets/*.js` hashes match the base build exactly (`diff` against the recorded baseline printed nothing) and contain no `ElementBuilder` or `Ranvier.View.Foundation.Sugar` names. The sugar costs 0 B when unused.

## Authoring counts (criterion 6)

Template lines are counted from the `let` line to the end of the template, after Fantomas formatting. `Component.define` calls are counted with `grep -c`.

| Fixture | Explicit | Sugared |
|---------|---------:|--------:|
| keyed list (`keyedRow` + `keyedList` against `keyedList`) | 39 lines | 22 lines |
| arms (`plainArm` + `countedArm` + `armRow` + `armList` against `armList`) | 31 lines | 22 lines |
| playground demo, setup included (`App.demoView` against `PlaygroundSugar.demoSugar` + `demoSetup`) | 103 lines | 89 lines |
| playground demo, setup excluded on both sides (explicit setup is 5 lines, `demoSetup` 6) | 98 lines | 83 lines |
| `Component.define` in `KeyedRowsPerf.fs` / `KeyedRowsSugarPerf.fs` | 6 | 2 |

`git diff --stat ca03e98` over `App.fs`, `ComponentsExample.fs` and `KeyedRowsPerf.fs` is empty (criterion 5). The separate module-level row and arm definitions are gone from the sugared fixtures: four definitions disappear (keyed row, plain arm, counted arm and arm row).

## Files

- [bundle.json](framework-performance/investigation/view-sugar/bundle.json)
- [template-run-1.json](framework-performance/investigation/view-sugar/template-run-1.json), [template-run-2.json](framework-performance/investigation/view-sugar/template-run-2.json), [template-run-3.json](framework-performance/investigation/view-sugar/template-run-3.json)
- [first-attempt-template-run-2.json](framework-performance/investigation/view-sugar/first-attempt-template-run-2.json)
- [keyed-sugar.json](framework-performance/investigation/view-sugar/keyed-sugar.json), [keyed-sugar-rerun.json](framework-performance/investigation/view-sugar/keyed-sugar-rerun.json)
- [heap-sugar.json](framework-performance/investigation/view-sugar/heap-sugar.json), [heap-sugar-4p.json](framework-performance/investigation/view-sugar/heap-sugar-4p.json)
- [alloc-template-row.json](framework-performance/investigation/view-sugar/alloc-template-row.json) (128-row mount), [alloc-sugar-full.json](framework-performance/investigation/view-sugar/alloc-sugar-full.json) (create1k, whole profile), [alloc-sugar.json](framework-performance/investigation/view-sugar/alloc-sugar.json) (superseded, top 60 only)
