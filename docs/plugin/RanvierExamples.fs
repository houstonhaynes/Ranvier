/// <summary>
/// The <c>map</c> fence: a plain F# scenario rewritten into a live or replayed signal map. The <c>ranvier</c> fence: a
/// Ranvier.View component. Both are rendered on the server and hydrated in the page.
/// </summary>
module Nacara.Plugins.RanvierExamples

open System.Collections.Generic
open Nacara.Core
open Nacara.Plugins
open Nacara.Plugins.Internal
open Ranvier.Docs.Maps.Authoring

/// <summary>The project that provides component types and the repository used by generated builds.</summary>
type Options =
    {
        Project: string
        Repository: string
        Packages: (string * string) list
    }

/// <summary>Uses a project relative to the Nacara root, with its parent as the repository.</summary>
let defaults =
    {
        Project = "maps/Ranvier.Docs.Maps.fsproj"
        Repository = ".."
        Packages = []
    }

/// <summary>What the fence transform leaves on a page for the compile hook.</summary>
let private dataKey = "ranvier-maps"

/// <summary>Where the bundles are written, relative to the output directory.</summary>
let private outputPath = "_ranvier/maps"

/// <summary>The map flags, server HTML and bundle URLs of the last compile.</summary>
type private Compiled() =
    member val Flags: Map<string * string, MapFlags> = Map.empty with get, set
    member val Fragments: Map<string, string> = Map.empty with get, set
    member val Scripts: Map<string, string> = Map.empty with get, set
    member val Messages: WorkspaceMessage list = [] with get, set

let private transformPage (context: TransformContext) (page: Page) =
    let hash =
        System.Security.Cryptography.SHA256.HashData (System.Text.Encoding.UTF8.GetBytes (string page.Id))

    let key =
        "p"
        + (System.Convert.ToHexString hash).Substring(0, 10).ToLowerInvariant()

    let scan = FenceScanner.scan key page.Body

    for line, problem in scan.Problems do
        let diagnostic = Diagnostic.error "fence" problem

        context.Diagnostics.Add (
            match page.SourceFile with
            | Some file ->
                diagnostic
                |> Diagnostic.at file (page.BodyLine + line - 1) 1
            | None -> diagnostic
        )

    if scan.Cells.IsEmpty then
        page
    else
        let unit =
            MapPage.unit key (page.SourceFile |> Option.map AbsolutePath.value) page.BodyLine scan.Cells

        { page with Body = scan.Body }.WithData(dataKey, unit)

let private report (context: HookContext) (message: WorkspaceMessage) =
    // Under watch an error is a warning, and the broken cell's host stays empty.
    let diagnostic =
        if message.IsError && not context.IsWatch then
            Diagnostic.error "compile" message.Text
        else
            Diagnostic.warning "compile" message.Text

    let diagnostic =
        match message.Page with
        | Some {
                   Source = Some source
                   BodyLine = bodyLine
               } ->
            let file = AbsolutePath.create source

            match message.At with
            | Some (line, column) ->
                diagnostic
                |> Diagnostic.at file (bodyLine + line - 1) column
            | None -> diagnostic |> Diagnostic.inFile file
        | _ -> diagnostic

    context.Diagnostics.Add diagnostic

let private compileAll (options: Options) (compiled: Compiled) (context: HookContext) =
    let units =
        context.Pages
        |> List.choose (fun page -> page.TryData<PageUnit> dataKey)
        |> List.filter (fun unit -> not (MapPage.rendered unit).IsEmpty)
        |> List.sortBy _.Key

    compiled.Fragments <- Map.empty
    compiled.Scripts <- Map.empty
    compiled.Messages <- []

    compiled.Flags <- MapPage.flags units

    if not units.IsEmpty then
        match EsbuildBinary.resolve () with
        | Error problem -> context.Diagnostics.Add (Diagnostic.error "esbuild" problem)
        | Ok esbuild ->
            let started = System.Diagnostics.Stopwatch.StartNew ()

            let root = AbsolutePath.value context.ProjectRoot

            let built =
                MapWorkspace.build
                    {
                        Workspace = System.IO.Path.Combine (root, ".nacara", "ranvier-maps")
                        MapsProject = System.IO.Path.GetFullPath (System.IO.Path.Combine (root, options.Project))
                        Repository = System.IO.Path.GetFullPath (System.IO.Path.Combine (root, options.Repository))
                        Esbuild = esbuild
                        Minify = not context.IsWatch
                        Packages = options.Packages
                        Log = fun line -> Log.info $"ranvier maps: %s{line}"
                    }
                    units

            // Reported once the pages are written: an error here would stop Nacara rendering every page.
            compiled.Messages <- built.Messages

            for name, text in built.Files do
                context.Write $"%s{outputPath}/%s{name}" text
                |> ignore

            // A failed page's hosts show its failure card and load no bundle.
            compiled.Fragments <- built.Fragments

            compiled.Scripts <-
                units
                |> List.filter (fun unit -> not (built.Failed.Contains unit.Key))
                |> List.map (fun unit -> unit.Key, context.Site.UrlOfAsset $"%s{outputPath}/%s{unit.Key}.js")
                |> Map.ofList

            if started.ElapsedMilliseconds > 1000L then
                Log.info $"ranvier maps: %d{units.Length} page(s) in %d{started.ElapsedMilliseconds}ms"

/// <summary>Registers server-rendered component and map fences, compilation diagnostics and client hydration.</summary>
/// <remarks>Register after HTML minification to preserve hydration markers. Build state belongs to this registration.</remarks>
let registerWith (options: Options) =
    let compiled = Compiled ()

    Site.plugin
        { new IPlugin with
            member _.Name = "ranvier-maps"

            member _.Configure registry =
                let registry =
                    registry
                    |> Registry.transform
                        {
                            Name = "ranvier-maps"
                            Extensions = [ ".md"; ".markdown" ]
                            Transform = transformPage
                        }

                // Rewrite fences before Markdown turns them into highlighted code.
                let transforms =
                    List.last registry.Transforms
                    :: List.take (registry.Transforms.Length - 1) registry.Transforms

                { registry with
                    Transforms = transforms
                }
                |> Registry.onPagesRouted (compileAll options compiled)
                |> Registry.onBuildComplete (fun context -> compiled.Messages |> List.iter (report context))
                |> Registry.assetTransform
                    {
                        Name = "map-code"
                        Extensions = [ ".html" ]
                        Transform =
                            fun context ->
                                if context.Content.Contains "rv-map-card" then
                                    MapCode.rewrite (fun page cell -> Map.tryFind (page, cell) compiled.Flags) context.Content
                                else
                                    context.Content
                    }
                |> Registry.assetTransform
                    {
                        Name = "ranvier-maps-hydrate"
                        Extensions = [ ".html" ]
                        Transform =
                            fun context ->
                                if context.Content.Contains "rvm-p" then
                                    MapPage.fill
                                        (fun host -> Map.tryFind host compiled.Fragments)
                                        (fun key -> Map.tryFind key compiled.Scripts)
                                        context.Content
                                else
                                    context.Content
                    }
        }

/// <summary>Registers component and map fences with the default project layout.</summary>
let register site =
    registerWith defaults site
