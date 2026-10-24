/// <summary>
/// Application-shaped workloads for Ranvier alone, compiled into both the .NET counter bench and its Fable harness, so
/// both targets run identical graphs.
/// </summary>
module CounterBench.Workloads

open System
open System.Threading
open System.Threading.Tasks
open Ranvier

type private TimedWorkloadClock() =
    inherit TimedClock()
    let timers = ResizeArray<Action * float ref>()
    let mutable now = 0.
    override _.NowMilliseconds = now

    override _.CreateTimer(callback) =
        let due = ref infinity
        timers.Add (callback, due)

        { new TimedTimer() with
            member _.Arm delay =
                due.Value <- now + delay.TotalMilliseconds

            member _.Disarm() =
                due.Value <- infinity

            member _.Dispose() =
                due.Value <- infinity
        }

    member _.Advance() =
        now <- now + 100.

        for i in 0 .. timers.Count - 1 do
            let callback, due = timers[i]

            if due.Value <= now then
                due.Value <- infinity
                callback.Invoke ()

/// <summary>State built outside the measured region, as the scenarios' <c>Prepared</c>.</summary>
type Workload =
    {
        Run: unit -> unit
        Teardown: unit -> unit
    }

[<Literal>]
let Rows = 1000

[<Literal>]
let DetailNodes = 20

[<Literal>]
let DiamondWidth = 100

[<Literal>]
let DynamicReaders = 100

[<Literal>]
let Widgets = 10

[<Literal>]
let SourcesPerWidget = 10

/// <summary>Written by every reaction, so no reaction's result is provably unused.</summary>
let mutable private sink = 0

#if RANVIER_TRACE && RANVIER_COUNTERS
let private created = ResizeArray<Graph>()

/// <summary>The graphs created by <c>newGraph</c> since the last call, oldest first.</summary>
let takeGraphs () : Graph[] =
    let graphs = created.ToArray ()
    created.Clear ()
    graphs

/// <summary>A new graph, recorded for <c>takeGraphs</c>.</summary>
let newGraph () =
    let graph = new Graph ()
    created.Add graph
    graph
#else
let inline newGraph () =
    new Graph ()
#endif

let private teardown (owner: Owner) (graph: Graph) () =
    owner.Dispose ()
    (graph :> IDisposable).Dispose()

/// <summary>Writes one source observed by 64 timed nodes, optionally admitting each changed capture.</summary>
let timed (mode: string) (admit: bool) (n: int) =
    let graph = newGraph ()
    let clock = TimedWorkloadClock ()

    let owner, source =
        graph.CreateRoot (fun owner ->
            use _active = graph.Activate ()
            let source = createSignal 0

            let factory =
                match mode with
                | "first" -> throttleFirstWith
                | "last" -> throttleLastWith
                | "both" -> throttleWith
                | _ -> debounceWith

            for _ in 1..64 do
                let output =
                    factory { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> source.Value) graph

                createEffect (fun () -> sink <- output.Value)

            owner, source)

    source.Value <- 1

    {
        Run =
            fun () ->
                for _ in 1..n do
                    source.Value <- source.Peek + 1

                    if admit then
                        clock.Advance ()
        Teardown = teardown owner graph
    }

/// <summary>
/// A <c>Rows</c>-row projection through a <c>filter</c> on a query signal and a <c>sortBy</c> on a direction signal.
/// One effect reads the visible keys; one effect per row reads its cell. An operation alternates between a query
/// change and a direction change.
/// </summary>
let table (n: int) =
    let graph = newGraph ()

    let owner, query, descending =
        graph.CreateRoot (fun owner ->
            use _ = graph.Activate ()
            let source = createSignal (Array.init Rows (fun i -> i, i))
            let query = createSignal 0
            let descending = createSignal false
            let rows = createProjection fst snd (fun () -> source.Value)

            let visible =
                rows
                |> Projection.filter (fun v -> v % 10 >= query.Value)
                |> Projection.sortBy (fun v -> if descending.Value then -v else v)

            createEffect (fun () -> sink <- visible.Keys.Length)

            for key in 0 .. Rows - 1 do
                createEffect (fun () -> sink <- rows.Get key)

            owner, query, descending)

    {
        Run =
            fun () ->
                for op in 1..n do
                    if op % 2 = 1 then
                        query.Value <- (query.Peek + 1) % 5
                    else
                        descending.Value <- not descending.Peek
        Teardown = teardown owner graph
    }

/// <summary>
/// <c>Rows</c> rows, each highlighted through a <c>createSelector</c> over the selected index, and a detail effect that
/// builds <c>DetailNodes</c> memo and effect pairs over the selected row. An operation moves the selection.
/// </summary>
let detail (n: int) =
    let graph = newGraph ()

    let owner, selected =
        graph.CreateRoot (fun owner ->
            use _ = graph.Activate ()
            let rows = Array.init Rows createSignal
            let selected = createSignal 0
            let isSelected = createSelector (fun () -> selected.Value)

            for i in 0 .. Rows - 1 do
                createEffect (fun () -> sink <- if isSelected.Get i then rows[i].Value else 0)

            createEffect (fun () ->
                let row = rows[selected.Value]

                for j in 1..DetailNodes do
                    let field = createMemo (fun _ -> row.Value + j)
                    createEffect (fun () -> sink <- field.Value))

            owner, selected)

    {
        Run =
            fun () ->
                for _ in 1..n do
                    selected.Value <- (selected.Peek + 37) % Rows
        Teardown = teardown owner graph
    }

/// <summary>
/// One source read by <c>DiamondWidth</c> memos, joined by one sum memo read by one effect. An operation writes the
/// source.
/// </summary>
let wideDiamond (n: int) =
    let graph = newGraph ()

    let owner, source =
        graph.CreateRoot (fun owner ->
            use _ = graph.Activate ()
            let source = createSignal 0

            let branches =
                Array.init DiamondWidth (fun j -> createMemo (fun _ -> source.Value + j))

            let sum =
                createMemo (fun _ ->
                    let mutable total = 0

                    for branch in branches do
                        total <- total + branch.Value

                    total)

            createEffect (fun () -> sink <- sum.Value)
            owner, source)

    {
        Run =
            fun () ->
                for _ in 1..n do
                    source.Value <- source.Peek + 1
        Teardown = teardown owner graph
    }

/// <summary>
/// <c>DynamicReaders</c> effects, each reading its own <c>a</c> or <c>b</c> source as a shared condition selects. An
/// operation alternates between flipping the condition and writing every active source.
/// </summary>
let dynamicBranches (n: int) =
    let graph = newGraph ()

    let owner, condition, a, b =
        graph.CreateRoot (fun owner ->
            use _ = graph.Activate ()
            let condition = createSignal true
            let a = Array.init DynamicReaders createSignal
            let b = Array.init DynamicReaders createSignal

            for i in 0 .. DynamicReaders - 1 do
                createEffect (fun () -> sink <- if condition.Value then a[i].Value else b[i].Value)

            owner, condition, a, b)

    {
        Run =
            fun () ->
                for op in 1..n do
                    if op % 2 = 1 then
                        condition.Value <- not condition.Peek
                    else
                        let active = if condition.Peek then a else b

                        for source in active do
                            source.Value <- source.Peek + 1
        Teardown = teardown owner graph
    }

/// <summary>
/// <c>Widgets</c> boundaries, each summing <c>SourcesPerWidget</c> async sources held in signals and read by one
/// effect. <c>boundary</c> wraps a body in <c>createSuspense</c> or <c>createBoundary</c>.
/// </summary>
let private widgets (boundary: (unit -> int) -> Boundary<int>) =
    let graph = newGraph ()

    let owner, slots =
        graph.CreateRoot (fun owner ->
            use _ = graph.Activate ()

            let slots =
                Array.init Widgets (fun _ ->
                    Array.init SourcesPerWidget (fun i ->
                        let source = AsyncSource<int> graph
                        source.Settle i
                        createSignal source))

            for widget in slots do
                let shown =
                    boundary (fun () ->
                        let mutable total = 0

                        for slot in widget do
                            total <- total + slot.Value.Value

                        total)

                createEffect (fun () -> sink <- shown.Value)

            owner, slots)

    graph, owner, slots

/// <summary>
/// <c>Widgets</c> suspense boundaries over <c>SourcesPerWidget</c> async sources each. An operation reloads every
/// source in one batch, then settles each one separately.
/// </summary>
let asyncResolve (n: int) =
    let graph, owner, slots =
        widgets (fun body -> createSuspense (fun last -> ValueOption.defaultValue -1 last) body)

    let flat = Array.concat slots
    let fresh = Array.zeroCreate<AsyncSource<int>> flat.Length

    {
        Run =
            fun () ->
                for op in 1..n do
                    graph.Batch (fun () ->
                        for i in 0 .. flat.Length - 1 do
                            fresh[i] <- AsyncSource<int> graph
                            flat[i].Value <- fresh[i])

                    for source in fresh do
                        source.Settle op
        Teardown = teardown owner graph
    }

/// <summary>
/// <c>Widgets</c> error boundaries over <c>SourcesPerWidget</c> async sources each. An operation fails one source of
/// one widget, then replaces it with a settled one.
/// </summary>
let asyncRecover (n: int) =
    let graph, owner, slots =
        widgets (fun body -> createBoundary (fun last -> ValueOption.defaultValue -1 last) (fun _ last -> ValueOption.defaultValue -2 last) body)

    let failure = exn "load failed"

    {
        Run =
            fun () ->
                for op in 1..n do
                    let slot = slots[op % Widgets][0]
                    let failing = AsyncSource<int> graph
                    slot.Value <- failing
                    failing.Fail failure
                    let settled = AsyncSource<int> graph
                    slot.Value <- settled
                    settled.Settle op
        Teardown = teardown owner graph
    }

/// <summary>Delivers <c>burst</c> items per round into a stream fold read by a memo and an effect, inside one batch.</summary>
let streamBurst (burst: int) (n: int) =
    let graph = newGraph ()
    let sink = ref Unchecked.defaultof<StreamSink<int>>

    let source =
        StreamSource.create (fun s _ ->
            sink.Value <- s
            TaskCompletionSource<unit>().Task :> Task)

    let seen = ref 0

    let owner =
        graph.CreateRoot (fun owner ->
            use _ = graph.Activate ()
            let memo = Api.createStreamFold 0 (+) (fun _ -> source)
            let doubled = createMemo (fun _ -> memo.Value * 2)
            createEffect (fun () -> seen.Value <- doubled.Value)
            owner)

    sink.Value.Next 0
    let expected = ref 0

    {
        Run =
            fun () ->
                for _ in 1..n do
                    graph.Batch (fun () ->
                        for i in 1..burst do
                            sink.Value.Next i)

                    expected.Value <- expected.Value + burst * (burst + 1) / 2

                if seen.Value <> 2 * expected.Value then
                    failwith $"stream-burst folded %d{seen.Value / 2}, expected %d{expected.Value}"
        Teardown = teardown owner graph
    }

/// <summary>The baseline for <c>streamBurst</c>: <c>burst</c> async-source settles per round, batched or not.</summary>
let settleBurst (batched: bool) (burst: int) (n: int) =
    let graph = newGraph ()
    let source = AsyncSource<int>(graph)
    let seen = ref 0

    let owner =
        graph.CreateRoot (fun owner ->
            use _ = graph.Activate ()
            let doubled = createMemo (fun _ -> source.Value * 2)
            createEffect (fun () -> seen.Value <- doubled.Value)
            owner)

    source.Settle 0
    let total = ref 0

    let round () =
        for i in 1..burst do
            total.Value <- total.Value + i
            source.Settle total.Value

    {
        Run =
            fun () ->
                for _ in 1..n do
                    if batched then
                        graph.Batch (fun () -> round ())
                    else
                        round ()

                if seen.Value <> 2 * total.Value then
                    failwith $"settle-burst saw %d{seen.Value / 2}, expected %d{total.Value}"
        Teardown = teardown owner graph
    }

/// <summary>A dependency write restarts an open stream, which then receives one item.</summary>
let streamRestart (n: int) =
    let graph = newGraph ()
    let trigger = Signal (graph, 0)
    let sink = ref Unchecked.defaultof<StreamSink<int>>
    let last = ref -1

    let source =
        StreamSource.create (fun s _ ->
            sink.Value <- s
            TaskCompletionSource<unit>().Task :> Task)

    let owner =
        graph.CreateRoot (fun owner ->
            use _ = graph.Activate ()

            let memo =
                Api.createStream (fun _ ->
                    trigger.Value |> ignore
                    source)

            createEffect (fun () ->
                last.Value <-
                    match memo.TryValue with
                    | Ready v -> v
                    | _ -> -1)

            owner)

    let next = ref 0

    {
        Run =
            fun () ->
                for _ in 1..n do
                    next.Value <- next.Value + 1
                    trigger.Value <- next.Value
                    sink.Value.Next next.Value

                if last.Value <> next.Value then
                    failwith $"stream-restart ended at %d{last.Value}, expected %d{next.Value}"
        Teardown = teardown owner graph
    }

/// <summary>
/// The baseline for <c>streamRestart</c>: a dependency write restarts an async memo's open flight under
/// <c>CancelPrevious</c>, read by an effect. The flights stay open.
/// </summary>
let flightRestart (n: int) =
    let graph = newGraph ()

    if graph.Options.FlightPolicy <> CancelPrevious then
        failwith "flight-restart expects the default CancelPrevious policy"

    let trigger = Signal (graph, 0)
    let flights = ref 0
    let previous = ref CancellationToken.None
    let latest = ref CancellationToken.None
    let pending = ref false

    let owner =
        graph.CreateRoot (fun owner ->
            use _ = graph.Activate ()

            let memo =
                Api.createAsync (fun _ token ->
                    trigger.Value |> ignore
                    flights.Value <- flights.Value + 1
                    previous.Value <- latest.Value
                    latest.Value <- token
                    TaskCompletionSource<int>().Task)

            createEffect (fun () ->
                pending.Value <-
                    match memo.TryValue with
                    | Pending -> true
                    | _ -> false)

            owner)

    let started = ref 0

    {
        Run =
            fun () ->
                for _ in 1..n do
                    started.Value <- started.Value + 1
                    trigger.Value <- started.Value

                if
                    flights.Value <> started.Value + 1
                    || not previous.Value.IsCancellationRequested
                    || not pending.Value
                then
                    failwith $"flight-restart started %d{flights.Value} flights, expected %d{started.Value + 1}"
        Teardown = teardown owner graph
    }

/// <summary>An item of the keyed-pass workloads, compared by reference under the default equality policy.</summary>
type KeyedItem = { Id: int; Value: int }

/// <summary>
/// A <c>Rows</c>-row projection keyed by Id, with one effect per row and one reading the keys. Operations alternate
/// between <c>edit</c> applied to the initial list and the initial list. With <c>fresh</c>, every write holds new item
/// objects; otherwise every write shares the initial list's items.
/// </summary>
let keyedPass (fresh: bool) (edit: KeyedItem[] -> KeyedItem[]) (n: int) =
    let graph = newGraph ()
    let initial = Array.init Rows (fun i -> { Id = i; Value = i })
    let edited = edit initial

    let writes =
        Array.init n (fun op ->
            let next = if op % 2 = 0 then edited else initial

            if fresh then
                next
                |> Array.map (fun item -> { item with Id = item.Id })
            else
                Array.copy next)

    let owner, source =
        graph.CreateRoot (fun owner ->
            use _active = graph.Activate ()
            let source = createSignal initial

            let rows =
                createProjection (fun item -> item.Id) (fun item -> item.Value) (fun () -> source.Value :> seq<_>)

            createEffect (fun () -> sink <- rows.Keys.Length)

            // Key Rows is the key the insert workload adds.
            for key in 0..Rows do
                createEffect (fun () ->
                    match rows.TryGet key with
                    | Some value -> sink <- value
                    | None -> ())

            owner, source)

    {
        Run =
            fun () ->
                for write in writes do
                    source.Value <- write
        Teardown = teardown owner graph
    }

/// <summary>Removes the middle item.</summary>
let removeMiddle (items: KeyedItem[]) =
    Array.removeAt (items.Length / 2) items

/// <summary>Swaps the second and second-to-last items.</summary>
let swapInner (items: KeyedItem[]) =
    let swapped = Array.copy items
    swapped[1] <- items[items.Length - 2]
    swapped[items.Length - 2] <- items[1]
    swapped

/// <summary>Prepends an item of Id <c>Rows</c>.</summary>
let insertFirst (items: KeyedItem[]) =
    Array.insertAt 0 { Id = Rows; Value = Rows } items
