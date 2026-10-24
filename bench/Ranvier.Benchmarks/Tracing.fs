module Ranvier.Benchmarks.Tracing

open System
open System.Threading
open BenchmarkDotNet.Attributes
open Ranvier

/// <summary>
/// Benchmark invocations per iteration of <c>TraceCostBenchmarks</c>. A traced graph's log will hold at most this
/// many invocations' events.
/// </summary>
[<Literal>]
let private Invocations = 4096

/// <summary>A graph built by <c>build</c>, which returns the graph's write.</summary>
type private Rig(build: Graph -> (unit -> unit)) =
    let mutable graph = Unchecked.defaultof<Graph>
    let mutable write = ignore

    /// <summary>Disposes the current graph, if any, and builds a new one.</summary>
    member this.Rebuild() =
        this.Dispose ()
        graph <- new Graph ()
        write <- build graph

    member _.Step() = write ()

    member _.Dispose() =
        if not (isNull (box graph)) then
            (graph :> IDisposable).Dispose()
            graph <- Unchecked.defaultof<Graph>

    /// <summary>
    /// Runs five rounds of <c>Invocations</c> writes on fresh graphs, each followed by a pause, so the write path
    /// will run as tier-1 code from the first measured iteration.
    /// </summary>
    member this.Warm() =
        for _ in 1..5 do
            this.Rebuild ()

            for _ in 1..Invocations do
                this.Step ()

            this.Dispose ()
            Thread.Sleep 150

/// <summary>Written by every reaction, so no reaction's result is provably unused.</summary>
let mutable private sink = 0

let private signalWorkload (graph: Graph) =
    let source = Signal (graph, 0)

    new Effect (graph, (fun () -> sink <- sink + source.Value))
    |> ignore

    fun () -> source.Value <- source.Peek + 1

let private memoWorkload (graph: Graph) =
    let source = Signal (graph, 0)
    let doubled = Memo (graph, (fun _ -> source.Value * 2))

    new Effect (graph, (fun () -> sink <- sink + doubled.Value))
    |> ignore

    fun () -> source.Value <- source.Peek + 1

let private chainWorkload (graph: Graph) =
    let source = Signal (graph, 0)
    let mutable last: unit -> int = fun () -> source.Value

    for _ in 1..4 do
        let previous = last
        let memo = Memo (graph, (fun _ -> previous () + 1))
        last <- fun () -> memo.Value

    let tail = last

    new Effect (graph, (fun () -> sink <- sink + tail ()))
    |> ignore

    fun () -> source.Value <- source.Peek + 1

let private fanoutWorkload (graph: Graph) =
    let source = Signal (graph, 0)
    let memos = Array.init 64 (fun i -> Memo (graph, (fun _ -> source.Value + i)))

    new Effect (
        graph,
        fun () ->
            for memo in memos do
                sink <- sink + memo.Value
    )
    |> ignore

    fun () -> source.Value <- source.Peek + 1

let private cutoffWorkload (graph: Graph) =
    let source = Signal (graph, 0)
    let sign = Memo (graph, (fun _ -> source.Value >>> 31))

    new Effect (graph, (fun () -> sink <- sink + sign.Value))
    |> ignore

    fun () -> source.Value <- source.Peek + 1

let private batchWorkload (graph: Graph) =
    let source = Signal (graph, 0)

    new Effect (graph, (fun () -> sink <- sink + source.Value))
    |> ignore

    let body =
        Func<unit>(fun () ->
            for _ in 1..10 do
                source.Value <- source.Peek + 1)

    fun () -> graph.Batch body

/// <summary>
/// Updates on a built graph, the same source in traced and untraced builds. Every iteration runs 4,096 invocations
/// on graphs rebuilt before it and disposed after it, outside the measurement.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Trace"; Config.TracedConfig; InvocationCount(Invocations, 16)>]
type TraceCostBenchmarks() =
    let signal = Rig signalWorkload
    let memo = Rig memoWorkload
    let chain = Rig chainWorkload
    let fanout = Rig fanoutWorkload
    let cutoff = Rig cutoffWorkload
    let batch = Rig batchWorkload
    let rigs = [ signal; memo; chain; fanout; cutoff; batch ]

    [<GlobalSetup(Target = "Signal")>]
    member _.WarmSignal() =
        signal.Warm ()

    [<GlobalSetup(Target = "Memo")>]
    member _.WarmMemo() =
        memo.Warm ()

    [<GlobalSetup(Target = "Chain")>]
    member _.WarmChain() =
        chain.Warm ()

    [<GlobalSetup(Target = "Fanout")>]
    member _.WarmFanout() =
        fanout.Warm ()

    [<GlobalSetup(Target = "Cutoff")>]
    member _.WarmCutoff() =
        cutoff.Warm ()

    [<GlobalSetup(Target = "Batch")>]
    member _.WarmBatch() =
        batch.Warm ()

    [<IterationSetup>]
    member _.Setup() =
        for rig in rigs do
            rig.Rebuild ()

    [<IterationCleanup>]
    member _.Cleanup() =
        for rig in rigs do
            rig.Dispose ()

    /// <summary>One signal write observed by one effect.</summary>
    [<Benchmark>]
    member _.Signal() =
        signal.Step ()

    /// <summary>One signal write read by a memo and an effect.</summary>
    [<Benchmark>]
    member _.Memo() =
        memo.Step ()

    /// <summary>One write to the head of a 4-deep memo chain, observed at the tail by an effect.</summary>
    [<Benchmark>]
    member _.Chain() =
        chain.Step ()

    /// <summary>One signal write read by 64 memos, all read by one effect.</summary>
    [<Benchmark>]
    member _.Fanout() =
        fanout.Step ()

    /// <summary>One signal write whose memo recomputes to an equal value, so the observing effect stays clean.</summary>
    [<Benchmark>]
    member _.Cutoff() =
        cutoff.Step ()

    /// <summary>Ten writes to one effect-observed signal inside one <c>Batch</c>.</summary>
    [<Benchmark>]
    member _.Batch() =
        batch.Step ()

/// <summary>Rows built by <c>DisposeRows</c>.</summary>
[<Literal>]
let private Rows = 1000

/// <summary>
/// Builds a graph and disposes it, per invocation, so construction and teardown are measured apart from
/// <c>TraceCostBenchmarks</c>' updates. Each construction case builds the graph of the steady-state case of the
/// same name.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Trace"; Config.TracedConfig>]
type TraceConstructionBenchmarks() =
    let construct (build: Graph -> (unit -> unit)) =
        let graph = new Graph ()
        build graph |> ignore
        (graph :> IDisposable).Dispose()

    /// <summary>A graph with one signal observed by one effect.</summary>
    [<Benchmark>]
    member _.Signal() =
        construct signalWorkload

    /// <summary>A graph with one signal read by a memo and an effect.</summary>
    [<Benchmark>]
    member _.Memo() =
        construct memoWorkload

    /// <summary>A graph with a 4-deep memo chain observed at the tail by an effect.</summary>
    [<Benchmark>]
    member _.Chain() =
        construct chainWorkload

    /// <summary>A graph with one signal read by 64 memos, all read by one effect.</summary>
    [<Benchmark>]
    member _.Fanout() =
        construct fanoutWorkload

    /// <summary>A root of 1,000 effects, each reading one shared signal, created and then disposed.</summary>
    [<Benchmark>]
    member _.DisposeRows() =
        let graph = new Graph ()
        let source = Signal (graph, 0)

        let root =
            graph.CreateRoot (fun owner ->
                for _ in 1..Rows do
                    new Effect (graph, (fun () -> sink <- sink + source.Value))
                    |> ignore

                owner)

        root.Dispose ()
        (graph :> IDisposable).Dispose()

/// <summary>Updates written after a <c>MemoLog</c>'s recorded read position, for <c>TailRead</c>.</summary>
[<Literal>]
let private TailUpdates = 100

/// <summary>
/// A <c>memo</c>-workload graph after <c>updates</c> signal writes. <c>Read</c> is the log's length
/// <c>TailUpdates</c> writes before the end, which is also its last seq then, or 0 in an untraced build.
/// </summary>
type private MemoLog(updates: int) =
    let graph = new Graph ()
    let source = Signal (graph, 0)
    let doubled = Memo (graph, (fun _ -> source.Value * 2))

    do
        new Effect (graph, (fun () -> sink <- sink + doubled.Value))
        |> ignore

        for _ in 1 .. updates - TailUpdates do
            source.Value <- source.Peek + 1

    let read =
#if RANVIER_TRACE
        (Trace.events graph).Length
#else
        0
#endif

    do
        for _ in 1..TailUpdates do
            source.Value <- source.Peek + 1

    member _.Graph = graph
    member _.Node = doubled :> INode
    member _.Read = read

    member _.Dispose() =
        (graph :> IDisposable).Dispose()

/// <summary>
/// Trace export and query operations on a <c>MemoLog</c> of <c>Updates</c> writes, run by the <c>Traced</c> job only.
/// Each operation reads the log without appending to it, so every invocation sees the same history.
/// </summary>
/// <remarks>
/// The class compiles into the untraced host process too, where its benchmarks return <c>null</c>. BenchmarkDotNet
/// generates the traced job's runner from the host's signatures, so every benchmark returns <c>obj</c> in both builds.
/// </remarks>
[<MemoryDiagnoser; BenchmarkCategory "Trace"; Config.TracedOnlyConfig>]
type TraceQueryBenchmarks() =
    let mutable log = Unchecked.defaultof<MemoLog>

    /// <summary>Signal writes recorded in the log before measurement.</summary>
    [<Params(1000, 10000, 100000)>]
    member val Updates = 0 with get, set

    [<GlobalSetup>]
    member this.Build() =
        log <- MemoLog this.Updates

    [<GlobalCleanup>]
    member _.Dispose() =
        log.Dispose ()

#if RANVIER_TRACE
    /// <summary>A copy of the full log.</summary>
    [<Benchmark>]
    member _.Events() : obj =
        box (Trace.events log.Graph)

    /// <summary>The state folded from the full log.</summary>
    [<Benchmark>]
    member _.Snapshot() : obj =
        box (Trace.snapshot log.Graph)

    /// <summary>The cause chain of the memo's last run.</summary>
    [<Benchmark>]
    member _.Why() : obj =
        box (Trace.why log.Graph log.Node)

    /// <summary>The memo's origin.</summary>
    [<Benchmark>]
    member _.Origin() : obj =
        box (Trace.origin log.Graph log.Node)

    /// <summary>The JSONL dump of the full log.</summary>
    [<Benchmark>]
    member _.DumpText() : obj =
        box (Trace.dumpText log.Graph)

    /// <summary>
    /// The events recorded by the last 100 updates, read as the signal maps read them: a copy of the full log, sliced
    /// from the previous read's length.
    /// </summary>
    [<Benchmark>]
    member _.TailRead() : obj =
        let events = Trace.events log.Graph
        box events[log.Read ..]

    /// <summary>The events recorded by the last 100 updates, read with <c>Trace.eventsSince</c> from a seq cursor.</summary>
    [<Benchmark>]
    member _.EventsSince() : obj =
        box (Trace.eventsSince log.Graph log.Read)
#else
    [<Benchmark>]
    member _.Events() : obj = null

    [<Benchmark>]
    member _.Snapshot() : obj = null

    [<Benchmark>]
    member _.Why() : obj = null

    [<Benchmark>]
    member _.Origin() : obj = null

    [<Benchmark>]
    member _.DumpText() : obj = null

    [<Benchmark>]
    member _.TailRead() : obj = null

    [<Benchmark>]
    member _.EventsSince() : obj = null
#endif

/// <summary>
/// <c>Trace.history</c> of the memo in a <c>MemoLog</c>, run by the <c>Traced</c> job only. Its cost grows with the
/// square of the run count (about 14 ms at 1,000 updates and 1.2 s at 10,000), so the series stops at 10,000.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Trace"; Config.TracedOnlyConfig>]
type TraceHistoryBenchmarks() =
    let mutable log = Unchecked.defaultof<MemoLog>

    /// <summary>Signal writes recorded in the log before measurement.</summary>
    [<Params(1000, 3000, 10000)>]
    member val Updates = 0 with get, set

    [<GlobalSetup>]
    member this.Build() =
        log <- MemoLog this.Updates

    [<GlobalCleanup>]
    member _.Dispose() =
        log.Dispose ()

    /// <summary>Every recorded run of the memo.</summary>
    [<Benchmark>]
    member _.History() : obj =
#if RANVIER_TRACE
        box (Trace.history log.Graph log.Node)
#else
        null
#endif
