module Ranvier.Tests.Streams

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open Ranvier

/// <summary>One start of a <c>ManualStream</c>: its sink, its token and the task the test completes.</summary>
type StreamStart<'T> =
    {
        Sink: StreamSink<'T>
        Token: CancellationToken
        Done: TaskCompletionSource<unit>
    }

/// <summary>A stream source the test drives: every start records its sink, token and completion.</summary>
type ManualStream<'T>() =
    let starts = ResizeArray<StreamStart<'T>>()

    /// <summary>A source whose every <c>Run</c> records a start; the same object serves every restart.</summary>
    member val Source =
        { new StreamSource<'T>() with
            member _.Run(sink, token) =
                let start =
                    {
                        Sink = sink
                        Token = token
                        Done = TaskCompletionSource<unit>()
                    }

                starts.Add start
                start.Done.Task :> Task
        }

    member _.Starts = starts.Count
    member _.Latest = starts[starts.Count - 1]

    member _.At(index: int) =
        starts[index]

    member this.Push(item: 'T) =
        this.Latest.Sink.Next item

    member this.Complete() =
        this.Latest.Done.SetResult ()

    member this.Fail(error: exn) =
        this.Latest.Done.SetException error

#if !FABLE_COMPILER
/// <summary>Runs <c>work</c> on a thread other than the caller's and waits for it.</summary>
let private offThread (work: unit -> unit) =
    let thread = Thread (ThreadStart work)
    thread.Start ()
    thread.Join ()

let private manualGraph () =
    new Graph (
        { GraphOptions.Default with
            Dispatcher = Some (ManualDispatcher () :> IGraphDispatcher)
        }
    )

#endif
let private reading (memo: StreamMemo<'S>) =
    memo.TryValue

[<Tests>]
let tests =
    testList
        "Stream memos"
        [
            testCase "a stream starts on its first read, not at creation"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStream (fun _ -> feed.Source))
                Expect.equal feed.Starts 0 "not started at creation"
                Expect.equal (reading memo) Pending "pending before the first item"
                Expect.equal feed.Starts 1 "started by the read"

            testCase "an item publishes the latest state; a fold accumulates"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<string>()
                let latest = graph.Run (fun () -> Api.createStream (fun _ -> feed.Source))

                let joined =
                    graph.Run (fun () -> Api.createStreamFold "" (fun state item -> state + item) (fun _ -> feed.Source))

                reading latest |> ignore
                reading joined |> ignore
                feed.At(0).Sink.Next "a"
                feed.At(0).Sink.Next "b"
                feed.At(1).Sink.Next "x"
                feed.At(1).Sink.Next "y"
                Expect.equal (reading latest) (Ready "b") "latest item"
                Expect.equal (reading joined) (Ready "xy") "folded items"

            testCase "an effect sees one run per item on the graph thread, and one per batch"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> feed.Source))
                let seen = ResizeArray<int>()

                new Effect (
                    graph,
                    fun () ->
                        match memo.TryValue with
                        | Ready v -> seen.Add v
                        | _ -> ()
                )
                |> ignore

                feed.Push 1
                feed.Push 2

                graph.Run (fun () ->
                    Api.batch (fun () ->
                        feed.Push 3
                        feed.Push 4))

                Expect.sequenceEqual seen [ 1; 3; 10 ] "one run per item outside a batch, one for the batch"

            testCase "items delivered while Run has not returned publish when it returns"
            <| fun _ ->
                use graph = new Graph ()

                let eager =
                    { new StreamSource<int>() with
                        member _.Run(sink, _) =
                            sink.Next 1
                            sink.Next 2
                            TaskCompletionSource<unit>().Task :> Task
                    }

                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> eager))
                Expect.equal (reading memo) (Ready 3) "the synchronous prefix is published by the first read"

            testCase "completion keeps the last state"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStream (fun _ -> feed.Source))
                reading memo |> ignore
                feed.Push 5
                feed.Complete ()
                Expect.equal (reading memo) (Ready 5) "ready with the last item"

            testCase "empty completion gives seed for a fold and InvalidOperationException for createStream"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let latest = graph.Run (fun () -> Api.createStream (fun _ -> feed.Source))
                let folded = graph.Run (fun () -> Api.createStreamFold 7 (+) (fun _ -> feed.Source))
                reading latest |> ignore
                reading folded |> ignore
                feed.At(0).Done.SetResult()
                feed.At(1).Done.SetResult()

                match reading latest with
                | Failed (:? InvalidOperationException) -> ()
                | other -> failtestf "expected InvalidOperationException, got %A" other

                Expect.equal (reading folded) (Ready 7) "seed"

            testCase "a stream failure publishes Failed with the stream memo as origin"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStream (fun _ -> feed.Source))
                reading memo |> ignore
                feed.Push 1
                let boom = Exception "boom"
                feed.Fail boom

                match reading memo with
                | Failed error -> Expect.equal error.Message "boom" "the stream's exception"
                | other -> failtestf "expected Failed, got %A" other

                Expect.isTrue (obj.ReferenceEquals (memo.ErrorOrigin, memo)) "origin"

            testCase "a dependency change cancels the stream and restarts it from seed"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let topic = Signal (graph, "a")

                let memo =
                    graph.Run (fun () ->
                        Api.createStreamFold 0 (+) (fun _ ->
                            topic.Value |> ignore
                            feed.Source))

                reading memo |> ignore
                feed.Push 4
                let first = feed.Latest
                topic.Value <- "b"
                Expect.equal (reading memo) Pending "pending until the new stream's first item"
                Expect.isTrue first.Token.IsCancellationRequested "the old token is cancelled"
                Expect.equal feed.Starts 2 "restarted"
                first.Sink.Next 100
                feed.Push 1
                Expect.equal (reading memo) (Ready 1) "reset to seed; the old sink's item is ignored"

            testCase "a start that returns the same StreamSource object on every restart"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let topic = Signal (graph, 0)

                let memo =
                    graph.Run (fun () ->
                        Api.createStream (fun _ ->
                            topic.Value |> ignore
                            feed.Source))

                reading memo |> ignore
                topic.Value <- 1
                reading memo |> ignore
                Expect.isFalse (obj.ReferenceEquals (feed.At(0).Sink, feed.At(1).Sink)) "a fresh sink per start"
                feed.At(0).Sink.Next 9
                Expect.equal (reading memo) Pending "the first start's sink is closed"
                feed.At(1).Sink.Next 2
                Expect.equal (reading memo) (Ready 2) "the second start's sink publishes"

            testCase "a pending read in start keeps the node pending and starts it when the read settles"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let gate = AsyncSource<int>(graph)

                let memo =
                    graph.Run (fun () ->
                        Api.createStream (fun _ ->
                            gate.Value |> ignore
                            feed.Source))

                let runs = ref 0

                new Effect (
                    graph,
                    fun () ->
                        memo.TryValue |> ignore
                        runs.Value <- runs.Value + 1
                )
                |> ignore

                Expect.equal feed.Starts 0 "start suspended on the gate"
                Expect.equal runs.Value 1 "the effect ran once while pending"
                gate.Settle 1
                Expect.equal feed.Starts 1 "started once the gate settled"
                feed.Push 5
                Expect.equal (reading memo) (Ready 5) "the started stream publishes"

            testCase "a misbehaving source's items after completion are ignored"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStream (fun _ -> feed.Source))
                reading memo |> ignore
                feed.Push 1
                feed.Complete ()
                feed.Push 2
                Expect.equal (reading memo) (Ready 1) "nothing republished after completion"

            testCase "Run throwing synchronously, and Run returning null, both publish Failed"
            <| fun _ ->
                use graph = new Graph ()

                let throwing =
                    { new StreamSource<int>() with
                        member _.Run(_, _) =
                            raise (Exception "sync")
                    }

                let nothing =
                    { new StreamSource<int>() with
                        member _.Run(_, _) = null
                    }

                let a = graph.Run (fun () -> Api.createStream (fun _ -> throwing))
                let b = graph.Run (fun () -> Api.createStream (fun _ -> nothing))

                match reading a, reading b with
                | Failed error, Failed (:? InvalidOperationException) -> Expect.equal error.Message "sync" "thrown"
                | other -> failtestf "expected two failures, got %A" other

            testCase "a fold that throws on the second item"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let topic = Signal (graph, 0)

                let memo =
                    graph.Run (fun () ->
                        Api.createStreamFold 0 (fun state item -> if item = 2 then failwith "fold" else state + item) (fun _ ->
                            topic.Value |> ignore
                            feed.Source))

                reading memo |> ignore
                feed.Push 1
                feed.Push 2

                match reading memo with
                | Failed error -> Expect.equal error.Message "fold" "fold's exception"
                | other -> failtestf "expected Failed, got %A" other

                Expect.isTrue feed.Latest.Token.IsCancellationRequested "token cancelled"
                topic.Value <- 1
                reading memo |> ignore
                feed.Push 3
                Expect.equal (reading memo) (Ready 3) "a fresh stream from seed"

            testCase "an equal published state notifies no reader"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStream (fun _ -> feed.Source))
                let mutable runs = 0

                new Effect (
                    graph,
                    fun () ->
                        memo.TryValue |> ignore
                        runs <- runs + 1
                )
                |> ignore

                feed.Push 1
                let after = runs
                feed.Push 1
                Expect.equal runs after "equal state, no run"

            testCase "disposing a pending stream memo publishes ObjectDisposedException and cancels the token"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStream (fun _ -> feed.Source))
                reading memo |> ignore
                memo.Dispose ()
                Expect.isTrue feed.Latest.Token.IsCancellationRequested "cancelled"

                match reading memo with
                | Failed error when isDisposedError error -> ()
                | other -> failtestf "expected ObjectDisposedException, got %A" other

                feed.Push 1
                Expect.isTrue (memo.TryValue.IsFailed) "items after disposal are ignored"

            testCase "owner disposal disposes the stream memo"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()

                let owner, memo =
                    graph.Run (fun () -> Api.createRoot (fun owner -> owner, Api.createStream (fun _ -> feed.Source)))

                reading memo |> ignore
                owner.Dispose ()
                Expect.isTrue feed.Latest.Token.IsCancellationRequested "cancelled with its owner"

            testCase "an effect that disposes the stream memo while reacting to its publication"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStream (fun _ -> feed.Source))
                let seen = ResizeArray<int>()

                new Effect (
                    graph,
                    fun () ->
                        match memo.TryValue with
                        | Ready v ->
                            seen.Add v
                            memo.Dispose ()
                        | _ -> ()
                )
                |> ignore

                feed.Push 1
                feed.Push 2
                Expect.sequenceEqual seen [ 1 ] "no publication after disposal"

            testCase "a pure memo that creates a stream memo fails"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()

                let bad =
                    graph.Run (fun () ->
                        Api.createMemo (fun _ ->
                            Api.createStream (fun _ -> feed.Source) |> ignore
                            0))

                Expect.throwsT<InvalidOperationException> (fun () -> graph.Run (fun () -> bad.Value) |> ignore) "owned node in a pure body"

            testCase "a closed stream's items are not folded"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let topic = Signal (graph, 0)
                let folds = ref 0

                let memo =
                    graph.Run (fun () ->
                        Api.createStreamFold
                            0
                            (fun state item ->
                                folds.Value <- folds.Value + 1
                                state + item)
                            (fun _ ->
                                topic.Value |> ignore
                                feed.Source))

                reading memo |> ignore
                feed.Push 1
                feed.Complete ()
                feed.Push 2
                Expect.equal folds.Value 1 "nothing folded after completion"
                topic.Value <- 1
                reading memo |> ignore
                feed.At(0).Sink.Next 3
                Expect.equal folds.Value 1 "nothing folded by a restarted stream's old sink"

            testCase "a completion from a replaced stream leaves the running stream alone"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let topic = Signal (graph, 0)

                let memo =
                    graph.Run (fun () ->
                        Api.createStream (fun _ ->
                            topic.Value |> ignore
                            feed.Source))

                reading memo |> ignore
                topic.Value <- 1
                reading memo |> ignore
                feed.At(0).Done.SetResult()
                Expect.equal (reading memo) Pending "the new stream is still waiting for its first item"
                Expect.isFalse feed.Latest.Token.IsCancellationRequested "the running stream is not cancelled"
                feed.Push 3
                Expect.equal (reading memo) (Ready 3) "the running stream publishes"

            testCase "items awaiting a wake are not published once a dependency change has made the stream stale"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let topic = Signal (graph, 0)

                let memo =
                    graph.Run (fun () ->
                        Api.createStream (fun _ ->
                            topic.Value |> ignore
                            feed.Source))

                reading memo |> ignore

                graph.Run (fun () ->
                    Api.batch (fun () ->
                        feed.Push 3
                        topic.Value <- 1))

                Expect.isTrue (memo.Status.HasFlag Status.Pending) "the stale stream's item is not published"

            testCase "items folded in a batch publish when a Check upstream resolves clean"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let topic = Signal (graph, 1)

                let positive = graph.Run (fun () -> Api.createMemo (fun _ -> topic.Value > 0))

                let memo =
                    graph.Run (fun () ->
                        Api.createStream (fun _ ->
                            positive.Value |> ignore
                            feed.Source))

                reading memo |> ignore

                graph.Run (fun () ->
                    Api.batch (fun () ->
                        feed.Push 5
                        topic.Value <- 2))

                Expect.isFalse (memo.Status.HasFlag Status.Pending) "published although the upstream was only checked"
                Expect.equal memo.Peek 5 "the item"

            testCase "graph-thread items in a batch publish once, when the batch ends"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> feed.Source))
                let seen = ResizeArray<int>()

                new Effect (
                    graph,
                    fun () ->
                        match memo.TryValue with
                        | Ready v -> seen.Add v
                        | _ -> ()
                )
                |> ignore

                feed.Push 1
                seen.Clear ()
                let mutable inside = Unchecked.defaultof<Reading<int>>

                graph.Run (fun () ->
                    Api.batch (fun () ->
                        feed.Push 2
                        feed.Push 3
                        inside <- Ready memo.Peek))

                Expect.equal inside (Ready 1) "unpublished while the batch runs"
                Expect.sequenceEqual seen [ 6 ] "one effect run with both items"
                Expect.equal (reading memo) (Ready 6) "published when the batch ends"

            testCase "completion after a graph-thread item keeps the item"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> feed.Source))
                reading memo |> ignore

                graph.Run (fun () ->
                    Api.batch (fun () ->
                        feed.Push 4
                        feed.Complete ()))

                Expect.equal (reading memo) (Ready 4) "the item completed inside the batch is published"

            testCase "a pending read inside Run fails the node"
            <| fun _ ->
                use graph = new Graph ()
                let gate = AsyncSource<int>(graph)

                let reader =
                    { new StreamSource<int>() with
                        member _.Run(_, _) =
                            gate.Value |> ignore
                            TaskCompletionSource<unit>().Task :> Task
                    }

                let memo = graph.Run (fun () -> Api.createStream (fun _ -> reader))

                match reading memo with
                | Failed (:? InvalidOperationException as error) ->
                    match error.InnerException with
                    | NotReadyException _ -> ()
                    | inner -> failtestf "expected NotReadyException inside, got %A" inner
                | other -> failtestf "expected InvalidOperationException, got %A" other

            testCase "disposing the stream memo inside start leaves the source unrun"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let memo = ref Unchecked.defaultof<StreamMemo<int>>

                memo.Value <-
                    graph.Run (fun () ->
                        Api.createStream (fun _ ->
                            memo.Value.Dispose ()
                            feed.Source))

                match reading memo.Value with
                | Failed error when isDisposedError error -> ()
                | other -> failtestf "expected ObjectDisposedException, got %A" other

                Expect.equal feed.Starts 0 "Run was not called"

#if !FABLE_COMPILER
            // Off-thread delivery and a manual dispatcher have no synchronous Fable equivalent.
            testCase "an off-thread item of a restarted stream publishes although a wake of the previous stream is queued"
            <| fun _ ->
                use graph =
                    new Graph (
                        { GraphOptions.Default with
                            Dispatcher = Some (ManualDispatcher () :> IGraphDispatcher)
                        }
                    )

                let feed = ManualStream<int>()
                let topic = Signal (graph, 0)

                let memo =
                    graph.Run (fun () ->
                        Api.createStream (fun _ ->
                            topic.Value |> ignore
                            feed.Source))

                reading memo |> ignore

                graph.Run (fun () ->
                    topic.Value <- 1
                    offThread (fun () -> feed.At(0).Sink.Next 1)
                    reading memo |> ignore
                    offThread (fun () -> feed.At(1).Sink.Next 2))

                Expect.equal feed.Starts 2 "restarted"
                graph.Pump () |> ignore
                Expect.equal (reading memo) (Ready 2) "the second stream's item is published"

            testCase "a wake of the previous stream leaves the restarted stream pending until its first item"
            <| fun _ ->
                use graph =
                    new Graph (
                        { GraphOptions.Default with
                            Dispatcher = Some (ManualDispatcher () :> IGraphDispatcher)
                        }
                    )

                let feed = ManualStream<int>()
                let topic = Signal (graph, 0)

                let memo =
                    graph.Run (fun () ->
                        Api.createStream (fun _ ->
                            topic.Value |> ignore
                            feed.Source))

                reading memo |> ignore

                graph.Run (fun () ->
                    topic.Value <- 1
                    offThread (fun () -> feed.At(0).Sink.Next 1)
                    reading memo |> ignore)

                graph.Pump () |> ignore
                Expect.equal (reading memo) Pending "no item from the second stream yet"

            testCase "1,000 off-thread items before a pump give one wake and one effect run"
            <| fun _ ->
                use graph = manualGraph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> feed.Source))
                let seen = ResizeArray<int>()

                new Effect (
                    graph,
                    fun () ->
                        match memo.TryValue with
                        | Ready v -> seen.Add v
                        | _ -> ()
                )
                |> ignore

                graph.Pump () |> ignore
                seen.Clear ()

                offThread (fun () ->
                    for i in 1..1000 do
                        feed.Push i)

                Expect.equal graph.PendingWork 1 "one wake queued"
                graph.Pump () |> ignore
                Expect.sequenceEqual seen [ 500500 ] "one run with the final state"

            testCase "an item delivered while the wake runs posts exactly one further wake"
            <| fun _ ->
                use graph = manualGraph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> feed.Source))
                let seen = ResizeArray<int>()
                let mutable injected = false

                new Effect (
                    graph,
                    fun () ->
                        match memo.TryValue with
                        | Ready v ->
                            seen.Add v

                            if not injected then
                                injected <- true
                                offThread (fun () -> feed.Push 10)
                        | _ -> ()
                )
                |> ignore

                graph.Pump () |> ignore
                offThread (fun () -> feed.Push 1)
                let ran = graph.Pump ()
                Expect.equal ran 2 "the first wake and exactly one further wake"
                Expect.equal graph.PendingWork 0 "nothing left queued"
                Expect.sequenceEqual seen [ 1; 11 ] "the later item's state is published"

            testCase "an off-thread item delivered during a wake that runs inline posts a further wake"
            <| fun _ ->
                use graph = manualGraph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> feed.Source))
                let seen = ResizeArray<int>()
                let mutable injected = false

                new Effect (
                    graph,
                    fun () ->
                        match memo.TryValue with
                        | Ready v ->
                            seen.Add v

                            if not injected then
                                injected <- true
                                offThread (fun () -> feed.Push 10)
                        | _ -> ()
                )
                |> ignore

                graph.Pump () |> ignore
                feed.Push 1
                Expect.equal graph.PendingWork 1 "the item during the inline wake posted one wake"
                graph.Pump () |> ignore
                Expect.sequenceEqual seen [ 1; 11 ] "the later item's state is published"

            testCase "a graph-thread item and an off-thread item before the same pump both land"
            <| fun _ ->
                use graph = manualGraph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> feed.Source))
                let seen = ResizeArray<int>()

                new Effect (
                    graph,
                    fun () ->
                        match memo.TryValue with
                        | Ready v -> seen.Add v
                        | _ -> ()
                )
                |> ignore

                graph.Pump () |> ignore
                offThread (fun () -> feed.Push 1)
                Expect.equal graph.PendingWork 1 "the off-thread item posted a wake"
                feed.Push 2
                Expect.equal (reading memo) (Ready 3) "the graph-thread item publishes both items without a pump"
                graph.Pump () |> ignore
                Expect.equal (reading memo) (Ready 3) "the pump adds nothing"
                Expect.sequenceEqual seen [ 3 ] "one effect run"

            testCase "an off-thread item after a graph-thread item in the same batch lands"
            <| fun _ ->
                use graph = manualGraph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> feed.Source))
                reading memo |> ignore

                graph.Run (fun () ->
                    Api.batch (fun () ->
                        feed.Push 1
                        offThread (fun () -> feed.Push 10)))

                Expect.equal (reading memo) (Ready 11) "the batch publishes the items folded before it ended"
                Expect.equal graph.PendingWork 1 "the off-thread item posted a wake"
                graph.Pump () |> ignore
                Expect.equal (reading memo) (Ready 11) "the wake republishes the same state"

            testCase "completion after off-thread items publishes the final state first"
            <| fun _ ->
                use graph = manualGraph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStream (fun _ -> feed.Source))
                reading memo |> ignore

                offThread (fun () ->
                    feed.Push 1
                    feed.Push 2
                    feed.Complete ())

                graph.Pump () |> ignore
                Expect.equal (reading memo) (Ready 2) "completion did not overtake the items"

            testCase "concurrent Next callers leave a commutative fold equal to sequential delivery"
            <| fun _ ->
                use graph = manualGraph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> feed.Source))
                reading memo |> ignore
                let sink = feed.Latest.Sink

                let workers =
                    [|
                        for w in 0..3 ->
                            Thread (
                                (fun () ->
                                    for i in 1..10000 do
                                        sink.Next (w * 10000 + i)),
                                IsBackground = true
                            )
                    |]

                for t in workers do
                    t.Start ()

                for t in workers do
                    t.Join ()

                graph.Pump () |> ignore

                let expected =
                    Seq.sum (
                        seq {
                            for w in 0..3 do
                                for i in 1..10000 -> w * 10000 + i
                        }
                    )

                Expect.equal (reading memo) (Ready expected) "every item folded once"
#endif
        ]

type private Recorder<'T>() =
    let observers = ResizeArray<IObserver<'T>>()
    member val Unsubscribed = 0 with get, set
    member _.Observer = observers[observers.Count - 1]

    interface IObservable<'T> with
        member this.Subscribe observer =
            observers.Add observer

            { new IDisposable with
                member _.Dispose() =
                    this.Unsubscribed <- this.Unsubscribed + 1
            }

#if FABLE_COMPILER
[<Fable.Core.Emit("({ [Symbol.asyncIterator]() { return { next: $0, return: $1 } } })")>]
let private iterableOf (next: unit -> Task<obj>) (ret: unit -> Task<obj>) : Fable.Core.JS.AsyncIterable<int> =
    Fable.Core.Util.jsNative

[<Fable.Core.Emit("({ done: false, value: $0 })")>]
let private yielded (value: int) : obj =
    Fable.Core.Util.jsNative

[<Fable.Core.Emit("({ done: true })")>]
let private finishedResult () : obj =
    Fable.Core.Util.jsNative

let private manualIterable () =
    let nexts = Array.init 4 (fun _ -> TaskCompletionSource<obj>())
    let mutable calls = 0
    let mutable returns = 0

    let source =
        iterableOf
            (fun () ->
                calls <- calls + 1
                nexts[calls - 1].Task)
            (fun () ->
                returns <- returns + 1
                Task.FromResult (null: obj))

    nexts, (fun () -> returns), (fun () -> calls), source
#endif

/// <summary>Runs <c>source</c>, cancels its token, and returns the outcome of <c>Run</c>'s task.</summary>
let private cancelledRun (source: StreamSource<int>) =
    async {
        use cancel = new CancellationTokenSource ()
        let outcome = ref None
        let run = source.Run (StreamSink<int> ignore, cancel.Token)
        Platform.whenDone run (fun o -> outcome.Value <- Some o)
        cancel.Cancel ()
#if FABLE_COMPILER
        // Settlement arrives on a microtask; a macrotask follows all of them.
        do! Async.Sleep 0
#endif
        return outcome.Value
    }

let private expectCancelled (outcome: Platform.FlightOutcome<unit> option) =
    match outcome with
    | Some (Platform.FlightOutcome.Canceled _)
    | Some (Platform.FlightOutcome.Faulted (:? OperationCanceledException)) -> ()
#if FABLE_COMPILER
    // Fable's type test does not distinguish OperationCanceledException from Exception.
    | Some (Platform.FlightOutcome.Faulted error) when error.Message = "The operation was canceled." -> ()
#endif
    | other -> failtestf "expected a cancelled Run, got %A" other

#if NET8_0_OR_GREATER && !FABLE_COMPILER
/// <summary>A reader of <c>1 .. count</c> whose every wait completes synchronously and admits one item.</summary>
type private SyncReader(count: int) =
    inherit Channels.ChannelReader<int>()
    let mutable next = 1
    let mutable admitted = false

    override _.TryRead(item: byref<int>) =
        if admitted && next <= count then
            item <- next
            next <- next + 1
            admitted <- false
            true
        else
            false

    override _.WaitToReadAsync(_: CancellationToken) =
        admitted <- next <= count
        ValueTask<bool> admitted

/// <summary>A reader whose first wait completes on <c>Open</c>; then <c>TryRead</c> throws, or the next wait throws.</summary>
type private FaultyReader(fault: string) =
    inherit Channels.ChannelReader<int>()
    let gate = TaskCompletionSource<bool>()
    let mutable waits = 0

    member _.Open() =
        gate.SetResult true

    override _.TryRead(item: byref<int>) =
        if fault = "read" then
            raise (Exception "read")

        item <- 0
        false

    override _.WaitToReadAsync(_: CancellationToken) =
        waits <- waits + 1

        if waits = 1 then ValueTask<bool> gate.Task
        elif fault = "wait" then raise (Exception "wait")
        else ValueTask<bool>(TaskCompletionSource<bool>().Task)

/// <summary>A reader of <c>items</c> whose first wait completes on <c>Open</c>; <c>OnRead</c> runs after each read.</summary>
type private QueueReader(items: int list) =
    inherit Channels.ChannelReader<int>()
    let queue = Collections.Generic.Queue<int>(items)
    let gate = TaskCompletionSource<bool>()
    member val OnRead: unit -> unit = ignore with get, set
    member _.Remaining = queue.Count

    member _.Open() =
        gate.SetResult true

    override this.TryRead(item: byref<int>) =
        if queue.Count > 0 then
            item <- queue.Dequeue ()
            this.OnRead ()
            true
        else
            false

    override _.WaitToReadAsync(token: CancellationToken) =
        if token.IsCancellationRequested then
            ValueTask<bool>(Task.FromCanceled<bool> token)
        else
            ValueTask<bool> gate.Task
#endif

[<Tests>]
let converterTests =
    testList
        "Stream sources"
        [
            testCaseAsync "ofObservable: OnNext publishes, OnCompleted keeps the state, restart unsubscribes once"
            <| async {
                use graph = new Graph ()
                let subject = Recorder<int>()
                let topic = Signal (graph, 0)

                let memo =
                    graph.Run (fun () ->
                        Api.createStream (fun _ ->
                            topic.Value |> ignore
                            StreamSource.ofObservable subject))

                graph.Run (fun () -> memo.TryValue) |> ignore
                subject.Observer.OnNext 3
                Expect.equal (graph.Run (fun () -> memo.TryValue)) (Ready 3) "item"
                topic.Value <- 1
                graph.Run (fun () -> memo.TryValue) |> ignore
                Expect.equal subject.Unsubscribed 1 "the restart disposed the first subscription"
                subject.Observer.OnCompleted ()
#if FABLE_COMPILER
                // Completion arrives on a microtask; a macrotask follows all of them.
                do! Async.Sleep 0
#endif

                match graph.Run (fun () -> memo.TryValue) with
                | Failed (:? InvalidOperationException) -> ()
                | other -> failtestf "empty second stream: expected InvalidOperationException, got %A" other
            }

            testCaseAsync "ofObservable: OnError publishes Failed"
            <| async {
                use graph = new Graph ()
                let subject = Recorder<int>()

                let memo =
                    graph.Run (fun () -> Api.createStream (fun _ -> StreamSource.ofObservable subject))

                graph.Run (fun () -> memo.TryValue) |> ignore
                subject.Observer.OnError (Exception "rx")
#if FABLE_COMPILER
                // Completion arrives on a microtask; a macrotask follows all of them.
                do! Async.Sleep 0
#endif

                match graph.Run (fun () -> memo.TryValue) with
                | Failed error -> Expect.equal error.Message "rx" "error"
                | other -> failtestf "expected Failed, got %A" other
            }

            testCase "ofObservable: disposing the memo unsubscribes exactly once"
            <| fun _ ->
                use graph = new Graph ()
                let subject = Recorder<int>()

                let memo =
                    graph.Run (fun () -> Api.createStream (fun _ -> StreamSource.ofObservable subject))

                graph.Run (fun () -> memo.TryValue) |> ignore
                memo.Dispose ()
                Expect.equal subject.Unsubscribed 1 "unsubscribed once"

            testCase "create: the function receives the sink and token"
            <| fun _ ->
                use graph = new Graph ()
                let mutable token = CancellationToken.None

                let memo =
                    graph.Run (fun () ->
                        Api.createStream (fun _ ->
                            StreamSource.create (fun sink t ->
                                token <- t
                                sink.Next 1
                                TaskCompletionSource<unit>().Task :> Task)))

                Expect.equal (graph.Run (fun () -> memo.TryValue)) (Ready 1) "item"
                memo.Dispose ()
                Expect.isTrue token.IsCancellationRequested "token"
#if !FABLE_COMPILER
            testCase "ofAsyncEnumerable: items, completion, and DisposeAsync exactly once on cancellation"
            <| fun _ ->
                use graph = new Graph ()
                let mutable disposedCount = 0
                let gate = TaskCompletionSource<bool>()

                let items =
                    { new Collections.Generic.IAsyncEnumerable<int> with
                        member _.GetAsyncEnumerator token =
                            let mutable i = 0

                            { new Collections.Generic.IAsyncEnumerator<int> with
                                member _.Current = i

                                member _.MoveNextAsync() =
                                    i <- i + 1

                                    if i <= 2 then
                                        ValueTask<bool> true
                                    else
                                        ValueTask<bool> gate.Task

                                member _.DisposeAsync() =
                                    disposedCount <- disposedCount + 1
                                    ValueTask ()
                            }
                    }

                let memo =
                    graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> StreamSource.ofAsyncEnumerable items))

                Expect.equal (graph.Run (fun () -> memo.TryValue)) (Ready 3) "the synchronous burst"
                memo.Dispose ()
                gate.SetResult false
                Expect.equal disposedCount 1 "DisposeAsync once"

            testCase "ofAsyncEnumerable: a fault from MoveNextAsync fails the stream and disposes once"
            <| fun _ ->
                use graph = new Graph ()
                let mutable disposedCount = 0
                let gate = TaskCompletionSource<bool>()

                let items =
                    { new Collections.Generic.IAsyncEnumerable<int> with
                        member _.GetAsyncEnumerator token =
                            { new Collections.Generic.IAsyncEnumerator<int> with
                                member _.Current = 0

                                member _.MoveNextAsync() =
                                    ValueTask<bool> gate.Task

                                member _.DisposeAsync() =
                                    disposedCount <- disposedCount + 1
                                    ValueTask ()
                            }
                    }

                let memo =
                    graph.Run (fun () -> Api.createStream (fun _ -> StreamSource.ofAsyncEnumerable items))

                graph.Run (fun () -> memo.TryValue) |> ignore
                gate.SetException (Exception "boom")

                match graph.Run (fun () -> memo.TryValue) with
                | Failed error -> Expect.equal error.Message "boom" "error"
                | other -> failtestf "expected Failed, got %A" other

                Expect.equal disposedCount 1 "DisposeAsync once"

            testCase "ofAsyncEnumerable: a throwing Current fails the stream and disposes once"
            <| fun _ ->
                use graph = new Graph ()
                let mutable disposedCount = 0
                let gate = TaskCompletionSource<bool>()

                let items =
                    { new Collections.Generic.IAsyncEnumerable<int> with
                        member _.GetAsyncEnumerator token =
                            { new Collections.Generic.IAsyncEnumerator<int> with
                                member _.Current = raise (Exception "current")

                                member _.MoveNextAsync() =
                                    ValueTask<bool> gate.Task

                                member _.DisposeAsync() =
                                    disposedCount <- disposedCount + 1
                                    ValueTask ()
                            }
                    }

                let memo =
                    graph.Run (fun () -> Api.createStream (fun _ -> StreamSource.ofAsyncEnumerable items))

                graph.Run (fun () -> memo.TryValue) |> ignore
                gate.SetResult true

                match graph.Run (fun () -> memo.TryValue) with
                | Failed error -> Expect.equal error.Message "current" "error"
                | other -> failtestf "expected Failed, got %A" other

                Expect.equal disposedCount 1 "DisposeAsync once"

            testCase "ofAsyncEnumerable: cancelling a MoveNextAsync that honours the token disposes once"
            <| fun _ ->
                use graph = new Graph ()
                let mutable disposedCount = 0
                let mutable seenToken = CancellationToken.None

                let items =
                    { new Collections.Generic.IAsyncEnumerable<int> with
                        member _.GetAsyncEnumerator token =
                            seenToken <- token

                            { new Collections.Generic.IAsyncEnumerator<int> with
                                member _.Current = 0

                                member _.MoveNextAsync() =
                                    let pending = TaskCompletionSource<bool>()

                                    token.Register (fun () -> pending.TrySetCanceled token |> ignore)
                                    |> ignore

                                    ValueTask<bool> pending.Task

                                member _.DisposeAsync() =
                                    disposedCount <- disposedCount + 1
                                    ValueTask ()
                            }
                    }

                let memo =
                    graph.Run (fun () -> Api.createStream (fun _ -> StreamSource.ofAsyncEnumerable items))

                graph.Run (fun () -> memo.TryValue) |> ignore
                memo.Dispose ()
                Expect.isTrue seenToken.IsCancellationRequested "the token was cancelled"
                Expect.equal disposedCount 1 "DisposeAsync once"
#endif
#if NET8_0_OR_GREATER && !FABLE_COMPILER
            testCase "ofChannel: items buffered before the first read publish with that read"
            <| fun _ ->
                use graph = new Graph ()
                let channel = Channels.Channel.CreateUnbounded<int>()

                for i in 1..100 do
                    channel.Writer.TryWrite i |> ignore

                channel.Writer.Complete ()

                let memo =
                    graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> StreamSource.ofChannel channel.Reader))

                Expect.equal (graph.Run (fun () -> memo.TryValue)) (Ready 5050) "every buffered item, then completion"

            testCase "ofChannel: an off-thread burst queues one wake and the completion"
            <| fun _ ->
                use graph =
                    new Graph (
                        { GraphOptions.Default with
                            Dispatcher = Some (ManualDispatcher () :> IGraphDispatcher)
                        }
                    )
                // The reader runs on the writer's thread; the burst is queued when Join returns.
                let channel =
                    Channels.Channel.CreateUnbounded<int>(Channels.UnboundedChannelOptions (AllowSynchronousContinuations = true))

                let memo =
                    graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> StreamSource.ofChannel channel.Reader))

                Expect.equal (graph.Run (fun () -> memo.TryValue)) Pending "waiting on an empty channel"

                let writer =
                    Thread (
                        (fun () ->
                            for i in 1..100 do
                                channel.Writer.TryWrite i |> ignore

                            channel.Writer.Complete ()),
                        IsBackground = true
                    )

                writer.Start ()
                writer.Join ()
                Expect.equal graph.PendingWork 2 "one wake for 100 items, and the completion"
                graph.Pump () |> ignore
                Expect.equal (graph.Run (fun () -> memo.TryValue)) (Ready 5050) "every item"

            testCase "ofChannel: completing the channel with an exception fails the stream"
            <| fun _ ->
                use graph = new Graph ()

                let channel =
                    Channels.Channel.CreateUnbounded<int>(Channels.UnboundedChannelOptions (AllowSynchronousContinuations = true))

                let memo =
                    graph.Run (fun () -> Api.createStream (fun _ -> StreamSource.ofChannel channel.Reader))

                graph.Run (fun () -> memo.TryValue) |> ignore
                channel.Writer.Complete (Exception "x")

                match graph.Run (fun () -> memo.TryValue) with
                | Failed error -> Expect.equal error.Message "x" "error"
                | other -> failtestf "expected Failed, got %A" other

            testCase "ofChannel: disposing the memo cancels the pending read"
            <| fun _ ->
                use graph = new Graph ()

                let channel =
                    Channels.Channel.CreateUnbounded<int>(Channels.UnboundedChannelOptions (AllowSynchronousContinuations = true))

                let memo =
                    graph.Run (fun () -> Api.createStream (fun _ -> StreamSource.ofChannel channel.Reader))

                graph.Run (fun () -> memo.TryValue) |> ignore
                memo.Dispose ()
                channel.Writer.TryWrite 1 |> ignore
                let mutable item = 0
                Expect.isTrue (channel.Reader.TryRead &item) "the item stayed in the channel"
                Expect.equal item 1 "unread"

            testCase "ofChannel: 200,000 waits that complete synchronously drain without a stack overflow"
            <| fun _ ->
                use graph = new Graph ()
                let count = 200000

                let memo =
                    graph.Run (fun () -> Api.createStreamFold 0L (fun s x -> s + int64 x) (fun _ -> StreamSource.ofChannel (SyncReader count)))

                Expect.equal (reading memo) (Ready (int64 count * int64 (count + 1) / 2L)) "every item folded"

            for fault in [ "read"; "wait" ] do
                testCase $"ofChannel: a reader whose %s{fault} throws after a wait fails the stream"
                <| fun _ ->
                    use graph = new Graph ()
                    let reader = FaultyReader fault

                    let memo =
                        graph.Run (fun () -> Api.createStream (fun _ -> StreamSource.ofChannel reader))

                    Expect.equal (reading memo) Pending "waiting"
                    reader.Open ()

                    match reading memo with
                    | Failed error -> Expect.equal error.Message fault "the reader's exception"
                    | other -> failtestf "expected Failed, got %A" other

            testCase "ofChannel: cancellation during a drain leaves the remaining items in the reader"
            <| fun _ ->
                use graph = new Graph ()
                let reader = QueueReader [ 1; 2; 3 ]

                let memo =
                    graph.Run (fun () -> Api.createStream (fun _ -> StreamSource.ofChannel reader))

                Expect.equal (reading memo) Pending "waiting"
                reader.OnRead <- fun () -> memo.Dispose ()
                reader.Open ()
                Expect.equal reader.Remaining 2 "items after the cancelling read stay unread"
#endif
            testCaseAsync "ofObservable: cancellation ends Run with OperationCanceledException"
            <| async {
                let! outcome = cancelledRun (StreamSource.ofObservable (Recorder<int>()))
                expectCancelled outcome
            }
#if FABLE_COMPILER
            testCaseAsync "ofAsyncIterable: items fold and done completes"
            <| async {
                use graph = new Graph ()
                let nexts, _, _, source = manualIterable ()

                let memo =
                    graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> StreamSource.ofAsyncIterable source))

                graph.Run (fun () -> memo.TryValue) |> ignore
                nexts[0].SetResult(yielded 1)
                do! Async.Sleep 0
                nexts[1].SetResult(yielded 2)
                do! Async.Sleep 0
                nexts[2].SetResult(finishedResult ())
                do! Async.Sleep 0
                Expect.equal (graph.Run (fun () -> memo.TryValue)) (Ready 3) "both items, then completion"
            }

            testCaseAsync "ofAsyncIterable: a rejected next fails the stream"
            <| async {
                use graph = new Graph ()
                let nexts, _, _, source = manualIterable ()

                let memo =
                    graph.Run (fun () -> Api.createStream (fun _ -> StreamSource.ofAsyncIterable source))

                graph.Run (fun () -> memo.TryValue) |> ignore
                nexts[0].SetException(Exception "boom")
                do! Async.Sleep 0

                match graph.Run (fun () -> memo.TryValue) with
                | Failed error -> Expect.equal error.Message "boom" "error"
                | other -> failtestf "expected Failed, got %A" other
            }

            testCaseAsync "ofAsyncIterable: disposing while next is pending calls return once and ignores the later value"
            <| async {
                use graph = new Graph ()
                let nexts, returns, calls, source = manualIterable ()

                let memo =
                    graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> StreamSource.ofAsyncIterable source))

                graph.Run (fun () -> memo.TryValue) |> ignore
                memo.Dispose ()
                Expect.equal (returns ()) 1 "return called once"
                nexts[0].SetResult(yielded 9)
                do! Async.Sleep 0
                Expect.equal (calls ()) 1 "the later value did not request another"
            }

            testCaseAsync "ofAsyncIterable: cancellation ends Run with OperationCanceledException"
            <| async {
                let _, _, _, source = manualIterable ()
                let! outcome = cancelledRun (StreamSource.ofAsyncIterable source)
                expectCancelled outcome
            }
#endif
        ]

#if !FABLE_COMPILER
// .NET only: JavaScript has no finalizer thread and no UnobservedTaskException.
/// <summary>Fails a running, a superseded and a disposed stream with <c>marker</c>, and returns no reference.</summary>
[<Runtime.CompilerServices.MethodImpl(Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
let private faultStreams (marker: string) () =
    use graph = new Graph ()
    let feed = ManualStream<int>()
    let topic = Signal (graph, 0)

    let memo =
        graph.Run (fun () ->
            Api.createStream (fun _ ->
                topic.Value |> ignore
                feed.Source))

    reading memo |> ignore
    topic.Value <- 1
    reading memo |> ignore
    feed.At(0).Done.SetException(InvalidOperationException marker)
    feed.Fail (InvalidOperationException marker)
    reading memo |> ignore

    let other = ManualStream<int>()
    let disposed = graph.Run (fun () -> Api.createStream (fun _ -> other.Source))
    reading disposed |> ignore
    disposed.Dispose ()
    other.Fail (InvalidOperationException marker)

    let failing =
        graph.Run (fun () -> Api.createStream (fun _ -> StreamSource.create (fun _ _ -> Task.FromException (InvalidOperationException marker))))

    reading failing |> ignore

[<Tests>]
let unobserved =
    testSequenced
    <| testList
        "Stream memo unobserved faults"
        [
            test "a failed, superseded or disposed stream's fault is observed" {
                let marker = $"stream-{Guid.NewGuid ()}"
                let reported = AsyncEdges.unobservedDuring marker (faultStreams marker)
                Expect.isEmpty reported "no stream exception reached UnobservedTaskException"
            }
        ]
#endif

#if RANVIER_TRACE
[<Tests>]
let traceTests =
    testList
        "Stream memo traces"
        [
            testCaseAsync "a start, a burst, a restart and a completion are traced"
            <| async {
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let topic = Signal (graph, 0)

                let memo =
                    graph.Run (fun () ->
                        Api.createStreamFold 0 (+) (fun _ ->
                            topic.Value |> ignore
                            feed.Source))

                graph.Run (fun () -> memo.TryValue) |> ignore

                graph.Run (fun () ->
                    Api.batch (fun () ->
                        feed.Push 1
                        feed.Push 2
                        feed.Push 3))

                topic.Value <- 1
                graph.Run (fun () -> memo.TryValue) |> ignore
                feed.Push 5
                feed.Complete ()
#if FABLE_COMPILER
                // Completion arrives on a microtask; a macrotask follows all of them.
                do! Async.Sleep 0
#endif

                let summary =
                    Trace.events graph
                    |> Array.filter (fun e -> int e.Kind >= 60 && int e.Kind <= 62)
                    |> Array.map (fun e -> e.Kind, e.Arg, e.Flag)
                    |> Array.toList

                Expect.equal
                    summary
                    [
                        TraceEventKind.StreamStarted, 1, 0
                        TraceEventKind.StreamPublished, 0, 3
                        TraceEventKind.StreamEnded, int TraceStreamEnd.Restarted, 0
                        TraceEventKind.StreamStarted, 2, 0
                        TraceEventKind.StreamPublished, 0, 1
                        TraceEventKind.StreamEnded, int TraceStreamEnd.Completed, 0
                        TraceEventKind.StreamPublished, 0, 0
                    ]
                    "events in order"
            }

            testCase "a stream's run ends with its start, and a reader's later write names the reader"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let target = Signal (graph, 0)
                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> feed.Source))

                let reader =
                    new Effect (
                        graph,
                        fun () ->
                            memo.TryValue |> ignore
                            target.Value <- target.Peek + 1
                    )

                feed.Push 4
                let stream = (memo :> INode).Id
                let events = Trace.events graph

                let ends =
                    events
                    |> Array.filter (fun e -> e.Kind = TraceEventKind.RunEnd && e.Node = stream)
                    |> Array.map (fun e -> enum<RunStatus> e.Arg)

                Expect.equal ends [| RunStatus.Pending |] "the stream's one run ends Pending when start returns"

                let writers =
                    events
                    |> Array.filter (fun e ->
                        e.Kind = TraceEventKind.Write
                        && e.Node = (target :> INode).Id)
                    |> Array.map (fun e -> e.Other)

                Expect.equal writers [| (reader :> INode).Id; (reader :> INode).Id |] "both writes name the reader"

                let started =
                    events
                    |> Array.find (fun e -> e.Kind = TraceEventKind.StreamStarted)

                let published =
                    events
                    |> Array.filter (fun e -> e.Kind = TraceEventKind.StreamPublished)
                    |> Array.map (fun e -> e.Cause)

                Expect.equal published [| started.Seq |] "the publication cites the stream's start"

            testCase "StreamEnd and TraceStreamEnd carry the same values"
            <| fun _ ->
                Expect.equal (int StreamEnd.Completed) (int TraceStreamEnd.Completed) "Completed"
                Expect.equal (int StreamEnd.Failed) (int TraceStreamEnd.Failed) "Failed"
                Expect.equal (int StreamEnd.Restarted) (int TraceStreamEnd.Restarted) "Restarted"
                Expect.equal (int StreamEnd.Disposed) (int TraceStreamEnd.Disposed) "Disposed"
        ]
#else
let traceTests = testList "Stream memo traces" []
#endif
