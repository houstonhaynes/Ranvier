/// <summary>
/// Hydration benchmark fixtures: the component and keyed pages of <c>ServerFixtures</c>, served, hydrated and mounted.
/// </summary>
module HydrationPerf

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template

let private report (errors: exn list) =
    failwithf "%A" errors

let private strictOptions: Hydration.HydrationOptions =
    { IdPrefix = "r"; Strict = true }

/// <summary>The page of <c>fixture</c>: <c>"component"</c> or <c>"keyed"</c>.</summary>
let private page (fixture: string) : Component<int> =
    match fixture with
    | "component" -> ServerFixtures.componentPage
    | "keyed" -> ServerFixtures.keyedPage
    | _ -> invalidArg (nameof fixture) ("Unknown fixture " + fixture)

/// <summary>The server HTML of <c>fixture</c> at <c>rows</c> rows, rendered with the <c>Omit</c> policy.</summary>
let serverHtml (fixture: string) (rows: int) : string =
    Server.render
        { Server.Options.defaults with
            Policy = Omit
        }
        (page fixture)
        rows

let private hydrateWith (options: Hydration.HydrationOptions) (fixture: string) (host: HTMLElement) (rows: int) : unit -> unit =
    let graph = new Graph ()

    let mounted = Hydration.hydrate graph None report options host (page fixture) rows

    fun () ->
        mounted.Dispose ()
        graph.Dispose ()

/// <summary>
/// Hydrates the server HTML of <c>fixture</c> at <c>rows</c> rows held by <c>host</c> with <c>Strict</c> set, in a new
/// graph, and returns the disposal of the mount and its graph.
/// </summary>
let hydrateFixture (fixture: string) (host: HTMLElement) (rows: int) : unit -> unit =
    hydrateWith strictOptions fixture host rows

/// <summary>Hydrates as <c>hydrateFixture</c> does, with <c>Hydration.HydrationOptions.defaults</c>.</summary>
let hydrateFixtureLenient (fixture: string) (host: HTMLElement) (rows: int) : unit -> unit =
    hydrateWith Hydration.HydrationOptions.defaults fixture host rows

/// <summary>Mounts <c>fixture</c> at <c>rows</c> rows into <c>host</c>, in a new graph, and returns the disposal of the mount and its graph.</summary>
let mountFixture (fixture: string) (host: HTMLElement) (rows: int) : unit -> unit =
    let graph = new Graph ()

    let mounted =
        Mounting.mount graph None report host None (fun context -> Program.run (Component.render (page fixture) rows) context.Backend)

    fun () ->
        mounted.Dispose ()
        graph.Dispose ()
