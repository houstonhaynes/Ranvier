(**
---
title: Aggregates
order: 6
---
*)
(**
:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

Keep totals, counts and other folds current as [projection](collections.fsx) rows change.
An aggregate is a `Memo<'S>`: read, track and dispose it like any other memo.
*)
(*** hide ***)
#load "../../literate.fsx"

open Ranvier
open Ranvier.Docs.Maps

let graph = new Graph ()
let active = graph.Activate ()

(**
The examples on this page run inside an active graph, as in [Getting started](getting-started.md), over one list of
tasks:
*)

type Task = { Id: int; Estimate: int; Done: bool }

let tasks =
    createSignal
        [
            { Id = 1; Estimate = 3; Done = false }
            { Id = 2; Estimate = 5; Done = true }
            { Id = 3; Estimate = 2; Done = false }
        ]

let rows = createProjection _.Id id (fun () -> tasks.Value)

(**
## Choosing an aggregate

- For totals and counts, use `sumBy` and `countBy`.
- For any/all checks, use `exists` and `forall`.
- For a fold with an inverse, use `foldGroup` to update changed contributions.
- For a minimum or maximum, use `tryMinBy` and `tryMaxBy`; for another order-dependent result, use `fold` to recompute from cached values.
- For one aggregate per group, use `Grouping.aggregate` over a `groupBy` (see [Collection views](collection-views.fsx)).

:::details Aggregate functions and row-edit costs

| Function | Result | Cost of a row edit |
|----------|--------|--------------------|
| `Projection.foldGroup add subtract zero` | the fold of every settled value with `add` | O(1) calls: `subtract` of the old value, `add` of the new |
| `Projection.foldGroupBy projection add subtract zero result` | `result` of the `foldGroup` of `projection` over the values | as `foldGroup` |
| `Projection.fold folder state` | the fold of every settled value in `Keys` order | O(N) calls of `folder` over the cached values |
| `Projection.sumBy projection` | the sum of `projection` over the values | as `foldGroup` |
| `Projection.countBy predicate` | the count of values satisfying `predicate` | as `foldGroup` |
| `Projection.exists predicate` | whether `countBy predicate` is above zero | as `foldGroup` |
| `Projection.forall predicate` | whether `countBy` of the misses is zero | as `foldGroup` |
| `Projection.length`, `Projection.isEmpty` | the key count; whether it is zero | no work: a row edit wakes neither |
| `Projection.tryHead`, `tryLast`, `tryItem index` (`head`, `last`, `item` raise) | the key at a position with its value | O(1); an edit off the position wakes no reader |
| `Projection.tryFind`, `tryFindKey`, `tryPick` (`find`, `findKey`, `pick` raise) | the first match in upstream order | one predicate call; a membership flip costs an O(N) filter pass |
| `Projection.tryAverageBy projection` (`averageBy` raises) | the mean of `projection` over the settled values | as `foldGroup` |
| `Projection.tryMinBy`, `tryMaxBy projection` (`minBy`, `maxBy` raise) | a value with the least or greatest rank | O(1); O(N) when the holder leaves or changes |

:::

:::details Membership changes and row reads

A key added to or removed from `Keys` costs an O(N) key diff, then one `add` or `subtract` per changed key. Each
aggregate reads only the rows that changed, so an edit to one row of a thousand re-runs the row's function alone.

:::

Change one row's estimate, then remove another row. The total updates its contributions,
and the count changes only when membership changes.

*)
(*** map replay code=collapsed ***)
let estimates = createSignal [ 1, 3; 2, 5; 3, 2 ]
let estimateRows = createProjection fst snd (fun () -> estimates.Value)
let estimateTotal = estimateRows |> Projection.sumBy id
let estimateCount = estimateRows |> Projection.countBy (fun _ -> true)
createEffect (fun () -> printfn "estimate = %d, tasks = %d" estimateTotal.Value estimateCount.Value)

controls
    [
        button "Estimate task 1 at 8" (fun () ->
            estimates.Value <-
                estimates.Value
                |> List.map (fun (key, estimate) -> key, (if key = 1 then 8 else estimate)))
        |> describe "One contribution grows by 5; membership stays at three tasks."
        |> expect "One contribution grows by 5; membership stays at three tasks." (fun () -> estimateTotal.Peek = 15 && estimateCount.Peek = 3)
        button "Remove task 2" (fun () ->
            estimates.Value <-
                estimates.Value
                |> List.filter (fun (key, _) -> key <> 2))
        |> describe "Removing the estimate of 5 leaves a total of 10 across two tasks."
        |> expect "Removing the estimate of 5 leaves a total of 10 across two tasks." (fun () -> estimateTotal.Peek = 10 && estimateCount.Peek = 2)
    ]

(**
## Folds with an inverse

`Projection.foldGroup` updates a total by removing old contributions and adding new ones:

- An added key adds its value.
- A removed key subtracts its last value.
- A changed row subtracts its old value and adds its new value.

`subtract` must undo `add`: `subtract (add s v) v` must equal `s`.
*)

let totalEstimate =
    rows
    |> Projection.foldGroup (fun s t -> s + t.Estimate) (fun s t -> s - t.Estimate) 0

totalEstimate.Value // 10

tasks.Value <-
    tasks.Value
    |> List.map (fun t -> if t.Id = 1 then { t with Estimate = 8 } else t)

totalEstimate.Value // 15

(**
`Projection.foldGroupBy` folds a projection of each value and finishes the state with `result`. The memo owns the
view of projected values and the fold.
*)

let meanEstimate =
    rows
    |> Projection.foldGroupBy
        _.Estimate
        (fun (struct (sum, count)) estimate -> struct (sum + estimate, count + 1))
        (fun (struct (sum, count)) estimate -> struct (sum - estimate, count - 1))
        (struct (0, 0))
        (fun (struct (sum, count)) -> if count = 0 then 0 else sum / count)

meanEstimate.Value // 5

(**
:::details Pending and failed rows

The fold reads settled values. A pending row keeps its last settled value in the state, as in `Snapshot`, and
contributes from its first settled value; the upstream's `AnyPending` reports it. While a row is failed, reading the
aggregate raises the row's exception, and the fold recovers once the row settles.

:::

The state returns to exactly `zero` when the last row value leaves it.

## The shorthands

Use these shorthands for common totals and predicates. Their functions re-run for changed rows.
A pending or throwing function behaves like a pending or failed row.

:::details How the shorthands are built
Each is `Projection.foldGroupBy`: a `Projection.map` view and a `foldGroup` that the returned memo owns.
:::
*)

let remaining =
    rows
    |> Projection.sumBy (fun t -> if t.Done then 0 else t.Estimate)

let doneCount = rows |> Projection.countBy _.Done
let anyDone = rows |> Projection.exists _.Done
let allDone = rows |> Projection.forall _.Done

(remaining.Value, doneCount.Value, anyDone.Value, allDone.Value) // (10, 1, true, false)

tasks.Value <-
    tasks.Value
    |> List.map (fun t -> { t with Done = true })

(remaining.Value, doneCount.Value, anyDone.Value, allDone.Value) // (0, 3, true, true)

(**
An empty projection gives `exists` as `false` and `forall` as `true`.

:::details Floating-point rounding, infinity and NaN

`sumBy` over `float` or `float32` subtracts in floating point, so a long run of edits can leave a rounding error in
the last digits. While the sum is infinite or NaN, each row change re-adds every cached value in O(N), so removing
an infinite or NaN row restores a finite sum.

:::

## Folds with no inverse

Use `Projection.fold` for an order-dependent result whose old contribution cannot be subtracted. For a minimum or
maximum, use `Projection.tryMinBy` and `tryMaxBy`.
It folds cached values in `Keys` order after any key or value change. This costs O(N) folder calls,
but reads only the changed rows again.
*)

let longest =
    rows
    |> Projection.fold (fun best t -> max best t.Estimate) 0

let order =
    rows
    |> Projection.sortBy _.Estimate
    |> Projection.fold (fun ids t -> ids @ [ t.Id ]) []

(longest.Value, order.Value) // (8, [ 3; 2; 1 ])

(**
## Reading other nodes

A signal or memo read inside `add`, `subtract` or `folder` is tracked. When it changes, the aggregate re-folds every
cached value with the new reading.
*)

let rate = createSignal 2

let cost =
    rows
    |> Projection.foldGroup (fun s t -> s + t.Estimate * rate.Value) (fun s t -> s - t.Estimate * rate.Value) 0

cost.Value // 30
rate.Value <- 3
cost.Value // 45

(**
:::warning Create nodes outside the aggregate

These functions re-run on row changes, outside the calling scope. A node created inside one of them raises
`InvalidOperationException`; create it outside the aggregate and read it inside.

:::
*)

(**
## Size

`Projection.length` and `Projection.isEmpty` follow `Keys` alone: a row edit wakes neither, and `isEmpty` wakes its
readers only when the projection becomes empty or stops being empty. Both also read a projection from
`createProjectionOnce`.
*)

let scores = createSignal [ 1, 40; 2, 75; 3, 60 ]
let scoreRows = createProjection fst snd (fun () -> scores.Value)
let scoreCount = scoreRows |> Projection.length
let noScores = scoreRows |> Projection.isEmpty

(scoreCount.Value, noScores.Value) // (3, false)

(**
## Position

`Projection.tryHead`, `tryLast` and `tryItem` read the key at a position with its value, and `None` when the position
is outside `Keys`. They depend exclusively on `Keys` and the row at the position; over a sorted view, `tryHead` is
the top row. A pending row at the position makes the memo pending, and a failed one
raises.

`Projection.head`, `last` and `item` raise `KeyNotFoundException` instead of returning `None`. The memo fails while
the position is empty, so every reader needs a boundary or `TryValue`; prefer the `try` forms for bindings.
*)

let firstScore = scoreRows |> Projection.tryHead

let topScore =
    scoreRows
    |> Projection.sortBy (fun score -> -score)
    |> Projection.tryHead

let secondScore = scoreRows |> Projection.tryItem (fun () -> 1)

(firstScore.Value, topScore.Value, secondScore.Value) // (Some (1, 40), Some (2, 75), Some (2, 75))

(**
## Search

`Projection.tryFind`, `tryFindKey` and `tryPick` return the first match in upstream order. Each owns a `filter` (or
`choose`) view, so the function re-runs only for changed rows, and the filter's rules decide the candidates: a
throwing predicate skips its key, and a pending one keeps the key's last membership. `find`, `findKey` and `pick`
raise `KeyNotFoundException` while nothing matches.
*)

let firstPass =
    scoreRows
    |> Projection.tryFindKey (fun score -> score >= 50)

firstPass.Value // Some 2

(**
## Averages

`Projection.tryAverageBy` keeps a running sum and count, so a row edit costs O(1), and reads `None` while no value has
settled. `Projection.averageBy` raises `KeyNotFoundException` instead. Both follow `sumBy` for infinity and NaN, and
accept `float`, `float32` and `decimal`; `int` does not compile, as with `List.averageBy`.
*)

let meanScore = scoreRows |> Projection.tryAverageBy float

meanScore.Value // Some 58.33333333

(**
## Least and greatest

`Projection.tryMinBy` and `tryMaxBy` return a value with the least or greatest rank, under the `sortBy` order: a NaN
rank is the greatest. Among equal ranks the current result stays, including when its holder's row is edited to an
equal rank; when the holder is removed, the result is the first holder in `Keys` order. A row change costs O(1) unless
it removes or edits the holder's row, which re-folds the cached ranks in O(N). `minBy` and `maxBy` raise
`KeyNotFoundException` while no value has settled.
*)

let lowest = scoreRows |> Projection.tryMinBy id
let highest = scoreRows |> Projection.tryMaxBy id

(lowest.Value, highest.Value) // (Some 40, Some 75)

(**
## Lifetime

The aggregate belongs to the scope that created it. Disposing the aggregate, or that scope, disposes it with the views
and folds it built; it stops observing the rows, and reads return the last state.

*)

(**
Behavior tests: [Combinators.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/Combinators.fs).

*)

(*** hide ***)
active.Dispose ()
graph.Dispose ()
