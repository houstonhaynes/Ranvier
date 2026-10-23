/// <summary>Dumps of a mounted view: its instances, regions, rows and the values they show, as data, text and JSON.</summary>
module Ranvier.View.Dom.Debug

#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
open System
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier
open Ranvier.View
open Ranvier.View.Template

type DumpOptions =
    {
        /// <summary>Rows shown per list; the rest are one line with the elided keys' range.</summary>
        MaxRows: int
        /// <summary>Characters of a text node or value before it is cut with its full length noted.</summary>
        MaxText: int
        /// <summary>Items shown of a collection value.</summary>
        MaxItems: int
        MaxDepth: int
        /// <summary>Total characters of the text; the remainder is replaced by a count of omitted lines.</summary>
        MaxChars: int
        /// <summary>Instance fields and their values.</summary>
        Values: bool
        /// <summary>The document head section.</summary>
        Head: bool
        /// <summary>Core node ids on field lines.</summary>
        NodeIds: bool
    }

module DumpOptions =
    let defaults =
        {
            MaxRows = 12
            MaxText = 80
            MaxItems = 5
            MaxDepth = 64
            MaxChars = 65536
            Values = true
            Head = true
            NodeIds = false
        }

/// <summary>A mounted view as data; the text and JSON forms are renderings of it.</summary>
[<NoEquality; NoComparison>]
type DumpNode =
    | Element of tag: string * attributes: (string * string) array * properties: (string * string) array * children: DumpNode array
    | Text of value: string * bound: bool
    | Instance of InstanceDump * children: DumpNode array
    | List of path: string * rows: (string * DumpNode array) array * elided: Elided option * stale: bool
    | Arm of path: string * key: string * definition: string * children: DumpNode array * stale: bool
    | Boundary of path: string * state: BoundaryDump * children: DumpNode array
    | Hole of path: string * children: DumpNode array
    | Leaving of DumpNode
    | LeavingRow of key: string * node: DumpNode
    | Truncated of lines: int

and InstanceDump =
    {
        Name: string
        Path: string
        Hash: string
        Fields: FieldDump array
    }

and FieldDump =
    {
        Name: string
        Kind: string
        Value: string
        Status: string
        Node: string
    }

and BoundaryDump =
    {
        State: string
        Waiting: int
        Reason: string
    }

and Elided =
    {
        Count: int
        FirstKey: string
        LastKey: string
    }

[<NoEquality; NoComparison>]
type Dump =
    {
        Mount: string
        Root: DumpNode array
        Head: (string * string) array
        Options: DumpOptions
        TracePaths: Map<string, string>
    }

#if FABLE_COMPILER
[<Emit("Object.getPrototypeOf($0)")>]
let private prototypeOf (value: obj) : obj = jsNative

[<Emit("$0.prototype")>]
let private constructorPrototype (constructor: obj) : obj = jsNative

[<Emit("($1 != null && typeof $1 === 'object' && Object.prototype.isPrototypeOf.call($0, $1))")>]
let private hasPrototype (prototype: obj) (value: obj) : bool = jsNative

let private recordPrototype =
    prototypeOf (constructorPrototype (box (JsInterop.jsConstructor<FieldDump>)))

let private unionPrototype =
    prototypeOf (constructorPrototype (box (JsInterop.jsConstructor<Choice<obj, obj>>)))

let private listPrototype = prototypeOf (box ([]: obj list))
let private mapPrototype = prototypeOf (box (Map.empty<int, int>))
let private setPrototype = prototypeOf (box (Set.empty<int>))
let private decimalPrototype = prototypeOf (box 0M)
let private optionPrototype = prototypeOf (box (Some (box null)))
let private exceptionPrototype = prototypeOf (box (exn ""))

let private corePrototypes =
    [|
        constructorPrototype (box (JsInterop.jsConstructor<Signal<obj>>))
        constructorPrototype (box (JsInterop.jsConstructor<Memo<obj>>))
        constructorPrototype (box (JsInterop.jsConstructor<AsyncMemo<obj>>))
        constructorPrototype (box (JsInterop.jsConstructor<AsyncSource<obj>>))
    |]

let private isNode (value: obj) =
    corePrototypes
    |> Array.exists (fun prototype -> hasPrototype prototype value)

[<Emit("$0.value")>]
let private peek (node: obj) : obj = jsNative

[<Emit("(() => { if ($0 == null) return 'null'; const t = typeof $0; if(t === 'function') return 'fun'; if(t === 'string') return 'string'; if(t === 'boolean') return 'bool'; if(t === 'number') return 'number'; if(t === 'bigint') return 'int64'; if(Array.isArray($0) || ArrayBuffer.isView($0)) return 'array'; let p=Object.getPrototypeOf($0); while(p) { const d=Object.getOwnPropertyDescriptor(p,'constructor'); if(d && 'value' in d) { const n=Object.getOwnPropertyDescriptor(d.value,'name'); if(n && 'value' in n) return n.value === 'Object' ? 'record' : n.value.replace(/\\$\\d+$/, ''); } p=Object.getPrototypeOf(p); } return 'object'; })()")>]
let private rawTypeName (value: obj) : string = jsNative

let private typeName (value: obj) =
    if hasPrototype decimalPrototype value then "decimal"
    elif hasPrototype mapPrototype value then "map"
    elif hasPrototype setPrototype value then "set"
    elif hasPrototype listPrototype value then "list"
    else rawTypeName value

[<Emit("typeof $0 === 'bigint'")>]
let private isBigInteger (value: obj) : bool = jsNative

let private isExactScalar (value: obj) =
    isBigInteger value
    || hasPrototype decimalPrototype value

let private isCountedCollection (value: obj) =
    hasPrototype mapPrototype value
    || hasPrototype setPrototype value

[<Emit("Object.entries(Object.getOwnPropertyDescriptors($0)).filter(([k, d]) => d.enumerable && 'value' in d).map(([k, d]) => [k, d.value])")>]
let private dataFields (value: obj) : (string * obj) array = jsNative

[<Emit("($0 != null && Object.getPrototypeOf($0) === Object.prototype)")>]
let private isPlainRecord (value: obj) : bool = jsNative

let private ownFields (value: obj) =
    if
        hasPrototype recordPrototype value
        || isPlainRecord value
    then
        dataFields value
    else
        null

[<Emit("($0.Status ?? 0)")>]
let private statusOf (node: obj) : int = jsNative

[<Emit("($0.Id ?? 0)")>]
let private idOf (node: obj) : int = jsNative

[<Emit("($0.failure?.error?.message ?? '')")>]
let private errorOf (node: obj) : string = jsNative

[<Emit("typeof $0 === 'string'")>]
let private isString (value: obj) : bool = jsNative

[<Emit("typeof $0 === 'number'")>]
let private isNumber (value: obj) : bool = jsNative

[<Emit("typeof $0 === 'boolean'")>]
let private isBool (value: obj) : bool = jsNative

[<Emit("(Array.isArray($0) || ArrayBuffer.isView($0))")>]
let private isArray (value: obj) : bool = jsNative

[<Emit("Array.from($0)")>]
let private itemsOf (value: obj) : obj array = jsNative

let private isList (value: obj) =
    hasPrototype listPrototype value

[<Emit("$0.nodeType")>]
let private kindOf (node: Node) : float = jsNative

[<Emit("$0.data")>]
let private dataOf (node: Node) : string = jsNative

[<Emit("$0.localName")>]
let private tagOf (node: Node) : string = jsNative

[<Emit("Array.from($0.attributes, a => [a.name, a.value])")>]
let private attributesOf (node: Node) : (string * string) array = jsNative

[<Emit("Array.from($0.childNodes)")>]
let private childrenOf (node: Node) : Node array = jsNative

[<Emit("(($0.localName === 'input' || $0.localName === 'textarea') ? $0.value : null)")>]
let private valueOf (node: Node) : string = jsNative

[<Emit("($0.localName === 'input' ? $0.checked : null)")>]
let private checkedOf (node: Node) : obj = jsNative

[<Emit("Array.from(($0.ownerDocument?.head ?? $0.head)?.querySelectorAll('[data-ranvier-head]') ?? [])")>]
let private markedHead (node: Node) : Node array = jsNative

[<Emit("(Array.isArray($0) && $0.length === 2 ? $0[0] : $0)")>]
let private firstOfPair (value: obj) : obj = jsNative

[<Emit("($0 instanceof Error)")>]
let private isNativeError (value: obj) : bool = jsNative

let private isError (value: obj) =
    isNativeError value
    || hasPrototype exceptionPrototype value

[<Emit("(Object.getOwnPropertyDescriptor($0,'message')?.value ?? '')")>]
let private messageOf (value: obj) : string = jsNative

[<Emit("(($0.ownerDocument ?? $0).title ?? '')")>]
let private documentTitle (node: Node) : string = jsNative
#else
let private isNode (value: obj) : bool =
    value :? INode

let private peek (node: obj) : obj =
    node.GetType().GetProperty("Peek").GetValue node

let private typeName (value: obj) : string =
    match value with
    | null -> "null"
    | :? string
    | :? char
    | :? Guid -> "string"
    | :? int64
    | :? uint64
    | :? bigint -> "int64"
    | :? decimal -> "decimal"
    | :? bool -> "bool"
    | :? Array -> "array"
    | :? Delegate -> "fun"
    | _ when Microsoft.FSharp.Reflection.FSharpType.IsFunction (value.GetType ()) -> "fun"
    | _ ->
        let t = value.GetType ()

        if t.IsPrimitive then
            "number"
        elif
            t.IsGenericType
            && t.GetGenericTypeDefinition () = typedefof<ResizeArray<_>>
        then
            "array"
        elif
            t.IsGenericType
            && t.GetGenericTypeDefinition () = typedefof<Map<_, _>>
        then
            "map"
        elif
            t.IsGenericType
            && t.GetGenericTypeDefinition () = typedefof<Set<_>>
        then
            "set"
        elif
            t.IsGenericType
            && t.GetGenericTypeDefinition () = typedefof<list<_>>
        then
            "list"
        elif t.Name.StartsWith "<>f__AnonymousType" then
            "record"
        else
            let t =
                if
                    Microsoft.FSharp.Reflection.FSharpType.IsUnion t
                    && not (isNull t.BaseType)
                    && Microsoft.FSharp.Reflection.FSharpType.IsUnion t.BaseType
                then
                    t.BaseType
                else
                    t

            let tick = t.Name.IndexOf '`'
            if tick < 0 then t.Name else t.Name.Substring (0, tick)

let private isExactScalar (value: obj) =
    value :? int64
    || value :? uint64
    || value :? bigint
    || value :? decimal

let private isCountedCollection (value: obj) =
    not (isNull value)
    && value.GetType().IsGenericType
    && (value.GetType().GetGenericTypeDefinition() = typedefof<Map<_, _>>
        || value.GetType().GetGenericTypeDefinition() = typedefof<Set<_>>)

let private ownFields (value: obj) : (string * obj) array =
    match value with
    | null -> null
    | :? INode -> null
    | _ ->
        let t = value.GetType ()

        let any =
            Reflection.BindingFlags.Public
            ||| Reflection.BindingFlags.NonPublic

        if Microsoft.FSharp.Reflection.FSharpType.IsRecord (t, any) then
            Microsoft.FSharp.Reflection.FSharpType.GetRecordFields (t, any)
            |> Array.map (fun field -> field.Name, field.GetValue value)
        elif Microsoft.FSharp.Reflection.FSharpType.IsTuple t then
            Microsoft.FSharp.Reflection.FSharpValue.GetTupleFields value
            |> Array.mapi (fun i item -> $"Item{i + 1}", item)
        else
            null

let private statusOf (node: obj) : int =
    int (unbox<INode> node).Status

let private idOf (node: obj) : int =
    (unbox<INode> node).Id

let private errorOf (node: obj) : string =
    let flags =
        Reflection.BindingFlags.Public
        ||| Reflection.BindingFlags.NonPublic
        ||| Reflection.BindingFlags.Instance

    let read (target: obj) (name: string) =
        match target with
        | null -> null
        | _ ->
            match target.GetType().GetProperty(name, flags) with
            | null -> null
            | property -> property.GetValue target

    match read node "Error" with
    | :? exn as error -> error.Message
    | _ ->
        match read (read node "Failure") "Error" with
        | :? exn as error -> error.Message
        | _ -> ""

let private isString (value: obj) =
    value :? string || value :? char || value :? Guid

let private isNumber (value: obj) =
    match value with
    | :? int
    | :? float
    | :? int64
    | :? float32
    | :? decimal
    | :? int16
    | :? byte
    | :? sbyte
    | :? uint16
    | :? uint64
    | :? nativeint
    | :? unativeint
    | :? uint32 -> true
    | _ -> false

let private isBool (value: obj) =
    value :? bool

let private isArray (value: obj) =
    value :? Array
    || (not (isNull value)
        && value.GetType().IsGenericType
        && value.GetType().GetGenericTypeDefinition() = typedefof<ResizeArray<_>>)

let private itemsOf (value: obj) : obj array =
    [| for item in (value :?> Collections.IEnumerable) -> item |]

let private isList (value: obj) =
    not (isNull value)
    && value.GetType().IsGenericType
    && value.GetType().GetGenericTypeDefinition() = typedefof<list<_>>

let private kindOf (node: Node) : float =
    node.nodeType

let private dataOf (node: Node) : string =
    Native.dataOf node

let private tagOf (node: Node) : string =
    (unbox<Element> node).localName

let private attributesOf (node: Node) =
    Native.attributesOf (unbox<Element> node)

let private childrenOf (node: Node) =
    Native.childNodesOf node

let private valueOf (node: Node) : string =
    match box node with
    | :? HTMLInputElement as input -> input.value
    | :? HTMLTextAreaElement as area -> area.value
    | _ -> null

let private checkedOf (node: Node) : obj =
    match box node with
    | :? HTMLInputElement as input -> box input.``checked``
    | _ -> null

let private markedHead (node: Node) : Node array =
    [| for element in Native.markedIn (Native.documentOf node) -> element :> Node |]

let private firstOfPair (value: obj) : obj =
    if
        not (isNull value)
        && Microsoft.FSharp.Reflection.FSharpType.IsTuple (value.GetType ())
    then
        (Microsoft.FSharp.Reflection.FSharpValue.GetTupleFields value)[0]
    else
        value

let private isError (value: obj) =
    value :? exn

let private messageOf (value: obj) =
    (value :?> exn).Message

let private documentTitle (node: Node) =
    (Native.documentOf node).title
#endif

#if FABLE_COMPILER
let private isSome (value: obj) =
    hasPrototype optionPrototype value

[<Emit("$0.value")>]
let private someValue (value: obj) : obj = jsNative

let private isUnion (value: obj) =
    hasPrototype unionPrototype value

[<Emit("[$0.cases()[$0.tag], $0.fields]")>]
let private unionParts (value: obj) : string * obj array = jsNative

[<Emit("String($0)")>]
let private scalarText (value: obj) : string = jsNative

[<Emit("$0.size")>]
let private collectionCount (value: obj) : int = jsNative
#else
let private isSome (value: obj) =
    not (isNull value)
    && value.GetType().IsGenericType
    && value.GetType().GetGenericTypeDefinition() = typedefof<option<_>>

let private someValue (value: obj) =
    value.GetType().GetProperty("Value").GetValue value

let private isUnion (value: obj) =
    not (isNull value)
    && Microsoft.FSharp.Reflection.FSharpType.IsUnion (value.GetType ())

let private unionParts (value: obj) =
    let case, fields =
        Microsoft.FSharp.Reflection.FSharpValue.GetUnionFields (value, value.GetType ())

    case.Name, fields

let private scalarText (value: obj) =
    match value with
    | :? IFormattable as number -> number.ToString (null, Globalization.CultureInfo.InvariantCulture)
    | _ -> string value

let private collectionCount (value: obj) =
    unbox<int>(value.GetType().GetProperty("Count").GetValue value)
#endif

let private numberOf (value: obj) : float =
#if FABLE_COMPILER
    unbox<float> value
#else
    Convert.ToDouble value
#endif

let private quote (options: DumpOptions) (text: string) =
    let escaped =
        text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r")

    if text.Length > options.MaxText then
        "\""
        + escaped.Substring (0, min escaped.Length options.MaxText)
        + "…\" ("
        + string text.Length
        + ")"
    else
        "\"" + escaped + "\""

/// <summary>A value as the dump shows it: quoted strings, ECMAScript numbers, bounded collections and records.</summary>
let rec private format (options: DumpOptions) (path: obj list) (depth: int) (value: obj) : string =
    if isNull value then
        "null"
    elif isString value then
        quote options (scalarText value)
    elif isBool value then
        (if unbox<bool> value then "true" else "false")
    elif isExactScalar value then
        scalarText value
    elif isNumber value then
        Template.numberText (numberOf value)
    elif depth >= options.MaxDepth then
        "…"
    elif List.exists (fun seen -> obj.ReferenceEquals (seen, value)) path then
        "<cycle>"
    elif isNode value then
        typeName(value).ToLowerInvariant()
    elif isSome value then
        format options path depth (someValue value)
    elif isError value then
        typeName value
        + " "
        + quote options (messageOf value)
    else
        let path = value :: path

        let items (opening: string) (closing: string) (all: obj array) =
            let shown =
                all
                |> Array.truncate options.MaxItems
                |> Array.map (format options path (depth + 1))

            let more = if all.Length > options.MaxItems then [| "…" |] else [||]

            opening
            + String.Join ("; ", Array.append shown more)
            + closing
            + " ("
            + string all.Length
            + ")"

        if isArray value then
            items "[|" "|]" (itemsOf value)
        elif isList value then
            items "[" "]" (itemsOf value)
        elif isCountedCollection value then
            typeName value
            + " ("
            + string (collectionCount value)
            + ")"
        elif isUnion value then
            let name, fields = unionParts value

            if fields.Length = 0 then
                name
            else
                name
                + " "
                + String.Join (
                    " ",
                    fields
                    |> Array.map (format options path (depth + 1))
                )
        else
            match ownFields value with
            | null -> typeName value
            | fields when fields.Length = 0 -> typeName value
            | fields ->
                let shown =
                    fields
                    |> Array.truncate options.MaxItems
                    |> Array.map (fun (name, field) -> name + "=" + format options path (depth + 1) field)

                let more = if fields.Length > options.MaxItems then [| "…" |] else [||]

                "{"
                + String.Join ("; ", Array.append shown more)
                + "}"

let private nodeKind (value: obj) =
    match typeName value with
    | "Signal" -> "signal"
    | "Memo" -> "memo"
    | "AsyncMemo" -> "asyncmemo"
    | other -> other

let private fieldOf (options: DumpOptions) (name: string, value: obj) : FieldDump =
    if isNode value then
        let status = statusOf value

        let shown, statusText =
            if status &&& int Status.Pending <> 0 then
                "pending", "pending"
            elif status &&& int Status.Error <> 0 then
                "error: " + errorOf value, "error"
            elif status &&& int Status.Uninitialized <> 0 then
                "uninitialised", "uninitialised"
            else
                format options [] 1 (peek value), ""

        {
            Name = name
            Kind = nodeKind value
            Value = shown
            Status = statusText
            Node = string (idOf value)
        }
    else
        {
            Name = name
            Kind = typeName (if isSome value then someValue value else value)
            Value = format options [] 1 value
            Status = ""
            Node = ""
        }

let private shownPath (prefix: string) (path: InstancePath) =
    if isNull (box path) then "" else prefix + "-" + path.Text

let private instanceOf (options: DumpOptions) (prefix: string) (definition: TemplateComponent) (instance: obj) (path: InstancePath) =
    let hash =
        if isNull (box definition) then
            ""
        else
            try
                Template.structuralHash definition
            with _ ->
                "?"

    {
        Name = (if isNull instance then "unit" else typeName instance)
        Path = shownPath prefix path
        Hash = hash
        Fields =
            if options.Values then
                match ownFields instance with
                | null -> [||]
                | fields -> fields |> Array.map (fieldOf options)
            else
                [||]
    }

let private isComment (node: Node) =
    kindOf node = 8.0

let private regionEnd (start: Node) : Node =
    let mutable depth = 1
    let mutable cursor = Native.nextOrNull start
    let mutable found: Node = Unchecked.defaultof<_>

    while isNull (box found) && not (isNull (box cursor)) do
        if isComment cursor then
            let data = dataOf cursor

            if data.StartsWith "[" then
                depth <- depth + 1
            elif data = "]" then
                depth <- depth - 1

                if depth = 0 then
                    found <- cursor

        if isNull (box found) then
            cursor <- Native.nextOrNull cursor

    found

let private leaving (node: Node) =
    let seam = Departures.seam

    not (isNull (box node))
    && seam.Enabled = true
    && not (isNull (box seam.Within))
    && seam.Within.Invoke node

let private checkFlushing (probe: Inspect.Probe) =
    match probe with
    | Inspect.MountProbe (_, _, flushing, _, _) when flushing () -> raise (InvalidOperationException "Debug.dump ran inside a flush")
    | _ -> ()

let private keyText (options: DumpOptions) (key: obj) =
    if isString key then
        quote options (unbox<string> key)
    else
        format options [] 1 key

let rec private rootName (nodes: DumpNode array) =
    nodes
    |> Array.tryPick (function
        | Element (tag, _, _, _) -> Some tag
        | Instance (_, children) -> Some (rootName children)
        | _ -> None)
    |> Option.defaultValue "fragment"

let private selectedView (nodes: DumpNode array) =
    nodes
    |> Array.tryPick (function
        | Instance (instance, children) -> Some (rootName children + "#" + instance.Hash)
        | _ -> None)
    |> Option.defaultValue (rootName nodes)

/// <summary>The dump nodes of the siblings from <c>first</c> up to, not including, <c>until</c> (null for the last sibling).</summary>
let rec private walk (options: DumpOptions) (prefix: string ref) (first: Node) (until: Node) : DumpNode array =
    let nodes = ResizeArray<DumpNode>()
    let mutable cursor = first
    let inheritedPrefix = prefix.Value
    let mutable mountPrefixes = []

    while not (isNull (box cursor))
          && not (obj.ReferenceEquals (cursor, until)) do
        let next = Native.nextOrNull cursor
        let probe = Inspect.probeOf cursor
        let startIndex = nodes.Count

        match probe with
        | Some (Inspect.MountProbe (_, _, _, mountPrefix, _) as mount) ->
            checkFlushing mount
            mountPrefixes <- prefix.Value :: mountPrefixes
            prefix.Value <- mountPrefix
        | _ when isComment cursor && dataOf cursor = "ranvier:end" ->
            match mountPrefixes with
            | parent :: rest ->
                prefix.Value <- parent
                mountPrefixes <- rest
            | [] -> ()
        | _ -> ()

        let regionChildren () =
            let finish = regionEnd cursor

            walk options prefix next finish,
            (if isNull (box finish) then
                 Unchecked.defaultof<_>
             else
                 Native.nextOrNull finish)

        let resume =
            match probe with
            | Some (Inspect.ListProbe (path, rows, stale)) ->
                let finish = regionEnd cursor
                let rows = rows ()

                let leading =
                    walk options prefix next (if rows.Length = 0 then finish else snd rows[0])

                let rowNodes =
                    rows
                    |> Array.mapi (fun index (key, firstNode) ->
                        let stop =
                            if index + 1 < rows.Length then
                                snd rows[index + 1]
                            else
                                finish

                        lazy (keyText options key, walk options prefix firstNode stop))

                let shown, elided =
                    if rowNodes.Length > options.MaxRows then
                        let tail = min 3 (max 0 options.MaxRows)
                        let head = max 0 (options.MaxRows - tail)

                        let hidden = rows[head .. rows.Length - tail - 1]

                        Array.append rowNodes[.. head - 1] rowNodes[rowNodes.Length - tail ..],
                        Some
                            {
                                Count = hidden.Length
                                FirstKey = keyText options (fst hidden[0])
                                LastKey = keyText options (fst hidden[hidden.Length - 1])
                            }
                    else
                        rowNodes, None

                let shown = shown |> Array.map (fun row -> row.Value)

                let shown =
                    if leading.Length = 0 then
                        shown
                    else
                        Array.append [| "", leading |] shown

                nodes.Add (List (shownPath prefix.Value path, shown, elided, stale ()))

                if isNull (box finish) then
                    Unchecked.defaultof<_>
                else
                    Native.nextOrNull finish
            | Some (Inspect.ArmProbe (path, selected, stale)) ->
                let struct (key, definition) = selected ()
                let children, after = regionChildren ()

                let definitionText =
                    if isNull (box definition) then
                        ""
                    else
                        let hash =
                            try
                                Template.structuralHash definition
                            with _ ->
                                "?"

                        rootName children + "#" + hash

                nodes.Add (
                    Arm (
                        shownPath prefix.Value path,
                        (if isNull key then "" else keyText options (firstOfPair key)),
                        definitionText,
                        children,
                        stale ()
                    )
                )

                after
            | Some (Inspect.BoundaryProbe (path, state)) ->
                let children, after = regionChildren ()

                let dump =
                    match state () with
                    | Inspect.Ready ->
                        {
                            State = "ready"
                            Waiting = 0
                            Reason = ""
                        }
                    | Inspect.Pending (waiting, revealed) ->
                        {
                            State = "pending"
                            Waiting = waiting
                            Reason = (if revealed then "revealed" else "")
                        }
                    | Inspect.Failed (error, _) ->
                        {
                            State = "failed"
                            Waiting = 0
                            Reason = (if isNull error then "" else error.Message)
                        }

                nodes.Add (Boundary (shownPath prefix.Value path, dump, children))
                after
            | Some (Inspect.HoleProbe path) ->
                let children, after = regionChildren ()
                nodes.Add (Hole (shownPath prefix.Value path, children))
                after
            | _ ->
                let shown = nodeOf options prefix cursor

                match shown with
                | Some dump ->
                    let dump =
                        if
                            leaving cursor
                            && not (leaving (Native.parentOrNull cursor))
                        then
                            match Inspect.rowKeyOf cursor with
                            | Some key -> LeavingRow (keyText options key, dump)
                            | None -> Leaving dump
                        else
                            dump

                    nodes.Add dump
                | None -> ()

                next

        let instances = Inspect.instancesOf cursor

        if instances.Length > 0 && nodes.Count > startIndex then
            let children = [| for i in startIndex .. nodes.Count - 1 -> nodes[i] |]
            nodes.RemoveRange (startIndex, nodes.Count - startIndex)

            let wrapped =
                Array.foldBack
                    (fun probe children ->
                        match probe with
                        | Inspect.InstanceProbe (definition, instance, _, path) ->
                            [|
                                Instance (instanceOf options prefix.Value definition instance path, children)
                            |]
                        | _ -> children)
                    instances
                    children

            nodes.AddRange wrapped

        cursor <- resume

    prefix.Value <- inheritedPrefix
    nodes.ToArray ()

/// <summary>The dump of one element or text node; None for a comment.</summary>
and private nodeOf (options: DumpOptions) (prefix: string ref) (node: Node) : DumpNode option =
    match kindOf node with
    | 1.0 ->
        let attributes = attributesOf node

        let properties =
            [|
                match valueOf node with
                | null -> ()
                | value ->
                    let attribute =
                        attributes
                        |> Array.tryFind (fun (name, _) -> name = "value")
                        |> Option.map snd

                    if
                        tagOf node = "input"
                        && attribute <> Some value
                        && not (attribute.IsNone && value = "")
                    then
                        yield "value", value

                match checkedOf node with
                | null -> ()
                | isChecked ->
                    let attribute =
                        attributes
                        |> Array.exists (fun (name, _) -> name = "checked")

                    if unbox<bool> isChecked <> attribute then
                        yield "checked", (if unbox<bool> isChecked then "true" else "false")
            |]

        let children = childrenOf node

        let first =
            if children.Length > 0 then
                children[0]
            else
                Unchecked.defaultof<_>

        Some (Element (tagOf node, attributes, properties, walk options prefix first Unchecked.defaultof<_>))
    | 3.0 -> Some (Text (dataOf node, Inspect.isBound node))
    | _ -> None

let private tracePaths (options: DumpOptions) (graph: Graph) =
#if RANVIER_TRACE
    if options.NodeIds then
        (Trace.snapshot graph).Nodes
        |> Map.toSeq
        |> Seq.map (fun (id, node) -> string id, node.Path)
        |> Map.ofSeq
    else
        Map.empty
#else
    Map.empty<string, string>
#endif

let private captureHead (options: DumpOptions) (root: Node) =
    if options.Head then
        [|
            match documentTitle root with
            | null
            | "" -> ()
            | title -> yield "title  key=title", quote options title

            for element in markedHead root ->
                let attributes = attributesOf element

                let key =
                    attributes
                    |> Array.tryFind (fun (name, _) -> name = "data-ranvier-head")
                    |> Option.map snd
                    |> Option.defaultValue ""

                let rest =
                    attributes
                    |> Array.filter (fun (name, _) -> name <> "data-ranvier-head")
                    |> Array.map (fun (name, value) -> name + "=" + quote options value)

                let text =
                    if tagOf element = "title" then
                        quote options (Native.textContentOf element)
                    else
                        String.Join (" ", rest)

                tagOf element + "  key=" + key, text
        |]
    else
        [||]

/// <summary>The dump of the nodes under <c>root</c>, with the probes registered on them.</summary>
let captureNode (options: DumpOptions) (graph: Graph) (root: Node) : Dump =
    if not graph.IsOnGraphThread then
        raise (InvalidOperationException "Debug.dump ran off the graph's thread")

    graph.Untrack (fun () ->
        let children = childrenOf root

        let first =
            if children.Length > 0 then
                children[0]
            else
                Unchecked.defaultof<_>

        {
            Mount = ""
            Options = options
            TracePaths = tracePaths options graph
            Root = walk options (ref "") first Unchecked.defaultof<_>
            Head = captureHead options root
        })

/// <summary>The dump of <c>mounted</c>; raises <c>InvalidOperationException</c> during a flush or off the graph's thread.</summary>
let capture (options: DumpOptions) (mounted: Mounting.Mounted) : Dump =
    let graph = mounted.Graph

    if not graph.IsOnGraphThread then
        raise (InvalidOperationException "Debug.dump ran off the graph's thread")

    let struct (first, last) = mounted.Range

    let header =
        match Inspect.probeOf first with
        | Some (Inspect.MountProbe (phase, queued, flushing, prefix, _)) ->
            if flushing () then
                raise (InvalidOperationException "Debug.dump ran inside a flush")

            let failures = Seq.length mounted.Failures

            prefix
            + "  phase="
            + string (phase ())
            + "  commits="
            + (if queued then "queued" else "synchronous")
            + "  failures="
            + string failures
        | _ -> "?"

    graph.Untrack (fun () ->
        let root = walk options (ref "") first last

        {
            Mount = header
            Options = options
            TracePaths = tracePaths options graph
            Root = root
            Head = captureHead options first
        })

let private fieldLine (options: DumpOptions) (paths: Map<string, string>) (field: FieldDump) =
    field.Name
    + "  "
    + field.Kind
    + (if field.Kind = field.Value then "" else "  " + field.Value)
    + (if options.NodeIds && field.Node <> "" then
           "  node="
           + field.Node
           + (paths
              |> Map.tryFind field.Node
              |> Option.map (fun path -> "  trace=" + path)
              |> Option.defaultValue "")
       else
           "")

let private elementLine (options: DumpOptions) (tag: string) (attributes: (string * string) array) (properties: (string * string) array) =
    let parts =
        [|
            for name, value in attributes -> " " + name + "=" + quote options value
            for name, value in properties ->
                " ."
                + name
                + "="
                + (if name = "checked" then value else quote options value)
        |]

    "<" + tag + String.Join ("", parts) + ">"

/// <summary>The text form: two spaces of indentation per level, the head section, then the <c>MaxChars</c> cut.</summary>
let textWith (options: DumpOptions) (dump: Dump) : string =
    let lines = ResizeArray<string>()

    let rec node (depth: int) (dumped: DumpNode) =
        let pad = String (' ', 2 * depth)

        match dumped with
        | Element (tag, attributes, properties, children) ->
            lines.Add (
                pad
                + elementLine options tag attributes properties
            )

            for child in children do
                node (depth + 1) child
        | Text (value, bound) ->
            lines.Add (
                pad
                + (if bound then "=" else "")
                + quote options value
            )
        | Instance (instance, children) ->
            lines.Add (
                pad
                + "instance "
                + instance.Name
                + (if instance.Path = "" then "" else "  path=" + instance.Path)
                + (if instance.Hash = "" then "" else "  hash=" + instance.Hash)
            )

            for field in instance.Fields do
                lines.Add (
                    pad
                    + "  "
                    + fieldLine options dump.TracePaths field
                )

            for child in children do
                node depth child
        | List (path, rows, elided, stale) ->
            let count =
                (rows
                 |> Array.filter (fun (key, _) -> key <> "")
                 |> Array.length)
                + (match elided with
                   | Some e -> e.Count
                   | None -> 0)

            lines.Add (
                pad
                + "list  path="
                + path
                + "  rows="
                + string count
                + (if stale then "  stale" else "")
            )

            let headRows =
                match elided with
                | Some _ -> rows.Length - min 3 (max 0 options.MaxRows)
                | None -> rows.Length

            let showElision () =
                match elided with
                | Some e ->
                    lines.Add (
                        pad
                        + "  … "
                        + string e.Count
                        + " rows (keys "
                        + e.FirstKey
                        + " … "
                        + e.LastKey
                        + ")"
                    )
                | None -> ()

            rows
            |> Array.iteri (fun index (key, children) ->
                if index = headRows then
                    showElision ()

                if key <> "" then
                    lines.Add (pad + "  row " + key)

                for child in children do
                    match child with
                    | Leaving _
                    | LeavingRow _ -> ()
                    | _ -> node (depth + 2) child

                for child in children do
                    match child with
                    | LeavingRow (key, inner) ->
                        lines.Add (pad + "  leaving  key=" + key)
                        node (depth + 2) inner
                    | Leaving inner ->
                        lines.Add (pad + "  leaving")
                        node (depth + 2) inner
                    | _ -> ())

            if headRows = rows.Length then
                showElision ()
        | Arm (path, key, definition, children, stale) ->
            lines.Add (
                pad
                + "arm  path="
                + path
                + (if key = "" then "" else "  key=" + key)
                + (if definition = "" then "" else "  def=" + definition)
                + (if stale then "  stale" else "")
            )

            for child in children do
                node (depth + 1) child
        | Boundary (path, state, children) ->
            lines.Add (
                pad
                + "boundary  path="
                + path
                + "  state="
                + state.State
                + "  view="
                + selectedView children
                + (if state.State = "pending" then
                       "  waiting=" + string state.Waiting
                   else
                       "")
                + (if state.Reason = "" then
                       ""
                   else
                       "  reason=" + quote options state.Reason)
            )

            for child in children do
                node (depth + 1) child
        | Hole (path, children) ->
            lines.Add (pad + "hole  path=" + path)

            for child in children do
                node (depth + 1) child
        | Leaving inner ->
            let before = lines.Count
            node depth inner

            if lines.Count > before then
                lines[before] <- lines[before] + "  leaving"
        | LeavingRow (key, inner) ->
            let before = lines.Count
            node depth inner

            if lines.Count > before then
                lines[before] <- lines[before] + "  leaving  key=" + key
        | Truncated count -> lines.Add (pad + "… " + string count + " lines omitted")

    let depth =
        if dump.Mount = "" then
            0
        else
            lines.Add ("mount " + dump.Mount)
            1

    for root in dump.Root do
        node depth root

    if options.Head && dump.Head.Length > 0 then
        lines.Add "head"

        for key, value in dump.Head do
            lines.Add ("  " + key + "  " + value)

    let output = Text.StringBuilder ()
    let mutable index = 0
    let mutable fits = true

    while fits && index < lines.Count do
        let line = lines[index]

        let needed =
            output.Length
            + line.Length
            + (if index > 0 then 1 else 0)

        if needed > options.MaxChars then
            fits <- false
        else
            if index > 0 then
                output.Append '\n' |> ignore

            output.Append line |> ignore
            index <- index + 1

    if index < lines.Count then
        if output.Length > 0 then
            output.Append '\n' |> ignore

        output.Append("… ").Append(string (lines.Count - index)).Append " lines omitted"
        |> ignore

    output.ToString ()

/// <summary>The text form under the options used to capture the dump.</summary>
let text (dump: Dump) : string =
    textWith dump.Options dump

let private jsonString (value: string) =
    let builder = Text.StringBuilder ("\"")

    for c in value do
        match c with
        | '"' -> builder.Append "\\\"" |> ignore
        | '\\' -> builder.Append "\\\\" |> ignore
        | '\n' -> builder.Append "\\n" |> ignore
        | '\r' -> builder.Append "\\r" |> ignore
        | '\t' -> builder.Append "\\t" |> ignore
        | c when c < ' ' ->
            builder.Append("\\u").Append((int c).ToString "x4")
            |> ignore
        | c -> builder.Append c |> ignore

    builder.Append('"').ToString()

let private jsonPairs (pairs: (string * string) array) =
    "["
    + String.Join (
        ",",
        pairs
        |> Array.map (fun (a, b) -> "[" + jsonString a + "," + jsonString b + "]")
    )
    + "]"

/// <summary>JSON with <c>"schema": "ranvier-view-dump/1"</c>; keys in a fixed order.</summary>
let json (dump: Dump) : string =
    let rec node (dumped: DumpNode) : string =
        let children (items: DumpNode array) =
            "["
            + String.Join (",", items |> Array.map node)
            + "]"

        match dumped with
        | Element (tag, attributes, properties, items) ->
            "{\"kind\":\"element\",\"tag\":"
            + jsonString tag
            + ",\"attributes\":"
            + jsonPairs attributes
            + ",\"properties\":"
            + jsonPairs properties
            + ",\"children\":"
            + children items
            + "}"
        | Text (value, bound) ->
            "{\"kind\":\"text\",\"value\":"
            + jsonString value
            + ",\"bound\":"
            + (if bound then "true" else "false")
            + "}"
        | Instance (instance, items) ->
            let fields =
                instance.Fields
                |> Array.map (fun field ->
                    "{\"name\":"
                    + jsonString field.Name
                    + ",\"kind\":"
                    + jsonString field.Kind
                    + ",\"value\":"
                    + jsonString field.Value
                    + ",\"status\":"
                    + jsonString field.Status
                    + (if dump.Options.NodeIds then
                           ",\"node\":" + jsonString field.Node
                       else
                           "")
                    + (if dump.Options.NodeIds then
                           dump.TracePaths
                           |> Map.tryFind field.Node
                           |> Option.map (fun path -> ",\"trace\":" + jsonString path)
                           |> Option.defaultValue ""
                       else
                           "")
                    + "}")

            "{\"kind\":\"instance\",\"name\":"
            + jsonString instance.Name
            + ",\"path\":"
            + jsonString instance.Path
            + ",\"hash\":"
            + jsonString instance.Hash
            + ",\"fields\":["
            + String.Join (",", fields)
            + "],\"children\":"
            + children items
            + "}"
        | List (path, rows, elided, stale) ->
            let rows =
                rows
                |> Array.map (fun (key, items) ->
                    "{\"key\":"
                    + jsonString key
                    + ",\"children\":"
                    + children items
                    + "}")

            let elided =
                match elided with
                | Some e ->
                    "{\"count\":"
                    + string e.Count
                    + ",\"firstKey\":"
                    + jsonString e.FirstKey
                    + ",\"lastKey\":"
                    + jsonString e.LastKey
                    + "}"
                | None -> "null"

            "{\"kind\":\"list\",\"path\":"
            + jsonString path
            + ",\"rows\":["
            + String.Join (",", rows)
            + "],\"elided\":"
            + elided
            + ",\"stale\":"
            + (if stale then "true" else "false")
            + "}"
        | Arm (path, key, definition, items, stale) ->
            "{\"kind\":\"arm\",\"path\":"
            + jsonString path
            + ",\"key\":"
            + jsonString key
            + ",\"definition\":"
            + jsonString definition
            + ",\"stale\":"
            + (if stale then "true" else "false")
            + ",\"children\":"
            + children items
            + "}"
        | Boundary (path, state, items) ->
            "{\"kind\":\"boundary\",\"path\":"
            + jsonString path
            + ",\"state\":"
            + jsonString state.State
            + ",\"waiting\":"
            + string state.Waiting
            + ",\"reason\":"
            + jsonString state.Reason
            + ",\"children\":"
            + children items
            + "}"
        | Hole (path, items) ->
            "{\"kind\":\"hole\",\"path\":"
            + jsonString path
            + ",\"children\":"
            + children items
            + "}"
        | Leaving inner ->
            "{\"kind\":\"leaving\",\"node\":"
            + node inner
            + "}"
        | LeavingRow (key, inner) ->
            "{\"kind\":\"leaving\",\"key\":"
            + jsonString key
            + ",\"node\":"
            + node inner
            + "}"
        | Truncated count ->
            "{\"kind\":\"truncated\",\"lines\":"
            + string count
            + "}"

    let mount =
        if dump.Mount = "" then
            "null"
        else
            let parts = dump.Mount.Split ([| "  " |], StringSplitOptions.None)

            let value key =
                parts
                |> Array.tryFind (fun p -> p.StartsWith (key + "="))
                |> Option.map (fun p -> p.Substring (key.Length + 1))
                |> Option.defaultValue ""

            let failures =
                match Int32.TryParse (value "failures") with
                | true, n -> n
                | _ -> 0

            "{\"prefix\":"
            + jsonString parts[0]
            + ",\"phase\":"
            + jsonString (value "phase")
            + ",\"commits\":"
            + jsonString (value "commits")
            + ",\"failures\":"
            + string failures
            + "}"

    "{\"schema\":\"ranvier-view-dump/1\",\"mount\":"
    + mount
    + ",\"root\":["
    + String.Join (",", dump.Root |> Array.map node)
    + "],\"head\":"
    + jsonPairs dump.Head
    + "}"

/// <summary>The text of <c>capture DumpOptions.defaults mounted</c>.</summary>
let dump (mounted: Mounting.Mounted) : string =
    text (capture DumpOptions.defaults mounted)

/// <summary>The core node held by the field at <c>path</c>: an instance path, a <c>.</c>, and the field's name.</summary>
let nodeAt (mounted: Mounting.Mounted, path: string) : INode option =
    let dot = path.LastIndexOf '.'

    if dot < 0 then
        None
    else
        let instancePath, fieldName = path.Substring (0, dot), path.Substring (dot + 1)
        let struct (first, last) = mounted.Range

        let mountPrefix =
            match Inspect.probeOf first with
            | Some (Inspect.MountProbe (_, _, _, prefix, _)) -> prefix
            | _ -> ""

        let rec find (node: Node) : INode option =
            if
                isNull (box node)
                || obj.ReferenceEquals (node, last)
            then
                None
            else
                let here =
                    Inspect.instancesOf node
                    |> Array.tryPick (function
                        | Inspect.InstanceProbe (_, instance, _, instancePathValue) when
                            not (isNull (box instancePathValue))
                            && (mountPrefix + "-" + instancePathValue.Text) = instancePath
                            ->
                            match ownFields instance with
                            | null -> None
                            | fields ->
                                fields
                                |> Array.tryFind (fun (name, value) -> name = fieldName && isNode value)
                                |> Option.map (fun (_, value) -> unbox<INode> value)
                        | _ -> None)

                match here with
                | Some _ -> here
                | None ->
                    let inner =
                        if kindOf node = 1.0 then
                            find (Native.firstChildOf node)
                        else
                            None

                    match inner with
                    | Some _ -> inner
                    | None -> find (Native.nextOrNull node)

        find (Native.nextOrNull first)

/// <summary>
/// Up to 40 lines from the nearest enclosing instance, or the nodes under <c>root</c>, after a line break.
/// Returns empty outside a registered mount, off its graph thread, or during a flush.
/// </summary>
let excerpt (root: Node) : string =
    let graphAt (node: Node) =
        match Inspect.probeOf node with
        | Some (Inspect.MountProbe (_, _, _, _, graph)) -> graph
        | _ -> Unchecked.defaultof<_>

    let rec under (node: Node) : Graph =
        let mutable found = graphAt node

        let mutable child =
            if
                kindOf node = 1.0
                || kindOf node = 9.0
                || kindOf node = 11.0
            then
                Native.firstChildOf node
            else
                Unchecked.defaultof<_>

        while isNull (box found) && not (isNull (box child)) do
            found <- under child
            child <- Native.nextOrNull child

        found

    let rec around (node: Node) : Graph =
        if isNull (box node) then
            Unchecked.defaultof<_>
        else
            let mutable found: Graph = Unchecked.defaultof<_>
            let mutable child = Native.firstChildOf node

            while isNull (box found) && not (isNull (box child)) do
                found <- graphAt child
                child <- Native.nextOrNull child

            if isNull (box found) then
                around (Native.parentOrNull node)
            else
                found

    let graph =
        let found = under root

        if isNull (box found) then
            around (Native.parentOrNull root)
        else
            found

    if isNull (box graph) || not graph.IsOnGraphThread then
        ""
    else
        try
            let rec prefixAt (node: Node) =
                let parent = Native.parentOrNull node

                if isNull (box parent) then
                    ""
                else
                    let mutable cursor = Native.firstChildOf parent
                    let mutable mounts = []

                    while not (isNull (box cursor))
                          && not (obj.ReferenceEquals (cursor, node)) do
                        match Inspect.probeOf cursor with
                        | Some (Inspect.MountProbe (_, _, _, prefix, _) as mount) ->
                            checkFlushing mount
                            mounts <- prefix :: mounts
                        | _ when isComment cursor && dataOf cursor = "ranvier:end" ->
                            mounts <-
                                match mounts with
                                | _ :: rest -> rest
                                | [] -> []
                        | _ -> ()

                        cursor <- Native.nextOrNull cursor

                    match mounts with
                    | prefix :: _ -> prefix
                    | [] -> prefixAt parent

            let options =
                { DumpOptions.defaults with
                    Head = false
                }

            let captured =
                match Inspect.enclosingInstanceOf root with
                | None -> captureNode options graph root
                | Some (instance, finish) ->
                    graph.Untrack (fun () ->
                        {
                            Mount = ""
                            Root = walk options (ref (prefixAt instance)) instance finish
                            Head = [||]
                            Options = options
                            TracePaths = Map.empty
                        })

            let lines = (text captured).Split '\n'

            "\n" + String.Join ("\n", Array.truncate 40 lines)
        with :? InvalidOperationException ->
            ""
#endif
