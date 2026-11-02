module MutationObservationTests

open System
open System.Reflection
open System.Runtime.CompilerServices
open Ranvier.Dom

type NodeProxy() =
    inherit DispatchProxy()
    override _.Invoke(_, _) = null

let mutable checks = 0

let check message condition =
    checks <- checks + 1

    if not condition then
        failwith message

let named expected action =
    let mutable actual = None

    try
        action ()
    with MutationObserverException (name, _) ->
        actual <- Some name

    check ("Expected " + expected) (actual = Some expected)

let nullArgument action =
    let mutable rejected = false

    try
        action ()
    with :? ArgumentNullException ->
        rejected <- true

    check "Expected null argument rejection" rejected

type Signal() =
    let callbacks = ResizeArray<AbortReason -> unit>()
    let mutable aborted = false
    member _.Count = callbacks.Count

    member _.Abort() =
        aborted <- true

        for callback in callbacks.ToArray () do
            callback AbortReason.Abort

    interface AbortSignal with
        member _.Aborted = aborted
        member _.Reason = if aborted then Some AbortReason.Abort else None

        member _.ThrowIfAborted() =
            if aborted then
                raise (AbortedException AbortReason.Abort)

        member _.OnAbort callback =
            callbacks.Add callback

            { new IDisposable with
                member _.Dispose() =
                    callbacks.Remove callback |> ignore
            }

type Engine(deliver: MutationRecord array -> unit, closed: unit -> unit) =
    let mutable records = [||]
    let mutable releases = 0
    let mutable disconnects = 0
    let mutable observed = None
    member _.Releases = releases
    member _.Disconnects = disconnects
    member _.Observed = observed

    member _.Queue value =
        records <- value

    member _.Deliver value =
        deliver value

    member _.AutomaticClose() = closed ()

    member _.Handle =
        {
            Observe = fun target options -> observed <- Some (target, options)
            Disconnect =
                fun () ->
                    disconnects <- disconnects + 1
                    records <- [||]
            TakeRecords =
                fun () ->
                    let value = records in
                    records <- [||]
                    value
            Release =
                fun () ->
                    releases <- releases + 1
                    records <- [||]
                    closed ()
        }

let engines = ResizeArray<Engine>()

let provider =
    {
        Create =
            fun deliver closed ->
                let engine = Engine (deliver, closed) in
                engines.Add engine
                engine.Handle
    }

let node = DispatchProxy.Create<Node, NodeProxy>()

let record =
    {
        Type = MutationType.Attributes
        Target = node
        AddedNodes = [||]
        RemovedNodes = [||]
        PreviousSibling = None
        NextSibling = None
        AttributeName = Some "data-x"
        AttributeNamespace = None
        OldValue = Some "\u0000\uD800"
    }

[<MethodImpl(MethodImplOptions.NoInlining)>]
let callbackRetention (service: MutationObservationService) =
    let payload = obj ()
    let weak = WeakReference payload
    let observer = service.Create (fun _ _ -> GC.KeepAlive payload)
    observer, weak

let cleanupFailures () =
    let releases = ResizeArray<int>()
    let mutable next = 0
    let mutable takes = 0
    let mutable service = Unchecked.defaultof<MutationObservationService>
    let failure = InvalidOperationException ("release failure")
    let mutable caught = None

    service <-
        MutationObservation.create
            {
                Create =
                    fun _ _ ->
                        next <- next + 1
                        let id = next

                        {
                            Observe = (fun _ _ -> ())
                            Disconnect = ignore
                            TakeRecords =
                                (fun () ->
                                    takes <- takes + 1
                                    [||])
                            Release =
                                (fun () ->
                                    releases.Add id
                                    service.Dispose ()

                                    if id = 1 then
                                        raise failure)
                        }
            }

    let first = service.Create (fun _ _ -> ())
    let second = service.Create (fun _ _ -> ())

    try
        service.Dispose ()
    with error ->
        caught <- Some error

    check
        "Release failure preserves first error"
        (caught
         |> Option.exists (fun error -> obj.ReferenceEquals (error, failure)))

    check "Release failure continues snapshot cleanup" (releases.ToArray () = [| 1; 2 |])
    second.TakeRecords () |> ignore
    check "Later observer is closed after first release failure" (takes = 0)
    service.Dispose ()
    first.Dispose ()
    second.Dispose ()
    check "Failed/reentrant service disposal remains idempotent" (releases.ToArray () = [| 1; 2 |])

    let phases = ResizeArray<string>()
    let deliveries = ResizeArray<MutationRecord array -> unit>()
    let mutable nextRegistration = 0
    let mutable nextEngine = 0
    let mutable calls = 0
    let registrationFailure = InvalidOperationException ("registration release failure")

    let signal =
        { new AbortSignal with
            member _.Aborted = false
            member _.Reason = None
            member _.ThrowIfAborted() = ()

            member _.OnAbort _ =
                nextRegistration <- nextRegistration + 1
                let id = nextRegistration

                { new IDisposable with
                    member _.Dispose() =
                        phases.Add ("abort" + string id)
                        raise registrationFailure
                }
        }

    let failingService =
        MutationObservation.create
            {
                Create =
                    fun deliver closed ->
                        deliveries.Add deliver
                        nextEngine <- nextEngine + 1
                        let id = nextEngine

                        {
                            Observe = (fun _ _ -> ())
                            Disconnect = ignore
                            TakeRecords = (fun () -> failwith "closed observer consulted provider")
                            Release =
                                (fun () ->
                                    phases.Add ("engine" + string id)
                                    closed ()
                                    failwith "secondary engine release failure")
                        }
            }

    let left = failingService.Create ((fun _ _ -> calls <- calls + 1), signal)
    let right = failingService.Create ((fun _ _ -> calls <- calls + 1), signal)
    caught <- None

    try
        failingService.Dispose ()
    with error ->
        caught <- Some error

    check
        "Abort registration failure remains first"
        (caught
         |> Option.exists (fun error -> obj.ReferenceEquals (error, registrationFailure)))

    check "All registrations and engines attempted after failures" (phases.ToArray () = [| "abort1"; "engine1"; "abort2"; "engine2" |])

    for deliver in deliveries do
        deliver [||]

    check "Cleanup failures still clear every callback" (calls = 0)
    failingService.Dispose ()
    left.Dispose ()
    right.Dispose ()

    check
        "Registration/engine failure retries do not repeat release"
        (phases.Count = 4
         && left.TakeRecords().Length = 0
         && right.TakeRecords().Length = 0)

[<EntryPoint>]
let main _ =
    nullArgument (fun () ->
        MutationObservation.create Unchecked.defaultof<_>
        |> ignore)

    use service = MutationObservation.create provider
    nullArgument (fun () -> service.Create Unchecked.defaultof<_> |> ignore)

    nullArgument (fun () ->
        service.Create ((fun _ _ -> ()), signal = Unchecked.defaultof<_>)
        |> ignore)

    let mutable delivered = [||]
    let mutable callbackObserver = None

    use observer =
        service.Create (fun records self ->
            delivered <- records
            callbackObserver <- Some self)

    let engine = engines[0]
    check "Create does not deliver" (Array.isEmpty delivered)
    engine.Deliver [| record |]
    check "Callback gets exact observer" (obj.ReferenceEquals (callbackObserver.Value, observer))
    check "Record keeps node identity" (obj.ReferenceEquals (delivered[0].Target, node))
    check "Record keeps UTF16" (delivered[0].OldValue = Some "\u0000\uD800")

    nullArgument (fun () -> observer.Observe (null, MutationObserverOptions.Default))
    nullArgument (fun () -> observer.Observe (node, Unchecked.defaultof<_>))

    nullArgument (fun () ->
        observer.Observe (
            node,
            { MutationObserverOptions.Default with
                AttributeFilter = Some null
            }
        ))

    nullArgument (fun () ->
        observer.Observe (
            node,
            { MutationObserverOptions.Default with
                AttributeFilter = Some [| null |]
            }
        ))

    let filter = [| "data-x"; ""; "\u0000\uD800" |]

    observer.Observe (
        node,
        { MutationObserverOptions.Default with
            AttributeFilter = Some filter
            AttributeOldValue = Some false
        }
    )

    filter[0] <- "changed"
    let target, options = engine.Observed.Value
    check "Observe preserves node identity" (obj.ReferenceEquals (target, node))
    check "Observe copies filter" (options.AttributeFilter.Value[0] = "data-x")
    check "Observe preserves optional false" (options.AttributeOldValue = Some false)
    check "Observe preserves absent category" options.Attributes.IsNone
    check "Observe preserves filter UTF16" (options.AttributeFilter.Value[2] = "\u0000\uD800")

    observer.Observe (
        node,
        { MutationObserverOptions.Default with
            AttributeFilter = Some [||]
        }
    )

    check "Present empty filter retained" (engine.Observed.Value |> snd |> _.AttributeFilter = Some [||])

    engine.Queue [| record |]
    check "TakeRecords drains queue" (observer.TakeRecords().Length = 1)
    check "Second drain empty" (observer.TakeRecords().Length = 0)
    engine.Queue [| record |]
    observer.Disconnect ()
    check "Disconnect calls engine" (engine.Disconnects = 1)
    check "Disconnect discards queue" (observer.TakeRecords().Length = 0)

    observer.Observe (
        node,
        { MutationObserverOptions.Default with
            ChildList = true
        }
    )

    check "Observe after disconnect" ((snd engine.Observed.Value).ChildList)

    let mutable selfCalls = 0

    use selfClosing =
        service.Create (fun _ self ->
            selfCalls <- selfCalls + 1
            self.Dispose ())

    let selfEngine = engines[1]
    selfEngine.Deliver [| record |]
    selfEngine.Deliver [| record |]
    check "Self close stops later callbacks" (selfCalls = 1)
    check "Self close releases once" (selfEngine.Releases = 1)
    named "InvalidStateError" (fun () -> selfClosing.Observe (node, MutationObserverOptions.Default))
    named "InvalidStateError" selfClosing.Disconnect
    check "Disposed drain empty" (selfClosing.TakeRecords().Length = 0)
    selfClosing.Dispose ()
    check "Repeat dispose idempotent" (selfEngine.Releases = 1)

    let signal = Signal ()
    let mutable cancelledCalls = 0

    let cancelled =
        service.Create ((fun _ _ -> cancelledCalls <- cancelledCalls + 1), signal)

    let cancelEngine = engines[2]
    check "Abort registration retained" (signal.Count = 1)
    signal.Abort ()
    cancelEngine.Deliver [| record |]
    check "Abort stops delivery" (cancelledCalls = 0)
    check "Abort releases engine" (cancelEngine.Releases = 1)
    check "Abort removes registration" (signal.Count = 0)
    check "Aborted observer drain empty" (cancelled.TakeRecords().Length = 0)
    let count = engines.Count

    use alreadyAborted =
        service.Create ((fun _ _ -> failwith "already aborted callback"), signal)

    check "Already aborted allocates no engine" (engines.Count = count)

    let mutable leakedDelivery = ignore

    use failed =
        MutationObservation.create
            {
                Create =
                    fun deliver _closed ->
                        leakedDelivery <- deliver
                        failwith "create failed"
            }

    let mutable failedCalls = 0
    let mutable createFailed = false

    try
        failed.Create (fun _ _ -> failedCalls <- failedCalls + 1)
        |> ignore
    with _ ->
        createFailed <- true

    check "Create failure preserved" createFailed
    leakedDelivery [| record |]
    check "Create failure clears callback" (failedCalls = 0)

    let mutable releaseRegistration = 0

    let brokenSignal =
        { new AbortSignal with
            member _.Aborted = false
            member _.Reason = None
            member _.ThrowIfAborted() = ()

            member _.OnAbort _ =
                failwith "registration failed"
        }

    try
        service.Create ((fun _ _ -> ()), brokenSignal)
        |> ignore
    with _ ->
        releaseRegistration <- engines[3].Releases

    check "Registration failure releases engine" (releaseRegistration = 1)

    let mutable reentrantCalls = 0
    let reentrantService = MutationObservation.create provider

    let closer =
        reentrantService.Create (fun _ _ ->
            reentrantCalls <- reentrantCalls + 1
            reentrantService.Dispose ())

    let closerEngine = engines[4]
    closerEngine.Deliver [| record |]
    closerEngine.Deliver [| record |]
    check "Service dispose in callback" (reentrantCalls = 1 && closerEngine.Releases = 1)
    named "InvalidStateError" (fun () -> reentrantService.Create (fun _ _ -> ()) |> ignore)
    closer.Dispose ()
    service.Dispose ()
    check "Service closes remaining observer" (engine.Releases = 1)
    engine.Deliver [| record |]
    named "InvalidStateError" (fun () -> service.Create (fun _ _ -> ()) |> ignore)
    use retentionService = MutationObservation.create provider
    let retainedObserver, weak = callbackRetention retentionService
    GC.Collect ()
    GC.WaitForPendingFinalizers ()
    check "Active observer retains callback payload" weak.IsAlive
    retainedObserver.Dispose ()
    GC.Collect ()
    GC.WaitForPendingFinalizers ()
    GC.Collect ()
    check "Retained disposed observer releases callback payload" (not weak.IsAlive)
    GC.KeepAlive retainedObserver
    use automaticService = MutationObservation.create provider
    let automaticObserver, automaticWeak = callbackRetention automaticService
    let automaticEngine = engines[engines.Count - 1]
    automaticEngine.AutomaticClose ()
    automaticEngine.AutomaticClose ()
    automaticEngine.Deliver [| record |]
    GC.Collect ()
    GC.WaitForPendingFinalizers ()
    GC.Collect ()
    check "Automatic closure releases callback payload" (not automaticWeak.IsAlive)
    check "Automatic closure releases once despite reentrancy" (automaticEngine.Releases = 1)
    named "InvalidStateError" automaticObserver.Disconnect
    check "Automatic closure drain empty" (automaticObserver.TakeRecords().Length = 0)
    automaticObserver.Dispose ()
    GC.KeepAlive automaticObserver
    let autoSignal = Signal ()

    let autoSignalled =
        automaticService.Create ((fun _ _ -> failwith "closed signal callback"), autoSignal)

    let autoSignalEngine = engines[engines.Count - 1]
    autoSignalEngine.AutomaticClose ()
    check "Automatic closure removes abort registration" (autoSignal.Count = 0)
    autoSignal.Abort ()
    autoSignalled.Dispose ()
    check "Automatic closure/abort/dispose release once" (autoSignalEngine.Releases = 1)
    let mutable immediateReleases = 0

    use immediateService =
        MutationObservation.create
            {
                Create =
                    fun _ closed ->
                        closed ()

                        {
                            Observe = (fun _ _ -> failwith "closed admission")
                            Disconnect = (fun () -> failwith "closed admission")
                            TakeRecords = (fun () -> failwith "closed admission")
                            Release =
                                (fun () ->
                                    immediateReleases <- immediateReleases + 1
                                    closed ())
                        }
            }

    use immediateObserver =
        immediateService.Create (fun _ _ -> failwith "immediate callback")

    check "Closure during Create releases returned handle" (immediateReleases = 1)
    check "Closure during Create returns disposed observer" (immediateObserver.TakeRecords().Length = 0)

    cleanupFailures ()

    MutationObservationConcurrency.run check named

    if checks <> 67 then
        failwithf "Incomplete shared fixture: %d" checks

    printfn "MutationObservation shared: %d assertions PASS" checks
    0
