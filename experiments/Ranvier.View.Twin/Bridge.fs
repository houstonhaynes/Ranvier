namespace Ranvier.View.Twin

open System
open System.Collections.Generic
open System.Reflection
open System.Runtime.CompilerServices
open Microsoft.FSharp.Reflection

type internal Member = TwinNode -> obj array -> obj

/// <summary>A member implementation and the DOM interfaces it serves.</summary>
type internal Entry = { Accepts: Type -> bool; Run: Member }

/// <summary>A wrapper around a DOM method of one node, or of every node of a document when <c>Target</c> is null.</summary>
type internal Interception =
    {
        Target: TwinNode
        Name: string
        Wrap: Func<Func<obj array, obj>, obj array, obj>
    }

/// <summary>The members served on the current thread while a recording is open.</summary>
[<Sealed; AbstractClass>]
type internal ServedOnThread =
    [<ThreadStatic; DefaultValue>]
    static val mutable private recording: HashSet<string>

    static member Recording
        with get () = ServedOnThread.recording
        and set (value: HashSet<string>) = ServedOnThread.recording <- value

/// <summary>
/// A live list of twin nodes, which the bridge presents as the declared list interface. <c>Key</c> names the list on
/// its owner, so every read of the same list returns the same object.
/// </summary>
type internal ListValue(owner: obj, key: string, nodes: unit -> TwinNode seq) =
    member _.Owner = owner
    member _.Key = key
    member _.Nodes = nodes ()

/// <summary>An element's attributes, which the bridge presents as <c>NamedNodeMap</c>.</summary>
type internal AttributesValue(element: TwinElement) =
    member _.Element = element

/// <summary>One attribute of an element, which the bridge presents as <c>Attr</c>.</summary>
type internal AttributeValue(element: TwinElement, name: string) =
    member _.Element = element
    member _.Name = name

/// <summary>A proxy whose members are answered by a handler: lists, attribute maps and attributes.</summary>
type ValueProxy() =
    inherit DispatchProxy()
    member val internal Handler: Func<MethodInfo, obj array, obj> = null with get, set

    override this.Invoke(targetMethod: MethodInfo, args: obj array) : obj =
        this.Handler.Invoke (targetMethod, (if isNull args then [||] else args))

/// <summary>The bridged object of a twin node: a proxy of the DOM interface its kind and tag imply.</summary>
type NodeProxy() =
    inherit DispatchProxy()
    member val Node: TwinNode = null with get, set

    override this.Invoke(targetMethod: MethodInfo, args: obj array) : obj =
        BridgeCore.invoke this.Node targetMethod args

and internal BridgeCore private () =
    // By member name and arity: the implementations, each with the interfaces it serves.
    static let table = System.Collections.Concurrent.ConcurrentDictionary<string, Entry list>()
    static let interfaces = System.Collections.Concurrent.ConcurrentDictionary<string, Type>()
    static let someOf = System.Collections.Concurrent.ConcurrentDictionary<Type, MethodInfo>()

    // Per owner: the list proxies already handed out, by list key and interface.
    static let lists = ConditionalWeakTable<obj, Dictionary<string, obj>>()

    // Per document: the interceptions in the order they were added.
    static let interceptions = ConditionalWeakTable<TwinDocument, ResizeArray<Interception>>()

    static member Register(key: string, entry: Entry) =
        table.AddOrUpdate (key, [ entry ], fun _ entries -> entries @ [ entry ])
        |> ignore

    static member Domain = typeof<Fable.Core.TS.Dom.Node>.Assembly

    static member Interface(name: string) : Type =
        match interfaces.TryGetValue name with
        | true, t -> t
        | false, _ ->
            let t = BridgeCore.Domain.GetType ("Fable.Core.TS+Dom+" + name, true)
            interfaces[name] <- t
            t

    /// <summary>The interface's name without the generic arity suffix: <c>NodeListOf</c> for <c>NodeListOf`1</c>.</summary>
    static member NameOf(t: Type) : string =
        match t.Name.IndexOf '`' with
        | -1 -> t.Name
        | index -> t.Name.Substring (0, index)

    static member Qualified(targetMethod: MethodInfo) : string =
        let name = targetMethod.Name

        let display =
            if name.StartsWith "get_" || name.StartsWith "set_" then
                name.Substring 4
            else
                name

        BridgeCore.NameOf targetMethod.DeclaringType
        + "."
        + display

    static member Serve(name: string) =
        match ServedOnThread.Recording with
        | null -> ()
        | recording -> recording.Add name |> ignore

    /// <summary>The proxy of <c>node</c>, created on first use.</summary>
    static member ProxyOf(node: TwinNode) : obj =
        if isNull node then
            null
        else
            if isNull node.Proxy then
                let proxy =
                    DispatchProxy.Create (BridgeCore.InterfaceOf node, typeof<NodeProxy>) :?> NodeProxy

                proxy.Node <- node
                node.Proxy <- proxy

            node.Proxy

    static member InterfaceOf(node: TwinNode) : Type =
        match node with
        | :? TwinDocument -> BridgeCore.Interface "Document"
        | :? TwinFragment -> BridgeCore.Interface "DocumentFragment"
        | :? TwinCharacterData as c when c.Kind = NodeKind.Text -> BridgeCore.Interface "Text"
        | :? TwinCharacterData -> BridgeCore.Interface "Comment"
        | :? TwinElement as e ->
            let map = BridgeCore.Interface "HTMLElementTagNameMap"

            match map.GetProperty e.LocalName with
            | null -> BridgeCore.Interface "HTMLElement"
            | property -> property.PropertyType
        | _ -> BridgeCore.Interface "Node"

    static member Unwrap(value: obj) : obj =
        match value with
        | :? NodeProxy as proxy -> box proxy.Node
        | null -> null
        | _ ->
            let t = value.GetType ()

            if
                t.IsGenericType
                && t.GetGenericTypeDefinition () = typedefof<option<_>>
            then
                BridgeCore.Unwrap (t.GetProperty("Value").GetValue value)
            elif
                FSharpType.IsUnion (t, true)
                && t.Namespace = "Fable.Core"
            then
                BridgeCore.Unwrap ((FSharpValue.GetUnionFields (value, t, true) |> snd)[0])
            else
                value

    static member Unsupported(targetMethod: MethodInfo) : exn =
        NotSupportedException (
            "The DOM twin does not implement "
            + BridgeCore.Qualified targetMethod
        )

    static member ServeMethod(targetMethod: MethodInfo) =
        BridgeCore.Serve (BridgeCore.Qualified targetMethod)

    static member ValueProxyOf(interfaceType: Type, handler: MethodInfo -> obj array -> obj) : obj =
        let proxy = DispatchProxy.Create (interfaceType, typeof<ValueProxy>) :?> ValueProxy
        proxy.Handler <- Func<MethodInfo, obj array, obj> handler
        box proxy

    /// <summary>The proxy of a list or attribute map: one object per owner, key and interface.</summary>
    static member Cached(owner: obj, key: string, interfaceType: Type, create: unit -> obj) : obj =
        let cache = lists.GetValue (owner, fun _ -> Dictionary<string, obj>())
        let key = key + "|" + interfaceType.FullName

        match cache.TryGetValue key with
        | true, proxy -> proxy
        | false, _ ->
            let proxy = create ()
            cache[key] <- proxy
            proxy

    static member ListProxy(interfaceType: Type, list: ListValue) : obj =
        BridgeCore.ValueProxyOf (
            interfaceType,
            fun m args ->
                match m.Name with
                | "get_length" ->
                    BridgeCore.ServeMethod m
                    box (float (Seq.length list.Nodes))
                | "item"
                | "get_Item" ->
                    BridgeCore.ServeMethod m
                    let index = int (Convert.ToDouble args[0])

                    let item =
                        if index < 0 then
                            null
                        else
                            list.Nodes |> Seq.tryItem index |> Option.toObj

                    BridgeCore.Wrap (m, m.ReturnType, box item)
                | _ -> raise (BridgeCore.Unsupported m)
        )

    static member AttributesProxy(interfaceType: Type, attributes: AttributesValue) : obj =
        BridgeCore.ValueProxyOf (
            interfaceType,
            fun m args ->
                let element = attributes.Element

                match m.Name with
                | "get_length" ->
                    BridgeCore.ServeMethod m
                    box (float element.Attributes.Length)
                | "item"
                | "get_Item" ->
                    BridgeCore.ServeMethod m

                    match List.tryItem (int (Convert.ToDouble args[0])) element.Attributes with
                    | Some (name, _) -> BridgeCore.Wrap (m, m.ReturnType, AttributeValue (element, name))
                    | None -> null
                | _ -> raise (BridgeCore.Unsupported m)
        )

    static member AttributeProxy(interfaceType: Type, attribute: AttributeValue) : obj =
        BridgeCore.ValueProxyOf (
            interfaceType,
            fun m _ ->
                match m.Name with
                | "get_name"
                | "get_localName" ->
                    BridgeCore.ServeMethod m
                    box attribute.Name
                | "get_value" ->
                    BridgeCore.ServeMethod m

                    box (
                        match attribute.Element.GetAttribute attribute.Name with
                        | null -> ""
                        | v -> v
                    )
                | _ -> raise (BridgeCore.Unsupported m)
        )

    /// <summary>
    /// The value as the member's declared return type: nodes as their proxies, options for nullable returns. A value
    /// the type cannot hold raises the member's <c>NotSupportedException</c>.
    /// </summary>
    static member Wrap(targetMethod: MethodInfo, returnType: Type, value: obj) : obj =
        let isOption =
            returnType.IsGenericType
            && returnType.GetGenericTypeDefinition () = typedefof<option<_>>

        let target =
            if isOption then
                returnType.GetGenericArguments()[0]
            else
                returnType

        let value =
            match value with
            | :? TwinNode as node -> BridgeCore.ProxyOf node
            | :? ListValue as list -> BridgeCore.Cached (list.Owner, list.Key, target, fun () -> BridgeCore.ListProxy (target, list))
            | :? AttributesValue as attributes ->
                BridgeCore.Cached (attributes.Element, "attributes", target, fun () -> BridgeCore.AttributesProxy (target, attributes))
            | :? AttributeValue as attribute -> BridgeCore.AttributeProxy (target, attribute)
            | :? int as i when target = typeof<float> -> box (float i)
            | _ -> value

        let fits =
            if returnType = typeof<Void> then
                true
            elif isNull value then
                isOption || not target.IsValueType
            else
                target.IsGenericParameter
                || target.ContainsGenericParameters
                || target.IsInstanceOfType value

        if not fits then
            raise (BridgeCore.Unsupported targetMethod)
        elif returnType = typeof<Void> then
            null
        elif isOption then
            if isNull value then
                null
            else
                let some =
                    match someOf.TryGetValue returnType with
                    | true, m -> m
                    | false, _ ->
                        let m = returnType.GetMethod "Some"
                        someOf[returnType] <- m
                        m

                some.Invoke (null, [| value |])
        else
            value

    static member invoke (node: TwinNode) (targetMethod: MethodInfo) (args: obj array) : obj =
        node.Document.Check targetMethod.Name
        let arity = if isNull args then 0 else args.Length
        let key = $"{targetMethod.Name}/{arity}"
        let declaring = targetMethod.DeclaringType

        let entry =
            match table.TryGetValue key with
            | true, entries ->
                entries
                |> List.tryFind (fun e -> e.Accepts declaring)
            | false, _ -> None

        match entry with
        | Some entry ->
            let original (args: obj array) : obj =
                BridgeCore.Serve (BridgeCore.Qualified targetMethod)
                BridgeCore.Wrap (targetMethod, targetMethod.ReturnType, entry.Run node (Array.map BridgeCore.Unwrap args))

            let args = if isNull args then [||] else args

            match interceptions.TryGetValue node.Document with
            | true, list when list.Count > 0 ->
                let applying =
                    [
                        for i in list do
                            if
                                i.Name = targetMethod.Name
                                && (isNull i.Target
                                    || obj.ReferenceEquals (i.Target, node))
                            then
                                i
                    ]

                // A later interception wraps the earlier ones, as a later patch of a JavaScript method does.
                let call =
                    applying
                    |> List.fold (fun (inner: obj array -> obj) i -> fun (a: obj array) -> i.Wrap.Invoke (Func<obj array, obj> inner, a)) original

                // The wrappers see the arguments as DOM objects: nodes as their proxies, options and unions unwrapped.
                let visible =
                    args
                    |> Array.map (fun a ->
                        match BridgeCore.Unwrap a with
                        | :? TwinNode as n -> BridgeCore.ProxyOf n
                        | v -> v)

                if applying.IsEmpty then original args else call visible
            | _ -> original args
        | None -> raise (BridgeCore.Unsupported targetMethod)

    /// <summary>Routes calls of the method <c>name</c> on <c>target</c>, or on every node of <c>document</c> when null, through <c>wrap</c>.</summary>
    static member Intercept(document: TwinDocument, target: TwinNode, name: string, wrap: Func<Func<obj array, obj>, obj array, obj>) : unit -> unit =
        let list = interceptions.GetValue (document, fun _ -> ResizeArray ())

        let interception =
            {
                Target = target
                Name = name
                Wrap = wrap
            }

        list.Add interception
        fun () -> list.Remove interception |> ignore

[<RequireQualifiedAccess>]
module Bridge =
    let nodeOf (value: obj) : TwinNode =
        match value with
        | :? NodeProxy as proxy -> proxy.Node
        | _ -> null

    /// <summary>Accepts the members the named interfaces declare.</summary>
    let declaredBy (names: string list) : Type -> bool =
        let set = HashSet<string> names
        fun t -> set.Contains (BridgeCore.NameOf t)

    /// <summary>Accepts the members of <c>EventTarget</c>, of every interface that extends it, and of the event-handler mixins.</summary>
    let eventTargets: Type -> bool =
        let target = lazy (BridgeCore.Interface "EventTarget")
        // The event-handler mixins redeclare the listener methods without extending EventTarget.
        let mixins =
            HashSet<string>
                [
                    "GlobalEventHandlers"
                    "WindowEventHandlers"
                    "DocumentAndElementEventHandlers"
                ]

        fun t ->
            target.Value.IsAssignableFrom t
            || mixins.Contains (BridgeCore.NameOf t)

    /// <summary>Registers an implementation of the member <c>name</c> with <c>arity</c> arguments for the accepted interfaces.</summary>
    let registerMember (accepts: Type -> bool) (name: string) (arity: int) (implementation: TwinNode -> obj array -> obj) =
        BridgeCore.Register (
            $"{name}/{arity}",
            {
                Accepts = accepts
                Run = implementation
            }
        )

    /// <summary>
    /// Routes calls of the DOM method <c>name</c> on <c>target</c>, or on every node of <c>document</c> when
    /// <c>target</c> is null, through <c>wrap</c> until the returned function runs. <c>wrap</c> receives the original
    /// method and the call's arguments, and its result is the call's result.
    /// </summary>
    let intercept (document: TwinDocument) (target: TwinNode) (name: string) (wrap: Func<Func<obj array, obj>, obj array, obj>) =
        BridgeCore.Intercept (document, target, name, wrap)

    /// <summary>The members served on the calling thread while <c>run</c> runs, sorted.</summary>
    let servedDuring (run: unit -> unit) : string list =
        let previous = ServedOnThread.Recording
        let recording = HashSet<string>()
        ServedOnThread.Recording <- recording

        try
            run ()
        finally
            ServedOnThread.Recording <- previous

            if not (isNull previous) then
                previous.UnionWith recording

        recording |> Seq.sort |> List.ofSeq

    let private element (node: TwinNode) =
        node :?> TwinElement

    let private text (value: obj) =
        if isNull value then "" else string value

    let private flag (value: obj) =
        match value with
        | :? bool as b -> b
        | _ -> false

    let private getter on name (read: TwinNode -> obj) =
        registerMember (declaredBy on) ("get_" + name) 0 (fun node _ -> read node)

    let private setter on name (write: TwinNode -> obj -> unit) =
        registerMember (declaredBy on) ("set_" + name) 1 (fun node args ->
            write node args[0]
            null)

    let private method on name arity (run: TwinNode -> obj array -> obj) =
        registerMember (declaredBy on) name arity run

    let private nodeType (node: TwinNode) =
        match node.Kind with
        | NodeKind.Element -> 1
        | NodeKind.Text -> 3
        | NodeKind.Comment -> 8
        | NodeKind.Document -> 9
        | NodeKind.Fragment -> 11

    let private nodeName (node: TwinNode) =
        match node with
        | :? TwinElement as e ->
            if e.Namespace = "html" then
                e.LocalName.ToUpperInvariant ()
            else
                e.LocalName
        | _ ->
            match node.Kind with
            | NodeKind.Text -> "#text"
            | NodeKind.Comment -> "#comment"
            | NodeKind.Document -> "#document"
            | _ -> "#document-fragment"

    let private siblingsFrom (first: TwinNode) : TwinNode seq =
        seq {
            let mutable child = first

            while not (isNull child) do
                yield child
                child <- child.NextSibling
        }

    let private childList (owner: TwinNode) (key: string) (nodes: unit -> TwinNode seq) : obj =
        box (ListValue (owner, key, nodes))

    let private nodes = [ "Node" ]
    let private elements = [ "Element" ]
    let private documents = [ "Document" ]
    let private inputs = [ "HTMLInputElement" ]

    let private valued =
        [
            "HTMLInputElement"
            "HTMLTextAreaElement"
            "HTMLSelectElement"
            "HTMLOptionElement"
            "HTMLButtonElement"
        ]

    let private disableable =
        [
            "HTMLButtonElement"
            "HTMLFieldSetElement"
            "HTMLInputElement"
            "HTMLOptGroupElement"
            "HTMLOptionElement"
            "HTMLSelectElement"
            "HTMLTextAreaElement"
        ]

    // The node's text; a document's is null and its writer does nothing.
    let private textContentOf (n: TwinNode) : obj =
        if n.Kind = NodeKind.Document then
            null
        else
            box n.TextContent

    let private writeTextContent (n: TwinNode) (v: obj) =
        if n.Kind <> NodeKind.Document then
            n.TextContent <- text v

    let private nodeArgument (owner: TwinNode) (value: obj) : TwinNode =
        match value with
        | :? TwinNode as node -> node
        | v -> owner.Document.CreateText (string v) :> TwinNode

    // The DOM's replaceWith: the replacements go before the first following sibling outside them.
    let private replaceWith (n: TwinNode) (replacements: obj array) =
        let parent = n.Parent

        if not (isNull parent) then
            let inserted =
                replacements
                |> Array.map (BridgeCore.Unwrap >> nodeArgument n)

            let mutable viable = n.NextSibling

            while not (isNull viable)
                  && inserted
                     |> Array.exists (fun r -> obj.ReferenceEquals (r, viable)) do
                viable <- viable.NextSibling

            if not (Array.exists (fun r -> obj.ReferenceEquals (r, n)) inserted) then
                n.Remove ()

            for replacement in inserted do
                parent.InsertBefore (replacement, viable)
                |> ignore

    let private core =
        lazy
            (
             // Node
             getter nodes "nodeType" (nodeType >> box)
             getter nodes "nodeName" (nodeName >> box)
             getter nodes "parentNode" (fun n -> box n.Parent)

             getter nodes "parentElement" (fun n ->
                 match n.Parent with
                 | :? TwinElement as e -> box e
                 | _ -> null)

             getter nodes "firstChild" (fun n -> box n.FirstChild)
             getter nodes "lastChild" (fun n -> box n.LastChild)
             getter nodes "nextSibling" (fun n -> box n.NextSibling)
             getter nodes "previousSibling" (fun n -> box n.PreviousSibling)

             getter [ "Node"; "CharacterData"; "Element"; "DocumentFragment"; "Document" ] "ownerDocument" (fun n ->
                 if n.Kind = NodeKind.Document then null else box n.Document)

             getter nodes "isConnected" (fun n -> box n.IsConnected)
             getter nodes "childNodes" (fun n -> childList n "childNodes" (fun () -> siblingsFrom n.FirstChild))
             getter [ "Node"; "CharacterData"; "Element"; "DocumentFragment"; "Document" ] "textContent" textContentOf
             setter [ "Node"; "CharacterData"; "Element"; "DocumentFragment" ] "textContent" writeTextContent
             method nodes "appendChild" 1 (fun n a -> box (n.AppendChild (a[0] :?> TwinNode)))
             method nodes "insertBefore" 2 (fun n a -> box (n.InsertBefore (a[0] :?> TwinNode, a[1] :?> TwinNode)))
             method nodes "removeChild" 1 (fun n a -> box (n.RemoveChild (a[0] :?> TwinNode)))
             method nodes "replaceChild" 2 (fun n a -> box (n.ReplaceChild (a[0] :?> TwinNode, a[1] :?> TwinNode)))

             method nodes "contains" 1 (fun n a ->
                 box (
                     not (isNull a[0])
                     && n.Contains (a[0] :?> TwinNode)
                 ))

             method nodes "cloneNode" 1 (fun n a -> box (n.Clone (flag a[0])))
             // CharacterData
             getter [ "CharacterData" ] "data" (fun n -> box (n :?> TwinCharacterData).Data)
             setter [ "CharacterData" ] "data" (fun n v -> (n :?> TwinCharacterData).Data <- text v)
             // ChildNode
             method [ "ChildNode" ] "remove" 0 (fun n _ ->
                 n.Remove ()
                 null)

             method [ "ChildNode" ] "replaceWith" 1 (fun n a ->
                 replaceWith n (a[0] :?> obj array)
                 null)
             // ParentNode
             getter [ "ParentNode" ] "children" (fun n ->
                 childList n "children" (fun () ->
                     siblingsFrom n.FirstChild
                     |> Seq.filter (fun c -> c.Kind = NodeKind.Element)))

             method [ "ParentNode" ] "replaceChildren" 1 (fun n a ->
                 let replacements =
                     (a[0] :?> obj array)
                     |> Array.map (BridgeCore.Unwrap >> nodeArgument n)

                 for child in n.Children do
                     n.RemoveChild child |> ignore

                 for child in replacements do
                     n.AppendChild child |> ignore

                 null)
             // Element
             getter elements "localName" (fun n -> box (element n).LocalName)
             getter elements "tagName" (fun n -> box (nodeName n))

             getter elements "namespaceURI" (fun n ->
                 box (
                     match (element n).Namespace with
                     | "svg" -> "http://www.w3.org/2000/svg"
                     | "math" -> "http://www.w3.org/1998/Math/MathML"
                     | _ -> "http://www.w3.org/1999/xhtml"
                 ))

             getter elements "prefix" (fun _ -> null)
             getter elements "id" (fun n -> box (text ((element n).GetAttribute "id")))
             setter elements "id" (fun n v -> (element n).SetAttribute("id", text v))
             getter elements "className" (fun n -> box (text ((element n).GetAttribute "class")))
             setter elements "className" (fun n v -> (element n).SetAttribute("class", text v))
             method elements "getAttribute" 1 (fun n a -> box ((element n).GetAttribute(string a[0])))

             method elements "setAttribute" 2 (fun n a ->
                 (element n).SetAttribute(string a[0], text a[1])
                 null)

             method elements "removeAttribute" 1 (fun n a ->
                 (element n).RemoveAttribute(string a[0])
                 null)

             method elements "hasAttribute" 1 (fun n a -> box ((element n).HasAttribute(string a[0])))
             getter elements "attributes" (fun n -> box (AttributesValue (element n)))
             getter elements "innerHTML" (fun n -> box (Html.inner n))

             setter elements "innerHTML" (fun n v ->
                 let target =
                     match n with
                     | :? TwinElement as e when e.LocalName = "template" -> e.Content
                     | _ -> n

                 for child in target.Children do
                     target.RemoveChild child |> ignore

                 Html.parseInto n (text v))

             getter elements "outerHTML" (fun n -> box (Html.outer n))
             // HTMLTemplateElement
             getter [ "HTMLTemplateElement" ] "content" (fun n -> box (element n).Content)
             // Form controls
             getter valued "value" (fun n -> box (element n).Value)
             setter valued "value" (fun n v -> (element n).Value <- text v)
             getter [ "HTMLInputElement"; "HTMLTextAreaElement" ] "defaultValue" (fun n -> box (element n).DefaultValue)

             setter [ "HTMLInputElement"; "HTMLTextAreaElement" ] "defaultValue" (fun n v -> (element n).DefaultValue <- text v)

             getter inputs "checked" (fun n -> box (element n).Checked)
             setter inputs "checked" (fun n v -> (element n).Checked <- flag v)
             getter inputs "defaultChecked" (fun n -> box ((element n).HasAttribute "checked"))
             getter [ "HTMLOptionElement" ] "selected" (fun n -> box (element n).Selected)
             setter [ "HTMLOptionElement" ] "selected" (fun n v -> (element n).Selected <- flag v)
             getter inputs "type" (fun n -> box (element n).InputType)
             getter [ "HTMLTextAreaElement" ] "type" (fun _ -> box "textarea")
             getter disableable "disabled" (fun n -> box ((element n).HasAttribute "disabled"))

             setter disableable "disabled" (fun n v ->
                 if flag v then
                     (element n).SetAttribute("disabled", "")
                 else
                     (element n).RemoveAttribute "disabled")
             // Document
             getter documents "head" (fun n -> box (n :?> TwinDocument).Head)
             getter documents "body" (fun n -> box (n :?> TwinDocument).Body)
             getter documents "documentElement" (fun n -> box (n :?> TwinDocument).Html)

             getter [ "Document"; "HTMLElement" ] "title" (fun n ->
                 match n with
                 | :? TwinDocument as d ->
                     d.Head.Children
                     |> List.tryPick (fun c ->
                         match c with
                         | :? TwinElement as e when e.LocalName = "title" -> Some (box e.TextContent)
                         | _ -> None)
                     |> Option.defaultValue (box "")
                 | _ -> box (text ((element n).GetAttribute "title")))

             setter [ "Document"; "HTMLElement" ] "title" (fun n v ->
                 match n with
                 | :? TwinDocument as d ->
                     let existing =
                         d.Head.Children
                         |> List.tryPick (fun c ->
                             match c with
                             | :? TwinElement as e when e.LocalName = "title" -> Some e
                             | _ -> None)

                     let title =
                         match existing with
                         | Some t -> t
                         | None -> d.Head.AppendChild (d.CreateElement "title") :?> TwinElement

                     title.TextContent <- text v
                 | _ -> (element n).SetAttribute("title", text v))

             method documents "createElement" 2 (fun n a -> box ((n :?> TwinDocument).CreateElement(string a[0])))
             method documents "createTextNode" 1 (fun n a -> box ((n :?> TwinDocument).CreateText(text a[0])))
             method documents "createComment" 1 (fun n a -> box ((n :?> TwinDocument).CreateComment(text a[0])))
             method documents "createDocumentFragment" 0 (fun n _ -> box ((n :?> TwinDocument).CreateFragment()))
             method documents "importNode" 2 (fun _ a -> box ((a[0] :?> TwinNode).Clone(flag a[1])))

             method [ "Document"; "NonElementParentNode" ] "getElementById" 1 (fun n a -> box ((n :?> TwinDocument).ElementById(string a[0])))

             getter [ "DocumentOrShadowRoot" ] "activeElement" (fun n ->
                 let document = n :?> TwinDocument

                 match document.ActiveElement with
                 | null -> box document.Body
                 | active -> box active))

    // The event and selector members live in later files; they register through their own ensure, found by name.
    let private extensions =
        lazy
            (let assembly = typeof<NodeProxy>.Assembly

             for name in [ "Ranvier.View.Twin.Events"; "Ranvier.View.Twin.SelectorMembers" ] do
                 match assembly.GetType name with
                 | null -> ()
                 | t ->
                     match
                         t.GetMethod (
                             "ensure",
                             BindingFlags.Public
                             ||| BindingFlags.NonPublic
                             ||| BindingFlags.Static
                         )
                     with
                     | null -> ()
                     | ensure -> ensure.Invoke (null, [||]) |> ignore)

    /// <summary>Registers the core members once per process; every entry point that creates a proxy calls it first.</summary>
    let ensure () =
        core.Force ()

    /// <summary>Registers every member the twin serves: the core, event and selector members.</summary>
    let ensureAll () =
        core.Force ()
        extensions.Force ()

[<AutoOpen>]
module NodeNative =
    type TwinNode with
        /// <summary>The node as <c>Fable.Core.TS.Dom</c> sees it: the same object on every read.</summary>
        member this.Native: Fable.Core.TS.Dom.Node =
            Bridge.ensureAll ()
            BridgeCore.ProxyOf this :?> Fable.Core.TS.Dom.Node
