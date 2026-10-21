module DocumentChecks

open System
open Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View
open Ranvier.View.Dom
open CheckSupport

let private raisesArgument (action: unit -> unit) =
    try
        action ()
        false
    with :? ArgumentException ->
        true

[<Emit("document.title")>]
let private title () : string = jsNative

[<Emit("document.title = $0")>]
let private setTitle (value: string) : unit = jsNative

[<Emit("document.head.querySelectorAll($0).length")>]
let private headCount (selector: string) : int = jsNative

[<Emit("(document.head.querySelector($0)?.getAttribute($1) ?? null)")>]
let private headAttribute (selector: string) (name: string) : string = jsNative

/// <summary>Adds a marked meta description to the head, as a server page carries it, and returns it.</summary>
[<Emit("(() => { const meta = document.createElement('meta'); meta.setAttribute('name', 'description'); meta.setAttribute('content', 'server'); meta.setAttribute('data-ranvier-head', 'meta:name=description'); document.head.append(meta); return meta; })()")>]
let private serverDescription () : Node = jsNative

[<Emit("$0.isConnected")>]
let private isConnected (node: Node) : bool = jsNative

[<Emit("(document.head.querySelector($0) === $1)")>]
let private headHolds (selector: string) (node: Node) : bool = jsNative

[<Emit("document")>]
let private currentDocument () : Document = jsNative

[<Emit("document.implementation.createHTMLDocument($0)")>]
let private otherDocument (documentTitle: string) : Document = jsNative

[<Emit("$0.title")>]
let private titleOf (document: Document) : string = jsNative

[<Emit("$0.head.querySelectorAll($1).length")>]
let private headCountOf (document: Document) (selector: string) : int = jsNative

/// <summary>Adds a marked meta description to the document's head and returns it.</summary>
[<Emit("(() => { const meta = $0.createElement('meta'); meta.setAttribute('name', 'description'); meta.setAttribute('data-ranvier-head', 'meta:name=description'); $0.head.append(meta); return meta; })()")>]
let private markedDescriptionIn (document: Document) : Element = jsNative

let private description (content: string) (t: TemplateBuilder<'inst>) =
    t.Document.meta
        [|
            t.Attrs.attribute ("name", "description")
            t.Attrs.attribute ("content", content)
        |]

let private titled =
    Component.define (fun _ (name: string) -> name) (fun t ->
        t.fragment
            [|
                t.Document.title (fun name -> name + " · Shop")
                t.h1 [||] [| t.text (fun name -> name) |]
            |])

let private titleRestored queued () =
    use graph = new Graph ()
    setTitle "Before"

    let mounted =
        mountTemplate queued graph strict (host ()) (Component.render titled "Lamp")

    let shown = title ()
    mounted.Dispose ()
    equal ("Lamp · Shop", "Before") (shown, title ())

let private innerMeta =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> description "inner" t)

let private noMeta =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.fragment [||])

let private outerMeta =
    Component.define (fun _ (opened: Signal<bool>) -> opened) (fun t ->
        t.div
            [||]
            [|
                description "outer" t
                t.branch (fun (opened: Signal<bool>) -> opened.Value) (fun opened -> if opened then t.arm innerMeta () else t.arm noMeta ())
            |])

let private latestShown queued () =
    use graph = new Graph ()
    let opened = graph.Run (fun () -> Api.createSignal true)

    let mounted =
        mountTemplate queued graph strict (host ()) (Component.render outerMeta opened)

    let read () =
        headCount "meta[name=description]", headAttribute "meta[name=description]" "content"

    let first = read ()
    graph.Run (fun () -> opened.Value <- false)
    mounted.Flush ()
    let closed = read ()
    graph.Run (fun () -> opened.Value <- true)
    mounted.Flush ()
    let reopened = read ()
    mounted.Dispose ()
    equal ((1, "inner"), (1, "outer"), (1, "inner"), (0, null)) (first, closed, reopened, read ())

let private linkRow =
    Component.define (fun _ (item: unit -> int) -> item) (fun t ->
        t.fragment
            [|
                t.Document.link
                    [|
                        t.Attrs.attribute ("rel", "stylesheet")
                        t.Attrs.attribute ("href", (fun (item: unit -> int) -> Some (sprintf "/%d.css" (item ()))))
                    |]
                t.Document.link
                    [|
                        t.Attrs.attribute ("rel", "canonical")
                        t.Attrs.attribute ("href", (fun (item: unit -> int) -> Some (sprintf "/%d" (item ()))))
                    |]
            |])

let private linkRows =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.each id (fun _ -> [| 1; 2 |]) linkRow |])

let private linkKeys queued () =
    use graph = new Graph ()

    let mounted =
        mountTemplate queued graph strict (host ()) (Component.render linkRows ())

    let live =
        headCount "link[rel=stylesheet]", headCount "link[rel=canonical]", headAttribute "link[rel=canonical]" "href"

    mounted.Dispose ()
    equal ((2, 1, "/2"), (0, 0)) (live, (headCount "link[rel=stylesheet]", headCount "link[rel=canonical]"))

let private childTitle =
    Component.define (fun _ () -> ()) (fun t -> t.fragment [| t.Document.title "Child"; t.span [||] [||] |])

let private layoutTitle =
    Component.define (fun _ () -> ()) (fun t -> t.fragment [| t.div [||] [| t.render childTitle id |]; t.Document.title "Layout" |])

let private templateNestedTitle queued () =
    use graph = new Graph ()
    setTitle "Before"

    let mounted =
        mountTemplate queued graph strict (host ()) (Component.render layoutTitle ())

    let shown = title ()
    mounted.Dispose ()
    equal ("Child", "Before") (shown, title ())

let private titleEntry (value: string) : HeadEntry =
    {
        Kind = HeadKind.Title
        Key = "title"
        Attributes = [||]
        Readers = [||]
        Title = fun () -> value
    }

/// <summary>A layout component whose title entry follows a div holding a child component's title entry.</summary>
let private nestedTitle (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    setTitle "Before"

    let entry value =
        ops.Embed (fun backend -> backend.Head (titleEntry value))

    let view =
        ops.Component (fun _ ->
            ops.Fragment
                [|
                    Typed.div ops [||] [| ops.Component (fun _ -> entry "Child") |]
                    entry "Layout"
                |])

    let mounted = mountView ops run queued graph strict (host ()) view
    let shown = title ()
    mounted.Dispose ()
    equal ("Child", "Before") (shown, title ())

let private canonicalAndExplicit =
    Component.define (fun _ () -> ()) (fun t ->
        t.fragment
            [|
                t.Document.link
                    [|
                        t.Attrs.attribute ("rel", "canonical")
                        t.Attrs.attribute ("href", "/page")
                    |]
                t.Document.link (
                    "canonical",
                    [|
                        t.Attrs.attribute ("rel", "alternate")
                        t.Attrs.attribute ("href", "/alternate")
                    |]
                )
            |])

let private explicitKeyDistinct queued () =
    use graph = new Graph ()

    let mounted =
        mountTemplate queued graph strict (host ()) (Component.render canonicalAndExplicit ())

    let live =
        headAttribute "link[rel=canonical]" "href", headAttribute "link[rel=alternate]" "href"

    mounted.Dispose ()
    equal (("/page", "/alternate"), (0, 0)) (live, (headCount "link[rel=canonical]", headCount "link[rel=alternate]"))

let mutable private titleSource: Signal<AsyncSource<string>> =
    Unchecked.defaultof<_>

let private pendingTitle =
    Component.define (fun _ (_: BoundaryStatus) -> ()) (fun t ->
        t.fragment
            [|
                t.Document.title (fun () -> titleSource.Value.Value)
                t.span [||] [| t.text "page" |]
            |])

let private titlePending =
    Component.define (fun _ () -> ()) (fun t -> t.span [||] [| t.text "loading" |])

let private titleFailed =
    Component.define (fun _ (failure: BoundaryFailure) -> failure) (fun t ->
        t.span [||] [| t.text (fun failure -> "failed: " + failure.Error.Message) |])

let private guardedTitle =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary titlePending titleFailed pendingTitle |])

let private reactiveTitle queued () =
    use graph = new Graph ()
    setTitle "Before"
    titleSource <- graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<string>()))
    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render guardedTitle ())

    let pending = target.textContent, title ()
    graph.Run (fun () -> titleSource.Value.Settle "Ready")
    mounted.Flush ()
    let revealed = target.textContent, title ()
    let next = graph.Run (fun () -> Api.createAsyncSource<string>())
    graph.Run (fun () -> titleSource.Value <- next)
    mounted.Flush ()
    let refetching = title ()
    graph.Run (fun () -> next.Settle "Again")
    mounted.Flush ()
    let updated = title ()
    mounted.Dispose ()

    equal (("loading", "Before"), ("page", "Ready"), "Ready", "Again", "Before") (pending, revealed, refetching, updated, title ())

let mutable private metaSource: Signal<AsyncSource<string>> = Unchecked.defaultof<_>

let private pendingMeta =
    Component.define (fun _ (_: BoundaryStatus) -> ()) (fun t ->
        t.fragment
            [|
                description "content" t
                t.span [||] [| t.text (fun () -> metaSource.Value.Value) |]
            |])

let private guardedMeta =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary titlePending titleFailed pendingMeta |])

let private hiddenEntries queued () =
    use graph = new Graph ()
    metaSource <- graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<string>()))
    let reports = ResizeArray<exn list>()

    let mounted =
        mountTemplate queued graph reports.Add (host ()) (Component.render guardedMeta ())

    let hidden = headCount "meta[name=description]"
    graph.Run (fun () -> metaSource.Value.Settle "ready")
    mounted.Flush ()
    let shown = headCount "meta[name=description]"
    let next = graph.Run (fun () -> Api.createAsyncSource<string>())
    graph.Run (fun () -> metaSource.Value <- next)
    graph.Run (fun () -> next.Fail (Exception "gone"))
    mounted.Flush ()
    let tornDown = headCount "meta[name=description]"
    mounted.Dispose ()
    equal ((0, 1, 0), [ [ "gone" ] ]) ((hidden, shown, tornDown), messages reports)

let private clientMeta =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| description "client" t |])

let private adoptsMarked queued () =
    use graph = new Graph ()
    let server = serverDescription ()

    let mounted =
        mountTemplate queued graph strict (host ()) (Component.render clientMeta ())

    let adopted =
        headCount "meta[name=description]", headAttribute "meta[name=description]" "content", headHolds "meta[name=description]" server

    mounted.Dispose ()
    equal ((1, "client", true), (0, false)) (adopted, (headCount "meta[name=description]", isConnected server))

/// <summary>An outer and an inner entry of a marked key: the outer one adopts the marked element, the inner one owns a new one.</summary>
let private adoptsOnce queued () =
    use graph = new Graph ()
    let server = serverDescription ()
    let opened = graph.Run (fun () -> Api.createSignal true)

    let mounted =
        mountTemplate queued graph strict (host ()) (Component.render outerMeta opened)

    let read () =
        headCount "meta[name=description]", headAttribute "meta[name=description]" "content", headHolds "meta[name=description]" server

    let first = read ()
    graph.Run (fun () -> opened.Value <- false)
    mounted.Flush ()
    let closed = read ()
    mounted.Dispose ()
    equal ((1, "inner", false), (1, "outer", true), 0) (first, closed, headCount "meta[name=description]")

/// <summary>Two title entries on two graphs; the first activated withdraws while the second is shown.</summary>
let private crossGraphTitles queued () =
    use first = new Graph ()
    use second = new Graph ()
    setTitle "Before"
    let a = mountTemplate queued first strict (host ()) (Component.render titled "A")
    let b = mountTemplate queued second strict (host ()) (Component.render titled "B")
    let both = title ()
    a.Dispose ()
    let afterFirst = title ()
    b.Dispose ()
    equal ("B · Shop", "B · Shop", "Before") (both, afterFirst, title ())

let mutable private siblingSource: Signal<AsyncSource<string>> =
    Unchecked.defaultof<_>

let private pendingSibling =
    Component.define (fun _ (_: BoundaryStatus) -> ()) (fun t ->
        t.fragment
            [|
                description "content" t
                t.span [||] [| t.text (fun () -> siblingSource.Value.Value) |]
            |])

let private siblingRegions =
    Component.define (fun _ (second: Signal<bool>) -> second) (fun t ->
        t.div
            [||]
            [|
                t.boundary titlePending titleFailed pendingSibling
                t.branch (fun (second: Signal<bool>) -> second.Value) (fun second -> if second then t.arm innerMeta () else t.arm noMeta ())
            |])

/// <summary>
/// A pending boundary's entry adopts the marked element, and a sibling region's entry of the same key activates first.
/// The head holds one element of the key throughout.
/// </summary>
let private siblingAdoption (fails: bool) queued () =
    use graph = new Graph ()
    let server = serverDescription ()
    siblingSource <- graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<string>()))
    let second = graph.Run (fun () -> Api.createSignal false)
    let reports = ResizeArray<exn list>()

    let mounted =
        mountTemplate queued graph reports.Add (host ()) (Component.render siblingRegions second)

    let read () =
        headCount "meta[name=description]", headAttribute "meta[name=description]" "content"

    let pending = read ()
    graph.Run (fun () -> second.Value <- true)
    mounted.Flush ()
    let sibling = read ()

    graph.Run (fun () ->
        if fails then
            siblingSource.Value.Fail (Exception "gone")
        else
            siblingSource.Value.Settle "ready")

    mounted.Flush ()
    let settled = read ()
    graph.Run (fun () -> second.Value <- false)
    mounted.Flush ()
    let closed = read (), isConnected server
    mounted.Dispose ()

    let expectedSettled, expectedClosed, expectedReports =
        if fails then
            (1, "inner"), ((0, null), false), [ [ "gone" ] ]
        else
            (1, "content"), ((1, "content"), true), []

    equal
        ((1, "content"), (1, "inner"), expectedSettled, expectedClosed, 0, expectedReports)
        (pending, sibling, settled, closed, headCount "meta[name=description]", messages reports)

/// <summary>A pending boundary's entry adopts the marked element; the element leaves the head when the boundary fails.</summary>
let private pendingAdopterFails queued () =
    use graph = new Graph ()
    let server = serverDescription ()
    siblingSource <- graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<string>()))
    let second = graph.Run (fun () -> Api.createSignal false)
    let reports = ResizeArray<exn list>()

    let mounted =
        mountTemplate queued graph reports.Add (host ()) (Component.render siblingRegions second)

    let pending = headCount "meta[name=description]", isConnected server
    graph.Run (fun () -> siblingSource.Value.Fail (Exception "gone"))
    mounted.Flush ()
    let failed = headCount "meta[name=description]", isConnected server
    mounted.Dispose ()
    equal ((1, true), (0, false), [ [ "gone" ] ]) (pending, failed, messages reports)

let private nullTitle =
    Component.define (fun _ () -> ()) (fun t -> t.fragment [| t.Document.title (fun () -> (null: string)) |])

let private nullTitleEmpty queued () =
    use graph = new Graph ()
    setTitle "Before"

    let mounted =
        mountTemplate queued graph strict (host ()) (Component.render nullTitle ())

    let shown = title ()
    mounted.Dispose ()
    equal ("", "Before") (shown, title ())

/// <summary>Title and meta records of the global document and of a second document.</summary>
let private perDocument () =
    setTitle "Before"
    let other = otherDocument "Other"
    let marked = markedDescriptionIn other
    let here = DocumentHead.create (currentDocument ()) HeadKind.Title "title"
    let there = DocumentHead.create other HeadKind.Title "title"
    DocumentHead.setTitle here "Here"
    DocumentHead.setTitle there "There"
    DocumentHead.activate here
    DocumentHead.activate there
    let titles = title (), titleOf other
    DocumentHead.withdraw here
    let afterHere = title (), titleOf other
    DocumentHead.withdraw there
    let restored = title (), titleOf other

    let hereMeta =
        DocumentHead.create (currentDocument ()) HeadKind.Meta "meta:name=description"

    let thereMeta = DocumentHead.create other HeadKind.Meta "meta:name=description"
    DocumentHead.activate hereMeta
    DocumentHead.activate thereMeta

    let adopted =
        obj.ReferenceEquals (thereMeta.Element, marked),
        obj.ReferenceEquals (hereMeta.Element, marked),
        headCount "meta[data-ranvier-head]",
        headCountOf other "meta[name=description]"

    DocumentHead.withdraw hereMeta
    DocumentHead.withdraw thereMeta

    equal
        (("Here", "There"), ("Before", "There"), ("Before", "Other"), (true, false, 1, 1), (0, 0))
        (titles, afterHere, restored, adopted, (headCount "meta[data-ranvier-head]", headCountOf other "meta[name=description]"))

let private definitionChecks () =
    let defines (build: TemplateBuilder<unit> -> TemplateView<unit>) =
        raisesArgument (fun () -> Component.define (fun _ () -> ()) build |> ignore)

    equal
        [ true; true; true; true; true; true; true; true; true; false ]
        [
            defines (fun t -> t.Document.meta [| t.Attrs.attribute ("name", "x"); t.Events.onClick (fun _ _ -> ()) |])
            defines (fun t ->
                t.Document.meta
                    [|
                        t.Attrs.attribute ("name", "x")
                        t.Props.propertyStatic ((fun (node: HTMLMetaElement) (value: string) -> node.content <- value), "x")
                    |])
            defines (fun t -> t.Document.meta [| t.Attrs.attribute ("name", (fun () -> Some "x")) |])
            defines (fun t ->
                t.Document.meta
                    [|
                        t.Attrs.attribute ("property", "og:x")
                        t.Attrs.attribute ("name", (fun () -> Some "x"))
                    |])
            defines (fun t -> t.Document.meta [| t.Attrs.attribute ("content", "x") |])
            defines (fun t -> t.Document.link ("theme", [| t.Attrs.attribute ("rel", (fun () -> Some "stylesheet")) |]))
            defines (fun t -> t.Document.link [| t.Attrs.attribute ("rel", (fun () -> Some "canonical")) |])
            defines (fun t ->
                t.Document.meta
                    [|
                        t.Attrs.attribute ("name", "x")
                        t.Attrs.attribute ("data-ranvier-head", "meta:name=y")
                    |])
            defines (fun t ->
                t.Document.link
                    [|
                        t.Attrs.attribute ("rel", "stylesheet")
                        t.Attrs.attribute ("data-ranvier-head", (fun () -> Some "x"))
                    |])
            defines (fun t ->
                t.Document.link
                    [|
                        t.Attrs.attribute ("rel", "alternate")
                        t.Attrs.attribute ("name", (fun () -> Some "x"))
                        t.Attrs.attribute ("property", (fun () -> Some "x"))
                    |])
        ]

/// <summary>The template rendered by <c>Html.render</c> as compiled and as expanded into a Program view.</summary>
let private serverEntries () =
    let views =
        [
            Component.render titled "Lamp"
            Component.renderWith Program.ops titled "Lamp"
        ]

    let rejected view =
        try
            Html.render Reject (Program.run view) |> ignore
            false
        with :? InvalidOperationException as error ->
            error.Message = "Document metadata requires Server.renderPage"

    equal [ ("<h1>Lamp</h1>", true); ("<h1>Lamp</h1>", true) ] [ for view in views -> Html.render Omit (Program.run view), rejected view ]

let private earlyChild =
    Component.define (fun _ () -> ()) (fun t -> t.fragment [| t.Document.title "Child"; description "child" t |])

let private lateParent =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [||]
            [|
                t.render earlyChild ignore
                t.Document.title "Parent"
                description "parent" t
            |])

let private titledRow =
    Component.define (fun _ (item: unit -> int) -> item) (fun t -> t.Document.title (fun (item: unit -> int) -> "Row " + string (item ())))

let private rowsThenChild =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [||]
            [|
                t.ul [||] [| t.each id (fun _ -> [| 1; 2 |]) titledRow |]
                t.render earlyChild ignore
            |])

/// <summary>The title text and the meta description of <c>Server.renderPage</c>'s head for <c>definition</c>.</summary>
let private serverShown (definition: Component<unit>) =
    let head = (Server.renderPage Server.Options.defaults definition ()).Head
    let opening = "<title data-ranvier-head=\"title\">"
    let start = head.IndexOf opening + opening.Length
    let marker = "name=\"description\" content=\""
    let content = head.IndexOf marker + marker.Length
    head.Substring (start, head.IndexOf ("</title>", start) - start), head.Substring (content, head.IndexOf ("\"", content) - content)

let private serverHeadShown queued () =
    let clientShown (definition: Component<unit>) =
        use graph = new Graph ()

        let mounted =
            mountTemplate queued graph strict (host ()) (Component.render definition ())

        let shown = title (), headAttribute "meta[name=description]" "content"
        mounted.Dispose ()
        shown

    equal [ ("Child", "child"); ("Row 2", "child") ] [ for definition in [ lateParent; rowsThenChild ] -> clientShown definition ]

    equal [ ("Child", "child"); ("Row 2", "child") ] [ for definition in [ lateParent; rowsThenChild ] -> serverShown definition ]

let private innerRow =
    Component.define (fun _ (_: unit -> int) -> ()) (fun t -> t.fragment [| t.Document.title "Inner"; description "inner" t |])

let private outerRow =
    Component.define (fun _ (item: unit -> int) -> item ()) (fun t ->
        t.fragment
            [|
                t.Document.title (fun (index: int) -> "Row " + string index)
                t.Document.meta
                    [|
                        t.Attrs.attribute ("name", "description")
                        t.Attrs.attribute ("content", fun (index: int) -> Some ("row " + string index))
                    |]
                t.ul
                    [||]
                    [|
                        t.each id (fun (index: int) -> if index = 1 then [| 10 |] else [||]) innerRow
                    |]
            |])

let private nestedLists =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.each id (fun _ -> [| 1; 2 |]) outerRow |])

/// <summary>A template whose view hole holds <c>definition</c> expanded into a Program view.</summary>
let private expandedIn (definition: Component<unit>) =
    Component.define (fun _ () -> Component.renderWith Program.ops definition ()) (fun t -> t.div [||] [| t.view id |])

let private containerShown queued () =
    let clientShown (definition: Component<unit>) =
        use graph = new Graph ()

        let mounted =
            mountTemplate queued graph strict (host ()) (Component.render definition ())

        let shown = title (), headAttribute "meta[name=description]" "content"
        mounted.Dispose ()
        shown

    let definitions = [ nestedLists; expandedIn nestedLists; expandedIn rowsThenChild ]

    let expected = [ ("Inner", "inner"); ("Inner", "inner"); ("Row 2", "child") ]

    equal expected [ for definition in definitions -> clientShown definition ]
    equal expected [ for definition in definitions -> serverShown definition ]

let private holeTitle =
    Component.define (fun _ () -> ()) (fun t -> t.Document.title "Hole")

let private holeThenChild =
    Component.define (fun _ () -> Component.render holeTitle ()) (fun t -> t.div [||] [| t.view id; t.render earlyChild ignore |])

let private holeShown queued () =
    use graph = new Graph ()

    let mounted =
        mountTemplate queued graph strict (host ()) (Component.render holeThenChild ())

    let shown = title (), headAttribute "meta[name=description]" "content"
    mounted.Dispose ()
    equal [ ("Hole", "child"); ("Hole", "child") ] [ shown; serverShown holeThenChild ]

/// <summary>A title entry and an element whose mounted hook logs <c>document.title</c>, the title first when <c>titleFirst</c>.</summary>
let private titleAndHook (titleFirst: bool) =
    Component.define (fun _ (log: ResizeArray<string>) -> log) (fun t ->
        let hooked =
            t.div
                [|
                    t.Hooks.mounted (fun (log: ResizeArray<string>) _ ->
                        log.Add (title ())
                        ignore)
                |]
                [||]

        t.fragment (
            if titleFirst then
                [| t.Document.title "T"; hooked |]
            else
                [| hooked; t.Document.title "T" |]
        ))

let private hookSeesEarlierHead queued () =
    let logged (titleFirst: bool) =
        use graph = new Graph ()
        setTitle "Before"
        let log = ResizeArray<string>()

        let mounted =
            mountTemplate queued graph strict (host ()) (Component.render (titleAndHook titleFirst) log)

        mounted.Dispose ()
        List.ofSeq log

    equal ([ "T" ], [ "Before" ]) (logged true, logged false)

[<Emit("document.head.innerHTML = $0")>]
let private setHead (html: string) : unit = jsNative

[<Emit("document.head.innerHTML")>]
let private headHtml () : string = jsNative

[<Emit("document.head.querySelector($0)")>]
let private inHead (selector: string) : Node = jsNative

[<Emit("Array.from(document.head.querySelectorAll('*'))")>]
let private headElements () : Node array = jsNative

[<Emit("document.head.querySelector('title')?.firstChild ?? null")>]
let private titleText () : Node = jsNative

/// <summary>
/// Records the head's mutations; the returned function stops and returns the count of attribute and text writes, and
/// of child list changes inside a <c>title</c> element.
/// </summary>
[<Emit("(() => { const o = new document.defaultView.MutationObserver(() => {}); o.observe(document.head, { subtree: true, childList: true, attributes: true, characterData: true }); return () => { const n = o.takeRecords().filter(r => r.type !== 'childList' || r.target.nodeName === 'TITLE').length; o.disconnect(); return n; }; })()")>]
let private recordWrites () : unit -> int = jsNative

let private sameNodes (before: Node array) (after: Node array) =
    before.Length = after.Length
    && Array.forall2 (fun (left: Node) (right: Node) -> obj.ReferenceEquals (left, right)) before after

/// <summary>Runs <c>check</c>, then empties the document's head.</summary>
let private withHead (check: unit -> unit) =
    try
        check ()
    finally
        setHead ""

let private hydrationOptions strictly : Hydration.HydrationOptions =
    { IdPrefix = "r"; Strict = strictly }

/// <summary>The props of <c>shopPage</c>: the product name, and the list receiving each instance's name signal.</summary>
type private Shop =
    {
        Name: string
        Names: ResizeArray<Signal<string>>
    }

let private shop name =
    { Name = name; Names = ResizeArray () }

let private shopSetup _ (props: Shop) =
    let name = Api.createSignal props.Name
    props.Names.Add name
    name

let private shopEntries (t: TemplateBuilder<Signal<string>>) =
    [|
        t.Document.title (fun (name: Signal<string>) -> name.Value + " · Shop")
        description "client" t
        t.Document.link
            [|
                t.Attrs.attribute ("rel", "canonical")
                t.Attrs.attribute ("href", "/lamp")
            |]
        t.Document.link
            [|
                t.Attrs.attribute ("rel", "preload")
                t.Attrs.attribute ("href", "/font.woff2")
            |]
    |]

/// <summary>A titled page with a meta description, a canonical link and an unkeyed link; a robots meta too when <c>extra</c>.</summary>
let private shopPage (extra: bool) =
    Component.define shopSetup (fun t ->
        t.fragment
            [|
                yield! shopEntries t
                if extra then
                    yield
                        t.Document.meta
                            [|
                                t.Attrs.attribute ("name", "robots")
                                t.Attrs.attribute ("content", "index")
                            |]
                yield t.h1 [||] [| t.text (fun (name: Signal<string>) -> name.Value) |]
            |])

/// <summary><c>shopPage false</c> with a div after the entries whose mounted hook raises.</summary>
let private failingShop =
    Component.define shopSetup (fun t ->
        t.fragment
            [|
                yield! shopEntries t
                yield t.h1 [||] [| t.text (fun (name: Signal<string>) -> name.Value) |]
                yield t.div [| t.Hooks.mounted (fun _ _ -> raise (InvalidOperationException "hook")) |] [||]
            |])

let private omitted =
    { Server.Options.defaults with
        Policy = Omit
    }

/// <summary>Writes the head of <c>shopPage true</c> for Lamp into the document and returns a host holding its body.</summary>
let private servePage () =
    let page = Server.renderPage omitted (shopPage true) (shop "Lamp")
    setHead page.Head
    let target = host ()
    loadServed target page.Body
    target

/// <summary>The head written by <c>servePage</c>, as the document serialises it.</summary>
let private servedHead =
    "<title data-ranvier-head=\"title\">Lamp · Shop</title>"
    + "<meta data-ranvier-head=\"meta:name=description\" name=\"description\" content=\"client\">"
    + "<link data-ranvier-head=\"link:canonical\" rel=\"canonical\" href=\"/lamp\">"
    + "<link data-ranvier-head=\"\" rel=\"preload\" href=\"/font.woff2\">"
    + "<meta data-ranvier-head=\"meta:name=robots\" name=\"robots\" content=\"index\">"

/// <summary>The head of <c>servePage</c> once hydrated by <c>shopPage false</c>.</summary>
let private hydratedHead (title: string) =
    "<title data-ranvier-head=\"title\">"
    + title
    + "</title>"
    + "<meta data-ranvier-head=\"meta:name=description\" name=\"description\" content=\"client\">"
    + "<link data-ranvier-head=\"link:canonical\" rel=\"canonical\" href=\"/lamp\">"
    + "<link data-ranvier-head=\"\" rel=\"preload\" href=\"/font.woff2\">"

/// <summary>The served title, title text, description, canonical link, unkeyed link and robots meta.</summary>
let private servedElements () =
    [|
        inHead "title"
        titleText ()
        inHead "meta[name=description]"
        inHead "link[rel=canonical]"
        inHead "link[rel=preload]"
        inHead "meta[name=robots]"
    |]

let private headHydrated () =
    withHead (fun () ->
        use graph = new Graph ()
        let target = servePage ()
        let served = servedElements ()
        let names = ResizeArray<Signal<string>>()
        let writes = recordWrites ()

        let mounted =
            Hydration.hydrate graph None strict (hydrationOptions true) target (shopPage false) { Name = "Lamp"; Names = names }

        let written = writes ()
        let now = servedElements ()

        let adopted =
            [ for index in 0..3 -> obj.ReferenceEquals (served[index], now[index]) ]

        let unadopted = isConnected served[4], isConnected served[5]
        let shown = headHtml (), title ()
        graph.Run (fun () -> names[0].Value <- "Desk")

        let updated =
            title (), obj.ReferenceEquals (inHead "title", served[0]), headCount "link[rel=preload]"

        mounted.Dispose ()

        equal
            (0, [ true; true; true; true ], (false, false), (hydratedHead "Lamp · Shop", "Lamp · Shop"), ("Desk · Shop", true, 1))
            (written, adopted, unadopted, shown, updated))

let private spacedTitleKept () =
    withHead (fun () ->
        use graph = new Graph ()
        setHead "<title data-ranvier-head=\"title\">  Lamp  · Shop</title>"
        let text = titleText ()
        let target = host ()
        loadServed target (Server.render omitted (shopPage false) (shop "  Lamp "))
        let writes = recordWrites ()

        let mounted =
            Hydration.hydrate graph None strict (hydrationOptions true) target (shopPage false) (shop "  Lamp ")

        let shown =
            writes (), obj.ReferenceEquals (titleText (), text), title (), (inHead "title").textContent

        mounted.Dispose ()
        equal (0, true, "Lamp · Shop", Some "  Lamp  · Shop") shown)

let private mountAdoptsHead queued () =
    withHead (fun () ->
        use graph = new Graph ()
        servePage () |> ignore
        let served = servedElements ()

        let mounted =
            mountTemplate queued graph strict (host ()) (Component.render (shopPage false) (shop "Lamp"))

        let now = servedElements ()

        let adopted =
            [ for index in [ 0; 2; 3 ] -> obj.ReferenceEquals (served[index], now[index]) ]

        let counts =
            [
                headCount "title"
                headCount "meta[name=description]"
                headCount "link[rel=canonical]"
                headCount "link[rel=preload]"
                headCount "meta[name=robots]"
            ]

        let kept = isConnected served[4], isConnected served[5], title ()
        mounted.Dispose ()
        equal ([ true; true; true ], [ 1; 1; 1; 2; 1 ], (true, true, "Lamp · Shop")) (adopted, counts, kept))

let private preloadOnly =
    Component.define (fun _ () -> ()) (fun t ->
        t.fragment
            [|
                t.Document.link
                    [|
                        t.Attrs.attribute ("rel", "preload")
                        t.Attrs.attribute ("href", "/font.woff2")
                    |]
                t.span [||] [| t.text "page" |]
            |])

let private unkeyedLinkReplaced () =
    withHead (fun () ->
        use graph = new Graph ()
        setHead "<link data-ranvier-head=\"\" rel=\"preload\" href=\"/font.woff2\">"
        let server = inHead "link[rel=preload]"
        let target = host ()
        loadServed target (Server.render omitted preloadOnly ())

        let mounted =
            Hydration.hydrate graph None strict (hydrationOptions true) target preloadOnly ()

        let shown =
            headCount "link[rel=preload]", obj.ReferenceEquals (inHead "link[rel=preload]", server), isConnected server, headHtml ()

        mounted.Dispose ()

        equal (1, false, false, "<link data-ranvier-head=\"\" rel=\"preload\" href=\"/font.woff2\">") shown)

/// <summary>The state of one case's <c>pendingHead</c> instances.</summary>
type private HeadContent =
    {
        /// <summary>Whether an instance settles its source during setup.</summary>
        mutable Ready: bool
        /// <summary>The sources created by the instances, in setup order.</summary>
        Sources: ResizeArray<AsyncSource<string>>
    }

let private pendingHead (content: HeadContent) =
    Component.define
        (fun _ (_: BoundaryStatus) ->
            let source = Api.createAsyncSource<string>()

            if content.Ready then
                source.Settle "ready"

            content.Sources.Add source
            source)
        (fun t ->
            t.fragment
                [|
                    t.Document.title "Client"
                    t.Document.meta
                        [|
                            t.Attrs.attribute ("name", "description")
                            t.Attrs.attribute ("content", (fun (_: AsyncSource<string>) -> Some "client"))
                        |]
                    t.span [||] [| t.text (fun (source: AsyncSource<string>) -> source.Value) |]
                |])

let private guardedHead (content: HeadContent) =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary titlePending titleFailed (pendingHead content) |])

let private pendingHeadAdopted () =
    withHead (fun () ->
        use graph = new Graph ()
        let target = host ()

        let content =
            {
                Ready = true
                Sources = ResizeArray ()
            }

        let guarded = guardedHead content
        loadServed target (Server.render omitted guarded ())
        content.Ready <- false
        content.Sources.Clear ()

        setHead (
            "<title data-ranvier-head=\"title\">Server</title>"
            + "<meta data-ranvier-head=\"meta:name=description\" content=\"server\" data-extra=\"x\">"
            + "<meta data-ranvier-head=\"meta:name=robots\" name=\"robots\" content=\"index\">"
        )

        let server = inHead "meta[data-ranvier-head]"
        let robots = inHead "meta[name=robots]"

        let mounted =
            Hydration.hydrate graph None strict (hydrationOptions true) target guarded ()

        let read () =
            obj.ReferenceEquals (inHead "meta[data-ranvier-head]", server),
            isConnected server,
            (server :?> Element).getAttribute "content",
            (server :?> Element).getAttribute "data-extra",
            title ()

        let pending = read (), isConnected robots
        graph.Run (fun () -> content.Sources[0].Settle "ready")
        let revealed = read (), target.textContent
        mounted.Dispose ()

        equal
            (((true, true, Some "client", Some "x", "Server"), false), ((true, true, Some "client", Some "x", "Client"), "ready"))
            (pending, revealed))

let private headRestoredOnFailure () =
    let check (head: string) (definition: Component<Shop>) (body: string) =
        withHead (fun () ->
            use graph = new Graph ()
            setHead head
            let target = host ()
            loadServed target body
            let served = headElements ()
            let text = titleText ()

            let failed =
                try
                    Hydration.hydrate graph None strict (hydrationOptions true) target definition (shop "Desk")
                    |> ignore

                    false
                with _ ->
                    true

            equal
                (true, head, true, true, "Lamp · Shop")
                (failed, headHtml (), sameNodes served (headElements ()), obj.ReferenceEquals (titleText (), text), title ()))

    let page = Server.render omitted (shopPage false) (shop "Lamp")

    let trailing =
        page.Substring (0, page.Length - 8)
        + "<b></b><!--]-->"

    check servedHead (shopPage false) trailing
    check servedHead failingShop (Server.render omitted failingShop (shop "Lamp"))
    // The client writes its description and canonical values over the served ones at bind, before the mismatch.
    check (servedHead.Replace("content=\"client\"", "content=\"server\"").Replace("href=\"/lamp\"", "href=\"/old\"")) (shopPage false) trailing

let private headRecovered () =
    withHead (fun () ->
        use graph = new Graph ()
        servePage () |> ignore
        let served = servedElements ()
        let target = host ()
        let page = Server.render omitted (shopPage false) (shop "Lamp")

        loadServed
            target
            (page.Substring (0, page.Length - 8)
             + "<b></b><!--]-->")

        let reports = ResizeArray<exn list>()

        let mounted =
            Hydration.hydrate graph None reports.Add (hydrationOptions false) target (shopPage false) (shop "Desk")

        let now = servedElements ()

        let shown =
            [ for index in [ 0; 2; 3 ] -> obj.ReferenceEquals (served[index], now[index]) ],
            (isConnected served[4], isConnected served[5]),
            headHtml (),
            title (),
            reports.Count

        mounted.Dispose ()
        equal ([ true; true; true ], (false, false), hydratedHead "Desk · Shop", "Desk · Shop", 1) shown)

/// <summary>A description meta without content, then a div whose mounted hook raises.</summary>
let private contentless =
    Component.define (fun _ () -> ()) (fun t ->
        t.fragment
            [|
                t.Document.meta
                    [|
                        t.Attrs.attribute ("name", "description")
                        t.Attrs.attribute ("content", (fun (_: unit) -> (None: string option)))
                    |]
                t.div [| t.Hooks.mounted (fun _ _ -> raise (InvalidOperationException "hook")) |] [||]
            |])

let private removedAttributeRestoredInPlace () =
    withHead (fun () ->
        use graph = new Graph ()

        let head =
            "<meta data-ranvier-head=\"meta:name=description\" content=\"server\" name=\"description\">"

        setHead head
        let target = host ()
        loadServed target (Server.render omitted contentless ())
        let served = inHead "meta"
        let writes = recordWrites ()

        let failed =
            try
                Hydration.hydrate graph None strict (hydrationOptions true) target contentless ()
                |> ignore

                "none"
            with error ->
                error.Message

        equal ("hook", true, head, true) (failed, writes () > 0, headHtml (), obj.ReferenceEquals (inHead "meta", served)))

let private freshMountFailureRestoresHead () =
    withHead (fun () ->
        use graph = new Graph ()
        setHead servedHead
        let served = headElements ()
        let text = titleText ()
        let target = host ()
        let page = Server.render omitted failingShop (shop "Lamp")

        loadServed
            target
            (page.Substring (0, page.Length - 8)
             + "<b></b><!--]-->")

        let reports = ResizeArray<exn list>()

        let failed =
            try
                Hydration.hydrate graph None reports.Add (hydrationOptions false) target failingShop (shop "Desk")
                |> ignore

                "none"
            with error ->
                error.Message

        equal
            ("hook", 1, servedHead, true, true, "Lamp · Shop")
            (failed, reports.Count, headHtml (), sameNodes served (headElements ()), obj.ReferenceEquals (titleText (), text), title ()))

/// <summary>Changes the href of the head's first link, when the head has one, and adds a media attribute to it.</summary>
[<Emit("(() => { const link = document.head.querySelector('link'); if (link) { link.setAttribute('href', '/client.css'); link.setAttribute('media', 'print'); } })()")>]
let private editStylesheet () : unit = jsNative

/// <summary>A div whose setup edits the head's first link and whose mounted hook raises.</summary>
let private stylesheetEditor =
    Component.define (fun _ () -> editStylesheet ()) (fun t -> t.div [| t.Hooks.mounted (fun _ _ -> raise (InvalidOperationException "hook")) |] [||])

let private unmarkedEditRestored () =
    withHead (fun () ->
        use graph = new Graph ()
        let target = host ()
        loadServed target (Server.render omitted stylesheetEditor ())
        let head = "<link rel=\"stylesheet\" href=\"/served.css\">"
        setHead head

        let failed =
            try
                Hydration.hydrate graph None strict (hydrationOptions true) target stylesheetEditor ()
                |> ignore

                "none"
            with error ->
                error.Message

        equal ("hook", head) (failed, headHtml ()))

[<Emit("document.head.querySelector('link').setAttribute('href', $0)")>]
let private setStylesheet (href: string) : unit = jsNative

let private plainSpan =
    Component.define (fun _ () -> ()) (fun t -> t.span [||] [| t.text "page" |])

let private earlierEditsKept () =
    withHead (fun () ->
        use graph = new Graph ()
        let plain = host ()
        loadServed plain (Server.render omitted plainSpan ())
        let target = host ()
        loadServed target (Server.render omitted stylesheetEditor ())
        setHead "<link rel=\"stylesheet\" href=\"/served.css\">"

        let hydrated =
            Hydration.hydrate graph None strict (hydrationOptions true) plain plainSpan ()

        setStylesheet "/between.css"

        let failed =
            try
                Hydration.hydrate graph None strict (hydrationOptions true) target stylesheetEditor ()
                |> ignore

                "none"
            with error ->
                error.Message

        hydrated.Dispose ()
        equal ("hook", "<link rel=\"stylesheet\" href=\"/between.css\">") (failed, headHtml ()))

[<Emit("$0.head.innerHTML = $1")>]
let private setHeadOf (document: Document) (html: string) : unit = jsNative

[<Emit("$0.head.innerHTML")>]
let private headHtmlOf (document: Document) : string = jsNative

[<Emit("$0.body.appendChild($0.createElement('div'))")>]
let private hostIn (document: Document) : HTMLElement = jsNative

let private windowlessHeadRestored () =
    let other = otherDocument "Other"
    setHeadOf other servedHead
    let target = hostIn other
    let page = Server.render omitted (shopPage false) (shop "Lamp")

    loadServed
        target
        (page.Substring (0, page.Length - 8)
         + "<b></b><!--]-->")

    let failed =
        try
            use graph = new Graph ()

            Hydration.hydrate graph None strict (hydrationOptions true) target (shopPage false) (shop "Desk")
            |> ignore

            false
        with _ ->
            true

    equal (true, servedHead, "Lamp · Shop") (failed, headHtmlOf other, titleOf other)

let private metaRow =
    Component.define (fun _ (item: unit -> int) -> item) (fun t ->
        t.fragment
            [|
                t.Document.meta
                    [|
                        t.Attrs.attribute ("name", "row")
                        t.Attrs.attribute ("content", fun (item: unit -> int) -> Some ("row " + string (item ())))
                    |]
                t.li [||] [| t.text (fun (item: unit -> int) -> string (item ())) |]
            |])

/// <summary>A titled page with a meta description and a list whose rows each bind a row meta.</summary>
let private rowsPage =
    Component.define (fun _ (items: int array) -> items) (fun t ->
        t.fragment
            [|
                t.Document.title "Page"
                description "client" t
                t.ul [||] [| t.each id (fun (items: int array) -> items) metaRow |]
            |])

let private characterCell =
    Component.define (fun _ (text: unit -> string) -> text) (fun t ->
        t.element (HtmlTag "b") [||] [| t.text (fun (text: unit -> string) -> text ()) |])

let private characterRow =
    Component.define (fun _ (item: unit -> string) -> item) (fun t ->
        t.fragment
            [|
                t.Document.meta
                    [|
                        t.Attrs.attribute ("name", "row")
                        t.Attrs.attribute ("content", fun (item: unit -> string) -> Some ("row " + item ()))
                    |]
                t.li
                    [||]
                    [|
                        t.each id (fun (item: unit -> string) -> [| for ch in item () -> string ch |]) characterCell
                    |]
            |])

/// <summary>A titled page with a meta description and a list whose rows bind a row meta and list their characters.</summary>
let private charactersPage =
    Component.define (fun _ (items: string array) -> items) (fun t ->
        t.fragment
            [|
                t.Document.title "Page"
                description "client" t
                t.ul [||] [| t.each id (fun (items: string array) -> items) characterRow |]
            |])

/// <summary>Writes the head of <c>definition</c>'s page for <c>props</c>, with a robots meta, and returns a host holding its body.</summary>
let private servePageWithRobots (definition: Component<'props>) (props: 'props) =
    let page = Server.renderPage omitted definition props

    setHead (
        page.Head
        + "<meta data-ranvier-head=\"meta:name=robots\" name=\"robots\" content=\"index\">"
    )

    let target = host ()
    loadServed target page.Body
    target

let private nestedStrictRestoresHead () =
    withHead (fun () ->
        use graph = new Graph ()
        let target = servePageWithRobots charactersPage [| "ab" |]
        let head = headHtml ()
        let served = headElements ()
        let body = target.innerHTML

        let failed =
            try
                Hydration.hydrate graph None strict (hydrationOptions true) target charactersPage [| "abc" |]
                |> ignore

                "none"
            with
            | TemplateDom.HydrationMismatchException _ as error -> error.Message
            | error -> "other " + error.Message

        equal
            ("Hydration mismatch at #0: expected <b>, found <!--]-->", head, true, "row ab", body)
            (failed, headHtml (), sameNodes served (headElements ()), headAttribute "meta[name=row]" "content", target.innerHTML))

let private regionRecoveryRemovesOnce () =
    withHead (fun () ->
        use graph = new Graph ()
        let target = servePageWithRobots rowsPage [| 1; 2 |]
        let titleElement = inHead "title"
        let descriptionElement = inHead "meta[name=description]"
        let row = inHead "meta[name=row]"
        let robots = inHead "meta[name=robots]"
        // Whether the robots meta is in the head at each report, and the report's length.
        let atReport = ResizeArray<bool * int>()

        let report (errors: exn list) =
            atReport.Add (isConnected robots, errors.Length)

        let mounted =
            Hydration.hydrate graph None report (hydrationOptions false) target rowsPage [| 1; 2; 3 |]

        let shown =
            List.ofSeq atReport,
            isConnected robots,
            obj.ReferenceEquals (inHead "title", titleElement),
            obj.ReferenceEquals (inHead "meta[name=description]", descriptionElement),
            obj.ReferenceEquals (inHead "meta[name=row]", row),
            headCount "meta[name=row]",
            target.textContent,
            headHtml ()

        mounted.Dispose ()

        equal
            ([ true, 1 ],
             false,
             true,
             true,
             false,
             1,
             "123",
             "<title data-ranvier-head=\"title\">Page</title>"
             + "<meta data-ranvier-head=\"meta:name=description\" name=\"description\" content=\"client\">"
             + "<meta data-ranvier-head=\"meta:name=row\" name=\"row\" content=\"row 3\">")
            shown)

let cases: (string * (unit -> unit)) array =
    strictCases (
        Array.concat
            [
                templated "a title entry sets document.title once placed and restores the previous title when disposed" titleRestored
                templated "the most recent entry for a key is shown and the previous one returns when it withdraws" latestShown
                templated "unkeyed links are each present while live; a canonical link is keyed" linkKeys
                templated "a link with the explicit key canonical and a canonical link are both shown" explicitKeyDistinct
                templated "an inlined child's title entry is shown over its parent's later title entry" templateNestedTitle
                both
                    "an inlined child's title entry is shown over its parent's later title entry"
                    (nestedTitle Tree.ops Tree.run)
                    (nestedTitle Program.ops Program.run)
                templated
                    "renderPage's head shows the title and meta a client mount shows for an inlined child before its parent's later entries and rows after both"
                    serverHeadShown
                templated
                    "renderPage's head shows the title and meta a client mount shows for nested lists and for rows and a child expanded in a view hole"
                    containerShown
                templated "renderPage's head shows the title a client mount shows for a view hole before a later inlined child" holeShown
                templated "a reactive title updates document.title and its pending read waits under a boundary" reactiveTitle
                templated "entries under hidden content activate at reveal and withdraw on teardown" hiddenEntries
                templated "a client mount adopts marked head elements without hydrate" adoptsMarked
                templated "a marked head element is adopted by one entry of its key" adoptsOnce
                templated
                    "a pending entry's adopted element gives way to a sibling entry of its key and leaves when the boundary fails"
                    (siblingAdoption true)
                templated "a pending entry's adopted element gives way to a sibling entry of its key and returns at reveal" (siblingAdoption false)
                templated "a pending entry's adopted element leaves the head when the boundary fails" pendingAdopterFails
                templated "a title reader returning null sets an empty document.title" nullTitleEmpty
                templated "a mounted hook sees the title entries before its element in document order and not those after it" hookSeesEarlierHead
                templated "title entries from two graphs withdrawn out of order keep the shown title and restore the original" crossGraphTitles
                [|
                    "event and property parts on a document entry fail at definition time; a dynamic key attribute and a data-ranvier-head attribute fail",
                    definitionChecks
                    "Html.render writes no document entry under Omit and raises under Reject", serverEntries
                    "each document keeps its own title and adopts only its own marked elements", perDocument
                    "hydration adopts marked head elements by key and removes unadopted ones", headHydrated
                    "hydration replaces a served unkeyed link with the client's own", unkeyedLinkReplaced
                    "hydration leaves a served title with spacing equal to the entry's text unwritten", spacedTitleKept
                    "an entry under pending content keeps its adopted element through hydration and shows the client's attributes before and after reveal",
                    pendingHeadAdopted
                    "a failed hydration leaves the head as the server wrote it", headRestoredOnFailure
                    "a hydration that re-renders the root adopts the head as a fresh mount and removes unadopted elements", headRecovered
                    "a failed hydration puts an attribute the client removed back in its served position", removedAttributeRestoredInPlace
                    "a re-rendered root whose fresh mount fails leaves the head as the server wrote it", freshMountFailureRestoresHead
                    "a failed hydration sets back the attributes setup wrote on an unmarked head element", unmarkedEditRestored
                    "a failed hydration keeps the head edits made after an earlier hydration returned", earlierEditsKept
                    "a failed hydration in a document without a window leaves its head as the server wrote it", windowlessHeadRestored
                    "a Strict mismatch in a row's nested list leaves the head as the server wrote it, attributes written at bind included",
                    nestedStrictRestoresHead
                    "a re-rendered list region recreates its rows' head elements and unadopted elements are removed once, after hydrate returns",
                    regionRecoveryRemovesOnce
                |]
                templated "a client mount adopts marked elements without hydrate and removes none" mountAdoptsHead
            ]
    )
