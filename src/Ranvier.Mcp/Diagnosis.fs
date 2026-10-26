namespace Ranvier.Mcp

#if RANVIER_TRACE
open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.RegularExpressions
open Ranvier

/// <summary>Source lines by trace site (<c>File.fs:line</c>), from the F# and C# files under a root directory.</summary>
/// <remarks>A site whose file name matches several files under the root resolves to no line.</remarks>
[<Sealed>]
type internal SourceLines(root: string) =
    static let skipped =
        set [ "bin"; "obj"; ".git"; ".vs"; ".idea"; "node_modules"; "packages" ]

    static let binding =
        Regex (@"^\s*(?:let|and)\s+(?:(?:mutable|private|internal|inline|rec)\s+)*([A-Za-z_][A-Za-z0-9_']*)", RegexOptions.CultureInvariant)

    let files =
        lazy
            (let files =
                Dictionary<string, ResizeArray<string>>(StringComparer.OrdinalIgnoreCase)

             let rec walk (directory: string) =
                 try
                     for file in Directory.EnumerateFiles directory do
                         match Path.GetExtension file with
                         | ".fs"
                         | ".fsx"
                         | ".fsi"
                         | ".cs" ->
                             let name = Path.GetFileName file

                             match files.TryGetValue name with
                             | true, paths -> paths.Add file
                             | _ -> files[name] <- ResizeArray [ file ]
                         | _ -> ()

                     for child in Directory.EnumerateDirectories directory do
                         if not (skipped.Contains (Path.GetFileName child)) then
                             walk child
                 with _ ->
                     ()

             walk root
             files)

    let texts = Dictionary<string, struct (DateTime * string[])>()

    let read (path: string) =
        let stamp = File.GetLastWriteTimeUtc path

        match texts.TryGetValue path with
        | true, struct (taken, text) when taken = stamp -> text
        | _ ->
            let text = File.ReadAllLines path
            texts[path] <- struct (stamp, text)
            text

    let lines (site: string) =
        let colon = site.LastIndexOf ':'

        if colon <= 0 then
            None
        else
            match Int32.TryParse (site.Substring (colon + 1)) with
            | true, line when line > 0 ->
                match files.Value.TryGetValue (site.Substring (0, colon)) with
                | true, paths when paths.Count = 1 ->
                    try
                        let text = read paths[0]

                        if line <= text.Length then Some (text, line - 1) else None
                    with _ ->
                        None
                | _ -> None
            | _ -> None

    let indent (line: string) =
        line.Length - line.TrimStart().Length

    /// <summary>The trimmed text of the site's line.</summary>
    member _.Line(site: string) : string option =
        lines site
        |> Option.map (fun (text, index) -> text[index].Trim())

    /// <summary>
    /// The name bound by a <c>let</c> on the site's line, or by the nearest <c>let</c> less indented than the site
    /// within the 4 lines above it.
    /// </summary>
    member _.Binding(site: string) : string option =
        match lines site with
        | None -> None
        | Some (text, index) ->
            let own = binding.Match text[index]

            if own.Success then
                Some own.Groups[1].Value
            else
                let depth = indent text[index]

                seq { index - 1 .. -1 .. max 0 (index - 4) }
                |> Seq.filter (fun i ->
                    text[i].Trim().Length > 0
                    && indent text[i] < depth)
                |> Seq.tryHead
                |> Option.bind (fun i ->
                    let m = binding.Match text[i]
                    if m.Success then Some m.Groups[1].Value else None)

/// <summary>
/// Compact text answers over a graph's trace log: <c>Explain</c>, <c>Overview</c> and <c>Neighbourhood</c>. Each node
/// reads as <c>name#id</c>, its name taken from <c>Expose.node</c>, a trace label, or the <c>let</c> binding at its
/// creation site.
/// </summary>
/// <remarks>Construct on the graph thread; the instance reads the log once, at construction.</remarks>
[<Sealed>]
type internal Diagnosis(graph: Graph, exposed: KeyValuePair<string, INode> list, journal: DispatchRecord list, sources: SourceLines) =
    let snap = Trace.snapshot graph
    let events = Trace.events graph
    let checkpoint = (Trace.checkpointOf graph).Name

    let exposedById =
        exposed
        |> List.map (fun (KeyValue (name, node)) -> node.Id, name)
        |> Map.ofList

    let inode =
        exposed
        |> List.map (fun (KeyValue (_, node)) -> node.Id, node)
        |> Map.ofList

    let names = Dictionary<int, string>()

    let clip (limit: int) (text: string) =
        if text.Length <= limit then
            text
        else
            text.Substring (0, limit - 1) + "…"

    let name (id: int) =
        match names.TryGetValue id with
        | true, known -> known
        | _ ->
            let found =
                match exposedById.TryFind id with
                | Some name -> name
                | None ->
                    match snap.Nodes.TryFind id with
                    | Some node ->
                        match node.Label with
                        | Some label -> label
                        | None ->
                            sources.Binding node.Site
                            |> Option.defaultWith (fun () -> (string node.Kind).ToLowerInvariant())
                    | None -> "node"

            names[id] <- found
            found

    let tag (id: int) =
        $"{name id}#{id}"

    let value (id: int) =
        match snap.Nodes.TryFind id with
        | Some node when node.Value.IsSome -> clip 60 node.Value.Value
        | _ -> "?"

    /// A step's value, as ` to text` when the text is short enough to read in a chain.
    let stepValue (value: obj option) =
        match value with
        | Some v ->
            let text = TraceModel.valueText v
            if text.Length <= 30 then $" {text}" else ""
        | None -> ""

    let sourcesOf (id: int) =
        snap.Sources.TryFind id |> Option.defaultValue []

    let observersOf (id: int) =
        snap.Observers.TryFind id
        |> Option.map Set.toList
        |> Option.defaultValue []

    let live (id: int) =
        match snap.Nodes.TryFind id with
        | Some node -> node.Status <> TraceNodeStatus.Disposed
        | None -> false

    /// The nodes within `hops` steps of `id` along `next`, with their distance, nearest first; `id` excluded.
    let reach (next: int -> int list) (hops: int) (id: int) =
        let seen = Dictionary<int, int>()
        seen[id] <- 0
        let order = ResizeArray<int>()
        let mutable frontier = [ id ]
        let mutable depth = 0

        while not frontier.IsEmpty && depth < hops do
            depth <- depth + 1

            frontier <-
                [
                    for node in frontier do
                        for other in next node do
                            if not (seen.ContainsKey other) then
                                seen[other] <- depth
                                order.Add other
                                other
                ]

        order
        |> Seq.map (fun node -> node, seen[node])
        |> List.ofSeq

    let tags (ids: int list) (limit: int) =
        let shown = ids |> List.truncate limit |> List.map tag
        let more = ids.Length - shown.Length

        String.Join (", ", shown)
        + (if more > 0 then $", +{more} more" else "")

    let messageAt (seq: int) =
        journal
        |> List.filter (fun e -> e.Seq < seq)
        |> List.tryLast

    /// The signals written by a dispatched message: the models of the exposed Mvus.
    let models =
        let written = HashSet<int>()

        match journal with
        | first :: _ ->
            for e in events do
                if
                    e.Seq > first.Seq
                    && e.Kind = TraceEventKind.Write
                    && e.Flag = 1
                then
                    written.Add e.Node |> ignore
        | [] -> ()

        written

    /// A node reading a dispatched model alone, such as <c>Mvu.Select</c>: it keeps its value by design when another
    /// part of the model moves.
    let selection (id: int) =
        match sourcesOf id with
        | [ source ] -> models.Contains source
        | _ -> false

    let messageText (entry: DispatchRecord) =
        $"message {entry.Index} {entry.Message} {clip 60 entry.Args} (v{entry.Version})"

    let byMessage (seq: int) =
        match messageAt seq with
        | Some entry -> $", by {messageText entry}"
        | None -> ""

    let why (id: int) =
        match inode.TryFind id with
        | Some node -> Trace.why graph node
        | None -> TraceModel.why events checkpoint id 0

    let whyNot (id: int) =
        match inode.TryFind id with
        | Some node -> Trace.whyNot graph node
        | None -> TraceModel.whyNot events id

    let event (seq: int) =
        events |> Array.tryFind (fun e -> e.Seq = seq)

    /// The cause chain of a node's last run, oldest first: the root, then each write and move.
    let chain (id: int) =
        let w = why id

        let root =
            match w.Root with
            | Some (WhyRoot.UserWrite write) ->
                match messageAt write with
                | Some entry -> messageText entry
                | None -> $"a write at #{write} from outside every run"
            | Some (WhyRoot.Created _) -> "its first run, at creation"
            | Some (WhyRoot.Pulled reader) -> $"a read by {tag reader}"
            | Some (WhyRoot.Settled settle) -> $"an async settle at #{settle}"
            | Some (WhyRoot.BeforeCheckpoint _) -> "a cause before the log's checkpoint"
            | Some (WhyRoot.Unrecorded last) -> $"an unrecorded cause before #{last}"
            | None -> "a cause beyond the chain's depth"

        let steps =
            w.Steps
            |> List.rev
            |> List.choose (fun step ->
                match step.Kind with
                | TraceEventKind.Write -> Some $"{tag step.Node} written{stepValue step.Value}"
                | TraceEventKind.Moved when step.Node <> id -> Some $"{tag step.Node} moved{stepValue step.Value}"
                | TraceEventKind.Settle -> Some $"{tag step.Node} settled{stepValue step.Value}"
                | _ -> None)

        String.Join (" → ", root :: steps @ [ $"{name id} ran" ])

    let reason (id: int) =
        match whyNot id with
        | None -> "not marked; no source it reads has moved"
        | Some (WhyNotReason.CheckedClean (at, upstream)) -> $"checked clean at #{at}: its sources [{tags upstream 6}] did not move"
        | Some (WhyNotReason.NotReached stopAt) ->
            match event stopAt with
            | Some e when e.Kind = TraceEventKind.Write -> $"propagation stopped at #{stopAt}: {tag e.Node} was written with an equal value"
            | Some e when e.Kind = TraceEventKind.RunEnd -> $"propagation stopped at #{stopAt}: {tag e.Node} re-ran and its value did not move"
            | _ -> $"propagation stopped upstream at #{stopAt}"
        | Some (WhyNotReason.Unobserved mark) -> $"marked at #{mark}, and nothing observes or reads it since"
        | Some (WhyNotReason.Queued schedule) -> $"queued at #{schedule}; its run has not started (a batch is open or the flush is pending)"
        | Some (WhyNotReason.Disposed seq) -> $"disposed at #{seq}"
        | Some (WhyNotReason.SkippedAsRunningReader seq) -> $"skipped at #{seq} as the running reader of its own source"

    /// Each node's latest move or moving write after `after`, as (node, seq, value).
    let movesAfter (after: int) =
        let latest = Dictionary<int, struct (int * obj)>()

        for e in events do
            if
                e.Seq > after
                && ((e.Kind = TraceEventKind.Write && e.Flag = 1)
                    || e.Kind = TraceEventKind.Moved)
            then
                latest[e.Node] <- struct (e.Seq, e.Payload)

        latest

    /// The first run start after `after` of `id` whose run ended without moving, as its RunStart seq.
    let unmovedRunAfter (after: int) (id: int) =
        let mutable start = 0
        let mutable found = None

        for e in events do
            if e.Seq > after && e.Node = id && found.IsNone then
                if e.Kind = TraceEventKind.RunStart then
                    start <- e.Seq
                elif
                    e.Kind = TraceEventKind.RunEnd
                    && e.Flag = 0
                    && start > 0
                then
                    found <- Some start

        found

    /// The latest run of `id` up to `until` that ended without moving after one of its sources moved, as
    /// (RunStart seq, source, the source's move seq, its value).
    let lastCutoff (until: int) (id: int) =
        let reads = HashSet<int>(sourcesOf id)
        let mutable moved = None
        let mutable start = 0
        let mutable found = None

        for e in events do
            if e.Seq <= until then
                if
                    reads.Contains e.Node
                    && ((e.Kind = TraceEventKind.Write && e.Flag = 1)
                        || e.Kind = TraceEventKind.Moved)
                then
                    moved <- Some (e.Node, e.Seq, e.Payload)
                elif e.Node = id && e.Kind = TraceEventKind.RunStart then
                    start <- e.Seq
                elif e.Node = id && e.Kind = TraceEventKind.RunEnd then
                    match moved with
                    | Some (s, seq, payload) when e.Flag = 0 && seq < start -> found <- Some (start, s, seq, payload)
                    | _ -> ()

                    moved <- None

        found

    let siteLine (id: int) =
        match snap.Nodes.TryFind id with
        | Some node ->
            match sources.Line node.Site with
            | Some text -> $"    {node.Site}: {clip 110 text}"
            | None -> ""
        | None -> ""

    let summary (id: int) =
        let node = snap.Nodes[id]
        let reads = sourcesOf id

        let readsText = if reads.IsEmpty then "" else $" reads [{tags reads 6}]"

        let valueText =
            match node.Value with
            | Some _ -> $" = {value id}"
            | None -> ""

        $"{tag id} {node.Kind} {node.Site}{valueText}{readsText}"

    /// <summary>The node a request names: an exposed name, <c>#id</c> or an id, a node name, or an identity path.</summary>
    member _.Resolve(text: string) : Result<int, string> =
        let text = text.Trim ()

        let byId =
            match Int32.TryParse (text.TrimStart '#') with
            | true, id when snap.Nodes.ContainsKey id -> Some id
            | _ -> None

        let byExposed =
            exposed
            |> List.tryFind (fun (KeyValue (n, _)) -> n = text)
            |> Option.map (fun (KeyValue (_, node)) -> node.Id)

        match byExposed |> Option.orElse byId with
        | Some id -> Result.Ok id
        | None ->
            let named =
                snap.Nodes
                |> Seq.filter (fun (KeyValue (id, _)) -> live id && name id = text)
                |> Seq.map _.Key
                |> List.ofSeq

            match named with
            | [ id ] -> Result.Ok id
            | _ :: _ -> Result.Error $"'{text}' names several nodes: {tags named 8}. Pass one as #id."
            | [] ->
                match Trace.resolve graph text with
                | Some id -> Result.Ok id
                | None ->
                    let known = String.Join (", ", exposed |> List.map _.Key)
                    Result.Error $"No node '{text}'. Exposed nodes: {known}; or pass #id, a name from 'graph', or an identity path."

    /// <summary>
    /// Why the node holds its value: its creation site and line, the sources it reads, the cause chain of its last run
    /// back to the dispatched message, why it has not re-run since, the upstream nodes that re-ran without moving, and
    /// the related nodes that changed after its last run that it does not read.
    /// </summary>
    member _.Explain(id: int) : string =
        let out = StringBuilder ()

        let line (text: string) =
            if text <> "" then
                out.AppendLine text |> ignore

        let node = snap.Nodes[id]
        line (summary id)
        line (siteLine id)

        if node.Runs = 0 then
            line $"{name id} has not run."
        else
            line $"last ran at #{node.LastRun} (run {node.Runs}), caused by: {chain id}"
            let after = node.LastRun
            line $"since then: {reason id}"

            let upstream = reach sourcesOf 8 id |> List.map fst
            let upstreamSet = HashSet<int> upstream
            let downstreamSet = HashSet<int>(reach observersOf 8 id |> List.map fst)
            let moves = movesAfter after

            // Upstream nodes that re-ran after the node's last run and kept their value; selections are left out.
            let cutoffs =
                upstream
                |> List.filter (selection >> not)
                |> List.choose (fun u ->
                    unmovedRunAfter after u
                    |> Option.bind (fun start ->
                        sourcesOf u
                        |> List.tryPick (fun s ->
                            match moves.TryGetValue s with
                            | true, struct (seq, payload) when seq < start -> Some (u, start, s, seq, payload)
                            | _ -> None)))
                |> List.truncate 3

            // Upstream nodes that moved after the node's last run while the node did not re-run.
            let stale =
                upstream
                |> List.filter (fun u ->
                    moves.ContainsKey u
                    && (sourcesOf id |> List.contains u))
                |> List.truncate 3

            for u in stale do
                let struct (seq, _) = moves[u]
                line $"stale: {tag u}, which {name id} reads, moved at #{seq} after {name id}'s last run, and {name id} has not re-run."

            // Changed nodes outside the node's sources that share an ancestor with it, or are exposed.
            let ancestors (n: int) =
                reach sourcesOf 8 n |> List.map fst

            let related (n: int) =
                exposedById.ContainsKey n
                || ancestors n |> List.exists upstreamSet.Contains

            let unread =
                moves
                |> Seq.map (fun (KeyValue (n, struct (seq, payload))) -> n, seq, payload)
                |> Seq.filter (fun (n, _, _) ->
                    n <> id
                    && live n
                    && not (upstreamSet.Contains n)
                    && not (downstreamSet.Contains n)
                    && (match snap.Nodes[n].Kind with
                        | TraceNodeKind.Effect
                        | TraceNodeKind.Binding -> false
                        | _ -> true)
                    && related n)
                |> Seq.sortBy (fun (n, seq, _) -> (if exposedById.ContainsKey n then 0 else 1), -seq)
                |> List.ofSeq

            for n, seq, payload in List.truncate 3 unread do
                line
                    $"unread: {tag n} moved to {clip 40 (TraceModel.valueText payload)} at #{seq}{byMessage seq}; {name id} does not read it (its sources: [{tags (sourcesOf id) 6}])."

                line (siteLine n)

            if unread.Length > 3 then
                line $"unread: +{unread.Length - 3} more changed nodes."

            // Without a cutoff since the last run, the latest earlier ones: a defect seen before the last message.
            let earlier =
                if cutoffs.IsEmpty then
                    upstream
                    |> List.filter (selection >> not)
                    |> List.choose (fun u ->
                        lastCutoff after u
                        |> Option.map (fun (start, s, seq, payload) -> u, start, s, seq, payload))
                    |> List.truncate 2
                else
                    []

            for u, start, s, seq, payload in earlier do
                line
                    $"earlier cutoff: {tag u} re-ran at #{start} after {tag s} moved to {clip 40 (TraceModel.valueText payload)} (#{seq}{byMessage seq}), and kept its value: its equality or comparer called the new result equal."

                line (siteLine u)

            // With an unread change, the cutoffs share one line.
            if unread.IsEmpty then
                for u, start, s, seq, payload in cutoffs do
                    line
                        $"cutoff: {tag u} re-ran at #{start} after {tag s} moved to {clip 40 (TraceModel.valueText payload)} (#{seq}{byMessage seq}), and kept its value {value u}: its equality or comparer called the new result equal."

                    line (siteLine u)
            elif not cutoffs.IsEmpty then
                let kept =
                    cutoffs
                    |> List.map (fun (u, _, s, _, _) -> $"{tag u} after {tag s}")
                    |> fun parts -> String.Join (", ", parts)

                line $"also kept their value after a source moved: {kept}."

        out.ToString().TrimEnd()

    /// <summary>The graph at a glance: node counts by kind, the exposed nodes, and the nodes the last message moved.</summary>
    member _.Overview(limit: int) : string =
        let out = StringBuilder ()

        let line (text: string) =
            out.AppendLine text |> ignore

        let liveNodes =
            snap.Nodes
            |> Seq.filter (fun (KeyValue (id, _)) -> live id)
            |> List.ofSeq

        let counts =
            liveNodes
            |> List.countBy (fun (KeyValue (_, node)) -> string node.Kind)
            |> List.map (fun (kind, count) -> $"{count} {kind}")
            |> fun parts -> String.Join (", ", parts)

        line $"{liveNodes.Length} live nodes ({counts}), log seq #{snap.Seq}, {journal.Length} messages dispatched."

        line "exposed:"

        for KeyValue (_, node) in exposed do
            if snap.Nodes.ContainsKey node.Id then
                line ("  " + summary node.Id)

        match List.tryLast journal with
        | Some entry ->
            let moved =
                movesAfter entry.Seq
                |> Seq.map _.Key
                |> Seq.filter (fun n -> not (exposedById.ContainsKey n))
                |> List.ofSeq

            if not moved.IsEmpty then
                line $"moved by {messageText entry}: {tags moved 12}"
        | None -> ()

        line "Ask explain {node} why a node holds its value, or node {node, around} for its neighbours."
        let text = out.ToString().TrimEnd()

        if text.Length <= limit then
            text
        else
            text.Substring(0, limit - 80).TrimEnd()
            + "\n… cut at the size limit; ask node {node} for one node."

    /// <summary>
    /// The node and the nodes within <c>around</c> hops along its sources and observers, one line each, from line
    /// <c>start</c>; at most <c>limit</c> lines and <c>budget</c> characters. <c>full</c> adds observers, runs, status and
    /// the creation line.
    /// </summary>
    member _.Neighbourhood(id: int, around: int, full: bool, start: int, limit: int, budget: int) : string =
        let neighbours =
            let next n =
                sourcesOf n @ observersOf n

            (id, 0) :: reach next around id

        let upstream = HashSet<int>(reach sourcesOf around id |> List.map fst)
        let downstream = HashSet<int>(reach observersOf around id |> List.map fst)

        let describe (n: int, distance: int) =
            let node = snap.Nodes[n]
            let hops = if distance = 1 then "1 hop" else $"{distance} hops"

            let relation =
                if distance = 0 then ""
                elif upstream.Contains n then $" [source, {hops}]"
                elif downstream.Contains n then $" [observer, {hops}]"
                else $" [{hops}]"

            let head = summary n + relation

            if full || distance = 0 then
                let observed = observersOf n

                let observedText =
                    if observed.IsEmpty then
                        ""
                    else
                        $" observed by [{tags observed 6}]"

                let detail =
                    $"\n    {node.Status}, {node.Runs} runs, last run #{node.LastRun}{observedText}"

                let source = siteLine n

                head
                + detail
                + (if source = "" then "" else "\n" + source)
            else
                head

        let out = StringBuilder ()
        let mutable index = start
        let mutable shown = 0
        let mutable full' = false

        while index < neighbours.Length
              && shown < limit
              && not full' do
            let text = describe neighbours[index]

            if out.Length + text.Length + 1 > budget - 160 then
                full' <- true
            else
                out.AppendLine text |> ignore
                index <- index + 1
                shown <- shown + 1

        if index < neighbours.Length then
            out.AppendLine $"… {neighbours.Length - index} more. Continue with cursor \"{index}\", or narrow with a smaller 'around'."
            |> ignore

        out.ToString().TrimEnd()
#endif
