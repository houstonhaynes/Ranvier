module Ranvier.View.Twin.Tests.EventTests

open Expecto
open Fable.Core.TS.Dom
open Ranvier.View.Twin

let private setup () =
    Events.ensure ()
    let document = new TwinDocument ()
    let outer = document.CreateElement "div"
    let inner = document.CreateElement "button"
    outer.AppendChild inner |> ignore
    document.Body.AppendChild outer |> ignore
    document, outer, inner

let private plain name bubbles =
    Events.create
        {
            Type = name
            Kind = Ranvier.View.Testing.Plain
            Bubbles = bubbles
            Cancelable = true
            Init = []
        }

[<Tests>]
let tests =
    testList
        "Events"
        [
            testCase "a focused element removed and appended again is not active"
            <| fun () ->
                Events.ensure ()
                use document = new TwinDocument ()
                let input = document.CreateElement "input"
                document.Body.AppendChild input |> ignore
                Events.focus input
                input.Remove ()
                document.Body.AppendChild input |> ignore
                Expect.isNull document.ActiveElement "focus fixup ran at the removal"

            testCase "a control in a disabled fieldset neither clicks nor submits"
            <| fun () ->
                Events.ensure ()
                use document = new TwinDocument ()
                let form = document.CreateElement "form"
                Html.parseInto form "<fieldset disabled><button>go</button></fieldset>"
                document.Body.AppendChild form |> ignore
                let button = form.FirstChild.FirstChild :?> TwinElement
                let clicks, submits = ref 0, ref 0

                Events.listen button "click" false (fun _ -> clicks.Value <- clicks.Value + 1)
                |> ignore

                Events.listen form "submit" false (fun _ -> submits.Value <- submits.Value + 1)
                |> ignore

                Events.click button
                Expect.equal (clicks.Value, submits.Value) (0, 0) "disabled by the fieldset"

            testCase "listener failures are recorded in the order they happen, across nested dispatches"
            <| fun () ->
                let document, outer, inner = setup ()
                use _ = document

                Events.listen outer "outer" false (fun _ -> failwith "first")
                |> ignore

                Events.listen outer "outer" false (fun _ ->
                    Events.dispatch inner (plain "inner" true)
                    |> ignore)
                |> ignore

                Events.listen inner "inner" false (fun _ -> failwith "second")
                |> ignore

                Events.dispatch outer (plain "outer" true)
                |> ignore

                Expect.equal
                    [
                        for e in document.ListenerErrors ->
                            match e with
                            | TwinListenerException (_, inner) -> inner.Message
                            | other -> other.Message
                    ]
                    [ "first"; "second" ]
                    "chronological"

            testCase "capture runs root to target, then target, then bubble"
            <| fun () ->
                let document, outer, inner = setup ()
                use _ = document
                let log = ResizeArray<string>()

                Events.listen outer "x" true (fun _ -> log.Add "outer capture")
                |> ignore

                Events.listen inner "x" false (fun _ -> log.Add "inner")
                |> ignore

                Events.listen outer "x" false (fun _ -> log.Add "outer bubble")
                |> ignore

                Events.dispatch inner (plain "x" true) |> ignore
                Expect.equal (List.ofSeq log) [ "outer capture"; "inner"; "outer bubble" ] "order"

            testCase "a non-bubbling event skips the bubble phase"
            <| fun () ->
                let document, outer, inner = setup ()
                use _ = document
                let log = ResizeArray<string>()

                Events.listen outer "x" false (fun _ -> log.Add "outer")
                |> ignore

                Events.dispatch inner (plain "x" false) |> ignore
                Expect.isEmpty log "no bubble"

            testCase "stopPropagation finishes the node's listeners; stopImmediatePropagation does not"
            <| fun () ->
                let document, outer, inner = setup ()
                use _ = document
                let log = ResizeArray<string>()

                Events.listen inner "x" false (fun e ->
                    (Events.eventOf e).StopPropagation()
                    log.Add "a")
                |> ignore

                Events.listen inner "x" false (fun _ -> log.Add "b")
                |> ignore

                Events.listen outer "x" false (fun _ -> log.Add "outer")
                |> ignore

                Events.dispatch inner (plain "x" true) |> ignore
                Expect.equal (List.ofSeq log) [ "a"; "b" ] "stopped after the node"

            testCase "preventDefault on a cancelable event makes dispatch return false"
            <| fun () ->
                let document, _, inner = setup ()
                use _ = document

                Events.listen inner "x" false (fun e -> (Events.eventOf e).PreventDefault())
                |> ignore

                Expect.isFalse (Events.dispatch inner (plain "x" true)) "cancelled"

            testCase "clicking a checkbox toggles it and fires click, input and change"
            <| fun () ->
                Events.ensure ()
                use document = new TwinDocument ()
                let box = document.CreateElement "input"
                box.SetAttribute ("type", "checkbox")
                document.Body.AppendChild box |> ignore
                let log = ResizeArray<string>()

                for name in [ "click"; "input"; "change" ] do
                    Events.listen box name false (fun _ -> log.Add (name + ":" + string box.Checked))
                    |> ignore

                Events.click box
                Expect.equal (List.ofSeq log) [ "click:True"; "input:True"; "change:True" ] "sequence"

            testCase "clicking a detached checkbox toggles it and fires click only"
            <| fun () ->
                Events.ensure ()
                use document = new TwinDocument ()
                let box = document.CreateElement "input"
                box.SetAttribute ("type", "checkbox")
                let log = ResizeArray<string>()

                for name in [ "click"; "input"; "change" ] do
                    Events.listen box name false (fun _ -> log.Add name)
                    |> ignore

                Events.click box
                Expect.equal (box.Checked, List.ofSeq log) (true, [ "click" ]) "no input or change"

            testCase "a cancelled checkbox click restores the state and fires no change"
            <| fun () ->
                Events.ensure ()
                use document = new TwinDocument ()
                let box = document.CreateElement "input"
                box.SetAttribute ("type", "checkbox")
                document.Body.AppendChild box |> ignore
                let changed = ref false

                Events.listen box "click" false (fun e -> (Events.eventOf e).PreventDefault())
                |> ignore

                Events.listen box "change" false (fun _ -> changed.Value <- true)
                |> ignore

                Events.click box
                Expect.equal (box.Checked, changed.Value) (false, false) "restored"

            testCase "clicking a submit button in a form fires submit"
            <| fun () ->
                Events.ensure ()
                use document = new TwinDocument ()
                let form = document.CreateElement "form"
                let button = document.CreateElement "button"
                form.AppendChild button |> ignore
                document.Body.AppendChild form |> ignore
                let submitted = ref false

                Events.listen form "submit" false (fun _ -> submitted.Value <- true)
                |> ignore

                Events.click button
                Expect.isTrue submitted.Value "submit"

            testCase "clicking a label clicks its control"
            <| fun () ->
                Events.ensure ()
                use document = new TwinDocument ()
                let label = document.CreateElement "label"
                let box = document.CreateElement "input"
                box.SetAttribute ("type", "checkbox")
                label.AppendChild box |> ignore
                document.Body.AppendChild label |> ignore
                Events.click label
                Expect.isTrue box.Checked "forwarded"

            testCase "focus moves fire blur, focusout, focus and focusin in order"
            <| fun () ->
                Events.ensure ()
                use document = new TwinDocument ()
                let a, b = document.CreateElement "input", document.CreateElement "input"
                document.Body.AppendChild a |> ignore
                document.Body.AppendChild b |> ignore
                let log = ResizeArray<string>()

                for name in [ "blur"; "focusout"; "focus"; "focusin" ] do
                    Events.listen document.Body name true (fun e ->
                        log.Add (
                            name
                            + ":"
                            + (Bridge.nodeOf (Events.eventOf e).Target.Native :?> TwinElement).LocalName
                        ))
                    |> ignore

                Events.focus a
                log.Clear ()
                Events.focus b
                Expect.equal (List.ofSeq log) [ "blur:input"; "focusout:input"; "focus:input"; "focusin:input" ] "order"
                Expect.equal document.ActiveElement b "active"

            testCase "activeElement is the body when nothing is focused or the focused element leaves the document"
            <| fun () ->
                Events.ensure ()
                use document = new TwinDocument ()
                let native = document.Native :?> Document
                let body = document.Body.Native
                Expect.isTrue (obj.ReferenceEquals (native.activeElement.Value, body)) "nothing focused"
                let a, b = document.CreateElement "input", document.CreateElement "input"
                document.Body.AppendChild a |> ignore
                document.Body.AppendChild b |> ignore
                Events.focus a
                Expect.isTrue (obj.ReferenceEquals (native.activeElement.Value, a.Native)) "focused"
                let blurred = ref false

                Events.listen a "blur" false (fun _ -> blurred.Value <- true)
                |> ignore

                a.Remove ()
                Expect.isTrue (obj.ReferenceEquals (native.activeElement.Value, body)) "focus fixup"
                Events.focus b
                Expect.isFalse blurred.Value "a removed element is not blurred"

            testCase "a click arrives as a PointerEvent proxy"
            <| fun () ->
                let document, _, inner = setup ()
                use _ = document
                let seen = ref (null: obj)

                Events.listen inner "click" false (fun e -> seen.Value <- e)
                |> ignore

                Events.click inner
                Expect.isTrue (seen.Value :? PointerEvent) "PointerEvent"
                Expect.isTrue (obj.ReferenceEquals ((seen.Value :?> Event).target.Value, inner.Native)) "target"

            testCase "a listener's exception surfaces after the dispatch ends"
            <| fun () ->
                let document, outer, inner = setup ()
                use _ = document
                let outerRan = ref false
                document.RaiseListenerErrors <- true

                Events.listen inner "x" false (fun _ -> failwith "boom")
                |> ignore

                Events.listen outer "x" false (fun _ -> outerRan.Value <- true)
                |> ignore

                Expect.throwsT<TwinListenerException> (fun () -> Events.dispatch inner (plain "x" true) |> ignore) "raised"
                Expect.isTrue outerRan.Value "dispatch continued"

            testCase "a listener's exception is collected by default and the dispatch returns"
            <| fun () ->
                let document, _, inner = setup ()
                use _ = document

                Events.listen inner "x" false (fun _ -> failwith "boom")
                |> ignore

                Events.dispatch inner (plain "x" true) |> ignore

                Expect.equal
                    (document.ListenerErrors
                     |> Seq.map (fun e ->
                         match e with
                         | TwinListenerException (eventType, inner) -> eventType, inner.Message
                         | other -> "?", other.Message)
                     |> List.ofSeq)
                    [ "x", "boom" ]
                    "collected with the event type"

            testCase "a failure in a nested dispatch is recorded once and raised when the outermost dispatch ends"
            <| fun () ->
                let document, outer, inner = setup ()
                use _ = document
                document.RaiseListenerErrors <- true

                Events.listen inner "inner" false (fun _ -> failwith "boom")
                |> ignore

                Events.listen outer "outer" false (fun _ ->
                    Events.dispatch inner (plain "inner" true)
                    |> ignore)
                |> ignore

                Expect.throwsT<TwinListenerException>
                    (fun () ->
                        Events.dispatch outer (plain "outer" true)
                        |> ignore)
                    "raised"

                Expect.equal document.ListenerErrors.Count 1 "recorded once"

            testCase "a click whose listener raises still runs the activation behaviour"
            <| fun () ->
                Events.ensure ()
                use document = new TwinDocument ()
                document.RaiseListenerErrors <- true
                let box = document.CreateElement "input"
                box.SetAttribute ("type", "checkbox")
                document.Body.AppendChild box |> ignore
                let changed = ref false

                Events.listen box "click" false (fun _ -> failwith "boom")
                |> ignore

                Events.listen box "change" false (fun _ -> changed.Value <- true)
                |> ignore

                Expect.throwsT<TwinListenerException> (fun () -> Events.click box) "raised after the click"
                Expect.equal (box.Checked, changed.Value) (true, true) "toggled and changed"

            testCase "on the target, capture listeners run before non-capture ones"
            <| fun () ->
                let document, _, inner = setup ()
                use _ = document
                let log = ResizeArray<string>()

                Events.listen inner "x" false (fun _ -> log.Add "target-bubble")
                |> ignore

                Events.listen inner "x" true (fun _ -> log.Add "target-capture")
                |> ignore

                Events.dispatch inner (plain "x" false) |> ignore
                Expect.equal (List.ofSeq log) [ "target-capture"; "target-bubble" ] "capture first"

            testCase "stopPropagation in a target capture listener skips the target's other listeners"
            <| fun () ->
                let document, _, inner = setup ()
                use _ = document
                let log = ResizeArray<string>()

                Events.listen inner "x" true (fun e ->
                    log.Add "cap"
                    (Events.eventOf e).StopPropagation())
                |> ignore

                Events.listen inner "x" false (fun _ -> log.Add "bub")
                |> ignore

                Events.dispatch inner (plain "x" true) |> ignore
                Expect.equal (List.ofSeq log) [ "cap" ] "stopped"

            testCase "clicking inside a label clicks its control"
            <| fun () ->
                Events.ensure ()
                use document = new TwinDocument ()
                let label = document.CreateElement "label"
                let span = document.CreateElement "span"
                let box = document.CreateElement "input"
                box.SetAttribute ("type", "checkbox")
                label.AppendChild span |> ignore
                label.AppendChild box |> ignore
                document.Body.AppendChild label |> ignore
                Events.click span
                Expect.isTrue box.Checked "checked through the label"

            testCase "clicking inside a submit button submits its form"
            <| fun () ->
                Events.ensure ()
                use document = new TwinDocument ()
                let form = document.CreateElement "form"
                let button = document.CreateElement "button"
                let span = document.CreateElement "span"
                button.AppendChild span |> ignore
                form.AppendChild button |> ignore
                document.Body.AppendChild form |> ignore
                let submitted = ref false

                Events.listen form "submit" false (fun _ -> submitted.Value <- true)
                |> ignore

                Events.click span
                Expect.isTrue submitted.Value "submitted"

            testCase "addEventListener reads capture, once and passive from an options object and keys by capture"
            <| fun () ->
                let document, outer, inner = setup ()
                use _ = document
                let log = ResizeArray<string>()

                let options capture once passive =
                    { new AddEventListenerOptions with
                        member _.capture
                            with get () = Some capture
                            and set _ = ()

                        member _.once
                            with get () = Some once
                            and set _ = ()

                        member _.passive
                            with get () = Some passive
                            and set _ = ()

                        member _.signal
                            with get () = None
                            and set _ = ()
                    }

                let listener: Event -> unit = fun _ -> log.Add "outer"
                let native = outer.Native
                native.addEventListener ("x", Fable.Core.U2.Case1 listener, Fable.Core.U2.Case2 (options true true false))
                native.addEventListener ("x", Fable.Core.U2.Case1 listener, Fable.Core.U2.Case1 false)

                let passive: Event -> unit = fun e -> e.preventDefault ()
                native.addEventListener ("x", Fable.Core.U2.Case1 passive, Fable.Core.U2.Case2 (options false false true))

                Expect.isTrue (Events.dispatch inner (plain "x" true)) "a passive listener cannot cancel"
                Expect.equal (List.ofSeq log) [ "outer"; "outer" ] "capture and non-capture are two listeners"
                log.Clear ()
                Events.dispatch inner (plain "x" true) |> ignore
                Expect.equal (List.ofSeq log) [ "outer" ] "the once listener is gone"
                native.removeEventListener ("x", Fable.Core.U2.Case1 listener, Fable.Core.U2.Case1 false)
                log.Clear ()
                Events.dispatch inner (plain "x" true) |> ignore
                Expect.equal (List.ofSeq log) [] "removed by capture"

            testCase "every listener of one dispatch receives the same event object"
            <| fun () ->
                let document, outer, inner = setup ()
                use _ = document
                let seen = ResizeArray<obj>()
                Events.listen inner "x" false seen.Add |> ignore
                Events.listen outer "x" false seen.Add |> ignore
                Events.listen outer "x" true seen.Add |> ignore
                Events.dispatch inner (plain "x" true) |> ignore
                Expect.equal seen.Count 3 "three listeners"

                Expect.isTrue
                    (seen
                     |> Seq.forall (fun e -> obj.ReferenceEquals (e, seen[0])))
                    "one object"
        ]
