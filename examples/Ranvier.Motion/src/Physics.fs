/// <summary>
/// A spring playground: a puck thrown with a spring release, a chain of followers trailing it, a timeline scrubbed by a
/// range input, and the motion policy switched by hand.
/// </summary>
module Physics

open Fable.Core
open global.Ranvier
open Ranvier.Anime
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Dom.Scheduling
open Ranvier.View.Sugar
open Ranvier.View.Anime

[<NoEquality; NoComparison>]
type Playground =
    {
        /// <summary>The puck's offset from its home, in pixels.</summary>
        Puck: Signal<struct (float * float)>
        Dragging: Signal<bool>
        /// <summary>The scrubbed timeline's progress, in 0..1.</summary>
        Scrub: Signal<float>
        Tips: Signal<bool>
        Policy: Signal<Reactive.MotionPolicy>
        /// <summary>The scrubbed timeline once placed.</summary>
        Timeline: Timeline ref
        /// <summary>The spring returning the puck home; a new spring, a grab or the instance's disposal cancels it.</summary>
        Spring: MotionSlot
    }

/// <summary>The followers' durations: each trails the puck further behind the one before it.</summary>
let trail = [| 140.; 260.; 380.; 500.; 620. |]

let private create () =
    {
        Puck = Api.createSignal (struct (0., 0.))
        Dragging = Api.createSignal false
        Scrub = Api.createSignal 0.
        Tips = Api.createSignal false
        Policy = Reactive.policy ()
        Timeline = ref null
        Spring = Motion.slot ()
    }

let private reduced (playground: Playground) =
    Api.untrack (fun () -> playground.Policy.Value) = Reactive.Reduced

/// <summary>
/// Returns the puck home along a spring, through the position signal its draggable follows, in place of any running
/// spring; under the reduced policy it jumps home.
/// </summary>
let springHome (playground: Playground) =
    let struct (x, y) = Api.untrack (fun () -> playground.Puck.Value)

    if reduced playground then
        playground.Spring.Stop ()
        playground.Puck.Value <- struct (0., 0.)
    else
        let graph = Graph.Current
        let point = Page.point x y

        playground.Spring.Play (fun () ->
            Anime.animate (
                Targets.object point,
                Anim (
                    x = 0,
                    y = 0,
                    ease = Ease.spring (stiffness = 170., damping = 11.),
                    onUpdate = fun _ -> graph.Batch (fun () -> playground.Puck.Value <- struct (point.x, point.y))
                )
            ))

let private xOf (playground: Playground) =
    let struct (x, _) = playground.Puck.Value
    x

let private yOf (playground: Playground) =
    let struct (_, y) = playground.Puck.Value
    y

// The track's dots fan out, turn and change colour along one timeline that the range input seeks.
let private scrubbed (playground: Playground) (track: TS.Dom.Element) =
    let timeline =
        Anime
            .timeline(TimelineParams (autoplay = false, defaults = Anim (duration = 600, ease = Ease.inOutQuad)))
            .add(Page.queryAll track ".dot", Anim (y = Value.stagger (Value.fromTo (-70, 70)), rotate = 180, delay = Value.stagger 60))
            .add(
                Page.queryAll track ".dot",
                Anim (scale = Value.fromTo (1, 1.6), borderRadius = "20%", delay = Value.stagger (60, from = StaggerFrom.center))
            )

    playground.Timeline.Value <- timeline
    Reactive.scrub (fun () -> playground.Scrub.Value) timeline
    timeline

let private follower (index: int) =
    let parameters = AnimatableParams (duration = trail[index], ease = Ease.outQuart)

    div (Attrs.className "follower", Attrs.data ("follower", string index), Motion.follow "x" parameters xOf, Motion.follow "y" parameters yOf) { () }

let private tips =
    show
        (fun (p: Playground) -> p.Tips.Value)
        (aside (Attrs.className "tips", Motion.rise) {
            h3 () { "Try this" }

            ul () {
                li () { "Throw the puck: it keeps its speed, then a spring settles it inside the arena." }
                li () { "Drag the slider: the timeline seeks to its progress, forward or back." }
                li () { "Switch to reduced motion, then toggle this panel: it appears without its rise." }
            }
        })

/// <summary>The playground's component.</summary>
let view: Component<Playground -> unit> =
    Component.template
        (fun _ (publish: Playground -> unit) ->
            let playground = create ()
            publish playground
            playground)
        (section (Attrs.className "physics") {
            div (Attrs.className "arena") {
                for index in trail.Length - 1 .. -1 .. 0 do
                    follower index

                div (
                    Attrs.className (fun (p: Playground) -> if p.Dragging.Value then "puck grabbed" else "puck"),
                    Motion.drag
                        (DraggableParams (
                            container = ".arena",
                            releaseStiffness = 120.,
                            releaseDamping = 9.,
                            releaseMass = 1.,
                            velocityMultiplier = 1.4
                        ))
                        (fun (p: Playground) ->
                            {
                                Position = p.Puck
                                Dragging = ValueSome p.Dragging
                                Slot = ValueSome p.Spring
                            })
                ) {
                    "drag"
                }
            }

            div (Attrs.className "controls") {
                button (Attrs.className "primary home", Events.onClick (fun (p: Playground) _ -> springHome p)) { "Spring home" }

                span (Attrs.className "readout") {
                    fun (p: Playground) ->
                        let struct (x, y) = p.Puck.Value
                        sprintf "x %.0f · y %.0f" x y
                }

                label (Attrs.className "switch") {
                    input (
                        Attrs.attribute ("type", "checkbox"),
                        Attrs.className "reduced",
                        Props.Input.``checked`` (fun (p: Playground) -> p.Policy.Value = Reactive.Reduced),
                        Events.onChange (fun (p: Playground) event ->
                            p.Policy.Value <-
                                if Page.targetChecked event then
                                    Reactive.Reduced
                                else
                                    Reactive.Full)
                    )

                    "Reduced motion"
                }

                button (Attrs.className "tips-toggle", Events.onClick (fun (p: Playground) _ -> p.Tips.Value <- not p.Tips.Value)) {
                    fun (p: Playground) -> if p.Tips.Value then "Hide tips" else "Show tips"
                }
            }

            div (Attrs.className "scrubber") {
                div (Attrs.className "track", Motion.on scrubbed) {
                    for index in 1..9 do
                        div (Attrs.className "dot", Attrs.data ("dot", string index)) { () }
                }

                input (
                    Attrs.attribute ("type", "range"),
                    Attrs.attribute ("min", "0"),
                    Attrs.attribute ("max", "1000"),
                    Attrs.className "scrub",
                    Attrs.aria ("label", "Timeline progress"),
                    Props.Input.value (fun (p: Playground) -> string (System.Math.Round (p.Scrub.Value * 1000.))),
                    Events.onInput (fun (p: Playground) event -> p.Scrub.Value <- float (Page.targetValue event) / 1000.)
                )

                span (Attrs.className "readout") { fun (p: Playground) -> sprintf "%.0f%%" (p.Scrub.Value * 100.) }
            }

            div (Attrs.className "tips-frame") { tips }
        })

/// <summary>A mounted playground and the instance its tests read.</summary>
[<NoEquality; NoComparison>]
type Mounted =
    {
        Playground: Playground
        Mount: Mounting.Mounted
    }

/// <summary>Mounts a playground into <c>host</c> under <c>mode</c>.</summary>
let mount (mode: CommitMode) (graph: Graph) (report: exn list -> unit) (host: TS.Dom.HTMLElement) : Mounted =
    let mutable playground = Unchecked.defaultof<Playground>

    let mounted =
        Mounting.mountWith mode graph None report host None (fun context ->
            Program.run (Component.render view (fun created -> playground <- created)) context.Backend)

    {
        Playground = playground
        Mount = mounted
    }

/// <summary>Publishes the playground's browser controls.</summary>
let activate (graph: Graph) (playground: Playground) : unit -> unit =

    Page.expose
        {|
            graph = graph
            playground = playground
            puck =
                fun () ->
                    let struct (x, y) =
                        graph.Run (fun () -> Api.untrack (fun () -> playground.Puck.Value))

                    [| x; y |]
            place = System.Func<float, float, unit>(fun x y -> graph.Run (fun () -> playground.Puck.Value <- struct (x, y)))
            dragging = fun () -> graph.Run (fun () -> Api.untrack (fun () -> playground.Dragging.Value))
            scrub = fun (progress: float) -> graph.Run (fun () -> playground.Scrub.Value <- progress)
            progress = fun () -> playground.Timeline.Value.progress
            reduced = fun () -> graph.Run (fun () -> Api.untrack (fun () -> playground.Policy.Value = Reactive.Reduced))
        |}

    ignore
