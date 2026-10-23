module Ranvier.View.Dom.Mounting

open System
open Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Fable.Core.TS
open Ranvier.View
open Ranvier.View.Representation
open Ranvier.View.Dom.Scheduling

type NativeOwnership =
    | Owned
    | Borrowed

#if FABLE_COMPILER
[<Global>]
let private globalThis: GlobalThis = jsNative

[<Global "document">]
let private document: Document = jsNative
#endif

let private fragment () =
    Native.createDocumentFragment ()

let private comment (value: string) =
    Native.createComment value

let private text (value: string) =
    Native.createTextNode value

let private propertyEquals = Func<obj, obj, bool>(fun left right -> left = right)

let private attributeEquals =
    Func<string option, string option, bool>(fun left right -> left = right)

let inline private insert (parent: #Node) (node: #Node) (before: #Node option) : unit =
    let before: Node option = before |> Option.map (fun x -> x :> Node)

    parent.insertBefore (node, ?child = before)
    |> ignore

let inline private nextSiblingNode (node: #Node) : Node option =
    node.nextSibling
    |> Option.map (fun x -> x :> Node)

let private remove (node: ChildNode) : unit =
    node.remove ()

let private contains (parent: Node) (node: Node) : bool =
    parent.contains node

let private sameDocument (a: Node) (b: Node) : bool =
    match a.ownerDocument, b.ownerDocument with
    | None, None -> true
    | Some left, Some right -> obj.ReferenceEquals (left, right)
    | _ -> false

let private inputValue (node: HTMLInputElement) (value: string) : unit =
    node.value <- value

let private attributeValue (node: Element) name =
    function
    | Some value -> node.setAttribute (name, value)
    | None -> node.removeAttribute name

let private isDocumentFragment (node: Node) : bool =
    node.nodeType = 11.

let private invalid message =
    raise (InvalidOperationException message)

let private same a b =
    obj.ReferenceEquals (a, b)

#if FABLE_COMPILER
let inline private optionSame expected candidate =
    match candidate with
    | Some actual -> obj.ReferenceEquals (expected, actual)
    | None -> false
#endif

let rec private walkPrepared visit (node: Node) =
    visit node

    let children =
        if
            node.nodeType = 1.
            && (node :?> Element).tagName = "TEMPLATE"
        then
            (node :?> HTMLTemplateElement).content :> Node
        else
            node

    let mutable child = children.firstChild

    while child.IsSome do
        let current = child.Value
        child <- current.nextSibling
        walkPrepared visit current

let private createClaims () : WeakSet<Node> =
#if FABLE_COMPILER
    globalThis.WeakSet.Create ()
#else
    Native.newNodeWeakSet ()
#endif

let private claims = createClaims ()

#if FABLE_COMPILER
[<Emit("new Set(['beforeinput', 'click', 'contextmenu', 'dblclick', 'focusin', 'focusout', 'input', 'keydown', 'keyup', 'mousedown', 'mousemove', 'mouseout', 'mouseover', 'mouseup', 'pointerdown', 'pointermove', 'pointerout', 'pointerover', 'pointerup', 'touchend', 'touchmove', 'touchstart'])")>]
let private createDelegatedEvents () : obj = jsNative
#else
let private createDelegatedEvents () : obj =
    let events = Native.newSet ()

    for name in
        [
            "beforeinput"
            "click"
            "contextmenu"
            "dblclick"
            "focusin"
            "focusout"
            "input"
            "keydown"
            "keyup"
            "mousedown"
            "mousemove"
            "mouseout"
            "mouseover"
            "mouseup"
            "pointerdown"
            "pointermove"
            "pointerout"
            "pointerover"
            "pointerup"
            "touchend"
            "touchmove"
            "touchstart"
        ] do
        Native.add events name

    events
#endif

/// <summary>Bubbling events dispatched from one listener per mount host.</summary>
let private delegatedEvents = createDelegatedEvents ()

#if FABLE_COMPILER
[<Emit("$0.has($1)")>]
let private isDelegated (events: obj) (eventName: string) : bool = jsNative
#else
let private isDelegated (events: obj) (eventName: string) : bool =
    Native.has events eventName
#endif

#if FABLE_COMPILER
[<Emit("$0['$$ranvier:' + $1] = $2")>]
let private setDelegatedHandler (node: Node) (eventName: string) (handler: obj -> unit) : unit = jsNative
#else
let private setDelegatedHandler (node: Node) (eventName: string) (handler: obj -> unit) : unit =
    Native.setExpando node ("$$ranvier:" + eventName) handler
#endif

#if FABLE_COMPILER
[<Emit("new Map()")>]
let private createSharedListeners () : obj = jsNative
#else
let private createSharedListeners () : obj =
    Native.newMap ()
#endif

/// <summary>Per event name: the listener shared by every host.</summary>
let private sharedListeners = createSharedListeners ()

#if FABLE_COMPILER
[<Emit("new WeakMap()")>]
let private createHostCounts () : obj = jsNative
#else
let private createHostCounts () : obj =
    Native.newWeakMap ()
#endif

/// <summary>Per host: the number of mounts using each shared listener.</summary>
let private hostCounts = createHostCounts ()

#if FABLE_COMPILER
// The listener runs with its host as `this` and walks from the target to that host, presenting each handled node as currentTarget. A node with a handler runs it; for a template event node, `TemplateDom.dispatch` receives its instance state. An inner host records itself on the event, so an outer host resumes above it.
let private delegateFrom: Func<Node, string, unit> =
    JsInterop.emitJsExpr
        (sharedListeners, TemplateDom.dispatch, hostCounts)
        """(host, eventName) => { let listener = $0.get(eventName); if (!listener) { const key = '$$ranvier:' + eventName; listener = function (event) { const host = this; let node = event.$$ranvierHost ? event.$$ranvierHost.parentNode : event.target; Object.defineProperty(event, 'currentTarget', { configurable: true, get: () => node }); while (node) { const handler = node[key]; if (handler) { handler(event); if (event.cancelBubble) break; } else if (node.$$ranvier !== undefined) { $1(node.$$ranvier, node, event); if (event.cancelBubble) break; } if (node === host) break; node = node.parentNode; } event.$$ranvierHost = host; }; $0.set(eventName, listener); } let counts = $2.get(host); if (!counts) $2.set(host, counts = new Map()); const count = counts.get(eventName) | 0; if (count === 0) host.addEventListener(eventName, listener); counts.set(eventName, count + 1); }"""

let private undelegateFrom: Func<Node, string, unit> =
    JsInterop.emitJsExpr
        (sharedListeners, hostCounts)
        """(host, eventName) => { const counts = $1.get(host); const count = counts ? counts.get(eventName) | 0 : 0; if (count === 1) host.removeEventListener(eventName, $0.get(eventName)); if (count > 0) counts.set(eventName, count - 1); }"""
#else
type private HostListeners = Collections.Generic.Dictionary<string, struct (int * (unit -> unit))>

// Walks from the event's target, or from above the inner host recorded on the event, to `host`, presenting each node
// as currentTarget: a node's direct handler runs, a template event node dispatches with its instance state, and
// `cancelBubble` ends the walk.
let private walk (host: Node) (eventName: string) (event: obj) =
    let key = "$$ranvier:" + eventName
    let inner: Node = Native.expando event "$$ranvierHost"

    let mutable node: Node =
        if Native.isNullish inner then
            unbox<Node>(Native.valueOrNull (unbox<Event> event).target)
        else
            Native.parentOrNull inner

    Native.overrideCurrentTarget event (fun () -> box node)
    let mutable going = true

    while going && not (Native.isNullish node) do
        let handler: obj = Native.expando node key

        if not (Native.isNullish handler) then
            (unbox<obj -> unit> handler) event
            going <- not (unbox<Event> event).cancelBubble
        else
            let state: obj array = Native.expando node "$$ranvier"

            if not (Native.isNullish state) then
                TemplateDom.dispatch.Invoke (state, node, event)
                going <- not (unbox<Event> event).cancelBubble

        if going then
            if obj.ReferenceEquals (node, host) then
                going <- false
            else
                node <- Native.parentOrNull node

    Native.setExpando event "$$ranvierHost" host

let private listenersOf (host: Node) : HostListeners =
    match Native.get<Node, HostListeners> hostCounts host with
    | null ->
        let created = HostListeners ()
        Native.set hostCounts host created
        created
    | existing -> existing

let private delegateFrom: Func<Node, string, unit> =
    Func<Node, string, unit>(fun host eventName ->
        let listeners = listenersOf host

        match listeners.TryGetValue eventName with
        | true, struct (count, remove) -> listeners[eventName] <- struct (count + 1, remove)
        | false, _ -> listeners[eventName] <- struct (1, Native.listen host eventName (walk host eventName)))

let private undelegateFrom: Func<Node, string, unit> =
    Func<Node, string, unit>(fun host eventName ->
        match Native.get<Node, HostListeners> hostCounts host with
        | null -> ()
        | listeners ->
            match listeners.TryGetValue eventName with
            | true, struct (1, remove) ->
                remove ()
                listeners.Remove eventName |> ignore
            | true, struct (count, remove) -> listeners[eventName] <- struct (count - 1, remove)
            | false, _ -> ())
#endif

let private claim (registry: WeakSet<Node>) (node: Node) : unit =
    registry.add node |> ignore

let private unclaim (registry: WeakSet<Node>) (node: Node) : unit =
    registry.delete node |> ignore

#if FABLE_COMPILER
[<Emit("(() => { for (let p = $1; p; p = p.parentNode) { if ($0.has(p)) return true; } if (!$1.firstChild) return false; const pending = Array.from($1.childNodes); while (pending.length) { const n = pending.pop(); if ($0.has(n)) return true; for (let c = n.firstChild; c; c = c.nextSibling) pending.push(c); } return false; })()")>]
let private overlaps (registry: WeakSet<Node>) (node: Node) : bool = jsNative
#else
let private overlaps (registry: WeakSet<Node>) (node: Node) : bool =
    let mutable found = false
    let mutable cursor = node

    while not found && not (Native.isNullish cursor) do
        found <- registry.has cursor
        cursor <- Native.parentOrNull cursor

    let pending = Collections.Generic.Stack<Node>(Native.childNodesOf node)

    while not found && pending.Count > 0 do
        let current = pending.Pop ()
        found <- registry.has current

        for child in Native.childNodesOf current do
            pending.Push child

    found
#endif

#if FABLE_COMPILER
[<Emit("$0.parentNode === $1")>]
let private isChildOf (node: Node) (parent: Node) : bool = jsNative
#else
let private isChildOf (node: Node) (parent: Node) : bool =
    obj.ReferenceEquals (Native.parentOrNull node, parent)
#endif

#if FABLE_COMPILER
[<Emit("$0.parentNode == null")>]
let private isDetached (node: Node) : bool = jsNative
#else
let private isDetached (node: Node) : bool =
    Native.isNullish (Native.parentOrNull node)
#endif

#if FABLE_COMPILER
[<Emit("$0.$$ranvierWidget === true")>]
let private isWidget (node: Node) : bool = jsNative
#else
let private isWidget (node: Node) : bool =
    Native.expando<bool> node "$$ranvierWidget"
#endif

#if FABLE_COMPILER
[<Emit("$0.$$ranvierWidget = true")>]
let private markWidget (node: Node) : unit = jsNative
#else
let private markWidget (node: Node) : unit =
    Native.setExpando node "$$ranvierWidget" true
#endif

/// <summary>The node's next sibling, or <c>null</c> after the last child.</summary>
#if FABLE_COMPILER
[<Emit("$0.nextSibling")>]
let private nextOrNull (node: Node) : Node = jsNative
#else
let private nextOrNull (node: Node) : Node =
    Native.nextOrNull node
#endif

let private nextNode = System.Func<Node, Node>(fun node -> nextOrNull node)

#if FABLE_COMPILER
[<Emit("$0.textContent = ''")>]
let private clearChildren (parent: Node) : unit = jsNative
#else
let private clearChildren (parent: Node) : unit =
    parent.textContent <- Some ""
#endif

/// <summary>Removes the siblings between <c>first</c> and <c>last</c> in one operation.</summary>
#if FABLE_COMPILER
[<Emit("(() => { const range = $0.ownerDocument.createRange(); range.setStartAfter($0); range.setEndBefore($1); range.deleteContents(); })()")>]
let private deleteBetween (first: Node) (last: Node) : unit = jsNative
#else
let private deleteBetween (first: Node) (last: Node) : unit =
    Native.deleteBetween first last
#endif

/// <summary>Whether <c>first</c> and <c>last</c> are the first and last children of a document fragment.</summary>
#if FABLE_COMPILER
[<Emit("$0.parentNode != null && $0.parentNode.nodeType === 11 && $0.parentNode.firstChild === $0 && $0.parentNode.lastChild === $1")>]
let private fillsFragment (first: Node) (last: Node) : bool = jsNative
#else
let private fillsFragment (first: Node) (last: Node) : bool =
    let parent = Native.parentOrNull first

    not (Native.isNullish parent)
    && parent.nodeType = 11.0
    && obj.ReferenceEquals (Native.firstChildOf parent, first)
    && obj.ReferenceEquals (Native.lastChildOf parent, last)
#endif

#if FABLE_COMPILER
[<Emit("$0.parentNode")>]
let private parentOrNull (node: Node) : Node = jsNative
#else
let private parentOrNull (node: Node) : Node =
    Native.parentOrNull node
#endif

/// <summary>Reads an element without a bounds check.</summary>
#if FABLE_COMPILER
[<Emit("$0[$1]")>]
let private at (items: ResizeArray<'T>) (index: int) : 'T = jsNative
#else
let private at (items: ResizeArray<'T>) (index: int) : 'T =
    Native.listAt items index
#endif

/// <summary>Writes an element without a bounds check.</summary>
#if FABLE_COMPILER
[<Emit("$0[$1] = $2")>]
let private setAt (items: ResizeArray<'T>) (index: int) (value: 'T) : unit = jsNative
#else
let private setAt (items: ResizeArray<'T>) (index: int) (value: 'T) : unit =
    Native.listSet items index value
#endif

/// <summary>Removes the elements from <c>count</c> onwards.</summary>
#if FABLE_COMPILER
[<Emit("$0.length = $1")>]
let private truncate (items: ResizeArray<'T>) (count: int) : unit = jsNative
#else
let private truncate (items: ResizeArray<'T>) (count: int) : unit =
    Native.listTruncate items count
#endif

/// <summary>Reads an array element without a bounds check.</summary>
#if FABLE_COMPILER
[<Emit("$0[$1]")>]
let private item (items: 'T array) (index: int) : 'T = jsNative
#else
let private item (items: 'T array) (index: int) : 'T =
    Native.arrayAt items index
#endif

/// <summary>Writes an array element without a bounds check.</summary>
#if FABLE_COMPILER
[<Emit("$0[$1] = $2")>]
let private setItem (items: 'T array) (index: int) (value: 'T) : unit = jsNative
#else
let private setItem (items: 'T array) (index: int) (value: 'T) : unit =
    Native.arraySet items index value
#endif

#if FABLE_COMPILER
[<Emit("$0 == null")>]
let private isNullish (value: 'T) : bool = jsNative
#else
let private isNullish (value: 'T) : bool =
    Native.isNullish value
#endif

#if FABLE_COMPILER
[<Emit("$0.slice()")>]
let private copyRows (rows: 'T array) : 'T array = jsNative
#else
let private copyRows (rows: 'T array) : 'T array =
    Native.arrayCopy rows
#endif

/// <summary>Replaces <c>count</c> elements from <c>start</c> with <c>inserted</c>.</summary>
#if FABLE_COMPILER
[<Emit("$0.splice($1, $2, ...$3)")>]
let private spliceRows (rows: 'T array) (start: int) (count: int) (inserted: 'T array) : unit = jsNative
#else
let private spliceRows (rows: 'T array) (start: int) (count: int) (inserted: 'T array) : unit =
    raise (System.InvalidOperationException "A .NET array cannot change length; the keyed pass builds a new array on .NET")
#endif

/// <summary>Replaces <c>count</c> elements from <c>start</c> with <c>inserted</c>.</summary>
#if FABLE_COMPILER
[<Emit("$0.splice($1, $2, ...$3)")>]
let private spliceList (items: ResizeArray<'T>) (start: int) (count: int) (inserted: 'T array) : unit = jsNative
#else
let private spliceList (items: ResizeArray<'T>) (start: int) (count: int) (inserted: 'T array) : unit =
    Native.listSplice items start count inserted
#endif

#if FABLE_COMPILER
[<Emit("$0.slice($1, $2)")>]
let private sliceRows (rows: 'T array) (start: int) (finish: int) : 'T array = jsNative
#else
let private sliceRows (rows: 'T array) (start: int) (finish: int) : 'T array =
    Native.arraySlice rows start finish
#endif

/// <summary>The largest changed window whose rows a keyed pass splices into a copy of the previous rows.</summary>
let private smallWindow = 64

#if FABLE_COMPILER
[<Emit("new Map()")>]
let private rowMap () : obj = jsNative
#else
let private rowMap () : obj =
    Native.newMap ()
#endif

#if FABLE_COMPILER
[<Emit("$0.set($1, $2)")>]
let private rowMapSet (map: obj) (key: 'T) (value: int) : unit = jsNative
#else
let private rowMapSet (map: obj) (key: 'T) (value: int) : unit =
    Native.set map key value
#endif

/// <summary>The value stored for <c>key</c>, or -1.</summary>
#if FABLE_COMPILER
[<Emit("($0.get($1) ?? -1)")>]
let private rowMapGet (map: obj) (key: 'T) : int = jsNative
#else
let private rowMapGet (map: obj) (key: 'T) : int =
    Native.getIndex map key
#endif

#if FABLE_COMPILER
[<Emit("new Set()")>]
let private rowSet () : obj = jsNative
#else
let private rowSet () : obj =
    Native.newSet ()
#endif

#if FABLE_COMPILER
[<Emit("$0.add($1)")>]
let private rowSetAdd (set: obj) (value: 'T) : unit = jsNative
#else
let private rowSetAdd (set: obj) (value: 'T) : unit =
    Native.add set value
#endif

#if FABLE_COMPILER
[<Emit("$0.has($1)")>]
let private rowSetHas (set: obj) (value: 'T) : bool = jsNative
#else
let private rowSetHas (set: obj) (value: 'T) : bool =
    Native.has set value
#endif

let private skip: unit -> unit = ignore

#if FABLE_COMPILER
[<Emit("$0 != null && $0.nodeType === 8 && $0.data === '!'")>]
let private isSeparator (node: Node) : bool = jsNative
#else
let private isSeparator (node: Node) : bool =
    not (Native.isNullish node)
    && node.nodeType = 8.0
    && Native.dataOf node = "!"
#endif

#if FABLE_COMPILER
[<Emit("$0 != null && $0.nodeType === 8 && $0.data === 'ranvier:row'")>]
let private isRowMarker (node: Node) : bool = jsNative
#else
let private isRowMarker (node: Node) : bool =
    not (Native.isNullish node)
    && node.nodeType = 8.0
    && Native.dataOf node = "ranvier:row"
#endif

#if FABLE_COMPILER
[<Emit("$0.data")>]
let private commentData (node: Node) : string = jsNative
#else
let private commentData (node: Node) : string =
    Native.dataOf node
#endif

/// <summary>Removes the server nodes between a region's markers and records their reinsertion before <c>regionEnd</c>.</summary>
let private clearAdopted (cursor: TemplateDom.HydrationCursor) (regionStart: Node) (regionEnd: Node) =
    let removed = ResizeArray<Node>()
    let mutable node = nextOrNull regionStart

    while not (isNull (box node))
          && not (same node regionEnd) do
        let next = nextOrNull node
        removed.Add node
        remove (node :?> ChildNode)
        node <- next

    if removed.Count > 0 then
        cursor.Undo (fun () ->
            let parent = parentOrNull regionEnd

            for index in 0 .. removed.Count - 1 do
                parent.insertBefore (removed[index], regionEnd)
                |> ignore)

/// <summary>The logical position of <c>node</c> among the nodes after <c>regionStart</c>, counting from 0.</summary>
let private positionAfter (regionStart: Node) (node: Node) =
    let mutable position = 0
    let mutable current = nextOrNull regionStart

    while not (same current node) do
        current <- TemplateDom.logicalNext current
        position <- position + 1

    position

#if RANVIER_VIEW_COUNTERS
/// <summary>Counts of keyed-list work, defined only in the DOM test build.</summary>
module internal ListCounters =
    /// <summary>Laid-out rows checked by keyed-list validation.</summary>
    let validatedRows = ref 0

    /// <summary>Rows looked up in a keyed list's projection.</summary>
    let lookups = ref 0

    /// <summary>Longest-run computations made by keyed-list layout.</summary>
    let increasingRuns = ref 0

/// <summary>Counts of boundary work, defined only in the DOM test build.</summary>
module internal BoundaryCounters =
    /// <summary>Writes of a boundary's wake signal.</summary>
    let wakes = ref 0
#endif

#if FABLE_COMPILER
[<Emit("new Set()")>]
let private nameSet () : obj = jsNative
#else
let private nameSet () : obj =
    Native.newSet ()
#endif

#if FABLE_COMPILER
[<Emit("$0.has($1)")>]
let private setHas (set: obj) (name: string) : bool = jsNative
#else
let private setHas (set: obj) (name: string) : bool =
    Native.has set name
#endif

#if FABLE_COMPILER
[<Emit("$0.add($1)")>]
let private setAdd (set: obj) (name: string) : unit = jsNative
#else
let private setAdd (set: obj) (name: string) : unit =
    Native.add set name
#endif

#if FABLE_COMPILER
[<Emit("$0.forEach($1)")>]
let private setIter (set: obj) (action: string -> unit) : unit = jsNative
#else
let private setIter (set: obj) (action: string -> unit) : unit =
    Native.forEach set action
#endif

/// <summary>For each position of <c>sources</c>, whether it belongs to one longest increasing run of its non-negative values.</summary>
let private increasingRun (sources: int array) : bool array =
#if RANVIER_VIEW_COUNTERS
    ListCounters.increasingRuns.Value <- ListCounters.increasingRuns.Value + 1
#endif
    let keep: bool array = Array.zeroCreate sources.Length
    let tails = ResizeArray<int>()
    let previous = Array.create sources.Length -1

    for index in 0 .. sources.Length - 1 do
        let value = sources[index]

        if value >= 0 then
            let mutable low = 0
            let mutable high = tails.Count

            while low < high do
                let middle = (low + high) >>> 1

                if sources[tails[middle]] < value then
                    low <- middle + 1
                else
                    high <- middle

            if low > 0 then
                previous[index] <- tails[low - 1]

            if low = tails.Count then
                tails.Add index
            else
                tails[low] <- index

    if tails.Count > 0 then
        let mutable cursor = tails[tails.Count - 1]

        while cursor >= 0 do
            keep[cursor] <- true
            cursor <- previous[cursor]

    keep

/// <summary>A region's failure state: set up failures recorded during the first pass, reports after it.</summary>
type private RegionErrors(report: exn list -> unit) =
    member val Initializing = true with get, set
    member val SetupFailure: exn option = None with get, set

    member _.Report(errors: exn list) =
        if not errors.IsEmpty then
            try
                Api.untrack (fun () -> report errors)
            with reporterError ->
                raise (AggregateException ("Keyed update and reporter failed", List.toArray (errors @ [ reporterError ])))

    /// <summary>Records <c>errors</c> as the region's setup failure during the first pass; afterwards reports them and raises <c>error</c>.</summary>
    member this.Fail(error: exn, errors: exn list) =
        if this.Initializing then
            this.SetupFailure <-
                Some (
                    match errors with
                    | [ single ] -> single
                    | _ ->
                        AggregateException (
                            "Keyed row setup and cleanup failed: "
                            + String.concat "; " (errors |> List.map _.Message),
                            errors
                        )
                        :> exn
                )
        else
            this.Report errors
            raise error

type private Entry =
    {
        Node: Node
        EndNode: Node
        Validate: unit -> unit
        Relocate: unit -> unit
        Restore: (unit -> unit) option
    }

/// <summary>
/// The nodes a mount owns in its host: its boundary entries <c>first</c> and <c>last</c>, and between them its roots
/// and its top-level regions' markers.
/// </summary>
type private OwnedRange(host: Node, first: Node, last: Node) =
    // Null until the range is first staged or moved.
    let mutable staging: DocumentFragment = Unchecked.defaultof<_>
    let entries = ResizeArray<Entry>()
    let mutable generatedDescendants: ResizeArray<Node> = null
    // The parent of the entries; null while they are detached and unstaged.
    let mutable placement: Node = Unchecked.defaultof<_>
    // False while every root is a detached node created by the mount; `Attach` then inserts each root individually. A keyed region, a native node or a cloned-fragment root moves all roots into `staging`.
    let mutable staged = false
#if LADDER_NAIVE_RANGE
    let naiveRoots = ResizeArray<Node>()
#endif

    let intactAt (parent: Node) =
        let mutable cursor: Node = first
        let mutable intact = true

        for index in 0 .. entries.Count - 1 do
            let entry = at entries index
            entry.Validate ()

            if
                not (isChildOf entry.Node parent)
                || not (same cursor entry.Node)
            then
                intact <- false

            let mutable spanCursor = entry.Node

            while not (isNull (box spanCursor))
                  && not (same spanCursor entry.EndNode) do
                spanCursor <- nextOrNull spanCursor

            if
                isNull (box spanCursor)
                || not (isChildOf entry.EndNode parent)
            then
                intact <- false

            cursor <- nextOrNull entry.EndNode

        intact

    let stagingFragment () =
        if isNull (box staging) then
            staging <- fragment ()

        staging

    let stage () =
        if not staged then
            staged <- true
            let staging = stagingFragment ()

            if isNull (box placement) then
                placement <- staging

            for index in 0 .. entries.Count - 1 do
                let node = (at entries index).Node

                if not (isDetached node || isChildOf node staging) then
                    invalid "Range boundaries or owned roots were externally mutated"

                staging.appendChild node |> ignore

    do
        entries.Add
            {
                Node = first
                EndNode = first
                Validate = skip
                Relocate = skip
                Restore = None
            }

        entries.Add
            {
                Node = last
                EndNode = last
                Validate = skip
                Relocate = skip
                Restore = None
            }

        claim claims first
        claim claims last

    /// <summary>A range whose boundary entries are new <c>ranvier:start</c> and <c>ranvier:end</c> comments.</summary>
    new(host: Node) = OwnedRange (host, comment "ranvier:start", comment "ranvier:end")

    /// <summary>The range's start boundary.</summary>
    member _.First = first
#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
    member internal _.Last = last
#endif

    /// <summary>Adds a root already in place in the host as an entry, claimed and left where it is.</summary>
    member _.AddAdopted(node: Node) =
        entries.Insert (
            entries.Count - 1,
            {
                Node = node
                EndNode = node
                Validate = skip
                Relocate = skip
                Restore = None
            }
        )

        claim claims node

    /// <summary>Places the range at its entries' current position: they must be children of <c>parent</c>, in order and contiguous.</summary>
    member _.Adopt(parent: Node) =
        if not (intactAt parent) then
            invalid "Adopted roots are not contiguous children of the host"

        placement <- parent

    member _.AddRegion(start: Node, finish: Node, validate: unit -> unit, relocate: unit -> unit) =
        stage ()

        entries.Insert (
            entries.Count - 1,
            {
                Node = start
                EndNode = finish
                Validate = validate
                Relocate = relocate
                Restore = None
            }
        )

        claim claims start
        claim claims finish
        insert staging start (Some last)
        insert staging finish (Some last)

    /// <summary>Joins a region's start and end markers, adjacent roots of the range, into one entry spanning the region's rows.</summary>
    member _.SpanRegion(start: Node, finish: Node, validate: unit -> unit, relocate: unit -> unit) =
        let mutable index = entries.Count - 2

        while index > 1
              && not (same (at entries index).Node finish) do
            index <- index - 1

        if
            index < 2
            || not (same (at entries (index - 1)).Node start)
        then
            invalid "Region markers must be adjacent roots of the range"

        entries[index - 1] <-
            {
                Node = start
                EndNode = finish
                Validate = validate
                Relocate = relocate
                Restore = None
            }

        entries.RemoveAt index

    /// <summary>The parent of the range's entries.</summary>
    member _.Placement: Node = placement

    member this.AppendGenerated(parent: Node, node: Node) =
        if isNull generatedDescendants then
            generatedDescendants <- ResizeArray ()

        generatedDescendants.Add node
        claim claims node
        parent.appendChild node |> ignore

#if LADDER_NAIVE_RANGE
    member _.AddNaive(node: Node) =
        stage ()
        naiveRoots.Add node
        insert staging node (Some last)

#endif
    /// <summary>Adds a root node created by the mount, either detached or a child of a cloned fragment.</summary>
    /// <remarks>The node and its subtree are unclaimed and contain neither the host nor the range.</remarks>
    member _.AddCreated(node: Node) =
        let attachNow = staged || not (isDetached node)

        if attachNow then
            stage ()

        entries.Insert (
            entries.Count - 1,
            {
                Node = node
                EndNode = node
                Validate = skip
                Relocate = skip
                Restore = None
            }
        )

        claim claims node

        if attachNow then
            insert staging node (Some last)

    member _.Add(node: ChildNode, ownership) =
        if
            contains node host
            || (not (isNull (box staging))
                && contains node staging)
        then
            invalid "A native node cannot contain its mount host or range"

        if overlaps claims node then
            invalid "Native ownership overlaps a live mount"

        let restore =
            match ownership with
            | Owned -> None
            | Borrowed ->
                let originalParent, originalNext = node.parentNode, node.nextSibling

                Some (fun () ->
                    match originalParent with
                    | None -> remove node
                    | Some parent ->
                        let before =
                            originalNext
#if FABLE_COMPILER
                            |> Option.filter (fun next -> next.parentNode |> optionSame parent)
#else
                            |> Option.filter (fun next -> next.parentNode |> Option.exists (same parent))
#endif

                        insert parent node before)

        stage ()

        entries.Insert (
            entries.Count - 1,
            {
                Node = node
                EndNode = node
                Validate = skip
                Relocate = skip
                Restore = restore
            }
        )

        claim claims node
        insert staging node (Some last)

    member _.Validate() =
        if isNull (box placement) then
            for index in 0 .. entries.Count - 1 do
                if not (isDetached (at entries index).Node) then
                    invalid "Range boundaries or owned roots were externally mutated"
        elif intactAt placement then
            ()
        elif
            isDocumentFragment placement
            && placement.firstChild.IsNone
        then
            let relocatedParent =
                first.parentNode
                |> Option.defaultWith (fun () -> invalid "Range boundaries or owned roots were externally mutated")

            if not (intactAt relocatedParent) then
                invalid "Range roots were not contiguous after their staging fragment was attached"

            placement <- relocatedParent
        else
            invalid "Range boundaries or owned roots were externally mutated"

    member private _.ValidateDestination(parent: Node, before: Node option) =
        if not (sameDocument first parent) then
            invalid "Cross-document movement requires an explicit adoption policy"

        let anchor: Node =
            match before with
            | Some anchor when not (isChildOf anchor parent) -> invalid "Insertion anchor does not belong to the destination"
            | Some anchor -> anchor
            | None -> Unchecked.defaultof<_>

        for index in 0 .. entries.Count - 1 do
            let entry = at entries index
            let mutable cursor = entry.Node

            while not (isNull (box cursor)) do
                let node = cursor

                if contains node parent then
                    invalid "Cannot move a range into itself"

                if same node anchor && not (same first anchor) then
                    invalid "Cannot insert a range inside its own boundaries"

                cursor <-
                    if same node entry.EndNode then
                        Unchecked.defaultof<_>
                    else
                        nextOrNull node

    member this.Attach(parent, before) =
#if !LADDER_NAIVE_RANGE
        this.Validate ()
        this.ValidateDestination (parent, before)
#endif
        if staged then
            insert parent staging before

            for index in 0 .. entries.Count - 1 do
                (at entries index).Relocate()
        else
            let mutable index = 0

            // An insertion that disposes the mount empties `entries`, ending the loop.
            while index < entries.Count do
                insert parent (at entries index).Node before
                index <- index + 1

        placement <- parent

    member _.Relocate() =
        placement <-
            first.parentNode
            |> Option.map (fun node -> node :> Node)
            |> Option.defaultWith (fun () -> invalid "Range has no parent")

        for entry in entries do
            entry.Relocate ()

    member this.Move(parent, before, rollbackFailed: unit -> unit) =
        this.Validate ()
        this.ValidateDestination (parent, before)

        if
#if FABLE_COMPILER
            not (before |> optionSame first)
#else
            not (before |> Option.exists (same first))
#endif
        then
            let originalParent, originalNext = placement, last.nextSibling
            let staging = stagingFragment ()

            try
                for entry in entries do
                    let mutable cursor = Some entry.Node

                    while cursor.IsSome do
                        let node = cursor.Value

                        cursor <-
                            if same node entry.EndNode then
                                None
                            else
                                nextSiblingNode node

                        staging.appendChild node |> ignore

                insert parent staging before
                placement <- parent

                for entry in entries do
                    entry.Relocate ()
            with original ->
                try
                    for entry in entries do
                        let mutable cursor = Some entry.Node

                        while cursor.IsSome do
                            let node = cursor.Value

                            cursor <-
                                if same node entry.EndNode then
                                    None
                                else
                                    nextSiblingNode node

                            staging.appendChild node |> ignore

                    insert originalParent staging originalNext
                    placement <- originalParent

                    for entry in entries do
                        entry.Relocate ()
                with rollback ->
                    rollbackFailed ()
                    raise (AggregateException ("Range movement and rollback failed", [| original; rollback |]))

                raise original

    /// <summary>Unclaims the range's nodes and removes them, or with <c>keep</c> leaves every node where it is.</summary>
    member _.Release(keep: bool, held: ResizeArray<unit -> unit>) =
        let errors = ResizeArray<exn>()

#if LADDER_NAIVE_RANGE
        for index in naiveRoots.Count - 1 .. -1 .. 0 do
            remove (naiveRoots[index] :?> ChildNode)

        naiveRoots.Clear ()

#endif
        if not (isNull generatedDescendants) then
            for index in generatedDescendants.Count - 1 .. -1 .. 0 do
                let node = at generatedDescendants index

                // A retiring release leaves each node to its root: held, or removed with it.
                if not keep && isNull held then
                    try
                        remove (node :?> ChildNode)
                    with error ->
                        errors.Add error

                unclaim claims node

            generatedDescendants.Clear ()

        for index in entries.Count - 1 .. -1 .. 0 do
            let entry = at entries index
            let spans = not (same entry.Node entry.EndNode)

            match entry.Restore with
            | _ when keep -> ()
            | Some restore ->
                try
                    restore ()
                with error ->
                    errors.Add error
            | None ->
                try
                    if
                        spans
                        || isNull held
                        || not (Departures.seam.Retire.Invoke (entry.Node, held))
                    then
                        remove (entry.Node :?> ChildNode)
                with error ->
                    errors.Add error

                if spans then
                    try
                        remove (entry.EndNode :?> ChildNode)
                    with error ->
                        errors.Add error

            unclaim claims entry.Node

            if spans then
                unclaim claims entry.EndNode

        entries.Clear ()

        if errors.Count = 1 then
            raise errors[0]
        elif errors.Count > 1 then
            raise (AggregateException ("Range release failed", errors))

/// <summary>How a keyed region's markers join the mount's range.</summary>
type private RegionPlacement =
    /// <summary>Markers created for an authored list at the range's top level.</summary>
    | NewRegion
    /// <summary>Markers already added as adjacent roots of the range.</summary>
    | RootRegion
    /// <summary>Markers inside an element.</summary>
    | NestedRegion

/// <summary>Arm selections are equal when both are null, or when their keys (<c>fst</c>) are equal under <c>=</c>.</summary>
let private sameArm =
    { new System.Collections.Generic.IEqualityComparer<obj> with
        member _.Equals(left, right) =
            if isNull left || isNull right then
                isNull left && isNull right
            else
                fst (unbox<obj * obj> left) = fst (unbox<obj * obj> right)

        member _.GetHashCode _ = 0
    }

/// <summary>The operations a view's build function uses during setup.</summary>
type BuildContext internal (state: MountState) =
    member _.Backend: Backend = state

    /// <summary>Adds a native node as a top-level root of the mount.</summary>
    member _.Native(node: Node, ownership: NativeOwnership) =
        state.Native (node, ownership)

    member _.OnCleanup(callback: unit -> unit) =
        state.Root.OnCleanup callback

    member _.Phase() =
        state.Root.Phase

and Mounted internal (state: MountState) =
    member _.Phase = state.Root.Phase
    member _.Failures = state.Root.Failures
#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
    /// <summary>The first and last node of the mount's range.</summary>
    member internal _.Range = state.RangeEnds

    member internal _.Graph = state.Graph
#endif
    member _.CommitErrors = state.CommitErrors

    member _.Flush() =
        if not state.Root.IsDisposed then
            state.Flush ()

    /// <summary>Checks that the mount's recorded DOM range remains intact.</summary>
    member internal _.Validate() =
        state.Validate ()

    member internal _.Relocate() =
        state.Relocate ()

    member _.Dispose() =
        state.Root.Dispose ()

    /// <summary>Tears the mount down; inside a teardown window serving its reporter, its errors join that window.</summary>
    member internal _.Abandon() =
        state.Root.Abandon ()

    /// <summary>
    /// Tears down a mount built with a cursor, leaving its nodes in place, and undoes the edits recorded with the cursor.
    /// </summary>
    member internal _.AbandonAdopted() =
        state.AbandonAdopted ()

    member _.MoveBefore(parent, before) =
        if state.Root.Phase <> Attached then
            invalid "Only an attached mount can move"

        state.Move (parent, before)

/// <summary>The nodes of a row or arm, from its first node to its last.</summary>
and [<Sealed; AllowNullLiteral>] private Row(first: Node, last: Node) =
    member _.First = first
    member _.Last = last
    member val Removed = false with get, set
    /// <summary>Whether the row's nodes are server nodes adopted by hydration.</summary>
    member val Adopted = false with get, set
    /// <summary>The nested mount of an authored row, or null for a template row.</summary>
    member val Mount: Mounted = Unchecked.defaultof<_> with get, set

    static member MoveBefore(row: Row, parent: Node, anchor: Node) =
        let mutable cursor = row.First
        let mutable going = true

        while going do
            let next = nextOrNull cursor

            if same cursor row.Last then
                going <- false

            parent.insertBefore (cursor, anchor) |> ignore
            cursor <- next

    /// <summary>
    /// Marks the row removed and removes its nodes. With <c>held</c>, a node that <c>Departures.seam.Retire</c> holds stays in
    /// place and joins <c>held</c>. A failure of one node's retirement is raised once every node is handled.
    /// </summary>
    static member Remove(row: Row, held: ResizeArray<unit -> unit>) =
        if not row.Removed then
            row.Removed <- true
            let mutable cursor = row.First
            let mutable going = true
            let mutable failure: exn = null

            while going do
                let next = nextOrNull cursor

                if same cursor row.Last then
                    going <- false

                try
                    if
                        isNull held
                        || not (Departures.seam.Retire.Invoke (cursor, held))
                    then
                        remove (cursor :?> ChildNode)
                with error ->
                    failure <- error

                cursor <- next

            if not (isNull failure) then
                raise failure

    /// <summary>Removes the nodes between the markers, which are children of <c>parent</c>; both markers stay in place.</summary>
    static member ClearBetween(parent: Node, regionStart: Node, regionEnd: Node) =
        if
#if FABLE_COMPILER
            parent.firstChild |> optionSame regionStart
            && parent.lastChild |> optionSame regionEnd
#else
            parent.firstChild
            |> Option.exists (same regionStart)
            && parent.lastChild |> Option.exists (same regionEnd)
#endif
        then
            clearChildren parent
            parent.appendChild regionStart |> ignore
            parent.appendChild regionEnd |> ignore
        else
            deleteBetween regionStart regionEnd

    /// <summary>Raises unless the markers share <c>parent</c> and the laid-out rows sit between them in order.</summary>
    static member Validate(regionStart: Node, regionEnd: Node, laidOut: ResizeArray<Row>, parent: Node) =
        Row.ValidateWindow (regionStart, regionEnd, laidOut, parent, 0, laidOut.Count - 1)

    /// <summary>
    /// Requires the markers to share <c>parent</c>, and each non-removed laid-out row from <c>first</c> to <c>last</c>
    /// (clamped to the laid-out range) to start at the node after the preceding row, or after <c>regionStart</c>, and to
    /// pass its nested mount's validation. A window that reaches the final row also requires the next node to be
    /// <c>regionEnd</c>. Raises <c>InvalidOperationException</c> otherwise.
    /// </summary>
    static member ValidateWindow(regionStart: Node, regionEnd: Node, laidOut: ResizeArray<Row>, parent: Node, first: int, last: int) =
        if
#if FABLE_COMPILER
            not (regionStart.parentNode |> optionSame parent)
            || not (regionEnd.parentNode |> optionSame parent)
#else
            not (
                regionStart.parentNode
                |> Option.exists (same parent)
            )
            || not (
                regionEnd.parentNode
                |> Option.exists (same parent)
            )
#endif
        then
            invalid "Keyed-list boundaries were externally mutated"

        let last = min last (laidOut.Count - 1)
        let mutable index = max first 0

        let step =
            if Departures.seam.Enabled then
                Departures.seam.NextLive
            else
                nextNode

        let mutable cursor =
            step.Invoke (if index = 0 then regionStart else laidOut[index - 1].Last)

        while index <= last do
            let row = laidOut[index]

            if not row.Removed then
#if RANVIER_VIEW_COUNTERS
                ListCounters.validatedRows.Value <- ListCounters.validatedRows.Value + 1
#endif
                if not (isNull (box row.Mount)) then
                    row.Mount.Validate ()

                if not (same cursor row.First) then
                    invalid "Keyed-list rows or contents were externally mutated"

                cursor <- step.Invoke row.Last

            index <- index + 1

        if
            last = laidOut.Count - 1
            && not (same cursor regionEnd)
        then
            invalid "Keyed-list boundaries were externally reordered"

    static member Relocate(laidOut: ResizeArray<Row>) =
        for index in 0 .. laidOut.Count - 1 do
            let row = laidOut[index]

            if not (isNull (box row.Mount)) then
                row.Mount.Relocate ()

    static member private Place(row: Row, parent: Node, anchor: Node) =
        Row.MoveBefore (row, parent, anchor)

        if not (isNull (box row.Mount)) then
            row.Mount.Relocate ()

    /// <summary>
    /// Moves the region's rows into the order of <c>next</c> and stores it as the laid-out rows. A window that exchanges
    /// its first and last rows moves those two rows; any other window moves the rows outside one longest increasing run.
    /// Stops when <c>live</c> turns false. The first <c>leading</c> and last <c>trailing</c> rows of <c>next</c> must be
    /// the laid-out rows at the same offsets from the start and from the end.
    /// </summary>
    static member Layout
        (
            live: unit -> bool,
            laidOut: ResizeArray<Row>,
            staging: DocumentFragment,
            parent: Node,
            regionEnd: Node,
            next: Row array,
            leading: int,
            trailing: int
        ) =
        // Removed rows stay in `previous`: they are absent from `next`, so they fall inside the changed window.
        let previous = laidOut

        let mutable aStart = leading
        let mutable bStart = leading
        let mutable aEnd = previous.Count - trailing
        let mutable bEnd = next.Length - trailing

        while aStart < aEnd
              && bStart < bEnd
              && same previous[aStart] next[bStart] do
            aStart <- aStart + 1
            bStart <- bStart + 1

        while aEnd > aStart
              && bEnd > bStart
              && same previous[aEnd - 1] next[bEnd - 1] do
            aEnd <- aEnd - 1
            bEnd <- bEnd - 1

        if bStart < bEnd && live () then
            let anchor = if bEnd < next.Length then next[bEnd].First else regionEnd

            if aStart = aEnd then
                let staged =
#if FABLE_COMPILER
                    staging.firstChild
                    |> optionSame next[bStart].First
                    && staging.lastChild
                       |> optionSame next[bEnd - 1].Last
#else
                    staging.firstChild
                    |> Option.exists (same next[bStart].First)
                    && staging.lastChild
                       |> Option.exists (same next[bEnd - 1].Last)
#endif

                if staged then
                    parent.insertBefore (staging, anchor) |> ignore
                else
                    let mutable index = bStart

                    while index < bEnd && live () do
                        Row.Place (next[index], parent, anchor)
                        index <- index + 1
            else
                let count = bEnd - bStart

                let mutable swapped =
                    count >= 2
                    && aEnd - aStart = count
                    && same previous[aStart] next[bEnd - 1]
                    && same previous[aEnd - 1] next[bStart]

                let mutable offset = 1

                while swapped && offset < count - 1 do
                    swapped <- same previous[aStart + offset] next[bStart + offset]
                    offset <- offset + 1

                if swapped then
                    Row.Place (next[bStart], parent, previous[aStart].First)

                    if count > 2 && live () then
                        Row.Place (next[bEnd - 1], parent, anchor)
                else
                    let sources = Array.create count -1
                    let positions = rowMap ()

                    for index in aStart .. aEnd - 1 do
                        rowMapSet positions (at previous index) index

                    for offset in 0 .. count - 1 do
                        setItem sources offset (rowMapGet positions (item next (bStart + offset)))

                    let keep = increasingRun sources
                    let mutable before = anchor
                    let mutable offset = count - 1

                    while offset >= 0 && live () do
                        let row = next[bStart + offset]

                        if not keep[offset] then
                            Row.Place (row, parent, before)

                        before <- row.First
                        offset <- offset - 1

        // Rows before `bStart` are in place; rows after `bEnd` keep their positions when the row count is unchanged, and
        // are the rows after `aEnd` otherwise.
        if
            previous.Count <> next.Length
            && bEnd - bStart <= smallWindow
        then
            spliceList laidOut aStart (aEnd - aStart) (sliceRows next bStart bEnd)
        else
            let last =
                if previous.Count = next.Length then
                    bEnd - 1
                else
                    next.Length - 1

            for index in bStart..last do
                let row = item next index

                if index < laidOut.Count then
                    setAt laidOut index row
                else
                    laidOut.Add row

            truncate laidOut next.Length

/// <summary>
/// Materialises the template rows and arms of one region into the region's staging fragment and serves their setup.
/// While <c>hydration</c> returns a cursor, a row adopts the server nodes at the cursor instead, and they stay in place.
/// </summary>
and private RowBuilder(state: MountState, hydration: unit -> TemplateDom.HydrationCursor) =
    let roots = ResizeArray<Node>()
    let mutable staging: DocumentFragment = Unchecked.defaultof<_>
    let mutable settingUp = false
    let mutable setupFailure: exn option = None
    // The passes of the region whose row is being built; null outside Build.
    let mutable placement: PlacementPass = null
    // The cursor of the row being built; null outside Build and for a row created fresh.
    let mutable cursor: TemplateDom.HydrationCursor = null

    // Removes the `<!--!-->` separator at the cursor, recording its reinsertion.
    let removeSeparator () =
        let separator = cursor.Next

        if isSeparator separator then
            let next = nextOrNull separator
            let parent = parentOrNull separator
            remove (separator :?> ChildNode)
            cursor.Next <- next
            cursor.Undo (fun () -> parent.insertBefore (separator, next) |> ignore)

    // Adopts the `<!--ranvier:row-->` comment of a row with no roots.
    let adoptRowMarker () =
        let marker = cursor.Next

        if not (isRowMarker marker) then
            raise (TemplateDom.mismatch "#0" "<!--ranvier:row-->" (TemplateDom.describe marker))

        cursor.Next <- nextOrNull marker
        roots.Add marker

    let setup: Setup =
        {
            OnCleanup = fun callback -> state.OwnedCleanup (Api.getOwner (), callback)
            CreateId = state.Identity.CreateId
        }

    member _.Staging =
        if isNull (box staging) then
            staging <- fragment ()

        staging

    /// <summary>
    /// Materialises the row created at index <c>row</c> of its region, its hooks held by <c>target</c>. While hydrating, a
    /// row after the region's first removes the <c>&lt;!--!--&gt;</c> separator at the cursor before it adopts.
    /// </summary>
    /// <exception cref="T:Ranvier.View.Dom.TemplateDom.HydrationMismatchException">
    /// While hydrating, the server nodes at the cursor differ from the row's roots.
    /// </exception>
    member this.Build
        (materialise: Func<(unit -> obj), TemplateDom.MountServices, int, unit>, current: unit -> obj, target: PlacementPass, row: int)
        : Row =
        placement <- target
        cursor <- hydration ()
        let adopted = not (isNull cursor)
        setupFailure <- None
        settingUp <- true

        try
            try
                if not (isNull cursor) && row > 0 then
                    removeSeparator ()

                materialise.Invoke (current, this :> TemplateDom.MountServices, row)

                if roots.Count = 0 && not (isNull cursor) then
                    adoptRowMarker ()
            with _ ->
                if not adopted then
                    for index in 0 .. roots.Count - 1 do
                        remove (roots[index] :?> ChildNode)

                roots.Clear ()
                reraise ()
        finally
            settingUp <- false
            placement <- null
            cursor <- null

        if roots.Count = 0 then
            let marker = comment "ranvier:row"
            this.Staging.appendChild marker |> ignore
            roots.Add marker

        let built = Row (roots[0], roots[roots.Count - 1], Adopted = adopted)
        // Cleared once the row is built, so a removed row's nodes become collectable.
        roots.Clear ()
        built

    interface TemplateDom.MountServices with
        member this.Append(node) =
            if isNull cursor then
                this.Staging.appendChild node |> ignore

            roots.Add node

        member _.Setup = setup

        member _.SettingUp() = settingUp

        member _.SetupFailed(error) =
            setupFailure <- Some error

        member _.RaiseSetupFailure() =
            setupFailure |> Option.iter raise

        member _.IsDelegated(eventName) =
            isDelegated delegatedEvents eventName

        member _.Delegate(eventName) =
            state.DelegateLate eventName

        member _.ListenDirect(node, eventName, invoke) =
            state.ListenDirect (node, eventName, invoke)

        member _.ReportFailure(error) =
            state.Scope.Fail [ error ]

        member _.ClearFailure() =
            state.ClearReported ()

        member _.Scope = state.Scope

        member _.Placement = placement.Container

        member _.Head(entry) =
            state.HeadIn (placement.Container, entry)

        member _.Identity = state.Identity

        // A row's first commit can run after its build; until the mount's hydration ends, the commit writes only a value
        // that differs from the node's current value, and records the write with the hydration cursor.
        member _.Hydration = if settingUp then cursor else state.HydrationCursor

        member _.Region(start, finish, _, kind, items, render, rows, parentPath, region) =
            state.NestedRegion (start, finish, kind, items, render, rows, parentPath, region, placement.Container, cursor)

/// <summary>
/// The markers and laid-out rows of one region, with the region's owner, row builder and failure state.
/// </summary>
and private RegionSite(state: MountState, regionStart: Node, regionEnd: Node, report: exn list -> unit, releaseNodes: Row -> unit) =
    let laidOut = ResizeArray<Row>()
    let errors = RegionErrors report
    // Template rows released while `Deferring` is set, with their nodes still in place.
    let pending = ResizeArray<Row>()
    let mutable owner: Owner = Unchecked.defaultof<_>
    let mutable builder: RowBuilder = Unchecked.defaultof<_>
    let mutable created = 0
    // The binding state word of the region's source read; `BindingState.Waiting` while a pending read is registered.
    let mutable sourceState = 0
    // Whether server nodes the region did not adopt remain between the markers.
    let mutable stale = false
    // Whether the source read of the latest pass raised a pending read.
    let mutable sourcePending = false

    member _.LaidOut = laidOut
    member _.Errors = errors

#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
    /// <summary>Runs <c>action</c> as a pass of the region's mount, counted by its <c>PassDepth</c>.</summary>
    member internal _.InPass(action: unit -> 'T) : 'T =
        state.PassDepth <- state.PassDepth + 1

        try
            action ()
        finally
            state.PassDepth <- state.PassDepth - 1
#endif

    /// <summary>The creation index of the region's next row, counting from 0 for the region's first row.</summary>
    member _.NextRow() : int =
        let row = created
        created <- row + 1
        row

    /// <summary>The region's owner, set by <c>Own</c>.</summary>
    member _.Owner = owner

    /// <summary>Whether the region's first pass adopts the server rows at the mount's hydration cursor.</summary>
    member val Hydrating = false with get, set


    /// <summary>
    /// Re-renders the region after a structural mismatch of its hydrating pass and reports the errors it receives, the
    /// mismatch first; null when a mismatch fails the hydration, and once <c>Settle</c> ends the hydrating pass.
    /// </summary>
    member val Recover: exn list -> unit = Unchecked.defaultof<_> with get, set

    /// <summary>Whether the region's next pass is the fresh mount of a <c>Recover</c>; its failure fails the hydration.</summary>
    member val Recovering = false with get, set

    /// <summary>The region's row builder; it adopts at the mount's hydration cursor while <c>Hydrating</c> is set.</summary>
    member this.Builder =
        if isNull (box builder) then
            builder <- RowBuilder (state, (fun () -> if this.Hydrating then state.HydrationCursor else null))

        builder

    /// <summary>
    /// Disposes the region's owner and clears its laid-out and held rows; the region's errors return to the first-pass
    /// state. The row counter keeps counting.
    /// </summary>
    member _.Reset() =
        if not (isNull (box owner)) then
            Api.batch (fun () -> owner.Dispose ())

        laidOut.Clear ()
        pending.Clear ()
        errors.Initializing <- true
        errors.SetupFailure <- None

    /// <summary>Whether the mount and the region's owner are both live.</summary>
    member _.Live() =
        not state.Root.IsDisposed && not owner.IsDisposed

    /// <summary>The markers' shared parent. Raises when the markers have no parent or different parents.</summary>
    member _.Parent() : Node =
        let regionParent =
            regionStart.parentNode
            |> Option.defaultWith (fun () -> invalid "Keyed-list boundaries have no parent")

        if
#if FABLE_COMPILER
            not (regionEnd.parentNode |> optionSame regionParent)
#else
            not (
                regionEnd.parentNode
                |> Option.exists (same regionParent)
            )
#endif
        then
            invalid "Keyed-list boundaries were externally mutated"

        regionParent

    member this.Validate() =
        Row.Validate (regionStart, regionEnd, laidOut, this.Parent ())

    /// <summary>Whether <c>ReleaseRow</c> holds released template rows for <c>FlushReleased</c> while the mount is live.</summary>
    member val Deferring = false with get, set

    /// <summary>
    /// Marks an authored row removed, and a template row removed while the region's owner is disposed in a live mount:
    /// its nodes leave with the enclosing row. Holds a template row for <c>FlushReleased</c> while <c>Deferring</c> is
    /// set and the mount is live, and otherwise releases its nodes through the mount.
    /// </summary>
    member this.ReleaseRow(row: Row) =
        if not (isNull row) then
            if
                not (isNull (box row.Mount))
                || not (isNull (box owner))
                   && owner.IsDisposed
                   && not state.Root.IsDisposed
            then
                row.Removed <- true
            elif this.Deferring && not state.Root.IsDisposed then
                pending.Add row
            else
                releaseNodes row

    /// <summary>Whether the held rows are exactly the region's live laid-out rows.</summary>
    /// <remarks>
    /// Guards an invariant: a successful pass that empties the list holds every live laid-out row. Rows held in
    /// laid-out order compare in one walk; any other order compares through a set.
    /// </remarks>
    member _.ReleasedAll() =
        let mutable live = 0
        let mutable ordered = true

        for index in 0 .. laidOut.Count - 1 do
            let row = at laidOut index

            if not row.Removed then
                if
                    ordered
                    && (live >= pending.Count
                        || not (same row (at pending live)))
                then
                    ordered <- false

                live <- live + 1

        if live <> pending.Count then
            false
        elif ordered then
            true
        else
            let rows = rowSet ()

            for index in 0 .. laidOut.Count - 1 do
                let row = at laidOut index

                if not row.Removed then
                    rowSetAdd rows row

            let mutable all = true
            let mutable index = 0

            while all && index < pending.Count do
                all <- rowSetHas rows (at pending index)
                index <- index + 1

            all

    /// <summary>
    /// Releases the held rows. When <c>cleared</c> and the markers share a parent, removes the region's contents in one
    /// operation and marks the rows removed; otherwise releases each row's nodes through the mount, in release order.
    /// </summary>
    member _.FlushReleased(cleared: bool) =
        if pending.Count > 0 then
            let rows = pending.ToArray ()
            pending.Clear ()

            let parent =
                match regionStart.parentNode with
                | Some parent when isChildOf regionEnd parent -> parent
                | _ -> Unchecked.defaultof<_>

            if
                cleared
                && not (isNull (box parent))
                && not (
                    Departures.seam.Enabled
                    && Departures.seam.Holds.Invoke (regionStart, regionEnd)
                )
            then
                Row.ClearBetween (parent, regionStart, regionEnd)

                for index in 0 .. rows.Length - 1 do
                    rows[index].Removed <- true
            else
                let errors = ResizeArray<exn>()

                for index in 0 .. rows.Length - 1 do
                    try
                        releaseNodes rows[index]
                    with error ->
                        errors.Add error

                if errors.Count = 1 then
                    raise errors[0]
                elif errors.Count > 1 then
                    raise (AggregateException ("Row release failed", errors))


    /// <summary>
    /// Runs <c>body</c> in a new root owner of the region, then raises the setup failure recorded during that first pass.
    /// </summary>
    member this.Own(body: unit -> unit) =
        Api.createRoot (fun created ->
            owner <- created

            if state.Scope.Tracks then
                Api.onCleanup (fun () -> this.ReleaseSource ())

            body ())

        errors.Initializing <- false
        errors.SetupFailure |> Option.iter raise

    member private _.ReleaseSource() =
        let previous = sourceState
        sourceState <- BindingState.released previous
        BindingState.ready state.Scope previous

    /// <summary>The value of <c>read</c>, the region's source read for a pass; a pending read marks the pass for <c>Settle</c> and is raised.</summary>
    member _.ReadSource(read: unit -> 'value) : 'value =
        sourcePending <- false

        try
            read ()
        with NotReadyException _ ->
            sourcePending <- true
            reraise ()

    /// <summary>Whether the source read of the latest pass, through <c>ReadSource</c>, was pending.</summary>
    member _.SourcePending = sourcePending

    /// <summary>True when the pass's source read was pending; registers the read with the mount's scope.</summary>
    member private _.Pends(failure: exn option, hydrating: bool) =
        if sourcePending then
            sourceState <- BindingState.rejected state.Scope sourceState failure.Value
            stale <- stale || hydrating
            true
        else
            false

    /// <summary>
    /// Removes the server nodes left between the markers by a hydrating pass whose source read was pending. Call after
    /// the source read of a pass that lays out rows.
    /// </summary>
    member this.Resume() =
        if stale then
            stale <- false
            Row.ClearBetween (this.Parent (), regionStart, regionEnd)

    /// <summary>
    /// Ends a pass. A region disposed during the pass reports <c>recorded</c>. A pass whose source read is pending keeps
    /// the region's rows and waits with the mount's boundary until a later pass ends; a hydrating pass keeps the server
    /// nodes until <c>Resume</c>. Otherwise a failure is recorded or reported with <c>recorded</c>, and a success runs
    /// <c>settle recorded</c>.
    /// </summary>
    /// <remarks>
    /// A failed <c>hydrating</c> pass passes a structural mismatch to <c>Recover</c> when it is set, with the failure's
    /// other errors and <c>recorded</c>; a failure of <c>Recover</c> fails the mount's hydration. Any other failure of a
    /// hydrating pass fails the mount's hydration. A failure of the pass that follows <c>Recovering</c> being set also
    /// fails the mount's hydration. While <c>Errors.Initializing</c> is set, either failure is also recorded as the
    /// region's setup failure.
    /// </remarks>
    member this.Settle(recorded: exn list, failure: exn option, hydrating: bool, settle: exn list -> unit) =
        let recovering = this.Recovering
        this.Recovering <- false
        let recover = this.Recover

        if hydrating then
            this.Recover <- Unchecked.defaultof<_>

        if not (this.Live ()) then
            errors.Report recorded
        elif this.Pends (failure, hydrating) then
            errors.Report recorded
        else
            this.ReleaseSource ()

            match failure with
            | Some error when recovering ->
                state.HydrationFailed error

                if errors.Initializing then
                    errors.Fail (error, error :: recorded)
            | Some error when hydrating ->
                match TemplateDom.findMismatch error with
                | Some found when not (isNull (box recover)) ->
                    try
                        recover (
                            (found :> exn)
                            :: TemplateDom.failuresBesides found error
                            @ recorded
                        )
                    with recoveryError ->
                        state.HydrationFailed recoveryError
                | _ ->
                    state.HydrationFailed error

                    if errors.Initializing then
                        errors.Fail (error, error :: recorded)
            | Some error -> errors.Fail (error, error :: recorded)
            | None -> settle recorded

/// <summary>
/// A boundary between region markers: its content, mounted detached until every binding under it has read a value;
/// its pending view, shown between the markers meanwhile; and its error view, which replaces both after a failure under
/// the content.
/// </summary>
/// <remarks>
/// The pending and error views are mounted under <c>outer</c>, the scope enclosing the boundary's region, or null at the
/// top level. Their failures go to <c>enclosing</c>; while <c>unwinds</c> is true they raise instead. A failure during
/// the content's setup or reveal replaces the content at once; after them, the reveal and the teardown run in the
/// boundary's effect, after the binding, handler or pass that requested them. Each exception object is reported once
/// until a retry.
/// <para>
/// With <c>hydration</c>, the boundary starts from the server nodes between the markers and the view the start marker
/// records. Under <c>[c</c>, the content hydrates in place; when one of its reads is still pending at the end of the
/// hydrating mount's build, its bound nodes are held detached and a copy of the server nodes, taken before its first
/// commits, stays between the markers as the inert pending view until the reveal. Under <c>[p</c>, the content mounts
/// detached; while it is pending, the pending view hydrates in place. Under <c>[e</c>, the server nodes are removed and
/// the boundary mounts as in a mount. A structural mismatch of a hydrating view is reported once through
/// <c>report</c>, the server nodes are removed and the view mounts fresh; with <c>hydration.Strict</c>, it goes to
/// <c>failHydration</c> instead. <c>hydrationCursor</c> returns the mount's cursor, which records each removal of server
/// nodes while the mount hydrates. <c>atBuildEnd</c> runs an action at the end of the hydrating mount's build, while
/// that cursor still records.
/// </para>
/// </remarks>
and [<Sealed>] private ViewBoundary
    (
        graph: Graph,
        report: exn list -> unit,
        site: RegionSite,
        regionStart: Node,
        regionEnd: Node,
        views: BoundaryViews,
        enclosing: ViewScope,
        outer: ViewScope,
        unwinds: unit -> bool,
        parent: Placement,
        parentPath: InstancePath,
        region: int,
        mountView:
            (unit -> obj)
                -> ((unit -> obj) -> Backend -> unit)
                -> ViewScope
                -> Placement
                -> InstancePath
                -> int
                -> int
                -> TemplateDom.HydrationCursor
                -> Row,
        hydration: TemplateDom.HydrationCursor,
        hydrationCursor: unit -> TemplateDom.HydrationCursor,
        failHydration: exn -> unit,
        atBuildEnd: (unit -> unit) -> unit
    ) =
    // Whether the boundary starts from the server nodes between the markers.
    let hydrates = not (isNull hydration)
    let strict = hydrates && hydration.Strict
    // The content's nested mount; null until its setup returns.
    let mutable content: Row = null
    // The container of the content's hooks, placed at the reveal; null after a teardown.
    let mutable contentContainer: Placement = null
    // The view between the markers while the content is hidden; null otherwise.
    let mutable shown: Row = null
    let mutable waiting = 0
    let mutable settingUp = false
    let mutable revealed = false
    let mutable failed = false
    // Advances on each failure; a retry of an earlier failure is ignored.
    let mutable generation = 0
#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
    // The first error of the latest failure; null until one.
    let mutable failure: exn = null
#endif
    // Failures received while the content sets up.
    let held = ResizeArray<exn>()
    // Failures received after setup, waiting for the boundary's effect.
    let mutable received: exn list = []
    // The exception objects reported since the latest retry.
    let reported = System.Collections.Generic.HashSet<exn>(HashIdentity.Reference)
    let mutable wake: Signal<int> = Unchecked.defaultof<_>
    let mutable pending: Signal<bool> = Unchecked.defaultof<_>
    let mutable status: BoundaryStatus = Unchecked.defaultof<_>
    // Whether unowned server nodes lie between the markers.
    let mutable served = false
    // A copy of the server nodes between the markers, taken before a `[c` content's first commits; null otherwise.
    let mutable copy: DocumentFragment = Unchecked.defaultof<_>

    let noItem () : obj = null

    // A copy of the server nodes between the markers.
    let copyServed () =
        let copied = fragment ()
        let mutable node = nextOrNull regionStart

        while not (same node regionEnd) do
            copied.appendChild (node.cloneNode true) |> ignore
            node <- nextOrNull node

        copied

    // Removes the server nodes between the markers, recording their reinsertion while the mount hydrates.
    let clearServed () =
        if served then
            served <- false
            let cursor = hydrationCursor ()

            if isNull cursor then
                Row.ClearBetween (site.Parent (), regionStart, regionEnd)
            else
                clearAdopted cursor regionStart regionEnd

    // Tears down a view's mount. A view that adopted server nodes while the mount hydrates leaves them in place, unowned.
    let abandon (row: Row) =
        if row.Adopted && not (isNull (hydrationCursor ())) then
            row.Mount.AbandonAdopted ()
            served <- true
        else
            row.Mount.Abandon ()

    // Raises a mismatch unless the cursor, after the adopted view `row`, is at the end marker.
    let checkEnd (row: Row) (cursor: TemplateDom.HydrationCursor) =
        if not (same cursor.Next regionEnd) then
            raise (
                TemplateDom.mismatch
                    ("#"
                     + string (positionAfter row.First cursor.Next - 1))
                    "<!--]-->"
                    (TemplateDom.describe cursor.Next)
            )

    // Reports a structural mismatch of a hydrating view once, first among the failure's errors.
    let reportMismatch (found: TemplateDom.HydrationMismatchException) (error: exn) =
        Api.untrack (fun () ->
            report (
                (found :> exn)
                :: TemplateDom.failuresBesides found error
            ))

    // Reports the exception objects of `errors` not reported before, in order, in one untracked call.
    let reportAll (errors: exn list) =
        let fresh = errors |> List.filter reported.Add

        if not fresh.IsEmpty then
            try
                Api.untrack (fun () -> report fresh)
            with reporterError ->
                raise (AggregateException ("Boundary failure and reporter failed", List.toArray (fresh @ [ reporterError ])))

    // Moves the row's nodes before the end marker, in one insertion when they fill their fragment. An adopted row is in
    // place already; any other row first replaces the server nodes.
    let place (row: Row) =
        if row.Adopted then
            served <- false
            copy <- Unchecked.defaultof<_>
        else
            clearServed ()
            let parent = site.Parent ()

            if fillsFragment row.First row.Last then
                parent.insertBefore (parentOrNull row.First, regionEnd)
                |> ignore
            else
                Row.MoveBefore (row, parent, regionEnd)

        row.Mount.Relocate ()
        site.LaidOut.Add row

    let retire () =
        if not (isNull shown) then
            let row = shown
            shown <- null
            site.LaidOut.Clear ()

            if row.Adopted && not (isNull (hydrationCursor ())) then
                row.Mount.AbandonAdopted ()
                served <- true
            else
                row.Mount.Dispose ()

    /// <summary>
    /// Mounts the pending or error view and places it between the markers. With <c>cursor</c>, the view adopts the
    /// server nodes from <c>cursor.Next</c> to the end marker.
    /// </summary>
    member private this.Show(render: Backend -> unit, cursor: TemplateDom.HydrationCursor) =
        let mutable row: Row = null
        let viewContainer = Placement parent

        try
            row <-
                Api.runWithOwner site.Owner (fun () ->
                    mountView noItem (fun _ backend -> render backend) outer viewContainer parentPath region (site.NextRow ()) cursor)

            if not (isNull cursor) then
                checkEnd row cursor

            place row
            shown <- row
            viewContainer.Place ()
        with error ->
            viewContainer.Discard ()

            if not (isNull row) then
                abandon row

            match
                (if isNull cursor then
                     None
                 else
                     TemplateDom.findMismatch error)
            with
            | Some found when not cursor.Strict ->
                reportMismatch found error
                clearServed ()
                this.Show (render, null)
            | Some _ -> failHydration error
            | None ->
                clearServed ()

                if unwinds () then
                    reraise ()

                enclosing.Fail [ error ]

    /// <summary>
    /// Holds the pending adopted content's nodes detached and puts the copy of the server nodes between the markers as
    /// the inert pending view; the reveal replaces the copy with the content's nodes. A Strict failure puts the
    /// content's nodes back in place of the copy.
    /// </summary>
    member private _.Detach() =
        let detached = fragment ()
        Row.MoveBefore (content, detached, Unchecked.defaultof<_>)
        let shownCopy = copy
        copy <- Unchecked.defaultof<_>

        let copied =
            [|
                for index in 0 .. int shownCopy.childNodes.length - 1 -> unbox<Node>(shownCopy.childNodes.item (float index))
            |]

        let parent = site.Parent ()

        parent.insertBefore (shownCopy, regionEnd)
        |> ignore

        content.Adopted <- false
        served <- true
        let cursor = hydrationCursor ()
        let bound = content

        if not (isNull cursor) then
            cursor.Undo (fun () ->
                for node in copied do
                    if not (isNull (box (parentOrNull node))) then
                        remove (node :?> ChildNode)

                Row.MoveBefore (bound, parentOrNull regionEnd, regionEnd))

    /// <summary>Detaches the adopted content behind its copy when the content is still pending and unrevealed.</summary>
    member private this.DetachPending() =
        if
            not revealed
            && not failed
            && waiting > 0
            && not (isNull content)
            && content.Adopted
        then
            this.Detach ()

    member private this.Reveal() =
        revealed <- true

        try
            retire ()
            place content
            contentContainer.Place ()
        with error ->
            this.TearDown [ error ]

    /// <summary>
    /// Disposes the content and the pending view in one teardown window, mounts the error view, and reports
    /// <c>errors</c> followed by the teardown's errors.
    /// </summary>
    member private this.TearDown(errors: exn list) =
        failed <- true
#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
        failure <- List.tryHead errors |> Option.toObj
#endif
        copy <- Unchecked.defaultof<_>
        generation <- generation + 1
        let current = generation

        let teardown, raised =
            MountLifetime.Capture (
                graph,
                report,
                fun () ->
                    if not (isNull content) then
                        abandon content

                    if not (isNull shown) then
                        abandon shown
            )

        content <- null
        shown <- null

        if not (isNull contentContainer) then
            contentContainer.Discard ()
            contentContainer <- null

        site.LaidOut.Clear ()
        waiting <- 0
        revealed <- false
        pending.Value <- false

        let failure =
            {
                Error = List.head errors
                Retry = fun () -> this.Retry current
            }

        let mutable unwound = None

        try
            this.Show (views.Failed failure, null)
        with error ->
            unwound <- Some error

        reportAll (errors @ teardown @ Option.toList raised)
        unwound |> Option.iter raise

    member private this.Retry(expected: int) =
        if failed && generation = expected && site.Live () then
            graph.Run (fun () ->
                retire ()
                failed <- false
                reported.Clear ()
                this.MountContent ())

    member private this.Settle() =
        if not received.IsEmpty then
            let errors = received
            received <- []
            this.TearDown errors
        elif
            not revealed
            && not failed
            && not settingUp
            && waiting = 0
            && not (isNull content)
            && site.Live ()
        then
            this.Reveal ()

    /// <summary>
    /// Mounts the content as row <c>row</c> of the region, adopting the server nodes at <c>cursor</c> when it is set,
    /// and returns the mount's failure.
    /// </summary>
    member private this.SetUpContent(row: int, cursor: TemplateDom.HydrationCursor) : exn option =
        settingUp <- true
        waiting <- 0
        held.Clear ()
        contentContainer <- Placement parent
        let mutable failure = None

        try
            try
                content <-
                    Api.runWithOwner site.Owner (fun () ->
                        mountView
                            noItem
                            (fun _ backend -> views.Content status backend)
                            (this :> ViewScope)
                            contentContainer
                            parentPath
                            region
                            row
                            cursor)
            with error ->
                failure <- Some error
        finally
            settingUp <- false

        failure

    /// <summary>
    /// Ends the content's setup: a failure tears the content down, ready content is revealed, and pending content shows
    /// the pending view. Pending content adopted in place stays in place until the end of the hydrating mount's build,
    /// and is then detached behind its copy of the server nodes if it is still pending; under server nodes left between
    /// the markers, the pending view hydrates.
    /// </summary>
    member private this.SetUpEnded(failure: exn option) =
        match failure with
        | Some error -> this.TearDown (error :: List.ofSeq held)
        | None when held.Count > 0 -> this.TearDown (List.ofSeq held)
        | None when waiting = 0 -> this.Reveal ()
        | None when content.Adopted -> atBuildEnd (fun () -> this.DetachPending ())
        | None when served -> this.Show (views.Pending, TemplateDom.HydrationCursor (nextOrNull regionStart, strict))
        | None -> this.Show (views.Pending, null)

    member private this.MountContent() =
        this.SetUpEnded (this.SetUpContent (site.NextRow (), null))

    /// <summary>Hydrates the content of a <c>[c</c> boundary in place.</summary>
    member private this.HydrateContent() =
        copy <- copyServed ()
        let attempt = TemplateDom.HydrationCursor (nextOrNull regionStart, strict)

        let failure =
            match this.SetUpContent (site.NextRow (), attempt) with
            | None ->
                try
                    checkEnd content attempt
                    served <- false
                    None
                with error ->
                    Some error
            | failure -> failure

        match failure |> Option.bind TemplateDom.findMismatch with
        | Some found ->
            // A wake during the abandon leaves the boundary unrevealed.
            let attempted = content
            content <- null
            copy <- Unchecked.defaultof<_>

            if not (isNull attempted) then
                abandon attempted

            contentContainer.Discard ()
            contentContainer <- null

            if strict then
                failHydration failure.Value
            else
                reportMismatch found failure.Value
                clearServed ()
                this.MountContent ()
        | None -> this.SetUpEnded failure

    /// <summary>
    /// Creates the boundary's effect and mounts its content. Call once, inside the region's owner; disposing that owner
    /// discards the hooks held by an unrevealed content.
    /// </summary>
    member this.Start() =
#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
        Api.onCleanup (
            Inspect.register
                regionStart
                (Inspect.BoundaryProbe (
                    parentPath.Row (region, -1),
                    fun () ->
                        if failed then
                            Inspect.Failed (failure, generation)
                        elif waiting > 0 || not revealed then
                            Inspect.Pending (waiting, revealed)
                        else
                            Inspect.Ready
                ))
        )
#endif
        wake <- Api.createSignal 0
        pending <- Api.createSignal false
        status <- { IsPending = fun () -> pending.Value }

        Api.createEffect (fun () ->
            wake.Value |> ignore
#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
            Api.untrack (fun () -> site.InPass (fun () -> this.Settle ())))
#else
            Api.untrack (fun () -> this.Settle ()))
#endif

        Api.onCleanup (fun () ->
            if not (isNull contentContainer) then
                contentContainer.Discard ()
                contentContainer <- null)

        if not hydrates then
            this.MountContent ()
        else
            served <- true

            match commentData regionStart with
            | "[c" -> this.HydrateContent ()
            | "[p" -> this.MountContent ()
            | _ ->
                clearServed ()
                this.MountContent ()

    /// <summary>Takes failures of the content, every one of them a failure including a pending read.</summary>
    member this.FailAll(errors: exn list) =
        if not errors.IsEmpty then
            if settingUp then
                held.AddRange errors
            elif failed || not (site.Live ()) then
                graph.Run (fun () -> reportAll errors)
            else
                received <- received @ errors
#if RANVIER_VIEW_COUNTERS
                BoundaryCounters.wakes.Value <- BoundaryCounters.wakes.Value + 1
#endif
                graph.Run (fun () -> Api.untrack (fun () -> wake.Value <- wake.Value + 1))

    interface ViewScope with
        member _.Tracks = true

        member _.Wait() =
            waiting <- waiting + 1

            if revealed then
                pending.Value <- true

        member _.Expect() =
            if revealed || failed then
                false
            else
                waiting <- waiting + 1
                true

        member _.Ready() =
            if waiting > 0 then
                waiting <- waiting - 1

                if waiting = 0 then
                    if revealed then
                        pending.Value <- false
                    elif not settingUp then
#if RANVIER_VIEW_COUNTERS
                        BoundaryCounters.wakes.Value <- BoundaryCounters.wakes.Value + 1
#endif
                        Api.untrack (fun () -> wake.Value <- wake.Value + 1)

        member this.Fail(errors) =
            this.FailAll (
                errors
                |> List.filter (fun error ->
                    match error with
                    | NotReadyException _ -> false
                    | _ -> true)
            )

        member this.FailAll(errors) =
            this.FailAll errors

/// <summary>The state of a mounted view and the backend that builds it.</summary>
/// <remarks>
/// <c>boundary</c> is the boundary enclosing the mount, or null outside every boundary; <c>inherited</c> is null for a
/// root mount.
/// </remarks>
and internal MountState
    (
        scheduling: Scheduling,
        graph: Graph,
        parent: MountLifetime,
        parentOwner: Owner,
        report: exn list -> unit,
        host: Node,
        boundary: ViewScope,
        inherited: Placement,
        identity: IdentityScope,
        parentPath: InstancePath,
        kind: int,
        a: int,
        b: int,
        hydration: TemplateDom.HydrationCursor,
        rootStart: Node,
        rootEnd: Node
    ) =
    inherit Prepared.Backend()

    // With a cursor, the range's boundary entries are the server's root markers.
    let range =
        if isNull hydration then
            OwnedRange host
        else
            OwnedRange (host, rootStart, rootEnd)

    // The cursor of the server nodes the mount adopts; null outside hydration and once `Run` returns.
    let mutable cursor = hydration
    // Whether the mount adopts server nodes.
    let adopts = not (isNull hydration)
    // The cursor whose inverses a failed `Run` and `AbandonAdopted` run; null outside hydration and once a root mount's
    // `Run` succeeds.
    let mutable rollback = hydration
    // Whether the release leaves the range's nodes and adopted rows in place: set when a hydrating `Run` fails.
    let mutable keepNodes = false
    // The mount whose `MountRow` created this one; null for a root mount.
    let mutable enclosing: MountState = Unchecked.defaultof<_>
    // The first unrecovered failure of a region's hydrating pass; `Run` raises it once the build returns.
    let mutable hydrationFailure: exn = null
    // The actions a root mount runs once its build returns, in registration order; null when there are none.
    let mutable buildEnd: ResizeArray<unit -> unit> = null

    // The mount's path, one step of `kind` below `parentPath`; null until first requested.
    let mutable mountPath: InstancePath = null
    // Whether a template took the mount's own position; later templates take authored steps under the mount's path.
    let mutable positioned = false
    // The authored steps under the mount's path taken by templates and authored regions.
    let mutable authoredSteps = 0

    let pathOfMount () =
        if isNull mountPath then
            mountPath <- InstancePath.Below (parentPath, kind, a, b)

        mountPath

    let nextAuthored () =
        let step = authoredSteps
        authoredSteps <- step + 1
        step

    // The container of the mount's hooks: a root mount's own, or the container of the pass, arm, hole or boundary view
    // that created the mount.
    // A view hole expanded under queued commits holds the hole's container while its view renders.
    let mutable container = if isNull inherited then Placement null else inherited

    // Template rows retired while the mount tears down, before its release; their nodes leave in the release, before
    // the range's. A row retired after the release removes its nodes at once. A row held for a region's flush leaves
    // in that flush, which can follow the release.
    let mutable releasedRows: ResizeArray<Row> = null
    let mutable rowsReleased = false

    let mutable root: MountLifetime = null

    // The retired row roots held in place by their exits; null until the first is held.
    let mutable leaving: ResizeArray<unit -> unit> = null

    let ensureLeaving () =
        if isNull leaving then
            leaving <- ResizeArray ()

        leaving

    // Removes the nodes of a template row; unless `forced`, a root with an exit stays until its exit ends. A row whose
    // first node is detached left with an enclosing row's nodes and is only marked removed.
    let removeRow (row: Row) (forced: bool) =
        if isDetached row.First then
            row.Removed <- true
        else
            Row.Remove (
                row,
                if Departures.seam.Enabled && not forced then
                    ensureLeaving ()
                else
                    null
            )

    // Ends every held exit: cancels it, then removes its node.
    // Whether the release leaves the mount's nodes in place. A mount built with a cursor also keeps them while its
    // enclosing mount keeps its own.
    let keeps () =
        keepNodes
        || adopts
           && not (isNull (box enclosing))
           && enclosing.KeepsNodes

    let release () =
        rowsReleased <- true
        let errors = ResizeArray<exn>()
        let keep = keeps ()

        // A live release inside a live enclosing mount: a list row's or an arm's roots with exits stay in place, held by
        // the enclosing mount, and this mount's held exits move there, still running.
        let departing = Departures.seam.Enabled && not keep

        let live =
            departing
            && not (isNull (box enclosing))
            && not enclosing.Root.IsDisposed

        // Inside a root that is leaving, or whose exit follows, the nodes stay in place and leave with that root.
        let within =
            departing
            && Departures.seam.Within.Invoke range.First

        // Ends every held exit, or moves them, still running, into the enclosing mount on a live release.
        if not (isNull leaving) then
            Departures.seam.End.Invoke (leaving, errors, (if live then enclosing.Leaving else null))

        if not (isNull releasedRows) then
            let rows = releasedRows
            releasedRows <- null

            for index in 0 .. rows.Count - 1 do
                try
                    if not (within || keep && rows[index].Adopted) then
                        removeRow rows[index] true
                with error ->
                    errors.Add error

        try
            // A list row's or an arm's mount, which inherits a region pass's container, retires its roots on a live release.
            range.Release (keep || within, (if live && inherited.Row then enclosing.Leaving else null))
        with error ->
            errors.Add error

        if errors.Count = 1 then
            raise errors[0]
        elif errors.Count > 1 then
            raise (AggregateException ("Range release failed", errors))

    let ownedCleanup (owner: Owner) (callback: unit -> unit) =
        if owner.IsDisposed then
            invalid "Component cleanup registration requires a live component"

        owner.OnCleanup (Action callback)

    // Removes the nodes of a template row; while the mount tears down, before its release, queues them for the release.
    let releaseNodes (row: Row) =
        if root.IsDisposed && not rowsReleased then
            if isNull releasedRows then
                releasedRows <- ResizeArray ()

            releasedRows.Add row
        else
            removeRow row root.IsDisposed

    // The element receiving created nodes; null at the top level of the range.
    let mutable target: Node = Unchecked.defaultof<_>
    let mutable bindingSetupFailure: exn option = None
    let mutable modifierNode: HTMLElement = Unchecked.defaultof<_>
    let mutable modifierTag = Div

    let ensureSetup () =
#if LADDER_NAIVE_CHECKS
        ()
#else
        if root.Phase <> SettingUp then
            invalid "View construction is only valid during live setup"
#endif

    let append node =
        ensureSetup ()

#if LADDER_NAIVE_RANGE
        if isNull (box target) then
            range.AddNaive node
        else
            target.appendChild node |> ignore
#else
        if isNull (box target) then
            range.AddCreated node
        else
            target.appendChild node |> ignore
#endif

    let currentElement () =
#if !LADDER_NAIVE_CHECKS
        ensureSetup ()

        if isNull (box modifierNode) then
            invalid "Element modifiers require an active element"

#endif
        modifierNode

    let applyModifiers (backend: Backend) tag node (modifiers: ModifierAction array) =
        let previousNode = modifierNode
        let previousTag = modifierTag
        modifierNode <- node
        modifierTag <- tag

        try
            for index in 0 .. modifiers.Length - 1 do
                ensureSetup ()
                modifiers[index] backend
                ensureSetup ()
        finally
            modifierNode <- previousNode
            modifierTag <- previousTag

    let openElement (backend: Backend) tag modifiers =
        ensureSetup ()
        let create = Departures.seam.CreateElement
        let tagName = name tag
        refuseScript tagName

        let node =
            if isNullish create then
#if FABLE_COMPILER
                document.createElement tagName
#else
                Native.createElement tagName
#endif
            else
                create.Invoke ((if isNull (box target) then host else target), tagName)

        append node

        if tag <> Select then
            applyModifiers backend tag node modifiers

        node

    let childParent tag (node: HTMLElement) : Node =
        if name tag = "template" then
            (node :?> HTMLTemplateElement).content :> Node
        else
            node

    let mutable lastReported: exn = null

    // Reports the failures in one untracked call. A pending read is dropped, and each exception object is reported once
    // until `clearReported` runs.
    let reportFailures (errors: exn list) =
        let fresh = ResizeArray<exn>()

        for error in errors do
            match error with
            | NotReadyException _ -> ()
            | _ when obj.ReferenceEquals (error, lastReported) -> ()
            | _ ->
                lastReported <- error
                fresh.Add error

        if fresh.Count > 0 then
            let reported = List.ofSeq fresh

            try
                Api.untrack (fun () -> report reported)
            with reporterError ->
                raise (AggregateException ("Binding failure and reporter failed", List.toArray (reported @ [ reporterError ])))

    // Called when a failed reader recovers; the same exception object is reported again if it recurs.
    let clearReported () =
        lastReported <- null

    let startBinding (scope: ViewScope) (equals: Func<_, _, bool>) read write =
#if LADDER_NAIVE_BINDINGS
        Api.createEffect (fun () -> write (read ()))
#else
        let mutable state = BindingState.started scope

        if scope.Tracks then
            Api.onCleanup (fun () ->
                let previous = state
                state <- BindingState.released previous
                BindingState.ready scope previous)

        match scheduling with
        | Synchronous ->
            let mutable hasCommitted = false
            let mutable committed = Unchecked.defaultof<_>
            let mutable pending = Unchecked.defaultof<_>

            let commit () =
                let changed =
                    not hasCommitted
                    || not (equals.Invoke (committed, pending))

                if changed && not root.IsDisposed then
                    try
                        write pending
                        committed <- pending
                        hasCommitted <- true
                    with
                    | error when root.Phase = SettingUp ->
                        bindingSetupFailure <- Some error
                        reraise ()
                    | error -> scope.Fail [ error ]

            Api.createEffect (fun () ->
                let value =
                    try
                        read ()
                    with error ->
                        state <- BindingState.rejected scope state error
                        BindingState.report scope error
                        reraise ()

                if state <> 0 then
                    state <- BindingState.recovered clearReported state

                if not root.IsDisposed then
                    pending <- value
                    Api.untrack commit

                if state <> 0 then
                    let previous = state
                    state <- BindingState.released previous
                    BindingState.ready scope previous)
        | Queued queue ->
            let live () =
                not root.IsDisposed

            let guardedWrite value =
                try
                    write value
                with error when root.Phase = SettingUp ->
                    bindingSetupFailure <- Some error
                    reraise ()

            let reportingRead () =
                let value =
                    try
                        read ()
                    with error ->
                        state <- BindingState.rejected scope state error
                        BindingState.report scope error
                        reraise ()

                if state <> 0 then
                    state <- BindingState.recovered clearReported state

                value

            let afterCommit =
                if scope.Tracks then
                    fun () ->
                        if state <> 0 then
                            let previous = state
                            state <- BindingState.released previous
                            BindingState.ready scope previous
                else
                    Unchecked.defaultof<_>

            Binding.startQueuedIn
                scope
                (fun () -> root.Phase = SettingUp)
                root.OnCleanup
                live
                queue
                (fun left right -> equals.Invoke (left, right))
                reportingRead
                guardedWrite
                afterCommit

        bindingSetupFailure |> Option.iter raise
#endif

    // A set of event names; null until a delegated event is registered.
    let mutable delegatedNames: obj = null
    let mutable delegatedHost: Node = Unchecked.defaultof<_>

    let delegateAt (host: Node) =
        if not (isNull delegatedNames) then
            setIter delegatedNames (fun eventName -> delegateFrom.Invoke (host, eventName))

        delegatedHost <- host

    let releaseDelegation () =
        if
            not (isNull (box delegatedHost))
            && not (isNull delegatedNames)
        then
            let releasedHost = delegatedHost
            setIter delegatedNames (fun eventName -> undelegateFrom.Invoke (releasedHost, eventName))
            delegatedHost <- Unchecked.defaultof<_>

    let mutable preparedRoot: Node = Unchecked.defaultof<_>

    let ensurePrepared (node: Node) =
        ensureSetup ()

        if isNull (box preparedRoot) then
            invalid "Prepared node operations require active clone setup"

        let mutable belongs = contains preparedRoot node

        if not belongs then
            walkPrepared
                (fun current ->
                    if same current node then
                        belongs <- true)
                preparedRoot

        if not belongs then
            invalid "Prepared node operations require a node from the current clone"

    let registerDelegated (eventName: string) =
        if isNull delegatedNames then
            delegatedNames <- nameSet ()

        setAdd delegatedNames eventName

    let mutable delegationReleased = false

    // A row registers delegated events after its mount attached: the name joins the mount's host at once.
    let delegateLate (eventName: string) =
        if
            isNull delegatedNames
            || not (setHas delegatedNames eventName)
        then
            registerDelegated eventName

            if not (isNull (box delegatedHost)) then
                delegateFrom.Invoke (delegatedHost, eventName)

                if not delegationReleased then
                    delegationReleased <- true
                    root.OnCleanup releaseDelegation

    let listenDirect (node: Node) eventName (invoke: obj -> unit) =
        let cleanup = DomEvents.listenDirect (box node) eventName invoke

        if root.IsDisposed then
            cleanup ()
            ensureSetup ()

        ownedCleanup (Api.getOwner ()) cleanup

    let mutable templateSetup: Setup = Unchecked.defaultof<_>

    member _.Root: MountLifetime = root
#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
    /// <summary>The first and last node of the mount's range.</summary>
    member internal _.RangeEnds = struct (range.First, range.Last)

    member internal _.Graph = graph
#endif

    member internal _.Identity: IdentityScope = identity

    member internal _.OwnedCleanup(owner: Owner, callback: unit -> unit) =
        ownedCleanup owner callback

    member internal _.DelegateLate(eventName: string) =
        delegateLate eventName

    member internal _.ListenDirect(node: Node, eventName: string, invoke: obj -> unit) =
        listenDirect node eventName invoke

    /// <summary>The receiver of the pending reads and failures of the bindings this mount creates: its boundary, or the mount itself.</summary>
    member internal this.Scope: ViewScope =
        if isNull (box boundary) then
            this :> ViewScope
        else
            boundary

    member internal _.ClearReported() =
        clearReported ()

    member _.Native(node: Node, ownership: NativeOwnership) : unit =
        ensureSetup ()

        if not (isNull (box target)) then
            invalid "Native roots require top-level placement"

        range.Add (node :?> ChildNode, ownership)

    member _.Move(parent: Node, before: Node option) : unit =
        range.Move (parent, before, root.Dispose)

        if not (same parent delegatedHost) then
            releaseDelegation ()
            delegateAt parent

    member _.Validate() : unit =
        range.Validate ()

    member _.Relocate() : unit =
        range.Relocate ()

        if
            not (isNull delegatedNames)
            && not (same range.Placement delegatedHost)
        then
            releaseDelegation ()
            delegateAt range.Placement

    member _.Flush() : unit =
        match scheduling with
        | Synchronous -> graph.Run Api.flush
        | Queued queue -> queue.Flush ()

    member _.CommitErrors: exn array =
        match scheduling with
        | Synchronous -> [||]
        | Queued queue -> queue.Errors

    /// <summary>
    /// Builds the view, attaches it before <c>before</c> in the host, and returns its handle. With a cursor, the build
    /// adopts the server nodes between the root markers, which stay in place.
    /// </summary>
    /// <remarks>
    /// A build or attachment failure disposes the mount and rethrows. With a cursor, the disposal leaves the adopted nodes
    /// in place and every edit recorded with the cursor is undone.
    /// </remarks>
    member this.Run(before: Node option, build: BuildContext -> unit) =
        let context = BuildContext this

        try
            graph.Run (fun () ->
                let create () =
                    Api.createRoot (fun owner ->
                        root <- MountLifetime.Create (graph, owner, report, release, parent)
                        // A row or view shares its container with its siblings; its hooks form its own group.
                        let previous = container.Enter ()

                        try
                            Api.untrack (fun () -> build context)
                        finally
                            container.Leave previous)

                if isNull (box parentOwner) then
                    create ()
                else
                    Api.runWithOwner parentOwner create)

            ensureSetup ()

            if not (isNull hydrationFailure) then
                raise hydrationFailure

            if not (isNull buildEnd) then
                let actions = buildEnd
                buildEnd <- null

                for action in actions do
                    action ()

            if isNull cursor then
                range.Attach (host, before)
            else
                range.Adopt host
                cursor <- null

            root.MarkAttached ()
#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
            let queued, flushing =
                match scheduling with
                | Queued queue -> true, (fun () -> this.PassDepth > 0 || queue.Flushing)
                | Synchronous -> false, (fun () -> this.PassDepth > 0)

            root.OnCleanup (Inspect.register range.First (Inspect.MountProbe ((fun () -> root.Phase), queued, flushing, identity.Prefix, graph)))
#endif
            delegateAt host

            if not (isNull delegatedNames) then
                delegationReleased <- true
                root.OnCleanup releaseDelegation

            if isNull inherited then
                container.Placing <- true

                try
                    graph.Run (fun () -> container.Place ())
                finally
                    container.Placing <- false

                container.RaiseFailure ()

            if adopts && isNull (box enclosing) then
                rollback.Commit ()
                rollback <- null

            Mounted this
        with original ->
            if not adopts then
                if not (isNull root) then
                    root.Abandon ()

                reraise ()
            else
                cursor <- null
                keepNodes <- true

                try
                    try
                        if not (isNull root) then
                            root.Abandon ()
                    finally
                        rollback.Restore ()
                with cleanup ->
                    raise (AggregateException ("Hydration and its rollback failed", [| original; cleanup |]))

                reraise ()

    override _.Clone(template, setup) =
        ensureSetup ()
        let node = template.cloneNode true :?> HTMLElement
        append node
        let previous = preparedRoot
        preparedRoot <- node

        try
            setup node
            ensureSetup ()
        finally
            preparedRoot <- previous

    override this.Apply(node, modifiers) =
        ensurePrepared node

        let tag =
            if node.tagName = "INPUT" then
                Input
            else
                HtmlTag node.localName

        applyModifiers this tag node modifiers

    override this.BindText(node, read) =
        ensurePrepared node

        if node.nodeType <> 3. then
            raise (ArgumentException ("Prepared text bindings require a text node", "node"))

        startBinding this.Scope (Func<_, _, _>(fun left right -> left = right)) read (fun value -> node.textContent <- Some value)
        ensureSetup ()

    override _.Text(value) =
        ensureSetup ()
        append (text value)

    override _.Attribute(key, value) =
        let node = currentElement ()
#if !LADDER_NAIVE_CHECKS && !LADDER_UNCHECKED_ATTRIBUTES
        validateAttribute key
#endif
        node.setAttribute (key, value)

    override _.OptionalAttribute(key, value) =
        let node = currentElement ()
#if !LADDER_NAIVE_CHECKS && !LADDER_UNCHECKED_ATTRIBUTES
        validateAttribute key
#endif
        attributeValue node key value

    override this.ReactiveAttribute(key, read) =
        let node = currentElement ()
#if !LADDER_NAIVE_CHECKS && !LADDER_UNCHECKED_ATTRIBUTES
        validateAttribute key
#endif
        startBinding this.Scope attributeEquals read (attributeValue node key)

    override _.InputValue(value) =
        let node = currentElement ()

        if modifierTag <> Input then
            invalidArg "modifiers" "Input value requires input"

        inputValue (node :?> HTMLInputElement) value

    override _.Browser(hook) =
        let cleanup = hook (box (currentElement ()))

        if root.IsDisposed then
            cleanup ()
            ensureSetup ()

        ownedCleanup (Api.getOwner ()) cleanup

    override this.Placed(kind, hook) =
        let node = currentElement ()

        if kind = PlacedKind.Widget then
            if isWidget node then
                raise (ArgumentException ("An element takes at most one content part", "modifiers"))

            if node.hasChildNodes () then
                raise (ArgumentException ("A widget element has no children", "children"))

            markWidget node

        PlacedHook.register container this.Scope (Api.getOwner ()) kind (node :> Node) false (not (isNull cursor)) (fun flags ->
            unbox (hook.Invoke (box node, flags)))

    override this.Head(entry) =
        this.HeadIn (container, entry)

    /// <summary>The document receiving the mount's head entries: the root mount host's document.</summary>
    member val internal HeadDocument = DocumentHead.documentOf host with get, set

#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
    /// <summary>The region passes of this mount running now.</summary>
    member val internal PassDepth = 0 with get, set
#endif

    /// <summary>
    /// Binds a document head entry for the current owner. Its readers are bindings of this mount; it is shown once
    /// <c>target</c> is placed and withdrawn when the owner is disposed. A meta or link element shows the entry's
    /// attribute values from the bind on, and each attribute is written only when it differs. A failure to show the
    /// entry fails the mount as a placed hook's failure does.
    /// </summary>
    member internal this.HeadIn(target: Placement, entry: HeadEntry) =
        let owner = Api.getOwner ()
        let record = DocumentHead.create this.HeadDocument entry.Kind entry.Key
        let scope = this.Scope
        ownedCleanup owner (fun () -> DocumentHead.withdraw record)

        if entry.Kind = HeadKind.Title then
            startBinding scope (Func<string, string, bool>(fun left right -> left = right)) entry.Title (DocumentHead.setTitle record)
        else
            let element = record.Element

            for name, value in entry.Attributes do
                DocumentHead.writeAttribute element name (Some value)

            for name, read in entry.Readers do
                startBinding scope attributeEquals read (DocumentHead.writeAttribute element name)

        target.Add (
            owner,
            fun () ->
                try
                    DocumentHead.activate record
                with error ->
                    let root = target.Root

                    if not scope.Tracks && root.Placing then
                        root.Hold error
                    else
                        scope.FailAll [ error ]
        )

    override this.Listen(eventName, handler) =
        let node = currentElement ()
        let scope = this.Scope

        let invoke =
            if scope.Tracks then
                let owned = Typed.ownedHandler handler

                fun event ->
                    try
                        owned event
                    with error ->
                        scope.FailAll [ error ]
            else
                Typed.ownedHandler handler

        if isDelegated delegatedEvents eventName then
            setDelegatedHandler node eventName invoke
            registerDelegated eventName
        else
            listenDirect node eventName invoke

    override _.Property(write) =
        write (box (currentElement ()))

    override this.ReactiveProperty(read, write) =
        let node = currentElement ()
        startBinding this.Scope propertyEquals read (fun value -> write.Invoke (box node, value))

    override this.Element(tag, modifiers, children) =
        let node = openElement this tag modifiers

        if not (isVoid tag) then
            let previous = target
            target <- childParent tag node

            try
                children ()
            finally
                target <- previous

        if isWidget node && node.hasChildNodes () then
            raise (ArgumentException ("A widget element has no children", "children"))

        if tag = Select then
            applyModifiers this tag node modifiers

    override this.ElementWith(tag, modifiers, children) =
        let node = openElement this tag modifiers

        if not (isVoid tag) then
            let previous = target
            target <- childParent tag node

            try
                for index in 0 .. children.Length - 1 do
                    children[index] this
            finally
                target <- previous

        if isWidget node && node.hasChildNodes () then
            raise (ArgumentException ("A widget element has no children", "children"))

        if tag = Select then
            applyModifiers this tag node modifiers

    override this.Bind(equals, read) =
        ensureSetup ()
        let node = text ""
        append node
        startBinding this.Scope (Func<_, _, _>(fun left right -> equals left right)) read (fun value -> node.textContent <- value)

    override this.Keyed(items, render) =
        this.AuthoredRegion (TemplateDom.ListRows, items, render)

    override this.Switch(read, render) =
        this.AuthoredRegion (TemplateDom.SelectedArm, KeyedItems.ofPairs read, render)

    override this.Boundary(views) =
        this.AuthoredRegion (TemplateDom.BoundaryContent, KeyedItems.ofPairs (fun () -> [| null, box views |]), (fun _ _ -> ()))

    /// <summary>Renders the view in a child container of the mount's current one, as a compiled view hole does.</summary>
    override this.Hole(render) =
        let outer = container
        let hole = Placement outer
        container <- hole

        try
            render this
        finally
            container <- outer

        hole.Place ()

    /// <summary>Adds boundary nodes at the current position and mounts a region of <c>kind</c> between them.</summary>
    member private this.AuthoredRegion(kind, items, render) =
        ensureSetup ()

        let regionStart, regionEnd =
            comment "ranvier:list:start", comment "ranvier:list:end"

        let placement =
            if isNull (box target) then
                NewRegion
            else
                range.AppendGenerated (target, regionStart)
                range.AppendGenerated (target, regionEnd)
                NestedRegion

        // An authored region's rows sit under an authored step of the mount's own path.
        let regionPath = pathOfMount().Authored(nextAuthored ())
        this.RegionOf (regionStart, regionEnd, placement, kind, items, render, null, regionPath, 0, container, null)

    /// <summary>
    /// The cursor of the server nodes the mount adopts while <c>Run</c> builds it, and afterwards the enclosing mount's
    /// cursor; null outside hydration.
    /// </summary>
    member internal _.HydrationCursor: TemplateDom.HydrationCursor =
        if not (isNull cursor) then cursor
        elif isNull (box enclosing) then null
        else enclosing.HydrationCursor

    /// <summary>Whether the mount's release leaves its nodes in place.</summary>
    member internal _.KeepsNodes = keeps ()

    /// <summary>The retired row roots this mount holds in place while their exits run.</summary>
    member internal _.Leaving: ResizeArray<unit -> unit> = ensureLeaving ()

    /// <summary>The mount whose <c>MountRow</c> created this one; null for a root mount.</summary>
    member internal _.Enclosing
        with get () = enclosing
        and set (value: MountState) = enclosing <- value

    /// <summary>
    /// Tears down a mount built with a cursor, leaving its nodes in place, and undoes the edits recorded with the cursor.
    /// </summary>
    member internal _.AbandonAdopted() =
        keepNodes <- true

        try
            root.Abandon ()
        finally
            if not (isNull rollback) then
                rollback.Restore ()

    /// <summary>
    /// Runs <c>action</c> once the root mount's build returns, before its cursor is dropped; a failed build discards
    /// the queued actions.
    /// </summary>
    member internal _.AtBuildEnd(action: unit -> unit) =
        if isNull (box enclosing) then
            if isNull buildEnd then
                buildEnd <- ResizeArray ()

            buildEnd.Add action
        else
            enclosing.AtBuildEnd action

    /// <summary>Fails the hydrating <c>Run</c> with <c>error</c> once its build returns, unless an earlier failure was recorded.</summary>
    member internal _.HydrationFailed(error: exn) =
        if isNull hydrationFailure then
            hydrationFailure <- error

    member internal this.NestedRegion
        (
            start: Node,
            finish: Node,
            kind: TemplateDom.RegionKind,
            items: KeyedItems,
            render: (unit -> obj) -> Backend -> unit,
            rows: Func<(unit -> obj), TemplateDom.MountServices, int, unit>,
            parentPath: InstancePath,
            region: int,
            parentContainer: Placement,
            regionCursor: TemplateDom.HydrationCursor
        ) =
        this.RegionOf (start, finish, NestedRegion, kind, items, render, rows, parentPath, region, parentContainer, regionCursor)

    member private _.PlaceRegion(regionStart: Node, regionEnd: Node, placement: RegionPlacement, validate: unit -> unit, relocate: unit -> unit) =
        match placement with
        | NestedRegion -> ()
        | NewRegion -> range.AddRegion (regionStart, regionEnd, validate, relocate)
        | RootRegion -> range.SpanRegion (regionStart, regionEnd, validate, relocate)

    /// <summary>The site of a region between <c>regionStart</c> and <c>regionEnd</c>, placed in the mount's range.</summary>
    /// <remarks>Under a boundary the region's reports go to the boundary.</remarks>
    member private this.Site(regionStart: Node, regionEnd: Node, placement: RegionPlacement) : RegionSite =
        let regionReport =
            if isNull (box boundary) then
                report
            else
                fun errors -> boundary.Fail errors

        let site = RegionSite (this, regionStart, regionEnd, regionReport, releaseNodes)

        this.PlaceRegion (regionStart, regionEnd, placement, (fun () -> site.Validate ()), (fun () -> Row.Relocate site.LaidOut))

        site

    /// <summary>
    /// Mounts an authored row or a boundary's view as a nested mount under <c>scope</c> and returns its nodes. The
    /// nested mount's first template takes the position of row <c>row</c> of region <c>region</c> under
    /// <c>parentPath</c>.
    /// </summary>
    /// <remarks>
    /// With a <c>cursor</c>, the nested mount adopts the server nodes from <c>cursor.Next</c> to the node at which its
    /// build leaves the cursor, and they stay in place between its <c>ranvier:start</c> and <c>ranvier:end</c>
    /// comments, inserted around them. The insertions are recorded with the cursor, which is nested in this mount's
    /// cursor once the nested mount is built. A failure of the nested mount leaves the server nodes as they were.
    /// </remarks>
    member private this.MountRow
        (
            current: unit -> obj,
            render: (unit -> obj) -> Backend -> unit,
            scope: ViewScope,
            target: Placement,
            parentPath: InstancePath,
            region: int,
            row: int,
            cursor: TemplateDom.HydrationCursor,
            regionStart: Node
        ) : Row =
        let adopting = not (isNull cursor)

        let rowHost =
            if adopting then
                parentOrNull cursor.Next
            else
                fragment () :> Node

        let (first: Node), (last: Node) =
            if adopting then
                (comment "ranvier:start" :> Node), (comment "ranvier:end" :> Node)
            else
                Unchecked.defaultof<_>, Unchecked.defaultof<_>

        if adopting then
            rowHost.insertBefore (first, cursor.Next)
            |> ignore

            cursor.Undo (fun () -> remove (first :?> ChildNode))

        let nested =
            MountState (
                scheduling,
                graph,
                root,
                Unchecked.defaultof<_>,
                report,
                rowHost,
                scope,
                target,
                identity,
                parentPath,
                InstanceStep.Row,
                region,
                row,
                cursor,
                first,
                last
            )

        nested.HeadDocument <- this.HeadDocument
        nested.Enclosing <- this
        let nest = Departures.seam.Nest

        if not (isNullish nest) then
            nest.Invoke (rowHost, regionStart)

        let build (context: BuildContext) =
            render current context.Backend

            if adopting then
                rowHost.insertBefore (last, cursor.Next) |> ignore
                cursor.Undo (fun () -> remove (last :?> ChildNode))

        let mounted = nested.Run (None, build)

        if adopting then
            let enclosingCursor = this.HydrationCursor

            if not (isNull enclosingCursor) then
                enclosingCursor.Nest cursor

            Row (first, last, Mount = mounted, Adopted = true)
        else
            let first =
                rowHost.firstChild
                |> Option.defaultWith (fun () -> invalid "A keyed row has no start boundary")

            let last =
                rowHost.lastChild
                |> Option.defaultWith (fun () -> invalid "A keyed row has no end boundary")

            Row (first, last, Mount = mounted)

    /// <summary>
    /// Runs a reflow's <c>After</c> once the pass's commits are in place: under synchronous commits as an untracked core
    /// effect, which runs after the effects the pass created, and under queued commits on a commit slot. A failure goes to
    /// the mount's scope.
    /// </summary>
    member private this.Post(action: unit -> unit) =
        match scheduling with
        | Synchronous ->
            Api.createEffect (fun () ->
                try
                    Api.untrack action
                with error ->
                    this.Scope.FailAll [ error ])
        | Queued queue -> queue.Enqueue (queue.Reserve this.Scope, action)

    /// <summary>
    /// Mounts one row per keyed item between the region's markers, on a core projection that creates new rows before it
    /// retires removed ones. Template rows (<c>rows</c>) are cloned into the region; authored rows (<c>render</c>) are
    /// nested mounts.
    /// </summary>
    /// <remarks>
    /// A first pass with <c>site.Hydrating</c> set adopts the server rows from the node after <c>regionStart</c>, leaves
    /// them in place, and requires the cursor to end at <c>regionEnd</c>; the mount's cursor position is kept. Its
    /// failure is settled by <c>RegionSite.Settle</c>.
    /// </remarks>
    member private this.ListRegion
        (
            site: RegionSite,
            regionStart: Node,
            regionEnd: Node,
            items: KeyedItems,
            render: (unit -> obj) -> Backend -> unit,
            rows: Func<(unit -> obj), TemplateDom.MountServices, int, unit>,
            parentPath: InstancePath,
            region: int,
            parentContainer: Placement
        ) =
        let mutable projection: Projection<obj, Row> = Unchecked.defaultof<_>
        // The items of the latest reconcile, the projection's only input; each write is followed by a bump of `version`,
        // which marks the projection stale.
        let mutable version: Signal<int> = Unchecked.defaultof<_>
        let mutable snapshot: obj array = [||]
        // The keys, their version and the rows of the last successful pass; null, -1 and empty until a pass succeeds, and
        // after a failed pass.
        let mutable previousKeys: obj array = null
        let mutable previousVersion = -1
        let mutable previousRows: Row array = [||]
        // Authored rows remove their nodes inside the pass, so their window is found from the items and validated before
        // it. The items of the last successful pass of authored rows, in the region's own copy; empty otherwise.
        let authored = isNull rows
        let mutable previousItems: obj array = [||]
        // The rows released since the last layout.
        let retired = ResizeArray<Row>()
        // The positions of an in-place window whose key differs from the last pass, in order.
        let moved = ResizeArray<int>()
        // The passes whose containers hold the hooks of the rows each pass creates.
        let passPlacement = PlacementPass parentContainer

        // When the window from `first` keeps its length and spans more than `smallWindow` positions, collects into `moved`
        // the positions whose key differs between `prior` and `next` and returns true.
        let collectMoved (prior: obj array) (next: obj array) (first: int) (last0: int) (last1: int) =
            moved.Clear ()

            if
                not (isNull prior)
                && last0 = last1
                && prior.Length = next.Length
                && last1 - first + 1 > smallWindow
            then
                for index in first..last1 do
                    if item prior index <> item next index then
                        moved.Add index

                true
            else
                false

        // Validates the laid-out rows from one before to one after each run of moved positions.
        let validateMoved (parent: Node) =
            let mutable k = 0

            while k < moved.Count do
                let first = at moved k
                let mutable last = first

                while k + 1 < moved.Count
                      && at moved (k + 1) <= last + 2 do
                    k <- k + 1
                    last <- at moved k

                Row.ValidateWindow (regionStart, regionEnd, site.LaidOut, parent, first - 1, last + 1)
                k <- k + 1

        let buildRow (current: unit -> obj) : Row =
            let mutable row: Row = null

            // Runs after the row's own cleanups.
            Api.onCleanup (fun () ->
                if not (isNull row) then
                    retired.Add row

                site.ReleaseRow row)

            row <-
                if isNull rows then
                    this.MountRow (current, render, boundary, passPlacement.Container, parentPath, region, site.NextRow (), null, regionStart)
                else
                    site.Builder.Build (rows, current, passPlacement, site.NextRow ())

            row

        let reconcile () =
            if site.Live () then
                // A hydrating pass adopts its rows in place, without window validation or a flush of released rows.
                let hydrating = site.Hydrating

                let measured =
                    if Departures.seam.ReflowEnabled && not hydrating then
                        Departures.seam.Before.Invoke (regionStart, this.Post)
                    else
                        Unchecked.defaultof<_>

                let resumed = if hydrating then cursor.Next else Unchecked.defaultof<_>

                if hydrating then
                    cursor.Next <- nextOrNull regionStart

                let parent = ref Unchecked.defaultof<Node>
                let keys = ref Unchecked.defaultof<obj array>
                let priorKeys = previousKeys
                let priorVersion = previousVersion
                let priorRows = previousRows
                let priorItems = previousItems
                previousKeys <- null
                previousVersion <- -1
                previousRows <- [||]
                previousItems <- [||]

                if priorRows.Length = 0 then
                    retired.Clear ()
                // Keys before `start` and after `end0` (previous) or `end1` (current) are equal in both passes.
                let start = ref 0
                let end0 = ref -1
                let end1 = ref -1
                // The window keeps the rows of its unchanged keys in place, and `moved` holds its changed positions.
                let inPlace = ref false

                // Validates the laid-out rows from `first - 1` to `last0 + 1`, or every row after a failed pass.
                let validate (parent: Node) (first: int) (last0: int) =
                    if priorRows.Length = site.LaidOut.Count then
                        Row.ValidateWindow (regionStart, regionEnd, site.LaidOut, parent, first - 1, last0 + 1)
                    else
                        Row.Validate (regionStart, regionEnd, site.LaidOut, parent)

                let recorded, failure =
                    try
                        MountLifetime.Capture (
                            graph,
                            report,
                            fun () ->
                                let current = site.ReadSource items.Read
                                // A pass whose source read is pending begins no placement pass.
                                passPlacement.Begin ()
                                site.Resume ()
                                snapshot <- current
                                Api.untrack (fun () -> version.Value <- version.Value + 1)
                                parent.Value <- site.Parent ()

                                if authored then
                                    let n0 = priorItems.Length
                                    let n1 = current.Length
                                    let mutable first = 0

                                    while first < n0
                                          && first < n1
                                          && obj.ReferenceEquals (item priorItems first, item current first) do
                                        first <- first + 1

                                    let mutable last0 = n0 - 1
                                    let mutable last1 = n1 - 1

                                    while last0 >= first
                                          && last1 >= first
                                          && obj.ReferenceEquals (item priorItems last0, item current last1) do
                                        last0 <- last0 - 1
                                        last1 <- last1 - 1

                                    start.Value <- first
                                    end0.Value <- last0
                                    end1.Value <- last1
                                    validate parent.Value first last0

                                // Runs the pass: its row setup, rollback and retirement errors are reported with this update.
                                // Rows retired by the pass keep their nodes until the flush.
                                site.Deferring <- true
                                let errors = ResizeArray<exn>()

                                try
                                    try
                                        keys.Value <- Api.untrack (fun () -> projection.Keys)
                                    finally
                                        site.Deferring <- false
                                with error ->
                                    errors.Add error

                                // A pass that empties the list removes every node between the markers in one operation.
                                let cleared =
                                    not authored
                                    && not (isNull keys.Value)
                                    && keys.Value.Length = 0
                                    && site.Live ()
                                    && site.ReleasedAll ()

                                // Before the flush removes any node, a pass that keeps rows validates the laid-out rows around the changed window.
                                let validated =
                                    try
                                        if authored || cleared || not (site.Live ()) then
                                            ()
                                        elif isNull keys.Value then
                                            if not hydrating then
                                                Row.Validate (regionStart, regionEnd, site.LaidOut, parent.Value)
                                        else
                                            let next = keys.Value
                                            let mutable first = 0

                                            let mutable last0 =
                                                (if isNull priorKeys then 0 else priorKeys.Length)
                                                - 1

                                            let mutable last1 = next.Length - 1

                                            let window =
                                                if isNull priorKeys then
                                                    ValueNone
                                                else
                                                    Api.untrack (fun () -> projection.ChangedSince priorVersion)

                                            match window with
                                            | ValueSome (struct (windowStart, windowEnd0, windowEnd1)) ->
                                                first <- windowStart
                                                last0 <- windowEnd0
                                                last1 <- windowEnd1
                                            // Without the projection's window, the common ends of the two key arrays bound the window.
                                            | ValueNone ->
                                                while first <= last0
                                                      && first <= last1
                                                      && item priorKeys first = item next first do
                                                    first <- first + 1

                                                while last0 >= first
                                                      && last1 >= first
                                                      && item priorKeys last0 = item next last1 do
                                                    last0 <- last0 - 1
                                                    last1 <- last1 - 1

                                            start.Value <- first
                                            end0.Value <- last0
                                            end1.Value <- last1
                                            inPlace.Value <- collectMoved priorKeys next first last0 last1

                                            if hydrating then
                                                ()
                                            elif
                                                inPlace.Value
                                                && priorRows.Length = site.LaidOut.Count
                                            then
                                                validateMoved parent.Value
                                            else
                                                validate parent.Value first last0

                                        true
                                    with error ->
                                        errors.Add error
                                        false

                                if validated && not hydrating then
                                    try
                                        site.FlushReleased (cleared && site.Live ())
                                    with error ->
                                        errors.Add error

                                if
                                    hydrating
                                    && errors.Count = 0
                                    && not (same cursor.Next regionEnd)
                                then
                                    errors.Add (
                                        TemplateDom.mismatch
                                            ("#"
                                             + string (positionAfter regionStart cursor.Next))
                                            "<!--]-->"
                                            (TemplateDom.describe cursor.Next)
                                    )

                                if errors.Count = 1 then
                                    raise errors[0]
                                elif errors.Count > 1 then
                                    raise (AggregateException ("Keyed update and row release failed", errors))
                        )
                    finally
                        if hydrating then
                            site.Hydrating <- false
                            cursor.Next <- resumed

                site.Settle (
                    recorded,
                    failure,
                    hydrating,
                    fun recorded ->
                        let layout =
                            try
                                Api.untrack (fun () ->
                                    let keys = keys.Value
                                    let count = keys.Length
                                    let start, end0, end1 = start.Value, end0.Value, end1.Value

                                    let lookup index =
#if RANVIER_VIEW_COUNTERS
                                        ListCounters.lookups.Value <- ListCounters.lookups.Value + 1
#endif
                                        projection.Get (item keys index)

                                    let removed = end0 - start + 1
                                    let inserted = end1 - start + 1

                                    let inPlace =
                                        priorRows.Length = count
                                        && if authored then
                                               collectMoved priorKeys keys start end0 end1
                                           else
                                               inPlace.Value

                                    let next: Row array =
                                        if inPlace then
                                            let next = copyRows priorRows

                                            for k in 0 .. moved.Count - 1 do
                                                let index = at moved k
                                                setItem next index (lookup index)

                                            next
                                        elif
                                            inserted <= smallWindow
                                            && priorRows.Length - removed + inserted = count
                                        then
#if FABLE_COMPILER
                                            let next = copyRows priorRows
                                            spliceRows next start removed (Array.init inserted (fun offset -> lookup (start + offset)))
                                            next
#else
                                            Array.concat
                                                [|
                                                    Array.sub priorRows 0 start
                                                    Array.init inserted (fun offset -> lookup (start + offset))
                                                    Array.sub priorRows (start + removed) (priorRows.Length - start - removed)
                                                |]
#endif
                                        else
                                            let next = Array.zeroCreate count

                                            for index in 0 .. count - 1 do
                                                let kept =
                                                    if index < start then item priorRows index
                                                    elif index > end1 then item priorRows (end0 + index - end1)
                                                    else null

                                                setItem next index (if isNullish kept then lookup index else kept)

                                            next

                                    // Kept rows released since the last layout are replaced, and bound the rows that
                                    // `Row.Layout` can skip.
                                    let mutable leading = min start count
                                    let mutable trailing = max 0 (count - 1 - end1)

                                    let checkedRows =
                                        if inPlace || leading + trailing > 0 then
                                            retired.Count
                                        else
                                            0

                                    if checkedRows > 0 then
                                        let window = rowSet ()

                                        if inPlace then
                                            for k in 0 .. moved.Count - 1 do
                                                rowSetAdd window (item priorRows (at moved k))
                                        else
                                            for index in start..end0 do
                                                rowSetAdd window (item priorRows index)

                                        for index in 0 .. checkedRows - 1 do
                                            let row = at retired index

                                            if not (rowSetHas window row) then
                                                for position in 0 .. count - 1 do
                                                    if
                                                        (inPlace || position < start || position > end1)
                                                        && same (item next position) row
                                                    then
                                                        setItem next position (lookup position)
                                                        leading <- min leading position
                                                        trailing <- min trailing (count - 1 - position)

                                    retired.Clear ()

                                    if priorRows.Length <> site.LaidOut.Count then
                                        leading <- 0
                                        trailing <- 0

                                    // Adopted rows are in place and in order.
                                    if hydrating then
                                        site.LaidOut.AddRange next
                                    else
                                        Row.Layout (
                                            (fun () -> site.Live ()),
                                            site.LaidOut,
                                            site.Builder.Staging,
                                            parent.Value,
                                            regionEnd,
                                            next,
                                            leading,
                                            trailing
                                        )

                                    next)
                                |> Ok
                            with error ->
                                Error error

                        match layout with
                        // A layout that disposed its own region reports the pass's errors and ends the update.
                        | Error _ when not (site.Live ()) -> site.Errors.Report recorded
                        | Error error ->
                            root.Dispose ()
                            site.Errors.Fail (error, recorded @ [ error ])
                        | Ok next ->
                            if site.Live () then
                                previousKeys <- keys.Value
                                previousVersion <- Api.untrack (fun () -> projection.KeysVersion)
                                previousRows <- next

#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
                                for index in 0 .. min previousKeys.Length next.Length - 1 do
                                    let row = item next index

                                    if not (isNull row) then
                                        Inspect.rememberRowKey row.First (item previousKeys index)
#endif

                                if authored then
                                    previousItems <- Array.copy snapshot

                                passPlacement.Place ()

                            site.Errors.Report recorded
                )

                // A pass whose source read was pending keeps the last pass's rows, so the next pass compares against them.
                if site.SourcePending then
                    previousKeys <- priorKeys
                    previousVersion <- priorVersion
                    previousRows <- priorRows
                    previousItems <- priorItems

                if not (isNull (box measured)) then
                    measured ()

        site.Own (fun () ->
#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
            // The reconcile effect's node: stale once the region's source changed after its last pass.
            let reconcileNode = ref Unchecked.defaultof<INode>

            Api.onCleanup (
                Inspect.register
                    regionStart
                    (Inspect.ListProbe (
                        parentPath.Row (region, -1)
                        , fun () ->
                            if isNull previousKeys then
                                [||]
                            else
                                [|
                                    for index in 0 .. min previousKeys.Length previousRows.Length - 1 do
                                        let row = item previousRows index

                                        if not (isNull row) then
                                            item previousKeys index, row.First
                                |]
                        , fun () -> Inspect.isStale reconcileNode.Value
                    ))
            )
#endif
            version <- Api.createSignal 0

            projection <-
                Api.createProjectionOnce ProjectionOptions.CreateFirst items.Key.Invoke buildRow (fun () ->
                    version.Value |> ignore
                    snapshot :> seq<_>)

#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
            Api.untrack (fun () -> reconcileNode.Value <- Api.createEffectNode (fun () -> site.InPass reconcile)))
#else
            Api.untrack (fun () -> Api.createEffect reconcile))
#endif

    /// <summary>
    /// Mounts the arm selected by the region's first item between its markers. An arm stays while its key is unchanged
    /// under <c>=</c> and receives the item's new value; another key disposes the arm, its cleanups and its nodes, before
    /// the next arm is set up.
    /// </summary>
    /// <remarks>
    /// A first selection with <c>site.Hydrating</c> set requires <c>regionStart</c> to be <c>&lt;!--[a:HASH--&gt;</c>,
    /// with the selected template arm's structural hash, adopts the server arm after it in place, and requires the
    /// cursor to end at <c>regionEnd</c>; the mount's cursor position is kept. A selected arm whose hash raises is a
    /// mismatch. A failure of the pass is settled by <c>RegionSite.Settle</c>.
    /// </remarks>
    member private this.ArmRegion
        (
            site: RegionSite,
            regionStart: Node,
            regionEnd: Node,
            read: unit -> obj array,
            render: (unit -> obj) -> Backend -> unit,
            rows: Func<(unit -> obj), TemplateDom.MountServices, int, unit>,
            parentPath: InstancePath,
            region: int,
            parentContainer: Placement
        ) =
        let mutable items: Memo<obj array> = Unchecked.defaultof<_>
        let mutable selection: Memo<obj> = Unchecked.defaultof<_>
        // The mounted arm's root owner, item signal and key; null while the region is empty.
        let mutable armOwner: Owner = Unchecked.defaultof<_>
        let mutable armItem: Signal<obj> = Unchecked.defaultof<_>
        let mutable armKey: obj = null
        // The arm mounts, each a pass whose container holds the hooks of the arm.
        let armPlacement = PlacementPass parentContainer

        let disposeArm () =
            if not (isNull (box armOwner)) then
                let owner = armOwner
                armOwner <- Unchecked.defaultof<_>
                armItem <- Unchecked.defaultof<_>
                armKey <- null
                site.LaidOut.Clear ()
                Api.batch (fun () -> owner.Dispose ())

        // Raises a mismatch at `#0` unless the start marker carries the hash of the selected template arm's definition.
        let checkHash (value: obj) =
            let compiled = TemplateDom.compiledOf (unbox<Template.Arm> value).Definition

            let expected =
                try
                    "<!--[a:" + compiled.Hash.Value + "-->"
                with error ->
                    "an arm the server renders ("
                    + error.Message
                    + ")"

            let found = TemplateDom.describe regionStart

            if found <> expected then
                raise (TemplateDom.mismatch "#0" expected found)

        let mountArm (selected: obj) (hydrating: bool) =
            if not (isNull selected) then
                let key, value = unbox<obj * obj> selected

                if hydrating then
                    checkHash value

                let item = Api.createSignal value
                let mutable created: Owner = Unchecked.defaultof<_>
                let mutable row: Row = null

                try
                    Api.runWithOwner site.Owner (fun () ->
                        Api.createRoot (fun owner ->
                            created <- owner
                            root.Adopt owner

                            // Runs after the arm's own cleanups.
                            Api.onCleanup (fun () -> site.ReleaseRow row)

                            let current () =
                                item.Value

                            let built =
                                if isNull rows then
                                    this.MountRow (
                                        current,
                                        render,
                                        boundary,
                                        armPlacement.Container,
                                        parentPath,
                                        region,
                                        site.NextRow (),
                                        null,
                                        regionStart
                                    )
                                else
                                    site.Builder.Build (rows, current, armPlacement, site.NextRow ())

                            // An adopted arm whose end check fails keeps its nodes for the restore or the region's re-render.
                            if hydrating && not (same cursor.Next regionEnd) then
                                raise (
                                    TemplateDom.mismatch
                                        ("#"
                                         + string (positionAfter regionStart cursor.Next))
                                        "<!--]-->"
                                        (TemplateDom.describe cursor.Next)
                                )

                            row <- built))

                    // An arm whose setup disposed the region leaves with the region's owner.
                    if site.Live () then
                        if not hydrating then
                            Row.MoveBefore (row, site.Parent (), regionEnd)

                        if not (isNull (box row.Mount)) then
                            row.Mount.Relocate ()

                        site.LaidOut.Add row
                        armOwner <- created
                        armItem <- item
                        armKey <- key
                        armPlacement.Place ()
                with _ ->
                    if not (isNull (box created)) then
                        created.Dispose ()

                    reraise ()

        let select () =
            selection.Value

        let reconcile () =
            if site.Live () then
                let hydrating = site.Hydrating
                let resumed = if hydrating then cursor.Next else Unchecked.defaultof<_>

                let measured =
                    if Departures.seam.ReflowEnabled && not hydrating then
                        Departures.seam.Before.Invoke (regionStart, this.Post)
                    else
                        Unchecked.defaultof<_>

                if hydrating then
                    cursor.Next <- nextOrNull regionStart

                let recorded, failure =
                    try
                        MountLifetime.Capture (
                            graph,
                            report,
                            fun () ->
                                let selected = site.ReadSource select
                                // A pass whose source read is pending begins no placement pass.
                                armPlacement.Begin ()

                                Api.untrack (fun () ->
                                    site.Resume ()

                                    // A selection read again after a pending or failed read keeps an arm of the same key.
                                    let kept =
                                        not hydrating
                                        && not (isNull selected)
                                        && not (isNull (box armItem))
                                        && fst (unbox<obj * obj> selected) = armKey

                                    if kept then
                                        armItem.Value <- snd (unbox<obj * obj> selected)
                                    else
                                        if hydrating then
                                            site.Parent () |> ignore
                                        else
                                            site.Validate ()

                                        disposeArm ()
                                        mountArm selected hydrating)
                        )
                    finally
                        if hydrating then
                            site.Hydrating <- false
                            cursor.Next <- resumed

                site.Settle (recorded, failure, hydrating, (fun recorded -> site.Errors.Report recorded))

                if not (isNull (box measured)) then
                    measured ()

        let updatePayload () =
            let current = items.Value

            Api.untrack (fun () ->
                if current.Length > 0 && not (isNull (box armItem)) then
                    let key, value = unbox<obj * obj> current[0]

                    if key = armKey then
                        armItem.Value <- value)

        site.Own (fun () ->
#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
            Api.onCleanup (
                Inspect.register
                    regionStart
                    (Inspect.ArmProbe (
                        parentPath.Row (region, -1)
                        , fun () ->
                            let definition =
                                if isNull (box armItem) then
                                    Unchecked.defaultof<_>
                                else
                                    match armItem.Peek with
                                    | :? Template.Arm as arm -> arm.Definition
                                    | _ -> Unchecked.defaultof<_>

                            struct (armKey, definition)
                        , fun () -> Inspect.isStale (box selection :?> INode)
                    ))
            )
#endif
            items <- Api.createMemo (fun _ -> read ())

            selection <-
                Api.createMemoWithComparer sameArm (fun _ ->
                    let current = items.Value

                    if current.Length = 0 then null else current[0])

#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
            Api.untrack (fun () -> Api.createEffect (fun () -> site.InPass reconcile))
#else
            Api.untrack (fun () -> Api.createEffect reconcile)
#endif
            Api.untrack (fun () -> Api.createEffect updatePayload))

    /// <summary>Mounts the view read once from the region's single item between its markers.</summary>
    member private this.HoleRegion
        (
            regionStart: Node,
            regionEnd: Node,
            placement: RegionPlacement,
            read: unit -> obj array,
            render: (unit -> obj) -> Backend -> unit,
            parentPath: InstancePath,
            region: int,
            parentContainer: Placement
        ) =
        let site = this.Site (regionStart, regionEnd, placement)
        let items = read ()
        let view = KeyedItems.value (items[0])
        let holeContainer = Placement parentContainer

#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
        let row =
            site.InPass (fun () ->
                this.MountRow ((fun () -> view), render, boundary, holeContainer, parentPath, region, site.NextRow (), null, regionStart))
#else
        let row =
            this.MountRow ((fun () -> view), render, boundary, holeContainer, parentPath, region, site.NextRow (), null, regionStart)
#endif

        Row.MoveBefore (row, site.Parent (), regionEnd)
        row.Mount.Relocate ()
        site.LaidOut.Add row
        holeContainer.Place ()
#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
        Api.onCleanup (Inspect.register regionStart (Inspect.HoleProbe (parentPath.Row (region, -1))))
#endif

    /// <summary>Mounts a boundary between the markers. Its content's bindings report to the boundary, and its pending view's bindings to this mount's boundary, or to the view's own mount outside every boundary.</summary>
    /// <remarks>
    /// With <c>regionCursor</c>, the boundary hydrates from the server nodes between the markers as the start marker's
    /// view (<c>[c</c>, <c>[p</c> or <c>[e</c>) records.
    /// </remarks>
    member private this.BoundaryRegion
        (
            regionStart: Node,
            regionEnd: Node,
            placement: RegionPlacement,
            views: BoundaryViews,
            parentPath: InstancePath,
            region: int,
            parentContainer: Placement,
            regionCursor: TemplateDom.HydrationCursor
        ) =
        let site = this.Site (regionStart, regionEnd, placement)

        let created =
            ViewBoundary (
                graph,
                report,
                site,
                regionStart,
                regionEnd,
                views,
                this.Scope,
                boundary,
                (fun () -> isNull (box boundary) && root.Phase = SettingUp),
                parentContainer,
                parentPath,
                region,
                (fun current render scope target parentPath region row cursor ->
                    this.MountRow (current, render, scope, target, parentPath, region, row, cursor, regionStart)),
                regionCursor,
                (fun () -> this.HydrationCursor),
                (fun error -> this.HydrationFailed error),
                (fun action -> this.AtBuildEnd action)
            )

        site.Own (fun () -> created.Start ())

    /// <summary>
    /// Mounts a region of <c>kind</c> between its markers. With <c>regionCursor</c>, a template list or arm adopts the
    /// server rows between the markers, a boundary hydrates as <c>BoundaryRegion</c> describes, and every other region
    /// replaces the server nodes between them with a fresh mount.
    /// </summary>
    /// <remarks>
    /// A structural mismatch of the region's hydrating pass, unless <c>regionCursor.Strict</c> is set, disposes the rows
    /// the pass created, removes the server nodes between the markers, reports the mismatch once and mounts the region
    /// fresh, its rows continuing the region's row counter. Each removal is recorded with the cursor.
    /// </remarks>
    member private this.RegionOf
        (
            start: Node,
            finish: Node,
            placement: RegionPlacement,
            kind: TemplateDom.RegionKind,
            items: KeyedItems,
            render: (unit -> obj) -> Backend -> unit,
            rows: Func<(unit -> obj), TemplateDom.MountServices, int, unit>,
            parentPath: InstancePath,
            region: int,
            parentContainer: Placement,
            regionCursor: TemplateDom.HydrationCursor
        ) =
        let mountRegion (site: RegionSite) =
            match kind with
            | TemplateDom.ListRows -> this.ListRegion (site, start, finish, items, render, rows, parentPath, region, parentContainer)
            | _ -> this.ArmRegion (site, start, finish, items.Read, render, rows, parentPath, region, parentContainer)

        match kind with
        | TemplateDom.ListRows
        | TemplateDom.SelectedArm when not (isNull regionCursor) && not (isNull rows) ->
            let site = this.Site (start, finish, placement)
            site.Hydrating <- true

            if not regionCursor.Strict then
                let enclosing = Api.getOwner ()

                site.Recover <-
                    fun errors ->
                        site.Reset ()
                        clearAdopted regionCursor start finish
                        report errors
                        site.Recovering <- true
                        Api.runWithOwner enclosing (fun () -> mountRegion site)

            mountRegion site
        | TemplateDom.BoundaryContent ->
            let pairs = items.Read ()

            this.BoundaryRegion (
                start,
                finish,
                placement,
                unbox<BoundaryViews>(KeyedItems.value (pairs[0])),
                parentPath,
                region,
                parentContainer,
                regionCursor
            )
        | _ ->
            if not (isNull regionCursor) then
                clearAdopted regionCursor start finish

            match kind with
            | TemplateDom.ListRows
            | TemplateDom.SelectedArm -> mountRegion (this.Site (start, finish, placement))
            | _ -> this.HoleRegion (start, finish, placement, items.Read, render, parentPath, region, parentContainer)

    override _.Component(setup) =
        ensureSetup ()
        // The component's hooks run after its parent's, as an inlined child's do.
        let previous = container.Enter ()

        try
#if LADDER_NAIVE_COMPONENTS
            setup
                {
                    OnCleanup = ownedCleanup (Api.getOwner ())
                    CreateId = fun () -> identity.CreateAuthoredId (pathOfMount ())
                }
#else
            Api.createRoot (fun owner ->
                root.Adopt owner

                Api.untrack (fun () ->
                    setup
                        {
                            OnCleanup = fun callback -> ownedCleanup owner callback
                            CreateId = fun () -> identity.CreateAuthoredId (pathOfMount ())
                        })

                if owner.IsDisposed then
                    invalid "View construction is only valid during live setup")
#endif
        finally
            container.Leave previous

    /// <summary>
    /// Renders a template: the mount's first template takes the mount's own position, and each later one the next
    /// authored step under the mount's path.
    /// </summary>
    override this.Template(definition, props) =
        ensureSetup ()
        let previous = container.Enter ()

        try
            match scheduling with
            | Synchronous ->
                let services = this :> TemplateDom.MountServices

                if positioned then
                    TemplateDom.materialise definition props services (pathOfMount ()) InstanceStep.Authored (nextAuthored ()) 0
                else
                    positioned <- true
                    TemplateDom.materialise definition props services parentPath kind a b
            | Queued _ -> definition.Expand (this, props)
        finally
            container.Leave previous

    interface TemplateDom.MountServices with
        member _.Append(node) =
            if isNull cursor || not (isNull (box target)) then
                append (node :?> ChildNode)
            else
                ensureSetup ()
                range.AddAdopted node

        // A first commit can run after the build; until the hydration ends, the commit writes only a value that differs
        // from the node's current value, and records the write with the hydration cursor.
        member this.Hydration =
            if root.Phase = SettingUp then
                cursor
            else
                this.HydrationCursor

        member _.Setup =
            if isNull (box templateSetup) then
                templateSetup <-
                    {
                        OnCleanup = fun callback -> ownedCleanup (Api.getOwner ()) callback
                        CreateId = identity.CreateId
                    }

            templateSetup

        member _.Identity = identity

        member _.SettingUp() =
            root.Phase = SettingUp

        member _.SetupFailed(error) =
            bindingSetupFailure <- Some error

        member _.RaiseSetupFailure() =
            bindingSetupFailure |> Option.iter raise

        member _.IsDelegated(eventName) =
            isDelegated delegatedEvents eventName

        member _.Delegate(eventName) =
            registerDelegated eventName

        member _.ListenDirect(node, eventName, invoke) =
            listenDirect node eventName invoke

        member this.ReportFailure(error) =
            this.Scope.Fail [ error ]

        member _.ClearFailure() =
            clearReported ()

        member this.Scope = this.Scope

        member _.Placement = container

        member this.Head(entry) =
            this.HeadIn (container, entry)

        member this.Region(start, finish, rootLevel, kind, items, render, rows, parentPath, region) =
            ensureSetup ()

            let placement =
                if rootLevel && isNull (box target) then
                    RootRegion
                else
                    NestedRegion

            this.RegionOf (start, finish, placement, kind, items, render, rows, parentPath, region, container, cursor)

    interface ViewScope with
        member _.Tracks = false
        member _.Wait() = ()
        member _.Expect() = false
        member _.Ready() = ()

        member _.Fail(errors) =
            reportFailures errors

        member _.FailAll(errors) =
            reportFailures errors

/// <summary>Mounts a view with an owned commit queue when deferred scheduling is selected.</summary>
/// <remarks>
/// <para>Nested keyed mounts share the containing mount's scheduler.</para>
/// <para>
/// A hook failure during the first placement, outside every boundary, disposes the mount and is rethrown once the
/// placement ends. The hooks held for that placement all run, including those after the failing one, and are torn down
/// with the mount.
/// </para>
/// <para>
/// A mount created inside a batch or a computation body, including a placed hook such as a widget's, runs its hooks
/// after <c>mountWith</c> returns: their failures go to <c>report</c> and are not rethrown.
/// </para>
/// </remarks>
let mountWith mode graph (parentOwner: Owner option) report host before build =
    let parentOwner =
        match parentOwner with
        | Some owner -> owner
        | None -> Unchecked.defaultof<_>

    match mode with
    | SynchronousCommits ->
        MountState(
            Synchronous,
            graph,
            null,
            parentOwner,
            report,
            host,
            Unchecked.defaultof<_>,
            Unchecked.defaultof<_>,
            IdentityScope "r",
            null,
            InstanceStep.Root,
            0,
            0,
            null,
            null,
            null
        )
            .Run(before, build)
    | QueuedCommits (failure, post) ->
        let queue = Queue (graph, failure, post)

        try
            let state =
                MountState (
                    Queued queue,
                    graph,
                    null,
                    parentOwner,
                    report,
                    host,
                    Unchecked.defaultof<_>,
                    Unchecked.defaultof<_>,
                    IdentityScope "r",
                    null,
                    InstanceStep.Root,
                    0,
                    0,
                    null,
                    null,
                    null
                )

            let mounted = state.Run (before, build)
            state.Root.OnCleanup queue.Dispose
            mounted
        with _ ->
            queue.Dispose ()
            reraise ()

let mount graph (parentOwner: Owner option) report host before build =
    mountWith SynchronousCommits graph parentOwner report host before build
