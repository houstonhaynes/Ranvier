[<AutoOpen>]
module Ranvier.View.Representation

type Tag =
    | Div
    | Button
    | Input
    | Select
    | Option
    | HtmlTag of string

type BrowserPolicy =
    | Reject
    | Omit

type Setup =
    {
        OnCleanup: (unit -> unit) -> unit
        /// <summary>A new id for the instance whose setup receives this <c>Setup</c>.</summary>
        /// <remarks>
        /// <para>
        /// A compiled template rendered on the server or mounted with <c>SynchronousCommits</c> receives positional
        /// ids: the root prefix, the instance's positional path and a counter within the instance, as in
        /// <c>r-0.c1.r0.3-0</c>. Positional ids match between the server and a client mount with
        /// <c>SynchronousCommits</c> for the same compiled template structure.
        /// </para>
        /// <para>
        /// An authored component, and a compiled template mounted with <c>QueuedCommits</c>, receive authored ids,
        /// <c>prefix-path-an</c> with <c>n</c> counting across the render or mount; these can differ between server
        /// and client.
        /// </para>
        /// <para>
        /// A positional <c>CreateId</c> raises <c>InvalidOperationException</c> when it is called outside its own
        /// instance's setup: after that setup returns, or during another instance's setup. An authored
        /// <c>CreateId</c> returns an id whenever it is called.
        /// </para>
        /// </remarks>
        CreateId: unit -> string
    }

/// <summary>
/// <c>setup</c> with a <c>CreateId</c> that issues positional ids in the window open in <c>identity</c>, and raises
/// once another window is open or none is.
/// </summary>
let ownSetup (identity: IdentityScope) (setup: Setup) : Setup =
    let owner = identity.Window

    { setup with
        CreateId = fun () -> identity.CreateIdIn owner
    }

/// <summary>The items of a keyed region, with the key of each item; list items keep the caller's references.</summary>
[<NoEquality; NoComparison>]
type KeyedItems =
    {
        Read: unit -> obj array
        Key: System.Func<obj, obj>
    }

/// <summary>Constructors and accessors of <c>KeyedItems</c>.</summary>
module KeyedItems =
    /// <summary>The items as an <c>obj</c> array; an item of a reference type keeps its reference, and Fable returns the array itself.</summary>
    let inline internal boxAll (items: 'item array) : obj array =
#if FABLE_COMPILER
        unbox items
#else
        Array.map box items
#endif

    /// <summary>The key of a boxed <c>(key, value)</c> pair.</summary>
    let internal pairKey =
        System.Func<obj, obj>(fun pair -> fst (unbox<obj * obj> pair))

    /// <summary>Items that are boxed <c>(key, value)</c> pairs, keyed by the pair's key.</summary>
    let ofPairs (read: unit -> (obj * obj) array) =
        {
            Read = fun () -> boxAll (read ())
            Key = pairKey
        }

    /// <summary>The value of a boxed <c>(key, value)</c> pair.</summary>
    let value (pair: obj) =
        snd (unbox<obj * obj> pair)

/// <summary>The failure an error view receives.</summary>
[<NoEquality; NoComparison>]
type BoundaryFailure =
    {
        /// <summary>The first exception of the failure that replaced the boundary's content.</summary>
        Error: exn
        /// <summary>Disposes the error view and mounts the content again, with a new setup and a new pending phase.</summary>
        Retry: unit -> unit
    }

/// <summary>The state of a boundary that its content reads.</summary>
[<NoEquality; NoComparison>]
type BoundaryStatus =
    {
        /// <summary>A tracked reader: true after the first reveal while a binding under the boundary waits for a pending read.</summary>
        IsPending: unit -> bool
    }

/// <summary>What a placed hook does with its element.</summary>
[<RequireQualifiedAccess>]
type PlacedKind =
    /// <summary>Runs code against the element.</summary>
    | Mounted
    /// <summary>Borrows the element: adds listeners, attributes and properties beyond its declared parts, and removes them in its cleanup.</summary>
    | Lent
    /// <summary>Owns the element's content.</summary>
    | Widget
    /// <summary>Runs code against the element with the element's placement context.</summary>
    | Motion
    /// <summary>Holds a retired row or arm root element in place until the hook finishes.</summary>
    | Exit
    /// <summary>Measures the element before and after the region passes under it.</summary>
    | Reflow

/// <summary>
/// The part name of a placed hook in a browser-capability error; null for a motion, exit or reflow hook; the server omits
/// these under every policy.
/// </summary>
let internal placedPart kind =
    match kind with
    | PlacedKind.Mounted -> "mounted hook"
    | PlacedKind.Lent -> "lent hook"
    | PlacedKind.Widget -> "widget"
    | _ -> null

/// <summary>The browser-capability error under <c>Reject</c> for <c>part</c> of an element <c>tag</c>.</summary>
let internal browserRejected (tag: string) (part: string) =
    System.InvalidOperationException (
        "Browser capability requires explicit Omit policy: <"
        + tag
        + "> "
        + part
    )

/// <summary>
/// The element of a placed instance: <c>Some</c> while the element is placed, <c>None</c> during setup, before its
/// boundary reveals it and after disposal.
/// </summary>
/// <remarks>Reading <c>Current</c> is untracked.</remarks>
[<Sealed>]
type NodeRef<'native> internal () =
    let mutable current: 'native option = None
    member _.Current = current

    member internal _.Set(value: 'native option) =
        current <- value

    /// <summary>
    /// Sets the reference to <c>node</c> and returns the cleanup that resets it to <c>None</c> while it still holds
    /// <c>node</c>; a reference shared by several elements keeps the element placed latest.
    /// </summary>
    member internal this.Place(node: obj) : unit -> unit =
        this.Set (Some (unbox node))

        fun () ->
            match current with
            | Some held when obj.ReferenceEquals (box held, node) -> this.Set None
            | _ -> ()

module NodeRef =
    let create () : NodeRef<'native> =
        NodeRef<'native>()

/// <summary>The placement of an element with a motion hook.</summary>
[<NoEquality; NoComparison>]
type MotionContext =
    {
        /// <summary>Whether the element was adopted from server HTML.</summary>
        Hydrated: bool
        /// <summary>Whether the element's row or arm was created by a region pass after the region's first.</summary>
        Inserted: bool
        /// <summary>
        /// A reader of whether the element or an ancestor has an exit hook that has not started. Read in the motion hook's
        /// cleanup, true means the element stays in place for that exit once the instance is disposed, unless the mount
        /// tears down.
        /// </summary>
        ExitPending: unit -> bool
    }

/// <summary>The measurements a reflow hook takes around the region passes under its element.</summary>
[<NoEquality; NoComparison>]
type Reflow =
    {
        /// <summary>Runs before the first region pass under the element.</summary>
        Before: unit -> unit
        /// <summary>Runs once the passes' nodes and commits are in place.</summary>
        After: unit -> unit
    }

/// <summary>The element a document head entry stands for.</summary>
[<RequireQualifiedAccess>]
type HeadKind =
    | Title
    | Meta
    | Link

/// <summary>A document head entry bound to one instance.</summary>
[<NoEquality; NoComparison>]
type HeadEntry =
    {
        Kind: HeadKind
        /// <summary>The entry's key; null for an unkeyed link.</summary>
        Key: string
        Attributes: (string * string) array
        /// <summary>The entry's dynamic attributes; <c>None</c> removes one.</summary>
        Readers: (string * (unit -> string option)) array
        /// <summary>The reader of a title entry; null for a meta or link entry.</summary>
        Title: unit -> string
    }

let private validNameCharacters underscore (value: string) =
    let mutable valid = true
    let mutable index = 0

    while valid && index < value.Length do
        let c = value[index]

        valid <-
            (c >= 'a' && c <= 'z')
            || (c >= '0' && c <= '9')
            || c = '-'
            || (underscore && c = '_')

        index <- index + 1

    valid

/// <summary>
/// The case-sensitive SVG tag and attribute names accepted besides lowercase names, as the HTML parser's SVG adjustment
/// tables restore them. On .NET every name is accepted; under Fable, the module of the SVG sugar tags sets them, so a
/// bundle without an SVG tag accepts lowercase names only.
/// </summary>
[<Sealed>]
type internal SvgNames() =
#if FABLE_COMPILER
    /// <summary>A JS Set of the names; null until the SVG sugar tags' module loads.</summary>
    member val Names: obj = null with get, set
#else
    member val Names =
        System.Collections.Generic.HashSet<string>(
            "altGlyph altGlyphDef altGlyphItem animateColor animateMotion animateTransform clipPath feBlend feColorMatrix feComponentTransfer feComposite feConvolveMatrix feDiffuseLighting feDisplacementMap feDistantLight feDropShadow feFlood feFuncA feFuncB feFuncG feFuncR feGaussianBlur feImage feMerge feMergeNode feMorphology feOffset fePointLight feSpecularLighting feSpotLight feTile feTurbulence foreignObject glyphRef linearGradient radialGradient textPath attributeName attributeType baseFrequency baseProfile calcMode clipPathUnits diffuseConstant edgeMode filterUnits gradientTransform gradientUnits kernelMatrix kernelUnitLength keyPoints keySplines keyTimes lengthAdjust limitingConeAngle markerHeight markerUnits markerWidth maskContentUnits maskUnits numOctaves pathLength patternContentUnits patternTransform patternUnits pointsAtX pointsAtY pointsAtZ preserveAlpha preserveAspectRatio primitiveUnits refX refY repeatCount repeatDur requiredExtensions requiredFeatures specularConstant specularExponent spreadMethod startOffset stdDeviation stitchTiles surfaceScale systemLanguage tableValues targetX targetY textLength viewBox viewTarget xChannelSelector yChannelSelector zoomAndPan"
                .Split
                ' '
        ) with get, set
#endif

let internal svgNames = SvgNames ()

#if FABLE_COMPILER
[<Fable.Core.Emit("($0 != null && $0.has($1))")>]
let private has (names: obj) (value: string) : bool =
    Fable.Core.Util.jsNative

// Whether the value is exactly one listed name: ASCII letters only, then a member of the set.
let private listed (value: string) =
    validNameCharacters false (value.ToLowerInvariant ())
    && has svgNames.Names value
#else
let private listed (value: string) =
    validNameCharacters false (value.ToLowerInvariant ())
    && svgNames.Names.Contains value
#endif

let name =
    function
    | Div -> "div"
    | Button -> "button"
    | Input -> "input"
    | Select -> "select"
    | Option -> "option"
    | HtmlTag value ->
        if
            System.String.IsNullOrEmpty value
            || value[0] < 'a'
            || value[0] > 'z'
            || not (validNameCharacters false value || listed value)
        then
            invalidArg "tag" "Tag names must contain lowercase letters, digits or hyphens, or be a case-sensitive SVG name"

        value

/// <summary>The failure of a script element, which no client mount path creates, in either namespace.</summary>
let internal scriptRefused =
    "Script elements are not created by templates or typed views; load scripts through the server document shell"

/// <summary>Raises for a script element name, matched case-insensitively.</summary>
let refuseScript (tagName: string) =
    if tagName.ToLowerInvariant () = "script" then
        raise (System.InvalidOperationException scriptRefused)

let isVoid tag =
    match (name tag).ToLowerInvariant() with
    | "area"
    | "base"
    | "br"
    | "col"
    | "embed"
    | "hr"
    | "img"
    | "input"
    | "link"
    | "meta"
    | "param"
    | "source"
    | "track"
    | "wbr" -> true
    | _ -> false

let validateAttribute (name: string) =
    if System.String.IsNullOrEmpty name then
        raise (System.ArgumentException ("Attribute names must not be empty", "name"))

    let lowered = name.ToLowerInvariant ()

    if
        name[0] < 'a'
        || name[0] > 'z'
        || lowered.StartsWith ("on", System.StringComparison.Ordinal)
        || lowered = "srcdoc"
        || not (validNameCharacters true name || listed name)
    then
        raise (
            System.ArgumentException (
                "Attribute names must be lowercase or a case-sensitive SVG name, and cannot contain event handlers or raw document content",
                "name"
            )
        )

/// <summary>The entity of an escaped character: one of <c>&amp; &lt; &gt; \r "</c>.</summary>
let private entity (c: char) =
    match c with
    | '&' -> "&amp;"
    | '<' -> "&lt;"
    | '>' -> "&gt;"
    | '\r' -> "&#13;"
    | _ -> "&quot;"

#if !FABLE_COMPILER
let private textEscapes: System.Buffers.SearchValues<char> =
    System.Buffers.SearchValues.Create (System.MemoryExtensions.AsSpan "&<>\r")

let private attributeEscapes: System.Buffers.SearchValues<char> =
    System.Buffers.SearchValues.Create (System.MemoryExtensions.AsSpan "&<>\r\"")
#endif

let private escapeText (attribute: bool) (value: string) : string =
#if FABLE_COMPILER
    let escapes c =
        c = '&'
        || c = '<'
        || c = '>'
        || c = '\r'
        || (attribute && c = '"')

    let mutable first = -1
    let mutable index = 0

    while first < 0 && index < value.Length do
        if escapes value[index] then
            first <- index

        index <- index + 1

    if first < 0 then
        value
    else
        let parts = ResizeArray<string>()
        let mutable start = 0

        for index in first .. value.Length - 1 do
            let c = value[index]

            if escapes c then
                if index > start then
                    parts.Add (value.Substring (start, index - start))

                parts.Add (entity c)
                start <- index + 1

        if start < value.Length then
            parts.Add (value.Substring start)

        System.String.Join ("", parts)
#else
    let search = if attribute then attributeEscapes else textEscapes
    let span = System.MemoryExtensions.AsSpan value
    let mutable hit = System.MemoryExtensions.IndexOfAny (span, search)

    if hit < 0 then
        value
    else
        let builder = System.Text.StringBuilder (value.Length + 16)
        let mutable start = 0

        while hit >= 0 do
            builder.Append(span.Slice (start, hit)).Append(entity span[start + hit])
            |> ignore

            start <- start + hit + 1
            hit <- System.MemoryExtensions.IndexOfAny (span.Slice start, search)

        builder.Append(span.Slice start).ToString()
#endif

/// <summary>
/// Escapes <c>&amp; &lt; &gt; \r</c>, and <c>"</c> when <c>attribute</c> is true; a value free of these characters is
/// returned as the same string, and null is escaped as the empty string.
/// </summary>
let internal escapeHtml (attribute: bool) (value: string) : string =
    if isNull value then "" else escapeText attribute value

/// <summary>An HTML buffer that can be truncated back to a mark.</summary>
[<Sealed>]
type HtmlSink() =
#if FABLE_COMPILER
    let mutable text = ""

    member _.Append(value: string) =
        text <- text + value

    member _.Mark() =
        text.Length

    /// <summary>Drops everything appended after <c>mark</c>.</summary>
    member _.Truncate(mark: int) =
        text <- text.Substring (0, mark)

    /// <summary>Writes the buffer in order, as one segment under Fable.</summary>
    member _.Segments(write: string -> unit) =
        if text.Length > 0 then
            write text

    override _.ToString() = text
#else
    let builder = System.Text.StringBuilder ()

    member _.Append(value: string) =
        builder.Append value |> ignore

    member _.Mark() =
        builder.Length

    /// <summary>Drops everything appended after <c>mark</c>.</summary>
    member _.Truncate(mark: int) =
        builder.Length <- mark

    /// <summary>Writes the buffer in order.</summary>
    member _.Segments(write: string -> unit) =
        for chunk in builder.GetChunks () do
            write (chunk.ToString ())

    override _.ToString() =
        builder.ToString ()
#endif

/// <summary>The server HTML of document head entries, marked with <c>data-ranvier-head</c> for hydration.</summary>
module internal HeadMarkup =
    /// <summary>
    /// The start tag of an entry through its static attributes, led by <c>data-ranvier-head="key"</c>: closed for a
    /// title, open for a meta or link. A null key writes an empty mark.
    /// </summary>
    let opening (kind: HeadKind) (key: string) (attributes: (string * string) array) : string =
        let tag =
            match kind with
            | HeadKind.Title -> "title"
            | HeadKind.Meta -> "meta"
            | HeadKind.Link -> "link"

        let parts = ResizeArray<string>()

        parts.Add (
            "<"
            + tag
            + " data-ranvier-head=\""
            + escapeHtml true key
            + "\""
        )

        for name, value in attributes do
            validateAttribute name
            parts.Add (" " + name + "=\"" + escapeHtml true value + "\"")

        if kind = HeadKind.Title then
            parts.Add ">"

        System.String.Join ("", parts)

    /// <summary><c> name="value"</c> for <c>Some</c>, and the empty string for <c>None</c>.</summary>
    let attribute (name: string) (value: string option) : string =
        match value with
        | Some value -> " " + name + "=\"" + escapeHtml true value + "\""
        | None -> ""

    /// <summary>A title element from its opening and its text; null text is written as empty.</summary>
    let title (opening: string) (text: string) : string =
        opening + escapeHtml false text + "</title>"

    /// <summary>The HTML of an entry bound to its instance.</summary>
    let entry (entry: HeadEntry) : string =
        let opening = opening entry.Kind entry.Key entry.Attributes

        if entry.Kind = HeadKind.Title then
            title opening (entry.Title ())
        else
            let parts = ResizeArray<string>()
            parts.Add opening

            for name, read in entry.Readers do
                validateAttribute name
                parts.Add (attribute name (read ()))

            parts.Add ">"
            System.String.Join ("", parts)

/// <summary>
/// The server counterpart of a client placement container (the request's root, a region, or a view's keyed, switch or
/// boundary content) with at least one entry in it or in a descendant.
/// </summary>
[<Sealed; AllowNullLiteral>]
type internal HeadContainer(parent: HeadContainer) =
    let mutable completed = 0
    let mutable path: int array = null

    /// <summary>The position among the parent's child containers in the order they closed; -1 while open.</summary>
    member val Position = -1 with get, set

    /// <summary>Gives <c>child</c> the next position among this container's closed children.</summary>
    member _.Close(child: HeadContainer) =
        child.Position <- completed
        completed <- completed + 1

    /// <summary>Whether this container and every ancestor except the root have closed.</summary>
    member this.Final: bool =
        isNull parent
        || (this.Position >= 0 && parent.Final)

    /// <summary>The positions from the root's child down to this container; empty for the root.</summary>
    member this.Path: int array =
        if not (isNull path) then
            path
        elif isNull parent then
            path <- [||]
            path
        else
            // An open container closes after every closed sibling.
            let position =
                if this.Position < 0 then
                    System.Int32.MaxValue
                else
                    this.Position

            let computed = Array.append parent.Path [| position |]

            if this.Final then
                path <- computed

            computed

/// <summary>
/// The document head entries written by one request, as a log of keyed markup, written in the client's activation
/// order. A container's own entries come first, ordered by their instance group and then as logged; the entries of its
/// child containers follow, ordered by the position at which each child closed. Per key, the last activated entry is
/// written at the position of the key's first activated entry; unkeyed entries are each written at their own position.
/// </summary>
/// <remarks>Until its first entry, a container or a group holds just its slots in the open-container stack.</remarks>
[<Sealed; AllowNullLiteral>]
type internal HeadCollector() =
    let keys = ResizeArray<string>()
    let markup = ResizeArray<string>()
    let owners = ResizeArray<HeadContainer>()
    let groups = ResizeArray<int>()
    // The open containers, innermost last: each one's container (null until its first entry), current group and
    // number of groups entered.
    let containers = ResizeArray<HeadContainer>([ null ])
    let currentGroups = ResizeArray<int>([ 0 ])
    let entered = ResizeArray<int>([ 0 ])

    let current () =
        let top = containers.Count - 1

        if isNull containers[top] then
            for index in 0..top do
                if isNull containers[index] then
                    containers[index] <- HeadContainer (if index = 0 then null else containers[index - 1])

        containers[top]

    let activation (left: int) (right: int) =
        let leftPath = owners[left].Path
        let rightPath = owners[right].Path
        let shared = min leftPath.Length rightPath.Length
        let mutable order = 0
        let mutable index = 0

        while order = 0 && index < shared do
            order <- compare leftPath[index] rightPath[index]
            index <- index + 1

        if order <> 0 then
            order
        elif leftPath.Length <> rightPath.Length then
            compare leftPath.Length rightPath.Length
        elif groups[left] <> groups[right] then
            compare groups[left] groups[right]
        else
            compare left right

    member _.Count = keys.Count

    /// <summary>Logs an entry of the current group of the innermost open container; a null key marks it unkeyed.</summary>
    member _.Add(key: string, html: string) =
        keys.Add key
        markup.Add html
        owners.Add (current ())
        groups.Add currentGroups[currentGroups.Count - 1]

    /// <summary>Opens a child container of the innermost open container.</summary>
    member _.Open() =
        containers.Add null
        currentGroups.Add 0
        entered.Add 0

    /// <summary>Closes the innermost open container, placing its entries after those of its closed siblings.</summary>
    member _.Close() =
        let top = containers.Count - 1
        let closing = containers[top]

        if not (isNull closing) then
            containers[top - 1].Close closing

        containers.RemoveAt top
        currentGroups.RemoveAt top
        entered.RemoveAt top

    /// <summary>
    /// Starts the group of an instance in the innermost open container: its entries activate after those of the groups
    /// entered before it. Returns the group to pass to <c>LeaveGroup</c>.
    /// </summary>
    member _.EnterGroup() : int =
        let top = containers.Count - 1
        let previous = currentGroups[top]
        entered[top] <- entered[top] + 1
        currentGroups[top] <- entered[top]
        previous

    /// <summary>Ends an instance's group: later entries join <c>previous</c>, the group returned by its <c>EnterGroup</c>.</summary>
    member _.LeaveGroup(previous: int) =
        currentGroups[currentGroups.Count - 1] <- previous

    /// <summary>
    /// Keeps the first <c>count</c> entries, so <c>ToString</c> writes the head of those entries alone. Raises
    /// <c>ArgumentException</c> when <c>count</c> lies outside zero to <c>Count</c>.
    /// </summary>
    member _.Truncate(count: int) =
        if count < 0 || count > keys.Count then
            invalidArg "count" "The count must lie between zero and the number of entries"

        keys.RemoveRange (count, keys.Count - count)
        markup.RemoveRange (count, markup.Count - count)
        owners.RemoveRange (count, owners.Count - count)
        groups.RemoveRange (count, groups.Count - count)

    /// <summary>The head HTML of the logged entries, concatenated without separators.</summary>
    override _.ToString() =
        let written = ResizeArray<string>()
        let positions = System.Collections.Generic.Dictionary<string, int>()
        let order = Array.init keys.Count id
        Array.sortInPlaceWith activation order

        for index in order do
            let key = keys[index]

            if isNull key then
                written.Add markup[index]
            else
                match positions.TryGetValue key with
                | true, position -> written[position] <- markup[index]
                | _ ->
                    positions[key] <- written.Count
                    written.Add markup[index]

        System.String.Join ("", written)

/// <summary>The operations of a server scope used by authored content on an <c>HtmlBackend</c>.</summary>
[<AbstractClass>]
type internal ServerScopeBase() =
    /// <summary>Runs the body in a new nested scope; when it raises, that scope's cleanups run before the exception propagates.</summary>
    abstract AttemptWith: (ServerScopeBase -> unit) -> unit

    /// <summary>
    /// Takes the cleanup failures of an authored component that ran under the scope and returns true, or returns false
    /// and leaves them to the component.
    /// </summary>
    abstract TakeCleanupFailures: exn list -> bool

[<AbstractClass>]
type Backend() =
    abstract Text: string -> unit
    abstract Element: Tag * (Backend -> unit) array * (unit -> unit) -> unit
    /// <summary>Creates an element whose children run against this backend in order.</summary>
    abstract ElementWith: Tag * (Backend -> unit) array * (Backend -> unit) array -> unit

    default this.ElementWith(tag, modifiers, children) =
        this.Element (
            tag,
            modifiers,
            fun () ->
                for child in children do
                    child this
        )

    /// <summary>Sets an attribute on the element currently receiving modifiers.</summary>
    abstract Attribute: string * string -> unit
    /// <summary>Sets or removes an attribute on the element currently receiving modifiers.</summary>
    abstract OptionalAttribute: string * string option -> unit
    /// <summary>Binds an attribute reader to the element currently receiving modifiers.</summary>
    abstract ReactiveAttribute: string * (unit -> string option) -> unit
    /// <summary>Sets the live input value of the element currently receiving modifiers.</summary>
    abstract InputValue: string -> unit
    /// <summary>Acquires browser resources for the element currently receiving modifiers.</summary>
    abstract Browser: (obj -> (unit -> unit)) -> unit

    /// <summary>
    /// Registers a hook for the element currently receiving modifiers, run once the element is placed in the mount's
    /// host. The hook receives the element and the flags of its <c>MotionContext</c> (1 hydrated, 2 inserted), and returns
    /// its cleanup, which runs when the element's owner is disposed.
    /// </summary>
    abstract Placed: PlacedKind * System.Func<obj, obj, obj> -> unit

    default _.Placed(_, _) =
        raise (System.NotSupportedException "Placed hooks require a DOM mount")

    /// <summary>Adds a document head entry for the current instance: shown once the instance is placed, withdrawn when it is disposed.</summary>
    abstract Head: HeadEntry -> unit

    default _.Head _ =
        raise (System.NotSupportedException "Document entries require a DOM mount")

    /// <summary>Handles a named DOM event on the element currently receiving modifiers.</summary>
    /// <remarks>The handler receives the event object. A backend may dispatch it through a listener on an ancestor.</remarks>
    abstract Listen: string * (obj -> unit) -> unit
    /// <summary>Applies a static property writer to the element currently receiving modifiers.</summary>
    abstract Property: (obj -> unit) -> unit
    /// <summary>Binds a property reader and target-aware writer to the element currently receiving modifiers.</summary>
    abstract ReactiveProperty: (unit -> obj) * System.Func<obj, obj, unit> -> unit
    abstract Bind: (string -> string -> bool) * (unit -> string) -> unit
    /// <summary>Interprets a keyed item snapshot and renders its items into this backend; a row's reader returns its current item.</summary>
    abstract Keyed: KeyedItems * ((unit -> obj) -> Backend -> unit) -> unit

    /// <summary>Renders the single keyed row selected by a switch; the previous row is disposed before the next is created.</summary>
    abstract Switch: (unit -> (obj * obj) array) * ((unit -> obj) -> Backend -> unit) -> unit

    default this.Switch(read, render) =
        this.Keyed (KeyedItems.ofPairs read, (fun current -> render (fun () -> KeyedItems.value (current ()))))

    abstract Component: (Setup -> unit) -> unit

    /// <summary>Renders a template component definition with its props.</summary>
    abstract Template: TemplateDefinition * obj -> unit

    default this.Template(definition, props) =
        definition.Expand (this, props)

    /// <summary>Renders content under a boundary with its pending and error views; the default renders the content only.</summary>
    abstract Boundary: BoundaryViews -> unit

    default this.Boundary(views) =
        views.Content { IsPending = fun () -> false } this

    /// <summary>
    /// Renders the view of a template's view hole. Its hooks and head entries activate after those of the enclosing
    /// instance and its inlined children, as a region's rows do.
    /// </summary>
    abstract Hole: (Backend -> unit) -> unit

    default this.Hole(render) =
        render this

/// <summary>A component definition whose template is built once and compiled lazily per backend.</summary>
and [<AbstractClass>] TemplateDefinition internal () =
    /// <summary>Renders the definition through ordinary backend operations.</summary>
    abstract Expand: Backend * obj -> unit

    /// <summary>Writes an instance's HTML through the backend, under the backend's active server scope.</summary>
    abstract RenderHtml: HtmlBackend * obj -> unit

    /// <summary>The DOM backend's compiled form, or null before the first DOM render.</summary>
    member val Compiled: obj = null with get, set

/// <summary>The three views of a boundary, each rendered through the backend it receives.</summary>
and [<NoEquality; NoComparison>] BoundaryViews =
    {
        Pending: Backend -> unit
        Failed: BoundaryFailure -> Backend -> unit
        Content: BoundaryStatus -> Backend -> unit
    }

/// <summary>
/// The server backend: writes HTML to <c>sink</c>. Under <c>Reject</c> a browser-only part raises; under <c>Omit</c> it
/// is dropped.
/// </summary>
and [<Sealed>] HtmlBackend(policy: BrowserPolicy, sink: HtmlSink) =
    inherit Backend()
    let mutable leadingLineFeed = false
    // Whether the element receiving modifiers has a widget hook.
    let mutable widget = false
    // Advances on each child node a DOM mount would create: text, binding, element, region or template.
    let mutable childNodes = 0

    let mutable attributeTarget: System.Collections.Generic.Dictionary<string, string> =
        Unchecked.defaultof<_>

    let mutable attributeKeys: ResizeArray<string> = Unchecked.defaultof<_>
    let mutable attributeTag = Div
    // Allocated on the first read of `Identity` unless set first.
    let mutable identity: IdentityScope = Unchecked.defaultof<_>

    let ensureAttributes () =
        if isNull (box attributeTarget) then
            invalidOp "Element modifiers require an active element"

    let set key value =
        ensureAttributes ()

        if not (attributeKeys.Contains key) then
            attributeKeys.Add key

        attributeTarget[key] <- value

    let setOptional key value =
        ensureAttributes ()
        validateAttribute key

        match value with
        | Some value -> set key value
        | None ->
            attributeTarget.Remove key |> ignore
            attributeKeys.Remove key |> ignore

    let applyModifiers (backend: Backend) tag attributes keys (modifiers: (Backend -> unit) array) =
        let previousTarget = attributeTarget
        let previousKeys = attributeKeys
        let previousTag = attributeTag
        attributeTarget <- attributes
        attributeKeys <- keys
        attributeTag <- tag

        try
            for apply in modifiers do
                apply backend
        finally
            attributeTarget <- previousTarget
            attributeKeys <- previousKeys
            attributeTag <- previousTag

    let browserCapability (part: string) =
        ensureAttributes ()

        if policy = Reject then
            raise (browserRejected (name attributeTag) part)

    let append (value: string) =
        if leadingLineFeed && value.Length > 0 then
            leadingLineFeed <- false

            if value[0] = '\n' then
                sink.Append "\n"

        sink.Append value

    member _.Policy = policy
    member _.Sink = sink

    /// <summary>Appends <c>value</c>; the first non-empty value after a <c>pre</c>, <c>textarea</c> or <c>listing</c> open tag gets a second leading line feed when it starts with one.</summary>
    member _.Append(value: string) =
        append value

    /// <summary>The active <c>ServerWriter</c>, or null before the first template.</summary>
    member val internal Writer: obj = null with get, set

    /// <summary>The active <c>ServerScope</c>: a template rendered through this backend runs under it. Null selects an <c>HtmlScope</c>.</summary>
    member val internal Scope: obj = null with get, set

    /// <summary>The request's head collector: a document entry is collected whatever the policy. Null applies the policy.</summary>
    member val internal Heads: HeadCollector = null with get, set

    /// <summary>The ids of the render, an <c>IdentityScope "r"</c> until set; a server request sets one of its own prefix.</summary>
    member _.Identity
        with get (): IdentityScope =
            if isNull (box identity) then
                identity <- IdentityScope "r"

            identity
        and set (value: IdentityScope) = identity <- value

    /// <summary>The path that authored content sits under: the root's, or a view row's while its view runs.</summary>
    member val internal Path: InstancePath = InstancePath.Root with get, set

    /// <summary>The tags of the open elements, nearest first.</summary>
    member val internal Ancestors: string list = [] with get, set

    override _.Text value =
        childNodes <- childNodes + 1
        append (escapeHtml false value)

    override _.Bind(_, read) =
        childNodes <- childNodes + 1
        append (escapeHtml false (read ()))

    override this.Keyed(items, render) =
        childNodes <- childNodes + 1
        let values = Array.copy (items.Read ())
        Ranvier.Projection.ensureUniqueKeys items.Key.Invoke values

        match this.Heads with
        | null ->
            for item in values do
                render (fun () -> item) this
        | heads ->
            heads.Open ()

            try
                for item in values do
                    render (fun () -> item) this
            finally
                heads.Close ()

    override _.Attribute(key, value) =
        validateAttribute key
        set key value

    override _.OptionalAttribute(key, value) =
        setOptional key value

    override _.ReactiveAttribute(key, read) =
        ensureAttributes ()
        validateAttribute key
        setOptional key (read ())

    override _.InputValue value =
        ensureAttributes ()

        if attributeTag <> Input then
            invalidArg "modifiers" "Input value requires input"

        set "value" value

    override _.Browser _ =
        browserCapability "browser hook"

    override _.Placed(kind, _) =
        match placedPart kind with
        | null -> ensureAttributes ()
        | part -> browserCapability part

        if kind = PlacedKind.Widget then
            if widget then
                raise (System.ArgumentException ("An element takes at most one content part", "modifiers"))

            widget <- true

    override this.Head entry =
        match this.Heads with
        | null ->
            if policy = Reject then
                raise (System.InvalidOperationException "Document metadata requires Server.renderPage")
        | heads -> heads.Add (entry.Key, HeadMarkup.entry entry)

    override _.Listen(eventName, _) =
        browserCapability ("listener " + eventName)

    override _.Property _ =
        browserCapability "property"

    override _.ReactiveProperty(_, _) =
        browserCapability "property"

    override this.Hole(render) =
        match this.Heads with
        | null -> render this
        | heads ->
            heads.Open ()

            try
                render this
            finally
                heads.Close ()

    override this.Element(tag, modifiers, children) =
        if
            match (name tag).ToLowerInvariant() with
            | "script"
            | "style"
            | "iframe"
            | "noscript"
            | "xmp"
            | "noembed"
            | "noframes"
            | "plaintext" -> true
            | _ -> false
        then
            raise (System.InvalidOperationException "Raw-text elements require an explicit HTML serialization policy")

        childNodes <- childNodes + 1
        append ("<" + name tag)
        let attributes = System.Collections.Generic.Dictionary<string, string>()
        let keys = ResizeArray<string>()
        let outerWidget = widget
        widget <- false
        applyModifiers this tag attributes keys modifiers
        let isWidget = widget
        widget <- outerWidget

        for key in keys do
            match attributes.TryGetValue key with
            | false, _ -> ()
            | true, value -> append (" " + key + "=\"" + escapeHtml true value + "\"")

        append ">"

        if not (isVoid tag) then
            leadingLineFeed <-
                name tag = "pre"
                || name tag = "textarea"
                || name tag = "listing"

            let before = childNodes
            let outer = this.Ancestors
            this.Ancestors <- name tag :: outer

            try
                children ()
            finally
                this.Ancestors <- outer

            if isWidget && childNodes <> before then
                raise (System.ArgumentException ("A widget element has no children", "children"))

            append ("</" + name tag + ">")

    override this.Component setup =
        this.ComponentWith (setup, (fun () -> this.Identity.CreateAuthoredId this.Path))

    /// <summary>Runs a component as <c>Component</c> does, its <c>Setup</c> issuing ids through <c>createId</c>.</summary>
    member internal this.ComponentWith(setup: Setup -> unit, createId: unit -> string) =
        match this.Heads with
        | null -> this.RunComponent (setup, createId)
        | heads ->
            let outer = heads.EnterGroup ()

            try
                this.RunComponent (setup, createId)
            finally
                heads.LeaveGroup outer

    /// <summary>
    /// Runs a component's setup, then its cleanups; a failure of either raises after every cleanup has run. Under an
    /// active scope that takes cleanup failures, the scope receives them and only the setup failure raises.
    /// </summary>
    member private this.RunComponent(setup: Setup -> unit, createId: unit -> string) =
        let cleanups = ResizeArray<unit -> unit>()
        let mutable completed = false

        let onCleanup callback =
            if completed then
                raise (System.InvalidOperationException "Server component setup is completed")

            cleanups.Add callback

        let mutable setupFailure: exn option = None

        try
            setup
                {
                    OnCleanup = onCleanup
                    CreateId = createId
                }
        with error ->
            setupFailure <- Some error

        completed <- true
        let errors = ResizeArray<exn>()

        for cleanup in cleanups do
            try
                cleanup ()
            with error ->
                errors.Add error

        if
            errors.Count > 0
            && not (isNull this.Scope)
            && (unbox<ServerScopeBase> this.Scope).TakeCleanupFailures(List.ofSeq errors)
        then
            errors.Clear ()

        match setupFailure, errors.Count with
        | None, 0 -> ()
        | Some error, 0 -> raise error
        | Some error, _ ->
            errors.Insert (0, error)
            raise (System.AggregateException errors)
        | None, _ -> raise (System.AggregateException errors)

    override this.Template(definition, props) =
        childNodes <- childNodes + 1
        definition.RenderHtml (this, props)

    /// <summary>
    /// Writes the content, in an attempt of the active scope when one is set; when it raises, the attempt's cleanups
    /// have run, the content's output and head entries are dropped and the pending or error view is written instead.
    /// The start marker is <c>&lt;!--[--&gt;</c> whichever view is written.
    /// </summary>
    override this.Boundary(views) =
        childNodes <- childNodes + 1
        append "<!--[-->"
        let mark = sink.Mark ()

        let heads = this.Heads
        let entries = if isNull heads then 0 else heads.Count

        if not (isNull heads) then
            heads.Open ()

        let content () =
            views.Content { IsPending = fun () -> false } this

        let outcome =
            try
                try
                    match this.Scope with
                    | null -> content ()
                    | scope ->
                        (unbox<ServerScopeBase> scope)
                            .AttemptWith(fun attempt ->
                                let outer = this.Scope
                                this.Scope <- box attempt

                                try
                                    content ()
                                finally
                                    this.Scope <- outer)

                    None
                with error ->
                    Some error
            finally
                if not (isNull heads) then
                    heads.Close ()

        match outcome with
        | None -> ()
        | Some error ->
            sink.Truncate mark

            if not (isNull heads) then
                heads.Truncate entries
                heads.Open ()

            try
                match error with
                | Ranvier.NotReadyException _ -> views.Pending this
                | error -> views.Failed { Error = error; Retry = ignore } this
            finally
                if not (isNull heads) then
                    heads.Close ()

        append "<!--]-->"

/// <summary>Applies a modifier to the backend's current element.</summary>
type ModifierAction = Backend -> unit

/// <summary>Creates a static attribute modifier.</summary>
let Attribute (key, value) : ModifierAction =
    fun backend -> backend.Attribute (key, value)

/// <summary>Creates an attribute modifier that removes its attribute for None.</summary>
let OptionalAttribute (key, value) : ModifierAction =
    fun backend -> backend.OptionalAttribute (key, value)

/// <summary>Creates an attribute modifier from a tracked reader.</summary>
let ReactiveAttribute (key, read) : ModifierAction =
    fun backend -> backend.ReactiveAttribute (key, read)

/// <summary>Creates a modifier for a live input value.</summary>
let InputValue value : ModifierAction =
    fun backend -> backend.InputValue value

/// <summary>Creates a modifier that acquires browser resources and returns their cleanup.</summary>
let browserModifier hook : ModifierAction =
    fun backend -> backend.Browser hook

/// <summary>Creates a modifier that handles a named DOM event.</summary>
let listenModifier eventName handler : ModifierAction =
    fun backend -> backend.Listen (eventName, handler)

type Operations<'view> =
    {
        Empty: 'view
        Text: string -> 'view
        Fragment: 'view array -> 'view
        Element: Tag -> ModifierAction array -> 'view array -> 'view
        Bind: (string -> string -> bool) -> (unit -> string) -> 'view
        /// <summary>Creates the internal keyed-list representation used by <c>Keyed.list</c>.</summary>
        Keyed: KeyedItems -> ((unit -> obj) -> 'view) -> 'view
        /// <summary>Creates the single keyed row selected by a switch; the previous row is disposed before the next is created.</summary>
        Switch: (unit -> (obj * obj) array) -> ((unit -> obj) -> 'view) -> 'view
        Component: (Setup -> 'view) -> 'view
        /// <summary>Runs backend operations in the view's place; a template view hole embeds its view through it.</summary>
        Embed: (Backend -> unit) -> 'view
        /// <summary>Renders the content under a boundary: the pending view until the content's bindings have values, the error view after a failure under the content.</summary>
        Boundary: 'view * (BoundaryFailure -> 'view) * (BoundaryStatus -> 'view) -> 'view
    }

module Keyed =
    let private create (ops: Operations<'view>) (key: 'item -> 'key) (read: unit -> 'item array) (render: (unit -> 'item) -> 'view) =
        if isNull (box read) then
            invalidArg "read" "Keyed item reader cannot be null"

        if isNull (box key) then
            invalidArg "key" "Key selector cannot be null"

        if isNull (box render) then
            invalidArg "render" "Keyed row renderer cannot be null"

        let items =
            {
                Read = fun () -> KeyedItems.boxAll (read ())
                Key = System.Func<obj, obj>(fun item -> box (key (unbox<'item> item)))
            }

        let renderObject (current: unit -> obj) =
            render (fun () -> unbox<'item>(current ()))

        ops.Keyed items renderObject

    /// <summary>Retains keyed rows and supplies their current item values.</summary>
    /// <remarks>The input array's slots are copied before key callbacks run. Keys are compared with <c>=</c> and must be pure.
    /// An item keeps its key while it is in the list; replace the item to change its key. Duplicate keys fail before reconciliation mutates the DOM.</remarks>
    let array<'item, 'key, 'view when 'key: equality>
        (ops: Operations<'view>)
        (key: 'item -> 'key)
        (read: unit -> 'item array)
        (render: (unit -> 'item) -> 'view)
        =
        if isNull (box read) then
            invalidArg "read" "Keyed item reader cannot be null"

        create ops key (fun () -> Array.copy (read ())) render

    /// <summary>Adapts list snapshots to the array-backed keyed renderer.</summary>
    /// <remarks>Equal keys under <c>=</c> retain rows; their item readers return current values. Keys must be pure.
    /// An item keeps its key while it is in the list; replace the item to change its key.</remarks>
    let list<'item, 'key, 'view when 'key: equality>
        (ops: Operations<'view>)
        (key: 'item -> 'key)
        (read: unit -> 'item list)
        (render: (unit -> 'item) -> 'view)
        =
        if isNull (box read) then
            invalidArg "read" "Keyed item reader cannot be null"

        create ops key (fun () -> List.toArray (read ())) render

module Branch =
    /// <summary>Selects a mounted instance by key and supplies a reader for its current selection.</summary>
    /// <remarks>Equal keys under <c>=</c> retain the instance; a changed key runs the previous instance's cleanups and
    /// removes its nodes before the next instance is set up. Include the branch discriminator in the key when component
    /// shape changes.</remarks>
    let select<'item, 'key, 'view when 'key: equality>
        (ops: Operations<'view>)
        (key: 'item -> 'key)
        (read: unit -> 'item)
        (render: (unit -> 'item) -> 'view)
        =
        if isNull (box key) then
            invalidArg "key" "Key selector cannot be null"

        if isNull (box read) then
            invalidArg "read" "Selection reader cannot be null"

        if isNull (box render) then
            invalidArg "render" "Instance renderer cannot be null"

        let readObjects () =
            let item = read ()
            [| box (key item), box item |]

        ops.Switch readObjects (fun current -> render (fun () -> unbox (current ())))

module Tree =
    type View =
        private
        | Empty
        | Text of string
        | Fragment of View array
        | Element of Tag * ModifierAction array * View array
        | Bind of (string -> string -> bool) * (unit -> string)
        | Keyed of KeyedItems * ((unit -> obj) -> View)
        | Component of (Setup -> View)
        | Embed of (Backend -> unit)

    let rec run view (backend: Backend) =
        match view with
        | Empty -> ()
        | Text value -> backend.Text value
        | Fragment children ->
            for child in children do
                run child backend
        | Element (tag, tokens, children) ->
            backend.Element (
                tag,
                tokens,
                fun () ->
                    for child in children do
                        run child backend
            )
        | Bind (equals, read) -> backend.Bind (equals, read)
        | Keyed (items, render) -> backend.Keyed (items, fun current target -> run (render current) target)
        | Component setup -> backend.Component (fun context -> run (setup context) backend)
        | Embed build -> build backend

    let ops =
        {
            Empty = Empty
            Text = Text
            Fragment = Fragment
            Element = fun tag tokens children -> Element (tag, tokens, children)
            Bind = fun equals read -> Bind (equals, read)
            Keyed = fun items render -> Keyed (items, render)
            Switch = fun read render -> Embed (fun backend -> backend.Switch (read, fun current target -> run (render current) target))
            Component = Component
            Embed = Embed
            Boundary =
                fun (pending, failed, content) ->
                    Embed (fun backend ->
                        backend.Boundary
                            {
                                Pending = run pending
                                Failed = fun failure target -> run (failed failure) target
                                Content = fun status target -> run (content status) target
                            })
        }

module Program =
    /// <summary>Builds a reusable view through its target backend.</summary>
    type View = delegate of Backend -> unit

    let inline run (build: View) (backend: Backend) =
        build.Invoke backend

    /// <summary>Builds fixed text.</summary>
    let inline text value : View =
        View (fun backend -> backend.Text value)

    /// <summary>Builds children in array order.</summary>
    let inline fragment (children: View array) : View =
        View (fun backend ->
            for child in children do
                child.Invoke backend)

    /// <summary>Builds an element with its modifier and child arrays.</summary>
    /// <summary>Presents views as backend actions; Fable reuses the erased view array.</summary>
    let childActions (children: View array) : (Backend -> unit) array =
#if FABLE_COMPILER
        unbox children
#else
        children |> Array.map (fun child -> child.Invoke)
#endif

    let inline element tag (modifiers: ModifierAction array) (children: View array) : View =
        View (fun backend -> backend.ElementWith (tag, modifiers, childActions children))

    /// <summary>Builds text from a tracked reader.</summary>
    let inline bind equals read : View =
        View (fun backend -> backend.Bind (equals, read))

    /// <summary>Runs setup once for each mounted component instance.</summary>
    let inline createComponent (setup: Setup -> View) : View =
        View (fun backend -> backend.Component (fun context -> (setup context).Invoke backend))

    let ops =
        {
            Empty = View ignore
            Text = text
            Fragment = fragment
            Element = element
            Bind = bind
            Keyed = fun items render -> View (fun backend -> backend.Keyed (items, fun current target -> (render current).Invoke target))
            Switch = fun read render -> View (fun backend -> backend.Switch (read, fun current target -> (render current).Invoke target))
            Component = createComponent
            Embed = fun build -> View (fun backend -> build backend)
            Boundary =
                fun (pending, failed, content) ->
                    View (fun backend ->
                        backend.Boundary
                            {
                                Pending = fun target -> pending.Invoke target
                                Failed = fun failure target -> (failed failure).Invoke target
                                Content = fun status target -> (content status).Invoke target
                            })
        }

module Html =
    /// <summary>
    /// Escapes text for HTML; a <c>true</c> flag also escapes double quotes for attribute values. A value free of
    /// escaped characters is returned as the same string, and null is escaped as the empty string.
    /// </summary>
    let escape attribute (value: string) =
        escapeHtml attribute value

    /// <summary>Renders <c>build</c> into a new sink and returns the HTML.</summary>
    let render policy (build: Backend -> unit) =
        let sink = HtmlSink ()
        build (HtmlBackend (policy, sink))
        sink.ToString ()

type FragmentBuilder<'view>(ops: Operations<'view>) =
    member _.Yield(view: 'view) =
        [| view |]

    member _.YieldFrom(views: 'view array) = views
    member _.Zero() = [||]

    member _.Combine(left, right) =
        Array.append left right

    member _.Delay(body: unit -> 'view array) = body ()

    member _.Run(children) =
        ops.Fragment children

let functionSugar (ops: Operations<'view>) text =
    ops.Element Div [| Attribute ("title", "sample") |] [| ops.Text text |]
