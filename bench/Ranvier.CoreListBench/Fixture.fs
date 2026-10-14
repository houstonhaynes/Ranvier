/// <summary>
/// A keyed list on the production projection path (<c>createProjectionOnce</c> with <c>CreateFirst</c>), with no DOM.
/// Each row owns one effect writing its item's label to the row, as a row's text binding does; one effect reads
/// <c>Keys</c> and every row untracked, as layout does. <c>run.mjs</c> runs the same workload on Solid 2's
/// <c>mapArray</c>.
/// </summary>
module Ranvier.CoreListBench.Fixture

open System
open Ranvier

type Item = { Id: int; Label: Signal<string> }

[<AllowNullLiteral>]
type Row(id: int) =
    member _.Id = id
    member val Text = "" with get, set

let mutable private sink = 0

let private buildRow (item: unit -> Item) : Row =
    let row = Row ((item ()).Id)
    Api.createEffect (fun () -> row.Text <- (item ()).Label.Value)
    row

/// <summary>A mounted list: the item source, its projection and the layout effect.</summary>
type Fixture() =
    let graph = new Graph ()
    let items = graph.Run (fun () -> Api.createSignal ([||]: Item array))
    let mutable next = 1
    let mutable projection: Projection<int, Row> = Unchecked.defaultof<_>

    let build count =
        Array.init count (fun _ ->
            let id = next
            next <- next + 1

            {
                Id = id
                Label = Api.createSignal ("row " + string id)
            })

    do
        graph.Run (fun () ->
            Api.createRoot (fun _ ->
                projection <-
                    Api.createProjectionOnce ProjectionOptions.CreateFirst (fun (item: Item) -> item.Id) buildRow (fun () -> items.Value :> seq<_>)

                Api.createEffect (fun () ->
                    let keys = projection.Keys

                    Api.untrack (fun () ->
                        for key in keys do
                            sink <- sink + (projection.Get key).Id))))

    member _.Create(count: int) =
        graph.Run (fun () -> items.Value <- build count)

    member _.Append(count: int) =
        graph.Run (fun () -> items.Value <- Array.append items.Value (build count))

    member _.UpdateEvery10th() =
        graph.Run (fun () ->
            Api.batch (fun () ->
                let current = items.Value
                let mutable index = 0

                while index < current.Length do
                    current[index].Label.Value <- current[index].Label.Value + " !!!"
                    index <- index + 10))

    member _.Swap() =
        graph.Run (fun () ->
            let swapped = Array.copy items.Value
            let last = swapped.Length - 2
            let first = swapped[1]
            swapped[1] <- swapped[last]
            swapped[last] <- first
            items.Value <- swapped)

    member _.Remove(index: int) =
        graph.Run (fun () -> items.Value <- Array.removeAt index items.Value)

    member _.Clear() =
        graph.Run (fun () -> items.Value <- [||])

    /// <summary>The rows in order, as <c>id:text</c> joined by commas.</summary>
    member _.Snapshot() =
        graph.Run (fun () ->
            Api.untrack (fun () ->
                projection.Keys
                |> Seq.map (fun key ->
                    let row = projection.Get key
                    string row.Id + ":" + row.Text)
                |> String.concat ","))

    member _.Dispose() =
        graph.Dispose ()

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()

let sinkValue () = sink
