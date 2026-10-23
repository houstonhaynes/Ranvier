module UnsafeHtmlChecks

open System
open Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View
open Ranvier.View.Testing
open CheckSupport

let private raisesArgument (action: unit -> unit) =
    try
        action ()
        false
    with :? ArgumentException ->
        true

/// <summary>Observes the node's subtree; the returned function stops observing and returns the number of child-list records.</summary>
#if FABLE_COMPILER
[<Emit("(() => { const records = []; const observer = new ($0.ownerDocument.defaultView.MutationObserver)(items => records.push(...items)); observer.observe($0, { childList: true, subtree: true }); return () => { records.push(...observer.takeRecords()); observer.disconnect(); return records.length; }; })()")>]
let private observeChildList (node: Node) : unit -> int = jsNative
#else
// The child-list writes to the node and its subtree until the result is called, one per insertion or removal call.
let private observeChildList (node: Node) : unit -> int =
    let count = ref 0

    let restores =
        [
            for name in
                [
                    "appendChild"
                    "insertBefore"
                    "removeChild"
                    "replaceChild"
                    "remove"
                    "replaceWith"
                    "replaceChildren"
                    "set_innerHTML"
                ] ->
                Conformance.interceptOn null name (fun original target args ->
                    match target with
                    | :? Node as touched when node.contains touched -> count.Value <- count.Value + 1
                    | _ -> ()

                    original args)
        ]

    fun () ->
        for restore in restores do
            restore ()

        count.Value
#endif

let private article =
    Component.define (fun _ (body: unit -> UnsafeHtml) -> body) (fun t ->
        t.div [| t.Attrs.className "prose"; t.Unsafe.html (fun body -> body ()) |] [||])

let private reparse queued () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "<b>one</b>")
    let tick = graph.Run (fun () -> Api.createSignal 0)
    let target = host ()

    let body () =
        tick.Value |> ignore
        UnsafeHtml.Trust source.Value

    let mounted =
        mountTemplate queued graph strict target (Component.render article body)

    let div =
        target.querySelector "div"
        |> Option.get
        |> unbox<HTMLDivElement>

    let first = div.innerHTML
    let stop = observeChildList div
    graph.Run (fun () -> tick.Value <- 1)
    mounted.Flush ()
    graph.Run (fun () -> source.Value <- "<i>two</i>")
    mounted.Flush ()
    equal ("<b>one</b>", "<i>two</i>", 1) (first, div.innerHTML, stop ())
    mounted.Dispose ()

let private tableBody =
    Component.define (fun _ () -> ()) (fun t ->
        t.table
            [||]
            [|
                t.tbody [| t.Unsafe.html (UnsafeHtml.Trust "<tr><td>a</td></tr><tr><td>b</td></tr>") |] [||]
            |])

let private inContext queued () =
    use graph = new Graph ()
    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render tableBody ())

    equal (2., "ab") ((target.querySelectorAll "tbody > tr").length, target.textContent)
    mounted.Dispose ()

let private svgGroup =
    Component.define (fun _ () -> ()) (fun t ->
        t.element
            (HtmlTag "svg")
            [||]
            [|
                t.element (HtmlTag "g") [| t.Unsafe.html (UnsafeHtml.Trust "<circle></circle>") |] [||]
            |])

/// <summary>Raw HTML inside an SVG element is written as the element's markup.</summary>
let private inSvg queued () =
    use graph = new Graph ()
    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render svgGroup ())

    let shown = target.innerHTML
    mounted.Dispose ()
    Conformance.recordIfRunning "shown" shown
    equal true (shown.Contains "<g><circle></circle></g>")

let private definitionChecks () =
    let defines (build: TemplateBuilder<unit> -> TemplateView<unit>) =
        raisesArgument (fun () -> Component.define (fun _ () -> ()) build |> ignore)

    let raw = UnsafeHtml.Trust "<b>x</b>"

    equal
        [ true; true; true; true; true; true; true; true; true; false ]
        [
            // Asserts that the definition refuses the content part.
            // fsharpanalyzer: ignore-line-next RAN023
            defines (fun t -> t.div [| t.Unsafe.html raw |] [| t.text "child" |])
            // Asserts that the definition refuses the content part.
            // fsharpanalyzer: ignore-line-next RAN023
            defines (fun t -> t.div [| t.Unsafe.html raw |] [| t.fragment [||] |])
            // Asserts that the definition refuses the content part.
            // fsharpanalyzer: ignore-line-next RAN023
            defines (fun t -> t.div [| t.Hooks.widget (fun _ (_: HTMLDivElement) -> ignore) |] [| t.fragment [||] |])
            // Asserts that the definition refuses the content part.
            // fsharpanalyzer: ignore-line-next RAN023
            defines (fun t -> t.div [| t.Unsafe.html raw; t.Hooks.widget (fun _ (_: HTMLDivElement) -> ignore) |] [||])
            // Asserts that the definition refuses the content part.
            // fsharpanalyzer: ignore-line-next RAN023
            defines (fun t -> t.br [| t.Unsafe.html raw |])
            // Asserts that the definition refuses the content part.
            // Asserts that the definition refuses raw HTML on this element.
            // fsharpanalyzer: ignore-line-next RAN023, RAN024
            defines (fun t -> t.script [| t.Unsafe.html raw |] [||])
            // Asserts that the definition refuses the content part.
            // Asserts that the definition refuses raw HTML on this element.
            // fsharpanalyzer: ignore-line-next RAN023, RAN024
            defines (fun t -> t.template [| t.Unsafe.html raw |] [||])
            // Asserts that the definition refuses the content part.
            // fsharpanalyzer: ignore-line-next RAN023
            defines (fun t -> t.textarea [| t.Unsafe.html raw |] [||])
            // Asserts that the definition refuses the content part.
            // fsharpanalyzer: ignore-line-next RAN023
            defines (fun t -> t.title [| t.Unsafe.html raw |] [||])
            defines (fun t ->
                t.div
                    [|
                        t.Attrs.className "prose"
                        t.Unsafe.html raw
                        t.Hooks.lend (fun _ (_: HTMLDivElement) -> ignore)
                    |]
                    [||])
        ]

let private rawSource () =
    Conformance.local "UnsafeHtmlChecks.rawSource" (fun () -> ref (Unchecked.defaultof<_>: Signal<AsyncSource<string>>))

let private rawContent =
    Component.define (fun _ (_: BoundaryStatus) -> ()) (fun t ->
        t.div
            [|
                t.Unsafe.html (fun () -> UnsafeHtml.Trust (rawSource ()).Value.Value.Value)
            |]
            [||])

let private rawPending =
    Component.define (fun _ () -> ()) (fun t -> t.span [||] [| t.text "loading" |])

let private rawFailed =
    Component.define (fun _ (failure: BoundaryFailure) -> failure) (fun t ->
        t.span [||] [| t.text (fun failure -> "failed: " + failure.Error.Message) |])

let private rawGuarded =
    Component.define (fun _ () -> ()) (fun t -> t.section [||] [| t.boundary rawPending rawFailed rawContent |])

let private pendingReader queued () =
    use graph = new Graph ()
    (rawSource ()).Value <- graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<string>()))
    let reports = ResizeArray<exn list>()
    let target = host ()

    let mounted =
        mountTemplate queued graph reports.Add target (Component.render rawGuarded ())

    let pending = target.textContent
    graph.Run (fun () -> (rawSource ()).Value.Value.Settle "<b>ready</b>")
    mounted.Flush ()

    let revealed =
        (target.querySelector "b"
         |> Option.get
         |> unbox<HTMLElement>)
            .textContent

    graph.Run (fun () -> (rawSource ()).Value.Value <- Api.createAsyncSource<string>())
    graph.Run (fun () -> (rawSource ()).Value.Value.Fail(Exception "markup"))
    mounted.Flush ()
    equal ("loading", "ready", "failed: markup", [ [ "markup" ] ]) (pending, revealed, target.textContent, messages reports)
    mounted.Dispose ()

let private serverRaw =
    Component.define (fun _ () -> ()) (fun t ->
        t.fragment
            [|
                t.div [| t.Unsafe.html (UnsafeHtml.Trust "<b>raw</b> &amp; <i>kept</i>") |] [||]
                t.span [||] [| t.text (fun () -> "<escaped>") |]
            |])

let private serverHtml () =
    let render policy =
        Html.render policy (Program.run (Component.render serverRaw ()))

    let expected = "<div><b>raw</b> &amp; <i>kept</i></div><span>&lt;escaped&gt;</span>"

    equal (expected, expected) (render Reject, render Omit)

let private nullMessage = "An UnsafeHtml reader returned null"

let private namesNull (error: exn) =
    error :? ArgumentException
    && error.Message.StartsWith nullMessage

/// <summary>A raw-HTML reader returning null, mounted and rendered on the server.</summary>
let private nullReader queued () =
    use graph = new Graph ()
    let reports = ResizeArray<exn list>()

    let none () =
        Unchecked.defaultof<UnsafeHtml>

    let raised =
        try
            let mounted =
                mountTemplate queued graph reports.Add (host ()) (Component.render article none)

            mounted.Dispose ()
            []
        with error ->
            [ error ]

    let server =
        try
            Html.render Omit (Program.run (Component.render article none))
            |> ignore

            false
        with error ->
            namesNull error

    equal ([ true ], true) ([ for error in raised @ List.concat reports -> namesNull error ], server)

/// <summary>The data of every comment the HTML parser produces for <c>markup</c> as a div's content, in document order.</summary>
#if FABLE_COMPILER
[<Emit("(() => { const div = document.createElement('div'); div.innerHTML = $0; const walker = document.createTreeWalker(div, 128); const data = []; while (walker.nextNode()) data.push(walker.currentNode.data); return data; })()")>]
let private parsedComments (markup: string) : string array = jsNative
#else
let private parsedComments (markup: string) : string array =
    let div = (Conformance.document ()).createElement "div"
    div.innerHTML <- markup
    let data = ResizeArray<string>()

    let rec walk (node: Node) =
        let mutable child = node.firstChild

        while child.IsSome do
            let current = unbox<Node> child.Value

            if current.nodeType = 8.0 then
                data.Add (unbox<CharacterData> current).data

            walk current
            child <- current.nextSibling

    walk div
    data.ToArray ()
#endif

/// <summary>Raw markup the parser turns into hydration-marker comments is rejected on the server; markup whose comments are plain is written.</summary>
let private markerParity () =
    let markerShaped (data: string) =
        data.StartsWith ("[", StringComparison.Ordinal)
        || data = "]"

    let observed markup =
        let server =
            try
                Html.render Omit (Program.run (Component.render article (fun () -> UnsafeHtml.Trust markup)))
                |> ignore

                false
            with error ->
                error.Message = "Raw HTML cannot contain a comment that starts with [ or equals ]"

        Array.exists markerShaped (parsedComments markup), server

    let markers =
        [
            "<!--[-->y"
            "<!--]-->"
            "<![>y"
            "</[>y"
            "<!]>y"
            "<!--x--!><!--[-->y"
            "<!--]--!>"
            "<!--]-"
            "<!--]--!"
            "</]"
            "<![CDATA[x]]>"
        ]

    let plain =
        [
            "<!-- [-->"
            "<!--]x-->"
            "<!--]--x-->"
            "<!doctype html>"
            "<?xml?>"
            "<!x>"
            "</ x>"
        ]

    equal (List.map (fun _ -> true, true) markers) (List.map observed markers)
    equal (List.map (fun _ -> false, false) plain) (List.map observed plain)

/// <summary>Mounts trusted markup in a div appended to the document body; returns the div and the disposal.</summary>
let unsafeMarkup (markup: string) : HTMLElement * (unit -> unit) =
    let graph = new Graph ()
    let target = host ()
    attachToBody target

    let mounted =
        mountTemplate false graph strict target (Component.render article (fun () -> UnsafeHtml.Trust markup))

    target,
    fun () ->
        mounted.Dispose ()
        target.remove ()
        graph.Dispose ()

let private checks: (string * (unit -> unit)) array =
    strictCases (
        Array.concat
            [
                templated "raw HTML replaces the element's content and is re-parsed only when the markup changes" reparse
                templated "raw HTML inside a tbody element parses table rows in context" inContext
                templated "raw HTML inside an SVG element is written as its markup" inSvg
                [|
                    "an element with raw HTML and template children fails at definition time; void, raw-text, template, textarea and title elements reject raw HTML",
                    definitionChecks
                    "server HTML writes raw content verbatim under Reject and Omit, and text slots beside it stay escaped", serverHtml
                    Conformance.platformOnly
                        "jsdom"
                        "parses bogus comments and CDATA, outside the twin parser's subset"
                        ("server HTML rejects exactly the raw markup that jsdom parses into marker comments, for these inputs", markerParity)
                |]
                templated "a pending raw-HTML reader keeps its boundary pending; a failing one shows the error view" pendingReader
                templated "a raw-HTML reader returning null fails naming the null value, on the client and the server" nullReader
            ]
    )

let cases: Conformance.Case array = Conformance.ofChecks checks
