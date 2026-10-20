# PR #120 arm64 .NET verdict: project-chain and app-table

CI run on PR #120 (base 61674f4, head 878572c) flagged two .NET scenarios on Neoverse-N2 instruction counts:

| Scenario | Base | Head | Change |
|---|---:|---:|---:|
| app-table | 9,136,008 | 9,824,145 | +7.53% |
| project-chain | 3,843,586 | 4,247,854 | +10.52% |

Verdict: **measurement state**. The PR adds no work on either path.

## Identical work

- `git diff --stat 61674f4..878572c -- src/` is empty.
- Every library counter, `bytes` (204,824 and 422,712.8 B/op), and the GC and JIT flags are identical between base and head.
- With the CI worker environment (`DOTNET_TieredCompilation=0`, `DOTNET_TieredPGO=0`, `DOTNET_ReadyToRun=0`), `DOTNET_JitDisasmSummary` lists 3,664 compiled methods on both sides. The lists have the same methods in the same order with the same code sizes. The only differences are the line numbers in the names of the `Template.fs` closures, plus the order of JSON serializer methods compiled after the last case.

## The extra instructions are a per-process state

The figures for each process in `ab-arm64-counters.json` take discrete values. Each value is the base floor plus k × c, where c is fixed per scenario: 134.8K for project-chain and 229.5K for app-table. Both scenarios show the same k in every process:

| process | b0 | h0 | b1 | h1 | b2 | h2 | b3 | h3 | b4 | h4 |
|---|---|---|---|---|---|---|---|---|---|---|
| k | 0 | 3 | 0 | 2 | 0 | 3.25 | 1 | 1 | 1 | 4 |

- c/project-chain : c/app-table = 1.703. Both scenarios sort through `SortView.compareRanks`, which calls FSharp.Core's `compare` on a generic `'S`, so `GenericComparisonIntrinsic<int>` boxes both values and dispatches `IComparable.CompareTo` through `GenericCompare`. The sort makes 4,214 such calls per operation in project-chain and 7,171 in app-table, a ratio of 1.702. Each step of k therefore costs 32.0 instructions per comparison in both scenarios.
- The extra instructions retire at about 5.5 per cycle (+404K instructions, +73K cycles on h0's project-chain), which fits a repeated cached lookup rather than new work.
- No other scenario moves with k.
- The `perf-data` history shows the same states on master before #42 added server-render, including on docs-only commits. For example, 305d009 (docs) has k = 3 in h1, 7853137 has k = 3 in h2 and h4, and 72df34d has k = 2 in b2. Base 61674f4 itself has k = 1 in b3 and b4 on this run.

The cost of the boxed comparison's dispatch depends on runtime cache state that varies from process to process (type-handle addresses and the cache entries already present). The library's work does not change it.

## Local check (Windows x64, ETW PMC, CI worker environment)

InstructionRetired per op, as median and range, over 8 worker processes per side:

| Scenario | base 61674f4 | head 878572c |
|---|---|---|
| project-chain | 2,994,876 (2,994,171–2,996,796) | 2,995,018 (2,993,318–3,006,194) |
| app-table | 7,379,171 (7,359,726–7,405,340) | 7,389,494 (7,356,412–7,411,577) |

Base and head match. x64 Windows stays at one state here, so the arm64 steps do not reproduce.

## Harness fix ruled out

- The bench loads `Ranvier.View.Portable` and runs the `ServerFixtures` initialisers lazily, when server-render's warm run JIT-compiles it. That run comes just before project-chain.
- Moving server-render after `control` left project-chain and app-table unchanged (2,998,633 and 7,414,313), and it raised server-render's own count from about 491K to 593K.
- The states also predate server-render.

Removing the state needs a library change: for example, comparing sort keys through `LanguagePrimitives.FastGenericComparer<'S>` instead of the boxed `compare`. That belongs in its own PR.
