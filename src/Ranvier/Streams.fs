namespace Ranvier

open System
open System.Threading
open System.Threading.Tasks

/// <summary>The accumulator behind a <c>StreamSink</c>.</summary>
[<AbstractClass>]
type internal StreamInlet<'T>() =
    abstract Deliver: item: 'T -> unit

/// <summary>The receiver of a running stream's items.</summary>
[<Sealed>]
type StreamSink<'T> internal (inlet: StreamInlet<'T>) =
    internal new(deliver: 'T -> unit) =
        StreamSink<'T>(
            { new StreamInlet<'T>() with
                member _.Deliver item =
                    deliver item
            }
        )

    /// <summary>Folds <c>item</c> into the stream's state. Callable from any thread. Calls from other threads may overlap each other; a call on the graph thread must not overlap any other call.</summary>
    member _.Next(item: 'T) =
        inlet.Deliver item

/// <summary>A producer of items, run once per stream start.</summary>
[<AbstractClass>]
type StreamSource<'T>() =
    /// <summary>
    /// Delivers items to <c>sink</c> until the stream ends. The task completes when the stream completes, faults when it
    /// fails, and is cancelled or faults with <c>OperationCanceledException</c> after <c>token</c> is cancelled.
    /// </summary>
    abstract Run: sink: StreamSink<'T> * token: CancellationToken -> Task

/// <summary>The node a stream run schedules a publication on.</summary>
type internal IStreamRequester =
    /// <summary>Queues the node for the current or next flush. Called on the graph thread.</summary>
    abstract RequestOnGraph: unit -> unit
#if !FABLE_COMPILER
    /// <summary>Posts one wake to the graph while none is outstanding. Called off the graph thread.</summary>
    abstract RequestOffGraph: unit -> unit
#endif

/// <summary>The state of one started stream, shared by its producer and the graph thread.</summary>
type internal IStreamRun<'S> =
    /// <summary>Captures the state as <c>Captured</c>; returns the number of items folded since the previous snapshot.</summary>
    abstract Snapshot: unit -> int
    /// <summary>The state captured by the latest <c>Snapshot</c>.</summary>
    abstract Captured: 'S
    /// <summary>True once an item has been folded.</summary>
    abstract Received: bool
    /// <summary>Ignores every later item.</summary>
    abstract Close: unit -> unit

/// <summary>The mutual exclusion guarding a stream run's state; a no-op under Fable.</summary>
#if NET9_0_OR_GREATER && !FABLE_COMPILER
type internal StreamGate = Lock
#else
type internal StreamGate = obj
#endif

[<RequireQualifiedAccess>]
module internal StreamGate =
    let inline create () : StreamGate =
#if NET9_0_OR_GREATER && !FABLE_COMPILER
        Lock ()
#else
        obj ()
#endif

    /// <summary>Runs <c>body</c> while holding <c>gate</c>.</summary>
    let inline hold (gate: StreamGate) ([<InlineIfLambda>] body: unit -> 'R) : 'R =
#if FABLE_COMPILER
        ignore gate
        body ()
#else
#if NET9_0_OR_GREATER
        gate.Enter ()

        try
            body ()
        finally
            gate.Exit ()
#else
        lock gate body
#endif
#endif

/// <summary>
/// A started stream's accumulator. Items fold on the delivering thread: without a lock on the graph thread, under the
/// lock elsewhere.
/// </summary>
[<Sealed>]
type internal StreamRun<'S, 'T>(graph: Graph, seed: 'S, fold: 'S -> 'T -> 'S, requester: IStreamRequester, failed: exn -> unit) =
    inherit StreamInlet<'T>()
    let gate = StreamGate.create ()
    let mutable state = seed
    let mutable captured = seed
    let mutable folded = 0
    let mutable received = false
    let mutable closed = false
    let mutable foldError: exn = null

    /// <summary>Folds <c>item</c>: 1 when folded, 0 when closed, 2 when the fold threw and closed the run.</summary>
    member private _.Fold(item: 'T) =
        if closed then
            0
        else
            try
                state <- fold state item
                folded <- folded + 1
                received <- true
                1
            with error ->
                closed <- true
                foldError <- error
                2

    /// <summary>Folds <c>item</c> and requests a publication; a throwing fold closes the run and reports the exception.</summary>
    override this.Deliver(item: 'T) =
#if FABLE_COMPILER
        let outcome = this.Fold item

        if outcome = 1 then
            requester.RequestOnGraph ()
        elif outcome = 2 then
            failed foldError
#else
        // The graph thread folds without the lock; this depends on StreamSink.Next calls never overlapping.
        if graph.IsOnGraphThread then
            let outcome = this.Fold item

            if outcome = 1 then
                requester.RequestOnGraph ()
            elif outcome = 2 then
                failed foldError
        else
            let outcome = StreamGate.hold gate (fun () -> this.Fold item)

            if outcome = 1 then
                requester.RequestOffGraph ()
            elif outcome = 2 then
                failed foldError
#endif

    interface IStreamRun<'S> with
        member _.Snapshot() =
            StreamGate.hold gate (fun () ->
                let count = folded
                folded <- 0
                captured <- state
                count)

        member _.Captured = captured

        member _.Received =
#if FABLE_COMPILER
            received
#else
            Volatile.Read &received
#endif

        member _.Close() =
            StreamGate.hold gate (fun () -> closed <- true)

/// <summary>How a publication reaches the stream memo's readers when its state moved.</summary>
type internal StreamNotice =
    /// <summary>Readers are not notified.</summary>
    | Silent = 0
    /// <summary>Readers are marked dirty and the graph flushes.</summary>
    | Flush = 1
    /// <summary>Readers are marked dirty; the running flush runs the effects this queues.</summary>
    | InFlush = 2

/// <summary>Why a stream ended, recorded by <c>StreamEnded</c>.</summary>
type internal StreamEnd =
    | Completed = 0
    | Failed = 1
    | Restarted = 2
    | Disposed = 3

/// <summary>
/// The latest state of a stream: a fold of its items into a seed, published at most once per graph pass.
/// </summary>
/// <remarks>
/// Starts on its first read. A change to a value read by <c>start</c> cancels the stream and starts a new one from the
/// seed; the node is pending until the new stream's first item or completion. Disposal cancels the stream, and a
/// pending node becomes failed with <c>ObjectDisposedException</c>.
/// </remarks>
[<Sealed>]
type StreamMemo<'S>
    internal
    (
        graph: Graph,
        seed: 'S,
        emptyFails: bool,
        launch: IStreamRequester -> (exn -> unit) -> CancellationToken -> struct (IStreamRun<'S> * (unit -> Task))
    ) as this =
    let id = graph.NextId ()
    let observers = ObserverSet ()
    do Tracer.Bind (observers, graph, id)
    let sources = SourceList ()
    do Tracer.Bind (sources, graph, id)
    let equal = graph.Options.Equality.Comparer<'S>()
    let mutable freshness = Freshness.Dirty
    let mutable status = Status.Pending ||| Status.Uninitialized
    let mutable value = Unchecked.defaultof<'S>
    let mutable failure: Failure = null
    let mutable runs = 0
    let mutable disposed = false
    let mutable violated = false
    let mutable link: OwnerLink = null
    let mutable generation = 0
    let mutable run: IStreamRun<'S> = Unchecked.defaultof<_>
    let mutable cts: CancellationTokenSource = null
    let mutable running = false
    let mutable launching = false
    let mutable queued = false
#if !FABLE_COMPILER
    let mutable wakePosted = 0

    let wake () =
        Interlocked.Exchange (&wakePosted, 0) |> ignore

        if not disposed then
            this.Enqueue ()
            graph.RequestFlush ()
#endif

    member private this.Attach() =
        link <- graph.CurrentOwner.AttachLinked this
        Tracer.StreamNew (graph, id, link.Owner)

    member private this.Enqueue() =
        if not queued then
            queued <- true
            graph.Schedule (this :> IScheduled)

    /// <summary>Queues a publication for an item folded on the graph thread; items folded while <c>Run</c> runs need none.</summary>
    member private this.RequestOnGraph() =
        if not launching && not disposed then
            this.Enqueue ()
            graph.RequestFlush ()

#if !FABLE_COMPILER
    /// <summary>Posts one wake for items folded off the graph thread while none is outstanding.</summary>
    member private _.RequestOffGraph() =
        if
            Volatile.Read &wakePosted = 0
            && Interlocked.Exchange (&wakePosted, 1) = 0
        then
            graph.Post wake
#endif

    member private _.Notify() =
        observers.NotifyDirtyExcept graph.CurrentComputation
        Tracer.Notified graph
        graph.RequestFlush ()

    member private _.NotifyInFlush() =
        observers.NotifyDirtyExcept graph.CurrentComputation
        Tracer.Notified graph

    /// <summary>Closes the open stream and cancels its token.</summary>
    member private _.Stop(reason: StreamEnd) =
        if not (Platform.isNull (box run)) then
            run.Close ()

        if running then
            running <- false
            Tracer.StreamEnded (graph, id, int reason)

        if not (Platform.isNull cts) then
            let source = cts
            cts <- null
            source.Cancel ()
            source.Dispose ()

    member private this.Fail(error: exn) =
        this.Stop StreamEnd.Failed
        let next = graph.FailureOf (error, this, failure)

        let moved =
            status <> Status.Error
            || Failure.Moved (next, failure)

        failure <- next
        status <- Status.Error
        Tracer.StreamPublished (graph, id, int (byte status), 0, box error)

        if moved then
            this.Notify ()

    member private this.Publish(state: 'S, count: int, notice: StreamNotice) =
        try
            let moved =
                status <> Status.None
                || not (equal.Equals (value, state))

            value <- state
            status <- Status.None
            failure <- null
            Tracer.StreamPublished (graph, id, int (byte status), count, box state)

            if moved then
                if notice = StreamNotice.Flush then
                    this.Notify ()
                elif notice = StreamNotice.InFlush then
                    this.NotifyInFlush ()
        with error ->
            this.Fail error

    member private this.PublishLatest(notice: StreamNotice) =
        let count = run.Snapshot ()
        this.Publish (run.Captured, count, notice)

    member private this.Ended(gen: int, outcome: Platform.FlightOutcome<unit>) =
        if not disposed && gen = generation && running then
            match outcome with
            | Platform.FlightOutcome.Completed _ ->
                let received = run.Received
                this.Stop StreamEnd.Completed

                if received then
                    this.PublishLatest StreamNotice.Flush
                elif emptyFails then
                    this.Fail (InvalidOperationException "The stream completed without an item.")
                else
                    this.Publish (seed, 0, StreamNotice.Flush)
            | Platform.FlightOutcome.Faulted error
            | Platform.FlightOutcome.Canceled error -> this.Fail error

    member private this.FoldFailed(gen: int, error: exn) =
        if not disposed && gen = generation && running then
            this.Fail error

    member private this.Launch() =
        freshness <- Freshness.Clean
        this.Stop StreamEnd.Restarted
        generation <- generation + 1
        let gen = generation
        runs <- runs + 1
        Tracer.RunStart (graph, id, runs)
        failure <- null

        status <-
            Status.Pending
            ||| (status &&& Status.Uninitialized)

        sources.BeginRun ()
        let source = new CancellationTokenSource ()
        cts <- source

        try
            try
                let struct (started, body) =
                    graph.RunHosted (
                        this :> IComputation,
                        fun () -> launch (this :> IStreamRequester) (fun error -> graph.Post (fun () -> this.FoldFailed (gen, error))) source.Token
                    )

                if violated then
                    raise (InvalidOperationException (ScopeMessages.forMode ScopeMode.Pure))

                if not disposed then
                    run <- started
                    running <- true
                    Tracer.StreamStarted (graph, id, gen)
                    launching <- true

                    let work =
                        try
                            try
                                graph.RunUntracked body
                            with NotReadyException _ as pending ->
                                let failure = InvalidOperationException "StreamSource.Run read a pending source."
#if FABLE_COMPILER
                                Platform.setInner failure pending
                                raise failure
#else
                                raise (InvalidOperationException (failure.Message, pending))
#endif
                        finally
                            launching <- false

                    if Platform.isNull work then
                        raise (InvalidOperationException "StreamSource.Run returned null.")

                    Platform.whenDone work (fun outcome -> graph.Post (fun () -> this.Ended (gen, outcome)))

                    if gen = generation && running && started.Received then
                        this.PublishLatest StreamNotice.Silent
            finally
                if disposed then
                    sources.Clear (this :> IComputation)
                else
                    sources.EndRun (this :> IComputation)
        with
        | error when violated ->
            violated <- false
            this.Fail (ScopeMessages.failure ScopeMode.Pure error)
        | NotReadyException _ -> this.Stop StreamEnd.Restarted
        | error -> this.Fail error

        Tracer.RunEnd (graph, id, status)

    member private this.EnsureCurrent() =
        if freshness <> Freshness.Clean && not disposed then
            this.Pull ()

    member private this.Pull() =
        graph.EnterPull ()

        try
            if freshness = Freshness.Check then
                Tracer.CheckStart (graph, id)
                let mutable i = 0

                while freshness = Freshness.Check && i < sources.Count do
                    sources.SourceAt(i).UpdateIfNecessary()
                    i <- i + 1

                Tracer.CheckResolved (graph, id, (freshness = Freshness.Dirty))

                if freshness = Freshness.Check then
                    freshness <- Freshness.Clean

            if freshness = Freshness.Dirty then
                this.Launch ()
        finally
            graph.ExitPull ()

    static member internal Create<'T>(graph: Graph, seed: 'S, fold: 'S -> 'T -> 'S, start: CancellationToken -> StreamSource<'T>, emptyFails: bool) =
        if Platform.isNull (box fold) then
            nullArg (nameof fold)

        if Platform.isNull (box start) then
            nullArg (nameof start)

        let launch requester failed (token: CancellationToken) =
            let source = start token

            if Platform.isNull (box source) then
                raise (InvalidOperationException "The stream's start function returned null.")

            let started = StreamRun<'S, 'T>(graph, seed, fold, requester, failed)
            struct (started :> IStreamRun<'S>, (fun () -> source.Run (StreamSink<'T> started, token)))

        let memo = new StreamMemo<'S> (graph, seed, emptyFails, launch)
        memo.Attach ()
        memo

    interface INode with
        member _.Id = id
        member _.Status = status

    interface ISource with
        member _.AddObserver(observer, slot) =
            observers.Add (observer, slot)

        member _.TryAddObserver(observer, slot) =
            observers.TryAdd (observer, slot)

        member _.RemoveObserverAt(observer, handle) =
            observers.RemoveAt (observer, handle)

        member this.UpdateIfNecessary() =
            this.EnsureCurrent ()

    interface IComputation with
        member this.AddSource source =
            sources.Add (this :> IComputation, source)

        member _.MovedIn(slot, handle) =
            sources.MovedIn (slot, handle)

        member _.MarkDirty() =
            if freshness <> Freshness.Dirty then
                freshness <- Freshness.Dirty
                observers.NotifyDirty ()

        member _.MarkCheck() =
            if freshness = Freshness.Clean then
                freshness <- Freshness.Check
                observers.NotifyCheck ()

    interface IScheduled with
        member this.Execute() =
            queued <- false

            if not disposed && running then
                if freshness = Freshness.Check then
                    graph.EnterPull ()

                    try
                        Tracer.CheckStart (graph, id)
                        let mutable i = 0

                        while freshness = Freshness.Check && i < sources.Count do
                            sources.SourceAt(i).UpdateIfNecessary()
                            i <- i + 1

                        Tracer.CheckResolved (graph, id, (freshness = Freshness.Dirty))

                        if freshness = Freshness.Check then
                            freshness <- Freshness.Clean
                    finally
                        graph.ExitPull ()

                if
                    freshness = Freshness.Clean
                    && running
                    && not disposed
                    && run.Received
                then
                    let count = run.Snapshot ()

                    if count > 0 then
                        this.Publish (run.Captured, count, StreamNotice.InFlush)

    interface IStreamRequester with
        member this.RequestOnGraph() =
            this.RequestOnGraph ()
#if !FABLE_COMPILER
        member this.RequestOffGraph() =
            this.RequestOffGraph ()
#endif

    interface IScopeHost with
        member _.Scope =
            violated <- true
            raise (InvalidOperationException (ScopeMessages.forMode ScopeMode.Pure))

    interface IOwned with
        member this.Release() =
            link <- null
            this.Dispose ()

    /// <summary>The graph that owns this value.</summary>
    member _.Graph = graph
    /// <summary>The published state.</summary>
    member _.Status = status
    /// <summary>The number of stream starts, including starts that suspended or failed.</summary>
    member _.Runs = runs
    /// <summary>The last published state, read without tracking; never starts the stream or raises.</summary>
    member _.Peek = value
    /// <summary>The node the current failure originated in while <c>Status</c> has <c>Error</c>, otherwise null.</summary>
    member _.ErrorOrigin: INode = Failure.OriginOf failure

    /// <summary>Tracks and reads the published state; raises while it is pending or failed.</summary>
    member this.Value: 'S =
        this.EnsureCurrent ()
        graph.Track (this :> ISource)

        if status.HasFlag Status.Pending then
            raise (graph.NotReady (this :> INode))

        if status.HasFlag Status.Error then
            graph.Raise failure

        value

    /// <summary>Tracks and returns the published ready, pending or failed state.</summary>
    member this.TryValue: Reading<'S> =
        this.EnsureCurrent ()
        graph.Track (this :> ISource)

        if status.HasFlag Status.Pending then Pending
        elif status.HasFlag Status.Error then Failed failure.Error
        else Ready value

    /// <summary>Cancels the stream and detaches from its sources; repeated disposal is harmless.</summary>
    member this.Dispose() =
        graph.Entered (
            "Disposing a node",
            fun () ->
                if not disposed then
                    disposed <- true
                    Tracer.NodeDispose (graph, id)

                    if not (Platform.isNull link) then
                        link.Detach ()
                        link <- null

                    sources.Clear (this :> IComputation)
                    this.Stop StreamEnd.Disposed

                    if status.HasFlag Status.Pending then
                        failure <- Failure (ObjectDisposedException (this.GetType().Name), (this :> INode))
                        status <- Status.Error
                        this.Notify ()
        )

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()

/// <summary>Stream sources from common item producers.</summary>
[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module StreamSource =
    /// <summary>A source whose every start calls <c>run</c>.</summary>
    /// <exception cref="T:System.ArgumentNullException"><c>run</c> is null.</exception>
    let create (run: StreamSink<'T> -> CancellationToken -> Task) : StreamSource<'T> =
        if Platform.isNull (box run) then
            nullArg (nameof run)

        { new StreamSource<'T>() with
            member _.Run(sink, token) =
                run sink token
        }

    let private cancelled (token: CancellationToken) : exn =
#if FABLE_COMPILER
        OperationCanceledException "The operation was canceled."
#else
        OperationCanceledException token
#endif

    /// <summary>Subscribes on each start: <c>OnNext</c> delivers, <c>OnCompleted</c> completes, <c>OnError</c> fails.</summary>
    /// <remarks>Cancellation disposes the subscription and faults the task with <c>OperationCanceledException</c>.</remarks>
    let ofObservable (items: IObservable<'T>) : StreamSource<'T> =
        if Platform.isNull (box items) then
            nullArg (nameof items)

        create (fun sink token ->
            let finished = Platform.completion<unit>()

            let subscription =
                items.Subscribe
                    { new IObserver<'T> with
                        member _.OnNext item =
                            sink.Next item

                        member _.OnCompleted() =
                            finished.Resolve ()

                        member _.OnError error =
                            finished.Reject error
                    }

            token.Register (fun () ->
                try
                    subscription.Dispose ()
                with _ ->
                    ()

                finished.Reject (cancelled token))
            |> ignore

            finished.Task :> Task)

#if !FABLE_COMPILER
    /// <summary>
    /// Enumerates on each start with the stream's token. Items already available are delivered without an
    /// intervening continuation. <c>DisposeAsync</c> runs once, on completion, failure or cancellation.
    /// </summary>
    let ofAsyncEnumerable (items: Collections.Generic.IAsyncEnumerable<'T>) : StreamSource<'T> =
        if Platform.isNull (box items) then
            nullArg (nameof items)

        create (fun sink token ->
            let finished = Platform.completion<unit>()
            let enumerator = items.GetAsyncEnumerator token

            let finish (error: exn) =
                let disposal =
                    try
                        enumerator.DisposeAsync().AsTask()
                    with disposeError ->
                        Task.FromException disposeError

                disposal.ContinueWith (
                    Action<Task>(fun d ->
                        if not (isNull error) then
                            finished.Reject error
                        elif d.IsFaulted then
                            finished.Reject (d.Exception.GetBaseException ())
                        else
                            finished.Resolve ()),
                    TaskContinuationOptions.ExecuteSynchronously
                )
                |> ignore

            let rec pump () =
                try
                    let mutable next = enumerator.MoveNextAsync ()
                    let mutable go = true

                    while go && next.IsCompleted do
                        if next.Result then
                            sink.Next enumerator.Current
                            next <- enumerator.MoveNextAsync ()
                        else
                            go <- false
                            finish null

                    if go then
                        next
                            .AsTask()
                            .ContinueWith(
                                Action<Task<bool>>(fun t ->
                                    if t.IsFaulted then
                                        finish (t.Exception.GetBaseException ())
                                    elif t.IsCanceled then
                                        finish (cancelled token)
                                    elif t.Result then
                                        try
                                            sink.Next enumerator.Current
                                            pump ()
                                        with error ->
                                            finish error
                                    else
                                        finish null),
                                TaskContinuationOptions.ExecuteSynchronously
                            )
                        |> ignore
                with error ->
                    finish error

            pump ()
            finished.Task :> Task)
#endif

#if NET8_0_OR_GREATER && !FABLE_COMPILER
    /// <summary>
    /// Reads on each start: waits with the stream's token, then delivers every buffered item before waiting again, and
    /// reads nothing once the token is cancelled. The stream completes when the channel completes, and fails with the
    /// exception passed to the writer's <c>Complete</c>.
    /// </summary>
    let ofChannel (reader: Channels.ChannelReader<'T>) : StreamSource<'T> =
        if Platform.isNull (box reader) then
            nullArg (nameof reader)

        create (fun sink token ->
            let finished = Platform.completion<unit>()

            let drain () =
                let mutable item = Unchecked.defaultof<'T>

                while not token.IsCancellationRequested
                      && reader.TryRead &item do
                    sink.Next item

            let ready (wait: Task<bool>) =
                wait.Status = TaskStatus.RanToCompletion
                && wait.Result
                && not token.IsCancellationRequested

            let ended (wait: Task<bool>) =
                if wait.IsFaulted then
                    finished.Reject (wait.Exception.GetBaseException ())
                elif wait.IsCanceled || token.IsCancellationRequested then
                    finished.Reject (cancelled token)
                else
                    finished.Resolve ()

            let rec pump () =
                try
                    let mutable wait = reader.WaitToReadAsync token

                    while wait.IsCompletedSuccessfully
                          && wait.Result
                          && not token.IsCancellationRequested do
                        drain ()
                        wait <- reader.WaitToReadAsync token

                    if wait.IsCompleted then
                        ended (wait.AsTask ())
                    else
                        wait
                            .AsTask()
                            .ContinueWith(
                                Action<Task<bool>>(fun t ->
                                    if ready t then
                                        try
                                            drain ()
                                            pump ()
                                        with error ->
                                            finished.Reject error
                                    else
                                        ended t),
                                TaskContinuationOptions.ExecuteSynchronously
                            )
                        |> ignore
                with error ->
                    finished.Reject error

            pump ()
            finished.Task :> Task)
#endif

#if FABLE_COMPILER
    [<Fable.Core.Emit("$0[Symbol.asyncIterator]()")>]
    let private iteratorOf (items: Fable.Core.JS.AsyncIterable<'T>) : obj =
        Fable.Core.Util.jsNative

    [<Fable.Core.Emit("$0.next()")>]
    let private nextOf (iterator: obj) : Task<obj> =
        Fable.Core.Util.jsNative

    [<Fable.Core.Emit("($0.return ? Promise.resolve($0.return()).catch(() => {}) : undefined)")>]
    let private returnOf (iterator: obj) : unit =
        Fable.Core.Util.jsNative

    [<Fable.Core.Emit("$0.done")>]
    let private isDone (result: obj) : bool =
        Fable.Core.Util.jsNative

    [<Fable.Core.Emit("$0.value")>]
    let private valueOf (result: obj) : 'T =
        Fable.Core.Util.jsNative

    /// <summary>
    /// Iterates on each start. Cancellation calls the iterator's <c>return ()</c> and faults the task with
    /// <c>OperationCanceledException</c>.
    /// </summary>
    let ofAsyncIterable (items: Fable.Core.JS.AsyncIterable<'T>) : StreamSource<'T> =
        if Platform.isNull (box items) then
            nullArg (nameof items)

        create (fun sink token ->
            let finished = Platform.completion<unit>()
            let iterator = iteratorOf items
            let mutable stopped = false

            token.Register (fun () ->
                stopped <- true

                try
                    returnOf iterator
                with _ ->
                    ()

                finished.Reject (cancelled token))
            |> ignore

            let rec step () =
                Platform.whenSettled (nextOf iterator) (fun outcome ->
                    if not stopped then
                        match outcome with
                        | Platform.FlightOutcome.Completed result when isDone result -> finished.Resolve ()
                        | Platform.FlightOutcome.Completed result ->
                            sink.Next (valueOf<'T> result)
                            step ()
                        | Platform.FlightOutcome.Faulted error
                        | Platform.FlightOutcome.Canceled error -> finished.Reject error)
                |> ignore

            step ()
            finished.Task :> Task)
#endif
