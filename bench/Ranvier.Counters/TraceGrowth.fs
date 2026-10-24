/// <summary>
/// Trace history growth under sustained updates on one graph, compiled into both the .NET counter bench and its Fable
/// harness, so both targets run identical graphs.
/// </summary>
module CounterBench.TraceGrowth

/// <summary>A workload's graph after <c>Updates</c> signal writes.</summary>
type Checkpoint =
    {
        Workload: string
        Updates: int
        /// <summary>Events in the graph's log; 0 in an untraced build.</summary>
        Events: int
        /// <summary>Heap bytes after a full collection, minus the heap before the graph was built.</summary>
        RetainedBytes: float
        /// <summary>Milliseconds to copy the full log with <c>Trace.events</c>.</summary>
        EventsMs: float
        /// <summary>Milliseconds to fold the full log with <c>Trace.snapshot</c>.</summary>
        SnapshotMs: float
        /// <summary>Milliseconds to write the full log with <c>Trace.dumpText</c>.</summary>
        DumpTextMs: float
        /// <summary>
        /// Milliseconds to read the events of the last <c>TailEvents</c> writes as the signal maps read them: a copy
        /// of the full log, sliced from the previous read's length.
        /// </summary>
        TailReadMs: float
        /// <summary>Milliseconds to read the same events with <c>Trace.eventsSince</c> from the previous read's last seq.</summary>
        SinceMs: float
        /// <summary>Events in the tail read.</summary>
        TailEvents: int
    }

/// <summary>A workload's growth across its checkpoints.</summary>
type GrowthSummary =
    {
        Workload: string
        Checkpoints: int
        /// <summary>Events per update at the checkpoint with the most updates, graph construction included.</summary>
        EventsPerUpdate: float
        /// <summary>Retained bytes per event at the margin: <c>RetainedSlope / EventSlope</c>, or 0 with no events.</summary>
        BytesPerEvent: float
        /// <summary>The least-squares slope of events against updates.</summary>
        EventSlope: float
        /// <summary>The least-squares slope of retained bytes against updates.</summary>
        RetainedSlope: float
    }

/// <summary>The least-squares slope of <c>ys</c> against <c>xs</c>; <c>xs</c> holds at least two distinct values.</summary>
let private slope (xs: float[]) (ys: float[]) =
    let mx = Array.average xs
    let my = Array.average ys
    let mutable covariance = 0.
    let mutable variance = 0.

    for i in 0 .. xs.Length - 1 do
        covariance <- covariance + (xs[i] - mx) * (ys[i] - my)
        variance <- variance + (xs[i] - mx) * (xs[i] - mx)

    covariance / variance

/// <summary>
/// The growth of one workload's <c>checkpoints</c>. An error when they hold fewer than two distinct update counts or
/// more than one workload.
/// </summary>
let summarise (checkpoints: Checkpoint[]) : Result<GrowthSummary, string> =
    let updateCounts =
        checkpoints
        |> Array.map _.Updates
        |> Array.distinct

    let workloads =
        checkpoints
        |> Array.map (fun c -> c.Workload)
        |> Array.distinct

    if updateCounts.Length < 2 then
        Error $"A growth summary needs checkpoints at two update counts or more; got %d{updateCounts.Length}."
    elif workloads.Length > 1 then
        Error (
            "A growth summary covers one workload; got "
            + String.concat ", " workloads
            + "."
        )
    else
        let updates =
            checkpoints
            |> Array.map (fun c -> float c.Updates)

        let eventSlope = slope updates (checkpoints |> Array.map (fun c -> float c.Events))
        let retainedSlope = slope updates (checkpoints |> Array.map _.RetainedBytes)
        let last = checkpoints |> Array.maxBy _.Updates

        Ok
            {
                Workload = workloads[0]
                Checkpoints = checkpoints.Length
                EventsPerUpdate = float last.Events / float last.Updates
                BytesPerEvent = if eventSlope = 0. then 0. else retainedSlope / eventSlope
                EventSlope = eventSlope
                RetainedSlope = retainedSlope
            }

/// <summary>The runtime's heap and clock, as the probe reads them.</summary>
type Host =
    {
        /// <summary>Heap bytes in use after a full collection.</summary>
        RetainedBytes: unit -> float
        /// <summary>A monotonic clock in milliseconds.</summary>
        Now: unit -> float
    }

/// <summary>Every checkpoint of a probe run, oldest first per workload, and each workload's growth.</summary>
type GrowthRun =
    {
        /// <summary><c>dotnet</c> or <c>node</c>.</summary>
        Runtime: string
        /// <summary>Whether the library was compiled with <c>RANVIER_TRACE</c>.</summary>
        Traced: bool
        Checkpoints: Checkpoint[]
        Summaries: GrowthSummary[]
    }

/// <summary>
/// Writes before each checkpoint that the tail read covers. Checkpoints lie at least this many writes apart.
/// </summary>
[<Literal>]
let TailUpdates = 100

/// <summary>Timed runs of each export operation; a checkpoint records the fastest.</summary>
[<Literal>]
let private Repeats = 3

/// <summary>Written by every reaction, so no reaction's result is provably unused.</summary>
let mutable private sink = 0

/// <summary>The last export result, so no export is provably unused.</summary>
let mutable private kept: obj = null

let private traced =
#if RANVIER_TRACE
    true
#else
    false
#endif

/// <summary>One signal read by one memo, read by one effect. Returns the signal's write.</summary>
let private memo (graph: Ranvier.Graph) : unit -> unit =
    let source =
        graph.CreateRoot (fun _ ->
            use _active = graph.Activate ()
            let source = Ranvier.Api.createSignal 0
            let doubled = Ranvier.Api.createMemo (fun _ -> source.Value * 2)
            Ranvier.Api.createEffect (fun () -> sink <- sink + doubled.Value)
            source)

    fun () -> source.Value <- source.Peek + 1

/// <summary>One signal read by 64 memos, all read by one effect. Returns the signal's write.</summary>
let private fanout (graph: Ranvier.Graph) : unit -> unit =
    let source =
        graph.CreateRoot (fun _ ->
            use _active = graph.Activate ()
            let source = Ranvier.Api.createSignal 0

            let memos =
                Array.init 64 (fun i -> Ranvier.Api.createMemo (fun _ -> source.Value + i))

            Ranvier.Api.createEffect (fun () ->
                for m in memos do
                    sink <- sink + m.Value)

            source)

    fun () -> source.Value <- source.Peek + 1

/// <summary>
/// The measured workloads and their checkpoints. A <c>fanout</c> write records about 28 times the events of a
/// <c>memo</c> write, so its checkpoints stop at 10,000 updates, about 3.9 million events. <c>memo-checkpointed</c>
/// is <c>memo</c> with a <c>Trace.checkpoint</c> at each read position, so its log retains the last
/// <c>TailUpdates</c> writes.
/// </summary>
let plan: (string * int[]) list =
    [
        "memo", [| 1000; 10000; 100000 |]
        "memo-checkpointed", [| 1000; 10000; 100000 |]
        "fanout", [| 100; 1000; 10000 |]
    ]

let private workload (name: string) =
    match name with
    | "memo"
    | "memo-checkpointed" -> memo
    | "fanout" -> fanout
    | _ -> invalidArg (nameof name) $"No trace growth workload named '%s{name}'."

/// <summary>The fastest of <c>Repeats</c> runs of <c>f</c>, in milliseconds.</summary>
let private fastest (host: Host) (f: unit -> obj) =
    let mutable best = infinity

    for _ in 1..Repeats do
        let start = host.Now ()
        kept <- f ()
        best <- min best (host.Now () - start)

    best

/// <summary>
/// The checkpoints of <c>name</c>'s workload on one graph, written to <c>checkpoints</c> times in ascending order.
/// </summary>
/// <exception cref="T:System.ArgumentException">
/// A checkpoint lies under <c>TailUpdates</c> writes after the previous one, or after the start.
/// </exception>
let probe (host: Host) (name: string) (checkpoints: int[]) : Checkpoint[] =
    checkpoints
    |> Array.iteri (fun i target ->
        let previous = if i = 0 then 0 else checkpoints[i - 1]

        if target - previous < TailUpdates then
            invalidArg (nameof checkpoints) $"Checkpoint %d{target} lies under %d{TailUpdates} writes after %d{previous}.")

    let build = workload name
    let baseline = host.RetainedBytes ()
    let graph = new Ranvier.Graph ()
    let write = build graph
    let mutable updates = 0

    let advance (target: int) =
        while updates < target do
            write ()
            updates <- updates + 1

    let result =
        [|
            for target in checkpoints do
                advance (target - TailUpdates)
#if RANVIER_TRACE
                let cursor =
                    match Ranvier.Trace.events graph with
                    | [||] -> (Ranvier.Trace.checkpointOf graph).Snapshot.Seq
                    | retained -> retained[retained.Length - 1].Seq

                if name = "memo-checkpointed" then
                    Ranvier.Trace.checkpoint graph cursor (string target)

                let read = (Ranvier.Trace.events graph).Length
#endif
                advance target
                let retained = host.RetainedBytes () - baseline
#if RANVIER_TRACE
                let events = (Ranvier.Trace.events graph).Length

                yield
                    {
                        Workload = name
                        Updates = target
                        Events = events
                        RetainedBytes = retained
                        EventsMs = fastest host (fun () -> box (Ranvier.Trace.events graph))
                        SnapshotMs = fastest host (fun () -> box (Ranvier.Trace.snapshot graph))
                        DumpTextMs = fastest host (fun () -> box (Ranvier.Trace.dumpText graph))
                        TailReadMs =
                            fastest host (fun () ->
                                let all = Ranvier.Trace.events graph
                                box all[read..])
                        SinceMs = fastest host (fun () -> box (Ranvier.Trace.eventsSince graph cursor))
                        TailEvents = events - read
                    }
#else
                yield
                    {
                        Workload = name
                        Updates = target
                        Events = 0
                        RetainedBytes = retained
                        EventsMs = 0.
                        SnapshotMs = 0.
                        DumpTextMs = 0.
                        TailReadMs = 0.
                        SinceMs = 0.
                        TailEvents = 0
                    }
#endif
        |]

    kept <- null
    (graph :> System.IDisposable).Dispose()
    result

/// <summary>Every workload of <c>workloads</c> probed on its own graph, in order, with its growth.</summary>
let run (host: Host) (runtime: string) (workloads: (string * int[]) list) : GrowthRun =
    let probed =
        workloads
        |> List.map (fun (name, checkpoints) -> probe host name checkpoints)

    {
        Runtime = runtime
        Traced = traced
        Checkpoints = Array.concat probed
        Summaries =
            probed
            |> List.choose (fun checkpoints ->
                match summarise checkpoints with
                | Ok summary -> Some summary
                | Error _ -> None)
            |> List.toArray
    }

#if !FABLE_COMPILER
/// <summary>The .NET heap after a full, compacting collection, and <c>Stopwatch</c> time.</summary>
let dotnetHost: Host =
    {
        RetainedBytes =
            fun () ->
                System.GC.Collect ()
                System.GC.WaitForPendingFinalizers ()
                float (System.GC.GetTotalMemory true)
        Now = fun () -> System.Diagnostics.Stopwatch.GetElapsedTime(0L).TotalMilliseconds
    }

let private jsonOptions =
    System.Text.Json.JsonSerializerOptions (WriteIndented = true)

/// <summary>Writes <c>growth</c> to <c>path</c> as JSON.</summary>
let write (path: string) (growth: GrowthRun) =
    System.IO.File.WriteAllText (path, System.Text.Json.JsonSerializer.Serialize (growth, jsonOptions))

/// <summary>The probe run in the JSON file at <c>path</c>, written by <c>write</c> or by the Fable harness.</summary>
let read (path: string) : GrowthRun =
    System.Text.Json.JsonSerializer.Deserialize<GrowthRun>(System.IO.File.ReadAllText path, jsonOptions)
#endif
