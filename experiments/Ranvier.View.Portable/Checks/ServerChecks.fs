/// <summary>Server HTML checks, run on .NET by RepresentationChecks and under Fable by tests/server.test.mjs.</summary>
module ServerChecks

open System
open global.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open ServerFixtures

let equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

/// <summary>The exception <c>action</c> raises; fails when it returns.</summary>
let failure (action: unit -> unit) : exn =
    let mutable caught: exn = null

    try
        action ()
    with error ->
        caught <- error

    if isNull caught then
        failwith "Expected an exception"

    caught

let private templatePins () =
    equal """<div class="panel" title="T &amp;">Hello <!--!-->&lt;Ada&gt;<!--!-->!</div>""" (renderOmit greeting greetingProps)
    equal """<div class="panel" title="T &amp;"><span>&lt;Ada&gt;</span></div>""" (renderOmit named greetingProps)
    equal "<button></button>" (renderOmit clicking greetingProps)
    equal "<div></div>" (renderOmit nullTitle greetingProps)

    equal
        """<div class="card">A<!--[--><em>&lt;b&gt;</em><!--]--></div>"""
        (renderOmit
            holed
            {
                Caption = "A"
                Body = Program.element (HtmlTag "em") [||] [| Program.text "<b>" |]
            })

    equal
        """<div class="card">B<!--[--><!--]--></div>"""
        (renderOmit
            holed
            {
                Caption = "B"
                Body = Program.fragment [||]
            })

    equal "<ul><!--[--><li>a</li><li>&lt;b&gt;</li><!--]--></ul>" (renderOmit listed listedItems)
    equal "<ul><!--[--><!--]--></ul>" (renderOmit listed [||])
    equal """<div>&lt;Ada&gt;<span title="3">3</span></div>""" (renderOmit carded greetingProps)
    equal "<span>a<!--!-->&lt;Ada&gt;</span>" (renderOmit besideText greetingProps)

    equal
        [
            """<button title="a" aria-busy="false" data-state="x">go</button>"""
            """<button title="b" id="n" disabled="" aria-expanded="false" aria-busy="true" data-state="y">go</button>"""
            """<button title="c" id="m" aria-expanded="true" aria-busy="false" data-state="z">go</button>"""
            """<button aria-busy="false" data-state="n">go</button>"""
        ]
        [ for flags in flagCases -> renderOmit flagsTemplate flags ]

    equal
        """<div><button aria-expanded="false" title="t"></button><button disabled="" aria-expanded="true" id="i" data-k="v"></button></div>"""
        (renderOmit staticTemplate ())

    equal """<div class="panel" title="T &amp;">Hello <!--!-->&lt;Ada&gt;<!--!-->!</div><span>&lt;Ada&gt;</span>""" (renderOmit pair greetingProps)

let private armPins () =
    equal "<div><!--[a:6a39ec02--><span>working 3</span><!--]--></div>" (renderOmit staged (Working 3))
    equal "<div><!--[a:6a39ec02--><span>complete &lt;ok&gt;</span><!--]--></div>" (renderOmit staged (Complete "<ok>"))
    equal "<div><!--[a:6a39ec02--><span>4</span><!--]--></div>" (renderOmit decades 42)

let private boundaryPins () =
    use graph = new Graph ()
    boundarySource <- graph.Run (fun () -> Api.createAsyncSource<string>())
    equal "<div><!--[p--><span>loading</span><!--]--></div>" (renderOmit guarded ())
    graph.Run (fun () -> boundarySource.Settle "<A>")
    equal "<div><!--[c--><span>&lt;A&gt;</span><!--]--></div>" (renderOmit guarded ())
    graph.Run (fun () -> boundarySource.Fail (Exception "<bad>"))
    equal "<div><!--[e--><span>&lt;bad&gt;</span><!--]--></div>" (renderOmit guarded ())

let private edgePins () =
    equal "<span><!--_--></span>" (renderOmit emptyText "")
    equal "A void element has children at div/br" (failure (fun () -> renderOmit voidChildren () |> ignore)).Message

    equal "The HTML parser restructures this markup (table-row) at table/tr" (failure (fun () -> renderOmit rowInTable () |> ignore)).Message

let private cleanupLog = ResizeArray<string>()

let private loggedChild =
    Component.define (fun setup () -> setup.OnCleanup (fun () -> cleanupLog.Add "child")) (fun t -> t.span [||] [| t.text "c" |])

let private loggedRow =
    Component.define
        (fun setup (item: unit -> int) ->
            let value = item ()
            setup.OnCleanup (fun () -> cleanupLog.Add ("row " + string value)))
        (fun t -> t.li [||] [||])

let private loggedParent =
    Component.define
        (fun setup (rows: int array) ->
            setup.OnCleanup (fun () -> cleanupLog.Add "parent")
            rows)
        (fun t -> t.div [||] [| t.render loggedChild (fun _ -> ()); t.ul [||] [| t.each id id loggedRow |] |])

let private inlinedCleanupOrder () =
    cleanupLog.Clear ()
    let html = renderOmit loggedParent [| 1; 2 |]
    equal "<div><span>c</span><ul><!--[--><li></li><li></li><!--]--></ul></div>" html
    equal [ "row 1"; "row 2"; "parent"; "child" ] (List.ofSeq cleanupLog)

let private rawContent =
    Component.define (fun _ (markup: string) -> markup) (fun t ->
        t.fragment
            [|
                t.div [| t.Unsafe.html (fun markup -> UnsafeHtml.Trust markup) |] [||]
                t.span [||] [| t.text "<b>" |]
            |])

let private rawHtmlPin () =
    equal "<div><b>raw</b> &amp; <!--note--></div><span>&lt;b&gt;</span>" (renderOmit rawContent "<b>raw</b> &amp; <!--note-->")

let private rawHtmlForgesNoMarker () =
    let rejected markup =
        let error = failure (fun () -> renderOmit rawContent markup |> ignore)

        error :? InvalidOperationException
        && error.Message = "Raw HTML cannot contain a comment that starts with [ or equals ]"

    let markers =
        [
            "<!--[-->x"
            "a<!--]-->"
            "<!--[a:0123abcd-->"
            "<!--[ x"
            "<![>"
            "</[>"
            "<!]>"
            "<!--x--!><!--[-->"
            "<!--]--!>"
            "<!--]-"
            "</]"
            "<![CDATA[x]]>"
        ]

    equal (List.map (fun _ -> true) markers) [ for markup in markers -> rejected markup ]

    let accepted =
        "<!-- [--><!--]x--><!-- ]--><!--]--x--><!doctype html><?xml?><!x></ x>"

    equal
        ("<div>"
         + accepted
         + "</div><span>&lt;b&gt;</span>")
        (renderOmit rawContent accepted)

let private nullEscape () =
    equal ("", "") (Html.escape false null, Html.escape true null)

    equal
        "<div title=\"\"></div>"
        (Html.render Omit (Program.run (Program.element Div [| Attribute ("title", null) |] [| Program.bind (=) (fun () -> (null: string)) |])))

let private preformatted =
    Component.define (fun _ (value: string) -> value) (fun t ->
        t.fragment
            [|
                t.pre [||] [| t.text (fun value -> value) |]
                t.element (HtmlTag "listing") [||] [| t.text "\nstatic" |]
                t.div [||] [| t.text (fun value -> value) |]
            |])

let private leadingLineFeed () =
    equal "<pre>\n\nslot</pre><listing>\n\nstatic</listing><div>\nslot</div>" (renderOmit preformatted "\nslot")
    equal "<pre>slot</pre><listing>\n\nstatic</listing><div>slot</div>" (renderOmit preformatted "slot")

let private escapeCorpus () =
    equal "&lt;&amp;&gt;&#13;\"'" (Html.escape false "<&>\r\"'")
    equal "&lt;&amp;&gt;&#13;&quot;'" (Html.escape true "<&>\r\"'")
    equal "\uD83D\uDE00 plain" (Html.escape false "\uD83D\uDE00 plain")
    equal "&lt;!--]--&gt;" (Html.escape false "<!--]-->")
    equal "&lt;/script&gt;" (Html.escape true "</script>")
    equal "a&amp;&amp;b" (Html.escape false "a&&b")
    equal "" (Html.escape true "")

let private namesAtDefinition () =
    let define (part: TemplateBuilder<unit> -> TemplateView<unit>) () =
        Component.define (fun _ () -> ()) part |> ignore

    let rejected =
        [
            failure (define (fun t -> t.div [| t.Attrs.attribute ("onclick", "x") |] [||]))
            failure (define (fun t -> t.div [| t.Attrs.attribute ("srcdoc", "x") |] [||]))
            failure (define (fun t -> t.div [| t.Attrs.attribute ("Title", "x") |] [||]))
            failure (define (fun t -> t.div [| t.Attrs.attribute ("", "x") |] [||]))
            failure (define (fun t -> t.element (HtmlTag "DIV") [||] [||]))
            failure (define (fun t -> t.element (HtmlTag "") [||] [||]))
            failure (define (fun t -> t.div [| t.Attrs.attribute ("oNclick", "x") |] [||]))
            failure (define (fun t -> t.div [| t.Attrs.attribute ("ONCLICK", "x") |] [||]))
            failure (define (fun t -> t.div [| t.Attrs.attribute ("srcDoc", "x") |] [||]))
            failure (define (fun t -> t.div [| t.Attrs.attribute ("className", "x") |] [||]))
            failure (define (fun t -> t.element (HtmlTag "sCript") [||] [||]))
            failure (define (fun t -> t.element (HtmlTag "iFrame") [||] [||]))
            failure (define (fun t -> t.element (HtmlTag "tAble") [||] [||]))
            failure (define (fun t -> t.element (HtmlTag "sTyle") [||] [||]))
            failure (define (fun t -> t.div [| t.Attrs.attribute ("refX refY", "x") |] [||]))
            failure (define (fun t -> t.element (HtmlTag "feBlend feColorMatrix") [||] [||]))
            failure (define (fun t -> t.div [| t.Attrs.attribute (null, "x") |] [||]))
        ]

    equal (List.replicate 17 true) [ for error in rejected -> error.Message.Contains "names must" ]

#if !FABLE_COMPILER
    let caseTwins =
        failure (define (fun t -> t.element (HtmlTag "svg") [| t.Attrs.attribute ("viewBox", "1"); t.Attrs.attribute ("viewbox", "2") |] [||]))

    equal true (caseTwins.Message.Contains "differ only by case")
#endif

    // An SVG tag makes the SVG names available under Fable.
    Ranvier.View.Foundation.Sugar.SvgElements.svg<unit>()
    |> ignore

    let svg =
        Component.define (fun _ () -> ()) (fun t ->
            t.element
                (HtmlTag "svg")
                [|
                    t.Attrs.attribute ("viewBox", "0 0 1 1")
                    t.Attrs.attribute ("preserveAspectRatio", "none")
                |]
                [|
                    t.element (HtmlTag "linearGradient") [| t.Attrs.attribute ("gradientUnits", "userSpaceOnUse") |] [||]
                    t.element (HtmlTag "feGaussianBlur") [| t.Attrs.attribute ("stdDeviation", "2") |] [||]
                    t.element (HtmlTag "foreignObject") [| t.Attrs.attribute ("refX", "1") |] [||]
                |])

    equal
        """<svg viewBox="0 0 1 1" preserveAspectRatio="none"><linearGradient gradientUnits="userSpaceOnUse"></linearGradient><feGaussianBlur stdDeviation="2"></feGaussianBlur><foreignObject refX="1"></foreignObject></svg>"""
        (renderOmit svg ())

let private sharedPlan () =
    equal true (obj.ReferenceEquals (serverPlan greeting, serverPlan greeting))

    match (serverPlan staticTemplate).Ops with
    | [| Static _ |] -> ()
    | ops -> failwithf "Expected one merged static segment; got %d operations" ops.Length

let private browserRejection () =
    let error =
        failure (fun () ->
            Html.render Reject (Program.run (Component.render clicking greetingProps))
            |> ignore)

    equal (true, true) (error :? InvalidOperationException, error.Message.Contains "<button> listener click")

/// <summary>An owner whose cleanups run when it is disposed, after its children's.</summary>
type private StandInOwner() =
    member val Children = ResizeArray<StandInOwner>()
    member val Cleanups = ResizeArray<unit -> unit>()

    member this.Dispose() =
        for child in this.Children do
            child.Dispose ()

        for cleanup in this.Cleanups do
            cleanup ()

/// <summary>A server scope that gives each nested instance a child owner of <c>owner</c>.</summary>
type private OwnedScope(owner: StandInOwner, identity: IdentityScope) =
    inherit ServerScope()

    override _.Nest body =
        let child = StandInOwner ()
        owner.Children.Add child

        body
            (OwnedScope (child, identity))
            {
                OnCleanup = fun cleanup -> child.Cleanups.Add cleanup
                CreateId = identity.CreateId
            }

    override this.Attempt body = body this

let private holeChild =
    Component.define (fun setup () -> setup.OnCleanup (fun () -> cleanupLog.Add "hole child")) (fun t -> t.span [||] [| t.text "in" |])

let private holeHolder =
    Component.define
        (fun setup (body: Program.View) ->
            setup.OnCleanup (fun () -> cleanupLog.Add "holder")
            body)
        (fun t -> t.div [||] [| t.view id |])

let private holeTemplateOwnedByRequest () =
    cleanupLog.Clear ()
    let root = StandInOwner ()
    let sink = HtmlSink ()
    let backend = HtmlBackend (Omit, sink)
    backend.Scope <- OwnedScope (root, backend.Identity)
    Program.run (Component.render holeHolder (Component.render holeChild ())) backend
    let during = List.ofSeq cleanupLog
    root.Dispose ()

    equal
        ("<div><!--[--><span>in</span><!--]--></div>", List.empty<string>, [ "hole child"; "holder" ])
        (sink.ToString (), during, List.ofSeq cleanupLog)

let private requestHolder =
    Component.define
        (fun setup (body: Program.View) ->
            setup.OnCleanup (fun () -> cleanupLog.Add "holder")
            body)
        (fun t -> t.div [||] [| t.view id; t.text (fun _ -> string cleanupLog.Count) |])

let private holeTemplateOwnedByRequestScope () =
    cleanupLog.Clear ()

    let html =
        Server.render Server.Options.defaults requestHolder (Component.render holeChild ())

    equal ("<!--[r--><div><!--[--><span>in</span><!--]-->0</div><!--]-->", [ "hole child"; "holder" ]) (html, List.ofSeq cleanupLog)

let private requestRoot () =
    let log = ResizeArray<string>()
    let owners = ResizeArray<Owner>()
    let graphs = ResizeArray<Graph>()

    let child =
        Component.define
            (fun setup (name: string) ->
                owners.Add (Api.getOwner ())
                setup.OnCleanup (fun () -> log.Add ("cleanup " + name))
                Api.createSignal name)
            (fun t -> t.span [||] [| t.text (fun (name: Signal<string>) -> name.Value) |])

    let parent =
        Component.define
            (fun setup () ->
                graphs.Add Graph.Current
                owners.Add (Api.getOwner ())
                setup.OnCleanup (fun () -> log.Add "cleanup parent"))
            (fun t -> t.div [||] [| t.render child (fun _ -> "a"); t.render child (fun _ -> "b") |])

    equal "<!--[r--><div><span>a</span><span>b</span></div><!--]-->" (Server.render Server.Options.defaults parent ())
    equal ("cleanup parent", [ "cleanup a"; "cleanup b"; "cleanup parent" ]) (Seq.last log, List.sort (List.ofSeq log))
    equal true (owners |> Seq.forall _.IsDisposed)
    equal true graphs[0].Root.IsDisposed

let private setupFailureDisposes () =
    let log = ResizeArray<string>()
    let owners = ResizeArray<Owner>()

    let cleaned =
        Component.define
            (fun setup () ->
                owners.Add (Api.getOwner ())
                setup.OnCleanup (fun () -> log.Add "first"))
            (fun t -> t.span [||] [||])

    let throwing =
        Component.define (fun _ () -> failwith "second failed") (fun t -> t.span [||] [||])

    let parent =
        Component.define (fun setup () -> setup.OnCleanup (fun () -> log.Add "parent")) (fun t ->
            t.div [||] [| t.render cleaned ignore; t.render throwing ignore |])

    let error =
        failure (fun () ->
            Server.render Server.Options.defaults parent ()
            |> ignore)

    equal ("second failed", [ "first"; "parent" ], true) (error.Message, List.ofSeq log, owners |> Seq.forall _.IsDisposed)

let private cleanupAfterSetupRejected () =
    let captured: Setup option ref = ref None

    let capturing =
        Component.define (fun setup () -> captured.Value <- Some setup) (fun t -> t.span [||] [||])

    Server.render Server.Options.defaults capturing ()
    |> ignore

    let error = failure (fun () -> captured.Value.Value.OnCleanup ignore)
    equal true (error :? InvalidOperationException)

let private duplicateKeys () =
    let items = [| { Id = 1; Text = "a" }; { Id = 1; Text = "b" } |]

    let expected =
        failure (fun () -> Projection.ensureUniqueKeys (fun (item: obj) -> box (unbox<Listed> item).Id) (Array.map box items))

    let actual =
        failure (fun () ->
            Server.render
                { Server.Options.defaults with
                    Policy = Omit
                }
                listed
                items
            |> ignore)

    equal (true, expected.Message) (actual :? InvalidOperationException, actual.Message)

let private renderToSegments () =
    let written = ResizeArray<string>()
    Server.renderTo Server.Options.defaults named greetingProps written.Add
    equal (Server.render Server.Options.defaults named greetingProps) (String.concat "" written)
    let failed = ResizeArray<string>()

    let throwing =
        Component.define (fun _ () -> failwith "setup") (fun t -> t.span [||] [||])

    failure (fun () -> Server.renderTo Server.Options.defaults throwing () failed.Add)
    |> ignore

    equal 0 failed.Count

let private keptSetupInSiblingRejected () =
    let captured: Setup option ref = ref None
    let raised = ResizeArray<string>()

    let keeper =
        Component.define (fun setup () -> captured.Value <- Some setup) (fun t -> t.span [||] [||])

    let user =
        Component.define
            (fun _ () ->
                try
                    captured.Value.Value.OnCleanup ignore
                    raised.Add "accepted"
                with error ->
                    raised.Add error.Message)
            (fun t -> t.i [||] [||])

    let page =
        Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.render keeper ignore; t.render user ignore |])

    equal "<!--[r--><div><span></span><i></i></div><!--]-->" (Server.render Server.Options.defaults page ())
    equal [ "Server component setup is completed" ] (List.ofSeq raised)

let private orderLog = ResizeArray<string>()

let private failingLeaf =
    Component.define
        (fun setup (name: string) ->
            setup.OnCleanup (fun () ->
                orderLog.Add name
                failwith (name + " failed"))

            name)
        (fun t -> t.b [||] [| t.text id |])

let private resolvedContent =
    Component.define
        (fun setup (_: BoundaryStatus) ->
            setup.OnCleanup (fun () ->
                orderLog.Add "content"
                failwith "content failed"))
        (fun t -> t.span [||] [| t.render failingLeaf (fun _ -> "inner") |])

let private besideBoundary =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [||]
            [|
                t.render failingLeaf (fun _ -> "before")
                t.boundary boundaryPending boundaryFailed resolvedContent
                t.render failingLeaf (fun _ -> "after")
            |])

let private resolvedBoundaryCleanupOrder () =
    orderLog.Clear ()

    let error =
        failure (fun () ->
            Server.render Server.Options.defaults besideBoundary ()
            |> ignore)

    equal [ "after"; "inner"; "content"; "before" ] (List.ofSeq orderLog)

    equal
        true
        (error.Message.StartsWith (
            "Server render cleanup failed: after failed; inner failed; content failed; before failed",
            StringComparison.Ordinal
        ))

let private aggregateFailures () =
    let cleanupFails =
        Component.define
            (fun setup (fails: bool) ->
                setup.OnCleanup (fun () -> failwith "cleanup failed")

                if fails then
                    failwith "setup failed")
            (fun t -> t.span [||] [||])

    let shape (error: exn) (inner: string list) =
#if FABLE_COMPILER
        // Fable's exception stores the inner array as its inner exception.
        let count = (unbox<exn array>(box error.InnerException)).Length
#else
        let count = (error :?> AggregateException).InnerExceptions.Count
#endif
        count,
        inner
        |> List.forall (fun (part: string) -> error.Message.Contains part)

    let setupAndCleanup =
        failure (fun () ->
            Server.render Server.Options.defaults cleanupFails true
            |> ignore)

    let cleanupOnly =
        failure (fun () ->
            Server.render Server.Options.defaults cleanupFails false
            |> ignore)

    equal
        ((2, true), (1, true))
        (shape setupAndCleanup [ "Server render failed: "; "setup failed"; "cleanup failed" ],
         shape cleanupOnly [ "Server render cleanup failed: "; "cleanup failed" ])

let private omitOptions =
    { Server.Options.defaults with
        Policy = Omit
    }

type private PageProps = { Title: string; Description: string }

let private description (t: TemplateBuilder<PageProps>) =
    t.Document.meta
        [|
            t.Attrs.attribute ("name", "description")
            t.Attrs.attribute ("content", fun (page: PageProps) -> Some page.Description)
        |]

let private headChild =
    Component.define (fun _ (page: PageProps) -> page) (fun t ->
        t.fragment [| t.Document.title (fun (page: PageProps) -> page.Title); description t |])

let private preload (t: TemplateBuilder<'inst>) (href: string) =
    t.Document.link [| t.Attrs.attribute ("rel", "preload"); t.Attrs.attribute ("href", href) |]

let private headParent =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [||]
            [|
                t.Document.title "Parent"
                preload t "/a.css"
                t.Document.meta
                    [|
                        t.Attrs.attribute ("name", "description")
                        t.Attrs.attribute ("content", "parent")
                    |]
                t.render headChild (fun _ ->
                    {
                        Title = "Child"
                        Description = "child"
                    })
                preload t "/b.css"
                t.span [||] [| t.text "body" |]
            |])

let private headOrder () =
    equal
        {
            Server.Page.Body = "<!--[r--><div><span>body</span></div><!--]-->"
            Server.Page.Head =
                """<title data-ranvier-head="title">Child</title>"""
                + """<link data-ranvier-head="" rel="preload" href="/a.css">"""
                + """<meta data-ranvier-head="meta:name=description" name="description" content="child">"""
                + """<link data-ranvier-head="" rel="preload" href="/b.css">"""
        }
        (Server.renderPage Server.Options.defaults headParent ())

let private earlyChild =
    Component.define (fun _ () -> ()) (fun t ->
        t.fragment
            [|
                t.Document.title "Child"
                t.Document.meta
                    [|
                        t.Attrs.attribute ("name", "description")
                        t.Attrs.attribute ("content", "child")
                    |]
                preload t "/c.css"
            |])

let private lateParent =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [||]
            [|
                t.render earlyChild ignore
                t.Document.title "Parent"
                t.Document.meta
                    [|
                        t.Attrs.attribute ("name", "description")
                        t.Attrs.attribute ("content", "parent")
                    |]
                preload t "/p.css"
            |])

let private titledRow =
    Component.define (fun _ (item: unit -> int) -> item) (fun t -> t.Document.title (fun (item: unit -> int) -> "Row " + string (item ())))

let private rowsThenChild =
    Component.define (fun _ (rows: int array) -> rows) (fun t -> t.div [||] [| t.ul [||] [| t.each id id titledRow |]; t.render earlyChild ignore |])

let private activationOrder () =
    equal
        ("""<title data-ranvier-head="title">Child</title>"""
         + """<meta data-ranvier-head="meta:name=description" name="description" content="child">"""
         + """<link data-ranvier-head="" rel="preload" href="/p.css">"""
         + """<link data-ranvier-head="" rel="preload" href="/c.css">""")
        (Server.renderPage Server.Options.defaults lateParent ()).Head

    equal
        ("""<title data-ranvier-head="title">Row 2</title>"""
         + """<meta data-ranvier-head="meta:name=description" name="description" content="child">"""
         + """<link data-ranvier-head="" rel="preload" href="/c.css">""")
        (Server.renderPage Server.Options.defaults rowsThenChild [| 1; 2 |]).Head

let private innerRow =
    Component.define (fun _ (item: unit -> int) -> item) (fun t -> t.fragment [| t.Document.title "Inner"; preload t "/inner.css" |])

let private outerRow =
    Component.define (fun _ (item: unit -> int) -> item ()) (fun t ->
        t.fragment
            [|
                t.Document.title (fun (index: int) -> "Row " + string index)
                t.Document.link
                    [|
                        t.Attrs.attribute ("rel", "preload")
                        t.Attrs.attribute ("href", fun (index: int) -> Some ("/row-" + string index + ".css"))
                    |]
                t.ul
                    [||]
                    [|
                        t.each id (fun (index: int) -> if index = 1 then [| 10 |] else [||]) innerRow
                    |]
            |])

let private nestedLists =
    Component.define (fun _ () -> [| 1; 2 |]) (fun t -> t.div [||] [| t.each id id outerRow |])

let private nestedListsHead =
    """<title data-ranvier-head="title">Inner</title>"""
    + """<link data-ranvier-head="" rel="preload" href="/row-1.css">"""
    + """<link data-ranvier-head="" rel="preload" href="/row-2.css">"""
    + """<link data-ranvier-head="" rel="preload" href="/inner.css">"""

let private containerOrder () =
    let expandedHole =
        Server.renderPage
            Server.Options.defaults
            holed
            {
                Caption = "C"
                Body = Component.renderWith Program.ops rowsThenChild [| 1; 2 |]
            }

    equal
        (nestedListsHead,
         nestedListsHead,
         """<title data-ranvier-head="title">Row 2</title>"""
         + """<meta data-ranvier-head="meta:name=description" name="description" content="child">"""
         + """<link data-ranvier-head="" rel="preload" href="/c.css">""")
        ((Server.renderPage Server.Options.defaults nestedLists ()).Head,
         (Server.renderPage
             Server.Options.defaults
             holed
             {
                 Caption = "C"
                 Body = Component.renderWith Program.ops nestedLists ()
             })
             .Head,
         expandedHole.Head)

let private escapedPage =
    Component.define (fun _ (page: PageProps) -> page) (fun t ->
        t.fragment
            [|
                t.Document.title (fun (page: PageProps) -> page.Title)
                description t
                t.Document.meta [| t.Attrs.attribute ("charset", "utf-8") |]
                t.Document.link (
                    "canonical",
                    [|
                        t.Attrs.attribute ("rel", "canonical")
                        t.Attrs.attribute ("href", fun _ -> Some "/p?a=1&b=2")
                    |]
                )
                t.Document.link (
                    "icon",
                    [|
                        t.Attrs.attribute ("rel", "icon")
                        t.Attrs.attribute ("href", "/i.png")
                        t.Attrs.attribute ("type", fun _ -> None)
                    |]
                )
                preload t "/s.css"
                t.h1 [||] [| t.text (fun (page: PageProps) -> page.Title) |]
            |])

let private escapedProps =
    {
        Title = "<a & \"b\">"
        Description = "say \"hi\" & <go>"
    }

let private escapedHead =
    """<title data-ranvier-head="title">&lt;a &amp; "b"&gt;</title>"""
    + """<meta data-ranvier-head="meta:name=description" name="description" content="say &quot;hi&quot; &amp; &lt;go&gt;">"""
    + """<meta data-ranvier-head="meta:charset" charset="utf-8">"""
    + """<link data-ranvier-head="link:canonical" rel="canonical" href="/p?a=1&amp;b=2">"""
    + """<link data-ranvier-head="link:key:icon" rel="icon" href="/i.png">"""
    + """<link data-ranvier-head="" rel="preload" href="/s.css">"""

let private pageEscapes () =
    let page = Server.renderPage omitOptions escapedPage escapedProps
    equal escapedHead page.Head
    equal (Server.render omitOptions escapedPage escapedProps) page.Body
    equal """<!--[r--><h1>&lt;a &amp; "b"&gt;</h1><!--]-->""" page.Body

let private headPolicy () =
    let error =
        failure (fun () ->
            Server.render Server.Options.defaults escapedPage escapedProps
            |> ignore)

    equal (true, "Document metadata requires Server.renderPage") (error :? InvalidOperationException, error.Message)
    equal """<!--[r--><h1>&lt;a &amp; "b"&gt;</h1><!--]-->""" (Server.render omitOptions escapedPage escapedProps)
    equal escapedHead (Server.renderPage Server.Options.defaults escapedPage escapedProps).Head

let private holedHeads () =
    let child = { Title = "Held"; Description = "held" }

    let expected =
        """<title data-ranvier-head="title">Held</title>"""
        + """<meta data-ranvier-head="meta:name=description" name="description" content="held">"""

    let headOf (body: Program.View) =
        (Server.renderPage Server.Options.defaults holed { Caption = "C"; Body = body }).Head

    equal
        [ expected; expected ]
        [
            headOf (Component.render headChild child)
            headOf (Component.renderWith Program.ops headChild child)
        ]

/// <summary>A page writing a title before <c>pause</c> and a meta after it, both taken from the props.</summary>
let private namedPage (pause: Component<unit>) =
    Component.define (fun _ (name: string) -> name) (fun t ->
        t.fragment
            [|
                t.Document.title (fun name -> "title " + name)
                t.render pause ignore
                t.Document.meta
                    [|
                        t.Attrs.attribute ("name", "description")
                        t.Attrs.attribute ("content", fun name -> Some ("meta " + name))
                    |]
            |])

let private namedHead (name: string) =
    "<title data-ranvier-head=\"title\">title "
    + name
    + "</title><meta data-ranvier-head=\"meta:name=description\" name=\"description\" content=\"meta "
    + name
    + "\">"

#if FABLE_COMPILER
let private sequentialHeads () =
    let page = namedPage (Component.define (fun _ () -> ()) (fun t -> t.span [||] [||]))

    let left = Server.renderPage Server.Options.defaults page "left"
    let right = Server.renderPage Server.Options.defaults page "right"
    equal (namedHead "left", namedHead "right") (left.Head, right.Head)
#endif

let private serverIdentity () =
    identityLog.Clear ()

    Server.render
        { Server.Options.defaults with
            IdPrefix = "a"
        }
        identified
        [| 7; 8 |]
    |> ignore

    equal (identityIds "a") (List.sort (List.ofSeq identityLog))

let private createIdAfterSetup () =
    let captured: Setup option ref = ref None

    let capturing =
        Component.define (fun setup () -> captured.Value <- Some setup) (fun t -> t.span [||] [||])

    Server.render Server.Options.defaults capturing ()
    |> ignore

    equal true (failure (fun () -> captured.Value.Value.CreateId () |> ignore) :? InvalidOperationException)

let private authoredIds () =
    let ids = ResizeArray<string>()

    let authored =
        Program.createComponent (fun setup ->
            ids.Add (setup.CreateId ())
            Program.text "x")

    Html.render Reject (Program.run (Program.fragment [| authored; authored |]))
    |> ignore

    identityLog.Clear ()

    Html.render Reject (Program.run (Component.render idLeaf ()))
    |> ignore

    equal ([ "r-0-a0"; "r-0-a1" ], [ "r-0-0" ]) (List.ofSeq ids, List.ofSeq identityLog)

let private holeIdentity () =
    identityLog.Clear ()
    let html = Server.render Server.Options.defaults idHole ()

    equal
        ("""<!--[r--><div id="r-0-0"><!--[--><i id="r-0.r0.0-0"></i><i id="r-0.r0.0.a0-0"></i><!--]--></div><!--]-->""", holeIds)
        (html, List.ofSeq identityLog)

/// <summary>The <c>id</c> attribute values of <c>html</c>, in document order.</summary>
let private htmlIds (html: string) =
    [
        for found in Text.RegularExpressions.Regex.Matches (html, " id=\"([^\"]*)\"") do
            found.Groups[1].Value
    ]

let private nestedIdentity () =
    identityLog.Clear ()
    let html = Server.render Server.Options.defaults idNested [| 1; 2 |]
    equal (nestedIds, nestedIds) (List.ofSeq identityLog, htmlIds html)

let private holesIdentity () =
    identityLog.Clear ()
    let html = Server.render Server.Options.defaults idHoles ()
    equal (holesIds, holesIds) (List.ofSeq identityLog, htmlIds html)

let private createIdInLaterSetup () =
    let error =
        failure (fun () ->
            Server.render Server.Options.defaults (capturedCreateId ()) ()
            |> ignore)

    equal (true, "CreateId is valid only while a component's setup runs") (error :? InvalidOperationException, error.Message)

let private serialised =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [|
                t.Props.className (fun _ -> "a<b")
                t.Props.tabIndex (fun _ -> 2.0)
                t.Props.ariaBusy (fun _ -> Some "true")
            |]
            [|
                t.input
                    [|
                        t.Props.Input.value (fun _ -> "v\"")
                        t.Props.Input.``checked`` (fun _ -> true)
                        t.Props.Input.disabled (fun _ -> false)
                    |]
                t.button [| t.Props.Button.disabled true |] [||]
            |])

let private browserOnlyProperty =
    Component.define (fun _ () -> ()) (fun t -> t.div [| t.Props.accessKey (fun _ -> "k") |] [| t.text "x" |])

let private serialisedProperties () =
    equal
        """<!--[r--><div class="a&lt;b" tabindex="2" aria-busy="true"><input value="v&quot;" checked=""><button disabled=""></button></div><!--]-->"""
        (Server.render Server.Options.defaults serialised ())

    equal
        "<!--[r--><div>x</div><!--]-->"
        (Server.render
            { Server.Options.defaults with
                Policy = Omit
            }
            browserOnlyProperty
            ())

    let error =
        failure (fun () ->
            Server.render Server.Options.defaults browserOnlyProperty ()
            |> ignore)

    equal (true, true) (error :? InvalidOperationException, error.Message.Contains "<div> property")

let private browserPartsUninvoked () =
    let mutable invoked = 0

    let hook =
        Program.element
            Div
            [|
                browserModifier (fun _ ->
                    invoked <- invoked + 1
                    ignore)
            |]
            [||]

    let parts =
        Component.define (fun _ () -> ()) (fun t ->
            t.div
                [||]
                [|
                    t.button [| t.Events.onClick (fun _ _ -> invoked <- invoked + 1) |] [||]
                    t.view (fun _ -> hook)
                |])

    equal
        "<!--[r--><div><button></button><!--[--><div></div><!--]--></div><!--]-->"
        (Server.render
            { Server.Options.defaults with
                Policy = Omit
            }
            parts
            ())

    failure (fun () ->
        Server.render Server.Options.defaults parts ()
        |> ignore)
    |> ignore

    equal 0 invoked

let private duplicateAttributeSource () =
    let both =
        Component.define (fun _ () -> ()) (fun t -> t.div [| t.Attrs.className "x"; t.Props.className (fun _ -> "y") |] [||])

    let error =
        failure (fun () ->
            Server.render Server.Options.defaults both ()
            |> ignore)

    equal (true, true) (error :? InvalidOperationException, error.Message.Contains "class")

let private childText =
    Component.define (fun _ (value: string) -> value) (fun t ->
        t.fragment
            [|
                t.textarea [| t.Props.TextArea.value (fun (value: string) -> value) |] [||]
                t.textarea [| t.Props.TextArea.value "<static>" |] [||]
            |])

let private textareaChildText () =
    equal
        "<!--[r--><textarea>a&lt;b&amp;</textarea><textarea>&lt;static&gt;</textarea><!--]-->"
        (Server.render Server.Options.defaults childText "a<b&")

    let withChildren =
        Component.define (fun _ () -> ()) (fun t -> t.textarea [| t.Props.TextArea.value (fun _ -> "v") |] [| t.text "child" |])

    let error =
        failure (fun () ->
            Server.render Server.Options.defaults withChildren ()
            |> ignore)

    equal (true, true) (error :? InvalidOperationException, error.Message.Contains "<textarea>")

let private placedRejection () =
    let placed =
        Component.define (fun _ () -> ()) (fun t -> t.div [| t.Hooks.mounted (fun _ _ -> ignore) |] [||])

    let authored =
        Program.element (HtmlTag "span") [| browserModifier (fun _ -> ignore) |] [||]

    let messages =
        [
            (failure (fun () ->
                Server.render Server.Options.defaults placed ()
                |> ignore))
                .Message
            (failure (fun () ->
                Html.render Reject (Program.run authored)
                |> ignore))
                .Message
        ]

    equal
        [
            "Browser capability requires explicit Omit policy: <div> mounted hook"
            "Browser capability requires explicit Omit policy: <span> browser hook"
        ]
        messages

let private meter =
    Component.define (fun _ (value: float) -> value) (fun t -> t.meter [| t.Props.Meter.value (fun (value: float) -> value) |] [||])

let private numberForm () =
    let values =
        [
            2.0
            0.5
            2.5e-5
            1e-7
            1e21
            1.2345678901234568e20
            -0.0
            nan
            infinity
            -infinity
            123.456
            1e-6
        ]

    let expected =
        [
            "2"
            "0.5"
            "0.000025"
            "1e-7"
            "1e+21"
            "123456789012345680000"
            "0"
            "NaN"
            "Infinity"
            "-Infinity"
            "123.456"
            "0.000001"
        ]

    equal
        [
            for text in expected ->
                "<!--[r--><meter value=\""
                + text
                + "\"></meter><!--]-->"
        ]
        [ for value in values -> Server.render Server.Options.defaults meter value ]

let private holeTitle =
    Component.define (fun _ () -> ()) (fun t -> t.Document.title "Hole")

let private childTitle =
    Component.define (fun _ () -> ()) (fun t -> t.Document.title "Child")

let private holeThenChild =
    Component.define (fun _ () -> Component.render holeTitle ()) (fun t -> t.div [||] [| t.view id; t.render childTitle ignore |])

let private holeAfterChildren () =
    let hole = """<title data-ranvier-head="title">Hole</title>"""

    let expanded =
        Server.renderPage
            Server.Options.defaults
            holed
            {
                Caption = "C"
                Body = Component.renderWith Program.ops holeThenChild ()
            }

    equal (hole, hole) ((Server.renderPage Server.Options.defaults holeThenChild ()).Head, expanded.Head)

type FormState =
    {
        Text: string
        On: bool
        Choice: string
    }

let formFixture =
    Component.define (fun _ (state: FormState) -> state) (fun t ->
        t.form
            [||]
            [|
                t.textarea [| t.Props.TextArea.value (fun r -> r.Text) |] [||]
                t.input
                    [|
                        t.Attrs.Input.``type`` "checkbox"
                        t.Props.Input.``checked`` (fun r -> r.On)
                    |]
                t.select
                    [| t.Props.Select.value (fun r -> r.Choice) |]
                    [|
                        t.option
                            [|
                                t.Attrs.Option.value "a"
                                t.Props.Option.selected (fun r -> r.Choice = "a")
                            |]
                            [| t.text "A" |]
                        t.option
                            [|
                                t.Attrs.Option.value "b"
                                t.Props.Option.selected (fun r -> r.Choice = "b")
                            |]
                            [| t.text "B" |]
                    |]
                t.pre [||] [| t.text (fun r -> r.Text) |]
                t.pre [||] [| t.text "\nstatic" |]
            |])

let private forms () =
    equal
        "<!--[r--><form><textarea>\n\nline &lt;1&gt;</textarea><input type=\"checkbox\" checked=\"\"><select><option value=\"a\">A</option><option value=\"b\" selected=\"\">B</option></select><pre>\n\nline &lt;1&gt;</pre><pre>\n\nstatic</pre></form><!--]-->"
        (Server.render
            { Server.Options.defaults with
                Policy = Omit
            }
            formFixture
            {
                Text = "\nline <1>"
                On = true
                Choice = "b"
            })

let private staticTextarea =
    Component.define (fun _ () -> ()) (fun t -> t.textarea [| t.Props.TextArea.value "\nstatic" |] [||])

let private staticTextareaLineFeed () =
    equal "<!--[r--><textarea>\n\nstatic</textarea><!--]-->" (Server.render Server.Options.defaults staticTextarea ())

let private formsUnset () =
    equal
        "<!--[r--><form><textarea>text</textarea><input type=\"checkbox\"><select><option value=\"a\">A</option><option value=\"b\">B</option></select><pre>text</pre><pre>\n\nstatic</pre></form><!--]-->"
        (Server.render
            { Server.Options.defaults with
                Policy = Omit
            }
            formFixture
            {
                Text = "text"
                On = false
                Choice = "c"
            })

let private textareaValues () =
    let render (value: string) =
        Server.render Server.Options.defaults childText value

    equal
        [
            "<!--[r--><textarea>&lt;/textarea&gt;&amp;</textarea><textarea>&lt;static&gt;</textarea><!--]-->"
            "<!--[r--><textarea></textarea><textarea>&lt;static&gt;</textarea><!--]-->"
            "<!--[r--><textarea></textarea><textarea>&lt;static&gt;</textarea><!--]-->"
        ]
        [ render "</textarea>&"; render ""; render null ]

/// <summary>The server HTML of <c>holed</c> with an empty caption and <c>body</c> in its view hole.</summary>
let private inHole (options: Server.Options) (body: Program.View) =
    Server.render options holed { Caption = ""; Body = body }

let private expandedProperties () =
    let omit =
        { Server.Options.defaults with
            Policy = Omit
        }

    let hole (inner: string) =
        "<!--[r--><div class=\"card\"><!--_--><!--[-->"
        + inner
        + "<!--]--></div><!--]-->"

    let state =
        {
            Text = "\nline <1>"
            On = true
            Choice = "b"
        }

    equal
        [
            hole """<div class="a&lt;b" tabindex="2" aria-busy="true"><input value="v&quot;" checked=""><button disabled=""></button></div>"""
            hole
                "<form><textarea>\n\nline &lt;1&gt;</textarea><input type=\"checkbox\" checked=\"\"><select><option value=\"a\">A</option><option value=\"b\" selected=\"\">B</option></select><pre>\n\nline &lt;1&gt;</pre><pre>\n\nstatic</pre></form>"
            hole "<textarea>a&lt;b&amp;</textarea><textarea>&lt;static&gt;</textarea>"
            hole "<textarea></textarea><textarea>&lt;static&gt;</textarea>"
            hole "<textarea>\n\nstatic</textarea>"
        ]
        [
            inHole Server.Options.defaults (Component.renderWith Program.ops serialised ())
            inHole omit (Component.renderWith Program.ops formFixture state)
            inHole Server.Options.defaults (Component.renderWith Program.ops childText "a<b&")
            inHole Server.Options.defaults (Component.renderWith Program.ops childText null)
            inHole Server.Options.defaults (Component.renderWith Program.ops staticTextarea ())
        ]

    let rejected =
        failure (fun () ->
            inHole Server.Options.defaults (Component.renderWith Program.ops formFixture state)
            |> ignore)

    equal true (rejected.Message.Contains "<select> property")

type private ContentOutcome =
    | ContentPends
    | ContentFails
    | ContentReads

let private attemptLog = ResizeArray<string>()
let mutable private contentOutcome = ContentPends

let private attemptPending =
    Component.define (fun _ () -> attemptLog.Add "pending setup") (fun t -> t.span [||] [| t.text "loading" |])

let private attemptFailed =
    Component.define
        (fun _ (failure: BoundaryFailure) ->
            attemptLog.Add "error setup"
            failure.Retry ()
            failure)
        (fun t -> t.span [||] [| t.text (fun failure -> failure.Error.Message) |])

let private attemptContent =
    Component.define
        (fun setup (_: BoundaryStatus) ->
            attemptLog.Add "content setup"
            setup.OnCleanup (fun () -> attemptLog.Add "content cleanup")
            let source = Api.createAsyncSource<string>()

            match contentOutcome with
            | ContentReads -> source.Settle "ready"
            | ContentFails -> source.Fail (Exception "<failed>")
            | ContentPends -> ()

            source)
        (fun t -> t.span [||] [| t.text (fun (source: AsyncSource<string>) -> source.Value) |])

let private attempted =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary attemptPending attemptFailed attemptContent |])

let private boundaryAttempts () =
    let render outcome =
        contentOutcome <- outcome
        attemptLog.Clear ()
        let html = Server.render Server.Options.defaults attempted ()
        html, List.ofSeq attemptLog

    equal
        ("<!--[r--><div><!--[p--><span>loading</span><!--]--></div><!--]-->", [ "content setup"; "content cleanup"; "pending setup" ])
        (render ContentPends)

    equal
        ("<!--[r--><div><!--[e--><span>&lt;failed&gt;</span><!--]--></div><!--]-->", [ "content setup"; "content cleanup"; "error setup" ])
        (render ContentFails)

    equal ("<!--[r--><div><!--[c--><span>ready</span><!--]--></div><!--]-->", [ "content setup"; "content cleanup" ]) (render ContentReads)

let private expandedBoundaryAttempts () =
    let render outcome (body: Program.View) =
        contentOutcome <- outcome
        attemptLog.Clear ()
        let html = inHole Server.Options.defaults body
        html, List.ofSeq attemptLog

    let authored =
        Program.ops.Boundary (
            Component.render attemptPending (),
            (fun failure -> Component.render attemptFailed failure),
            fun status -> Component.render attemptContent status
        )

    let expanded = Component.renderWith Program.ops attempted ()

    let hole (inner: string) =
        "<!--[r--><div class=\"card\"><!--_--><!--[-->"
        + inner
        + "<!--]--></div><!--]-->"

    equal
        [
            hole "<!--[--><span>loading</span><!--]-->", [ "content setup"; "content cleanup"; "pending setup" ]
            hole "<!--[--><span>&lt;failed&gt;</span><!--]-->", [ "content setup"; "content cleanup"; "error setup" ]
            hole "<!--[--><span>ready</span><!--]-->", [ "content setup"; "content cleanup" ]
            hole "<div><!--[--><span>loading</span><!--]--></div>", [ "content setup"; "content cleanup"; "pending setup" ]
            hole "<div><!--[--><span>&lt;failed&gt;</span><!--]--></div>", [ "content setup"; "content cleanup"; "error setup" ]
            hole "<div><!--[--><span>ready</span><!--]--></div>", [ "content setup"; "content cleanup" ]
        ]
        [
            for body in [ authored; expanded ] do
                for outcome in [ ContentPends; ContentFails; ContentReads ] -> render outcome body
        ]

/// <summary>An authored component whose setup reads a pending source after registering a failing cleanup.</summary>
let private authoredPendingCleanup =
    Program.ops.Component (fun setup ->
        setup.OnCleanup (fun () -> failwith "authored cleanup failed")
        Program.text (Api.createAsyncSource<string>().Value))

let private authoredPendingContent =
    Component.define (fun _ (_: BoundaryStatus) -> ()) (fun t -> t.span [||] [| t.view (fun _ -> authoredPendingCleanup) |])

let private authoredPendingGuarded =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary attemptPending attemptFailed authoredPendingContent |])

let private authoredPendingWithCleanupFailure () =
    let outside =
        failure (fun () ->
            inHole Server.Options.defaults authoredPendingCleanup
            |> ignore)

    attemptLog.Clear ()

    let inside =
        failure (fun () ->
            Server.render Server.Options.defaults authoredPendingGuarded ()
            |> ignore)

#if FABLE_COMPILER
    // Fable's exception stores the inner array as its inner exception.
    let firstInner = (unbox<exn array>(box outside.InnerException))[0]
#else
    let firstInner = (outside :?> AggregateException).InnerExceptions[0]
#endif

    let pendingFirst =
        match firstInner with
        | Server.ServerPendingException (NotReadyException _) -> true
        | _ -> false

    equal
        (true, true, true, [ "pending setup" ])
        (outside.Message.StartsWith (
            "Server render failed: A read outside every boundary is pending; authored cleanup failed",
            StringComparison.Ordinal
         ),
         pendingFirst,
         inside.Message.StartsWith ("Server render cleanup failed: authored cleanup failed", StringComparison.Ordinal),
         List.ofSeq attemptLog)

let private failingPending =
    Component.define (fun _ () -> failwith "pending failed") (fun t -> t.span [||] [||])

let private innerContent =
    Component.define (fun _ (_: BoundaryStatus) -> Api.createAsyncSource<string>()) (fun t ->
        t.span [||] [| t.text (fun (source: AsyncSource<string>) -> source.Value) |])

let private outerContent =
    Component.define (fun _ (_: BoundaryStatus) -> ()) (fun t -> t.boundary failingPending attemptFailed innerContent)

let private nestedBoundaries =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary attemptPending attemptFailed outerContent |])

let private pendingViewFailure () =
    equal "<!--[r--><div><!--[e--><span>pending failed</span><!--]--></div><!--]-->" (Server.render Server.Options.defaults nestedBoundaries ())

let private unguarded =
    Component.define
        (fun setup () ->
            setup.OnCleanup (fun () -> attemptLog.Add "unguarded cleanup")
            Api.createAsyncSource<string>())
        (fun t -> t.span [||] [| t.text (fun (source: AsyncSource<string>) -> source.Value) |])

let private pendingOutsideBoundaries () =
    attemptLog.Clear ()

    match
        failure (fun () ->
            Server.render Server.Options.defaults unguarded ()
            |> ignore)
    with
    | Server.ServerPendingException pending ->
        let isNotReady =
            match pending with
            | NotReadyException _ -> true
            | _ -> false

        equal (true, [ "unguarded cleanup" ]) (isNotReady, List.ofSeq attemptLog)
    | other -> failwithf "Expected ServerPendingException; got %O" other

let private pendingMessage () =
    equal
        "A read outside every boundary is pending"
        (failure (fun () ->
            Server.render Server.Options.defaults unguarded ()
            |> ignore))
            .Message

let private boundaryIdentity () =
    let render ready fails =
        idContentReady <- ready
        idContentFails <- fails
        identityLog.Clear ()
        let html = Server.render Server.Options.defaults idBoundary ()
        html, List.ofSeq identityLog

    let ready = render true false
    let pending = render false false
    let failed = render true true
    idContentReady <- true
    idContentFails <- false

    equal ("""<!--[r--><div id="r-0-0"><!--[c--><b id="r-0.r0.0-0">ready</b><!--]--></div><!--]-->""", boundaryContentIds) ready

    equal ("""<!--[r--><div id="r-0-0"><!--[p--><i id="r-0.r0.1-0"></i><!--]--></div><!--]-->""", boundaryPendingIds) pending

    equal ("""<!--[r--><div id="r-0-0"><!--[e--><span id="r-0.r0.1-0"></span><!--]--></div><!--]-->""", boundaryErrorIds) failed

let private failingCleanupContent =
    Component.define
        (fun setup (_: BoundaryStatus) ->
            setup.OnCleanup (fun () -> failwith "content cleanup failed")
            Api.createAsyncSource<string>())
        (fun t -> t.span [||] [| t.text (fun (source: AsyncSource<string>) -> source.Value) |])

let private cleanupGuarded =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary attemptPending attemptFailed failingCleanupContent |])

let private abandonedCleanupFailure () =
    let error =
        failure (fun () ->
            Server.render Server.Options.defaults cleanupGuarded ()
            |> ignore)

    // .NET appends the inner messages to an aggregate's message.
    equal true (error.Message.StartsWith "Server render cleanup failed: content cleanup failed")

let private headPending =
    Component.define (fun _ () -> ()) (fun t -> t.fragment [| preload t "/pending.css"; t.span [||] [| t.text "loading" |] |])

let private headContent =
    Component.define (fun _ (_: BoundaryStatus) -> Api.createAsyncSource<string>()) (fun t ->
        t.fragment
            [|
                t.Document.title "Content"
                preload t "/content.css"
                t.span [||] [| t.text (fun (source: AsyncSource<string>) -> source.Value) |]
            |])

let private headGuarded =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [||]
            [|
                t.Document.title "Page"
                t.boundary headPending attemptFailed headContent
                preload t "/after.css"
            |])

let private abandonedHeads () =
    let pageHead =
        """<title data-ranvier-head="title">Page</title>"""
        + """<link data-ranvier-head="" rel="preload" href="/after.css">"""
        + """<link data-ranvier-head="" rel="preload" href="/pending.css">"""

    let holeHead (body: Program.View) =
        (Server.renderPage Server.Options.defaults holed { Caption = "C"; Body = body }).Head

    let authored =
        Program.ops.Boundary (Program.text "wait", (fun _ -> Program.text "failed"), fun status -> Component.render headContent status)

    equal
        ({
            Server.Page.Body = "<!--[r--><div><!--[p--><span>loading</span><!--]--></div><!--]-->"
            Server.Page.Head = pageHead
         },
         pageHead,
         "")
        (Server.renderPage Server.Options.defaults headGuarded (), holeHead (Component.renderWith Program.ops headGuarded ()), holeHead authored)

let private hashVectors () =
    equal ("811c9dc5", "e40c292c", "25dbbe5a") (fnv1a "", fnv1a "a", fnv1a "<span>x</span>|")
    let x = Component.define (fun _ () -> ()) (fun t -> t.span [||] [| t.text "x" |])
    let y = Component.define (fun _ () -> ()) (fun t -> t.span [||] [| t.text "y" |])
    equal ("25dbbe5a", "6a39ec02", false) (structuralHash x, structuralHash workingArm, structuralHash x = structuralHash y)
    equal "ac3cc9fa" (fnv1a "é\U0001F600￿")

let private unarmed =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [||]
            [|
                TemplateView (
                    RegionNode
                        {
                            Items = Func<obj, obj array>(fun _ -> [||])
                            Key = KeyedItems.pairKey
                            Row = ArmRow
                        }
                )
            |])

let private armRegionArity () =
    let error =
        failure (fun () ->
            Server.render Server.Options.defaults unarmed ()
            |> ignore)

    equal (true, "An arm region writes exactly one arm; it read 0") (error :? InvalidOperationException, error.Message)

let private armedList =
    Component.define (fun _ (items: Listed array) -> items) (fun t -> t.ul [||] [| t.matchWith id (fun items -> t.arm listedRow items[0]) |])

let private regionHashes () =
    equal
        ("789182d0", "85919747", "f8780f23", "cfae8b1a", "d263bb31")
        (structuralHash listed, structuralHash armedList, structuralHash holed, structuralHash guarded, structuralHash greeting)

    equal (fnv1a "<ul><!--[--><!--]--></ul>|l", fnv1a "<ul><!--[--><!--]--></ul>|a") (structuralHash listed, structuralHash armedList)

let private texts =
    Component.define (fun _ (items: string array) -> items) (fun t -> t.div [||] [| t.each id id textRow |])

let private empties =
    Component.define (fun _ (items: string array) -> items) (fun t -> t.div [||] [| t.each id id emptyRow |])

let private markers () =
    let render definition props =
        Server.render Server.Options.defaults definition props

    equal "<!--[r--><div><!--[-->a<!--!-->b<!--!--><!--_--><!--]--></div><!--]-->" (render texts [| "a"; "b"; "" |])
    equal "<!--[r--><div><!--[--><!--ranvier:row--><!--ranvier:row--><!--]--></div><!--]-->" (render empties [| "a"; "b" |])
    equal "<!--[r--><div><!--[a:6a39ec02--><span>working 3</span><!--]--></div><!--]-->" (render staged (Working 3))
    equal "<!--[r--><span><!--_--></span><!--]-->" (render emptyText null)

let private leadingTextRow =
    Component.define (fun _ (item: unit -> string) -> item) (fun t -> t.fragment [| t.text (fun item -> item ()); t.em [||] [||] |])

let private trailingTextRow =
    Component.define (fun _ (item: unit -> string) -> item) (fun t -> t.fragment [| t.em [||] [||]; t.text (fun item -> item ()) |])

let private rowEdges =
    Component.define (fun _ (items: string array) -> items) (fun t ->
        t.fragment
            [|
                t.div [||] [| t.each id id leadingTextRow |]
                t.div [||] [| t.each id id trailingTextRow |]
                t.div
                    [||]
                    [|
                        t.matchWith (fun (items: string array) -> items[0]) (fun item -> t.arm emptyRow item)
                    |]
            |])

let private rowMarkers () =
    equal
        ("<!--[r--><div><!--[-->a<em></em>b<em></em><!--]--></div>"
         + "<div><!--[--><em></em>a<em></em>b<!--]--></div>"
         + "<div><!--[a:"
         + fnv1a "|"
         + "--><!--ranvier:row--><!--]--></div><!--]-->")
        (Server.render Server.Options.defaults rowEdges [| "a"; "b" |])

let private rcdataSlots =
    Component.define (fun _ (value: string) -> value) (fun t ->
        t.fragment
            [|
                t.textarea [||] [| t.text (fun value -> value) |]
                t.title [||] [| t.text (fun value -> value) |]
            |])

let private rcdataText () =
    let render (value: string) =
        Server.render Server.Options.defaults rcdataSlots value

    equal
        [
            "<!--[r--><textarea></textarea><title></title><!--]-->"
            "<!--[r--><textarea></textarea><title></title><!--]-->"
            "<!--[r--><textarea>\n\nx &lt;</textarea><title>\nx &lt;</title><!--]-->"
        ]
        [ render ""; render null; render "\nx <" ]

    let rejection (definition: Component<unit>) =
        let error =
            failure (fun () ->
                Server.render Server.Options.defaults definition ()
                |> ignore)

        error :? InvalidOperationException, error.Message

    equal
        [
            true, "<textarea> takes at most one text child"
            true, "<title> takes at most one text child"
        ]
        [
            rejection (Component.define (fun _ () -> ()) (fun t -> t.textarea [||] [| t.text "a"; t.text (fun _ -> "b") |]))
            rejection (Component.define (fun _ () -> ()) (fun t -> t.title [||] [| t.text (fun _ -> "a"); t.text (fun _ -> "b") |]))
        ]

let private preformattedContent =
    Component.define (fun _ (markup: string) -> markup) (fun t ->
        let trusted (markup: string) =
            UnsafeHtml.Trust markup

        t.fragment
            [|
                t.pre [| t.Unsafe.html trusted |] [||]
                t.element (HtmlTag "listing") [| t.Unsafe.html trusted |] [||]
                t.div [| t.Unsafe.html trusted |] [||]
            |])

let private contentLeadingLineFeed () =
    equal
        [
            "<pre>\n\nx</pre><listing>\n\nx</listing><div>\nx</div>"
            "<pre>\n\r\nx</pre><listing>\n\r\nx</listing><div>\r\nx</div>"
            "<pre>\n&#10;x</pre><listing>\n&#10;x</listing><div>&#10;x</div>"
            "<pre>\n<b>x</b></pre><listing>\n<b>x</b></listing><div><b>x</b></div>"
            "<pre></pre><listing></listing><div></div>"
        ]
        [
            for markup in [ "\nx"; "\r\nx"; "&#10;x"; "<b>x</b>"; "" ] -> renderOmit preformattedContent markup
        ]

let private expandedContent () =
    let hole (inner: string) =
        "<!--[r--><div class=\"card\"><!--_--><!--[-->"
        + inner
        + "<!--]--></div><!--]-->"

    let expanded definition (markup: string) =
        inHole Server.Options.defaults (Component.renderWith Program.ops definition markup)

    equal
        [
            hole "<div><b>raw</b> &amp; <!--note--></div><span>&lt;b&gt;</span>"
            hole "<pre>\n\nx</pre><listing>\n\nx</listing><div>\nx</div>"
            hole "<pre></pre><listing></listing><div></div>"
        ]
        [
            expanded rawContent "<b>raw</b> &amp; <!--note-->"
            expanded preformattedContent "\nx"
            expanded preformattedContent ""
        ]

    let forged = failure (fun () -> expanded rawContent "<!--[-->x" |> ignore)
    equal "Raw HTML cannot contain a comment that starts with [ or equals ]" forged.Message

let private restructures (rule: string) (path: string) =
    true,
    "The HTML parser restructures this markup ("
    + rule
    + ") at "
    + path

/// <summary>The type test and message of the exception raised by <c>render</c>.</summary>
let private rejection (render: unit -> unit) =
    let error = failure render
    error :? InvalidOperationException, error.Message

let private restructured (build: TemplateBuilder<unit> -> TemplateView<unit>) =
    let definition = Component.define (fun _ () -> ()) build

    rejection (fun () ->
        Server.render Server.Options.defaults definition ()
        |> ignore)

let private spanChild =
    Component.define (fun _ () -> ()) (fun t -> t.span [||] [||])

let private contentModel () =
    let cases =
        [
            // Asserts the server render's content-model refusal.
            // fsharpanalyzer: ignore-line-next RAN024
            (fun (t: TemplateBuilder<unit>) -> t.table [||] [| t.tr [||] [| t.td [||] [||] |] |]), restructures "table-row" "table/tr"
            // Asserts the server render's content-model refusal.
            // fsharpanalyzer: ignore-line-next RAN024
            (fun t -> t.div [||] [| t.tbody [||] [||] |]), restructures "table-section" "div/tbody"
            // Asserts the server render's content-model refusal.
            // fsharpanalyzer: ignore-line-next RAN024
            (fun t -> t.table [||] [| t.tbody [||] [| t.td [||] [||] |] |]), restructures "table-cell" "table/tbody/td"
            (fun t -> t.table [||] [| t.element (HtmlTag "col") [||] [||] |]), restructures "table-column" "table/col"
            (fun t -> t.table [||] [| t.text "x" |]), restructures "table-foster" "table/#text"
            // Asserts the server render's content-model refusal.
            // fsharpanalyzer: ignore-line-next RAN024
            (fun t -> t.table [||] [| t.tbody [||] [| t.tr [||] [| t.div [||] [||] |] |] |]), restructures "table-foster" "table/tbody/tr/div"
            // Asserts the server render's content-model refusal.
            // fsharpanalyzer: ignore-line-next RAN024
            (fun t -> t.p [||] [| t.span [||] [| t.div [||] [||] |] |]), restructures "paragraph-close" "p/span/div"
            // Asserts the server render's content-model refusal.
            // fsharpanalyzer: ignore-line-next RAN024
            (fun t -> t.p [||] [| t.li [||] [||] |]), restructures "paragraph-close" "p/li"
            // Asserts the server render's content-model refusal.
            // fsharpanalyzer: ignore-line-next RAN024
            (fun t -> t.a [||] [| t.span [||] [| t.a [||] [||] |] |]), restructures "nested-anchor" "a/span/a"
            // Asserts the server render's content-model refusal.
            // fsharpanalyzer: ignore-line-next RAN024
            (fun t -> t.form [||] [| t.div [||] [| t.form [||] [||] |] |]), restructures "nested-form" "form/div/form"
            // Asserts the server render's content-model refusal.
            // fsharpanalyzer: ignore-line-next RAN024
            (fun t -> t.select [||] [| t.div [||] [||] |]), restructures "select-content" "select/div"
            // Asserts the server render's content-model refusal.
            // fsharpanalyzer: ignore-line-next RAN024
            (fun t -> t.textarea [||] [| t.span [||] [||] |]), restructures "rcdata-content" "textarea/span"
            (fun t -> t.textarea [||] [| t.render spanChild (fun _ -> ()) |]), restructures "rcdata-content" "textarea/span"
            // Asserts the server render's content-model refusal.
            // fsharpanalyzer: ignore-line-next RAN024
            (fun t -> t.table [||] [| t.element (HtmlTag "colgroup") [||] [| t.div [||] [||] |] |]), restructures "table-foster" "table/colgroup/div"
            (fun t -> t.table [||] [| t.element (HtmlTag "colgroup") [||] [| t.text "x" |] |]), restructures "table-foster" "table/colgroup/#text"
            // Asserts the server render's content-model refusal.
            // fsharpanalyzer: ignore-line-next RAN024
            (fun t -> t.select [||] [| t.option [||] [| t.option [||] [||] |] |]), restructures "select-content" "select/option/option"
            // Asserts the server render's content-model refusal.
            // fsharpanalyzer: ignore-line-next RAN024
            (fun t -> t.ul [||] [| t.li [||] [| t.li [||] [||] |] |]), restructures "nested-list-item" "ul/li/li"
            // Asserts the server render's content-model refusal.
            // fsharpanalyzer: ignore-line-next RAN024
            (fun t -> t.ul [||] [| t.li [||] [| t.div [||] [| t.li [||] [||] |] |] |]), restructures "nested-list-item" "ul/li/div/li"
            // Asserts the server render's content-model refusal.
            // fsharpanalyzer: ignore-line-next RAN024
            (fun t -> t.ul [||] [| t.li [||] [| t.span [||] [| t.li [||] [||] |] |] |]), restructures "nested-list-item" "ul/li/span/li"
            (fun t -> t.element (HtmlTag "dl") [||] [| t.element (HtmlTag "dd") [||] [| t.element (HtmlTag "dt") [||] [||] |] |]),
            restructures "nested-description" "dl/dd/dt"
            (fun t ->
                t.element
                    (HtmlTag "dl")
                    [||]
                    [|
                        t.element (HtmlTag "dt") [||] [| t.div [||] [| t.element (HtmlTag "dd") [||] [||] |] |]
                    |]),
            restructures "nested-description" "dl/dt/div/dd"
            (fun t -> t.element (HtmlTag "dl") [||] [| t.element (HtmlTag "dd") [||] [| t.element (HtmlTag "dd") [||] [||] |] |]),
            restructures "nested-description" "dl/dd/dd"
            // Asserts the server render's content-model refusal.
            // fsharpanalyzer: ignore-line-next RAN024
            (fun t -> t.h1 [||] [| t.h2 [||] [||] |]), restructures "nested-heading" "h1/h2"
            // Asserts the server render's content-model refusal.
            // fsharpanalyzer: ignore-line-next RAN024
            (fun t -> t.button [||] [| t.button [||] [||] |]), restructures "nested-button" "button/button"
            // Asserts the server render's content-model refusal.
            // fsharpanalyzer: ignore-line-next RAN024
            (fun t -> t.button [||] [| t.span [||] [| t.button [||] [||] |] |]), restructures "nested-button" "button/span/button"
        ]

    equal [ for _, expected in cases -> expected ] [ for build, _ in cases -> restructured build ]

    let closers = [ "center"; "dir"; "summary"; "listing"; "li"; "dd"; "dt" ]

    equal
        [ for tag in closers -> restructures "paragraph-close" ("p/" + tag) ]
        [
            for tag in closers -> restructured (fun t -> t.p [||] [| t.element (HtmlTag tag) [||] [||] |])
        ]

    let nearest =
        Component.define (fun _ () -> ()) (fun t ->
            t.fragment
                [|
                    t.table
                        [||]
                        [|
                            t.element (HtmlTag "colgroup") [||] [| t.text " "; t.element (HtmlTag "col") [||] [||] |]
                        |]
                    t.ul [||] [| t.li [||] [| t.ul [||] [| t.li [||] [||] |] |] |]
                    t.element
                        (HtmlTag "dl")
                        [||]
                        [|
                            t.element (HtmlTag "dd") [||] [| t.element (HtmlTag "dl") [||] [| t.element (HtmlTag "dt") [||] [||] |] |]
                        |]
                    t.h1 [||] [| t.span [||] [| t.h2 [||] [||] |] |]
                    t.button [||] [| t.span [||] [||] |]
                    t.button
                        [||]
                        [|
                            t.table [||] [| t.tbody [||] [| t.tr [||] [| t.td [||] [| t.button [||] [||] |] |] |] |]
                        |]
                    t.select [||] [| t.option [||] [||]; t.optgroup [||] [| t.option [||] [||] |] |]
                |])

    equal
        ("<!--[r--><table><colgroup> <col></colgroup></table><ul><li><ul><li></li></ul></li></ul>"
         + "<dl><dd><dl><dt></dt></dl></dd></dl><h1><span><h2></h2></span></h1><button><span></span></button>"
         + "<button><table><tbody><tr><td><button></button></td></tr></tbody></table></button>"
         + "<select><option></option><optgroup><option></option></optgroup></select><!--]-->")
        (Server.render Server.Options.defaults nearest ())

    let accepted =
        Component.define (fun _ () -> ()) (fun t ->
            t.div
                [||]
                [|
                    t.p [||] [| t.button [||] [| t.div [||] [||] |] |]
                    t.table [||] [| t.tbody [||] [| t.text " "; t.tr [||] [| t.td [||] [| t.text "x" |] |] |] |]
                |])

    equal
        "<!--[r--><div><p><button><div></div></button></p><table><tbody> <tr><td>x</td></tr></tbody></table></div><!--]-->"
        (Server.render Server.Options.defaults accepted ())

    let anchorInCell =
        Component.define (fun _ () -> ()) (fun t ->
            t.a
                [||]
                [|
                    t.table
                        [||]
                        [|
                            t.tbody [||] [| t.tr [||] [| t.td [||] [| t.a [||] [| t.text "x" |] |] |] |]
                        |]
                |])

    equal "<!--[r--><a><table><tbody><tr><td><a>x</a></td></tr></tbody></table></a><!--]-->" (Server.render Server.Options.defaults anchorInCell ())

let private tableRow =
    Component.define (fun _ (_: unit -> int) -> ()) (fun t -> t.tr [||] [| t.td [||] [||] |])

let private rowsInTable =
    Component.define (fun _ (rows: int array) -> rows) (fun t -> t.table [||] [| t.each id id tableRow |])

let private rowsInBody =
    Component.define (fun _ (rows: int array) -> rows) (fun t -> t.table [||] [| t.tbody [||] [| t.each id id tableRow |] |])

let private blockRow =
    Component.define (fun _ (_: unit -> int) -> ()) (fun t -> t.span [||] [| t.div [||] [||] |])

let private blocksInParagraph =
    Component.define (fun _ (rows: int array) -> rows) (fun t -> t.p [||] [| t.each id id blockRow |])

let private blocksInButton =
    Component.define (fun _ (rows: int array) -> rows) (fun t -> t.p [||] [| t.button [||] [| t.each id id blockRow |] |])

let private wrapped (inner: TemplateBuilder<unit> -> TemplateView<unit>) =
    Component.define (fun _ (_: unit -> int) -> ()) (fun t -> t.span [||] [| inner t |])

let private rowsUnder (outer: TemplateBuilder<int array> -> TemplateView<int array> -> TemplateView<int array>) row =
    Component.define (fun _ (rows: int array) -> rows) (fun t -> outer t (t.each id id row))

let private scopedRows () =
    let render definition =
        rejection (fun () ->
            Server.render Server.Options.defaults definition [| 1 |]
            |> ignore)

    equal
        [
            restructures "nested-anchor" "a/span/a"
            restructures "nested-form" "form/span/form"
            restructures "nested-button" "button/span/button"
            restructures "nested-list-item" "li/span/li"
            restructures "nested-description" "dd/span/dt"
            restructures "nested-heading" "h1/h2"
        ]
        [
            render (rowsUnder (fun t region -> t.a [||] [| region |]) (wrapped (fun t -> t.a [||] [||])))
            render (rowsUnder (fun t region -> t.form [||] [| region |]) (wrapped (fun t -> t.form [||] [||])))
            render (rowsUnder (fun t region -> t.button [||] [| region |]) (wrapped (fun t -> t.button [||] [||])))
            render (rowsUnder (fun t region -> t.li [||] [| region |]) (wrapped (fun t -> t.li [||] [||])))
            render (rowsUnder (fun t region -> t.element (HtmlTag "dd") [||] [| region |]) (wrapped (fun t -> t.element (HtmlTag "dt") [||] [||])))
            render (rowsUnder (fun t region -> t.h1 [||] [| region |]) (Component.define (fun _ (_: unit -> int) -> ()) (fun t -> t.h2 [||] [||])))
        ]

    let cellRow =
        Component.define (fun _ (_: unit -> int) -> ()) (fun t -> t.td [||] [| t.a [||] [||] |])

    let anchorCells =
        rowsUnder (fun t region -> t.a [||] [| t.table [||] [| t.tbody [||] [| t.tr [||] [| region |] |] |] |]) cellRow

    equal
        "<!--[r--><a><table><tbody><tr><!--[--><td><a></a></td><!--]--></tr></tbody></table></a><!--]-->"
        (Server.render Server.Options.defaults anchorCells [| 1 |])

let private dynamicContentModel () =
    let render definition (rows: int array) =
        Server.render Server.Options.defaults definition rows

    equal
        [ restructures "table-row" "table/tr"; restructures "table-row" "table/tr" ]
        [ for _ in 1..2 -> rejection (fun () -> render rowsInTable [| 1 |] |> ignore) ]

    equal
        [
            "<!--[r--><table><tbody><!--[--><tr><td></td></tr><!--]--></tbody></table><!--]-->"
            "<!--[r--><table><tbody><!--[--><tr><td></td></tr><tr><td></td></tr><!--]--></tbody></table><!--]-->"
        ]
        [ render rowsInBody [| 1 |]; render rowsInBody [| 1; 2 |] ]

    equal (restructures "paragraph-close" "p/span/div") (rejection (fun () -> render blocksInParagraph [| 1 |] |> ignore))

    equal "<!--[r--><p><button><!--[--><span><div></div></span><!--]--></button></p><!--]-->" (render blocksInButton [| 1 |])

let private tableHole =
    Component.define (fun _ (body: Program.View) -> body) (fun t -> t.table [||] [| t.view id |])

let private authoredContext () =
    let row = Component.render tableRow (fun () -> 1)

    let render (body: Program.View) =
        Server.render Server.Options.defaults tableHole body

    equal (restructures "table-row" "table/tr") (rejection (fun () -> render row |> ignore))

    equal
        "<!--[r--><table><!--[--><tbody><tr><td></td></tr></tbody><!--]--></table><!--]-->"
        (render (Program.element (HtmlTag "tbody") [||] [| row |]))

let private titleRegion =
    Component.define (fun _ (rows: int array) -> rows) (fun t -> t.title [||] [| t.each id id tableRow |])

let private rcdataRegion () =
    equal
        (restructures "rcdata-content" "title/#region")
        (rejection (fun () ->
            Server.render Server.Options.defaults titleRegion [| 1 |]
            |> ignore))

let private templateElement () =
    equal
        (true, "Template elements are not supported in templates at div/template")
        (restructured (fun t -> t.div [||] [| t.element (HtmlTag "template") [||] [||] |]))

    equal (true, "A void element has children at p/br") (restructured (fun t -> t.p [||] [| t.element (HtmlTag "br") [||] [| t.span [||] [||] |] |]))

let private stateScriptEscapes () =
    equal
        "<script type=\"application/json\" id=\"s&quot;\">{\"a\":\"\\u003c/script>\\u2028\\u003c/script>\\u2028\"}</script>"
        (Server.stateScript "s\"" "{\"a\":\"</script>\u2028</script>\u2028\"}")

    equal "<script type=\"application/json\" id=\"s\">[\"\\u2029\\u003c!--&amp;>\"]</script>" (Server.stateScript "s" "[\"\u2029<!--&amp;>\"]")

let private documentLog = ResizeArray<string>()

let private documentPage =
    Component.define
        (fun _ (page: PageProps) ->
            documentLog.Add "setup"
            page)
        (fun t ->
            t.div
                [||]
                [|
                    t.Document.title (fun (page: PageProps) -> page.Title)
                    description t
                    t.p [||] [| t.text (fun (page: PageProps) -> page.Description) |]
                |])

let private documentProps =
    {
        Title = "Tides"
        Description = "moons"
    }

let private documentHead =
    """<title data-ranvier-head="title">Tides</title>"""
    + """<meta data-ranvier-head="meta:name=description" name="description" content="moons">"""

let private fullDocument =
    { Server.DocumentShell.defaults with
        Lang = Some "en"
        State = [ "page-props", "{\"title\":\"Tides\"}" ]
        Stylesheets = [ "/site.css" ]
        Scripts = [ Server.ModuleScript.Src "/main.js"; Server.ModuleScript.Inline "start();" ]
        HtmlAttributes = [ "data-theme", "dark" ]
        BodyAttributes = [ "class", "page" ]
    }

let private documentOutput () =
    equal
        ("<!doctype html><html><head><meta charset=\"utf-8\">"
         + documentHead
         + "</head><body><div id=\"app\"><!--[r--><div><p>moons</p></div><!--]--></div></body></html>")
        (Server.renderDocument Server.Options.defaults Server.DocumentShell.defaults documentPage documentProps)

    equal
        ("<!doctype html><html lang=\"en\" data-theme=\"dark\"><head><meta charset=\"utf-8\">"
         + documentHead
         + "<link rel=\"stylesheet\" href=\"/site.css\"></head><body class=\"page\"><div id=\"app\">"
         + "<!--[r--><div><p>moons</p></div><!--]--></div>"
         + "<script type=\"application/json\" id=\"page-props\">{\"title\":\"Tides\"}</script>"
         + "<script type=\"module\" src=\"/main.js\"></script><script type=\"module\">start();</script></body></html>")
        (Server.renderDocument Server.Options.defaults fullDocument documentPage documentProps)

let private documentOrder () =
    let page = Server.renderPage Server.Options.defaults documentPage documentProps

    let html =
        Server.renderDocument Server.Options.defaults fullDocument documentPage documentProps

    let at (part: string) =
        html.IndexOf part

    let host = "<div id=\"app\">" + page.Body + "</div>"

    equal
        (true, true, true, true, true)
        (at page.Head > at "<meta charset"
         && at page.Head < at "</head>",
         at "<link rel=\"stylesheet\"" > at page.Head,
         at host > at "<body",
         at "<script type=\"application/json\"" = at host + host.Length,
         at "<script type=\"module\"" > at "<script type=\"application/json\"")

    let written = ResizeArray<string>()
    Server.renderDocumentTo Server.Options.defaults fullDocument documentPage documentProps written.Add
    equal html (String.concat "" written)

    let headless = Component.define (fun _ () -> ()) (fun t -> t.span [||] [||])
    let streamed = ResizeArray<string>()
    Server.renderDocumentTo Server.Options.defaults Server.DocumentShell.defaults headless () streamed.Add

    equal
        ("<!doctype html><html><head><meta charset=\"utf-8\"></head><body><div id=\"app\"><!--[r--><span></span><!--]--></div></body></html>", true)
        (Server.renderDocument Server.Options.defaults Server.DocumentShell.defaults headless (),
         Server.renderDocument Server.Options.defaults Server.DocumentShell.defaults headless () = String.concat "" streamed)

let private documentEscapes () =
    let hostile = "\"><script>alert(1)</script>&"
    let escaped = "&quot;&gt;&lt;script&gt;alert(1)&lt;/script&gt;&amp;"

    let document =
        { Server.DocumentShell.defaults with
            Lang = Some hostile
            HostId = "a\"b"
            State = [ "s\"", "\"</script>\"" ]
            Stylesheets = [ hostile ]
            Scripts = [ Server.ModuleScript.Src hostile ]
            HtmlAttributes = [ "data-x", hostile ]
            BodyAttributes = [ "class", hostile ]
        }

    let empty = Component.define (fun _ () -> ()) (fun t -> t.span [||] [||])

    equal
        ("<!doctype html><html lang=\""
         + escaped
         + "\" data-x=\""
         + escaped
         + "\"><head><meta charset=\"utf-8\"><link rel=\"stylesheet\" href=\""
         + escaped
         + "\"></head><body class=\""
         + escaped
         + "\"><div id=\"a&quot;b\"><!--[r--><span></span><!--]--></div>"
         + "<script type=\"application/json\" id=\"s&quot;\">\"\\u003c/script>\"</script>"
         + "<script type=\"module\" src=\""
         + escaped
         + "\"></script></body></html>")
        (Server.renderDocument Server.Options.defaults document empty ())

let private documentRejections () =
    let rejected (document: Server.DocumentShell) =
        documentLog.Clear ()
        let written = ResizeArray<string>()

        let error =
            failure (fun () -> Server.renderDocumentTo Server.Options.defaults document documentPage documentProps written.Add)

        let direct =
            failure (fun () ->
                Server.renderDocument Server.Options.defaults document documentPage documentProps
                |> ignore)

        error.Message, direct.Message = error.Message, written.Count, documentLog.Count

    let inlineScript text =
        { Server.DocumentShell.defaults with
            Scripts = [ Server.ModuleScript.Inline text ]
        }

    let invalidId = "An element id must be non-empty and contain no ASCII whitespace"
    let invalidInline = "Inline module text cannot contain </script or <!--"

    let messages =
        [
            for document in
                [
                    { Server.DocumentShell.defaults with
                        HostId = ""
                    }
                    { Server.DocumentShell.defaults with
                        HostId = null
                    }
                    { Server.DocumentShell.defaults with
                        HostId = " "
                    }
                    { Server.DocumentShell.defaults with
                        HostId = "a b"
                    }
                    { Server.DocumentShell.defaults with
                        HostId = "a\tb"
                    }
                    { Server.DocumentShell.defaults with
                        HostId = "a\u000Cb"
                    }
                    { Server.DocumentShell.defaults with
                        State = [ "", "1" ]
                    }
                    { Server.DocumentShell.defaults with
                        State = [ "s\n", "1" ]
                    }
                    { Server.DocumentShell.defaults with
                        BodyAttributes = [ "id", "a b" ]
                    }
                    { Server.DocumentShell.defaults with
                        HtmlAttributes = [ "id", "" ]
                    }
                ] ->
                let message, same, written, setups = rejected document
                message.StartsWith (invalidId, StringComparison.Ordinal), same, written, setups
            for text in [ "</script>"; "a</SCRIPT"; "x = 1 </ScRiPt "; "<!--"; "a<!-- b" ] ->
                let message, same, written, setups = rejected (inlineScript text)
                message.StartsWith (invalidInline, StringComparison.Ordinal), same, written, setups
        ]

    equal (List.replicate 15 (true, true, 0, 0)) messages

    let duplicates =
        [
            { Server.DocumentShell.defaults with
                State = [ "app", "1" ]
            }
            { Server.DocumentShell.defaults with
                State = [ "s", "1"; "s", "2" ]
            }
            { Server.DocumentShell.defaults with
                Lang = Some "en"
                HtmlAttributes = [ "lang", "fr" ]
            }
            { Server.DocumentShell.defaults with
                BodyAttributes = [ "class", "a"; "class", "b" ]
            }
            { Server.DocumentShell.defaults with
                BodyAttributes = [ "id", "app" ]
            }
            { Server.DocumentShell.defaults with
                State = [ "s", "1" ]
                HtmlAttributes = [ "id", "s" ]
            }
            { Server.DocumentShell.defaults with
                HtmlAttributes = [ "id", "x" ]
                BodyAttributes = [ "id", "x" ]
            }
        ]
        |> List.map (fun document ->
            let message, _, written, setups = rejected document
            message.Contains "twice", written, setups)

    equal (List.replicate 7 (true, 0, 0)) duplicates

    let names =
        [ "onclick"; "Class"; ""; "a b" ]
        |> List.map (fun name ->
            let message, same, written, setups =
                rejected
                    { Server.DocumentShell.defaults with
                        BodyAttributes = [ name, "x" ]
                    }

            message.Contains "names must", same, written, setups)

    equal (List.replicate 4 (true, true, 0, 0)) names

    let nullState =
        let message, same, written, setups =
            rejected
                { Server.DocumentShell.defaults with
                    State = [ "s", null ]
                }

        message.StartsWith ("The state element s has a null JSON value", StringComparison.Ordinal), same, written, setups

    equal (true, true, 0, 0) nullState

    let hostIds =
        Server.renderDocument
            Server.Options.defaults
            { Server.DocumentShell.defaults with
                HtmlAttributes = [ "id", "root" ]
                BodyAttributes = [ "id", "page" ]
            }
            documentPage
            documentProps

    equal
        true
        (hostIds.Contains "<html id=\"root\">"
         && hostIds.Contains "<body id=\"page\"><div id=\"app\">")

    let accepted =
        [ "if (a < b) {}"; "</scrip"; "</style>"; "<!-"; "'<\\/script>'"; "" ]
        |> List.map (fun text ->
            (Server.renderDocument Server.Options.defaults (inlineScript text) documentPage documentProps)
                .Contains("<script type=\"module\">" + text + "</script>"))

    equal (List.replicate 6 true) accepted

let private documentNonce () =
    let nonced =
        { fullDocument with
            Nonce = Some "aB3+/=-_"
        }

    equal
        ("<link rel=\"stylesheet\" href=\"/site.css\" nonce=\"aB3+/=-_\"></head><body class=\"page\"><div id=\"app\">"
         + "<!--[r--><div><p>moons</p></div><!--]--></div>"
         + "<script type=\"application/json\" id=\"page-props\" nonce=\"aB3+/=-_\">{\"title\":\"Tides\"}</script>"
         + "<script type=\"module\" src=\"/main.js\" nonce=\"aB3+/=-_\"></script>"
         + "<script type=\"module\" nonce=\"aB3+/=-_\">start();</script></body></html>")
        (let html =
            Server.renderDocument Server.Options.defaults nonced documentPage documentProps

         html.Substring (html.IndexOf "<link"))

    let rejected =
        [ ""; "a b"; "a\"b"; "a'b"; "a<b"; "a&b"; "é"; "a\n" ]
        |> List.map (fun nonce ->
            documentLog.Clear ()
            let written = ResizeArray<string>()

            let error =
                failure (fun () ->
                    Server.renderDocumentTo Server.Options.defaults { fullDocument with Nonce = Some nonce } documentPage documentProps written.Add)

            error.Message.StartsWith ("A nonce must be non-empty", StringComparison.Ordinal), written.Count, documentLog.Count)

    equal (List.replicate 8 (true, 0, 0)) rejected

    let render (document: Server.DocumentShell) =
        Server.renderDocument Server.Options.defaults document documentPage documentProps

    equal
        (render
            { fullDocument with
                Lang = None
                Nonce = None
            })
        (render
            { fullDocument with
                Lang = Some null
                Nonce = Some null
            })

let private documentFailures () =
    attemptLog.Clear ()
    let written = ResizeArray<string>()

    let pending =
        failure (fun () -> Server.renderDocumentTo Server.Options.defaults Server.DocumentShell.defaults unguarded () written.Add)

    let isPending =
        match pending with
        | Server.ServerPendingException _ -> true
        | _ -> false

    let cleanupFails =
        Component.define (fun setup () -> setup.OnCleanup (fun () -> failwith "cleanup failed")) (fun t -> t.span [||] [||])

    let cleanupAndSetupFail =
        Component.define
            (fun setup () ->
                setup.OnCleanup (fun () -> failwith "cleanup failed")
                failwith "setup failed")
            (fun t -> t.span [||] [||])

    let both (definition: Component<unit>) =
        let streamed = ResizeArray<string>()

        let direct =
            failure (fun () ->
                Server.renderDocument Server.Options.defaults Server.DocumentShell.defaults definition ()
                |> ignore)

        let viaWrite =
            failure (fun () -> Server.renderDocumentTo Server.Options.defaults Server.DocumentShell.defaults definition () streamed.Add)

        direct.Message, viaWrite.Message = direct.Message, streamed.Count

    let cleanupMessage, cleanupSame, cleanupWritten = both cleanupFails
    let failedMessage, failedSame, failedWritten = both cleanupAndSetupFail

    equal
        (true, [ "unguarded cleanup" ], 0, (true, true, 0), (true, true, 0))
        (isPending,
         List.ofSeq attemptLog,
         written.Count,
         (cleanupMessage.StartsWith ("Server render cleanup failed: cleanup failed", StringComparison.Ordinal), cleanupSame, cleanupWritten),
         (failedMessage.StartsWith ("Server render failed: setup failed; cleanup failed", StringComparison.Ordinal), failedSame, failedWritten))

let cases: (string * (unit -> unit)) array =
    [|
        "CreateId numbers inlined children in pre-order and puts an inlined child's rows under its path", nestedIdentity
        "each view row counts its authored steps from 0 and the enclosing row resumes its count after it", holesIdentity
        "a Setup kept from one setup raises when called during another instance's setup", createIdInLaterSetup
        "CreateId follows the instance path for inlined children before and after regions, nested rows and arms", serverIdentity
        "CreateId after setup raises", createIdAfterSetup
        "authored components receive ids distinct from template ids", authoredIds
        "a view hole's first template takes the row's position and a later one the authored step under it", holeIdentity
        "renderPage writes keys in first-activation order and the last entry for a key", headOrder
        "renderPage activates an instance's own entries before its inlined children's and its regions' instances after both", activationOrder
        "renderPage activates every row of a pass before the rows' inner rows, compiled and expanded", containerOrder
        "renderPage escapes titles and attribute values and marks every element", pageEscapes
        "render keeps the document entry policy and renderPage collects whatever the policy", headPolicy
        "renderPage collects the entries of a compiled and an expanded template in a view hole", holedHeads
        "a boundary's abandoned content attempt runs its cleanups before the pending or error view is written", boundaryAttempts
        "an authored or expanded boundary in a view hole runs its content in an attempt and writes <!--[--> for every view", expandedBoundaryAttempts
        "an authored component's pending read is pending and its cleanup failure fails the render", authoredPendingWithCleanupFailure
        "a template expanded through Program.ops writes the serialised properties the compiled plan writes", expandedProperties
        "boundary markers record content, pending and error views; a failure inside the pending view goes to the enclosing boundary",
        pendingViewFailure
        "a pending read outside every boundary raises ServerPendingException after disposal", pendingOutsideBoundaries
        "ServerPendingException says a read outside every boundary is pending", pendingMessage
        "a boundary's content takes row 0 and its pending or error view row 1 of the boundary's region", boundaryIdentity
        "a cleanup failure of an abandoned content attempt fails the render", abandonedCleanupFailure
        "an abandoned boundary attempt's entries are not written and an overridden key's earlier entry returns", abandonedHeads
#if FABLE_COMPILER
        "two sequential server requests collect only their own entries", sequentialHeads
#endif
        "server plan matches the current writer for every template fixture", templatePins
        "server plan matches the current writer for arms", armPins
        "server plan matches the current writer for boundaries", boundaryPins
        "empty text renders as recorded; void children and a tr in a table fail at plan compile", edgePins
        "an inlined child's cleanups run with its parent's, after the rows' own", inlinedCleanupOrder
        "raw HTML content is written verbatim", rawHtmlPin
        "raw HTML containing a marker-shaped comment raises", rawHtmlForgesNoMarker
        "a pre, textarea or listing first child text starting with a line feed gets a second one", leadingLineFeed
        "escape corpus is byte-identical on .NET and Fable", escapeCorpus
        "null text and attribute values escape as empty strings on .NET and Fable", nullEscape
        "attribute and tag names outside the validated set fail at definition time", namesAtDefinition
        "a definition's server plan is compiled once and merges adjacent static markup", sharedPlan
        "a browser-only part under Reject names the element and the part", browserRejection
        "serialised properties write their attributes; browser-only properties raise under Reject and write nothing under Omit", serialisedProperties
        "listeners and browser hooks are never invoked on the server", browserPartsUninvoked
        "a property and a static attribute writing the same attribute fail at plan compile", duplicateAttributeSource
        "number properties serialise in ECMAScript number form on .NET and Fable", numberForm
        "a textarea's value property writes its escaped child text, and a textarea with children and that property fails at plan compile",
        textareaChildText
        "a placed hook and an authored browser hook under Reject name the element and the part", placedRejection
        "renderPage activates a view hole's entries after a later inlined child's, compiled and expanded", holeAfterChildren
        "a template in a view hole is owned by the backend's active scope", holeTemplateOwnedByRequest
        "a template in a view hole is owned by the request and cleaned up child-first after the render", holeTemplateOwnedByRequestScope
        "a request owns every instance and disposes them child-first after the render", requestRoot
        "setup failure disposes the request", setupFailureDisposes
        "OnCleanup after setup raises on the server", cleanupAfterSetupRejected
        "a Setup kept from a completed setup raises in a later sibling's setup", keptSetupInSiblingRejected
        "a resolved boundary's content cleans up between its siblings, in order with their errors", resolvedBoundaryCleanupOrder
        "duplicate keys fail the server render with the core's exception", duplicateKeys
        "renderTo writes the rendered segments after success and nothing after failure", renderToSegments
        "a render whose cleanups fail raises an aggregate holding and naming every error", aggregateFailures
        "textarea, checkbox and select write their server state; select.value is browser-only", forms
        "a static textarea value starting with a line feed gets a second one", staticTextareaLineFeed
        "an unchecked checkbox and a select with no matching option write no checked or selected attribute", formsUnset
        "a textarea value escapes its closing tag and ampersand; an empty or null value writes no text", textareaValues
        "the structural hash is FNV-1a over the canonical skeleton, equal on .NET and Fable", hashVectors
        "the structural hash names each region's kind", regionHashes
        "an arm region reading no arm fails with a named error", armRegionArity
        "server HTML carries arm, empty-text, separator and zero-root markers", markers
        "rows are separated only between a trailing and a leading text, and a zero-root arm writes the row marker", rowMarkers
        "an empty text slot in a textarea or title writes nothing, and more than one text child fails at plan compile", rcdataText
        "non-empty raw HTML in a pre or listing is written after a line feed, dropped on parsing", contentLeadingLineFeed
        "a template expanded through Program.ops writes raw content as the compiled plan does, under Reject", expandedContent
        "content-model violations fail at plan compile with the rule and the element path", contentModel
        "a row definition with tr roots inside a table region fails on first render", dynamicContentModel
        "a row whose descendant closes an open a, form, button, li, dd or heading fails on first render", scopedRows
        "a template in a view hole is checked against the template and authored elements enclosing it", authoredContext
        "a region inside a textarea or title fails at plan compile", rcdataRegion
        "void elements with children and template elements fail at plan compile", templateElement
        "stateScript escapes the script end and line separators", stateScriptEscapes
        "renderDocument writes the doctype, html, head entries, stylesheets, host, state and scripts", documentOutput
        "renderDocument places head entries in the head, only the body in the host, then state, then scripts; renderDocumentTo writes the same document",
        documentOrder
        "renderDocument escapes every attribute value and the state script's id", documentEscapes
        "renderDocument rejects invalid or repeated ids across host, state, html and body, invalid attributes, null state and closing inline script text before the render",
        documentRejections
        "renderDocument raises ServerPendingException and the cleanup aggregate as renderPage does, and renderDocumentTo writes nothing then",
        documentFailures
        "renderDocument writes the nonce on every script, state and stylesheet element, reads Some null as None and rejects an empty or out-of-charset nonce",
        documentNonce
    |]

#if !FABLE_COMPILER
let private escapeKeepsReference () =
    let text = "plain text without markup"
    equal true (obj.ReferenceEquals (text, Html.escape false text))
    equal true (obj.ReferenceEquals (text, Html.escape true text))

/// <summary>Runs <c>count</c> calls of <c>body</c> on pool threads released together.</summary>
let private together (count: int) (body: unit -> 'value) : 'value array =
    use barrier = new System.Threading.Barrier (count)

    [|
        for _ in 1..count ->
            System.Threading.Tasks.Task.Run (fun () ->
                if not (barrier.SignalAndWait (TimeSpan.FromSeconds 10.)) then
                    failwith "Barrier timed out"

                body ())
    |]
    |> Array.map _.Result

let private concurrentPublication () =
    let fresh =
        Component.define (fun _ () -> ()) (fun t -> t.span [||] [| t.text "fresh" |])

    let plans = together 8 (fun () -> serverPlan fresh)

    equal
        true
        (plans
         |> Array.forall (fun plan -> obj.ReferenceEquals (plan, plans[0])))

    let arm =
        Component.define (fun _ (_: unit -> int) -> ()) (fun t -> t.span [||] [||])

    let readers = together 8 (fun () -> (TemplateBuilder<unit>().arm arm 1).Reader)

    equal
        true
        (readers
         |> Array.forall (fun reader -> obj.ReferenceEquals (reader, readers[0])))

let private concurrentRequests () =
    use barrier = new System.Threading.Barrier (2)
    let graphs = System.Collections.Concurrent.ConcurrentBag<Graph>()

    let waiting =
        Component.define
            (fun _ (name: string) ->
                graphs.Add Graph.Current

                if not (barrier.SignalAndWait (TimeSpan.FromSeconds 10.)) then
                    failwith "Barrier timed out"

                Api.createSignal name)
            (fun t -> t.span [||] [| t.text (fun (name: Signal<string>) -> name.Value) |])

    let request name =
        System.Threading.Tasks.Task.Run (fun () -> Server.render Server.Options.defaults waiting name)

    let left, right = request "left", request "right"
    equal ("<!--[r--><span>left</span><!--]-->", "<!--[r--><span>right</span><!--]-->") (left.Result, right.Result)
    equal [ true; true ] [ for graph in graphs -> graph.Root.IsDisposed ]
    equal false (obj.ReferenceEquals (Seq.head graphs, Seq.last graphs))

/// <summary>Reads and writes a signal of a graph outside the request from a request on a pool thread.</summary>
let private sharedSignal (affinity: ThreadAffinity) =
    use outer = new Graph (GraphOptions.Default.WithThreadAffinity affinity)
    let shared = outer.Run (fun () -> Api.createSignal "s")

    let reading =
        Component.define (fun _ () -> shared) (fun t -> t.span [||] [| t.text (fun (s: Signal<string>) -> s.Value) |])

    let writing =
        Component.define (fun _ (value: string) -> shared.Value <- value) (fun t -> t.span [||] [||])

    let onPool (body: unit -> string) =
        try
            Ok (System.Threading.Tasks.Task.Run(body).Result)
        with :? AggregateException as error ->
            Error error.InnerException

    equal (Ok "<!--[r--><span>s</span><!--]-->") (onPool (fun () -> Server.render Server.Options.defaults reading ()))

    match affinity, onPool (fun () -> Server.render Server.Options.defaults writing "w") with
    | ThreadAffinity.Guarded, Error error -> equal true (error :? InvalidOperationException)
    | ThreadAffinity.Unchecked, Ok _ ->
        equal (Ok "<!--[r--><span>w</span><!--]-->") (onPool (fun () -> Server.render Server.Options.defaults reading ()))
    | _, outcome -> failwithf "Unexpected outcome %A under %O" outcome affinity

/// <summary>The messages of a render failure: an <c>AggregateException</c>'s inner messages, or the message itself.</summary>
let private messages (error: exn) : string list =
    match error with
    | :? AggregateException as aggregate -> [ for inner in aggregate.InnerExceptions -> inner.Message ]
    | error -> [ error.Message ]

let private concurrentTeardown () =
    let parties = 8
    use barrier = new System.Threading.Barrier (parties)

    let throwing =
        Component.define
            (fun setup (name: string) ->
                setup.OnCleanup (fun () ->
                    if not (barrier.SignalAndWait (TimeSpan.FromSeconds 10.)) then
                        failwith "Barrier timed out"

                    failwith ("cleanup " + name))

                name)
            (fun t -> t.span [||] [| t.text id |])

    let request name =
        System.Threading.Tasks.Task.Run (fun () ->
            failure (fun () ->
                Server.render Server.Options.defaults throwing name
                |> ignore))

    let outcome (error: exn) =
        error.Message.StartsWith "Server render cleanup failed: ", messages error

    let names = [ for party in 1..parties -> "r" + string party ]

    let rounds =
        [
            for _ in 1..50 ->
                let requests = [ for name in names -> request name ]
                [ for request in requests -> outcome request.Result ]
        ]

    equal (List.replicate 50 [ for name in names -> true, [ "cleanup " + name ] ]) rounds

let private concurrentHeads () =
    let parties = 8
    use barrier = new System.Threading.Barrier (parties)

    let pause =
        Component.define
            (fun _ () ->
                if not (barrier.SignalAndWait (TimeSpan.FromSeconds 10.)) then
                    failwith "Barrier timed out")
            (fun t -> t.span [||] [||])

    let page = namedPage pause

    let request name =
        System.Threading.Tasks.Task.Run (fun () -> Server.renderPage Server.Options.defaults page name)

    let names = [ for party in 1..parties -> "r" + string party ]

    let rounds =
        [
            for _ in 1..50 ->
                let requests = [ for name in names -> request name ]
                [ for request in requests -> request.Result.Head ]
        ]

    equal (List.replicate 50 [ for name in names -> namedHead name ]) rounds

let private collectorTruncates () =
    let heads = HeadCollector ()
    heads.Add ("title", "<t1>")
    heads.Add (null, "<l1>")
    heads.Add ("meta:name=d", "<m1>")
    let mark = heads.Count
    heads.Open ()
    let outer = heads.EnterGroup ()
    heads.Add ("title", "<t2>")
    heads.Add (null, "<l2>")
    heads.LeaveGroup outer
    heads.Add ("meta:name=d", "<m2>")
    heads.Add ("link:canonical", "<c>")
    heads.Close ()
    equal "<t2><l1><m2><c><l2>" (heads.ToString ())
    heads.Truncate mark
    equal (3, "<t1><l1><m1>") (heads.Count, heads.ToString ())
    equal true ((failure (fun () -> heads.Truncate 4)) :? ArgumentException)

let private documentParamNames () =
    let paramName (document: Server.DocumentShell) =
        match
            failure (fun () ->
                Server.renderDocument Server.Options.defaults document documentPage documentProps
                |> ignore)
        with
        | :? ArgumentException as error -> error.ParamName
        | error -> error.GetType().Name

    equal
        (List.replicate 7 "document")
        [
            for document in
                [
                    { Server.DocumentShell.defaults with
                        HostId = ""
                    }
                    { Server.DocumentShell.defaults with
                        State = [ "app", "1" ]
                    }
                    { Server.DocumentShell.defaults with
                        State = [ "s", null ]
                    }
                    { Server.DocumentShell.defaults with
                        BodyAttributes = [ "onclick", "x" ]
                    }
                    { Server.DocumentShell.defaults with
                        BodyAttributes = [ "class", "a"; "class", "b" ]
                    }
                    { Server.DocumentShell.defaults with
                        Nonce = Some "a b"
                    }
                    { Server.DocumentShell.defaults with
                        Scripts = [ Server.ModuleScript.Inline "</script>" ]
                    }
                ] -> paramName document
        ]

let dotnetCases: (string * (unit -> unit)) array =
    [|
        "every renderDocument rejection is an ArgumentException naming the document parameter", documentParamNames
        "escape returns the input string when nothing needs escaping", escapeKeepsReference
        "concurrent first renders publish one plan and one arm reader", concurrentPublication
        "two concurrent requests render their own props", concurrentRequests
        "a module-level signal read by two requests is shared: a Guarded graph rejects a request's write",
        fun () -> sharedSignal ThreadAffinity.Guarded
        "a module-level signal read by two requests is shared: an Unchecked graph takes a request's write",
        fun () -> sharedSignal ThreadAffinity.Unchecked
        "two requests whose cleanups fail concurrently each report only their own error", concurrentTeardown
        "two concurrent server requests collect only their own entries", concurrentHeads
        "truncating the head collector drops later entries and restores an overridden key's earlier entry", collectorTruncates
    |]
#endif
