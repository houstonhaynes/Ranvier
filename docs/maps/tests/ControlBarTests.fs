module Ranvier.Docs.Maps.Tests.ControlBarTests

open Expecto
open global.Ranvier
open Ranvier.View
open Ranvier.View.Sugar
open Ranvier.View.Testing
open Ranvier.View.Twin
open Ranvier.Docs.Maps

/// A control bar over test-owned values: each write is recorded and shown. The twin leaves number and range inputs to
/// the browser suite, since it does not sanitise their values.
type private Bench =
    {
        Items: ControlItem array
        Values: Signal<Map<int, InputValue>>
        Writes: ResizeArray<int * InputValue>
    }

let mutable private latest: Bench option = None

let private bench =
    Component.template
        (fun _ () ->
            let values =
                createSignal (
                    Map.ofList
                        [
                            1, Shown.Text "Tea"
                            2, Shown.Text "Tin"
                            4, Shown.Toggle false
                            5, Shown.Toggle false
                        ]
                )

            let writes = ResizeArray<int * InputValue>()

            let item key label kind =
                {
                    Key = key
                    Label = label
                    Kind = kind
                    Value = fun () -> values.Value[key]
                    Write =
                        fun value ->
                            writes.Add (key, value)
                            values.Value <- Map.add key value values.Peek
                    Press = fun () -> writes.Add (key, Shown.Toggle true)
                }

            let created =
                {
                    Items =
                        [|
                            item 1 "Name" WidgetKind.TextField
                            item 2 "Pack" WidgetKind.TextField
                            item 4 "Gift" WidgetKind.CheckBox
                            item 5 "Load" WidgetKind.Press
                        |]
                    Values = values
                    Writes = writes
                }

            latest <- Some created
            created)
        (div (Attrs.className "bar") { ControlBar.rows (fun (b: Bench) -> b.Items) })

let private field (host: Fable.Core.TS.Dom.HTMLElement) (index: int) =
    let fields = Query.queryAll host ".rv-map__input-field"
    unbox<Fable.Core.TS.Dom.HTMLInputElement> fields[index]

let private set (session: Session) key value =
    let b = latest.Value
    session.Act (fun () -> b.Values.Value <- Map.add key value b.Values.Peek)

[<Tests>]
let tests =
    testSequenced
    <| testList
        "ControlBar"
        [
            test "each control renders its widget with its label and value" {
                use session = Twin.mount bench ()
                Expect.equal (Query.queryAll session.Host "label").Length 3 "three inputs"
                Expect.equal (field session.Host 0).value "Tea" "the first text"
                Expect.equal (field session.Host 1).value "Tin" "the second text"
                Expect.isFalse (field session.Host 2).``checked`` "the toggle"
                Expect.text session.Host "button" "Load"
            }

            test "committed edits, flips and presses write their values" {
                use session = Twin.mount bench ()
                Interact.input session.Host "label:nth-child(1) input" "Coffee"
                Expect.isEmpty latest.Value.Writes "text writes when committed"
                Interact.change session.Host "label:nth-child(1) input"
                Interact.check session.Host "input[type=checkbox]" true
                Interact.click session.Host "button"

                Expect.sequenceEqual
                    latest.Value.Writes
                    [ 1, Shown.Text "Coffee"; 4, Shown.Toggle true; 5, Shown.Toggle true ]
                    "one write per commit, flip and press"
            }

            test "an external write refreshes its own control and leaves another unfinished edit alone" {
                use session = Twin.mount bench ()
                Interact.input session.Host "label:nth-child(1) input" "Cof"
                set session 2 (Shown.Text "Jar")
                set session 4 (Shown.Toggle true)

                Expect.equal (field session.Host 0).value "Cof" "the unfinished text survives other writes"
                Expect.equal (field session.Host 1).value "Jar" "the other field follows its write"
                Expect.isTrue (field session.Host 2).``checked`` "the toggle follows"

                set session 1 (Shown.Text "Jam")
                Expect.equal (field session.Host 0).value "Jam" "its own external write replaces the text"
            }
        ]
