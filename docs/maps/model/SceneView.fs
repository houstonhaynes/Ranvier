namespace Ranvier.Docs.Maps

#if RANVIER_TRACE
open System.Collections.Generic
open Ranvier

/// <summary>The geometry of a scene: where each drawn node, edge and collection box sits on the map.</summary>
[<RequireQualifiedAccess>]
module SceneView =
    /// <summary>A drawn node at its centre.</summary>
    type Node =
        {
            Id: int
            Place: Place
            Kind: TraceNodeKind
            X: float
            Y: float
            /// <summary>The text under the node.</summary>
            Caption: string
        }

    /// <summary>A drawn dependency; the edge from <c>Source</c> to <c>Target</c> is drawn once.</summary>
    type Edge =
        {
            Source: int
            Target: int
            /// <summary>The SVG path data, from the source's port to the observer's.</summary>
            Path: string
        }

    /// <summary>The box of an expanded collection, holding its node, rows and members.</summary>
    type Box =
        {
            Host: int
            X: float
            Y: float
            Width: float
            Height: float
        }

    /// <summary>The caption above a member column of a collection's box.</summary>
    type Column =
        {
            Host: int
            /// <summary>The column's position from the rows, counting from 0.</summary>
            Index: int
            X: float
            Y: float
            Text: string
        }

    /// <summary>
    /// A scene's geometry. Nodes are ordered by id, edges by observer and source, boxes by host. The same nodes,
    /// rows and edges always yield the same geometry.
    /// </summary>
    type Projection =
        {
            Nodes: Node list
            Edges: Edge list
            Boxes: Box list
            Columns: Column list
            /// <summary>The width of the SVG view box.</summary>
            Width: float
            /// <summary>The height of the SVG view box.</summary>
            Height: float
        }

    let private column = 150.0
    let private rowHeight = 78.0
    let private margin = 52.0
    /// The space between one layer's widest node and the next's.
    let private gap = 98.0
    /// The distance from a collection's node to its first row, and between rows.
    let private firstSlot = 64.0
    let private slot = 50.0
    /// Half a row's width.
    let private rowHalf = 38.0
    /// The distance between a box's member columns.
    let private memberGap = 44.0
    let private boxPad = 14.0

    /// <summary>The distance from a node's centre to where its edges attach.</summary>
    let half (place: Place) =
        match place with
        | Row _ -> rowHalf
        | Member _ -> 14.0
        | _ -> 22.0

    /// <summary>A cubic edge from <c>h1</c> right of a source's centre to <c>h2</c> left of an observer's.</summary>
    let edgePath (x1: float, y1: float) (h1: float) (x2: float, y2: float) (h2: float) =
        let x1 = x1 + h1
        let x2 = x2 - h2
        let mid = (x1 + x2) / 2.0
        $"M{x1} {y1} C{mid} {y1} {mid} {y2} {x2} {y2}"

    /// <summary>The geometry of <c>scene</c>'s live nodes: those laid out on their own, and expanded rows and members.</summary>
    let project (scene: Scene) : Projection =
        let snapshot = scene.Snapshot

        let top =
            snapshot.Nodes.Values
            |> Seq.filter (fun n ->
                MapModel.visible scene n
                && n.Status <> TraceNodeStatus.Disposed)
            |> Seq.map _.Id
            |> Set.ofSeq

        let boxed =
            top
            |> Seq.map (fun id -> id, MapModel.rows scene id)
            |> Seq.filter (snd >> List.isEmpty >> not)
            |> Map.ofSeq

        let live =
            boxed
            |> Map.fold
                (fun live _ rows ->
                    rows
                    |> List.fold (fun live (row, members) -> Set.union (live.Add row) (Set.ofList members)) live)
                top

        let outer id =
            MapModel.laidOutAs scene id

        let links =
            MapModel.edges scene
            |> List.filter (fun (s, o) ->
                live.Contains s
                && live.Contains o
                && not (outer s = outer o && (s = outer s || o = outer o)))

        let slotOf = Dictionary<int, int>()

        for KeyValue (_, rows) in boxed do
            rows
            |> List.iteri (fun i (row, members) ->
                for id in row :: members do
                    slotOf[id] <- i)

        let slotY i =
            firstSlot + float i * slot

        let sources =
            links
            |> List.choose (fun (s, o) ->
                let offset =
                    match slotOf.TryGetValue s with
                    | true, i -> slotY i / rowHeight
                    | _ -> 0.0

                if outer s = outer o then
                    None
                else
                    Some (outer o, (outer s, offset)))
            |> List.distinct
            |> List.groupBy fst
            |> List.map (fun (o, pairs) -> o, List.map snd pairs)
            |> Map.ofList

        let span id =
            match boxed.TryFind id with
            | Some rows -> int (ceil ((68.0 + slot * float rows.Length) / rowHeight))
            | None -> 1

        let memberReach rows =
            match
                rows
                |> List.map (snd >> List.length)
                |> List.fold max 0
            with
            | 0 -> 0.0
            | columns -> 48.0 + float (columns - 1) * memberGap

        let extent id =
            match boxed.TryFind id with
            | Some rows -> rowHalf + memberReach rows + boxPad, rowHalf + boxPad
            | None -> 26.0, 26.0

        let placed = Layout.placeSpanned span (Set.toList top) sources

        let layers = placed.Values |> Seq.map fst |> Seq.fold max 0

        let reach pick layer =
            placed
            |> Seq.filter (fun (KeyValue (_, (l, _))) -> l = layer)
            |> Seq.map (fun (KeyValue (id, _)) -> pick (extent id))
            |> Seq.fold max 26.0

        let xs = Array.zeroCreate (layers + 1)

        for l in 0..layers do
            xs[l] <-
                if l = 0 then
                    margin - 26.0 + reach fst 0
                else
                    xs[l - 1] + reach snd (l - 1) + gap + reach fst l

        let at = Dictionary<int, float * float>()

        let memberX x j =
            x - rowHalf - 30.0 - float j * memberGap

        let above id =
            if boxed.ContainsKey id then 44.0 else 30.0

        let below id =
            match boxed.TryFind id with
            | Some rows -> slotY (rows.Length - 1) + 22.0
            | None -> 40.0

        let inLayer layer =
            placed
            |> Seq.filter (fun p -> fst p.Value = layer)
            |> Seq.sortBy (fun p -> snd p.Value)
            |> Seq.map (fun p -> p.Key, snd p.Value)
            |> List.ofSeq

        let sourcePorts id =
            sources.TryFind id
            |> Option.defaultValue []
            |> List.choose (fun (s, offset) ->
                match at.TryGetValue s with
                | true, (_, y) -> Some (y + offset * rowHeight)
                | _ -> None)

        let observerPorts id =
            links
            |> List.filter (fun (s, o) -> outer s = id && outer o <> id)
            |> List.map (fun (s, o) ->
                let offset =
                    match slotOf.TryGetValue s with
                    | true, i -> slotY i
                    | _ -> 0.0

                snd at[o] - offset)

        // A layer keeps the layout's order; each node sits level with the mean of its ports, lowered as far as the
        // node above it requires.
        let settle layer (ports: int -> float list) =
            let mutable floor = margin - 30.0

            for id, row in inLayer layer do
                let wanted =
                    match ports id with
                    | [] -> margin + float row * rowHeight
                    | ys -> List.average ys

                let y = max wanted (floor + above id)
                let x = xs[layer]
                at[id] <- (x, y)
                floor <- y + below id + 12.0

                match boxed.TryFind id with
                | Some rows ->
                    rows
                    |> List.iteri (fun i (row, members) ->
                        let rowY = y + slotY i
                        at[row] <- (x, rowY)

                        members
                        |> List.iteri (fun j m -> at[m] <- (memberX x j, rowY)))
                | None -> ()

        for layer in 0..layers do
            settle layer sourcePorts

        // A layer of roots centres on the nodes it feeds.
        for layer in 0..layers do
            if
                inLayer layer
                |> List.forall (fst >> sourcePorts >> List.isEmpty)
            then
                settle layer observerPorts

        let bottom =
            placed
            |> Seq.map (fun (KeyValue (id, _)) ->
                let _, y = at[id]

                match boxed.TryFind id with
                | Some rows -> y + slotY (rows.Length - 1)
                | None -> y)
            |> Seq.fold max 0.0

        let nodes =
            at
            |> Seq.map (fun (KeyValue (id, (x, y))) ->
                {
                    Id = id
                    Place = MapModel.placeOf scene id
                    Kind = snapshot.Nodes[id].Kind
                    X = x
                    Y = y
                    Caption = MapModel.caption scene id
                })
            |> Seq.sortBy _.Id
            |> List.ofSeq

        let boxes, columns =
            boxed
            |> Map.toList
            |> List.map (fun (host, rows) ->
                let x, y = at[host]
                let left, right = extent host

                let box =
                    {
                        Host = host
                        X = x - left
                        Y = y - 44.0
                        Width = left + right
                        Height = 44.0 + slotY (rows.Length - 1) + 22.0
                    }

                let columns =
                    match rows with
                    | (_, members) :: _ ->
                        members
                        |> List.mapi (fun j m ->
                            {
                                Host = host
                                Index = j
                                X = memberX x j
                                Y = y + slotY 0 - 22.0
                                Text = MapModel.caption scene m
                            })
                    | [] -> []

                box, columns)
            |> List.unzip

        let edges =
            [
                for s, o in links do
                    // An edge leaving a box from a member starts at the member's row.
                    let port =
                        match MapModel.placeOf scene s with
                        | Member (h, row) when outer o <> h -> row
                        | _ -> s

                    {
                        Source = s
                        Target = o
                        Path = edgePath at[port] (half (MapModel.placeOf scene port)) at[o] (half (MapModel.placeOf scene o))
                    }
            ]

        {
            Nodes = nodes
            Edges = edges
            Boxes = boxes
            Columns = List.concat columns
            Width = xs[layers] + reach snd layers + margin - 26.0
            Height = bottom + margin
        }
#endif
