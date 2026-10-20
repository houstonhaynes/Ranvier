/// The F# samples of docs/content/view/testing.md, in page order; a sample that is an excerpt is wrapped in a function.
module CounterChecks

open Fable.Core
open Fable.Core.TS.Dom
open global.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Counter

[<Global "document">]
let private document: Document = jsNative

let private expect (expected: string) (actual: string) =
    if expected <> actual then
        failwithf "Expected %s; got %s" expected actual

let clickIncrements () =
    use graph = new Graph ()
    let host = document.createElement "div"
    document.body.appendChild host |> ignore

    let mounted =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context ->
            Program.run (Component.render counter { Start = 0; Label = "Clicks" }) context.Backend)

    let button = unbox<HTMLElement> (host.querySelector "button").Value
    button.click ()
    button.click ()
    expect "2" button.textContent
    mounted.Dispose ()
    expect "" host.innerHTML

let cases = [| "a click increments the count", clickIncrements |]
