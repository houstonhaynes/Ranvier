# Projection operations, batch 1: design

Date: 2026-10-20
Branch: `feat/projection-ops`
Baseline: master `98e0980` (build: use Partas.Build 0.8.2 (#116))
Input: the scoping document `projection-gap-scoping.md` (gap table, first batch, per-task tests, Q1–Q10). Citations
below are `file:line` at `98e0980`. `C.fs` is `src/Ranvier/Combinators.fs`, `P.fs` is `src/Ranvier/Projections.fs`,
`Core.fs` is `src/Ranvier/Core.fs`, `T/C.fs` is `tests/Ranvier.Tests/Combinators.fs`.
Status: draft for review.

## 1. Purpose and boundaries

Add the first batch of incremental `Projection` operations, preceded by an ownership fix that every memo-returning
composition inherits.

The batch includes:

- **Task 0 (ownership).** A memo returned by a `Projection` aggregate owns and disposes every node it builds: the
  intermediate views, the `ProjectionFold`, and any inner memo. Covers `foldGroup`, `fold`, `sumBy`, `countBy`,
  `exists`, `forall` today, and every new memo below. Adds one public function, `Projection.foldGroupBy` (ruling R1).
- **The 15 scoped functions**, in the scoping order: `length`, `isEmpty`; `tryHead`, `tryLast`, `tryItem`; `tryFind`,
  `tryFindKey`, `tryPick`; `sortByDescending`; `tryAverageBy`; `tryMinBy`, `tryMaxBy`; `partition`; `distinctBy`;
  `Grouping.aggregate`.
- **The raising counterparts** of every partial function: `head`, `last`, `item`, `find`, `findKey`, `pick`,
  `averageBy`, `minBy`, `maxBy`.

It excludes: the C# LINQ names (Q10), `tryMin`/`tryMax`/`distinct`/`sum`/`average` aliases, a heap-backed extreme,
`sortWith`, `append`, joins, and every other batch-2 candidate in the scoping document. No existing public signature
changes; no package reference is added.

## 2. Decisions

### 2.1 The user's decisions (binding)

1. **Scope.** Task 0 first, then the 15 functions in the scoping order.
2. **Partial results: both forms.** Each partial function gets an option-returning `try*` memo and a raising
   counterpart. The raising form is a thin layer over the `try*` form (section 4), never a second incremental
   implementation. `averageBy` on an empty projection is partial, so the option form is `tryAverageBy` and the raising
   form takes the plain name `averageBy` (the scoping document's Q1 alias).
3. **Min/max.** An O(N) recompute when the current extreme leaves, through the existing `ProjectionFold`
   throwing-subtract path (`C.fs:704-710`, `841-863`); O(1) otherwise. On ties the current result stays. A `float` or
   `float32` NaN orders last, as in `sortBy`. A heap is batch 2.
4. **Q6–Q10** settled below with the scoping recommendations.

### 2.2 Rulings

- **Q2 — Ruling:** positional memos (`tryHead`, `tryLast`, `tryItem` and their raising forms) use `Get` semantics: a
  pending row at the position makes the memo pending, and a failed row makes it raise the row's exception — they are
  reads, not folds, and match `Get` and boundaries; settled semantics would need `GetSettled` and would let a
  never-settled head vanish silently — cost if wrong: a UI that wants "last known head while loading" wraps the memo in
  a boundary instead of reading it directly.
- **Q6 — Ruling:** `partition` builds one inclusion helper owned by an internal owner parented to the calling scope;
  each half is independently disposable, and the helper lives until the calling scope is disposed — the halves are
  independent values a caller may drop one at a time, and sharing halves the predicate calls — cost if wrong: after
  both halves are disposed the helper and its per-key predicate rows stay allocated (idle, unobserved) until the scope
  goes; a later reference count is additive.
- **Q7 — Ruling:** `distinctBy` keeps the upstream key of the first holder and returns `Projection<'K,'V>` — it
  composes with every `Projection<'K,'V>` operation, and keying by the distinct value is `groupBy f` plus
  `Grouping.aggregate` — cost if wrong: an output key changes when the holder changes, so a key reader sees
  `Removed old` / `Added new` on a hand-over; a value-keyed variant is an addition, not a break.
- **Q8 — Ruling:** `tryAverageBy`/`averageBy` are SRTP (`inline`, `[<NoDynamicInvocation>]`) over
  `LanguagePrimitives.DivideByInt`, for `float`, `float32` and `decimal` (and user types with `+`, `-`, `Zero` and
  `DivideByInt`); `int` does not compile, as with `List.averageBy` — `List.averageBy` parity, gated by Task 5's first
  red test passing under Fable — cost if wrong: if Fable rejects `DivideByInt` for a type, Task 5 falls back to
  `float`-only (`'V -> float`), a signature narrowing made before release.
- **Q9 — Ruling:** `Grouping.aggregate` lives in a new `[<RequireQualifiedAccess>]` module `Grouping` with
  `[<CompilationRepresentation(ModuleSuffix)>]` (compiled `GroupingModule`) — it keeps `Projection.foldGroup`, the
  algebraic "group", apart from grouping — cost if wrong: a later move to `Projection.aggregateGroups` is a rename
  before 1.0.
- **Q10 — Ruling:** no C# LINQ names in this batch — option results do not map cleanly to `FirstOrDefault`, and the
  F# signatures should settle first — cost if wrong: C# callers reach the new functions through `ProjectionModule`
  until a follow-up adds `First`, `Min`, `Max`, `Average`, `DistinctBy`, `OrderByDescending`, `Count`, `Any()`.
- **R1 — Ruling:** add public `Projection.foldGroupBy`, a non-inline `map` + `foldGroup` + result function that owns
  its intermediates; the inline `sumBy`, `tryAverageBy` and `averageBy` call it — F# rejects a public `inline`
  function that calls an internal one (error FS1113, reproduced on SDK 10.0.100), so an inline aggregate can reach an
  owning composition only through a public function — cost if wrong: one extra public function in the baseline;
  removing it later is a breaking change.
- **R2 — Ruling:** every raising form raises `System.Collections.Generic.KeyNotFoundException` with a fixed message
  per function (section 4.2) — it is the exception `Projection.Get` raises for an absent key, and F#'s own `find`,
  `findKey` and `pick` raise it; `List.head`, `List.item`, `List.minBy` and `List.averageBy` raise
  `ArgumentException` because their input is invalid at call time, while a reactive memo's emptiness is a state —
  cost if wrong: changing the type later is a behavioural break pinned by tests on both targets.
- **R3 — Ruling:** a raising form is one memo whose body is the `try*` form's body followed by a function that returns
  the `Some` value or raises one exception instance cached per memo — `Failure.Moved` compares exception instances
  (`Core.fs:1472`), so a fresh exception per run would wake every reader on each re-run while still empty; one memo
  instead of two also keeps the inline `averageBy` within R1 — cost if wrong: a reader of a raising form sees the same
  exception object across empty periods, so mutating its `Data` would leak between them.
- **R4 — Ruling:** `length`, `isEmpty`, `tryHead`, `tryLast`, `tryItem`, `head`, `last` and `item` accept a
  `createProjectionOnce` projection; every other new function calls `RequireRowMemos` — the first group reads only
  `Keys` and `Get`, which serve memo-free rows (`P.fs:1286-1300`), while views and folds read row memos — cost if
  wrong: a later rejection of once-projections in the first group would be a break.
- **R5 — Ruling:** the `item` message names no index ("Projection.item: the projection has no key at the index.") —
  R3's single cached exception requires one message per memo — cost if wrong: a debugging user reads the index from
  their own signal.

## 3. Ownership (Task 0)

### 3.1 The gap

A composed aggregate does not own its intermediates (`C.fs:1336-1371`). `exists` creates a `map` view, a fold, a count
memo and a bool memo, all in the calling scope. `foldGroup` creates a `ProjectionFold` and a memo, both in the calling
scope (`C.fs:1304-1306`). Disposing only the returned memo leaves the fold observing every row (`FoldRow` edges) and,
for `sumBy`/`countBy`/`exists`/`forall`, the `map` view observing the upstream rows, until the calling scope goes.
The fold also keeps a reference to the disposed memo, so the memo is not collectable.

### 3.2 Contract

A memo returned by any `Projection` aggregate or element memo owns the nodes the function built for it. Disposing the
memo disposes them; disposing the calling scope disposes the memo and them. A disposed memo reads its last value, as
today (`Core.fs:3340-3346`).

### 3.3 Mechanism

- `Memo<'T>` gains `member internal Adopt: companion: IDisposable -> unit`. `Dispose` disposes the companion after its
  own teardown; `Adopt` on a disposed memo disposes the companion at once. One companion per memo.
- `ProjectionFold` implements `IDisposable` (its existing `Dispose`).
- `foldGroup` and `fold` create the fold and memo in the calling scope as today, then `memo.Adopt fold`.
- A private `ownedScope graph` creates `new Owner (graph.Root)` parented to `graph.CurrentOwner` (the key-scope
  pattern, `P.fs:2028-2029`). Composed memos build their intermediates inside it with `graph.RunOwned` and then
  `memo.Adopt owner`. The returned memo itself is created in the calling scope, so its trace owner is unchanged.
- A private `foldOwned graph rows add subtract zero result` builds the fold over `rows ()` inside an owned scope and
  returns a memo whose body is `result (fold.Compute ValueNone)`; the fold drives that memo directly, so a
  `result` adds no node. `foldGroupBy`, `sumBy`, `countBy`, `exists`, `forall`, `tryAverageBy`, `averageBy`,
  `tryMinBy`, `tryMaxBy`, `minBy`, `maxBy` use it. `exists`/`forall` lose their intermediate count memo: the bool is
  the fold's result.

Disposal order: the calling scope tears down in reverse acquisition order, so the memo (acquired after its owned
scope) is released first and disposes the owned scope; the later release of the owned scope link is idempotent.
`Memo.Dispose` → owned scope → `ProjectionFold.Dispose` → `memo.Dispose` is a no-op on the second entry.

Creating an aggregate inside a pure memo body raises `InvalidOperationException` from `graph.CurrentOwner`, before
any node is attached, as creating a view does today.

### 3.4 `foldGroupBy`

```fsharp
val foldGroupBy :
    projection: ('V -> 'U) -> add: ('S -> 'U -> 'S) -> subtract: ('S -> 'U -> 'S) -> zero: 'S -> result: ('S -> 'R) ->
    upstream: Projection<'K, 'V> -> Memo<'R>
```

A memo of `result` applied to the `foldGroup add subtract zero` of `projection` over the values of `upstream`.
`projection` re-runs per changed upstream row, in a view the memo owns; `result` re-runs when the fold state moves.
Pending, failed, cost and disposal as `foldGroup`. A null `subtract` raises `ArgumentNullException` (.NET).

## 4. The raising forms

### 4.1 How a failing memo reports

A memo whose body raises records a `Failure` with origin = the memo (`Core.fs:3244-3250`), sets `Status.Error`, and
keeps its last value. `Value` rethrows through `graph.Raise` (`Core.fs:3478-3487`); `TryValue` returns
`Failed ex`. A memo or effect reading it fails with the same `Failure` record, so `ErrorOrigin` points at the raising
memo (`Core.fs:2689-2709`). An effect records it in `Error` and never throws into the flush (`Core.fs:3770-3777`). A
later run that returns a value clears the failure and wakes readers. Pending wins: a body that raises
`NotReadyException` is pending, not failed.

### 4.2 Contract

For each partial function `f`, the raising form is `try f` followed by "the `Some` value, or raise":

| Raising form | `try` form | Exception while the `try` form reads `None` |
|---|---|---|
| `head` | `tryHead` | `KeyNotFoundException "Projection.head: the projection has no keys."` |
| `last` | `tryLast` | `KeyNotFoundException "Projection.last: the projection has no keys."` |
| `item` | `tryItem` | `KeyNotFoundException "Projection.item: the projection has no key at the index."` |
| `find` | `tryFind` | `KeyNotFoundException "Projection.find: no value satisfies the predicate."` |
| `findKey` | `tryFindKey` | `KeyNotFoundException "Projection.findKey: no value satisfies the predicate."` |
| `pick` | `tryPick` | `KeyNotFoundException "Projection.pick: the chooser returns None for every value."` |
| `averageBy` | `tryAverageBy` | `KeyNotFoundException "Projection.averageBy: the projection has no settled values."` |
| `minBy` | `tryMinBy` | `KeyNotFoundException "Projection.minBy: the projection has no settled values."` |
| `maxBy` | `tryMaxBy` | `KeyNotFoundException "Projection.maxBy: the projection has no settled values."` |

- While the `try` form would be pending, the raising form is pending. While it would raise an upstream row's
  exception, the raising form raises that exception, with that row's origin.
- The memo's `ErrorOrigin` while empty is the memo itself.
- One exception instance per memo: every empty period raises the same object, so a re-run that stays empty leaves
  readers asleep (R3).
- Recovery: the first run that finds a value publishes it, clears `Status.Error` and wakes readers.
- Creation never raises for emptiness: like every memo, the raising form is lazy.

### 4.3 Implementation

A private inline `orRaise (message: string) : 'T option -> 'T` allocates a `ref` holding the cached exception and
returns the function. Each raising form calls `orRaise` once at creation and composes it after the `try` body:

- positional and find forms: `Memo.Create (graph, (fun _ -> raiseEmpty (body ())), Pure)` over the same private body
  the `try` form uses;
- fold forms: the same `foldOwned` call as the `try` form, with `result >> raiseEmpty`.

`orRaise` is private inline and references only public members, so the inline `averageBy` can use it.

### 4.4 Conflicts with the existing failure semantics

1. The error channel was built for faults. A raising form turns an ordinary state (an empty projection, no match)
   into a failure that fails every downstream memo transitively, is recorded in every reading effect's `Error`, and
   stops at a boundary or a `TryValue`. The scoping document rejected raising forms for this reason; the user chose
   to ship them beside the `try` forms, and the docs steer readers to the `try` forms.
2. `Failure.Moved` compares instances, so a fresh exception per run would wake readers spuriously; R3 caches one.
3. The inline `averageBy` cannot reach internal helpers (FS1113), so it composes over the same fold with a raising
   result rather than over a second memo; R1 makes the fold composition public.
4. F#'s `List` raising functions raise `ArgumentException` for an empty list; R2 chooses `KeyNotFoundException`
   uniformly.
5. A raising form cannot distinguish "empty" from "upstream failed" by type when an upstream row raises
   `KeyNotFoundException` itself; `ErrorOrigin` distinguishes them (the memo for empty, the row otherwise).

## 5. Public interface

All in `module Projection` (`[<RequireQualifiedAccess>]`, compiled `ProjectionModule`) unless noted. N = upstream key
count.

### 5.1 Size (Task 1)

```fsharp
val length : upstream: Projection<'K, 'V> -> Memo<int>
val isEmpty : upstream: Projection<'K, 'V> -> Memo<bool>
```

`Keys.Length` and `Keys.Length = 0`. O(1) per `Keys` write; a row edit wakes neither. Pending rows count, as `Count`
does. A pending pass makes both pending; a failed pass makes both raise its exception. `isEmpty` wakes its readers
only on the empty/non-empty edge (memo cutoff).

### 5.2 Position (Task 2)

```fsharp
val tryHead : upstream: Projection<'K, 'V> -> Memo<('K * 'V) option>
val tryLast : upstream: Projection<'K, 'V> -> Memo<('K * 'V) option>
val tryItem : index: (unit -> int) -> upstream: Projection<'K, 'V> -> Memo<('K * 'V) option>
val head : upstream: Projection<'K, 'V> -> Memo<'K * 'V>
val last : upstream: Projection<'K, 'V> -> Memo<'K * 'V>
val item : index: (unit -> int) -> upstream: Projection<'K, 'V> -> Memo<'K * 'V>
```

The key at position 0, `Keys.Length - 1` or `index ()` with its value, `None` when the position is outside `Keys`
(negative included). Tracked reads of `Keys`, of the row at the position, and of `index`. O(1) per `Keys` write or
row edit at the position; an edit elsewhere wakes no reader. Q2 Get semantics. Over `sortBy`, `tryHead` is "top 1".
A throwing `index` fails the memo with its exception.

### 5.3 Search (Task 3)

```fsharp
val tryFind : predicate: ('V -> bool) -> upstream: Projection<'K, 'V> -> Memo<'V option>
val tryFindKey : predicate: ('V -> bool) -> upstream: Projection<'K, 'V> -> Memo<'K option>
val tryPick : chooser: ('V -> 'U option) -> upstream: Projection<'K, 'V> -> Memo<'U option>
val find : predicate: ('V -> bool) -> upstream: Projection<'K, 'V> -> Memo<'V>
val findKey : predicate: ('V -> bool) -> upstream: Projection<'K, 'V> -> Memo<'K>
val pick : chooser: ('V -> 'U option) -> upstream: Projection<'K, 'V> -> Memo<'U>
```

The first match in upstream order: `filter predicate` (or `choose chooser`) owned by the memo, then the head. The
function re-runs per changed upstream row; a membership flip costs the filter's O(N) pass; the memo is O(1). Filter
rules apply to the candidates: a throwing predicate excludes its key; a pending predicate keeps the key's last
membership; a key whose predicate has never settled takes no part until it settles. Q2 applies to the head: a head
whose predicate is pending makes the memo pending.

### 5.4 Descending sort (Task 4)

```fsharp
val sortByDescending : projection: ('V -> 'S) -> upstream: Projection<'K, 'V> -> Projection<'K, 'V> when 'S: comparison
```

`sortBy` with the order reversed: largest first; equal sort keys keep upstream order (stable, as
`List.sortByDescending`); a `float`/`float32` NaN sort key orders last; `None` orders after `Some`. Costs, pending,
failure and disposal as `sortBy`. `SortView` gains a direction; `isNaN`/`compareRanks` move to an internal `Ranks`
module shared with Task 6.

### 5.5 Average (Task 5)

```fsharp
val inline tryAverageBy : projection: ('V -> ^N) -> upstream: Projection<'K, 'V> -> Memo< ^N option >
    when ^N: (static member (+): ^N * ^N -> ^N) and ^N: (static member (-): ^N * ^N -> ^N)
     and ^N: (static member Zero: ^N) and ^N: (static member DivideByInt: ^N * int -> ^N) and ^N: equality
val inline averageBy : projection: ('V -> ^N) -> upstream: Projection<'K, 'V> -> Memo< ^N >   // same constraints
```

`foldGroupBy projection` over `struct (sum, count)`, then `DivideByInt sum count`, `None` (or the raise) at count 0.
O(1) per row edit; the infinity/NaN rule of `sumBy` (`C.fs:1341-1351`): while the sum is non-finite each row change
re-adds every cached value. Settled semantics as `foldGroup`.

### 5.6 Extremes (Task 6)

```fsharp
val tryMinBy : projection: ('V -> 'S) -> upstream: Projection<'K, 'V> -> Memo<'V option> when 'S: comparison
val tryMaxBy : projection: ('V -> 'S) -> upstream: Projection<'K, 'V> -> Memo<'V option> when 'S: comparison
val minBy : projection: ('V -> 'S) -> upstream: Projection<'K, 'V> -> Memo<'V> when 'S: comparison
val maxBy : projection: ('V -> 'S) -> upstream: Projection<'K, 'V> -> Memo<'V> when 'S: comparison
```

A value with the least (greatest) rank under the `sortBy` order: NaN orders after every other rank, so `tryMaxBy` is
NaN-holding when any rank is NaN and `tryMinBy` reaches a NaN only when every rank is NaN. Among equal ranks the
current result stays, including when its holder's row is edited to an equal rank; when the holder is removed, the
result is the first holder in `Keys` order.

- Rows: an owned key-aware map view `struct (key, projection value, value)`.
- Fold state: `Extreme = { Count; Key; Rank; Value }` (internal struct record).
- `add` is O(1): a better rank replaces the holder.
- `subtract` of a non-holder is O(1). `subtract` of the holder raises, so the fold re-folds the cached values in
  O(N) (`C.fs:704-710`).
- Worst case: draining the extremes is O(N) per removal, and editing the holder's row is O(N) because the fold applies
  an edit as subtract-then-add.

### 5.7 Partition (Task 7)

```fsharp
val partition : predicate: ('V -> bool) -> upstream: Projection<'K, 'V> -> Projection<'K, 'V> * Projection<'K, 'V>
```

`(filter predicate, filter (not << predicate))` over one shared inclusion helper, so the predicate runs once per
changed row across both halves. `FilterView` gains an `admit: bool`. A throwing predicate excludes the key from both
halves, and `Get`/`TryGet` of either raise. A pending predicate keeps the key on its last side. A never-settled key is
in `PendingKeys` of both. Ownership: Q6.

### 5.8 Distinct (Task 8)

```fsharp
val distinctBy : projection: ('V -> 'G) -> upstream: Projection<'K, 'V> -> Projection<'K, 'V> when 'G: equality
```

The first upstream key per distinct `projection` value, in upstream order, as `List.distinctBy`. When the holder
leaves or changes value, the next holder enters. O(N) per pass with a `KeySet<'G>`; an edit that keeps every
holder's value publishes no `Keys`. Pending, never-settled and failed keys follow `filter` (`C.fs:212-234`): a
pending value keeps the key's last settled value, a never-settled one is in `PendingKeys`, a failed one is excluded
and its `Get` raises.

### 5.9 Grouping aggregate (Task 9)

```fsharp
[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Grouping =
    val aggregate : perGroup: (Projection<'K, 'V> -> Memo<'S>) -> grouping: Grouping<'G, 'K, 'V> -> Projection<'G, 'S>
```

One memo per group, created once by `perGroup` in the group row's key scope (`mapWith`), keyed and ordered as the
grouping. The inner view is the same object for the life of a group row (an emptied group is removed in the same
pass, `C.fs:479-487`; a returning group gets a new row and a new memo). A row reads the memo: its pending or failure
is the row's. Ungrouped keys contribute to no group. Disposing the result disposes every memo; disposing the grouping
empties the result.

## 6. Cross-cutting constraints

- **Fable.** Every test compiles and runs under Fable; `WeakReference` retention tests are `.NET only` inside
  `#if !FABLE_COMPILER` with a `// .NET only: <reason>` first line (`Report.mjs:84-107`). No `SortedSet`,
  `PriorityQueue`; order from `Keys` arrays and staged lists, never `KeyMap.Iterate`. NaN detection only through
  `Ranks.isNaN`. No user-parameter null sentinels.
- **Tracing.** No new `Tracer` call. The owned scope is traced by `Owner.SetParent` (`Tracer.OwnerAdopt`); views are
  `Projection`s and results are `Memo`s. Every test runs in the traced and the untraced suite.
- **NativeAOT.** `tests/Ranvier.AotSmoke` stays warning-free; Tasks 2 and 6 add a smoke line each.
- **Public API baseline.** `docs/.ai/public-api-baseline.txt`: additions only (25 `ProjectionModule`/`GroupingModule`
  members, `$W` witness variants of the inline functions, `type Ranvier.GroupingModule`). No line removed or changed.
- **Docs.** `docs/content/guide/aggregates.fsx` (memos) and `collection-views.fsx` (views), each with a "Tests
  covering this behaviour" block. `docs/maps/authoring/MapFence.fs`'s aggregate regex lists every memo-returning
  `Projection` function, pinned by `docs/maps/tests/AuthoringTests.fs`.
- **Comments.** `.claude/rules/comments.md`; no `FOR-REVIEW` left at commit.

## 7. Testing

Each task follows the scoping document's tests (section 3 there), adapted to the rulings above, plus the Review
Focus tests of the plan. Raising-form tests pin the exception type, the exact message, `ErrorOrigin`, one wake per
transition, and recovery, on .NET and Fable. Ownership tests pin disposal through the upstream row's observer count
(both targets) and through `WeakReference` (.NET).
