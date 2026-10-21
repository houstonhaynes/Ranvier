/// The F# samples of docs/content/server/state-transfer.md, in page order; a sample that is an excerpt is wrapped in a function.
module StateTransfer

open Ranvier.View
open Ranvier.View.Dom
open Counter
open Shared

let stateElement (props: CounterProps) =
    Server.stateScript stateId (counterProps.Encode props)

let readProps () =
    let props =
        match Hydration.readState stateId |> Option.map counterProps.Decode with
        | Some (Ok props) -> props
        | Some (Error error) -> failwithf "Unreadable props: %s" error.Message
        | None -> { Start = 0; Label = "Clicks" }

    props
