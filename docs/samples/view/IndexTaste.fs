/// The F# samples of docs/content/view/index.md, in page order; a sample that is an excerpt is wrapped in a function.
module IndexTaste

open global.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar

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
