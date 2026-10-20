namespace Ranvier

/// <summary>F# diagnostic text for trace fields, collections and records.</summary>
module internal TraceText =
    /// <summary>Quotes strings, prints null as <c>null</c>, and calls <c>ToString</c> on other values.</summary>
    let value (value: obj) =
        match value with
        | null -> "null"
        | :? string as s -> "\"" + s + "\""
        | :? bool as b -> if b then "true" else "false"
        | value -> value.ToString ()

    let private sequence render values =
        values |> Seq.map render |> String.concat "; "

    /// <summary>Renders elements between <c>[</c> and <c>]</c>, separated by semicolons.</summary>
    let list render values =
        "[" + sequence render values + "]"

    /// <summary>Renders elements between <c>[|</c> and <c>|]</c>, separated by semicolons.</summary>
    let array render values =
        "[|" + sequence render values + "|]"

    /// <summary>Prints <c>None</c> or <c>Some</c> with the rendered value, parenthesising a case with arguments.</summary>
    let option (render: 'T -> string) value =
        match value with
        | None -> "None"
        | Some v ->
            let text = render v

            if
                text.Contains " "
                && text.Length > 0
                && "([{\"".IndexOf text[0] < 0
            then
                "Some (" + text + ")"
            else
                "Some " + text

    /// <summary>Prints fields in declaration order, one per line, with nested lines indented.</summary>
    let record fields =
        "{ "
        + (fields
           |> List.map (fun (name, text: string) -> name + " = " + text.Replace ("\n", "\n  "))
           |> String.concat "\n  ")
        + " }"

    /// <summary>Prints ordered key/value pairs as <c>map [(key, value); ...]</c>.</summary>
    let map key value entries =
        "map "
        + list (fun (k, v) -> "(" + key k + ", " + value v + ")") (Map.toSeq entries)

    /// <summary>Prints ordered elements as <c>set [value; ...]</c>.</summary>
    let set render values =
        "set " + list render values

/// <summary>What a <c>TraceEvent</c> records. Each kind gives its own meaning to the event's fields.</summary>
type TraceEventKind =
    /// <summary>The graph was constructed. <c>Other</c>: the root owner id.</summary>
    | GraphNew = 1
    /// <summary>
    /// A node was constructed. <c>Other</c>: its owner id, or 0. <c>Arg</c>: its <c>TraceNodeKind</c>. <c>Cause</c>:
    /// the <c>RunStart</c> of the creating run, or 0.
    /// </summary>
    | NodeNew = 2
    /// <summary>
    /// An owner joined the log. <c>Other</c>: its parent owner id, or 0. <c>Arg</c>: the host node id of a run scope,
    /// or 0. <c>Flag</c>: 1 for a <c>createRoot</c> scope. <c>Cause</c>: as for <c>NodeNew</c>.
    /// </summary>
    | OwnerNew = 3
    /// <summary>A node was disposed.</summary>
    | Dispose = 4
    /// <summary>An owner was disposed.</summary>
    | OwnerDispose = 5
    /// <summary>
    /// A <c>Trace.named</c> or <c>Trace.label</c> label. <c>Node</c>: the labelled node or owner id, or 0 when the
    /// thunk created neither. <c>Arg</c>: 1 from <c>Trace.label</c>, else 0. <c>Payload</c>: the label.
    /// </summary>
    | Label = 6
    /// <summary>
    /// A node joined a collection as one of its parts. <c>Other</c>: the collection's node id. <c>Payload</c>: the
    /// part's key, or null for a part shared by every key. <c>Flag</c>: 1 when <c>Node</c> is the owner of a key's nodes.
    /// </summary>
    | Part = 7
    /// <summary>A signal was written. <c>Other</c>: the running computation, or 0. <c>Flag</c>: 1 when the value moved.</summary>
    | Write = 10
    /// <summary>A reader was marked. <c>Other</c>: the source. <c>Arg</c>: 1 check, 2 dirty.</summary>
    | Mark = 11
    /// <summary>A mark skipped the running reader. Fields as for <c>Mark</c>.</summary>
    | MarkSkip = 12
    /// <summary>
    /// A node was queued. <c>Arg</c>: the queue entries ahead of it. For a projection pass or a lookup refresh, the
    /// queued passes; for any other node, the queued passes and the entries queued before it, which include the
    /// internal items that run queued passes.
    /// </summary>
    | Schedule = 13
    /// <summary>A check walk started on a node.</summary>
    | CheckStart = 14
    /// <summary>
    /// A check walk resolved. <c>Other</c>: the source answered dirty, or 0. <c>Flag</c>: 1 dirty, 0 clean.
    /// </summary>
    | CheckResolved = 15
    /// <summary>A computation gained a source. <c>Other</c>: the source. <c>Arg</c>: the slot.</summary>
    | EdgeAdd = 16
    /// <summary>A computation lost a source. Fields as for <c>EdgeAdd</c>.</summary>
    | EdgeRemove = 17
    /// <summary>A source gained an observer. <c>Other</c>: the observer.</summary>
    | ObserverAdd = 18
    /// <summary>A source lost an observer. <c>Other</c>: the observer.</summary>
    | ObserverRemove = 19
    /// <summary>
    /// A run started. <c>Other</c>: the puller, or 0. <c>Arg</c>: the run number, from 1. <c>Cause</c>: the first
    /// dirty <c>Mark</c> since the previous run, or 0.
    /// </summary>
    | RunStart = 20
    /// <summary>A run moved the node's value. <c>Arg</c>: the run number.</summary>
    | Moved = 21
    /// <summary>A run ended. <c>Arg</c>: its <c>RunStatus</c>. <c>Flag</c>: 1 when the run recorded <c>Moved</c>.</summary>
    | RunEnd = 22
    /// <summary>A walker frame was unwound without its pop.</summary>
    | WalkAbandoned = 23
    /// <summary>
    /// A change reached an async memo while a flight is in progress under <c>FinishCurrent</c>, and a trailing run is
    /// owed. <c>Other</c>: the puller, or 0. <c>Arg</c>: the flight in progress. <c>Cause</c>: the first dirty
    /// <c>Mark</c> since the previous run, or 0.
    /// </summary>
    | RunDeferred = 24
    /// <summary>
    /// A change reached an async memo while a flight is in progress under <c>Drop</c> or <c>DropWhilePending</c>, and
    /// was discarded. <c>Other</c>: the puller, or 0. <c>Arg</c>: the flight in progress. <c>Flag</c>: 1 when discarded
    /// as the flight settled, 0 when discarded at a read. <c>Cause</c>: the first dirty <c>Mark</c> since the previous
    /// run, or 0.
    /// </summary>
    | RunDropped = 25
    /// <summary>A flush started. <c>Arg</c>: the flush number.</summary>
    | FlushStart = 30
    /// <summary>A flush ended. <c>Arg</c>: the flush number.</summary>
    | FlushEnd = 31
    /// <summary>A batch opened. <c>Arg</c>: the depth after the change.</summary>
    | BatchEnter = 32
    /// <summary>A batch closed. <c>Arg</c>: the depth after the change.</summary>
    | BatchExit = 33
    /// <summary>A run scope's discharge started. <c>Node</c>: the owner id. <c>Other</c>: the host node id.</summary>
    | DischargeStart = 34
    /// <summary>A run scope's discharge ended. Fields as for <c>DischargeStart</c>.</summary>
    | DischargeEnd = 35
    /// <summary>
    /// A running computation read a pending source. <c>Other</c>: the source. <c>Cause</c>: the reader's
    /// <c>RunStart</c>.
    /// </summary>
    | Suspend = 40
    /// <summary>An async memo started a flight. <c>Arg</c>: the flight number, from 1. <c>Cause</c>: the <c>RunStart</c>.</summary>
    | FlightStart = 41
    /// <summary>
    /// An async value settled. <c>Arg</c>: the flight number, or 0 for an async source. <c>Flag</c>: 1 when the node
    /// stays pending on a newer run suspended on a pending source, 2 when it stays pending on a trailing run owed under
    /// <c>FinishCurrent</c>. <c>Cause</c>: the <c>FlightStart</c>, or 0.
    /// </summary>
    | Settle = 42
    /// <summary>An async value failed. <c>Flag</c>: 1 when cancelled. Other fields as for <c>Settle</c>.</summary>
    | Fail = 43
    /// <summary>
    /// A flight's result was discarded. <c>Arg</c>: the flight number. <c>Flag</c>: a <c>TraceDropReason</c>.
    /// <c>Cause</c>: the <c>FlightStart</c>, or 0.
    /// </summary>
    | FlightDrop = 44
    /// <summary>A captured input. Arg is its Status byte; Cause is its capture RunStart or prior capture for an admission failure.</summary>
    | TimedCaptured = 50
    /// <summary>A window opened or extended. Arg is the admission mode; Payload is the configured duration in milliseconds.</summary>
    | TimedWindow = 51
    /// <summary>A suppressed value. Arg is 1 for equal capture, 2 for equal publication, 3 for leading cooldown.</summary>
    | TimedSuppressed = 52
    /// <summary>A pending candidate was cancelled before admission.</summary>
    | TimedCancelled = 53
    /// <summary>A publication. Arg is its Status byte; Flag marks movement; Cause is its winning capture.</summary>
    | TimedPublished = 54
    /// <summary>A captured/published timing snapshot with offsets from this clock's first timed node in the graph.</summary>
    | TimedState = 55
    /// <summary>A stream memo started a stream. <c>Arg</c>: the generation, from 1. <c>Cause</c>: the run's <c>RunStart</c>.</summary>
    | StreamStarted = 60
    /// <summary>
    /// A stream memo published. <c>Arg</c>: its <c>Status</c> byte. <c>Flag</c>: the items folded since the previous
    /// publication. <c>Payload</c>: the state, or the exception. <c>Cause</c>: the node's open <c>RunStart</c>, else the
    /// latest <c>StreamStarted</c>.
    /// </summary>
    | StreamPublished = 61
    /// <summary>
    /// A stream ended. <c>Arg</c>: a <c>TraceStreamEnd</c>. <c>Cause</c>: the node's open <c>RunStart</c>, else the
    /// latest <c>StreamStarted</c>.
    /// </summary>
    | StreamEnded = 62

/// <summary>The node type a <c>NodeNew</c> event records.</summary>
type TraceNodeKind =
    | Signal = 1
    | AsyncSource = 2
    | Memo = 3
    | Effect = 4
    | AsyncMemo = 5
    | Boundary = 6
    | Projection = 7
    | ProjectionBeacon = 8
    | RowWatch = 9
    | LookupCell = 10
    | Timed = 11
    | Binding = 12
    | Stream = 13

/// <summary>The captured and published state of a timed node at the query or recorded event.</summary>
type TraceTiming =
    {
        /// <summary>The debounce or throttle factory name.</summary>
        Mode: string
        /// <summary>The latest capture's status.</summary>
        CapturedStatus: Status
        /// <summary>The status exposed to tracked readers.</summary>
        PublishedStatus: Status
        /// <summary>True while a candidate or leading cooldown holds the timing gate open.</summary>
        WindowOpen: bool
        /// <summary>Milliseconds until the deadline, clamped to zero.</summary>
        RemainingMilliseconds: float
        /// <summary>The candidate's captured event, or the published state's capture when no candidate is held.</summary>
        WinningCapture: int
    }

    /// <summary>The timing fields, one per line, in F# record syntax.</summary>
    override this.ToString() =
        TraceText.record
            [
                "Mode", TraceText.value this.Mode
                "CapturedStatus", string this.CapturedStatus
                "PublishedStatus", string this.PublishedStatus
                "WindowOpen", TraceText.value this.WindowOpen
                "RemainingMilliseconds", string this.RemainingMilliseconds
                "WinningCapture", string this.WinningCapture
            ]

/// <summary>A recorded timing state and its offset from this clock's first timed node in the graph.</summary>
type internal TraceTimedState =
    {
        Clock: int
        AtMilliseconds: float
        Timing: TraceTiming
    }

    /// <summary>The clock, offset and timing fields in F# record syntax.</summary>
    override this.ToString() =
        TraceText.record
            [
                "Clock", string this.Clock
                "AtMilliseconds", string this.AtMilliseconds
                "Timing", this.Timing.ToString ()
            ]

/// <summary>A timed node's live diagnostic state and graph identity.</summary>
type internal ITimedTrace =
    /// <summary>The node's timing gate and capture/publication states.</summary>
    abstract Timing: TraceTiming
    /// <summary>The graph instance that owns this state.</summary>
    abstract TimingGraph: obj

/// <summary>How a run ended, in <c>RunEnd.Arg</c>.</summary>
type RunStatus =
    | Ok = 0
    | Pending = 1
    | Error = 2
    /// <summary>
    /// The run was still open when its flush ended, an enclosing run ended, the node started its next run, or a dump
    /// ran outside every run.
    /// </summary>
    | Abandoned = 3

/// <summary>Why a flight's result was discarded, in <c>FlightDrop.Flag</c>.</summary>
type TraceDropReason =
    /// <summary>A newer flight started first.</summary>
    | Superseded = 1
    /// <summary>The node was disposed.</summary>
    | Disposed = 2
    /// <summary>A failure arrived while the node's newest run waits on a pending source.</summary>
    | Suspended = 3
    /// <summary>A failure arrived while a trailing run is owed under <c>FinishCurrent</c>.</summary>
    | Trailing = 4
    /// <summary>
    /// A failure arrived after a change was discarded under <c>DropWhilePending</c>, and the node runs once more.
    /// </summary>
    | Retried = 5

/// <summary>Why a stream ended, in <c>StreamEnded.Arg</c>.</summary>
type TraceStreamEnd =
    | Completed = 0
    | Failed = 1
    | Restarted = 2
    | Disposed = 3

/// <summary>The case names of the trace enums, on .NET and under Fable. A value outside the cases gives its number.</summary>
module internal TraceNames =
    /// <summary>The run-status case name, or its number for an unknown value.</summary>
    let runStatus (value: RunStatus) =
        match value with
        | RunStatus.Ok -> "Ok"
        | RunStatus.Pending -> "Pending"
        | RunStatus.Error -> "Error"
        | RunStatus.Abandoned -> "Abandoned"
        | other -> string (int other)

    let eventKind (value: TraceEventKind) : string =
#if FABLE_COMPILER
        match value with
        | TraceEventKind.GraphNew -> "GraphNew"
        | TraceEventKind.NodeNew -> "NodeNew"
        | TraceEventKind.OwnerNew -> "OwnerNew"
        | TraceEventKind.Dispose -> "Dispose"
        | TraceEventKind.OwnerDispose -> "OwnerDispose"
        | TraceEventKind.Label -> "Label"
        | TraceEventKind.Part -> "Part"
        | TraceEventKind.Write -> "Write"
        | TraceEventKind.Mark -> "Mark"
        | TraceEventKind.MarkSkip -> "MarkSkip"
        | TraceEventKind.Schedule -> "Schedule"
        | TraceEventKind.CheckStart -> "CheckStart"
        | TraceEventKind.CheckResolved -> "CheckResolved"
        | TraceEventKind.EdgeAdd -> "EdgeAdd"
        | TraceEventKind.EdgeRemove -> "EdgeRemove"
        | TraceEventKind.ObserverAdd -> "ObserverAdd"
        | TraceEventKind.ObserverRemove -> "ObserverRemove"
        | TraceEventKind.RunStart -> "RunStart"
        | TraceEventKind.Moved -> "Moved"
        | TraceEventKind.RunEnd -> "RunEnd"
        | TraceEventKind.WalkAbandoned -> "WalkAbandoned"
        | TraceEventKind.RunDeferred -> "RunDeferred"
        | TraceEventKind.RunDropped -> "RunDropped"
        | TraceEventKind.FlushStart -> "FlushStart"
        | TraceEventKind.FlushEnd -> "FlushEnd"
        | TraceEventKind.BatchEnter -> "BatchEnter"
        | TraceEventKind.BatchExit -> "BatchExit"
        | TraceEventKind.DischargeStart -> "DischargeStart"
        | TraceEventKind.DischargeEnd -> "DischargeEnd"
        | TraceEventKind.Suspend -> "Suspend"
        | TraceEventKind.FlightStart -> "FlightStart"
        | TraceEventKind.Settle -> "Settle"
        | TraceEventKind.Fail -> "Fail"
        | TraceEventKind.FlightDrop -> "FlightDrop"
        | TraceEventKind.TimedCaptured -> "TimedCaptured"
        | TraceEventKind.TimedWindow -> "TimedWindow"
        | TraceEventKind.TimedSuppressed -> "TimedSuppressed"
        | TraceEventKind.TimedCancelled -> "TimedCancelled"
        | TraceEventKind.TimedPublished -> "TimedPublished"
        | TraceEventKind.TimedState -> "TimedState"
        | TraceEventKind.StreamStarted -> "StreamStarted"
        | TraceEventKind.StreamPublished -> "StreamPublished"
        | TraceEventKind.StreamEnded -> "StreamEnded"
        | other -> string (int other)
#else
        string value
#endif

    let nodeKind (value: TraceNodeKind) : string =
#if FABLE_COMPILER
        match value with
        | TraceNodeKind.Signal -> "Signal"
        | TraceNodeKind.AsyncSource -> "AsyncSource"
        | TraceNodeKind.Memo -> "Memo"
        | TraceNodeKind.Effect -> "Effect"
        | TraceNodeKind.AsyncMemo -> "AsyncMemo"
        | TraceNodeKind.Boundary -> "Boundary"
        | TraceNodeKind.Projection -> "Projection"
        | TraceNodeKind.ProjectionBeacon -> "ProjectionBeacon"
        | TraceNodeKind.RowWatch -> "RowWatch"
        | TraceNodeKind.LookupCell -> "LookupCell"
        | TraceNodeKind.Timed -> "Timed"
        | TraceNodeKind.Binding -> "Binding"
        | TraceNodeKind.Stream -> "Stream"
        | other -> string (int other)
#else
        string value
#endif

    let dropReason (value: TraceDropReason) : string =
#if FABLE_COMPILER
        match value with
        | TraceDropReason.Superseded -> "Superseded"
        | TraceDropReason.Disposed -> "Disposed"
        | TraceDropReason.Suspended -> "Suspended"
        | TraceDropReason.Trailing -> "Trailing"
        | TraceDropReason.Retried -> "Retried"
        | other -> string (int other)
#else
        string value
#endif

/// <summary>One entry of a graph's trace log.</summary>
/// <remarks>
/// Node ids are the graph's own ids; owner ids come from the log's counter. <c>Cause</c> is the <c>Seq</c> of the
/// causing event, or 0. A payload is never a node, owner, graph or projection.
/// </remarks>
[<Struct; NoEquality; NoComparison>]
type TraceEvent =
    {
        /// <summary>The graph's clock at this event, starting at 1.</summary>
        Seq: int
        Kind: TraceEventKind
        Node: int
        Other: int
        Arg: int
        Flag: int
        Cause: int
        Payload: obj
    }

    /// <summary>The event fields, one per line, with the payload's <c>ToString</c> text.</summary>
    override this.ToString() =
        TraceText.record
            [
                "Seq", string this.Seq
                "Kind", TraceNames.eventKind this.Kind
                "Node", string this.Node
                "Other", string this.Other
                "Arg", string this.Arg
                "Flag", string this.Flag
                "Cause", string this.Cause
                "Payload", TraceText.value this.Payload
            ]

/// <summary>A node's creation record.</summary>
type TraceOrigin =
    {
        /// <summary>The <c>Seq</c> of the node's <c>NodeNew</c>.</summary>
        Seq: int
        Node: int
        Kind: TraceNodeKind
        /// <summary>The node's latest label, from <c>Trace.label</c> or else <c>Trace.named</c>, if any.</summary>
        Label: string option
        /// <summary>The owner id the node attached to, or 0.</summary>
        Owner: int
        /// <summary>The owner ids from <c>Owner</c> up to the graph root, innermost first; empty when <c>Owner</c> is 0.</summary>
        Owners: int list
        /// <summary>The identity path, with its <c>@k</c> suffix when more than one node has held the path.</summary>
        Path: string
        /// <summary>The <c>RunStart</c> seq of the creating run, or 0.</summary>
        Run: int
        /// <summary>The creation site, or null when none was captured.</summary>
        Site: obj
    }

    /// <summary>The origin fields, one per line, with the site's <c>ToString</c> text.</summary>
    override this.ToString() =
        TraceText.record
            [
                "Seq", string this.Seq
                "Node", string this.Node
                "Kind", TraceNames.nodeKind this.Kind
                "Label", TraceText.option TraceText.value this.Label
                "Owner", string this.Owner
                "Owners", TraceText.list string this.Owners
                "Path", TraceText.value this.Path
                "Run", string this.Run
                "Site", TraceText.value this.Site
            ]
