/// The F# samples of docs/content/server/pages.md, in page order; a sample that is an excerpt is wrapped in a function.
module PageDeclarations

open Ranvier.View
open Counter
open Shared

let counterPage: Page.Definition<CounterProps> =
    { Component = counter
      InitialProps = fun () -> { Start = 0; Label = "Clicks" }
      Document =
        { Server.DocumentShell.defaults with
            Lang = Some "en"
            Scripts = [ Server.ModuleScript.Src "/generated/CounterEntry.js" ] }
      Options = { Server.Options.defaults with Policy = Omit }
      State =
        Some
            { Id = stateId
              Encode = counterProps.Encode
              Decode = fun json -> counterProps.Decode json |> Result.mapError (fun error -> error.Message) } }

let renderPage () = Page.render counterPage

let clientSource = Page.clientSource "CounterEntry" "PageDeclarations.counterPage"
