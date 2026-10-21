/// The F# samples of docs/content/view/templates.md, in page order; a sample that is an excerpt is wrapped in a function.
module Templates

open Fable.Core.TS
open global.Ranvier
open Ranvier.View
open Ranvier.View.Sugar

let elements =
    Component.template (fun _ () -> ()) (
        div (Attrs.className "panel", Attrs.id "main") {
            h2 () { "Totals" }
            input (Attrs.attribute ("type", "search"))
            p () { }
        }
    )

type Avatar = { Shown: Signal<bool> }

let avatar =
    Component.template (fun _ (shown: Signal<bool>) -> { Shown = shown }) (
        span (Attrs.className "avatar") { show (fun a -> a.Shown.Value) (img (Attrs.attribute ("alt", "avatar"))).View }
    )

let rule = Component.template (fun _ () -> ()) (hr (Attrs.className "rule")).View

type Greeting = { Name: Signal<string>; Shout: Memo<string> }

let greeting =
    Component.template
        (fun _ (name: Signal<string>) -> { Name = name; Shout = createMemo (fun _ -> name.Value.ToUpper ()) })
        (p (Attrs.className "greeting") {
            "Hello, "
            fun g -> g.Name.Value
            "! "
            from (fun g -> g.Shout)
        })

let columns = [ "Name"; "Size"; "Modified" ]

let header =
    Component.template (fun _ () -> ()) (
        table () {
            thead () {
                tr () {
                    for column in columns do
                        th () { column }

                    if columns.Length > 2 then
                        th (Attrs.className "actions") { }
                }
            }
        }
    )

type Chore =
    { Title: Signal<string>
      Note: Signal<string option>
      Done: Signal<bool>
      Owner: string }

let chore =
    Component.template (fun _ (chore: Chore) -> chore) (
        li (
            Attrs.className "chore",
            Attrs.title (fun c -> c.Title.Value),
            Attrs.data ("owner", fun c -> Some c.Owner),
            Attrs.attribute ("aria-describedby", fun c -> c.Note.Value |> Option.map (fun _ -> "note")),
            Attrs.hidden (from (fun c -> c.Done))
        ) {
            fun c -> c.Title.Value
        }
    )

type Field = { Text: Signal<string>; Busy: Signal<bool> }

let field =
    Component.template (fun _ (field: Field) -> field) (
        form () {
            input (
                Props.Input.value (from (fun f -> f.Text)),
                Events.onInput (fun f event -> f.Text.Value <- (unbox<Dom.HTMLInputElement> event.currentTarget.Value).value)
            )

            button (Props.Button.disabled (fun f -> f.Busy.Value || f.Text.Value = "")) { "Save" }
        }
    )

type Selection = { Partial: Signal<bool> }

let selectAll =
    Component.template (fun _ (selection: Selection) -> selection) (
        label () {
            input (
                Attrs.attribute ("type", "checkbox"),
                Props.propertyWith ((fun (box: Dom.HTMLInputElement) partial -> box.indeterminate <- partial), (fun s -> s.Partial.Value), primitiveSame)
            )

            "Select all"
        }
    )

type Note = { Html: UnsafeHtml; Count: Signal<int> }

let note =
    Component.define (fun _ (note: Note) -> note) (fun t ->
        div () {
            div (t.Unsafe.html (fun n -> n.Html)) { }
            span () { t.isolated (t.text (fun n -> string n.Count.Value)) }
        })

module Explicit =
    let greeting =
        Component.define
            (fun _ (name: Signal<string>) -> { Name = name; Shout = createMemo (fun _ -> name.Value.ToUpper ()) })
            (fun t ->
                t.p
                    [| t.Attrs.className "greeting" |]
                    [|
                        t.text "Hello, "
                        t.text (fun (g: Greeting) -> g.Name.Value)
                        t.text "! "
                        t.text (t.from (fun (g: Greeting) -> g.Shout))
                    |])
