# View sugar v2 measurements

The v2 sugar (free tags `div (...) { ... }`, free `forEach`/`forEachFixed`/`matchWith`/`armOf`/`show`, and the declaration helpers `Component.template`, `Component.ofMembers` and `Component.ofClass`) was measured against its explicit twins on 2026-10-21. Every number below comes from the JSON files linked in the last section. The design is in the [spec](../superpowers/specs/2026-10-20-view-sugar-v2-design.md); v1's measurements are in the [v1 note](2026-10-20-view-sugar.md).

## Setup

| Item | Value |
|------|-------|
| Commit | `432592f` (branch `feat/view-sugar-v2`) plus the twin fixture `FrameworkTwinsPerf.fs` and the script changes committed with this note |
| CPU | AMD Ryzen 9 9900X, 24 threads, 62 GB |
| Browser | Chromium 153.0.8010.12 (Playwright) |
| Node | v26.7.0 |
| Fable | 5.18.0 |
| Solid | 2.0.0-rc.14 (`solid`, `solid-web`, compiler) |
| Build | Release Fable output, minified, production bundles; the allocation sample uses an unminified build |
| Locks | each timed run held the shared bench lock, and each Chromium run the browser lock; no other benchmark ran |

`run.mjs --template-comparison` ran three times (11 rounds, 3 warmups, 3 independent Chromium processes each), and each process printed `Verified 8 adapters`. The keyed run used 3 processes (15 rounds, 3 warmups), the heap run 2 processes, and the template-row allocation sample 4 processes.

## Template comparison (criterion 3, first bullet)

Median of the three runs' medians, in microseconds, with the lowest and highest per-process value over the three runs in brackets. `ranvier-template` is the explicit `Component.define` row; `-sugar` is the free row with `Component.template`, `-members` the `Component.ofMembers` row and `-class` the `Component.ofClass` row.

| Phase | Width | `ranvier-template` | `ranvier-template-sugar` | `ranvier-template-members` | `ranvier-template-class` |
|---|--:|--:|--:|--:|--:|
| mount | 1 | 5.80 [5.51-7.70] | 5.49 [5.16-6.05] | 5.55 [5.29-6.56] | 5.61 [5.33-7.05] |
| update | 1 | 1.01 [0.98-1.29] | 0.99 [0.97-1.18] | 1.00 [0.97-1.28] | 0.99 [0.96-1.26] |
| dispose | 1 | 1.43 [1.21-2.19] | 1.39 [1.27-1.84] | 1.41 [1.21-1.68] | 1.43 [1.31-2.01] |
| mount | 16 | 45.7 [44.2-48.2] | 45.4 [43.9-48.0] | 45.8 [43.6-48.4] | 44.7 [43.5-48.0] |
| update | 16 | 15.1 [14.5-15.9] | 15.0 [14.5-15.6] | 14.9 [14.5-15.6] | 14.9 [14.6-15.4] |
| dispose | 16 | 7.15 [5.23-8.20] | 7.11 [5.51-7.89] | 6.99 [5.78-8.05] | 7.23 [5.35-8.24] |
| mount | 128 | 342.3 [332.3-357.8] | 340.8 [335.9-368.3] | 344.5 [328.1-385.2] | 343.3 [326.6-365.3] |
| update | 128 | 116.2 [112.0-125.5] | 115.3 [113.6-119.9] | 115.5 [114.4-118.4] | 115.5 [113.7-118.8] |
| dispose | 128 | 31.7 [25.9-35.6] | 31.1 [26.1-33.3] | 31.6 [25.5-33.6] | 30.8 [27.3-34.5] |

Per-run verdicts (`sugar-compare.mjs template`, 27 rows per run: 3 modes × 9 phase-widths): runs 1 and 2 overlap on all 27 rows. Run 3 printed one `DISJOINT` row on its first attempt:

- `ranvier-template-sugar mount@1`: explicit `[5.703, 5.762]` against sugar `[5.273, 5.488]`. The sugared range lies below the explicit range; the sugared row was faster.

Run 3 was repeated once, as the plan prescribes, and all 27 rows overlapped. The first attempt is kept as `first-attempt-template-run-3.json`; the table uses the repeat. Every sugared median is within 6 % of the explicit median, and the largest gaps are below it (`mount@1`, −5.3 % for the free row).

**`ofClass` (decision 10).** The class row overlaps the explicit row in every phase and width of every run, so its per-instance constructor array is accepted (spec criterion 3) and no tuple-free shape is needed.

## Keyed rows (criterion 3, second bullet)

`keyed-rows.mjs --modes=ranvier,ranvier-sugar,solid`, 3 processes. Median in ms with the process range in brackets. `ranvier` is the explicit fixture of `KeyedRowsPerf.fs`; `ranvier-sugar` is the v2 fixture of `KeyedRowsSugarPerf.fs`.

First run (`keyed-sugar.json`):

| Operation | `ranvier` | `ranvier-sugar` | `solid` |
|---|--:|--:|--:|
| create1k | 7.21 [6.78-7.58] | 7.02 [6.78-7.53] | 4.30 [4.22-4.56] |
| create10k | 56.08 [55.54-56.37] | 55.67 [53.26-56.45] | 33.29 [32.37-33.74] |
| replace1k | 6.80 [6.40-7.01] | 6.76 [6.27-7.13] | 4.21 [4.15-4.44] |
| update10th1k | 0.075 [0.065-0.100] | 0.070 [0.070-0.085] | 0.185 [0.165-0.205] |
| swap1k | 0.150 [0.140-0.170] | 0.165 [0.150-0.170] | 0.125 [0.125-0.135] |
| remove1k | 0.175 [0.165-0.180] | 0.180 [0.170-0.190] | 0.125 [0.120-0.140] |
| remove10k | 0.695 [0.680-0.705] | 0.645 [0.545-0.675] | 0.735 [0.730-0.780] |
| clear1k | 1.16 [1.10-1.19] | 1.19 [1.14-1.28] | 0.660 [0.650-0.685] |
| clear10k | 10.15 [9.51-10.31] | 7.25 [6.82-8.31] | 5.38 [5.29-5.39] |
| append1k | 4.23 [4.13-4.40] | 3.88 [3.79-4.02] | 3.34 [3.10-3.58] |
| armsCreate1k | 13.26 [12.63-13.78] | 13.07 [12.67-13.87] | 7.83 [7.71-8.16] |
| armsSwitch1k | 6.50 [6.25-6.77] | 6.60 [6.51-6.64] | 3.88 [3.78-3.90] |
| armsPayload1k | 0.970 [0.910-1.04] | 1.00 [0.905-1.06] | 2.08 [1.79-2.28] |
| armsClear1k | 2.58 [2.06-2.86] | 2.56 [2.19-2.92] | 1.30 [1.20-1.32] |

11 of 14 operations overlap. `remove10k`, `clear10k` and `append1k` printed `DISJOINT`, each with the sugared range below the explicit range (the sugar was faster). The rerun (`keyed-sugar-rerun.json`, same modes) overlapped on all 14 operations:

| Operation | `ranvier` | `ranvier-sugar` | `solid` |
|---|--:|--:|--:|
| create1k | 6.32 [6.23-6.39] | 6.27 [6.10-6.42] | 3.89 [3.82-4.07] |
| create10k | 53.38 [52.84-53.73] | 53.05 [52.65-54.35] | 37.60 [37.56-37.70] |
| replace1k | 5.92 [5.66-6.02] | 5.64 [5.33-5.80] | 3.68 [3.58-3.93] |
| update10th1k | 0.065 [0.060-0.075] | 0.060 [0.060-0.070] | 0.155 [0.135-0.165] |
| swap1k | 0.140 [0.125-0.145] | 0.140 [0.135-0.160] | 0.115 [0.105-0.125] |
| remove1k | 0.155 [0.140-0.160] | 0.155 [0.145-0.165] | 0.110 [0.110-0.115] |
| remove10k | 0.635 [0.620-0.655] | 0.635 [0.630-0.650] | 0.710 [0.665-0.765] |
| clear1k | 0.945 [0.925-0.975] | 0.950 [0.920-0.995] | 0.580 [0.565-0.635] |
| clear10k | 6.69 [6.43-9.23] | 6.38 [6.20-6.80] | 5.28 [5.21-5.43] |
| append1k | 3.77 [3.71-3.86] | 3.55 [3.22-3.75] | 3.02 [2.86-3.31] |
| armsCreate1k | 11.48 [11.22-11.61] | 11.73 [11.28-11.93] | 6.96 [6.70-7.19] |
| armsSwitch1k | 5.60 [5.50-5.81] | 5.59 [5.20-5.90] | 3.38 [3.28-3.61] |
| armsPayload1k | 0.790 [0.775-0.790] | 0.795 [0.760-0.810] | 1.61 [1.58-1.63] |
| armsClear1k | 2.00 [1.83-2.63] | 2.05 [1.79-2.64] | 1.11 [1.06-1.27] |

The explicit `clear10k` ranged from 6.4 to 10.3 ms across the two runs, as in v1's measurements (6.3 to 10.1 ms); the first-run gaps are run-to-run variation of the explicit adapter.

## Retained heap (criterion 3, third bullet)

`keyed-heap.mjs --counts=1000,10000 --processes=2`. JS bytes retained per row.

| Rows | `ranvier` (2 processes) | `ranvier-sugar` (2 processes) | `solid` |
|-----:|------------------------:|------------------------------:|--------:|
| 1,000 | 864.1, 864.1 | 803.3, 864.1 | 905.4, 905.4 |
| 10,000 | 847.3, 847.3 | 847.3, 847.3 | 936.0, 936.0 |

The comparer reports `same` at 10,000 rows and `LOWER` at 1,000 rows, exit 0. One sugared process at 1,000 rows retains 60.8 B/row less: the same constant 60.8 KB that v1's note traced to a fixed-size string present in some processes. No sugared sample retains more than the explicit fixture.

## Allocation sample (criterion 3, fourth bullet)

**128-row mount of the template rows** (`alloc-template-row.json`). `sugar-alloc.mjs --processes=4` mounts 10 hosts of 128 rows per mode (1,280 rows per sample) after 3 warm-up rounds, with V8 sampling at 64 B, on an unminified build. Each frame is attributed to its bundle module from the `//#region` comments. The explicit twin is sampled first and last in each process; a positive control (`sugar-alloc-control`, a fresh `Component.ofMembers` definition per mount) is sampled last.

- **Frames:** all 12 sugared samples (free, members, class × 4 processes) are `clean`: no sampled frame lies in `TemplateSugar.js` or `Generated/HtmlTemplateSugar.js`.
- **Control:** 3,016, 2,648, 2,868 and 2,392 B in sugar modules in processes 1 to 4, so module attribution sees sugar allocation when it happens.
- **Fixture-module bytes (the gate):** `FrameworkPerf.js` plus the row's own module, per row, against the twin's `FrameworkPerf.js` (8 samples: 161.2, 156.4, 161.8, 126.2, 157.8, 124.6, 157.5, 162.9; median 157.7; tolerance = range 38.3 B/row).

| Row | B/row by process | Median | Excess | Result |
|---|---|--:|--:|---|
| free (`-sugar`) | 163.2, 122.8, 123.8, 157.7 | 140.8 | −16.9 | bytes ok |
| members | 123.3, 155.9, 156.4, 119.2 | 139.6 | −18.1 | bytes ok |
| class | 152.0, 148.5, 158.1, 154.5 | 153.2 | −4.4 | bytes ok |

- **Total heap (info):** free 1,197.8, members 1,196.6, class 1,249.8 against 1,213.8 B/row explicit; the twin's own spread limits detection to excess above 59.7 B/row.
- **`ofClass` constructor array, against explicit.** In the steady state the class row allocates no more than the explicit row in its fixture modules (−4.4 B/row, inside the 38.3 B/row tolerance; Task 11's run measured −5.7 inside 34.8). The `ofClass` arrow, which builds the 2-element `tupledArg` array and the class instance, samples 66–68 kB per sample (about 52 B/row) against about 58 B/row for the explicit setup arrow; the array cannot be separated from the instance in the sample. The class row's excess lies outside its fixture modules: `src/Ranvier/Core.js` 421.4 against 392.3 B/row (+29.2, explicit range 27.4), reported as info. Task 11's under-warmed run (one warm mount per mode) measured the class row's own module at about +24 B/row over the other declaration styles: the cold-start cost of the array, which escape analysis removes once the code is optimised.

**1,000-row keyed create** (`alloc-sugar.json`, `keyed-rows.mjs --profile --repeats=10 --ops=create1k`, 1 process): explicit 16,729,404 B, sugared 16,771,296 B (+0.25 %); `clean create1k` among 83 sugared functions. This sample records one sample per mode without modules, so the byte check prints `UNTESTED` (exit 0), as designed.

## Bundle size (criterion 4)

`sugar-size.mjs` builds minified Vite bundles from six entries. One-row entries hold one benchmark row plus the keyed and arm fixtures; the combined entries hold three rows plus the same fixtures.

| Entry | Contents | Raw | Gzip | Brotli |
|---|---|--:|--:|--:|
| explicit | `FrameworkPerf.mountTemplate`, `KeyedRowsPerf` | 212,352 | 61,169 | 53,606 |
| explicit-combined | explicit plus `FrameworkTwinsPerf` (explicit twins of the members and class rows) | 213,734 | 61,350 | 53,733 |
| sugared | free row (`Component.template`), `KeyedRowsSugarPerf` | 216,387 | 62,009 | 54,304 |
| members | `Component.ofMembers` row, `KeyedRowsSugarPerf` | 216,537 | 61,993 | 54,298 |
| class | `Component.ofClass` row, `KeyedRowsSugarPerf` | 216,627 | 62,061 | 54,314 |
| combined | free, members and class rows, `KeyedRowsSugarPerf` | 218,022 | 62,219 | 54,446 |

| Delta | Against | Raw | Gzip | Brotli |
|---|---|--:|--:|--:|
| sugared | explicit | +4,035 | +840 | +698 |
| members | explicit | +4,185 | +824 | +692 |
| class | explicit | +4,275 | +892 | +708 |
| combined | explicit-combined (like for like, the budget) | +4,288 | **+869** | +713 |
| combined | explicit (one row) | +5,670 | +1,050 | +840 |

- `withinBudget: true`: every delta against its twin entry is at most 1,024 B gzip. The combined entry's delta against the like-for-like explicit-combined entry is 869 B (85 % of the budget). Against the one-row explicit entry it is 1,050 B, 26 B over; 181 B of that is the two extra row definitions, which explicit-combined holds in explicit form.
- **Against v1** (measured before its removal, `sugar2-baseline/v1-bundle.json`): v1's sugared entry was +597 B gzip (+2,812 raw, +489 Brotli) over the explicit entry of its day (60,704 B gzip). v2's free row is +840 B gzip, 243 B more than v1. The explicit entry grew from 60,704 to 61,169 B gzip with master's merged code.
- `explicitHoldsSugar: false` for both explicit entries; `sugaredHoldsSugar: true`; `frameCheck`: 53 sugar declarations in the combined bundle, 0 uncovered, 0 violations.
- **`render`, `renderOf`, `ComponentTag`** (ruling 7, outside the budget): no benchmark fixture uses them, and the unminified `sugared` bundle holds none of them (tree-shaken to 0 B). In the emitted `TemplateSugar.js` their definitions are `render` 129 B, `renderOf` 141 B and `ComponentTag$2` with its reflection function 386 B (unminified, LF line endings).
- **Unused sugar costs 0 B:** the playground's `dist/assets/*.js` hashes equal the baseline taken after Task 9's merge (`diff` printed nothing), and `grep -c "Ranvier.View.Foundation.Sugar\|ComponentTag\|VoidElement"` over `dist/assets` counts 0 in each file.
- `tests/sugar-emit.test.mjs`: 2 of 2 pass; `TemplateSugar.js` and `Generated/HtmlTemplateSugar.js` contain 0 `curry`, `uncurry` or `partialApply`.

The explicit twins in `FrameworkTwinsPerf.fs` render the same DOM as the sugared rows and `FrameworkPerf`'s row at mount, after a click, after an update and after disposal; `tests/sugar-rows.test.mjs`, part of `npm run test:dom`, checks each row and twin against `FrameworkPerf`'s row.

## Authoring counts (criterion 6)

Non-whitespace characters exclude comment lines. Annotations count written instance-lambda annotations (`fun (x: T) ->` with `T` the instance or row type). Call sites count `Component.define`, `Component.template`, `Component.ofMembers` and `Component.ofClass`. v1 is read at `563b6e0`.

| Fixture | Range | Annotations v1 → v2 | Characters v1 → v2 | Call sites v1 → v2 |
|---|---|--:|--:|--:|
| Demo screen, `Component.template` | v1 `demoSetup` + `demoSugar`; v2 `demoSugar` | 10 → 0 | 2,432 → 2,116 | 1 `define` → 1 `template` |
| Demo screen, `ofMembers` | v2 `type DemoMembers` + `demoMembers` | 10 → 0 | 2,432 → 2,220 | 1 → 1 `ofMembers` |
| Demo screen, `ofClass` | v2 `type DemoClass` + `demoClass` | 10 → 8 | 2,432 → 2,291 | 1 → 1 `ofClass` |
| Keyed rows | `keyedList` | 4 → 3 | 638 → 560 | 1 → 1 |
| Arm rows | v1 `armList`; v2 `plain`, `counted`, `armList` | 6 → 1 | 538 → 403 | 1 → 1 |
| Benchmark row, free | `perfRowSugar` | 6 → 0 | 530 → 433 | 1 → 1 |
| Benchmark row, `ofMembers` | `type PerfRowMembers` + `perfRowMembers` | 6 → 0 | 530 → 587 | 1 → 1 |
| Benchmark row, `ofClass` | `type PerfRowClass` + `perfRowClass` | 6 → 6 | 530 → 636 | 1 → 1 |

For comparison, the explicit `demoExplicit` is 2,556 characters with 10 annotations, and the explicit `perfRow` 479 characters with 0. The `ofMembers` and `ofClass` ranges include the instance type declaration, which the free and v1 ranges declare elsewhere (`DemoInstance`, `FrameworkPerf.PerfRow`).

## Files

- [bundle.json](framework-performance/investigation/view-sugar-v2/bundle.json)
- [template-run-1.json](framework-performance/investigation/view-sugar-v2/template-run-1.json), [template-run-2.json](framework-performance/investigation/view-sugar-v2/template-run-2.json), [template-run-3.json](framework-performance/investigation/view-sugar-v2/template-run-3.json)
- [first-attempt-template-run-3.json](framework-performance/investigation/view-sugar-v2/first-attempt-template-run-3.json)
- [keyed-sugar.json](framework-performance/investigation/view-sugar-v2/keyed-sugar.json), [keyed-sugar-rerun.json](framework-performance/investigation/view-sugar-v2/keyed-sugar-rerun.json)
- [heap-sugar.json](framework-performance/investigation/view-sugar-v2/heap-sugar.json)
- [alloc-template-row.json](framework-performance/investigation/view-sugar-v2/alloc-template-row.json) (128-row mount, module-attributed), [alloc-sugar.json](framework-performance/investigation/view-sugar-v2/alloc-sugar.json) (create1k, whole profile)
