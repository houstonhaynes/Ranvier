module Docs.Site

open System.IO
open Feliz.ViewEngine
open Nacara.Core
open Nacara.Plugins
open Partas.Nacara.Theme

let versions = [ SiteVersion.root "1.0" ]

/// <summary>The public API of the assemblies built beside the site.</summary>
let apiOptions =
    let beside =
        System.Reflection.Assembly.GetExecutingAssembly().Location
        |> Path.GetDirectoryName

    { FSharpApi.defaults with
        Root = "reference"
        Title = "API reference"
        Sources =
            [
                for name in [ "Ranvier"; "Ranvier.CSharp"; "Ranvier.Elmish"; "Ranvier.Query" ] ->
                    FSharpApiSource.create (Path.Combine (beside, $"%s{name}.dll"))
            ]
    }

let theme =
    Theme.theme (
        Theme.navbar
            [
                NavbarSection ("Guide", "guide", "/guide/")
                NavbarDropdown (
                    "Build",
                    [
                        NavbarSection ("Views", "view", "/view/")
                        NavbarSection ("Server", "server", "/server/")
                        NavbarSection ("Fable", "fable", "/fable/")
                    ]
                )
                NavbarSection ("Examples", "examples", "/examples/")
                NavbarDropdown (
                    "Reference",
                    [
                        NavbarSection ("API reference", "reference", "/reference/")
                        NavbarSection ("Analyzers", "analyzers", "/analyzers/")
                    ]
                )
                NavbarDropdown (
                    "Learn",
                    [
                        NavbarSection ("Concepts", "concepts", "/concepts/")
                        NavbarSection ("Blog", "blog", "/blog/")
                        NavbarSection ("Benchmarks", "benchmarks", "/benchmarks/")
                    ]
                )
            ]
        >> Theme.menu
            "guide"
            [
                Menu.section
                    "Introduction"
                    [
                        Menu.page "guide/index.md"
                        Menu.page "guide/installation.md"
                        Menu.page "guide/getting-started.md"
                        Menu.page "guide/csharp.md"
                        Menu.page "guide/blazor-server.md"
                        Menu.page "guide/examples.md"
                    ]
                Menu.section
                    "Core"
                    [
                        Menu.section
                            "Core concepts"
                            [
                                Menu.page "guide/graph.fsx"
                                Menu.page "guide/signals.fsx"
                                Menu.page "guide/memos.md"
                                Menu.page "guide/timing.md"
                                Menu.page "guide/effects.md"
                                Menu.page "guide/bindings.md"
                                Menu.page "guide/roots.md"
                                Menu.page "guide/cleanup.md"
                                Menu.page "guide/untrack.md"
                                Menu.page "guide/batch.md"
                                Menu.page "guide/editable.md"
                                Menu.page "guide/drafts.md"
                                Menu.page "guide/equality.md"
                            ]
                        Menu.section
                            "Async and pending"
                            [
                                Menu.page "guide/async-and-pending.md"
                                Menu.page "guide/pending.md"
                                Menu.page "guide/async-sources.md"
                                Menu.page "guide/async-memos.md"
                                Menu.page "guide/streams.md"
                                Menu.page "guide/boundaries.md"
                                Menu.page "guide/threading.md"
                                Menu.page "guide/testing.md"
                            ]
                        Menu.section
                            "Collections"
                            [
                                Menu.page "guide/collections.fsx"
                                Menu.page "guide/projections.fsx"
                                Menu.page "guide/lookups.fsx"
                                Menu.page "guide/selectors.fsx"
                                Menu.page "guide/collection-updates.fsx"
                                Menu.page "guide/collection-views.fsx"
                                Menu.page "guide/aggregates.fsx"
                            ]
                        Menu.section "Recipes" [ Menu.page "guide/forms.md" ]
                    ]
                Menu.section
                    "Extensions"
                    [
                        Menu.section "Ranvier.Query" [ Menu.page "guide/queries.md"; Menu.page "guide/query-navigation.md" ]
                        Menu.section "Ranvier.Elmish" [ Menu.page "guide/elmish.md" ]
                        Menu.section "Ranvier.Mcp" [ Menu.page "guide/mcp.md" ]
                    ]
                Menu.section
                    "Reference"
                    [
                        Menu.page "guide/tracing.md"
                        Menu.page "guide/signal-maps.md"
                        Menu.page "guide/troubleshooting.md"
                    ]
            ]
        >> Theme.menu
            "view"
            [
                Menu.section
                    "Introduction"
                    [
                        Menu.page "view/index.md"
                        Menu.page "view/getting-started.md"
                        Menu.page "examples/index.md"
                    ]
                Menu.section
                    "Authoring"
                    [
                        Menu.page "view/templates.md"
                        Menu.page "view/components.md"
                        Menu.page "view/collections.md"
                        Menu.page "view/arms.md"
                        Menu.page "view/boundaries.md"
                        Menu.page "view/events.md"
                        Menu.page "view/document-head.md"
                        Menu.page "view/sugar.md"
                        Menu.page "view/authored.md"
                    ]
                Menu.section
                    "Motion"
                    [
                        Menu.page "view/motion.md"
                        Menu.page "view/motion-reactive.md"
                        Menu.page "view/motion-parts.md"
                        Menu.page "view/motion-testing.md"
                    ]
                Menu.section
                    "Running views"
                    [
                        Menu.page "view/lifetimes.md"
                        Menu.page "view/elmish.md"
                        Menu.page "view/testing.md"
                    ]
                Menu.section "Reference" [ Menu.page "view/troubleshooting.md"; Menu.page "view/reference.md" ]
            ]
        >> Theme.menu
            "server"
            [
                Menu.section
                    "Server"
                    [
                        Menu.page "server/index.md"
                        Menu.page "server/rendering.md"
                        Menu.page "server/hydration.md"
                        Menu.page "server/pages.md"
                        Menu.page "server/backends.md"
                        Menu.page "examples/index.md"
                        Menu.page "server/state-transfer.md"
                        Menu.page "server/deployment.md"
                        Menu.page "server/performance.md"
                    ]
            ]
        >> Theme.menu
            "concepts"
            [
                Menu.section
                    "Concepts"
                    [
                        Menu.page "concepts/index.md"
                        Menu.page "concepts/suspension.md"
                        Menu.page "concepts/async-graph.md"
                        Menu.page "concepts/contracts.md"
                        Menu.page "concepts/ecosystem.md"
                        Menu.page "concepts/roadmap.md"
                    ]
            ]
        >> Theme.menu
            "benchmarks"
            [
                Menu.section
                    "Benchmarks"
                    [
                        Menu.page "benchmarks/index.md"
                        Menu.page "benchmarks/signals.md"
                        Menu.page "benchmarks/memos.md"
                        Menu.page "benchmarks/effects.md"
                        Menu.page "benchmarks/lifetimes.md"
                        Menu.page "benchmarks/projections.md"
                        Menu.page "benchmarks/suspension.md"
                        Menu.page "benchmarks/streams.md"
                        Menu.page "benchmarks/counters.md"
                    ]
                Menu.section "Ranvier.Query" [ Menu.page "benchmarks/queries.md" ]
            ]
        >> Theme.menu "examples" [ Menu.section "Browser examples" [ Menu.page "examples/index.md" ] ]
        >> Theme.menu "fable" [ Menu.section "Fable" [ Menu.page "fable/index.md" ] ]
        >> Theme.menu
            "analyzers"
            [
                Menu.section
                    "Analyzers"
                    [
                        Menu.page "analyzers/index.md"
                        Menu.page "analyzers/ran001.md"
                        Menu.page "analyzers/ran002.md"
                        Menu.page "analyzers/ran010.md"
                        Menu.page "analyzers/ran011.md"
                        Menu.page "analyzers/ran012.md"
                        Menu.page "analyzers/ran013.md"
                        Menu.page "analyzers/ran015.md"
                        Menu.page "analyzers/ran020.md"
                        Menu.page "analyzers/ran021.md"
                        Menu.page "analyzers/ran022.md"
                        Menu.page "analyzers/ran023.md"
                        Menu.page "analyzers/ran024.md"
                        Menu.page "analyzers/ran025.md"
                    ]
            ]
        >> Theme.menu
            "blog"
            [
                Menu.section "Blog" [ Menu.page "blog/index.md"; Menu.page "blog/desktop-adaptive.md" ]
            ]
        >> Theme.navbarEnd
            [
                NavbarIcon ("GitHub", "https://github.com/shayanhabibi/Ranvier", Icons.github)
            ]
        >> Theme.editUrl "https://github.com/shayanhabibi/Ranvier/edit/master/docs"
        >> Theme.footer (
            Html.p
                [
                    Html.text "Ranvier · Preview · "
                    Html.a [ prop.href "https://github.com/shayanhabibi/Ranvier"; prop.text "GitHub" ]
                    Html.text " · Built with Nacara"
                ]
        )
    )

/// Live Partas.Solid components on the pages (the landing page's animated state mark), compiled
/// against the Partas.Solid 3 build committed under feed/. The generated project inherits
/// docs/nuget.config, whose partas-local source serves it.
let private solidExamples (options: SolidExamplesOptions) =
    options
    |> SolidExamples.partasVersion "3.0.0-local.e08ad85"
    |> SolidExamples.npm "animejs" "4.5.0"
    |> SolidExamples.project "../src/Fable.Ranvier.Anime/Fable.Ranvier.Anime.fsproj"
    |> SolidExamples.property "RanvierTrace" "true"
    |> SolidExamples.targetFramework "net10.0"

/// The og card of each section, keyed by the path segment that names it, with the card's alt text.
let private ogCards =
    [
        "async-and-pending", "async", "Async and pending: boundary states along a nerve fibre"
        "tracing", "tracing", "Tracing: a cause chain traced back along a nerve fibre"
        "signal-maps", "signal-maps", "Signal maps: a traced graph drawn live, a write travelling its edges"
        "timing", "timing", "Debounce and throttle: capture changes now, admit them after quiet or at a fixed deadline"
        "guide", "guide", "Ranvier guide: build with signals"
        "concepts", "concepts", "Ranvier concepts: how the graph works"
        "fable", "fable", "Ranvier on Fable: the JavaScript target"
        "benchmarks", "benchmarks", "Ranvier benchmarks: measured per commit"
    ]

/// The card of the most specific section named by a page's path.
let private sectionCard (page: Page) =
    let segments =
        page.Id.Split ([| '/'; '\\'; ':' |])
        |> Array.map (fun s -> s.Replace (".md", ""))

    ogCards
    |> List.tryFind (fun (segment, _, _) -> Array.contains segment segments)
    |> Option.map (fun (_, card, alt) ->
        OgImage.image $"/og/%s{card}.png"
        |> OgImage.withAlt alt
        |> OgImage.withSize 1200 630)

let private ogImages (options: OgImageOptions) =
    options
    |> OgImage.fallback sectionCard
    |> OgImage.defaultImage (
        OgImage.image "/og/home.png"
        |> OgImage.withAlt "Ranvier: fine-grained reactive computation for .NET"
        |> OgImage.withSize 1200 630
    )

/// The shipped C# grammar (<c>c_sharp</c>, after its wasm's <c>tree_sitter_c_sharp</c> export), also written with
/// <c>csharp</c>, <c>cs</c> or <c>c#</c> fences.
let private csharpGrammar (options: TreeSitterOptions) =
    let grammar = TreeSitter.bundled "c_sharp"

    options
    |> TreeSitter.grammars (
        TreeSitter.aliases [ "csharp"; "cs"; "c#" ] grammar
        :: options.Grammars
    )

/// <summary>Copies the built browser examples into the documentation output.</summary>
let private browserExamples =
    Site.plugin
        { new IPlugin with
            member _.Name = "browser-examples"

            member _.Configure registry =
                [
                    "Ranvier.Motion", "motion"
                    "Fable.Ranvier.Playground", "playground"
                    "Ranvier.Hydration", "hydration"
                ]
                |> List.fold
                    (fun assets (project, route) ->
                        let dist =
                            Path.Combine (__SOURCE_DIRECTORY__, $"../examples/{project}/dist")
                            |> Path.GetFullPath

                        assets
                        |> Registry.asset (Asset.CopyDirectory (AbsolutePath.create dist, RelativePath.create $"examples/{route}")))
                    registry
        }

let reference =
    FSharpApi.collection "reference" DocFrontMatter.decoder apiOptions
    |> Collection.title _.Title
    |> Collection.layout (Theme.layout theme)

let site =
    Site.create "Ranvier"
    |> Site.baseUrl Spec.baseUrl
    |> Site.origin "https://shayanhabibi.github.io"
    |> Site.output "output"
    |> Site.staticFiles "static"
    |> Markdown.register
    |> Literate.registerWith (fun options -> { options with Extensions = [ ".fsx" ] })
    |> TreeSitter.registerWith csharpGrammar
    |> Docs.Directives.register
    |> Sitemap.register
    |> FSharpApi.register apiOptions
    |> LinkValidator.register
    |> browserExamples
    |> SolidExamples.registerWith solidExamples
    |> LiveExample.registerWith (
        LiveExample.preset (
            LiveExamplePreset.create "maps"
            |> LiveExamplePreset.project "maps/Ranvier.Docs.Maps.fsproj"
            |> LiveExamplePreset.property "RanvierTrace" "true"
            |> LiveExamplePreset.css "../brand/tokens/ranvier-brand-tokens.css"
            |> LiveExamplePreset.css "theme/maps.css"
            |> LiveExamplePreset.template "live/snippet.html"
            |> LiveExamplePreset.asDefault
        )
    )
    |> OgImage.registerWith ogImages
    |> AgentFriendly.registerWith (
        AgentFriendly.summary "Fine-grained reactive computation for .NET, with a Fable target"
        >> AgentFriendly.details
            "Start with the guide. Views covers the experimental template components, Server their server rendering and hydration; Concepts explains the engine's model; Benchmarks holds measured results."
    )
    |> LightningCss.register
    |> Esbuild.register
    |> Maps.components
    |> Nuglify.minifyHtml
    |> Maps.hydrate
    |> Versions.register versions
    |> Theme.register theme
    |> Site.collection (Theme.docs theme "content")
    |> Site.collection reference

[<EntryPoint>]
let main argv =
    Nacara.run site argv
