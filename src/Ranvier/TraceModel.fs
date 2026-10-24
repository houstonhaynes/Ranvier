namespace Ranvier

open System
open System.Collections.Generic
open System.Text

/// <summary>A payload value of one of the four types every trace schema writes losslessly.</summary>
[<RequireQualifiedAccess>]
type TraceScalar =
    | Int of int
    | String of string
    | Bool of bool
    | Float of float

/// <summary>A recorded value in a trace contract document or a schema 3 dump, marked as lossless or lossy.</summary>
[<RequireQualifiedAccess>]
type TracePayload =
    | Null
    /// <summary>An <c>int</c>, <c>string</c>, <c>bool</c> or <c>float</c>, held exactly.</summary>
    | Exact of TraceScalar
    /// <summary>The <c>data</c> written by version <c>version</c> of the codec named <c>codec</c>; lossless.</summary>
    | Encoded of codec: string * version: int * data: string
    /// <summary>The value's <c>TraceModel.valueText</c>: diagnostic text that will not decode to a value.</summary>
    | Lossy of text: string

/// <summary>Where a <c>Trace.why</c> chain ends.</summary>
type WhyRoot =
    /// <summary>A write from outside every run: the <c>Write</c> seq.</summary>
    | UserWrite of write: int
    /// <summary>The node's first run, with no dirty mark: the <c>NodeNew</c> seq, or 0 when neither the log nor its checkpoint holds it.</summary>
    | Created of nodeNew: int
    /// <summary>A run with no dirty mark, started by a reader's pull: the reader's node id.</summary>
    | Pulled of reader: int
    /// <summary>A cause older than the events in the log: the checkpoint file that holds it, or null.</summary>
    | BeforeCheckpoint of file: string
    /// <summary>A run or mark whose cause the log does not record: the seq of the last step.</summary>
    | Unrecorded of last: int
    /// <summary>An async source's settle or failure: the <c>Settle</c> or <c>Fail</c> seq.</summary>
    | Settled of settle: int

    /// <summary>The root's case name and its event, node or checkpoint.</summary>
    override this.ToString() =
        match this with
        | UserWrite write -> "UserWrite " + string write
        | Created nodeNew -> "Created " + string nodeNew
        | Pulled reader -> "Pulled " + string reader
        | BeforeCheckpoint file -> "BeforeCheckpoint " + TraceText.value file
        | Unrecorded last -> "Unrecorded " + string last
        | Settled settle -> "Settled " + string settle

/// <summary>One event of a <c>Trace.why</c> chain.</summary>
type WhyStep =
    {
        Seq: int
        Kind: TraceEventKind
        Node: int
        Other: int
        /// <summary>The value or exception recorded by a <c>Write</c>, <c>Moved</c>, <c>Settle</c> or <c>Fail</c>.</summary>
        Value: obj option
    }

    /// <summary>The step fields, one per line, with the value's <c>ToString</c> text.</summary>
    override this.ToString() =
        TraceText.record
            [
                "Seq", string this.Seq
                "Kind", TraceNames.eventKind this.Kind
                "Node", string this.Node
                "Other", string this.Other
                "Value", TraceText.option TraceText.value this.Value
            ]

/// <summary>The cause chain of one run, from its <c>RunStart</c> back to a <c>WhyRoot</c>.</summary>
type Why =
    {
        Node: int
        /// <summary>The run number explained.</summary>
        Run: int
        /// <summary>The steps from the run's <c>RunStart</c> outwards, newest first.</summary>
        Steps: WhyStep list
        /// <summary>The chain's end, or <c>None</c> when <c>whyDepth</c> stopped it first.</summary>
        Root: WhyRoot option
    }

    /// <summary>The node, run, steps and root in F# record syntax.</summary>
    override this.ToString() =
        TraceText.record
            [
                "Node", string this.Node
                "Run", string this.Run
                "Steps", TraceText.list string this.Steps
                "Root", TraceText.option string this.Root
            ]

/// <summary>Why a node has not run since its last run ended, in <c>Trace.whyNot</c>.</summary>
type WhyNotReason =
    /// <summary>The node was disposed at <c>seq</c>.</summary>
    | Disposed of seq: int
    /// <summary>The node is scheduled and its run has not started: a batch is open or the flush is not reached.</summary>
    | Queued of schedule: int
    /// <summary>The node was marked at <c>mark</c> and has no observer and no pull since.</summary>
    | Unobserved of mark: int
    /// <summary>A check walk resolved the node clean at <c>resolvedAt</c>; <c>upstream</c> are its sources then.</summary>
    | CheckedClean of resolvedAt: int * upstream: int list
    /// <summary>A source's notification skipped the node as the running reader, at <c>seq</c>.</summary>
    | SkippedAsRunningReader of seq: int
    /// <summary>
    /// Propagation stopped upstream at <c>stopAt</c>: a <c>Write</c> that left its value, or a <c>RunEnd</c> that did
    /// not move.
    /// </summary>
    | NotReached of stopAt: int

    /// <summary>The reason's case name and its event and upstream nodes.</summary>
    override this.ToString() =
        match this with
        | Disposed seq -> "Disposed " + string seq
        | Queued schedule -> "Queued " + string schedule
        | Unobserved mark -> "Unobserved " + string mark
        | CheckedClean (resolvedAt, upstream) ->
            "CheckedClean ("
            + string resolvedAt
            + ", "
            + TraceText.list string upstream
            + ")"
        | SkippedAsRunningReader seq -> "SkippedAsRunningReader " + string seq
        | NotReached stopAt -> "NotReached " + string stopAt

/// <summary>One run of a node, in <c>Trace.history</c>.</summary>
type TraceRun =
    {
        /// <summary>The run number, from 1.</summary>
        Run: int
        /// <summary>The seq of the run's <c>RunStart</c>.</summary>
        Start: int
        /// <summary>How the run ended, or <c>None</c> while it is open.</summary>
        Status: RunStatus option
        /// <summary>True when the run moved the node's value, during the run or at its flight's settle.</summary>
        Moved: bool
        /// <summary>The end of the run's cause chain.</summary>
        Root: WhyRoot
        /// <summary>The number of the flush the run started in, or 0 outside every flush.</summary>
        Flush: int
        /// <summary>The value recorded by the run's <c>Moved</c>, else by the latest earlier run's.</summary>
        Value: obj option
    }

    /// <summary>The run fields, one per line, with the value's <c>ToString</c> text.</summary>
    override this.ToString() =
        TraceText.record
            [
                "Run", string this.Run
                "Start", string this.Start
                "Status", TraceText.option TraceNames.runStatus this.Status
                "Moved", TraceText.value this.Moved
                "Root", string this.Root
                "Flush", string this.Flush
                "Value", TraceText.option TraceText.value this.Value
            ]

/// <summary>A node's recorded runs, in <c>Trace.history</c>.</summary>
type TraceHistory =
    {
        Node: int
        /// <summary>The runs, oldest first.</summary>
        Runs: TraceRun list
    }

    /// <summary>The node and its ordered runs in F# record syntax.</summary>
    override this.ToString() =
        TraceText.record [ "Node", string this.Node; "Runs", TraceText.list string this.Runs ]

/// <summary>A flight's state, in <c>TraceFlight</c>.</summary>
[<RequireQualifiedAccess>]
type TraceFlightState =
    /// <summary>The log records the flight's start alone.</summary>
    | InFlight
    /// <summary>The flight settled at <c>seq</c>. <c>held</c> marks a value that left the node pending on a newer run.</summary>
    | Settled of seq: int * held: bool
    /// <summary>The flight failed at <c>seq</c>, cancelled when <c>cancelled</c> is true.</summary>
    | Failed of seq: int * cancelled: bool
    /// <summary>The flight's result was discarded at <c>seq</c>.</summary>
    | Dropped of seq: int * reason: TraceDropReason

    /// <summary>The flight state's case name and event details.</summary>
    override this.ToString() =
        match this with
        | TraceFlightState.InFlight -> "InFlight"
        | TraceFlightState.Settled (seq, held) ->
            "Settled ("
            + string seq
            + ", "
            + TraceText.value held
            + ")"
        | TraceFlightState.Failed (seq, cancelled) ->
            "Failed ("
            + string seq
            + ", "
            + TraceText.value cancelled
            + ")"
        | TraceFlightState.Dropped (seq, reason) ->
            "Dropped ("
            + string seq
            + ", "
            + TraceNames.dropReason reason
            + ")"

/// <summary>One flight of an async memo, in <c>TraceWaiting</c>.</summary>
type TraceFlight =
    {
        /// <summary>The flight number, from 1.</summary>
        Flight: int
        /// <summary>The seq of the flight's <c>FlightStart</c>.</summary>
        Start: int
        /// <summary>The number of the run that started the flight, or 0 when the log does not record it.</summary>
        Run: int
        State: TraceFlightState
    }

    /// <summary>The flight fields, one per line, in F# record syntax.</summary>
    override this.ToString() =
        TraceText.record
            [
                "Flight", string this.Flight
                "Start", string this.Start
                "Run", string this.Run
                "State", string this.State
            ]

/// <summary>What a node waits on, in <c>Trace.waitingOn</c>.</summary>
type TraceWaiting =
    {
        Node: int
        /// <summary>The pending sources read by the node's last run, in read order.</summary>
        Sources: int list
        /// <summary>The node's flights, newest first.</summary>
        Flights: TraceFlight list
    }

    /// <summary>The node, pending sources and flights in F# record syntax.</summary>
    override this.ToString() =
        TraceText.record
            [
                "Node", string this.Node
                "Sources", TraceText.list string this.Sources
                "Flights", TraceText.list string this.Flights
            ]

/// <summary>A node's state in a <c>TraceSnapshot</c>.</summary>
[<RequireQualifiedAccess>]
type TraceNodeStatus =
    /// <summary>Created and not yet run.</summary>
    | Fresh
    /// <summary>A run is open.</summary>
    | Running
    /// <summary>The last run ended with the <c>RunStatus</c>.</summary>
    | Ended of RunStatus
    | Disposed

    /// <summary>The node state's case name and the run status of an ended run.</summary>
    override this.ToString() =
        match this with
        | TraceNodeStatus.Fresh -> "Fresh"
        | TraceNodeStatus.Running -> "Running"
        | TraceNodeStatus.Ended status -> "Ended " + TraceNames.runStatus status
        | TraceNodeStatus.Disposed -> "Disposed"

/// <summary>A node in a <c>TraceSnapshot</c>.</summary>
type TraceSnapshotNode =
    {
        Id: int
        Kind: TraceNodeKind
        /// <summary>The owner id recorded by the node's <c>NodeNew</c>, or 0.</summary>
        Owner: int
        /// <summary>The identity path, without its <c>@k</c> suffix.</summary>
        Path: string
        /// <summary>The <c>k</c> of the node's <c>@k</c> suffix: 1 for the first node to hold the path.</summary>
        Incarnation: int
        Label: string option
        /// <summary>The creation site, or <c>"?"</c>.</summary>
        Site: string
        /// <summary>The seq of the node's <c>NodeNew</c>.</summary>
        Created: int
        /// <summary>The number of runs recorded.</summary>
        Runs: int
        /// <summary>The seq of the node's last <c>RunStart</c>, or 0.</summary>
        LastRun: int
        /// <summary>The first dirty <c>Mark</c> seq since the last <c>RunStart</c>, or 0.</summary>
        PendingMark: int
        Status: TraceNodeStatus
        /// <summary>
        /// The <c>valueText</c> of the latest value or exception recorded by a moving <c>Write</c>, a <c>Moved</c>, or
        /// an async source's <c>Settle</c> or <c>Fail</c>. A null written by a <c>Write</c> or settled by an async source,
        /// or a <c>Moved</c> to null by a memo, async memo, boundary, lookup cell or timed node, gives
        /// <c>valueText null</c>.
        /// </summary>
        Value: string option
    }

    /// <summary>The node snapshot's fields, one per line, in F# record syntax.</summary>
    override this.ToString() =
        TraceText.record
            [
                "Id", string this.Id
                "Kind", TraceNames.nodeKind this.Kind
                "Owner", string this.Owner
                "Path", TraceText.value this.Path
                "Incarnation", string this.Incarnation
                "Label", TraceText.option TraceText.value this.Label
                "Site", TraceText.value this.Site
                "Created", string this.Created
                "Runs", string this.Runs
                "LastRun", string this.LastRun
                "PendingMark", string this.PendingMark
                "Status", string this.Status
                "Value", TraceText.option TraceText.value this.Value
            ]

/// <summary>An owner in a <c>TraceSnapshot</c>.</summary>
type TraceSnapshotOwner =
    {
        Id: int
        /// <summary>The parent owner id, or 0 for the graph root.</summary>
        Parent: int
        /// <summary>The host node id of a run scope, or 0.</summary>
        Host: int
        /// <summary>True for a <c>createRoot</c> scope.</summary>
        Root: bool
        /// <summary>The identity path. A run scope shares its host's path; the graph root's is empty.</summary>
        Path: string
        Label: string option
        Disposed: bool
    }

    /// <summary>The owner snapshot's fields, one per line, in F# record syntax.</summary>
    override this.ToString() =
        TraceText.record
            [
                "Id", string this.Id
                "Parent", string this.Parent
                "Host", string this.Host
                "Root", TraceText.value this.Root
                "Path", TraceText.value this.Path
                "Label", TraceText.option TraceText.value this.Label
                "Disposed", TraceText.value this.Disposed
            ]

/// <summary>A graph's state folded from its trace events up to <c>Seq</c>.</summary>
type TraceSnapshot =
    {
        /// <summary>The seq of the last event folded, or 0.</summary>
        Seq: int
        /// <summary>The graph root's owner id, or 0 before <c>GraphNew</c>.</summary>
        Root: int
        Nodes: Map<int, TraceSnapshotNode>
        Owners: Map<int, TraceSnapshotOwner>
        /// <summary>Each computation's sources, in slot order. Empty lists are left out.</summary>
        Sources: Map<int, int list>
        /// <summary>Each source's observers. Empty sets are left out.</summary>
        Observers: Map<int, Set<int>>
        /// <summary>
        /// By parent path, the number of children created with each segment text since the parent's last run. The
        /// <c>createRoot</c> count is keyed <c>root#</c>.
        /// </summary>
        Siblings: Map<string, Map<string, int>>
        /// <summary>By path, the number of nodes that have held it.</summary>
        Incarnations: Map<string, int>
    }

    /// <summary>The snapshot fields and ordered maps in F# record syntax.</summary>
    override this.ToString() =
        TraceText.record
            [
                "Seq", string this.Seq
                "Root", string this.Root
                "Nodes", TraceText.map string string this.Nodes
                "Owners", TraceText.map string string this.Owners
                "Sources", TraceText.map string (TraceText.list string) this.Sources
                "Observers", TraceText.map string (TraceText.set string) this.Observers
                "Siblings", TraceText.map TraceText.value (TraceText.map TraceText.value string) this.Siblings
                "Incarnations", TraceText.map TraceText.value string this.Incarnations
            ]

/// <summary>A parsed JSONL dump: its header, the snapshot at <c>SeqFrom</c> and the events after it.</summary>
/// <remarks>
/// Parsed event payloads are strings, or null: in schema 3 the scalar's text for <c>Exact</c>, the data for
/// <c>Encoded</c> and the text for <c>Lossy</c>.
/// </remarks>
type TraceDump =
    {
        /// <summary>The dump schema: 1, 2 with timed nodes, or 3 with payload forms.</summary>
        Schema: int
        Target: string
        /// <summary>The graph's root owner id.</summary>
        Graph: int
        /// <summary>The checkpoint file holding earlier events, or null.</summary>
        Checkpoint: string
        /// <summary>The seq of the first event after the snapshot.</summary>
        SeqFrom: int
        Snapshot: TraceSnapshot
        Events: TraceEvent[]
        /// <summary>
        /// The form of each event's payload, at the event's index. Schema 3 gives the form written. Schemas 1 and 2
        /// carry no type: null gives <c>Null</c>, and text gives <c>Lossy</c>. A <c>TimedState</c> event's payload is
        /// its timing, and its form is <c>Null</c>.
        /// </summary>
        Payloads: TracePayload[]
    }

    /// <summary>The dump header, snapshot and events in F# record syntax.</summary>
    override this.ToString() =
        TraceText.record
            [
                "Target", TraceText.value this.Target
                "Graph", string this.Graph
                "Checkpoint", TraceText.value this.Checkpoint
                "SeqFrom", string this.SeqFrom
                "Snapshot", string this.Snapshot
                "Events", TraceText.array string this.Events
            ]

/// <summary>A graph's trace checkpoint: its name and the state folded from the events dropped from the log.</summary>
type TraceCheckpoint =
    {
        /// <summary>The name given to <c>Trace.checkpoint</c>, or null before the first checkpoint.</summary>
        Name: string
        /// <summary>
        /// The state folded from the dropped events, pruned with <c>TraceModel.prune</c>. <c>Snapshot.Seq</c> is the
        /// last dropped seq, or 0.
        /// </summary>
        Snapshot: TraceSnapshot
    }

    /// <summary>The checkpoint's name and snapshot in F# record syntax.</summary>
    override this.ToString() =
        TraceText.record [ "Name", TraceText.value this.Name; "Snapshot", string this.Snapshot ]

/// <summary>Pure queries over an array of trace events, oldest first.</summary>
/// <remarks>
/// An array may start after seq 1 when earlier events were written to a checkpoint; a cause before the first event
/// resolves to <c>BeforeCheckpoint</c>.
/// </remarks>
[<RequireQualifiedAccess>]
module TraceModel =
    let private find (events: TraceEvent[]) (seq: int) : TraceEvent voption =
        if events.Length = 0 then
            ValueNone
        else
            let i = seq - events[0].Seq

            if i >= 0 && i < events.Length && events[i].Seq = seq then
                ValueSome events[i]
            else
                match events |> Array.tryFind (fun e -> e.Seq = seq) with
                | Some e -> ValueSome e
                | None -> ValueNone

    /// <summary>
    /// A value as one line of at most 60 characters: an exception as its type name and message, a string as itself,
    /// and collections as up to 64 elements, nested up to four levels. Other values use <c>ToString</c> on .NET
    /// and <c>%A</c> under Fable. Application payloads must supply an AOT-compatible <c>ToString</c> for NativeAOT.
    /// </summary>
    let valueText (value: obj) : string =
        let lines (text: string) =
            text.Split ([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.map _.Trim()
            |> Array.filter (fun p -> p.Length > 0)

        let line =
            match value with
#if FABLE_COMPILER
            | :? exn as ex -> String.Join (" ", lines ex.Message)
#else
            | :? exn as ex -> String.Join (" ", lines (ex.GetType().Name + ": " + ex.Message))
#endif
            | :? string as s -> String.Join (" ", lines s)
#if FABLE_COMPILER
            | :? float as f -> string f
#else
            | :? float as f -> f.ToString ("R", Globalization.CultureInfo.InvariantCulture)
#endif
            | v ->
                let text =
#if FABLE_COMPILER
                    sprintf "%A" v
#else
                    let rec preview depth (value: obj) =
                        match value with
                        | null -> "null"
                        | :? string -> TraceText.value value
                        | _ when depth = 0 -> "..."
                        | :? System.Array as items -> TraceText.array (preview (depth - 1)) (items |> Seq.cast<obj> |> Seq.truncate 64)
                        | :? System.Collections.IEnumerable as items ->
                            TraceText.list (preview (depth - 1)) (items |> Seq.cast<obj> |> Seq.truncate 64)
                        | value -> value.ToString ()

                    preview 4 v
#endif
                let parts = lines text
                let joined = StringBuilder ()

                for i in 0 .. parts.Length - 1 do
                    if i > 0 then
                        let prev = parts[i - 1]
                        let opens = "[{(;,".IndexOf(prev[prev.Length - 1]) >= 0
                        let closes = "]})|".IndexOf(parts[i][0]) >= 0

                        joined.Append (if opens || closes then " " else "; ")
                        |> ignore

                    joined.Append parts[i] |> ignore

                let text = joined.ToString ()

#if FABLE_COMPILER
                text
#else
                // FSharp.Core releases differ in whether %A brackets an anonymous record with "{|".
                if
                    not (Platform.isNull v)
                    && v.GetType().Name.StartsWith("<>f__AnonymousType", StringComparison.Ordinal)
                    && text.StartsWith ("{ ", StringComparison.Ordinal)
                    && text.EndsWith (" }", StringComparison.Ordinal)
                then
                    "{| "
                    + text.Substring (2, text.Length - 4)
                    + " |}"
                else
                    text
#endif

        if line.Length > 60 then
            line.Substring (0, 59) + "…"
        else
            line

    // The source lists of `initial` advanced by the edge events.
    let internal sourcesFrom (initial: Map<int, int list>) (events: TraceEvent[]) : Map<int, int list> =
        let lists = Dictionary<int, ResizeArray<int>>()

        for KeyValue (node, list) in initial do
            lists[node] <- ResizeArray list

        let listOf node =
            match lists.TryGetValue node with
            | true, list -> list
            | _ ->
                let list = ResizeArray ()
                lists[node] <- list
                list

        for e in events do
            match e.Kind with
            | TraceEventKind.EdgeAdd ->
                let list = listOf e.Node

                if list.Count > e.Arg then
                    list.RemoveRange (e.Arg, list.Count - e.Arg)

                list.Add e.Other
            | TraceEventKind.EdgeRemove ->
                let list = listOf e.Node

                if list.Count > e.Arg then
                    list.RemoveRange (e.Arg, list.Count - e.Arg)
            | _ -> ()

        lists
        |> Seq.filter (fun kv -> kv.Value.Count > 0)
        |> Seq.map (fun kv -> kv.Key, List.ofSeq kv.Value)
        |> Map.ofSeq

    /// <summary>
    /// The source lists folded from <c>EdgeAdd</c>/<c>EdgeRemove</c>, by computation id, in slot order. Lists that
    /// fold to empty are left out.
    /// </summary>
    let sources (events: TraceEvent[]) : Map<int, int list> =
        sourcesFrom Map.empty events

    // The observer sets of `initial` advanced by the observer events.
    let internal observersFrom (initial: Map<int, Set<int>>) (events: TraceEvent[]) : Map<int, Set<int>> =
        let sets = Dictionary<int, HashSet<int>>()

        for KeyValue (node, set) in initial do
            sets[node] <- HashSet set

        for e in events do
            match e.Kind with
            | TraceEventKind.ObserverAdd ->
                match sets.TryGetValue e.Node with
                | true, set -> set.Add e.Other |> ignore
                | _ -> sets[e.Node] <- HashSet [ e.Other ]
            | TraceEventKind.ObserverRemove ->
                match sets.TryGetValue e.Node with
                | true, set -> set.Remove e.Other |> ignore
                | _ -> ()
            | _ -> ()

        sets
        |> Seq.filter (fun kv -> kv.Value.Count > 0)
        |> Seq.map (fun kv -> kv.Key, Set.ofSeq kv.Value)
        |> Map.ofSeq

    /// <summary>
    /// The observer sets folded from <c>ObserverAdd</c>/<c>ObserverRemove</c>, by source id. Sets that fold to empty
    /// are left out.
    /// </summary>
    let observers (events: TraceEvent[]) : Map<int, Set<int>> =
        observersFrom Map.empty events

    /// <summary>
    /// <c>whyDepth</c>, with <c>created</c> giving the <c>NodeNew</c> seq of a node whose <c>NodeNew</c> precedes the
    /// events, or 0.
    /// </summary>
    let internal whyDepthWith (created: int -> int) (events: TraceEvent[]) (checkpoint: string) (depth: int) (node: int) (run: int) : Why =
        let start =
            events
            |> Array.tryFindBack (fun e ->
                e.Kind = TraceEventKind.RunStart
                && e.Node = node
                && (run = 0 || e.Arg = run))

        match start with
        | None ->
            raise (
                ArgumentException (
                    "The trace log holds no RunStart for node "
                    + string node
                    + " run "
                    + string run
                    + ".",
                    nameof run
                )
            )
        | Some start ->
            let steps = ResizeArray<WhyStep>()

            let step (e: TraceEvent) =
                steps.Add
                    {
                        Seq = e.Seq
                        Kind = e.Kind
                        Node = e.Node
                        Other = e.Other
                        Value =
                            match e.Kind with
                            | TraceEventKind.Write
                            | TraceEventKind.Moved
                            | TraceEventKind.Settle
                            | TraceEventKind.Fail -> Option.ofObj e.Payload
                            | _ -> None
                    }

            let rec walk (e: TraceEvent) : WhyRoot option =
                if depth > 0 && steps.Count >= depth then
                    None
                else
                    step e

                    let next (cause: int) (orElse: unit -> WhyRoot) =
                        if cause = 0 then
                            Some (orElse ())
                        else
                            match find events cause with
                            | ValueSome c -> walk c
                            | ValueNone when events.Length > 0 && cause < events[0].Seq -> Some (BeforeCheckpoint checkpoint)
                            | ValueNone -> Some (Unrecorded e.Seq)

                    match e.Kind with
                    | TraceEventKind.RunStart ->
                        next e.Cause (fun () ->
                            if e.Arg = 1 then
                                events
                                |> Array.tryFind (fun n -> n.Kind = TraceEventKind.NodeNew && n.Node = e.Node)
                                |> Option.map (fun n -> n.Seq)
                                |> Option.defaultWith (fun () -> created e.Node)
                                |> Created
                            elif e.Other <> 0 then
                                Pulled e.Other
                            else
                                Unrecorded e.Seq)
                    | TraceEventKind.Write ->
                        if e.Other = 0 then
                            Some (UserWrite e.Seq)
                        else
                            next e.Cause (fun () -> Unrecorded e.Seq)
                    | TraceEventKind.Settle
                    | TraceEventKind.Fail -> next e.Cause (fun () -> Settled e.Seq)
                    | _ -> next e.Cause (fun () -> Unrecorded e.Seq)

            let root = walk start

            {
                Node = node
                Run = start.Arg
                Steps = List.ofSeq steps
                Root = root
            }

    /// <summary>
    /// The cause chain of run number <c>run</c> of <c>node</c>, or of its last run when <c>run</c> is 0, stopped after
    /// <c>depth</c> steps when <c>depth</c> is positive. <c>checkpoint</c> names the file holding earlier events, or is
    /// null.
    /// </summary>
    /// <exception cref="T:System.ArgumentException">The events hold no matching <c>RunStart</c> for <c>node</c>.</exception>
    let whyDepth (events: TraceEvent[]) (checkpoint: string) (depth: int) (node: int) (run: int) : Why =
        whyDepthWith (fun _ -> 0) events checkpoint depth node run

    /// <summary>The full cause chain of run number <c>run</c> of <c>node</c>, or of its last run when <c>run</c> is 0.</summary>
    let why (events: TraceEvent[]) (checkpoint: string) (node: int) (run: int) : Why =
        whyDepth events checkpoint 0 node run

    /// <summary>
    /// The run number a <c>Moved</c> belongs to: the run that started the settled flight when a <c>Settle</c> caused
    /// it, else its <c>Arg</c>.
    /// </summary>
    let private movedRun (events: TraceEvent[]) (moved: TraceEvent) =
        let fromFlight =
            match find events moved.Cause with
            | ValueSome settle when
                settle.Kind = TraceEventKind.Settle
                || settle.Kind = TraceEventKind.Fail
                ->
                match find events settle.Cause with
                | ValueSome flight when flight.Kind = TraceEventKind.FlightStart ->
                    match find events flight.Cause with
                    | ValueSome start when start.Kind = TraceEventKind.RunStart -> ValueSome start.Arg
                    | _ -> ValueNone
                | _ -> ValueNone
            | _ -> ValueNone

        defaultValueArg fromFlight moved.Arg

    /// <summary>
    /// <c>history</c>, with <c>created</c> giving the <c>NodeNew</c> seq of a node whose <c>NodeNew</c> precedes the
    /// events, or 0.
    /// </summary>
    let internal historyWith (created: int -> int) (events: TraceEvent[]) (checkpoint: string) (node: int) : TraceHistory =
        let ends = Dictionary<int, RunStatus>()
        let moved = Dictionary<int, obj>()
        let flushes = ResizeArray<int>()
        let starts = ResizeArray<struct (TraceEvent * int)>()

        for e in events do
            match e.Kind with
            | TraceEventKind.FlushStart -> flushes.Add e.Arg
            | TraceEventKind.FlushEnd when flushes.Count > 0 -> flushes.RemoveAt (flushes.Count - 1)
            | TraceEventKind.RunStart when e.Node = node ->
                let flush = if flushes.Count = 0 then 0 else flushes[flushes.Count - 1]
                starts.Add (struct (e, flush))
            | TraceEventKind.RunEnd when e.Node = node -> ends[e.Cause] <- enum<RunStatus> e.Arg
            | TraceEventKind.Moved when e.Node = node -> moved[movedRun events e] <- e.Payload
            | _ -> ()

        let mutable value = None

        let runs =
            [
                for struct (start, flush) in starts do
                    match moved.TryGetValue start.Arg with
                    | true, payload -> value <- Option.ofObj payload
                    | _ -> ()

                    {
                        Run = start.Arg
                        Start = start.Seq
                        Status =
                            match ends.TryGetValue start.Seq with
                            | true, status -> Some status
                            | _ -> None
                        Moved = moved.ContainsKey start.Arg
                        Root =
                            (whyDepthWith created events checkpoint 0 node start.Arg).Root
                            |> Option.defaultValue (Unrecorded start.Seq)
                        Flush = flush
                        Value = value
                    }
            ]

        { Node = node; Runs = runs }

    /// <summary>
    /// Every recorded run of <c>node</c>, oldest first. <c>checkpoint</c> names the file holding earlier events, or is
    /// null.
    /// </summary>
    let history (events: TraceEvent[]) (checkpoint: string) (node: int) : TraceHistory =
        historyWith (fun _ -> 0) events checkpoint node

    /// <summary>The pending sources read by <c>node</c>'s last run, and the node's flights.</summary>
    let waitingOn (events: TraceEvent[]) (node: int) : TraceWaiting =
        let lastRun =
            events
            |> Array.tryFindBack (fun e -> e.Kind = TraceEventKind.RunStart && e.Node = node)
            |> Option.map _.Seq
            |> Option.defaultValue -1

        let sources =
            events
            |> Array.filter (fun e ->
                e.Kind = TraceEventKind.Suspend
                && e.Node = node
                && e.Cause = lastRun)
            |> Array.map _.Other
            |> Array.distinct
            |> List.ofArray

        let states = Dictionary<int, TraceFlightState>()

        for e in events do
            if e.Node = node && e.Cause <> 0 then
                match e.Kind with
                | TraceEventKind.Settle -> states[e.Cause] <- TraceFlightState.Settled (e.Seq, e.Flag <> 0)
                | TraceEventKind.Fail -> states[e.Cause] <- TraceFlightState.Failed (e.Seq, e.Flag = 1)
                | TraceEventKind.FlightDrop -> states[e.Cause] <- TraceFlightState.Dropped (e.Seq, enum<TraceDropReason> e.Flag)
                | _ -> ()

        let flights =
            [
                for e in Array.rev events do
                    if
                        e.Kind = TraceEventKind.FlightStart
                        && e.Node = node
                    then
                        {
                            Flight = e.Arg
                            Start = e.Seq
                            Run =
                                match find events e.Cause with
                                | ValueSome start when start.Kind = TraceEventKind.RunStart -> start.Arg
                                | _ -> 0
                            State =
                                match states.TryGetValue e.Seq with
                                | true, state -> state
                                | _ -> TraceFlightState.InFlight
                        }
            ]

        {
            Node = node
            Sources = sources
            Flights = flights
        }

    // `whyNot` over events that follow a checkpoint whose snapshot holds `initialSources` and `initialObservers`.
    let internal whyNotFrom
        (initialSources: Map<int, int list>)
        (initialObservers: Map<int, Set<int>>)
        (events: TraceEvent[])
        (node: int)
        : WhyNotReason option =
        let from =
            let last =
                events
                |> Array.tryFindIndexBack (fun e ->
                    (e.Kind = TraceEventKind.RunEnd
                     || e.Kind = TraceEventKind.NodeNew)
                    && e.Node = node)

            defaultArg (Option.map ((+) 1) last) 0

        let window = events[from..]

        let on kind (e: TraceEvent) =
            e.Kind = kind && e.Node = node

        let first kind =
            window |> Array.tryFind (on kind)

        let disposed () =
            first TraceEventKind.Dispose
            |> Option.map (fun e -> Disposed e.Seq)

        let answered (e: TraceEvent) =
            on TraceEventKind.RunStart e
            || (on TraceEventKind.CheckResolved e && e.Flag = 0)
            || e.Kind = TraceEventKind.FlushEnd

        let queued () =
            window
            |> Array.tryFindIndexBack (on TraceEventKind.Schedule)
            |> Option.bind (fun i ->
                if window[i + 1 ..] |> Array.exists answered then
                    None
                else
                    Some (Queued window[i].Seq))

        let pulled (e: TraceEvent) =
            on TraceEventKind.RunStart e
            || on TraceEventKind.Schedule e
            || on TraceEventKind.CheckStart e
            || on TraceEventKind.CheckResolved e

        let unobserved () =
            match first TraceEventKind.Mark with
            | Some mark when
                not (
                    observersFrom initialObservers events
                    |> Map.containsKey node
                )
                && not (window |> Array.exists pulled)
                ->
                Some (Unobserved mark.Seq)
            | _ -> None

        let checkedClean () =
            window
            |> Array.tryFindBack (fun e -> on TraceEventKind.CheckResolved e && e.Flag = 0)
            |> Option.map (fun e ->
                let upstream =
                    sourcesFrom initialSources events[.. e.Seq - events[0].Seq]
                    |> Map.tryFind node
                    |> Option.defaultValue []

                CheckedClean (e.Seq, upstream))

        let skipped () =
            let lastRun =
                events
                |> Array.tryFindIndexBack (on TraceEventKind.RunStart)
                |> Option.defaultValue from

            events[min lastRun from ..]
            |> Array.tryFind (on TraceEventKind.MarkSkip)
            |> Option.map (fun e -> SkippedAsRunningReader e.Seq)

        let notReached () =
            let lists = sourcesFrom initialSources events
            let seen = HashSet<int>()
            let upstream = ResizeArray<int>()

            let rec visit n =
                for s in lists |> Map.tryFind n |> Option.defaultValue [] do
                    if seen.Add s then
                        upstream.Add s
                        visit s

            visit node

            window
            |> Array.tryFindBack (fun e ->
                seen.Contains e.Node
                && e.Flag = 0
                && (e.Kind = TraceEventKind.Write
                    || e.Kind = TraceEventKind.RunEnd))
            |> Option.map (fun e -> NotReached e.Seq)

        // A clean check with no moving write since a skip is the skip's consequence.
        let skippedThenClean () =
            match skipped (), checkedClean () with
            | Some (SkippedAsRunningReader skip as reason), Some (CheckedClean (resolved, _)) when
                skip < resolved
                && not (
                    events
                    |> Array.exists (fun e ->
                        e.Seq > skip
                        && e.Seq < resolved
                        && e.Kind = TraceEventKind.Write
                        && e.Flag = 1)
                )
                ->
                Some reason
            | _ -> None

        [
            disposed
            queued
            unobserved
            skippedThenClean
            checkedClean
            skipped
            notReached
        ]
        |> List.tryPick (fun reason -> reason ())

    /// <summary>
    /// The first <c>WhyNotReason</c> matching the events after <c>node</c>'s last <c>RunEnd</c>, or after its
    /// <c>NodeNew</c> when it has not run; <c>None</c> when none matches.
    /// </summary>
    /// <remarks>
    /// Reasons are tried in the order <c>Disposed</c>, <c>Queued</c>, <c>Unobserved</c>, <c>CheckedClean</c>,
    /// <c>SkippedAsRunningReader</c>, <c>NotReached</c>. <c>SkippedAsRunningReader</c> also matches a skip inside
    /// the last run.
    /// </remarks>
    let whyNot (events: TraceEvent[]) (node: int) : WhyNotReason option =
        whyNotFrom Map.empty Map.empty events node

    // -----------------------------------------------------------------------------------------------------------
    // Identity paths and snapshots

    /// <summary>The fold state before any event.</summary>
    let emptySnapshot: TraceSnapshot =
        {
            Seq = 0
            Root = 0
            Nodes = Map.empty
            Owners = Map.empty
            Sources = Map.empty
            Observers = Map.empty
            Siblings = Map.empty
            Incarnations = Map.empty
        }

    /// <summary><c>text</c> with each of <c>/ # [ ] @ \</c> prefixed by a backslash.</summary>
    let escape (text: string) : string =
        let sb = StringBuilder text.Length

        for c in text do
            match c with
            | '/'
            | '#'
            | '['
            | ']'
            | '@'
            | '\\' -> sb.Append('\\').Append c |> ignore
            | c -> sb.Append c |> ignore

        sb.ToString ()

    [<Literal>]
    let private RootKey = "root#"

    let private siteOf (payload: obj) =
        match payload with
        | :? string as s when s <> "" -> s
        | _ -> "?"

    /// <summary><c>state</c> advanced by <c>events</c>, which follow <c>state.Seq</c> in order.</summary>
    /// <remarks>
    /// A path segment is the node's label, else its site, else its kind; a <c>createRoot</c> scope is <c>root#n</c>
    /// and a run scope takes its host's path. A repeated segment under one parent takes <c>#n</c>, counted since the
    /// parent's last run.
    /// </remarks>
    let fold (state: TraceSnapshot) (events: TraceEvent[]) : TraceSnapshot =
        let mutable s = state

        let parentPath owner =
            let owner = if owner = 0 then s.Root else owner

            match s.Owners.TryFind owner with
            | Some o -> o.Path
            | None -> ""

        let next (parent: string) (text: string) =
            let perParent =
                s.Siblings.TryFind parent
                |> Option.defaultValue Map.empty

            let n = perParent.TryFind text |> Option.defaultValue 0

            s <-
                { s with
                    Siblings = s.Siblings.Add (parent, perParent.Add (text, n + 1))
                }

            n

        let child parent (text: string) =
            match next parent text with
            | 0 -> parent + "/" + text
            | n -> parent + "/" + text + "#" + string n

        let segment (label: string option) (site: string) (fallback: string) =
            match label with
            | Some label -> escape label
            | None when site <> "?" -> escape site
            | None -> fallback

        let labelAt i id =
            if i + 1 < events.Length then
                let l = events[i + 1]

                if
                    l.Kind = TraceEventKind.Label
                    && l.Arg = 0
                    && l.Node = id
                    && id <> 0
                then
                    Some (string l.Payload)
                else
                    None
            else
                None

        let updateNode id f =
            match s.Nodes.TryFind id with
            | Some n -> s <- { s with Nodes = s.Nodes.Add (id, f n) }
            | None -> ()

        let valueOf (payload: obj) =
            Option.ofObj payload |> Option.map valueText

        for i in 0 .. events.Length - 1 do
            let e = events[i]

            match e.Kind with
            | TraceEventKind.GraphNew ->
                let root =
                    {
                        Id = e.Other
                        Parent = 0
                        Host = 0
                        Root = false
                        Path = ""
                        Label = None
                        Disposed = false
                    }

                s <-
                    { s with
                        Root = e.Other
                        Owners = s.Owners.Add (e.Other, root)
                    }
            | TraceEventKind.NodeNew ->
                let kind = enum<TraceNodeKind> e.Arg
                let label = labelAt i e.Node
                let site = siteOf e.Payload

                let path =
                    child (parentPath e.Other) (segment label site (TraceNames.nodeKind kind))

                let k =
                    (s.Incarnations.TryFind path
                     |> Option.defaultValue 0)
                    + 1

                let node =
                    {
                        Id = e.Node
                        Kind = kind
                        Owner = e.Other
                        Path = path
                        Incarnation = k
                        Label = label
                        Site = site
                        Created = e.Seq
                        Runs = 0
                        LastRun = 0
                        PendingMark = 0
                        Status = TraceNodeStatus.Fresh
                        Value = None
                    }

                s <-
                    { s with
                        Incarnations = s.Incarnations.Add (path, k)
                        Nodes = s.Nodes.Add (e.Node, node)
                    }
            | TraceEventKind.OwnerNew ->
                let parent = parentPath e.Other
                let label = if e.Arg = 0 then labelAt i e.Node else None

                let path =
                    if e.Arg <> 0 then
                        match s.Nodes.TryFind e.Arg with
                        | Some host -> host.Path
                        | None -> parent
                    elif e.Flag = 1 then
                        parent
                        + "/"
                        + RootKey
                        + string (next parent RootKey)
                    else
                        child parent (segment label (siteOf e.Payload) "owner")

                let owner =
                    {
                        Id = e.Node
                        Parent = e.Other
                        Host = e.Arg
                        Root = (e.Flag = 1)
                        Path = path
                        Label = label
                        Disposed = false
                    }

                s <-
                    { s with
                        Owners = s.Owners.Add (e.Node, owner)
                    }
            | TraceEventKind.Dispose ->
                updateNode e.Node (fun n ->
                    { n with
                        Status = TraceNodeStatus.Disposed
                    })
            | TraceEventKind.OwnerDispose ->
                match s.Owners.TryFind e.Node with
                | Some o ->
                    s <-
                        { s with
                            Owners = s.Owners.Add (e.Node, { o with Disposed = true })
                        }
                | None -> ()
            | TraceEventKind.Write when e.Flag = 1 ->
                updateNode e.Node (fun n ->
                    { n with
                        Value = Some (valueText e.Payload)
                    })
            | TraceEventKind.Moved when not (Platform.isNull e.Payload) -> updateNode e.Node (fun n -> { n with Value = valueOf e.Payload })
            | TraceEventKind.Moved ->
                updateNode e.Node (fun n ->
                    match n.Kind with
                    | TraceNodeKind.Memo
                    | TraceNodeKind.AsyncMemo
                    | TraceNodeKind.Boundary
                    | TraceNodeKind.LookupCell
                    | TraceNodeKind.Timed -> { n with Value = Some (valueText null) }
                    | _ -> n)
            | TraceEventKind.Settle
            | TraceEventKind.Fail when e.Arg = 0 ->
                updateNode e.Node (fun n ->
                    { n with
                        Value = Some (valueText e.Payload)
                    })
            | TraceEventKind.Mark when e.Arg = 2 ->
                updateNode e.Node (fun n ->
                    if n.PendingMark = 0 then
                        { n with PendingMark = e.Seq }
                    else
                        n)
            | TraceEventKind.RunStart ->
                match s.Nodes.TryFind e.Node with
                | Some n ->
                    let n =
                        { n with
                            Runs = n.Runs + 1
                            LastRun = e.Seq
                            PendingMark = 0
                            Status =
                                (if n.Status = TraceNodeStatus.Disposed then
                                     TraceNodeStatus.Disposed
                                 elif
                                     n.Kind = TraceNodeKind.Timed
                                     || n.Kind = TraceNodeKind.Stream
                                 then
                                     n.Status
                                 else
                                     TraceNodeStatus.Running)
                        }

                    s <-
                        { s with
                            Siblings = s.Siblings.Remove n.Path
                            Nodes = s.Nodes.Add (e.Node, n)
                        }
                | None -> ()
            | TraceEventKind.RunEnd ->
                updateNode e.Node (fun n ->
                    if
                        n.Status = TraceNodeStatus.Disposed
                        || n.Kind = TraceNodeKind.Timed
                        || n.Kind = TraceNodeKind.Stream
                    then
                        n
                    else
                        { n with
                            Status = TraceNodeStatus.Ended (enum<RunStatus> e.Arg)
                        })
            | TraceEventKind.TimedPublished
            | TraceEventKind.StreamPublished ->
                updateNode e.Node (fun n ->
                    if n.Status = TraceNodeStatus.Disposed then
                        n
                    else
                        { n with
                            Status =
                                TraceNodeStatus.Ended (
                                    if e.Arg &&& 1 <> 0 then RunStatus.Pending
                                    elif e.Arg &&& 2 <> 0 then RunStatus.Error
                                    else RunStatus.Ok
                                )
                        })
            | TraceEventKind.EdgeAdd
            | TraceEventKind.EdgeRemove ->
                let kept =
                    s.Sources.TryFind e.Node
                    |> Option.defaultValue []
                    |> List.truncate e.Arg

                let list =
                    if e.Kind = TraceEventKind.EdgeAdd then
                        kept @ [ e.Other ]
                    else
                        kept

                s <-
                    { s with
                        Sources =
                            (if list.IsEmpty then
                                 s.Sources.Remove e.Node
                             else
                                 s.Sources.Add (e.Node, list))
                    }
            | TraceEventKind.ObserverAdd
            | TraceEventKind.ObserverRemove ->
                let set =
                    s.Observers.TryFind e.Node
                    |> Option.defaultValue Set.empty

                let set =
                    if e.Kind = TraceEventKind.ObserverAdd then
                        set.Add e.Other
                    else
                        set.Remove e.Other

                s <-
                    { s with
                        Observers =
                            (if set.IsEmpty then
                                 s.Observers.Remove e.Node
                             else
                                 s.Observers.Add (e.Node, set))
                    }
            | TraceEventKind.Label when e.Arg = 1 ->
                match s.Nodes.TryFind e.Node with
                | Some n ->
                    let label = string e.Payload
                    let old = n.Path

                    let path =
                        child (parentPath n.Owner) (segment (Some label) n.Site (TraceNames.nodeKind n.Kind))

                    let k =
                        (s.Incarnations.TryFind path
                         |> Option.defaultValue 0)
                        + 1

                    let moved (p: string) =
                        if p = old then
                            path
                        elif p.StartsWith (old + "/", StringComparison.Ordinal) then
                            path + p.Substring old.Length
                        else
                            p

                    s <-
                        { s with
                            Incarnations = s.Incarnations.Add (path, k)
                            Nodes =
                                s.Nodes
                                |> Map.map (fun id m ->
                                    if id = e.Node then
                                        { m with
                                            Path = path
                                            Label = Some label
                                            Incarnation = k
                                        }
                                    else
                                        { m with Path = moved m.Path })
                            Owners =
                                s.Owners
                                |> Map.map (fun _ o -> { o with Path = moved o.Path })
                            Siblings =
                                s.Siblings
                                |> Map.toSeq
                                |> Seq.map (fun (p, m) -> moved p, m)
                                |> Map.ofSeq
                        }
                | None -> ()
            | _ -> ()

            s <- { s with Seq = e.Seq }

        s

    // The path before the last separator of `path`: a `/` preceded by an even number of backslashes.
    let private parentOf (path: string) =
        let mutable cut = -1
        let mutable i = path.Length - 1

        while cut < 0 && i >= 0 do
            if path[i] = '/' then
                let mutable slashes = 0

                while i - 1 - slashes >= 0
                      && path[i - 1 - slashes] = '\\' do
                    slashes <- slashes + 1

                if slashes % 2 = 0 then
                    cut <- i

            i <- i - 1

        if cut < 0 then "" else path.Substring (0, cut)

    /// <summary>
    /// <c>snapshot</c> pruned as by <c>prune</c>, with the nodes in <c>released</c> treated as disposed.
    /// </summary>
    let internal pruneReleasing (released: HashSet<int>) (snapshot: TraceSnapshot) : TraceSnapshot =
        let nodes = HashSet<int>()
        let owners = HashSet<int>()

        let rec keepOwner id =
            if owners.Add id then
                match snapshot.Owners.TryFind id with
                | Some o ->
                    if o.Parent <> 0 then
                        keepOwner o.Parent

                    if o.Host <> 0 then
                        keepNode o.Host
                | None -> ()

        and keepNode id =
            if nodes.Add id then
                match snapshot.Nodes.TryFind id with
                | Some n when n.Owner <> 0 -> keepOwner n.Owner
                | _ -> ()

        if snapshot.Root <> 0 then
            keepOwner snapshot.Root

        for KeyValue (id, o) in snapshot.Owners do
            if not o.Disposed then
                keepOwner id

        for KeyValue (id, n) in snapshot.Nodes do
            if
                n.Status <> TraceNodeStatus.Disposed
                && not (released.Contains id)
            then
                keepNode id

        let kept = Map.filter (fun id _ -> nodes.Contains id) snapshot.Nodes

        let keptOwners = Map.filter (fun id _ -> owners.Contains id) snapshot.Owners

        let paths = HashSet<string>()
        let hosts = HashSet<string>()

        for KeyValue (_, n) in kept do
            paths.Add n.Path |> ignore
            hosts.Add n.Path |> ignore

        for KeyValue (_, o) in keptOwners do
            paths.Add o.Path |> ignore

        // A run of a kept node resets the sibling counts beneath its path, so a path beneath it can recur.
        let rec beneathHost (path: string) =
            path <> ""
            && (let parent = parentOf path
                hosts.Contains parent || beneathHost parent)

        let reachable (path: string) _ =
            paths.Contains path || beneathHost path

        { snapshot with
            Nodes = kept
            Owners = keptOwners
            Sources = Map.filter (fun id _ -> nodes.Contains id) snapshot.Sources
            Observers = Map.filter (fun id _ -> nodes.Contains id) snapshot.Observers
            Siblings = Map.filter reachable snapshot.Siblings
            Incarnations = Map.filter reachable snapshot.Incarnations
        }

    /// <summary>
    /// <c>snapshot</c> restricted to its live nodes and owners, the disposed ones a kept entry references as owner,
    /// parent or host, their edges, and the sibling and incarnation counts of the paths that can recur.
    /// </summary>
    /// <remarks>
    /// Pruning, folding later events and pruning again will give the pruned snapshot of the unpruned fold. A path
    /// beneath a kept node can recur, so its counts stay for the node's lifetime.
    /// </remarks>
    let prune (snapshot: TraceSnapshot) : TraceSnapshot =
        pruneReleasing (HashSet<int>()) snapshot

    /// <summary>The state folded from the events up to and including seq <c>seq</c>.</summary>
    let snapshotAt (events: TraceEvent[]) (seq: int) : TraceSnapshot =
        fold emptySnapshot (events |> Array.filter (fun e -> e.Seq <= seq))

    /// <summary>The state folded from every event.</summary>
    let snapshot (events: TraceEvent[]) : TraceSnapshot =
        fold emptySnapshot events

    /// <summary>
    /// The node id at <c>path</c>. A <c>@k</c> suffix selects the k-th node to hold the path; a bare path selects the
    /// live holder, else the latest.
    /// </summary>
    let resolve (snapshot: TraceSnapshot) (path: string) : int option =
        let at = path.LastIndexOf '@'

        let bare, k =
            if at > 0 && path[at - 1] <> '\\' then
                match Int32.TryParse (path.Substring (at + 1)) with
                | true, k -> path.Substring (0, at), Some k
                | _ -> path, None
            else
                path, None

        let holders =
            snapshot.Nodes.Values
            |> Seq.filter (fun n -> n.Path = bare)
            |> Seq.sortBy (fun n -> n.Incarnation)
            |> List.ofSeq

        match k with
        | Some k ->
            holders
            |> List.tryFind (fun n -> n.Incarnation = k)
            |> Option.map (fun n -> n.Id)
        | None ->
            holders
            |> List.tryFindBack (fun n -> n.Status <> TraceNodeStatus.Disposed)
            |> Option.orElse (List.tryLast holders)
            |> Option.map (fun n -> n.Id)

    // -----------------------------------------------------------------------------------------------------------
    // Rendering

    /// <summary>The node's path, with its <c>@k</c> suffix when more than one node has held the path.</summary>
    let pathOf (snapshot: TraceSnapshot) (node: int) : string =
        match snapshot.Nodes.TryFind node with
        | Some n ->
            if
                snapshot.Incarnations.TryFind n.Path
                |> Option.exists (fun k -> k > 1)
            then
                n.Path + "@" + string n.Incarnation
            else
                n.Path
        | None when node = 0 -> "-"
        | None -> "#" + string node

    let private kindPlural (kind: TraceNodeKind) =
        match kind with
        | TraceNodeKind.Memo -> "memos"
        | TraceNodeKind.Effect -> "effects"
        | TraceNodeKind.Signal -> "signals"
        | TraceNodeKind.AsyncSource -> "async sources"
        | TraceNodeKind.AsyncMemo -> "async memos"
        | TraceNodeKind.Boundary -> "boundaries"
        | TraceNodeKind.Projection -> "projections"
        | TraceNodeKind.ProjectionBeacon -> "beacons"
        | TraceNodeKind.RowWatch -> "row watches"
        | TraceNodeKind.LookupCell -> "lookup cells"
        | TraceNodeKind.Binding -> "bindings"
        | _ -> "nodes"

    let private eventLine (snapshot: TraceSnapshot) (e: TraceEvent) =
        let other =
            match e.Kind with
            | TraceEventKind.Mark
            | TraceEventKind.MarkSkip
            | TraceEventKind.EdgeAdd
            | TraceEventKind.EdgeRemove
            | TraceEventKind.ObserverAdd
            | TraceEventKind.ObserverRemove
            | TraceEventKind.RunStart
            | TraceEventKind.Write
            | TraceEventKind.CheckResolved when e.Other <> 0 -> " <- " + pathOf snapshot e.Other
            | _ -> ""

        let node = if e.Node = 0 then "" else " " + pathOf snapshot e.Node

        "#"
        + string e.Seq
        + " "
        + TraceNames.eventKind e.Kind
        + node
        + other

    /// <summary>
    /// One line per event. Within a run of consecutive <c>Mark</c>, <c>MarkSkip</c> and <c>Schedule</c> events, the
    /// marks sharing a cause, a mark kind, a node kind and a site fold into one line at the first of them.
    /// </summary>
    let renderEvents (snapshot: TraceSnapshot) (events: TraceEvent[]) : string =
        let lines = ResizeArray<string>()

        let inBurst (e: TraceEvent) =
            e.Kind = TraceEventKind.Mark
            || e.Kind = TraceEventKind.MarkSkip
            || e.Kind = TraceEventKind.Schedule

        let keyOf (e: TraceEvent) =
            match snapshot.Nodes.TryFind e.Node with
            | Some n -> struct (e.Cause, e.Arg, n.Kind, n.Site)
            | None -> struct (e.Cause, e.Arg, enum 0, "#" + string e.Node)

        let mutable i = 0

        while i < events.Length do
            if events[i].Kind = TraceEventKind.Mark then
                let mutable j = i

                while j < events.Length && inBurst events[j] do
                    j <- j + 1

                let burst = events[i .. j - 1]

                let counts =
                    burst
                    |> Array.filter (fun e -> e.Kind = TraceEventKind.Mark)
                    |> Array.countBy keyOf
                    |> dict

                let shown = HashSet<struct (int * int * TraceNodeKind * string)>()

                for e in burst do
                    if
                        e.Kind = TraceEventKind.Mark
                        && counts[keyOf e] > 1
                    then
                        let key = keyOf e

                        if shown.Add key then
                            let struct (cause, arg, kind, site) = key
                            let mark = if arg = 2 then "dirty" else "check"

                            lines.Add (
                                "#"
                                + string e.Seq
                                + " × "
                                + string counts[key]
                                + " "
                                + kindPlural kind
                                + " marked "
                                + mark
                                + " via "
                                + site
                                + " (cause #"
                                + string cause
                                + ")"
                            )
                    else
                        lines.Add (eventLine snapshot e)

                i <- j
            else
                lines.Add (eventLine snapshot events[i])
                i <- i + 1

        String.Join ("\n", lines)

    let private rootText (snapshot: TraceSnapshot) (root: WhyRoot option) =
        match root with
        | None -> "stopped at depth"
        | Some (UserWrite seq) -> "user write #" + string seq
        | Some (Created seq) -> "created #" + string seq
        | Some (Pulled reader) -> "pulled by " + pathOf snapshot reader
        | Some (BeforeCheckpoint null) -> "before the checkpoint"
        | Some (BeforeCheckpoint file) -> "before checkpoint " + file
        | Some (Unrecorded seq) -> "unrecorded after #" + string seq
        | Some (Settled seq) -> "settle #" + string seq

    /// <summary>The cause chain as text: a heading, one line per step with its site, and the root.</summary>
    let renderWhy (snapshot: TraceSnapshot) (why: Why) : string =
        let lines = ResizeArray<string>()

        lines.Add (
            "why "
            + pathOf snapshot why.Node
            + " run "
            + string why.Run
        )

        for step in why.Steps do
            let other =
                if step.Other = 0 then
                    ""
                else
                    " <- " + pathOf snapshot step.Other

            let site =
                match snapshot.Nodes.TryFind step.Node with
                | Some n when n.Site <> "?" -> " (" + n.Site + ")"
                | _ -> ""

            let value =
                match step.Value with
                | Some v -> " = " + valueText v
                | None -> ""

            lines.Add (
                "  #"
                + string step.Seq
                + " "
                + TraceNames.eventKind step.Kind
                + " "
                + pathOf snapshot step.Node
                + other
                + site
                + value
            )

        lines.Add ("  root: " + rootText snapshot why.Root)
        String.Join ("\n", lines)

    /// <summary>The reason as one line.</summary>
    let renderWhyNot (snapshot: TraceSnapshot) (reason: WhyNotReason option) : string =
        match reason with
        | None -> "no reason recorded"
        | Some (Disposed seq) -> "disposed #" + string seq
        | Some (Queued seq) -> "queued #" + string seq
        | Some (Unobserved seq) -> "unobserved since mark #" + string seq
        | Some (CheckedClean (seq, upstream)) ->
            "checked clean #"
            + string seq
            + " over "
            + String.Join (", ", upstream |> List.map (pathOf snapshot))
        | Some (SkippedAsRunningReader seq) -> "skipped as the running reader #" + string seq
        | Some (NotReached seq) ->
            "not reached: propagation stopped at #"
            + string seq

    let private runStatusText (status: RunStatus) =
        match status with
        | RunStatus.Ok -> "ok"
        | RunStatus.Pending -> "pending"
        | RunStatus.Error -> "error"
        | RunStatus.Abandoned -> "abandoned"
        | other -> string (int other)

    /// <summary>The runs as text: a heading, then one line per run with its status, flush and root.</summary>
    let renderHistory (snapshot: TraceSnapshot) (history: TraceHistory) : string =
        let lines = ResizeArray<string>()
        lines.Add ("history " + pathOf snapshot history.Node)

        for r in history.Runs do
            let status =
                match r.Status with
                | Some status -> runStatusText status
                | None -> "running"

            let moved = if r.Moved then " moved" else ""
            let flush = if r.Flush = 0 then "" else " flush " + string r.Flush

            let value =
                match r.Status, r.Value with
                | Some (RunStatus.Ok | RunStatus.Error), Some v -> " = " + valueText v
                | _ -> ""

            lines.Add (
                "  run "
                + string r.Run
                + " #"
                + string r.Start
                + " "
                + status
                + moved
                + flush
                + value
                + " root: "
                + rootText snapshot (Some r.Root)
            )

        String.Join ("\n", lines)

    /// <summary>The suspension sources and flights as text: a heading, then one line per source and per flight.</summary>
    let renderWaiting (snapshot: TraceSnapshot) (waiting: TraceWaiting) : string =
        let lines = ResizeArray<string>()
        lines.Add ("waiting " + pathOf snapshot waiting.Node)

        for source in waiting.Sources do
            lines.Add ("  suspended on " + pathOf snapshot source)

        for f in waiting.Flights do
            let state =
                match f.State with
                | TraceFlightState.InFlight -> "in flight"
                | TraceFlightState.Settled (seq, false) -> "settled #" + string seq
                | TraceFlightState.Settled (seq, true) -> "settled #" + string seq + ", held pending"
                | TraceFlightState.Failed (seq, false) -> "failed #" + string seq
                | TraceFlightState.Failed (seq, true) -> "cancelled #" + string seq
                | TraceFlightState.Dropped (seq, reason) ->
                    "dropped #"
                    + string seq
                    + " "
                    + (TraceNames.dropReason reason).ToLowerInvariant()

            lines.Add (
                "  flight "
                + string f.Flight
                + " #"
                + string f.Start
                + " run "
                + string f.Run
                + " "
                + state
            )

        if waiting.Sources.IsEmpty && waiting.Flights.IsEmpty then
            lines.Add "  nothing recorded"

        String.Join ("\n", lines)

    let private statusText (status: TraceNodeStatus) =
        match status with
        | TraceNodeStatus.Fresh -> "fresh"
        | TraceNodeStatus.Running -> "running"
        | TraceNodeStatus.Ended RunStatus.Ok -> "ok"
        | TraceNodeStatus.Ended RunStatus.Pending -> "pending"
        | TraceNodeStatus.Ended RunStatus.Error -> "error"
        | TraceNodeStatus.Ended RunStatus.Abandoned -> "abandoned"
        | TraceNodeStatus.Ended other -> string (int other)
        | TraceNodeStatus.Disposed -> "disposed"

    /// <summary>
    /// The owner tree as indented text: each owner's nodes with path, kind, status, run count and sources.
    /// </summary>
    let renderSnapshot (snapshot: TraceSnapshot) : string =
        let lines = ResizeArray<string>()

        let children =
            snapshot.Owners.Values
            |> Seq.filter (fun o -> o.Id <> snapshot.Root && o.Host = 0)
            |> Seq.groupBy (fun o -> if o.Parent = 0 then snapshot.Root else o.Parent)
            |> Map.ofSeq

        let scopes =
            snapshot.Owners.Values
            |> Seq.filter (fun o -> o.Host <> 0)
            |> Seq.groupBy (fun o -> o.Host)
            |> Map.ofSeq

        let nodesOf owner =
            snapshot.Nodes.Values
            |> Seq.filter (fun n -> (if n.Owner = 0 then snapshot.Root else n.Owner) = owner)

        let rec owner (indent: string) (id: int) =
            for n in nodesOf id do
                let sources =
                    match snapshot.Sources.TryFind n.Id with
                    | Some list ->
                        " <- "
                        + String.Join (", ", list |> List.map (pathOf snapshot))
                    | None -> ""

                lines.Add (
                    indent
                    + pathOf snapshot n.Id
                    + " "
                    + TraceNames.nodeKind n.Kind
                    + " "
                    + statusText n.Status
                    + " runs "
                    + string n.Runs
                    + sources
                )

                for scope in
                    scopes.TryFind n.Id
                    |> Option.defaultValue Seq.empty do
                    if not scope.Disposed then
                        owner (indent + "  ") scope.Id

            for o in
                children.TryFind id
                |> Option.defaultValue Seq.empty do
                lines.Add (
                    indent
                    + o.Path
                    + (if o.Disposed then " disposed" else "")
                )

                owner (indent + "  ") o.Id

        lines.Add "/"
        owner "  " snapshot.Root
        String.Join ("\n", lines)

    // -----------------------------------------------------------------------------------------------------------
    // JSONL dump, schema 1

    let private jsonString (sb: StringBuilder) (text: string) =
        if Platform.isNull text then
            sb.Append "null" |> ignore
        else
            sb.Append '"' |> ignore

            for c in text do
                match c with
                | '"' -> sb.Append "\\\"" |> ignore
                | '\\' -> sb.Append "\\\\" |> ignore
                | '\n' -> sb.Append "\\n" |> ignore
                | '\r' -> sb.Append "\\r" |> ignore
                | '\t' -> sb.Append "\\t" |> ignore
                | c when c < ' ' ->
                    sb.Append("\\u").Append((int c).ToString "x4")
                    |> ignore
                | c -> sb.Append c |> ignore

            sb.Append '"' |> ignore

    let private jsonInts (sb: StringBuilder) (values: int seq) =
        sb.Append '[' |> ignore
        let mutable first = true

        for v in values do
            if not first then
                sb.Append ',' |> ignore

            first <- false
            sb.Append v |> ignore

        sb.Append ']' |> ignore

    /// <summary>
    /// A payload's canonical text: an <c>int</c>, <c>string</c>, <c>bool</c> or <c>float</c> (round-trip) as its
    /// value, null as null, and any other value as its <c>valueText</c>.
    /// </summary>
    let payloadText (payload: obj) : string =
        match payload with
        | null -> null
        | :? string as s -> s
        | :? int as i -> string i
        | :? bool as b -> (if b then "true" else "false")
#if FABLE_COMPILER
        | :? float as f -> string f
#else
        | :? float as f -> f.ToString ("R", Globalization.CultureInfo.InvariantCulture)
#endif
        | p -> valueText p

    let private statusOf (text: string) =
        match text with
        | "fresh" -> TraceNodeStatus.Fresh
        | "running" -> TraceNodeStatus.Running
        | "ok" -> TraceNodeStatus.Ended RunStatus.Ok
        | "pending" -> TraceNodeStatus.Ended RunStatus.Pending
        | "error" -> TraceNodeStatus.Ended RunStatus.Error
        | "abandoned" -> TraceNodeStatus.Ended RunStatus.Abandoned
        | "disposed" -> TraceNodeStatus.Disposed
        | other -> TraceNodeStatus.Ended (enum<RunStatus>(int other))

    let private snapshotJson (sb: StringBuilder) (s: TraceSnapshot) =
        sb.Append("{\"snapshot\":{\"seq\":").Append(s.Seq).Append(",\"root\":").Append(s.Root).Append ",\"nodes\":["
        |> ignore

        let mutable first = true

        for n in s.Nodes.Values do
            if not first then
                sb.Append ',' |> ignore

            first <- false

            sb
                .Append("{\"id\":")
                .Append(n.Id)
                .Append(",\"kind\":\"")
                .Append(TraceNames.nodeKind n.Kind)
                .Append("\",\"owner\":")
                .Append(n.Owner)
                .Append
                ",\"path\":"
            |> ignore

            jsonString sb n.Path

            sb.Append(",\"incarnation\":").Append(n.Incarnation).Append ",\"label\":"
            |> ignore

            jsonString sb (Option.toObj n.Label)
            sb.Append ",\"site\":" |> ignore
            jsonString sb n.Site

            sb
                .Append(",\"created\":")
                .Append(n.Created)
                .Append(",\"runs\":")
                .Append(n.Runs)
                .Append(",\"lastRun\":")
                .Append(n.LastRun)
                .Append(",\"pendingMark\":")
                .Append(n.PendingMark)
                .Append(",\"status\":\"")
                .Append(statusText n.Status)
                .Append
                "\",\"value\":"
            |> ignore

            jsonString sb (Option.toObj n.Value)
            sb.Append '}' |> ignore

        sb.Append "],\"owners\":[" |> ignore
        first <- true

        for o in s.Owners.Values do
            if not first then
                sb.Append ',' |> ignore

            first <- false

            sb
                .Append("{\"id\":")
                .Append(o.Id)
                .Append(",\"parent\":")
                .Append(o.Parent)
                .Append(",\"host\":")
                .Append(o.Host)
                .Append(",\"root\":")
                .Append((if o.Root then "true" else "false"))
                .Append
                ",\"path\":"
            |> ignore

            jsonString sb o.Path
            sb.Append ",\"label\":" |> ignore
            jsonString sb (Option.toObj o.Label)

            sb.Append(",\"disposed\":").Append((if o.Disposed then "true" else "false")).Append '}'
            |> ignore

        sb.Append "],\"sources\":[" |> ignore
        first <- true

        for KeyValue (node, list) in s.Sources do
            if not first then
                sb.Append ',' |> ignore

            first <- false
            sb.Append('[').Append(node).Append ',' |> ignore
            jsonInts sb list
            sb.Append ']' |> ignore

        sb.Append "],\"observers\":[" |> ignore
        first <- true

        for KeyValue (node, set) in s.Observers do
            if not first then
                sb.Append ',' |> ignore

            first <- false
            sb.Append('[').Append(node).Append ',' |> ignore
            jsonInts sb set
            sb.Append ']' |> ignore

        sb.Append "],\"siblings\":[" |> ignore
        first <- true

        for KeyValue (parent, counts) in s.Siblings do
            if not first then
                sb.Append ',' |> ignore

            first <- false
            sb.Append '[' |> ignore
            jsonString sb parent
            sb.Append ",[" |> ignore
            let mutable inner = true

            for KeyValue (text, n) in counts do
                if not inner then
                    sb.Append ',' |> ignore

                inner <- false
                sb.Append '[' |> ignore
                jsonString sb text
                sb.Append(',').Append(n).Append ']' |> ignore

            sb.Append "]]" |> ignore

        sb.Append "],\"incarnations\":[" |> ignore
        first <- true

        for KeyValue (path, k) in s.Incarnations do
            if not first then
                sb.Append ',' |> ignore

            first <- false
            sb.Append '[' |> ignore
            jsonString sb path
            sb.Append(',').Append(k).Append ']' |> ignore

        sb.Append "]}}" |> ignore

    /// <summary>The latest recorded captured and published timing state of a node, or None for an absent timed state.</summary>
    let timing (events: TraceEvent[]) (node: int) : TraceTiming option =
        events
        |> Array.tryFindBack (fun e ->
            e.Node = node
            && e.Kind = TraceEventKind.TimedState)
        |> Option.map (fun e -> (e.Payload :?> TraceTimedState).Timing)

    let private jsonFloat (value: float) : string =
#if FABLE_COMPILER
        Fable.Core.JsInterop.emitJsExpr value "String($0)"
#else
        value.ToString ("R", Globalization.CultureInfo.InvariantCulture)
#endif

    // A schema 3 payload form: null, or an object with its `form`.
    let private payloadForm (sb: StringBuilder) (payload: TracePayload) =
        let number (f: float) =
            if Double.IsNaN f then "\"NaN\""
            elif f = infinity then "\"Infinity\""
            elif f = -infinity then "\"-Infinity\""
            else jsonFloat f

        match payload with
        | TracePayload.Null -> sb.Append "null" |> ignore
        | TracePayload.Exact scalar ->
            sb.Append "{\"form\":\"exact\",\"type\":"
            |> ignore

            match scalar with
            | TraceScalar.Int i ->
                sb.Append("\"int\",\"value\":").Append(string i)
                |> ignore
            | TraceScalar.Bool b ->
                sb.Append("\"bool\",\"value\":").Append(if b then "true" else "false")
                |> ignore
            | TraceScalar.Float f ->
                sb.Append("\"float\",\"value\":").Append(number f)
                |> ignore
            | TraceScalar.String s ->
                sb.Append "\"string\",\"value\":" |> ignore
                jsonString sb s

            sb.Append '}' |> ignore
        | TracePayload.Encoded (codec, version, data) ->
            sb.Append "{\"form\":\"encoded\",\"codec\":"
            |> ignore

            jsonString sb codec

            sb.Append(",\"version\":").Append(string version).Append ",\"data\":"
            |> ignore

            jsonString sb data
            sb.Append '}' |> ignore
        | TracePayload.Lossy text ->
            sb.Append "{\"form\":\"lossy\",\"text\":"
            |> ignore

            jsonString sb text
            sb.Append '}' |> ignore

    let private eventJson (sb: StringBuilder) (encode: (obj -> TracePayload) option) (e: TraceEvent) =
        sb
            .Append("{\"seq\":")
            .Append(e.Seq)
            .Append(",\"kind\":\"")
            .Append(TraceNames.eventKind e.Kind)
            .Append("\",\"node\":")
            .Append(e.Node)
            .Append(",\"other\":")
            .Append(e.Other)
            .Append(",\"arg\":")
            .Append(e.Arg)
            .Append(",\"flag\":")
            .Append(e.Flag)
            .Append(",\"cause\":")
            .Append(e.Cause)
            .Append
            ",\"payload\":"
        |> ignore

        if e.Kind = TraceEventKind.TimedState then
            let state = e.Payload :?> TraceTimedState
            let timing = state.Timing

            sb
                .Append("null,\"timing\":{\"timebase\":")
                .Append(state.Clock)
                .Append(",\"at\":")
                .Append(jsonFloat state.AtMilliseconds)
                .Append(",\"mode\":")
            |> ignore

            jsonString sb timing.Mode

            sb.Append(",\"captured\":").Append(int (byte timing.CapturedStatus)).Append(",\"published\":").Append(int (byte timing.PublishedStatus))
            |> ignore

            sb.Append(",\"open\":").Append(if timing.WindowOpen then "true" else "false").Append(",\"remaining\":")
            |> ignore

            sb.Append(jsonFloat timing.RemainingMilliseconds).Append(",\"capture\":").Append(timing.WinningCapture).Append('}')
            |> ignore
        else
            match encode with
            | Some encode -> payloadForm sb (encode e.Payload)
            | None -> jsonString sb (payloadText e.Payload)

        sb.Append '}' |> ignore

    /// <summary>
    /// The JSONL dump of <c>dumpText</c>; with <c>encode</c>, in schema 3 with each payload written in the form
    /// <c>encode</c> gives.
    /// </summary>
    let internal dumpTextEncoded
        (encode: (obj -> TracePayload) option)
        (target: string)
        (checkpoint: string)
        (before: TraceSnapshot)
        (events: TraceEvent[])
        : string =
        let sb = StringBuilder ()
        let seqFrom = if events.Length = 0 then before.Seq + 1 else events[0].Seq

        let timed =
            events
            |> Array.exists (fun e ->
                e.Kind = TraceEventKind.NodeNew
                && e.Arg = int TraceNodeKind.Timed)

        let schema =
            if encode.IsSome then
                3
            elif
                timed
                || (before.Nodes.Values
                    |> Seq.exists (fun n -> n.Kind = TraceNodeKind.Timed))
            then
                2
            else
                1

        sb.Append("{\"schema\":").Append(schema).Append(",\"target\":")
        |> ignore

        jsonString sb target

        let graph =
            if before.Root <> 0 then
                before.Root
            else
                events
                |> Array.tryPick (fun e ->
                    if e.Kind = TraceEventKind.GraphNew then
                        Some e.Other
                    else
                        None)
                |> Option.defaultValue 0

        sb.Append(",\"graph\":").Append(graph).Append ",\"checkpoint\":"
        |> ignore

        jsonString sb checkpoint

        sb.Append(",\"seqFrom\":").Append(seqFrom).Append "}\n"
        |> ignore

        snapshotJson sb before
        sb.Append '\n' |> ignore

        for e in events do
            eventJson sb encode e
            sb.Append '\n' |> ignore

        sb.ToString ()

    /// <summary>
    /// The JSONL dump: a header, the snapshot holding <c>before</c>, then one line per event. Timed nodes select schema 2.
    /// </summary>
    /// <remarks>
    /// <c>before</c> is the state ahead of the first event; <c>checkpoint</c> names the file holding earlier events, or
    /// is null. Lines end in <c>\n</c>. The same events and state give the same text.
    /// </remarks>
    let dumpText (target: string) (checkpoint: string) (before: TraceSnapshot) (events: TraceEvent[]) : string =
        dumpTextEncoded None target checkpoint before events

#if !FABLE_COMPILER
    // The form of a schema 1 or 2 payload's text, which carries no type.
    let private textForm (text: string) =
        match text with
        | null -> TracePayload.Null
        | _ -> TracePayload.Lossy text

    // A schema 3 payload form, and the text an event of schema 1 or 2 would hold for it.
    let private readForm (e: Text.Json.JsonElement) =
        let str (name: string) =
            let p = e.GetProperty name

            if p.ValueKind = Text.Json.JsonValueKind.Null then
                null
            else
                p.GetString ()

        if e.ValueKind = Text.Json.JsonValueKind.Null then
            TracePayload.Null
        else
            match e.GetProperty("form").GetString() with
            | "exact" ->
                let value = e.GetProperty "value"

                match e.GetProperty("type").GetString() with
                | "int" -> TracePayload.Exact (TraceScalar.Int (value.GetInt32 ()))
                | "bool" -> TracePayload.Exact (TraceScalar.Bool (value.GetBoolean ()))
                | "string" -> TracePayload.Exact (TraceScalar.String (str "value"))
                | "float" when value.ValueKind = Text.Json.JsonValueKind.String ->
                    match value.GetString () with
                    | "NaN" -> TracePayload.Exact (TraceScalar.Float nan)
                    | "Infinity" -> TracePayload.Exact (TraceScalar.Float infinity)
                    | "-Infinity" -> TracePayload.Exact (TraceScalar.Float -infinity)
                    | other ->
                        raise (
                            FormatException (
                                "A trace dump holds an unknown float: "
                                + other
                                + "."
                            )
                        )
                | "float" -> TracePayload.Exact (TraceScalar.Float (value.GetDouble ()))
                | other ->
                    raise (
                        FormatException (
                            "A trace dump holds an unknown exact type: "
                            + other
                            + "."
                        )
                    )
            | "encoded" -> TracePayload.Encoded (str "codec", e.GetProperty("version").GetInt32(), str "data")
            | "lossy" -> TracePayload.Lossy (str "text")
            | other ->
                raise (
                    FormatException (
                        "A trace dump holds an unknown payload form: "
                        + other
                        + "."
                    )
                )

    let private formText (payload: TracePayload) =
        match payload with
        | TracePayload.Null -> null
        | TracePayload.Exact (TraceScalar.Int i) -> payloadText (box i)
        | TracePayload.Exact (TraceScalar.Bool b) -> payloadText (box b)
        | TracePayload.Exact (TraceScalar.Float f) -> payloadText (box f)
        | TracePayload.Exact (TraceScalar.String s) -> s
        | TracePayload.Encoded (_, _, data) -> data
        | TracePayload.Lossy text -> text

    /// <summary>The header, snapshot and events of a JSONL dump in schema 1, 2 or 3.</summary>
    /// <exception cref="T:System.FormatException">The text is not a supported trace dump.</exception>
    let parseDump (text: string) : TraceDump =
        let lines = text.Split ([| '\n' |], StringSplitOptions.RemoveEmptyEntries)

        if lines.Length < 2 then
            raise (FormatException "A trace dump holds a header line and a snapshot line.")

        use header = Text.Json.JsonDocument.Parse lines[0]
        let h = header.RootElement

        let schema = h.GetProperty("schema").GetInt32()

        if schema < 1 || schema > 3 then
            raise (
                FormatException (
                    "The trace dump schema "
                    + string schema
                    + " is unsupported."
                )
            )

        let str (e: Text.Json.JsonElement) (name: string) =
            let p = e.GetProperty name

            if p.ValueKind = Text.Json.JsonValueKind.Null then
                null
            else
                p.GetString ()

        let int' (e: Text.Json.JsonElement) (name: string) =
            e.GetProperty(name).GetInt32()

        let bool' (e: Text.Json.JsonElement) (name: string) =
            e.GetProperty(name).GetBoolean()

        let ints (e: Text.Json.JsonElement) =
            [ for i in e.EnumerateArray () -> i.GetInt32 () ]

        use snap = Text.Json.JsonDocument.Parse lines[1]
        let s = snap.RootElement.GetProperty "snapshot"

        let nodes =
            [
                for n in s.GetProperty("nodes").EnumerateArray() ->
                    let id = int' n "id"

                    id,
                    {
                        Id = id
                        Kind = Enum.Parse<TraceNodeKind>(str n "kind")
                        Owner = int' n "owner"
                        Path = str n "path"
                        Incarnation = int' n "incarnation"
                        Label = Option.ofObj (str n "label")
                        Site = str n "site"
                        Created = int' n "created"
                        Runs = int' n "runs"
                        LastRun = int' n "lastRun"
                        PendingMark = int' n "pendingMark"
                        Status = statusOf (str n "status")
                        Value = Option.ofObj (str n "value")
                    }
            ]

        let owners =
            [
                for o in s.GetProperty("owners").EnumerateArray() ->
                    let id = int' o "id"

                    id,
                    {
                        Id = id
                        Parent = int' o "parent"
                        Host = int' o "host"
                        Root = bool' o "root"
                        Path = str o "path"
                        Label = Option.ofObj (str o "label")
                        Disposed = bool' o "disposed"
                    }
            ]

        let pairs (name: string) =
            [ for p in s.GetProperty(name).EnumerateArray() -> p[0], p[1] ]

        let snapshot =
            {
                Seq = int' s "seq"
                Root = int' s "root"
                Nodes = Map.ofList nodes
                Owners = Map.ofList owners
                Sources =
                    pairs "sources"
                    |> List.map (fun (k, v) -> k.GetInt32 (), ints v)
                    |> Map.ofList
                Observers =
                    pairs "observers"
                    |> List.map (fun (k, v) -> k.GetInt32 (), Set.ofList (ints v))
                    |> Map.ofList
                Siblings =
                    pairs "siblings"
                    |> List.map (fun (k, v) ->
                        k.GetString (),
                        [ for c in v.EnumerateArray () -> c[0].GetString(), c[1].GetInt32() ]
                        |> Map.ofList)
                    |> Map.ofList
                Incarnations =
                    pairs "incarnations"
                    |> List.map (fun (k, v) -> k.GetString (), v.GetInt32 ())
                    |> Map.ofList
            }

        let parsed =
            [|
                for line in lines[2..] ->
                    use doc = Text.Json.JsonDocument.Parse line
                    let e = doc.RootElement
                    let kind = Enum.Parse<TraceEventKind>(str e "kind")

                    let form =
                        if kind = TraceEventKind.TimedState then TracePayload.Null
                        elif schema = 3 then readForm (e.GetProperty "payload")
                        else textForm (str e "payload")

                    let payload =
                        if kind = TraceEventKind.TimedState then
                            let t = e.GetProperty "timing"

                            box
                                {
                                    Clock = int' t "timebase"
                                    AtMilliseconds = t.GetProperty("at").GetDouble()
                                    Timing =
                                        {
                                            Mode = str t "mode"
                                            CapturedStatus = LanguagePrimitives.EnumOfValue<byte, Status>(byte (int' t "captured"))
                                            PublishedStatus = LanguagePrimitives.EnumOfValue<byte, Status>(byte (int' t "published"))
                                            WindowOpen = bool' t "open"
                                            RemainingMilliseconds = t.GetProperty("remaining").GetDouble()
                                            WinningCapture = int' t "capture"
                                        }
                                }
                        elif schema = 3 then
                            box (formText form)
                        else
                            box (str e "payload")

                    {
                        Seq = int' e "seq"
                        Kind = kind
                        Node = int' e "node"
                        Other = int' e "other"
                        Arg = int' e "arg"
                        Flag = int' e "flag"
                        Cause = int' e "cause"
                        Payload = payload
                    },
                    form
            |]

        {
            Schema = schema
            Payloads = Array.map snd parsed
            Target = str h "target"
            Graph = int' h "graph"
            Checkpoint = str h "checkpoint"
            SeqFrom = int' h "seqFrom"
            Snapshot = snapshot
            Events = Array.map fst parsed
        }
#endif
