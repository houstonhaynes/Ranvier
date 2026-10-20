(**
---
title: Collection views
---
*)
(*** hide ***)
#load "../../literate.fsx"

open Ranvier
open Ranvier.Docs.Maps

let graph = new Graph ()
let active = graph.Activate ()
(**


The examples run inside an [active graph](graph.fsx).

Derive a live view from a projection with `filter`, `choose`, `map`, `mapWith`, `sortBy`, `sortByDescending`, `partition`, `distinctBy` or `groupBy`, and aggregate each group of a `groupBy` with
`Grouping.aggregate`. Each view keeps rows by key and re-runs its function only for changed upstream rows.

As with a source projection, `Keys` tracks membership and order; `Get key` tracks a row value.

These operators work with reactive row values, including `createProjectionWith` and editable
collections' `Rows`. They reject the fixed factory results of `createProjectionOnce` with
`NotSupportedException`; use the ordinary factory form when rows feed a collection view.

## Filter and map


*)
type Todo = { Id: int; Title: string }
(**


*)
let catalogue =
    createSignal [ { Id = 1; Title = "a" }; { Id = 2; Title = "bb" }; { Id = 3; Title = "ccc" } ]

let upstreamTitles = createProjection _.Id _.Title (fun () -> catalogue.Value)

let longTitles =
    upstreamTitles
    |> Projection.filter (fun title -> title.Length > 1)

let lengths = upstreamTitles |> Projection.map String.length
longTitles.Keys
(**

```text
[|2; 3|]
```


*)
lengths.Get 3
(**

```text
3
```

:::details Cost of membership and order changes

Map membership consumes upstream key deltas, retaining unchanged rows and reusing the upstream key
array. Source membership changes copy the ordered key array. Filters, sorts and grouping still scan upstream keys on their
membership paths; their cached per-row functions avoid repeated user calls for unchanged rows.
Some aggregate membership paths also scan keys.

With an [editable keyed source](collection-updates.fsx#direct-edits), an existing-key write avoids
a whole-input collection pass and preserves the key array. This does not make every view's
membership or order processing proportional to the changed rows.

:::

### Sort rows

`Projection.sortBy` orders the keys ascending by a sort key under `compare`. Keys with equal sort keys keep their
upstream order, including after the upstream reorders. A `float` or `float32` NaN sort key comes after every other key
and `None` comes before `Some`, on .NET and under Fable; a NaN nested in a tuple, record or option orders as `compare`
orders it.


*)
let catalogueOrder =
    createSignal [ { Id = 1; Title = "bb" }; { Id = 2; Title = "a" }; { Id = 3; Title = "cc" } ]

let orderedTitles = createProjection _.Id _.Title (fun () -> catalogueOrder.Value)
let byLength = orderedTitles |> Projection.sortBy String.length
byLength.Keys
(**

```text
[|2; 1; 3|]
```

`Projection.sortByDescending` orders largest first. Equal sort keys keep their upstream order, a NaN sort key still
comes last, and `None` comes after `Some`.


*)
let longestFirst = orderedTitles |> Projection.sortByDescending String.length
longestFirst.Keys
(**

```text
[|1; 3; 2|]
```

:::details Sort costs

When every sort key, membership and the upstream order are unchanged, a `sortBy` pass costs O(N) and publishes no new
`Keys`; otherwise it re-sorts in O(N log N).

:::

### Choose values

`Projection.choose` keeps the keys whose chooser returns `Some`, each with the value inside it, in one view: the chooser
runs once per key per upstream row change. A change from one
`Some` value to another wakes only readers of the key's row. A pending or raising chooser follows the predicate rules
below.

:::details Pending and failed combinator rows

A predicate or sort key that raises leaves the key out of `Keys`, and `Get` of the key raises the exception. A mapping
that raises keeps the key, with the same `Get` behaviour. A pending predicate keeps the key's last membership; a
pending sort key keeps its last settled sort key. A key whose predicate or sort key has never settled appears in
`PendingKeys` and not in `Keys`; `AnyPending` counts only rows of keys in `Keys`.

In a chain, such a key is also in the `PendingKeys` of every view built on the one that holds it out, so the end of
`filter >> map` still reports it. A key excluded by a failed predicate or sort key is absent from every view built on
the excluding view, and only `Get` and `TryGet` of the excluding view raise its error. A view's `Status` and `Error`
describe its pass, not its rows.

:::

:::details Tests covering this behaviour

Pinned by `a throwing predicate excludes the key, and Get and TryGet raise its error`, `a pending predicate keeps
membership; a never-settled key is only in PendingKeys`, `an effect reading Keys and Get runs exactly once per write`,
`sorts ascending, and equal sort keys keep upstream order`, `sortByDescending sorts largest first, and equal sort
keys keep upstream order`, `sortByDescending orders NaN last, across updates`, `ties keep upstream order at 40 keys,
across reorder and removal`, `NaN sort keys sort after every other key, across updates`, `None sorts before Some,
across updates`, `a pass whose sort keys and upstream order are unchanged does not re-sort` and
`a pending sort key keeps its last settled sort key; a never-settled key is only in PendingKeys`, `a pending chooser
keeps membership; a never-settled key is only in PendingKeys`, `a throwing chooser excludes the key, and Get and TryGet
raise its error`, and the
`chained pending` tests
([Combinators.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/Combinators.fs)).
:::

### Create nodes per mapped row

`Projection.mapWith` is the factory form of `map`: its mapping runs once per key with the key and a tracked read of the
upstream value, and returns the key's reader. Nodes the mapping creates belong to the key and are disposed with it.


*)
let labels =
    orderedTitles
    |> Projection.mapWith (fun id title ->
        let length = createMemo (fun _ -> String.length (title ()))
        fun () -> $"{id}:{length.Value}")

labels.Get 3
(**

```text
"3:2"
```

### Group rows

`Projection.groupBy` groups the keys by a group key. The groups follow the upstream position of each group's first
member, and each group is an inner view of its keys in upstream order. A key whose group key changes leaves its old
group and joins its new one in the same pass. `groupBy` returns a `Grouping`, which is a
`Projection<'G, Projection<'K, 'V>>`; branches that must unify with a plain projection need an upcast.

:::details Pending groups and UngroupedKeys

A pending group key keeps the key's last settled group. A key whose group key raises or has never settled is in no
group: `UngroupedKeys` lists it, in upstream order, followed by the pending keys the upstream view holds out.
`GroupOf` returns a key's group, re-raises its group key's exception, or raises `NotReadyException` while its group
key has never settled.

:::


*)
let byLengthGroup = orderedTitles |> Projection.groupBy String.length
[ for length in byLengthGroup.Keys -> length, (byLengthGroup.Get length).Keys ]
(**

```text
[(2, [|1; 3|]); (1, [|2|])]
```

:::details Lifetime of an empty group

An inner view is disposed once its group is empty: a reader holding it sees empty `Keys`, and `Get` raises
`ObjectDisposedException`. A group that returns later has a new inner view.

:::

:::details Tests covering this behaviour

Pinned by the `Projection.mapWith` and `Projection.groupBy` tests, including `a never-settled key is in UngroupedKeys,
in upstream order, until it settles and joins its group` and `filter then groupBy lists the keys the filter holds out
in UngroupedKeys`
([Combinators.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/Combinators.fs)).
:::

### Split rows

`Projection.partition` returns the keys whose value satisfies a predicate and the rest, as two views over one
predicate view: the predicate runs once per changed row for both halves. A raising predicate leaves the key out of
both halves; a pending one keeps the key on its last side. Each half can be disposed on its own.

*)
let shortTitles, longerTitles = orderedTitles |> Projection.partition (fun title -> title.Length < 2)
(shortTitles.Keys, longerTitles.Keys)
(**

```text
([|2|], [|1; 3|])
```

:::details Tests covering this behaviour

Pinned by the `Projection.partition` tests, including `200 single edits at N=1000 make 200 predicate calls across
both halves` and `a pending predicate keeps the last side; a never-settled key is pending in both`
([Combinators.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/Combinators.fs)).
:::

### Distinct rows

`Projection.distinctBy` keeps the first key for each distinct value of a function, in upstream order, under that
key. When the holder leaves or changes value, the next key with the value takes its place, and a key reader sees the
old key removed and the new one added. Pending and raising functions follow the predicate rules above.

*)
let oneTitlePerLength = orderedTitles |> Projection.distinctBy String.length
oneTitlePerLength.Keys
(**

```text
[|1; 2|]
```

:::details Tests covering this behaviour

Pinned by the `Projection.distinctBy` tests, including `a key reader sees a hand-over as Removed and Added`, and
`distinctBy keys follow the law while values fail and recover`
([Combinators.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/Combinators.fs),
[Projections.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/Projections.fs)).
:::

### Aggregate each group

`Grouping.aggregate` turns a grouping into a projection whose row for each group is the value of that group's memo,
such as a total. The memo is created once per group, in the group's scope, so an emptied group disposes it and a returning group gets a new one.


*)
let titleCountByLength = byLengthGroup |> Grouping.aggregate Projection.length
[ for length in titleCountByLength.Keys -> length, titleCountByLength.Get length ]
(**

```text
[(2, 2); (1, 1)]
```

:::details Tests covering this behaviour

Pinned by the `Grouping.aggregate` tests, including `an edit within a group runs only that group's aggregate` and `an
emptied group's aggregate is disposed, and a returning group gets a new one`
([Combinators.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/Combinators.fs)).
:::

### Page through rows

`Projection.take`, `skip` and `sub` select keys by upstream position. Their count functions are
tracked, so a signal can drive the window.

:::details Counts and offsets outside the range
Negative offsets become 0. A negative `take` count or an offset beyond the last key gives an empty
window. A count past the end stops at the last key.
:::


*)
let page = createSignal 0

let pageOfTitles =
    orderedTitles
    |> Projection.sub (fun () -> page.Value * 2) (fun () -> 2)

pageOfTitles.Keys
(**

```text
[|1; 2|]
```

:::details Window reuse and costs

A slice pass costs O(window). A key that stays in the window keeps its row, so shifting the window by d positions
creates and disposes at most d rows, and a write to a row outside the window wakes no reader of the slice.

:::

:::details Tests covering this behaviour

Pinned by `take, skip and sub select positions and clamp counts as List.truncate and a clamped skip do`, `a key that
stays in a shifted window keeps its row` and `a write outside the window wakes no reader`
([Combinators.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/Combinators.fs)).
:::

To fold the values of a projection into one value, such as a total or a count, see [Aggregates](aggregates.fsx).

*)
(*** hide ***)
active.Dispose ()
graph.Dispose ()
