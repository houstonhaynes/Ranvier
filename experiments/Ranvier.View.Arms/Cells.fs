/// <summary>A site's lookup from an instance to the entries of its mounted arms, per target.</summary>
module internal Ranvier.View.Arms.Cells

open global.Ranvier
open Ranvier.View.Representation
#if FABLE_COMPILER
open Fable.Core

// An entry is { g: graph, c: the arm's current props, l: last values (from mount on), n: the next entry, w: its
// withdrawal }. An instance's slots hold the first entry of target t at key t, the others linked through n in mount
// order. A withdrawn entry stays in the chain with c null and its last values; the next arm of its target on the
// instance rebinds it, so a target holds no more entries than it had live arms at once.

/// <summary>The site's WeakMap from an instance to its slots.</summary>
type Cells = obj

[<Emit("new WeakMap()")>]
let create () : Cells = jsNative

/// <summary>The value of an optional argument, or undefined.</summary>
[<Emit("$0")>]
let erased (value: obj option) : obj = jsNative

/// <summary>The ambient graph, or undefined outside one.</summary>
[<Emit("$0")>]
let tag (graph: Graph voption) : obj = jsNative

[<Emit("($0.get($1) || $0.set($1, {}).get($1))")>]
let slotsOf (cells: Cells) (instance: obj) : obj = jsNative

[<Emit("$0.get($1)")>]
let tryFind (cells: Cells) (instance: obj) : obj = jsNative

[<Emit("{ g: $0, c: $1, l: $2 }")>]
let entry (graph: obj) (current: obj) (values: obj array) : obj = jsNative

/// <summary>Binds a withdrawn entry to the graph, the arm's props and its mount-time values.</summary>
[<Emit("($0.g = $1, $0.c = $2, $0.l = $3)")>]
let rebind (entry: obj) (graph: obj) (current: obj) (values: obj array) : unit = jsNative

/// <summary>The first entry of the target, or null.</summary>
[<Emit("($0 && $0[$1])")>]
let first (slots: obj) (target: obj) : obj = jsNative

[<Emit("$0[$1] = $2")>]
let setFirst (slots: obj) (target: obj) (entry: obj) : unit = jsNative

[<Emit("$0.n")>]
let next (entry: obj) : obj = jsNative

[<Emit("$0.n = $1")>]
let setNext (entry: obj) (next: obj) : unit = jsNative

/// <summary>Whether the entry belongs to a live arm.</summary>
[<Emit("$0.c")>]
let isLive (entry: obj) : bool = jsNative

/// <summary>The entry's withdrawal, created once per entry.</summary>
[<Emit("($0.w || ($0.w = $1))")>]
let withdrawalOf (entry: obj) (withdraw: unit -> unit) : unit -> unit = jsNative

/// <summary>Records the arm's last values on the entry and releases the arm's props, which withdraws the entry.</summary>
[<Emit("($0.l = $0.c()[1], $0.c = null)")>]
let retire (entry: obj) : unit = jsNative

[<Emit("$0.g")>]
let graphOf (entry: obj) : obj = jsNative

[<Emit("$0 === $1")>]
let same (a: obj) (b: obj) : bool = jsNative

[<Emit("$0 == null")>]
let absent (value: obj) : bool = jsNative

[<Emit("null")>]
let unread () : obj array = jsNative

[<Emit("($0 == null || $1.g == null || $1.g === $0)")>]
let servesUnder (graph: obj) (entry: obj) : bool = jsNative

[<Emit("$0.l = $0.c()[1]")>]
let read (entry: obj) : obj array = jsNative

[<Emit("$0.l")>]
let last (entry: obj) : obj array = jsNative

/// <summary>
/// Registers the arm's current props (the instance and its bound values) and its mount-time values for the instance and
/// target, and withdraws them on the arm's cleanup. A withdrawn entry of the target is rebound in place, one tagged with
/// the ambient graph first.
/// </summary>
let inline enter (cells: Cells) (setup: Setup) (instance: obj) (target: obj) (current: unit -> obj * obj array) (values: obj array) : unit =
    let slots = slotsOf cells instance
    let graph = tag Graph.TryCurrent
    let mutable candidate = first slots target
    let mutable tail: obj = null
    let mutable withdrawn: obj = null

    // The first withdrawn entry, preferring one tagged with the ambient graph.
    while not (absent candidate) do
        if
            not (isLive candidate)
            && (absent withdrawn
                || (same (graphOf candidate) graph
                    && not (same (graphOf withdrawn) graph)))
        then
            withdrawn <- candidate

        tail <- candidate
        candidate <- next candidate

    let registered =
        if absent withdrawn then
            let created = entry graph current values

            if absent tail then
                setFirst slots target created
            else
                setNext tail created

            created
        else
            rebind withdrawn graph current values
            withdrawn

    setup.OnCleanup (withdrawalOf registered (fun () -> retire registered))

/// <summary>
/// The bound values of the reader's own arm: the pattern variables, then the matched value. Reads every live entry
/// serving the ambient graph and returns the first; with none, the last values of the first withdrawn entry serving the
/// ambient graph.
/// </summary>
let inline values (cells: Cells) (instance: obj) (target: obj) : obj array =
    let slots = tryFind cells instance
    let graph = tag Graph.TryCurrent
    let mutable candidate = first slots target
    let mutable found = unread ()
    let mutable held = unread ()

    while not (absent candidate) do
        if servesUnder graph candidate then
            if isLive candidate then
                let values = read candidate

                if absent found then
                    found <- values
            elif absent held then
                held <- last candidate

        candidate <- next candidate

    if not (absent found) then found
    elif not (absent held) then held
    else Errors.noArm ()
#else
let private ambient () : obj =
    match Graph.TryCurrent with
    | ValueSome graph -> box graph
    | ValueNone -> null

/// <summary>A mounted arm's payload reader, the graph its setup ran under, and its values, from mount on.</summary>
[<AllowNullLiteral>]
type Entry(graph: obj, values: unit -> obj array, initial: obj array) =
    member _.Graph = graph
    /// <summary>The payload reader; null once the entry is withdrawn.</summary>
    member val Values = values with get, set
    member val Last = initial with get, set

/// <summary>Whether an entry serves a read under the graph: a null graph or a null tag matches any.</summary>
let private serves (graph: obj) (entry: Entry) =
    isNull graph
    || isNull entry.Graph
    || obj.ReferenceEquals (entry.Graph, graph)

/// <summary>
/// The live entries of an instance per target, as arrays replaced on each change, and each target's last withdrawn
/// entry.
/// </summary>
[<AllowNullLiteral>]
type Slots() =
    let mutable live: Entry array array = [||]
    let mutable withdrawn: Entry array = [||]

    member private _.Ensure(target: int) =
        if live.Length <= target then
            live <- Array.append live (Array.create (target + 1 - live.Length) [||])
            withdrawn <- Array.append withdrawn (Array.create (target + 1 - withdrawn.Length) null)

    member this.Add(target: int, entry: Entry) =
        this.Ensure target
        let next = Array.copy live
        next[target] <- Array.append live[target] [| entry |]
        live <- next

    member this.Withdraw(target: int, entry: Entry) =
        this.Ensure target
        let next = Array.copy live

        next[target] <-
            live[target]
            |> Array.filter (fun other -> not (obj.ReferenceEquals (other, entry)))

        live <- next
        withdrawn[target] <- entry

    /// <summary>The live entries of the target, as an immutable array.</summary>
    member _.Live(target: int) : Entry array =
        let current = live
        if target < current.Length then current[target] else [||]

    member _.Withdrawn(target: int) : Entry =
        let current = withdrawn
        if target < current.Length then current[target] else null

open System.Runtime.CompilerServices

let private newTable () : obj =
    ConditionalWeakTable<obj, Slots>() :> obj

let private tryFind (table: obj) (instance: obj) : Slots =
    match (table :?> ConditionalWeakTable<obj, Slots>).TryGetValue instance with
    | true, slots -> slots
    | _ -> null

let private slotsOf (table: obj) (instance: obj) : Slots =
    (table :?> ConditionalWeakTable<obj, Slots>).GetValue(instance, fun _ -> Slots ())

/// <summary>Runs <c>action</c> holding the instance's lock.</summary>
let inline private guarded (slots: Slots) ([<InlineIfLambda>] action: unit -> unit) : unit =
    lock slots action

[<Sealed>]
type Cells internal (table: obj) =
    member internal _.Table = table

let create () =
    Cells (newTable ())

/// <summary>
/// Registers the arm's payload reader and mount-time values for the instance and target; on the arm's cleanup, records its
/// last values, drops the reader and withdraws the entry.
/// </summary>
let enter (cells: Cells) (setup: Setup) (instance: obj) (target: int) (values: unit -> obj array) (initial: obj array) : unit =
    let slots = slotsOf cells.Table instance
    let entry = Entry (ambient (), values, initial)
    guarded slots (fun () -> slots.Add (target, entry))

    setup.OnCleanup (fun () ->
        entry.Last <- entry.Values ()
        entry.Values <- Unchecked.defaultof<_>
        guarded slots (fun () -> slots.Withdraw (target, entry)))

/// <summary>
/// The bound values of the reader's own arm: the pattern variables, then the matched value. Reads every live entry
/// serving the ambient graph and returns the first; with none, the values held by the target's last withdrawn entry
/// when it serves the ambient graph. A withdrawal under another graph replaces the target's withdrawn entry; a held read
/// under the first graph then raises AM006. Reads run outside the instance's lock.
/// </summary>
let values (cells: Cells) (instance: obj) (target: int) : obj array =
    match tryFind cells.Table instance with
    | null -> Errors.noArm ()
    | slots ->
        let graph = ambient ()
        let mutable first: obj array = null

        for entry in slots.Live target do
            if serves graph entry then
                let read = entry.Values ()
                entry.Last <- read

                if isNull first then
                    first <- read

        if not (isNull first) then
            first
        else
            match slots.Withdrawn target with
            | null -> Errors.noArm ()
            | held when serves graph held -> held.Last
            | _ -> Errors.noArm ()

#endif
