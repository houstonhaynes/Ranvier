/// The F# samples of docs/content/view/events.md, in page order; a sample that is an excerpt is wrapped in a function.
module Events

open Fable.Core.TS
open global.Ranvier
open Ranvier.View
open Ranvier.View.Sugar

type Basket = { Items: Signal<int>; Total: Signal<decimal> }

let basket =
    Component.template (fun _ (basket: Basket) -> basket) (
        div () {
            button (
                Attrs.attribute ("type", "button"),
                Events.onClick (fun b _ ->
                    Api.batch (fun () ->
                        b.Items.Value <- b.Items.Value + 1
                        b.Total.Value <- b.Total.Value + 2.50m))
            ) {
                "Add"
            }

            span (Events.on ("dblclick", fun b _ -> b.Items.Value <- 0)) { fun b -> string b.Items.Value + " items" }
        }
    )

type Search =
    { Query: Signal<string>; Box: NodeRef<Dom.HTMLInputElement> }

    static member Create(_: Setup, initial: string) =
        { Query = createSignal initial; Box = NodeRef.create () }

    static member View =
        div () {
            input (
                Hooks.ref (fun s -> s.Box),
                Hooks.mounted (fun _ (element: Dom.HTMLInputElement) ->
                    element.focus ()
                    fun () -> ()),
                Props.Input.value (from (fun s -> s.Query))
            )

            button (Events.onClick (fun s _ -> s.Box.Current |> Option.iter (fun box -> box.select ()))) { "Select all" }
        }

let search = Component.ofMembers<Search, _> ()

type Chart = { Points: Signal<float array> }

/// Stands in for a third-party library that draws into an element and returns its teardown.
let drawChart (host: Dom.HTMLElement) (points: unit -> float array) : unit -> unit =
    ignore (host, points)
    fun () -> ()

let chart =
    Component.template (fun _ (chart: Chart) -> chart) (
        figure () {
            div (Attrs.className "chart", Hooks.widget (fun c (host: Dom.HTMLDivElement) -> drawChart host (fun () -> c.Points.Value))) { }
            figcaption () { "Weekly totals" }
        }
    )
