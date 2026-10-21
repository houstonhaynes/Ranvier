namespace Ranvier.View.Twin

open System
open System.Collections.Generic

[<RequireQualifiedAccess>]
type NodeKind =
    | Element
    | Text
    | Comment
    | Fragment
    | Document

/// <summary>A tree operation the DOM rejects; <c>name</c> is the DOM exception name.</summary>
exception TwinDomException of name: string * detail: string with
    override this.Message = this.name + ": " + this.detail

/// <summary>A node of a twin document. Each node has one bridged object, the value Foundation sees.</summary>
[<AbstractClass; AllowNullLiteral>]
type TwinNode internal (kind: NodeKind, owner: TwinDocument) =
    let mutable parent: TwinNode = null
    let mutable firstChild: TwinNode = null
    let mutable lastChild: TwinNode = null
    let mutable nextSibling: TwinNode = null
    let mutable previousSibling: TwinNode = null

    member _.Kind = kind
    member _.Parent = parent
    member _.FirstChild = firstChild
    member _.LastChild = lastChild
    member _.NextSibling = nextSibling
    member _.PreviousSibling = previousSibling

    member this.Document: TwinDocument =
        if isNull owner then this :?> TwinDocument else owner

    /// <summary>The bridged object; set once by the bridge.</summary>
    member val internal Proxy: obj = null with get, set

    member _.Children: TwinNode list =
        let rec collect (node: TwinNode) acc =
            if isNull node then
                List.rev acc
            else
                collect node.NextSibling (node :: acc)

        collect firstChild []

    member internal _.SetLinks(p, n, pr) =
        parent <- p
        nextSibling <- n
        previousSibling <- pr

    member internal _.SetNext n =
        nextSibling <- n

    member internal _.SetPrevious p =
        previousSibling <- p

    member internal _.SetFirst f =
        firstChild <- f

    member internal _.SetLast l =
        lastChild <- l

    member this.Contains(other: TwinNode) =
        let mutable cursor = other
        let mutable found = false

        while not found && not (isNull cursor) do
            found <- obj.ReferenceEquals (cursor, this)
            cursor <- cursor.Parent

        found

    member this.IsConnected =
        let mutable cursor: TwinNode = this

        while not (isNull cursor.Parent) do
            cursor <- cursor.Parent

        cursor.Kind = NodeKind.Document

    member private this.Unlink(child: TwinNode) =
        let previous, next = child.PreviousSibling, child.NextSibling

        if isNull previous then
            this.SetFirst next
        else
            previous.SetNext next

        if isNull next then
            this.SetLast previous
        else
            next.SetPrevious previous

        child.SetLinks (null, null, null)

    member this.RemoveChild(child: TwinNode) : TwinNode =
        if
            isNull child
            || not (obj.ReferenceEquals (child.Parent, this))
        then
            raise (TwinDomException ("NotFoundError", "The node to remove is not a child of this node."))

        this.Unlink child
        child

    member this.Remove() =
        if not (isNull parent) then
            parent.RemoveChild this |> ignore

    /// <summary>
    /// The DOM's pre-insertion validity for <c>node</c> as a child of this node; <c>replaced</c> is the child a
    /// replaceChild removes, which does not count against the document's single element.
    /// </summary>
    member internal this.EnsureInsertable(node: TwinNode, replaced: TwinNode) =
        let hierarchy detail =
            raise (TwinDomException ("HierarchyRequestError", detail))

        match kind with
        | NodeKind.Text
        | NodeKind.Comment -> hierarchy "Text and comment nodes have no children."
        | _ -> ()

        if node.Contains this then
            hierarchy "The new child is an ancestor of the parent."

        if node.Kind = NodeKind.Document then
            hierarchy "A document cannot be inserted."

        if kind = NodeKind.Document then
            let elements (parent: TwinNode) =
                parent.Children
                |> List.filter (fun c ->
                    c.Kind = NodeKind.Element
                    && not (obj.ReferenceEquals (c, replaced))
                    && not (obj.ReferenceEquals (c, node)))
                |> List.length

            let inserted =
                if node.Kind = NodeKind.Fragment then
                    node.Children
                else
                    [ node ]

            if
                inserted
                |> List.exists (fun c -> c.Kind = NodeKind.Text)
            then
                hierarchy "A document cannot hold text."

            let insertedElements =
                inserted
                |> List.filter (fun c -> c.Kind = NodeKind.Element)
                |> List.length

            if
                insertedElements > 1
                || (insertedElements = 1 && elements this > 0)
            then
                hierarchy "A document holds one element."

    member this.InsertBefore(node: TwinNode, reference: TwinNode) : TwinNode =
        if
            not (isNull reference)
            && not (obj.ReferenceEquals (reference.Parent, this))
        then
            raise (TwinDomException ("NotFoundError", "The reference node is not a child of this node."))

        this.EnsureInsertable (node, null)
        this.InsertValidated (node, reference)

    member private this.InsertValidated(node: TwinNode, reference: TwinNode) : TwinNode =
        if node.Kind = NodeKind.Fragment then
            for child in node.Children do
                this.InsertValidated (child, reference) |> ignore

            node
        else
            let reference =
                if obj.ReferenceEquals (reference, node) then
                    node.NextSibling
                else
                    reference

            node.Remove ()

            let previous =
                if isNull reference then
                    lastChild
                else
                    reference.PreviousSibling

            node.SetLinks (this, reference, previous)

            if isNull previous then
                firstChild <- node
            else
                previous.SetNext node

            if isNull reference then
                lastChild <- node
            else
                reference.SetPrevious node

            node

    member this.AppendChild(node: TwinNode) =
        this.InsertBefore (node, null)

    member this.ReplaceChild(node: TwinNode, child: TwinNode) : TwinNode =
        if
            isNull child
            || not (obj.ReferenceEquals (child.Parent, this))
        then
            raise (TwinDomException ("NotFoundError", "The node to replace is not a child of this node."))

        this.EnsureInsertable (node, child)

        let reference =
            if obj.ReferenceEquals (child.NextSibling, node) then
                node.NextSibling
            else
                child.NextSibling

        if not (obj.ReferenceEquals (child, node)) then
            this.RemoveChild child |> ignore
            this.InsertValidated (node, reference) |> ignore

        child

    abstract TextContent: string with get, set

    default this.TextContent
        with get () =
            let text = Text.StringBuilder ()

            let rec walk (node: TwinNode) =
                for child in node.Children do
                    match child with
                    | :? TwinCharacterData as data when data.Kind = NodeKind.Text -> text.Append (data.Data: string) |> ignore
                    | :? TwinCharacterData -> ()
                    | _ -> walk child

            walk this
            text.ToString ()
        and set value =
            for child in this.Children do
                this.RemoveChild child |> ignore

            if not (String.IsNullOrEmpty value) then
                this.AppendChild (this.Document.CreateText value)
                |> ignore

    abstract Clone: deep: bool -> TwinNode

and [<Sealed; AllowNullLiteral>] TwinCharacterData internal (kind: NodeKind, owner: TwinDocument, data: string) =
    inherit TwinNode(kind, owner)
    let mutable data = data

    member _.Data
        with get () = data
        and set value = data <- (if isNull value then "" else value)

    override _.TextContent
        with get () = data
        and set value = data <- (if isNull value then "" else value)

    override this.Clone _ =
        TwinCharacterData (kind, this.Document, data)

and [<Sealed; AllowNullLiteral>] TwinFragment internal (owner: TwinDocument) =
    inherit TwinNode(NodeKind.Fragment, owner)

    override this.Clone deep =
        let copy = TwinFragment this.Document

        if deep then
            for child in this.Children do
                copy.AppendChild (child.Clone true) |> ignore

        copy

and [<Sealed; AllowNullLiteral>] TwinElement internal (owner: TwinDocument, localName: string, ns: string) =
    inherit TwinNode(NodeKind.Element, owner)

    static let inputTypes =
        HashSet<string>
            [
                "hidden"
                "text"
                "search"
                "tel"
                "url"
                "email"
                "password"
                "date"
                "month"
                "week"
                "time"
                "datetime-local"
                "number"
                "range"
                "color"
                "checkbox"
                "radio"
                "file"
                "submit"
                "image"
                "reset"
                "button"
            ]

    static let defaultValueTypes =
        HashSet<string> [ "hidden"; "submit"; "image"; "reset"; "button" ]

    let attributes = ResizeArray<string * string>()
    let content = if localName = "template" then TwinFragment owner else null
    let mutable dirtyValue: string = null
    let mutable checkedness = false
    let mutable dirtyChecked = false
    let mutable selectedness = false
    let mutable dirtySelected = false

    member _.LocalName = localName

    /// <summary>The element's namespace: <c>html</c>, <c>svg</c> or <c>math</c>.</summary>
    member _.Namespace = ns

    /// <summary>An attribute name as the element stores it: lowercased for an HTML element, as written for SVG and MathML.</summary>
    member private _.Key(name: string) =
        if ns = "html" then name.ToLowerInvariant () else name

    member _.Attributes: (string * string) list = List.ofSeq attributes
    member _.Content: TwinNode = content

    member this.GetAttribute(name: string) : string =
        let name = this.Key name

        match attributes |> Seq.tryFind (fun (n, _) -> n = name) with
        | Some (_, value) -> value
        | None -> null

    member this.HasAttribute(name: string) =
        not (isNull (this.GetAttribute name))

    member this.SetAttribute(name: string, value: string) =
        let name = this.Key name

        match attributes.FindIndex (fun (n, _) -> n = name) with
        | -1 -> attributes.Add ((name, value))
        | index -> attributes[index] <- (name, value)

    member this.RemoveAttribute(name: string) =
        let name = this.Key name

        attributes.RemoveAll (fun (n, _) -> n = name)
        |> ignore

    /// <summary>The input's type keyword; a missing or unknown <c>type</c> attribute reads as <c>text</c>.</summary>
    member internal this.InputType =
        match this.GetAttribute "type" with
        | null -> "text"
        | t ->
            let keyword = t.ToLowerInvariant ()
            if inputTypes.Contains keyword then keyword else "text"

    // HTML's "default" value mode: value reads and writes the value attribute.
    member private this.InDefaultValueMode =
        localName = "input"
        && defaultValueTypes.Contains this.InputType

    member internal this.Options: TwinElement list =
        let rec walk (node: TwinNode) =
            node.Children
            |> List.collect (fun c ->
                match c with
                | :? TwinElement as e when e.LocalName = "option" -> [ e ]
                | :? TwinElement as e when e.LocalName = "optgroup" -> walk e
                | _ -> [])

        walk this

    /// <summary>The form value, by HTML's value modes: the attribute until the property is written, then the written value.</summary>
    member this.Value
        with get () =
            match localName with
            | "input" when
                this.InputType = "checkbox"
                || this.InputType = "radio"
                ->
                match this.GetAttribute "value" with
                | null -> "on"
                | value -> value
            | "input" when this.InDefaultValueMode ->
                match this.GetAttribute "value" with
                | null -> ""
                | v -> v
            | "input" ->
                if isNull dirtyValue then
                    (match this.GetAttribute "value" with
                     | null -> ""
                     | v -> v)
                else
                    dirtyValue
            | "textarea" -> if isNull dirtyValue then this.TextContent else dirtyValue
            | "option" ->
                match this.GetAttribute "value" with
                | null -> String.Join (" ", this.TextContent.Split ([| ' '; '\t'; '\n'; '\f'; '\r' |], StringSplitOptions.RemoveEmptyEntries))
                | value -> value
            | "select" ->
                match this.Options |> List.tryFind (fun o -> o.Selected) with
                | Some option -> option.Value
                | None -> ""
            | "button" ->
                match this.GetAttribute "value" with
                | null -> ""
                | v -> v
            | _ -> null
        and set (value: string) =
            match localName with
            | "input" when
                this.InputType = "checkbox"
                || this.InputType = "radio"
                || this.InDefaultValueMode
                ->
                this.SetAttribute ("value", value)
            | "input"
            | "textarea" -> dirtyValue <- (if isNull value then "" else value)
            | "select" ->
                let mutable found = false

                for option in this.Options do
                    let selected = not found && option.Value = value
                    found <- found || selected
                    option.SetSelectedness selected
            | "option"
            | "button" -> this.SetAttribute ("value", value)
            | _ -> ()

    member this.DefaultValue
        with get () =
            match localName with
            | "textarea" -> this.TextContent
            | _ ->
                match this.GetAttribute "value" with
                | null -> ""
                | v -> v
        and set (value: string) =
            match localName with
            | "textarea" -> this.TextContent <- value
            | _ -> this.SetAttribute ("value", value)

    member this.Checked
        with get () =
            if dirtyChecked then
                checkedness
            else
                this.HasAttribute "checked"
        and set (value: bool) =
            dirtyChecked <- true
            checkedness <- value

            if
                value
                && localName = "input"
                && this.InputType = "radio"
            then
                let name = this.GetAttribute "name"

                if not (String.IsNullOrEmpty name) then
                    let mutable root: TwinNode = this

                    while not (isNull root.Parent) do
                        root <- root.Parent

                    let rec walk (node: TwinNode) =
                        for child in node.Children do
                            match child with
                            | :? TwinElement as e when
                                not (obj.ReferenceEquals (e, this))
                                && e.LocalName = "input"
                                && e.InputType = "radio"
                                && e.GetAttribute "name" = name
                                ->
                                e.Checked <- false
                            | _ -> ()

                            walk child

                    walk root

    // The option's own selectedness: the written value once written, else the selected attribute.
    member private this.RawSelected =
        if dirtySelected then
            selectedness
        else
            this.HasAttribute "selected"

    member private _.SelectedIsDirty = dirtySelected

    /// <summary>The option's select: its parent, or its optgroup's parent.</summary>
    member private this.SelectOf: TwinElement =
        let parentElement (node: TwinNode) =
            match node.Parent with
            | :? TwinElement as e -> e
            | _ -> null

        match parentElement this with
        | null -> null
        | p when p.LocalName = "select" -> p
        | p when p.LocalName = "optgroup" ->
            match parentElement p with
            | null -> null
            | s when s.LocalName = "select" -> s
            | _ -> null
        | _ -> null

    member private this.OptionDisabled =
        this.HasAttribute "disabled"
        || (match this.Parent with
            | :? TwinElement as group when group.LocalName = "optgroup" -> group.HasAttribute "disabled"
            | _ -> false)

    // The selected option of a single-choice select, by HTML's selectedness setting algorithm: the last selected
    // option, or, while no option has a written selectedness, the first enabled option.
    member internal this.ChosenOption: TwinElement =
        let options = this.Options

        match options |> List.filter (fun o -> o.RawSelected) with
        | [] when
            options
            |> List.forall (fun o -> not o.SelectedIsDirty)
            ->
            options
            |> List.tryFind (fun o -> not o.OptionDisabled)
            |> Option.toObj
        | [] -> null
        | selected -> List.last selected

    /// <summary>
    /// An option's selectedness. In a single-choice select one option is selected: the last selected one, or the
    /// first enabled one when none is; selecting an option deselects the others.
    /// </summary>
    member this.Selected
        with get () =
            if localName <> "option" then
                false
            else
                match this.SelectOf with
                | null -> this.RawSelected
                | select when select.HasAttribute "multiple" -> this.RawSelected
                | select -> obj.ReferenceEquals (select.ChosenOption, this)
        and set (value: bool) =
            this.SetSelectedness value

            match this.SelectOf with
            | null -> ()
            | select when select.HasAttribute "multiple" -> ()
            | select when value ->
                for option in select.Options do
                    if not (obj.ReferenceEquals (option, this)) then
                        option.SetSelectedness false
            | _ -> ()

    member internal _.SetSelectedness(value: bool) =
        dirtySelected <- true
        selectedness <- value

    // The cloning steps of input and textarea: the value, its dirtiness and the checkedness travel with the copy.
    member private _.CopyFormState(value: string, isCheckedDirty: bool, isChecked: bool) =
        dirtyValue <- value
        dirtyChecked <- isCheckedDirty
        checkedness <- isChecked

    override this.Clone deep =
        let copy = TwinElement (this.Document, localName, ns)

        for name, value in attributes do
            copy.SetAttribute (name, value)

        if localName = "input" || localName = "textarea" then
            copy.CopyFormState (dirtyValue, dirtyChecked, checkedness)

        if deep then
            for child in this.Children do
                copy.AppendChild (child.Clone true) |> ignore

            if not (isNull content) then
                for child in content.Children do
                    copy.Content.AppendChild (child.Clone true)
                    |> ignore

        copy

and [<Sealed; AllowNullLiteral>] TwinDocument() as this =
    inherit TwinNode(NodeKind.Document, null)
    let ownerThread = Environment.CurrentManagedThreadId
    let disposals = ResizeArray<unit -> unit>()
    let html = TwinElement (this, "html", "html")
    let head = TwinElement (this, "head", "html")
    let body = TwinElement (this, "body", "html")

    do
        this.AppendChild html |> ignore
        html.AppendChild head |> ignore
        html.AppendChild body |> ignore

    member _.Html = html
    member _.Head = head
    member _.Body = body
    member _.OwnerThread = ownerThread
    member val private Focused: TwinElement = null with get, set

    /// <summary>The focused element; null when nothing is focused or the focused element has left the document.</summary>
    member this.ActiveElement
        with get (): TwinElement =
            if
                not (isNull this.Focused)
                && this.Focused.IsConnected
            then
                this.Focused
            else
                null
        and set (value: TwinElement) = this.Focused <- value

    /// <summary>The exceptions raised by listeners during dispatches, in order; a dispatch continues past each.</summary>
    member val ListenerErrors = ResizeArray<exn>()

    /// <summary>Whether a dispatch raises its first listener exception once the dispatch ends.</summary>
    member val RaiseListenerErrors = false with get, set

    /// <summary>Runs when the outermost dispatch, click, focus or blur returns; a session settles its clock and raises pending reports in it.</summary>
    member val internal AfterDispatch: unit -> unit = ignore with get, set

    member val internal DispatchDepth = 0 with get, set

    member this.CreateElement(tag: string) =
        TwinElement (this, tag.ToLowerInvariant (), "html")

    /// <summary>An element of <c>ns</c> (<c>html</c>, <c>svg</c> or <c>math</c>) whose local name keeps the case given.</summary>
    member this.CreateElementIn(ns: string, localName: string) =
        TwinElement (this, localName, ns)

    member this.CreateText(data: string) =
        TwinCharacterData (NodeKind.Text, this, data)

    member this.CreateComment(data: string) =
        TwinCharacterData (NodeKind.Comment, this, data)

    member this.CreateFragment() =
        TwinFragment this

    member this.ElementById(id: string) : TwinElement =
        let rec find (node: TwinNode) =
            node.Children
            |> List.tryPick (fun c ->
                match c with
                | :? TwinElement as e when e.GetAttribute "id" = id -> Some e
                | _ -> find c)

        find this |> Option.toObj

    /// <summary>Raises <c>InvalidOperationException</c> naming <c>operation</c> when called off the creating thread.</summary>
    member _.Check(operation: string) =
        let current = Environment.CurrentManagedThreadId

        if current <> ownerThread then
            raise (InvalidOperationException ($"The DOM twin document belongs to thread {ownerThread}; {operation} ran on thread {current}."))

    member _.OnDispose(action: unit -> unit) =
        disposals.Add action

    override this.Clone _ =
        raise (TwinDomException ("NotSupportedError", "A twin document cannot be cloned."))

    interface IDisposable with
        member _.Dispose() =
            for action in Seq.rev (List.ofSeq disposals) do
                action ()

            disposals.Clear ()
