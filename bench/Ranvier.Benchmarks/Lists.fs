module Ranvier.Benchmarks.Lists

open BenchmarkDotNet.Attributes
open Ranvier

type Item = { Id: int; Label: Signal<string> }

[<AllowNullLiteral>]
type Row(id: int) =
    member _.Id = id
    member val Text = "" with get, set

/// <summary>
/// The production row form over <c>Rows</c> items: <c>createProjectionOnce CreateFirst</c> keyed by id, one effect per row
/// writing its label, and, with <c>Layout</c>, one effect reading every row through <c>Keys</c>. Each benchmark alternates
/// between two states of equal cost, so no per-invocation setup is needed. Every benchmark runs two passes.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "ListPass">]
type ListPassBenchmarks() =
    let graph = new Graph ()
    let mutable items: Signal<Item array> = Unchecked.defaultof<_>
    let mutable projection: Projection<int, Row> = Unchecked.defaultof<_>
    let mutable full: Item array = [||]
    let mutable other: Item array = [||]
    let mutable sink = 0
    let mutable next = 1

    let build count =
        Array.init count (fun _ ->
            let id = next
            next <- next + 1

            {
                Id = id
                Label = Signal (graph, "row " + string id)
            })

    [<Params(1000, 10000)>]
    member val Rows = 1000 with get, set

    [<Params(true, false)>]
    member val Layout = true with get, set

    [<GlobalSetup>]
    member this.Setup() =
        use _ = graph.Activate ()
        items <- Signal (graph, [||])
        full <- build this.Rows
        other <- build this.Rows
        let layout = this.Layout

        createRoot (fun _ ->
            projection <-
                createProjectionOnce
                    ProjectionOptions.CreateFirst
                    (fun (item: Item) -> item.Id)
                    (fun item ->
                        let row = Row ((item ()).Id)
                        createEffect (fun () -> row.Text <- (item ()).Label.Value)
                        row)
                    (fun () -> items.Value :> seq<_>)

            createEffect (fun () ->
                let keys = projection.Keys

                if layout then
                    untrack (fun () ->
                        for key in keys do
                            sink <- sink + (projection.Get key).Id)))

        items.Value <- full

        if
            (untrack (fun () -> projection.Keys)).Length
            <> this.Rows
        then
            failwith "The projection did not build every row."

    /// <summary>Swaps the items at 1 and <c>Rows - 2</c>.</summary>
    [<Benchmark>]
    member _.Swap() =
        let swapped = Array.copy items.Peek
        let last = swapped.Length - 2
        let first = swapped[1]
        swapped[1] <- swapped[last]
        swapped[last] <- first
        items.Value <- swapped
        items.Value <- full

    /// <summary>Removes the item at 3, then restores it, which creates its row again.</summary>
    [<Benchmark>]
    member _.RemoveAndRestore() =
        items.Value <- Array.removeAt 3 full
        items.Value <- full

    /// <summary>Replaces the item at 500 with an equal-key copy, then restores the original.</summary>
    [<Benchmark>]
    member _.UpdateOneItem() =
        items.Value <- Array.updateAt 500 { full[500] with Id = full[500].Id } full
        items.Value <- full

    /// <summary>Replaces every row with a new set, then restores the original set.</summary>
    [<Benchmark>]
    member _.ReplaceAll() =
        items.Value <- other
        items.Value <- full

    /// <summary>Clears the list, then recreates every row.</summary>
    [<Benchmark>]
    member _.ClearAndCreate() =
        items.Value <- [||]
        items.Value <- full

    [<GlobalCleanup>]
    member _.Cleanup() =
        graph.Dispose ()
