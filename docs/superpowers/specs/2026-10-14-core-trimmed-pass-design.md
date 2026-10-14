# A keyed projection pass that costs the size of the change

Date: 2026-10-14. Branch `perf/core-trimmed-pass`, from `master` at `e2e0f95`. Revised after two reviews (algorithm correctness; executability and provenance).

## Problem

A structural pass of a `KeyedProjection` visits every item of its source. For each item it calls `keyOf`, adds the key to `Seen` (the duplicate check and the pass's key set), looks the key up in `Entries`, and stages an item write. `ApplyDiff` then walks every entry against `Seen` to find removed keys. The source is read through a `seq<'T>` enumerator. On the core-only list benchmark (`docs/research/2026-10-14-core-list-solid.md` on `poc/fable-ranvier-dom`) this makes a swap 4.7× Solid 2's `mapArray` at 1,000 rows and 9.1× at 10,000, and a single removal 4.0× and 5.5×. Solid 2's cost follows the size of the change.

## Goal

A `KeyedProjection` pass whose hashing, `keyOf` calls, entry lookups and item writes are proportional to the number of changed positions. The work linear in the list length is: one copy of the item references, at most two identity comparisons per position, and building the key array when membership or order moved. `ObservePass`'s walk of the rows when nothing observes the key set or the summary stays linear; it is out of scope.

Five changes, all in `KeyedProjection` and the `Projection` steps it uses:

1. **Item identity before the key.** A kept item keeps its key and row without a `keyOf` call and without an item write. An item is kept when it is equal, under the graph's equality policy for `'T`, to the item it lines up with in the previous pass (see 2).
2. **Common prefix and suffix.** Items equal from the start (same index) and from the end (same offset from the end) are kept. When the previous and new lists have the same length, the middle is also compared index by index. Only the remaining changed positions are reconciled.
3. **Duplicate detection over the changed positions.** A changed key that occurs twice among the changed positions, or that has a row and is absent from the previous pass's changed keys, duplicates a kept key. Both fail the pass with the existing duplicate-key exception.
4. **Removed keys from the changed positions.** The removed keys are the previous pass's changed keys that are absent from this pass's changed keys. `ApplyDiff` does not walk every entry for a trimmed pass.
5. **An array fast path.** A source result that is an array (on Fable, a JavaScript array or typed array) is copied by `Array.blit` into the projection's own buffer. Any other `seq<'T>` is enumerated into that buffer.

## Core and view state

This change is core-only. The view (on `poc/fable-ranvier-dom`) uses `createProjectionOnce` rows and gains the speed when it merges `master`. No view state is added or duplicated.

## Base

`master` lacks `createProjectionOnce`, `ProjectionOptions.CreateFirst` and the held-error state, which the view's rows use and the benchmark measures. The branch first replays the seven core commits of PR #42 that add them (`a895955`, `55b9350`, `bd738d8`, `73c754c`, `c17251c`, `0cce240`, `a7b20d5`), cherry-picked as `44ef305` to `dc0ee50` inclusive. They touch `src/Ranvier`, `tests/Ranvier.Tests` and the Fable test list only. The PR for this branch carries them; if PR #42 merges first, they become no-op changes on merge.

## Contract change

- Equal items produce equal keys. An item equal, under the graph's equality policy, to the item it replaces in the previous pass's order (same index from the start, or same offset from the end) keeps its key and row without a call to `keyOf`. `keyOf` must return equal keys for equal items. Under the default `JsIdentityPolicy` a reference-type item whose key field is mutated in place keeps its old key until the item lands in a reconciled range or is replaced. A `keyOf` that reads untracked state (`untrack`, `Peek`, mutable fields) is not re-evaluated for kept items. Solid 2's `mapArray` already keys by identity.
- `keyOf` keeps its tracking context. A `keyOf` call that reads a tracked source, creates a node, or uses the pass's owner (`onCleanup`, `getOwner`) makes the projection run full passes from then on (see "Gate"), so a key that depends on a signal keeps updating.
- `source` is evaluated to completion before the first `keyOf` call of a pass. Today a lazy `seq` interleaves its enumeration with `keyOf`; an exception from a lazy source now wins over a duplicate key that precedes it.
- The order in which one pass retires its removed keys is unspecified. A duplicate-key failure names one of the duplicated keys.

## Design

### State kept by `KeyedProjection`

- `lastItems: 'T[]`, `lastCount: int`: the items of the last installed pass. A buffer owned by the projection.
- `lastKeys: 'K[]`: a private copy of the key array of the last installed pass. Never the array `Keys` returns to callers.
- `spare: 'T[]`: a reusable item buffer, or null while a pass holds it.
- `passItems: 'T[]`, `passCount: int`, `passRun: int`: the current pass's items and the value of `Runs` when it read them.
- `trimValid: bool`: true only between an installing `Applied` and the start of the next `Enumerate`.
- `keyPure: bool`: true until a `keyOf` call trips the gate. Never set back to true.

### A pass

1. `Enumerate` starts with `let trim = trimValid && keyPure`, then sets `trimValid <- false`. A pass nested in this one (a factory or cleanup writing a source and reading the projection) therefore runs in full.
2. Read: evaluate `source ()`, take `spare` (or allocate when it is null or too small), and fill it by `Array.blit` from an array source or by enumeration. Record `passItems`, `passCount`, `passRun <- this.Runs`. All loops in the pass read these through locals.
3. If `trim`, run the trimmed pass. Otherwise run the full pass: the existing per-item `Visit` loop, with the gate measured around each `keyOf`.
4. `Applied` (end of `SettleAdditions` on the snapshot path): if `this.Runs = passRun` and `keyPure`, install: the old `lastItems` becomes `spare`, `passItems` becomes `lastItems`, clear the stale tail references; copy the published key array into `lastKeys` when its reference changed since the last install; set `trimValid <- true`. Otherwise set `trimValid <- false` and return `passItems` to `spare`. With `keyPure` false no copy is made.
5. `Abandoned` (from `Abandon`): `trimValid <- false`; return `passItems` to `spare`.
6. Dispose releases `lastItems`, `lastKeys`, `spare` and `passItems`.

### Trimmed pass

With `prev = lastItems`, `prevKeys = lastKeys`, `n0 = lastCount`, `cur = passItems`, `n1 = passCount`, `same = comparer.Equals` for the graph's comparer of `'T` (the comparer of every row's item signal):

1. `start`: the first index where `start = min n0 n1` or `not (same prev[start] cur[start])`.
2. `end0`, `end1`: walking back from `n0 - 1` and `n1 - 1` while both are `>= start` and `same prev[end0] cur[end1]`. The previous middle is `start .. end0` and the new middle is `start .. end1`.
3. `aligned = (n0 = n1)`. Changed positions: under `aligned`, the middle indices `i` with `not (same prev[i] cur[i])`, collected once into a reused `int` buffer; otherwise every middle index.
4. `P`: the keys `prevKeys[i]` at the changed previous positions.
5. For each changed new position in order: `key = keyOf cur[i]` (gate measured). If `key` is already in `N`, raise the duplicate-key exception. Add it to `N`. If `Entries` has a row for `key` and `key` is not in `P`, raise the duplicate-key exception. Stage an add when there is no row, an item write otherwise.
6. Removed keys: `P` minus `N`, iterated over the changed previous positions only, staged on `Projection`.
7. Key array: unchanged when `aligned` and every changed position kept its key. Otherwise `prevKeys[0..start-1]`, the new middle's keys and `prevKeys[end0+1..n0-1]`, in a new array.
8. `P` and `N` are emptied by removing the keys added this pass, not by `Clear` (which costs the set's capacity on .NET).

A duplicate raised from step 5 precedes all staging; `Run` clears the staging and calls `Abandon`. The pass after it runs in full.

### `Projection` steps

- `ApplyDiff` takes the removed keys from the pass when the pass staged them, and otherwise walks `Entries` against `Seen` as today.
- `publishKeys` writes the staged key array when the pass staged one, and skips the write when the pass reported its keys unchanged. Otherwise it compares `PassKeys` as today.
- `IProjectionPass` gains `Applied` and `Abandoned`. `RowsOf` implements both as overridable no-ops. The grouping projection (`Combinators.fs`) implements them as no-ops.
- The projection counts `IComputation.AddSource` calls and `IScopeHost.Scope` requests.

### Gate

Around each `keyOf` call, a change in the projection's `AddSource` count, its `IScopeHost.Scope` request count, or the graph's node counter sets `keyPure <- false`. A `keyOf` that raises `NotReadyException` has tracked first.

### Consumers

Each needs a test against a trimmed pass:

- `KeyedProjection` built by the combinators: `filter` and `choose` (`inclusion`), `sortBy` (`sortKeys`), `groupBy` (`groupKeys`) and each group's view use an internal `KeyedProjection` keyed by `id` over an upstream key array, so they trim too. Their outer views keep their own passes.
- Hidden keys (`HideLast`, `SettleHidden`): used only by the filter and `sortBy` views, which keep the full pass over a trimmed inner projection.
- Pending rows (`inFlight`): a pending row at a kept position stays pending; a pending row whose key is removed leaves the summary.
- Delta readers (`KeyLog`): additions record through `AddEntry`, removals through `Retire`.
- `CreateFirst` rollback: a factory failure on a middle add disposes that pass's new rows, keeps the previous rows, holds the error until a source changes, and leaves `trimValid` false.
- Held error: the pass after a held error runs in full; the pass after it trims again.
- Re-entry: a `CreateFirst` factory that writes the source and reads a sibling row gives the same result as a full-pass projection.

`IndexProjection`, the `Collections` projection and the grouping projection keep the full pass.

## Testing

- **Differential test.** Seeded random operations (swap, move, insert, remove, replace with an equal-key copy, replace with a new key, append, clear, reverse, unchanged republish, a duplicate at a kept or changed position, and, for the create-first kinds, an item whose factory fails) on three projection kinds (`createProjectionWith`, `createProjectionWithOptions CreateFirst`, `createProjectionOnce CreateFirst`). Each kind runs as a trimmed projection and as a full-pass oracle whose `keyOf` reads an otherwise unused signal. After each operation both report equal `Keys`, row values, creations in order, disposals per operation as a multiset, key-reader changes as a set, `Status`, and failure kind.
- **Cost tests.** A swap of two items in 1,000 calls `keyOf` twice and creates no row. Removing one item calls `keyOf` zero times. Prepending one calls it once. Replacing one item with an equal-key copy calls it once and updates one row.
- **Duplicates.** A changed key equal to a prefix key, a suffix key, a key at an identity-kept middle position, another changed key, and a prefix key in a length-changing middle.
- **Contract.** An item mutated in place keeps its old key. A `keyOf` reading a signal keeps updating, including when the reading item enters after the first pass. A `keyOf` calling `onCleanup` keeps its cleanups registered every pass.
- **Sources.** `list`, lazy `seq` and array sources agree; an array mutated in place and republished is compared against the copy; an `int[]` source takes a trimmed pass (a typed array under Fable).
- **Consumers.** Every item under "Consumers".

## Measurements

- **Before:** commit `B0`, which adds the benchmarks to the cherry-picked core.
- **After:** commit `A1`, which completes the core change. `bench/Ranvier.CoreListBench/Fixture.fs` and its project are identical at both.
- **Node:** `bench/Ranvier.CoreListBench` (Ranvier against Solid 2 `mapArray`, `@solidjs/signals` 2.0.0-rc.14 prod, 7 processes × 10 warmup + 31 shuffled rounds). The after run loads the `B0` build as a third framework in the same rounds; before/after is that paired ratio.
- **.NET:** a BenchmarkDotNet class running the same list operations at 1,000 and 10,000 rows on `createProjectionOnce CreateFirst`, with and without the layout effect, plus the existing `ProjectionBenchmarks`; separate runs at `B0` and `A1`.
- Raw JSON for every run goes under `docs/research/core-trimmed-pass/`, with the measured commit, the dirty-file list captured by the tool before the run, and machine fields. BenchmarkDotNet artifacts are written outside the repository and copied in. The note `docs/research/2026-10-14-core-trimmed-pass.md` reports before and after. Figures are measurements, not gates.

## Not in scope

- `IndexProjection` and the views' own passes.
- A small-move path for a length-changing middle (Solid 2's `trySmallMove`).
- `ObservePass`'s walk of unobserved rows.
- Edge-removal cost on dispose (#54, deferred).
