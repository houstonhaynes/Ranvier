/// The F# samples of docs/content/view/motion.md, in page order; a sample that is an excerpt is wrapped in a function.
module MotionBindings

open Fable.Core
open Ranvier.Anime

let reveal (card: TS.Dom.Element) =
    Anime.animate (card, Anim (x = 100, opacity = Value.fromTo (0, 1), duration = 400, ease = Ease.outQuart))

let settle (card: TS.Dom.Element) (cards: TS.Dom.Element array) =
    Anime.animate (card, Anim (x = "+=20", rotate = Value.from 45, opacity = Value.keys [| Value.op_Implicit 0; Value.op_Implicit 1; Value.op_Implicit 0.5 |]))
    |> ignore

    Anime.animate (cards, Anim (y = Value.tween (``to`` = 0, duration = 200, ease = Ease.outQuad), x = Value.per (fun _ index _ -> Value.op_Implicit (index * 10))))

let eases () =
    [|
        Ease.outQuart
        Ease.inOutSine
        Ease.cubicBezier (0.2, 0., 0., 1.)
        Ease.steps 4
        Ease.linearStops (0., 0.4, 1.)
        Ease.fn (fun t -> t * t)
        Ease.named "cubic-bezier(.2,0,0,1)"
    |]

let springy (card: TS.Dom.Element) =
    Anime.animate (card, Anim (y = 0, ease = Ease.spring (stiffness = 200, damping = 12)))

let overshoot (card: TS.Dom.Element) =
    Anime.animate (card, Anim (scale = Value.fromTo (0.7, 1), duration = 420, ease = Ease.outBackBy 2.))

let cascade (cards: TS.Dom.Element array) =
    Anime.animate (cards, Anim (opacity = Value.fromTo (0, 1), delay = Value.stagger (40, start = 100, from = StaggerFrom.center)))

let ripple (cells: TS.Dom.Element array) =
    Anime.animate (
        cells,
        Anim (scale = Value.fromTo (0.5, 1), delay = Value.stagger (20, grid = Grid.size (8, 4), from = StaggerFrom.at (0.5, 0.5)))
    )

let intro (title: TS.Dom.Element) (cards: TS.Dom.Element array) (finished: unit -> unit) =
    Anime
        .timeline(TimelineParams (defaults = Anim (duration = 300, ease = Ease.outQuad)))
        .add(title, Anim (opacity = Value.fromTo (0, 1)))
        .label("cards")
        .add(cards, Anim (y = Value.fromTo (12, 0)), At.stagger (Value.stagger 40))
        .add(title, Anim (scale = Value.fromTo (0.96, 1)), At.withPrevious)
        .call ((fun _ -> finished ()), At.after 100)

let follower (puck: TS.Dom.Element) =
    let animatable = Anime.animatable (puck, AnimatableParams (x = 400, y = 400, ease = Ease.outQuart))

    fun (x: float, y: float) ->
        animatable.set ("x", x)
        animatable.set ("y", y)

let travel (dot: TS.Dom.Element) (edge: TS.Dom.Element) =
    let path = Svg.motionPath edge
    Anime.animate (dot, Anim (translateX = path.translateX, translateY = path.translateY, duration = 460, ease = Ease.inOutSine))
