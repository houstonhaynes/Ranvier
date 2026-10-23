module Ranvier.View.Twin.Tests.ServerParityTests

open Expecto
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Twin

// Browser-only parts render under Omit.
let private omit =
    { Server.Options.defaults with
        Policy = Representation.Omit
    }

let private serverFixtures () : (string * string) list =
    let options = Server.Options.defaults

    [
        "greeting", Server.render options ServerFixtures.greeting ServerFixtures.greetingProps
        "named", Server.render options ServerFixtures.named ServerFixtures.greetingProps
        "null title", Server.render options ServerFixtures.nullTitle ServerFixtures.greetingProps
        "staged waiting", Server.render options ServerFixtures.staged ServerFixtures.Waiting
        "staged working", Server.render options ServerFixtures.staged (ServerFixtures.Working 3)
        "staged complete", Server.render options ServerFixtures.staged (ServerFixtures.Complete "<done>")
        "decades", Server.render options ServerFixtures.decades 42
        "listed", Server.render options ServerFixtures.listed ServerFixtures.listedItems
        "carded", Server.render options ServerFixtures.carded ServerFixtures.greetingProps
        "pair", Server.render options ServerFixtures.pair ServerFixtures.greetingProps
        "static template", Server.render options ServerFixtures.staticTemplate ()
        "empty text", Server.render options ServerFixtures.emptyText ""
        "clicking", Server.render omit ServerFixtures.clicking ServerFixtures.greetingProps
        // The head fixture renders a <head> element into a fragment, where the HTML parser drops it.
        for name, explicitHtml, sugarHtml in SugarFixtures.elementServerPairs () do
            if not (name.Contains "head") then
                "sugar explicit: " + name, explicitHtml
                "sugar: " + name, sugarHtml
    ]

/// <summary>A fixture's server page under Omit and its strict hydration over a parsed body.</summary>
type private Hydrated =
    {
        Name: string
        Page: unit -> Server.Page
        Hydrate: string -> (Fable.Core.TS.Dom.HTMLElement -> unit) -> Session
        /// <summary>The document-order positions of the server nodes the client replaces.</summary>
        Replaced: int list
    }

let private collect =
    { TwinMount.defaults with
        Reports = Reports.Collect
    }

let private strict =
    { Hydration.HydrationOptions.defaults with
        Strict = true
    }

let private hydrated name (definition: Component<'props>) (props: 'props) =
    {
        Name = name
        Page = fun () -> Server.renderPage omit definition props
        Hydrate = fun body parsed -> Twin.hydrateHtmlWith collect strict body parsed definition props
        Replaced = []
    }

let private hydratedFixtures =
    [
        hydrated "greeting" ServerFixtures.greeting ServerFixtures.greetingProps
        hydrated "named" ServerFixtures.named ServerFixtures.greetingProps
        hydrated "null title" ServerFixtures.nullTitle ServerFixtures.greetingProps
        hydrated "staged waiting" ServerFixtures.staged ServerFixtures.Waiting
        hydrated "staged working" ServerFixtures.staged (ServerFixtures.Working 3)
        hydrated "staged complete" ServerFixtures.staged (ServerFixtures.Complete "<done>")
        hydrated "decades" ServerFixtures.decades 42
        hydrated "listed" ServerFixtures.listed ServerFixtures.listedItems
        hydrated "carded" ServerFixtures.carded ServerFixtures.greetingProps
        hydrated "pair" ServerFixtures.pair ServerFixtures.greetingProps
        hydrated "static template" ServerFixtures.staticTemplate ()
        // The server writes an empty text slot as a comment, which the client replaces with an empty text node.
        { hydrated "empty text" ServerFixtures.emptyText "" with
            Replaced = [ 2 ]
        }
        hydrated "clicking" ServerFixtures.clicking ServerFixtures.greetingProps
        hydrated "sugar explicit: benchmark row" SugarFixtures.benchExplicit 3
        hydrated "sugar: benchmark row" SugarFixtures.benchSugar 3
        hydrated "sugar explicit: free tags with t. parts" SugarFixtures.tagsExplicit ()
        hydrated "sugar: free tags with t. parts" SugarFixtures.tagsFree ()
    ]

/// <summary>A page with a title and a meta entry.</summary>
let private headed =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [||]
            [|
                t.Document.title "Page"
                t.Document.meta
                    [|
                        t.Attrs.attribute ("name", "description")
                        t.Attrs.attribute ("content", "d")
                    |]
                t.text "body"
            |])

// Every node under the host, in document order.
let private nodesUnder (root: Fable.Core.TS.Dom.Node) =
    let found = ResizeArray<obj>()

    let rec walk (node: Fable.Core.TS.Dom.Node) =
        let mutable child = node.firstChild

        while child.IsSome do
            let current = unbox<Fable.Core.TS.Dom.Node> child.Value
            found.Add (box current)
            walk current
            child <- current.nextSibling

    walk root
    List.ofSeq found

[<Tests>]
let tests =
    testList
        "Server parity"
        [
            for name, html in serverFixtures () do
                testCase $"{name}: Html.server of the parsed render is the render"
                <| fun () ->
                    use document = new TwinDocument ()
                    Expect.equal (Html.server (Html.parse document html)) html "identity"

            for fixture in hydratedFixtures do
                testCase $"{fixture.Name}: strict hydration reports nothing and keeps every server node"
                <| fun () ->
                    let mutable served = []

                    use session =
                        fixture.Hydrate (fixture.Page().Body) (fun host -> served <- nodesUnder host)

                    Expect.isEmpty session.Reports "no reports"
                    let kept = nodesUnder session.Host
                    Expect.equal kept.Length served.Length "as many nodes"

                    let replaced =
                        List.zip served kept
                        |> List.indexed
                        |> List.filter (fun (_, (a, b)) -> not (obj.ReferenceEquals (a, b)))
                        |> List.map fst

                    Expect.equal replaced fixture.Replaced "the server nodes kept"

            testCase "hydration over queued commits is not supported"
            <| fun () ->
                Expect.throwsT<System.NotSupportedException>
                    (fun () ->
                        Twin.hydrateHtml
                            { TwinMount.defaults with
                                Commits = Commits.Queued Ranvier.View.Dom.Scheduling.CollectAndContinue
                            }
                            Hydration.HydrationOptions.defaults
                            ""
                            ServerFixtures.clicking
                            ServerFixtures.greetingProps
                        |> ignore)
                    "queued"

            testCase "Twin.hydrate renders the page and hydrates it"
            <| fun () ->
                use session =
                    Twin.hydrate omit strict ServerFixtures.clicking ServerFixtures.greetingProps

                Expect.stringContains (session.Html ()) "<button" "rendered and kept"

            testCase "Twin.hydrate writes the page's head and adopts its entries"
            <| fun () ->
                let page = Server.renderPage omit headed ()

                use session = Twin.hydrate omit strict headed ()

                Expect.equal (Html.inner session.Document.Head) page.Head "the served head, kept"
        ]
