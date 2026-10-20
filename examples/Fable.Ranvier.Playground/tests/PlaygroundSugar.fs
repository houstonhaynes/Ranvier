/// <summary>The playground's demo screen: a verbatim copy of <c>App.fs</c>'s template and its rewrites in the template, record and class styles.</summary>
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

/// <summary>The demo screen with free tags and <c>Component.template</c>.</summary>
let demoSugar =
    Component.template
        (fun _ (state: DemoState) ->
            {
                State = state
                Doubled = Api.createMemo (fun _ -> state.Count.Value * 2)
            })
        (section (
            Attrs.className "demo-grid",
            Attrs.data ("testid", "demo-root"),
            Attrs.data ("parity", (fun demo -> Some (if demo.State.Count.Value % 2 = 0 then "even" else "odd")))
        ) {
            article (Attrs.className "panel counter-panel") {
                div (Attrs.className "panel-heading") {
                    span (Attrs.className "eyebrow") { "01 / Counter" }
                    span (Attrs.className "tag") { "signal → text" }
                }

                output (Attrs.className "count", Attrs.data ("testid", "count"), Attrs.aria ("live", "polite")) {
                    fun demo -> string demo.State.Count.Value
                }

                div (Attrs.className "derived") {
                    span () { "Doubled" }
                    output (Attrs.data ("testid", "doubled")) { fun demo -> string demo.Doubled.Value }
                }

                div (Attrs.className "actions") {
                    button (
                        Attrs.className "primary",
                        Attrs.data ("testid", "increment"),
                        Attrs.attribute ("type", "button"),
                        Events.onClick (fun demo _ -> demo.State.Count.Value <- demo.State.Count.Value + 1)
                    ) {
                        "Increment +1"
                    }

                    button (
                        Attrs.className "secondary",
                        Attrs.data ("testid", "reset"),
                        Attrs.attribute ("type", "button"),
                        Props.Button.disabled (fun demo -> demo.State.Count.Value = 0),
                        Events.onClick (fun demo _ -> demo.State.Count.Value <- 0)
                    ) {
                        "Reset"
                    }

                    button (
                        Attrs.className "secondary",
                        Attrs.data ("testid", "burst"),
                        Attrs.attribute ("type", "button"),
                        Events.onClick (fun demo _ ->
                            Api.batch (fun () ->
                                for _ in 1..100 do
                                    demo.State.Count.Value <- demo.State.Count.Value + 1))
                    ) {
                        "Burst +100"
                    }
                }

                p (Attrs.className "note") { "The output changes. The DOM nodes stay." }
            }

            article (Attrs.className "panel name-panel") {
                div (Attrs.className "panel-heading") {
                    span (Attrs.className "eyebrow") { "02 / Input" }
                    span (Attrs.className "tag") { "event → signal" }
                }

                label (Attrs.attribute ("for", "name-field")) { "Your name" }

                input (
                    Attrs.id "name-field",
                    Attrs.attribute ("type", "text"),
                    Attrs.attribute ("autocomplete", "off"),
                    Attrs.data ("testid", "name"),
                    Props.Input.value (fun demo -> demo.State.Name.Value),
                    Events.onInput (fun demo event -> demo.State.Name.Value <- (unbox<HTMLInputElement> event.currentTarget.Value).value)
                )

                p (Attrs.className "greeting", Attrs.data ("testid", "greeting"), Attrs.aria ("live", "polite")) {
                    fun demo -> $"Hello, {demo.State.Name.Value}"
                }

                p (Attrs.className "note") { "Type anywhere in the field. Focus and selection are preserved." }
            }
        })

/// <summary>The demo screen as a record with static <c>Create</c> and <c>View</c> members.</summary>
type DemoMembers =
    {
        State: DemoState
        Doubled: Memo<int>
    }

    static member Create(_: Setup, state: DemoState) =
        {
            State = state
            Doubled = Api.createMemo (fun _ -> state.Count.Value * 2)
        }

    static member View =
        section (
            Attrs.className "demo-grid",
            Attrs.data ("testid", "demo-root"),
            Attrs.data ("parity", (fun demo -> Some (if demo.State.Count.Value % 2 = 0 then "even" else "odd")))
        ) {
            article (Attrs.className "panel counter-panel") {
                div (Attrs.className "panel-heading") {
                    span (Attrs.className "eyebrow") { "01 / Counter" }
                    span (Attrs.className "tag") { "signal → text" }
                }

                output (Attrs.className "count", Attrs.data ("testid", "count"), Attrs.aria ("live", "polite")) {
                    fun demo -> string demo.State.Count.Value
                }

                div (Attrs.className "derived") {
                    span () { "Doubled" }
                    output (Attrs.data ("testid", "doubled")) { fun demo -> string demo.Doubled.Value }
                }

                div (Attrs.className "actions") {
                    button (
                        Attrs.className "primary",
                        Attrs.data ("testid", "increment"),
                        Attrs.attribute ("type", "button"),
                        Events.onClick (fun demo _ -> demo.State.Count.Value <- demo.State.Count.Value + 1)
                    ) {
                        "Increment +1"
                    }

                    button (
                        Attrs.className "secondary",
                        Attrs.data ("testid", "reset"),
                        Attrs.attribute ("type", "button"),
                        Props.Button.disabled (fun demo -> demo.State.Count.Value = 0),
                        Events.onClick (fun demo _ -> demo.State.Count.Value <- 0)
                    ) {
                        "Reset"
                    }

                    button (
                        Attrs.className "secondary",
                        Attrs.data ("testid", "burst"),
                        Attrs.attribute ("type", "button"),
                        Events.onClick (fun demo _ ->
                            Api.batch (fun () ->
                                for _ in 1..100 do
                                    demo.State.Count.Value <- demo.State.Count.Value + 1))
                    ) {
                        "Burst +100"
                    }
                }

                p (Attrs.className "note") { "The output changes. The DOM nodes stay." }
            }

            article (Attrs.className "panel name-panel") {
                div (Attrs.className "panel-heading") {
                    span (Attrs.className "eyebrow") { "02 / Input" }
                    span (Attrs.className "tag") { "event → signal" }
                }

                label (Attrs.attribute ("for", "name-field")) { "Your name" }

                input (
                    Attrs.id "name-field",
                    Attrs.attribute ("type", "text"),
                    Attrs.attribute ("autocomplete", "off"),
                    Attrs.data ("testid", "name"),
                    Props.Input.value (fun demo -> demo.State.Name.Value),
                    Events.onInput (fun demo event -> demo.State.Name.Value <- (unbox<HTMLInputElement> event.currentTarget.Value).value)
                )

                p (Attrs.className "greeting", Attrs.data ("testid", "greeting"), Attrs.aria ("live", "polite")) {
                    fun demo -> $"Hello, {demo.State.Name.Value}"
                }

                p (Attrs.className "note") { "Type anywhere in the field. Focus and selection are preserved." }
            }
        }

let demoMembers = Component.ofMembers<DemoMembers, _>()

/// <summary>The demo screen as a class: the constructor is the setup, the static <c>View</c> the template.</summary>
type DemoClass(setup: Setup, state: DemoState) =
    member _.State = state
    member val Doubled = Api.createMemo (fun _ -> state.Count.Value * 2)

    static member View: TemplateView<DemoClass> =
        section (
            Attrs.className "demo-grid",
            Attrs.data ("testid", "demo-root"),
            Attrs.data ("parity", (fun (demo: DemoClass) -> Some (if demo.State.Count.Value % 2 = 0 then "even" else "odd")))
        ) {
            article (Attrs.className "panel counter-panel") {
                div (Attrs.className "panel-heading") {
                    span (Attrs.className "eyebrow") { "01 / Counter" }
                    span (Attrs.className "tag") { "signal → text" }
                }

                output (Attrs.className "count", Attrs.data ("testid", "count"), Attrs.aria ("live", "polite")) {
                    fun (demo: DemoClass) -> string demo.State.Count.Value
                }

                div (Attrs.className "derived") {
                    span () { "Doubled" }
                    output (Attrs.data ("testid", "doubled")) { fun (demo: DemoClass) -> string demo.Doubled.Value }
                }

                div (Attrs.className "actions") {
                    button (
                        Attrs.className "primary",
                        Attrs.data ("testid", "increment"),
                        Attrs.attribute ("type", "button"),
                        Events.onClick (fun (demo: DemoClass) _ -> demo.State.Count.Value <- demo.State.Count.Value + 1)
                    ) {
                        "Increment +1"
                    }

                    button (
                        Attrs.className "secondary",
                        Attrs.data ("testid", "reset"),
                        Attrs.attribute ("type", "button"),
                        Props.Button.disabled (fun (demo: DemoClass) -> demo.State.Count.Value = 0),
                        Events.onClick (fun demo _ -> demo.State.Count.Value <- 0)
                    ) {
                        "Reset"
                    }

                    button (
                        Attrs.className "secondary",
                        Attrs.data ("testid", "burst"),
                        Attrs.attribute ("type", "button"),
                        Events.onClick (fun (demo: DemoClass) _ ->
                            Api.batch (fun () ->
                                for _ in 1..100 do
                                    demo.State.Count.Value <- demo.State.Count.Value + 1))
                    ) {
                        "Burst +100"
                    }
                }

                p (Attrs.className "note") { "The output changes. The DOM nodes stay." }
            }

            article (Attrs.className "panel name-panel") {
                div (Attrs.className "panel-heading") {
                    span (Attrs.className "eyebrow") { "02 / Input" }
                    span (Attrs.className "tag") { "event → signal" }
                }

                label (Attrs.attribute ("for", "name-field")) { "Your name" }

                input (
                    Attrs.id "name-field",
                    Attrs.attribute ("type", "text"),
                    Attrs.attribute ("autocomplete", "off"),
                    Attrs.data ("testid", "name"),
                    Props.Input.value (fun (demo: DemoClass) -> demo.State.Name.Value),
                    Events.onInput (fun demo event -> demo.State.Name.Value <- (unbox<HTMLInputElement> event.currentTarget.Value).value)
                )

                p (Attrs.className "greeting", Attrs.data ("testid", "greeting"), Attrs.aria ("live", "polite")) {
                    fun (demo: DemoClass) -> $"Hello, {demo.State.Name.Value}"
                }

                p (Attrs.className "note") { "Type anywhere in the field. Focus and selection are preserved." }
            }
        }

let demoClass = Component.ofClass DemoClass
