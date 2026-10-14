# Core trimmed pass: keyed list cost

Date: 2026-10-14

## Method

- Commits: `B0` = `093269fcc4009a8d59866f1b3910a5746f03d549` (the benchmarks commit on `perf/core-trimmed-pass`, on top of `dc0ee50`, the branch's projection work: memo-free `createProjectionOnce`, the shared `ensureUniqueKeys`, the create-first stop and rollback kept off the default pass). The core is unchanged from `dc0ee50`.
- Machine: AMD Ryzen 9 9900X 12-Core Processor, 24 logical cores, 62 GB, win32 10.0.26200. Node v26.7.0, .NET SDK 10.0.402, Fable 5.18.0, `@solidjs/signals` 2.0.0-rc.14.
- Node run: `docs/research/core-trimmed-pass/before.json` (commit `B0`, `dirtyAtStart` empty). .NET run: `docs/research/core-trimmed-pass/dotnet-before/` (see `provenance.json`).
- After run: A1 = `1d354b231a895b2d4e8f3811f0c1ab4b3069eeff` (`perf/core-trimmed-pass`; the fixture, the fixture project and `Lists.fs` are identical to `B0`). Node: `docs/research/core-trimmed-pass/after.json` (`dirtyAtStart` empty, `baseCommit` = `B0`, `baseDirty` empty). .NET: `dotnet-after/` and `dotnet-before-short/` (see their `provenance.json`).

Seven Node processes, each with 10 warmup and 31 timed rounds per operation. A round times the operation once per framework in a seeded random order, each on a fresh list after a forced GC. Before timing, each operation's resulting rows (order and text) are checked equal across the frameworks.

The Before tables use the full settings. The After section uses reduced settings: Node 3 processes, each with 5 warmup and 15 timed rounds (3×(5+15)), and .NET `ListPassBenchmarks` with the BenchmarkDotNet `Short` job (3 warmup, 3 iterations, 1 launch). `ProjectionBenchmarks` was not re-run. BenchmarkDotNet reports the Host line as DEBUG: that is the launcher only; the measured job is Release.

Each .NET benchmark runs two passes and alternates between two states of equal cost. With `Layout`, an effect reads every row after every key change, which is linear in the list length in both versions.

## Before

### Node: Ranvier against Solid 2 (milliseconds per operation)

| Operation | Solid 2 ms | Ranvier ms | Ratio Ranvier / Solid 2 [q1, q3] | Per-process medians |
| --- | --- | --- | --- | --- |
| create1k | 1.4107 | 4.3832 | 3.079 [2.539, 3.747] | 3.32, 3.09, 2.94, 3.01, 3.21, 2.99, 2.86 |
| create10k | 7.1480 | 25.3979 | 3.428 [2.605, 4.602] | 3.51, 3.34, 3.09, 3.50, 3.88, 3.48, 3.93 |
| replace1k | 1.8512 | 4.4127 | 2.407 [1.988, 2.866] | 2.69, 2.26, 2.32, 2.43, 2.12, 2.42, 2.59 |
| append1k | 1.6978 | 3.6939 | 2.136 [1.709, 2.740] | 2.45, 2.05, 2.32, 1.87, 1.97, 2.20, 2.03 |
| update10th1k | 0.2224 | 0.1736 | 0.805 [0.599, 1.060] | 0.73, 0.78, 0.81, 0.87, 0.84, 0.83, 0.68 |
| swap1k | 0.2092 | 0.8253 | 4.175 [3.062, 5.668] | 4.09, 4.10, 3.98, 3.75, 4.36, 4.62, 4.62 |
| swap10k | 0.8850 | 6.9082 | 8.207 [5.428, 12.073] | 10.13, 9.34, 6.05, 8.42, 7.45, 8.69, 8.44 |
| remove1k | 0.2747 | 1.0608 | 3.815 [3.050, 5.009] | 3.53, 4.22, 3.61, 3.76, 3.79, 3.73, 4.09 |
| remove10k | 1.2121 | 7.0198 | 5.942 [4.457, 8.061] | 6.05, 5.94, 5.01, 5.00, 6.19, 7.85, 5.98 |
| clear1k | 0.6671 | 1.5776 | 2.446 [1.840, 3.366] | 2.58, 2.82, 2.52, 2.40, 2.12, 3.43, 2.10 |
| clear10k | 3.3818 | 9.0862 | 2.663 [2.032, 3.771] | 2.53, 2.83, 2.37, 2.63, 2.60, 3.50, 2.48 |

### .NET: ListPassBenchmarks

| Benchmark | Rows | Layout | Mean | Allocated |
| --- | --- | --- | --- | --- |
| Swap | 1000 | False | 35.36 us | 15.93 KB |
| RemoveAndRestore | 1000 | False | 34.79 us | 16.65 KB |
| UpdateOneItem | 1000 | False | 33.19 us | 8.10 KB |
| ReplaceAll | 1000 | False | 433.49 us | 1461.22 KB |
| ClearAndCreate | 1000 | False | 202.62 us | 730.70 KB |
| Swap | 1000 | True | 53.96 us | 15.99 KB |
| RemoveAndRestore | 1000 | True | 53.94 us | 16.71 KB |
| UpdateOneItem | 1000 | True | 33.1 us | 8.10 KB |
| ReplaceAll | 1000 | True | 465.45 us | 1461.28 KB |
| ClearAndCreate | 1000 | True | 192.52 us | 730.76 KB |
| Swap | 10000 | False | 313.68 us | 156.55 KB |
| RemoveAndRestore | 10000 | False | 309.24 us | 157.27 KB |
| UpdateOneItem | 10000 | False | 310.7 us | 78.41 KB |
| ReplaceAll | 10000 | False | 10,178.97 us | 14609.66 KB |
| ClearAndCreate | 10000 | False | 2,908.06 us | 7304.91 KB |
| Swap | 10000 | True | 530.9 us | 156.62 KB |
| RemoveAndRestore | 10000 | True | 545.11 us | 157.34 KB |
| UpdateOneItem | 10000 | True | 375.76 us | 78.41 KB |
| ReplaceAll | 10000 | True | 11,677.75 us | 14609.72 KB |
| ClearAndCreate | 10000 | True | 3,267.12 us | 7304.98 KB |

### .NET: ProjectionBenchmarks

| Benchmark | Items | Mean | Allocated |
| --- | --- | --- | --- |
| ReadRow | 8 | 8.8 ns | 0 B |
| EditOneItem | 8 | 289.0 ns | 184 B |
| Reorder | 8 | 261.9 ns | 440 B |
| ChurnOneKey | 8 | 440.8 ns | 752 B |
| ReadRow | 64 | 8.8 ns | 0 B |
| EditOneItem | 64 | 1200.9 ns | 184 B |
| Reorder | 64 | 1417.9 ns | 2456 B |
| ChurnOneKey | 64 | 1327.0 ns | 1424 B |
| ReadRow | 512 | 8.8 ns | 0 B |
| EditOneItem | 512 | 8320.2 ns | 184 B |
| Reorder | 512 | 10517.0 ns | 18584 B |
| ChurnOneKey | 512 | 8666.7 ns | 6800 B |

## After

Settings: Node 3×(5+15) (3 processes, 5 warmup and 15 timed rounds per operation); .NET `Short` job on `ListPassBenchmarks` only. `before.json` used 7×(10+31) and records `B0` in two-framework rounds (Solid 2, Ranvier); it is not paired with `after.json`. In `after.json` each round times three frameworks (Solid 2, `B0` Ranvier, `A1` Ranvier) in a seeded random order, so "before ms" below is `baseMs` from the same rounds and the after/before ratio (`vsBase`) is paired within a round.

### Node: A1 against B0 and Solid 2 (milliseconds per operation)

| Operation | Solid 2 ms | Before ms (`B0`, paired) | After ms (`A1`) | After / Solid 2 [q1, q3] | After / before [q1, q3] | Per-process after / before medians | Per-process after / Solid 2 medians |
| --- | --- | --- | --- | --- | --- | --- | --- |
| create1k | 1.2453 | 3.6133 | 3.9221 | 3.103 [2.735, 3.446] | 1.084 [0.966, 1.196] | 1.11, 1.04, 1.10 | 3.15, 3.10, 3.10 |
| create10k | 5.6715 | 21.6306 | 23.0245 | 3.661 [2.579, 5.145] | 1.086 [0.916, 1.340] | 1.05, 1.09, 1.15 | 3.89, 3.39, 4.51 |
| replace1k | 1.7315 | 3.4101 | 3.8446 | 2.196 [2.026, 2.596] | 1.101 [0.977, 1.245] | 1.16, 1.18, 1.01 | 2.15, 2.19, 2.43 |
| append1k | 1.4108 | 2.9030 | 2.7840 | 1.932 [1.732, 2.310] | 0.931 [0.840, 1.171] | 0.99, 1.04, 0.91 | 2.26, 1.89, 1.86 |
| update10th1k | 0.1708 | 0.1599 | 0.1425 | 0.801 [0.683, 1.080] | 0.835 [0.701, 1.188] | 0.81, 0.79, 0.96 | 0.76, 0.77, 0.86 |
| swap1k | 0.1567 | 0.7869 | 0.3949 | 2.708 [2.182, 3.603] | 0.541 [0.394, 0.680] | 0.55, 0.58, 0.44 | 3.23, 2.98, 2.18 |
| swap10k | 0.8334 | 5.7030 | 2.1991 | 2.816 [1.814, 3.212] | 0.399 [0.345, 0.452] | 0.40, 0.40, 0.40 | 2.90, 2.37, 2.83 |
| remove1k | 0.1874 | 0.7886 | 0.3957 | 1.954 [1.684, 2.498] | 0.468 [0.363, 0.599] | 0.59, 0.49, 0.39 | 2.53, 1.76, 1.90 |
| remove10k | 1.2164 | 5.7163 | 2.5552 | 2.178 [1.804, 2.670] | 0.475 [0.395, 0.535] | 0.51, 0.47, 0.45 | 2.18, 2.11, 2.21 |
| clear1k | 0.5966 | 1.1569 | 1.3782 | 2.309 [1.905, 2.847] | 1.142 [0.969, 1.366] | 1.05, 1.23, 1.21 | 2.56, 2.16, 2.25 |
| clear10k | 3.2409 | 7.7152 | 9.1500 | 2.820 [2.485, 3.772] | 1.158 [1.027, 1.499] | 1.44, 1.15, 1.13 | 2.78, 2.66, 2.87 |

### .NET: ListPassBenchmarks (separate runs, not paired)

Both columns use the `Short` job. The `B0` run is in `dotnet-before-short/`, the `A1` run in `dotnet-after/`. The full-job `B0` record in `dotnet-before/` is not used here. With 3 iterations the reported errors are large for `ReplaceAll` and `ClearAndCreate` (see the `*-report-github.md` files).

| Benchmark | Rows | Layout | Before mean (`B0`) | After mean (`A1`) | After / before | Before allocated | After allocated |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Swap | 1000 | False | 32.77 us | 7.57 us | 0.231 | 15.93 KB | 23.87 KB |
| RemoveAndRestore | 1000 | False | 31.35 us | 6.88 us | 0.219 | 16.65 KB | 24.59 KB |
| UpdateOneItem | 1000 | False | 30.77 us | 4.95 us | 0.161 | 8.10 KB | 8.18 KB |
| ReplaceAll | 1000 | False | 398.11 us | 478.29 us | 1.201 | 1461.22 KB | 1469.16 KB |
| ClearAndCreate | 1000 | False | 176.74 us | 230.24 us | 1.303 | 730.70 KB | 734.78 KB |
| Swap | 1000 | True | 49.16 us | 28.56 us | 0.581 | 15.99 KB | 23.93 KB |
| RemoveAndRestore | 1000 | True | 55.71 us | 27.91 us | 0.501 | 16.71 KB | 24.65 KB |
| UpdateOneItem | 1000 | True | 32.00 us | 5.13 us | 0.160 | 8.10 KB | 8.18 KB |
| ReplaceAll | 1000 | True | 419.50 us | 486.66 us | 1.160 | 1461.28 KB | 1469.22 KB |
| ClearAndCreate | 1000 | True | 188.26 us | 251.56 us | 1.336 | 730.76 KB | 734.84 KB |
| Swap | 10000 | False | 335.69 us | 62.33 us | 0.186 | 156.55 KB | 234.80 KB |
| RemoveAndRestore | 10000 | False | 328.11 us | 54.97 us | 0.168 | 157.27 KB | 235.52 KB |
| UpdateOneItem | 10000 | False | 332.48 us | 49.08 us | 0.148 | 78.41 KB | 78.49 KB |
| ReplaceAll | 10000 | False | 8,158.88 us | 12,792.54 us | 1.568 | 14609.66 KB | 14687.91 KB |
| ClearAndCreate | 10000 | False | 2,354.78 us | 3,441.81 us | 1.462 | 7304.91 KB | 7344.16 KB |
| Swap | 10000 | True | 466.66 us | 284.53 us | 0.610 | 156.62 KB | 234.87 KB |
| RemoveAndRestore | 10000 | True | 483.62 us | 268.16 us | 0.554 | 157.34 KB | 235.59 KB |
| UpdateOneItem | 10000 | True | 326.44 us | 47.76 us | 0.146 | 78.41 KB | 78.49 KB |
| ReplaceAll | 10000 | True | 10,570.31 us | 10,249.01 us | 0.970 | 14609.72 KB | 14687.91 KB |
| ClearAndCreate | 10000 | True | 2,802.27 us | 3,860.96 us | 1.378 | 7304.98 KB | 7344.22 KB |

### What moved and what did not

- Node swap and remove: after / before is 0.541 (swap1k), 0.399 (swap10k), 0.468 (remove1k) and 0.475 (remove10k); each interval [q1, q3] lies below 1 and every per-process median lies below 1.
- Node create, replace, clear: after / before is 1.084 (create1k), 1.086 (create10k), 1.101 (replace1k), 1.142 (clear1k) and 1.158 (clear10k). Only the clear10k interval [1.027, 1.499] lies above 1; the other four intervals span 1 with medians above 1. append1k is 0.931 [0.840, 1.171] and update10th1k is 0.835 [0.701, 1.188]; both intervals span 1.
- Remaining Node gaps to Solid 2 (after / Solid 2 median): swap1k 2.708, swap10k 2.816, remove1k 1.954, remove10k 2.178, create1k 3.103, create10k 3.661, replace1k 2.196, append1k 1.932, clear1k 2.309, clear10k 2.820. update10th1k is 0.801 [0.683, 1.080].
- .NET without `Layout`: Swap, RemoveAndRestore and UpdateOneItem take 0.231, 0.219 and 0.161 of the before mean at 1000 rows, and 0.186, 0.168 and 0.148 at 10000 rows. With `Layout`: Swap and RemoveAndRestore are 0.581 and 0.501 at 1000 rows and 0.610 and 0.554 at 10000 rows; UpdateOneItem is 0.160 and 0.146.
- .NET ReplaceAll and ClearAndCreate: after / before is between 0.970 and 1.568 across the eight rows, with the `Short` job's large reported errors; no direction is established.
- .NET allocation: Swap and RemoveAndRestore allocate about 7.9 KB more per operation at 1000 rows and about 78 KB more at 10000 rows. UpdateOneItem allocates 0.08 KB more; ReplaceAll and ClearAndCreate allocate 4 to 8 KB more at 1000 rows and 39 to 78 KB more at 10000 rows.

## Rerun after the nested-pass fixes

C1 = `5b4e3b4` (`perf/core-trimmed-pass`, after the nested-pass fixes, the traced-build rerun guard and the shared under-construction reader). Node: `after-fixes.json`, the same settings and pairing as `after.json` (3×(5+15), `B0` as `base` in the same rounds). .NET: `dotnet-after-fixes/`, the `Short` job on `ListPassBenchmarks`, a separate run from `dotnet-after/` (see its `provenance.json`).

| Operation | Solid 2 ms | Before ms (`B0`, paired) | After ms (`C1`) | After / Solid 2 [q1, q3] | After / before [q1, q3] | Per-process after / before medians |
| --- | --- | --- | --- | --- | --- | --- |
| create1k | 0.9978 | 2.6283 | 2.7732 | 2.712 [2.512, 3.086] | 1.070 [0.996, 1.116] | 1.05, 1.05, 1.11 |
| create10k | 4.8659 | 16.4635 | 17.8855 | 3.513 [2.516, 4.542] | 1.087 [0.951, 1.181] | 1.04, 1.10, 1.04 |
| replace1k | 1.3376 | 2.6616 | 2.8227 | 2.123 [1.941, 2.447] | 1.049 [0.897, 1.182] | 1.04, 1.08, 1.03 |
| append1k | 1.0932 | 2.2926 | 2.0565 | 1.881 [1.725, 2.103] | 0.911 [0.811, 1.012] | 0.89, 0.91, 0.95 |
| update10th1k | 0.1111 | 0.0730 | 0.0707 | 0.663 [0.560, 0.750] | 0.926 [0.783, 1.093] | 0.98, 0.94, 0.87 |
| swap1k | 0.0840 | 0.4696 | 0.2513 | 3.188 [2.425, 3.974] | 0.500 [0.473, 0.591] | 0.56, 0.50, 0.48 |
| swap10k | 0.3846 | 3.7676 | 1.8092 | 5.062 [3.819, 6.170] | 0.471 [0.435, 0.549] | 0.47, 0.49, 0.46 |
| remove1k | 0.1122 | 0.5272 | 0.2716 | 2.534 [2.201, 2.779] | 0.513 [0.440, 0.598] | 0.48, 0.54, 0.49 |
| remove10k | 0.6370 | 4.0651 | 1.8765 | 2.900 [2.344, 3.459] | 0.452 [0.416, 0.508] | 0.43, 0.46, 0.45 |
| clear1k | 0.3823 | 0.8394 | 1.0324 | 2.484 [2.198, 3.034] | 1.158 [1.007, 1.312] | 1.23, 1.08, 1.19 |
| clear10k | 2.0243 | 4.8308 | 5.8558 | 2.850 [2.291, 3.217] | 1.197 [0.991, 1.423] | 1.18, 1.18, 1.26 |

### .NET: C1 against A1 (separate runs, not paired)

| Benchmark | Rows | Layout | `A1` mean | `C1` mean | `C1` / `A1` | `A1` allocated | `C1` allocated |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Swap | 1000 | False | 7.571 us | 5.797 us | 0.766 | 23.87 KB | 23.8 KB |
| RemoveAndRestore | 1000 | False | 6.879 us | 5.481 us | 0.797 | 24.59 KB | 24.52 KB |
| UpdateOneItem | 1000 | False | 4.947 us | 4.519 us | 0.913 | 8.18 KB | 8.12 KB |
| ReplaceAll | 1000 | False | 478.290 us | 370.718 us | 0.775 | 1469.16 KB | 1469.09 KB |
| ClearAndCreate | 1000 | False | 230.237 us | 161.183 us | 0.700 | 734.78 KB | 734.72 KB |
| Swap | 1000 | True | 28.562 us | 23.517 us | 0.823 | 23.93 KB | 23.87 KB |
| RemoveAndRestore | 1000 | True | 27.915 us | 23.631 us | 0.847 | 24.65 KB | 24.59 KB |
| UpdateOneItem | 1000 | True | 5.128 us | 4.373 us | 0.853 | 8.18 KB | 8.12 KB |
| ReplaceAll | 1000 | True | 486.664 us | 378.282 us | 0.777 | 1469.22 KB | 1469.16 KB |
| ClearAndCreate | 1000 | True | 251.559 us | 241.428 us | 0.960 | 734.84 KB | 734.78 KB |
| Swap | 10000 | False | 62.326 us | 46.726 us | 0.750 | 234.8 KB | 234.74 KB |
| RemoveAndRestore | 10000 | False | 54.970 us | 48.706 us | 0.886 | 235.52 KB | 235.46 KB |
| UpdateOneItem | 10000 | False | 49.083 us | 43.454 us | 0.885 | 78.49 KB | 78.43 KB |
| ReplaceAll | 10000 | False | 12,792.538 us | 8,208.468 us | 0.642 | 14687.91 KB | 14687.84 KB |
| ClearAndCreate | 10000 | False | 3,441.814 us | 2,536.530 us | 0.737 | 7344.16 KB | 7344.09 KB |
| Swap | 10000 | True | 284.527 us | 228.185 us | 0.802 | 234.87 KB | 234.8 KB |
| RemoveAndRestore | 10000 | True | 268.155 us | 229.998 us | 0.858 | 235.59 KB | 235.52 KB |
| UpdateOneItem | 10000 | True | 47.758 us | 43.029 us | 0.901 | 78.49 KB | 78.43 KB |
| ReplaceAll | 10000 | True | 10,249.010 us | 8,176.409 us | 0.798 | 14687.91 KB | 14687.91 KB |
| ClearAndCreate | 10000 | True | 3,860.961 us | 2,515.374 us | 0.651 | 7344.22 KB | 7344.16 KB |

### What the fixes changed

- Node: every after / before median is within 0.1 of its `A1` value, and every `A1` median lies inside the matching `C1` interval [q1, q3] except swap10k (`A1` 0.399, `C1` [0.435, 0.549]). Swap and remove stay below 1 in every interval; create and clear stay at 1.07 to 1.20.
- .NET allocation: every row is within 0.07 KB of `A1`. The build before C1 (`e34c138`) created one closure per memo-free row and allocated about 47 B more per created row (ReplaceAll 1515.97 KB at 1000 rows); the shared reader removed it.
- .NET time: every `C1` mean is 0.64 to 0.96 of the `A1` mean. The runs are not paired, and the Node Solid 2 timings in the same session are also 20 to 50% below those in `after.json`, so the machine ran faster; no speed-up is attributed to the fixes.
