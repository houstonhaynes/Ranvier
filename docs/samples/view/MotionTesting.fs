/// The F# samples of docs/content/view/motion-testing.md, in page order; a sample that is an excerpt is wrapped in a function.
module MotionTesting

open Fable.Core
open Ranvier.Anime

[<Emit("document.body.appendChild(document.createElement($0))")>]
let createElement (tag: string) : TS.Dom.Element = jsNative

[<Emit("$0.style.opacity")>]
let opacityOf (element: TS.Dom.Element) : string = jsNative

/// Advances the mocked clock by the given milliseconds.
type Clock = float -> unit

let linearFade (advance: Clock) =
    Testing.manual ()
    let card = createElement "div"
    Anime.animate (card, Anim (opacity = Value.fromTo (0, 1), duration = 1000, ease = Ease.linear)) |> ignore

    let samples =
        [
            for step in 0..4 do
                advance (if step = 0 then 0. else 250.)
                Testing.step ()
                opacityOf card
        ]

    if samples <> [ "0"; "0.262"; "0.512"; "0.762"; "1" ] then
        failwithf "Unexpected opacities %A" samples

let checks = [| "a linear fade reads the same values every run", linearFade |]
