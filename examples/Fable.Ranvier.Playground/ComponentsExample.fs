module ComponentsExample

open Ranvier
open Ranvier.View
open Ranvier.View.Sugar
open Fable.Core.TS.Dom

type private CounterProps =
    {
        Id: string
        Label: Signal<string>
        InitialCount: int
        Changed: int -> unit
        Child: unit -> string
    }

type private Counter =
    {
        Props: CounterProps
        Count: Signal<int>
    }

    static member Create(_: Setup, props: CounterProps) =
        {
            Props = props
            Count = createSignal props.InitialCount
        }

    static member View: TemplateView<Counter> =
        article (Attrs.id _.Props.Id, Attrs.className "panel", Attrs.title _.Props.Label.Value) {
            h2 () { fun c -> c.Props.Label.Value }
            output (Attrs.id (fun c -> c.Props.Id + "-count"), Attrs.className "count") { fun c -> string c.Count.Value }

            button (
                Attrs.id (fun c -> c.Props.Id + "-increment"),
                Attrs.className "primary",
                Attrs.Button.``type`` "button",
                Events.onClick (fun c _ ->
                    c.Count.Value <- c.Count.Value + 1
                    c.Props.Changed c.Count.Value)
            ) {
                "Increment"
            }

            p () { fun c -> c.Props.Child () }
        }

let private counter =
    Component.template (fun setup props -> Counter.Create (setup, props)) Counter.View

type private Example =
    {
        Label: Signal<string>
        Changed: Signal<string>
        Renamed: Memo<bool>
        Selected: Signal<bool>
    }

    static member Create(_: Setup, ()) =
        let label = createSignal "Shared label"

        {
            Label = label
            Changed = createSignal "No changes yet"
            Renamed = createMemo (fun _ -> label.Value = "Renamed")
            Selected = createSignal false
        }

    static member View: TemplateView<Example> =
        section (Attrs.id "component-example") {
            h2 () { "Reusable components" }

            input (
                Attrs.id "component-label",
                Attrs.Input.placeholder "Shared label",
                Props.Input.value _.Label.Value,
                Events.onInput (fun state event -> state.Label.Value <- (unbox<HTMLInputElement> event.currentTarget.Value).value)
            )

            button (
                Attrs.id "component-rename",
                Attrs.Button.``type`` "button",
                Props.Button.disabled _.Renamed.Value,
                Events.onClick (fun state _ -> state.Label.Value <- "Renamed")
            ) {
                "Rename both counters"
            }

            p (Attrs.id "component-changed") { fun state -> state.Changed.Value }

            input (
                Attrs.id "component-checkbox",
                Attrs.Input.``type`` "checkbox",
                Props.Input.``checked`` _.Selected.Value,
                Events.onChange (fun state event -> state.Selected.Value <- (unbox<HTMLInputElement> event.currentTarget.Value).``checked``)
            )

            output (Attrs.id "component-checkbox-state") { fun state -> if state.Selected.Value then "Checked" else "Unchecked" }

            button (
                Attrs.id "component-toggle-checkbox",
                Attrs.Button.``type`` "button",
                Events.onClick (fun state _ -> state.Selected.Value <- not state.Selected.Value)
            ) {
                "Toggle checkbox"
            }

            div (Attrs.className "demo-grid") {
                render counter (fun state ->
                    {
                        Id = "component-first"
                        Label = state.Label
                        InitialCount = 0
                        Changed = fun value -> state.Changed.Value <- "First changed to " + string value
                        Child = fun () -> "First counter child content"
                    })

                render counter (fun state ->
                    {
                        Id = "component-second"
                        Label = state.Label
                        InitialCount = 10
                        Changed = fun value -> state.Changed.Value <- "Second changed to " + string value
                        Child = fun () -> "Child sees " + state.Label.Value
                    })
            }
        }

/// <summary>Two independently owned counters sharing a reactive label and reporting changes to their parent.</summary>
let ``component`` =
    Component.template (fun setup props -> Example.Create (setup, props)) Example.View
