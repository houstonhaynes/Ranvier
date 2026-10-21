/// The F# samples of docs/content/view/motion-reactive.md, in page order; a sample that is an excerpt is wrapped in a function.
module MotionReactive

open global.Ranvier
open Fable.Core
open Ranvier.Anime

/// Fades the panel in, marks it shown once the fade completes, and scales the bar with the level.
let animatePanel (panel: TS.Dom.Element) (bar: TS.Dom.Element) (level: Signal<float>) (shown: Signal<bool>) =
    let fade =
        Reactive.own (Anime.animate (panel, Anim (opacity = Value.fromTo (0, 1), duration = 600)))

    let playback = Reactive.track fade
    Api.createEffect (fun () -> if playback.Completed.Value then shown.Value <- true)

    let fill =
        Reactive.own (Anime.animatable (bar, AnimatableParams (scaleX = 400, ease = Ease.outQuart)))

    Reactive.drive (fun () -> level.Value) fill "scaleX"

let scrubbed (stage: TS.Dom.Element) (position: Signal<float>) =
    let tour =
        Reactive.own (
            Anime
                .timeline(TimelineParams (autoplay = false))
                .add(stage, Anim (x = Value.fromTo (0, 240), duration = 1000))
                .add(stage, Anim (rotate = 90, duration = 500))
        )

    Reactive.scrub (fun () -> position.Value) tour

let honoured (card: TS.Dom.Element) =
    let duration = if (Reactive.policy ()).Value = Reactive.Reduced then 0 else 400
    Anime.animate (card, Anim (opacity = Value.fromTo (0, 1), duration = duration))
