namespace Ranvier.View.WebCore

open System
open System.Runtime.InteropServices
open Fable.Core
open Fable.Core.TS.Dom
open RanvierWebCoreAbi

/// <summary>Native event listeners, dispatch and <c>currentTarget</c>.</summary>
[<Sealed; AbstractClass>]
type internal Events private () =
    static member private Run(context: nativeint, event: nativeint) : int =
        let target = GCHandle.FromIntPtr(context).Target :?> (Session * Registration)
        let session, registration = target
        session.ScriptDepth <- session.ScriptDepth + 1

        try
            try
                registration.Invoke (session.BorrowedEvent event)
            with error ->
                session.ListenerErrors.Add error
        finally
            session.ScriptDepth <- session.ScriptDepth - 1

            if
                session.ScriptDepth = 0
                && not (isNull session.AfterListener)
            then
                session.AfterListener.Invoke ()

        0

    static member val private Callback = Native.EventCallback (fun context event -> Events.Run (context, event))

    static member val private Pointer = Marshal.GetFunctionPointerForDelegate<Native.EventCallback> Events.Callback

    /// <summary>Registers <c>invoke</c> natively on the node; <c>callback</c> identifies the registration for removal.</summary>
    static member internal Listen(target: NativeObject, eventType: string, capture: bool, callback: obj, invoke: obj -> unit) : Registration =
        let session = target.Session

        let registration =
            Registration (Interop.handle (box target), eventType, capture, callback, invoke)

        let context = GCHandle.Alloc (box (session, registration))

        let listener =
            Native.rv_node_add_event_listener (registration.Target, eventType, (if capture then 1 else 0), Events.Pointer, GCHandle.ToIntPtr context)

        if listener = 0n then
            context.Free ()
            raise (InvalidOperationException $"rv_node_add_event_listener returned NULL for '{eventType}'")

        registration.Listener <- listener
        registration.Context <- context
        session.Registrations.Add registration |> ignore
        registration

    /// <summary>Removes a registration; a second removal does nothing.</summary>
    static member internal Unlisten(session: Session, registration: Registration) =
        if session.Registrations.Remove registration then
            Native.rv_listener_remove registration.Listener
            |> Interop.check "EventTarget.removeEventListener"

            registration.Context.Free ()

    /// <summary>Removes every registration of the session.</summary>
    static member internal RemoveAll(session: Session) =
        for registration in Seq.toArray session.Registrations do
            Events.Unlisten (session, registration)

    static member private Find(target: NativeObject, eventType: string, capture: bool, callback: obj) =
        let handle = target.Handle

        target.Session.Registrations
        |> Seq.tryFind (fun r ->
            r.Target = handle
            && r.Type = eventType
            && r.Capture = capture
            && obj.ReferenceEquals (r.Callback, callback))

    static member private Identity(callback: U2<Event -> unit, EventListenerObject>) : obj =
        match callback with
        | U2.Case1 f -> box f
        | U2.Case2 o -> box o

    /// <summary><c>EventTarget.addEventListener</c>: a repeated registration of one callback, type and phase is ignored.</summary>
    static member Add
        (
            target: NativeObject,
            eventType: string,
            callback: U2<Event -> unit, EventListenerObject> option,
            options: U2<bool, AddEventListenerOptions> option
        ) : unit =
        match callback with
        | None -> ()
        | Some callback ->
            let capture, once =
                match options with
                | Some (U2.Case1 capture) -> capture, false
                | Some (U2.Case2 options) ->
                    if options.signal.IsSome then
                        Interop.unsupported "AddEventListenerOptions.signal"

                    Option.defaultValue false options.capture, Option.defaultValue false options.once
                | None -> false, false

            let identity = Events.Identity callback

            if (Events.Find (target, eventType, capture, identity)).IsNone then
                let invoke: obj -> unit =
                    match callback with
                    | U2.Case1 f -> fun e -> f (unbox<Event> e)
                    | U2.Case2 o -> fun e -> o.handleEvent (unbox<Event> e)

                if once then
                    let registration: Registration ref = ref null

                    registration.Value <-
                        Events.Listen (
                            target,
                            eventType,
                            capture,
                            identity,
                            fun e ->
                                Events.Unlisten (target.Session, registration.Value)
                                invoke e
                        )
                else
                    Events.Listen (target, eventType, capture, identity, invoke)
                    |> ignore

    /// <summary><c>EventTarget.removeEventListener</c>.</summary>
    static member Remove
        (
            target: NativeObject,
            eventType: string,
            callback: U2<Event -> unit, EventListenerObject> option,
            options: U2<bool, EventListenerOptions> option
        ) : unit =
        match callback with
        | None -> ()
        | Some callback ->
            let capture =
                match options with
                | Some (U2.Case1 capture) -> capture
                | Some (U2.Case2 options) -> Option.defaultValue false options.capture
                | None -> false

            match Events.Find (target, eventType, capture, Events.Identity callback) with
            | Some registration -> Events.Unlisten (target.Session, registration)
            | None -> ()

    /// <summary>
    /// <c>EventTarget.dispatchEvent</c>: dispatches a native event synchronously and returns false when a listener
    /// cancelled it.
    /// </summary>
    static member Dispatch(target: NativeObject, event: Event) : bool =
        match box event with
        | :? NativeObject as native ->
            let session = target.Session
            let mutable prevented = 0
            session.ScriptDepth <- session.ScriptDepth + 1

            try
                Native.rv_node_dispatch (Interop.handle (box target), native.Handle, &prevented)
                |> Interop.check "EventTarget.dispatchEvent"
            finally
                session.ScriptDepth <- session.ScriptDepth - 1

            prevented = 0
        | _ -> raise (ArgumentException "dispatchEvent takes an event of the native backend")

    /// <summary>An init field of a host-created event, else the native value.</summary>
    static member Init<'T>(event: NativeObject, name: string, read: unit -> 'T) : 'T =
        match event.Init with
        | null -> read ()
        | init ->
            match init.TryGetValue name with
            | true, value -> unbox<'T> value
            | _ -> read ()

    /// <summary><c>Event.currentTarget</c>, or the override the view's delegation set for the dispatch.</summary>
    static member CurrentTarget(event: NativeObject) : EventTarget option =
        if isNull (box event.CurrentTargetOverride) then
            Interop.optionOf<EventTarget>(event.Session.Node (Native.rv_event_target (event.Handle, 1)))
        else
            match event.CurrentTargetOverride () with
            | null -> None
            | target -> Some (unbox<EventTarget> target)
