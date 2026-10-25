namespace Ranvier.View.Twin

open System
open Ranvier.View
open Ranvier.View.Testing

/// <summary>A twin document with its clock, installed as the thread's DOM host until the document is disposed.</summary>
[<Sealed>]
type TwinHost private (document: TwinDocument, clock: TwinClock) =
    member _.Document = document
    member _.Clock = clock

    static member Create() : TwinHost =
        Events.ensure ()
        SelectorMembers.ensure ()
        let document = new TwinDocument ()
        let host = TwinHost (document, TwinClock ())
        let native = Native.install host
        let events = EventSpec.install host
        document.OnDispose (fun () -> native.Dispose ())
        document.OnDispose (fun () -> events.Dispose ())
        host

    interface Native.IHost with
        member _.Document = document.Native :?> Fable.Core.TS.Dom.Document

        member _.QueueMicrotask callback =
            clock.Post callback

        member _.SetTimeout(action, milliseconds) =
            box (clock.SetTimeout (action, float milliseconds))

        member _.ClearTimeout timer =
            match timer with
            | :? int as id -> clock.ClearTimeout id
            | _ -> ()

        member _.RequestAnimationFrame callback =
            box (clock.RequestAnimationFrame callback)

        member _.CancelAnimationFrame frame =
            match frame with
            | :? int as id -> clock.CancelAnimationFrame id
            | _ -> ()

        member _.DeleteBetween(first, last) =
            let last = Bridge.nodeOf last
            let mutable cursor = (Bridge.nodeOf first).NextSibling

            while not (isNull cursor)
                  && not (obj.ReferenceEquals (cursor, last)) do
                let next = cursor.NextSibling
                cursor.Remove ()
                cursor <- next

        // Through addEventListener and removeEventListener, as the JavaScript body calls them, so a case's
        // interception of those members sees the view's listeners.
        member _.Listen(target, eventName, listener) =
            let target = target :?> Fable.Core.TS.Dom.EventTarget
            let callback: Fable.Core.TS.Dom.Event -> unit = fun e -> listener (box e)
            target.addEventListener (eventName, Fable.Core.U2.Case1 callback)
            fun () -> target.removeEventListener (eventName, Fable.Core.U2.Case1 callback)

        member _.OverrideCurrentTarget(event, read) =
            (Events.eventOf event).CurrentTargetOverride <- read

    interface Native.IExpandoHost with
        member _.ExpandoSlots(target, create) =
            match target with
            | :? NodeProxy as proxy ->
                if create && isNull proxy.Expandos then
                    proxy.Expandos <- Native.ExpandoSlots ()

                ValueSome proxy.Expandos
            | _ -> ValueNone

    interface EventSpec.IEventSource with
        member _.Create spec =
            Events.proxyOf (Events.create spec) :?> Fable.Core.TS.Dom.Event
