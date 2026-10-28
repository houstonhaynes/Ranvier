namespace Ranvier.View.WebCore

open System
open System.Runtime.CompilerServices
open Fable.Core
open Fable.Core.TS.Dom
open RanvierWebCoreAbi

/// <summary>
/// <c>HTMLTemplateElement.content</c> and template <c>innerHTML</c>, which WebCore does not export: a template's
/// content is a fragment parsed with the template as context, kept on the managed side.
/// </summary>
[<Sealed; AbstractClass>]
type internal Templates private () =
    static let contents = ConditionalWeakTable<NativeObject, NativeObject>()

    static member private Fragment(session: Session, handle: nativeint) =
        if handle = 0n then
            raise (InvalidOperationException "WebCore returned no fragment")

        session.Node handle

    static member Content(template: NativeObject) : DocumentFragment =
        match contents.TryGetValue template with
        | true, content -> unbox content
        | _ ->
            let session = template.Session

            let content =
                Templates.Fragment (session, Native.rv_document_create_fragment session.Host)

            contents.AddOrUpdate (template, content)
            session.Pin template
            unbox content

    static member InnerHTML(element: NativeObject) : string =
        match box element with
        | :? HTMLTemplateElement ->
            let session = element.Session
            let content = Templates.Content element
            let host = Native.rv_document_create_element (session.Host, "div")
            let clone = Native.rv_node_clone (Interop.handle (box content), 1)

            try
                Native.rv_node_insert_before (host, clone, 0n)
                |> Interop.check "HTMLTemplateElement.innerHTML"

                Interop.text "HTMLTemplateElement.innerHTML" (fun b c l -> Native.rv_node_get_string (host, 4, b, c, l))
            finally
                Native.rv_node_release clone
                Native.rv_node_release host
        | _ -> Interop.text "Element.innerHTML" (fun b c l -> Native.rv_node_get_string (element.Handle, 4, b, c, l))

    /// <summary>Gives a deep clone of a template element a clone of the template's content.</summary>
    static member internal CopyContent(source: NativeObject, clone: NativeObject) =
        match box source with
        | :? HTMLTemplateElement ->
            match contents.TryGetValue source with
            | true, content ->
                let session = clone.Session
                contents.AddOrUpdate (clone, Templates.Fragment (session, Native.rv_node_clone (content.Handle, 1)))
                session.Pin clone
            | _ -> ()
        | _ -> ()

    /// <summary><c>Node.cloneNode</c>, with a deep clone of a template given a clone of its content.</summary>
    static member CloneNode(node: NativeObject, subtree: bool option) : Node =
        let deep = defaultArg subtree false

        match Native.rv_node_clone (node.Handle, (if deep then 1 else 0)) with
        | 0n -> raise (InvalidOperationException "rv_node_clone returned NULL")
        | cloned ->
            let clone = node.Session.Node cloned

            if deep then
                Templates.CopyContent (node, clone)

            unbox<Node> clone

    static member SetInnerHTML(element: NativeObject, value: string) : unit =
        match box element with
        | :? HTMLTemplateElement ->
            let session = element.Session

            let content =
                Templates.Fragment (session, Native.rv_node_parse_fragment (element.Handle, value))

            contents.AddOrUpdate (element, content)
            session.Pin element
        | _ ->
            Native.rv_node_set_string (element.Handle, 4, value)
            |> Interop.check "Element.innerHTML"

/// <summary><c>Element.attributes</c>: the element's attributes in order, read through the element on each call.</summary>
[<Sealed>]
type internal NamedNodeMapObject(element: NativeObject) =
    member private _.Node(name: string) : Attr option =
        if isNull name then
            None
        else
            let mutable result = 0n

            NativeDom.rv_dom_element_get_attribute_node (element.Handle, name, &result)
            |> Interop.check "NamedNodeMap.getNamedItem"

            Interop.optionOf<Attr>(element.Session.Node result)

    member private this.At(index: float) : Attr option =
        let count = Native.rv_element_attribute_count element.Handle

        if index < 0.0 || index >= float count then
            None
        else
            this.Node (Interop.text "NamedNodeMap.item" (fun b c l -> Native.rv_element_attribute_at (element.Handle, int index, 0, b, c, l)))

    interface NamedNodeMap with
        member _.length = float (Native.rv_element_attribute_count element.Handle)

        member this.getNamedItem(qualifiedName: string) =
            this.Node qualifiedName

        member _.getNamedItemNS(_: string option, _: string) =
            Interop.unsupported "NamedNodeMap.getNamedItemNS"

        member this.item(index: float) =
            this.At index

        member _.removeNamedItem(_: string) : Attr =
            Interop.unsupported "NamedNodeMap.removeNamedItem"

        member _.removeNamedItemNS(_: string option, _: string) : Attr =
            Interop.unsupported "NamedNodeMap.removeNamedItemNS"

        member _.setNamedItem(_: Attr) : Attr option =
            Interop.unsupported "NamedNodeMap.setNamedItem"

        member _.setNamedItemNS(_: Attr) : Attr option =
            Interop.unsupported "NamedNodeMap.setNamedItemNS"

        member this.Item
            with get (index: float): Attr =
                match this.At index with
                | Some attribute -> attribute
                | None -> Unchecked.defaultof<_>
            and set (_: float) (_: Attr) = Interop.unsupported "NamedNodeMap.Item"

[<Sealed; AbstractClass>]
type internal Attributes private () =
    static member Map(element: NativeObject) : NamedNodeMap =
        NamedNodeMapObject element

/// <summary>Document members whose IDL arguments the generated ABI leaves out.</summary>
[<Sealed; AbstractClass>]
type internal Documents private () =
    static member CreateElement(document: NativeObject, tagName: string) : HTMLElement =
        let session = document.Session

        match Native.rv_document_create_element (session.Host, tagName) with
        | 0n -> raise (WebCoreDomException ("InvalidCharacterError", $"'{tagName}' is not a valid element name"))
        | handle -> unbox<HTMLElement>(session.Node handle)

    static member ImportNode<'T>(document: NativeObject, node: 'T, options: U2<bool, ImportNodeOptions> option) : 'T =
        let deep =
            match options with
            | Some (U2.Case1 deep) -> deep
            | Some (U2.Case2 options) -> not (Option.defaultValue false options.selfOnly)
            | None -> false

        let session = document.Session

        match Native.rv_document_import_node (session.Host, Interop.handle (box node), (if deep then 1 else 0)) with
        | 0n -> raise (WebCoreDomException ("NotSupportedError", "The node cannot be imported"))
        | handle ->
            let imported = session.Node handle

            match box node with
            | :? NativeObject as source when deep -> Templates.CopyContent (source, imported)
            | _ -> ()

            unbox<'T> imported

/// <summary>The variadic <c>ChildNode</c> and <c>ParentNode</c> insertions, through one fragment.</summary>
[<Sealed; AbstractClass>]
type internal Nodes private () =
    static member private Relative(handle: nativeint, relation: int) =
        Native.rv_node_relative (handle, relation)

    /// <summary>A new fragment holding the nodes, strings as text nodes, as an owned handle.</summary>
    static member private Fragment(session: Session, nodes: U2<string, Node> array) : nativeint =
        let fragment = Native.rv_document_create_fragment session.Host

        for item in nodes do
            match item with
            | U2.Case1 text ->
                let created = Native.rv_document_create_text (session.Host, text)

                Native.rv_node_insert_before (fragment, created, 0n)
                |> Interop.check "ChildNode.after"

                Native.rv_node_release created
            | U2.Case2 node ->
                Native.rv_node_insert_before (fragment, Interop.handle (box node), 0n)
                |> Interop.check "ChildNode.after"

        fragment

    static member private Contains(nodes: U2<string, Node> array, handle: nativeint) =
        nodes
        |> Array.exists (function
            | U2.Case2 node -> Interop.handle (box node) = handle
            | _ -> false)

    /// <summary>Inserts the nodes into <c>parent</c> before <c>before</c> (zero for last), then releases the owned handles.</summary>
    static member private Insert(session: Session, parent: nativeint, nodes: U2<string, Node> array, before: nativeint, name: string) =
        let fragment = Nodes.Fragment (session, nodes)

        try
            Native.rv_node_insert_before (parent, fragment, before)
            |> Interop.check name
        finally
            Native.rv_node_release fragment

    /// <summary>The first sibling at or after <c>start</c> (an owned handle) outside <c>nodes</c>, as an owned handle.</summary>
    static member private Viable(start: nativeint, nodes: U2<string, Node> array, relation: int) =
        let mutable cursor = start

        while cursor <> 0n && Nodes.Contains (nodes, cursor) do
            let next = Nodes.Relative (cursor, relation)
            Native.rv_node_release cursor
            cursor <- next

        cursor

    static member After(node: NativeObject, nodes: U2<string, Node> array) : unit =
        let parent = Nodes.Relative (node.Handle, 0)

        if parent <> 0n then
            let next = Nodes.Viable (Nodes.Relative (node.Handle, 3), nodes, 3)

            try
                Nodes.Insert (node.Session, parent, nodes, next, "ChildNode.after")
            finally
                if next <> 0n then
                    Native.rv_node_release next

                Native.rv_node_release parent

    static member Before(node: NativeObject, nodes: U2<string, Node> array) : unit =
        let parent = Nodes.Relative (node.Handle, 0)

        if parent <> 0n then
            let previous = Nodes.Viable (Nodes.Relative (node.Handle, 4), nodes, 4)

            let before =
                if previous = 0n then
                    Nodes.Relative (parent, 1)
                else
                    Nodes.Relative (previous, 3)

            try
                Nodes.Insert (node.Session, parent, nodes, before, "ChildNode.before")
            finally
                for handle in [ previous; before; parent ] do
                    if handle <> 0n then
                        Native.rv_node_release handle

    static member ReplaceWith(node: NativeObject, nodes: U2<string, Node> array) : unit =
        let parent = Nodes.Relative (node.Handle, 0)

        if parent <> 0n then
            try
                match nodes with
                | [| U2.Case2 replacement |] ->
                    Native.rv_node_replace_child (parent, Interop.handle (box replacement), node.Handle)
                    |> Interop.check "ChildNode.replaceWith"
                | _ ->
                    let next = Nodes.Viable (Nodes.Relative (node.Handle, 3), nodes, 3)

                    try
                        let current = Nodes.Relative (node.Handle, 0)

                        if current = parent then
                            let fragment = Nodes.Fragment (node.Session, nodes)

                            try
                                Native.rv_node_replace_child (parent, fragment, node.Handle)
                                |> Interop.check "ChildNode.replaceWith"
                            finally
                                Native.rv_node_release fragment
                        else
                            Nodes.Insert (node.Session, parent, nodes, next, "ChildNode.replaceWith")

                        if current <> 0n then
                            Native.rv_node_release current
                    finally
                        if next <> 0n then
                            Native.rv_node_release next
            finally
                Native.rv_node_release parent

    /// <summary><c>setSelectionRange</c> of an input or textarea; the direction is not supported natively.</summary>
    static member SetSelectionRange(field: NativeObject, start: float option, finish: float option) : unit =
        Native.rv_text_set_selection (field.Handle, int (defaultArg start 0.0), int (defaultArg finish 0.0))
        |> Interop.check "HTMLInputElement.setSelectionRange"

    static member Append(parent: NativeObject, nodes: U2<string, Node> array) : unit =
        Nodes.Insert (parent.Session, parent.Handle, nodes, 0n, "ParentNode.append")

    static member Prepend(parent: NativeObject, nodes: U2<string, Node> array) : unit =
        let first = Nodes.Relative (parent.Handle, 1)

        try
            Nodes.Insert (parent.Session, parent.Handle, nodes, first, "ParentNode.prepend")
        finally
            if first <> 0n then
                Native.rv_node_release first

    static member ReplaceChildren(parent: NativeObject, nodes: U2<string, Node> array) : unit =
        let fragment = Nodes.Fragment (parent.Session, nodes)

        try
            let mutable child = Nodes.Relative (parent.Handle, 1)

            while child <> 0n do
                Native.rv_node_remove child
                |> Interop.check "ParentNode.replaceChildren"

                Native.rv_node_release child
                child <- Nodes.Relative (parent.Handle, 1)

            Native.rv_node_insert_before (parent.Handle, fragment, 0n)
            |> Interop.check "ParentNode.replaceChildren"
        finally
            Native.rv_node_release fragment

/// <summary>The CSS properties of <c>CSSStyleProperties</c>, through <c>getPropertyValue</c> and <c>setProperty</c>.</summary>
[<Sealed; AbstractClass>]
type internal Styles private () =
    static member Get(style: NativeObject, property: string) : string =
        Interop.text "CSSStyleDeclaration.getPropertyValue" (fun b c l ->
            NativeDom.rv_dom_css_style_declaration_get_property_value (style.Handle, property, b, c, l))

    static member Set(style: NativeObject, property: string, value: string) : unit =
        NativeDom.rv_dom_css_style_declaration_set_property (style.Handle, property, value, "")
        |> Interop.check "CSSStyleDeclaration.setProperty"
