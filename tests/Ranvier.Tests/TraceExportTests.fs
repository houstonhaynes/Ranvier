module Ranvier.Tests.TraceExportTests

open Expecto
open Ranvier

#if RANVIER_TRACE
let private events (from: int) (``to``: int) =
    TraceExportRecord.Events (from, ``to``, string from + ".." + string ``to`` + "\n")

let private waiting =
    {
        Capacity = 64
        Full = TraceExportFull.Wait
        Checkpoint = TraceExportCheckpoint.Never
        Codecs = TraceCodecs.ofList []
    }

// The last seq of the events the sink holds, 0 for none.
let private exportedTo (sink: MemoryTraceSink) =
    sink.Records
    |> List.fold
        (fun last record ->
            match record with
            | TraceExportRecord.Events (_, ``to``, _) -> ``to``
            | _ -> last)
        0

// The seqs of the events the sink holds, in stream order.
let private exportedSeqs (sink: MemoryTraceSink) =
    sink.Records
    |> List.collect (fun record ->
        match record with
        | TraceExportRecord.Events (from, ``to``, _) -> [ from..``to`` ]
        | TraceExportRecord.Gap (from, ``to``) -> failtestf "a gap from %d to %d" from ``to``
        | TraceExportRecord.Header _ -> [])

let private dropping =
    { waiting with
        Full = TraceExportFull.Drop
    }

// The seqs of the events and gaps the sink holds, in stream order.
let private streamSeqs (sink: MemoryTraceSink) =
    sink.Records
    |> List.collect (fun record ->
        match record with
        | TraceExportRecord.Events (from, ``to``, _)
        | TraceExportRecord.Gap (from, ``to``) -> [ from..``to`` ]
        | TraceExportRecord.Header _ -> [])

let private gaps (sink: MemoryTraceSink) =
    sink.Records
    |> List.choose (fun record ->
        match record with
        | TraceExportRecord.Gap (from, ``to``) -> Some (from, ``to``)
        | _ -> None)

let private checkpointSeq (graph: Graph) =
    (Trace.checkpointOf graph).Snapshot.Seq

#if !FABLE_COMPILER
// Runs the graph's dispatched work until the export completes, for a sink whose writes complete off the graph's thread.
let private pumpUntilComplete (graph: Graph) (export: TraceExport) =
    let deadline = System.DateTime.UtcNow.AddSeconds 30.0

    while not export.Completion.IsCompleted do
        if System.DateTime.UtcNow > deadline then
            failtest "the export did not complete within 30 s"

        if graph.Pump () = 0 then
            System.Threading.Thread.Sleep 1

    Expect.isFalse export.Completion.IsFaulted "the export completes without a fault"
#endif

type private Opaque = { Value: int }

type private ThrowingSink() =
    interface ITraceSink with
        member _.Name = "throwing"

        member _.Write _ =
            raise (System.InvalidOperationException "the sink is down")
#endif

[<Tests>]
let tests =
    testList
        "TraceExport"
        [
#if RANVIER_TRACE
            test "TryWrite returns false once the queued events reach the capacity" {
                let sink = TraceSinks.memory ()
                sink.Stall ()
                let queue = TraceExportQueue (sink, 4)

                Expect.isTrue (queue.TryWrite (events 1 3, 3)) "three events fit"
                Expect.isFalse (queue.TryWrite (events 4 5, 2)) "five events exceed the capacity"
                Expect.isTrue (queue.TryWrite (events 4 4, 1)) "four events fit"
                Expect.isFalse (queue.TryWrite (events 5 5, 1)) "the queue is full"
                Expect.isTrue (queue.TryWrite (TraceExportRecord.Gap (5, 5), 0)) "a record of no events fits"
                Expect.equal queue.Held 4 "the queue holds four events"
            }

            test "the capacity counts events, not records" {
                let sink = TraceSinks.memory ()
                sink.Stall ()
                let queue = TraceExportQueue (sink, 4)

                for seq in 1..4 do
                    Expect.isTrue (queue.TryWrite (events seq seq, 1)) ("record " + string seq)

                Expect.isFalse (queue.TryWrite (events 5 5, 1)) "the fifth event"

                let wide = TraceExportQueue (TraceSinks.memory (), 4)
                Expect.isFalse (wide.TryWrite (events 1 5, 5)) "a record wider than the capacity never fits"
            }

            test "a completed write frees its events, and the sink holds the records in order" {
                let sink = TraceSinks.memory ()
                sink.Stall ()
                let queue = TraceExportQueue (sink, 4)
                let mutable freed = 0
                queue.Freed <- fun _ -> freed <- freed + 1

                queue.TryWrite (events 1 2, 2) |> ignore
                queue.TryWrite (events 3 4, 2) |> ignore
                Expect.isEmpty sink.Records "a stalled sink holds nothing"
                Expect.isFalse (queue.TryWrite (events 5 5, 1)) "the queue is full"

                sink.Release ()

                Expect.equal queue.Held 0 "every write completed"
                Expect.isGreaterThan freed 0 "the queue reported the freed slots"
                Expect.isTrue (queue.TryWrite (events 5 5, 1)) "the freed slots take a record"
                Expect.sequenceEqual sink.Records [ events 1 2; events 3 4; events 5 5 ] "the records in write order"
                Expect.equal sink.Text "1..2\n3..4\n5..5\n" "the text of the records"
            }

            test "Dispose stops the export and completes Completion once the sink holds every record" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                createEffect (fun () -> s.Value |> ignore)
                let sink = TraceSinks.memory ()
                sink.Stall ()
                let export = Trace.export g sink waiting
                s.Value <- 1
                let last = Trace.lastSeq g
                export.Dispose ()

                Expect.isFalse export.Completion.IsCompleted "the sink holds back the writes"

                s.Value <- 2
                sink.Release ()

                Expect.isTrue export.Completion.IsCompleted "the sink holds every record"
                Expect.equal (exportedTo sink) last "the events up to Dispose"
            }

            test "a write completed off the graph's thread resumes the export through Graph.Dispatch" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                createEffect (fun () -> s.Value |> ignore)
                let sink = TraceSinks.memory ()
                sink.Stall ()
                let export = Trace.export g sink waiting

                for i in 1..1000 do
                    s.Value <- i

                // A dedicated thread: Task.Wait may run a queued task inline on the graph's thread.
                let releasing = System.Threading.Thread (fun () -> sink.Release ())
                releasing.Start ()
                releasing.Join ()
                Expect.isLessThan (exportedTo sink) (Trace.lastSeq g) "the rest waits for the graph's thread"

                Expect.equal (g.Pump ()) 1 "the pump ran the dispatched resume"
                Expect.equal (exportedTo sink) (Trace.lastSeq g) "the dispatched resume exported the rest"

                export.Dispose ()
                Expect.isTrue export.Completion.IsCompleted "the sink holds every record"
                Expect.sequenceEqual (exportedSeqs sink) [ 1 .. Trace.lastSeq g ] "every seq, once and in order"
            }

#if !FABLE_COMPILER
            test "under Wait, Completion settles off the graph's thread after Dispose on a full queue" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                createEffect (fun () -> s.Value |> ignore)
                let sink = TraceSinks.memory ()
                sink.Stall ()
                let export = Trace.export g sink { waiting with Capacity = 8 }

                for i in 1..50 do
                    s.Value <- i

                let last = Trace.lastSeq g
                export.Dispose ()

                let releasing =
                    System.Threading.Thread (fun () ->
                        System.Threading.Thread.Sleep 50
                        sink.Release ())

                releasing.Start ()
                let settled = export.Completion.Wait 5000
                releasing.Join ()

                Expect.isTrue settled "Completion settles while the graph's thread waits on it"
                Expect.sequenceEqual (exportedSeqs sink) [ 1..last ] "every seq up to Dispose, once and in order"
            }

            test "a checkpoint taken between an off-thread Dispose and its run ends the gap at Dispose" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                createEffect (fun () -> s.Value |> ignore)
                let sink = TraceSinks.memory ()
                let export = Trace.export g sink waiting

                for i in 1..10 do
                    s.Value <- i

                let last = Trace.lastSeq g
                let disposing = System.Threading.Thread (fun () -> export.Dispose ())
                disposing.Start ()
                disposing.Join ()

                for _ in 1..5 do
                    createSignal 0 |> ignore

                Trace.checkpoint g (Trace.lastSeq g) "elsewhere"
                g.Pump () |> ignore

                Expect.isTrue export.Completion.IsCompleted "the sink holds every record"
                Expect.sequenceEqual (streamSeqs sink) [ 1..last ] "the stream ends at Dispose"
            }
#endif

            test "Drop writes the dropped seqs in one gap once the stalled sink frees a slot" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                createEffect (fun () -> s.Value |> ignore)
                let sink = TraceSinks.memory ()
                sink.Stall ()
                let export = Trace.export g sink dropping

                for i in 1..1000 do
                    s.Value <- i

                let last = Trace.lastSeq g
                Expect.isGreaterThan export.Dropped 0 "the full queue dropped events"

                sink.Release ()

                match gaps sink with
                | [ (from, ``to``) ] -> Expect.equal (``to`` - from + 1) export.Dropped "the gap names every dropped seq"
                | other -> failtestf "one gap before Dispose: %A" other

                export.Dispose ()
                Expect.isTrue export.Completion.IsCompleted "the sink holds every record"
                Expect.sequenceEqual (streamSeqs sink) [ 1..last ] "the events and the gap cover every seq once, in order"
            }

            test "Drop writes the pending gap when Dispose follows the drop" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                createEffect (fun () -> s.Value |> ignore)
                let sink = TraceSinks.memory ()
                sink.Stall ()
                let export = Trace.export g sink dropping

                for i in 1..1000 do
                    s.Value <- i

                let last = Trace.lastSeq g
                export.Dispose ()
                sink.Release ()

                Expect.isTrue export.Completion.IsCompleted "the sink holds every record"
                Expect.equal (List.length (gaps sink)) 1 "one gap"
                Expect.sequenceEqual (streamSeqs sink) [ 1..last ] "the events and the gap cover every seq once, in order"
#if !FABLE_COMPILER
                let dump = TraceContract.parseDumpWith dropping.Codecs sink.Text
                Expect.equal dump.Gaps (gaps sink) "the stream's gap parses"

                Expect.sequenceEqual
                    (dump.Events |> Array.map _.Seq)
                    (streamSeqs sink
                     |> List.except
                         [
                             for from, ``to`` in gaps sink do
                                 yield! [ from..``to`` ]
                         ])
                    "the stream's events parse"
#endif
            }

            // Sequenced: the memory reading is process-wide.
            testSequenced
            <| test "Enqueued keeps the log and the process memory bounded under a stalled sink with Drop" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                createEffect (fun () -> s.Value |> ignore)
                let sink = TraceSinks.memory ()
                sink.Stall ()

                let export =
                    Trace.export
                        g
                        sink
                        { dropping with
                            Checkpoint = TraceExportCheckpoint.Enqueued
                        }

                for i in 1..10000 do
                    s.Value <- i

                let bound = System.GC.GetTotalMemory true

                for i in 10001..100000 do
                    s.Value <- i

                let used = System.GC.GetTotalMemory true
                let last = Trace.lastSeq g
                Expect.isLessThan used (bound + 1_000_000L) "90,000 more writes hold under 1 MB more"

                Expect.isLessThanOrEqual (TraceGate.logOf g).Events.Length dropping.Capacity "the log retains at most a queue of events"

                Expect.equal (Trace.checkpointOf g).Name "memory" "the checkpoint carries the sink's name"

                sink.Release ()
                export.Dispose ()

                Expect.isTrue export.Completion.IsCompleted "the sink holds every record"
                Expect.equal (List.length (gaps sink)) 1 "one gap"
                Expect.sequenceEqual (streamSeqs sink) [ 1..last ] "the events and the gap cover every seq once, in order"
            }

            test "Enqueued checkpoints once the exported events past the checkpoint reach the capacity" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                createEffect (fun () -> s.Value |> ignore)
                let sink = TraceSinks.memory ()

                let policy =
                    { waiting with
                        Checkpoint = TraceExportCheckpoint.Enqueued
                    }

                let export = Trace.export g sink policy
                s.Value <- 1

                Expect.isLessThan (Trace.lastSeq g) policy.Capacity "fewer events than the capacity"
                Expect.equal (checkpointSeq g) 0 "fewer exported events than the capacity take no checkpoint"

                for i in 2..1000 do
                    s.Value <- i

                    Expect.isLessThan (Trace.lastSeq g - checkpointSeq g) policy.Capacity "the log retains fewer events than the capacity"

                Expect.isGreaterThan (checkpointSeq g) 0 "the exported events took a checkpoint"
                export.Dispose ()

                Expect.isTrue export.Completion.IsCompleted "the sink holds every record"
                Expect.equal (checkpointSeq g) (Trace.lastSeq g) "the stream's end checkpoints every event"
                Expect.sequenceEqual (exportedSeqs sink) [ 1 .. Trace.lastSeq g ] "every seq, once and in order"
            }

            test "Enqueued checkpoints before a NodeNew whose Label follows it" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = Trace.named "count" (fun () -> createSignal 0)

                let created, _ =
                    Trace.eventsSince g 0
                    |> Array.pairwise
                    |> Array.find (fun (e, l) ->
                        e.Kind = TraceEventKind.NodeNew
                        && l.Kind = TraceEventKind.Label)

                let sink = TraceSinks.memory ()
                sink.Stall ()

                let export =
                    Trace.export
                        g
                        sink
                        { waiting with
                            Capacity = created.Seq
                            Checkpoint = TraceExportCheckpoint.Enqueued
                        }

                Expect.equal (checkpointSeq g) (created.Seq - 1) "the checkpoint precedes the NodeNew"

                s.Value <- 1
                sink.Release ()
                export.Dispose ()

                Expect.isTrue export.Completion.IsCompleted "the sink holds every record"
                Expect.equal (checkpointSeq g) (Trace.lastSeq g) "the checkpoint follows the stream's end"
                Expect.equal (Trace.checkpointOf g).Snapshot.Nodes[created.Node].Label (Some "count") "the checkpoint keeps the label"
            }

            test "Acknowledged checkpoints once the sink completes a write" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                createEffect (fun () -> s.Value |> ignore)
                let sink = TraceSinks.memory ()
                sink.Stall ()

                let export =
                    Trace.export
                        g
                        sink
                        { waiting with
                            Checkpoint = TraceExportCheckpoint.Acknowledged
                        }

                for i in 1..100 do
                    s.Value <- i

                Expect.equal (checkpointSeq g) 0 "a stalled sink acknowledges nothing"

                sink.Release ()

                Expect.isGreaterThan (checkpointSeq g) 0 "the acknowledged events took a checkpoint"

                Expect.isLessThan
                    (exportedTo sink - checkpointSeq g)
                    waiting.Capacity
                    "fewer acknowledged events than the capacity stay past the checkpoint"

                Expect.equal (Trace.checkpointOf g).Name "memory" "the checkpoint carries the sink's name"

                export.Dispose ()
                Expect.isTrue export.Completion.IsCompleted "the sink holds every record"
                Expect.equal (checkpointSeq g) (Trace.lastSeq g) "every event is acknowledged"
            }

            test "a checkpoint taken past the cursor yields a gap" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                createEffect (fun () -> s.Value |> ignore)
                let sink = TraceSinks.memory ()
                sink.Stall ()
                let export = Trace.export g sink waiting

                for i in 1..100 do
                    s.Value <- i

                let folded = Trace.lastSeq g
                Trace.checkpoint g folded "elsewhere"

                for i in 101..110 do
                    s.Value <- i

                sink.Release ()
                export.Dispose ()

                Expect.isTrue export.Completion.IsCompleted "the sink holds every record"
                Expect.equal export.Dropped 0 "a checkpoint drops no event"

                match gaps sink with
                | [ (_, ``to``) ] -> Expect.equal ``to`` folded "the gap ends at the checkpoint"
                | other -> failtestf "one gap: %A" other

                Expect.sequenceEqual (streamSeqs sink) [ 1 .. Trace.lastSeq g ] "the events and the gap cover every seq once, in order"
            }

            test "a throwing sink faults Completion, and the graph keeps running" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                let mutable seen = 0
                createEffect (fun () -> seen <- s.Value)
                let export = Trace.export g (ThrowingSink ()) waiting

                s.Value <- 1
                s.Value <- 2

                Expect.equal seen 2 "the effect ran"
                Expect.isTrue export.Completion.IsFaulted "the sink's error faults Completion"

                Expect.equal (export.Completion.Exception.GetBaseException ()).Message "the sink is down" "the sink's error"
            }

            test "a strict codec faults Completion, and the graph keeps running" {
                use g = new Graph ()
                use _ = g.Activate ()
                let sink = TraceSinks.memory ()

                let export =
                    Trace.export
                        g
                        sink
                        { waiting with
                            Codecs = TraceCodecs.strict (TraceCodecs.ofList [])
                        }

                let s = createSignal { Value = 0 }
                let mutable seen = 0
                createEffect (fun () -> seen <- s.Value.Value)
                s.Value <- { Value = 1 }
                s.Value <- { Value = 2 }

                Expect.equal seen 2 "the effect ran"
                Expect.isTrue export.Completion.IsFaulted "the codec's error faults Completion"

                Expect.isTrue (export.Completion.Exception.GetBaseException () :? TraceCodecException) "a TraceCodecException"
            }

#if !FABLE_COMPILER
            // .NET only: the JSONL sink writes a file.
            test "Wait loses no event in a JSONL file, and the file is the dumpTextWith text" {
                let path = System.IO.Path.GetTempFileName ()

                try
                    use g = new Graph ()
                    use _ = g.Activate ()
                    let s = createSignal 0
                    createEffect (fun () -> s.Value |> ignore)
                    let sink = TraceSinks.jsonl path
                    let export = Trace.export g sink waiting

                    for i in 1..10000 do
                        s.Value <- i

                    let expected = Trace.dumpTextWith g waiting.Codecs
                    export.Dispose ()
                    pumpUntilComplete g export
                    (sink :> System.IDisposable).Dispose()

                    Expect.equal export.Dropped 0 "no event dropped"
                    let text = System.IO.File.ReadAllText path
                    Expect.equal text expected "the file is the dumpTextWith text"
                    let dump = TraceContract.parseDumpWith waiting.Codecs text
                    Expect.equal dump.Gaps [] "no gap"
                    Expect.sequenceEqual (dump.Events |> Array.map _.Seq) [ 1 .. Trace.lastSeq g ] "every seq, once and in order"
                finally
                    System.IO.File.Delete path
            }

            test "Drop writes a JSONL file whose gaps name the dropped seqs" {
                let path = System.IO.Path.GetTempFileName ()

                try
                    use g = new Graph ()
                    use _ = g.Activate ()
                    let s = createSignal 0
                    createEffect (fun () -> s.Value |> ignore)
                    let sink = TraceSinks.jsonl path

                    let policy =
                        { dropping with
                            Capacity = 4
                            Checkpoint = TraceExportCheckpoint.Enqueued
                        }

                    let export = Trace.export g sink policy

                    for i in 1..10000 do
                        s.Value <- i
                        Expect.isLessThanOrEqual (TraceGate.logOf g).Events.Length policy.Capacity "the log retains at most a queue of events"

                    let last = Trace.lastSeq g
                    export.Dispose ()
                    pumpUntilComplete g export
                    (sink :> System.IDisposable).Dispose()

                    Expect.isGreaterThan export.Dropped 0 "the full queue dropped events"

                    let dump =
                        TraceContract.parseDumpWith policy.Codecs (System.IO.File.ReadAllText path)

                    let gapped =
                        [
                            for from, ``to`` in dump.Gaps do
                                yield! [ from..``to`` ]
                        ]

                    Expect.equal gapped.Length export.Dropped "the gaps name every dropped seq"

                    Expect.sequenceEqual
                        (List.sort (
                            (dump.Events |> Array.map _.Seq |> List.ofArray)
                            @ gapped
                        ))
                        [ 1..last ]
                        "the events and the gaps cover every seq once"
                finally
                    System.IO.File.Delete path
            }
#endif

            test "a gap record writes a gap line" {
                Expect.equal (TraceExportRecord.text (TraceExportRecord.Gap (3, 7))) "{\"gap\":{\"from\":3,\"to\":7}}\n" "the gap line"
            }
#endif
        ]
