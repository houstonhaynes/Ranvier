/// <summary>A .NET site's lookup from an instance to the entries of its mounted arms, per target.</summary>
module internal Ranvier.View.Arms.Cells

#if !FABLE_COMPILER
open global.Ranvier
open Ranvier.View.Representation

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
