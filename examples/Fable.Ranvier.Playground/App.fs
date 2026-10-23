module App

open System
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier
open Ranvier.View
open Ranvier.View.Sugar
open Ranvier.View.Dom

type private DemoState =
    {
        Count: Signal<int>
        Name: Signal<string>
    }

type private Demo =
    {
        State: DemoState
        Doubled: Memo<int>
    }

    static member Create(_: Setup, state: DemoState) =
        {
            State = state
            Doubled = createMemo (fun _ -> state.Count.Value * 2)
        }

    static member View: TemplateView<Demo> =
        section (
            Attrs.className "demo-grid",
            Attrs.data ("testid", "demo-root"),
            Attrs.data ("parity", fun demo -> Some (if demo.State.Count.Value % 2 = 0 then "even" else "odd"))
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
                        Attrs.Button.``type`` "button",
                        Events.onClick (fun demo _ -> demo.State.Count.Value <- demo.State.Count.Value + 1)
                    ) {
                        "Increment +1"
                    }

                    button (
                        Attrs.className "secondary",
                        Attrs.data ("testid", "reset"),
                        Attrs.Button.``type`` "button",
                        Props.Button.disabled (fun demo -> demo.State.Count.Value = 0),
                        Events.onClick (fun demo _ -> demo.State.Count.Value <- 0)
                    ) {
                        "Reset"
                    }

                    button (
                        Attrs.className "secondary",
                        Attrs.data ("testid", "burst"),
                        Attrs.Button.``type`` "button",
                        Events.onClick (fun demo _ ->
                            batch (fun () ->
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
                    Attrs.Input.``type`` "text",
                    Attrs.attribute ("autocomplete", "off"),
                    Attrs.data ("testid", "name"),
                    Props.Input.value _.State.Name.Value,
                    Events.onInput (fun demo event -> demo.State.Name.Value <- (unbox<HTMLInputElement> event.currentTarget.Value).value)
                )

                p (Attrs.className "greeting", Attrs.data ("testid", "greeting"), Attrs.aria ("live", "polite")) {
                    fun demo -> $"Hello, {demo.State.Name.Value}"
                }

                p (Attrs.className "note") { "Type anywhere in the field. Focus and selection are preserved." }
            }
        }

let private demoComponent =
    Component.template (fun setup props -> Demo.Create (setup, props)) Demo.View

type private Foundation =
    {
        Rows: Signal<(int * string) array>
        Busy: unit -> bool
        Status: unit -> string
        Result: unit -> string
        Error: unit -> string
        Pending: unit -> unit
        Fail: unit -> unit
        Recover: unit -> unit
    }

    static member Create(_: Setup, ()) =
        let graph = Graph.Current
        let source = createSignal (createAsyncSource<string>())

        let result =
            createBoundary (fun _ -> "Loading…") (fun _ _ -> "Could not load the result") (fun () -> source.Value.Value)

        source.Value.Settle "Loaded result"

        {
            Rows = createSignal [| 1, "Alpha"; 2, "Beta" |]
            Busy =
                fun () ->
                    result.Value |> ignore
                    result.IsWaiting
            Status =
                fun () ->
                    result.Value |> ignore

                    if result.IsWaiting then "Pending"
                    elif not (isNull result.Caught) then "Failed"
                    else "Ready"
            Result = fun () -> result.Value
            Error =
                fun () ->
                    result.Value |> ignore
                    if isNull result.Caught then "" else result.Caught.Message
            Pending = fun () -> graph.Run (fun () -> source.Value <- createAsyncSource<string>())
            Fail = fun () -> source.Value.Fail (Exception "Connection unavailable")
            Recover = fun () -> source.Value.Settle "Recovered result"
        }

    static member View: TemplateView<Foundation> =
        section () {
            section (
                Attrs.id "foundation-demo",
                Attrs.className "panel",
                Attrs.data ("row-count", fun state -> Some (string state.Rows.Value.Length))
            ) {
                "Foundation implementation: keyed rows"

                button (
                    Attrs.id "foundation-add",
                    Attrs.Button.``type`` "button",
                    Events.onClick (fun state _ ->
                        let next =
                            state.Rows.Value
                            |> Array.map fst
                            |> Array.max
                            |> (+) 1

                        state.Rows.Value <- Array.append state.Rows.Value [| next, "Item " + string next |])
                ) {
                    "Add row"
                }

                button (
                    Attrs.id "foundation-reverse",
                    Attrs.Button.``type`` "button",
                    Events.onClick (fun state _ -> state.Rows.Value <- Array.rev state.Rows.Value)
                ) {
                    "Reverse rows"
                }

                forEach
                    fst
                    _.Rows.Value
                    (div (Attrs.id (fun current -> "foundation-row-" + string (fst (current ())))) { fun current -> snd (current ()) })
            }

            renderOf ComponentsExample.``component`` ()

            div (
                Attrs.id "foundation-load",
                Attrs.role "region",
                Attrs.aria ("label", "Load result"),
                Attrs.aria ("busy", fun state -> Some (if state.Busy () then "true" else "false"))
            ) {
                "Load result"
                div (Attrs.id "foundation-load-status") { fun state -> state.Status () }
                div (Attrs.id "foundation-load-result") { fun state -> state.Result () }
                div (Attrs.id "foundation-load-error") { fun state -> state.Error () }

                button (Attrs.id "foundation-pending", Attrs.Button.``type`` "button", Events.onClick (fun state _ -> state.Pending ())) {
                    "Start loading"
                }

                button (Attrs.id "foundation-fail", Attrs.Button.``type`` "button", Events.onClick (fun state _ -> state.Fail ())) {
                    "Simulate failure"
                }

                button (Attrs.id "foundation-recover", Attrs.Button.``type`` "button", Events.onClick (fun state _ -> state.Recover ())) { "Recover" }
            }
        }

let private foundationComponent =
    Component.template (fun setup props -> Foundation.Create (setup, props)) Foundation.View

type private Shell =
    {
        Mounted: Signal<bool>
        Queued: Signal<bool>
        Toggle: unit -> unit
        ToggleScheduling: unit -> unit
        Flush: unit -> unit
        AttachDemo: HTMLDivElement -> (unit -> unit)
    }

    static member Create(_: Setup, ()) =
        let graph = Graph.Current

        let state =
            {
                Count = createSignal 0
                Name = createSignal "world"
            }

        let mounted = createSignal true
        let queued = createSignal false
        let mutable host: HTMLElement = Unchecked.defaultof<_>
        let mutable demo: Mounting.Mounted option = None

        let disposeDemo () =
            demo
            |> Option.iter (fun current -> current.Dispose ())

            demo <- None

        let mountDemo () =
            let mode =
                if queued.Value then
                    Microtask.mode Scheduling.CollectAndContinue
                else
                    Scheduling.SynchronousCommits

            let report (errors: exn list) =
                for error in errors do
                    JS.console.error ("Ranvier mount failure", error)

            demo <-
                Some (
                    Mounting.mountWith mode graph None report host None (fun context ->
                        Program.run (Ranvier.View.Template.Component.render demoComponent state) context.Backend)
                )

        {
            Mounted = mounted
            Queued = queued
            Toggle =
                fun () ->
                    match demo with
                    | Some _ ->
                        disposeDemo ()
                        mounted.Value <- false
                    | None ->
                        mountDemo ()
                        mounted.Value <- true
            ToggleScheduling =
                fun () ->
                    queued.Value <- not queued.Value

                    if demo.IsSome then
                        disposeDemo ()
                        mountDemo ()
            Flush =
                fun () ->
                    demo
                    |> Option.iter (fun current -> current.Flush ())
            AttachDemo =
                fun element ->
                    host <- element

                    if mounted.Value then
                        mountDemo ()

                    fun () ->
                        disposeDemo ()
                        host <- Unchecked.defaultof<_>
        }

    static member View: TemplateView<Shell> =
        div (Attrs.className "playground") {
            header (Attrs.className "page-heading") {
                div (Attrs.className "brand") {
                    span (Attrs.className "brand-mark") { "r" }
                    span () { "Fable.Ranvier" }
                }

                span (Attrs.className "preview") { "DOM PoC" }
            }

            section (Attrs.className "intro") {
                p (Attrs.className "eyebrow") { "F# · Fable · Browser DOM" }

                h1 () {
                    "Create once."
                    br ()
                    span () { "Update what changes." }
                }

                p (Attrs.className "lead") { "A small playground for reactive text, properties and events. No component tree rebuild on each edit." }
            }

            div (Attrs.className "mount-bar") {
                span (Attrs.data ("testid", "scheduling-status")) {
                    fun shell ->
                        if shell.Queued.Value then
                            "Microtask DOM commits"
                        else
                            "Synchronous DOM commits"
                }

                button (
                    Attrs.className "secondary",
                    Attrs.data ("testid", "scheduling-toggle"),
                    Attrs.Button.``type`` "button",
                    Events.onClick (fun shell _ -> shell.ToggleScheduling ())
                ) {
                    fun shell ->
                        if shell.Queued.Value then
                            "Use synchronous"
                        else
                            "Use microtasks"
                }

                button (
                    Attrs.className "secondary",
                    Attrs.data ("testid", "flush-dom"),
                    Attrs.Button.``type`` "button",
                    Events.onClick (fun shell _ -> shell.Flush ())
                ) {
                    "Flush DOM"
                }
            }

            div (Attrs.className "mount-bar") {
                div (Attrs.className "mount-state") {
                    span (Attrs.className "status-dot", Attrs.data ("active", fun shell -> if shell.Mounted.Value then Some "true" else None)) { }

                    span (Attrs.data ("testid", "mount-status")) {
                        fun shell ->
                            if shell.Mounted.Value then
                                "Demo mounted"
                            else
                                "Demo unmounted"
                    }
                }

                button (
                    Attrs.className "secondary toggle",
                    Attrs.data ("testid", "mount-toggle"),
                    Attrs.Button.``type`` "button",
                    Events.onClick (fun shell _ -> shell.Toggle ())
                ) {
                    fun shell ->
                        if shell.Mounted.Value then
                            "Unmount demo"
                        else
                            "Remount demo"
                }
            }
            // This widget owns the mount whose commit scheduler and lifetime the controls demonstrate.
            div (Hooks.widget (fun shell element -> shell.AttachDemo element)) { }
            renderOf foundationComponent ()

            section (Attrs.className "empty-state", Attrs.hidden _.Mounted.Value) {
                h2 () { "The mount is disposed." }
                p () { "Its nodes, bindings and event listeners have been removed. Remount to continue with the same state." }
            }

            footer () { "Built with Xantham DOM bindings and Ranvier scopes. Experimental API; DOM scheduling is opt-in." }
        }

/// <summary>The shared playground shell, keyed rows, load status and reusable component examples.</summary>
let ``component`` =
    Component.template (fun setup props -> Shell.Create (setup, props)) Shell.View

[<Emit("globalThis.playground = { dispose: $0 }")>]
let private publish (dispose: unit -> unit) : unit = jsNative

/// <summary>Publishes the generated page's disposal handle for browser lifecycle tests.</summary>
let expose (page: PageClient.Started) =
    publish page.Dispose
