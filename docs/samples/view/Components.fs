/// The F# samples of docs/content/view/components.md, in page order; a sample that is an excerpt is wrapped in a function.
module Components

open global.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar

type CounterProps = { Start: int; Caption: string }

type Counter =
    { Count: Signal<int>; Caption: string }

    static member Create(_: Setup, props: CounterProps) =
        { Count = createSignal props.Start; Caption = props.Caption }

    static member View =
        div (Attrs.className "counter") {
            span (Attrs.className "caption") { fun c -> c.Caption }

            button (Attrs.attribute ("type", "button"), Events.onClick (fun c _ -> c.Count.Value <- c.Count.Value + 1)) {
                fun c -> string c.Count.Value
            }
        }

let counter = Component.ofMembers<Counter, _> ()

let badge =
    Component.template
        (fun _ (label: string) -> label)
        (span (Attrs.className "badge") { fun label -> label })

type Switch(setup: Setup, initial: bool) =
    let on = createSignal initial

    member _.On = on

    static member View: TemplateView<Switch> =
        button (Attrs.attribute ("type", "button"), Events.onClick (fun (s: Switch) _ -> s.On.Value <- not s.On.Value)) {
            fun (s: Switch) -> if s.On.Value then "On" else "Off"
        }

let switch = Component.ofClass Switch

type CounterState = { Count: Signal<int>; Caption: string }

let counterExplicit =
    Component.define
        (fun _ (props: CounterProps) -> { Count = createSignal props.Start; Caption = props.Caption })
        (fun t ->
            t.div
                [| t.Attrs.className "counter" |]
                [|
                    t.span [| t.Attrs.className "caption" |] [| t.text (fun (c: CounterState) -> c.Caption) |]
                    t.button
                        [|
                            t.Attrs.attribute ("type", "button")
                            t.Events.onClick (fun (c: CounterState) _ -> c.Count.Value <- c.Count.Value + 1)
                        |]
                        [| t.text (fun (c: CounterState) -> string c.Count.Value) |]
                |])

type Labelled =
    { Id: string; Text: string }

    static member Create(setup: Setup, text: string) =
        let id = setup.CreateId ()
        setup.OnCleanup (fun () -> printfn "disposed %s" id)
        { Id = id; Text = text }

    static member View =
        div () {
            label (Attrs.attribute ("for", fun l -> Some l.Id)) { fun l -> l.Text }
            input (Attrs.id (fun l -> l.Id))
        }

let labelled = Component.ofMembers<Labelled, _> ()

type PanelProps = { Title: string }

type Panel =
    { Title: string; Hits: Signal<int> }

    static member Create(_: Setup, props: PanelProps) =
        { Title = props.Title; Hits = createSignal 0 }

    static member View =
        section (Attrs.className "panel") {
            h2 () { fun p -> p.Title }
            render counter (fun p -> { Start = 1; Caption = p.Title })
            renderOf switch true
            renderOf badge "fixed"
        }

let panel = Component.ofMembers<Panel, _> ()

type badgeOf<'i> private (label: 'i -> string, primary: unit) =
    inherit ComponentTag<string, 'i>(badge, label)
    new(label: 'i -> string) = badgeOf<'i> (label, ())
    new(label: string) = badgeOf<'i> ((fun _ -> label), ())

let labels =
    Component.template (fun _ (count: int) -> count) (
        p () {
            badgeOf "new"
            badgeOf (fun count -> string count + " left")
        }
    )

let soldOut = Component.template (fun _ () -> ()) (badgeOf "sold out").View
