namespace Ranvier.Dom

open System
open System.Collections.Generic

/// <summary>The category of a DOM mutation.</summary>
[<RequireQualifiedAccess>]
type MutationType =
    | ChildList
    | Attributes
    | CharacterData

/// <summary>The categories, subtree and old values to observe on a node.</summary>
/// <remarks>None omits an option. A present old-value option or filter can enable its omitted category.</remarks>
type MutationObserverOptions =
    {
        ChildList: bool
        Subtree: bool
        Attributes: bool option
        CharacterData: bool option
        AttributeOldValue: bool option
        CharacterDataOldValue: bool option
        /// <summary>Attribute local names; Some empty observes no attributes.</summary>
        AttributeFilter: string array option
    }

    /// <summary>All categories disabled; Observe requires enabling at least one category.</summary>
    static member Default =
        {
            ChildList = false
            Subtree = false
            Attributes = None
            CharacterData = None
            AttributeOldValue = None
            CharacterDataOldValue = None
            AttributeFilter = None
        }

/// <summary>A copied mutation record whose nodes retain their ordinary DOM identity and lifetime.</summary>
/// <remarks>Strings and arrays remain readable after delivery or observer disposal. Nodes follow their host's lifetime.</remarks>
type MutationRecord =
    {
        Type: MutationType
        Target: Node
        AddedNodes: Node array
        RemovedNodes: Node array
        PreviousSibling: Node option
        NextSibling: Node option
        AttributeName: string option
        AttributeNamespace: string option
        /// <summary>The previous value when requested; None for unavailable values, Some empty for an empty value.</summary>
        OldValue: string option
    }

/// <summary>An observation failure carrying its portable DOM error name.</summary>
exception MutationObserverException of name: string * detail: string with
    override this.Message = this.name + ": " + this.detail

/// <summary>The engine operations of a mutation observer.</summary>
type MutationObserverHandle =
    {
        /// <summary>Observes an accessible node; the provider validates and copies the options before returning.</summary>
        Observe: Node -> MutationObserverOptions -> unit
        Disconnect: unit -> unit
        TakeRecords: unit -> MutationRecord array
        /// <summary>Disables future delivery and releases the observer and callback; in-flight callbacks finish safely.</summary>
        Release: unit -> unit
    }

/// <summary>Creates engine mutation observers with copied records and preserved node identity.</summary>
type MutationObservationProvider =
    {
        /// <summary>Creates an observer delivering records at the engine's mutation notification checkpoint.</summary>
        /// <remarks>
        /// Delivery begins after Create returns. Calls onClosed for provider-originated automatic closure.
        /// Reports callback exceptions through the host.
        /// </remarks>
        Create: (MutationRecord array -> unit) -> (unit -> unit) -> MutationObserverHandle
    }

/// <summary>A DOM mutation observer; disposal disconnects it and releases its callback.</summary>
/// <remarks>DOM operations follow the provider's owner-thread rules. Disposal may be requested off-thread;
/// a callback admitted before closure may finish.</remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type MutationObserver =
    inherit IDisposable
    /// <summary>Observes a node, replacing this observer's previous options for the same target.</summary>
    /// <remarks>Accessible nodes from other documents and adopted nodes are supported. Invalid options raise named TypeError.</remarks>
    abstract Observe: target: Node * options: MutationObserverOptions -> unit
    /// <summary>Stops observation and discards queued records; Observe can register targets again.</summary>
    abstract Disconnect: unit -> unit
    /// <summary>Drains the queued records without invoking the callback; returns empty after disposal.</summary>
    abstract TakeRecords: unit -> MutationRecord array

/// <summary>Creates mutation observers; disposal closes every observer it created.</summary>
/// <remarks>Cleanup attempts every observer and propagates the first cleanup failure after those attempts.</remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type MutationObservationService =
    inherit IDisposable
    /// <summary>Creates an observer passing each delivered batch and that observer to the callback.</summary>
    /// <remarks>Aborting signal disposes the observer. An already aborted signal returns a disposed observer.</remarks>
    abstract Create: callback: (MutationRecord array -> MutationObserver -> unit) * ?signal: AbortSignal -> MutationObserver

/// <summary>Creates mutation observation services over an engine provider.</summary>
[<RequireQualifiedAccess>]
module MutationObservation =
    let private finishCleanup (actions: (unit -> unit) seq) =
        let mutable first: exn option = None

        for action in actions do
            try
                action ()
            with error ->
                if first.IsNone then
                    first <- Some error

        match first with
        | Some error -> raise error
        | None -> ()

    /// <summary>A service over provider; calls on disposed services or observers raise named InvalidStateError.</summary>
    let create (provider: MutationObservationProvider) : MutationObservationService =
        if isNull (box provider) then
            nullArg "provider"

        let mutable disposed = false
        let observers = List<IDisposable>()
        let gate = obj ()

        let live () =
            lock gate (fun () ->
                if disposed then
                    raise (MutationObserverException ("InvalidStateError", "The mutation observation service is disposed")))

        let observer callback (signal: AbortSignal option) =
            let mutable released = false
            let mutable callback = Some callback
            let mutable handle: MutationObserverHandle option = None
            let mutable registration: IDisposable option = None

            let currentEngine () =
                lock gate (fun () ->
                    if released || disposed then
                        raise (MutationObserverException ("InvalidStateError", "The mutation observer is disposed"))

                    handle.Value)

            let self =
                { new MutationObserver with
                    member _.Observe(target, options) =
                        let engine = currentEngine ()

                        if isNull (box target) then
                            nullArg "target"

                        if isNull (box options) then
                            nullArg "options"

                        let snapshot =
                            match options.AttributeFilter with
                            | None -> options
                            | Some filter ->
                                if isNull filter then
                                    nullArg "options.AttributeFilter"

                                if Array.exists isNull filter then
                                    nullArg "options.AttributeFilter entry"

                                { options with
                                    AttributeFilter = Some (Array.copy filter)
                                }

                        engine.Observe target snapshot

                    member _.Disconnect() =
                        (currentEngine ()).Disconnect()

                    member _.TakeRecords() =
                        let engine = lock gate (fun () -> if released || disposed then None else handle)

                        match engine with
                        | Some value -> value.TakeRecords ()
                        | None -> [||]

                    member this.Dispose() =
                        let resources =
                            lock gate (fun () ->
                                if released then
                                    None
                                else
                                    released <- true
                                    callback <- None
                                    observers.Remove this |> ignore
                                    let owned = Some (registration, handle)
                                    registration <- None
                                    handle <- None
                                    owned)

                        match resources with
                        | None -> ()
                        | Some (abortRegistration, engine) ->
                            finishCleanup
                                [|
                                    (fun () ->
                                        abortRegistration
                                        |> Option.iter (fun value -> value.Dispose ()))
                                    (fun () ->
                                        engine
                                        |> Option.iter (fun value -> value.Release ()))
                                |]
                }

            lock gate (fun () ->
                if disposed then
                    raise (MutationObserverException ("InvalidStateError", "The mutation observation service is disposed"))

                observers.Add self)

            try
                match signal with
                | Some signal when signal.Aborted -> self.Dispose ()
                | _ ->
                    let engine =
                        provider.Create
                            (fun entries ->
                                let admitted = lock gate (fun () -> if released || disposed then None else callback)

                                admitted
                                |> Option.iter (fun deliver -> deliver entries self))
                            self.Dispose

                    let attached =
                        lock gate (fun () ->
                            if released || disposed then
                                false
                            else
                                handle <- Some engine
                                true)

                    if not attached then
                        finishCleanup [| self.Dispose; engine.Release |]
                    else
                        match signal with
                        | Some signal ->
                            let value = signal.OnAbort (fun _ -> self.Dispose ())

                            let registered =
                                lock gate (fun () ->
                                    if released || disposed then
                                        false
                                    else
                                        registration <- Some value
                                        true)

                            if not registered then
                                value.Dispose ()

                            if signal.Aborted then
                                self.Dispose ()
                        | None -> ()
            with error ->
                finishCleanup [| (fun () -> raise error); self.Dispose |]

            self

        { new MutationObservationService with
            member _.Create(callback, ?signal) =
                live ()

                if isNull (box callback) then
                    nullArg "callback"

                signal
                |> Option.iter (fun signal ->
                    if isNull (box signal) then
                        nullArg "signal")

                observer callback signal

            member _.Dispose() =
                let snapshot =
                    lock gate (fun () ->
                        if disposed then
                            [||]
                        else
                            disposed <- true
                            observers.ToArray ())

                snapshot
                |> Seq.map (fun observer -> fun () -> observer.Dispose ())
                |> finishCleanup
        }
