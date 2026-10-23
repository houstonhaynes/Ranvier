/// <summary>Strict hydration and graph ownership for shared page declarations.</summary>
module Ranvier.View.Dom.PageClient

open System
open Ranvier
open Ranvier.View
open Ranvier.View.Dom.Mounting

/// <summary>A hydrated page owning its mount and graph; Dispose releases both once, even if mount cleanup fails.</summary>
[<Sealed>]
type Started internal (graph: Graph, mounted: Mounted) =
    let mutable disposed = false

    /// <summary>The graph owning the page's reactive state.</summary>
    member _.Graph = graph

    /// <summary>The hydrated root; dispose Started to release both the root and its graph.</summary>
    member _.Mounted = mounted

    /// <summary>Disposes the mount and then its graph; repeated calls have no effect.</summary>
    member _.Dispose() =
        if not disposed then
            disposed <- true

            try
                mounted.Dispose ()
            finally
                graph.Dispose ()

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()

/// <summary>Hydrates the declared host strictly, using decoded state or the initial props factory when State is None.</summary>
/// <remarks>The caller owns the result. Hydration uses the declaration's graph options and id prefix.</remarks>
/// <exception cref="T:System.InvalidOperationException">The host or required state is absent, or the state decoder returns an error.</exception>
let start (definition: Page.Definition<'props>) : Started =
    let host = Native.elementById definition.Document.HostId

    if Native.isNullish host then
        invalidOp (
            "Page host is missing: "
            + definition.Document.HostId
        )

    let props =
        match definition.State with
        | None -> definition.InitialProps ()
        | Some codec ->
            match Hydration.readState codec.Id with
            | None -> invalidOp ("Page state is missing: " + codec.Id)
            | Some json ->
                match codec.Decode json with
                | Result.Ok props -> props
                | Result.Error error -> invalidOp ("Page state " + codec.Id + ": " + error)

    let graph = new Graph (definition.Options.GraphOptions)

    try
        let options: Hydration.HydrationOptions =
            {
                IdPrefix = definition.Options.IdPrefix
                Strict = true
            }

        let report (errors: exn list) =
            raise (AggregateException errors)

        let mounted =
            Hydration.hydrate graph None report options host definition.Component props

        new Started (graph, mounted)
    with _ ->
        graph.Dispose ()
        reraise ()
