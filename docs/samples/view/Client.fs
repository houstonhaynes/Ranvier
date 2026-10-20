/// The F# samples of docs/content/server/hydration.md, in page order; a sample that is an excerpt is wrapped in a function.
module Client

open Counter
open Shared
open Fable.Core
open Fable.Core.TS.Dom
open global.Ranvier
open Ranvier.View.Foundation

[<Global "document">]
let private document: Document = jsNative

let start () : unit -> unit =
    let props =
        match Hydration.readState stateId |> Option.map counterProps.Decode with
        | Some (Ok props) -> props
        | Some (Error error) -> failwithf "Unreadable props: %s" error.Message
        | None -> { Start = 0; Label = "Clicks" }

    let graph = new Graph ()
    let host = document.getElementById "app" |> Option.get

    let report (errors: exn list) =
        for error in errors do
            JS.console.warn ("Hydration", error)

    let mounted =
        Hydration.hydrate graph None report Hydration.HydrationOptions.defaults host counter props

    fun () ->
        mounted.Dispose ()
        graph.Dispose ()
