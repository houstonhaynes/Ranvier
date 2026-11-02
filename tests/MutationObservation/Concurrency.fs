module MutationObservationConcurrency

open System
open System.Threading
open Ranvier.Dom

let run (check: string -> bool -> unit) (named: string -> (unit -> unit) -> unit) =
    use entered = new ManualResetEventSlim (false)
    use stopped = new ManualResetEventSlim (false)
    let mutable releases = 0
    let mutable calls = 0
    let mutable deliver = ignore
    let mutable failure: exn option = None
    let mutable service = Unchecked.defaultof<MutationObservationService>

    service <-
        MutationObservation.create
            {
                Create =
                    fun callback _ ->
                        deliver <- callback
                        entered.Set ()

                        if not (stopped.Wait (5000)) then
                            failwith "Dispose blocked behind provider"

                        {
                            Observe = (fun _ _ -> ())
                            Disconnect = ignore
                            TakeRecords = (fun () -> [||])
                            Release = (fun () -> Interlocked.Increment (&releases) |> ignore)
                        }
            }

    let worker =
        Thread (fun () ->
            try
                if not (entered.Wait (5000)) then
                    failwith "factory did not start"

                service.Dispose ()
            with error ->
                failure <- Some error

            stopped.Set ())

    worker.IsBackground <- true
    worker.Start ()
    let observer = service.Create (fun _ _ -> calls <- calls + 1)

    if not (worker.Join (5000)) then
        failwith "cleanup worker did not finish"

    deliver [||]
    check "Create/dispose worker finishes outside gate" failure.IsNone
    check "Late factory handle released" (releases = 1)
    check "Late factory callback disabled" (calls = 0)
    named "InvalidStateError" observer.Disconnect
    observer.Dispose ()

    use admitted = new ManualResetEventSlim (false)
    use finish = new ManualResetEventSlim (false)
    let mutable admittedDeliver = ignore
    let mutable admittedCalls = 0
    let mutable admittedReleases = 0

    use admittedService =
        MutationObservation.create
            {
                Create =
                    fun callback _ ->
                        admittedDeliver <- callback

                        {
                            Observe = (fun _ _ -> ())
                            Disconnect = ignore
                            TakeRecords = (fun () -> [||])
                            Release =
                                (fun () ->
                                    Interlocked.Increment (&admittedReleases)
                                    |> ignore)
                        }
            }

    let active =
        admittedService.Create (fun _ _ ->
            admitted.Set ()

            if not (finish.Wait (5000)) then
                failwith "Dispose holds user callback gate"

            Interlocked.Increment (&admittedCalls) |> ignore)

    let callbackWorker = Thread (fun () -> admittedDeliver [||])
    callbackWorker.IsBackground <- true
    callbackWorker.Start ()

    if not (admitted.Wait (5000)) then
        failwith "callback not admitted"

    try
        active.Dispose ()
    finally
        finish.Set ()

    if not (callbackWorker.Join (5000)) then
        failwith "callback did not finish"

    admittedDeliver [||]
    check "Admitted callback finishes once" (admittedCalls = 1)
    check "Admitted callback disposal releases once" (admittedReleases = 1)

    let mutable abort: AbortReason -> unit = ignore
    let mutable closed: unit -> unit = ignore
    let mutable finalDeliver = ignore
    let mutable finalReleases = 0
    let mutable registrations = 0
    let mutable finalCalls = 0

    let signal =
        { new AbortSignal with
            member _.Aborted = false
            member _.Reason = None
            member _.ThrowIfAborted() = ()

            member _.OnAbort callback =
                abort <- callback

                { new IDisposable with
                    member _.Dispose() =
                        Interlocked.Increment (&registrations) |> ignore
                }
        }

    use finalService =
        MutationObservation.create
            {
                Create =
                    fun callback onClosed ->
                        closed <- onClosed
                        finalDeliver <- callback

                        {
                            Observe = (fun _ _ -> ())
                            Disconnect = ignore
                            TakeRecords = (fun () -> [||])
                            Release =
                                (fun () ->
                                    Interlocked.Increment (&finalReleases) |> ignore
                                    onClosed ())
                        }
            }

    let finalObserver =
        finalService.Create ((fun _ _ -> finalCalls <- finalCalls + 1), signal)

    use barrier = new Barrier (4)

    let racers =
        [| (fun () -> abort AbortReason.Abort); closed; finalObserver.Dispose |]
        |> Array.map (fun action ->
            let thread =
                Thread (fun () ->
                    barrier.SignalAndWait () |> ignore
                    action ())

            thread.IsBackground <- true
            thread.Start ()
            thread)

    barrier.SignalAndWait () |> ignore

    for thread in racers do
        if not (thread.Join (5000)) then
            failwith "simultaneous close did not finish"

    finalDeliver [||]
    finalService.Dispose ()
    finalObserver.Dispose ()
    check "Abort/onClosed/Dispose release both leases once" (finalReleases = 1 && registrations = 1)
    check "Simultaneous close suppresses later callback" (finalCalls = 0)
