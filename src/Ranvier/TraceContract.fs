namespace Ranvier

/// <summary>An application codec for payload values of one shape.</summary>
/// <remarks>
/// <c>Encode</c> claims a value by returning its data, and returns <c>None</c> for a value of another shape.
/// <c>Decode</c> inverts <c>Encode</c> for data written by the same <c>Version</c>.
/// </remarks>
type TraceCodec =
    {
        Name: string
        Version: int
        Encode: obj -> string option
        Decode: string -> obj
    }

/// <summary>A payload that a <c>TraceCodecs</c> list will not encode or decode.</summary>
type TraceCodecException(message: string) =
    inherit System.Exception(message)

/// <summary>An ordered codec list, and whether a value claimed by none of them raises.</summary>
type TraceCodecs =
    {
        /// <summary>The codecs, tried in order: the first whose <c>Encode</c> returns <c>Some</c> encodes the value.</summary>
        Codecs: TraceCodec list
        /// <summary>True when a value claimed by no codec raises <c>TraceCodecException</c> instead of encoding as <c>Lossy</c>.</summary>
        Strict: bool
    }

/// <summary>Encoding and decoding of trace payloads through a <c>TraceCodecs</c> list.</summary>
[<RequireQualifiedAccess>]
module TraceCodecs =
    /// <summary>The non-strict list of <c>codecs</c>.</summary>
    let ofList (codecs: TraceCodec list) : TraceCodecs =
        { Codecs = codecs; Strict = false }

    /// <summary><c>codecs</c> with a value claimed by no codec raising <c>TraceCodecException</c>.</summary>
    let strict (codecs: TraceCodecs) : TraceCodecs =
        { codecs with Strict = true }

    /// <summary>
    /// The payload of <c>value</c>: <c>Null</c>, <c>Exact</c> for the four scalar types, <c>Encoded</c> by the first
    /// codec that claims it, else <c>Lossy</c> of its <c>valueText</c>.
    /// </summary>
    /// <remarks>
    /// Under Fable an <c>int</c> and a <c>float</c> share one runtime representation: a number with an integral
    /// value in the 32-bit signed range encodes as <c>Int</c>, and every other number, NaN and the infinities
    /// included, encodes as <c>Float</c>. A <c>float</c> such as <c>2.0</c> therefore encodes as <c>Int 2</c> on Fable
    /// and as <c>Float 2.0</c> on .NET.
    /// </remarks>
    /// <exception cref="T:Ranvier.TraceCodecException">The list is strict and no codec claims <c>value</c>.</exception>
    let encode (codecs: TraceCodecs) (value: obj) : TracePayload =
        match value with
        | null -> TracePayload.Null
        | :? string as s -> TracePayload.Exact (TraceScalar.String s)
        | :? bool as b -> TracePayload.Exact (TraceScalar.Bool b)
#if FABLE_COMPILER
        | value when Platform.isInt32 value -> TracePayload.Exact (TraceScalar.Int (unbox<int> value))
        | :? float as f -> TracePayload.Exact (TraceScalar.Float f)
#else
        | :? int as i -> TracePayload.Exact (TraceScalar.Int i)
        | :? float as f -> TracePayload.Exact (TraceScalar.Float f)
#endif
        | value ->
            match
                codecs.Codecs
                |> List.tryPick (fun c -> c.Encode value |> Option.map (fun data -> c, data))
            with
            | Some (c, data) -> TracePayload.Encoded (c.Name, c.Version, data)
            | None when codecs.Strict ->
                let shape =
#if FABLE_COMPILER
                    TraceModel.valueText value
#else
                    value.GetType().FullName
#endif
                raise (TraceCodecException ("No trace codec encodes a value of " + shape + "."))
            | None -> TracePayload.Lossy (TraceModel.valueText value)

    /// <summary>The value of <c>payload</c>: null, the scalar, or the value decoded by its codec and version.</summary>
    /// <exception cref="T:Ranvier.TraceCodecException">
    /// The payload is <c>Lossy</c>, or its codec and version are not in the list.
    /// </exception>
    let decode (codecs: TraceCodecs) (payload: TracePayload) : obj =
        match payload with
        | TracePayload.Null -> null
        | TracePayload.Exact (TraceScalar.Int i) -> box i
        | TracePayload.Exact (TraceScalar.String s) -> box s
        | TracePayload.Exact (TraceScalar.Bool b) -> box b
        | TracePayload.Exact (TraceScalar.Float f) -> box f
        | TracePayload.Encoded (name, version, data) ->
            match
                codecs.Codecs
                |> List.tryFind (fun c -> c.Name = name && c.Version = version)
            with
            | Some c -> c.Decode data
            | None ->
                raise (
                    TraceCodecException (
                        "No trace codec decodes "
                        + name
                        + " version "
                        + string version
                        + "."
                    )
                )
        | TracePayload.Lossy text -> raise (TraceCodecException ("A lossy payload holds text alone: " + text))

/// <summary>How far a node's identity path is stable.</summary>
[<RequireQualifiedAccess>]
type TracePathStability =
    /// <summary>
    /// Every segment is a label, a <c>createRoot</c> scope or a node kind. Equal across reruns and across code edits
    /// that keep the labelled tree and its creation order.
    /// </summary>
    | Named
    /// <summary>
    /// A segment is a creation site. Equal across reruns of the same build; an edit that moves the site changes it.
    /// </summary>
    /// <remarks>Fable records no creation sites, so every ref under Fable is <c>Named</c>.</remarks>
    | Located

/// <summary>A node's identity in each of its scopes.</summary>
/// <remarks>
/// Every field is graph-local: two graphs built by the same program give equal refs. A caller holding refs from
/// several graphs keeps each with its graph.
/// </remarks>
type TraceNodeRef =
    {
        /// <summary>The graph-local node id, valid for the graph instance alone.</summary>
        Node: int
        /// <summary>The identity path, with its <c>@k</c> suffix when more than one node has held the path.</summary>
        Path: string
        Stability: TracePathStability
        Label: string option
    }

/// <summary>A node address in a trace lookup.</summary>
[<RequireQualifiedAccess>]
type TraceAddress =
    /// <summary>A graph-local node id.</summary>
    | ById of node: int
    /// <summary>An identity path, resolved as by <c>TraceModel.resolve</c>.</summary>
    | ByPath of path: string

/// <summary>The node at a <c>TraceAddress</c> in a graph's checkpoint and retained events.</summary>
[<RequireQualifiedAccess>]
type TraceLookup =
    /// <summary>A live node created in the retained events.</summary>
    | Live of TraceNodeRef
    /// <summary>A disposed node: the <c>Dispose</c> seq, or 0 when the dispose is folded into the checkpoint.</summary>
    | Disposed of node: TraceNodeRef * seq: int
    /// <summary>A live node created before the checkpoint: its <c>NodeNew</c> and earlier causes are folded into it.</summary>
    | Checkpointed of TraceNodeRef
    /// <summary>
    /// The address matches no node in the checkpoint or the retained events, including a node released by the
    /// checkpoint.
    /// </summary>
    | Missing
    /// <summary>A node created by another graph, or a node that records no <c>NodeNew</c>.</summary>
    | Foreign

/// <summary>A node in a <c>TraceGraphState</c>.</summary>
type TraceNodeState =
    {
        Ref: TraceNodeRef
        Kind: TraceNodeKind
        /// <summary>The owner id recorded by the node's <c>NodeNew</c>, or 0.</summary>
        Owner: int
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
        /// The latest value or exception recorded by a moving <c>Write</c>, a <c>Moved</c>, or an async source's
        /// <c>Settle</c> or <c>Fail</c>, or <c>None</c>. A value folded into the checkpoint is <c>Lossy</c>.
        /// A memo, async memo, boundary or timed node that moved to a null value, and an async source settled to
        /// null, give <c>Null</c>; a projection's
        /// <c>Moved</c> carries no value and gives <c>None</c>; a lookup cell's null <c>Moved</c>, which records
        /// a null value or a suspension, gives <c>Lossy "null"</c>.
        /// </summary>
        Value: TracePayload option
    }

/// <summary>A graph's state at <c>Seq</c>, folded from its checkpoint and retained events.</summary>
type TraceGraphState =
    {
        /// <summary>The contract schema version.</summary>
        Schema: int
        /// <summary>The seq of the last event folded, or 0.</summary>
        Seq: int
        /// <summary>The graph root's owner id.</summary>
        Root: int
        /// <summary>The checkpoint's name, or <c>None</c> before the first checkpoint.</summary>
        Checkpoint: string option
        /// <summary>The seq of the first retained event.</summary>
        SeqFrom: int
        Nodes: Map<int, TraceNodeState>
        Owners: Map<int, TraceSnapshotOwner>
        /// <summary>Each computation's sources, in slot order. Empty lists are left out.</summary>
        Sources: Map<int, int list>
        /// <summary>Each source's observers. Empty sets are left out.</summary>
        Observers: Map<int, Set<int>>
    }

/// <summary>One run of a node, in <c>Trace.runs</c>.</summary>
/// <remarks>A literal names the type in its first label, so an unqualified <c>TraceRun</c> literal stays a <c>TraceRun</c>.</remarks>
[<RequireQualifiedAccess>]
type TraceRunState =
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
        Value: TracePayload option
    }

/// <summary>One event of a <c>TraceWhy</c> chain.</summary>
[<RequireQualifiedAccess>]
type TraceWhyStep =
    {
        Seq: int
        Kind: TraceEventKind
        Node: int
        Other: int
        /// <summary>The value or exception recorded by a <c>Write</c>, <c>Moved</c>, <c>Settle</c> or <c>Fail</c>.</summary>
        Value: TracePayload option
    }

/// <summary>The cause chain of one run, in <c>Trace.whyWith</c>: a <c>Why</c> with its step values encoded.</summary>
[<RequireQualifiedAccess>]
type TraceWhy =
    {
        Node: int
        /// <summary>The run number explained.</summary>
        Run: int
        /// <summary>The steps from the run's <c>RunStart</c> outwards, newest first.</summary>
        Steps: TraceWhyStep list
        /// <summary>The chain's end, or <c>None</c> when <c>whyDepth</c> stopped it first.</summary>
        Root: WhyRoot option
    }

/// <summary>Contract values over a trace snapshot.</summary>
[<RequireQualifiedAccess>]
module TraceContract =
    // The last segment of an owner path, without its `#n` sibling suffix.
    let private bareSegment (path: string) =
        let mutable start = 0
        let mutable hash = -1
        let mutable i = 0

        while i < path.Length do
            match path[i] with
            | '\\' -> i <- i + 1
            | '/' ->
                start <- i + 1
                hash <- -1
            | '#' -> hash <- i
            | _ -> ()

            i <- i + 1

        if hash >= 0 then
            path.Substring (start, hash - start)
        else
            path.Substring start

    let rec private nodeLocated (s: TraceSnapshot) (node: int) =
        match s.Nodes.TryFind node with
        | Some n ->
            (n.Label.IsNone && n.Site <> "?")
            || ownerLocated s n.Owner
        | None -> false

    and private ownerLocated (s: TraceSnapshot) (owner: int) =
        let owner = if owner = 0 then s.Root else owner

        match s.Owners.TryFind owner with
        | None -> false
        | Some o when o.Id = s.Root -> false
        | Some o when o.Host <> 0 -> nodeLocated s o.Host
        | Some o when o.Root -> ownerLocated s o.Parent
        | Some o ->
            (o.Label.IsNone && bareSegment o.Path <> "owner")
            || ownerLocated s o.Parent

    /// <summary>The ref of node <c>node</c> in <c>snapshot</c>, or <c>None</c> when the snapshot does not hold it.</summary>
    let nodeRef (snapshot: TraceSnapshot) (node: int) : TraceNodeRef option =
        snapshot.Nodes.TryFind node
        |> Option.map (fun n ->
            {
                Node = node
                Path = TraceModel.pathOf snapshot node
                Stability =
                    if nodeLocated snapshot node then
                        TracePathStability.Located
                    else
                        TracePathStability.Named
                Label = n.Label
            })

    /// <summary>The node at <c>address</c> in <c>checkpoint</c> advanced by <c>events</c>, which follow it.</summary>
    let lookup (checkpoint: TraceSnapshot) (events: TraceEvent[]) (address: TraceAddress) : TraceLookup =
        let snap = TraceModel.fold checkpoint events

        let id =
            match address with
            | TraceAddress.ById id when snap.Nodes.ContainsKey id -> Some id
            | TraceAddress.ById _ -> None
            | TraceAddress.ByPath path -> TraceModel.resolve snap path

        match
            id
            |> Option.bind (fun id ->
                nodeRef snap id
                |> Option.map (fun r -> r, snap.Nodes[id]))
        with
        | None -> TraceLookup.Missing
        | Some (r, n) when n.Status = TraceNodeStatus.Disposed ->
            let seq =
                events
                |> Array.tryFindBack (fun e -> e.Kind = TraceEventKind.Dispose && e.Node = r.Node)
                |> Option.map _.Seq
                |> Option.defaultValue 0

            TraceLookup.Disposed (r, seq)
        | Some (r, n) when n.Created <= checkpoint.Seq -> TraceLookup.Checkpointed r
        | Some (r, _) -> TraceLookup.Live r

    /// <summary>The schema version of the contract documents.</summary>
    [<Literal>]
    let Schema = 3

    // A node's latest value event in the retained events.
    [<RequireQualifiedAccess>]
    type private RetainedValue =
        | Value of obj
        // A lookup cell's null `Moved`: a null value or a suspension.
        | Ambiguous
        // A `Moved` that carries no value, such as a projection's.
        | Unrecorded

    /// <summary>
    /// The state folded from <c>checkpoint</c> and <c>events</c>, which follow it, with retained values encoded by
    /// <c>codecs</c>.
    /// </summary>
    /// <exception cref="T:Ranvier.TraceCodecException">
    /// <c>codecs</c> is strict and no codec claims a retained value.
    /// </exception>
    let state (codecs: TraceCodecs) (checkpoint: TraceCheckpoint) (events: TraceEvent[]) : TraceGraphState =
        let snap = TraceModel.fold checkpoint.Snapshot events
        let values = System.Collections.Generic.Dictionary<int, RetainedValue>()

        // The latest value event per node.
        for e in events do
            match e.Kind with
            | TraceEventKind.Write when e.Flag = 1 -> values[e.Node] <- RetainedValue.Value e.Payload
            | TraceEventKind.Moved when not (Platform.isNull e.Payload) -> values[e.Node] <- RetainedValue.Value e.Payload
            | TraceEventKind.Moved ->
                values[e.Node] <-
                    match snap.Nodes.TryFind e.Node with
                    | Some n when
                        n.Kind = TraceNodeKind.Memo
                        || n.Kind = TraceNodeKind.AsyncMemo
                        || n.Kind = TraceNodeKind.Boundary
                        || n.Kind = TraceNodeKind.Timed
                        ->
                        RetainedValue.Value null
                    | Some n when n.Kind = TraceNodeKind.LookupCell -> RetainedValue.Ambiguous
                    | _ -> RetainedValue.Unrecorded
            | TraceEventKind.Settle
            | TraceEventKind.Fail when e.Arg = 0 -> values[e.Node] <- RetainedValue.Value e.Payload
            | _ -> ()

        let node (id: int) (n: TraceSnapshotNode) =
            {
                Ref = (nodeRef snap id).Value
                Kind = n.Kind
                Owner = n.Owner
                Created = n.Created
                Runs = n.Runs
                LastRun = n.LastRun
                PendingMark = n.PendingMark
                Status = n.Status
                Value =
                    match values.TryGetValue id with
                    | true, RetainedValue.Value value -> Some (TraceCodecs.encode codecs value)
                    | true, RetainedValue.Ambiguous -> Some (TracePayload.Lossy (TraceModel.valueText null))
                    | true, RetainedValue.Unrecorded -> None
                    | _ -> n.Value |> Option.map TracePayload.Lossy
            }

        {
            Schema = Schema
            Seq = snap.Seq
            Root = snap.Root
            Checkpoint = Option.ofObj checkpoint.Name
            SeqFrom =
                if events.Length = 0 then
                    checkpoint.Snapshot.Seq + 1
                else
                    events[0].Seq
            Nodes = snap.Nodes |> Map.map node
            Owners = snap.Owners
            Sources = snap.Sources
            Observers = snap.Observers
        }

    /// <summary>The runs of <c>history</c>, with their values encoded by <c>codecs</c>.</summary>
    /// <exception cref="T:Ranvier.TraceCodecException"><c>codecs</c> is strict and no codec claims a run's value.</exception>
    let runs (codecs: TraceCodecs) (history: TraceHistory) : TraceRunState list =
        history.Runs
        |> List.map (fun r ->
            {
                TraceRunState.Run = r.Run
                Start = r.Start
                Status = r.Status
                Moved = r.Moved
                Root = r.Root
                Flush = r.Flush
                Value = r.Value |> Option.map (TraceCodecs.encode codecs)
            })

    /// <summary><c>why</c> with its step values encoded by <c>codecs</c>.</summary>
    /// <exception cref="T:Ranvier.TraceCodecException"><c>codecs</c> is strict and no codec claims a step's value.</exception>
    let why (codecs: TraceCodecs) (why: Why) : TraceWhy =
        {
            TraceWhy.Node = why.Node
            Run = why.Run
            Steps =
                why.Steps
                |> List.map (fun s ->
                    {
                        TraceWhyStep.Seq = s.Seq
                        Kind = s.Kind
                        Node = s.Node
                        Other = s.Other
                        Value = s.Value |> Option.map (TraceCodecs.encode codecs)
                    })
            Root = why.Root
        }

    /// <summary>
    /// The schema 3 JSONL dump of <c>before</c> and <c>events</c>, as <c>TraceModel.dumpText</c> writes it, with each
    /// payload written in the form <c>codecs</c> gives.
    /// </summary>
    /// <remarks>
    /// A payload is <c>null</c> or an object: <c>{"form":"exact","type":"int"|"string"|"bool"|"float","value":V}</c>,
    /// <c>{"form":"encoded","codec":C,"version":N,"data":D}</c> or <c>{"form":"lossy","text":T}</c>.
    /// </remarks>
    /// <exception cref="T:Ranvier.TraceCodecException"><c>codecs</c> is strict and no codec claims a payload.</exception>
    let dumpTextWith (codecs: TraceCodecs) (target: string) (checkpoint: string) (before: TraceSnapshot) (events: TraceEvent[]) : string =
        TraceModel.dumpTextEncoded (Some (TraceCodecs.encode codecs)) target checkpoint before events

#if !FABLE_COMPILER
    /// <summary>
    /// The dump of <c>TraceModel.parseDump</c>, with each event payload decoded by <c>codecs</c> from its form in
    /// <c>Payloads</c>; a <c>Lossy</c> payload stays its text.
    /// </summary>
    /// <exception cref="T:System.FormatException">The text is not a supported trace dump.</exception>
    /// <exception cref="T:Ranvier.TraceCodecException">
    /// A payload's codec and version are not in <c>codecs</c>, or <c>codecs</c> is strict and a payload is <c>Lossy</c>.
    /// </exception>
    let parseDumpWith (codecs: TraceCodecs) (text: string) : TraceDump =
        let dump = TraceModel.parseDump text

        let decode (i: int) (e: TraceEvent) =
            if e.Kind = TraceEventKind.TimedState then
                e
            else
                match dump.Payloads[i] with
                | TracePayload.Lossy text when not codecs.Strict -> { e with Payload = box text }
                | payload ->
                    { e with
                        Payload = TraceCodecs.decode codecs payload
                    }

        { dump with
            Events = Array.mapi decode dump.Events
        }
#endif

/// <summary>A contract document read by a <c>TraceJson</c> reader.</summary>
type TraceDocument<'T> =
    {
        /// <summary>The contract schema version the document was written in.</summary>
        Schema: int
        /// <summary>The document kind: <c>state</c>, <c>why</c>, <c>whyNot</c>, <c>waiting</c>, <c>runs</c> or <c>lookup</c>.</summary>
        Kind: string
        /// <summary>The seq the document was read at.</summary>
        Seq: int
        Body: 'T
    }

/// <summary>JSON text of the trace contract documents.</summary>
/// <remarks>
/// A document is <c>{"schema":3,"kind":K,"seq":S,"body":B}</c>, where <c>seq</c> is the read point. In a body a
/// record is an object with camel-case field names; a union case is <c>{"case":"Name",...}</c> with its named fields;
/// an enum is its case name; an option is <c>null</c> or its value; a list is an array; <c>nodes</c> and
/// <c>owners</c> are arrays of objects in id order; <c>sources</c> and <c>observers</c> are arrays of
/// <c>[id,[ids]]</c> pairs in id order. A float NaN or infinity is the string <c>"NaN"</c>, <c>"Infinity"</c> or
/// <c>"-Infinity"</c>. The same value gives the same text.
/// </remarks>
[<RequireQualifiedAccess>]
module TraceJson =
    let private quote (text: string) : string =
        if Platform.isNull text then
            "null"
        else
            let sb = System.Text.StringBuilder (text.Length + 2)
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

            sb.Append('"').ToString()

    let private number (value: float) : string =
        if System.Double.IsNaN value then
            "\"NaN\""
        elif value = infinity then
            "\"Infinity\""
        elif value = -infinity then
            "\"-Infinity\""
        else
#if FABLE_COMPILER
            Fable.Core.JsInterop.emitJsExpr value "String($0)"
#else
            value.ToString ("R", System.Globalization.CultureInfo.InvariantCulture)
#endif

    let private bool' (value: bool) =
        if value then "true" else "false"

    let private obj' (fields: (string * string) list) =
        "{"
        + (fields
           |> List.map (fun (name, value) -> "\"" + name + "\":" + value)
           |> String.concat ",")
        + "}"

    let private array (items: string seq) =
        "[" + String.concat "," items + "]"

    let private ints (values: int seq) =
        values |> Seq.map string |> array

    let private option (write: 'T -> string) (value: 'T option) =
        match value with
        | Some v -> write v
        | None -> "null"

    let private case (name: string) (fields: (string * string) list) =
        obj' (("case", quote name) :: fields)

    let private scalar (value: TraceScalar) =
        match value with
        | TraceScalar.Int i -> case "Int" [ "value", string i ]
        | TraceScalar.String s -> case "String" [ "value", quote s ]
        | TraceScalar.Bool b -> case "Bool" [ "value", bool' b ]
        | TraceScalar.Float f -> case "Float" [ "value", number f ]

    let private payload (value: TracePayload) =
        match value with
        | TracePayload.Null -> case "Null" []
        | TracePayload.Exact s -> case "Exact" [ "value", scalar s ]
        | TracePayload.Encoded (codec, version, data) -> case "Encoded" [ "codec", quote codec; "version", string version; "data", quote data ]
        | TracePayload.Lossy text -> case "Lossy" [ "text", quote text ]

    let private root (value: WhyRoot) =
        match value with
        | UserWrite write -> case "UserWrite" [ "write", string write ]
        | Created nodeNew -> case "Created" [ "nodeNew", string nodeNew ]
        | Pulled reader -> case "Pulled" [ "reader", string reader ]
        | BeforeCheckpoint file -> case "BeforeCheckpoint" [ "file", quote file ]
        | Unrecorded last -> case "Unrecorded" [ "last", string last ]
        | Settled settle -> case "Settled" [ "settle", string settle ]

    let private runStatus (value: RunStatus) =
        quote (TraceNames.runStatus value)

    let private stability (value: TracePathStability) =
        match value with
        | TracePathStability.Named -> case "Named" []
        | TracePathStability.Located -> case "Located" []

    let private nodeRef (value: TraceNodeRef) =
        obj'
            [
                "node", string value.Node
                "path", quote value.Path
                "stability", stability value.Stability
                "label", option quote value.Label
            ]

    let private status (value: TraceNodeStatus) =
        match value with
        | TraceNodeStatus.Fresh -> case "Fresh" []
        | TraceNodeStatus.Running -> case "Running" []
        | TraceNodeStatus.Ended s -> case "Ended" [ "status", runStatus s ]
        | TraceNodeStatus.Disposed -> case "Disposed" []

    let private node (value: TraceNodeState) =
        obj'
            [
                "ref", nodeRef value.Ref
                "kind", quote (TraceNames.nodeKind value.Kind)
                "owner", string value.Owner
                "created", string value.Created
                "runs", string value.Runs
                "lastRun", string value.LastRun
                "pendingMark", string value.PendingMark
                "status", status value.Status
                "value", option payload value.Value
            ]

    let private owner (value: TraceSnapshotOwner) =
        obj'
            [
                "id", string value.Id
                "parent", string value.Parent
                "host", string value.Host
                "root", bool' value.Root
                "path", quote value.Path
                "label", option quote value.Label
                "disposed", bool' value.Disposed
            ]

    let private pairs (write: 'T -> string) (map: Map<int, 'T>) =
        map
        |> Seq.map (fun (KeyValue (id, value)) -> "[" + string id + "," + write value + "]")
        |> array

    let private document (kind: string) (seq: int) (body: string) =
        obj'
            [
                "schema", string TraceContract.Schema
                "kind", quote kind
                "seq", string seq
                "body", body
            ]

    /// <summary>The <c>state</c> document of <c>state</c>, read at <c>state.Seq</c>.</summary>
    /// <remarks>The body holds every field except <c>Schema</c> and <c>Seq</c>, which the envelope carries.</remarks>
    let state (state: TraceGraphState) : string =
        obj'
            [
                "root", string state.Root
                "checkpoint", option quote state.Checkpoint
                "seqFrom", string state.SeqFrom
                "nodes", state.Nodes.Values |> Seq.map node |> array
                "owners", state.Owners.Values |> Seq.map owner |> array
                "sources", pairs ints state.Sources
                "observers", pairs ints state.Observers
            ]
        |> document "state" state.Seq

    /// <summary>The <c>why</c> document of <c>why</c>, read at <c>seq</c>.</summary>
    let why (seq: int) (why: TraceWhy) : string =
        let step (s: TraceWhyStep) =
            obj'
                [
                    "seq", string s.Seq
                    "kind", quote (TraceNames.eventKind s.Kind)
                    "node", string s.Node
                    "other", string s.Other
                    "value", option payload s.Value
                ]

        obj'
            [
                "node", string why.Node
                "run", string why.Run
                "steps", why.Steps |> Seq.map step |> array
                "root", option root why.Root
            ]
        |> document "why" seq

    /// <summary>The <c>whyNot</c> document of <c>reason</c>, read at <c>seq</c>.</summary>
    let whyNot (seq: int) (reason: WhyNotReason option) : string =
        let write (reason: WhyNotReason) =
            match reason with
            | WhyNotReason.Disposed s -> case "Disposed" [ "seq", string s ]
            | Queued schedule -> case "Queued" [ "schedule", string schedule ]
            | Unobserved mark -> case "Unobserved" [ "mark", string mark ]
            | CheckedClean (resolvedAt, upstream) -> case "CheckedClean" [ "resolvedAt", string resolvedAt; "upstream", ints upstream ]
            | SkippedAsRunningReader s -> case "SkippedAsRunningReader" [ "seq", string s ]
            | NotReached stopAt -> case "NotReached" [ "stopAt", string stopAt ]

        option write reason |> document "whyNot" seq

    /// <summary>The <c>waiting</c> document of <c>waiting</c>, read at <c>seq</c>.</summary>
    let waiting (seq: int) (waiting: TraceWaiting) : string =
        let state (value: TraceFlightState) =
            match value with
            | TraceFlightState.InFlight -> case "InFlight" []
            | TraceFlightState.Settled (s, held) -> case "Settled" [ "seq", string s; "held", bool' held ]
            | TraceFlightState.Failed (s, cancelled) -> case "Failed" [ "seq", string s; "cancelled", bool' cancelled ]
            | TraceFlightState.Dropped (s, reason) -> case "Dropped" [ "seq", string s; "reason", quote (TraceNames.dropReason reason) ]

        let flight (f: TraceFlight) =
            obj'
                [
                    "flight", string f.Flight
                    "start", string f.Start
                    "run", string f.Run
                    "state", state f.State
                ]

        obj'
            [
                "node", string waiting.Node
                "sources", ints waiting.Sources
                "flights", waiting.Flights |> Seq.map flight |> array
            ]
        |> document "waiting" seq

    /// <summary>The <c>runs</c> document of <c>runs</c>, read at <c>seq</c>: an array of runs, oldest first.</summary>
    let runs (seq: int) (runs: TraceRunState list) : string =
        let run (r: TraceRunState) =
            obj'
                [
                    "run", string r.Run
                    "start", string r.Start
                    "status", option runStatus r.Status
                    "moved", bool' r.Moved
                    "root", root r.Root
                    "flush", string r.Flush
                    "value", option payload r.Value
                ]

        runs
        |> Seq.map run
        |> array
        |> document "runs" seq

    /// <summary>The <c>lookup</c> document of <c>lookup</c>, read at <c>seq</c>.</summary>
    let lookup (seq: int) (lookup: TraceLookup) : string =
        let body =
            match lookup with
            | TraceLookup.Live r -> case "Live" [ "node", nodeRef r ]
            | TraceLookup.Disposed (r, s) -> case "Disposed" [ "node", nodeRef r; "seq", string s ]
            | TraceLookup.Checkpointed r -> case "Checkpointed" [ "node", nodeRef r ]
            | TraceLookup.Missing -> case "Missing" []
            | TraceLookup.Foreign -> case "Foreign" []

        document "lookup" seq body

#if !FABLE_COMPILER
    // -----------------------------------------------------------------------------------------------------------
    // Readers (.NET only)

    type private Json = System.Text.Json.JsonElement

    let private malformed (message: string) : 'T =
        raise (System.FormatException message)

    let private prop (e: Json) (name: string) =
        match e.TryGetProperty name with
        | true, p -> p
        | _ -> malformed ("A trace document field is missing: " + name + ".")

    let private isNull' (e: Json) =
        e.ValueKind = System.Text.Json.JsonValueKind.Null

    let private readStr (e: Json) =
        if isNull' e then null else e.GetString ()

    let private readInt (e: Json) =
        e.GetInt32 ()

    let private readBool (e: Json) =
        e.GetBoolean ()

    let private readOption (read: Json -> 'T) (e: Json) =
        if isNull' e then None else Some (read e)

    let private readList (read: Json -> 'T) (e: Json) =
        [ for item in e.EnumerateArray () -> read item ]

    let private readEnum<'T when 'T: struct and 'T: (new: unit -> 'T) and 'T :> System.ValueType> (e: Json) : 'T =
        match System.Enum.TryParse<'T>(e.GetString ()) with
        | true, value -> value
        | _ ->
            malformed (
                "A trace document holds an unknown "
                + typeof<'T>.Name
                + ": "
                + e.GetString ()
                + "."
            )

    let private caseOf (e: Json) =
        (prop e "case").GetString()

    let private unknownCase (union: string) (e: Json) : 'T =
        malformed (
            "A trace document holds an unknown "
            + union
            + " case: "
            + caseOf e
            + "."
        )

    let private readNumber (e: Json) =
        if e.ValueKind = System.Text.Json.JsonValueKind.String then
            match e.GetString () with
            | "NaN" -> nan
            | "Infinity" -> infinity
            | "-Infinity" -> -infinity
            | other ->
                malformed (
                    "A trace document holds an unknown float: "
                    + other
                    + "."
                )
        else
            e.GetDouble ()

    let private readScalar (e: Json) =
        let value = prop e "value"

        match caseOf e with
        | "Int" -> TraceScalar.Int (readInt value)
        | "String" -> TraceScalar.String (readStr value)
        | "Bool" -> TraceScalar.Bool (readBool value)
        | "Float" -> TraceScalar.Float (readNumber value)
        | _ -> unknownCase "TraceScalar" e

    let private readPayload (e: Json) =
        match caseOf e with
        | "Null" -> TracePayload.Null
        | "Exact" -> TracePayload.Exact (readScalar (prop e "value"))
        | "Encoded" -> TracePayload.Encoded (readStr (prop e "codec"), readInt (prop e "version"), readStr (prop e "data"))
        | "Lossy" -> TracePayload.Lossy (readStr (prop e "text"))
        | _ -> unknownCase "TracePayload" e

    let private readRoot (e: Json) =
        match caseOf e with
        | "UserWrite" -> UserWrite (readInt (prop e "write"))
        | "Created" -> Created (readInt (prop e "nodeNew"))
        | "Pulled" -> Pulled (readInt (prop e "reader"))
        | "BeforeCheckpoint" -> BeforeCheckpoint (readStr (prop e "file"))
        | "Unrecorded" -> Unrecorded (readInt (prop e "last"))
        | "Settled" -> Settled (readInt (prop e "settle"))
        | _ -> unknownCase "WhyRoot" e

    let private readNodeRef (e: Json) : TraceNodeRef =
        {
            Node = readInt (prop e "node")
            Path = readStr (prop e "path")
            Stability =
                let s = prop e "stability"

                match caseOf s with
                | "Named" -> TracePathStability.Named
                | "Located" -> TracePathStability.Located
                | _ -> unknownCase "TracePathStability" s
            Label = readOption readStr (prop e "label")
        }

    let private readStatus (e: Json) =
        match caseOf e with
        | "Fresh" -> TraceNodeStatus.Fresh
        | "Running" -> TraceNodeStatus.Running
        | "Ended" -> TraceNodeStatus.Ended (readEnum<RunStatus>(prop e "status"))
        | "Disposed" -> TraceNodeStatus.Disposed
        | _ -> unknownCase "TraceNodeStatus" e

    let private readNode (e: Json) : TraceNodeState =
        {
            Ref = readNodeRef (prop e "ref")
            Kind = readEnum<TraceNodeKind>(prop e "kind")
            Owner = readInt (prop e "owner")
            Created = readInt (prop e "created")
            Runs = readInt (prop e "runs")
            LastRun = readInt (prop e "lastRun")
            PendingMark = readInt (prop e "pendingMark")
            Status = readStatus (prop e "status")
            Value = readOption readPayload (prop e "value")
        }

    let private readOwner (e: Json) : TraceSnapshotOwner =
        {
            Id = readInt (prop e "id")
            Parent = readInt (prop e "parent")
            Host = readInt (prop e "host")
            Root = readBool (prop e "root")
            Path = readStr (prop e "path")
            Label = readOption readStr (prop e "label")
            Disposed = readBool (prop e "disposed")
        }

    let private readPairs (read: Json -> 'T) (e: Json) =
        [ for pair in e.EnumerateArray () -> readInt pair[0], read pair[1] ]
        |> Map.ofList

    /// <summary>The envelope of <c>text</c>, with its body read by <c>read</c>.</summary>
    /// <exception cref="T:System.FormatException">
    /// The text is not a <c>kind</c> document, or its schema is outside 1 to <c>TraceContract.Schema</c>.
    /// </exception>
    let private read (kind: string) (body: int -> Json -> 'T) (text: string) : TraceDocument<'T> =
        let parsed =
            try
                System.Text.Json.JsonDocument.Parse text
            with :? System.Text.Json.JsonException as e ->
                raise (System.FormatException ("A trace document is not JSON: " + e.Message, e))

        use doc = parsed
        let root = doc.RootElement
        let schema = readInt (prop root "schema")

        if schema < 1 || schema > TraceContract.Schema then
            malformed (
                "The trace document schema "
                + string schema
                + " is unsupported."
            )

        let actual = readStr (prop root "kind")

        if actual <> kind then
            malformed (
                "The trace document is a "
                + actual
                + " document, not a "
                + kind
                + " document."
            )

        let seq = readInt (prop root "seq")

        {
            Schema = schema
            Kind = kind
            Seq = seq
            Body = body schema (prop root "body")
        }

    /// <summary>The <c>state</c> document of <c>text</c>, written by <c>TraceJson.state</c>.</summary>
    /// <exception cref="T:System.FormatException">The text is not a supported <c>state</c> document.</exception>
    let readState (text: string) : TraceDocument<TraceGraphState> =
        let body (schema: int) (e: Json) : TraceGraphState =
            {
                Schema = schema
                Seq = 0
                Root = readInt (prop e "root")
                Checkpoint = readOption readStr (prop e "checkpoint")
                SeqFrom = readInt (prop e "seqFrom")
                Nodes =
                    readList readNode (prop e "nodes")
                    |> List.map (fun n -> n.Ref.Node, n)
                    |> Map.ofList
                Owners =
                    readList readOwner (prop e "owners")
                    |> List.map (fun o -> o.Id, o)
                    |> Map.ofList
                Sources = readPairs (readList readInt) (prop e "sources")
                Observers = readPairs (readList readInt >> Set.ofList) (prop e "observers")
            }

        let d = read "state" body text

        { d with
            Body = { d.Body with Seq = d.Seq }
        }

    /// <summary>The <c>why</c> document of <c>text</c>, written by <c>TraceJson.why</c>.</summary>
    /// <exception cref="T:System.FormatException">The text is not a supported <c>why</c> document.</exception>
    let readWhy (text: string) : TraceDocument<TraceWhy> =
        let step (e: Json) =
            {
                TraceWhyStep.Seq = readInt (prop e "seq")
                TraceWhyStep.Kind = readEnum<TraceEventKind>(prop e "kind")
                TraceWhyStep.Node = readInt (prop e "node")
                TraceWhyStep.Other = readInt (prop e "other")
                TraceWhyStep.Value = readOption readPayload (prop e "value")
            }

        let body _ (e: Json) =
            {
                TraceWhy.Node = readInt (prop e "node")
                TraceWhy.Run = readInt (prop e "run")
                TraceWhy.Steps = readList step (prop e "steps")
                TraceWhy.Root = readOption readRoot (prop e "root")
            }

        read "why" body text

    /// <summary>The <c>whyNot</c> document of <c>text</c>, written by <c>TraceJson.whyNot</c>.</summary>
    /// <exception cref="T:System.FormatException">The text is not a supported <c>whyNot</c> document.</exception>
    let readWhyNot (text: string) : TraceDocument<WhyNotReason option> =
        let reason (e: Json) =
            match caseOf e with
            | "Disposed" -> WhyNotReason.Disposed (readInt (prop e "seq"))
            | "Queued" -> Queued (readInt (prop e "schedule"))
            | "Unobserved" -> Unobserved (readInt (prop e "mark"))
            | "CheckedClean" -> CheckedClean (readInt (prop e "resolvedAt"), readList readInt (prop e "upstream"))
            | "SkippedAsRunningReader" -> SkippedAsRunningReader (readInt (prop e "seq"))
            | "NotReached" -> NotReached (readInt (prop e "stopAt"))
            | _ -> unknownCase "WhyNotReason" e

        read "whyNot" (fun _ e -> readOption reason e) text

    /// <summary>The <c>waiting</c> document of <c>text</c>, written by <c>TraceJson.waiting</c>.</summary>
    /// <exception cref="T:System.FormatException">The text is not a supported <c>waiting</c> document.</exception>
    let readWaiting (text: string) : TraceDocument<TraceWaiting> =
        let state (e: Json) =
            match caseOf e with
            | "InFlight" -> TraceFlightState.InFlight
            | "Settled" -> TraceFlightState.Settled (readInt (prop e "seq"), readBool (prop e "held"))
            | "Failed" -> TraceFlightState.Failed (readInt (prop e "seq"), readBool (prop e "cancelled"))
            | "Dropped" -> TraceFlightState.Dropped (readInt (prop e "seq"), readEnum<TraceDropReason>(prop e "reason"))
            | _ -> unknownCase "TraceFlightState" e

        let flight (e: Json) : TraceFlight =
            {
                Flight = readInt (prop e "flight")
                Start = readInt (prop e "start")
                Run = readInt (prop e "run")
                State = state (prop e "state")
            }

        let body _ (e: Json) : TraceWaiting =
            {
                Node = readInt (prop e "node")
                Sources = readList readInt (prop e "sources")
                Flights = readList flight (prop e "flights")
            }

        read "waiting" body text

    /// <summary>The <c>runs</c> document of <c>text</c>, written by <c>TraceJson.runs</c>.</summary>
    /// <exception cref="T:System.FormatException">The text is not a supported <c>runs</c> document.</exception>
    let readRuns (text: string) : TraceDocument<TraceRunState list> =
        let run (e: Json) =
            {
                TraceRunState.Run = readInt (prop e "run")
                TraceRunState.Start = readInt (prop e "start")
                TraceRunState.Status = readOption readEnum<RunStatus> (prop e "status")
                TraceRunState.Moved = readBool (prop e "moved")
                TraceRunState.Root = readRoot (prop e "root")
                TraceRunState.Flush = readInt (prop e "flush")
                TraceRunState.Value = readOption readPayload (prop e "value")
            }

        read "runs" (fun _ e -> readList run e) text

    /// <summary>The <c>lookup</c> document of <c>text</c>, written by <c>TraceJson.lookup</c>.</summary>
    /// <exception cref="T:System.FormatException">The text is not a supported <c>lookup</c> document.</exception>
    let readLookup (text: string) : TraceDocument<TraceLookup> =
        let body _ (e: Json) =
            match caseOf e with
            | "Live" -> TraceLookup.Live (readNodeRef (prop e "node"))
            | "Disposed" -> TraceLookup.Disposed (readNodeRef (prop e "node"), readInt (prop e "seq"))
            | "Checkpointed" -> TraceLookup.Checkpointed (readNodeRef (prop e "node"))
            | "Missing" -> TraceLookup.Missing
            | "Foreign" -> TraceLookup.Foreign
            | _ -> unknownCase "TraceLookup" e

        read "lookup" body text
#endif
