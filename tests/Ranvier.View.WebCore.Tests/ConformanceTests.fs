/// <summary>
/// The conformance modules of the DOM twin (RangeChecks, TemplateChecks, DelegationChecks, TestingApiChecks,
/// BoundaryChecks, PlacementChecks), each case run on a new page and on a new twin document, with equal artefacts.
/// </summary>
module Ranvier.View.WebCore.Tests.ConformanceTests

open System
open System.Collections.Generic
open Expecto
open Fable.Core.TS.Dom
open Ranvier.View.Testing
open Ranvier.View.Twin
open Ranvier.View.WebCore
open Ranvier.View.WebCore.Tests.Support

/// <summary>Creates events on the page: a native event of the spec's interface, its init fields kept on the object.</summary>
let private eventSource (host: WebCoreHost) =
    { new EventSpec.IEventSource with
        member _.Create spec =
            let session = host.Session

            let kind =
                match spec.Kind with
                | Plain -> 0
                | Mouse -> 2
                | Pointer -> 3
                | Keyboard -> 4
                | Input -> 5
                | Focus -> 6

            let handle =
                RanvierWebCoreAbi.Native.rv_host_create_event (
                    host.Handle,
                    kind,
                    spec.Type,
                    (if spec.Bubbles then 1 else 0),
                    (if spec.Cancelable then 1 else 0)
                )

            let event: NativeObject =
                match spec.Kind with
                | Plain -> EventObject (session, handle)
                | Mouse -> MouseEventObject (session, handle)
                | Pointer -> PointerEventObject (session, handle)
                | Keyboard -> KeyboardEventObject (session, handle)
                | Input -> InputEventObject (session, handle)
                | Focus -> FocusEventObject (session, handle)

            session.Register event
            event.Init <- Dictionary (dict spec.Init)
            unbox<Event> event
    }

let private childList =
    set
        [
            "appendChild"
            "insertBefore"
            "removeChild"
            "replaceChild"
            "append"
            "prepend"
            "replaceChildren"
            "set_innerHTML"
        ]

let private ofParent = set [ "remove"; "before"; "after"; "replaceWith" ]

let private attributes =
    set
        [
            "setAttribute"
            "removeAttribute"
            "toggleAttribute"
            "setAttributeNS"
            "removeAttributeNS"
            "set_id"
            "set_className"
            "set_title"
            "set_lang"
            "set_dir"
            "set_hidden"
            "set_disabled"
            "set_href"
            "set_src"
            "set_alt"
            "set_name"
            "set_type"
            "set_placeholder"
            "set_htmlFor"
            "set_slot"
            "set_defaultValue"
            "set_defaultChecked"
        ]

let private characterData =
    set
        [
            "set_data"
            "set_nodeValue"
            "appendData"
            "insertData"
            "deleteData"
            "replaceData"
        ]

/// <summary>
/// The page has no MutationObserver without script, so the harness derives the records from the DOM members the
/// view calls, as a MutationObserver over <c>root</c>'s subtree reports them: one record per change, on its target.
/// </summary>
let private observeMutations (host: WebCoreHost) (root: Node) : unit -> (string * Node) array =
    let changes = List<string * Node>()

    let under (node: obj) =
        match node with
        | :? Node as node -> root.contains node
        | _ -> false

    let add kind (target: obj) =
        if under target then
            changes.Add (kind, unbox<Node> target)

    let parentOf (node: obj) : obj =
        match node with
        | :? Node as node -> node.parentNode |> Option.map box |> Option.toObj
        | _ -> null

    // A node inserted from another parent leaves a record on that parent too.
    let moved (node: obj) =
        match node with
        | :? DocumentFragment -> ()
        | _ ->
            match parentOf node with
            | null -> ()
            | parent -> add "childList" parent

    let restores =
        [
            for name in Seq.concat [ childList; ofParent; attributes; characterData ] ->
                host.Intercept (
                    null,
                    name,
                    Func<Func<obj array, obj>, obj, obj array, obj>(fun original target args ->
                        let isText = target :? CharacterData

                        let empty =
                            match (if args.Length > 0 then args[0] else null) with
                            | :? DocumentFragment as fragment -> fragment.firstChild.IsNone
                            | _ -> false

                        let parent = parentOf target

                        if
                            (name = "appendChild"
                             || name = "insertBefore"
                             || name = "replaceChild")
                            && not empty
                        then
                            moved args[0]

                        let result = original.Invoke args

                        if childList.Contains name then
                            if not empty then
                                add "childList" target
                        elif ofParent.Contains name then
                            add "childList" parent
                        elif attributes.Contains name then
                            add "attributes" target
                        elif isText || characterData.Contains name then
                            add "characterData" target

                        result)
                )
            yield
                host.Intercept (
                    null,
                    "set_textContent",
                    Func<Func<obj array, obj>, obj, obj array, obj>(fun original target args ->
                        let result = original.Invoke args

                        add
                            (if target :? CharacterData then
                                 "characterData"
                             else
                                 "childList")
                            target

                        result)
                )
        ]

    host.DeletedFrom <- Action<obj>(fun parent -> add "childList" parent)

    fun () ->
        for restore in restores do
            restore ()

        host.DeletedFrom <- null
        changes.ToArray ()

let private webcore (records: List<string * string>) (run: Conformance.Harness -> unit) =
    use host = WebCoreHost.Create (800, 600)
    host.UseManualClock ()
    use _ = EventSpec.install (eventSource host)

    let harness =
        { new Conformance.Harness with
            member _.Platform = "webcore"
            member _.Document = host.Document

            member _.Post callback =
                host.QueueMicrotask callback

            member _.RunMicrotasks() =
                host.RunMicrotasks ()

            member _.AdvanceBy milliseconds =
                host.AdvanceBy milliseconds

            member _.ListenerErrors() =
                let messages = [ for error in host.Errors -> error.Message ]
                host.Errors.Clear ()
                messages

            member _.Intercept(target, memberName, wrap) =
                match target with
                | null
                | :? NativeObject -> host.Intercept (target, memberName, wrap)
                | other -> raise (NotSupportedException $"The page intercepts the members of nodes only, not of a {other.GetType().Name}")

            member _.ObserveMutations root =
                observeMutations host root

            member _.Record(name, text) =
                records.Add (name, text)
        }

    run harness

let private twin (records: List<string * string>) (run: Conformance.Harness -> unit) =
    let host = TwinHost.Create ()

    try
        let harness =
            { new Conformance.Harness with
                member _.Platform = "twin"
                member _.Document = host.Document.Native :?> Document

                member _.Post callback =
                    host.Clock.Post callback

                member _.RunMicrotasks() =
                    host.Clock.RunMicrotasks () |> ignore

                member _.AdvanceBy milliseconds =
                    host.Clock.AdvanceBy milliseconds

                member _.ListenerErrors() =
                    let messages =
                        [
                            for error in host.Document.ListenerErrors ->
                                match error with
                                | TwinListenerException (_, inner) -> inner.Message
                                | other -> other.Message
                        ]

                    host.Document.ListenerErrors.Clear ()
                    messages

                member _.Intercept(target, memberName, wrap) =
                    match target, Bridge.nodeOf target with
                    | null, _ -> Bridge.intercept host.Document null memberName wrap
                    | _, null -> raise (NotSupportedException $"The twin intercepts the members of nodes only, not of a {target.GetType().Name}")
                    | _, node -> Bridge.intercept host.Document node memberName wrap

                member _.ObserveMutations root =
                    let under = Bridge.nodeOf root
                    let changes = List<string * Node>()

                    let observer =
                        host.Document.ObserveMutations (fun (kind, target) ->
                            if under.Contains target then
                                changes.Add ((kind, unbox target.Native)))

                    fun () ->
                        observer.Dispose ()
                        changes.ToArray ()

                member _.Record(name, text) =
                    records.Add (name, text)
            }

        run harness
    finally
        (host.Document :> IDisposable).Dispose()

let private runCase (run: Conformance.Harness -> unit) () =
    onHostThread (fun () ->
        let expected = List<string * string>()
        twin expected run
        let actual = List<string * string>()
        webcore actual run
        Expect.sequenceEqual actual expected "the artefacts the twin recorded")

/// <summary>
/// The cases that need a DOM member the native ABI lacks: a second document (DOMImplementation.createHTMLDocument) and
/// createElementNS. They are pending until Ranvier.WebCore maps those members.
/// </summary>
let private needsAbi =
    set
        [
            "each document keeps its own title and adopts only its own marked elements"
            "a failed hydration in a document without a window leaves its head as the server wrote it"
            "a kept head restored after children were added holds the children it held"
            "an element with a prefix is a mismatch for a template element of its local name, named at its path"
        ]

[<Tests>]
let tests =
    testList
        "Conformance"
        [
            for moduleName, cases in ConformanceCases.modules do
                testList
                    moduleName
                    [
                        for name, run in cases do
                            if
                                Conformance.skippedOn "webcore" name
                                || needsAbi.Contains name
                            then
                                ptestCase name ignore
                            else
                                testCase name (runCase run)
                    ]
        ]
