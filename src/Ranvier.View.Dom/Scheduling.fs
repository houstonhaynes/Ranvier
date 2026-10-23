module Ranvier.View.Dom.Scheduling

open Ranvier.View
open System
open Ranvier

#if FABLE_COMPILER
open Fable.Core
#endif

type CommitFailure =
    | CollectAndContinue
    | FailFastCancel

type Slot internal (owner: obj, scope: ViewScope) =
    member internal _.Owner = owner
    member internal _.Scope = scope
    member val internal Version = 0 with get, set
    member val internal Listed = false with get, set
    member val internal Action: (unit -> unit) option = None with get, set
    /// <summary>Runs, when not null, after a flush stopped by <c>FailFastCancel</c> cancels the slot's current commit.</summary>
    member val internal Dropped: unit -> unit = Unchecked.defaultof<_> with get, set

#if FABLE_COMPILER
[<Fable.Core.Emit("$0.map($1)")>]
let private snapshot (pending: ResizeArray<Slot>) (project: Slot -> Slot * int * (unit -> unit) option) : (Slot * int * (unit -> unit) option) array =
    jsNative
#endif

type Queue(graph: Graph, failure: CommitFailure, post: (unit -> unit) -> unit) as this =
    let pending = ResizeArray<Slot>()
    let mutable disposed = false
    let mutable flushing = false
    let mutable scheduled = false
    let mutable generation = 0
    let mutable errors: exn array = [||]

    let owned (slot: Slot) =
        if not (obj.ReferenceEquals (slot.Owner, this)) then
            invalidArg "slot" "Commit slot belongs to another queue"

    member private _.Request() =
        if
            not disposed
            && not flushing
            && not scheduled
            && pending.Count > 0
        then
            scheduled <- true
            let ticket = generation

            try
                post (fun () ->
                    if not disposed && ticket = generation then
                        this.Flush ())
            with _ ->
                scheduled <- false
                reraise ()

    member _.Reserve() =
        if disposed then
            raise (InvalidOperationException "Commit queue is disposed")

        Slot (this, Unchecked.defaultof<_>)

    /// <summary>Reserves a slot whose commit failures go to <c>scope</c> after each flush.</summary>
    member internal _.Reserve(scope: ViewScope) =
        if disposed then
            raise (InvalidOperationException "Commit queue is disposed")

        Slot (this, scope)

    member _.Cancel(slot: Slot) =
        owned slot
        slot.Version <- slot.Version + 1
        slot.Action <- None

    member _.Enqueue(slot: Slot, action: unit -> unit) =
        owned slot

        if not disposed then
            slot.Version <- slot.Version + 1
            slot.Action <- Some action

            if not slot.Listed then
                slot.Listed <- true
                pending.Add slot

            this.Request ()

    member _.Flush() =
        if flushing then
            raise (InvalidOperationException "Recursive commit flushing is prohibited")

        if not disposed then
            generation <- generation + 1
            scheduled <- false
            flushing <- true
            let failures = ResizeArray<exn>()
            let mutable failedSlots: ResizeArray<Slot> = null

            try
                graph.Run (fun () ->
                    Api.flush ()

                    let work =
#if FABLE_COMPILER
                        snapshot pending (fun slot -> slot, slot.Version, slot.Action)
#else
                        pending
                        |> Seq.map (fun slot -> slot, slot.Version, slot.Action)
                        |> Seq.toArray
#endif

                    pending.Clear ()

                    for slot, _, _ in work do
                        slot.Listed <- false
                        slot.Action <- None

                    let mutable stopped = false

                    for slot, version, action in work do
                        if stopped then
                            let dropped = action.IsSome && slot.Version = version
                            this.Cancel slot

                            if dropped && not (isNull (box slot.Dropped)) then
                                slot.Dropped ()
                        elif not disposed && slot.Version = version then
                            match action with
                            | None -> ()
                            | Some commit ->
                                try
                                    Api.untrack commit
                                with error ->
                                    failures.Add error

                                    if isNull failedSlots then
                                        failedSlots <- ResizeArray<Slot>()

                                    failedSlots.Add slot
                                    stopped <- failure = FailFastCancel

                    if failures.Count > 0 then
                        Queue.Report (failures, failedSlots))
            finally
                errors <- failures.ToArray ()
                flushing <- false
                this.Request ()

    member _.Errors = Array.copy errors

#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
    /// <summary>Whether the queue is running its pending commits.</summary>
    member internal _.Flushing = flushing
#endif

    /// <summary>Sends each scope the failures of its slots in one call, in commit order.</summary>
    static member private Report(failures: ResizeArray<exn>, slots: ResizeArray<Slot>) =
        let reporterErrors = ResizeArray<exn>()

        for index in 0 .. slots.Count - 1 do
            let scope = slots[index].Scope
            let mutable first = not (isNull (box scope))
            let mutable earlier = 0

            while first && earlier < index do
                if obj.ReferenceEquals (slots[earlier].Scope, scope) then
                    first <- false

                earlier <- earlier + 1

            if first then
                let errors =
                    [
                        for later in index .. slots.Count - 1 do
                            if obj.ReferenceEquals (slots[later].Scope, scope) then
                                failures[later]
                    ]

                try
                    scope.Fail errors
                with error ->
                    reporterErrors.Add error

        if reporterErrors.Count = 1 then
            raise reporterErrors[0]
        elif reporterErrors.Count > 1 then
            raise (AggregateException ("Commit failure reporting failed", reporterErrors))

    member _.Dispose() =
        if not disposed then
            disposed <- true
            generation <- generation + 1
            scheduled <- false

            for slot in pending do
                slot.Listed <- false
                this.Cancel slot

            pending.Clear ()

type Scheduling =
    | Synchronous
    | Queued of Queue

type CommitMode =
    | SynchronousCommits
    | QueuedCommits of CommitFailure * ((unit -> unit) -> unit)
