/// The F# samples of docs/content/view/getting-started.md, in page order; a sample that is an excerpt is wrapped in a function.
module CounterExplicit

open global.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar
open Counter

type Counter = { Count: Signal<int>; Label: string }

let counter =
    Component.define
        (fun _ (props: CounterProps) -> { Count = createSignal props.Start; Label = props.Label })
        (fun t ->
            t.div
                [| t.Attrs.className "counter" |]
                [|
                    t.span [| t.Attrs.className "label" |] [| t.text (fun (c: Counter) -> c.Label) |]
                    t.button
                        [|
                            t.Attrs.attribute ("type", "button")
                            t.Events.onClick (fun (c: Counter) _ -> c.Count.Value <- c.Count.Value + 1)
                        |]
                        [| t.text (fun (c: Counter) -> string c.Count.Value) |]
                |])
