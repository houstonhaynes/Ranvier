module Ranvier.Tests.Combinators

open System
open Expecto
open Ranvier

type private Item = { Id: int; N: int }

let private items (pairs: (int * int) list) =
    [ for id, n in pairs -> { Id = id; N = n } ]

let private setN id n (xs: Item list) =
    xs
    |> List.map (fun x -> if x.Id = id then { x with N = n } else x)

let private rows (source: Signal<Item list>) =
    createProjection (fun x -> x.Id) (fun x -> x.N) (fun () -> source.Value)

/// <summary>An effect reading <c>Keys</c> and then <c>Get</c> for each key, and the count of its runs.</summary>
let private keysAndRows (view: Projection<int, 'V>) =
    let runs = ref 0

    createEffect (fun () ->
        runs.Value <- runs.Value + 1

        for key in view.Keys do
            view.Get key |> ignore)

    runs

/// <summary>Writes <c>next</c> and expects exactly one more run in <c>runs</c>.</summary>
let private once (runs: int ref) (source: Signal<Item list>) (name: string) (next: Item list -> Item list) =
    let before = runs.Value
    source.Value <- next source.Value
    Expect.equal runs.Value (before + 1) name

[<Tests>]
let filterTests =
    testList
        "Projection.filter"
        [
            test "200 single edits at N=1000 make 200 predicate calls" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..1000 -> i, 2 * i ])
                let calls = ref 0

                let view =
                    rows source
                    |> Projection.filter (fun n ->
                        calls.Value <- calls.Value + 1
                        n % 4 = 0)

                keysAndRows view |> ignore
                calls.Value <- 0

                for i in 1..200 do
                    source.Value <- setN i (2 * i + 2) source.Value

                Expect.equal calls.Value 200 "one predicate call per edit"
            }

            test "a Keys reader wakes only when membership flips" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 4; 2, 8; 3, 3 ])

                let view =
                    rows source
                    |> Projection.filter (fun n -> n % 4 = 0)

                let runs = ref 0

                createEffect (fun () ->
                    view.Keys |> ignore
                    runs.Value <- runs.Value + 1)

                source.Value <- setN 1 12 source.Value
                Expect.equal runs.Value 1 "an edit that keeps membership"

                source.Value <- setN 1 6 source.Value
                Expect.equal runs.Value 2 "an edit that flips membership"

                source.Value <- setN 3 16 source.Value
                Expect.equal runs.Value 3 "an edit that flips a key in"
                Expect.sequenceEqual view.Keys [ 2; 3 ] "the new membership"
            }

            test "an effect reading Keys and Get runs exactly once per write" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..5 -> i, i ])

                let view =
                    rows source
                    |> Projection.filter (fun n -> n % 2 = 0)

                let runs = keysAndRows view

                once runs source "edit a member's value" (setN 2 6)
                once runs source "flip a member out" (setN 4 5)
                once runs source "flip a key in" (setN 1 8)
                once runs source "remove a member" (List.filter (fun x -> x.Id <> 2))
                once runs source "add a member" (fun xs -> xs @ items [ 6, 10 ])
                once runs source "reorder" List.rev
                Expect.sequenceEqual view.Keys [ 6; 1 ] "the final membership, in upstream order"
            }

            test "an unread view leaves an unread upstream idle" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 2; 2, 3 ])
                let up = rows source
                let view = up |> Projection.filter (fun n -> n % 2 = 0)

                for i in 1..10 do
                    source.Value <- setN 1 (2 * i) source.Value

                Expect.equal up.Runs 0 "no upstream pass"
                Expect.equal view.Runs 0 "no view pass"
            }

            test "a key removed and re-added while unread follows the new upstream row" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 2; 2, 4; 3, 5 ])

                let view =
                    rows source
                    |> Projection.filter (fun n -> n % 2 = 0)

                Expect.sequenceEqual view.Keys [ 1; 2 ] "initial membership"
                source.Value <- source.Value |> List.filter (fun x -> x.Id <> 2)
                source.Value <- source.Value @ items [ 2, 6 ]
                Expect.sequenceEqual view.Keys [ 1; 2 ] "key 2 re-added even"

                source.Value <- setN 2 7 source.Value
                Expect.sequenceEqual view.Keys [ 1 ] "key 2 made odd"
            }

            test "a pending predicate keeps membership; a never-settled key is only in PendingKeys" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flights = dict [ for n in [ 10; 11; 30 ] -> n, createAsyncSource<bool>() ]
                flights[10].Settle true
                let source = createSignal (items [ 1, 10; 3, 30 ])

                let view =
                    rows source
                    |> Projection.filter (fun n -> flights[n].Value)

                Expect.sequenceEqual view.Keys [ 1 ] "key 3 has never settled"
                Expect.sequenceEqual view.PendingKeys [ 3 ] "key 3 is pending"

                source.Value <- setN 1 11 source.Value
                Expect.sequenceEqual view.Keys [ 1 ] "key 1 keeps its last membership"

                flights[30].Settle true
                Expect.sequenceEqual view.Keys [ 1; 3 ] "key 3 joins once settled"
                Expect.isFalse (Array.contains 3 view.PendingKeys) "key 3 is no longer pending"

                flights[11].Settle false
                Expect.sequenceEqual view.Keys [ 3 ] "key 1 leaves once its predicate settles false"
            }

            test "a PendingKeys reader wakes when a never-settled key settles excluded" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flight = createAsyncSource<bool>()
                let source = createSignal (items [ 1, 1 ])

                let view =
                    rows source
                    |> Projection.filter (fun _ -> flight.Value)

                let seen = ResizeArray<int[]>()

                createEffect (fun () -> seen.Add view.PendingKeys)
                flight.Settle false

                Expect.sequenceEqual seen [ [| 1 |]; [||] ] "pending, then settled"
                Expect.isEmpty view.Keys "the key settled excluded"
            }

            test "a throwing predicate excludes the key, and Get and TryGet raise its error" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 2; 2, 4 ])

                let view =
                    rows source
                    |> Projection.filter (fun n -> if n = 13 then invalidOp "boom 13" else n % 2 = 0)

                let keysRuns = ref 0
                let getRuns = ref 0

                createEffect (fun () ->
                    view.Keys |> ignore
                    keysRuns.Value <- keysRuns.Value + 1)

                createEffect (fun () ->
                    getRuns.Value <- getRuns.Value + 1

                    try
                        view.Get 2 |> ignore
                    with _ ->
                        ())

                source.Value <- setN 2 13 source.Value

                Expect.sequenceEqual view.Keys [ 1 ] "key 2 is excluded"
                Expect.equal keysRuns.Value 2 "the Keys reader woke"
                Expect.equal getRuns.Value 2 "the Get reader woke"

                Expect.throwsC (fun () -> view.Get 2 |> ignore) (fun ex -> Expect.equal ex.Message "boom 13" "Get raises the predicate's error")

                Expect.throwsC (fun () -> view.TryGet 2 |> ignore) (fun ex -> Expect.equal ex.Message "boom 13" "TryGet raises the predicate's error")

                Expect.equal view.Status Status.None "the pass itself succeeded"

                source.Value <- setN 2 4 source.Value
                Expect.sequenceEqual view.Keys [ 1; 2 ] "key 2 returns once the predicate succeeds"
                Expect.equal (view.Get 2) 4 "and reads its value"
            }

            test "Get outside Keys raises KeyNotFoundException" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 2; 2, 3 ])

                let view =
                    rows source
                    |> Projection.filter (fun n -> n % 2 = 0)

                Expect.equal (view.Get 1) 2 "a member reads its value"

                Expect.throwsT<Collections.Generic.KeyNotFoundException> (fun () -> view.Get 2 |> ignore) "an excluded key"

                Expect.equal (view.TryGet 2) None "TryGet of an excluded key"
            }

            test "removing a key disposes the key's memo" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 10; 2, 20; 3, 30 ])
                let threshold = createSignal 0
                let calls = ResizeArray<int>()

                let view =
                    rows source
                    |> Projection.filter (fun n ->
                        calls.Add n
                        n > threshold.Value)

                keysAndRows view |> ignore
                source.Value <- source.Value |> List.filter (fun x -> x.Id <> 2)
                calls.Clear ()
                threshold.Value <- 15

                Expect.sequenceEqual (Seq.sort calls) [ 10; 30 ] "the removed key's predicate never runs again"
                Expect.sequenceEqual view.Keys [ 3 ] "the survivors re-evaluated"
            }

            test "disposing the view detaches it from the upstream" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 2; 2, 4 ])
                let calls = ref 0

                let view =
                    rows source
                    |> Projection.filter (fun n ->
                        calls.Value <- calls.Value + 1
                        n % 2 = 0)

                keysAndRows view |> ignore
                view.Dispose ()
                calls.Value <- 0
                source.Value <- setN 1 6 source.Value

                Expect.equal calls.Value 0 "no predicate runs after disposal"
                Expect.isEmpty view.Keys "a disposed view is empty"
            }
        ]

[<Tests>]
let chooseTests =
    let halves (n: int) =
        if n % 2 = 0 then Some (n / 2) else None

    testList
        "Projection.choose"
        [
            test "choose keeps the Some keys in upstream order with their values" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 4; 2, 3; 3, 8; 4, 6 ])
                let view = rows source |> Projection.choose halves

                Expect.sequenceEqual view.Keys [ 1; 3; 4 ] "the Some keys, in upstream order"
                Expect.sequenceEqual [ for key in view.Keys -> view.Get key ] [ 2; 4; 3 ] "the Some values"
                Expect.equal (view.TryGet 2) None "TryGet of a None key"

                Expect.throwsT<Collections.Generic.KeyNotFoundException> (fun () -> view.Get 2 |> ignore) "Get of a None key"
            }

            test "200 single edits at N=1000 make 200 chooser calls" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..1000 -> i, i ])
                let calls = ref 0

                let view =
                    rows source
                    |> Projection.choose (fun n ->
                        calls.Value <- calls.Value + 1
                        halves n)

                keysAndRows view |> ignore
                calls.Value <- 0

                for i in 1..200 do
                    source.Value <- setN i (i + 1) source.Value

                Expect.equal calls.Value 200 "one chooser call per edit"
            }

            test "a Keys reader wakes only when membership flips, and a Get reader when the value changes" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 4; 2, 8; 3, 3 ])
                let view = rows source |> Projection.choose halves
                let keysRuns = ref 0
                let getRuns = ref 0

                createEffect (fun () ->
                    view.Keys |> ignore
                    keysRuns.Value <- keysRuns.Value + 1)

                createEffect (fun () ->
                    view.TryGet 1 |> ignore
                    getRuns.Value <- getRuns.Value + 1)

                let passes = view.Runs
                source.Value <- setN 1 12 source.Value
                Expect.equal view.Runs passes "a Some to Some edit runs no view pass"
                Expect.equal keysRuns.Value 1 "and keeps membership"
                Expect.equal getRuns.Value 2 "and wakes the key's reader"
                Expect.equal (view.Get 1) 6 "with the new value"

                source.Value <- setN 2 16 source.Value
                Expect.equal getRuns.Value 2 "another key's edit leaves the reader asleep"

                source.Value <- setN 1 5 source.Value
                Expect.equal keysRuns.Value 2 "a Some to None edit flips membership"

                source.Value <- setN 3 10 source.Value
                Expect.equal keysRuns.Value 3 "a None to Some edit flips a key in"
                Expect.sequenceEqual view.Keys [ 2; 3 ] "the new membership"
                Expect.equal (view.Get 3) 5 "the new member's value"
            }

            test "an effect reading Keys and Get runs exactly once per write" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..5 -> i, i ])
                let view = rows source |> Projection.choose halves
                let runs = keysAndRows view

                once runs source "edit a member's value" (setN 2 6)
                once runs source "flip a member out" (setN 4 5)
                once runs source "flip a key in" (setN 1 8)
                once runs source "remove a member" (List.filter (fun x -> x.Id <> 2))
                once runs source "add a member" (fun xs -> xs @ items [ 6, 10 ])
                once runs source "reorder" List.rev
                Expect.sequenceEqual view.Keys [ 6; 1 ] "the final membership, in upstream order"
                Expect.sequenceEqual [ view.Get 6; view.Get 1 ] [ 5; 4 ] "the final values"
            }

            test "a chooser reading a signal re-runs when the signal changes" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 10; 2, 20; 3, 30 ])
                let threshold = createSignal 15
                let calls = ref 0

                let view =
                    rows source
                    |> Projection.choose (fun n ->
                        calls.Value <- calls.Value + 1

                        if n > threshold.Value then
                            Some (n - threshold.Value)
                        else
                            None)

                keysAndRows view |> ignore
                Expect.sequenceEqual view.Keys [ 2; 3 ] "initial membership"
                calls.Value <- 0
                threshold.Value <- 5

                Expect.equal calls.Value 3 "one chooser call per key"
                Expect.sequenceEqual view.Keys [ 1; 2; 3 ] "the new membership"
                Expect.sequenceEqual [ for key in view.Keys -> view.Get key ] [ 5; 15; 25 ] "the new values"
            }

            test "a pending chooser keeps membership; a never-settled key is only in PendingKeys" {
                use g = new Graph ()
                use _ = g.Activate ()

                let flights =
                    dict [ for n in [ 10; 11; 12; 30 ] -> n, createAsyncSource<int option>() ]

                flights[10].Settle(Some 1)
                let source = createSignal (items [ 1, 10; 3, 30 ])

                let view =
                    rows source
                    |> Projection.choose (fun n -> flights[n].Value)

                Expect.sequenceEqual view.Keys [ 1 ] "key 3 has never settled"
                Expect.sequenceEqual view.PendingKeys [ 3 ] "key 3 is pending"
                Expect.isFalse view.AnyPending "AnyPending counts rows of keys in Keys"

                source.Value <- setN 1 11 source.Value
                Expect.sequenceEqual view.Keys [ 1 ] "key 1 keeps its last membership"
                Expect.throwsT<NotReadyException> (fun () -> view.Get 1 |> ignore) "key 1's row is pending"
                Expect.isTrue view.AnyPending "key 1's row counts as pending"

                flights[30].Settle(Some 3)
                Expect.sequenceEqual view.Keys [ 1; 3 ] "key 3 joins once settled"
                Expect.equal (view.Get 3) 3 "with its value"
                Expect.isFalse (Array.contains 3 view.PendingKeys) "key 3 is no longer pending"

                flights[11].Settle None
                Expect.sequenceEqual view.Keys [ 3 ] "key 1 leaves once its chooser settles None"

                source.Value <- setN 1 12 source.Value
                Expect.sequenceEqual view.Keys [ 3 ] "a pending chooser keeps key 1 out"
                Expect.isEmpty view.PendingKeys "a key kept out after settling is not pending"
            }

            test "a throwing chooser excludes the key, and Get and TryGet raise its error" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 2; 2, 4 ])

                let view =
                    rows source
                    |> Projection.choose (fun n -> if n = 13 then invalidOp "boom 13" else halves n)

                let keysRuns = ref 0

                createEffect (fun () ->
                    view.Keys |> ignore
                    keysRuns.Value <- keysRuns.Value + 1)

                source.Value <- setN 2 13 source.Value

                Expect.sequenceEqual view.Keys [ 1 ] "key 2 is excluded"
                Expect.equal keysRuns.Value 2 "the Keys reader woke"

                Expect.throwsC (fun () -> view.Get 2 |> ignore) (fun ex -> Expect.equal ex.Message "boom 13" "Get raises the chooser's error")

                Expect.throwsC (fun () -> view.TryGet 2 |> ignore) (fun ex -> Expect.equal ex.Message "boom 13" "TryGet raises the chooser's error")

                Expect.equal view.Status Status.None "the pass itself succeeded"

                source.Value <- setN 2 4 source.Value
                Expect.sequenceEqual view.Keys [ 1; 2 ] "key 2 returns once the chooser succeeds"
                Expect.equal (view.Get 2) 2 "and reads its value"
            }

            test "disposing the view detaches it from the upstream" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 2; 2, 4 ])
                let calls = ref 0

                let view =
                    rows source
                    |> Projection.choose (fun n ->
                        calls.Value <- calls.Value + 1
                        halves n)

                keysAndRows view |> ignore
                view.Dispose ()
                calls.Value <- 0
                source.Value <- setN 1 6 source.Value

                Expect.equal calls.Value 0 "no chooser runs after disposal"
                Expect.isEmpty view.Keys "a disposed view is empty"
            }

            test "choose composes with filter and sortBy" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 8; 2, 3; 3, 4; 4, 12; 5, 20 ])

                let view =
                    rows source
                    |> Projection.filter (fun n -> n < 20)
                    |> Projection.choose halves
                    |> Projection.sortBy (fun n -> -n)

                let runs = keysAndRows view
                Expect.sequenceEqual view.Keys [ 4; 1; 3 ] "filtered, chosen, then sorted descending"
                Expect.sequenceEqual [ for key in view.Keys -> view.Get key ] [ 6; 4; 2 ] "the chosen values"

                once runs source "flip a key in" (setN 2 18)
                Expect.sequenceEqual view.Keys [ 2; 4; 1; 3 ] "key 2 joins at its rank"
                once runs source "filter a key out" (setN 4 24)
                Expect.sequenceEqual view.Keys [ 2; 1; 3 ] "key 4 leaves"

                let chosenFirst =
                    rows source
                    |> Projection.choose halves
                    |> Projection.filter (fun n -> n > 2)

                Expect.sequenceEqual chosenFirst.Keys [ 1; 2; 4; 5 ] "filter over the chosen values"
            }

            test "choose composes with choose, and keeps a Some None value" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 8; 2, 3; 3, 4 ])

                let nested =
                    rows source
                    |> Projection.choose (fun n -> Some (if n % 2 = 0 then Some n else None))

                Expect.sequenceEqual nested.Keys [ 1; 2; 3 ] "every key, Some None included"
                Expect.equal (nested.Get 2) None "the inner None is the value"

                let twice = nested |> Projection.choose id
                Expect.sequenceEqual twice.Keys [ 1; 3 ] "the second choose drops the inner None"

                source.Value <- items [ 1, 8; 2, 6; 3, 4 ]
                Expect.sequenceEqual twice.Keys [ 1; 2; 3 ] "key 2 joins once its value is even"
                Expect.equal (twice.Get 2) 6 "the unwrapped value"
            }
        ]

[<Tests>]
let mapTests =
    testList
        "Projection.map"
        [
            test "map follows upstream keys and order, and re-runs one mapping per edit" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 3 ])
                let calls = ref 0

                let view =
                    rows source
                    |> Projection.map (fun n ->
                        calls.Value <- calls.Value + 1
                        n * 10)

                keysAndRows view |> ignore
                calls.Value <- 0
                source.Value <- setN 2 5 source.Value

                Expect.equal calls.Value 1 "one mapping call"
                Expect.equal (view.Get 2) 50 "the new value"

                source.Value <- List.rev source.Value
                Expect.sequenceEqual view.Keys [ 3; 2; 1 ] "upstream order"
            }

            test "an effect reading Keys and Get runs exactly once per write" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..4 -> i, i ])
                let view = rows source |> Projection.map (fun n -> n + 1)
                let runs = keysAndRows view

                once runs source "edit a value" (setN 2 6)
                once runs source "remove a key" (List.filter (fun x -> x.Id <> 2))
                once runs source "add a key" (fun xs -> xs @ items [ 5, 5 ])
                once runs source "reorder" List.rev
            }

            test "a throwing mapping keeps the key, and Get raises its error" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])

                let view =
                    rows source
                    |> Projection.map (fun n -> if n = 13 then invalidOp "boom 13" else n)

                keysAndRows view |> ignore
                source.Value <- setN 2 13 source.Value

                Expect.sequenceEqual view.Keys [ 1; 2 ] "the key stays"

                Expect.throwsC (fun () -> view.Get 2 |> ignore) (fun ex -> Expect.equal ex.Message "boom 13" "Get raises the mapping's error")
            }

            test "removing a key disposes the key's memo" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 10; 2, 20 ])
                let offset = createSignal 0
                let calls = ResizeArray<int>()

                let view =
                    rows source
                    |> Projection.map (fun n ->
                        calls.Add n
                        n + offset.Value)

                keysAndRows view |> ignore
                source.Value <- source.Value |> List.filter (fun x -> x.Id <> 2)
                calls.Clear ()
                offset.Value <- 1

                Expect.sequenceEqual calls [ 10 ] "the removed key's mapping never runs again"
            }
        ]

let private comparisons = ref 0

/// <summary>A sort key whose comparisons are counted in <c>comparisons</c>.</summary>
[<CustomEquality; CustomComparison>]
type private Counted =
    | Counted of int

    override this.Equals other =
        match other with
        | :? Counted as o -> compare this o = 0
        | _ -> false

    override this.GetHashCode() =
        let (Counted n) = this in n

    interface IComparable with
        member this.CompareTo other =
            comparisons.Value <- comparisons.Value + 1
            let (Counted a) = this
            let (Counted b) = other :?> Counted
            compare a b

/// <summary>A sort key whose comparison raises when either operand is 666.</summary>
[<CustomEquality; CustomComparison>]
type private Boom =
    | Boom of int

    override this.Equals other =
        match other with
        | :? Boom as o -> compare this o = 0
        | _ -> false

    override this.GetHashCode() =
        let (Boom n) = this in n

    interface IComparable with
        member this.CompareTo other =
            let (Boom a) = this
            let (Boom b) = other :?> Boom

            if a = 666 || b = 666 then
                invalidOp "boom 666"

            compare a b

[<Tests>]
let sortByTests =
    testList
        "Projection.sortBy"
        [
            test "sorts ascending, and equal sort keys keep upstream order" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 5; 2, 3; 3, 5; 4, 1 ])
                let view = rows source |> Projection.sortBy id

                Expect.sequenceEqual view.Keys [ 4; 2; 1; 3 ] "ascending, ties in upstream order"
                Expect.equal (view.Get 3) 5 "a row reads its own value"

                source.Value <- List.rev source.Value
                Expect.sequenceEqual view.Keys [ 4; 2; 3; 1 ] "the tie follows the upstream reorder"

                source.Value <- setN 2 5 source.Value
                Expect.sequenceEqual view.Keys [ 4; 3; 2; 1 ] "a key joining a tie takes its upstream position"
            }

            test "ties keep upstream order at 40 keys, across reorder and removal" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..40 -> i, i % 3 ])
                let view = rows source |> Projection.sortBy id

                let expected () =
                    source.Value
                    |> List.sortBy (fun x -> x.N)
                    |> List.map (fun x -> x.Id)

                Expect.sequenceEqual view.Keys (expected ()) "initial ties in upstream order"

                source.Value <- List.rev source.Value
                Expect.sequenceEqual view.Keys (expected ()) "ties follow the upstream reorder"

                source.Value <-
                    source.Value
                    |> List.filter (fun x -> x.Id % 7 <> 0)

                Expect.sequenceEqual view.Keys (expected ()) "ties keep upstream order after removals"
            }

            test "NaN sort keys sort after every other key, across updates" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, -1; 2, 3; 3, -1; 4, 1 ])

                let view =
                    rows source
                    |> Projection.sortBy (fun n -> if n < 0 then nan else float n)

                Expect.sequenceEqual view.Keys [ 4; 2; 1; 3 ] "NaN last, in upstream order"

                source.Value <- setN 4 -1 source.Value
                Expect.sequenceEqual view.Keys [ 2; 1; 3; 4 ] "a key turned NaN moves last"

                source.Value <- setN 1 0 source.Value
                Expect.sequenceEqual view.Keys [ 1; 2; 3; 4 ] "a key leaving NaN takes its sorted slot"

                source.Value <- List.rev source.Value
                Expect.sequenceEqual view.Keys [ 1; 2; 4; 3 ] "NaN ties follow the upstream reorder"
            }

            test "None sorts before Some, across updates" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 2; 2, -1; 3, 1; 4, -1 ])

                let view =
                    rows source
                    |> Projection.sortBy (fun n -> if n < 0 then None else Some n)

                Expect.sequenceEqual view.Keys [ 2; 4; 3; 1 ] "None first, in upstream order"

                source.Value <- setN 1 -1 source.Value
                Expect.sequenceEqual view.Keys [ 1; 2; 4; 3 ] "a key turned None moves first"

                source.Value <- setN 2 0 source.Value
                Expect.sequenceEqual view.Keys [ 1; 4; 2; 3 ] "a key leaving None takes its sorted slot"
            }

            test "200 single edits at N=1000 make 200 sort-key calls" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..1000 -> i, i ])
                let calls = ref 0

                let view =
                    rows source
                    |> Projection.sortBy (fun n ->
                        calls.Value <- calls.Value + 1
                        -n)

                keysAndRows view |> ignore
                calls.Value <- 0

                for i in 1..200 do
                    source.Value <- setN i (i + 2000) source.Value

                Expect.equal calls.Value 200 "one sort-key call per edit"
                Expect.equal view.Keys[0] 200 "the last edit sorts first"
            }

            test "a Keys reader wakes only when the order changes" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 10; 2, 20; 3, 30 ])
                let view = rows source |> Projection.sortBy (fun n -> n / 10)
                let runs = ref 0

                createEffect (fun () ->
                    view.Keys |> ignore
                    runs.Value <- runs.Value + 1)

                source.Value <- setN 1 11 source.Value
                Expect.equal runs.Value 1 "an edit that keeps the sort key"

                source.Value <- setN 1 15 source.Value
                Expect.equal runs.Value 1 "an edit that keeps the order"

                source.Value <- setN 1 45 source.Value
                Expect.equal runs.Value 2 "an edit that moves a key"
                Expect.sequenceEqual view.Keys [ 2; 3; 1 ] "the new order"

                source.Value <- List.rev source.Value
                Expect.equal runs.Value 2 "an upstream reorder that keeps the sorted order"
            }

            testSequenced (
                testList
                    "tests reading the shared comparison counter"
                    [
                        test "a pass whose sort keys and upstream order are unchanged does not re-sort" {
                            use g = new Graph ()
                            use _ = g.Activate ()
                            let flight = createAsyncSource<Counted>()
                            let source = createSignal (items [ for i in 1..1000 -> i, i ])

                            let view =
                                rows source
                                |> Projection.sortBy (fun n -> if n < 0 then flight.Value else Counted n)

                            keysAndRows view |> ignore
                            comparisons.Value <- 0

                            source.Value <- setN 1 -1 source.Value
                            flight.Settle (Counted 1)

                            Expect.isLessThan comparisons.Value 3000 "O(N) comparisons over two passes, no sort"
                            Expect.equal view.Keys[0] 1 "key 1 keeps its slot"

                            comparisons.Value <- 0
                            source.Value <- setN 2 5000 source.Value
                            Expect.isGreaterThan comparisons.Value 0 "a changed sort key re-sorts"
                            Expect.equal (Array.last view.Keys) 2 "key 2 sorts last"
                        }

                        test "a sortByDescending pass whose sort keys and upstream order are unchanged does not re-sort" {
                            use g = new Graph ()
                            use _ = g.Activate ()
                            let flight = createAsyncSource<Counted>()
                            let source = createSignal (items [ for i in 1..1000 -> i, i ])

                            let view =
                                rows source
                                |> Projection.sortByDescending (fun n -> if n < 0 then flight.Value else Counted n)

                            keysAndRows view |> ignore
                            let keysRuns = ref 0

                            createEffect (fun () ->
                                keysRuns.Value <- keysRuns.Value + 1
                                view.Keys |> ignore)

                            comparisons.Value <- 0
                            source.Value <- setN 1 -1 source.Value
                            flight.Settle (Counted 1)

                            Expect.isLessThan comparisons.Value 3000 "O(N) comparisons over two passes, no sort"
                            Expect.equal (Array.last view.Keys) 1 "key 1 keeps its slot"
                            Expect.equal keysRuns.Value 1 "no Keys write"

                            comparisons.Value <- 0
                            source.Value <- setN 2 5000 source.Value
                            Expect.isGreaterThan comparisons.Value 0 "a changed sort key re-sorts"
                            Expect.equal view.Keys[0] 2 "key 2 sorts first"
                        }

                        test "removing a non-extreme row costs no comparison; removing the extreme re-folds once" {
                            use g = new Graph ()
                            use _ = g.Activate ()
                            let source = createSignal (items [ for i in 1..1000 -> i, i ])
                            let least = rows source |> Projection.tryMinBy Counted

                            createEffect (fun () -> least.Value |> ignore)
                            comparisons.Value <- 0
                            source.Value <- source.Value |> List.filter (fun x -> x.Id <> 500)
                            Expect.equal comparisons.Value 0 "a non-extreme removal subtracts in O(1)"
                            Expect.equal least.Value (Some 1) "key 1 holds"

                            source.Value <- source.Value |> List.filter (fun x -> x.Id <> 1)
                            Expect.equal comparisons.Value 997 "one re-fold over the 998 cached values"
                            Expect.equal least.Value (Some 2) "key 2 holds"

                            comparisons.Value <- 0
                            source.Value <- setN 10 5000 source.Value
                            Expect.isLessThan comparisons.Value 10 "an edit of a non-holder costs O(1) comparisons"
                        }
                    ]
            )

            test "an effect reading Keys and Get runs exactly once per write" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..5 -> i, 10 * i ])
                let up = rows source
                let view = up |> Projection.sortBy (fun n -> -(n / 10))
                let runs = keysAndRows view

                // An edit in place that keeps the sort key runs no view pass.
                let passes viewPasses name next =
                    let upRuns, viewRuns = up.Runs, view.Runs
                    once runs source name next
                    Expect.equal up.Runs (upRuns + 1) $"{name}: one upstream pass"
                    Expect.equal view.Runs (viewRuns + viewPasses) $"{name}: view passes"

                passes 0 "edit a value in place" (setN 2 21)
                passes 1 "move a key" (setN 4 0)
                passes 1 "remove a key" (List.filter (fun x -> x.Id <> 2))
                passes 1 "add a key" (fun xs -> xs @ items [ 6, 30 ])
                passes 1 "reorder" List.rev
                Expect.sequenceEqual view.Keys [ 5; 6; 3; 1; 4 ] "the final order, the tie in upstream order"
            }

            test "an unread view leaves an unread upstream idle" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 2; 2, 3 ])
                let up = rows source
                let view = up |> Projection.sortBy id

                for i in 1..10 do
                    source.Value <- setN 1 (2 * i) source.Value

                Expect.equal up.Runs 0 "no upstream pass"
                Expect.equal view.Runs 0 "no view pass"
            }

            test "a key removed and re-added while unread sorts by the new upstream row" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 3 ])
                let view = rows source |> Projection.sortBy id

                Expect.sequenceEqual view.Keys [ 1; 2; 3 ] "initial order"
                source.Value <- source.Value |> List.filter (fun x -> x.Id <> 2)
                source.Value <- source.Value @ items [ 2, 0 ]
                Expect.sequenceEqual view.Keys [ 2; 1; 3 ] "key 2 re-added with a lower sort key"

                source.Value <- setN 2 9 source.Value
                Expect.sequenceEqual view.Keys [ 1; 3; 2 ] "key 2 follows its new row"
            }

            test "a pending sort key keeps its last settled sort key; a never-settled key is only in PendingKeys" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flights = dict [ for n in [ 10; 11; 20; 30 ] -> n, createAsyncSource<int>() ]
                flights[10].Settle 3
                flights[20].Settle 1
                let source = createSignal (items [ 1, 10; 2, 20; 3, 30 ])

                let view =
                    rows source
                    |> Projection.sortBy (fun n -> flights[n].Value)

                Expect.sequenceEqual view.Keys [ 2; 1 ] "key 3 has never settled"
                Expect.sequenceEqual view.PendingKeys [ 3 ] "key 3 is pending"

                source.Value <- setN 1 11 source.Value
                Expect.sequenceEqual view.Keys [ 2; 1 ] "key 1 keeps its last settled sort key"

                flights[30].Settle 2
                Expect.sequenceEqual view.Keys [ 2; 3; 1 ] "key 3 joins once settled"
                Expect.isFalse (Array.contains 3 view.PendingKeys) "key 3 is no longer pending"

                flights[11].Settle 0
                Expect.sequenceEqual view.Keys [ 1; 2; 3 ] "key 1 moves once its sort key settles"
            }

            test "a PendingKeys reader wakes when a never-settled key settles" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flight = createAsyncSource<int>()
                let source = createSignal (items [ 1, 1 ])

                let view =
                    rows source
                    |> Projection.sortBy (fun _ -> flight.Value)

                let seen = ResizeArray<int[]>()

                createEffect (fun () -> seen.Add view.PendingKeys)
                flight.Settle 0

                Expect.sequenceEqual seen [ [| 1 |]; [||] ] "pending, then settled"
                Expect.sequenceEqual view.Keys [ 1 ] "the key joined"
            }

            test "a throwing sort key excludes the key, and Get and TryGet raise its error" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 2; 2, 4 ])

                let view =
                    rows source
                    |> Projection.sortBy (fun n -> if n = 13 then invalidOp "boom 13" else -n)

                let keysRuns = ref 0
                let getRuns = ref 0

                createEffect (fun () ->
                    view.Keys |> ignore
                    keysRuns.Value <- keysRuns.Value + 1)

                createEffect (fun () ->
                    getRuns.Value <- getRuns.Value + 1

                    try
                        view.Get 2 |> ignore
                    with _ ->
                        ())

                source.Value <- setN 2 13 source.Value

                Expect.sequenceEqual view.Keys [ 1 ] "key 2 is excluded"
                Expect.equal keysRuns.Value 2 "the Keys reader woke"
                Expect.equal getRuns.Value 2 "the Get reader woke"

                Expect.throwsC (fun () -> view.Get 2 |> ignore) (fun ex -> Expect.equal ex.Message "boom 13" "Get raises the sort key's error")

                Expect.throwsC (fun () -> view.TryGet 2 |> ignore) (fun ex -> Expect.equal ex.Message "boom 13" "TryGet raises the sort key's error")

                Expect.equal view.Status Status.None "the pass itself succeeded"

                source.Value <- setN 2 4 source.Value
                Expect.sequenceEqual view.Keys [ 2; 1 ] "key 2 returns once the sort key succeeds"
                Expect.equal (view.Get 2) 4 "and reads its value"
            }

            test "a pass after a failed comparison re-sorts" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..20 -> i, 21 - i ])
                let view = rows source |> Projection.sortBy Boom
                let before = source.Value

                Expect.sequenceEqual view.Keys [ 20..-1..1 ] "initial order"

                source.Value <- before |> setN 1 100 |> setN 12 666
                Expect.throws (fun () -> view.Keys |> ignore) "the comparison raises"

                source.Value <- before
                Expect.sequenceEqual view.Keys [ 20..-1..1 ] "the restored rows sort as before"
            }

            test "Get outside Keys raises KeyNotFoundException" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 2 ])
                let view = rows source |> Projection.sortBy id

                Expect.throwsT<Collections.Generic.KeyNotFoundException> (fun () -> view.Get 2 |> ignore) "an absent key"

                Expect.equal (view.TryGet 2) None "TryGet of an absent key"
            }

            test "removing a key disposes the key's memo" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 10; 2, 20; 3, 30 ])
                let offset = createSignal 0
                let calls = ResizeArray<int>()

                let view =
                    rows source
                    |> Projection.sortBy (fun n ->
                        calls.Add n
                        n + offset.Value)

                keysAndRows view |> ignore
                source.Value <- source.Value |> List.filter (fun x -> x.Id <> 2)
                calls.Clear ()
                offset.Value <- 1

                Expect.sequenceEqual (Seq.sort calls) [ 10; 30 ] "the removed key's sort key never runs again"
            }

            test "disposing the view detaches it from the upstream" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 2; 2, 4 ])
                let calls = ref 0

                let view =
                    rows source
                    |> Projection.sortBy (fun n ->
                        calls.Value <- calls.Value + 1
                        n)

                keysAndRows view |> ignore
                view.Dispose ()
                calls.Value <- 0
                source.Value <- setN 1 6 source.Value

                Expect.equal calls.Value 0 "no sort-key runs after disposal"
                Expect.isEmpty view.Keys "a disposed view is empty"
            }

            test "sortByDescending sorts largest first, and equal sort keys keep upstream order" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 2; 2, 3; 3, 2; 4, 1 ])
                let view = rows source |> Projection.sortByDescending id

                Expect.sequenceEqual view.Keys [ 2; 1; 3; 4 ] "descending, ties in upstream order"
                source.Value <- List.rev source.Value
                Expect.sequenceEqual view.Keys [ 2; 3; 1; 4 ] "the tie follows the upstream reorder"
            }

            test "sortByDescending orders NaN last, across updates" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, -1; 2, 3; 3, -1; 4, 1 ])

                let view =
                    rows source
                    |> Projection.sortByDescending (fun n -> if n < 0 then nan else float n)

                Expect.sequenceEqual view.Keys [ 2; 4; 1; 3 ] "NaN last, in upstream order"
                source.Value <- setN 4 -1 source.Value
                Expect.sequenceEqual view.Keys [ 2; 1; 3; 4 ] "a key turned NaN moves last"
                source.Value <- setN 1 5 source.Value
                Expect.sequenceEqual view.Keys [ 1; 2; 3; 4 ] "a key leaving NaN takes its sorted slot"
                source.Value <- List.rev source.Value
                Expect.sequenceEqual view.Keys [ 1; 2; 4; 3 ] "NaN ties follow the upstream reorder"
            }

            test "sortByDescending orders None after Some" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 2; 2, -1; 3, 1; 4, -1 ])

                let view =
                    rows source
                    |> Projection.sortByDescending (fun n -> if n < 0 then None else Some n)

                Expect.sequenceEqual view.Keys [ 1; 3; 2; 4 ] "None last, in upstream order"
                source.Value <- setN 1 -1 source.Value
                Expect.sequenceEqual view.Keys [ 3; 1; 2; 4 ] "a key turned None moves among the Nones in upstream order"
            }

            test "disposing a sortByDescending view detaches it from the upstream" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 2; 2, 4 ])
                let calls = ref 0

                let view =
                    rows source
                    |> Projection.sortByDescending (fun n ->
                        calls.Value <- calls.Value + 1
                        n)

                keysAndRows view |> ignore
                view.Dispose ()
                calls.Value <- 0
                source.Value <- setN 1 6 source.Value

                Expect.equal calls.Value 0 "no sort-key runs after disposal"
                Expect.isEmpty view.Keys "a disposed view is empty"
            }
        ]

[<Tests>]
let sliceTests =
    testList
        "Projection.take, skip and sub"
        [
            test "take, skip and sub select positions and clamp counts as List.truncate and a clamped skip do" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..5 -> i, 10 * i ])
                let up = rows source

                let keysOf (view: Projection<int, int>) =
                    List.ofArray view.Keys

                Expect.equal (keysOf (up |> Projection.take (fun () -> 2))) [ 1; 2 ] "take 2"
                Expect.equal (keysOf (up |> Projection.take (fun () -> 9))) [ 1; 2; 3; 4; 5 ] "take past the end"
                Expect.equal (keysOf (up |> Projection.take (fun () -> -1))) [] "take a negative count"
                Expect.equal (keysOf (up |> Projection.skip (fun () -> 3))) [ 4; 5 ] "skip 3"
                Expect.equal (keysOf (up |> Projection.skip (fun () -> 9))) [] "skip past the end"
                Expect.equal (keysOf (up |> Projection.skip (fun () -> -2))) [ 1; 2; 3; 4; 5 ] "skip a negative count"
                Expect.equal (keysOf (up |> Projection.sub (fun () -> 1) (fun () -> 3))) [ 2; 3; 4 ] "sub 1 3"
                Expect.equal (keysOf (up |> Projection.sub (fun () -> 4) (fun () -> 3))) [ 5 ] "sub past the end"
                Expect.equal (keysOf (up |> Projection.sub (fun () -> -1) (fun () -> 2))) [ 1; 2 ] "sub at a negative offset"
                Expect.equal (keysOf (up |> Projection.sub (fun () -> 2) (fun () -> -1))) [] "sub of a negative count"

                Expect.equal
                    (keysOf (
                        up
                        |> Projection.sub (fun () -> 1) (fun () -> Int32.MaxValue)
                    ))
                    [ 2; 3; 4; 5 ]
                    "sub of Int32.MaxValue"

                Expect.equal ((up |> Projection.sub (fun () -> 1) (fun () -> 3)).Get 3) 30 "a row reads the upstream value"

                Expect.throwsT<Collections.Generic.KeyNotFoundException>
                    (fun () ->
                        (up |> Projection.take (fun () -> 2)).Get 3
                        |> ignore)
                    "Get outside the window"
            }

            test "the window follows signals read by offset and count, and a reader runs once per change" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..6 -> i, i ])
                let offset = createSignal 0
                let size = createSignal 2

                let view =
                    rows source
                    |> Projection.sub (fun () -> offset.Value) (fun () -> size.Value)

                let runs = keysAndRows view

                size.Value <- 3
                Expect.equal runs.Value 2 "a larger count"
                Expect.sequenceEqual view.Keys [ 1; 2; 3 ] "three keys"

                offset.Value <- 2
                Expect.equal runs.Value 3 "a shifted offset"
                Expect.sequenceEqual view.Keys [ 3; 4; 5 ] "the shifted window"

                size.Value <- 3
                Expect.equal runs.Value 3 "an unchanged count"
            }

            test "a key that stays in a shifted window keeps its row" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..6 -> i, i ])
                let offset = createSignal 0

                let view =
                    rows source
                    |> Projection.sub (fun () -> offset.Value) (fun () -> 3)

                let wakes = Array.zeroCreate<int> 7

                for key in 1..3 do
                    createEffect (fun () ->
                        wakes[key] <- wakes[key] + 1

                        try
                            view.Get key |> ignore
                        with _ ->
                            ())

                offset.Value <- 1
                Expect.sequenceEqual view.Keys [ 2; 3; 4 ] "the shifted window"
                Expect.equal wakes[1] 2 "the reader of the key that left wakes"
                Expect.equal wakes[2] 1 "the reader of a surviving key stays asleep"
                Expect.equal wakes[3] 1 "the reader of a surviving key stays asleep"
            }

            test "membership and order changes upstream move keys through the window" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..5 -> i, i ])
                let view = rows source |> Projection.take (fun () -> 3)
                let keysRuns = ref 0

                createEffect (fun () ->
                    view.Keys |> ignore
                    keysRuns.Value <- keysRuns.Value + 1)

                source.Value <- setN 2 20 source.Value
                Expect.equal keysRuns.Value 1 "a value edit leaves a Keys reader asleep"
                Expect.equal (view.Get 2) 20 "and reaches the row"

                source.Value <- source.Value |> List.filter (fun x -> x.Id <> 2)
                Expect.sequenceEqual view.Keys [ 1; 3; 4 ] "the next key slides in"

                source.Value <- items [ 0, 0 ] @ source.Value
                Expect.sequenceEqual view.Keys [ 0; 1; 3 ] "a key added in front pushes the last out"

                source.Value <- List.rev source.Value
                Expect.sequenceEqual view.Keys [ 5; 4; 3 ] "upstream order"

                source.Value <- source.Value |> List.filter (fun x -> x.Id = 5)
                Expect.sequenceEqual view.Keys [ 5 ] "fewer keys than the count"
                Expect.equal keysRuns.Value 5 "one Keys run per membership change"
            }

            test "a pending row keeps its key, and a key held out upstream is in PendingKeys" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flights = dict [ for k in 1..3 -> k, createAsyncSource<int>() ]
                flights[1].Settle 1
                let up = createProjection id (fun k -> flights[k].Value) (fun () -> [ 1; 2; 3 ])
                let view = up |> Projection.take (fun () -> 2)
                keysAndRows view |> ignore

                Expect.sequenceEqual view.Keys [ 1; 2 ] "a pending row keeps its position"
                Expect.isTrue view.AnyPending "key 2's row is pending"
                Expect.sequenceEqual view.PendingKeys [ 2 ] "key 3 is outside the window"

                flights[2].Settle 2
                Expect.isFalse view.AnyPending "settled"
                Expect.equal (view.Get 2) 2 "the settled value"

                let flight = createAsyncSource<bool>()

                let filtered =
                    createProjection id id (fun () -> [ 1; 2; 3 ])
                    |> Projection.filter (fun n -> n <> 2 || flight.Value)

                let sliced = filtered |> Projection.take (fun () -> 5)

                Expect.sequenceEqual sliced.Keys [ 1; 3 ] "key 2 is held out upstream"
                Expect.sequenceEqual sliced.PendingKeys [ 2 ] "and pending in the slice"
                Expect.isFalse sliced.AnyPending "AnyPending counts rows of keys in Keys"

                flight.Settle true
                Expect.sequenceEqual sliced.Keys [ 1; 2; 3 ] "key 2 joins once settled"
                Expect.isEmpty sliced.PendingKeys "and leaves PendingKeys"
            }

            test "a throwing count fails the pass, and a failed upstream row stays in the window" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 3 ])
                let size = createSignal 2

                let up =
                    rows source
                    |> Projection.map (fun n -> if n = 13 then invalidOp "boom 13" else n)

                let view =
                    up
                    |> Projection.take (fun () -> if size.Value < 0 then invalidOp "bad count" else size.Value)

                keysAndRows view |> ignore
                size.Value <- -1
                Expect.equal view.Status Status.Error "the pass failed"

                Expect.throwsC (fun () -> view.Keys |> ignore) (fun ex -> Expect.equal ex.Message "bad count" "Keys raises the count's error")

                size.Value <- 2
                Expect.sequenceEqual view.Keys [ 1; 2 ] "the recovered window"
                Expect.equal view.Status Status.None "the pass recovered"

                source.Value <- setN 2 13 source.Value
                Expect.sequenceEqual view.Keys [ 1; 2 ] "the failed row keeps its key"

                Expect.throwsC (fun () -> view.Get 2 |> ignore) (fun ex -> Expect.equal ex.Message "boom 13" "Get raises the upstream error")
            }

            test "disposing the view detaches it from the upstream and its count" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 3 ])
                let size = createSignal 2
                let calls = ref 0

                let view =
                    rows source
                    |> Projection.take (fun () ->
                        calls.Value <- calls.Value + 1
                        size.Value)

                keysAndRows view |> ignore
                view.Dispose ()
                calls.Value <- 0
                size.Value <- 3
                source.Value <- List.rev source.Value

                Expect.equal calls.Value 0 "count never runs after disposal"
                Expect.isEmpty view.Keys "a disposed view is empty"
            }

            test "a write outside the window wakes no reader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..6 -> i, i ])

                let view =
                    rows source
                    |> Projection.sub (fun () -> 1) (fun () -> 2)

                let runs = keysAndRows view

                source.Value <- setN 5 50 source.Value
                source.Value <- source.Value @ items [ 7, 7 ]
                Expect.equal runs.Value 1 "an edit and an append past the window"
                Expect.sequenceEqual view.Keys [ 2; 3 ] "the window is unchanged"

                source.Value <- setN 3 30 source.Value
                Expect.equal runs.Value 2 "an edit inside the window"
                Expect.equal (view.Get 3) 30 "the edited value"
            }

            test "take composes with filter and sortBy" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..8 -> i, (i * 5) % 8 ])
                let size = createSignal 3

                let view =
                    rows source
                    |> Projection.filter (fun n -> n % 2 = 1)
                    |> Projection.sortBy id
                    |> Projection.take (fun () -> size.Value)

                Expect.sequenceEqual [ for k in view.Keys -> view.Get k ] [ 1; 3; 5 ] "the three smallest odd values"

                size.Value <- 1
                Expect.sequenceEqual [ for k in view.Keys -> view.Get k ] [ 1 ] "the smallest odd value"

                let over =
                    rows source
                    |> Projection.take (fun () -> 4)
                    |> Projection.filter (fun n -> n % 2 = 0)

                Expect.sequenceEqual [ for k in over.Keys -> over.Get k ] [ 2; 4 ] "even values among the first four keys"
            }
        ]

[<Tests>]
let chainTests =
    let chains: (string * (Projection<int, int> -> Projection<int, int>)) list =
        [
            "filter then map",
            Projection.filter (fun n -> n % 2 = 0)
            >> Projection.map (fun n -> n * 3)
            "map then map",
            Projection.map (fun n -> n + 1)
            >> Projection.map (fun n -> n * 3)
            "map three times",
            Projection.map (fun n -> n + 1)
            >> Projection.map (fun n -> n * 3)
            >> Projection.map id
            "sortBy", Projection.sortBy (fun n -> n / 4)
            "filter, sortBy, then map",
            Projection.filter (fun n -> n % 2 = 0)
            >> Projection.sortBy (fun n -> n / 4)
            >> Projection.map (fun n -> n * 3)
            "sortBy then filter",
            Projection.sortBy (fun n -> n / 4)
            >> Projection.filter (fun n -> n % 2 = 0)
            "filter then mapWith",
            Projection.filter (fun n -> n % 2 = 0)
            >> Projection.mapWith (fun _ v -> fun () -> v () + 1)
            "choose then sortBy",
            Projection.choose (fun n -> if n % 2 = 0 then Some (n + 1) else None)
            >> Projection.sortBy (fun n -> n / 4)
            "sortBy then take",
            Projection.sortBy (fun n -> n / 4)
            >> Projection.take (fun () -> 10)
            "sortByDescending then take",
            Projection.sortByDescending (fun n -> n / 4)
            >> Projection.take (fun () -> 10)
            "distinctBy then sortBy",
            Projection.distinctBy (fun n -> n % 5)
            >> Projection.sortBy (fun n -> -n)
            "sub then map",
            Projection.sub (fun () -> 0) (fun () -> 10)
            >> Projection.map (fun n -> n * 3)
        ]

    testList
        "chained views"
        [
            for name, chain in chains ->
                test $"an effect reading Keys and Get of {name} runs exactly once per write" {
                    use g = new Graph ()
                    use _ = g.Activate ()
                    let source = createSignal (items [ for i in 1..6 -> i, 2 * i ])
                    let view = rows source |> chain
                    let runs = keysAndRows view

                    once runs source "remove the first key" (List.filter (fun x -> x.Id <> 1))
                    once runs source "remove a middle key" (List.filter (fun x -> x.Id <> 4))
                    once runs source "edit a value" (setN 2 8)
                    once runs source "flip a key's parity" (setN 3 7)
                    once runs source "add a key" (fun xs -> xs @ items [ 7, 14 ])
                    once runs source "reorder" List.rev
                }
        ]

[<Tests>]
let mapWithTests =
    testList
        "Projection.mapWith"
        [
            test "mapping runs once per key and its reader re-runs on an edit" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 3 ])
                let factories = ref 0
                let readers = ref 0

                let view =
                    rows source
                    |> Projection.mapWith (fun key value ->
                        factories.Value <- factories.Value + 1

                        fun () ->
                            readers.Value <- readers.Value + 1
                            key * 100 + value ())

                keysAndRows view |> ignore
                Expect.equal factories.Value 3 "one factory call per key"
                readers.Value <- 0
                source.Value <- setN 2 5 source.Value

                Expect.equal factories.Value 3 "an edit runs no factory"
                Expect.equal readers.Value 1 "an edit re-runs one reader"
                Expect.equal (view.Get 2) 205 "the new value"

                source.Value <- List.rev source.Value
                Expect.sequenceEqual view.Keys [ 3; 2; 1 ] "upstream order"
            }

            test "a node the mapping creates is disposed with its key" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let disposed = ResizeArray<int>()

                let view =
                    rows source
                    |> Projection.mapWith (fun key value ->
                        let doubled = createMemo (fun _ -> value () * 2)
                        onCleanup (fun () -> disposed.Add key)
                        fun () -> doubled.Value)

                keysAndRows view |> ignore
                Expect.equal (view.Get 2) 4 "the memo the mapping created"

                source.Value <- List.filter (fun x -> x.Id <> 2) source.Value
                Expect.sequenceEqual disposed [ 2 ] "the removed key's scope"
            }

            test "an effect reading Keys and Get runs exactly once per write" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..4 -> i, i ])

                let view =
                    rows source
                    |> Projection.mapWith (fun _ value -> fun () -> value () + 1)

                let runs = keysAndRows view

                once runs source "edit a value" (setN 2 9)
                once runs source "remove a key" (List.filter (fun x -> x.Id <> 3))
                once runs source "add a key" (fun xs -> xs @ items [ 5, 5 ])
                once runs source "reorder" List.rev
            }
        ]

/// <summary>The keys of each group of <c>view</c>, in group order.</summary>
let private groupsOf (view: Projection<'G, Projection<int, int>>) =
    [ for key in view.Keys -> key, List.ofArray (view.Get key).Keys ]

[<Tests>]
let groupByTests =
    testList
        "Projection.groupBy"
        [
            test "groups follow each group's first member, and members keep upstream order" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 3; 2, 2; 3, 5; 4, 4; 5, 1 ])
                let view = rows source |> Projection.groupBy (fun n -> n % 2)

                Expect.equal (groupsOf view) [ 1, [ 1; 3; 5 ]; 0, [ 2; 4 ] ] "odd first"
                Expect.equal ((view.Get 0).Get 4) 4 "an inner view reads the upstream value"

                source.Value <- List.rev source.Value
                Expect.equal (groupsOf view) [ 1, [ 5; 3; 1 ]; 0, [ 4; 2 ] ] "after a reorder"

                source.Value <- setN 5 2 source.Value
                Expect.equal (groupsOf view) [ 0, [ 5; 4; 2 ]; 1, [ 3; 1 ] ] "a group's first member moved"
            }

            test "moving a key between groups publishes the outer Keys once and both inner views are current" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 3; 4, 4 ])
                let view = rows source |> Projection.groupBy (fun n -> n % 2)
                let seen = ResizeArray ()
                let outer = ref 0

                createEffect (fun () ->
                    view.Keys |> ignore
                    outer.Value <- outer.Value + 1)

                createEffect (fun () -> seen.Add (groupsOf view))
                seen.Clear ()
                source.Value <- setN 3 6 source.Value

                Expect.equal outer.Value 1 "the group order is unchanged"
                Expect.equal (List.ofSeq seen) [ [ 1, [ 1 ]; 0, [ 2; 3; 4 ] ] ] "one run, both groups current"

                source.Value <- setN 1 8 source.Value
                Expect.equal outer.Value 2 "the emptied group leaves the outer Keys"
                Expect.equal (List.last (List.ofSeq seen)) [ 0, [ 1; 2; 3; 4 ] ] "one group"
            }

            test "an edit that keeps a key's group wakes no Keys reader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let view = rows source |> Projection.groupBy (fun n -> n % 2)
                let inner = view.Get 0
                let runs = ref 0

                createEffect (fun () ->
                    view.Keys |> ignore
                    inner.Keys |> ignore
                    runs.Value <- runs.Value + 1)

                source.Value <- setN 2 4 source.Value
                Expect.equal runs.Value 1 "no Keys changed"
                Expect.equal (inner.Get 2) 4 "the inner row is current"
            }

            test "a held inner view of an emptied group is disposed" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let view = rows source |> Projection.groupBy (fun n -> n % 2)
                let odd = view.Get 1
                let keys = ResizeArray ()
                createEffect (fun () -> keys.Add (List.ofArray odd.Keys))

                source.Value <- setN 1 4 source.Value

                Expect.equal (List.ofSeq keys) [ [ 1 ]; [] ] "a Keys reader wakes to the empty key set"
                Expect.isEmpty odd.Keys "empty Keys"
                Expect.isFalse odd.AnyPending "nothing pending"
                Expect.throwsT<ObjectDisposedException> (fun () -> odd.Get 1 |> ignore) "Get raises"
                Expect.isNone (odd.TryGet 1) "TryGet reports absent"

                source.Value <- setN 1 5 source.Value
                Expect.isFalse (obj.ReferenceEquals (view.Get 1, odd)) "a group that returns has a new inner view"
                Expect.sequenceEqual (view.Get 1).Keys [ 1 ] "with its members"
            }

            test "a reader of an inner view alone follows upstream edits and runs once per write" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 3; 4, 4 ])
                let view = rows source |> Projection.groupBy (fun n -> n % 2)
                let even = view.Get 0
                let runs = keysAndRows even

                once runs source "remove a member" (List.filter (fun x -> x.Id <> 2))
                Expect.sequenceEqual even.Keys [ 4 ] "the removed member left"
                once runs source "move a key in" (setN 1 6)
                Expect.sequenceEqual even.Keys [ 1; 4 ] "the moved key joined"
                once runs source "edit a member" (setN 4 8)
                Expect.equal (even.Get 4) 8 "the edited row"
            }

            test "an effect reading every group and row runs exactly once per write" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..6 -> i, i ])
                let view = rows source |> Projection.groupBy (fun n -> n % 3)
                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1

                    for key in view.Keys do
                        let inner = view.Get key

                        for member' in inner.Keys do
                            inner.Get member' |> ignore)

                once runs source "edit a value within its group" (setN 2 5)
                once runs source "move a key" (setN 1 3)
                once runs source "empty a group" (List.filter (fun x -> x.N % 3 <> 2))
                once runs source "add a key" (fun xs -> xs @ items [ 7, 7 ])
                once runs source "reorder" List.rev
            }

            test "a pending group key keeps the key's group; a never-settled key is in no group" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flights = dict [ for n in [ 10; 11; 30 ] -> n, createAsyncSource<int>() ]
                flights[10].Settle 1
                let source = createSignal (items [ 1, 10; 2, 30 ])

                let view =
                    rows source
                    |> Projection.groupBy (fun n -> flights[n].Value)

                Expect.equal (groupsOf view) [ 1, [ 1 ] ] "key 2 has never settled"

                source.Value <- setN 1 11 source.Value
                Expect.equal (groupsOf view) [ 1, [ 1 ] ] "key 1 keeps its group while pending"

                flights[30].Settle 1
                flights[11].Settle 2
                Expect.equal (groupsOf view) [ 2, [ 1 ]; 1, [ 2 ] ] "both settled, key 1's group first"
            }

            test "a throwing group key leaves the key in no group" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])

                let view =
                    rows source
                    |> Projection.groupBy (fun n -> if n < 0 then failwith "boom" else n % 2)

                Expect.equal (groupsOf view) [ 1, [ 1 ]; 0, [ 2 ] ] "precondition"
                source.Value <- setN 1 -1 source.Value
                Expect.equal (groupsOf view) [ 0, [ 2 ] ] "key 1 is in no group"
                Expect.equal view.Status Status.None "the pass succeeds"
            }

            test "disposing the view disposes its inner views" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let view = rows source |> Projection.groupBy (fun n -> n % 2)
                let odd = view.Get 1

                view.Dispose ()
                Expect.isEmpty view.Keys "the outer view is empty"
                Expect.isEmpty odd.Keys "the inner view is empty"
                Expect.throwsT<ObjectDisposedException> (fun () -> odd.Get 1 |> ignore) "and raises on Get"
            }

            test "a never-settled key is in UngroupedKeys, in upstream order, until it settles and joins its group" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flights = dict [ for n in [ 10; 30; 31 ] -> n, createAsyncSource<int>() ]
                flights[10].Settle 1
                let source = createSignal (items [ 1, 10; 2, 30; 3, 31 ])

                let view: Grouping<int, int, int> =
                    rows source
                    |> Projection.groupBy (fun n -> flights[n].Value)

                Expect.sequenceEqual view.UngroupedKeys [ 2; 3 ] "keys 2 and 3 have never settled"
                source.Value <- List.rev source.Value
                Expect.sequenceEqual view.UngroupedKeys [ 3; 2 ] "after a reorder"

                flights[30].Settle 0
                Expect.sequenceEqual view.UngroupedKeys [ 3 ] "key 2 left"
                Expect.equal (groupsOf view) [ 0, [ 2 ]; 1, [ 1 ] ] "and joined its group"

                flights[31].Settle 1
                Expect.isEmpty view.UngroupedKeys "every key is grouped"
                Expect.equal (groupsOf view) [ 1, [ 3; 1 ]; 0, [ 2 ] ] "key 3 joined its group"
            }

            test "a raising key is in UngroupedKeys and GroupOf raises its error until it recovers" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])

                let view =
                    rows source
                    |> Projection.groupBy (fun n -> if n < 0 then raise (TimeoutException "boom") else n % 2)

                Expect.isEmpty view.UngroupedKeys "precondition"
                source.Value <- setN 1 -1 source.Value

                Expect.sequenceEqual view.UngroupedKeys [ 1 ] "the raising key is ungrouped"
                Expect.throwsT<TimeoutException> (fun () -> view.GroupOf 1 |> ignore) "GroupOf raises the key's error, not its last group"
                Expect.equal view.Status Status.None "the pass succeeds"

                source.Value <- setN 1 3 source.Value
                Expect.isEmpty view.UngroupedKeys "the recovered key left"
                Expect.equal (view.GroupOf 1) 1 "GroupOf returns its group"
                Expect.equal (groupsOf view) [ 1, [ 1 ]; 0, [ 2 ] ] "and it is in its group"
            }

            test "a reader of UngroupedKeys runs once per settle" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flights = dict [ for n in [ 30; 31 ] -> n, createAsyncSource<int>() ]
                let source = createSignal (items [ 1, 1; 2, 30; 3, 31 ])

                let view =
                    rows source
                    |> Projection.groupBy (fun n -> if n >= 30 then flights[n].Value else n % 2)

                let seen = ResizeArray<int list>()
                createEffect (fun () -> seen.Add (List.ofArray view.UngroupedKeys))

                Expect.equal (List.ofSeq seen) [ [ 2; 3 ] ] "precondition"
                flights[30].Settle 0
                Expect.equal (List.ofSeq seen) [ [ 2; 3 ]; [ 3 ] ] "one run for the first settle"
                flights[31].Settle 1
                Expect.equal (List.ofSeq seen) [ [ 2; 3 ]; [ 3 ]; [] ] "one run for the second settle"
            }

            test "a reader of UngroupedKeys wakes on a settle after a pass that leaves it unchanged" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flight = createAsyncSource<int>()
                let source = createSignal (items [ 1, 1; 2, 30 ])

                let view =
                    rows source
                    |> Projection.groupBy (fun n -> if n >= 30 then flight.Value else n % 2)

                let seen = ResizeArray<int list>()
                createEffect (fun () -> seen.Add (List.ofArray view.UngroupedKeys))

                source.Value <- setN 1 2 source.Value
                Expect.equal (List.ofSeq seen) [ [ 2 ] ] "the move leaves the list unchanged"
                flight.Settle 0
                Expect.equal (List.ofSeq seen) [ [ 2 ]; [] ] "the settle wakes the reader"
            }

            test "filter then groupBy lists the keys the filter holds out in UngroupedKeys" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flights = dict [ for k in 1..3 -> k, createAsyncSource<int>() ]
                let gate = createAsyncSource<int>()
                flights[1].Settle 2
                flights[3].Settle 7
                let rows = createProjection id (fun k -> flights[k].Value) (fun () -> [ 1; 2; 3 ])

                let view =
                    rows
                    |> Projection.filter (fun n -> n > 0)
                    |> Projection.groupBy (fun n -> if n = 7 then gate.Value else n % 2)

                Expect.sequenceEqual view.UngroupedKeys [ 3; 2 ] "the never-settled key, then the key the filter holds out"

                Expect.throwsT<Collections.Generic.KeyNotFoundException>
                    (fun () -> view.GroupOf 2 |> ignore)
                    "a key the filter holds out is absent, as in the filter's Get"

                Expect.equal (groupsOf view) [ 0, [ 1 ] ] "the settled key only"

                gate.Settle 1
                Expect.sequenceEqual view.UngroupedKeys [ 2 ] "the key the filter holds out"

                flights[2].Settle 5
                Expect.isEmpty view.UngroupedKeys "the held-out key settled"
                Expect.equal (groupsOf view) [ 0, [ 1 ]; 1, [ 2; 3 ] ] "and joined its group"
            }

            test "GroupOf returns a grouped key's group, and a pending key's last settled group" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flights = dict [ for n in [ 10; 11; 12 ] -> n, createAsyncSource<int>() ]
                flights[10].Settle 1
                let source = createSignal (items [ 1, 10; 2, 2; 3, 12 ])

                let view =
                    rows source
                    |> Projection.groupBy (fun n -> if n >= 10 then flights[n].Value else n % 2)

                Expect.equal (view.GroupOf 1) 1 "key 1's group"
                Expect.equal (view.GroupOf 2) 0 "key 2's group"
                Expect.throwsT<NotReadyException> (fun () -> view.GroupOf 3 |> ignore) "key 3 has never settled"

                source.Value <- setN 1 11 source.Value
                Expect.equal (view.GroupOf 1) 1 "a pending key keeps its last group"
                Expect.sequenceEqual view.UngroupedKeys [ 3 ] "and is not ungrouped"

                flights[11].Settle 2
                Expect.equal (view.GroupOf 1) 2 "the settled group"
            }
        ]

/// <summary>
/// A projection of keys 1 and 2 whose rows await one flight each, with key 1 settled to 2 and key 2 in flight.
/// </summary>
let private halfSettled () =
    let flights = dict [ 1, createAsyncSource<int>(); 2, createAsyncSource<int>() ]
    flights[1].Settle 2
    let rows = createProjection id (fun k -> flights[k].Value) (fun () -> [ 1; 2 ])
    flights, rows

[<Tests>]
let chainPendingTests =
    let chains: (string * (Projection<int, int> -> Projection<int, int>) * (Projection<int, int> -> Projection<int, int>)) list =
        [
            "filter then map", Projection.filter (fun n -> n > 0), Projection.map (fun n -> n * 10)
            "sortBy then map", Projection.sortBy (fun n -> -n), Projection.map (fun n -> n * 10)
            "filter then filter", Projection.filter (fun n -> n > 0), Projection.filter (fun n -> n < 100)
            "filter then sortBy", Projection.filter (fun n -> n > 0), Projection.sortBy (fun n -> -n)
            "filter, map, then map",
            Projection.filter (fun n -> n > 0),
            Projection.map (fun n -> n * 10)
            >> Projection.map id
            "filter then mapWith", Projection.filter (fun n -> n > 0), Projection.mapWith (fun _ v -> fun () -> v () * 10)
            "choose then map", Projection.choose (fun n -> if n > 0 then Some n else None), Projection.map (fun n -> n * 10)
            "filter then choose", Projection.filter (fun n -> n > 0), Projection.choose (fun n -> Some (n * 10))
            "filter then take", Projection.filter (fun n -> n > 0), Projection.take (fun () -> 5)
            "filter, skip, then map",
            Projection.filter (fun n -> n > 0),
            Projection.skip (fun () -> 0)
            >> Projection.map (fun n -> n * 10)
        ]

    testList
        "chained pending"
        [
            for name, first, rest in chains do
                test $"the end of {name} lists a never-settled upstream key in PendingKeys, and not in AnyPending" {
                    use g = new Graph ()
                    use _ = g.Activate ()
                    let _, rows = halfSettled ()
                    let view = rows |> first |> rest

                    Expect.sequenceEqual view.Keys [ 1 ] "the settled key only"
                    Expect.sequenceEqual view.PendingKeys [ 2 ] "the key an upstream stage holds out of Keys"
                    Expect.isFalse view.AnyPending "AnyPending counts rows of keys in Keys"
                }

                test $"an effect reading PendingKeys and Keys at the end of {name} runs once when the upstream key settles" {
                    use g = new Graph ()
                    use _ = g.Activate ()
                    let flights, rows = halfSettled ()
                    let view = rows |> first |> rest
                    let seen = ResizeArray<int list * (int * int) list>()

                    createEffect (fun () ->
                        let pending = List.ofArray view.PendingKeys

                        let values =
                            [ for key in view.Keys -> key, view.Get key ]
                            |> List.sort

                        seen.Add (pending, values))

                    let before = seen.Count
                    Expect.equal (fst seen[before - 1]) [ 2 ] "precondition"
                    flights[2].Settle 5
                    let added = List.ofSeq seen |> List.skip before
                    Expect.equal (List.map fst added) [ [] ] "one run, with the key no longer pending"
                    Expect.equal (List.map (snd >> List.map fst) added) [ [ 1; 2 ] ] "and the key in Keys"
                }

                test $"a key the first stage of {name} excludes on failure moves from PendingKeys to absent" {
                    use g = new Graph ()
                    use _ = g.Activate ()
                    let flights, rows = halfSettled ()
                    let excluding = rows |> first
                    let view = excluding |> rest

                    Expect.sequenceEqual view.PendingKeys [ 2 ] "precondition"
                    flights[2].Fail(TimeoutException "boom")

                    Expect.throwsT<TimeoutException> (fun () -> excluding.Get 2 |> ignore) "the excluding stage raises the error"
                    Expect.sequenceEqual view.Keys [ 1 ] "the failed key is out of Keys"
                    Expect.isEmpty view.PendingKeys "and out of PendingKeys"
                    Expect.throwsT<Collections.Generic.KeyNotFoundException> (fun () -> view.Get 2 |> ignore) "and absent"
                    Expect.equal view.Status Status.None "Status describes the pass"
                }
        ]

/// <summary>A reading of <c>memo</c> as text: <c>show</c> of the value, the exception message, or <c>pending</c>.</summary>
let private describeWith (show: 'T -> string) (memo: Memo<'T>) =
    match memo.TryValue with
    | Ready v -> show v
    | Failed ex -> ex.Message
    | Pending -> "pending"

/// <summary>A reading of <c>memo</c> as text: the value, the exception message, or <c>pending</c>.</summary>
let private describe (memo: Memo<'T>) =
    describeWith string memo

/// <summary>A reading of an option memo as text: the value, <c>none</c>, the exception message, or <c>pending</c>.</summary>
let private describeOption (memo: Memo<'T option>) =
    memo
    |> describeWith (Option.map string >> Option.defaultValue "none")

[<Tests>]
let foldTests =
    testList
        "Projection.foldGroup"
        [
            test "the sum follows edits, removals and additions" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..5 -> i, i ])
                let total = rows source |> Projection.foldGroup (+) (-) 0
                let seen = ResizeArray<int>()

                createEffect (fun () -> seen.Add total.Value)
                source.Value <- setN 2 20 source.Value
                source.Value <- source.Value |> List.filter (fun x -> x.Id <> 3)
                source.Value <- source.Value @ items [ 9, 100 ]

                Expect.sequenceEqual seen [ 15; 33; 30; 130 ] "one result per write"
            }

            test "an edit at N=1000 makes one add and one subtract" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..1000 -> i, i ])
                let adds = ref 0
                let subtracts = ref 0

                let total =
                    rows source
                    |> Projection.foldGroup
                        (fun s n ->
                            adds.Value <- adds.Value + 1
                            s + n)
                        (fun s n ->
                            subtracts.Value <- subtracts.Value + 1
                            s - n)
                        0

                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1
                    total.Value |> ignore)

                Expect.equal adds.Value 1000 "one add per key on the first read"
                adds.Value <- 0

                for i in 1..100 do
                    source.Value <- setN i (i + 1) source.Value

                Expect.equal (adds.Value, subtracts.Value) (100, 100) "one add and one subtract per edit"
                Expect.equal runs.Value 101 "one reader run per edit"
                Expect.equal total.Value (500500 + 100) "the sum"
            }

            test "an edit that leaves the row value equal leaves the reader asleep" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 3; 2, 4 ])

                let parity =
                    createProjection (fun x -> x.Id) (fun x -> x.N % 2) (fun () -> source.Value)

                let odd = parity |> Projection.foldGroup (+) (-) 0
                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1
                    odd.Value |> ignore)

                source.Value <- setN 1 5 source.Value
                Expect.equal runs.Value 1 "the row value is still odd"
                Expect.equal odd.Value 1 "one odd row"
            }

            test "a value read by add re-folds on change" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let weight = createSignal 1

                let total =
                    rows source
                    |> Projection.foldGroup (fun s n -> s + n * weight.Value) (fun s n -> s - n * weight.Value) 0

                Expect.equal total.Value 3 "weight 1"
                weight.Value <- 10
                Expect.equal total.Value 30 "weight 10"
                source.Value <- setN 1 5 source.Value
                Expect.equal total.Value 70 "an edit under weight 10"
            }

            test "a key removed and re-added counts once, with its new value" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let total = rows source |> Projection.foldGroup (+) (-) 0

                Expect.equal total.Value 3 "initial"
                source.Value <- source.Value |> List.filter (fun x -> x.Id <> 2)
                source.Value <- source.Value @ items [ 2, 7 ]
                Expect.equal total.Value 8 "key 2 re-added unread"
            }

            test "a change to the second memo read by add re-folds" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let scale = createSignal 1
                let offset = createSignal 0
                let scaleMemo = createMemo (fun _ -> scale.Value)
                let offsetMemo = createMemo (fun _ -> offset.Value)

                let total =
                    rows source
                    |> Projection.foldGroup
                        (fun s n -> s + n * scaleMemo.Value + offsetMemo.Value)
                        (fun s n -> s - n * scaleMemo.Value - offsetMemo.Value)
                        0

                Expect.equal total.Value 3 "scale 1, offset 0"
                offset.Value <- 100
                Expect.equal total.Value 203 "offset 100"
                scale.Value <- 10
                Expect.equal total.Value 230 "scale 10"
            }

            test "a pending row keeps its last settled value; a never-settled row adds nothing" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flights = dict [ for n in [ 10; 11; 20 ] -> n, createAsyncSource<int>() ]
                flights[10].Settle 5
                let source = createSignal (items [ 1, 10; 2, 20 ])

                let view =
                    createProjection (fun x -> x.Id) (fun x -> flights[x.N].Value) (fun () -> source.Value)

                let total = view |> Projection.foldGroup (+) (-) 0
                let seen = ResizeArray<string>()

                createEffect (fun () -> seen.Add $"%d{total.Value} %b{view.AnyPending}")

                source.Value <- setN 1 11 source.Value
                Expect.equal total.Value 5 "key 1 keeps its last settled value"

                flights[20].Settle 7
                flights[11].Settle 9
                Expect.equal total.Value 16 "both settled"
                Expect.equal seen[0] "5 true" "the first run counts only the settled row"
                Expect.equal (Seq.last seen) "16 false" "the reader sees the settled sum"
            }

            test "a failed row raises its error until it recovers" {
                use g = new Graph ()
                use _ = g.Activate ()
                let bad = createSignal false

                let view =
                    createProjection id (fun k -> if k = 2 && bad.Value then invalidOp "boom 2" else k) (fun () -> [ 1; 2; 3 ])

                let total = view |> Projection.foldGroup (+) (-) 0
                let seen = ResizeArray<string>()

                createEffect (fun () -> seen.Add (describe total))
                bad.Value <- true
                bad.Value <- false

                Expect.sequenceEqual seen [ "6"; "boom 2"; "6" ] "value, failure, value"
            }

            test "a removed failed row stops raising" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal [ 1; 2; 3 ]

                let view =
                    createProjection id (fun k -> if k = 2 then invalidOp "boom 2" else k) (fun () -> source.Value)

                let total = view |> Projection.foldGroup (+) (-) 0

                Expect.equal (describe total) "boom 2" "precondition"
                source.Value <- [ 1; 3 ]
                Expect.equal total.Value 4 "key 2 is gone"
            }

            test "disposing the calling scope stops the fold" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let up = rows source
                let adds = ref 0
                let scope = ref Unchecked.defaultof<Owner>

                let total =
                    createRoot (fun owner ->
                        scope.Value <- owner

                        up
                        |> Projection.foldGroup
                            (fun s n ->
                                adds.Value <- adds.Value + 1
                                s + n)
                            (-)
                            0)

                Expect.equal total.Value 3 "precondition"
                scope.Value.Dispose ()
                adds.Value <- 0
                source.Value <- setN 1 50 source.Value
                total.Value |> ignore

                Expect.equal adds.Value 0 "no add runs after disposal"
                Expect.equal (up.Get 1) 50 "the upstream still follows its source"
            }

            test "a fold over filter then sortBy follows membership and values" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..6 -> i, i ])

                let total =
                    rows source
                    |> Projection.filter (fun n -> n % 2 = 0)
                    |> Projection.sortBy (fun n -> -n)
                    |> Projection.foldGroup (+) (-) 0

                Expect.equal total.Value 12 "2 + 4 + 6"
                source.Value <- setN 1 8 source.Value
                Expect.equal total.Value 20 "key 1 joins"
                source.Value <- setN 2 3 source.Value
                Expect.equal total.Value 18 "key 2 leaves"
                source.Value <- setN 4 10 source.Value
                Expect.equal total.Value 24 "key 4 changes"
            }

            test "fold visits settled values in Keys order and re-folds on change" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 3; 2, 1; 3, 2 ])
                let calls = ref 0

                let order =
                    rows source
                    |> Projection.sortBy id
                    |> Projection.fold
                        (fun acc n ->
                            calls.Value <- calls.Value + 1
                            acc @ [ n ])
                        []

                Expect.equal order.Value [ 1; 2; 3 ] "ascending"
                calls.Value <- 0
                source.Value <- setN 1 0 source.Value
                Expect.equal order.Value [ 0; 1; 2 ] "key 1 moves first"
                Expect.equal calls.Value 3 "one folder call per row"
            }

            test "sumBy, countBy, exists and forall follow edits" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 3 ])
                let view = rows source
                let sum = view |> Projection.sumBy float
                let big = view |> Projection.countBy (fun n -> n > 1)
                let any = view |> Projection.exists (fun n -> n > 2)
                let all = view |> Projection.forall (fun n -> n > 0)

                Expect.equal (sum.Value, big.Value, any.Value, all.Value) (6.0, 2, true, true) "initial"
                source.Value <- setN 3 0 source.Value
                Expect.equal (sum.Value, big.Value, any.Value, all.Value) (3.0, 1, false, false) "key 3 set to 0"
                source.Value <- []
                Expect.equal (sum.Value, big.Value, any.Value, all.Value) (0.0, 0, false, true) "empty"
            }

            test "a throwing countBy predicate fails the count" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])

                let count =
                    rows source
                    |> Projection.countBy (fun n -> if n = 13 then invalidOp "boom 13" else n > 1)

                Expect.equal count.Value 1 "precondition"
                source.Value <- setN 1 13 source.Value
                Expect.equal (describe count) "boom 13" "the predicate's error"
                source.Value <- setN 1 5 source.Value
                Expect.equal count.Value 2 "recovered"
            }

            test "sumBy over floats recovers from infinity and NaN" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal [ 1, infinity; 2, 1.0 ]

                let sum =
                    createProjection fst snd (fun () -> source.Value)
                    |> Projection.sumBy id

                Expect.equal sum.Value infinity "an infinite row"
                source.Value <- [ 1, 2.0; 2, 1.0 ]
                Expect.equal sum.Value 3.0 "the infinite row edited"
                source.Value <- [ 1, nan; 2, 1.0 ]
                Expect.isTrue (Double.IsNaN sum.Value) "a NaN row"
                source.Value <- [ 2, 1.0 ]
                Expect.equal sum.Value 1.0 "the NaN row removed"
            }

            test "sumBy over floats reads zero once every row leaves" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal [ for i in 1..10 -> i, 0.1 * float i ]

                let sum =
                    createProjection fst snd (fun () -> source.Value)
                    |> Projection.sumBy id

                sum.Value |> ignore

                for i in 1..10 do
                    source.Value <-
                        source.Value
                        |> List.filter (fun (id, _) -> id <> i)

                    sum.Value |> ignore

                Expect.equal sum.Value 0.0 "exactly zero"
            }

#if !FABLE_COMPILER
            // .NET only: Fable wraps a null function argument in a closure, so it is never null.
            test "foldGroup rejects a null subtract" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1 ])

                Expect.throwsT<ArgumentNullException>
                    (fun () ->
                        rows source
                        |> Projection.foldGroup (+) Unchecked.defaultof<_> 0
                        |> ignore)
                    "nullArg"
            }
#endif

            test "a throwing add recomputes from the cached values on the next read" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let throwOn = ref -1

                let total =
                    rows source
                    |> Projection.foldGroup (fun s n -> if n = throwOn.Value then invalidOp "boom" else s + n) (-) 0

                Expect.equal total.Value 3 "precondition"
                throwOn.Value <- 7
                source.Value <- setN 1 7 source.Value
                Expect.throwsT<InvalidOperationException> (fun () -> total.Value |> ignore) "the recompute raises"
                throwOn.Value <- -1
                source.Value <- setN 2 5 source.Value
                Expect.equal total.Value 12 "7 + 5"
            }

            test "a batch of edits reads the combined result" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..4 -> i, i ])
                let total = rows source |> Projection.foldGroup (+) (-) 0
                let seen = ResizeArray<int>()

                createEffect (fun () -> seen.Add total.Value)

                batch (fun () ->
                    source.Value <- setN 1 10 source.Value
                    source.Value <- source.Value |> List.filter (fun x -> x.Id <> 4)
                    source.Value <- source.Value @ items [ 5, 50 ])

                Expect.sequenceEqual seen [ 10; 65 ] "one run for the batch"
            }
        ]

/// <summary>How many nodes read the row of <c>key</c> in <c>view</c>.</summary>
let private readersOf (view: Projection<int, 'V>) (key: int) =
    (view.Entries.Find key).Row.ObserverCount

/// <summary>Each aggregate over an int projection, as a read and a disposal of its memo.</summary>
let private aggregateHandles: (string * (Projection<int, int> -> (unit -> unit) * (unit -> unit))) list =
    let handle (memo: Memo<'T>) =
        (fun () -> memo.Value |> ignore), (fun () -> memo.Dispose ())

    [
        "foldGroup", (fun up -> handle (Projection.foldGroup (+) (-) 0 up))
        "fold", (fun up -> handle (Projection.fold (+) 0 up))
        "foldGroupBy", (fun up -> handle (Projection.foldGroupBy id (+) (-) 0 string up))
        "sumBy", (fun up -> handle (Projection.sumBy id up))
        "countBy", (fun up -> handle (Projection.countBy (fun n -> n > 1) up))
        "exists", (fun up -> handle (Projection.exists (fun n -> n > 1) up))
        "forall", (fun up -> handle (Projection.forall (fun n -> n > 0) up))
    ]

[<Tests>]
let ownershipTests =
    testList
        "Projection aggregate ownership"
        [
            for name, create in aggregateHandles do
                test $"disposing the {name} memo releases the upstream rows" {
                    use g = new Graph ()
                    use _ = g.Activate ()
                    let source = createSignal (items [ 1, 1; 2, 2 ])
                    let up = rows source
                    up.Get 1 |> ignore
                    Expect.equal (readersOf up 1) 0 "precondition: nothing reads row 1"

                    let read, dispose = create up
                    read ()
                    Expect.isGreaterThan (readersOf up 1) 0 "the aggregate reads row 1"

                    dispose ()
                    Expect.equal (readersOf up 1) 0 "disposing the memo disposed its view and fold"
                }

                test $"disposing the {name} memo twice, then its scope, records no error" {
                    use g = new Graph ()
                    use _ = g.Activate ()
                    let source = createSignal (items [ 1, 1; 2, 2 ])
                    let up = rows source
                    let scope = ref Unchecked.defaultof<Owner>

                    let read, dispose =
                        createRoot (fun owner ->
                            scope.Value <- owner
                            create up)

                    read ()
                    dispose ()
                    dispose ()
                    scope.Value.Dispose ()

                    Expect.equal scope.Value.ErrorCount 0 "the scope recorded no teardown error"
                    Expect.equal g.Root.ErrorCount 0 "the graph recorded no teardown error"
                    Expect.equal (readersOf up 1) 0 "the rows are released"
                }

            test "creating an aggregate inside a pure memo body fails that memo and attaches nothing" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let up = rows source
                up.Get 1 |> ignore
                let inner = createMemo (fun _ -> (up |> Projection.exists (fun n -> n > 1)).Value)

                match inner.TryValue with
                | Failed ex -> Expect.isTrue (ex :? InvalidOperationException) "the purity error"
                | other -> failtestf "expected Failed, got %A" other

                Expect.equal (readersOf up 1) 0 "no half-built aggregate reads the upstream"
            }

            test "a disposed aggregate reads its last value" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let any = rows source |> Projection.exists (fun n -> n > 1)

                Expect.isTrue any.Value "precondition"
                any.Dispose ()
                source.Value <- items [ 1, 1 ]
                Expect.isTrue any.Value "the last value"
            }

            test "disposing only the calling scope of a foldGroupBy memo stops its fold steps" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let up = rows source
                let steps = ref 0

                let count step =
                    fun s n ->
                        steps.Value <- steps.Value + 1
                        step s n

                let total, scope =
                    createRoot (fun owner ->
                        (up
                         |> Projection.foldGroupBy id (count (+)) (count (-)) 0 id),
                        owner)

                Expect.equal total.Value 3 "precondition"
                scope.Dispose ()
                steps.Value <- 0

                source.Value <- setN 1 5 source.Value
                Expect.equal total.Value 3 "the last value"
                Expect.equal steps.Value 0 "the read after the write runs no fold step"
                Expect.equal scope.ErrorCount 0 "the scope recorded no teardown error"
                Expect.equal g.Root.ErrorCount 0 "the graph recorded no teardown error"
                Expect.equal (readersOf up 1) 0 "the rows are released"
            }

            test "Memo.Adopt disposes a replaced companion and the last companion once" {
                use g = new Graph ()
                use _ = g.Activate ()
                let memo = createMemo (fun _ -> 1)
                let firstDisposals = ref 0
                let secondDisposals = ref 0

                let companion (disposals: int ref) =
                    { new IDisposable with
                        member _.Dispose() =
                            disposals.Value <- disposals.Value + 1
                    }

                let first = companion firstDisposals
                let second = companion secondDisposals

                memo.Adopt first
                memo.Adopt second
                Expect.equal firstDisposals.Value 1 "adopting a second companion disposes the first"
                Expect.equal secondDisposals.Value 0 "the second companion is live"

                memo.Adopt second
                Expect.equal secondDisposals.Value 0 "re-adopting the same companion keeps it"

                memo.Dispose ()
                memo.Dispose ()
                Expect.equal firstDisposals.Value 1 "the first companion is disposed once"
                Expect.equal secondDisposals.Value 1 "disposing the memo disposes the current companion once"
            }
        ]

[<Tests>]
let sizeTests =
    testList
        "Projection.length and isEmpty"
        [
            test "length and isEmpty follow membership" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let up = rows source
                let count = Projection.length up
                let empty = Projection.isEmpty up
                let seen = ResizeArray<int * bool>()

                createEffect (fun () -> seen.Add (count.Value, empty.Value))
                source.Value <- source.Value |> List.filter (fun x -> x.Id <> 1)
                source.Value <- source.Value |> List.filter (fun x -> x.Id <> 2)
                source.Value <- source.Value @ items [ 3, 3 ]

                Expect.sequenceEqual seen [ (2, false); (1, false); (0, true); (1, false) ] "one result per membership change"
            }

            test "a row edit wakes no reader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let rowCalls = ref 0

                let up =
                    createProjection
                        (fun x -> x.Id)
                        (fun x ->
                            rowCalls.Value <- rowCalls.Value + 1
                            x.N)
                        (fun () -> source.Value)

                let count = Projection.length up
                let empty = Projection.isEmpty up
                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1
                    count.Value |> ignore
                    empty.Value |> ignore)

                let callsBefore = rowCalls.Value
                source.Value <- setN 1 5 source.Value
                Expect.equal count.Value 2 "the count"
                Expect.isFalse empty.Value "not empty"
                Expect.equal runs.Value 1 "an edit keeps the key count"
                Expect.equal rowCalls.Value callsBefore "neither memo reads a row"
            }

            test "isEmpty wakes its readers only when emptiness changes" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 3 ])
                let empty = rows source |> Projection.isEmpty
                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1
                    empty.Value |> ignore)

                source.Value <- source.Value |> List.filter (fun x -> x.Id <> 3)
                Expect.equal runs.Value 1 "a removal that leaves keys"
                source.Value <- []
                Expect.equal runs.Value 2 "the last removal"
            }

            test "length over a filter follows the filter's membership" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 3 ])

                let count =
                    rows source
                    |> Projection.filter (fun n -> n % 2 = 0)
                    |> Projection.length

                Expect.equal count.Value 1 "key 2"
                source.Value <- setN 1 4 source.Value
                Expect.equal count.Value 2 "key 1 joins"
            }

            test "a pending pass makes length pending until it settles" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createAsyncSource<int list>()

                let count =
                    createProjection id id (fun () -> source.Value)
                    |> Projection.length

                Expect.throwsT<NotReadyException> (fun () -> count.Value |> ignore) "pending"
                source.Settle [ 1; 2 ]
                Expect.equal count.Value 2 "settled"
            }

            test "a pending pass makes isEmpty pending until it settles" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createAsyncSource<int list>()

                let empty =
                    createProjection id id (fun () -> source.Value)
                    |> Projection.isEmpty

                Expect.throwsT<NotReadyException> (fun () -> empty.Value |> ignore) "pending"
                source.Settle [ 1; 2 ]
                Expect.isFalse empty.Value "settled"
            }

            test "a failed pass makes length and isEmpty fail" {
                use g = new Graph ()
                use _ = g.Activate ()
                let up = createProjection id id (fun () -> failwith "boom")
                let count = Projection.length up
                let empty = Projection.isEmpty up

                Expect.throws (fun () -> count.Value |> ignore) "length fails"
                Expect.throws (fun () -> empty.Value |> ignore) "isEmpty fails"
            }

            test "disposing the calling scope stops length" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let up = rows source
                let runs = ref 0
                let scope = ref Unchecked.defaultof<Owner>

                let count =
                    createRoot (fun owner ->
                        scope.Value <- owner
                        let count = Projection.length up

                        createEffect (fun () ->
                            runs.Value <- runs.Value + 1
                            count.Value |> ignore)

                        count)

                scope.Value.Dispose ()
                source.Value <- items [ 1, 1 ]

                Expect.equal runs.Value 1 "the effect is disposed"
                Expect.equal count.Value 2 "the memo reads its last value"
            }
        ]

/// <summary>Expects <c>memo</c> to raise <c>KeyNotFoundException</c> with <c>message</c>, originating in the memo.</summary>
let private expectEmpty (memo: Memo<'T>) (message: string) =
    Expect.throwsC (fun () -> memo.Value |> ignore) (fun ex ->
        Expect.isTrue (ex :? Collections.Generic.KeyNotFoundException) "a KeyNotFoundException"
        Expect.equal ex.Message message "the message")

    Expect.isTrue (obj.ReferenceEquals (memo.ErrorOrigin, memo)) "the failure originates in the memo"

[<Tests>]
let positionTests =
    testList
        "Projection.tryHead, tryLast and tryItem"
        [
            test "tryHead and tryLast follow order and values" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 10; 2, 20; 3, 30 ])
                let up = rows source
                let first = Projection.tryHead up
                let final = Projection.tryLast up

                Expect.equal (first.Value, final.Value) (Some (1, 10), Some (3, 30)) "initial"
                source.Value <- setN 1 11 source.Value
                Expect.equal first.Value (Some (1, 11)) "an edit of the head"
                source.Value <- source.Value |> List.filter (fun x -> x.Id <> 1)
                Expect.equal (first.Value, final.Value) (Some (2, 20), Some (3, 30)) "the head removed"
                source.Value <- List.rev source.Value
                Expect.equal (first.Value, final.Value) (Some (3, 30), Some (2, 20)) "reversed"
                source.Value <- []
                Expect.equal (first.Value, final.Value) (None, None) "empty"
            }

            test "an edit off the head and the last key wakes no reader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 10; 2, 20; 3, 30 ])
                let up = rows source
                let first = Projection.tryHead up
                let final = Projection.tryLast up
                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1
                    first.Value |> ignore
                    final.Value |> ignore)

                source.Value <- setN 2 99 source.Value
                Expect.equal runs.Value 1 "a middle edit"
            }

            test "appending a key after the head wakes no tryHead reader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 10; 2, 20 ])
                let first = rows source |> Projection.tryHead
                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1
                    first.Value |> ignore)

                source.Value <- items [ 1, 10; 2, 20; 3, 30 ]
                Expect.equal runs.Value 1 "an append"
                source.Value <- items [ 1, 10; 3, 30 ]
                Expect.equal runs.Value 1 "a removal after the head"
                source.Value <- items [ 1, 11; 3, 30 ]
                Expect.equal runs.Value 2 "an edit of the head"
            }

            test "tryHead, tryLast and tryItem read only the row at their position" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for n in 1..50 -> n, n * 10 ])
                let rowCalls = ref 0

                let up =
                    createProjection
                        (fun x -> x.Id)
                        (fun x ->
                            rowCalls.Value <- rowCalls.Value + 1
                            x.N)
                        (fun () -> source.Value)

                let first = Projection.tryHead up
                let final = Projection.tryLast up
                let at = Projection.tryItem (fun () -> 24) up

                Expect.equal (first.Value, final.Value, at.Value) (Some (1, 10), Some (50, 500), Some (25, 250)) "initial"
                let callsBefore = rowCalls.Value

                source.Value <- setN 10 7 source.Value
                Expect.equal (first.Value, final.Value, at.Value) (Some (1, 10), Some (50, 500), Some (25, 250)) "a middle edit"
                Expect.equal rowCalls.Value callsBefore "no row function call for an edit off the positions"

                source.Value <- source.Value @ items [ 51, 510 ]
                Expect.equal final.Value (Some (51, 510)) "an append moves the last key"
                Expect.equal (first.Value, at.Value) (Some (1, 10), Some (25, 250)) "the other positions"
                Expect.isLessThanOrEqual (rowCalls.Value - callsBefore) 1 "at most the new last row is evaluated"
            }

            test "tryItem follows its index signal, and a position outside Keys reads None" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 10; 2, 20; 3, 30 ])
                let index = createSignal 1

                let at =
                    rows source
                    |> Projection.tryItem (fun () -> index.Value)

                let seen = ResizeArray<(int * int) option>()

                createEffect (fun () -> seen.Add at.Value)
                index.Value <- 5
                index.Value <- -1
                index.Value <- 0

                Expect.sequenceEqual seen [ Some (2, 20); None; Some (1, 10) ] "one run per change of result"
            }

            test "a pending head row makes tryHead pending, and a failed head row raises its error" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flight = createAsyncSource<int>()
                let bad = createSignal false

                let up =
                    createProjection
                        id
                        (fun k ->
                            if k = 1 && bad.Value then invalidOp "boom 1"
                            elif k = 1 then flight.Value
                            else k * 10)
                        (fun () -> [ 1; 2 ])

                let first = Projection.tryHead up

                Expect.throwsT<NotReadyException> (fun () -> first.Value |> ignore) "the head row is pending"
                flight.Settle 10
                Expect.equal first.Value (Some (1, 10)) "settled"
                bad.Value <- true
                Expect.equal (describe first) "boom 1" "the head row's error"
                bad.Value <- false
                Expect.equal first.Value (Some (1, 10)) "recovered"
            }

            test "tryHead over sortBy follows the minimum" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 5; 2, 3; 3, 8 ])

                let least =
                    rows source
                    |> Projection.sortBy id
                    |> Projection.tryHead

                Expect.equal least.Value (Some (2, 3)) "key 2"
                source.Value <- setN 3 1 source.Value
                Expect.equal least.Value (Some (3, 1)) "key 3 moves first"
                source.Value <- setN 3 9 source.Value
                Expect.equal least.Value (Some (2, 3)) "key 2 again"
            }

            test "a throwing index fails tryItem until it recovers" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 10 ])
                let index = createSignal 0

                let at =
                    rows source
                    |> Projection.tryItem (fun () ->
                        if index.Value < -5 then
                            invalidOp "bad index"
                        else
                            index.Value)

                Expect.equal at.Value (Some (1, 10)) "precondition"
                index.Value <- -10
                Expect.equal (describe at) "bad index" "the index's error"
                index.Value <- 0
                Expect.equal at.Value (Some (1, 10)) "recovered"
            }

            test "tryHead over a disposed view reads None" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 10 ])
                let view = rows source |> Projection.filter (fun _ -> true)
                let first = Projection.tryHead view

                Expect.equal first.Value (Some (1, 10)) "precondition"
                view.Dispose ()
                Expect.equal first.Value None "a disposed view is empty"
            }

            test "head, last and item raise KeyNotFoundException while empty and recover" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [])
                let up = rows source
                let first = Projection.head up
                let final = Projection.last up
                let second = Projection.item (fun () -> 1) up

                expectEmpty first "Projection.head: the projection has no keys."
                expectEmpty final "Projection.last: the projection has no keys."
                expectEmpty second "Projection.item: the projection has no key at the index."

                source.Value <- items [ 1, 10; 2, 20 ]
                Expect.equal (first.Value, final.Value, second.Value) ((1, 10), (2, 20), (2, 20)) "recovered"

                source.Value <- items [ 3, 30 ]
                Expect.equal first.Value (3, 30) "one key"
                expectEmpty second "Projection.item: the projection has no key at the index."
            }

            test "head raises one exception instance per memo, and a reader runs once per transition" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 10 ])
                let first = rows source |> Projection.head
                let runs = ref 0
                let errors = ResizeArray<exn>()

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1

                    try
                        first.Value |> ignore
                    with ex ->
                        errors.Add ex)

                source.Value <- []
                source.Value <- items [ 2, 20 ]
                source.Value <- []

                Expect.equal runs.Value 4 "value, empty, value, empty"
                Expect.equal errors.Count 2 "two empty periods"
                Expect.isTrue (obj.ReferenceEquals (errors[0], errors[1])) "the same exception instance"
            }

            test "disposing the calling scope stops tryHead" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 10 ])
                let up = rows source
                let runs = ref 0
                let scope = ref Unchecked.defaultof<Owner>

                let first =
                    createRoot (fun owner ->
                        scope.Value <- owner
                        let first = Projection.tryHead up

                        createEffect (fun () ->
                            runs.Value <- runs.Value + 1
                            first.Value |> ignore)

                        first)

                scope.Value.Dispose ()
                source.Value <- items [ 2, 20 ]

                Expect.equal runs.Value 1 "the effect is disposed"
                Expect.equal first.Value (Some (1, 10)) "the memo reads its last value"
            }
        ]

[<Tests>]
let findTests =
    let even (n: int) = n % 2 = 0

    testList
        "Projection.tryFind, tryFindKey and tryPick"
        [
            test "the first match in upstream order" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 4; 3, 6 ])
                let up = rows source
                let value = Projection.tryFind even up
                let key = Projection.tryFindKey even up

                Expect.equal (value.Value, key.Value) (Some 4, Some 2) "key 2 is the first even"
                source.Value <- setN 1 2 source.Value
                Expect.equal (value.Value, key.Value) (Some 2, Some 1) "key 1 turns even"
                source.Value <- setN 1 3 source.Value
                Expect.equal (value.Value, key.Value) (Some 4, Some 2) "key 1 turns odd"
                source.Value <- items [ 1, 1; 2, 3 ]
                Expect.equal (value.Value, key.Value) (None, None) "no even value"
            }

            test "200 single edits at N=1000 make 200 predicate calls" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..1000 -> i, 2 * i ])
                let calls = ref 0

                let value =
                    rows source
                    |> Projection.tryFind (fun n ->
                        calls.Value <- calls.Value + 1
                        n % 4 = 0)

                createEffect (fun () -> value.Value |> ignore)
                calls.Value <- 0

                for i in 1..200 do
                    source.Value <- setN i (2 * i + 2) source.Value

                Expect.equal calls.Value 200 "one predicate call per edit"
            }

            test "tryPick returns the first chosen value" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 4; 3, 6 ])

                let picked =
                    rows source
                    |> Projection.tryPick (fun n -> if n > 3 then Some (n * 10) else None)

                Expect.equal picked.Value (Some 40) "key 2"
                source.Value <- source.Value |> List.filter (fun x -> x.Id <> 2)
                Expect.equal picked.Value (Some 60) "key 3"
            }

            test "an edit outside the first match wakes no tryFind reader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 4; 3, 6 ])
                let value = rows source |> Projection.tryFind even
                let key = rows source |> Projection.tryFindKey even
                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1
                    value.Value |> ignore
                    key.Value |> ignore)

                Expect.equal runs.Value 1 "precondition"
                source.Value <- source.Value @ items [ 4, 8 ]
                Expect.equal runs.Value 1 "a match appended after the first"
                source.Value <- setN 3 10 source.Value
                Expect.equal runs.Value 1 "a later match edited"
                source.Value <- source.Value |> List.filter (fun x -> x.Id <> 3)
                Expect.equal runs.Value 1 "a later match removed"
                source.Value <- setN 2 6 source.Value
                Expect.equal runs.Value 2 "the first match changes"
            }

            test "editing the matching row's value with its key unchanged wakes no tryFindKey reader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 4; 3, 6 ])
                let key = rows source |> Projection.tryFindKey even
                let required = rows source |> Projection.findKey even
                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1
                    key.Value |> ignore
                    required.Value |> ignore)

                Expect.equal runs.Value 1 "precondition"
                source.Value <- setN 2 8 source.Value
                Expect.equal runs.Value 1 "the value changes, the key stays"
                Expect.equal (key.Value, required.Value) (Some 2, 2) "the key"
            }

            test "a non-matching row past the first match costs one predicate call and wakes no reader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 4; 3, 6; 4, 3 ])
                let calls = ref 0

                let value =
                    rows source
                    |> Projection.tryFind (fun n ->
                        calls.Value <- calls.Value + 1
                        even n)

                let key = rows source |> Projection.tryFindKey even
                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1
                    value.Value |> ignore
                    key.Value |> ignore)

                calls.Value <- 0
                source.Value <- setN 4 5 source.Value
                Expect.equal calls.Value 1 "one predicate call"
                Expect.equal runs.Value 1 "no reader wakes"
            }

            test "a throwing predicate excludes its key from the search" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 2; 2, 4 ])

                let value =
                    rows source
                    |> Projection.tryFind (fun n -> if n = 13 then invalidOp "boom 13" else n % 2 = 0)

                Expect.equal value.Value (Some 2) "key 1"
                source.Value <- setN 2 13 source.Value
                Expect.equal value.Value (Some 2) "a throwing key after the match"
                source.Value <- setN 2 4 source.Value
                source.Value <- setN 1 13 source.Value
                Expect.equal value.Value (Some 4) "the throwing head is skipped"
                source.Value <- setN 1 2 source.Value
                Expect.equal value.Value (Some 2) "recovered"
            }

            test "a pending predicate on the head makes the search pending; elsewhere it keeps membership" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flights = dict [ for n in [ 10; 11; 20; 21 ] -> n, createAsyncSource<bool>() ]
                flights[10].Settle true
                flights[20].Settle true
                let source = createSignal (items [ 1, 10; 2, 20 ])

                let key =
                    rows source
                    |> Projection.tryFindKey (fun n -> flights[n].Value)

                Expect.equal key.Value (Some 1) "key 1"
                source.Value <- setN 2 21 source.Value
                Expect.equal key.Value (Some 1) "a pending key after the head"
                source.Value <- setN 1 11 source.Value
                Expect.throwsT<NotReadyException> (fun () -> key.Value |> ignore) "the head's predicate is pending"
                flights[11].Settle false
                Expect.throwsT<NotReadyException> (fun () -> key.Value |> ignore) "the next head's predicate is pending"
                flights[21].Settle true
                Expect.equal key.Value (Some 2) "key 2"
            }

            test "find, findKey and pick raise KeyNotFoundException while nothing matches and recover" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1 ])
                let up = rows source
                let value = Projection.find even up
                let key = Projection.findKey even up
                let picked = Projection.pick (fun n -> if n > 3 then Some n else None) up

                expectEmpty value "Projection.find: no value satisfies the predicate."
                expectEmpty key "Projection.findKey: no value satisfies the predicate."
                expectEmpty picked "Projection.pick: the chooser returns None for every value."

                source.Value <- setN 1 4 source.Value
                Expect.equal (value.Value, key.Value, picked.Value) (4, 1, 4) "recovered"
            }

            test "disposing the calling scope stops the search" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let up = rows source
                let calls = ref 0
                let scope = ref Unchecked.defaultof<Owner>

                let value =
                    createRoot (fun owner ->
                        scope.Value <- owner

                        up
                        |> Projection.tryFind (fun n ->
                            calls.Value <- calls.Value + 1
                            even n))

                Expect.equal value.Value (Some 2) "precondition"
                scope.Value.Dispose ()
                calls.Value <- 0
                source.Value <- setN 1 8 source.Value
                value.Value |> ignore

                Expect.equal calls.Value 0 "no predicate runs after disposal"
                Expect.equal value.Value (Some 2) "the last value"
            }

            test "disposing the tryFind memo releases the upstream rows" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let up = rows source
                up.Get 1 |> ignore
                let value = Projection.tryFind even up

                value.Value |> ignore
                Expect.isGreaterThan (readersOf up 1) 0 "the filter reads row 1"
                value.Dispose ()
                Expect.equal (readersOf up 1) 0 "disposing the memo disposed the filter"
            }
        ]

let private additions = ref 0

/// <summary>A number whose additions are counted in <c>additions</c>.</summary>
type Meter =
    {
        Amount: float
    }

    static member (+)(a: Meter, b: Meter) =
        additions.Value <- additions.Value + 1
        { Amount = a.Amount + b.Amount }

    static member (-)(a: Meter, b: Meter) =
        { Amount = a.Amount - b.Amount }

    static member Zero = { Amount = 0.0 }

    static member DivideByInt(a: Meter, n: int) =
        { Amount = a.Amount / float n }

[<Tests>]
let averageTests =
    testList
        "Projection.tryAverageBy and averageBy"
        [
            test "tryAverageBy averages float32 and decimal" {
                use g = new Graph ()
                use _ = g.Activate ()
                let up = rows (createSignal (items [ 1, 1; 2, 2 ]))

                Expect.equal (Projection.tryAverageBy float32 up).Value (Some 1.5f) "float32"
                Expect.equal (Projection.tryAverageBy decimal up).Value (Some 1.5m) "decimal"
            }

            test "tryAverageBy follows edits, removals and additions" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 2; 2, 4 ])
                let mean = rows source |> Projection.tryAverageBy float
                let seen = ResizeArray<float option>()

                createEffect (fun () -> seen.Add mean.Value)
                source.Value <- setN 1 6 source.Value
                source.Value <- []
                source.Value <- items [ 3, 9 ]

                Expect.sequenceEqual seen [ Some 3.0; Some 5.0; None; Some 9.0 ] "one result per write"
            }

            test "an equal mean after a key change wakes no reader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 2; 2, 4 ])
                let mean = rows source |> Projection.tryAverageBy float
                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1
                    mean.Value |> ignore)

                source.Value <- items [ 1, 2; 2, 4; 3, 3 ]

                Expect.equal mean.Value (Some 3.0) "the mean"
                Expect.equal runs.Value 1 "the mean is unchanged"
            }

#if !FABLE_COMPILER
            // .NET only: the policy is an object expression over a generic interface member.
            test "a read through a memo of another graph publishes the current mean under a custom policy" {
                let always =
                    { new IEqualityPolicy with
                        member _.Comparer<'T>() =
                            { new Collections.Generic.IEqualityComparer<'T> with
                                member _.Equals(_, _) = true
                                member _.GetHashCode _ = 0
                            }
                    }

                use a =
                    new Graph (
                        { GraphOptions.Default with
                            Equality = always
                        }
                    )

                use b = new Graph ()
                use _ = b.Activate ()
                let source = createSignal (items [ 1, 3 ])
                let mean = rows source |> Projection.tryAverageBy float
                let seen = ResizeArray<float option>()

                createEffect (fun () ->
                    source.Value |> ignore
                    let viaA = a.Run (fun () -> createMemo (fun _ -> mean.Value))
                    seen.Add viaA.Value)

                source.Value <- items [ 1, 5 ]

                Expect.equal (Seq.last seen) (Some 5.0) "the read sees the current mean"
            }
#endif

            test "an edit at N=1000 makes one addition" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..1000 -> i, i ])
                additions.Value <- 0

                let mean =
                    rows source
                    |> Projection.tryAverageBy (fun n -> { Amount = float n })

                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1
                    mean.Value |> ignore)

                Expect.equal additions.Value 1000 "one addition per key on the first read"
                additions.Value <- 0

                for i in 1..100 do
                    source.Value <- setN i (i + 1) source.Value

                Expect.equal additions.Value 100 "one addition per edit"
                Expect.equal runs.Value 101 "one reader run per edit"
                Expect.equal (mean.Value |> Option.map _.Amount) (Some (500600.0 / 1000.0)) "the mean"
            }

            test "tryAverageBy over floats recovers from infinity and NaN" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal [ 1, infinity; 2, 1.0 ]

                let mean =
                    createProjection fst snd (fun () -> source.Value)
                    |> Projection.tryAverageBy id

                Expect.equal mean.Value (Some infinity) "an infinite row"
                source.Value <- [ 1, 3.0; 2, 1.0 ]
                Expect.equal mean.Value (Some 2.0) "the infinite row edited"
                source.Value <- [ 1, nan; 2, 1.0 ]
                Expect.isTrue (mean.Value |> Option.exists Double.IsNaN) "a NaN row"
                source.Value <- [ 2, 1.0 ]
                Expect.equal mean.Value (Some 1.0) "the NaN row removed"
            }

            test "a pending row keeps its last settled value; a never-settled row adds nothing" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flights = dict [ for n in [ 10; 11; 20 ] -> n, createAsyncSource<int>() ]
                flights[10].Settle 4
                let source = createSignal (items [ 1, 10; 2, 20 ])

                let mean =
                    createProjection (fun x -> x.Id) (fun x -> flights[x.N].Value) (fun () -> source.Value)
                    |> Projection.tryAverageBy float

                Expect.equal mean.Value (Some 4.0) "the never-settled row is not counted"
                source.Value <- setN 1 11 source.Value
                Expect.equal mean.Value (Some 4.0) "key 1 keeps its last settled value"
                flights[20].Settle 8
                Expect.equal mean.Value (Some 6.0) "key 2 settles"
                flights[11].Settle 2
                Expect.equal mean.Value (Some 5.0) "key 1 settles"
            }

            test "averageBy raises KeyNotFoundException while empty and recovers" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [])
                let mean = rows source |> Projection.averageBy float

                expectEmpty mean "Projection.averageBy: the projection has no settled values."
                source.Value <- items [ 1, 2; 2, 4 ]
                Expect.equal mean.Value 3.0 "recovered"
                source.Value <- []
                expectEmpty mean "Projection.averageBy: the projection has no settled values."
            }

            test "disposing the tryAverageBy memo releases the upstream rows" {
                use g = new Graph ()
                use _ = g.Activate ()
                let up = rows (createSignal (items [ 1, 1; 2, 2 ]))
                up.Get 1 |> ignore
                let mean = up |> Projection.tryAverageBy float

                mean.Value |> ignore
                Expect.isGreaterThan (readersOf up 1) 0 "the mean reads row 1"
                mean.Dispose ()
                Expect.equal (readersOf up 1) 0 "disposing the memo disposed its view and fold"
            }
        ]

[<Tests>]
let extremeTests =
    testList
        "Projection.tryMinBy and tryMaxBy"
        [
            test "tryMinBy and tryMaxBy follow edits and removals" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 5; 2, 3; 3, 8 ])
                let up = rows source
                let least = Projection.tryMinBy id up
                let most = Projection.tryMaxBy id up

                Expect.equal (least.Value, most.Value) (Some 3, Some 8) "initial"
                source.Value <- setN 3 1 source.Value
                Expect.equal (least.Value, most.Value) (Some 1, Some 5) "key 3 drops to 1"
                source.Value <- source.Value |> List.filter (fun x -> x.Id <> 3)
                Expect.equal (least.Value, most.Value) (Some 3, Some 5) "key 3 removed"
                source.Value <- []
                Expect.equal (least.Value, most.Value) (None, None) "empty"
            }

            test "among equal ranks the current result stays until its holder's row is removed or edited" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 21; 2, 22 ])

                let least =
                    rows source
                    |> Projection.tryMinBy (fun n -> n / 10)

                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1
                    least.Value |> ignore)

                Expect.equal least.Value (Some 21) "key 1 holds the tie"
                source.Value <- setN 2 23 source.Value
                Expect.equal (least.Value, runs.Value) (Some 21, 1) "an edit of the other tie wakes no reader"
                source.Value <- List.rev source.Value
                Expect.equal (least.Value, runs.Value) (Some 21, 1) "a reorder keeps the holder and wakes no reader"
                source.Value <- setN 1 24 source.Value
                Expect.equal least.Value (Some 24) "an edit of the holder to an equal rank keeps the holder"
                source.Value <- source.Value |> List.filter (fun x -> x.Id <> 1)
                Expect.equal least.Value (Some 23) "the next holder"
            }

            test "NaN orders last: tryMaxBy holds NaN, tryMinBy skips it" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal [ 1, nan; 2, 1.0 ]
                let up = createProjection fst snd (fun () -> source.Value)
                let least = Projection.tryMinBy id up
                let most = Projection.tryMaxBy id up

                Expect.equal least.Value (Some 1.0) "the least non-NaN value"
                Expect.isTrue (most.Value |> Option.exists Double.IsNaN) "NaN is the greatest rank"
                source.Value <- [ 1, nan ]
                Expect.isTrue (least.Value |> Option.exists Double.IsNaN) "every rank is NaN"
            }

            test "a failed rank raises until it recovers" {
                use g = new Graph ()
                use _ = g.Activate ()
                let bad = createSignal false

                let up =
                    createProjection id (fun k -> if k = 2 && bad.Value then invalidOp "boom 2" else k) (fun () -> [ 1; 2; 3 ])

                let most = up |> Projection.tryMaxBy id
                let seen = ResizeArray<string>()

                createEffect (fun () -> seen.Add (describeOption most))
                bad.Value <- true
                bad.Value <- false

                Expect.sequenceEqual seen [ "3"; "boom 2"; "3" ] "value, failure, value"
            }

            test "a pending rank keeps its last settled rank" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flights = dict [ for n in [ 10; 11 ] -> n, createAsyncSource<int>() ]
                flights[10].Settle 10
                let source = createSignal (items [ 1, 10; 2, 5 ])

                let most =
                    createProjection (fun x -> x.Id) (fun x -> if x.N > 9 then flights[x.N].Value else x.N) (fun () -> source.Value)
                    |> Projection.tryMaxBy id

                Expect.equal most.Value (Some 10) "key 1"
                source.Value <- setN 1 11 source.Value
                Expect.equal most.Value (Some 10) "key 1 keeps its last settled rank"
                flights[11].Settle 11
                Expect.equal most.Value (Some 11) "key 1 settles"
            }

            test "a batch of edits reads the combined result" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..4 -> i, i ])
                let least = rows source |> Projection.tryMinBy id
                let seen = ResizeArray<int option>()

                createEffect (fun () -> seen.Add least.Value)

                batch (fun () ->
                    source.Value <- setN 1 10 source.Value
                    source.Value <- source.Value |> List.filter (fun x -> x.Id <> 2)
                    source.Value <- source.Value @ items [ 5, 0 ])

                Expect.sequenceEqual seen [ Some 1; Some 0 ] "one run for the batch"
            }

            test "a key removed and re-added while unread follows its new rank" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let least = rows source |> Projection.tryMinBy id

                Expect.equal least.Value (Some 1) "precondition"
                source.Value <- source.Value |> List.filter (fun x -> x.Id <> 1)
                source.Value <- source.Value @ items [ 1, 9 ]
                Expect.equal least.Value (Some 2) "key 1 re-added with rank 9"
            }

            test "minBy and maxBy raise KeyNotFoundException while empty and recover" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [])
                let up = rows source
                let least = Projection.minBy id up
                let most = Projection.maxBy id up

                expectEmpty least "Projection.minBy: the projection has no settled values."
                expectEmpty most "Projection.maxBy: the projection has no settled values."
                source.Value <- items [ 1, 4; 2, 7 ]
                Expect.equal (least.Value, most.Value) (4, 7) "recovered"
            }

            test "minBy stays asleep while it stays empty" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flight = createAsyncSource<int>()
                let source = createSignal [ 1 ]
                let up = createProjection id (fun _ -> flight.Value) (fun () -> source.Value)
                let least = Projection.minBy id up
                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1

                    try
                        least.Value |> ignore
                    with _ ->
                        ())

                source.Value <- [ 1; 2 ]
                Expect.equal runs.Value 1 "a Keys write that leaves the result empty wakes no reader"
                expectEmpty least "Projection.minBy: the projection has no settled values."
                flight.Settle 5
                Expect.equal least.Value 5 "settled"
            }

            test "disposing the calling scope stops the rank calls" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let up = rows source
                let calls = ref 0
                let scope = ref Unchecked.defaultof<Owner>

                let least =
                    createRoot (fun owner ->
                        scope.Value <- owner

                        up
                        |> Projection.tryMinBy (fun n ->
                            calls.Value <- calls.Value + 1
                            n))

                Expect.equal least.Value (Some 1) "precondition"
                scope.Value.Dispose ()
                calls.Value <- 0
                source.Value <- setN 1 0 source.Value
                least.Value |> ignore

                Expect.equal calls.Value 0 "no rank runs after disposal"
            }

            test "disposing the tryMinBy memo releases the upstream rows" {
                use g = new Graph ()
                use _ = g.Activate ()
                let up = rows (createSignal (items [ 1, 1; 2, 2 ]))
                up.Get 1 |> ignore
                let least = up |> Projection.tryMinBy id

                least.Value |> ignore
                Expect.isGreaterThan (readersOf up 1) 0 "the rank view reads row 1"
                least.Dispose ()
                Expect.equal (readersOf up 1) 0 "disposing the memo disposed its view and fold"
            }
        ]

[<Tests>]
let partitionTests =
    let even (n: int) = n % 2 = 0

    testList
        "Projection.partition"
        [
            test "the halves are complementary, in upstream order" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 3; 4, 4 ])
                let evens, odds = rows source |> Projection.partition even

                Expect.equal (evens.Keys, odds.Keys) ([| 2; 4 |], [| 1; 3 |]) "initial"
                source.Value <- setN 1 6 source.Value
                Expect.equal (evens.Keys, odds.Keys) ([| 1; 2; 4 |], [| 3 |]) "key 1 moves across"
            }

            test "200 single edits at N=1000 make 200 predicate calls across both halves" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..1000 -> i, 2 * i ])
                let calls = ref 0

                let fours, rest =
                    rows source
                    |> Projection.partition (fun n ->
                        calls.Value <- calls.Value + 1
                        n % 4 = 0)

                keysAndRows fours |> ignore
                keysAndRows rest |> ignore
                calls.Value <- 0

                for i in 1..200 do
                    source.Value <- setN i (2 * i + 2) source.Value

                Expect.equal calls.Value 200 "one predicate call per edit"
            }

            test "a throwing predicate excludes the key from both halves" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 3; 4, 4 ])

                let evens, odds =
                    rows source
                    |> Projection.partition (fun n -> if n = 13 then invalidOp "boom 13" else even n)

                keysAndRows evens |> ignore
                keysAndRows odds |> ignore
                source.Value <- setN 2 13 source.Value

                Expect.equal (evens.Keys, odds.Keys) ([| 4 |], [| 1; 3 |]) "key 2 is in neither half"
                Expect.throwsC (fun () -> evens.Get 2 |> ignore) (fun ex -> Expect.equal ex.Message "boom 13" "evens.Get")
                Expect.throwsC (fun () -> odds.Get 2 |> ignore) (fun ex -> Expect.equal ex.Message "boom 13" "odds.Get")
                source.Value <- setN 2 2 source.Value
                Expect.equal evens.Keys [| 2; 4 |] "recovered"
            }

            test "a pending predicate keeps the last side; a never-settled key is pending in both" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flights = dict [ for n in [ 10; 11; 30 ] -> n, createAsyncSource<bool>() ]
                flights[10].Settle true
                let source = createSignal (items [ 1, 10; 3, 30 ])

                let yes, no =
                    rows source
                    |> Projection.partition (fun n -> flights[n].Value)

                Expect.equal (yes.Keys, no.Keys) ([| 1 |], [||]) "key 3 has never settled"
                Expect.sequenceEqual yes.PendingKeys [ 3 ] "pending in the first half"
                Expect.sequenceEqual no.PendingKeys [ 3 ] "pending in the second half"

                source.Value <- setN 1 11 source.Value
                Expect.equal yes.Keys [| 1 |] "key 1 keeps its side"
                flights[11].Settle false
                Expect.equal (yes.Keys, no.Keys) ([||], [| 1 |]) "key 1 moves once settled"
                flights[30].Settle true
                Expect.equal yes.Keys [| 3 |] "key 3 joins"
            }

            test "an effect reading both halves runs once per write" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..6 -> i, i ])
                let evens, odds = rows source |> Projection.partition even
                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1

                    for key in evens.Keys do
                        evens.Get key |> ignore

                    for key in odds.Keys do
                        odds.Get key |> ignore)

                once runs source "move a key across" (setN 1 2)
                once runs source "edit within a half" (setN 2 4)
                once runs source "remove a key" (List.filter (fun x -> x.Id <> 3))
                once runs source "add a key" (fun xs -> xs @ items [ 7, 7 ])
                once runs source "reorder" List.rev
            }

            test "disposing one half leaves the other following edits" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 3 ])
                let evens, odds = rows source |> Projection.partition even

                keysAndRows odds |> ignore
                evens.Dispose ()
                source.Value <- setN 1 6 source.Value

                Expect.isEmpty evens.Keys "a disposed half is empty"
                Expect.equal odds.Keys [| 3 |] "the other half follows"
            }

            test "disposing the calling scope stops every predicate call" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let up = rows source
                let calls = ref 0
                let scope = ref Unchecked.defaultof<Owner>

                let evens, odds =
                    createRoot (fun owner ->
                        scope.Value <- owner

                        up
                        |> Projection.partition (fun n ->
                            calls.Value <- calls.Value + 1
                            even n))

                up.Get 1 |> ignore
                evens.Keys |> ignore
                odds.Keys |> ignore
                Expect.isGreaterThan (readersOf up 1) 0 "the predicate view reads row 1"
                scope.Value.Dispose ()
                calls.Value <- 0
                source.Value <- setN 1 8 source.Value

                Expect.isEmpty evens.Keys "the first half is disposed"
                Expect.isEmpty odds.Keys "the second half is disposed"
                Expect.equal (readersOf up 1) 0 "the shared predicate view released the upstream row"
                Expect.equal calls.Value 0 "no predicate runs after disposal"
            }
        ]

[<Tests>]
let distinctByTests =
    testList
        "Projection.distinctBy"
        [
            test "the first holder per value, in upstream order" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 1; 4, 3 ])

                let view =
                    rows source
                    |> Projection.distinctBy (fun n -> n % 3)

                Expect.sequenceEqual view.Keys [ 1; 2; 4 ] "keys 1, 2 and 4 hold 1, 2 and 0"
                source.Value <- source.Value |> List.filter (fun x -> x.Id <> 1)
                Expect.sequenceEqual view.Keys [ 2; 3; 4 ] "key 3 takes over value 1"
                source.Value <- List.rev source.Value
                Expect.sequenceEqual view.Keys [ 4; 3; 2 ] "upstream order"
            }

            test "distinctBy republishes Keys only when the holder set changes" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 1 ])

                let view =
                    rows source
                    |> Projection.distinctBy (fun n -> n % 3)

                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1
                    view.Keys |> ignore)

                source.Value <- setN 1 4 source.Value
                Expect.equal runs.Value 1 "key 1 still holds value 1"
            }

            test "a key reader sees a hand-over as Removed and Added" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 1 ])

                let view =
                    rows source
                    |> Projection.distinctBy (fun n -> n % 3)

                createEffect (fun () -> view.Keys |> ignore)
                let reader = view.NewKeyReader ()
                reader.Read () |> ignore
                source.Value <- source.Value |> List.filter (fun x -> x.Id <> 1)
                let delta = reader.Read ()

                Expect.sequenceEqual delta.Keys [ 2; 3 ] "key 3 holds value 1"

                Expect.equal
                    (delta.Changes
                     |> Seq.map (fun pair -> pair.Key, pair.Value)
                     |> Set.ofSeq)
                    (set [ 1, KeyChange.Removed; 3, KeyChange.Added ])
                    "the hand-over"
            }

            test "a pending value keeps the key's last settled value; a never-settled key is only in PendingKeys" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flights = dict [ for n in [ 10; 11; 30 ] -> n, createAsyncSource<int>() ]
                flights[10].Settle 1
                let source = createSignal (items [ 1, 10; 3, 30 ])

                let view =
                    rows source
                    |> Projection.distinctBy (fun n -> flights[n].Value)

                Expect.sequenceEqual view.Keys [ 1 ] "key 3 has never settled"
                Expect.sequenceEqual view.PendingKeys [ 3 ] "key 3 is pending"
                source.Value <- setN 1 11 source.Value
                Expect.sequenceEqual view.Keys [ 1 ] "key 1 keeps its last value"
                flights[30].Settle 1
                Expect.sequenceEqual view.Keys [ 1 ] "key 3 settles to key 1's value"
                flights[11].Settle 2
                Expect.sequenceEqual view.Keys [ 1; 3 ] "key 1 moves to value 2, and key 3 holds value 1"
            }

            test "a pending key whose last settled value repeats an earlier holder stays out of Keys" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flights = dict [ for n in [ 10; 20; 21 ] -> n, createAsyncSource<int>() ]
                flights[10].Settle 1
                flights[20].Settle 1
                let source = createSignal (items [ 1, 10; 2, 20 ])

                let view =
                    rows source
                    |> Projection.distinctBy (fun n -> flights[n].Value)

                Expect.sequenceEqual view.Keys [ 1 ] "key 2 repeats value 1"
                source.Value <- setN 2 21 source.Value
                Expect.sequenceEqual view.Keys [ 1 ] "pending key 2 still repeats value 1"
                flights[21].Settle 2
                Expect.sequenceEqual view.Keys [ 1; 2 ] "key 2 settles to value 2"
            }

            test "a throwing value excludes the key, and Get and TryGet raise its error" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 4 ])

                let view =
                    rows source
                    |> Projection.distinctBy (fun n -> if n = 13 then invalidOp "boom 13" else n % 3)

                Expect.sequenceEqual view.Keys [ 1 ] "key 2 repeats value 1"
                source.Value <- setN 1 13 source.Value
                Expect.sequenceEqual view.Keys [ 2 ] "key 2 takes over"
                Expect.throwsC (fun () -> view.Get 1 |> ignore) (fun ex -> Expect.equal ex.Message "boom 13" "Get raises")
                Expect.throwsC (fun () -> view.TryGet 1 |> ignore) (fun ex -> Expect.equal ex.Message "boom 13" "TryGet raises")
                source.Value <- setN 1 1 source.Value
                Expect.sequenceEqual view.Keys [ 1 ] "recovered"
            }

            test "disposing the view detaches it from the upstream" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let up = rows source
                let calls = ref 0

                let view =
                    up
                    |> Projection.distinctBy (fun n ->
                        calls.Value <- calls.Value + 1
                        n % 3)

                keysAndRows view |> ignore
                Expect.isGreaterThan (readersOf up 1) 0 "the value view reads row 1"
                view.Dispose ()
                Expect.equal (readersOf up 1) 0 "the value view released the upstream row"
                calls.Value <- 0
                source.Value <- setN 1 6 source.Value

                Expect.equal calls.Value 0 "no projection runs after disposal"
                Expect.isEmpty view.Keys "a disposed view is empty"
            }
        ]

/// <summary>The keys of <c>view</c> with their values, in <c>Keys</c> order.</summary>
let private pairsOf (view: Projection<'G, 'S>) =
    [ for key in view.Keys -> key, view.Get key ]

[<Tests>]
let groupingAggregateTests =
    let parity (n: int) = n % 2

    testList
        "Grouping.aggregate"
        [
            test "totals per group follow edits, moves and emptying" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 3; 4, 4 ])

                let totals =
                    rows source
                    |> Projection.groupBy parity
                    |> Grouping.aggregate (Projection.sumBy id)

                Expect.equal (pairsOf totals) [ 1, 4; 0, 6 ] "initial"
                source.Value <- setN 3 6 source.Value
                Expect.equal (pairsOf totals) [ 1, 1; 0, 12 ] "key 3 moves to the even group"
                source.Value <- setN 1 8 source.Value
                Expect.equal (pairsOf totals) [ 0, 20 ] "the odd group empties"
                source.Value <- setN 1 9 source.Value
                Expect.equal (pairsOf totals) [ 1, 9; 0, 12 ] "the odd group returns"
            }

            test "an edit within a group runs only that group's aggregate" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 3; 4, 4 ])
                let adds = ref 0

                let totals =
                    rows source
                    |> Projection.groupBy parity
                    |> Grouping.aggregate (
                        Projection.foldGroup
                            (fun s n ->
                                adds.Value <- adds.Value + 1
                                s + n)
                            (-)
                            0
                    )

                keysAndRows totals |> ignore
                adds.Value <- 0
                source.Value <- setN 2 20 source.Value

                Expect.equal adds.Value 1 "one add in the even group"
                Expect.equal (pairsOf totals) [ 1, 4; 0, 24 ] "the totals"
            }

            test "an emptied group's aggregate is disposed, and a returning group gets a new one" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 3 ])
                let created = ResizeArray<Memo<int>>()

                let totals =
                    rows source
                    |> Projection.groupBy parity
                    |> Grouping.aggregate (fun inner ->
                        let total = Projection.sumBy id inner
                        created.Add total
                        total)

                keysAndRows totals |> ignore
                let odd = created[0]
                source.Value <- setN 1 2 source.Value
                Expect.equal odd.Value 3 "key 3 alone in the odd group"
                source.Value <- setN 3 4 source.Value

                Expect.equal odd.Value 3 "a disposed aggregate reads its last total"
                source.Value <- setN 1 5 source.Value
                Expect.equal created.Count 3 "the returning odd group created a new aggregate"
                Expect.equal (pairsOf totals) [ 1, 5; 0, 6 ] "the totals"
            }

            test "an effect reading every group's total runs once per write" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..6 -> i, i ])

                let totals =
                    rows source
                    |> Projection.groupBy parity
                    |> Grouping.aggregate (Projection.sumBy id)

                let runs = keysAndRows totals

                once runs source "edit a value" (setN 2 8)
                once runs source "remove a key" (List.filter (fun x -> x.Id <> 4))
                once runs source "add a key" (fun xs -> xs @ items [ 7, 7 ])
            }

            test "emptying a group and creating one each run the reader once" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 3 ])

                let totals =
                    rows source
                    |> Projection.groupBy parity
                    |> Grouping.aggregate (Projection.sumBy id)

                let runs = keysAndRows totals

                once runs source "empty a group" (List.filter (fun x -> x.Id <> 2))
                Expect.equal (Array.toList totals.Keys) [ 1 ] "only the odd group"
                once runs source "create a group" (fun xs -> xs @ items [ 4, 4 ])
                Expect.equal (Array.toList totals.Keys) [ 1; 0 ] "the even group returns"
            }

            test "a pending or failed group memo makes its row pending or failed" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2; 3, 3 ])

                let found =
                    rows source
                    |> Projection.groupBy parity
                    |> Grouping.aggregate (Projection.find (fun n -> n > 2))

                Expect.equal (found.Get 1) 3 "the odd group has a match"

                Expect.throwsT<Collections.Generic.KeyNotFoundException> (fun () -> found.Get 0 |> ignore) "the even group has none"

                let flight = createAsyncSource<int>()

                let pending =
                    rows source
                    |> Projection.groupBy parity
                    |> Grouping.aggregate (fun inner -> createMemo (fun _ -> inner.Keys.Length + flight.Value))

                Expect.throwsT<NotReadyException> (fun () -> pending.Get 1 |> ignore) "the memo is pending"
                flight.Settle 10
                Expect.equal (pending.Get 1) 12 "the memo settled"
            }

            test "a reorder that keeps group order and totals wakes no reader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ for i in 1..5 -> i, i ])

                let totals =
                    rows source
                    |> Projection.groupBy parity
                    |> Grouping.aggregate (Projection.sumBy id)

                let runs = keysAndRows totals
                source.Value <- List.rev source.Value

                Expect.equal runs.Value 1 "the reader ran only on creation"
                Expect.equal (pairsOf totals) [ 1, 9; 0, 6 ] "the totals"
            }

            test "a never-settled group key contributes to no total" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flight = createAsyncSource<int>()
                let source = createSignal (items [ 1, 1; 2, -2; 3, 3 ])

                let grouping =
                    rows source
                    |> Projection.groupBy (fun n -> if n < 0 then flight.Value else parity n)

                let totals =
                    grouping
                    |> Grouping.aggregate (Projection.sumBy id)

                Expect.equal (pairsOf totals) [ 1, 4 ] "key 2 has no group"
                Expect.sequenceEqual grouping.UngroupedKeys [ 2 ] "key 2 is ungrouped"
                flight.Settle 0
                Expect.equal (pairsOf totals) [ 1, 4; 0, -2 ] "key 2 joins the even group"
            }

            test "disposing the result disposes every aggregate; disposing the grouping empties the result" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (items [ 1, 1; 2, 2 ])
                let created = ResizeArray<Memo<int>>()
                let grouping = rows source |> Projection.groupBy parity

                let totals =
                    grouping
                    |> Grouping.aggregate (fun inner ->
                        let total = Projection.sumBy id inner
                        created.Add total
                        total)

                keysAndRows totals |> ignore
                totals.Dispose ()
                source.Value <- items [ 1, 11; 2, 12 ]
                Expect.equal [ for total in created -> total.Value ] [ 1; 2 ] "the aggregates read their last totals"

                let again =
                    grouping
                    |> Grouping.aggregate (Projection.sumBy id)

                Expect.equal (pairsOf again) [ 1, 11; 0, 12 ] "a new result"
                grouping.Dispose ()
                Expect.isEmpty again.Keys "the result of a disposed grouping is empty"
            }
        ]
