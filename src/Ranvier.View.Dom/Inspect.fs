/// <summary>The inspection registry: per region start marker and template instance root, closures over the state the mount keeps.</summary>
module internal Ranvier.View.Dom.Inspect

#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
open Fable.Core.TS.Dom
open Ranvier
open Ranvier.View
open Ranvier.View.Template

[<NoEquality; NoComparison>]
type Probe =
    /// <summary>A list region: the keys of its laid-out rows, in order, and each row's first node.</summary>
    | ListProbe of path: InstancePath * rows: (unit -> (obj * Node) array) * stale: (unit -> bool)
    /// <summary>An arm region: the selected key and definition, or null when empty or the arm is authored.</summary>
    | ArmProbe of path: InstancePath * selected: (unit -> struct (obj * TemplateComponent)) * stale: (unit -> bool)
    | BoundaryProbe of path: InstancePath * state: (unit -> BoundaryProbeState)
    | HoleProbe of path: InstancePath
    /// <summary>A template instance: its definition, instance value, owner and path; keyed by its first root node.</summary>
    | InstanceProbe of TemplateComponent * instance: obj * owner: Owner * path: InstancePath
    /// <summary>A mount: its phase, commit mode, whether a pass or its queue is flushing, its identity prefix and its graph; keyed by its range's start marker.</summary>
    | MountProbe of phase: (unit -> ScopePhase) * queued: bool * flushing: (unit -> bool) * prefix: string * graph: Graph

and [<NoEquality; NoComparison>] BoundaryProbeState =
    | Ready
    | Pending of waiting: int * revealed: bool
    | Failed of error: exn * generation: int

let private table = Native.newWeakMap ()
let private instances = Native.newWeakMap ()
let private rowKeys = Native.newWeakMap ()
let private instanceRanges = Native.newWeakMap ()

/// <summary>Registers the inclusive root span of an instance until cleanup.</summary>
let registerInstanceRange (first: Node) (last: Node) =
    let entry = [| first; last |]
    let entries = Native.get<Node, Node array array> instanceRanges first
    Native.set instanceRanges first (Array.append (if isNull entries then [||] else entries) [| entry |])

    fun () ->
        let entries = Native.get<Node, Node array array> instanceRanges first

        if not (isNull entries) then
            let remaining =
                entries
                |> Array.filter (fun current -> not (obj.ReferenceEquals (current, entry)))

            if remaining.Length = 0 then
                Native.delete instanceRanges first
            else
                Native.set instanceRanges first remaining

/// <summary>The nearest instance's first root and current end-exclusive sibling around <c>target</c>.</summary>
let rec enclosingInstanceOf (target: Node) : (Node * Node) option =
    if isNull (box target) then
        None
    else
        let parent = Native.parentOrNull target

        let mutable cursor =
            if isNull (box parent) then
                target
            else
                Native.firstChildOf parent

        let mutable found = None
        let mutable searching = true

        while searching && not (isNull (box cursor)) do
            let entries = Native.get<Node, Node array array> instanceRanges cursor

            if not (isNull entries) then
                for entry in entries do
                    let mutable root = entry[0]
                    let mutable contains = false
                    let mutable within = true

                    while within && not (isNull (box root)) do
                        if obj.ReferenceEquals (root, target) then
                            contains <- true

                        if obj.ReferenceEquals (root, entry[1]) then
                            within <- false
                        else
                            root <- Native.nextOrNull root

                    if contains then
                        found <- Some (entry[0], Native.nextOrNull entry[1])

            if obj.ReferenceEquals (cursor, target) then
                searching <- false
            else
                cursor <- Native.nextOrNull cursor

        match found with
        | Some _ -> found
        | None -> enclosingInstanceOf parent

/// <summary>Retains a row's key at its first node through its exit hold.</summary>
let rememberRowKey (node: Node) (key: obj) =
    Native.set rowKeys node [| key |]

/// <summary>The row's key while its first node has a parent; a detached node clears its retained entry.</summary>
let rowKeyOf (node: Node) : obj option =
    if node.parentNode.IsNone then
        Native.delete rowKeys node
        None
    else
        match Native.get<Node, obj array> rowKeys node with
        | null -> None
        | values -> Some values[0]

/// <summary>The instances rooted at <c>node</c>, in outer-to-inner registration order.</summary>
let instancesOf (node: Node) : Probe array =
    match Native.get<Node, Probe array> instances node with
    | null -> [||]
    | probes -> probes

/// <summary>Whether the node's sources changed after its last run; false for null.</summary>
let isStale (node: INode) =
    not (isNull (box node))
    && node.Status &&& Status.Stale <> Status.None

/// <summary>Registers <c>probe</c> for <c>node</c> until the returned cleanup runs.</summary>
let register (node: Node) (probe: Probe) : unit -> unit =
    match probe with
    | InstanceProbe _ ->
        Native.set instances node (Array.append (instancesOf node) [| probe |])

        fun () ->
            let remaining =
                instancesOf node
                |> Array.filter (fun current -> not (obj.ReferenceEquals (current, probe)))

            if remaining.Length = 0 then
                Native.delete instances node
            else
                Native.set instances node remaining
    | _ ->
        Native.set table node probe

        fun () ->
            match Native.get<Node, Probe> table node with
            | current when obj.ReferenceEquals (current, probe) -> Native.delete table node
            | _ -> ()

let private bound = Native.newWeakSet ()

/// <summary>Marks a text node as a slot target.</summary>
let markBound (node: Node) =
    Native.add bound node

/// <summary>Whether the text node is a slot target.</summary>
let isBound (node: Node) =
    Native.has bound node

let probeOf (node: Node) : Probe option =
    match box (Native.get<Node, Probe> table node) with
    | null -> instancesOf node |> Array.tryHead
    | probe -> Some (unbox<Probe> probe)
#endif
