/// <summary>
/// The <c>map</c> fence: a plain F# scenario rewritten into a live or replayed signal map. The <c>ranvier</c> fence: a
/// Ranvier.View component. Both are rendered on the server and hydrated in the page.
/// </summary>
module Docs.Maps

open System.Collections.Generic
open Nacara.Core
open Nacara.Plugins
open Nacara.Plugins.Internal
open Ranvier.Docs.Maps.Authoring

let private spans (spans: MapSpan list) : SolidLineSpan list =
    spans
    |> List.map (fun s ->
        {
            Generated = s.Generated
            Length = s.Length
            Body = s.Body
            Indent = s.Indent
        })

/// <summary>The word that marks an <c>fsharp</c> fence as a Ranvier component.</summary>
let private fenceToken = "ranvier"

/// <summary>The word that marks an <c>fsharp</c> fence as a signal map scenario.</summary>
let private mapToken = "map"

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

let private cellOf (maps: Dictionary<string, MapFenceOutput * MapFlags>) (cell: SolidCell) : Result<PageCell, string> =
    let kind =
        match cell.Kind with
        | SolidCellKind.Transformed _ ->
            match maps.TryGetValue cell.Id with
            | true, (output, flags) -> Ok (PageCellKind.Map (output, flags))
            | _ -> Error "a transformed fence has no map"
        | SolidCellKind.Expression -> Ok PageCellKind.Expression
        | SolidCellKind.Declarations (Some name) -> Ok (PageCellKind.Render name)
        | SolidCellKind.Declarations None
        | SolidCellKind.Setup -> Ok PageCellKind.Declarations
        | SolidCellKind.Use _ -> Error "a ranvier component renders from a fence, not from a code span"

    kind
    |> Result.map (fun kind ->
        {
            Id = cell.Id
            Kind = kind
            Code = cell.Code
            Line = cell.Line
        })

let private validId = System.Text.RegularExpressions.Regex "^[A-Za-z0-9_-]+$"

/// <summary>Rewrites a page's component fences into cards and hosts, and keeps the page's module on the page.</summary>
let private transformPage (context: TransformContext) (page: Page) =
    if
        not (page.Body.Contains fenceToken)
        && not (page.Body.Contains mapToken)
    then
        page
    else
        let key = SolidGenerate.pageKey page.Id
        let maps = Dictionary<string, MapFenceOutput * MapFlags>()

        // Compiles a map fence's scenario and keeps its output and flags for the cell.
        let transform (input: SolidTransformInput) =
            match MapFlags.parse input.Flags, MapFence.compile input.CellId input.Flags input.Code with
            | Ok flags, Ok output ->
                maps[input.CellId] <- (output, flags)

                SolidTransformOutput.Compiled
                    {
                        Code = output.Code
                        Render = output.Render
                        Spans = spans output.Spans
                    }
            | _, Error problems -> SolidTransformOutput.Rejected problems
            | Error problem, _ -> SolidTransformOutput.Rejected [ 0, problem ]

        let scan = SolidScan.scanWith [ mapToken, transform ] fenceToken false key page.Body

        if scan.Cells.IsEmpty then
            page
        else
            let report line problem =
                let diagnostic = Diagnostic.error "fence" problem

                match page.SourceFile with
                | Some file ->
                    context.Diagnostics.Add (
                        diagnostic
                        |> Diagnostic.at file (page.BodyLine + line - 1) 1
                    )
                | None -> context.Diagnostics.Add diagnostic

            for line, problem in scan.Problems do
                report line problem

            let names = HashSet<string>()

            let cells =
                scan.Cells
                |> List.choose (fun cell ->
                    if not (validId.IsMatch cell.Id) then
                        report (cell.Line - 1) $"id=%s{cell.Id} must hold only letters, digits, '_' and '-'"
                        None
                    elif not (names.Add (MapPage.componentName cell.Id)) then
                        report (cell.Line - 1) $"id=%s{cell.Id} names the same generated module value as an earlier cell; '-' reads as '_'"
                        None
                    else
                        match cellOf maps cell with
                        | Ok cell -> Some cell
                        | Error problem ->
                            report (cell.Line - 1) problem
                            None)

            let unit =
                MapPage.unit key (page.SourceFile |> Option.map AbsolutePath.value) page.BodyLine cells

            { page with
                Body = MapPage.hosts scan.Body
            }
                .WithData(dataKey, unit)

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

let private compileAll (compiled: Compiled) (context: HookContext) =
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
                        MapsProject = System.IO.Path.Combine (root, "maps", "Ranvier.Docs.Maps.fsproj")
                        Repository = System.IO.Path.GetFullPath (System.IO.Path.Combine (root, ".."))
                        Esbuild = esbuild
                        Minify = not context.IsWatch
                        Packages = [ "animejs", "4.5.0" ]
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

let private compiled = Compiled ()

/// <summary>
/// Compiles <c>fsharp map</c> and <c>fsharp ranvier</c> fences into server-rendered, hydrated components, and wraps each
/// map card's code in a native disclosure before HTML minification.
/// </summary>
/// <remarks>Register <c>hydrate</c> after HTML minification.</remarks>
let components =
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

                // First in line: the fences are rewritten before the markdown renderer and the Solid scanner run.
                let transforms =
                    List.last registry.Transforms
                    :: List.take (registry.Transforms.Length - 1) registry.Transforms

                { registry with
                    Transforms = transforms
                }
                |> Registry.onPagesRouted (compileAll compiled)
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
        }

/// <summary>Fills each map host with its server HTML and adds the page's bundle, after HTML minification.</summary>
let hydrate =
    Site.plugin
        { new IPlugin with
            member _.Name = "ranvier-maps-hydrate"

            member _.Configure registry =
                registry
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
