module App

open System
open Fable.Core
open Fable.Core.TS.Dom
open global.Ranvier
open Ranvier.View

module FoundationMounting = Ranvier.View.Dom.Mounting
module FoundationProgram = Ranvier.View.Representation.Program
module View = Ranvier.View.Dom.Authoring
module GeneratedHtml = Ranvier.View.Dom.Generated.Html
module Elements = GeneratedHtml.Program
module GeneratedAttrs = GeneratedHtml.Attrs
module FoundationMicrotask = Ranvier.View.Dom.Microtask
module FoundationScheduling = Ranvier.View.Dom.Scheduling

[<Global "document">]
let private document: Document = jsNative

/// <summary>A mount's report: writes each failure to the browser console.</summary>
let private reportToConsole (errors: exn list) =
    for error in errors do
        JS.console.error ("Ranvier mount failure", error)

/// <summary>The demo's state, kept on the app's graph so a remount keeps it.</summary>
type DemoState =
    {
        Count: Signal<int>
        Name: Signal<string>
    }

type DemoInstance =
    { State: DemoState; Doubled: Memo<int> }

let private demoView =
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

/// <summary>The shell's state, and the operations invoked by its controls and widget elements.</summary>
type ShellProps =
    {
        Mounted: Signal<bool>
        Queued: Signal<bool>
        Toggle: unit -> unit
        ToggleScheduling: unit -> unit
        Flush: unit -> unit
        /// <summary>Mounts the demo into its widget element; the returned cleanup disposes it.</summary>
        AttachDemo: HTMLElement -> (unit -> unit)
        /// <summary>Mounts the Foundation panels into their widget element; the returned cleanup disposes them.</summary>
        AttachFoundation: HTMLElement -> (unit -> unit)
    }

let private shellView =
    Component.define (fun _ (props: ShellProps) -> props) (fun t ->
        t.div
            [| t.Attrs.className "playground" |]
            [|
                t.header
                    [| t.Attrs.className "page-heading" |]
                    [|
                        t.div
                            [| t.Attrs.className "brand" |]
                            [|
                                t.span [| t.Attrs.className "brand-mark" |] [| t.text "r" |]
                                t.span [||] [| t.text "Fable.Ranvier" |]
                            |]
                        t.span [| t.Attrs.className "preview" |] [| t.text "DOM PoC" |]
                    |]
                t.section
                    [| t.Attrs.className "intro" |]
                    [|
                        t.p [| t.Attrs.className "eyebrow" |] [| t.text "F# · Fable · Browser DOM" |]
                        t.h1
                            [||]
                            [|
                                t.text "Create once."
                                t.br [||]
                                t.span [||] [| t.text "Update what changes." |]
                            |]
                        t.p
                            [| t.Attrs.className "lead" |]
                            [|
                                t.text "A small playground for reactive text, properties and events. No component tree rebuild on each edit."
                            |]
                    |]
                t.div
                    [| t.Attrs.className "mount-bar" |]
                    [|
                        t.span
                            [| t.Attrs.data ("testid", "scheduling-status") |]
                            [|
                                t.text (fun (shell: ShellProps) ->
                                    if shell.Queued.Value then
                                        "Microtask DOM commits"
                                    else
                                        "Synchronous DOM commits")
                            |]
                        t.button
                            [|
                                t.Attrs.className "secondary"
                                t.Attrs.data ("testid", "scheduling-toggle")
                                t.Attrs.attribute ("type", "button")
                                t.Events.onClick (fun (shell: ShellProps) _ -> shell.ToggleScheduling ())
                            |]
                            [|
                                t.text (fun (shell: ShellProps) ->
                                    if shell.Queued.Value then
                                        "Use synchronous"
                                    else
                                        "Use microtasks")
                            |]
                        t.button
                            [|
                                t.Attrs.className "secondary"
                                t.Attrs.data ("testid", "flush-dom")
                                t.Attrs.attribute ("type", "button")
                                t.Events.onClick (fun (shell: ShellProps) _ -> shell.Flush ())
                            |]
                            [| t.text "Flush DOM" |]
                    |]
                t.div
                    [| t.Attrs.className "mount-bar" |]
                    [|
                        t.div
                            [| t.Attrs.className "mount-state" |]
                            [|
                                t.span
                                    [|
                                        t.Attrs.className "status-dot"
                                        t.Attrs.data ("active", (fun (shell: ShellProps) -> if shell.Mounted.Value then Some "true" else None))
                                    |]
                                    [||]
                                t.span
                                    [| t.Attrs.data ("testid", "mount-status") |]
                                    [|
                                        t.text (fun (shell: ShellProps) ->
                                            if shell.Mounted.Value then
                                                "Demo mounted"
                                            else
                                                "Demo unmounted")
                                    |]
                            |]
                        t.button
                            [|
                                t.Attrs.className "secondary toggle"
                                t.Attrs.data ("testid", "mount-toggle")
                                t.Attrs.attribute ("type", "button")
                                t.Events.onClick (fun (shell: ShellProps) _ -> shell.Toggle ())
                            |]
                            [|
                                t.text (fun (shell: ShellProps) ->
                                    if shell.Mounted.Value then
                                        "Unmount demo"
                                    else
                                        "Remount demo")
                            |]
                    |]
                t.div
                    [|
                        t.Hooks.widget (fun (shell: ShellProps) (host: HTMLDivElement) -> shell.AttachDemo (host :> HTMLElement))
                    |]
                    [||]
                t.section
                    [|
                        t.Hooks.widget (fun (shell: ShellProps) (host: HTMLElement) -> shell.AttachFoundation host)
                    |]
                    [||]
                t.section
                    [|
                        t.Attrs.className "empty-state"
                        t.Attrs.hidden (fun (shell: ShellProps) -> shell.Mounted.Value)
                    |]
                    [|
                        t.h2 [||] [| t.text "The mount is disposed." |]
                        t.p
                            [||]
                            [|
                                t.text "Its nodes, bindings and event listeners have been removed. Remount to continue with the same state."
                            |]
                    |]
                t.footer
                    [||]
                    [|
                        t.text "Built with Xantham DOM bindings and Ranvier scopes. Experimental API; DOM scheduling is opt-in."
                    |]
            |])

let start () : unit -> unit =
    let graph = new Graph ()
    let foundationGraph = new Graph ()

    try
        let host =
            document.getElementById ("app")
            |> Option.defaultWith (fun () -> invalidOp "The playground needs an #app host.")

        let count, name, mounted, queued =
            graph.Run (fun () -> createSignal 0, createSignal "world", createSignal true, createSignal false)

        let mutable demoHost: HTMLElement = Unchecked.defaultof<_>
        let mutable demo: FoundationMounting.Mounted option = None

        let disposeDemo () =
            demo
            |> Option.iter (fun current -> current.Dispose ())

            demo <- None

        let mountDemo () =
            let mode =
                if queued.Value then
                    FoundationMicrotask.mode FoundationScheduling.CollectAndContinue
                else
                    FoundationScheduling.SynchronousCommits

            demo <-
                Some (
                    FoundationMounting.mountWith mode graph None reportToConsole demoHost None (fun context ->
                        FoundationProgram.run (Component.render demoView { Count = count; Name = name }) context.Backend)
                )

        let toggle () =
            match demo with
            | Some _ ->
                disposeDemo ()
                mounted.Value <- false
            | None ->
                mountDemo ()
                mounted.Value <- true

        let toggleScheduling () =
            queued.Value <- not queued.Value

            if demo.IsSome then
                disposeDemo ()
                mountDemo ()

        let attachDemo (element: HTMLElement) =
            demoHost <- element

            if mounted.Value then
                mountDemo ()

            fun () ->
                disposeDemo ()
                demoHost <- Unchecked.defaultof<_>

        let foundationRows =
            foundationGraph.Run (fun () -> createSignal [| 1, "Alpha"; 2, "Beta" |])

        let loadSource =
            foundationGraph.Run (fun () -> createSignal (createAsyncSource<string>()))

        let loadResult =
            foundationGraph.Run (fun () ->
                createBoundary (fun _ -> "Loading…") (fun _ _ -> "Could not load the result") (fun () -> loadSource.Value.Value))

        foundationGraph.Run (fun () -> loadSource.Value.Settle "Loaded result")

        let foundationRowsView =
            View.keyedArray fst (fun () -> foundationRows.Value) (fun current ->
                let key = fst (current ())

                Elements.div [| GeneratedAttrs.idStatic ("foundation-row-" + string key) |] [| View.reactiveText (fun () -> snd (current ())) |])

        let foundationView =
            Elements.section
                [|
                    GeneratedAttrs.idStatic "foundation-demo"
                    GeneratedAttrs.classStatic "panel"
                    GeneratedAttrs.dataReactive "row-count" (fun () -> Some (string foundationRows.Value.Length))
                |]
                [|
                    View.text "Foundation implementation: keyed rows"
                    Elements.button
                        [|
                            GeneratedAttrs.idStatic "foundation-add"
                            GeneratedHtml.Button.onClick (fun _ ->
                                let next =
                                    foundationRows.Value
                                    |> Array.map fst
                                    |> Array.max
                                    |> (+) 1

                                foundationRows.Value <- Array.append foundationRows.Value [| next, "Item " + string next |])
                        |]
                        [| View.text "Add row" |]
                    Elements.button
                        [|
                            GeneratedAttrs.idStatic "foundation-reverse"
                            GeneratedHtml.Button.onClick (fun _ -> foundationRows.Value <- Array.rev foundationRows.Value)
                        |]
                        [| View.text "Reverse rows" |]
                    foundationRowsView
                |]

        let attachFoundation (element: HTMLElement) =
            let foundation =
                FoundationMounting.mountWith
                    (FoundationMicrotask.mode FoundationScheduling.CollectAndContinue)
                    foundationGraph
                    None
                    reportToConsole
                    element
                    None
                    (fun context ->
                        FoundationProgram.run foundationView context.Backend
                        FoundationProgram.run (ComponentsExample.view ()) context.Backend

                        let action id label update =
                            Elements.button
                                [|
                                    GeneratedAttrs.idStatic id
                                    GeneratedHtml.Button.onClick (fun _ -> update ())
                                |]
                                [| View.text label |]

                        let statusPanel =
                            Elements.div
                                [|
                                    GeneratedAttrs.idStatic "foundation-load"
                                    GeneratedAttrs.roleStatic "region"
                                    GeneratedAttrs.ariaLabelStatic "Load result"
                                    GeneratedAttrs.ariaBusyReactive (fun () ->
                                        loadResult.Value |> ignore
                                        Some loadResult.IsWaiting)
                                |]
                                [|
                                    View.text "Load result"
                                    Elements.div
                                        [| GeneratedAttrs.idStatic "foundation-load-status" |]
                                        [|
                                            View.reactiveText (fun () ->
                                                loadResult.Value |> ignore

                                                if loadResult.IsWaiting then "Pending"
                                                elif not (isNull loadResult.Caught) then "Failed"
                                                else "Ready")
                                        |]
                                    Elements.div
                                        [| GeneratedAttrs.idStatic "foundation-load-result" |]
                                        [| View.reactiveText (fun () -> loadResult.Value) |]
                                    Elements.div
                                        [| GeneratedAttrs.idStatic "foundation-load-error" |]
                                        [|
                                            View.reactiveText (fun () ->
                                                loadResult.Value |> ignore

                                                if isNull loadResult.Caught then
                                                    ""
                                                else
                                                    loadResult.Caught.Message)
                                        |]
                                    action "foundation-pending" "Start loading" (fun () ->
                                        foundationGraph.Run (fun () -> loadSource.Value <- createAsyncSource<string>()))
                                    action "foundation-fail" "Simulate failure" (fun () ->
                                        loadSource.Value.Fail (Exception "Connection unavailable"))
                                    action "foundation-recover" "Recover" (fun () -> loadSource.Value.Settle "Recovered result")
                                |]

                        FoundationProgram.run statusPanel context.Backend)

            fun () -> foundation.Dispose ()

        let shellProps =
            {
                Mounted = mounted
                Queued = queued
                Toggle = toggle
                ToggleScheduling = toggleScheduling
                Flush =
                    fun () ->
                        demo
                        |> Option.iter (fun current -> current.Flush ())
                AttachDemo = attachDemo
                AttachFoundation = attachFoundation
            }

        let shell =
            FoundationMounting.mountWith FoundationScheduling.SynchronousCommits graph None reportToConsole host None (fun context ->
                FoundationProgram.run (Component.render shellView shellProps) context.Backend)

        let mutable stopped = false

        fun () ->
            if not stopped then
                stopped <- true
                shell.Dispose ()
                foundationGraph.Dispose ()
                graph.Dispose ()
    with _ ->
        foundationGraph.Dispose ()
        graph.Dispose ()
        reraise ()
