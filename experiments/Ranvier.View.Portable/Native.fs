/// <summary>
/// The host primitives of the view sources. Under Fable each primitive is an Emit expression inlined at its call sites; on
/// .NET each is F# over the <c>Fable.Core.TS.Dom</c> interfaces, the installed host, or .NET collections.
/// </summary>
module internal Ranvier.View.Foundation.Native

open Fable.Core
open Fable.Core.TS.Dom

#if !FABLE_COMPILER
/// <summary>The DOM globals of the current thread; the DOM twin installs one per document.</summary>
type IHost =
    abstract Document: Document
    abstract QueueMicrotask: (unit -> unit) -> unit
    abstract SetTimeout: (unit -> unit) * int -> obj
    abstract ClearTimeout: obj -> unit
    abstract RequestAnimationFrame: (float -> unit) -> obj
    abstract CancelAnimationFrame: obj -> unit
    /// <summary>Removes the nodes strictly between two siblings, as <c>Range.deleteContents</c> does.</summary>
    abstract DeleteBetween: Node * Node -> unit
    /// <summary>Adds an event listener and returns its removal.</summary>
    abstract Listen: target: obj * eventName: string * listener: (obj -> unit) -> (unit -> unit)
    /// <summary>Makes <c>currentTarget</c> of <c>event</c> read <c>read ()</c> until the dispatch ends.</summary>
    abstract OverrideCurrentTarget: event: obj * read: (unit -> obj) -> unit

[<Sealed; AbstractClass>]
type private HostSlot =
    [<System.ThreadStatic; DefaultValue>]
    static val mutable private current: IHost

    [<System.ThreadStatic; DefaultValue>]
    static val mutable private depth: int

    static member Current
        with get () = HostSlot.current
        and set (value: IHost) = HostSlot.current <- value

    /// <summary>The number of installations open on the current thread.</summary>
    static member Depth
        with get () = HostSlot.depth
        and set (value: int) = HostSlot.depth <- value

/// <summary>
/// Installs <c>host</c> for the current thread until the returned value is disposed. Disposal restores the previous
/// host; it raises <c>InvalidOperationException</c> on another thread or while a later installation is current.
/// </summary>
let install (host: IHost) : System.IDisposable =
    let previous = HostSlot.Current
    let thread = System.Environment.CurrentManagedThreadId
    HostSlot.Current <- host
    HostSlot.Depth <- HostSlot.Depth + 1
    let depth = HostSlot.Depth
    let disposed = ref false

    { new System.IDisposable with
        member _.Dispose() =
            if not disposed.Value then
                let current = System.Environment.CurrentManagedThreadId

                if current <> thread then
                    raise (System.InvalidOperationException $"A DOM host installed on thread {thread} was disposed on thread {current}")

                if HostSlot.Depth <> depth then
                    raise (System.InvalidOperationException "A DOM host was disposed while a later installation is current")

                disposed.Value <- true
                HostSlot.Depth <- depth - 1
                HostSlot.Current <- previous
    }

let private host () : IHost =
    let current = HostSlot.Current

    if isNull (box current) then
        raise (System.InvalidOperationException "No DOM host is installed on this thread; create a TwinDocument first")

    current

/// <summary>The option's value, or the null reference for <c>None</c>; for DOM interface types, which F# does not let be null.</summary>
let valueOrNull (value: 'T option) : 'T =
    match value with
    | Some item -> item
    | None -> Unchecked.defaultof<'T>

let inline private orNull (value: 'T option) : 'R =
    match value with
    | Some item -> unbox<'R> item
    | None -> Unchecked.defaultof<'R>
#endif

#if FABLE_COMPILER
[<Emit("document")>]
let document () : Document = jsNative
#else
let document () : Document =
    (host ()).Document
#endif

#if FABLE_COMPILER
[<Emit("$0 == null")>]
let isNullish (value: 'T) : bool = jsNative
#else
let isNullish (value: 'T) : bool =
    isNull (box value)
#endif

#if FABLE_COMPILER
[<Emit("$0.nextSibling")>]
let nextOrNull (node: Node) : Node = jsNative
#else
let nextOrNull (node: Node) : Node =
    orNull node.nextSibling
#endif

#if FABLE_COMPILER
[<Emit("$0.parentNode")>]
let parentOrNull (node: Node) : Node = jsNative
#else
let parentOrNull (node: Node) : Node =
    orNull node.parentNode
#endif

#if FABLE_COMPILER
[<Emit("$0.firstChild")>]
let firstChildOf (node: Node) : Node = jsNative
#else
let firstChildOf (node: Node) : Node =
    orNull node.firstChild
#endif

#if FABLE_COMPILER
[<Emit("$0.remove()")>]
let removeNode (node: Node) : unit = jsNative
#else
let removeNode (node: Node) : unit =
    (unbox<ChildNode> node).remove()
#endif

#if FABLE_COMPILER
[<Emit("$0.textContent")>]
let textContentOf (node: Node) : string = jsNative
#else
let textContentOf (node: Node) : string =
    Option.toObj node.textContent
#endif

#if FABLE_COMPILER
[<Emit("document.getElementById($0)")>]
let elementById (id: string) : Element = jsNative
#else
let elementById (id: string) : Element =
    orNull ((document ()).getElementById id)
#endif

#if !FABLE_COMPILER
/// <summary>
/// JavaScript's SameValueZero over the values Fable writes as primitives: strings, chars, numbers, booleans and
/// <c>Some</c> of those compare by value, NaN equals NaN and -0 equals +0; every other value compares by reference.
/// </summary>
[<Sealed>]
type SameValueZero private () =
    static member val Instance = SameValueZero ()

    static member internal Normal(value: obj) : obj =
        match value with
        | null -> null
        | :? string -> value
        | :? char as c -> box (string c)
        | :? bool -> value
        | :? float32 as f ->
            box (
                System.Double.Parse (
                    f.ToString ("R", System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.CultureInfo.InvariantCulture
                )
            )
        | :? int8
        | :? uint8
        | :? int16
        | :? uint16
        | :? int32
        | :? uint32
        | :? float
        | :? decimal -> box (System.Convert.ToDouble value)
        | :? System.Enum -> box (System.Convert.ToDouble value)
        | _ ->
            let t = value.GetType ()

            if
                t.IsGenericType
                && t.GetGenericTypeDefinition () = typedefof<option<_>>
            then
                SameValueZero.Normal (t.GetProperty("Value").GetValue value)
            elif
                t.IsGenericType
                && t.GetGenericTypeDefinition () = typedefof<voption<_>>
            then
                if (t.GetProperty "IsSome").GetValue value :?> bool then
                    SameValueZero.Normal (t.GetProperty("Value").GetValue value)
                else
                    null
            else
                value

    interface System.Collections.Generic.IEqualityComparer<obj> with
        member _.Equals(a, b) =
            match SameValueZero.Normal a, SameValueZero.Normal b with
            | null, null -> true
            | (:? string as x), (:? string as y) -> System.String.Equals (x, y, System.StringComparison.Ordinal)
            | (:? float as x), (:? float as y) ->
                x = y
                || (System.Double.IsNaN x && System.Double.IsNaN y)
            | (:? System.ValueType as x), (:? System.ValueType as y) -> x.GetType () = y.GetType () && x.Equals y
            | x, y -> obj.ReferenceEquals (x, y)

        member _.GetHashCode a =
            match SameValueZero.Normal a with
            | null -> 0
            | :? string as s -> System.StringComparer.Ordinal.GetHashCode s
            | :? float as f ->
                if System.Double.IsNaN f then 0x7ff8
                elif f = 0.0 then 0
                else f.GetHashCode ()
            | :? System.ValueType as v -> v.GetHashCode ()
            | other -> System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode other

/// <summary>Entries in insertion order with SameValueZero keys, as a JavaScript <c>Map</c> keeps them.</summary>
[<Sealed>]
[<AllowNullLiteral>]
type private OrderedTable() =
    let order = System.Collections.Generic.LinkedList<struct (obj * obj)>()

    let index =
        System.Collections.Generic.Dictionary<obj, System.Collections.Generic.LinkedListNode<struct (obj * obj)>>(SameValueZero.Instance)

    member _.ContainsKey(key: obj) =
        index.ContainsKey key

    member _.TryGetValue(key: obj, value: byref<obj>) =
        match index.TryGetValue key with
        | true, node ->
            let struct (_, stored) = node.Value
            value <- stored
            true
        | false, _ -> false

    member _.Set(key: obj, value: obj) =
        match index.TryGetValue key with
        | true, node ->
            let struct (stored, _) = node.Value
            node.Value <- struct (stored, value)
        | false, _ -> index[key] <- order.AddLast (struct (key, value))

    member _.Remove(key: obj) =
        match index.TryGetValue key with
        | true, node ->
            order.Remove node
            index.Remove key |> ignore
        | false, _ -> ()

    member _.Keys: obj array = [| for struct (key, _) in order -> key |]

/// <summary>A JavaScript <c>Map</c>, <c>Set</c>, <c>WeakMap</c> or <c>WeakSet</c> on .NET; a set stores each key as its own value.</summary>
[<Sealed; AllowNullLiteral>]
type NativeTable(weak: bool) =
    let ordered = if weak then null else OrderedTable ()

    let held =
        if weak then
            System.Runtime.CompilerServices.ConditionalWeakTable<obj, obj>()
        else
            null

    member _.Has(key: obj) =
        if weak then
            let mutable value = null
            held.TryGetValue (key, &value)
        else
            ordered.ContainsKey key

    member _.Get(key: obj) : obj =
        let mutable value = null

        if weak then
            held.TryGetValue (key, &value) |> ignore
        else
            ordered.TryGetValue (key, &value) |> ignore

        value

    member _.Set(key: obj, value: obj) =
        if weak then
            held.AddOrUpdate (key, value)
        else
            ordered.Set (key, value)

    member _.Delete(key: obj) =
        if weak then
            held.Remove key |> ignore
        else
            ordered.Remove key

    member _.Keys: obj array =
        if weak then
            invalidOp "A weak table cannot be enumerated"
        else
            ordered.Keys

let private table (value: obj) : NativeTable =
    value :?> NativeTable

let private expandos =
    System.Runtime.CompilerServices.ConditionalWeakTable<obj, System.Collections.Generic.Dictionary<string, obj>>()
#endif

// Collections

#if FABLE_COMPILER
[<Emit("new Map()")>]
let newMap () : obj = jsNative

[<Emit("new Set()")>]
let newSet () : obj = jsNative

[<Emit("new WeakMap()")>]
let newWeakMap () : obj = jsNative

[<Emit("new WeakSet()")>]
let newWeakSet () : obj = jsNative
#else
let newMap () : obj =
    NativeTable false

let newSet () : obj =
    NativeTable false

let newWeakMap () : obj =
    NativeTable true

let newWeakSet () : obj =
    NativeTable true
#endif

#if FABLE_COMPILER
[<Emit("$0.has($1)")>]
let has (tableValue: obj) (key: 'K) : bool = jsNative
#else
let has (tableValue: obj) (key: 'K) : bool =
    (table tableValue).Has(box key)
#endif

#if FABLE_COMPILER
[<Emit("$0.get($1)")>]
let get<'K, 'T> (tableValue: obj) (key: 'K) : 'T = jsNative
#else
let get<'K, 'T> (tableValue: obj) (key: 'K) : 'T =
    match (table tableValue).Get(box key) with
    | null -> Unchecked.defaultof<'T>
    | value -> unbox<'T> value
#endif

#if FABLE_COMPILER
[<Emit("($0.get($1) ?? -1)")>]
let getIndex (tableValue: obj) (key: 'K) : int = jsNative
#else
let getIndex (tableValue: obj) (key: 'K) : int =
    match (table tableValue).Get(box key) with
    | null -> -1
    | value -> unbox<int> value
#endif

#if FABLE_COMPILER
[<Emit("$0.set($1, $2)")>]
let set (tableValue: obj) (key: 'K) (value: 'T) : unit = jsNative
#else
let set (tableValue: obj) (key: 'K) (value: 'T) : unit =
    (table tableValue).Set(box key, box value)
#endif

#if FABLE_COMPILER
[<Emit("$0.add($1)")>]
let add (tableValue: obj) (key: 'K) : unit = jsNative
#else
let add (tableValue: obj) (key: 'K) : unit =
    (table tableValue).Set(box key, box key)
#endif

#if FABLE_COMPILER
[<Emit("$0.delete($1)")>]
let delete (tableValue: obj) (key: 'K) : unit = jsNative
#else
let delete (tableValue: obj) (key: 'K) : unit =
    (table tableValue).Delete(box key)
#endif

#if FABLE_COMPILER
[<Emit("$0.forEach($1)")>]
let forEach (tableValue: obj) (action: 'V -> unit) : unit = jsNative
#else
let forEach (tableValue: obj) (action: 'V -> unit) : unit =
    let entries = table tableValue

    for key in entries.Keys do
        action (unbox<'V>(entries.Get key))
#endif

// Arrays: a JavaScript read past the end is undefined and a write at the end appends.

#if FABLE_COMPILER
[<Emit("$0[$1]")>]
let listAt (items: ResizeArray<'T>) (index: int) : 'T = jsNative

[<Emit("$0[$1] = $2")>]
let listSet (items: ResizeArray<'T>) (index: int) (value: 'T) : unit = jsNative

[<Emit("$0.length = $1")>]
let listTruncate (items: ResizeArray<'T>) (count: int) : unit = jsNative

[<Emit("$0.splice($1, $2, ...$3)")>]
let listSplice (items: ResizeArray<'T>) (start: int) (count: int) (inserted: 'T array) : unit = jsNative

[<Emit("$0[$1]")>]
let arrayAt (items: 'T array) (index: int) : 'T = jsNative

[<Emit("$0[$1] = $2")>]
let arraySet (items: 'T array) (index: int) (value: 'T) : unit = jsNative

[<Emit("$0.slice()")>]
let arrayCopy (items: 'T array) : 'T array = jsNative

[<Emit("$0.slice($1, $2)")>]
let arraySlice (items: 'T array) (start: int) (finish: int) : 'T array = jsNative

[<Emit("new Array($0)")>]
let allocate (length: int) : obj array = jsNative
#else
let listAt (items: ResizeArray<'T>) (index: int) : 'T =
    if index >= 0 && index < items.Count then
        items[index]
    else
        Unchecked.defaultof<'T>

let listSet (items: ResizeArray<'T>) (index: int) (value: 'T) : unit =
    while items.Count < index do
        items.Add Unchecked.defaultof<'T>

    if index = items.Count then
        items.Add value
    else
        items[index] <- value

let listTruncate (items: ResizeArray<'T>) (count: int) : unit =
    if count < items.Count then
        items.RemoveRange (count, items.Count - count)

let listSplice (items: ResizeArray<'T>) (start: int) (count: int) (inserted: 'T array) : unit =
    let start =
        if start < 0 then
            max 0 (items.Count + start)
        else
            min start items.Count

    items.RemoveRange (start, max 0 (min count (items.Count - start)))
    items.InsertRange (start, inserted)

let arrayAt (items: 'T array) (index: int) : 'T =
    if index >= 0 && index < items.Length then
        items[index]
    else
        Unchecked.defaultof<'T>

/// <summary>Writes an element; a .NET array cannot grow, so an index past the end raises.</summary>
let arraySet (items: 'T array) (index: int) (value: 'T) : unit =
    if index < 0 || index >= items.Length then
        raise (System.InvalidOperationException $"A .NET array of length {items.Length} cannot grow to index {index}")

    items[index] <- value

let arrayCopy (items: 'T array) : 'T array =
    Array.copy items

let arraySlice (items: 'T array) (start: int) (finish: int) : 'T array =
    let clamp index =
        if index < 0 then
            max 0 (items.Length + index)
        else
            min index items.Length

    let start = clamp start
    let finish = clamp finish

    if finish <= start then
        [||]
    else
        Array.sub items start (finish - start)

let allocate (length: int) : obj array =
    Array.zeroCreate length
#endif

// Expandos: the per-object properties Fable writes as JavaScript properties.

#if FABLE_COMPILER
[<Emit("$0[$1]")>]
let expando<'T> (target: obj) (name: string) : 'T = jsNative

[<Emit("$0[$1] = $2")>]
let setExpando (target: obj) (name: string) (value: 'T) : unit = jsNative
#else
let expando<'T> (target: obj) (name: string) : 'T =
    let mutable slots = null

    if expandos.TryGetValue (target, &slots) then
        match slots.TryGetValue name with
        | true, value -> unbox<'T> value
        | false, _ -> Unchecked.defaultof<'T>
    else
        Unchecked.defaultof<'T>

let setExpando (target: obj) (name: string) (value: 'T) : unit =
    let slots =
        expandos.GetValue (target, fun _ -> System.Collections.Generic.Dictionary<string, obj>())

    slots[name] <- box value
#endif

#if !FABLE_COMPILER
// .NET bodies of the node, document, timer and microtask helpers kept under a local `#if` in the view files.

let lastChildOf (node: Node) : Node =
    orNull node.lastChild

let dataOf (node: Node) : string =
    (unbox<CharacterData> node).data

let localNameOf (node: Node) : string =
    (unbox<Element> node).localName

let prefixOf (node: Node) : string =
    Option.toObj (unbox<Element> node).prefix

/// <summary>The ECMAScript Number::toString form of <c>value</c>.</summary>
let numberText (value: float) : string =
    if System.Double.IsNaN value then
        "NaN"
    elif System.Double.IsPositiveInfinity value then
        "Infinity"
    elif System.Double.IsNegativeInfinity value then
        "-Infinity"
    elif value = 0.0 then
        "0"
    else
        let roundTrip =
            (abs value).ToString("R", System.Globalization.CultureInfo.InvariantCulture)

        let mantissa, exponent =
            match roundTrip.IndexOf 'E' with
            | -1 -> roundTrip, 0
            | at -> roundTrip.Substring (0, at), int (roundTrip.Substring (at + 1))

        let whole, fraction =
            match mantissa.IndexOf '.' with
            | -1 -> mantissa, ""
            | at -> mantissa.Substring (0, at), mantissa.Substring (at + 1)

        // The value is 0.digits × 10^point.
        let mutable digits = whole + fraction
        let mutable point = whole.Length + exponent

        while digits.Length > 1 && digits[0] = '0' do
            digits <- digits.Substring 1
            point <- point - 1

        digits <- digits.TrimEnd '0'
        let k = digits.Length
        let n = point

        let body =
            if k <= n && n <= 21 then
                digits + System.String ('0', n - k)
            elif 0 < n && n <= 21 then
                digits.Substring (0, n) + "." + digits.Substring n
            elif -6 < n && n <= 0 then
                "0." + System.String ('0', -n) + digits
            else
                let power = n - 1
                let sign = if power >= 0 then "+" else "-"

                let significand =
                    if k = 1 then
                        digits
                    else
                        digits.Substring (0, 1) + "." + digits.Substring 1

                significand + "e" + sign + string (abs power)

        if value < 0.0 then "-" + body else body

/// <summary>The value as JavaScript's <c>String</c> writes it, for the values Fable writes as primitives.</summary>
let jsString (value: obj) : string =
    match SameValueZero.Normal value with
    | null -> "null"
    | :? string as s -> s
    | :? bool as b -> if b then "true" else "false"
    | :? float as f -> numberText f
    | other -> string other

let setData (node: obj) (value: obj) : unit =
    (unbox<CharacterData> node).data <- (if isNull value then "" else jsString value)

let detach (node: Node) : unit =
    let parent = parentOrNull node

    if not (isNull (box parent)) then
        parent.removeChild node |> ignore

let appendNode (parent: Node) (node: Node) : unit =
    parent.appendChild node |> ignore

let replaceNode (existing: Node) (replacement: Node) (replaced: Node) : unit =
    (parentOrNull existing).replaceChild(replacement, replaced)
    |> ignore

let importDeep (node: Node) : Node =
    (document ()).importNode(node, Fable.Core.U2.Case1 true)

let setAttributeOrRemove (node: obj) (value: obj) (name: string) : unit =
    let element = unbox<Element> node

    if isNull value then
        element.removeAttribute name
    else
        element.setAttribute (name, jsString value)

let defaultValueOf (node: obj) : string =
    (unbox<HTMLInputElement> node).defaultValue

let createElementIn (owner: Document) (tag: string) : Element =
    owner.createElement (tagName = tag) :> Element

let documentOf (node: Node) : Document =
    match node.ownerDocument with
    | Some owner -> owner
    | None -> document ()

let headOf (owner: Document) : Node =
    owner.head :> Node

let replaceWith (node: Element) (next: Element) : unit =
    (unbox<ChildNode> node).replaceWith [| Fable.Core.U2.Case1 (unbox next) |]

/// <summary>The elements of the document's head that carry <c>data-ranvier-head</c>, in document order.</summary>
let markedIn (owner: Document) : Element array =
    let found = ResizeArray<Element>()

    let rec walk (node: Node) =
        let mutable child = firstChildOf node

        while not (isNullish child) do
            if child.nodeType = 1.0 then
                let element = unbox<Element> child

                if (element.getAttribute "data-ranvier-head").IsSome then
                    found.Add element

                walk child

            child <- nextOrNull child

    let head = headOf owner

    if not (isNullish head) then
        walk head

    found.ToArray ()

let setTimeout (action: unit -> unit) (milliseconds: int) : obj =
    (host ()).SetTimeout(action, milliseconds)

let clearTimeout (timer: obj) : unit =
    (host ()).ClearTimeout timer

let queueMicrotask (callback: unit -> unit) : unit =
    (host ()).QueueMicrotask callback

/// <summary>A JavaScript <c>WeakSet</c> of nodes.</summary>
[<Sealed>]
type private NodeWeakSet() =
    let held = System.Runtime.CompilerServices.ConditionalWeakTable<Node, obj>()

    interface Fable.Core.TS.Es.WeakSet<Node> with
        member this.add value =
            held.AddOrUpdate (value, null)
            this :> _

        member _.delete value =
            held.Remove value

        member _.has value =
            let mutable found = null
            held.TryGetValue (value, &found)

let newNodeWeakSet () : Fable.Core.TS.Es.WeakSet<Node> =
    NodeWeakSet ()
#endif

#if !FABLE_COMPILER
/// <summary>The names of the expandos set on <c>target</c> whose value is not null.</summary>
let expandoNames (target: obj) : string list =
    let mutable slots = null

    if expandos.TryGetValue (target, &slots) then
        [
            for pair in slots do
                if not (isNull pair.Value) then
                    pair.Key
        ]
        |> List.sort
    else
        []

let listen (target: obj) (eventName: string) (listener: obj -> unit) : unit -> unit =
    (host ()).Listen(target, eventName, listener)

let overrideCurrentTarget (event: obj) (read: unit -> obj) : unit =
    (host ()).OverrideCurrentTarget(event, read)

let deleteBetween (first: Node) (last: Node) : unit =
    (host ()).DeleteBetween(first, last)

let childNodesOf (node: Node) : Node array =
    let children = ResizeArray<Node>()
    let mutable child = firstChildOf node

    while not (isNullish child) do
        children.Add child
        child <- nextOrNull child

    children.ToArray ()

/// <summary>The element's <c>value</c>, <c>checked</c> and <c>selected</c>; null or false where the element has none.</summary>
let formStateOf (node: obj) : string * bool * bool =
    match node with
    | :? HTMLInputElement as input -> input.value, input.``checked``, false
    | :? HTMLTextAreaElement as area -> area.value, false, false
    | :? HTMLSelectElement as select -> select.value, false, false
    | :? HTMLOptionElement as option -> option.value, false, option.selected
    | :? HTMLButtonElement as button -> button.value, false, false
    | _ -> null, false, false

/// <summary>Writes back each part of a <c>formStateOf</c> snapshot that differs from the element's current state.</summary>
let restoreFormState (node: obj) (value: string) (isChecked: bool) (selected: bool) : unit =
    match node with
    | :? HTMLInputElement as input ->
        if input.value <> value then
            input.value <- value

        if input.``checked`` <> isChecked then
            input.``checked`` <- isChecked
    | :? HTMLTextAreaElement as area ->
        if area.value <> value then
            area.value <- value
    | :? HTMLSelectElement as select ->
        if select.value <> value then
            select.value <- value
    | :? HTMLOptionElement as option ->
        if option.value <> value then
            option.value <- value

        if option.selected <> selected then
            option.selected <- selected
    | :? HTMLButtonElement as button ->
        if button.value <> value then
            button.value <- value
    | _ -> ()
#endif

// Node creation in the global document.

#if FABLE_COMPILER
[<Emit("document.createDocumentFragment()")>]
let createDocumentFragment () : DocumentFragment = jsNative

[<Emit("document.createComment($0)")>]
let createComment (data: string) : Comment = jsNative

[<Emit("document.createTextNode($0)")>]
let createTextNode (data: string) : Text = jsNative

[<Emit("document.createElement($0)")>]
let createElement (tag: string) : HTMLElement = jsNative
#else
let createDocumentFragment () : DocumentFragment =
    (document ()).createDocumentFragment()

let createComment (data: string) : Comment =
    (document ()).createComment data

let createTextNode (data: string) : Text =
    (document ()).createTextNode data

let createElement (tag: string) : HTMLElement =
    (document ()).createElement(tagName = tag)
#endif

#if !FABLE_COMPILER
/// <summary>The element's attributes in order, as name and value pairs.</summary>
let attributesOf (element: Element) : (string * string) array =
    let attributes = element.attributes

    Array.init (int attributes.length) (fun i ->
        let attribute = (attributes.item (float i)).Value
        attribute.name, attribute.value)

/// <summary>Replaces the element's attributes with <c>attributes</c>, in order.</summary>
let setAttributesBack (element: Element) (attributes: (string * string) array) : unit =
    for name, _ in attributesOf element do
        element.removeAttribute name

    for name, value in attributes do
        element.setAttribute (name, value)
#endif
