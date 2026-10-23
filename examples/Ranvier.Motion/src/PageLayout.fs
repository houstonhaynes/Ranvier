/// <summary>Shared page declarations for the server generator and generated browser entries.</summary>
module PageLayout

open global.Ranvier
open Ranvier.View
open Ranvier.View.Sugar

type private Info =
    {
        Name: string
        Title: string
        Tag: string
        Description: string
    }

let private infos =
    [|
        {
            Name = "kanban"
            Title = "Kanban"
            Tag = "Elmish · presence · flip · drag"
            Description =
                "Cards enter and leave with presence, glide between lanes with FLIP, and drop into the lane you drag them to. Every change goes through one Elmish loop, so undo is a list of earlier boards."
        }
        {
            Name = "dashboard"
            Title = "Dashboard"
            Tag = "stream · follow · draw · morph · track · boundary"
            Description =
                "Metrics arrive from a core stream. A skeleton pulses until the first sample, gauges follow each reading, the ring draws the CPU load, the chart morphs to every new window, and the request rate counts up through a tracked tween."
        }
        {
            Name = "story"
            Title = "Story"
            Tag = "scroll · split · stagger · appear · SSR"
            Description = "A scroll-driven landing page rendered by the .NET server and hydrated strictly in the browser."
        }
        {
            Name = "physics"
            Title = "Physics"
            Tag = "drag · spring · follow · scrub · policy"
            Description =
                "Throw the puck and a spring settles it; five followers trail it, each a step slower. The slider seeks a timeline through Reactive.scrub, and the switch writes the graph's motion policy."
        }
        {
            Name = "routes"
            Title = "Routes"
            Tag = "Elmish · hash routing · matchWith · presence · flip"
            Description =
                "An Elmish shell routes on the location hash. Every page is an arm that crosses over with presence, pinned in place while it leaves, and runs its own loop; the shell's layout and its tab indicator follow each change."
        }
    |]

let private info name =
    infos
    |> Array.find (fun entry -> entry.Name = name)

let private navigation (prefix: string) (current: string) : TemplateView<'instance> =
    header (Attrs.className "site-header") {
        a (Attrs.className "brand", Attrs.attribute ("href", prefix)) {
            span (Attrs.className "brand-mark") { () }
            "Ranvier Motion"
        }

        nav () {
            for entry in infos do
                a (
                    Attrs.attribute ("href", prefix + entry.Name + "/"),
                    Attrs.attribute ("aria-current", fun _ -> if current = entry.Name then Some "page" else None)
                ) {
                    entry.Title
                }
        }
    }

let private metadata (t: TemplateBuilder<'instance>) (title: string) (description: string) =
    t.fragment
        [|
            t.Document.title title
            t.Document.meta [| t.Attrs.attribute ("charset", "utf-8") |]
            t.Document.meta
                [|
                    t.Attrs.attribute ("name", "viewport")
                    t.Attrs.attribute ("content", "width=device-width, initial-scale=1")
                |]
            t.Document.meta
                [|
                    t.Attrs.attribute ("name", "description")
                    t.Attrs.attribute ("content", description)
                |]
        |]

let private framed (entry: Info) (content: TemplateView<'instance>) : TemplateView<'instance> =
    div (Attrs.className "frame") {
        navigation "../" entry.Name

        section (Attrs.className "intro") {
            span (Attrs.className "tag") { entry.Tag }
            h1 () { entry.Title }
            p () { entry.Description }
        }

        content
    }

/// <summary>Creates each page's reactive state inside its render scope and owns its browser setup.</summary>
let pageView name create view activate =
    let entry = info name

    Component.define
        (fun _ props ->
            let state = create props
            let nested = view state

            let selected =
                Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.render nested (fun () -> ()))

            state, selected)
        (fun t ->
            let content =
                div (
                    Hooks.mounted (fun (state, _) _ ->
                        let graph = Graph.Current
                        global.Page.afterRender (fun () -> activate graph state))
                ) {
                    t.matchWith snd (fun selected -> t.arm selected ())
                }

            t.fragment
                [|
                    metadata t (entry.Title + " · Ranvier Motion") entry.Description
                    if name = "story" then content else framed entry content
                |])

/// <summary>Declares the shared initial data, shell, styles and generated browser entry of an example.</summary>
let declaration name initial definition codec : Ranvier.View.Page.Definition<_> =
    let entry = info name

    {
        Component = definition
        InitialProps = initial
        Options =
            { Server.Options.defaults with
                Policy = Omit
            }
        Document =
            { Server.DocumentShell.defaults with
                Lang = Some "en"
                Stylesheets = [ "../styles/motion.css"; "./" + name + ".css" ]
                Scripts =
                    [
                        Server.ModuleScript.Src (
                            "/generated/generated-pages/"
                            + entry.Title
                            + ".js"
                        )
                    ]
            }
        State = codec
    }

/// <summary>The static landing page linking the five examples.</summary>
let index: Ranvier.View.Page.Definition<unit> =
    {
        Component =
            Component.define (fun _ () -> ()) (fun t ->
                div (Attrs.className "frame") {
                    metadata t "Ranvier Motion" "Five pages animated with anime.js through Ranvier's Motion parts."
                    navigation "./" ""

                    section (Attrs.className "hero") {
                        span (Attrs.className "tag") { "anime.js 4.5 · Ranvier view sugar" }
                        h1 () { "Motion that belongs to the view." }

                        p () {
                            "Each page below is an F# component tree, rendered on .NET and hydrated in the browser. Its animations are created when an element is placed, reverted when its owner is disposed, driven by signals and published back as signals."
                        }
                    }

                    ul (Attrs.className "pages") {
                        for entry in infos do
                            li () {
                                a (Attrs.attribute ("href", "./" + entry.Name + "/")) {
                                    span (Attrs.className ("swatch " + entry.Name)) { () }
                                    h2 () { entry.Title }
                                    p () { entry.Description }
                                    span (Attrs.className "parts") { entry.Tag }
                                }
                            }
                    }
                })
        InitialProps = fun () -> ()
        Options = Server.Options.defaults
        Document =
            { Server.DocumentShell.defaults with
                Lang = Some "en"
                Stylesheets = [ "./styles/motion.css"; "./index.css" ]
            }
        State = None
    }
