/// The first F# sample of docs/content/view/getting-started.md.
module Counter

open global.Ranvier
open Ranvier.View
open Ranvier.View.Sugar

type CounterProps = { Start: int; Label: string }

type Counter =
    { Count: Signal<int>; Label: string }

    static member Create(_: Setup, props: CounterProps) =
        { Count = createSignal props.Start; Label = props.Label }

    static member View =
        div (Attrs.className "counter") {
            span (Attrs.className "label") { fun c -> c.Label }

            button (Attrs.attribute ("type", "button"), Events.onClick (fun c _ -> c.Count.Value <- c.Count.Value + 1)) {
                fun c -> string c.Count.Value
            }
        }

let counter = Component.ofMembers<Counter, _> ()
