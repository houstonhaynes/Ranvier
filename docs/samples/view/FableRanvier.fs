/// The F# samples of docs/content/view/authored.md, in page order; a sample that is an excerpt is wrapped in a function.
module FableRanvier

open Fable.Core.TS.Dom
open global.Ranvier
open Fable.Ranvier

let start () =
    let graph = new Graph ()
    let count = graph.Run (fun () -> createSignal 0)
    let host = Global.document.getElementById("app").Value

    let mounted =
        Mount.mount graph host (fun () ->
            Dom.element "button"
                [ Dom.on "click" (fun _ -> count.Value <- count.Value + 1) ]
                [ Dom.reactiveText (fun () -> $"Count: {count.Value}") ]
            :> Node)
    mounted
