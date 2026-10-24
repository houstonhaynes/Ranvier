/// .NET only: FsCheck properties and garbage collection of checkpointed trace logs.
module Ranvier.Tests.TraceCheckpoints

open System
open Expecto
open Ranvier

#if RANVIER_TRACE
type Op =
    | Write of first: bool * value: int
    | Batched of writes: (bool * int) list
    | Read
    | Named of index: int
    | Watch
    | Unwatch

/// <summary>
/// A graph of two signals, a lazy memo over both, a lazy host memo that creates a child memo in each run, and the nodes
/// <c>Named</c> and <c>Watch</c> add.
/// </summary>
type private Script(graph: Graph) =
    let a = graph.Run (fun () -> Trace.named "a" (fun () -> Signal (graph, 0)))
    let b = graph.Run (fun () -> Signal (graph, 0))
    let m = graph.Run (fun () -> createMemo (fun _ -> a.Value + b.Value))

    let host =
        graph.Run (fun () ->
            createMemoWith (fun _ ->
                let child = Trace.named "child" (fun () -> createMemo (fun _ -> a.Value))
                child.Value + b.Value))

    let memos = ResizeArray<INode> [ m :> INode; host :> INode ]
    let roots = System.Collections.Generic.Queue<Owner>()

    member _.Memos = List.ofSeq memos

    member _.Apply(op: Op) =
        let pick first =
            if first then a else b

        graph.Run (fun () ->
            match op with
            | Write (first, value) -> (pick first).Value <- value
            | Batched writes ->
                batch (fun () ->
                    for first, value in writes do
                        (pick first).Value <- value)
            | Read ->
                m.Value |> ignore
                host.Value |> ignore
            | Named index ->
                let index = abs index % 3

                let n =
                    Trace.named ("n" + string index) (fun () -> createMemo (fun _ -> m.Value + index))

                n.Value |> ignore
                memos.Add n
            | Watch ->
                roots.Enqueue (
                    createRoot (fun owner ->
                        let watched = Trace.named "watched" (fun () -> createMemo (fun _ -> m.Value))
                        createEffect (fun () -> watched.Value |> ignore)
                        owner)
                )
            | Unwatch ->
                if roots.Count > 0 then
                    roots.Dequeue().Dispose())

let private shape (events: TraceEvent[]) =
    events
    |> Array.map (fun e -> e.Seq, e.Kind, e.Node, e.Other, e.Arg, e.Cause, e.Flag, string e.Payload)

let private lastSeq (graph: Graph) =
    match Trace.events graph with
    | [||] -> (Trace.checkpointOf graph).Snapshot.Seq
    | events -> events[events.Length - 1].Seq

/// <summary>A checkpoint seq of <c>graph</c> from <c>cut</c>, moved past a <c>Label</c> that follows its creation.</summary>
let private splitOf (graph: Graph) (cut: int) =
    let k = abs cut % (lastSeq graph + 1)
    let log = ((box graph) :?> ITraced).TraceLog
    if log.LabelFollows k then k + 1 else k

/// <summary>
/// The cause chain of a run in a log checkpointed at <c>k</c>: the steps after <c>k</c>, ending at
/// <c>BeforeCheckpoint</c> when the full chain reaches a step at or before <c>k</c>.
/// </summary>
let private cut (k: int) (name: string) (why: Why) =
    let kept = why.Steps |> List.takeWhile (fun s -> s.Seq > k)

    { why with
        Steps = kept
        Root =
            if kept.Length = why.Steps.Length then
                why.Root
            else
                Some (BeforeCheckpoint name)
    }

/// <summary>
/// True when the events <c>whyNot</c> reads for <c>node</c>, from its last <c>RunStart</c>, <c>RunEnd</c> or
/// <c>NodeNew</c> on, all follow <c>k</c>.
/// </summary>
let private windowAfter (k: int) (events: TraceEvent[]) (node: int) =
    let last (kinds: TraceEventKind list) =
        events
        |> Array.tryFindBack (fun e -> e.Node = node && List.contains e.Kind kinds)
        |> Option.map _.Seq

    (last [ TraceEventKind.RunEnd; TraceEventKind.NodeNew ]
     |> Option.exists (fun seq -> seq > k))
    && (last [ TraceEventKind.RunStart ]
        |> Option.forall (fun seq -> seq > k))

type private Payload() = class end

/// <summary>Writes a fresh payload to <c>signal</c> and then 0, and returns a weak reference to the payload.</summary>
[<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
let private writeAndReplace (graph: Graph) (signal: Signal<obj>) =
    let payload = Payload ()
    graph.Run (fun () -> signal.Value <- box payload)
    graph.Run (fun () -> signal.Value <- box 0)
    WeakReference payload

let private collected (weak: WeakReference) =
    GC.Collect ()
    GC.WaitForPendingFinalizers ()
    GC.Collect ()
    not weak.IsAlive

let private config =
    { FsCheckConfig.defaultConfig with
        maxTest = 200
    }

[<Tests>]
let tests =
    testList
        "Trace checkpoints"
        [
            testPropertyWithConfig config "the snapshot at a split folds with the events since it to the snapshot"
            <| fun (ops: Op list) (cut: int) ->
                use g = new Graph ()
                let script = Script g
                ops |> List.iter script.Apply
                let k = splitOf g cut
                Expect.equal (TraceModel.fold (Trace.snapshotAt g k) (Trace.eventsSince g k)) (Trace.snapshot g) $"split at {k}"

            testPropertyWithConfig config "a pruned snapshot at a split folds and prunes to the pruned snapshot"
            <| fun (ops: Op list) (cut: int) ->
                use g = new Graph ()
                let script = Script g
                ops |> List.iter script.Apply
                let k = splitOf g cut
                let pruned = TraceModel.prune (Trace.snapshotAt g k)

                Expect.equal (TraceModel.prune (TraceModel.fold pruned (Trace.eventsSince g k))) (TraceModel.prune (Trace.snapshot g)) $"split at {k}"

            test "a checkpoint releases the payloads of the events it drops" {
                use g = new Graph ()
                let signal = g.Run (fun () -> Signal<obj>(g, box 0))
                let weak = writeAndReplace g signal
                Expect.isFalse (collected weak) "the log holds the written payload"
                Trace.checkpoint g (lastSeq g) "cut"
                Expect.isTrue (collected weak) "the checkpoint releases the payload"
            }

            test "a checkpoint releases the storage of the events it drops" {
                use g = new Graph ()
                let signal = g.Run (fun () -> Signal (g, 0))

                let heap () =
                    float (GC.GetTotalMemory true)

                let start = heap ()

                for i in 1..20000 do
                    g.Run (fun () -> signal.Value <- i)

                let grown = heap () - start
                Trace.checkpoint g (lastSeq g) "cut"
                let kept = heap () - start
                Expect.isLessThan kept (grown / 4.) $"the checkpoint keeps {kept} of {grown} bytes"
            }

            testPropertyWithConfig config "a checkpointed graph answers as its uncheckpointed twin"
            <| fun (before: Op list) (after: Op list) (split: int) ->
                use twin = new Graph ()
                use g = new Graph ()
                let twinScript = Script twin
                let script = Script g

                for op in before do
                    twinScript.Apply op
                    script.Apply op

                let k = splitOf g split
                Trace.checkpoint g k "cut"

                for op in after do
                    twinScript.Apply op
                    script.Apply op

                Expect.equal (TraceModel.prune (Trace.snapshot g)) (TraceModel.prune (Trace.snapshot twin)) $"snapshot after a checkpoint at {k}"
                Expect.equal (shape (Trace.eventsSince g k)) (shape (Trace.eventsSince twin k)) "the events since the split"
                Expect.isEmpty (Trace.reconcile g) "the checkpointed edges match the live lists"

                for node, twinNode in List.zip script.Memos twinScript.Memos do
                    let runs =
                        (Trace.history twin twinNode).Runs
                        |> List.filter (fun r -> r.Start > k)

                    Expect.equal
                        ((Trace.history g node).Runs
                         |> List.map (fun r -> r.Run, r.Start))
                        (runs |> List.map (fun r -> r.Run, r.Start))
                        $"the runs of node {node.Id} after the split"

                    for r in runs do
                        Expect.equal (Trace.whyAt g node r.Run) (cut k "cut" (Trace.whyAt twin twinNode r.Run)) $"why run {r.Run} of node {node.Id}"

                    if windowAfter k (Trace.events twin) twinNode.Id then
                        Expect.equal (Trace.whyNot g node) (Trace.whyNot twin twinNode) $"whyNot of node {node.Id}"
        ]
#endif
