module Ranvier.View.Foundation.TemplateDom

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Core.TS.Dom
open Ranvier
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template

[<Literal>]
let FirstChild = 0

[<Literal>]
let NextSibling = 1

[<Literal>]
let Parent = 2

[<Literal>]
let Collect = 3

[<Literal>]
let StateCompiled = 0

[<Literal>]
let StateInstance = 1

[<Literal>]
let StateOwner = 2

[<Literal>]
let StateGraph = 3

[<Literal>]
let StateNodes = 4

/// <summary>How a region mounts its rows.</summary>
type RegionKind =
    /// <summary>One row per keyed item; new rows are created before removed rows retire.</summary>
    | ListRows
    /// <summary>The row of the selected arm; the previous arm is disposed before the next is created.</summary>
    | SelectedArm
    /// <summary>One view, read and mounted once.</summary>
    | ViewHole
    /// <summary>A boundary: the region's single item's value is its <c>BoundaryViews</c>, read once.</summary>
    | BoundaryContent

/// <summary>
/// A server node that differs from the node the template expects at <c>Path</c>: <c>root</c> for a missing or unclosed
/// root marker, <c>#k</c> for the k-th root of an instance, and otherwise the dynamic node's child-index path.
/// </summary>
exception HydrationMismatchException of Path: string * Expected: string * Found: string with
    override this.Message =
        "Hydration mismatch at "
        + this.Path
        + ": expected "
        + this.Expected
        + ", found "
        + this.Found

/// <summary>A <c>HydrationMismatchException</c> whose <c>Message</c> is the override's text on .NET and under Fable.</summary>
let internal mismatch (path: string) (expected: string) (found: string) : exn =
    let error = HydrationMismatchException (path, expected, found)
#if FABLE_COMPILER
    // Fable reads Message from the JavaScript error's message property.
    JsInterop.(?<-)
        error
        "message"
        ("Hydration mismatch at "
         + path
         + ": expected "
         + expected
         + ", found "
         + found)
#endif
    error

#if RANVIER_VIEW_COUNTERS
/// <summary>Counts of hydration rollback work, defined only in the DOM test build.</summary>
module internal HydrationCounters =
    /// <summary>Inverses recorded with any hydration cursor.</summary>
    let recorded = ref 0

    /// <summary>Recorded inverses neither run by a <c>Restore</c> nor discarded by a <c>Commit</c>.</summary>
    let held = ref 0
#endif

/// <summary>The next server node a hydrating mount adopts, and the inverses of the edits made to the server nodes so far.</summary>
[<Sealed; AllowNullLiteral>]
type internal HydrationCursor(next: Node, strict: bool) =
    let undo = ResizeArray<unit -> unit>()
    // The cursors of nested mounts recorded by `Nest`; null until the first.
    let mutable nested: ResizeArray<HydrationCursor> = null

    let discard () =
#if RANVIER_VIEW_COUNTERS
        HydrationCounters.held.Value <- HydrationCounters.held.Value - undo.Count
#endif
        undo.Clear ()
        nested <- null

    /// <summary>The next server node to adopt; null past the last.</summary>
    member val Next: Node = next with get, set

    member _.Strict = strict

    /// <summary>Records the inverse of an edit to the server nodes.</summary>
    member _.Undo(inverse: unit -> unit) =
#if RANVIER_VIEW_COUNTERS
        HydrationCounters.recorded.Value <- HydrationCounters.recorded.Value + 1
        HydrationCounters.held.Value <- HydrationCounters.held.Value + 1
#endif
        undo.Add inverse

    /// <summary>Records a nested mount's cursor: <c>Restore</c> restores it in its place among the inverses, and <c>Commit</c> commits it.</summary>
    member this.Nest(child: HydrationCursor) =
        if isNull nested then
            nested <- ResizeArray ()

        nested.Add child
        this.Undo (fun () -> child.Restore ())

    /// <summary>Runs the recorded inverses, most recent first; raises after all have run when any raised.</summary>
    member _.Restore() =
        let errors = ResizeArray<exn>()

        for index in undo.Count - 1 .. -1 .. 0 do
            try
                undo[index]()
            with error ->
                errors.Add error

        discard ()

        if errors.Count = 1 then
            raise errors[0]
        elif errors.Count > 1 then
            raise (AggregateException ("Hydration restore failed", errors))

    /// <summary>Discards the recorded inverses of the cursor and of every nested cursor, and sets <c>Next</c> to null.</summary>
    member this.Commit() =
        let children = nested
        discard ()
        this.Next <- Unchecked.defaultof<_>

        if not (isNull children) then
            for child in children do
                child.Commit ()

let private noNode: Node = Unchecked.defaultof<_>

#if FABLE_COMPILER
[<Emit("$0 == null")>]
let private absent (node: Node) : bool = jsNative
#else
let private absent (node: Node) : bool =
    Native.isNullish node
#endif

#if FABLE_COMPILER
[<Emit("$0.nodeType === 8")>]
let private isComment (node: Node) : bool = jsNative
#else
let private isComment (node: Node) : bool =
    node.nodeType = 8.0
#endif

#if FABLE_COMPILER
[<Emit("$0.data")>]
let private dataOf (node: Node) : string = jsNative
#else
let private dataOf (node: Node) : string =
    Native.dataOf node
#endif

#if FABLE_COMPILER
[<Emit("$0.nextSibling")>]
let private following (node: Node) : Node = jsNative
#else
let private following (node: Node) : Node =
    Native.nextOrNull node
#endif

/// <summary>Whether the node is a comment whose data starts with <c>[</c>.</summary>
#if FABLE_COMPILER
[<Emit("$0 != null && $0.nodeType === 8 && $0.data.startsWith('[')")>]
let internal isStartMarker (node: Node) : bool = jsNative
#else
let internal isStartMarker (node: Node) : bool =
    not (Native.isNullish node)
    && node.nodeType = 8.0
    && (Native.dataOf node).StartsWith "["
#endif

/// <summary>The sibling <c>]</c> comment closing the region that <c>start</c> opens.</summary>
/// <exception cref="T:Ranvier.View.Foundation.TemplateDom.HydrationMismatchException">The siblings end first.</exception>
let internal matchingEnd (start: Node) : Node =
    let mutable depth = 0
    let mutable node = following start
    let mutable found = noNode

    while absent found && not (absent node) do
        if isComment node then
            let data = dataOf node

            if data = "]" then
                if depth = 0 then found <- node else depth <- depth - 1
            elif data.StartsWith "[" then
                depth <- depth + 1

        if absent found then
            node <- following node

    if absent found then
        raise (mismatch ("<!--" + dataOf start + "-->") "<!--]-->" "absent")

    found

/// <summary>The node one logical position after <c>node</c>: a start marker's matching end, otherwise the next sibling.</summary>
let internal logicalNext (node: Node) : Node =
    if isStartMarker node then
        matchingEnd node
    else
        following node

/// <summary>The <c>]</c> comment closing the root marker <c>start</c>.</summary>
/// <exception cref="T:Ranvier.View.Foundation.TemplateDom.HydrationMismatchException">
/// The siblings end first: at <c>#k</c> when the k-th logical position after <c>start</c> is an unclosed start marker,
/// otherwise at <c>root</c>.
/// </exception>
let internal rootEnd (start: Node) : Node =
    try
        matchingEnd start
    with HydrationMismatchException _ ->
        let mutable node = following start
        let mutable position = 0

        while not (isNull (box node)) do
            node <-
                try
                    logicalNext node
                with HydrationMismatchException (_, closing, absent) ->
                    raise (mismatch ("#" + string position) closing absent)

            position <- position + 1

        raise (mismatch "root" "<!--]-->" "absent")

#if FABLE_COMPILER
/// <summary>The inner exceptions of an <c>AggregateException</c>, held under Fable as an <c>innerException</c> array; empty for any other exception.</summary>
[<Emit("$0 != null && Array.isArray($0.innerException) ? $0.innerException : []")>]
let private aggregated (error: exn) : exn array = jsNative
#else
/// <summary>The inner exceptions of an <c>AggregateException</c>; empty for any other exception.</summary>
let private aggregated (error: exn) : exn array =
    match error with
    | :? AggregateException as aggregate -> Seq.toArray aggregate.InnerExceptions
    | _ -> [||]
#endif

/// <summary>The first <c>HydrationMismatchException</c> in <c>error</c> or, at any depth, its aggregated inner exceptions.</summary>
let rec internal findMismatch (error: exn) : HydrationMismatchException option =
    match error with
    | :? HydrationMismatchException as found -> Some found
    | _ -> aggregated error |> Array.tryPick findMismatch

/// <summary>
/// The exceptions of <c>error</c> other than <c>excluded</c>, with every <c>AggregateException</c> at any depth replaced
/// by its inner exceptions, in depth-first order.
/// </summary>
let rec internal failuresBesides (excluded: exn) (error: exn) : exn list =
    let inner = aggregated error

    if inner.Length > 0 then
        inner
        |> Seq.collect (failuresBesides excluded)
        |> List.ofSeq
    elif obj.ReferenceEquals (error, excluded) then
        []
    else
        [ error ]

/// <summary>The server node a collected node of a template adopts.</summary>
type Expectation =
    /// <summary>An element of the tag, compared without case.</summary>
    | ExpectElement of tag: string
    /// <summary>A text node, or the <c>&lt;!--_--&gt;</c> placeholder of an empty one.</summary>
    | ExpectText
    /// <summary>The start marker of a region of the kind.</summary>
    | ExpectRegion of RegionKind

[<NoEquality; NoComparison>]
type CompiledRegion =
    {
        /// <summary>The node index of the region's start marker.</summary>
        Marker: int
        /// <summary>The part whose instance the region reads: 0 for the template, k for the k-th inlined child.</summary>
        Owner: int
        /// <summary>The region's position among its owning part's regions.</summary>
        Index: int
        /// <summary>True when the markers are roots of a multi-root skeleton.</summary>
        RootLevel: bool
        Kind: RegionKind
        Items: Func<obj, obj array>
        Key: Func<obj, obj>
        Render: (unit -> obj) -> Backend -> unit
        Row: RegionRow
    }

/// <summary>A definition compiled for the DOM: a skeleton, the walk to its dynamic nodes, and its slot tables.</summary>
/// <remarks>A single-root skeleton is the root node itself and its walk starts at the root; otherwise the skeleton is a fragment of <c>RootCount</c> roots. The root compiled form lists the regions and inlined children of every depth.</remarks>
[<NoEquality; NoComparison>]
type Compiled =
    {
        Skeleton: Node
        RootCount: int
        Walk: int array
        NodeCount: int
        RenderSlots: (int * Slot) array
        IsolatedSlots: (int * Slot) array
        Properties: (int * PortableProperty * obj) array
        Events: (int * string * Func<obj, obj, unit>) array
        /// <summary>The placed hooks of the part's nodes: node index, kind and hook.</summary>
        Placed: (int * PlacedKind * Func<obj, obj, obj, obj>) array
        /// <summary>
        /// The document head entries of the part in document order, each with the number of the part's dynamic nodes
        /// before it.
        /// </summary>
        Heads: (HeadTemplate * int) array
        EventNames: string array
        Dispatch: Func<obj array, Node, obj, unit>
        Regions: CompiledRegion array
        Children: CompiledChild array
        /// <summary>The server node each collected node adopts, in collection order; empty for an inlined child's tables.</summary>
        Expect: Expectation array
        /// <summary>The definition's <c>structuralHash</c>, computed on first use; empty for an inlined child's tables.</summary>
        Hash: Lazy<string>
    }

and [<NoEquality; NoComparison>] CompiledChild =
    {
        Definition: TemplateComponent
        Props: Func<obj, obj>
        /// <summary>The part whose instance the props reader receives.</summary>
        Owner: int
        /// <summary>The template's node index of the child's first dynamic node.</summary>
        Offset: int
        /// <summary>The child's slot and event tables, indexed from <c>Offset</c>; its skeleton is null and its walk empty.</summary>
        Tables: Compiled
    }

/// <summary>Compiles child-index paths, in document order, into walk opcodes ending each path with <c>Collect</c>.</summary>
let walkProgram (paths: int array array) : int array =
    let ops = ResizeArray<int>()
    let mutable cursor: int array = [||]

    for path in paths do
        let mutable shared = 0

        while shared < cursor.Length
              && shared < path.Length
              && cursor[shared] = path[shared] do
            shared <- shared + 1

        if shared = cursor.Length then
            for level in shared .. path.Length - 1 do
                ops.Add FirstChild

                for _ in 1 .. path[level] do
                    ops.Add NextSibling
        else
            for _ in shared + 1 .. cursor.Length - 1 do
                ops.Add Parent

            for _ in 1 .. path[shared] - cursor[shared] do
                ops.Add NextSibling

            for level in shared + 1 .. path.Length - 1 do
                ops.Add FirstChild

                for _ in 1 .. path[level] do
                    ops.Add NextSibling

        ops.Add Collect
        cursor <- path

    ops.ToArray ()

/// <summary>Marks an event node of a template instance under a boundary with the boundary.</summary>
#if FABLE_COMPILER
[<Emit("$0.$$ranvierScope = $1")>]
let private attachScope (node: obj) (scope: ViewScope) : unit = jsNative
#else
let private attachScope (node: obj) (scope: ViewScope) : unit =
    Native.setExpando node "$$ranvierScope" scope
#endif

/// <summary>The boundary of an event node, or null outside every boundary.</summary>
#if FABLE_COMPILER
[<Emit("$0.$$ranvierScope")>]
let private scopeOf (node: Node) : ViewScope = jsNative
#else
let private scopeOf (node: Node) : ViewScope =
    Native.expando<ViewScope> node "$$ranvierScope"
#endif

let private dispatchFor (nodeCount: int) (events: (int * string * Func<obj, obj, unit>) array) (eventNames: string array) =
    Func<obj array, Node, obj, unit>(fun state node event ->
#if FABLE_COMPILER
        let eventType: string = event?``type``
#else
        let eventType: string = (unbox<Event> event).``type``
#endif
        let mutable handled = false

        for eventName in eventNames do
            if eventName = eventType then
                handled <- true

        if handled then
            let mutable index = -1
            let mutable i = 0

            while index < 0 && i < nodeCount do
                if obj.ReferenceEquals (state[StateNodes + i], node) then
                    index <- i

                i <- i + 1

            if index >= 0 then
                let owner = state[StateOwner] :?> Owner
                let graph = state[StateGraph] :?> Graph

                for target, eventName, handler in events do
                    if
                        target = index
                        && eventName = eventType
                        && not owner.IsDisposed
                    then
                        try
                            graph.Run (fun () ->
                                Api.runWithOwner owner (fun () -> Api.untrack (fun () -> handler.Invoke (state[StateInstance], event))))
                        with error ->
                            let scope = scopeOf node

                            if isNull (box scope) || not scope.Tracks then
                                reraise ()

                            scope.FailAll [ error ])

/// <summary>Dispatches a delegated event to the template instance whose state is attached to the node.</summary>
let dispatch =
    Func<obj array, Node, obj, unit>(fun state node event -> (state[StateCompiled] :?> Compiled).Dispatch.Invoke(state, node, event))

let private restructured (detail: string) =
    InvalidOperationException (
        "The HTML parser restructured the template markup: "
        + detail
    )

#if FABLE_COMPILER
[<Emit("$0 != null && $0.nodeType === 8 && $0.data === '[' && $0.nextSibling != null && $0.nextSibling.nodeType === 8 && $0.nextSibling.data === ']'")>]
let private isRegionStart (node: Node) : bool = jsNative
#else
let private isRegionStart (node: Node) : bool =
    let next =
        if Native.isNullish node then
            Unchecked.defaultof<Node>
        else
            Native.nextOrNull node

    not (Native.isNullish node)
    && node.nodeType = 8.0
    && Native.dataOf node = "["
    && not (Native.isNullish next)
    && next.nodeType = 8.0
    && Native.dataOf next = "]"
#endif

#if FABLE_COMPILER
[<Emit("document.importNode($0, true)")>]
let private importDeep (node: Node) : Node = jsNative
#else
let private importDeep (node: Node) : Node =
    Native.importDeep node
#endif

let private validateParsed (content: DocumentFragment) (walk: int array) (targets: (TemplateNode * int array) array) (rootCount: int) =
    let parsedRoots = int content.childNodes.length

    if parsedRoots <> rootCount then
        raise (restructured $"expected %d{rootCount} root nodes, parsed %d{parsedRoots}")

    let mutable cursor: Node = content :> Node
    let mutable count = 0

    for op in walk do
        if op = Collect then
            let target, path = targets[count]

            let expected, matches =
                match target with
                | ElementNode (tag, _, _, _, _, _, _) ->
                    let tagName = name tag

                    "<" + tagName + ">",
                    not (isNull (box cursor))
                    && cursor.nodeType = 1.
                    && cursor.nodeName.ToLowerInvariant () = tagName.ToLowerInvariant ()
                | TextSlotNode _ -> "a text node", not (isNull (box cursor)) && cursor.nodeType = 3.
                | RegionNode _ -> "a region marker pair", isRegionStart cursor
                | TextNode _
                | FragmentNode _
                | ChildNode _
                | HeadNode _ -> invalidOp "Static nodes are not dynamic targets"

            if not matches then
                let found =
                    if isNull (box cursor) then
                        "absent"
                    else
                        cursor.nodeName.ToLowerInvariant ()

                let at = path |> Array.map string |> String.concat "/"
                raise (restructured $"the dynamic node at path {at} should be {expected} but is {found}")

            count <- count + 1
        elif not (isNull (box cursor)) then
            if op = FirstChild then
#if FABLE_COMPILER
                cursor <- unbox cursor.firstChild
#else
                cursor <- Native.firstChildOf cursor
#endif
            elif op = NextSibling then
#if FABLE_COMPILER
                cursor <- unbox cursor.nextSibling
#else
                cursor <- Native.nextOrNull cursor
#endif
            else
#if FABLE_COMPILER
                cursor <- unbox cursor.parentNode
#else
                cursor <- Native.parentOrNull cursor
#endif

let private tables (targets: (TemplateNode * int array) array) (owners: int array) (part: int) (first: int) (count: int) =
    let renderSlots = ResizeArray<int * Slot>()
    let isolatedSlots = ResizeArray<int * Slot>()
    let properties = ResizeArray<int * PortableProperty * obj>()
    let events = ResizeArray<int * string * Func<obj, obj, unit>>()
    let placed = ResizeArray<int * PlacedKind * Func<obj, obj, obj, obj>>()

    let add index (slot: Slot) =
        if slot.Mode = IsolatedMode then
            isolatedSlots.Add (index, slot)
        else
            renderSlots.Add (index, slot)

    for absolute in first .. first + count - 1 do
        if owners[absolute] = part then
            let index = absolute - first

            match fst (targets[absolute]) with
            | TextSlotNode slot -> add index slot
            | ElementNode (_, _, elementProperties, slots, elementEvents, elementPlaced, _) ->
                for property, value in elementProperties do
                    properties.Add (index, property, value)

                for slot in slots do
                    add index slot

                for eventName, handler in elementEvents do
                    events.Add (index, eventName, handler)

                for kind, hook in elementPlaced do
                    placed.Add (index, kind, hook)
            | RegionNode _ -> ()
            | TextNode _
            | FragmentNode _
            | ChildNode _
            | HeadNode _ -> invalidOp "Static nodes are not dynamic targets"

    renderSlots.ToArray (), isolatedSlots.ToArray (), properties.ToArray (), events.ToArray (), placed.ToArray ()

let private compilePart
    (layout: SkeletonLayout)
    (part: int)
    (first: int)
    (count: int)
    (skeleton: Node)
    (rootCount: int)
    (walk: int array)
    (regions: CompiledRegion array)
    (children: CompiledChild array)
    (expect: Expectation array)
    (hash: Lazy<string>)
    : Compiled =
    let renderSlots, isolatedSlots, properties, events, placed =
        tables layout.Targets layout.Owners part first count

    let eventNames =
        events
        |> Array.map (fun (_, eventName, _) -> eventName)
        |> Array.distinct

    {
        Skeleton = skeleton
        RootCount = rootCount
        Walk = walk
        NodeCount = count
        RenderSlots = renderSlots
        IsolatedSlots = isolatedSlots
        Properties = properties
        Events = events
        Placed = placed
        Heads =
            layout.Heads
            |> Array.filter (fun (_, owner, _) -> owner = part)
            |> Array.map (fun (head, _, position) -> head, position - first)
        EventNames = eventNames
        Dispatch = dispatchFor count events eventNames
        Regions = regions
        Children = children
        Expect = expect
        Hash = hash
    }

let private noHash = lazy ""

let private expectation (target: TemplateNode) : Expectation =
    match target with
    | ElementNode (tag, _, _, _, _, _, _) -> ExpectElement (name tag)
    | TextSlotNode _ -> ExpectText
    | RegionNode region ->
        ExpectRegion (
            match region.Row with
            | ItemRow _ -> ListRows
            | ArmRow -> SelectedArm
            | ViewRow -> ViewHole
            | BoundaryRow -> BoundaryContent
        )
    | TextNode _
    | FragmentNode _
    | ChildNode _
    | HeadNode _ -> raise (InvalidOperationException "Static nodes are not dynamic targets")

/// <summary>Compiles a definition once: parses its skeleton markup and builds its walk, its slot tables, its regions and its inlined children's slot tables.</summary>
/// <remarks>A skeleton with regions belongs to the main document, as do its clones; a skeleton without regions belongs to the template element's inert document.</remarks>
/// <exception cref="T:System.InvalidOperationException">The HTML parser restructures the markup, moving a dynamic node or changing the root count.</exception>
let compile (definition: TemplateComponent) : Compiled =
    let layout = skeletonLayout definition.Root
    let targets = layout.Targets
    let rootCount = layout.RootCount

    // The number of regions compiled so far, per part.
    let partRegions = Array.zeroCreate<int>(layout.Children.Length + 1)

    let regions =
        [|
            for index in 0 .. targets.Length - 1 do
                match targets[index] with
                | RegionNode region, path ->
                    let owner = layout.Owners[index]
                    let position = partRegions[owner]
                    partRegions[owner] <- position + 1

                    {
                        CompiledRegion.Marker = index
                        Owner = owner
                        Index = position
                        RootLevel = rootCount > 1 && path.Length = 1
                        Items = region.Items
                        Key = region.Key
                        Kind =
                            match region.Row with
                            | ItemRow _ -> ListRows
                            | ArmRow -> SelectedArm
                            | ViewRow -> ViewHole
                            | BoundaryRow -> BoundaryContent
                        Render = rowRenderer region.Row
                        Row = region.Row
                    }
                | _ -> ()
        |]

    let template = Native.createElement "template" :?> HTMLTemplateElement
    let parse = Departures.seam.Parse

    if isNull parse then
        template.innerHTML <- layout.Markup
    else
        parse.Invoke (template, layout.Markup)

    let content =
        if regions.Length = 0 then
            template.content
        else
            importDeep template.content :?> DocumentFragment

    let paths = Array.map snd targets
    let walk = walkProgram paths
    validateParsed content walk targets rootCount

    let children =
        layout.Children
        |> Array.mapi (fun index child ->
            {
                CompiledChild.Definition = child.Definition
                Props = child.Props
                Owner = child.Owner
                Offset = child.First
                Tables = compilePart layout (index + 1) child.First child.Count Unchecked.defaultof<Node> 0 [||] [||] [||] [||] noHash
            })

    compilePart
        layout
        0
        0
        targets.Length
        (if rootCount = 1 then
#if FABLE_COMPILER
             unbox content.firstChild
#else
             Native.firstChildOf content
#endif
         else
             content :> Node)
        rootCount
        (if rootCount = 1 then
             walkProgram (paths |> Array.map (fun path -> path[1..]))
         else
             walk)
        regions
        children
        (targets
         |> Array.map (fun (target, _) -> expectation target))
        (lazy (structuralHash definition))

/// <summary>The mount-owned operations a template instance uses.</summary>
type internal MountServices =
    abstract Append: Node -> unit
    abstract Setup: Setup
    abstract SettingUp: unit -> bool
    abstract SetupFailed: exn -> unit
    abstract RaiseSetupFailure: unit -> unit
    abstract IsDelegated: string -> bool
    abstract Delegate: string -> unit
    abstract ListenDirect: Node * string * (obj -> unit) -> unit
    abstract ReportFailure: exn -> unit
    abstract ClearFailure: unit -> unit
    /// <summary>The receiver of the pending reads and failures of the instance's bindings.</summary>
    abstract Scope: ViewScope
    /// <summary>The container that holds the instance's hooks until its nodes are placed.</summary>
    abstract Placement: Placement
    /// <summary>Binds a document head entry for the instance being materialised.</summary>
    abstract Head: HeadEntry -> unit
    /// <summary>The ids of the mount; an instance's setup runs in its window.</summary>
    abstract Identity: IdentityScope

    /// <summary>
    /// The cursor of the server nodes an instance adopts, which also records the edits of the instance's first
    /// commits; null outside hydration.
    /// </summary>
    abstract Hydration: HydrationCursor

    /// <summary>
    /// Mounts a region between its start and end markers. The flag marks markers that are roots of the appended clone;
    /// the materialiser, null for a view hole or a boundary, receives a row's current item, its services and its
    /// creation index. The region's rows sit under the owning part's path at the region's index.
    /// </summary>
    abstract Region:
        Node *
        Node *
        bool *
        RegionKind *
        KeyedItems *
        ((unit -> obj) -> Backend -> unit) *
        Func<(unit -> obj), MountServices, int, unit> *
        InstancePath *
        int ->
            unit

let private unset = obj ()

/// <summary>An array of <c>length</c> unassigned elements.</summary>
#if FABLE_COMPILER
[<Emit("new Array($0)")>]
let private allocate (length: int) : obj array = jsNative
#else
let private allocate (length: int) : obj array =
    Native.allocate length
#endif

/// <summary>Reads the element at an index; an index past the end reads <c>undefined</c>.</summary>
#if FABLE_COMPILER
[<Emit("$0[$1]")>]
let private at (array: 'T array) (index: int) : 'T = jsNative
#else
let private at (array: 'T array) (index: int) : 'T =
    Native.arrayAt array index
#endif

/// <summary>Writes the element at an index. Under Fable an index past the end grows the array; on .NET it raises.</summary>
#if FABLE_COMPILER
[<Emit("$0[$1] = $2")>]
let private store (array: obj array) (index: int) (value: obj) : unit = jsNative
#else
let private store (array: obj array) (index: int) (value: obj) : unit =
    Native.arraySet array index value
#endif

#if FABLE_COMPILER
[<Emit("$0.data = $1 == null ? '' : $1")>]
let private setData (node: obj) (value: obj) : unit = jsNative
#else
let private setData (node: obj) (value: obj) : unit =
    Native.setData node value
#endif

#if FABLE_COMPILER
[<Emit("$1 == null ? $0.removeAttribute($2) : $0.setAttribute($2, $1)")>]
let private setAttribute (node: obj) (value: obj) (name: string) : unit = jsNative
#else
let private setAttribute (node: obj) (value: obj) (name: string) : unit =
    Native.setAttributeOrRemove node value name
#endif

/// <summary>Marks a node as an event node of a template instance.</summary>
#if FABLE_COMPILER
[<Emit("$0.$$ranvier = $1")>]
let attachState (node: obj) (state: obj array) : unit = jsNative
#else
let attachState (node: obj) (state: obj array) : unit =
    Native.setExpando node "$$ranvier" state
#endif

let private write (slot: Slot) (node: obj) (value: obj) =
    match slot.Target with
    | AttributeTarget name -> setAttribute node value name
    | PropertyTarget property -> property.Write.Invoke (node, value)
    | TextTarget -> setData node value
    | ContentTarget -> (unbox<HTMLElement> node).innerHTML <- markupOf value

#if FABLE_COMPILER
[<Emit("$0.data !== ($1 == null ? '' : String($1))")>]
let private dataDiffers (node: obj) (value: obj) : bool = jsNative
#else
let private dataDiffers (node: obj) (value: obj) : bool =
    Native.dataOf (unbox<Node> node)
    <> (if isNull value then "" else Native.jsString value)
#endif

/// <summary>Whether the attribute differs from <c>value</c>, a string, or null for an absent attribute.</summary>
#if FABLE_COMPILER
[<Emit("$0.getAttribute($1) !== ($2 == null ? null : String($2))")>]
let private attributeDiffers (node: obj) (name: string) (value: obj) : bool = jsNative
#else
let private attributeDiffers (node: obj) (name: string) (value: obj) : bool =
    Option.toObj ((unbox<Element> node).getAttribute name)
    <> (if isNull value then null else Native.jsString value)
#endif

#if FABLE_COMPILER
[<Emit("$0.defaultValue !== ($1 == null ? '' : String($1))")>]
let private defaultValueDiffers (node: obj) (value: obj) : bool = jsNative
#else
let private defaultValueDiffers (node: obj) (value: obj) : bool =
    Native.defaultValueOf node
    <> (if isNull value then "" else Native.jsString value)
#endif

/// <summary>The inverse of a change to the node's data.</summary>
#if FABLE_COMPILER
[<Emit("(() => { const n = $0, d = n.data; return () => { n.data = d; }; })()")>]
let private keepData (node: obj) : unit -> unit = jsNative
#else
let private keepData (node: obj) : unit -> unit =
    let data = Native.dataOf (unbox<Node> node)
    fun () -> Native.setData node (box data)
#endif

/// <summary>The inverse of a change to the element's attributes: the attribute set is set back, in its order.</summary>
#if FABLE_COMPILER
[<Emit("(() => { const n = $0, a = Array.from(n.attributes, x => [x.name, x.value]); return () => { for (const x of Array.from(n.attributes)) n.removeAttribute(x.name); for (const [x, y] of a) n.setAttribute(x, y); }; })()")>]
let private keepAttributes (node: obj) : unit -> unit = jsNative
#else
let private keepAttributes (node: obj) : unit -> unit =
    let element = unbox<Element> node
    let attributes = Native.attributesOf element
    fun () -> Native.setAttributesBack element attributes
#endif

/// <summary>
/// The inverse of a property write: the element's children, its attribute set in order and its form state are set
/// back. A form value that is already equal is left untouched; a restored one marks its control as edited.
/// </summary>
#if FABLE_COMPILER
[<Emit("(() => { const n = $0, c = Array.from(n.childNodes), a = Array.from(n.attributes, x => [x.name, x.value]), v = n.value, k = n.checked, s = n.selected; return () => { n.replaceChildren(...c); for (const x of Array.from(n.attributes)) n.removeAttribute(x.name); for (const [x, y] of a) n.setAttribute(x, y); if ('value' in n && n.value !== v) n.value = v; if ('checked' in n && n.checked !== k) n.checked = k; if ('selected' in n && n.selected !== s) n.selected = s; }; })()")>]
let private keepElement (node: obj) : unit -> unit = jsNative
#else
let private keepElement (node: obj) : unit -> unit =
    let element = unbox<Element> node
    let children = Native.childNodesOf element
    let attributes = Native.attributesOf element
    let value, isChecked, selected = Native.formStateOf node

    fun () ->
        for child in Native.childNodesOf element do
            Native.removeNode child

        for child in children do
            Native.appendNode element child

        Native.setAttributesBack element attributes
        Native.restoreFormState node value isChecked selected
#endif

/// <summary>
/// Writes a property's value during hydration when the server HTML differs from the client's serialisation: an
/// attribute serialisation compares the attribute, a child-text property the element's <c>defaultValue</c>; a
/// browser-only property is always written. The write's inverse is recorded with <c>cursor</c>.
/// </summary>
let private writeHydratingProperty (cursor: HydrationCursor) (property: PortableProperty) (node: obj) (value: obj) =
    let differs =
        match property.Html with
        | AsAttribute (attribute, serialise) ->
            attributeDiffers
                node
                attribute
                (match serialise.Invoke value with
                 | Some text -> box text
                 | None -> null)
        | AsChildText -> defaultValueDiffers node value
        | BrowserOnly -> true

    if differs then
        let inverse = keepElement node
        property.Write.Invoke (node, value)
        cursor.Undo inverse

/// <summary>
/// Writes a slot's first value during hydration when it differs from the server node, recording the write's inverse
/// with <c>cursor</c>; raw content is always written.
/// </summary>
let private writeHydrating (cursor: HydrationCursor) (slot: Slot) (node: obj) (value: obj) =
    match slot.Target with
    | AttributeTarget name ->
        if attributeDiffers node name value then
            let inverse = keepAttributes node
            setAttribute node value name
            cursor.Undo inverse
    | PropertyTarget property -> writeHydratingProperty cursor property node value
    | TextTarget ->
        if dataDiffers node value then
            let inverse = keepData node
            setData node value
            cursor.Undo inverse
    | ContentTarget ->
        let inverse = keepElement node
        write slot node value
        cursor.Undo inverse

// A failed write keeps the slot's previous value. During setup the failure aborts the mount; afterwards it is reported.
// During hydration the first value is written when it differs from the node's current value, raw content always, and
// the write is recorded with the hydration cursor.
let private commit (services: MountServices) (state: obj array) (slot: Slot) (nodeIndex: int) (previousIndex: int) (value: obj) =
    let previous = at state previousIndex

    if obj.ReferenceEquals (value, unset) then
        match slot.Target with
        | TextTarget when obj.ReferenceEquals (previous, unset) ->
            let node = at state (StateNodes + nodeIndex)
            let cursor = services.Hydration

            if isNull cursor then
                setData node ""
            elif dataDiffers node "" then
                let inverse = keepData node
                setData node ""
                cursor.Undo inverse

            store state previousIndex (box "")
        | _ -> ()
    elif
        obj.ReferenceEquals (previous, unset)
        || not (slot.Same.Invoke (previous, value))
    then
        try
            if
                obj.ReferenceEquals (previous, unset)
                && not (isNull services.Hydration)
            then
                writeHydrating services.Hydration slot (at state (StateNodes + nodeIndex)) value
            else
                write slot (at state (StateNodes + nodeIndex)) value

            store state previousIndex value
        with error ->
            if services.SettingUp () then
                services.SetupFailed error
                reraise ()
            else
                services.ReportFailure error

let private collect (root: Node) (walk: int array) (state: obj array) =
    let mutable cursor = root
    let mutable count = 0

    for i in 0 .. walk.Length - 1 do
        let op = at walk i

        if op = FirstChild then
#if FABLE_COMPILER
            cursor <- unbox cursor.firstChild
#else
            cursor <- Native.firstChildOf cursor
#endif
        elif op = NextSibling then
#if FABLE_COMPILER
            cursor <- unbox cursor.nextSibling
#else
            cursor <- Native.nextOrNull cursor
#endif
        elif op = Parent then
#if FABLE_COMPILER
            cursor <- unbox cursor.parentNode
#else
            cursor <- Native.parentOrNull cursor
#endif
        else
            store state (StateNodes + count) (box cursor)
            count <- count + 1

/// <summary>A state array for an instance of <c>compiled</c>, with <c>extra</c> trailing elements and every previous value unset.</summary>
let private stateFor (compiled: Compiled) (instance: obj) (extra: int) =
    let renderCount = compiled.RenderSlots.Length
    let previousBase = StateNodes + compiled.NodeCount

    let pendingBase =
        previousBase
        + renderCount
        + compiled.IsolatedSlots.Length

    let state = allocate (pendingBase + renderCount + extra)
    store state StateCompiled (box compiled)
    store state StateInstance instance
    store state StateOwner (box (Api.getOwner ()))
    store state StateGraph (box Graph.Current)

    for index in previousBase .. pendingBase - 1 do
        store state index unset

    state

/// <summary>Starts the effect that reads every render slot of a part and commits them together.</summary>
let private startRender (services: MountServices) (state: obj array) =
    let scope = services.Scope
    let mutable flags = BindingState.started scope

    if scope.Tracks then
        Api.onCleanup (fun () ->
            let previous = flags
            flags <- BindingState.released previous
            BindingState.ready scope previous)

    Api.createEffect (fun () ->
        let compiled = at state StateCompiled :?> Compiled
        let instance = at state StateInstance
        let renderCount = compiled.RenderSlots.Length
        let previousBase = StateNodes + compiled.NodeCount

        let pendingBase =
            previousBase
            + renderCount
            + compiled.IsolatedSlots.Length

        let mutable failure: exn = null
        let mutable waiting = false

        for k in 0 .. renderCount - 1 do
            let _, slot = at compiled.RenderSlots k

            let value =
                try
                    slot.Read.Invoke instance
                with error ->
                    flags <- BindingState.rejected scope flags error
                    BindingState.report scope error

                    match error with
                    | NotReadyException _ -> waiting <- true
                    | _ when isNull failure -> failure <- error
                    | _ -> ()

                    unset

            store state (pendingBase + k) value

        Api.untrack (fun () ->
            for k in 0 .. renderCount - 1 do
                let nodeIndex, slot = at compiled.RenderSlots k
                commit services state slot nodeIndex (previousBase + k) (at state (pendingBase + k)))

        if flags <> 0 then
            if isNull failure then
                flags <- BindingState.recovered services.ClearFailure flags

            if not waiting then
                let previous = flags
                flags <- BindingState.released previous
                BindingState.ready scope previous

        if not (isNull failure) then
            raise failure)

    services.RaiseSetupFailure ()

/// <summary>Starts the effect of the isolated slot at index <c>k</c> of a part.</summary>
let private startIsolated (services: MountServices) (state: obj array) (k: int) =
    let scope = services.Scope
    let mutable flags = BindingState.started scope

    if scope.Tracks then
        Api.onCleanup (fun () ->
            let previous = flags
            flags <- BindingState.released previous
            BindingState.ready scope previous)

    Api.createEffect (fun () ->
        let compiled = at state StateCompiled :?> Compiled
        let nodeIndex, slot = at compiled.IsolatedSlots k

        let previousIndex =
            StateNodes
            + compiled.NodeCount
            + compiled.RenderSlots.Length
            + k

        let mutable failure: exn = null
        let mutable waiting = false

        let value =
            try
                slot.Read.Invoke (at state StateInstance)
            with error ->
                flags <- BindingState.rejected scope flags error
                BindingState.report scope error

                match error with
                | NotReadyException _ -> waiting <- true
                | _ -> failure <- error

                unset

        Api.untrack (fun () -> commit services state slot nodeIndex previousIndex value)

        if flags <> 0 then
            if isNull failure then
                flags <- BindingState.recovered services.ClearFailure flags

            if not waiting then
                let previous = flags
                flags <- BindingState.released previous
                BindingState.ready scope previous

        if not (isNull failure) then
            raise failure)

    services.RaiseSetupFailure ()

/// <summary>Attaches a direct listener for an event of a part's node.</summary>
let private listenDirect (services: MountServices) (state: obj array) (node: obj) (eventName: string) (handler: Func<obj, obj, unit>) =
    let owner = at state StateOwner :?> Owner
    let graph = at state StateGraph :?> Graph
    let instance = at state StateInstance
    let scope = services.Scope

    services.ListenDirect (
        unbox node,
        eventName,
        fun event ->
            if not owner.IsDisposed then
                try
                    graph.Run (fun () -> Api.runWithOwner owner (fun () -> Api.untrack (fun () -> handler.Invoke (instance, event))))
                with error ->
                    if not scope.Tracks then
                        reraise ()

                    scope.FailAll [ error ]
    )

/// <summary>
/// Writes a part's static properties, starts its render and isolated effects, attaches its events, and registers its
/// document head entries and placed hooks in document order. During hydration a static property is written as a slot's
/// first value is, and a placed hook on one of the adopted <c>roots</c> is a hook on a root.
/// </summary>
/// <remarks>The effects and listeners retain the state array and the services.</remarks>
let private bind (services: MountServices) (compiled: Compiled) (state: obj array) (roots: Node array) =
    let properties = compiled.Properties
    let cursor = services.Hydration

    for i in 0 .. properties.Length - 1 do
        let nodeIndex, property, value = at properties i

        try
            if not (isNull cursor) then
                writeHydratingProperty cursor property (at state (StateNodes + nodeIndex)) value
            else
                property.Write.Invoke (at state (StateNodes + nodeIndex), value)
        with error when services.SettingUp () ->
            services.SetupFailed error
            reraise ()

    if compiled.RenderSlots.Length > 0 then
        startRender services state

    for k in 0 .. compiled.IsolatedSlots.Length - 1 do
        startIsolated services state k

    let mutable attached = -1

    let events = compiled.Events

    let scope =
        if events.Length > 0 then
            services.Scope
        else
            Unchecked.defaultof<_>

    for i in 0 .. events.Length - 1 do
        let nodeIndex, eventName, handler = at events i
        let node = at state (StateNodes + nodeIndex)

        if services.IsDelegated eventName then
            if attached <> nodeIndex then
                attachState node state

                if scope.Tracks then
                    attachScope node scope

                attached <- nodeIndex

            services.Delegate eventName
        else
            listenDirect services state node eventName handler

    let placed = compiled.Placed
    let heads = compiled.Heads
    let instance = at state StateInstance
    let mutable head = 0

    if placed.Length > 0 then
        let owner = at state StateOwner :?> Owner

        for i in 0 .. placed.Length - 1 do
            let nodeIndex, kind, hook = at placed i

            while head < heads.Length
                  && snd (at heads head) <= nodeIndex do
                services.Head (headEntry (fst (at heads head)) instance)
                head <- head + 1

            let node: Node = unbox (at state (StateNodes + nodeIndex))

            let root =
                not (isNull roots)
                && Array.exists (fun (adopted: Node) -> obj.ReferenceEquals (adopted, node)) roots

            PlacedHook.register services.Placement services.Scope owner kind node root (not (isNull services.Hydration)) (fun flags ->
                unbox (hook.Invoke (instance, box node, flags)))

    while head < heads.Length do
        services.Head (headEntry (fst (at heads head)) instance)
        head <- head + 1

/// <summary>The instance of a part: the template's own for 0, an inlined child's from its state array otherwise.</summary>
let private partInstance (state: obj array) (childBase: int) (part: int) : obj =
    if part = 0 then
        at state StateInstance
    else
        at (unbox<obj array>(at state (childBase + part - 1))) StateInstance

/// <summary>Runs a template's setup on <c>props</c>, untracked, inside the instance's open identity window.</summary>
let private setUp (template: TemplateComponent) (services: MountServices) (props: obj) =
    let setup = ownSetup services.Identity services.Setup
    Api.untrack (fun () -> template.Setup.Invoke (setup, props))

/// <summary>
/// Runs an inlined child's setup on the props read from <c>owner</c>, the instance of its owning part, untracked,
/// inside the child's open identity window.
/// </summary>
let private setUpChild (services: MountServices) (child: CompiledChild) (owner: obj) =
    let setup = ownSetup services.Identity services.Setup
    Api.untrack (fun () -> child.Definition.Setup.Invoke (setup, child.Props.Invoke owner))

/// <summary>The server node's name for a mismatch: a comment's markup, an element's or text node's lowercase name, or <c>absent</c>.</summary>
let internal describe (node: Node) : string =
    if isNull (box node) then "absent"
    elif isComment node then "<!--" + dataOf node + "-->"
    else node.nodeName.ToLowerInvariant ()

#if FABLE_COMPILER
[<Emit("$0.parentNode.replaceChild($1, $2)")>]
let private replaceNode (existing: Node) (replacement: Node) (replaced: Node) : unit = jsNative
#else
let private replaceNode (existing: Node) (replacement: Node) (replaced: Node) : unit =
    Native.replaceNode existing replacement replaced
#endif

#if FABLE_COMPILER
[<Emit("$0.appendChild($1)")>]
let private appendNode (parent: Node) (node: Node) : unit = jsNative
#else
let private appendNode (parent: Node) (node: Node) : unit =
    Native.appendNode parent node
#endif

#if FABLE_COMPILER
[<Emit("$0.remove()")>]
let private removeNode (node: Node) : unit = jsNative
#else
let private removeNode (node: Node) : unit =
    Native.removeNode node
#endif

#if FABLE_COMPILER
[<Emit("$0 != null && $0.nodeType === 8 && $0.data === '_'")>]
let private isEmptyText (node: Node) : bool = jsNative
#else
let private isEmptyText (node: Node) : bool =
    not (Native.isNullish node)
    && node.nodeType = 8.0
    && Native.dataOf node = "_"
#endif

/// <summary>Replaces the <c>&lt;!--_--&gt;</c> placeholder with an empty text node, recording the inverse, and returns the text node.</summary>
let private replacePlaceholder (cursor: HydrationCursor) (placeholder: Node) : Node =
    let text = Native.createTextNode "" :> Node
    replaceNode placeholder text placeholder
    cursor.Undo (fun () -> replaceNode text placeholder text)
    text

let private pathText (path: ResizeArray<int>) =
    path |> Seq.map string |> String.concat "/"

/// <summary>
/// The mismatch path of <c>node</c>, an adopted node of the instance whose roots are <c>roots</c>: <c>#k</c> for the
/// k-th root, otherwise the root's index followed by the logical child index at each level below it.
/// </summary>
let private adoptedPath (roots: Node array) (node: Node) : string =
    let steps = ResizeArray<int>()
    let mutable current = node

    let rootIndex (candidate: Node) =
        roots
        |> Array.tryFindIndex (fun root -> obj.ReferenceEquals (root, candidate))

    let mutable root = rootIndex current

    while root.IsNone do
#if FABLE_COMPILER
        let parent: Node = unbox current.parentNode
#else
        let parent: Node = Native.parentOrNull current
#endif
#if FABLE_COMPILER
        let mutable sibling: Node = unbox parent.firstChild
#else
        let mutable sibling: Node = Native.firstChildOf parent
#endif
        let mutable position = 0

        while not (obj.ReferenceEquals (sibling, current)) do
            sibling <- logicalNext sibling
            position <- position + 1

        steps.Insert (0, position)
        current <- parent
        root <- rootIndex current

    if steps.Count = 0 then
        "#" + string root.Value
    else
        steps.Insert (0, root.Value)
        pathText steps

/// <summary>Whether <c>node</c> is an element whose lowercased name equals the lowercased name of the element <c>expected</c>.</summary>
#if FABLE_COMPILER
[<Emit("$0 != null && $0.nodeType === 1 && (($0.localName === $1.localName && $0.prefix === null && $1.prefix === null) || $0.nodeName.toLowerCase() === $1.nodeName.toLowerCase())")>]
let private sameElementName (node: Node) (expected: Node) : bool = jsNative
#else
let private sameElementName (node: Node) (expected: Node) : bool =
    not (Native.isNullish node)
    && node.nodeType = 1.0
    && ((Native.localNameOf node = Native.localNameOf expected
         && Native.isNullish (Native.prefixOf node)
         && Native.isNullish (Native.prefixOf expected))
        || node.nodeName.ToLowerInvariant () = expected.nodeName.ToLowerInvariant ())
#endif

/// <summary>Whether <c>node</c> is an element whose lowercased name equals the lowercased <c>tag</c>.</summary>
#if FABLE_COMPILER
[<Emit("$0 != null && $0.nodeType === 1 && (($0.localName === $1 && $0.prefix === null) || $0.nodeName.toLowerCase() === $1.toLowerCase())")>]
let private isElementNamed (node: Node) (tag: string) : bool = jsNative
#else
let private isElementNamed (node: Node) (tag: string) : bool =
    not (Native.isNullish node)
    && node.nodeType = 1.0
    && ((Native.localNameOf node = tag
         && Native.isNullish (Native.prefixOf node))
        || node.nodeName.ToLowerInvariant () = tag.ToLowerInvariant ())
#endif

/// <summary>The mismatch at the root position <c>#index</c>.</summary>
let private rootMismatch (index: int) (description: string) (found: Node) =
    mismatch ("#" + string index) description (describe found)

/// <summary>
/// Takes the instance's <c>RootCount</c> roots from the logical siblings at <c>cursor.Next</c>, each checked against the
/// skeleton root at its index, and moves the cursor past the last. A text root accepts a text node or the
/// <c>&lt;!--_--&gt;</c> placeholder, which is replaced; a start marker accepts any start marker, and another comment one
/// with the same data.
/// </summary>
/// <exception cref="T:Ranvier.View.Foundation.TemplateDom.HydrationMismatchException">A root differs from the skeleton's.</exception>
let private adoptRoots (cursor: HydrationCursor) (compiled: Compiled) : Node array =
    let count = compiled.RootCount
    let roots: Node array = Array.zeroCreate count
    let skeleton = compiled.Skeleton

    let mutable expected: Node =
#if FABLE_COMPILER
        if count = 1 then skeleton else unbox skeleton.firstChild
#else
        if count = 1 then skeleton else Native.firstChildOf skeleton
#endif

    let mutable node = cursor.Next

    for index in 0 .. count - 1 do
        if expected.nodeType = 1. then
            if not (sameElementName node expected) then
                raise (rootMismatch index ("<" + expected.nodeName.ToLowerInvariant () + ">") node)
        elif expected.nodeType = 3. then
            if isEmptyText node then
                node <- replacePlaceholder cursor node
            elif absent node || node.nodeType <> 3. then
                raise (rootMismatch index "a text node" node)
        elif isStartMarker expected then
            if not (isStartMarker node) then
                raise (rootMismatch index "a region marker" node)
        elif
            absent node
            || not (isComment node)
            || dataOf node <> dataOf expected
        then
            raise (rootMismatch index ("<!--" + dataOf expected + "-->") node)

        roots[index] <- node

        try
            node <- logicalNext node
        with HydrationMismatchException (_, closing, missing) ->
            raise (mismatch ("#" + string index) closing missing)

        expected <- following expected

    cursor.Next <- node
    roots

/// <summary>Whether the node is a start marker of a region of <c>kind</c>.</summary>
let private marksRegion (kind: RegionKind) (node: Node) =
    isStartMarker node
    && match kind with
       | ListRows
       | ViewHole -> dataOf node = "["
       | SelectedArm -> (dataOf node).StartsWith "[a:"
       | BoundaryContent ->
           let data = dataOf node
           data = "[c" || data = "[p" || data = "[e"

#if FABLE_COMPILER
[<Emit("$0 != null && $0.nodeType === 1 && ($0.localName === 'textarea' || $0.localName === 'title')")>]
let private isRcdataElement (node: Node) : bool = jsNative
#else
let private isRcdataElement (node: Node) : bool =
    not (Native.isNullish node)
    && node.nodeType = 1.0
    && (Native.localNameOf node = "textarea"
        || Native.localNameOf node = "title")
#endif

/// <summary>
/// The mismatch path of the position <c>walk</c> reaches before its op at <c>index</c>: the child index at each level,
/// in the skeleton's numbering, starting at the root's <c>0</c> for a single-root walk.
/// </summary>
let private walkPath (walk: int array) (single: bool) (index: int) : string =
    let path = ResizeArray<int>()

    if single then
        path.Add 0

    for position in 0 .. index - 1 do
        let op = at walk position

        if op = FirstChild then
            path.Add 0
        elif op = NextSibling then
            path[path.Count - 1] <- path[path.Count - 1] + 1
        elif op = Parent then
            path.RemoveAt (path.Count - 1)

    pathText path

/// <summary>
/// The server node a collected node adopts: <c>node</c> checked against <c>expect</c>. A text expectation replaces the
/// <c>&lt;!--_--&gt;</c> placeholder with an empty text node, and creates one in <c>parent</c> when it is an empty
/// <c>textarea</c> or <c>title</c>; each edit's inverse is recorded.
/// </summary>
/// <exception cref="T:Ranvier.View.Foundation.TemplateDom.HydrationMismatchException">The node differs from the expectation.</exception>
let private adoptTarget
    (cursor: HydrationCursor)
    (expect: Expectation)
    (walk: int array)
    (single: bool)
    (index: int)
    (parent: Node)
    (node: Node)
    : Node =
    match expect with
    | ExpectElement tag ->
        if not (isElementNamed node tag) then
            raise (mismatch (walkPath walk single index) ("<" + tag + ">") (describe node))

        node
    | ExpectText ->
        if isEmptyText node then
            replacePlaceholder cursor node
        elif absent node && isRcdataElement parent then
            let text = Native.createTextNode "" :> Node
            appendNode parent text
            cursor.Undo (fun () -> removeNode text)
            text
        elif absent node || node.nodeType <> 3. then
            raise (mismatch (walkPath walk single index) "a text node" (describe node))
        else
            node
    | ExpectRegion kind ->
        if not (marksRegion kind node) then
            raise (mismatch (walkPath walk single index) "a region marker" (describe node))

        node

/// <summary>
/// Collects the dynamic nodes of adopted roots into <c>state</c>, running the compiled walk with a logical
/// <c>NextSibling</c>: a start marker's next position is its matching end marker. A multi-root walk starts at the roots'
/// virtual fragment: its first <c>FirstChild</c> is the first root.
/// </summary>
let private collectAdopted (roots: Node array) (compiled: Compiled) (state: obj array) (cursor: HydrationCursor) =
    let walk = compiled.Walk
    let single = compiled.RootCount = 1
    let mutable node = if single then at roots 0 else noNode
    let mutable parent = noNode
    // The depth of `node` below the walk's start; at depth 1 of a multi-root walk, `rootIndex` is its root's index.
    let mutable depth = if single then 1 else 0
    let mutable rootIndex = 0
    let mutable count = 0

    for index in 0 .. walk.Length - 1 do
        let op = at walk index

        if op = FirstChild then
            let atFragment = depth = 0
            depth <- depth + 1
            parent <- node

            if atFragment then
                rootIndex <- 0
                node <- if roots.Length = 0 then noNode else at roots 0
            elif absent node then
                node <- noNode
            else
#if FABLE_COMPILER
                node <- unbox node.firstChild
#else
                node <- Native.firstChildOf node
#endif
        elif op = NextSibling then
            if depth = 1 then
                rootIndex <- rootIndex + 1

            if absent node then
                node <- noNode
            elif not single && depth = 1 then
                // A root's next position is the next adopted root.
                node <-
                    if rootIndex < roots.Length then
                        at roots rootIndex
                    else
                        noNode
            elif isStartMarker node then
                try
                    node <- matchingEnd node
                with HydrationMismatchException (_, closing, missing) ->
                    raise (mismatch (walkPath walk single index) closing missing)
            else
                node <- following node
        elif op = Parent then
            depth <- depth - 1
            node <- parent
#if FABLE_COMPILER
            parent <- if absent node then noNode else unbox node.parentNode
#else
            parent <- if absent node then noNode else Native.parentOrNull node
#endif
        else
            node <- adoptTarget cursor (at compiled.Expect count) walk single index parent node
            store state (StateNodes + count) (box node)
            count <- count + 1

#if FABLE_COMPILER
/// <summary>The definition's compiled form, compiling it on first use.</summary>
let internal compiledOf (definition: TemplateDefinition) : Compiled =
    if isNull definition.Compiled then
        definition.Compiled <- box (compile (definition :?> TemplateComponent))

    definition.Compiled :?> Compiled
#else
// Per document: the compiled forms, whose template nodes belong to that document and its thread.
let private compiledByDocument =
    System.Runtime.CompilerServices.ConditionalWeakTable<obj, System.Collections.Generic.Dictionary<TemplateDefinition, Compiled>>()

/// <summary>The definition's compiled form in the host document, compiling it on first use there.</summary>
let internal compiledOf (definition: TemplateDefinition) : Compiled =
    let compiled =
        compiledByDocument.GetValue (
            box (Native.document ()),
            fun _ -> System.Collections.Generic.Dictionary<TemplateDefinition, Compiled>(HashIdentity.Reference)
        )

    match compiled.TryGetValue definition with
    | true, existing -> existing
    | false, _ ->
        let created = compile (definition :?> TemplateComponent)
        compiled[definition] <- created
        created

/// <summary>The definition's compiled form in the host document, or <c>null</c> before its first use there.</summary>
let internal compiledIn (definition: TemplateDefinition) : obj =
    match compiledByDocument.TryGetValue (box (Native.document ())) with
    | true, compiled ->
        match compiled.TryGetValue definition with
        | true, existing -> box existing
        | false, _ -> null
    | false, _ -> null
#endif

/// <summary>
/// Materialises one instance at the position one step of <c>kind</c> below <c>parent</c>: runs setup, clones the
/// skeleton or, during hydration, adopts the server nodes at the cursor, binds its slots, events, head entries and
/// placed hooks and those of its inlined children, appends it and mounts its regions.
/// </summary>
let rec internal materialise
    (definition: TemplateDefinition)
    (props: obj)
    (services: MountServices)
    (parent: InstancePath)
    (kind: int)
    (a: int)
    (b: int)
    =
    let template = definition :?> TemplateComponent
    let compiled = compiledOf definition
    let children = compiled.Children
    let identity = services.Identity
    // The instance's path, taken only when a region or an inlined child sits under it.
    let mutable path: InstancePath = null
    identity.Enter (parent, kind, a, b)

    let instance =
        try
            let instance = setUp template services props

            if compiled.Regions.Length > 0 || children.Length > 0 then
                path <- identity.Path ()

            instance
        finally
            identity.Leave ()

    let cursor = services.Hydration
    let state = stateFor compiled instance children.Length
    let mutable clone = noNode
    let mutable roots: Node array = null

    if isNull cursor then
        clone <- compiled.Skeleton.cloneNode true
        collect clone compiled.Walk state
    else
        roots <- adoptRoots cursor compiled
        collectAdopted roots compiled state cursor

    bind services compiled state roots

    let childBase =
        StateNodes
        + compiled.NodeCount
        + 2 * compiled.RenderSlots.Length
        + compiled.IsolatedSlots.Length

    for k in 0 .. children.Length - 1 do
        let child = at children k

        identity.Enter (path, InstanceStep.Child, k, 0)

        let childInstance =
            try
                setUpChild services child (partInstance state childBase child.Owner)
            finally
                identity.Leave ()

        let tables = child.Tables
        let childState = stateFor tables childInstance 0

        for i in 0 .. tables.NodeCount - 1 do
            store childState (StateNodes + i) (at state (StateNodes + child.Offset + i))

        store state (childBase + k) (box childState)
        bind services tables childState roots

    if not (isNull cursor) then
        for index in 0 .. roots.Length - 1 do
            services.Append (at roots index)
    elif compiled.RootCount = 1 then
        services.Append clone
    else
#if FABLE_COMPILER
        let mutable root: Node = unbox clone.firstChild
#else
        let mutable root: Node = Native.firstChildOf clone
#endif

        while not (isNull (box root)) do
#if FABLE_COMPILER
            let next: Node = unbox root.nextSibling
#else
            let next: Node = Native.nextOrNull root
#endif
            services.Append root
            root <- next

    let regions = compiled.Regions
    // The path of the part owning the latest region, kept for the regions of the same part that follow it.
    let mutable pathOwner = -1
    let mutable partPath: InstancePath = null

    for i in 0 .. regions.Length - 1 do
        let region = at regions i
        let owner = partInstance state childBase region.Owner

        if region.Owner <> pathOwner then
            pathOwner <- region.Owner

            partPath <-
                if region.Owner = 0 then
                    path
                else
                    path.Child (region.Owner - 1)

        let regionPath = partPath
        let regionIndex = region.Index
        let start: Node = unbox (at state (StateNodes + region.Marker))

        let finish: Node =
            if isNull cursor then
#if FABLE_COMPILER
                unbox start.nextSibling
#else
                Native.nextOrNull start
#endif
            else
                try
                    matchingEnd start
                with HydrationMismatchException (_, closing, absent) ->
                    raise (mismatch (adoptedPath roots start) closing absent)

        let items = region.Items

        let read =
            match region.Kind with
            | ViewHole
            | BoundaryContent ->
                let snapshot = items.Invoke owner
                fun () -> snapshot
            | ListRows
            | SelectedArm -> fun () -> items.Invoke owner

        let rows =
            match region.Row with
            | ItemRow (rowDefinition, rowProps) ->
                Func<(unit -> obj), MountServices, int, unit>(fun current rowServices row ->
                    materialise rowDefinition (rowProps.Invoke current) rowServices regionPath InstanceStep.Row regionIndex row)
            | ArmRow ->
                Func<(unit -> obj), MountServices, int, unit>(fun current rowServices row ->
                    let arm = unbox<Arm>(current ())
                    materialise arm.Definition (arm.Reader.Invoke current) rowServices regionPath InstanceStep.Row regionIndex row)
            | ViewRow
            | BoundaryRow -> null

        services.Region (
            start,
            finish,
            region.RootLevel,
            region.Kind,
            { Read = read; Key = region.Key },
            region.Render,
            rows,
            regionPath,
            regionIndex
        )
