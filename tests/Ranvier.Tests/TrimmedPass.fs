module Ranvier.Tests.TrimmedPass

open System
open Expecto
open Ranvier

type private Item = { Id: int; Text: string }

/// <summary>A createProjectionWith over items 1..n, counting keyOf calls, factory runs and cleanups.</summary>
let private counted (n: int) =
    let items =
        createSignal (Array.init n (fun i -> { Id = i + 1; Text = string (i + 1) }))

    let keyCalls = ref 0
    let created = ResizeArray<int>()
    let disposed = ResizeArray<int>()

    let proj =
        createProjectionWith
            (fun (item: Item) ->
                keyCalls.Value <- keyCalls.Value + 1
                item.Id)
            (fun item ->
                let id = (item ()).Id
                created.Add id
                onCleanup (fun () -> disposed.Add id)
                fun () -> (item ()).Text)
            (fun () -> items.Value :> seq<_>)

    createEffect (fun () ->
        try
            for key in proj.Keys do
                proj.Get key |> ignore
        with _ ->
            ())

    items, keyCalls, created, disposed, proj

let private duplicateAfter (edit: Item[] -> Item[]) =
    use g = new Graph ()
    use _ = g.Activate ()
    let items, _, _, disposed, proj = counted 10
    // An unchanged republish first, so the duplicate meets a trimmed pass.
    items.Value <- Array.copy items.Value
    items.Value <- edit items.Value
    Expect.throwsT<InvalidOperationException> (fun () -> proj.Keys |> ignore) "the pass fails"
    Expect.stringContains proj.Error.Message "twice in one pass" "the duplicate-key failure"
    Expect.isEmpty disposed "the failed pass retired no row"

/// <summary>An item compared by reference under every equality policy.</summary>
type private Box(id: int, value: int) =
    member _.Id = id
    member _.Value = value

/// <summary>A key equal by <c>Id</c> that counts its <c>GetHashCode</c> calls in <c>hashes</c>.</summary>
type private HashedKey(id: int, hashes: int ref) =
    member _.Id = id

    override _.GetHashCode() =
        hashes.Value <- hashes.Value + 1
        id

    override _.Equals(other) =
        match other with
        | :? HashedKey as key -> key.Id = id
        | _ -> false

/// <summary>
/// A createProjectionWith over boxes with Ids 1..<c>n</c>, recording factory runs and cleanups. <c>write generation
/// ids</c> publishes a new box for every Id, whose row reads <c>generation * 10000 + id</c>.
/// </summary>
let private boxed (n: int) =
    let boxes (generation: int) (ids: int[]) =
        ids
        |> Array.map (fun id -> Box (id, generation * 10000 + id))

    let ids = Array.init n (fun i -> i + 1)
    let items = createSignal (boxes 0 ids)
    let created = ResizeArray<int>()
    let disposed = ResizeArray<int>()

    let proj =
        createProjectionWith
            (fun (item: Box) -> item.Id)
            (fun item ->
                let id = (item ()).Id
                created.Add id
                onCleanup (fun () -> disposed.Add id)
                fun () -> (item ()).Value)
            (fun () -> items.Value :> seq<_>)

    createEffect (fun () ->
        try
            for key in proj.Keys do
                proj.Get key |> ignore
        with _ ->
            ())

    let write (generation: int) (next: int[]) =
        items.Value <- boxes generation next

    ids, write, created, disposed, proj

/// <summary>
/// Writes new boxes at the Ids <c>edit</c> returns over 1,000 boxes, then new boxes at the same Ids again. After each
/// write, the keys are the written Ids, only added keys create rows, only removed keys dispose rows, and every row reads
/// its new box.
/// </summary>
let private freshAfter (edit: int[] -> int[]) =
    use g = new Graph ()
    use _ = g.Activate ()
    let ids, write, created, disposed, proj = boxed 1000
    let next = edit ids

    for generation, before in [ 1, ids; 2, next ] do
        created.Clear ()
        disposed.Clear ()
        write generation next
        let kept = set before
        let now = set next
        Expect.sequenceEqual proj.Keys next $"write {generation}: the keys"

        Expect.sequenceEqual
            (Seq.sort created)
            (next
             |> Array.filter (kept.Contains >> not)
             |> Array.sort)
            $"write {generation}: rows are created for added keys alone"

        Expect.sequenceEqual
            (Seq.sort disposed)
            (before
             |> Array.filter (now.Contains >> not)
             |> Array.sort)
            $"write {generation}: rows are disposed for removed keys alone"

        for key in next do
            Expect.equal (proj.Get key) (generation * 10000 + key) $"write {generation}: row {key} reads its new box"

/// <summary>
/// Writes new boxes at the Ids <c>edit</c> returns, which repeat a key, over 10 boxes; then new boxes at the original
/// Ids.
/// </summary>
let private freshDuplicate (edit: int[] -> int[]) =
    use g = new Graph ()
    use _ = g.Activate ()
    let ids, write, created, disposed, proj = boxed 10
    created.Clear ()
    write 1 (edit ids)
    Expect.throwsT<InvalidOperationException> (fun () -> proj.Keys |> ignore) "the pass fails"
    Expect.stringContains proj.Error.Message "twice in one pass" "the duplicate-key failure"
    Expect.isEmpty disposed "the failed pass retired no row"
    Expect.isEmpty created "the failed pass created no row"
    write 2 ids
    Expect.sequenceEqual proj.Keys ids "the original keys"
    Expect.isEmpty created "every row survived the failed pass"
    Expect.isEmpty disposed "no row was retired"

    for key in ids do
        Expect.equal (proj.Get key) (20000 + key) $"row {key} reads its new box"

/// <summary>
/// A seeded pseudo-random sequence (MINSTD). Produces identical sequences on .NET and Fable. <c>seed</c> must be
/// positive.
/// </summary>
type private Lcg(seed: int) =
    let mutable state = float seed

    /// <summary>The next value in <c>[0, bound)</c>; 0 without drawing when <c>bound</c> is 0.</summary>
    member _.Next(bound: int) =
        if bound = 0 then
            0
        else
            state <- (state * 48271.0) % 2147483647.0
            int (state % float bound)

/// <summary>One projection of the differential test, read through closures so that every kind has one shape.</summary>
type private Side =
    {
        Keys: unit -> int[]
        Row: int -> string
        StatusOf: unit -> Status
        ErrorOf: unit -> exn
        Reader: ProjectionReader<int>
        Created: ResizeArray<int>
        Disposed: ResizeArray<int>
    }

/// <summary>
/// A projection of <c>kind</c> (0: createProjectionWith, 1: createProjectionWithOptions CreateFirst, 2:
/// createProjectionOnce CreateFirst) over <c>items</c>. With <c>full</c>, every pass runs in full. The factory raises for
/// an Id of 10000 or more.
/// </summary>
let private side (kind: int) (full: bool) (unused: Signal<int>) (items: Signal<Item[]>) =
    let created = ResizeArray<int>()
    let disposed = ResizeArray<int>()

    let keyOf (item: Item) =
        if full then
            unused.Value |> ignore

        item.Id

    let enter (item: unit -> Item) =
        let id = (item ()).Id
        created.Add id
        onCleanup (fun () -> disposed.Add id)

        if id >= 10000 then
            failwith "factory failed"

    let source () =
        items.Value :> seq<_>

    let wrap (proj: Projection<int, 'V>) (read: 'V -> string) =
        let reader = proj.NewKeyReader ()

        createEffect (fun () ->
            try
                for key in proj.Keys do
                    proj.Get key |> ignore
            with _ ->
                ())

        {
            Keys = (fun () -> proj.Keys)
            Row = (fun key -> read (proj.Get key))
            StatusOf = (fun () -> proj.Status)
            ErrorOf = (fun () -> proj.Error)
            Reader = reader
            Created = created
            Disposed = disposed
        }

    let reader (item: unit -> Item) =
        enter item
        fun () -> (item ()).Text

    match kind with
    | 0 -> wrap (createProjectionWith keyOf reader source) id
    | 1 -> wrap (createProjectionWithOptions ProjectionOptions.CreateFirst keyOf reader source) id
    | _ -> wrap (createProjectionOnce ProjectionOptions.CreateFirst keyOf reader source) (fun read -> read ())

/// <summary>
/// Everything the differential test compares after an operation; <c>disposedFrom</c> starts the operation's disposals.
/// A failure compares by its message, or as "duplicate key" when it is the duplicate-key failure.
/// </summary>
let private observe (s: Side) (disposedFrom: int) =
    let name (ex: exn) =
        if ex.Message.Contains "twice in one pass" then
            "duplicate key"
        else
            ex.Message

    let keys =
        try
            Ok (List.ofArray (s.Keys ()))
        with ex ->
            Error (name ex)

    let rows =
        match keys with
        | Ok list ->
            list
            |> List.map (fun key ->
                key,
                (try
                    Ok (s.Row key)
                 with ex ->
                     Error (name ex)))
        | Error _ -> []

    let delta =
        try
            let d = s.Reader.Read ()
            Ok (set [ for kv in d.Changes -> kv.Key, kv.Value ], d.IsReset)
        with ex ->
            Error (name ex)

    let error = s.ErrorOf ()

    let failure =
        if isNull error then
            None
        else
            Some (error.Message.Contains "twice in one pass")

    let disposed =
        s.Disposed
        |> Seq.skip disposedFrom
        |> Seq.sort
        |> List.ofSeq

    keys, rows, List.ofSeq s.Created, disposed, delta, s.StatusOf (), failure

/// <summary>The next list of the differential test: operation <c>op</c> applied to <c>items</c>.</summary>
let private edit (rng: Lcg) (fresh: unit -> int) (op: int) (items: Item[]) =
    let n = items.Length

    let item () =
        let id = fresh () in { Id = id; Text = string id }

    let next =
        match op with
        | 0 when n >= 2 ->
            let copy = Array.copy items
            let i = rng.Next n
            let j = rng.Next n
            let t = copy[i]
            copy[i] <- copy[j]
            copy[j] <- t
            copy
        | 1 when n >= 1 ->
            let i = rng.Next n
            let rest = Array.removeAt i items
            Array.insertAt (rng.Next (rest.Length + 1)) items[i] rest
        | 2 -> Array.insertAt (rng.Next (n + 1)) (item ()) items
        | 3 when n >= 1 -> Array.removeAt (rng.Next n) items
        | 4 when n >= 1 ->
            let i = rng.Next n

            Array.updateAt
                i
                { items[i] with
                    Text = items[i].Text + "'"
                }
                items
        | 5 when n >= 1 -> Array.updateAt (rng.Next n) (item ()) items
        | 6 -> Array.append items (Array.init (1 + rng.Next 4) (fun _ -> item ()))
        | 7 -> Array.empty
        | 8 -> Array.rev items
        | 10 when n >= 1 ->
            let copy = { items[rng.Next n] with Text = "dup" }

            match rng.Next 4 with
            | 0 -> Array.updateAt (rng.Next n) copy items
            | 1 -> Array.insertAt (rng.Next (n + 1)) copy items
            | 2 ->
                // A key repeated within a reordered middle.
                Array.updateAt (rng.Next n) copy (Array.rev items)
            | _ ->
                let fresh = item ()
                let once = Array.insertAt (rng.Next (n + 1)) fresh items
                Array.insertAt (rng.Next (n + 2)) { fresh with Text = "dup" } once
        | 11 ->
            let id = 10000 + fresh ()
            Array.insertAt (rng.Next (n + 1)) { Id = id; Text = string id } items
        | _ -> Array.copy items

    Array.truncate 40 next

[<Tests>]
let tests =
    testList
        "Trimmed pass"
        [
            test "list, lazy seq and array sources give the same keys and rows" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 3; 1; 2 ]
                let fromList = createProjection id (fun x -> x * 10) (fun () -> items.Value)

                let fromSeq =
                    createProjection id (fun x -> x * 10) (fun () -> seq { for x in items.Value -> x })

                let fromArray =
                    createProjection id (fun x -> x * 10) (fun () -> List.toArray items.Value :> seq<_>)

                let read (p: Projection<int, int>) =
                    p.Keys |> Array.map (fun k -> k, p.Get k)

                for next in [ [ 1; 2; 3 ]; [ 2 ]; []; [ 4; 2; 9 ] ] do
                    items.Value <- next
                    Expect.equal (read fromSeq) (read fromList) $"seq after {next}"
                    Expect.equal (read fromArray) (read fromList) $"array after {next}"
            }

            test "an array mutated in place and republished is compared against the previous pass's copy" {
                use g = new Graph ()
                use _ = g.Activate ()
                let array = [| "a"; "b"; "c" |]
                let tick = createSignal 0

                let proj =
                    createProjection id id (fun () ->
                        tick.Value |> ignore
                        array :> seq<_>)

                Expect.sequenceEqual proj.Keys [ "a"; "b"; "c" ] "first pass"
                tick.Value <- 1
                Expect.sequenceEqual proj.Keys [ "a"; "b"; "c" ] "an unchanged republish"
                array[1] <- "x"
                tick.Value <- 2
                Expect.sequenceEqual proj.Keys [ "a"; "x"; "c" ] "the in-place change is seen"
            }

            test "an int array source reads by index" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [| 1; 2; 3; 4 |]
                let proj = createProjection id (fun x -> x + 100) (fun () -> items.Value :> seq<_>)
                Expect.sequenceEqual proj.Keys [ 1; 2; 3; 4 ] "first pass"
                items.Value <- [| 1; 3; 2; 4 |]
                Expect.sequenceEqual proj.Keys [ 1; 3; 2; 4 ] "swapped"
                items.Value <- [| 3; 1 |]
                Expect.sequenceEqual proj.Keys [ 3; 1 ] "shrunk"
                Expect.equal (proj.Get 1) 101 "row"
            }

            test "a lazy source is enumerated to completion before keyOf runs" {
                use g = new Graph ()
                use _ = g.Activate ()
                let log = ResizeArray<string>()

                let proj =
                    createProjection
                        (fun x ->
                            log.Add ("key " + string x)
                            x)
                        id
                        (fun () ->
                            seq {
                                for x in [ 1; 2 ] do
                                    log.Add ("item " + string x)
                                    yield x
                            })

                proj.Keys |> ignore
                Expect.sequenceEqual log [ "item 1"; "item 2"; "key 1"; "key 2" ] "source first, then keyOf"
            }

            test "a nested pass failing in its source leaves the projection failed with that failure" {
                use g = new Graph ()
                use _ = g.Activate ()
                let tick = createSignal 0
                let mutable failing = false
                let mutable fired = false
                let mutable proj: Projection<int, int> = Unchecked.defaultof<_>

                proj <-
                    createProjection
                        (fun x ->
                            if x = 2 && not fired then
                                fired <- true
                                failing <- true
                                tick.Value <- 1

                                try
                                    proj.Keys |> ignore
                                with _ ->
                                    ()

                                failing <- false

                            x)
                        (fun x -> x * 10)
                        (fun () ->
                            tick.Value |> ignore

                            if failing then
                                failwith "source failed"

                            [| 1; 2; 3; 4 |] :> seq<_>)

                let raised =
                    try
                        proj.Keys |> ignore
                        null
                    with ex ->
                        ex

                Expect.isFalse (isNull raised) "the read raises the nested pass's failure"
                Expect.equal raised.Message "source failed" "the source's exception"
                Expect.equal proj.Status Status.Error "the projection reports the failure"
                Expect.isFalse (isNull proj.Error) "and records it"
                Expect.equal proj.Error.Message "source failed" "as the source's exception"

                tick.Value <- 2
                let keys = proj.Keys
                Expect.sequenceEqual keys [ 1; 2; 3; 4 ] "every key once the source succeeds"
                Expect.equal (keys |> Array.map proj.Get) [| 10; 20; 30; 40 |] "and every row"
            }

            test "a nested pass suspending in its source leaves the projection pending" {
                for createFirst in [ false; true ] do
                    let label = if createFirst then "create-first" else "default"
                    use g = new Graph ()
                    use _ = g.Activate ()
                    let tick = createSignal 0
                    let flight = createAsyncSource<int>()
                    let mutable waiting = false
                    let mutable fired = false
                    let mutable proj: Projection<int, int> = Unchecked.defaultof<_>

                    let keyOf x =
                        if x = 2 && not fired then
                            fired <- true
                            waiting <- true
                            tick.Value <- 1

                            try
                                proj.Keys |> ignore
                            with _ ->
                                ()

                        x

                    let factory (item: unit -> int) =
                        fun () -> item () * 10

                    let source () =
                        tick.Value |> ignore

                        if waiting then
                            [| flight.Value |] :> seq<_>
                        else
                            [| 1; 2; 3 |] :> seq<_>

                    proj <-
                        if createFirst then
                            createProjectionWithOptions ProjectionOptions.CreateFirst keyOf factory source
                        else
                            createProjectionWith keyOf factory source

                    Expect.throwsT<NotReadyException> (fun () -> proj.Keys |> ignore) $"{label}: the read raises the nested pass's suspension"

                    Expect.equal proj.Status Status.Pending $"{label}: the projection reports the suspension"
                    flight.Settle 5
                    Expect.sequenceEqual proj.Keys [ 5 ] $"{label}: the keys of the settled source"
                    Expect.equal (proj.Get 5) 50 $"{label}: and its row"
            }

            test "a keyOf that writes the source on its first four calls and reads the projection once returns, and later reads converge" {
                use g = new Graph ()
                use _ = g.Activate ()
                let counter = createSignal 0
                let items = createSignal [| 1; 2 |]
                let mutable guard = false
                let mutable proj: Projection<int, int> = Unchecked.defaultof<_>

                proj <-
                    createProjection
                        (fun x ->
                            if x = 1 && counter.Peek < 4 then
                                counter.Value <- counter.Value + 1

                                if not guard then
                                    guard <- true

                                    try
                                        untrack (fun () -> proj.Keys) |> ignore
                                    with _ ->
                                        ()

                                    guard <- false

                            x)
                        (fun x -> x * 10)
                        (fun () ->
                            counter.Value |> ignore
                            items.Value :> seq<_>)

                Expect.sequenceEqual proj.Keys [ 1; 2 ] "the first read returns"
                let first = proj.Runs
                Expect.sequenceEqual proj.Keys [ 1; 2 ] "the second read"
                Expect.isGreaterThan proj.Runs first "the projection was left dirty, and the next read ran a fresh pass"
                let mutable reads = 0
                let mutable before = -1

                while reads < 10 && before <> proj.Runs do
                    before <- proj.Runs
                    Expect.sequenceEqual proj.Keys [ 1; 2 ] $"read {reads}"
                    reads <- reads + 1

                Expect.equal before proj.Runs "a later read runs no pass"
                Expect.sequenceEqual (proj.Keys |> Array.map proj.Get) [ 10; 20 ] "every key has a row"
            }

            test "a create-first factory that raises after a nested pass kept its key leaves that failure on the row" {
                for memoFree in [ false; true ] do
                    let label =
                        if memoFree then
                            "createProjectionOnce"
                        else
                            "createProjectionWithOptions"

                    use g = new Graph ()
                    use _ = g.Activate ()
                    let items = createSignal [| 1 |]
                    let mutable armed = false
                    let mutable proj: Projection<int, unit -> int> = Unchecked.defaultof<_>

                    // Key 2's factory writes a source that keeps key 2, reads the projection, then raises.
                    let factory (item: unit -> int) =
                        let x = item ()

                        if x = 2 && armed then
                            armed <- false
                            items.Value <- [| 1; 2 |]

                            try
                                untrack (fun () -> proj.Keys) |> ignore
                            with _ ->
                                ()

                            failwith "factory 2 failed"

                        fun () -> item () * 10

                    let source () =
                        items.Value :> seq<_>

                    proj <-
                        if memoFree then
                            createProjectionOnce ProjectionOptions.CreateFirst id factory source
                        else
                            createProjectionWithOptions
                                ProjectionOptions.CreateFirst
                                id
                                (fun item ->
                                    let read = factory item
                                    fun () -> read)
                                source

                    proj.Keys |> ignore
                    armed <- true
                    items.Value <- [| 1; 2; 4 |]
                    Expect.sequenceEqual proj.Keys [ 1; 2 ] $"{label}: the keys of the source the factory wrote"
                    Expect.equal ((proj.Get 1) ()) 10 $"{label}: row 1"

                    let raised =
                        try
                            proj.Get 2 |> ignore
                            ""
                        with ex ->
                            ex.Message

                    Expect.equal raised "factory 2 failed" $"{label}: row 2 raises the factory's failure"
            }

            test "a row read while its factory runs raises naming the key" {
                for createFirst in [ false; true ] do
                    let label = if createFirst then "create-first" else "default"
                    use g = new Graph ()
                    use _ = g.Activate ()
                    let items = createSignal [| 1 |]
                    let mutable armed = false
                    let inner = ref ""
                    let mutable proj: Projection<int, int> = Unchecked.defaultof<_>

                    let factory (item: unit -> int) =
                        let x = item ()

                        if x = 2 && armed then
                            armed <- false
                            items.Value <- [| 1; 2 |]

                            inner.Value <-
                                try
                                    untrack (fun () -> proj.Get 2) |> ignore
                                    ""
                                with ex ->
                                    ex.Message

                        fun () -> item () * 10

                    let source () =
                        items.Value :> seq<_>

                    proj <-
                        if createFirst then
                            createProjectionWithOptions ProjectionOptions.CreateFirst id factory source
                        else
                            createProjectionWith id factory source

                    proj.Keys |> ignore
                    armed <- true
                    items.Value <- [| 1; 2; 4 |]
                    let keys = proj.Keys
                    Expect.equal inner.Value "The row of key 2 is still being created." $"{label}: the read inside the factory"
                    Expect.sequenceEqual keys [ 1; 2 ] $"{label}: the keys of the source the factory wrote"
                    Expect.sequenceEqual (keys |> Array.map proj.Get) [ 10; 20 ] $"{label}: the rows once created"
            }

            test "a keyOf that writes the source on every call and reads the projection once leaves every read returning" {
                use g = new Graph ()
                use _ = g.Activate ()
                let counter = createSignal 0
                let items = createSignal [| 1; 2 |]
                let mutable guard = false
                let mutable proj: Projection<int, int> = Unchecked.defaultof<_>

                proj <-
                    createProjection
                        (fun x ->
                            if x = 1 then
                                counter.Value <- counter.Value + 1

                                if not guard then
                                    guard <- true

                                    try
                                        untrack (fun () -> proj.Keys) |> ignore
                                    with _ ->
                                        ()

                                    guard <- false

                            x)
                        (fun x -> x * 10)
                        (fun () ->
                            counter.Value |> ignore
                            items.Value :> seq<_>)

                for read in 1..3 do
                    Expect.sequenceEqual proj.Keys [ 1; 2 ] $"read {read} returns"

                Expect.sequenceEqual (proj.Keys |> Array.map proj.Get) [ 10; 20 ] "every key has a row"
            }

            test "an effect's first read of a projection whose keyOf writes the source ends with the latest keys" {
                for nested in [ false; true ] do
                    let label = if nested then "nested" else "not nested"
                    use g = new Graph ()
                    use _ = g.Activate ()
                    let items = createSignal [| 1; 2 |]
                    let mutable stage = 0
                    let mutable proj: Projection<int, int> = Unchecked.defaultof<_>

                    // The first pass's keyOf writes the source; when nested, it reads the projection, and the nested
                    // pass's keyOf writes the source again without reading it.
                    proj <-
                        createProjection
                            (fun x ->
                                if x = 1 || x = 9 then
                                    if stage = 0 then
                                        stage <- 1

                                        if nested then
                                            items.Value <- [| 9; 1; 2; 3 |]

                                            try
                                                untrack (fun () -> proj.Keys) |> ignore
                                            with _ ->
                                                ()
                                        else
                                            items.Value <- [| 9; 1; 2; 3; 4 |]
                                    elif stage = 1 && nested then
                                        stage <- 2
                                        items.Value <- [| 9; 1; 2; 3; 4 |]

                                x)
                            (fun x -> x * 10)
                            (fun () -> items.Value :> seq<_>)

                    let seen = ResizeArray<int[]>()
                    createEffect (fun () -> seen.Add proj.Keys)
                    Expect.sequenceEqual (Seq.last seen) [ 9; 1; 2; 3; 4 ] $"{label}: the effect's last keys"
            }

            test "a memo-free row read while its factory runs raises naming the key" {
                for createFirst in [ false; true ] do
                    let label = if createFirst then "create-first" else "default"
                    use g = new Graph ()
                    use _ = g.Activate ()
                    let items = createSignal [| 1 |]
                    let mutable armed = false
                    let inner = ref ""
                    let mutable proj: Projection<int, int> = Unchecked.defaultof<_>

                    let factory (item: unit -> int) =
                        let x = item ()

                        if x = 2 && armed then
                            armed <- false
                            items.Value <- [| 1; 2 |]

                            inner.Value <-
                                try
                                    untrack (fun () -> proj.Get 2) |> ignore
                                    ""
                                with ex ->
                                    ex.Message

                        x * 10

                    let options =
                        if createFirst then
                            ProjectionOptions.CreateFirst
                        else
                            ProjectionOptions.None

                    proj <- createProjectionOnce options id factory (fun () -> items.Value :> seq<_>)
                    proj.Keys |> ignore
                    armed <- true
                    items.Value <- [| 1; 2; 4 |]
                    let keys = proj.Keys
                    Expect.equal inner.Value "The row of key 2 is still being created." $"{label}: the read inside the factory"
                    Expect.sequenceEqual keys [ 1; 2 ] $"{label}: the keys of the source the factory wrote"
                    Expect.sequenceEqual (keys |> Array.map proj.Get) [ 10; 20 ] $"{label}: the rows once created"
            }

            test "a swap of two items calls keyOf twice and creates no row" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items, keyCalls, created, _, proj = counted 1000
                keyCalls.Value <- 0
                created.Clear ()
                let swapped = Array.copy items.Value
                let first = swapped[1]
                swapped[1] <- swapped[998]
                swapped[998] <- first
                items.Value <- swapped
                Expect.equal keyCalls.Value 2 "keyOf runs for the two moved positions"
                Expect.isEmpty created "no row is created"
                Expect.equal proj.Keys[1] 999 "the keys moved"
                Expect.equal proj.Keys[998] 2 "the keys moved"
            }

            test "removing one item calls keyOf zero times and disposes its row" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items, keyCalls, _, disposed, proj = counted 1000
                keyCalls.Value <- 0
                items.Value <- Array.removeAt 3 items.Value
                Expect.equal keyCalls.Value 0 "prefix and suffix cover every kept item"
                Expect.sequenceEqual disposed [ 4 ] "the removed row is disposed"
                Expect.equal proj.Keys.Length 999 "one key fewer"
            }

            test "prepending one item calls keyOf once" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items, keyCalls, created, _, proj = counted 1000
                Expect.sequenceEqual created [ 1..1000 ] "the first pass created every row"
                keyCalls.Value <- 0
                created.Clear ()
                items.Value <- Array.append [| { Id = 5000; Text = "new" } |] items.Value
                Expect.equal keyCalls.Value 1 "only the new item"
                Expect.sequenceEqual created [ 5000 ] "one row created"
                Expect.equal proj.Keys[0] 5000 "the new key leads"
            }

            test "appending one item calls keyOf once" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items, keyCalls, created, _, proj = counted 1000
                keyCalls.Value <- 0
                created.Clear ()
                items.Value <- Array.append items.Value [| { Id = 5000; Text = "new" } |]
                Expect.equal keyCalls.Value 1 "only the new item"
                Expect.sequenceEqual created [ 5000 ] "one row created"
                Expect.equal proj.Keys[1000] 5000 "the new key trails"
            }

            test "replacing one item with an equal-key copy calls keyOf once and updates that row" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items, keyCalls, created, _, proj = counted 1000
                keyCalls.Value <- 0
                created.Clear ()
                items.Value <- Array.updateAt 500 { Id = 501; Text = "changed" } items.Value
                Expect.equal keyCalls.Value 1 "only the replaced item"
                Expect.isEmpty created "the row survives"
                Expect.equal (proj.Get 501) "changed" "the row reads the new item"
            }

            test "an int array swap calls keyOf twice" {
                use g = new Graph ()
                use _ = g.Activate ()
                let calls = ref 0
                let items = createSignal (Array.init 100 id)

                let proj =
                    createProjection
                        (fun (x: int) ->
                            calls.Value <- calls.Value + 1
                            x)
                        id
                        (fun () -> items.Value :> seq<_>)

                proj.Keys |> ignore
                calls.Value <- 0
                let swapped = Array.copy items.Value
                swapped[10] <- 90
                swapped[90] <- 10
                items.Value <- swapped
                Expect.equal proj.Keys[10] 90 "swapped"
                Expect.equal calls.Value 2 "two changed positions"
            }

            test "a swap of the first and last items calls keyOf twice" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items, keyCalls, created, _, proj = counted 1000
                keyCalls.Value <- 0
                created.Clear ()
                let swapped = Array.copy items.Value
                swapped[0] <- items.Value[999]
                swapped[999] <- items.Value[0]
                items.Value <- swapped
                Expect.equal keyCalls.Value 2 "keyOf runs for the two moved positions"
                Expect.isEmpty created "no row is created"
                Expect.equal proj.Keys[0] 1000 "the keys moved"
                Expect.equal proj.Keys[999] 1 "the keys moved"
            }

            test "a pass over new items calls keyOf once per item" {
                use g = new Graph ()
                use _ = g.Activate ()
                let calls = ref 0
                let items = createSignal (Array.init 1000 (fun i -> Box (i, i)))

                let proj =
                    createProjection
                        (fun (item: Box) ->
                            calls.Value <- calls.Value + 1
                            item.Id)
                        (fun item -> item.Value)
                        (fun () -> items.Value :> seq<_>)

                proj.Keys |> ignore

                for edit in [ Array.removeAt 500; Array.insertAt 0 5000; Array.rev ] do
                    calls.Value <- 0
                    let next = edit (proj.Keys |> Array.copy)
                    items.Value <- next |> Array.map (fun id -> Box (id, id))
                    Expect.sequenceEqual proj.Keys next "the keys"
                    Expect.equal calls.Value next.Length "keyOf runs once per item"
            }

            test "a pass over new items hashes its keys as often as a full pass" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal (Array.init 1000 (fun i -> Box (i + 1, i + 1)))
                let tracked = createSignal 0
                let hashes = ref 0
                let fullHashes = ref 0

                let projection (keyOf: Box -> HashedKey) =
                    createProjection keyOf (fun item -> item.Value) (fun () -> items.Value :> seq<_>)

                let proj = projection (fun item -> HashedKey (item.Id, hashes))

                // A keyOf that reads a tracked source turns trimming off.
                let full =
                    projection (fun item ->
                        tracked.Value |> ignore
                        HashedKey (item.Id, fullHashes))

                proj.Keys |> ignore
                full.Keys |> ignore

                for edit in [ Array.removeAt 500; Array.insertAt 0 5000; Array.rev; id ] do
                    let next = edit (proj.Keys |> Array.map (fun k -> k.Id))
                    hashes.Value <- 0
                    fullHashes.Value <- 0
                    items.Value <- next |> Array.map (fun id -> Box (id, id))
                    Expect.sequenceEqual (proj.Keys |> Array.map (fun k -> k.Id)) next "the keys"
                    Expect.sequenceEqual (full.Keys |> Array.map (fun k -> k.Id)) next "the full pass's keys"
                    Expect.equal hashes.Value fullHashes.Value "key hashes against the full pass"
            }

            test "every swap of the same two items hashes its keys as often" {
                use g = new Graph ()
                use _ = g.Activate ()
                let hashes = ref 0
                let items = createSignal (Array.init 100 (fun i -> HashedKey (i + 1, hashes)))

                let proj =
                    createProjection id (fun (key: HashedKey) -> key.Id) (fun () -> items.Value :> seq<_>)

                proj.Keys |> ignore

                let swap () =
                    let swapped = Array.copy items.Value
                    let first = swapped[10]
                    swapped[10] <- swapped[90]
                    swapped[90] <- first
                    hashes.Value <- 0
                    items.Value <- swapped
                    proj.Keys |> ignore
                    hashes.Value

                let first = swap ()

                for pass in 2..5 do
                    Expect.equal (swap ()) first $"swap {pass}: key hashes against the first swap"
            }

            test "new items prepended with one new key keep every other row" { freshAfter (Array.insertAt 0 5000) }

            test "new items with the middle key removed keep every other row" { freshAfter (Array.removeAt 500) }

            test "new items with the second and second-to-last keys swapped keep every row" {
                freshAfter (fun ids ->
                    let swapped = Array.copy ids
                    swapped[1] <- ids[ids.Length - 2]
                    swapped[ids.Length - 2] <- ids[1]
                    swapped)
            }

            test "new items in reverse order keep every row" { freshAfter Array.rev }

            test "an empty list of new items disposes every row" { freshAfter (fun _ -> [||]) }

            test "new items of equal length repeating a key fail the pass" { freshDuplicate (Array.updateAt 5 1) }

            test "new items of a new length repeating a key fail the pass" { freshDuplicate (Array.insertAt 4 2) }

            test "a changed key equal to a prefix key fails the pass" { duplicateAfter (Array.updateAt 5 { Id = 1; Text = "dup" }) }

            test "a changed key equal to a suffix key fails the pass" { duplicateAfter (Array.updateAt 2 { Id = 10; Text = "dup" }) }

            test "a changed key equal to a key kept at an unchanged middle position fails the pass" {
                // Equal lengths: positions 2 and 7 change; position 4 keeps its item with key 5.
                duplicateAfter (fun a ->
                    a
                    |> Array.updateAt 2 { Id = 100; Text = "x" }
                    |> Array.updateAt 7 { Id = 5; Text = "dup" })
            }

            test "two changed positions with one key fail the pass" {
                duplicateAfter (fun a ->
                    a
                    |> Array.updateAt 3 { Id = 200; Text = "x" }
                    |> Array.updateAt 6 { Id = 200; Text = "y" })
            }

            test "a length change whose new middle repeats a prefix key fails the pass" { duplicateAfter (Array.insertAt 4 { Id = 2; Text = "dup" }) }

            test "an item mutated in place at the same position keeps its key" {
                use g = new Graph ()
                use _ = g.Activate ()
                let cell = ref 1
                let a = box cell
                let items = createSignal [| a |]

                let proj =
                    createProjection (fun (o: obj) -> (o :?> int ref).Value) id (fun () -> items.Value :> seq<_>)

                Expect.sequenceEqual proj.Keys [ 1 ] "first key"
                cell.Value <- 2
                items.Value <- [| a |]
                Expect.sequenceEqual proj.Keys [ 1 ] "the same item keeps its key"
            }

            test "a keyOf that reads a signal for one item keeps updating" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flag = createSignal 0
                let items = createSignal [| 1; 2; 3; 7 |]

                let proj =
                    createProjection (fun x -> if x = 7 then 70 + flag.Value else x) id (fun () -> items.Value :> seq<_>)

                Expect.sequenceEqual proj.Keys [ 1; 2; 3; 70 ] "first pass"
                flag.Value <- 1
                Expect.sequenceEqual proj.Keys [ 1; 2; 3; 71 ] "the key follows the signal"
                items.Value <- Array.copy items.Value
                flag.Value <- 2
                Expect.sequenceEqual proj.Keys [ 1; 2; 3; 72 ] "still after an unchanged republish"
            }

            test "a keyOf that reads a signal for an item entering later turns trimming off" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flag = createSignal 0
                let items = createSignal [| 1; 2; 3 |]

                let proj =
                    createProjection (fun x -> if x = 7 then 70 + flag.Value else x) id (fun () -> items.Value :> seq<_>)

                Expect.sequenceEqual proj.Keys [ 1; 2; 3 ] "first pass"
                items.Value <- Array.copy items.Value
                items.Value <- [| 1; 2; 3; 7 |]
                Expect.sequenceEqual proj.Keys [ 1; 2; 3; 70 ] "7 enters"
                items.Value <- Array.copy items.Value
                flag.Value <- 5
                Expect.sequenceEqual proj.Keys [ 1; 2; 3; 75 ] "the key follows the signal"
            }

            test "a keyOf that registers a cleanup keeps registering it every pass" {
                use g = new Graph ()
                use _ = g.Activate ()
                let cleanups = ref 0
                let items = createSignal [| 1; 2; 3 |]

                let proj =
                    createProjection
                        (fun x ->
                            onCleanup (fun () -> cleanups.Value <- cleanups.Value + 1)
                            x)
                        id
                        (fun () -> items.Value :> seq<_>)

                proj.Keys |> ignore
                items.Value <- Array.copy items.Value
                proj.Keys |> ignore
                Expect.equal cleanups.Value 3 "the first pass's three cleanups ran"
                items.Value <- Array.copy items.Value
                proj.Keys |> ignore
                Expect.equal cleanups.Value 6 "the second pass registered three again"
            }

            test "a caller reversing the Keys array in place does not change the next pass" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items, _, _, disposed, proj = counted 3
                let keys = proj.Keys
                Array.blit (Array.rev keys) 0 keys 0 keys.Length
                items.Value <- Array.take 2 items.Value
                Expect.sequenceEqual disposed [ 3 ] "the removed key is the last item's"
                Expect.equal (proj.Get 1) "1" "row 1 survives"
            }

            test "a create-first factory that writes the source and reads a sibling row matches a full pass" {
                // Run the same script on a trimmed projection and on one whose keyOf reads an unused signal.
                let script (forceFull: bool) =
                    use g = new Graph ()
                    use _ = g.Activate ()
                    let unused = createSignal 0
                    let items = createSignal [| 1; 2 |]
                    let mutable proj: Projection<int, int> = Unchecked.defaultof<_>

                    proj <-
                        createProjectionWithOptions
                            ProjectionOptions.CreateFirst
                            (fun x ->
                                if forceFull then
                                    unused.Value |> ignore

                                x)
                            (fun item ->
                                if item () = 3 then
                                    items.Value <- [| 1; 2; 3; 4 |]
                                    proj.TryGet 1 |> ignore

                                fun () -> item () * 10)
                            (fun () -> items.Value :> seq<_>)

                    proj.Keys |> ignore
                    items.Value <- Array.copy items.Value
                    proj.Keys |> ignore
                    items.Value <- [| 1; 2; 3 |]

                    let result =
                        try
                            proj.Keys
                            |> Array.map (fun k -> k, proj.Get k)
                            |> Array.toList
                            |> Ok
                        with ex ->
                            Error ex.Message

                    result, proj.Status

                Expect.equal (script false) (script true) "trimmed and full passes agree"
            }

            test "random edits give a trimmed pass the same results as a full pass" {
                let names =
                    [|
                        "swap"
                        "move"
                        "insert"
                        "remove"
                        "equal-key copy"
                        "new key"
                        "append"
                        "clear"
                        "reverse"
                        "republish"
                        "duplicate"
                        "failing factory"
                    |]

                for seed in 1..20 do
                    use g = new Graph ()
                    use _ = g.Activate ()
                    let kind = (seed - 1) % 3
                    let rng = Lcg seed
                    let counter = ref 100

                    let fresh () =
                        counter.Value <- counter.Value + 1
                        counter.Value

                    let unused = createSignal 0

                    let mutable good =
                        Array.init (rng.Next 21) (fun i -> { Id = i + 1; Text = string (i + 1) })

                    let items = createSignal good
                    let trimmed = side kind false unused items
                    let full = side kind true unused items

                    let compare (label: string) (fromTrimmed: int) (fromFull: int) =
                        let keys, rows, created, disposed, delta, status, failure =
                            observe trimmed fromTrimmed

                        let keys', rows', created', disposed', delta', status', failure' =
                            observe full fromFull

                        Expect.equal keys keys' $"{label}: keys"
                        Expect.equal rows rows' $"{label}: rows"
                        Expect.equal created created' $"{label}: created"
                        Expect.equal disposed disposed' $"{label}: disposed"
                        Expect.equal delta delta' $"{label}: key reader"
                        Expect.equal status status' $"{label}: status"
                        Expect.equal failure failure' $"{label}: failure"
                        keys

                    compare $"seed {seed} kind {kind} first pass" 0 0
                    |> ignore

                    let ops = if kind = 0 then names.Length - 1 else names.Length

                    for i in 0..299 do
                        let op = rng.Next ops
                        let next = edit rng fresh op good
                        let fromTrimmed = trimmed.Disposed.Count
                        let fromFull = full.Disposed.Count
                        items.Value <- next

                        match compare $"seed {seed} kind {kind} op {i} ({names[op]})" fromTrimmed fromFull with
                        | Ok _ -> good <- next
                        | Error _ -> ()
            }

            test "a keyOf that writes the source and reads the projection matches a full pass" {
                // keyOf for 5, the last changed position, writes the source and reads the projection mid-pass.
                let script (forceFull: bool) =
                    use g = new Graph ()
                    use _ = g.Activate ()
                    let unused = createSignal 0
                    let items = createSignal [| 1; 2 |]
                    let mutable fired = false
                    let mutable proj: Projection<int, int> = Unchecked.defaultof<_>

                    proj <-
                        createProjection
                            (fun x ->
                                if forceFull then
                                    unused.Value |> ignore

                                if x = 5 && not fired then
                                    fired <- true
                                    items.Value <- [| 1; 2; 3; 4 |]

                                    try
                                        proj.Keys |> ignore
                                    with _ ->
                                        ()

                                x)
                            (fun x -> x * 10)
                            (fun () -> items.Value :> seq<_>)

                    proj.Keys |> ignore
                    items.Value <- Array.copy items.Value
                    proj.Keys |> ignore
                    items.Value <- [| 1; 2; 3; 5 |]

                    let read () =
                        try
                            proj.Keys
                            |> Array.map (fun k -> k, proj.Get k)
                            |> Array.toList
                            |> Ok
                        with ex ->
                            Error ex.Message

                    let first = read ()
                    items.Value <- [| 1; 2 |]
                    first, read (), proj.Status

                Expect.equal (script false) (script true) "trimmed and full passes agree"
            }

            test "a pass nested in keyOf during a trimmed pass matches a full pass, and the next pass runs in full" {
                let script (createFirst: bool) (forceFull: bool) =
                    use g = new Graph ()
                    use _ = g.Activate ()
                    let unused = createSignal 0
                    let items = createSignal [| 1; 2; 3 |]
                    let keyCalls = ref 0
                    let mutable armed = false
                    let mutable proj: Projection<int, int> = Unchecked.defaultof<_>

                    let keyOf x =
                        keyCalls.Value <- keyCalls.Value + 1

                        if forceFull then
                            unused.Value |> ignore

                        if x = 9 && armed then
                            armed <- false
                            items.Value <- [| 1 |]

                            try
                                untrack (fun () -> proj.Keys) |> ignore
                            with _ ->
                                ()

                        x

                    let factory (item: unit -> int) =
                        fun () -> item () * 10

                    let source () =
                        items.Value :> seq<_>

                    proj <-
                        if createFirst then
                            createProjectionWithOptions ProjectionOptions.CreateFirst keyOf factory source
                        else
                            createProjectionWith keyOf factory source

                    let read () =
                        try
                            proj.Keys
                            |> Array.map (fun k ->
                                k,
                                (try
                                    Ok (proj.Get k)
                                 with ex ->
                                     Error ex.Message))
                            |> Array.toList
                            |> Ok
                        with ex ->
                            Error ex.Message

                    proj.Keys |> ignore
                    items.Value <- Array.copy items.Value
                    proj.Keys |> ignore
                    armed <- true
                    items.Value <- [| 1; 9; 3 |]
                    let nested = read (), proj.Status
                    // The items the nested-in pass read, republished.
                    keyCalls.Value <- 0
                    items.Value <- [| 1; 9; 3 |]
                    let after = read ()
                    nested, after, keyCalls.Value

                for createFirst in [ false; true ] do
                    let label = if createFirst then "create-first" else "default"
                    let (nested, after, calls) = script createFirst false
                    let (nested', after', _) = script createFirst true
                    Expect.equal nested (Ok [ 1, Ok 10 ], Status.None) $"{label}: the keys and rows of the source keyOf wrote"
                    Expect.equal nested nested' $"{label}: keys, rows and status after the nested pass"
                    Expect.equal after (Ok [ 1, Ok 10; 9, Ok 90; 3, Ok 30 ]) $"{label}: keys and rows on the next pass"
                    Expect.equal after after' $"{label}: the next pass matches a full pass"
                    Expect.equal calls 3 $"{label}: the next pass calls keyOf for every item"
            }

            test "a pending row stays pending through a swap and ends with its removal" {
                use g = new Graph ()
                use _ = g.Activate ()

                let items =
                    createSignal (Array.init 20 (fun i -> { Id = i + 1; Text = string (i + 1) }))

                let flight = createAsyncSource<int>()

                let proj =
                    createProjectionWith
                        (fun (item: Item) -> item.Id)
                        (fun item ->
                            let id = (item ()).Id
                            fun () -> if id = 3 then flight.Value else id)
                        (fun () -> items.Value :> seq<_>)

                createEffect (fun () ->
                    try
                        for key in proj.Keys do
                            proj.Get key |> ignore
                    with _ ->
                        ())

                items.Value <- Array.copy items.Value
                Expect.isTrue proj.AnyPending "the row is in flight"
                Expect.sequenceEqual proj.PendingKeys [| 3 |] "and listed"
                let swapped = Array.copy items.Value
                let t = swapped[10]
                swapped[10] <- swapped[15]
                swapped[15] <- t
                items.Value <- swapped
                Expect.sequenceEqual proj.PendingKeys [| 3 |] "still listed after a swap"
                items.Value <- Array.filter (fun item -> item.Id <> 3) swapped
                Expect.isFalse proj.AnyPending "nothing is pending once the row is removed"
            }

            test "a key reader sees the deltas of trimmed passes" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items, _, _, _, proj = counted 20
                let reader = proj.NewKeyReader ()
                reader.Read () |> ignore
                items.Value <- Array.copy items.Value

                let changes () =
                    set [ for kv in reader.Read().Changes -> kv.Key, kv.Value ]

                let swapped = Array.copy items.Value
                let t = swapped[2]
                swapped[2] <- swapped[12]
                swapped[12] <- t
                items.Value <- swapped
                Expect.equal (changes ()) Set.empty "a swap adds and removes nothing"
                items.Value <- Array.filter (fun item -> item.Id <> 4) swapped
                Expect.equal (changes ()) (set [ 4, KeyChange.Removed ]) "a removal"
                items.Value <- Array.insertAt 7 { Id = 500; Text = "500" } items.Value
                Expect.equal (changes ()) (set [ 500, KeyChange.Added ]) "an insert"
                let at = Array.findIndex (fun item -> item.Id = 6) items.Value
                items.Value <- Array.updateAt at { Id = 600; Text = "600" } items.Value

                Expect.equal (changes ()) (set [ 6, KeyChange.Removed; 600, KeyChange.Added ]) "a replacement at one position"
            }

            test "a create-first failure after a trimmed swap rolls back and the next pass recovers" {
                use g = new Graph ()
                use _ = g.Activate ()

                let items =
                    createSignal (Array.init 20 (fun i -> { Id = i + 1; Text = string (i + 1) }))

                let keyCalls = ref 0
                let runs = ref 0
                let created = ResizeArray<int>()
                let disposed = ResizeArray<int>()

                let proj =
                    createProjectionOnce
                        ProjectionOptions.CreateFirst
                        (fun (item: Item) ->
                            keyCalls.Value <- keyCalls.Value + 1
                            item.Id)
                        (fun item ->
                            let id = (item ()).Id
                            runs.Value <- runs.Value + 1
                            created.Add id
                            onCleanup (fun () -> disposed.Add id)

                            if id = 666 then
                                failwith "factory failed"

                            string id)
                        (fun () -> items.Value :> seq<_>)

                proj.Keys |> ignore
                items.Value <- Array.copy items.Value
                let swapped = Array.copy items.Value
                let t = swapped[1]
                swapped[1] <- swapped[18]
                swapped[18] <- t
                items.Value <- swapped
                Expect.equal proj.Keys.Length 20 "the swap applied"
                items.Value <- Array.insertAt 10 { Id = 666; Text = "666" } swapped
                Expect.throws (fun () -> proj.Keys |> ignore) "the failing factory fails the pass"
                let runsAfter = runs.Value
                Expect.throws (fun () -> proj.Keys |> ignore) "the failure is held"
                Expect.equal runs.Value runsAfter "no factory ran for the held failure"
                Expect.sequenceEqual disposed [ 666 ] "no previous row was disposed and the failed row did not leak"
                Expect.sequenceEqual (created |> Seq.filter (fun id -> id = 666)) [ 666 ] "666 was created once"
                items.Value <- Array.insertAt 10 { Id = 667; Text = "667" } swapped

                let expected =
                    swapped
                    |> Array.map (fun item -> item.Id)
                    |> Array.insertAt 10 667

                Expect.sequenceEqual proj.Keys expected "the retry applied"
                let again = Array.copy items.Value
                let u = again[3]
                again[3] <- again[5]
                again[5] <- u
                keyCalls.Value <- 0
                items.Value <- again
                proj.Keys |> ignore
                Expect.equal keyCalls.Value 2 "trimming resumed"
            }

            test "a held duplicate-key failure is followed by trimmed passes" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items, keyCalls, _, _, proj = counted 10
                items.Value <- Array.copy items.Value
                let original = items.Value
                items.Value <- Array.updateAt 5 { Id = 1; Text = "dup" } original
                Expect.throwsT<InvalidOperationException> (fun () -> proj.Keys |> ignore) "the pass fails"
                items.Value <- Array.removeAt 5 original
                Expect.sequenceEqual proj.Keys [ 1; 2; 3; 4; 5; 7; 8; 9; 10 ] "the removal recovers"
                keyCalls.Value <- 0
                items.Value <- Array.removeAt 2 items.Value
                Expect.equal proj.Keys.Length 8 "one key fewer"
                Expect.equal keyCalls.Value 0 "trimming resumed"
            }

            test "combinators over a trimmed upstream follow it" {
                use g = new Graph ()
                use _ = g.Activate ()

                let items =
                    createSignal (Array.init 12 (fun i -> { Id = i + 1; Text = string (i + 1) }))

                let up =
                    createProjection (fun (i: Item) -> i.Id) (fun i -> i.Id) (fun () -> items.Value :> seq<_>)

                let filtered = up |> Projection.filter (fun v -> v % 2 = 0)
                let sorted = up |> Projection.sortBy (fun v -> -v)
                let grouped = up |> Projection.groupBy (fun v -> v % 3)

                let check (label: string) =
                    let plain =
                        items.Value
                        |> Array.map (fun i -> i.Id)
                        |> List.ofArray

                    Expect.equal (List.ofArray filtered.Keys) (List.filter (fun v -> v % 2 = 0) plain) $"{label}: filter"
                    Expect.equal (List.ofArray sorted.Keys) (List.sortBy (fun v -> -v) plain) $"{label}: sortBy"
                    let expected = plain |> List.groupBy (fun v -> v % 3)

                    Expect.equal (List.sort (List.ofArray grouped.Keys)) (expected |> List.map fst |> List.sort) $"{label}: group keys"

                    for (gk, members) in expected do
                        Expect.equal (List.sort (List.ofArray (grouped.Get gk).Keys)) (List.sort members) $"{label}: members of {gk}"

                check "first pass"
                items.Value <- Array.copy items.Value
                check "republish"
                let swapped = Array.copy items.Value
                let t = swapped[2]
                swapped[2] <- swapped[9]
                swapped[9] <- t
                items.Value <- swapped
                check "swap"
                items.Value <- Array.removeAt 4 items.Value
                check "removal"
                items.Value <- Array.insertAt 3 { Id = 50; Text = "50" } items.Value
                check "insert"
                let at = Array.findIndex (fun i -> i.Id = 7) items.Value
                items.Value <- Array.updateAt at { Id = 7; Text = "7" } items.Value
                check "equal copy"
                let at = Array.findIndex (fun i -> i.Id = 8) items.Value
                items.Value <- Array.updateAt at { Id = 109; Text = "x" } items.Value
                check "replacement moves an item between groups"
            }

            test "a removed row's cleanup that disposes the projection during a trimmed pass leaves it empty" {
                use g = new Graph ()
                use _ = g.Activate ()

                let items =
                    createSignal (Array.init 10 (fun i -> { Id = i + 1; Text = string (i + 1) }))

                let mutable proj = Unchecked.defaultof<Projection<int, string>>

                proj <-
                    createProjectionWith
                        (fun (item: Item) -> item.Id)
                        (fun item ->
                            if (item ()).Id = 4 then
                                onCleanup (fun () -> proj.Dispose ())

                            fun () -> (item ()).Text)
                        (fun () -> items.Value :> seq<_>)

                createEffect (fun () -> proj.Keys |> ignore)
                items.Value <- Array.copy items.Value
                items.Value <- Array.filter (fun item -> item.Id <> 4) items.Value
                Expect.equal proj.Status Status.None "the pass did not fail"
                Expect.isNull proj.Error "and recorded no error"
                Expect.sequenceEqual proj.Keys [] "the projection reads empty"
            }

            test "a factory that runs a nested pass leaves the keys and rows of the latest source" {
                for createFirst in [ false; true ] do
                    let label = if createFirst then "create-first" else "default"
                    use g = new Graph ()
                    use _ = g.Activate ()
                    let items = createSignal [| 1 |]
                    let created = ResizeArray<int>()
                    let mutable proj: Projection<int, int> = Unchecked.defaultof<_>

                    // Key 2's factory writes the source and reads the projection inside the pass's diff.
                    let factory (item: unit -> int) =
                        let x = item ()
                        created.Add x

                        if x = 2 then
                            items.Value <- [| 1; 3 |]

                            try
                                untrack (fun () -> proj.Keys) |> ignore
                            with _ ->
                                ()

                        fun () -> item () * 10

                    let source () =
                        items.Value :> seq<_>

                    proj <-
                        if createFirst then
                            createProjectionWithOptions ProjectionOptions.CreateFirst id factory source
                        else
                            createProjectionWith id factory source

                    proj.Keys |> ignore
                    items.Value <- [| 1; 2; 4 |]
                    let keys = proj.Keys
                    Expect.equal proj.Status Status.None $"{label}: the nested pass succeeded"
                    Expect.sequenceEqual keys [ 1; 3 ] $"{label}: the keys of the source the factory wrote"
                    Expect.sequenceEqual (keys |> Array.map proj.Get) [ 10; 30 ] $"{label}: every key has a row"
                    Expect.equal (proj.TryGet 4) None $"{label}: the superseded pass created no row for 4"
                    Expect.sequenceEqual created [ 1; 2; 3 ] $"{label}: factories"
            }

            test "a cleanup that runs a nested pass leaves the keys and rows of the latest source" {
                for createFirst in [ false; true ] do
                    let label = if createFirst then "create-first" else "default"
                    use g = new Graph ()
                    use _ = g.Activate ()
                    let items = createSignal [| 1; 2 |]
                    let mutable armed = false
                    let mutable proj: Projection<int, int> = Unchecked.defaultof<_>

                    // keyOf for 5 writes the source, and a removed row's cleanup reads the projection inside the diff.
                    let keyOf x =
                        if x = 5 && armed then
                            items.Value <- [| 7 |]

                        x

                    let factory (item: unit -> int) =
                        onCleanup (fun () ->
                            if armed then
                                armed <- false

                                try
                                    untrack (fun () -> proj.Keys) |> ignore
                                with _ ->
                                    ())

                        fun () -> item () * 10

                    let source () =
                        items.Value :> seq<_>

                    proj <-
                        if createFirst then
                            createProjectionWithOptions ProjectionOptions.CreateFirst keyOf factory source
                        else
                            createProjectionWith keyOf factory source

                    proj.Keys |> ignore
                    armed <- true
                    items.Value <- [| 5 |]
                    let keys = proj.Keys
                    Expect.equal proj.Status Status.None $"{label}: the nested pass succeeded"
                    Expect.sequenceEqual keys [ 7 ] $"{label}: the keys of the source keyOf wrote"
                    Expect.sequenceEqual (keys |> Array.map proj.Get) [ 70 ] $"{label}: every key has a row"
                    Expect.equal (proj.TryGet 5) None $"{label}: the superseded pass left no row for 5"
            }
        ]
