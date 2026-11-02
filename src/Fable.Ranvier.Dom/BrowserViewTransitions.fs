namespace Ranvier.Dom

open System
open Fable.Core
open Fable.Core.JsInterop

/// <summary>Same-document view transitions in the document's browser realm.</summary>
module BrowserViewTransitions =
    [<Emit("(() => { try { return $0.defaultView?.document===$0 && typeof $0.startViewTransition==='function'; } catch { return false; } })()")>]
    let private available (document: Document) : bool = jsNative

    [<Emit("$0.startViewTransition({ update: $1, types: $2 })")>]
    let private start (document: Document) (update: unit -> unit) (types: string array) : ViewTransition = jsNative

    [<Emit("$0.types.size")>]
    let private count (transition: ViewTransition) : uint32 = jsNative

    [<Emit("$0.types.has($1)")>]
    let private has (transition: ViewTransition) (value: string) : bool = jsNative

    [<Emit("Array.from($0.types)")>]
    let private values (transition: ViewTransition) : string array = jsNative

    [<Emit("$0.types.add($1)")>]
    let private add (transition: ViewTransition) (value: string) : unit = jsNative

    [<Emit("$0.types.delete($1)")>]
    let private remove (transition: ViewTransition) (value: string) : bool = jsNative

    [<Emit("$0.types.clear()")>]
    let private clear (transition: ViewTransition) : unit = jsNative

    [<Emit("$0[$1].then(()=>$2(null),e=>$2(e?.name||'Error'))")>]
    let private observePromise (transition: ViewTransition) (name: string) (callback: string -> unit) : unit = jsNative

    [<Emit("(() => { const r=$0.defaultView; if(typeof r?.reportError==='function')r.reportError($1); else setTimeout(()=>{throw $1}); })()")>]
    let private report (document: Document) (error: obj) : unit = jsNative

    /// <summary>A provider bound to the active document; raises NotSupportedException when transitions are unavailable.</summary>
    let forDocument (document: Document) : ViewTransitionService =
        if isNull (box document) then
            nullArg (nameof document)

        if not (available document) then
            raise (NotSupportedException "Same-document view transitions require an active supported document")

        let mutable disposed = false
        let registrations = ResizeArray<IDisposable>()

        let active () =
            if disposed then
                raise (ObjectDisposedException "ViewTransitionService")

            if not (available document) then
                invalidOp "The view-transition document is no longer active"

        { new ViewTransitionService with
            member _.Start(?update, ?types) =
                active ()
                let mutable live = true
                let observers = ResizeArray<IDisposable>()

                let run () =
                    if not live then
                        emitJsStatement () "throw new TypeError('The view-transition update was revoked.');"

                    match update with
                    | Some callback ->
                        try
                            callback ()
                        with _ ->
                            emitJsStatement () "throw new TypeError('The view-transition update failed.');"
                    | None -> ()

                let transition = start document run (defaultArg types [||])

                for promise in [ "updateCallbackDone"; "ready"; "finished" ] do
                    observePromise transition promise ignore

                let ensure () =
                    active ()

                    if not live then
                        raise (ObjectDisposedException "ViewTransitionRegistration")

                let typeSet =
                    { new ViewTransitionTypes with
                        member _.Count =
                            ensure ()
                            count transition

                        member _.Has value =
                            ensure ()
                            has transition value

                        member _.Values() =
                            ensure ()
                            values transition

                        member _.Add value =
                            ensure ()
                            add transition value

                        member _.Delete value =
                            ensure ()
                            remove transition value

                        member _.Clear() =
                            ensure ()
                            clear transition
                    }

                let mutable owned: IDisposable option = None

                let registration =
                    { new ViewTransitionRegistration with
                        member _.Transition =
                            ensure ()
                            transition

                        member _.Types =
                            ensure ()
                            typeSet

                        member _.Observe callback =
                            ensure ()

                            if isNull (box callback) then
                                nullArg (nameof callback)

                            let mutable observing = true

                            let observer =
                                { new IDisposable with
                                    member _.Dispose() =
                                        observing <- false
                                }

                            observers.Add observer

                            for name, kind in
                                [
                                    "updateCallbackDone", ViewTransitionPromise.UpdateCallbackDone
                                    "ready", ViewTransitionPromise.Ready
                                    "finished", ViewTransitionPromise.Finished
                                ] do
                                observePromise transition name (fun error ->
                                    if
                                        observing
                                        && live
                                        && not disposed
                                        && available document
                                    then
                                        try
                                            callback
                                                {
                                                    Promise = kind
                                                    ErrorName = Option.ofObj error
                                                }
                                        with error ->
                                            report document (box error))

                            observer

                        member _.Dispose() =
                            if live then
                                live <- false

                                for observer in observers do
                                    observer.Dispose ()

                                observers.Clear ()

                                if available document then
                                    transition.skipTransition ()

                                owned
                                |> Option.iter (fun token -> registrations.Remove token |> ignore)
                    }

                owned <- Some registration
                registrations.Add registration
                registration

            member _.Dispose() =
                if not disposed then
                    for registration in registrations.ToArray () do
                        registration.Dispose ()

                    registrations.Clear ()
                    disposed <- true
        }
