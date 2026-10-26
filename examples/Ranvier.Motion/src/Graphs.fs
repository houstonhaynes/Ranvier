/// <summary>Interactive SVG charts with animated inspection, viewport changes and ranked bars.</summary>
module Graphs

open System
open Fable.Core
open global.Ranvier
open Ranvier.Anime
open Ranvier.View
open Ranvier.View.Sugar
open Ranvier.View.Anime

/// <summary>The visible horizontal interval in a 960-unit, 24-hour data set.</summary>
type Viewport = { Start: float; Span: float }

/// <summary>The graph controls and selected data point, owned by one page instance.</summary>
[<NoEquality; NoComparison>]
type Store =
    {
        Window: Signal<Viewport>
        Cursor: Signal<int>
        /// <summary>Tooltip position as percentages of the plot's width and height.</summary>
        Pointer: Signal<struct (float * float)>
        Inspecting: Signal<bool>
        Dragging: Signal<bool>
        Comparison: Signal<bool>
        Snapshot: Signal<int>
        Playing: Signal<bool>
    }

let private viewport start span =
    let span = max 120. (min 960. span)

    {
        Start = max 0. (min (960. - span) start)
        Span = span
    }

let private zoom (window: Viewport) factor anchor =
    let span = max 120. (min 960. (window.Span * factor))

    viewport
        (anchor
         - (anchor - window.Start) * span / window.Span)
        span

let private value channel index =
    let t = float index

    91.
    + 27. * sin (t / 10. + channel)
    + 18. * cos (t / 4.7 + channel)
    + 30. * exp (-(((t - 61.) / 9.) ** 2.))

let private y value =
    240. - value * 1.05

let private curve channel =
    let values = Array.init 97 (fun index -> y (value channel index))

    let slope index =
        (values[min 96 (index + 1)]
         - values[max 0 (index - 1)])
        / (if index = 0 || index = 96 then 1. else 2.)

    [|
        sprintf "M0 %.2f" values[0]
        for index in 1..96 do
            let x = float index * 10.

            sprintf
                "C%.2f %.2f %.2f %.2f %.2f %.2f"
                (x - 10. + 10. / 3.)
                (values[index - 1] + slope (index - 1) / 3.)
                (x - 10. / 3.)
                (values[index] - slope index / 3.)
                x
                values[index]
    |]
    |> String.concat " "

let private time x =
    let minutes = int (Math.Round (x * 1.5))
    sprintf "%02d:%02d" (minutes / 60) (minutes % 60)

let private regions = [| "Amsterdam"; "London"; "Singapore"; "Sydney"; "Tokyo" |]

let private snapshots =
    [|
        [| 68; 42; 55; 36; 61 |]
        [| 48; 81; 66; 58; 42 |]
        [| 76; 59; 44; 91; 68 |]
        [| 52; 73; 95; 64; 83 |]
    |]

let private times = [| "09:00"; "12:00"; "15:00"; "18:00" |]

let private rank snapshot index =
    [| 0..4 |]
    |> Array.sortBy (fun i -> -snapshots[snapshot][i], i)
    |> Array.findIndex ((=) index)

/// <summary>Creates the full-day viewport and a paused ranking chart in the current graph.</summary>
let createStore () =
    {
        Window = Api.createSignal (viewport 0. 960.)
        Cursor = Api.createSignal 48
        Pointer = Api.createSignal (struct (50., 35.))
        Inspecting = Api.createSignal false
        Dragging = Api.createSignal false
        Comparison = Api.createSignal true
        Snapshot = Api.createSignal 0
        Playing = Api.createSignal false
    }

let private changeZoom (store: Store) factor =
    let window = store.Window.Value
    store.Inspecting.Value <- false
    store.Window.Value <- zoom window factor (window.Start + window.Span / 2.)

let private pan (store: Store) direction =
    let window = store.Window.Value
    store.Inspecting.Value <- false
    store.Window.Value <- viewport (window.Start + direction * window.Span * 0.25) window.Span

[<Emit("$0.addEventListener($1, $2, {passive:false})")>]
let private listen (element: obj) (name: string) (callback: obj -> unit) : unit = jsNative

[<Emit("$0.removeEventListener($1, $2)")>]
let private unlisten (element: obj) (name: string) (callback: obj -> unit) : unit = jsNative

[<Emit("$0.clientX")>]
let private clientX (event: obj) : float = jsNative

[<Emit("($1 - $0.getBoundingClientRect().left) / $0.getBoundingClientRect().width")>]
let private fraction (element: obj) (x: float) : float = jsNative

[<Emit("({Start:$0.viewBox.baseVal.x,Span:$0.viewBox.baseVal.width})")>]
let private displayed (element: obj) : Viewport = jsNative

[<Emit("$0.preventDefault()")>]
let private prevent (event: obj) : unit = jsNative

[<Emit("$0.deltaY")>]
let private deltaY (event: obj) : float = jsNative

[<Emit("$0.key")>]
let private key (event: obj) : string = jsNative

[<Emit("$0.button === 0")>]
let private primary (event: obj) : bool = jsNative

[<Emit("$0.setPointerCapture($1.pointerId)")>]
let private capture (element: obj) (event: obj) : unit = jsNative

let private inspect (store: Store) world position =
    let index = max 0 (min 96 (int (Math.Round (world / 10.))))
    store.Cursor.Value <- index
    store.Pointer.Value <- struct (max 0. (min 100. (position * 100.)), y (value 0. index) / 2.8)
    store.Inspecting.Value <- true

let private pointerSetup (graph: Graph) (store: Store) (element: obj) =
    let mutable drag = None

    let move event =
        graph.Run (fun () ->
            let position = fraction element (clientX event)

            match drag with
            | Some (origin, window: Viewport) -> store.Window.Value <- viewport (window.Start - (position - origin) * window.Span) window.Span
            | None ->
                let window = displayed element
                inspect store (window.Start + position * window.Span) position)

    let down event =
        if primary event then
            prevent event
            capture element event

            graph.Run (fun () ->
                drag <- Some (fraction element (clientX event), displayed element)
                store.Dragging.Value <- true
                store.Inspecting.Value <- false)

    let up _ =
        drag <- None
        graph.Run (fun () -> store.Dragging.Value <- false)

    let leave _ =
        if drag.IsNone then
            graph.Run (fun () -> store.Inspecting.Value <- false)

    let wheel event =
        prevent event

        graph.Run (fun () ->
            let position = fraction element (clientX event)
            let window = displayed element
            let target = store.Window.Value

            let nextSpan =
                max
                    120.
                    (min
                        960.
                        (target.Span
                         * exp (max -400. (min 400. (deltaY event)) * 0.002)))

            let anchor = window.Start + position * window.Span
            store.Inspecting.Value <- false
            store.Window.Value <- viewport (anchor - position * nextSpan) nextSpan)

    let keyboard event =
        match key event with
        | "+"
        | "=" ->
            prevent event
            graph.Run (fun () -> changeZoom store 0.65)
        | "-" ->
            prevent event
            graph.Run (fun () -> changeZoom store (1. / 0.65))
        | "0" ->
            prevent event

            graph.Run (fun () ->
                store.Window.Value <- viewport 0. 960.
                store.Inspecting.Value <- false)
        | "ArrowLeft"
        | "ArrowRight" as direction ->
            prevent event

            graph.Run (fun () ->
                let window = store.Window.Value

                let index =
                    max
                        0
                        (min
                            96
                            (store.Cursor.Value
                             + (if direction = "ArrowLeft" then -1 else 1)))

                let world = float index * 10.
                let start = min world (max (world - window.Span) window.Start)
                let window = viewport start window.Span
                store.Window.Value <- window
                inspect store world ((world - window.Start) / window.Span))
        | "Escape" -> graph.Run (fun () -> store.Inspecting.Value <- false)
        | _ -> ()

    let listeners =
        [|
            "pointermove", move
            "pointerdown", down
            "pointerup", up
            "pointercancel", up
            "lostpointercapture", up
            "pointerleave", leave
            "wheel", wheel
            "keydown", keyboard
        |]

    for name, callback in listeners do
        listen element name callback

    fun () ->
        for name, callback in listeners do
            unlisten element name callback

let private inspector =
    article (Attrs.className "graph-card explorer") {
        header (Attrs.className "graph-heading") {
            div () {
                span (Attrs.className "eyebrow") { "01 / INSPECT & NAVIGATE" }
                h2 () { "Explore a signal" }
                p () { "API latency over one day. Move over the line, scroll to zoom, or drag to pan." }
            }

            span (Attrs.className "graph-unit") { "milliseconds" }
        }

        div (Attrs.className "graph-controls") {
            button (Events.onClick (fun store _ -> changeZoom store 0.65)) { "Zoom in" }
            button (Events.onClick (fun store _ -> changeZoom store (1. / 0.65))) { "Zoom out" }
            button (Events.onClick (fun store _ -> pan store -1.)) { "Pan left" }
            button (Events.onClick (fun store _ -> pan store 1.)) { "Pan right" }

            button (
                Events.onClick (fun (store: Store) _ ->
                    store.Window.Value <- viewport 0. 960.
                    store.Inspecting.Value <- false)
            ) {
                "Reset view"
            }

            button (
                Events.onClick (fun (store: Store) _ -> store.Comparison.Value <- not store.Comparison.Value),
                Attrs.attribute (
                    "aria-pressed",
                    fun (store: Store) ->
                        Some (
                            string store.Comparison.Value
                            |> fun s -> s.ToLowerInvariant ()
                        )
                )
            ) {
                "Worker comparison"
            }
        }

        div (Attrs.className "plot-frame") {
            div (Attrs.className "y-axis", Attrs.attribute ("aria-hidden", "true")) {
                for value in [ 200; 150; 100; 50; 0 ] do
                    span () { string value }
            }

            div (Attrs.className "plot-area") {
                svg (
                    Attrs.className "explorer-svg",
                    Attrs.attribute ("viewBox", "0 0 960 280"),
                    Attrs.attribute ("preserveAspectRatio", "none"),
                    Attrs.attribute ("tabindex", "0"),
                    Attrs.role "group",
                    Attrs.attribute ("aria-label", "Latency chart. Arrow keys inspect points; plus and minus zoom; zero resets."),
                    Hooks.mounted (fun store element -> pointerSetup Graph.Current store (box element)),
                    Motion.animateOn (fun (store: Store) -> store.Window.Value, store.Dragging.Value) (fun _ element (window, dragging) ->
                        let parameters =
                            Anim (duration = (if dragging then 100 else 650), ease = Ease.outQuart)

                        Anime.animate (element, parameters.With ("viewBox", Value.op_Implicit (sprintf "%.3f 0 %.3f 280" window.Start window.Span))))
                ) {
                    defs () {
                        linearGradient (
                            Attrs.attribute ("id", "graph-fill"),
                            Attrs.attribute ("x1", "0"),
                            Attrs.attribute ("y1", "0"),
                            Attrs.attribute ("x2", "0"),
                            Attrs.attribute ("y2", "1")
                        ) {
                            stop (
                                Attrs.attribute ("offset", "0%"),
                                Attrs.attribute ("stop-color", "#6953e8"),
                                Attrs.attribute ("stop-opacity", ".24")
                            ) {
                                ()
                            }

                            stop (
                                Attrs.attribute ("offset", "100%"),
                                Attrs.attribute ("stop-color", "#6953e8"),
                                Attrs.attribute ("stop-opacity", ".01")
                            ) {
                                ()
                            }
                        }
                    }

                    for value in [ 0; 50; 100; 150; 200 ] do
                        line (
                            Attrs.className "graph-grid",
                            Attrs.attribute ("x1", "0"),
                            Attrs.attribute ("x2", "960"),
                            Attrs.attribute ("y1", string (y (float value))),
                            Attrs.attribute ("y2", string (y (float value)))
                        ) {
                            ()
                        }

                    path (Attrs.className "signal-area", Attrs.attribute ("d", curve 0. + " L960 280 L0 280 Z")) { () }

                    show
                        (fun (store: Store) -> store.Comparison.Value)
                        (path (Attrs.className "comparison-line", Attrs.attribute ("d", curve 1.7), Motion.fade) { () })

                    path (Attrs.className "signal-line", Attrs.attribute ("d", curve 0.)) { () }

                    line (
                        Attrs.className "crosshair",
                        Attrs.attribute ("y1", "12"),
                        Attrs.attribute ("y2", "255"),
                        Motion.follow "x1" (AnimatableParams (duration = 240, ease = Ease.outQuart)) (fun (store: Store) ->
                            float store.Cursor.Value * 10.),
                        Motion.follow "x2" (AnimatableParams (duration = 240, ease = Ease.outQuart)) (fun (store: Store) ->
                            float store.Cursor.Value * 10.)
                    ) {
                        ()
                    }

                    ellipse (
                        Attrs.className "focus-point",
                        Attrs.attribute ("rx", "5"),
                        Attrs.attribute ("ry", "5"),
                        Motion.animateOn (fun (store: Store) -> store.Window.Value.Span, store.Dragging.Value) (fun _ element (span, dragging) ->
                            let parameters =
                                Anim (duration = (if dragging then 100 else 650), ease = Ease.outQuart)

                            Anime.animate (element, parameters.With ("rx", Value.op_Implicit (span * 5. / 960.)))),
                        Motion.follow "cx" (AnimatableParams (duration = 240, ease = Ease.outQuart)) (fun (store: Store) ->
                            float store.Cursor.Value * 10.),
                        Motion.follow "cy" (AnimatableParams (duration = 240, ease = Ease.outQuart)) (fun (store: Store) ->
                            y (value 0. store.Cursor.Value))
                    ) {
                        ()
                    }
                }

                show
                    (fun (store: Store) -> store.Inspecting.Value)
                    (div (
                        Attrs.className "graph-tooltip",
                        Attrs.role "status",
                        Motion.follow "--tip-x" (AnimatableParams (unit = "%", duration = 240, ease = Ease.outQuart)) (fun (store: Store) ->
                            let struct (x, _) = store.Pointer.Value in x),
                        Motion.follow "top" (AnimatableParams (unit = "%", duration = 240, ease = Ease.outQuart)) (fun (store: Store) ->
                            let struct (_, y) = store.Pointer.Value in y)
                    ) {
                        span () { fun (store: Store) -> time (float store.Cursor.Value * 10.) }
                        strong () { fun (store: Store) -> sprintf "%.1f ms" (value 0. store.Cursor.Value) }
                    })

                div (Attrs.className "x-axis", Attrs.attribute ("aria-hidden", "true")) {
                    for index in 0..4 do
                        span () { fun (store: Store) -> let window = store.Window.Value in time (window.Start + window.Span * float index / 4.) }
                }
            }
        }

        footer (Attrs.className "graph-footnote") {
            span (Attrs.className "legend-api") { "API" }
            span (Attrs.className "legend-worker") { "Worker" }
            span () { "← / → inspect · + / − zoom · 0 reset" }
        }

        p (Attrs.className "technique") {
            "Motion.follow eases the crosshair and tooltip. Motion.animateOn interpolates the SVG viewBox; pointer coordinates use the viewBox currently on screen."
        }
    }

let private bar (index: int) =
    div (
        Attrs.className ("rank-row region-" + string index),
        Attrs.data ("region", regions[index]),
        Attrs.role "listitem",
        Attrs.attribute ("style", sprintf "transform:translateY(%dpx)" (rank 0 index * 52)),
        Motion.animateOn
            (fun (store: Store) -> let snapshot = store.Snapshot.Value in rank snapshot index, snapshots[snapshot][index])
            (fun _ element (rank, score) ->
                let timeline = Anime.timeline (TimelineParams ())

                timeline.add (
                    element,
                    Anim (y = Value.op_Implicit (float rank * 52.), duration = 650, ease = Ease.outQuart),
                    At.ms (float rank * 30.)
                )
                |> ignore

                timeline.add (
                    Page.query element ".bar-fill",
                    Anim (scaleX = Value.op_Implicit (float score / 100.), duration = 650, ease = Ease.outQuart),
                    At.ms (80. + float rank * 30.)
                ))
    ) {
        span (Attrs.className "region-name") { regions[index] }
        div (Attrs.className "bar-track") { div (Attrs.className "bar-fill") { () } }
        strong (Attrs.className "region-score") { fun (store: Store) -> sprintf "%d k" (snapshots[store.Snapshot.Value][index]) }
    }

/// <summary>The line explorer and ranked traffic chart over <c>store</c>.</summary>
let view (store: Store) =
    Component.template
        (fun _ () -> store)
        (section (Attrs.className "graphs") {
            inspector

            article (Attrs.className "graph-card rankings") {
                header (Attrs.className "graph-heading") {
                    div () {
                        span (Attrs.className "eyebrow") { "02 / REORDER & RETARGET" }
                        h2 () { "A changing leaderboard" }
                        p () { "Regional requests per minute. Change the snapshot, or play the four-part cycle." }
                    }

                    strong (Attrs.className "snapshot-time") { fun (store: Store) -> times[store.Snapshot.Value] }
                }

                div (Attrs.className "graph-controls") {
                    button (
                        Attrs.className "primary",
                        Events.onClick (fun (store: Store) _ -> store.Snapshot.Value <- (store.Snapshot.Value + 1) % snapshots.Length)
                    ) {
                        "Next snapshot"
                    }

                    button (Events.onClick (fun (store: Store) _ -> store.Playing.Value <- not store.Playing.Value)) {
                        fun (store: Store) ->
                            if store.Playing.Value then
                                "Pause snapshots"
                            else
                                "Play snapshots"
                    }
                }

                div (Attrs.className "rank-bars", Attrs.role "list", Attrs.attribute ("aria-label", "Regional request ranking")) {
                    for index in 0..4 do
                        bar index
                }

                p (Attrs.className "technique") {
                    "Each row owns a timeline. Position and bar length overlap with a short rank-based stagger, and new snapshots retarget the same elements."
                }
            }

            p (Attrs.className "graph-credit") {
                "Interaction ideas inspired by the "
                a (Attrs.attribute ("href", "https://livecharts.dev/docs/Avalonia/2.0.4/gallery")) { "LiveCharts gallery" }
                ". These charts use Ranvier views and anime.js. Data is synthetic."
            }
        })

[<Emit("setInterval($0, $1)")>]
let private every (callback: unit -> unit) (ms: int) : int = jsNative

[<Emit("clearInterval($0)")>]
let private stop (handle: int) : unit = jsNative

/// <summary>Owns the snapshot timer and exposes the page's test handle.</summary>
let activate (graph: Graph) (store: Store) =
    let timer =
        every
            (fun () ->
                graph.Run (fun () ->
                    if store.Playing.Value then
                        store.Snapshot.Value <- (store.Snapshot.Value + 1) % snapshots.Length))
            2600

    Page.expose {| graph = graph; store = store |}
    fun () -> stop timer
