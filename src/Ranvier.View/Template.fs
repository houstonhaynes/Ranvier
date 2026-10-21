[<AutoOpen>]
module Ranvier.View.Template

open System
open System.Text
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier
open Ranvier.View.Representation

/// <summary>
/// Markup inserted into an element without escaping. Create it only from trusted or sanitised content. On the client,
/// script elements in the markup stay inert while inline event handler attributes and <c>javascript:</c> URLs run; in
/// server HTML the markup is part of the document, and every script, handler and URL in it runs.
/// </summary>
/// <remarks>The markup must be a well-formed fragment for the element that receives it.</remarks>
[<Sealed>]
type UnsafeHtml private (markup: string) =
    member _.Markup = markup

    static member Trust(markup: string) =
        if isNull markup then
            raise (ArgumentException ("Markup cannot be null", "markup"))

        UnsafeHtml markup

/// <summary>The markup of a raw-HTML slot value; a null value raises "An UnsafeHtml reader returned null".</summary>
let markupOf (value: obj) : string =
    if isNull value then
        raise (ArgumentException ("An UnsafeHtml reader returned null", "read"))

    (unbox<UnsafeHtml> value).Markup

/// <summary>How a property part appears in server HTML.</summary>
[<NoEquality; NoComparison>]
type PropertySerialization =
    /// <summary>The attribute the property reflects; the writer turns a value into the attribute value, <c>None</c> omits it.</summary>
    | AsAttribute of name: string * write: Func<obj, string option>
    /// <summary>The element's escaped child text: a textarea's value.</summary>
    | AsChildText
    /// <summary>A property that exists in the browser only.</summary>
    | BrowserOnly

/// <summary>A property writer with its server serialisation.</summary>
[<NoEquality; NoComparison>]
type PortableProperty =
    {
        Write: Func<obj, obj, unit>
        Html: PropertySerialization
    }

/// <summary>The ECMAScript Number::toString form of <c>value</c>, identical on .NET and under Fable.</summary>
let internal numberText (value: float) : string =
#if FABLE_COMPILER
    string value
#else
    Native.numberText value
#endif

/// <summary>Server serialisations of reflected properties.</summary>
module PropertyHtml =
    /// <summary>A <c>string</c> written as the attribute <c>name</c>; <c>null</c> omits it.</summary>
    let text (name: string) : PropertySerialization =
        AsAttribute (name, Func<obj, string option>(fun value -> if isNull value then None else Some (unbox<string> value)))

    /// <summary>A <c>float</c> written as the attribute <c>name</c> in ECMAScript number form on .NET and under Fable.</summary>
    let number (name: string) : PropertySerialization =
        AsAttribute (name, Func<obj, string option>(fun value -> Some (numberText (unbox<float> value))))

    /// <summary>A <c>bool</c> written as the attribute <c>name</c> with an empty value when true, omitted when false.</summary>
    let presence (name: string) : PropertySerialization =
        AsAttribute (name, Func<obj, string option>(fun value -> if unbox<bool> value then Some "" else None))

    /// <summary>A <c>string option</c> written as the attribute <c>name</c>; <c>None</c> omits it.</summary>
    let optional (name: string) : PropertySerialization =
        AsAttribute (name, Func<obj, string option>(fun value -> unbox<string option> value))

    let childText = AsChildText

    let browserOnly = BrowserOnly

/// <summary>Where a dynamic slot writes on its node.</summary>
[<NoEquality; NoComparison>]
type SlotTarget =
    /// <summary>An attribute written from a <c>string option</c>; <c>None</c> removes it.</summary>
    | AttributeTarget of string
    /// <summary>A property written by a shared writer.</summary>
    | PropertyTarget of PortableProperty
    /// <summary>The data of a text node.</summary>
    | TextTarget
    /// <summary>The element's children, parsed from an <c>UnsafeHtml</c> value.</summary>
    | ContentTarget

/// <summary>How a dynamic slot is re-evaluated.</summary>
type SlotMode =
    /// <summary>Within the instance's render effect.</summary>
    | RenderMode
    /// <summary>In its own effect.</summary>
    | IsolatedMode
    /// <summary>From one fixed source, evaluated in the render effect.</summary>
    | DirectMode

/// <summary>A dynamic part of a template: reads a value from the instance and writes it to a node.</summary>
[<NoEquality; NoComparison>]
type Slot =
    {
        Target: SlotTarget
        Mode: SlotMode
        Read: Func<obj, obj>
        Same: Func<obj, obj, bool>
    }

/// <summary>A node of a backend-neutral template tree.</summary>
[<NoEquality; NoComparison>]
type TemplateNode =
    /// <summary>An element: its tag, static attributes, static properties, slots, events, placed hooks and children.</summary>
    | ElementNode of
        Tag *
        (string * string) array *
        (PortableProperty * obj) array *
        Slot array *
        (string * Func<obj, obj, unit>) array *
        (PlacedKind * Func<obj, obj, obj, obj>) array *
        TemplateNode array
    | TextNode of string
    | TextSlotNode of Slot
    | FragmentNode of TemplateNode array
    /// <summary>Dynamic structure mounted between a pair of region markers.</summary>
    | RegionNode of Region
    /// <summary>A child template inlined into this template's skeleton, with the reader of its props from the inlining instance.</summary>
    | ChildNode of TemplateComponent * Func<obj, obj>
    /// <summary>A document head entry; it occupies zero skeleton width.</summary>
    | HeadNode of HeadTemplate

/// <summary>Dynamic structure of a template: keyed items read from an instance, rendered one row per item.</summary>
and [<NoEquality; NoComparison>] Region =
    {
        /// <summary>Reads the items from an instance: an <c>ItemRow</c> region's row values, otherwise one boxed <c>(key, value)</c> pair.</summary>
        Items: Func<obj, obj array>
        Key: Func<obj, obj>
        Row: RegionRow
    }

/// <summary>What a region's item is and how its row renders it.</summary>
and [<NoEquality; NoComparison>] RegionRow =
    /// <summary>The item is a <c>Program.View</c>, read once per instance.</summary>
    | ViewRow
    /// <summary>The item is an <c>Arm</c>; the arm's definition receives a reader of the current arm's payload.</summary>
    | ArmRow
    /// <summary>The item is a row value; the conversion turns a reader of the current item into the definition's props.</summary>
    | ItemRow of TemplateComponent * Func<(unit -> obj), obj>
    /// <summary>The item is the region's <c>BoundaryViews</c>, read once per instance.</summary>
    | BoundaryRow

/// <summary>A document head entry of a template; its readers receive the instance.</summary>
and [<NoEquality; NoComparison>] HeadTemplate =
    {
        Kind: HeadKind
        /// <summary>The entry's key; null for an unkeyed link.</summary>
        Key: string
        Attributes: (string * string) array
        /// <summary>Dynamic attributes: the name and a reader returning <c>string option</c>.</summary>
        Slots: (string * Func<obj, obj>) array
        /// <summary>The reader of a title entry, returning <c>string</c>; null for a meta or link entry.</summary>
        Title: Func<obj, obj>
    }

/// <summary>A template component definition without its props type.</summary>
and [<AbstractClass>] TemplateComponent internal (setup: Func<Setup, obj, obj>, root: TemplateNode) =
    inherit TemplateDefinition()

    /// <summary>The definition's <c>ServerPlan</c>; null before the first server render.</summary>
    [<DefaultValue>]
    val mutable internal serverPlan: obj

    [<DefaultValue>]
    val mutable internal armReader: Func<(unit -> obj), obj>

    /// <summary>The definition's setup function.</summary>
    member _.Setup = setup

    /// <summary>The definition's root template node.</summary>
    member _.Root = root

    /// <summary>The arm reader shared by every arm of the definition; <c>null</c> until the definition first appears in an arm.</summary>
    member internal this.ArmReader = this.armReader

/// <summary>
/// A component definition selected by a <c>matchWith</c> or <c>branch</c> arm, with the payload read by its props.
/// <c>Reader</c> must come from <c>Definition</c>; a reader of another definition raises <c>InvalidCastException</c> when
/// the arm renders.
/// </summary>
[<NoEquality; NoComparison>]
type Arm =
    {
        Definition: TemplateComponent
        Payload: obj
        /// <summary>Turns a reader of the current arm into the definition's props, a reader of the current payload.</summary>
        Reader: Func<(unit -> obj), obj>
    }

#if FABLE_COMPILER
[<Emit("$0 === $1")>]
let private strictEquals (left: obj) (right: obj) : bool = jsNative
#else
let private strictEquals (left: obj) (right: obj) =
    Object.Equals (left, right)
#endif

/// <summary>Compares primitive slot values: strings, booleans, numbers and options of them.</summary>
let primitiveSame = Func<obj, obj, bool>(fun left right -> strictEquals left right)

/// <summary>Compares raw-HTML slot values by their markup.</summary>
let private contentSame =
    Func<obj, obj, bool>(fun left right -> markupOf left = markupOf right)

let private innerHtml =
    Func<obj, obj, unit>(fun node markup -> (unbox<HTMLElement> node).innerHTML <- unbox<string> markup)

let private contentSlot (slots: Slot array) =
    slots
    |> Array.tryFind (fun slot ->
        match slot.Target with
        | ContentTarget -> true
        | _ -> false)

[<NoEquality; NoComparison>]
type private Flat =
    | Item of TemplateNode
    | Enter of TemplateComponent * Func<obj, obj>
    | Leave

let rec private flatten (nodes: TemplateNode array) : Flat list =
    [
        for node in nodes do
            match node with
            | FragmentNode children -> yield! flatten children
            | TextNode "" -> ()
            | ChildNode (definition, props) ->
                yield Enter (definition, props)
                yield! flatten [| definition.Root |]
                yield Leave
            | other -> yield Item other
    ]

let private isText node =
    match node with
    | TextNode _
    | TextSlotNode _ -> true
    | _ -> false

let private rawText tag =
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

// Whether the nodes, looked into through fragments, hold a node that `counts` accepts; empty text never counts.
let rec private holds (counts: TemplateNode -> bool) (nodes: TemplateNode array) =
    let mutable found = false
    let mutable index = 0

    while not found && index < nodes.Length do
        found <-
            match nodes[index] with
            | FragmentNode children -> holds counts children
            | TextNode "" -> false
            | node -> counts node

        index <- index + 1

    found



/// <summary>
/// The failure of an element that templates reject on both client paths, synchronous and queued: a <c>script</c>
/// element, a raw-text element with children (or <c>plaintext</c>), a <c>template</c> element or a void element with
/// children; null for any other element. The server rejects every raw-text element, and an RCDATA element with element children.
/// </summary>
let private refusal tag (children: TemplateNode array) : string =
    let lowered = (name tag).ToLowerInvariant()

    if lowered = "script" then
        scriptRefused
    elif
        rawText tag
        && (lowered = "plaintext"
            || holds (fun _ -> true) children)
    then
        "Raw-text elements require an explicit HTML serialization policy"
    elif lowered = "template" then
        "Template elements are not supported in templates"
    elif isVoid tag && holds (fun _ -> true) children then
        "A void element has children"
    else
        null

/// <summary>The number of skeleton sibling nodes occupied by a flattened node.</summary>
let private width node =
    match node with
    | RegionNode _ -> 2
    | _ -> 1

/// <summary>Renders a region row through a backend; the reader returns the row's current item.</summary>
let rowRenderer (row: RegionRow) (current: unit -> obj) (backend: Backend) : unit =
    match row with
    | ViewRow -> (unbox<Program.View>(current ())).Invoke backend
    | ArmRow ->
        let arm = unbox<Arm>(current ())
        backend.Template (arm.Definition, arm.Reader.Invoke current)
    | ItemRow (definition, props) -> backend.Template (definition, props.Invoke current)
    | BoundaryRow -> backend.Boundary (unbox<BoundaryViews>(current ()))

[<NoEquality; NoComparison>]
type InlinedChild =
    {
        Definition: TemplateComponent
        Props: Func<obj, obj>
        /// <summary>The part whose instance the props reader receives: 0 for the template, k for the k-th inlined child in document order.</summary>
        Owner: int
        /// <summary>The index of the child's first dynamic node.</summary>
        First: int
        /// <summary>The number of dynamic nodes of the child and its inlined descendants.</summary>
        Count: int
    }

/// <summary>The markup of a template's skeleton with its dynamic nodes and inlined children.</summary>
[<NoEquality; NoComparison>]
type SkeletonLayout =
    {
        Markup: string
        /// <summary>Each dynamic node in document order, with its child-index path from the skeleton's root fragment.</summary>
        Targets: (TemplateNode * int array) array
        /// <summary>The part owning each dynamic node: 0 for the template, k for the k-th inlined child in document order.</summary>
        Owners: int array
        RootCount: int
        /// <summary>The inlined children, at every depth, in document order.</summary>
        Children: InlinedChild array
        /// <summary>
        /// The document head entries, at every depth, with their owning part and the number of dynamic nodes before them
        /// in document order.
        /// </summary>
        Heads: (HeadTemplate * int * int) array
    }

type private WriterState() =
    member val Targets = ResizeArray<TemplateNode * int array>()
    member val Owners = ResizeArray<int>()
    member val Children = ResizeArray<InlinedChild>()
    member val Heads = ResizeArray<HeadTemplate * int * int>()
    member val Parts = ResizeArray<int>()
    member val Part = 0 with get, set

let private writeMarkup (append: string -> unit) (state: WriterState) (root: TemplateNode) =
    let target node (path: int list) =
        state.Targets.Add (node, Array.ofList (List.rev path))
        state.Owners.Add state.Part

    let enter (definition: TemplateComponent) (props: Func<obj, obj>) =
        state.Children.Add
            {
                InlinedChild.Definition = definition
                Props = props
                Owner = state.Part
                First = state.Targets.Count
                Count = 0
            }

        state.Parts.Add state.Part
        state.Part <- state.Children.Count

    let leave () =
        let index = state.Part - 1
        let child = state.Children[index]

        state.Children[index] <-
            { child with
                Count = state.Targets.Count - child.First
            }

        state.Part <- state.Parts[state.Parts.Count - 1]
        state.Parts.RemoveAt (state.Parts.Count - 1)

    let writeHead (head: HeadTemplate) =
        state.Heads.Add ((head, state.Part, state.Targets.Count))

    let rec writeChildren (children: TemplateNode array) (path: int list) =
        let mutable index = 0
        let mutable previousText = false

        for flat in flatten children do
            match flat with
            | Enter (definition, props) -> enter definition props
            | Leave -> leave ()
            | Item (HeadNode head) -> writeHead head
            | Item child ->
                let text = isText child

                if text && previousText then
                    append "<!--!-->"
                    index <- index + 1

                writeNode child (index :: path)
                index <- index + width child
                previousText <- text

        index

    and writeNode node path =
        match node with
        | TextNode value -> append (Html.escape false value)
        | TextSlotNode _ ->
            target node path
            append " "
        | FragmentNode _
        | ChildNode _
        | HeadNode _ -> invalidOp "Fragments, inlined children and head entries are flattened before writing"
        | ElementNode (tag, attributes, properties, slots, events, placed, children) ->
            match refusal tag children with
            | null -> ()
            | message -> raise (InvalidOperationException message)

            if
                properties.Length > 0
                || slots.Length > 0
                || events.Length > 0
                || placed.Length > 0
            then
                target node path

            append ("<" + name tag)

            for key, value in attributes do
                append (" " + key + "=\"" + Html.escape true value + "\"")

            append ">"

            if not (isVoid tag) then
                writeChildren children path |> ignore
                append ("</" + name tag + ">")
        | RegionNode _ ->
            target node path
            append "<!--[--><!--]-->"

    writeChildren [| root |] []

/// <summary>The skeleton of a template: its markup, its dynamic nodes in document order with their owning parts, its root count and its inlined children.</summary>
/// <remarks>A path lists child indexes from the skeleton's root fragment. A region's path leads to its start marker.</remarks>
let skeletonLayout (root: TemplateNode) : SkeletonLayout =
    let output = StringBuilder ()
    let state = WriterState ()

    let rootCount = writeMarkup (fun value -> output.Append value |> ignore) state root

    {
        Markup = output.ToString ()
        Targets = state.Targets.ToArray ()
        Owners = state.Owners.ToArray ()
        RootCount = rootCount
        Children = state.Children.ToArray ()
        Heads = state.Heads.ToArray ()
    }

/// <summary>The skeleton markup of a template, the path of every dynamic node in document order, and the number of root nodes.</summary>
/// <remarks>A path lists child indexes from the skeleton's root fragment. A region's path leads to its start marker.</remarks>
let skeletonMarkup (root: TemplateNode) =
    let layout = skeletonLayout root
    layout.Markup, layout.Targets, layout.RootCount

/// <summary>The 32-bit FNV-1a hash of the string's UTF-16 code units, as eight lowercase hex digits; equal on .NET and under Fable.</summary>
let fnv1a (value: string) : string =
    let mutable hash = 2166136261u

    for index in 0 .. value.Length - 1 do
        hash <- hash ^^^ uint32 value[index]
        // hash * 16777619, as shifts and adds that stay exact under Fable.
        hash <-
            hash
            + (hash <<< 1)
            + (hash <<< 4)
            + (hash <<< 7)
            + (hash <<< 8)
            + (hash <<< 24)

    let digits = "0123456789abcdef"
    let hex = StringBuilder 8

    for shift in 28..-4..0 do
        hex.Append digits[int ((hash >>> shift) &&& 15u)]
        |> ignore

    hex.ToString ()

/// <summary>A document head entry of a server plan; its readers receive the current part's instance.</summary>
[<NoEquality; NoComparison>]
type internal HeadPlan =
    {
        /// <summary>The entry's key; null for an unkeyed link.</summary>
        Key: string
        /// <summary>The marked start tag through the static attributes: closed for a title, open for a meta or link.</summary>
        Opening: string
        /// <summary>Dynamic attributes: the name and a reader returning <c>string option</c>.</summary>
        Slots: (string * Func<obj, obj>) array
        /// <summary>The reader of a title entry, returning <c>string</c>; null for a meta or link entry.</summary>
        Title: Func<obj, obj>
    }

/// <summary>One step of a definition's server plan, interpreted by <c>ServerWriter</c> against the current part.</summary>
[<NoEquality; NoComparison>]
type internal ServerOp =
    /// <summary>Markup written as it is.</summary>
    | Static of string
    /// <summary>
    /// A text slot's escaped value; with <c>leadingLineFeed</c>, a value starting with a line feed gets a second one.
    /// With <c>marksEmpty</c>, an empty or null value writes <c>&lt;!--_--&gt;</c>; otherwise it writes nothing.
    /// </summary>
    | TextOp of Slot * leadingLineFeed: bool * marksEmpty: bool
    /// <summary>
    /// An attribute slot: <c> name="value"</c> for <c>Some</c>; the attribute is omitted for <c>None</c>. <c>opening</c>
    /// is <c> name="</c>.
    /// </summary>
    | AttributeOp of opening: string * Slot
    /// <summary>A raw-HTML slot's markup, written verbatim; with <c>leadingLineFeed</c>, non-empty markup is written after a line feed, which the HTML parser drops.</summary>
    | ContentOp of Slot * leadingLineFeed: bool
    /// <summary>A document head entry: collected under <c>Server.renderPage</c>; otherwise raises under <c>Reject</c> and is dropped under <c>Omit</c>.</summary>
    | HeadOp of HeadPlan
    /// <summary>A serialised property slot: <c> name="value"</c>, or nothing when the serialisation returns <c>None</c>.</summary>
    | PropertyOp of PortableProperty * Slot
    /// <summary>A serialised static property: <c> name="value"</c>, or nothing when the serialisation returns <c>None</c>.</summary>
    | StaticPropertyOp of PortableProperty * obj
    /// <summary>A child-text property slot's escaped value, written after the open tag; with <c>leadingLineFeed</c>, a value starting with a line feed gets a second one.</summary>
    | ChildTextOp of PortableProperty * Slot * leadingLineFeed: bool
    /// <summary>A static child-text property's escaped value, written after the open tag; with <c>leadingLineFeed</c>, a value starting with a line feed gets a second one.</summary>
    | StaticChildTextOp of PortableProperty * obj * leadingLineFeed: bool
    /// <summary>A browser-only part of the element <c>tag</c>: raises under <c>Reject</c>, dropped under <c>Omit</c>.</summary>
    | BrowserOp of tag: string * part: string
    /// <summary>
    /// A region, with its position among the regions of its owning part and the tags of the elements enclosing it in
    /// the definition, nearest first.
    /// </summary>
    | RegionOp of index: int * Region * ancestors: string list
    /// <summary>Sets up the inlined child <c>child</c>, which becomes the current part.</summary>
    | EnterOp of child: int
    /// <summary>Restores the enclosing part.</summary>
    | LeaveOp

/// <summary>A definition's server HTML as operations over its flattened template; no two <c>Static</c> operations are adjacent.</summary>
[<NoEquality; NoComparison>]
type internal ServerPlan =
    {
        Ops: ServerOp array
        /// <summary>The inlined children, at every depth, in document order; <c>EnterOp k</c> sets up the child at <c>k - 1</c>.</summary>
        Children: InlinedChild array
        /// <summary>Whether the plan has an inlined child or a region, whose position needs the instance's path.</summary>
        Positioned: bool
        /// <summary>The definition's <c>structuralHash</c>.</summary>
        Hash: string
        RootCount: int
        /// <summary>Whether the first root is a text node or a text slot.</summary>
        FirstIsText: bool
        /// <summary>Whether the last root is a text node or a text slot.</summary>
        LastIsText: bool
        Summary: ContentModel.RootSummary
        /// <summary>The <c>ContentModel.contextKey</c> of each context the roots were accepted in; guarded by <c>lock</c> on .NET.</summary>
        CheckedContexts: Collections.Generic.HashSet<string>
        /// <summary>The ancestor list the roots were last accepted under.</summary>
        mutable Accepted: string list
    }

/// <summary>Elements whose first child text loses one leading line feed when parsed.</summary>
let private dropsLeadingLineFeed (tagName: string) =
    let tagName = tagName.ToLowerInvariant ()

    tagName = "pre"
    || tagName = "textarea"
    || tagName = "listing"

/// <summary>The content-model summary of the definition whose template is <c>root</c>.</summary>
let private summariseRoots (root: TemplateNode) : ContentModel.RootSummary =
    let tags = ResizeArray<string>()
    let textRoot = ref false
    let closers = Array.create<string> ContentModel.scopes.Length null

    // `reaching[i]`: whether every ancestor in the definition continues the search of `scopes[i]`.
    let rec walk (nodes: TemplateNode array) (above: string list) (reaching: bool array) =
        for flat in flatten nodes do
            match flat with
            | Item (ElementNode (tag, _, _, _, _, _, children)) ->
                let tagName = name tag

                if above.IsEmpty then
                    tags.Add tagName

                for index in 0 .. ContentModel.scopes.Length - 1 do
                    if
                        reaching[index]
                        && isNull closers[index]
                        && ContentModel.scopes[index].Closes tagName
                    then
                        closers[index] <- String.Join ("/", Array.ofList (List.rev (tagName :: above)))

                let inner =
                    Array.init ContentModel.scopes.Length (fun index ->
                        reaching[index]
                        && ContentModel.scopes[index].Continues tagName)

                walk children (tagName :: above) inner
            | Item (TextSlotNode _) when above.IsEmpty -> textRoot.Value <- true
            | Item (TextNode value) when
                above.IsEmpty
                && not (ContentModel.isWhitespace value)
                ->
                textRoot.Value <- true
            | Item _
            | Enter _
            | Leave -> ()

    walk [| root |] [] (Array.create ContentModel.scopes.Length true)

    {
        RootTags = tags.ToArray ()
        TextRoot = textRoot.Value
        Closers = closers
    }

let private isBrowserOnly (property: PortableProperty) =
    match property.Html with
    | BrowserOnly -> true
    | AsAttribute _
    | AsChildText -> false

let private isChildText (property: PortableProperty) =
    match property.Html with
    | AsChildText -> true
    | AsAttribute _
    | BrowserOnly -> false

/// <summary>Raises when a serialised property of the element <c>tagName</c> writes an attribute that another of its parts also writes.</summary>
let private checkAttributeSources tagName (attributes: (string * string) array) (properties: (PortableProperty * obj) array) (slots: Slot array) =
    let written = Collections.Generic.HashSet<string>()

    let add (attribute: string) =
        written.Add attribute |> ignore

    let serialised (property: PortableProperty) =
        match property.Html with
        | AsAttribute (attribute, _) ->
            validateAttribute attribute

            if not (written.Add attribute) then
                raise (
                    InvalidOperationException (
                        "<"
                        + tagName
                        + "> writes the attribute "
                        + attribute
                        + " from more than one part"
                    )
                )
        | AsChildText
        | BrowserOnly -> ()

    for attribute, _ in attributes do
        add attribute

    for slot in slots do
        match slot.Target with
        | AttributeTarget attribute -> add attribute
        | _ -> ()

    for property, _ in properties do
        serialised property

    for slot in slots do
        match slot.Target with
        | PropertyTarget property -> serialised property
        | _ -> ()

let private compilePlan (root: TemplateNode) : ServerPlan =
    let ops = ResizeArray<ServerOp>()
    let pending = StringBuilder ()
    let children = ResizeArray<InlinedChild>()
    let enclosing = ResizeArray<int>()
    // The number of regions compiled so far, per part.
    let regions = ResizeArray<int>([ 0 ])
    let mutable part = 0
    let mutable targets = 0

    let write (markup: string) =
        pending.Append markup |> ignore

    let emit op =
        if pending.Length > 0 then
            ops.Add (Static (pending.ToString ()))
            pending.Clear () |> ignore

        ops.Add op

    let rec compileChildren (nodes: TemplateNode array) (leading: bool) (marksEmpty: bool) (ancestors: string list) =
        let mutable first = leading
        let mutable previousText = false

        for flat in flatten nodes do
            match flat with
            | Enter (definition, props) ->
                children.Add
                    {
                        InlinedChild.Definition = definition
                        Props = props
                        Owner = part
                        First = targets
                        Count = 0
                    }

                enclosing.Add part
                part <- children.Count
                regions.Add 0
                emit (EnterOp part)
            | Leave ->
                let child = children[part - 1]

                children[part - 1] <-
                    { child with
                        Count = targets - child.First
                    }

                part <- enclosing[enclosing.Count - 1]
                enclosing.RemoveAt (enclosing.Count - 1)
                emit LeaveOp
            | Item (HeadNode head) ->
                for name, _ in head.Slots do
                    validateAttribute name

                emit (
                    HeadOp
                        {
                            Key = head.Key
                            Opening = HeadMarkup.opening head.Kind head.Key head.Attributes
                            Slots = head.Slots
                            Title = head.Title
                        }
                )
            | Item node ->
                let text = isText node

                if text && previousText then
                    write "<!--!-->"

                compileNode node first marksEmpty ancestors
                first <- false
                previousText <- text

    and compileNode node leading marksEmpty ancestors =
        match node with
        | TextNode value ->
            ContentModel.checkText ancestors (ContentModel.isWhitespace value)
            let escaped = Html.escape false value

            if leading && escaped.Length > 0 && escaped[0] = '\n' then
                write "\n"

            write escaped
        | TextSlotNode slot ->
            ContentModel.checkText ancestors false
            targets <- targets + 1
            emit (TextOp (slot, leading, marksEmpty))
        | ElementNode (tag, attributes, properties, slots, events, placed, nodes) ->
            let tagName = name tag
            let lowered = tagName.ToLowerInvariant ()

            if rawText tag then
                raise (InvalidOperationException "Raw-text elements require an explicit HTML serialization policy")

            if lowered = "template" then
                raise (
                    InvalidOperationException (
                        "Template elements are not supported in templates at "
                        + ContentModel.path ancestors lowered
                    )
                )

            ContentModel.check ancestors lowered

            if isVoid tag && not (List.isEmpty (flatten nodes)) then
                raise (
                    InvalidOperationException (
                        "A void element has children at "
                        + ContentModel.path ancestors lowered
                    )
                )

            if
                properties.Length > 0
                || slots.Length > 0
                || events.Length > 0
                || placed.Length > 0
            then
                targets <- targets + 1

            checkAttributeSources tagName attributes properties slots

            for property, _ in properties do
                if isBrowserOnly property then
                    emit (BrowserOp (tagName, "property"))

            for slot in slots do
                match slot.Target with
                | PropertyTarget property when isBrowserOnly property -> emit (BrowserOp (tagName, "property"))
                | _ -> ()

            for eventName, _ in events do
                emit (BrowserOp (tagName, "listener " + eventName))

            for kind, _ in placed do
                match placedPart kind with
                | null -> ()
                | part -> emit (BrowserOp (tagName, part))

            write ("<" + tagName)

            for key, value in attributes do
                write (" " + key + "=\"" + Html.escape true value + "\"")

            for property, value in properties do
                match property.Html with
                | AsAttribute _ -> emit (StaticPropertyOp (property, value))
                | AsChildText
                | BrowserOnly -> ()

            for slot in slots do
                match slot.Target with
                | AttributeTarget key -> emit (AttributeOp (" " + key + "=\"", slot))
                | PropertyTarget property ->
                    match property.Html with
                    | AsAttribute _ -> emit (PropertyOp (property, slot))
                    | AsChildText
                    | BrowserOnly -> ()
                | TextTarget
                | ContentTarget -> ()

            write ">"

            let leadingLineFeed = dropsLeadingLineFeed tagName

            let childText =
                [|
                    for property, value in properties do
                        if isChildText property then
                            StaticChildTextOp (property, value, leadingLineFeed)
                    for slot in slots do
                        match slot.Target with
                        | PropertyTarget property when isChildText property -> ChildTextOp (property, slot, leadingLineFeed)
                        | _ -> ()
                |]

            if childText.Length > 1 then
                raise (
                    InvalidOperationException (
                        "<"
                        + tagName
                        + "> takes at most one child-text property"
                    )
                )

            if childText.Length = 1 && nodes.Length > 0 then
                raise (
                    InvalidOperationException (
                        "<"
                        + tagName
                        + "> with a child-text property has no template children"
                    )
                )

            let rcdata = ContentModel.isRcdata tagName

            if rcdata then
                let texts =
                    flatten nodes
                    |> List.filter (fun flat ->
                        match flat with
                        | Item node -> isText node
                        | Enter _
                        | Leave -> false)
                    |> List.length

                if texts > 1 then
                    raise (InvalidOperationException ("<" + tagName + "> takes at most one text child"))

            if not (isVoid tag) then
                match contentSlot slots with
                | Some content -> emit (ContentOp (content, leadingLineFeed))
                | None when childText.Length = 1 -> emit childText[0]
                | None -> compileChildren nodes leadingLineFeed (not rcdata) (lowered :: ancestors)

                write ("</" + tagName + ">")
        | RegionNode region ->
            ContentModel.checkRegion ancestors
            targets <- targets + 1
            emit (RegionOp (regions[part], region, ancestors))
            regions[part] <- regions[part] + 1
        | FragmentNode _
        | ChildNode _
        | HeadNode _ -> invalidOp "Fragments, inlined children and head entries are flattened before compiling"

    compileChildren [| root |] false true []

    if pending.Length > 0 then
        ops.Add (Static (pending.ToString ()))

    let layout = skeletonLayout root

    let kinds =
        layout.Targets
        |> Array.choose (fun (node, _) ->
            match node with
            | RegionNode region ->
                match region.Row with
                | ItemRow _ -> Some "l"
                | ArmRow -> Some "a"
                | ViewRow -> Some "v"
                | BoundaryRow -> Some "b"
            | _ -> None)

    let roots =
        flatten [| root |]
        |> List.choose (fun flat ->
            match flat with
            | Item (HeadNode _) -> None
            | Item node -> Some node
            | Enter _
            | Leave -> None)

    {
        Ops = ops.ToArray ()
        Children = children.ToArray ()
        Positioned =
            children.Count > 0
            || ops
               |> Seq.exists (fun op ->
                   match op with
                   | RegionOp _ -> true
                   | _ -> false)
        Hash = fnv1a (layout.Markup + "|" + String.Join ("", kinds))
        RootCount = layout.RootCount
        FirstIsText = not roots.IsEmpty && isText (List.head roots)
        LastIsText = not roots.IsEmpty && isText (List.last roots)
        Summary = summariseRoots root
        CheckedContexts = Collections.Generic.HashSet<string>()
        Accepted = []
    }

/// <summary>
/// Raises when the plan's roots are restructured under <c>ancestors</c>; each context key is checked until it is
/// accepted once, and the rows of a region, which share one ancestor list, are checked by its first row.
/// </summary>
let private checkRoots (plan: ServerPlan) (ancestors: string list) =
    if
        not ancestors.IsEmpty
        && not (obj.ReferenceEquals (plan.Accepted, ancestors))
    then
        let key = ContentModel.contextKey ancestors
        let contexts = plan.CheckedContexts
#if FABLE_COMPILER
        if not (contexts.Contains key) then
            ContentModel.checkSummary plan.Summary ancestors
            contexts.Add key |> ignore
#else
        if not (lock contexts (fun () -> contexts.Contains key)) then
            ContentModel.checkSummary plan.Summary ancestors
            lock contexts (fun () -> contexts.Add key |> ignore)
#endif
        plan.Accepted <- ancestors

/// <summary>The definition's server plan, compiled on first use; every later call returns the same plan.</summary>
let internal serverPlan (definition: TemplateComponent) : ServerPlan =
    match definition.serverPlan with
    | null ->
        let plan = compilePlan definition.Root
#if FABLE_COMPILER
        definition.serverPlan <- plan
        plan
#else
        match Threading.Interlocked.CompareExchange (&definition.serverPlan, box plan, null) with
        | null -> plan
        | published -> unbox published
#endif
    | plan -> unbox plan

/// <summary>
/// The <c>fnv1a</c> hash of the definition's skeleton markup, a <c>|</c>, and one letter per region in document order:
/// <c>l</c> list, <c>a</c> arm, <c>v</c> view hole, <c>b</c> boundary.
/// </summary>
let structuralHash (definition: TemplateComponent) : string =
    (serverPlan definition).Hash

/// <summary>
/// Whether an HTML parser can produce a comment from <c>markup</c> whose data starts with <c>[</c> or equals <c>]</c>,
/// in any parsing context.
/// </summary>
/// <remarks>
/// Covers <c>&lt;!--</c> comments closed by <c>--&gt;</c>, <c>--!&gt;</c> or the end of input, and the bogus comments
/// opened by <c>&lt;!</c> (including <c>&lt;![CDATA[</c> in HTML content) and by <c>&lt;/</c> followed by a
/// non-letter. Markup inside attribute values, raw-text elements and foreign <c>CDATA</c> is also matched.
/// </remarks>
let private hasMarkerComment (markup: string) =
    let at (index: int) (value: string) =
        let mutable same = index + value.Length <= markup.Length
        let mutable offset = 0

        while same && offset < value.Length do
            same <- markup[index + offset] = value[offset]
            offset <- offset + 1

        same

    let rest (index: int) =
        markup.Substring index

    // The data "]" is complete when the comment closes or the input ends inside the comment's closing sequence.
    let commentCloses (tail: string) =
        "--!".StartsWith(tail, StringComparison.Ordinal)
        || tail.StartsWith ("-->", StringComparison.Ordinal)
        || tail.StartsWith ("--!>", StringComparison.Ordinal)

    let bogusCloses (tail: string) =
        tail.Length = 0 || tail[0] = '>'

    let mutable found = false
    let mutable index = markup.IndexOf '<'

    while not found && index >= 0 do
        found <-
            at index "<!--["
            || at index "<!["
            || at index "</["
            || (at index "<!--]"
                && commentCloses (rest (index + 5)))
            || ((at index "<!]" || at index "</]")
                && bogusCloses (rest (index + 3)))

        index <- markup.IndexOf ('<', index + 1)

    found

/// <summary>The owners of instances set up by a server writer.</summary>
[<AbstractClass>]
type internal ServerScope() =
    inherit ServerScopeBase()

    /// <summary>Runs the body with the scope and <c>Setup</c> of a new nested instance.</summary>
    abstract Nest: (ServerScope -> Setup -> unit) -> unit

    /// <summary>Runs the body in a new nested scope; when it raises, that scope's cleanups run before the exception propagates.</summary>
    abstract Attempt: (ServerScope -> unit) -> unit

    /// <summary>Runs the body with the scope and <c>Setup</c> of an inlined child of the instance set up with <c>parent</c>.</summary>
    abstract Inline: parent: Setup * body: (ServerScope -> Setup -> unit) -> unit

    default this.Inline(_, body) =
        this.Nest body

    override this.AttemptWith body =
        this.Attempt (fun attempt -> body (attempt :> ServerScopeBase))

    override _.TakeCleanupFailures _ = false

/// <summary>
/// The scope of <c>Html.render</c>: an instance's cleanups run after its markup is written, and an inlined child's run
/// with its parent's.
/// </summary>
[<Sealed>]
type internal HtmlScope(backend: HtmlBackend) =
    inherit ServerScope()

    override this.Nest body =
        backend.ComponentWith ((fun setup -> body this setup), (fun () -> backend.Identity.CreateId ()))

    override this.Inline(parent, body) =
        body this parent

    override this.Attempt body = body this

/// <summary>Writes template instances to a backend from their definitions' server plans.</summary>
/// <remarks>
/// Each instance is written at a position, one step below a parent path (see <c>InstanceStep</c>), and its setup runs in
/// the identity window of that position.
/// </remarks>
[<Sealed>]
type internal ServerWriter(backend: HtmlBackend) =
    let reject = backend.Policy = Reject
    // The position the next template reached from authored content takes, while `pending` is set.
    let mutable pending = false
    let mutable pendingParent: InstancePath = null
    let mutable pendingKind = 0
    let mutable pendingA = 0
    let mutable pendingB = 0
    // The templates reached from authored content at the authored step under `backend.Path`.
    let mutable authored = 0

    /// <summary>Runs <c>body</c> with <c>scope</c> as the backend's active scope.</summary>
    let under (scope: ServerScope) (body: unit -> unit) =
        let outer = backend.Scope
        backend.Scope <- scope

        try
            body ()
        finally
            backend.Scope <- outer

    let writeSerialised (property: PortableProperty) (value: obj) =
        match property.Html with
        | AsAttribute (name, serialise) ->
            match serialise.Invoke value with
            | Some text -> backend.Append (" " + name + "=\"" + Html.escape true text + "\"")
            | None -> ()
        | AsChildText
        | BrowserOnly -> raise (InvalidOperationException "Only an attribute serialisation is written as an attribute")

    /// <summary>
    /// Writes an escaped text value; with <c>leadingLineFeed</c>, a value starting with a line feed gets a second one.
    /// With <c>marksEmpty</c>, an empty or null value writes <c>&lt;!--_--&gt;</c>.
    /// </summary>
    let writeText (value: obj) (leadingLineFeed: bool) (marksEmpty: bool) =
        let value = unbox<string> value

        if String.IsNullOrEmpty value then
            if marksEmpty then
                backend.Append "<!--_-->"
        else
            if leadingLineFeed && value[0] = '\n' then
                backend.Append "\n"

            backend.Append (Html.escape false value)

    /// <summary>Sets the position the next template reached from authored content takes.</summary>
    member _.Pend(parent: InstancePath, kind: int, a: int, b: int) =
        pending <- true
        pendingParent <- parent
        pendingKind <- kind
        pendingA <- a
        pendingB <- b

    /// <summary>
    /// Writes an instance reached from authored content at the pending position, or, when none is pending, at the next
    /// authored step under the backend's <c>Path</c>.
    /// </summary>
    member this.Embed(scope: ServerScope, definition: TemplateComponent, props: obj) : unit =
        if pending then
            pending <- false
            this.Write (scope, definition, props, pendingParent, pendingKind, pendingA, pendingB)
        else
            let n = authored
            authored <- n + 1
            this.Write (scope, definition, props, backend.Path, InstanceStep.Authored, n, 0)

    /// <summary>
    /// Runs <c>render</c> under <c>scope</c> as the view of row <c>row</c> of region <c>region</c> under <c>parent</c>:
    /// the first template it reaches takes the row's position, and its authored content sits under the row's path.
    /// </summary>
    member _.View(scope: ServerScope, render: Backend -> unit, parent: InstancePath, region: int, row: int) : unit =
        let outerPending = pending
        let outerParent = pendingParent
        let outerKind = pendingKind
        let outerA = pendingA
        let outerB = pendingB
        let outerAuthored = authored
        let outerPath = backend.Path
        pending <- true
        pendingParent <- parent
        pendingKind <- InstanceStep.Row
        pendingA <- region
        pendingB <- row
        authored <- 0
        backend.Path <- parent.Row (region, row)

        try
            under scope (fun () -> render backend)
        finally
            pending <- outerPending
            pendingParent <- outerParent
            pendingKind <- outerKind
            pendingA <- outerA
            pendingB <- outerB
            authored <- outerAuthored
            backend.Path <- outerPath

    /// <summary>
    /// Sets up an instance of <c>definition</c> in <c>scope.Nest</c>, at the position one step of <c>kind</c> below
    /// <c>parent</c>, and writes it.
    /// </summary>
    /// <remarks>
    /// The instance and each inlined child are groups of the backend's innermost open head container, entered in
    /// document order; each region is a child container of it, closed in region order.
    /// </remarks>
    member this.Write(scope: ServerScope, definition: TemplateComponent, props: obj, parent: InstancePath, kind: int, a: int, b: int) : unit =
        match backend.Heads with
        | null -> this.Instance (scope, definition, props, parent, kind, a, b)
        | heads ->
            let outer = heads.EnterGroup ()

            try
                this.Instance (scope, definition, props, parent, kind, a, b)
            finally
                heads.LeaveGroup outer

    member private this.Instance
        (scope: ServerScope, definition: TemplateComponent, props: obj, parent: InstancePath, kind: int, a: int, b: int)
        : unit =
        let plan = serverPlan definition
        checkRoots plan backend.Ancestors

        scope.Nest (fun scope setup ->
            let identity = backend.Identity

            let parts: obj array =
                if plan.Children.Length = 0 then
                    [| null |]
                else
                    Array.zeroCreate (plan.Children.Length + 1)

            let mutable path: InstancePath = null
            identity.Enter (parent, kind, a, b)

            try
                parts[0] <- definition.Setup.Invoke (ownSetup identity setup, props)

                if plan.Positioned then
                    path <- identity.Path ()
            finally
                identity.Leave ()

            this.Run (scope, setup, plan, parts, path, 0, 0)
            |> ignore)

    /// <summary>
    /// Interprets the plan from <c>start</c> against <c>part</c> until its <c>LeaveOp</c> or the end; returns the index
    /// after it. <c>path</c> is the instance's path, null for a plan that is not <c>Positioned</c>.
    /// </summary>
    member private this.Run(scope: ServerScope, setup: Setup, plan: ServerPlan, parts: obj array, path: InstancePath, part: int, start: int) : int =
        let ops = plan.Ops
        let instance = parts[part]
        let mutable partPath: InstancePath = null
        let mutable index = start
        let mutable running = true

        while running && index < ops.Length do
            match ops[index] with
            | Static markup ->
                backend.Append markup
                index <- index + 1
            | TextOp (slot, leadingLineFeed, marksEmpty) ->
                writeText (slot.Read.Invoke instance) leadingLineFeed marksEmpty
                index <- index + 1
            | AttributeOp (opening, slot) ->
                match unbox<string option>(slot.Read.Invoke instance) with
                | Some value ->
                    backend.Append opening
                    backend.Append (Html.escape true value)
                    backend.Append "\""
                | None -> ()

                index <- index + 1
            | ContentOp (slot, leadingLineFeed) ->
                let markup = markupOf (slot.Read.Invoke instance)

                if hasMarkerComment markup then
                    raise (InvalidOperationException "Raw HTML cannot contain a comment that starts with [ or equals ]")

                if leadingLineFeed && markup.Length > 0 then
                    backend.Append "\n"

                backend.Append markup
                index <- index + 1
            | HeadOp head ->
                match backend.Heads with
                | null ->
                    if reject then
                        raise (InvalidOperationException "Document metadata requires Server.renderPage")
                | heads ->
                    let markup =
                        if isNull head.Title then
                            let parts = Array.zeroCreate<string>(head.Slots.Length + 2)
                            parts[0] <- head.Opening

                            for slot in 0 .. head.Slots.Length - 1 do
                                let name, read = head.Slots[slot]
                                parts[slot + 1] <- HeadMarkup.attribute name (unbox<string option>(read.Invoke instance))

                            parts[parts.Length - 1] <- ">"
                            String.Join ("", parts)
                        else
                            HeadMarkup.title head.Opening (unbox<string>(head.Title.Invoke instance))

                    heads.Add (head.Key, markup)

                index <- index + 1
            | PropertyOp (property, slot) ->
                writeSerialised property (slot.Read.Invoke instance)
                index <- index + 1
            | StaticPropertyOp (property, value) ->
                writeSerialised property value
                index <- index + 1
            | ChildTextOp (_, slot, leadingLineFeed) ->
                writeText (slot.Read.Invoke instance) leadingLineFeed false
                index <- index + 1
            | StaticChildTextOp (_, value, leadingLineFeed) ->
                writeText value leadingLineFeed false
                index <- index + 1
            | BrowserOp (tag, part) ->
                if reject then
                    raise (browserRejected tag part)

                index <- index + 1
            | RegionOp (regionIndex, region, ancestors) ->
                if isNull partPath then
                    partPath <- if part = 0 then path else path.Child (part - 1)

                let outer = backend.Ancestors
                backend.Ancestors <- List.append ancestors outer

                try
                    match backend.Heads with
                    | null -> this.Region (scope, region, instance, partPath, regionIndex)
                    | heads ->
                        heads.Open ()

                        try
                            this.Region (scope, region, instance, partPath, regionIndex)
                        finally
                            heads.Close ()
                finally
                    backend.Ancestors <- outer

                index <- index + 1
            | EnterOp child ->
                let inlined = plan.Children[child - 1]
                let props = inlined.Props.Invoke parts[inlined.Owner]
                let resume = index + 1
                let next = ref resume

                let write (scope: ServerScope) (childSetup: Setup) =
                    let identity = backend.Identity
                    identity.Enter (path, InstanceStep.Child, child - 1, 0)

                    try
                        parts[child] <- inlined.Definition.Setup.Invoke (ownSetup identity childSetup, props)
                    finally
                        identity.Leave ()

                    next.Value <- this.Run (scope, childSetup, plan, parts, path, child, resume)

                scope.Inline (
                    setup,
                    fun scope childSetup ->
                        match backend.Heads with
                        | null -> write scope childSetup
                        | heads ->
                            let outer = heads.EnterGroup ()

                            try
                                write scope childSetup
                            finally
                                heads.LeaveGroup outer
                )

                index <- next.Value
            | LeaveOp ->
                running <- false
                index <- index + 1

        index

    /// <summary>Writes a region whose rows sit under <c>parent</c>, the path of its owning part, at index <c>regionIndex</c>.</summary>
    member private this.Region(scope: ServerScope, region: Region, instance: obj, parent: InstancePath, regionIndex: int) =
        let items = Array.copy (region.Items.Invoke instance)
        Projection.ensureUniqueKeys region.Key.Invoke items

        match region.Row with
        | BoundaryRow -> this.Boundary (scope, unbox<BoundaryViews>(KeyedItems.value items[0]), parent, regionIndex)
        | ViewRow ->
            backend.Append "<!--[-->"

            for row in 0 .. items.Length - 1 do
                let view = unbox<Program.View>(KeyedItems.value items[row])
                this.View (scope, (fun target -> view.Invoke target), parent, regionIndex, row)

            backend.Append "<!--]-->"
        | ArmRow ->
            if items.Length <> 1 then
                raise (
                    InvalidOperationException (
                        "An arm region writes exactly one arm; it read "
                        + string items.Length
                    )
                )

            let value = KeyedItems.value items[0]
            let arm = unbox<Arm> value
            let plan = serverPlan arm.Definition
            backend.Append ("<!--[a:" + plan.Hash + "-->")
            this.Row (scope, plan, arm.Definition, arm.Reader.Invoke (fun () -> value), parent, regionIndex, 0)
            backend.Append "<!--]-->"
        | ItemRow (definition, props) ->
            let plan = serverPlan definition
            let separated = plan.LastIsText && plan.FirstIsText
            backend.Append "<!--[-->"

            for row in 0 .. items.Length - 1 do
                if separated && row > 0 then
                    backend.Append "<!--!-->"

                let item = items[row]
                this.Row (scope, plan, definition, props.Invoke (fun () -> item), parent, regionIndex, row)

            backend.Append "<!--]-->"

    /// <summary>Writes row <c>row</c> of region <c>regionIndex</c>; a row whose plan has no roots writes <c>&lt;!--ranvier:row--&gt;</c>.</summary>
    member private this.Row
        (scope: ServerScope, plan: ServerPlan, definition: TemplateComponent, props: obj, parent: InstancePath, regionIndex: int, row: int)
        =
        this.Write (scope, definition, props, parent, InstanceStep.Row, regionIndex, row)

        if plan.RootCount = 0 then
            backend.Append "<!--ranvier:row-->"

    /// <summary>
    /// Writes a boundary whose views sit under <c>parent</c> in region <c>regionIndex</c>: the content at row 0 in an
    /// attempt of <c>scope</c>, between <c>&lt;!--[c--&gt;</c> and <c>&lt;!--]--&gt;</c>. When the attempt raises, its
    /// HTML and head entries are dropped and row 1 under <c>scope</c> takes the pending view after <c>&lt;!--[p--&gt;</c>
    /// for a <c>NotReadyException</c>, or else the error view after <c>&lt;!--[e--&gt;</c>.
    /// </summary>
    member private this.Boundary(scope: ServerScope, views: BoundaryViews, parent: InstancePath, regionIndex: int) =
        let sink = backend.Sink
        let heads = backend.Heads
        let mark = sink.Mark ()
        let entries = if isNull heads then 0 else heads.Count

        // Each view is a head container of its own.
        let view (scope: ServerScope) (render: Backend -> unit) (row: int) =
            match heads with
            | null -> this.View (scope, render, parent, regionIndex, row)
            | heads ->
                heads.Open ()

                try
                    this.View (scope, render, parent, regionIndex, row)
                finally
                    heads.Close ()

        backend.Append "<!--[c-->"

        let failure =
            try
                scope.Attempt (fun attempt -> view attempt (views.Content { IsPending = fun () -> false }) 0)
                None
            with error ->
                Some error

        match failure with
        | None -> ()
        | Some error ->
            sink.Truncate mark

            if not (isNull heads) then
                heads.Truncate entries

            match error with
            | NotReadyException _ ->
                backend.Append "<!--[p-->"
                view scope views.Pending 1
            | error ->
                backend.Append "<!--[e-->"
                view scope (views.Failed { Error = error; Retry = ignore }) 1

        backend.Append "<!--]-->"

/// <summary>The head entry of <c>head</c> for <c>instance</c>.</summary>
let headEntry (head: HeadTemplate) (instance: obj) : HeadEntry =
    {
        HeadEntry.Kind = head.Kind
        Key = head.Key
        Attributes = head.Attributes
        Readers =
            head.Slots
            |> Array.map (fun (name, read) -> name, (fun () -> unbox<string option>(read.Invoke instance)))
        Title =
            if isNull head.Title then
                Unchecked.defaultof<_>
            else
                fun () ->
                    let value = unbox<string>(head.Title.Invoke instance)
                    if isNull value then "" else value
    }

/// <summary>
/// The modifier of a property part with the value returned by <c>read</c>. On an <c>HtmlBackend</c> an attribute serialisation
/// writes its attribute, a browser-only property is a browser part, and a child-text property is left to
/// <c>childTextView</c>; on any other backend the writer receives the value, once when <c>dynamic</c> is false.
/// </summary>
let private propertyModifier (property: PortableProperty) (dynamic: bool) (read: unit -> obj) : ModifierAction =
    fun backend ->
        match backend, property.Html with
        | :? HtmlBackend, AsAttribute (attribute, serialise) -> backend.OptionalAttribute (attribute, serialise.Invoke (read ()))
        | :? HtmlBackend, AsChildText -> ()
        | _ when dynamic -> backend.ReactiveProperty (read, property.Write)
        | _ -> backend.Property (fun target -> property.Write.Invoke (target, read ()))

/// <summary>The view of a child-text property: its value as escaped child text on an <c>HtmlBackend</c>, and empty on any other backend.</summary>
let private childTextView (ops: Operations<'view>) (read: unit -> obj) : 'view =
    ops.Embed (fun backend ->
        match backend with
        | :? HtmlBackend ->
            match unbox<string>(read ()) with
            | null -> ()
            | value -> backend.Text value
        | _ -> ())

/// <summary>
/// The view of a raw-HTML part: its markup written verbatim on an <c>HtmlBackend</c>, after a line feed when
/// <c>leadingLineFeed</c> is set and the markup is not empty, and empty on any other backend.
/// </summary>
let private contentView (ops: Operations<'view>) (leadingLineFeed: bool) (read: unit -> obj) : 'view =
    ops.Embed (fun backend ->
        match backend with
        | :? HtmlBackend as html ->
            let markup = markupOf (read ())

            if hasMarkerComment markup then
                raise (InvalidOperationException "Raw HTML cannot contain a comment that starts with [ or equals ]")

            if leadingLineFeed && markup.Length > 0 then
                html.Sink.Append "\n"

            html.Sink.Append markup
        | _ -> ())

let rec private expandNode (ops: Operations<'view>) (instance: obj) (node: TemplateNode) : 'view =
    match node with
    | TextNode value -> ops.Text value
    | TextSlotNode slot -> ops.Bind (fun left right -> slot.Same.Invoke (box left, box right)) (fun () -> unbox<string>(slot.Read.Invoke instance))
    | FragmentNode children -> ops.Fragment (Array.map (expandNode ops instance) children)
    | ElementNode (tag, attributes, properties, slots, events, placed, children) ->
        match refusal tag children with
        | null -> ()
        | message -> raise (InvalidOperationException message)

        let modifiers: ModifierAction array =
            [|
                for key, value in attributes -> Attribute (key, value)
                for property, value in properties -> propertyModifier property false (fun () -> value)
                for slot in slots ->
                    match slot.Target with
                    | AttributeTarget key -> ReactiveAttribute (key, fun () -> unbox<string option>(slot.Read.Invoke instance))
                    | PropertyTarget property -> propertyModifier property true (fun () -> slot.Read.Invoke instance)
                    | ContentTarget ->
                        fun (backend: Backend) ->
                            match backend with
                            | :? HtmlBackend -> ()
                            | _ -> backend.ReactiveProperty ((fun () -> box (markupOf (slot.Read.Invoke instance))), innerHtml)
                    | TextTarget -> invalidOp "A text slot is a child node"
                for eventName, handler in events -> listenModifier eventName (fun event -> handler.Invoke (instance, event))
                for kind, hook in placed ->
                    fun (backend: Backend) -> backend.Placed (kind, Func<obj, obj, obj>(fun node context -> hook.Invoke (instance, node, context)))
            |]

        // The child content written by the server plan for child-text properties and raw-HTML parts.
        let partContent =
            [|
                for property, value in properties do
                    if isChildText property then
                        childTextView ops (fun () -> value)
                for slot in slots do
                    match slot.Target with
                    | PropertyTarget property when isChildText property -> childTextView ops (fun () -> slot.Read.Invoke instance)
                    | ContentTarget -> contentView ops (dropsLeadingLineFeed (name tag)) (fun () -> slot.Read.Invoke instance)
                    | _ -> ()
            |]

        ops.Element tag modifiers (Array.append partContent (Array.map (expandNode ops instance) children))
    | RegionNode region ->
        match region.Row with
        | ViewRow
        | BoundaryRow ->
            let item = KeyedItems.value ((region.Items.Invoke instance)[0])
            expandRow ops region.Row (fun () -> item)
        | ArmRow -> ops.Switch (fun () -> Array.map unbox<obj * obj> (region.Items.Invoke instance)) (expandRow ops region.Row)
        | ItemRow _ ->
            ops.Keyed
                {
                    Read = fun () -> region.Items.Invoke instance
                    Key = region.Key
                }
                (expandRow ops region.Row)
    | ChildNode (definition, props) -> expandWith ops definition (props.Invoke instance)
    | HeadNode head -> ops.Embed (fun backend -> backend.Head (headEntry head instance))

/// <summary>Expands a region row through the operations; the reader returns the row's current item.</summary>
and private expandRow (ops: Operations<'view>) (row: RegionRow) (current: unit -> obj) : 'view =
    match row with
    | ViewRow -> ops.Embed (fun backend -> backend.Hole (rowRenderer row current))
    | BoundaryRow -> ops.Embed (rowRenderer row current)
    | ArmRow ->
        let arm = unbox<Arm>(current ())
        expandWith ops arm.Definition (arm.Reader.Invoke current)
    | ItemRow (definition, props) -> expandWith ops definition (props.Invoke current)

/// <summary>Expands a definition into any view representation through its operations.</summary>
and expandWith (ops: Operations<'view>) (definition: TemplateComponent) (props: obj) : 'view =
    ops.Component (fun context -> expandNode ops (definition.Setup.Invoke (context, props)) definition.Root)

/// <summary>A component whose template is built once per definition; setup runs per instance.</summary>
[<Sealed>]
type Component<'props> internal (setup: Func<Setup, obj, obj>, root: TemplateNode) =
    inherit TemplateComponent(setup, root)

    override this.Expand(backend, props) =
        Program.run (expandWith Program.ops (this :> TemplateComponent) props) backend

    override this.RenderHtml(backend, props) =
        let writer =
            match backend.Writer with
            | null ->
                // The first template of an Html.render backend is its root instance.
                let writer = ServerWriter backend
                writer.Pend (null, InstanceStep.Root, 0, 0)
                backend.Writer <- writer
                writer
            | writer -> unbox<ServerWriter> writer

        let scope =
            match backend.Scope with
            | null ->
                let scope = HtmlScope backend
                backend.Scope <- scope
                scope :> ServerScope
            | scope -> unbox<ServerScope> scope

        writer.Embed (scope, this, props)

/// <summary>One part of an element in a template for instances of type <c>'inst</c>.</summary>
[<NoEquality; NoComparison>]
type Part<'native, 'inst> =
    internal
    | StaticAttribute of string * string
    | StaticProperty of PortableProperty * obj
    | DynamicPart of Slot
    | EventPart of string * Func<obj, obj, unit>
    | PlacedPart of PlacedKind * Func<obj, obj, obj, obj>
    | NoPart

/// <summary>A template subtree for instances of type <c>'inst</c>.</summary>
[<NoEquality; NoComparison>]
type TemplateView<'inst> = internal TemplateView of TemplateNode

/// <summary>A reader of one fixed signal or memo of an instance.</summary>
[<NoEquality; NoComparison>]
type Source<'inst, 'value> = internal Source of Func<obj, obj>

let private slot target mode (read: 'inst -> 'value) same =
    {
        Slot.Target = target
        Mode = mode
        Read = Func<obj, obj>(fun instance -> box (read (unbox instance)))
        Same = same
    }

let private sourceSlot target (Source read: Source<'inst, 'value>) same =
    {
        Slot.Target = target
        Mode = DirectMode
        Read = read
        Same = same
    }

let private portable (html: PropertySerialization) (write: 'native -> 'value -> unit) =
    {
        Write = Func<obj, obj, unit>(fun node value -> write (unbox node) (unbox value))
        Html = html
    }

let private nodeOf (TemplateView node) = node

let private elementNode tag (parts: Part<'native, 'inst> array) (children: TemplateView<'inst> array) =
    name tag |> ignore
    let attributes = ResizeArray ()
    let properties = ResizeArray ()
    let slots = ResizeArray ()
    let events = ResizeArray ()
    let placed = ResizeArray ()

    for part in parts do
        match part with
        | StaticAttribute (name, value) ->
            validateAttribute name
            attributes.Add (name, value)
        | StaticProperty (property, value) -> properties.Add (property, value)
        | DynamicPart slot -> slots.Add slot
        | EventPart (name, handler) -> events.Add (name, handler)
        | PlacedPart (kind, hook) -> placed.Add (kind, hook)
        | NoPart -> ()

    let rawContents =
        slots
        |> Seq.filter (fun slot ->
            match slot.Target with
            | ContentTarget -> true
            | _ -> false)
        |> Seq.length

    let contents =
        rawContents
        + (placed
           |> Seq.filter (fun (kind, _) -> kind = PlacedKind.Widget)
           |> Seq.length)

    if contents > 1 then
        raise (ArgumentException ("An element takes at most one content part", "parts"))

#if !FABLE_COMPILER
    // Attribute names that differ only by case raise in the server's definition path only.
    let attributeNames = ResizeArray<string>()

    for attribute, _ in attributes do
        attributeNames.Add attribute

    for slot in slots do
        match slot.Target with
        | AttributeTarget attribute -> attributeNames.Add attribute
        | _ -> ()

    for first in 0 .. attributeNames.Count - 1 do
        for second in first + 1 .. attributeNames.Count - 1 do
            let earlier = attributeNames[first]
            let later = attributeNames[second]

            if
                earlier <> later
                && earlier.ToLowerInvariant () = later.ToLowerInvariant ()
            then
                raise (
                    ArgumentException (
                        "An element takes "
                        + earlier
                        + " and "
                        + later
                        + ", which differ only by case",
                        "parts"
                    )
                )
#endif

    if contents = 1 && children.Length > 0 then
        raise (ArgumentException ("An element with a content part has no template children", "children"))

    if
        rawContents = 1
        && (isVoid tag
            || rawText tag
            || (match (name tag).ToLowerInvariant() with
                | "template"
                | "textarea"
                | "title" -> true
                | _ -> false))
    then
        raise (ArgumentException ("Raw HTML requires an element whose children the HTML parser builds from markup", "parts"))

    TemplateView (
        ElementNode (
            tag,
            attributes.ToArray (),
            properties.ToArray (),
            slots.ToArray (),
            events.ToArray (),
            placed.ToArray (),
            Array.map nodeOf children
        )
    )

/// <summary>Part constructors used by the generated template catalogue.</summary>
module internal TemplateParts =
    let element tag (parts: Part<'native, 'inst> array) (children: TemplateView<'inst> array) : TemplateView<'inst> =
        elementNode tag parts children

    let attribute name (value: string) : Part<'native, 'inst> =
        validateAttribute name
        StaticAttribute (name, value)

    let optionalAttribute name (value: string option) : Part<'native, 'inst> =
        validateAttribute name

        match value with
        | Some value -> StaticAttribute (name, value)
        | None -> NoPart

    let readAttribute name (read: 'inst -> string option) : Part<'native, 'inst> =
        validateAttribute name
        DynamicPart (slot (AttributeTarget name) RenderMode read primitiveSame)

    let sourceAttribute name (source: Source<'inst, string option>) : Part<'native, 'inst> =
        validateAttribute name
        DynamicPart (sourceSlot (AttributeTarget name) source primitiveSame)

    let mapSource (map: 'value -> 'next) (Source read: Source<'inst, 'value>) : Source<'inst, 'next> =
        Source (Func<obj, obj>(fun instance -> box (map (unbox<'value>(read.Invoke instance)))))

    /// <summary>Wraps a string as an attribute value; <c>null</c> gives <c>None</c>.</summary>
    let present (value: string) =
        if isNull value then None else Some value

    let presence enabled =
        if enabled then Some "" else None

    let ariaText enabled =
        if enabled then "true" else "false"

    let ariaOption (value: bool option) =
        Option.map ariaText value

    let property (html: PropertySerialization) (write: 'native -> 'value -> unit) (value: 'value) : Part<'native, 'inst> =
        StaticProperty (portable html write, box value)

    let readProperty (html: PropertySerialization) (write: 'native -> 'value -> unit) (read: 'inst -> 'value) : Part<'native, 'inst> =
        DynamicPart (slot (PropertyTarget (portable html write)) RenderMode read primitiveSame)

    let sourceProperty (html: PropertySerialization) (write: 'native -> 'value -> unit) (source: Source<'inst, 'value>) : Part<'native, 'inst> =
        DynamicPart (sourceSlot (PropertyTarget (portable html write)) source primitiveSame)

    /// <summary>
    /// An event part. The handler runs unbatched: a handler that groups several writes wraps them in <c>Api.batch</c>,
    /// and effects then run once, after the outermost batch.
    /// </summary>
    let event name (handler: 'inst -> 'event -> unit) : Part<'native, 'inst> =
        EventPart (name, Func<obj, obj, unit>(fun instance event -> handler (unbox instance) (unbox event)))

    let text (value: string) : TemplateView<'inst> =
        TemplateView (TextNode value)

    let readText (read: 'inst -> string) : TemplateView<'inst> =
        TemplateView (TextSlotNode (slot TextTarget RenderMode read primitiveSame))

    let sourceText (source: Source<'inst, string>) : TemplateView<'inst> =
        TemplateView (TextSlotNode (sourceSlot TextTarget source primitiveSame))

    let arm (definition: Component<unit -> 'payload>) (payload: 'payload) : Arm =
        let template = definition :> TemplateComponent

        if isNull template.armReader then
            let reader =
                Func<(unit -> obj), obj>(fun current -> box (fun () -> unbox<'payload> (unbox<Arm>(current ())).Payload))

#if FABLE_COMPILER
            template.armReader <- reader
#else
            Threading.Interlocked.CompareExchange (&template.armReader, reader, null)
            |> ignore
#endif

        {
            Arm.Definition = template
            Payload = box payload
            Reader = template.ArmReader
        }

/// <summary>Attribute parts: a value is static, a reader or source is dynamic.</summary>
[<Sealed>]
type AttributeBuilder<'inst> internal () =
    member _.attribute(name: string, value: string) : Part<'native, 'inst> =
        TemplateParts.attribute name value

    member _.attribute(name: string, read: 'inst -> string option) : Part<'native, 'inst> =
        TemplateParts.readAttribute name read

    member _.attribute(name: string, source: Source<'inst, string option>) : Part<'native, 'inst> =
        TemplateParts.sourceAttribute name source

/// <summary>Property parts: a value is written once per instance, a reader or source is dynamic.</summary>
/// <remarks>
/// A writer sets properties and leaves the element's child nodes intact: nodes moved into a template clone by a writer
/// bypass the mount's ownership checks. A part without a <c>PropertySerialization</c> is browser-only.
/// </remarks>
[<Sealed>]
type PropertyBuilder<'inst> internal () =
    member _.propertyStatic(write: 'native -> 'value -> unit, value: 'value) : Part<'native, 'inst> =
        TemplateParts.property BrowserOnly write value

    member _.propertyWith(write: 'native -> 'value -> unit, read: 'inst -> 'value, same: Func<obj, obj, bool>) : Part<'native, 'inst> =
        DynamicPart (slot (PropertyTarget (portable BrowserOnly write)) RenderMode read same)

    member _.propertyFrom(write: 'native -> 'value -> unit, source: Source<'inst, 'value>, same: Func<obj, obj, bool>) : Part<'native, 'inst> =
        DynamicPart (sourceSlot (PropertyTarget (portable BrowserOnly write)) source same)

    member _.propertyStatic(write: 'native -> 'value -> unit, value: 'value, html: PropertySerialization) : Part<'native, 'inst> =
        TemplateParts.property html write value

    member _.propertyWith
        (write: 'native -> 'value -> unit, read: 'inst -> 'value, same: Func<obj, obj, bool>, html: PropertySerialization)
        : Part<'native, 'inst> =
        DynamicPart (slot (PropertyTarget (portable html write)) RenderMode read same)

    member _.propertyFrom
        (write: 'native -> 'value -> unit, source: Source<'inst, 'value>, same: Func<obj, obj, bool>, html: PropertySerialization)
        : Part<'native, 'inst> =
        DynamicPart (sourceSlot (PropertyTarget (portable html write)) source same)

/// <summary>Event parts: one shared handler per template, given the instance and the event.</summary>
[<Sealed>]
type EventBuilder<'inst> internal () =
    /// <summary>
    /// Handles <c>eventName</c>. The handler runs unbatched: a handler that groups several writes wraps them in
    /// <c>Api.batch</c>, and effects then run once, after the outermost batch.
    /// </summary>
    member _.on(eventName: string, handler: 'inst -> Event -> unit) : Part<'native, 'inst> =
        TemplateParts.event eventName handler

let private placedHook (hook: 'inst -> 'native -> (unit -> unit)) =
    Func<obj, obj, obj, obj>(fun instance node _ -> box (hook (unbox instance) (unbox node)))

/// <summary>Hook parts: code that runs once the element is placed in the mount's host.</summary>
[<Sealed>]
type HookBuilder<'inst> internal () =
    /// <summary>Runs once the element is placed; the returned cleanup runs when the instance is disposed, before its nodes leave.</summary>
    member _.mounted(hook: 'inst -> 'native -> (unit -> unit)) : Part<'native, 'inst> =
        PlacedPart (PlacedKind.Mounted, placedHook hook)

    /// <summary>
    /// Sets the <c>NodeRef</c> selected from the instance to the element while the element is placed, and to <c>None</c>
    /// once the instance is disposed.
    /// </summary>
    member _.ref(select: 'inst -> NodeRef<'native>) : Part<'native, 'inst> =
        PlacedPart (PlacedKind.Mounted, Func<obj, obj, obj, obj>(fun instance node _ -> box ((select (unbox instance)).Place node)))

    /// <summary>The widget owns the element's content once placed, and the template declares the element empty. The cleanup destroys the widget.</summary>
    member _.widget(attach: 'inst -> 'native -> (unit -> unit)) : Part<'native, 'inst> =
        PlacedPart (PlacedKind.Widget, placedHook attach)

    /// <summary>The integration borrows the element once placed; the cleanup removes what it added. The integration keeps the element under its parent; moving it is reported after the cleanup.</summary>
    member _.lend(attach: 'inst -> 'native -> (unit -> unit)) : Part<'native, 'inst> =
        PlacedPart (PlacedKind.Lent, placedHook attach)

    /// <summary>
    /// Runs once the element is placed, with whether the element was adopted from server HTML and whether its row or arm
    /// was created by a region pass after the region's first. The returned cleanup runs when the instance is disposed.
    /// The server omits the hook.
    /// </summary>
    member _.motion(hook: Func<'inst, 'native, MotionContext, unit -> unit>) : Part<'native, 'inst> =
        PlacedPart (
            PlacedKind.Motion,
            Func<obj, obj, obj, obj>(fun instance node flags ->
                let flags: int = unbox flags

                box (
                    hook.Invoke (
                        unbox instance,
                        unbox node,
                        {
                            Hydrated = flags &&& 1 <> 0
                            Inserted = flags &&& 2 <> 0
                            ExitPending = fun () -> Departures.exitFollows (unbox node)
                        }
                    )
                ))
        )

    /// <summary>
    /// On a row or arm root element: when the row is retired, its node stays in place with its content and <c>exit</c>
    /// runs with <c>finish</c>; the node leaves when <c>finish</c> is called. The returned function cancels the exit when
    /// the removal is forced. An exit applies once the element is placed. The server omits the hook.
    /// </summary>
    member _.exit(exit: Func<'inst, 'native, unit -> unit, unit -> unit>) : Part<'native, 'inst> =
        PlacedPart (
            PlacedKind.Exit,
            Func<obj, obj, obj, obj>(fun instance node _ ->
                box (Departures.register (unbox node) (Func<_, _>(fun finish -> exit.Invoke (unbox instance, unbox node, finish)))))
        )

    /// <summary>
    /// On a region's parent element: <c>Before</c> runs before the first region pass under the element in a flush,
    /// <c>After</c> once the pass's nodes and commits are in place. The server omits the hook.
    /// </summary>
    member _.reflow(measure: Func<'inst, 'native, Reflow>) : Part<'native, 'inst> =
        PlacedPart (
            PlacedKind.Reflow,
            Func<obj, obj, obj, obj>(fun instance node _ ->
                box (Departures.registerReflow (unbox node) (measure.Invoke (unbox instance, unbox node))))
        )

    /// <summary>
    /// <c>first</c> and <c>second</c> on one element as one part: the hook of <c>second</c> runs after the hook of
    /// <c>first</c>, and the cleanup of <c>second</c> before the cleanup of <c>first</c>. Both are hook parts, at most one
    /// of them other than a motion hook, and the part has that hook's kind; other parts raise. When the hook of <c>second</c>
    /// raises, the cleanup of <c>first</c> runs.
    /// </summary>
    member _.both(first: Part<'native, 'inst>, second: Part<'native, 'inst>) : Part<'native, 'inst> =
        match first, second with
        | PlacedPart (PlacedKind.Motion, firstHook), PlacedPart (kind, secondHook)
        | PlacedPart (kind, firstHook), PlacedPart (PlacedKind.Motion, secondHook) ->
            PlacedPart (
                kind,
                Func<obj, obj, obj, obj>(fun instance node flags ->
                    let before: unit -> unit = unbox (firstHook.Invoke (instance, node, flags))

                    let after: unit -> unit =
                        try
                            unbox (secondHook.Invoke (instance, node, flags))
                        with _ ->
                            if not (isNull (box before)) then
                                before ()

                            reraise ()

                    box (fun () ->
                        try
                            if not (isNull (box after)) then
                                after ()
                        finally
                            if not (isNull (box before)) then
                                before ()))
            )
        | _ -> invalidArg "second" "Hooks.both combines two hook parts, at most one of them other than a motion hook"

/// <summary>Raw-HTML parts: the element's whole content, parsed without escaping.</summary>
[<Sealed>]
type UnsafeBuilder<'inst> internal () =
    /// <summary>
    /// The element's content, parsed as HTML without escaping. The template declares the element empty. Scripts,
    /// handlers and URLs in the markup run as <c>UnsafeHtml</c> states for the client and for server HTML.
    /// </summary>
    member _.html(content: UnsafeHtml) : Part<'native, 'inst> =
        DynamicPart
            {
                Slot.Target = ContentTarget
                Mode = RenderMode
                Read = Func<obj, obj>(fun _ -> box content)
                Same = contentSame
            }

    /// <summary>
    /// The element's content, parsed as HTML without escaping and parsed again when the reader returns different
    /// markup. Scripts, handlers and URLs in the markup run as <c>UnsafeHtml</c> states for the client and for server HTML.
    /// </summary>
    member _.html(read: 'inst -> UnsafeHtml) : Part<'native, 'inst> =
        DynamicPart (slot ContentTarget RenderMode read contentSame)

let private metaKeys = [| "name"; "property"; "http-equiv"; "charset" |]

let private linkKeys = [| "rel" |]

/// <summary>The attribute holding a head element's registry key.</summary>
let private headMark = "data-ranvier-head"

let private rejectMark (name: string) =
    if String.Equals (name, headMark, StringComparison.OrdinalIgnoreCase) then
        raise (ArgumentException ("A document entry cannot set data-ranvier-head; the registry writes it", "parts"))

/// <summary>
/// The static attributes and attribute slots of a head entry's parts; the attributes in <c>keys</c> are static, and a
/// <c>data-ranvier-head</c> attribute raises.
/// </summary>
let private headParts (keys: string array) (parts: Part<'native, 'inst> array) =
    let attributes = ResizeArray<string * string>()
    let slots = ResizeArray<string * Func<obj, obj>>()

    for part in parts do
        match part with
        | StaticAttribute (name, value) ->
            rejectMark name
            attributes.Add ((name, value))
        | DynamicPart slot ->
            match slot.Target with
            | AttributeTarget name ->
                rejectMark name

                if Array.contains name keys then
                    raise (ArgumentException ("A document entry's key attribute must be static", "parts"))

                slots.Add ((name, slot.Read))
            | _ -> raise (ArgumentException ("Document entries take attribute parts only", "parts"))
        | NoPart -> ()
        | _ -> raise (ArgumentException ("Document entries take attribute parts only", "parts"))

    attributes.ToArray (), slots.ToArray ()

let private staticValue (name: string) (attributes: (string * string) array) =
    attributes
    |> Array.tryFind (fun (key, _) -> key = name)
    |> Option.map snd

let private headView kind key (attributes, slots) title : TemplateView<'inst> =
    TemplateView (
        HeadNode
            {
                HeadTemplate.Kind = kind
                Key = key
                Attributes = attributes
                Slots = slots
                Title = title
            }
    )

/// <summary>
/// The registry key of a link: <c>link:canonical</c> for a static canonical <c>rel</c>, <c>link:key:</c> followed by
/// the explicit key otherwise, and null for an unkeyed link. An explicit key and a canonical link have distinct keys.
/// </summary>
let private linkKey (attributes: (string * string) array) (explicit: string) =
    if staticValue "rel" attributes = Some "canonical" then
        "link:canonical"
    elif isNull explicit then
        null
    else
        "link:key:" + explicit

/// <summary>Entries for the document's head: shown once the instance is placed, withdrawn when it is disposed.</summary>
/// <remarks>Per key, the most recently shown live entry is in the head; unkeyed links are each in the head while live.</remarks>
[<Sealed>]
type DocumentBuilder<'inst> internal () =
    member _.title(value: string) : TemplateView<'inst> =
        headView HeadKind.Title "title" ([||], [||]) (Func<obj, obj>(fun _ -> box value))

    member _.title(read: 'inst -> string) : TemplateView<'inst> =
        headView HeadKind.Title "title" ([||], [||]) (Func<obj, obj>(fun instance -> box (read (unbox instance))))

    /// <summary>A meta element, keyed by its static name, property, http-equiv or charset attribute.</summary>
    member _.meta(parts: Part<HTMLMetaElement, 'inst> array) : TemplateView<'inst> =
        let attributes, slots = headParts metaKeys parts

        let key =
            metaKeys
            |> Array.tryPick (fun name ->
                staticValue name attributes
                |> Option.map (fun value ->
                    if name = "charset" then
                        "meta:charset"
                    else
                        "meta:" + name + "=" + value))
            |> Option.defaultWith (fun () ->
                raise (ArgumentException ("A meta entry needs a static name, property, http-equiv or charset attribute", "parts")))

        headView HeadKind.Meta key (attributes, slots) null

    /// <summary>A link element, keyed by its <c>rel</c> when that is <c>canonical</c> and unkeyed otherwise.</summary>
    member _.link(parts: Part<HTMLLinkElement, 'inst> array) : TemplateView<'inst> =
        let attributes, slots = headParts linkKeys parts
        headView HeadKind.Link (linkKey attributes null) (attributes, slots) null

    /// <summary>A link element, keyed by its <c>rel</c> when that is <c>canonical</c> and by <c>key</c> otherwise.</summary>
    member _.link(key: string, parts: Part<HTMLLinkElement, 'inst> array) : TemplateView<'inst> =
        if String.IsNullOrEmpty key then
            raise (ArgumentException ("A link key cannot be empty", "key"))

        let attributes, slots = headParts linkKeys parts
        headView HeadKind.Link (linkKey attributes key) (attributes, slots) null

#if DEBUG && FABLE_COMPILER
/// <summary>The number of region item reads in progress; <c>Component.define</c> raises while it is nonzero.</summary>
module internal RegionReads =
    let mutable depth = 0
#endif

/// <summary>Runs a region's item read; Fable Debug builds count it in <c>RegionReads.depth</c>.</summary>
let inline private regionRead ([<InlineIfLambda>] read: unit -> 'value) : 'value =
#if DEBUG && FABLE_COMPILER
    RegionReads.depth <- RegionReads.depth + 1

    try
        read ()
    finally
        RegionReads.depth <- RegionReads.depth - 1
#else
    read ()
#endif

/// <summary>Builds a template for instances of type <c>'inst</c>; dynamic parts receive the instance as their argument.</summary>
[<Sealed>]
type TemplateBuilder<'inst> internal () =
    let attrs = AttributeBuilder<'inst>()
    let props = PropertyBuilder<'inst>()
    let events = EventBuilder<'inst>()
    let hooks = HookBuilder<'inst>()
    let unsafe = UnsafeBuilder<'inst>()
    let documentEntries = DocumentBuilder<'inst>()

    member _.Attrs = attrs
    member _.Props = props
    member _.Events = events
    member _.Hooks = hooks
    member _.Unsafe = unsafe
    member _.Document = documentEntries

    member _.element (tag: Tag) (parts: Part<'native, 'inst> array) (children: TemplateView<'inst> array) : TemplateView<'inst> =
        elementNode tag parts children

    member _.fragment(children: TemplateView<'inst> array) : TemplateView<'inst> =
        TemplateView (FragmentNode (Array.map nodeOf children))

    member _.text(value: string) : TemplateView<'inst> =
        TemplateView (TextNode value)

    member _.text(read: 'inst -> string) : TemplateView<'inst> =
        TemplateView (TextSlotNode (slot TextTarget RenderMode read primitiveSame))

    member _.text(source: Source<'inst, string>) : TemplateView<'inst> =
        TemplateView (TextSlotNode (sourceSlot TextTarget source primitiveSame))

    member _.from(accessor: 'inst -> Signal<'value>) : Source<'inst, 'value> =
        Source (Func<obj, obj>(fun instance -> box (accessor (unbox instance)).Value))

    member _.from(accessor: 'inst -> Memo<'value>) : Source<'inst, 'value> =
        Source (Func<obj, obj>(fun instance -> box (accessor (unbox instance)).Value))

    member _.isolated(part: Part<'native, 'inst>) : Part<'native, 'inst> =
        match part with
        | DynamicPart slot -> DynamicPart { slot with Mode = IsolatedMode }
        | _ -> invalidArg "part" "Only dynamic parts can be isolated"

    member _.isolated(view: TemplateView<'inst>) : TemplateView<'inst> =
        match view with
        | TemplateView (TextSlotNode slot) -> TemplateView (TextSlotNode { slot with Mode = IsolatedMode })
        | _ -> invalidArg "view" "Only text slots can be isolated"

    /// <summary>A view hole: renders the view read from the instance as it is created, between region markers.</summary>
    /// <remarks>The reader runs once per instance, untracked.</remarks>
    member _.view(read: 'inst -> Program.View) : TemplateView<'inst> =
        TemplateView (
            RegionNode
                {
                    Items = Func<obj, obj array>(fun instance -> [| box ((null: obj), box (read (unbox instance))) |])
                    Key = KeyedItems.pairKey
                    Row = ViewRow
                }
        )

    /// <summary>
    /// A boundary between region markers. <c>content</c> receives the boundary's status and is shown once every binding
    /// under it has read a value; <c>pending</c> is shown until then, and <c>failed</c> replaces either after a failure
    /// under the content.
    /// </summary>
    member _.boundary (pending: Component<unit>) (failed: Component<BoundaryFailure>) (content: Component<BoundaryStatus>) : TemplateView<'inst> =
        let views =
            {
                Pending = fun backend -> backend.Template (pending, box ())
                Failed = fun failure backend -> backend.Template (failed, box failure)
                Content = fun status backend -> backend.Template (content, box status)
            }

        TemplateView (
            RegionNode
                {
                    Items = Func<obj, obj array>(fun _ -> [| box ((null: obj), box views) |])
                    Key = KeyedItems.pairKey
                    Row = BoundaryRow
                }
        )

    /// <summary>An arm that renders <c>definition</c>; its props read the payload of the arm currently selected.</summary>
    member _.arm (definition: Component<unit -> 'payload>) (payload: 'payload) : Arm =
        TemplateParts.arm definition payload

    /// <summary>Renders the arm that <c>arms</c> selects for the instance's union value, between region markers.</summary>
    /// <remarks>Arms are compared by key under <c>=</c>; a <c>matchWith</c> key is the arm's definition. Selecting another definition remounts, and selecting the same definition updates the payload read by its props. Writing <c>arms</c> as a <c>function</c> match lets the compiler check that every case has an arm. A remount runs the previous arm's cleanups and removes its nodes before the new arm is set up.</remarks>
    member _.matchWith (read: 'inst -> 'union) (arms: 'union -> Arm) : TemplateView<'inst> =
        TemplateView (
            RegionNode
                {
                    Items =
                        Func<obj, obj array>(fun instance ->
                            let arm = regionRead (fun () -> arms (read (unbox instance)))
                            [| box (box arm.Definition, box arm) |])
                    Key = KeyedItems.pairKey
                    Row = ArmRow
                }
        )

    /// <summary>Renders the arm selected for the instance's key, between region markers; a key change under <c>=</c> or a change of the selected definition remounts the arm.</summary>
    /// <remarks>An equal key with the same definition keeps the arm and updates the payload read by its props. A remount runs the previous arm's cleanups and removes its nodes before the new arm is set up.</remarks>
    member _.branch<'key when 'key: equality> (key: 'inst -> 'key) (arms: 'key -> Arm) : TemplateView<'inst> =
        TemplateView (
            RegionNode
                {
                    Items =
                        Func<obj, obj array>(fun instance ->
                            let current = key (unbox instance)
                            let arm = regionRead (fun () -> arms current)
                            [| box (box (box current, arm.Definition), box arm) |])
                    Key = KeyedItems.pairKey
                    Row = ArmRow
                }
        )

    /// <summary>Renders one <c>row</c> instance per item, between region markers; items with equal keys under <c>=</c> keep their row, whose props read the current item.</summary>
    /// <remarks>Keys must be pure. An item keeps its key while it is in the list; replace the item to change its key.
    /// Duplicate keys fail before the rows change.</remarks>
    member _.each<'item, 'key when 'key: equality>
        (key: 'item -> 'key)
        (items: 'inst -> 'item array)
        (row: Component<unit -> 'item>)
        : TemplateView<'inst> =
        TemplateView (
            RegionNode
                {
                    Items = Func<obj, obj array>(fun instance -> regionRead (fun () -> KeyedItems.boxAll (items (unbox instance))))
                    Key = Func<obj, obj>(fun item -> box (key (unbox<'item> item)))
                    Row = ItemRow (row :> TemplateComponent, Func<(unit -> obj), obj>(fun current -> box (fun () -> unbox<'item>(current ()))))
                }
        )

    /// <summary>Inlines a child template: its markup joins this template's skeleton, and its setup runs once per instance with props read untracked from the instance.</summary>
    member _.render (definition: Component<'props>) (props: 'inst -> 'props) : TemplateView<'inst> =
        TemplateView (ChildNode (definition :> TemplateComponent, Func<obj, obj>(fun instance -> box (props (unbox instance)))))

module Component =
    /// <summary>Defines a component from per-instance setup and a template built once.</summary>
    /// <remarks><c>setup</c> runs untracked for each instance and returns the instance that dynamic parts receive.</remarks>
    let define (setup: Setup -> 'props -> 'inst) (build: TemplateBuilder<'inst> -> TemplateView<'inst>) : Component<'props> =
#if DEBUG && FABLE_COMPILER
        if RegionReads.depth > 0 then
            raise (InvalidOperationException ("a template definition was created while a region was read; bind it in the template's build function"))
#endif
        let (TemplateView root) = build (TemplateBuilder<'inst>())
        Component<'props>(Func<Setup, obj, obj>(fun context props -> box (setup context (unbox props))), root)

    /// <summary>Renders an instance as a Program view; the DOM backend materialises the compiled template.</summary>
    let render (definition: Component<'props>) (props: 'props) : Program.View =
        Program.View (fun backend -> backend.Template (definition, box props))

    /// <summary>Renders an instance in any representation through the default expansion.</summary>
    let renderWith (ops: Operations<'view>) (definition: Component<'props>) (props: 'props) : 'view =
        expandWith ops (definition :> TemplateComponent) (box props)
