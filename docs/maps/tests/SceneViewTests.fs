module Ranvier.Docs.Maps.Tests.SceneViewTests

open Expecto

#if RANVIER_TRACE
open Ranvier
open Ranvier.Docs.Maps

let private sceneOf (start: Scene) (g: Graph) =
    match MapModel.frames start (Trace.events g) with
    | [||] -> start
    | frames -> (Array.last frames).After

let private finite (value: float) =
    not (
        System.Double.IsNaN value
        || System.Double.IsInfinity value
    )

let private nodeAt (projection: SceneView.Projection) id =
    projection.Nodes |> List.find (fun n -> n.Id = id)

let private id (node: INode) = node.Id

let private overlaps (a: SceneView.Box) (b: SceneView.Box) =
    a.X < b.X + b.Width
    && b.X < a.X + a.Width
    && a.Y < b.Y + b.Height
    && b.Y < a.Y + a.Height

/// A projection with a factory node per key, and a reader of each key.
let private quoted (g: Graph) (name: string) (keys: string list) =
    let desk = Desk<int>(true)
    let lines = createSignal keys
    Trace.label (g, lines, name + " lines")

    let rows =
        createProjectionWith
            (fun (key: string) -> key)
            (fun item ->
                let quote = createAsync (fun _ _ -> desk.Quote (String.length (item ())))
                Trace.label (g, quote, "quote")
                fun () -> quote.Value)
            (fun () -> lines.Value :> seq<_>)

    Trace.label (g, rows, name)

    for key in keys do
        createEffect (fun () -> rows.Get key |> ignore)

    rows

[<Tests>]
let tests =
    testList
        "SceneView"
        [
            test "an empty scene projects nothing within finite bounds" {
                let projection = SceneView.project MapModel.start
                Expect.isEmpty projection.Nodes "no nodes"
                Expect.isEmpty projection.Edges "no edges"
                Expect.isEmpty projection.Boxes "no boxes"
                Expect.isTrue (finite projection.Width && projection.Width > 0.0) "a finite width"

                Expect.isTrue
                    (finite projection.Height
                     && projection.Height > 0.0)
                    "a finite height"
            }

            test "a dependency chain lays out left to right with an edge per dependency" {
                use g = new Graph ()
                use _ = g.Activate ()
                let count = createSignal 1
                Trace.label (g, count, "count")
                let doubled = createMemo (fun _ -> count.Value * 2)
                Trace.label (g, doubled, "doubled")
                createEffect (fun () -> doubled.Value |> ignore)

                let projection = SceneView.project (sceneOf MapModel.start g)
                Expect.equal projection.Nodes.Length 3 "three nodes"

                let source = nodeAt projection (id count)
                let memo = nodeAt projection (id doubled)
                Expect.equal source.Caption "count" "the signal's caption"
                Expect.isLessThan source.X memo.X "the source sits left of its observer"

                Expect.contains
                    (projection.Edges
                     |> List.map (fun e -> e.Source, e.Target))
                    (id count, id doubled)
                    "the dependency's edge"

                for edge in projection.Edges do
                    Expect.stringStarts edge.Path "M" "a path"

                for node in projection.Nodes do
                    Expect.isTrue (finite node.X && finite node.Y) "a finite position"
                    Expect.isLessThanOrEqual node.X projection.Width "inside the width"
                    Expect.isLessThanOrEqual node.Y projection.Height "inside the height"

                count.Value <- 3
                let later = SceneView.project (sceneOf MapModel.start g)

                Expect.equal
                    (later.Nodes |> List.map (fun n -> n.Id, n.X, n.Y))
                    (projection.Nodes
                     |> List.map (fun n -> n.Id, n.X, n.Y))
                    "a write moves no node"
            }

            test "an expanded collection boxes its rows, labels its member columns and ports members through their rows" {
                use g = new Graph ()
                use _ = g.Activate ()
                let rows = quoted g "rows" [ "tea"; "jam" ]
                let projection = SceneView.project (sceneOf MapModel.start g)
                let host = id rows

                let box = projection.Boxes |> List.exactlyOne

                Expect.equal box.Host host "the collection's box"

                let rowNodes =
                    projection.Nodes
                    |> List.filter (fun n ->
                        match n.Place with
                        | Row _ -> true
                        | _ -> false)

                Expect.equal (rowNodes |> List.map _.Caption) [ "tea"; "jam" ] "a row per key, captioned by its key"

                for node in projection.Nodes do
                    match node.Place with
                    | Row h
                    | Member (h, _) when h = host ->
                        Expect.isTrue (node.X >= box.X && node.X <= box.X + box.Width) "inside the box horizontally"
                        Expect.isTrue (node.Y >= box.Y && node.Y <= box.Y + box.Height) "inside the box vertically"
                    | _ -> ()

                Expect.equal (projection.Columns |> List.map _.Text) [ "quote" ] "one column label per member"

                let members =
                    projection.Nodes
                    |> List.filter (fun n ->
                        match n.Place with
                        | Member _ -> true
                        | _ -> false)

                Expect.equal members.Length 2 "a quote per key"

                for m in members do
                    match m.Place with
                    | Member (_, row) ->
                        let rowNode = nodeAt projection row

                        let edge =
                            projection.Edges
                            |> List.find (fun e -> e.Source = m.Id && e.Target = row)

                        Expect.stringStarts edge.Path $"M{m.X + SceneView.half m.Place} {m.Y}" "an edge inside the box starts at its member"
                        Expect.equal rowNode.Y m.Y "a member sits level with its row"
                    | _ -> ()
            }

            test "a member's edge to a node outside its box leaves from the member's row" {
                use g = new Graph ()
                use _ = g.Activate ()
                let lines = createSignal [ "tea" ]

                let rows =
                    createProjectionWith
                        (fun (key: string) -> key)
                        (fun item ->
                            let size = createMemo (fun _ -> String.length (item ()))
                            Trace.label (g, size, "size")
                            fun () -> size.Value)
                        (fun () -> lines.Value :> seq<_>)

                Trace.label (g, rows, "rows")
                createEffect (fun () -> rows.Get "tea" |> ignore)
                let projection = SceneView.project (sceneOf MapModel.start g)

                let outside =
                    projection.Edges
                    |> List.filter (fun e ->
                        match (nodeAt projection e.Source).Place with
                        | Member _ -> (nodeAt projection e.Target).Place = Top
                        | _ -> false)

                for edge in outside do
                    match (nodeAt projection edge.Source).Place with
                    | Member (_, row) ->
                        let rowNode = nodeAt projection row
                        Expect.stringStarts edge.Path $"M{rowNode.X + SceneView.half rowNode.Place} {rowNode.Y}" "the row's port"
                    | _ -> ()
            }

            test "two collections' boxes do not overlap" {
                use g = new Graph ()
                use _ = g.Activate ()
                quoted g "first" [ "tea"; "jam"; "egg" ] |> ignore
                quoted g "second" [ "milk"; "rye" ] |> ignore
                let projection = SceneView.project (sceneOf MapModel.start g)
                Expect.equal projection.Boxes.Length 2 "two boxes"

                match projection.Boxes with
                | [ a; b ] -> Expect.isFalse (overlaps a b) "separate bounds"
                | _ -> ()
            }

            test "a collapsed collection draws as one node without a box" {
                use g = new Graph ()
                use _ = g.Activate ()
                quoted g "rows" [ "tea" ] |> ignore

                let collapse =
                    { MapModel.start with
                        Grouping = Collapse
                    }

                let projection = SceneView.project (sceneOf collapse g)
                Expect.isEmpty projection.Boxes "no box"

                Expect.isTrue
                    (projection.Nodes
                     |> List.forall (fun n -> n.Place = Top))
                    "every node laid out on its own"
            }

            test "a disposed node leaves the projection" {
                use g = new Graph ()
                use _ = g.Activate ()
                let count = createSignal 1
                let mutable inner = Unchecked.defaultof<Memo<int>>

                let owner =
                    createRoot (fun owner ->
                        inner <- createMemo (fun _ -> count.Value + 1)
                        createEffect (fun () -> inner.Value |> ignore)
                        owner)

                let before = SceneView.project (sceneOf MapModel.start g)

                Expect.isTrue
                    (before.Nodes
                     |> List.exists (fun n -> n.Id = id inner))
                    "drawn while live"

                (owner :> System.IDisposable).Dispose()
                let after = SceneView.project (sceneOf MapModel.start g)

                Expect.isFalse
                    (after.Nodes
                     |> List.exists (fun n -> n.Id = id inner))
                    "gone once disposed"

                Expect.isFalse
                    (after.Edges
                     |> List.exists (fun e -> e.Source = id inner || e.Target = id inner))
                    "its edges go with it"
            }
        ]
#else
[<Tests>]
let tests = testList "SceneView" []
#endif
