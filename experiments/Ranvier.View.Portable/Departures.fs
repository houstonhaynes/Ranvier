/// <summary>
/// The exit and reflow hooks of placed elements, and the retired row roots held in place while their exits run. The mount
/// paths reach the registry only through <c>seam</c>, which the first registration fills.
/// </summary>
module internal Ranvier.View.Foundation.Departures

open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View.Foundation.Representation

/// <summary>
/// The registry operations the mount paths call. The operations are null until <c>Enabled</c> (exits) or
/// <c>ReflowEnabled</c> (reflows) is set.
/// </summary>
[<AllowNullLiteral>]
type Seam =
    abstract Enabled: bool with get, set
    abstract ReflowEnabled: bool with get, set

    /// <summary>
    /// Retires a row root: true when the node is held, and its end action joins the held list; false when the node has no
    /// exit.
    /// </summary>
    abstract Retire: System.Func<Node, ResizeArray<unit -> unit>, bool> with get, set

    /// <summary>The node's next sibling that is not leaving, or null.</summary>
    abstract NextLive: System.Func<Node, Node> with get, set

    /// <summary>Whether a node strictly between the two nodes is leaving or has an exit.</summary>
    abstract Holds: System.Func<Node, Node, bool> with get, set

    /// <summary>Whether the node or an ancestor is leaving, or is a root whose exit runs once its row is retired.</summary>
    abstract Within: System.Func<Node, bool> with get, set

    /// <summary>
    /// Ends the held exits of the first list (cancel, then remove), adding their failures to the second; with a third
    /// list, moves them into it instead, still running.
    /// </summary>
    abstract End: System.Action<ResizeArray<unit -> unit>, ResizeArray<exn>, ResizeArray<unit -> unit>> with get, set

    /// <summary>
    /// Runs the <c>Before</c> of the reflow registered on the parent of a region's start marker unless its measurement is
    /// open, and returns the function that posts its <c>After</c> through the given post; null without a reflow. Of the
    /// posts made while the measurement is open, the one posted last closes it and runs <c>After</c>.
    /// </summary>
    abstract Before: System.Func<Node, (unit -> unit) -> unit, unit -> unit> with get, set

/// <summary>The seam, an empty object until the first registration; null on .NET.</summary>
#if FABLE_COMPILER
let seam: Seam = Fable.Core.JsInterop.emitJsExpr () "{}"
#else
let seam: Seam = null
#endif

[<Emit("new WeakMap()")>]
let private weakMap () : obj = jsNative

[<Emit("new WeakSet()")>]
let private weakSet () : obj = jsNative

[<Emit("$0.get($1)")>]
let private lookup (table: obj) (key: 'K) : 'T = jsNative

[<Emit("$0.set($1, $2)")>]
let private store (table: obj) (key: 'K) (value: 'T) : unit = jsNative

[<Emit("$0.has($1)")>]
let private holds (table: obj) (key: 'K) : bool = jsNative

[<Emit("$0.delete($1)")>]
let private forget (table: obj) (key: 'K) : unit = jsNative

[<Emit("$0.add($1)")>]
let private mark (table: obj) (node: Node) : unit = jsNative

[<Emit("$0 == null")>]
let private isNullish (value: 'T) : bool = jsNative

[<Emit("$0.nextSibling")>]
let private nextOrNull (node: Node) : Node = jsNative

[<Emit("$0.parentNode")>]
let private parentOf (node: Node) : Node = jsNative

[<Emit("$0.parentNode != null && $0.parentNode.removeChild($0)")>]
let private detach (node: Node) : unit = jsNative

[<Emit("setTimeout($0, $1)")>]
let private startTimer (action: unit -> unit) (milliseconds: int) : obj = jsNative

[<Emit("clearTimeout($0)")>]
let private stopTimer (timer: obj) : unit = jsNative

/// <summary>The milliseconds after which a held exit is finished.</summary>
let safetyTimeout = 10000

// Each registered exit, by element: given `finish`, starts the exit and returns its cancel.
let private exits = weakMap ()
let private leaving = weakSet ()
// Roots whose instance is disposed and whose exit runs once the row is retired.
let private pending = weakSet ()
// The held list each end action is in.
let private holders = weakMap ()
let private reflows = weakMap ()

[<NoEquality; NoComparison>]
type private ReflowEntry =
    {
        Reflow: Reflow
        /// <summary>Set by <c>Before</c>, cleared when the latest posted <c>After</c> runs.</summary>
        mutable Open: bool
        /// <summary>The number of <c>After</c> posts.</summary>
        mutable Posts: int
    }

let private nextLive (node: Node) : Node =
    let mutable next = nextOrNull node

    while not (isNullish next) && holds leaving next do
        next <- nextOrNull next

    next

let private holdsBetween (first: Node) (last: Node) : bool =
    let mutable cursor = nextOrNull first
    let mutable found = false

    while not found
          && not (isNullish cursor)
          && not (obj.ReferenceEquals (cursor, last)) do
        found <- holds leaving cursor || holds exits cursor
        cursor <- nextOrNull cursor

    found

let private within (node: Node) : bool =
    let mutable cursor = node
    let mutable found = false

    while not found && not (isNullish cursor) do
        found <- holds leaving cursor || holds pending cursor
        cursor <- parentOf cursor

    found

let private drop (ending: unit -> unit) =
    let held: ResizeArray<unit -> unit> = lookup holders ending
    let mutable index = held.Count - 1

    while index >= 0 do
        if obj.ReferenceEquals (held[index], ending) then
            held.RemoveAt index
            index <- -1
        else
            index <- index - 1

let private endAll (held: ResizeArray<unit -> unit>) (errors: ResizeArray<exn>) (target: ResizeArray<unit -> unit>) =
    let departing = held.ToArray ()

    if isNull target then
        for index in 0 .. departing.Length - 1 do
            try
                departing[index]()
            with error ->
                errors.Add error
    else
        held.Clear ()

        for index in 0 .. departing.Length - 1 do
            store holders departing[index] target
            target.Add departing[index]

// Withdraws the node's exit, marks the node leaving, adds its end action to `held` and starts its exit with a `finish`
// that removes the node and drops the end action; `safetyTimeout` milliseconds later `finish` runs unless it ran earlier.
// The end action cancels the exit, then removes the node. A failure of the exit finishes it and is raised.
let private retire (node: Node) (held: ResizeArray<unit -> unit>) : bool =
    let exit: (unit -> unit) -> (unit -> unit) = lookup exits node

    if isNullish exit then
        false
    else
        forget exits node
        forget pending node
        mark leaving node
        let mutable ended = false
        let mutable timer: obj = null
        let mutable cancel: unit -> unit = Unchecked.defaultof<_>
        let mutable ending: unit -> unit = Unchecked.defaultof<_>

        let leave () =
            ended <- true

            if not (isNull timer) then
                stopTimer timer

            forget leaving node
            drop ending
            detach node

        let finish () =
            if not ended then
                leave ()

        ending <-
            fun () ->
                if not ended then
                    let running = cancel
                    ended <- true

                    try
                        if not (isNullish running) then
                            running ()
                    finally
                        leave ()

        store holders ending held
        held.Add ending
        timer <- startTimer finish safetyTimeout

        try
            cancel <- exit finish
        with _ ->
            finish ()
            reraise ()

        true

let private before (regionStart: Node) (post: (unit -> unit) -> unit) : unit -> unit =
    let entry: ReflowEntry = lookup reflows (parentOf regionStart)

    if isNullish entry then
        Unchecked.defaultof<_>
    else
        let mutable failure: exn = null

        if not entry.Open then
            entry.Open <- true

            try
                entry.Reflow.Before ()
            with error ->
                entry.Open <- false
                failure <- error

        if not (isNull failure) then
            fun () -> post (fun () -> raise failure)
        else
            fun () ->
                let mutable posted = entry.Posts + 1
                entry.Posts <- posted

                post (fun () ->
                    if entry.Open && entry.Posts = posted then
                        entry.Open <- false
                        entry.Reflow.After ())

/// <summary>
/// Registers <c>exit</c> for <c>node</c>, replacing an earlier registration, enables exits, and returns the cleanup that
/// marks the node's exit as following: from then until the row retires the node, <c>Within</c> holds for the node and
/// its descendants, so nested mounts leave their nodes in place.
/// </summary>
let register (node: Node) (exit: (unit -> unit) -> (unit -> unit)) : unit -> unit =
    if not seam.Enabled then
        seam.Retire <- System.Func<_, _, _> retire
        seam.NextLive <- System.Func<_, _> nextLive
        seam.Holds <- System.Func<_, _, _> holdsBetween
        seam.Within <- System.Func<_, _> within
        seam.End <- System.Action<_, _, _> endAll
        seam.Enabled <- true

    forget pending node
    store exits node exit

    fun () ->
        if holds exits node then
            mark pending node

/// <summary>Whether <c>node</c> has a registered exit that has not started.</summary>
let hasExit (node: Node) : bool =
    holds exits node

/// <summary>Whether a reflow is registered for <c>node</c>.</summary>
let hasReflow (node: Node) : bool =
    holds reflows node

/// <summary>
/// Registers <c>reflow</c> for <c>node</c>, replacing an earlier registration, enables reflows, and returns the function
/// that withdraws this registration.
/// </summary>
let registerReflow (node: Node) (reflow: Reflow) : unit -> unit =
    if not seam.ReflowEnabled then
        seam.Before <- System.Func<_, _, _> before
        seam.ReflowEnabled <- true

    let entry =
        {
            Reflow = reflow
            Open = false
            Posts = 0
        }

    store reflows node entry

    fun () ->
        if obj.ReferenceEquals (lookup reflows node, entry) then
            forget reflows node
