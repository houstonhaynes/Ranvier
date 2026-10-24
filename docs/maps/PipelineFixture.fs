namespace Ranvier.Docs.Maps

open global.Ranvier
open Ranvier.View
open Ranvier.View.Sugar

/// <summary>A deterministic counter for checking the documentation's server rendering and hydration of components.</summary>
[<RequireQualifiedAccess>]
module PipelineFixture =
    type Counter =
        {
            Count: Signal<int>
        }

        static member Create(_: Setup, (): unit) =
            { Count = createSignal 0 }

        static member View =
            div (Attrs.className "rv-map-fixture") {
                button (
                    Attrs.attribute ("type", "button"),
                    Attrs.className "rv-map-fixture__count",
                    Events.onClick (fun c _ -> c.Count.Value <- c.Count.Value + 1)
                ) {
                    fun c -> string c.Count.Value
                }
            }

    /// <summary>Starts at 0; each click adds one.</summary>
    let counter: Component<unit> = Component.ofMembers<Counter, _>()
