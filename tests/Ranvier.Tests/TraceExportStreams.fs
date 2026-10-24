module Ranvier.Tests.TraceExportStreams

open Expecto
open Ranvier

#if RANVIER_TRACE
let private waiting =
    {
        Capacity = 64
        Full = TraceExportFull.Wait
        Checkpoint = TraceExportCheckpoint.Never
        Codecs = TraceCodecs.ofList []
    }

let private dropping =
    { waiting with
        Full = TraceExportFull.Drop
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

// Waits for the export's Completion. A memory sink's writes complete inline on .NET and on microtasks under Fable.
let private completion (export: TraceExport) : Async<unit> =
#if FABLE_COMPILER
    Async.AwaitTask export.Completion
#else
    async { Expect.isTrue export.Completion.IsCompleted "the sink holds every record" }
#endif
#endif

/// <summary>Memory-sink export streams, on .NET and Fable.</summary>
[<Tests>]
let tests =
    testList
        "TraceExportStreams"
        [
#if RANVIER_TRACE
            testCaseAsync "export writes the header first, then the retained events"
            <| async {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                s.Value <- 1
                let sink = TraceSinks.memory ()
                use _export = Trace.export g sink waiting
                do! drained ()

                match sink.Records with
                | TraceExportRecord.Header header :: _ ->
                    Expect.stringStarts header "{\"schema\":3," "a schema 3 header"
                    Expect.equal (header.Split('\n').Length) 3 "the header line and the snapshot line"
                | records -> failtestf "the first record is a header: %A" records

                Expect.equal (exportedTo sink) (Trace.lastSeq g) "the retained events"
            }

            testCaseAsync "the events of a flush are exported when it ends"
            <| async {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                let mutable seen = 0
                createEffect (fun () -> seen <- s.Value)
                let sink = TraceSinks.memory ()
                use _export = Trace.export g sink waiting

                s.Value <- 1
                do! drained ()

                Expect.equal seen 1 "the effect ran"
                Expect.equal (exportedTo sink) (Trace.lastSeq g) "the flush's events, with no Drain call"
            }

            testCaseAsync "Drain exports the events of a write that wakes no effect"
            <| async {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                let sink = TraceSinks.memory ()
                use export = Trace.export g sink waiting
                do! drained ()

                s.Value <- 1
                s.Value <- 2
                let before = exportedTo sink
                export.Drain ()
                do! drained ()

                Expect.isLessThan before (Trace.lastSeq g) "the writes ran no flush"
                Expect.equal (exportedTo sink) (Trace.lastSeq g) "Drain exported the writes"
            }

            testCaseAsync "Wait loses no event behind a stalled sink, and the stream is the dumpTextWith text"
            <| async {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                let mutable seen = 0
                createEffect (fun () -> seen <- s.Value)
                let sink = TraceSinks.memory ()
                sink.Stall ()
                let export = Trace.export g sink waiting

                for i in 1..10000 do
                    s.Value <- i

                let expected = Trace.dumpTextWith g waiting.Codecs
                let last = Trace.lastSeq g
                Expect.isLessThan (exportedTo sink) last "the stalled sink holds back the stream"

                sink.Release ()
                export.Dispose ()
                do! completion export

                Expect.equal export.Dropped 0 "no event dropped"
                Expect.sequenceEqual (streamSeqs sink) [ 1..last ] "every seq, once and in order"
                Expect.equal sink.Text expected "the header and events concatenate to dumpTextWith"
            }

            testCaseAsync "under Wait, the stream ends at Dispose when the queue is full and events follow it"
            <| async {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                createEffect (fun () -> s.Value |> ignore)
                let sink = TraceSinks.memory ()
                sink.Stall ()
                let export = Trace.export g sink { waiting with Capacity = 7 }

                for i in 1..50 do
                    s.Value <- i

                let last = Trace.lastSeq g
                export.Dispose ()

                for i in 51..100 do
                    s.Value <- i

                sink.Release ()
                do! completion export

                Expect.sequenceEqual (streamSeqs sink) [ 1..last ] "every seq up to Dispose, once and in order"
            }

            testCaseAsync "Drop with Enqueued bounds the log under a stalled sink, and one gap names the dropped seqs"
            <| async {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                createEffect (fun () -> s.Value |> ignore)
                let sink = TraceSinks.memory ()
                sink.Stall ()

                let policy =
                    { dropping with
                        Checkpoint = TraceExportCheckpoint.Enqueued
                    }

                let export = Trace.export g sink policy

                for i in 1..10000 do
                    s.Value <- i

                let last = Trace.lastSeq g
                Expect.isGreaterThan export.Dropped 0 "the full queue dropped events"

                Expect.isLessThan (last - (Trace.checkpointOf g).Snapshot.Seq) policy.Capacity "the log retains fewer events than a queue"

                export.Dispose ()
                sink.Release ()
                do! completion export

                match gaps sink with
                | [ (from, ``to``) ] -> Expect.equal (``to`` - from + 1) export.Dropped "the gap names every dropped seq"
                | other -> failtestf "one gap: %A" other

                Expect.sequenceEqual (streamSeqs sink) [ 1..last ] "the events and the gap cover every seq once, in order"
            }
#endif
        ]
