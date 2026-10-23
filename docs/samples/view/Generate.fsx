// Regenerates the sample sources in this directory from the fsharp blocks of the docs pages.
//   dotnet fsi docs/samples/view/Generate.fsx            writes the sources
//   dotnet fsi docs/samples/view/Generate.fsx --check    exits 1 when a committed source differs from its pages
// Program.fs is hand-written and is not generated.
open System
open System.IO
open System.Text

let samples = __SOURCE_DIRECTORY__
let content = Path.GetFullPath (Path.Combine (samples, "..", "..", "content"))
let fence = String ('`', 3)

/// The fsharp blocks of a page, each as lines; blank lines stay unindented.
let blocks (page: string) =
    let result = ResizeArray<string list> ()
    let mutable current: ResizeArray<string> option = None

    for line in File.ReadAllLines (Path.Combine (content, page)) do
        match current with
        | None ->
            if line = fence + "fsharp" then
                current <- Some (ResizeArray ())
        | Some lines ->
            if line.StartsWith fence then
                result.Add (List.ofSeq lines)
                current <- None
            else
                lines.Add line

    List.ofSeq result

let indent (spaces: int) (lines: string list) =
    let pad = String (' ', spaces)
    lines |> List.map (fun line -> if line = "" then line else pad + line)

let opens =
    [ "open global.Ranvier"
      "open Ranvier.View"
      "open Ranvier.View.Sugar" ]

/// The sample opens with the DOM renderer beside the view layer.
let domOpens =
    opens |> List.collect (fun line -> if line = "open Ranvier.View" then [ line; "open Ranvier.View.Dom" ] else [ line ])

let header (page: string) (name: string) =
    [ $"/// The F# samples of docs/content/{page}, in page order; a sample that is an excerpt is wrapped in a function."
      $"module {name}"
      "" ]

/// Every block of the page, each followed by a blank line.
let all (page: string) =
    blocks page |> List.collect (fun block -> block @ [ "" ])

let mounted = [ "    mounted" ]

let generated: (string * string list) list =
    let gettingStarted = blocks "view/getting-started.md"
    let templates = blocks "view/templates.md"
    let authored = blocks "view/authored.md"
    let stateTransfer = blocks "server/state-transfer.md"
    let deployment = blocks "server/deployment.md"
    let lifetimes = blocks "view/lifetimes.md"
    let documentHead = blocks "view/document-head.md"
    let elmish = blocks "view/elmish.md"
    let testing = blocks "view/testing.md"
    let serverIndex = blocks "server/index.md"
    let fableOpens = [ "open Fable.Core.TS" ] @ opens @ [ ""]

    let pageOfBlocks (page: string) (name: string) (preamble: string list) =
        header page name @ preamble @ all page

    [ "Counter.fs", "/// The first F# sample of docs/content/view/getting-started.md." :: gettingStarted[0]
      "CounterExplicit.fs",
      header "view/getting-started.md" "CounterExplicit" @ opens @ [ "open Counter"; "" ] @ gettingStarted[1]
      "Main.fs", "/// The mounting sample of docs/content/view/getting-started.md." :: gettingStarted[2]
      "IndexTaste.fs", header "view/index.md" "IndexTaste" @ (blocks "view/index.md")[0]

      "Templates.fs",
      header "view/templates.md" "Templates"
      @ templates[0]
      @ [ ""; "let elements ="; "    Component.template (fun _ () -> ()) (" ]
      @ indent 8 templates[1]
      @ [ "    )" ]
      @ (templates[2 .. templates.Length - 2] |> List.collect (fun block -> "" :: block))
      @ [ ""; "module Explicit =" ]
      @ indent 4 (List.last templates)

      "Components.fs", pageOfBlocks "view/components.md" "Components" []
      "Collections.fs", pageOfBlocks "view/collections.md" "Collections" fableOpens
      "Arms.fs", pageOfBlocks "view/arms.md" "Arms" fableOpens
      "Boundaries.fs", pageOfBlocks "view/boundaries.md" "Boundaries" fableOpens
      "Events.fs", pageOfBlocks "view/events.md" "Events" fableOpens

      "DocumentHead.fs",
      header "view/document-head.md" "DocumentHead"
      @ fableOpens
      @ documentHead[0]
      @ [ ""; "let options = { Server.Options.defaults with Policy = Omit }"; ""; "let renderArticle () =" ]
      @ indent 4 documentHead[1]
      @ [ "    page" ]

      "ElmishView.fs",
      header "view/elmish.md" "ElmishView"
      @ opens
      @ [ "open Ranvier.Elmish"; "" ]
      @ elmish[0]
      @ [ "" ]
      @ elmish[1]

      "Lifetimes.fs",
      header "view/lifetimes.md" "Lifetimes"
      @ ("open Fable.Core.TS" :: domOpens)
      @ [ "open Counter"
          ""
          "let mountSynchronous (graph: Graph) (host: Dom.HTMLElement) (report: exn list -> unit) =" ]
      @ indent 4 lifetimes[0]
      @ mounted
      @ [ ""; "let mountQueued (graph: Graph) (host: Dom.HTMLElement) (report: exn list -> unit) =" ]
      @ indent 4 lifetimes[1]
      @ mounted

      "SugarOpens.fs",
      header "view/sugar.md" "Sugar"
      @ (blocks "view/sugar.md")[0]
      @ [ ""; "let note = Component.template (fun _ () -> ()) (div () { \"opened\" })" ]

      "Authored.fs",
      header "view/authored.md" "Authored"
      @ authored[0]
      @ [ "" ]
      @ authored[1]
      @ [ "" ]
      @ authored[2]

      "FableRanvier.fs",
      header "view/authored.md" "FableRanvier"
      @ (authored[3] |> List.filter (fun line -> line.StartsWith "open "))
      @ [ ""; "let start () =" ]
      @ (indent 4 authored[3]
         |> List.filter (fun line -> not (line.StartsWith "    open "))
         |> fun lines ->
             match lines with
             | "" :: rest -> rest
             | _ -> lines)
      @ mounted

      "CounterHtml.fs", header "view/testing.md" "CounterHtml" @ testing[0]
      "CounterChecks.fs", header "view/testing.md" "CounterChecks" @ testing[1]
      "TwinChecks.fs", header "view/testing.md" "TwinChecks" @ testing[2]
      "ViewDumps.fs", header "view/testing.md" "ViewDumps" @ testing[3]
      "Shared.fs", header "server/state-transfer.md" "Shared" @ [ "open Counter"; "" ] @ stateTransfer[0]

      "StateTransfer.fs",
      header "server/state-transfer.md" "StateTransfer"
      @ [ "open Ranvier.View"
          "open Ranvier.View.Dom"
          "open Counter"
          "open Shared"
          ""
          "let stateElement (props: CounterProps) =" ]
      @ indent 4 stateTransfer[1]
      @ [ ""; "let readProps () =" ]
      @ indent 4 stateTransfer[2]
      @ [ ""; "    props" ]

      "ServerPage.fs", "/// The page sample of docs/content/server/deployment.md." :: deployment[0]

      "DeploymentEndpoint.fs",
      [ "/// The endpoint sample of docs/content/server/deployment.md, without its entry-point attribute, which types `args`."
        "module DeploymentEndpoint" ]
      @ (deployment[1]
         |> List.filter (fun line -> line <> "module Program" && line <> "[<EntryPoint>]")
         |> List.map (fun line -> if line = "let main args =" then "let main (args: string array) =" else line))

      "Client.fs",
      header "server/hydration.md" "Client"
      @ [ "open Counter"; "open Shared" ]
      @ (blocks "server/hydration.md")[0]

      "Rendering.fs", pageOfBlocks "server/rendering.md" "Rendering" (opens @ [ "open Counter"; "" ])
      "PageDeclarations.fs", pageOfBlocks "server/pages.md" "PageDeclarations" []

      "MotionBindings.fs", pageOfBlocks "view/motion.md" "MotionBindings" []
      "MotionReactive.fs", pageOfBlocks "view/motion-reactive.md" "MotionReactive" []
      "MotionParts.fs", pageOfBlocks "view/motion-parts.md" "MotionParts" []
      "MotionTesting.fs", pageOfBlocks "view/motion-testing.md" "MotionTesting" []

      "ServerOverview.fs",
      header "server/index.md" "ServerOverview"
      @ domOpens
      @ [ "open Counter"
          ""
          "let overview (graph: Graph) (report: exn list -> unit) host (props: CounterProps) =" ]
      @ indent 4 serverIndex[0]
      @ [ "    html, mounted" ] ]

let render (lines: string list) =
    let trimmed = lines |> List.rev |> List.skipWhile ((=) "") |> List.rev
    String.Join ("\n", trimmed) + "\n"

let utf8 = UTF8Encoding false
let check = fsi.CommandLineArgs |> Array.contains "--check"

let stale =
    [ for name, lines in generated do
          let path = Path.Combine (samples, name)
          let text = render lines

          if check then
              let current = if File.Exists path then File.ReadAllText path else ""

              if current.Replace ("\r\n", "\n") <> text then
                  name
          else
              File.WriteAllText (path, text, utf8) ]

if check && not stale.IsEmpty then
    eprintfn
        "Samples differ from their pages: %s. Run: dotnet fsi docs/samples/view/Generate.fsx"
        (String.Join (", ", stale))

    exit 1
elif check then
    printfn "All %d generated samples match their pages." generated.Length
else
    printfn "Wrote %d samples." generated.Length
