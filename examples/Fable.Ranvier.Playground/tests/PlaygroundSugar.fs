/// <summary>The playground's demo screen: a verbatim copy of <c>App.fs</c>'s template and its rewrite with the template sugar.</summary>
module PlaygroundSugar

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar

/// <summary>The demo's state, kept on the app's graph so a remount keeps it.</summary>
type DemoState =
    {
        Count: Signal<int>
        Name: Signal<string>
    }

type DemoInstance =
    { State: DemoState; Doubled: Memo<int> }

/// <summary>The demo screen of <c>App.fs</c> in the explicit template form.</summary>
let demoExplicit =
    Component.define
        (fun _ (state: DemoState) ->
            {
                State = state
                Doubled = Api.createMemo (fun _ -> state.Count.Value * 2)
            })
        (fun t ->
            t.section
                [|
                    t.Attrs.className "demo-grid"
                    t.Attrs.data ("testid", "demo-root")
                    t.Attrs.data ("parity", (fun (demo: DemoInstance) -> Some (if demo.State.Count.Value % 2 = 0 then "even" else "odd")))
                |]
                [|
                    t.article
                        [| t.Attrs.className "panel counter-panel" |]
                        [|
                            t.div
                                [| t.Attrs.className "panel-heading" |]
                                [|
                                    t.span [| t.Attrs.className "eyebrow" |] [| t.text "01 / Counter" |]
                                    t.span [| t.Attrs.className "tag" |] [| t.text "signal → text" |]
                                |]
                            t.output
                                [|
                                    t.Attrs.className "count"
                                    t.Attrs.data ("testid", "count")
                                    t.Attrs.aria ("live", "polite")
                                |]
                                [| t.text (fun (demo: DemoInstance) -> string demo.State.Count.Value) |]
                            t.div
                                [| t.Attrs.className "derived" |]
                                [|
                                    t.span [||] [| t.text "Doubled" |]
                                    t.output
                                        [| t.Attrs.data ("testid", "doubled") |]
                                        [| t.text (fun (demo: DemoInstance) -> string demo.Doubled.Value) |]
                                |]
                            t.div
                                [| t.Attrs.className "actions" |]
                                [|
                                    t.button
                                        [|
                                            t.Attrs.className "primary"
                                            t.Attrs.data ("testid", "increment")
                                            t.Attrs.attribute ("type", "button")
                                            t.Events.onClick (fun (demo: DemoInstance) _ -> demo.State.Count.Value <- demo.State.Count.Value + 1)
                                        |]
                                        [| t.text "Increment +1" |]
                                    t.button
                                        [|
                                            t.Attrs.className "secondary"
                                            t.Attrs.data ("testid", "reset")
                                            t.Attrs.attribute ("type", "button")
                                            t.Props.Button.disabled (fun (demo: DemoInstance) -> demo.State.Count.Value = 0)
                                            t.Events.onClick (fun (demo: DemoInstance) _ -> demo.State.Count.Value <- 0)
                                        |]
                                        [| t.text "Reset" |]
                                    t.button
                                        [|
                                            t.Attrs.className "secondary"
                                            t.Attrs.data ("testid", "burst")
                                            t.Attrs.attribute ("type", "button")
                                            t.Events.onClick (fun (demo: DemoInstance) _ ->
                                                Api.batch (fun () ->
                                                    for _ in 1..100 do
                                                        demo.State.Count.Value <- demo.State.Count.Value + 1))
                                        |]
                                        [| t.text "Burst +100" |]
                                |]
                            t.p [| t.Attrs.className "note" |] [| t.text "The output changes. The DOM nodes stay." |]
                        |]
                    t.article
                        [| t.Attrs.className "panel name-panel" |]
                        [|
                            t.div
                                [| t.Attrs.className "panel-heading" |]
                                [|
                                    t.span [| t.Attrs.className "eyebrow" |] [| t.text "02 / Input" |]
                                    t.span [| t.Attrs.className "tag" |] [| t.text "event → signal" |]
                                |]
                            t.label [| t.Attrs.attribute ("for", "name-field") |] [| t.text "Your name" |]
                            t.input
                                [|
                                    t.Attrs.id "name-field"
                                    t.Attrs.attribute ("type", "text")
                                    t.Attrs.attribute ("autocomplete", "off")
                                    t.Attrs.data ("testid", "name")
                                    t.Props.Input.value (fun (demo: DemoInstance) -> demo.State.Name.Value)
                                    t.Events.onInput (fun (demo: DemoInstance) event ->
                                        demo.State.Name.Value <- (unbox<HTMLInputElement> event.currentTarget.Value).value)
                                |]
                            t.p
                                [|
                                    t.Attrs.className "greeting"
                                    t.Attrs.data ("testid", "greeting")
                                    t.Attrs.aria ("live", "polite")
                                |]
                                [| t.text (fun (demo: DemoInstance) -> $"Hello, {demo.State.Name.Value}") |]
                            t.p [| t.Attrs.className "note" |] [| t.text "Type anywhere in the field. Focus and selection are preserved." |]
                        |]
                |])

/// <summary>The setup of <c>demoSugar</c>, equal to the setup of <c>demoExplicit</c>.</summary>
let private demoSetup =
    fun (_: Setup) (state: DemoState) ->
        {
            State = state
            Doubled = Api.createMemo (fun _ -> state.Count.Value * 2)
        }

/// <summary>The demo screen of <c>App.fs</c> written with the template sugar.</summary>
let demoSugar =
    Component.define demoSetup (fun t ->
        t.Section (
            t.Attrs.className "demo-grid",
            t.Attrs.data ("testid", "demo-root"),
            t.Attrs.data ("parity", (fun (demo: DemoInstance) -> Some (if demo.State.Count.Value % 2 = 0 then "even" else "odd")))
        ) {
            t.Article (t.Attrs.className "panel counter-panel") {
                t.Div (t.Attrs.className "panel-heading") {
                    t.Span (t.Attrs.className "eyebrow") { "01 / Counter" }
                    t.Span (t.Attrs.className "tag") { "signal → text" }
                }

                t.Output (t.Attrs.className "count", t.Attrs.data ("testid", "count"), t.Attrs.aria ("live", "polite")) {
                    fun (demo: DemoInstance) -> string demo.State.Count.Value
                }

                t.Div (t.Attrs.className "derived") {
                    t.Span () { "Doubled" }
                    t.Output (t.Attrs.data ("testid", "doubled")) { fun (demo: DemoInstance) -> string demo.Doubled.Value }
                }

                t.Div (t.Attrs.className "actions") {
                    t.Button (
                        t.Attrs.className "primary",
                        t.Attrs.data ("testid", "increment"),
                        t.Attrs.attribute ("type", "button"),
                        t.Events.onClick (fun (demo: DemoInstance) _ -> demo.State.Count.Value <- demo.State.Count.Value + 1)
                    ) {
                        "Increment +1"
                    }

                    t.Button (
                        t.Attrs.className "secondary",
                        t.Attrs.data ("testid", "reset"),
                        t.Attrs.attribute ("type", "button"),
                        t.Props.Button.disabled (fun (demo: DemoInstance) -> demo.State.Count.Value = 0),
                        t.Events.onClick (fun (demo: DemoInstance) _ -> demo.State.Count.Value <- 0)
                    ) {
                        "Reset"
                    }

                    t.Button (
                        t.Attrs.className "secondary",
                        t.Attrs.data ("testid", "burst"),
                        t.Attrs.attribute ("type", "button"),
                        t.Events.onClick (fun (demo: DemoInstance) _ ->
                            Api.batch (fun () ->
                                for _ in 1..100 do
                                    demo.State.Count.Value <- demo.State.Count.Value + 1))
                    ) {
                        "Burst +100"
                    }
                }

                t.P (t.Attrs.className "note") { "The output changes. The DOM nodes stay." }
            }

            t.Article (t.Attrs.className "panel name-panel") {
                t.Div (t.Attrs.className "panel-heading") {
                    t.Span (t.Attrs.className "eyebrow") { "02 / Input" }
                    t.Span (t.Attrs.className "tag") { "event → signal" }
                }

                t.Label (t.Attrs.attribute ("for", "name-field")) { "Your name" }

                t.Input (
                    t.Attrs.id "name-field",
                    t.Attrs.attribute ("type", "text"),
                    t.Attrs.attribute ("autocomplete", "off"),
                    t.Attrs.data ("testid", "name"),
                    t.Props.Input.value (fun (demo: DemoInstance) -> demo.State.Name.Value),
                    t.Events.onInput (fun (demo: DemoInstance) event ->
                        demo.State.Name.Value <- (unbox<HTMLInputElement> event.currentTarget.Value).value)
                )

                t.P (t.Attrs.className "greeting", t.Attrs.data ("testid", "greeting"), t.Attrs.aria ("live", "polite")) {
                    fun (demo: DemoInstance) -> $"Hello, {demo.State.Name.Value}"
                }

                t.P (t.Attrs.className "note") { "Type anywhere in the field. Focus and selection are preserved." }
            }
        })
