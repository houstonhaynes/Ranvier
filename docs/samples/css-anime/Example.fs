/// <summary>A typed CSS variable shared by a generated stylesheet and a reactive Motion attribute.</summary>
module CssAnimeExample

open global.Ranvier
open Ranvier.Anime
open Ranvier.Css
open Ranvier.Css.View
open Ranvier.View
open Ranvier.View.Sugar
open Ranvier.View.Anime

/// <summary>The length animated by each component instance.</summary>
let gap: CustomProperty<CssValue.Length.Value> = CustomProperty.create "--card-gap"

/// <summary>Static CSS whose width is computed by the browser from the animated variable.</summary>
let cardStyle = utility {
    Css.width (CssValue.Length.calc (CssValue.Length.variableWithFallback "--card-gap" (Css.px 0.) * Css.number 2. + Css.px 10.))
    Css.maxWidth (Css.percent 100.)
}

/// <summary>A card whose target gap follows its input signal while retaining its user class.</summary>
let card =
    Component.template
        (fun _ (target: Signal<float>) -> target)
        (div [|
            yield Attrs.className "card"
            yield! styles { cardStyle }
            yield Motion.followCss gap (AnimatableProp(duration = 300., ease = Ease.outQuart)) (fun (target: Signal<float>) -> Css.px target.Value)
        |] { "Animated typed CSS" })

/// <summary>Explicit endpoints usable with Motion.on, Motion.enter, timelines and Reactive.own.</summary>
let enter (element: Fable.Core.TS.Dom.Element) =
    Anime.animate(element, Anim(duration = 300, ease = Ease.outQuart).With(gap, Css.px 0., Css.px 100.))

/// <summary>A custom-property animation with typed numeric keyframes.</summary>
let pulse (element: Fable.Core.TS.Dom.Element) =
    Anime.animate(element, Anim(duration = 600).With(gap, [| Css.px 0.; Css.px 100.; Css.px 20. |]))
