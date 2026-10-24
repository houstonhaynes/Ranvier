namespace Ranvier.Mcp

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open Ranvier

/// <summary>
/// A synchronisation context whose callbacks run in posting order on the one thread that calls <c>Run</c>.
/// </summary>
type internal GraphThreadContext() =
    inherit SynchronizationContext()

    let queue = new BlockingCollection<struct (SendOrPostCallback * obj)> ()
    let mutable thread = 0

    override _.Post(callback, state) =
        try
            queue.Add (struct (callback, state))
        with :? InvalidOperationException ->
            // The loop has stopped: the graph is disposed and every pending invocation has faulted.
            ()

    override this.Send(callback, state) =
        if Environment.CurrentManagedThreadId = thread then
            callback.Invoke state
        else
            use finished = new ManualResetEventSlim ()

            this.Post (
                (fun state ->
                    try
                        callback.Invoke state
                    finally
                        finished.Set ()),
                state
            )

            finished.Wait ()

    override this.CreateCopy() = this

    /// <summary>Runs posted callbacks on the calling thread until <c>Stop</c>; a callback's exception is dropped.</summary>
    member this.Run() =
        thread <- Environment.CurrentManagedThreadId

        for struct (callback, state) in queue.GetConsumingEnumerable () do
            try
                callback.Invoke state
            with _ ->
                ()

    /// <summary>Ends <c>Run</c> after the callbacks already posted; later posts are discarded.</summary>
    member _.Stop() =
        queue.CompleteAdding ()

/// <summary>
/// A <c>Guarded</c> graph on a dedicated thread, and the value its setup built. <c>Invoke</c> is the only entry from
/// another thread.
/// </summary>
/// <remarks>
/// The graph's inbox drains on the host thread through a <c>SynchronizationContextDispatcher</c>, so
/// <c>Graph.DispatchAsync</c> completes without a pump. The graph is ambient on the host thread for its whole life.
/// A node touched from any other thread raises <c>InvalidOperationException</c>.
/// </remarks>
[<Sealed>]
type GraphHost<'App> internal (options: GraphOptions, setup: Graph -> 'App) =
    let context = GraphThreadContext ()
    let started = new ManualResetEventSlim ()
    let mutable graph = Unchecked.defaultof<Graph>
    let mutable app = Unchecked.defaultof<'App>
    let mutable fault: exn = null
    let mutable disposing = 0

    let body () =
        SynchronizationContext.SetSynchronizationContext context

        try
            graph <- new Graph (options.WithDispatcher (SynchronizationContextDispatcher context))
            use _ = graph.Activate ()

            try
                app <- setup graph
            with ex ->
                fault <- ex

            started.Set ()

            if isNull fault then
                context.Run ()

            graph.Dispose ()
        with ex ->
            if not started.IsSet then
                fault <- ex
                started.Set ()

    let thread =
        Thread (ThreadStart body, IsBackground = true, Name = "Ranvier graph host")

    do
        thread.Start ()
        started.Wait ()

        if not (isNull fault) then
            thread.Join ()
            Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fault).Throw()

    /// <summary>The managed thread id of the host thread, which owns the graph.</summary>
    member _.GraphThreadId = thread.ManagedThreadId

    /// <summary>True once disposal has begun.</summary>
    member internal _.IsDisposing = Volatile.Read &disposing = 1

    /// <summary>
    /// Runs <c>work</c> on the host thread with the setup's value, through <c>Graph.DispatchAsync</c>, and returns a
    /// task that completes with its result after the work and the effects it triggers have run.
    /// </summary>
    /// <remarks>
    /// The task faults with the work's exception when the work throws. After disposal begins, the task faults with
    /// <c>ObjectDisposedException</c> and the work does not run, including work queued before disposal. Continuations
    /// run on the thread pool. Called on the host thread, the work runs before <c>Invoke</c> returns.
    /// </remarks>
    member this.Invoke(work: 'App -> 'T) : Task<'T> =
        this.Invoke (work, CancellationToken.None)

    /// <summary>
    /// <c>Invoke</c>, cancelled instead when <c>cancellationToken</c> is cancelled before the work starts.
    /// </summary>
    member this.Invoke(work: 'App -> 'T, cancellationToken: CancellationToken) : Task<'T> =
        if this.IsDisposing then
            Task.FromException<'T>(ObjectDisposedException "GraphHost")
        else
            graph.DispatchAsync (
                Func<'T>(fun () ->
                    if this.IsDisposing then
                        raise (ObjectDisposedException "GraphHost")

                    work app),
                cancellationToken
            )

    /// <summary>
    /// Disposes the graph on the host thread and stops the thread, after any work already running. Pending
    /// invocations fault with <c>ObjectDisposedException</c>. Idempotent.
    /// </summary>
    /// <remarks>Called from another thread, it returns once the host thread has stopped.</remarks>
    member _.Dispose() =
        if Interlocked.Exchange (&disposing, 1) = 0 then
            context.Stop ()

            if
                Environment.CurrentManagedThreadId
                <> thread.ManagedThreadId
            then
                thread.Join ()

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()

/// <summary>Starts graph hosts.</summary>
[<AbstractClass; Sealed>]
type GraphHost =
    /// <summary>
    /// Starts a host thread, builds a <c>Guarded</c> graph on it with the default options, and runs <c>setup</c>
    /// there. Returns once <c>setup</c> has returned.
    /// </summary>
    /// <exception cref="T:System.Exception">The exception <c>setup</c> raised; the host thread has then stopped.</exception>
    static member Start(setup: Graph -> 'App) : GraphHost<'App> =
        new GraphHost<'App> (GraphOptions.Default, setup)

    /// <summary>
    /// <c>Start</c>, with the graph built from <c>options</c>; the host replaces the dispatcher with its own.
    /// </summary>
    /// <exception cref="T:System.ArgumentException">
    /// <c>options.ThreadAffinity</c> is <c>Serialised</c> or <c>Unchecked</c>; no thread starts and <c>setup</c> does
    /// not run.
    /// </exception>
    static member Start(options: GraphOptions, setup: Graph -> 'App) : GraphHost<'App> =
        if options.ThreadAffinity <> Guarded then
            invalidArg
                (nameof options)
                $"A GraphHost graph is Guarded; ThreadAffinity {options.ThreadAffinity} would let a transport thread touch it."

        new GraphHost<'App> (options, setup)
