module IntegrationChecks

open System
open Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View
open Ranvier.View.Dom.Scheduling
open Ranvier.View.Dom.Mounting
open CheckSupport

let private raisesArgument (action: unit -> unit) =
    try
        action ()
        false
    with :? ArgumentException ->
        true

[<Emit("document.createTextNode($0)")>]
let private textNode (value: string) : Node = jsNative

[<Emit("document.createElement('input')")>]
let private createInput () : HTMLInputElement = jsNative

/// <summary>A widget that writes its own text into its element once placed and logs whether the element is attached in its destroy.</summary>
let private widgetRenders (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let log = ResizeArray<string>()
    let target = host ()

    let view =
        Typed.div
            ops
            [|
                Typed.attribute "class" "chart"
                Typed.widget (fun (node: HTMLDivElement) ->
                    node.appendChild (textNode "drawn") |> ignore
                    log.Add (sprintf "attach %b" (target.contains node))
                    fun () -> log.Add (sprintf "destroy %b %s" (target.contains node) node.textContent))
            |]
            [||]

    let mounted = mountView ops run queued graph strict target view
    equal ([ "attach true" ], "drawn") (List.ofSeq log, target.textContent)
    mounted.Dispose ()
    equal ([ "attach true"; "destroy true drawn" ], 0.) (List.ofSeq log, target.childNodes.length)

type WidgetProbe =
    {
        Log: ResizeArray<string>
        Host: HTMLElement
    }

let private chart =
    Component.define (fun _ (probe: WidgetProbe) -> probe) (fun t ->
        t.div
            [|
                t.Attrs.className "chart"
                t.Hooks.widget (fun probe (node: HTMLDivElement) ->
                    node.appendChild (textNode "drawn") |> ignore
                    probe.Log.Add (sprintf "attach %b" (probe.Host.contains node))
                    fun () -> probe.Log.Add (sprintf "destroy %b %s" (probe.Host.contains node) node.textContent))
            |]
            [||])

let private templateWidgetRenders queued () =
    use graph = new Graph ()
    let probe = { Log = ResizeArray (); Host = host () }

    let mounted =
        mountTemplate queued graph strict probe.Host (Component.render chart probe)

    equal ([ "attach true" ], "drawn") (List.ofSeq probe.Log, probe.Host.textContent)
    mounted.Dispose ()
    equal ([ "attach true"; "destroy true drawn" ], 0.) (List.ofSeq probe.Log, probe.Host.childNodes.length)

let private widgetChildrenAtDefinition () =
    equal
        (true, true)
        (raisesArgument (fun () ->
            // Asserts that the definition refuses the content parts.
            // fsharpanalyzer: ignore-line-next RAN023
            Component.define (fun _ () -> ()) (fun t -> t.div [| t.Hooks.widget (fun _ (_: HTMLDivElement) -> ignore) |] [| t.text "child" |])
            |> ignore),
         raisesArgument (fun () ->
             Component.define (fun _ () -> ()) (fun t ->
                 t.div
                     [|
                         t.Hooks.widget (fun _ (_: HTMLDivElement) -> ignore)
                         // Asserts that the definition refuses the content parts.
                         // fsharpanalyzer: ignore-line-next RAN023
                         t.Hooks.widget (fun _ (_: HTMLDivElement) -> ignore)
                     |]
                     [||])
             |> ignore))

let private widgetChildrenAtMount (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let target = host ()

    let view =
        Typed.div ops [| Typed.widget (fun (_: HTMLDivElement) -> ignore) |] [| ops.Text "child" |]

    let doubled =
        Typed.div
            ops
            [|
                Typed.widget (fun (_: HTMLDivElement) -> ignore)
                Typed.widget (fun (_: HTMLDivElement) -> ignore)
            |]
            [||]

    let doubledTarget = host ()

    equal
        (true, 0., true, 0.)
        (raisesArgument (fun () ->
            mountView ops run queued graph strict target view
            |> ignore),
         target.childNodes.length,
         raisesArgument (fun () ->
             mountView ops run queued graph strict doubledTarget doubled
             |> ignore),
         doubledTarget.childNodes.length)

let private foreignRow =
    Component.define (fun _ (item: unit -> int) -> item) (fun t ->
        t.div
            [|
                t.Hooks.widget (fun item (node: HTMLDivElement) ->
                    node.innerHTML <- sprintf "<b>%d</b><i>foreign</i>" (item ())
                    ignore)
            |]
            [||])

let private foreignRows =
    Component.define (fun _ (items: Signal<int list>) -> items) (fun t ->
        t.section
            [||]
            [|
                t.each id (fun (items: Signal<int list>) -> Array.ofList items.Value) foreignRow
            |])

/// <summary>Rows whose widgets fill them with foreign nodes are reversed and cleared.</summary>
let private templateForeignContent queued () =
    use graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2; 3 ])
    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render foreignRows rows)

    graph.Run (fun () -> rows.Value <- [ 3; 2; 1 ])
    mounted.Flush ()
    let reversed = target.textContent
    graph.Run (fun () -> rows.Value <- [])
    mounted.Flush ()
    equal ("3foreign2foreign1foreign", "", 0.) (reversed, target.textContent, (target.querySelectorAll "b").length)
    mounted.Dispose ()

let private foreignContent (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2; 3 ])
    let target = host ()

    let view =
        Keyed.list ops id (fun () -> rows.Value) (fun current ->
            let item = current ()

            Typed.div
                ops
                [|
                    Typed.widget (fun (node: HTMLDivElement) ->
                        node.innerHTML <- sprintf "<b>%d</b><i>foreign</i>" item
                        ignore)
                |]
                [||])

    let mounted = mountView ops run queued graph strict target view
    graph.Run (fun () -> rows.Value <- [ 3; 2; 1 ])
    mounted.Flush ()
    let reversed = target.textContent
    graph.Run (fun () -> rows.Value <- [])
    mounted.Flush ()
    equal ("3foreign2foreign1foreign", "") (reversed, target.textContent)
    mounted.Dispose ()

/// <summary>A widget element hosting a nested mount with a reactive text and a delegated click.</summary>
let private nestedMountInWidget queued =
    use graph = new Graph ()
    let label = graph.Run (fun () -> Api.createSignal "inner")
    let clicks = ref 0
    let log = ResizeArray<string>()
    let nested = ref Unchecked.defaultof<Mounted>
    let target = host ()

    let inner =
        Typed.button
            Program.ops
            [| Typed.listener "click" (fun _ -> clicks.Value <- clicks.Value + 1) |]
            [| Typed.reactiveText Program.ops (fun () -> label.Value) |]

    let view =
        Typed.div
            Tree.ops
            [|
                Typed.widget (fun (node: HTMLDivElement) ->
                    nested.Value <-
                        mountWith (modeOf queued) graph (Some (Api.getOwner ())) strict node None (fun context -> Program.run inner context.Backend)

                    fun () ->
                        log.Add "destroy"
                        nested.Value.Dispose ())
            |]
            [||]

    let mounted = mountView Tree.ops Tree.run queued graph strict target view
    graph.Run (fun () -> label.Value <- "updated")
    nested.Value.Flush ()

    (target.querySelector "button"
     |> unbox<HTMLButtonElement>)
        .click()

    equal ("updated", 1) (target.textContent, clicks.Value)
    mounted.Dispose ()
    graph.Run (fun () -> label.Value <- "late")
    equal ([ "destroy" ], 0.) (List.ofSeq log, target.childNodes.length)

let private lentKeepsAttributes (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let label = graph.Run (fun () -> Api.createSignal "a")
    let log = ResizeArray<string>()
    let target = host ()

    let view =
        Typed.input
            ops
            [|
                Typed.reactiveAttribute "title" (fun () -> Some label.Value)
                Typed.lend (fun (node: HTMLInputElement) ->
                    node.setAttribute ("data-mask", "on")

                    fun () ->
                        node.removeAttribute "data-mask"
                        log.Add "removed")
            |]

    let mounted = mountView ops run queued graph strict target view

    let input =
        target.querySelector "input"
        |> unbox<HTMLInputElement>

    graph.Run (fun () -> label.Value <- "b")
    mounted.Flush ()
    equal (Some "on", Some "b") (input.getAttribute "data-mask", input.getAttribute "title")
    mounted.Dispose ()
    equal [ "removed" ] (List.ofSeq log)

let private maskedInput =
    Component.define (fun _ (label: Signal<string>) -> label) (fun t ->
        t.input
            [|
                t.Attrs.title (fun (label: Signal<string>) -> label.Value)
                t.Hooks.lend (fun _ (node: HTMLInputElement) ->
                    node.setAttribute ("data-mask", "on")
                    fun () -> node.removeAttribute "data-mask")
            |])

let private templateLentKeepsAttributes queued () =
    use graph = new Graph ()
    let label = graph.Run (fun () -> Api.createSignal "a")
    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render maskedInput label)

    let input =
        target.querySelector "input"
        |> unbox<HTMLInputElement>

    graph.Run (fun () -> label.Value <- "b")
    mounted.Flush ()
    equal (Some "on", Some "b") (input.getAttribute "data-mask", input.getAttribute "title")
    mounted.Dispose ()

/// <summary>A lent span inside a div, moved by its integration into another element when it attaches.</summary>
let private movedIntegration (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let elsewhere = host ()
    let log = ResizeArray<string>()
    let reports = ResizeArray<exn list>()
    let target = host ()

    let view =
        Typed.div
            ops
            [||]
            [|
                span
                    ops
                    [|
                        Typed.lend (fun (node: HTMLSpanElement) ->
                            elsewhere.appendChild node |> ignore
                            fun () -> log.Add "cleanup")
                    |]
                    [||]
            |]

    let mounted = mountView ops run queued graph reports.Add target view
    mounted.Dispose ()
    equal ([ "cleanup" ], [ [ "An integration moved its element" ] ]) (List.ofSeq log, messages reports)

/// <summary>A lent span inside a div, moved by its integration, whose cleanup fails.</summary>
let private movedIntegrationFailingCleanup (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let elsewhere = host ()
    let reports = ResizeArray<exn list>()
    let target = host ()

    let view =
        Typed.div
            ops
            [||]
            [|
                span
                    ops
                    [|
                        Typed.lend (fun (node: HTMLSpanElement) ->
                            elsewhere.appendChild node |> ignore
                            fun () -> failwith "cleanup failed")
                    |]
                    [||]
            |]

    let mounted = mountView ops run queued graph reports.Add target view
    mounted.Dispose ()
    equal [ [ "cleanup failed"; "An integration moved its element" ] ] (messages reports)

/// <summary>A lent mount root whose mount moves to another host before its disposal.</summary>
let private lentMountRootMoved (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let elsewhere = host ()
    let log = ResizeArray<string>()
    let reports = ResizeArray<exn list>()
    let target = host ()

    let view =
        span ops [| Typed.lend (fun (_: HTMLSpanElement) -> fun () -> log.Add "cleanup") |] [||]

    let mounted = mountView ops run queued graph reports.Add target view
    mounted.MoveBefore (elsewhere, None)
    let moved = (elsewhere.querySelector "span").IsSome
    mounted.Dispose ()
    equal (true, [ "cleanup" ], [], 0.) (moved, List.ofSeq log, messages reports, elsewhere.childNodes.length)

let private lentRowRoots (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let target = host ()

    let view =
        Keyed.list ops id (fun () -> rows.Value) (fun current ->
            span ops [| Typed.lend (fun (_: HTMLSpanElement) -> ignore) |] [| ops.Text (string (current ())) |])

    let elsewhere = host ()
    let mounted = mountView ops run queued graph strict target view
    graph.Run (fun () -> rows.Value <- [ 2; 1 ])
    mounted.Flush ()
    mounted.MoveBefore (elsewhere, None)
    equal ("21", "") (elsewhere.textContent, target.textContent)
    mounted.Dispose ()

let private lentRow =
    Component.define (fun _ (item: unit -> int) -> item) (fun t ->
        t.span [| t.Hooks.lend (fun _ (_: HTMLSpanElement) -> ignore) |] [| t.text (fun item -> string (item ())) |])

let private lentRowList =
    Component.define (fun _ (items: Signal<int list>) -> items) (fun t ->
        t.each id (fun (items: Signal<int list>) -> Array.ofList items.Value) lentRow)

let private templateLentRowRoots queued () =
    use graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let target = host ()

    let elsewhere = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render lentRowList rows)

    graph.Run (fun () -> rows.Value <- [ 2; 1 ])
    mounted.Flush ()
    mounted.MoveBefore (elsewhere, None)
    equal ("21", "") (elsewhere.textContent, target.textContent)
    mounted.Dispose ()

let private widgetUnderBoundary (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createAsyncSource<string>())
    let broken = graph.Run (fun () -> Api.createSignal false)
    let log = ResizeArray<string>()
    let reports = ResizeArray<exn list>()
    let target = host ()

    let widget =
        Typed.div
            ops
            [|
                Typed.widget (fun (node: HTMLDivElement) ->
                    log.Add (sprintf "attach %b" (target.contains node))
                    fun () -> log.Add (sprintf "destroy %b" (target.contains node)))
            |]
            [||]

    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure -> ops.Text ("failed: " + failure.Error.Message)),
            fun _ ->
                ops.Fragment
                    [|
                        widget
                        Typed.reactiveText ops (fun () -> if broken.Value then failwith "broken" else source.Value)
                    |]
        )

    let mounted = mountView ops run queued graph reports.Add target view
    equal [] (List.ofSeq log)
    graph.Run (fun () -> source.Settle "ready")
    mounted.Flush ()
    equal [ "attach true" ] (List.ofSeq log)
    graph.Run (fun () -> broken.Value <- true)
    mounted.Flush ()

    equal ([ "attach true"; "destroy true" ], "failed: broken", [ [ "broken" ] ]) (List.ofSeq log, target.textContent, messages reports)

    mounted.Dispose ()

let private widgetAttachFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let reports = ResizeArray<exn list>()
    let target = host ()

    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure -> ops.Text ("failed: " + failure.Error.Message)),
            fun _ -> Typed.div ops [| Typed.widget (fun (_: HTMLDivElement) -> failwith "attach") |] [||]
        )

    let mounted = mountView ops run queued graph reports.Add target view
    equal ("failed: attach", [ [ "attach" ] ]) (target.textContent, messages reports)
    mounted.Dispose ()

let private serverChart =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [|
                t.Attrs.className "chart"
                t.Hooks.widget (fun _ (_: HTMLDivElement) -> ignore)
            |]
            [||])

let private serverWidget () =
    let render policy =
        Html.render policy (Program.run (Component.render serverChart ()))

    let rejected =
        try
            render Reject |> ignore
            None
        with error ->
            Some error.Message

    equal
        ("<div class=\"chart\"></div>", Some true)
        (render Omit,
         rejected
         |> Option.map (fun message -> message.StartsWith "Browser capability requires explicit Omit policy"))

    let lent =
        Component.define (fun _ () -> ()) (fun t -> t.span [| t.Hooks.lend (fun _ (_: HTMLSpanElement) -> ignore) |] [||])

    let lentRejected =
        try
            Html.render Reject (Program.run (Component.render lent ()))
            |> ignore

            None
        with error ->
            Some error.Message

    equal
        [
            Some "Browser capability requires explicit Omit policy: <div> widget"
            Some "Browser capability requires explicit Omit policy: <span> lent hook"
        ]
        [ rejected; lentRejected ]

/// <summary>Authored widget elements rendered on the server under Omit: with a text child, an empty list child, or two widget hooks.</summary>
let private serverWidgetChildren (ops: Operations<'view>) (run: 'view -> Backend -> unit) =
    let widget () =
        Typed.widget (fun (_: HTMLDivElement) -> ignore)

    let rejects (view: 'view) =
        raisesArgument (fun () -> Html.render Omit (run view) |> ignore)

    equal
        (true, true, true, "<div></div>")
        (rejects (Typed.div ops [| widget () |] [| ops.Text "child" |]),
         rejects (Typed.div ops [| widget () |] [| Keyed.list ops id (fun () -> List.empty<int>) (fun _ -> ops.Text "row") |]),
         rejects (Typed.div ops [| widget (); widget () |] [||]),
         Html.render Omit (run (Typed.div ops [| widget () |] [||])))

/// <summary>A widget on a mount of another graph, the shape of the playground's Foundation panel.</summary>
let private widgetOnOtherGraph () =
    use graph = new Graph ()
    use other = new Graph ()
    let label = other.Run (fun () -> Api.createSignal "other")
    let log = ResizeArray<string>()
    let target = host ()

    let inner =
        span Program.ops [||] [| Typed.reactiveText Program.ops (fun () -> label.Value) |]

    let view =
        Typed.div
            Program.ops
            [|
                Typed.widget (fun (node: HTMLDivElement) ->
                    let nested =
                        mountWith SynchronousCommits other None strict node None (fun context -> Program.run inner context.Backend)

                    fun () ->
                        nested.Dispose ()
                        log.Add (sprintf "disposed %b" (node.hasChildNodes ())))
            |]
            [||]

    let mounted =
        mountWith SynchronousCommits graph None strict target None (fun context -> Program.run view context.Backend)

    other.Run (fun () -> label.Value <- "updated")
    let updated = target.textContent
    mounted.Dispose ()
    other.Run (fun () -> label.Value <- "late")
    equal ("updated", [ "disposed false" ], 0.) (updated, List.ofSeq log, target.childNodes.length)

let private measuredWidget () =
    use graph = new Graph ()
    let target = host ()
    attachToBody target
    let width = ref 0.

    let view =
        Typed.div
            Program.ops
            [|
                Typed.attribute "style" "width: 120px; height: 10px"
                Typed.widget (fun (node: HTMLDivElement) ->
                    width.Value <- node.getBoundingClientRect().width
                    ignore)
            |]
            [||]

    let mounted =
        mountWith SynchronousCommits graph None strict target None (fun context -> Program.run view context.Backend)

    mounted.Dispose ()
    target.remove ()
    equal true (width.Value > 0.)

let private focusInsideWidget () =
    use graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal [ 1 ])
    let field = ref Unchecked.defaultof<HTMLInputElement>
    let target = host ()
    attachToBody target

    let view =
        Typed.div
            Program.ops
            [||]
            [|
                Typed.div
                    Program.ops
                    [|
                        Typed.widget (fun (node: HTMLDivElement) ->
                            field.Value <- createInput ()
                            node.appendChild field.Value |> ignore
                            field.Value.focus ()
                            ignore)
                    |]
                    [||]
                Keyed.list Program.ops id (fun () -> rows.Value) (fun current -> span Program.ops [||] [| Program.text (string (current ())) |])
            |]

    let mounted =
        mountWith SynchronousCommits graph None strict target None (fun context -> Program.run view context.Backend)

    graph.Run (fun () -> rows.Value <- [ 2; 1; 3 ])
    let focused = obj.ReferenceEquals (activeElement (), field.Value)
    mounted.Dispose ()
    target.remove ()
    equal true focused


/// <summary>A strict report raised after a case returns fails the next case, and only that one.</summary>
let private lateStrictReport () =
    let wrapped = strictCases [| "first", ignore; "second", ignore |]

    let outcome (check: unit -> unit) =
        try
            check ()
            None
        with error ->
            Some error.Message

    snd wrapped[0] ()

    try
        strict [ Exception "late" ]
    with _ ->
        ()

    let next = outcome (snd wrapped[1])
    let after = outcome (snd wrapped[1])

    let late =
        next
        |> Option.exists (fun message ->
            message.StartsWith "Late report from the previous case"
            && message.Contains "late")

    equal (true, None) (late, after)

let cases: (string * (unit -> unit)) array =
    strictCases
    <| Array.concat
        [
            both
                "a widget renders into its element once placed and its destroy runs while the element is attached"
                (widgetRenders Tree.ops Tree.run)
                (widgetRenders Program.ops Program.run)
            templated "a widget renders into its element once placed and its destroy runs while the element is attached" templateWidgetRenders
            [|
                "an element with a widget part and template children fails at definition time", widgetChildrenAtDefinition
            |]
            both
                "an element with a widget part and children fails at mount"
                (widgetChildrenAtMount Tree.ops Tree.run)
                (widgetChildrenAtMount Program.ops Program.run)
            both
                "widget content is removed with the element and never validated as Ranvier content"
                (foreignContent Tree.ops Tree.run)
                (foreignContent Program.ops Program.run)
            templated "widget content is removed with the element and never validated as Ranvier content" templateForeignContent
            [|
                "synchronous: a nested mount inside a widget element updates, dispatches delegated events and disposes with its owner",
                (fun () -> nestedMountInWidget false)
                "queued: a nested mount inside a widget element updates, dispatches delegated events and disposes with its owner",
                (fun () -> nestedMountInWidget true)
            |]
            both
                "a lent element keeps attributes added by the integration and Ranvier's slots keep updating"
                (lentKeepsAttributes Tree.ops Tree.run)
                (lentKeepsAttributes Program.ops Program.run)
            templated "a lent element keeps attributes added by the integration and Ranvier's slots keep updating" templateLentKeepsAttributes
            both
                "an integration that moves its element is reported after its cleanup"
                (movedIntegration Tree.ops Tree.run)
                (movedIntegration Program.ops Program.run)
            both
                "a moved element is reported alongside a failing cleanup"
                (movedIntegrationFailingCleanup Tree.ops Tree.run)
                (movedIntegrationFailingCleanup Program.ops Program.run)
            both
                "a lent mount root moved with its mount to another host is not reported"
                (lentMountRootMoved Tree.ops Tree.run)
                (lentMountRootMoved Program.ops Program.run)
            both
                "a lent row root moved by a keyed reorder and with its mount to another host is not reported"
                (lentRowRoots Tree.ops Tree.run)
                (lentRowRoots Program.ops Program.run)
            templated "a lent row root moved by a keyed reorder and with its mount to another host is not reported" templateLentRowRoots
            both
                "a widget under hidden content attaches at reveal and is destroyed on teardown"
                (widgetUnderBoundary Tree.ops Tree.run)
                (widgetUnderBoundary Program.ops Program.run)
            both "a widget attach failure shows the error view" (widgetAttachFailure Tree.ops Tree.run) (widgetAttachFailure Program.ops Program.run)
            [|
                "a widget element renders empty on the server; Reject raises and Omit writes the element", serverWidget
                "tree: a widget element with children or a second content part raises under Omit", (fun () -> serverWidgetChildren Tree.ops Tree.run)
                "program: a widget element with children or a second content part raises under Omit",
                (fun () -> serverWidgetChildren Program.ops Program.run)
                "a widget hosting a mount on another graph disposes it with the outer mount", widgetOnOtherGraph
                "a strict report raised after a case returns fails the next case", lateStrictReport
            |]
        ]

let browserCases: (string * (unit -> unit)) array =
    strictCases
        [|
            "a widget measuring its element on attach sees layout", measuredWidget
            "focus inside a widget survives an unrelated list update", focusInsideWidget
        |]
