/// <summary>Hydration checks: jsdom through tests/hydration.test.mjs, Chromium through tests/hydration.spec.mjs.</summary>
module HydrationChecks

open System
open global.Ranvier
open Fable.Core
open Fable.Core.JsInterop
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open CheckSupport
open ServerFixtures

/// <summary>The <c>id</c> attributes of the elements under <c>root</c>, in document order.</summary>
let private domIds (root: HTMLElement) : string list =
    let found = root.querySelectorAll "[id]"

    [
        for index in 0 .. int found.length - 1 -> (unbox<Element>(found.item (float index))).id
    ]

let private mount graph (target: HTMLElement) view =
    Mounting.mount graph None strict target None (fun context -> Program.run view context.Backend)

/// <summary>The element ids of the server HTML of <c>definition</c>, parsed under a detached element.</summary>
let private servedIds (definition: Component<'props>) (props: 'props) =
    let parsed = host ()
    loadServed parsed (Server.render Server.Options.defaults definition props)
    domIds parsed

let private clientIdentity () =
    let served = servedIds identified [| 7; 8 |]
    use graph = new Graph ()
    let target = host ()
    identityLog.Clear ()
    let mounted = mount graph target (Component.render identified [| 7; 8 |])
    let issued = List.sort (List.ofSeq identityLog)
    let shown = domIds target
    mounted.Dispose ()
    // The root's second id is logged only.
    let visible =
        identityIds "r"
        |> List.filter (fun id -> id <> "r-0-1")

    equal (identityIds "r", visible, served) (issued, List.sort served, shown)

let private clientCreateIdAfterSetup () =
    use graph = new Graph ()
    let captured: Setup option ref = ref None

    let capturing =
        Component.define (fun setup () -> captured.Value <- Some setup) (fun t -> t.span [||] [||])

    let mounted = mount graph (host ()) (Component.render capturing ())

    let rejected =
        ServerChecks.failure (fun () -> captured.Value.Value.CreateId () |> ignore)

    mounted.Dispose ()
    equal true (rejected :? InvalidOperationException)

let private strictOptions: Hydration.HydrationOptions =
    { IdPrefix = "r"; Strict = true }

let private lenientOptions: Hydration.HydrationOptions =
    { IdPrefix = "r"; Strict = false }

let private omit =
    { Server.Options.defaults with
        Policy = Omit
    }

/// <summary>A detached host holding the server HTML of <c>definition</c> for <c>props</c>.</summary>
let private serve (definition: Component<'props>) (props: 'props) : HTMLElement =
    let host = host ()
    loadServed host (Server.render omit definition props)
    host

let private hydrate graph options (host: HTMLElement) (definition: Component<'props>) (props: 'props) =
    Hydration.hydrate graph None strict options host definition props

/// <summary>Every node under <c>root</c>, in document order.</summary>
[<Emit("(() => { const w = document.createTreeWalker($0, 0xFFFFFFFF); const a = []; while (w.nextNode()) a.push(w.currentNode); return a; })()")>]
let private nodesOf (root: Node) : Node array = jsNative

[<Emit("Array.from($0.querySelectorAll($1))")>]
let private all (root: Node) (selector: string) : Node array = jsNative

let private sameNodes (before: Node array) (after: Node array) =
    before.Length = after.Length
    && Array.forall2 (fun (left: Node) (right: Node) -> obj.ReferenceEquals (left, right)) before after

/// <summary>Records mutations under <c>root</c>; the returned function stops and returns their count.</summary>
[<Emit("(() => { const o = new MutationObserver(() => {}); o.observe($0, { subtree: true, childList: true, attributes: true, characterData: true }); return () => { const n = o.takeRecords().length; o.disconnect(); return n; }; })()")>]
let private recordMutations (root: Node) : unit -> int = jsNative

[<Emit("$0.dispatchEvent(new FocusEvent('focus'))")>]
let private focus (node: Node) : unit = jsNative

[<Emit("$0.remove()")>]
let private detach (node: Node) : unit = jsNative

[<Emit("$0.setSelectionRange($1, $2)")>]
let private select (node: Node) (start: int) (finish: int) : unit = jsNative

[<Emit("[$0.selectionStart, $0.selectionEnd]")>]
let private selection (node: Node) : int array = jsNative

[<Emit("$0.textContent = $1")>]
let private setText (node: obj) (text: string) : unit = jsNative

let private element (host: HTMLElement) (selector: string) =
    host.querySelector selector |> unbox<HTMLElement>

let private input (host: HTMLElement) (selector: string) =
    host.querySelector selector
    |> unbox<HTMLInputElement>

type private CardState =
    {
        Title: Signal<string>
        Clicks: Signal<int>
    }

let private card =
    Component.define
        (fun _ (title: string) ->
            {
                Title = Api.createSignal title
                Clicks = Api.createSignal 0
            })
        (fun t ->
            t.div
                [| t.Attrs.title (t.from (fun r -> r.Title)) |]
                [|
                    t.text "Title: "
                    t.text (t.from (fun r -> r.Title))
                    t.button [| t.Events.onClick (fun r _ -> r.Clicks.Value <- r.Clicks.Value + 1) |] [| t.text (fun r -> string r.Clicks.Value) |]
                    t.input
                        [|
                            t.Events.on ("focus", fun r _ -> r.Clicks.Value <- r.Clicks.Value + 10)
                            t.Attrs.Input.value (fun r -> r.Title.Value)
                        |]
                |])

/// <summary>The title attribute, the text and the input value of a hydrated <c>card</c>.</summary>
let private cardShown (host: HTMLElement) =
    (element host "div").getAttribute "title", (element host "div").textContent, (input host "input").value

let private adoptsEveryNode () =
    use graph = new Graph ()
    let host = serve card "a"
    equal """<!--[r--><div title="a">Title: <!--!-->a<button>0</button><input value="a"></div><!--]-->""" host.innerHTML
    let before = nodesOf host
    let mounted = hydrate graph strictOptions host card "a"
    equal (true, (Some "a", "Title: a0", "a")) (sameNodes before (nodesOf host), cardShown host)
    mounted.Dispose ()

let private updatesAndDisposes () =
    use graph = new Graph ()
    let host = serve card "a"
    let mounted = hydrate graph strictOptions host card "a"
    let button = element host "button"
    button.click ()
    equal "1" button.textContent
    focus (unbox<Node>(host.querySelector "input"))
    equal "11" button.textContent
    mounted.Dispose ()
    equal 0. host.childNodes.length
    button.click ()
    equal "11" button.textContent

let private matchingValuesWriteNothing () =
    use graph = new Graph ()
    let host = serve card "a"
    let mutations = recordMutations host
    let mounted = hydrate graph strictOptions host card "a"
    equal (0, (Some "a", "Title: a0", "a")) (mutations (), cardShown host)
    mounted.Dispose ()

let private differingValuesCorrected () =
    use graph = new Graph ()
    let host = serve card "a"
    let mounted = hydrate graph strictOptions host card "b"
    equal (Some "b", "Title: b0", "b") (cardShown host)
    equal (Some "b") ((input host "input").getAttribute "value")
    mounted.Dispose ()

let private appending =
    Component.define (fun _ (text: string) -> Api.createSignal text) (fun t ->
        t.span
            [|
                t.Events.onClick (fun (text: Signal<string>) _ -> text.Value <- text.Value + "x")
            |]
            [| t.text (fun (text: Signal<string>) -> text.Value) |])

let private emptyTextHydrates () =
    use graph = new Graph ()
    let host = serve appending ""
    equal "<!--[r--><span><!--_--></span><!--]-->" host.innerHTML
    let mounted = hydrate graph strictOptions host appending ""
    let span = element host "span"
    equal (1., 3.) (span.childNodes.length, span.firstChild.Value.nodeType)
    span.click ()
    equal "x" span.textContent
    mounted.Dispose ()

let private shapes () =
    let check (definition: Component<'props>) (props: 'props) (text: string) =
        use graph = new Graph ()
        let host = serve definition props
        let before = nodesOf host
        let mounted = hydrate graph strictOptions host definition props
        equal (true, text) (sameNodes before (nodesOf host), host.textContent)
        mounted.Dispose ()
        equal 0. host.childNodes.length

    check (Component.define (fun _ () -> ()) (fun t -> t.fragment [||])) () ""
    check textual "one" "one"
    check pair greetingProps "Hello <Ada>!<Ada>"
    check (Component.define (fun _ () -> ()) (fun t -> t.fragment [| t.text "a"; t.span [||] [| t.text "b" |] |])) () "ab"

let private regionsMountWithContent () =
    use graph = new Graph ()
    let host = serve listed listedItems
    let list = element host "ul"
    let markers = list.firstChild.Value, list.lastChild.Value
    let served = all host "li"
    let mounted = hydrate graph strictOptions host listed listedItems

    equal
        (true, true, true, "a<b>", true)
        (obj.ReferenceEquals (list, element host "ul"),
         obj.ReferenceEquals (fst markers, list.firstChild.Value),
         obj.ReferenceEquals (snd markers, list.lastChild.Value),
         list.textContent,
         sameNodes served (all host "li"))

    mounted.Dispose ()

let private afterRegions =
    Component.define (fun _ (items: Listed array) -> items) (fun t ->
        t.fragment
            [|
                t.each (fun (item: Listed) -> item.Id) id listedRow
                t.ul
                    [||]
                    [|
                        t.each (fun (item: Listed) -> item.Id) id listedRow
                        t.li [| t.Attrs.title (fun (items: Listed array) -> Some (string items.Length)) |] [| t.text "last" |]
                    |]
            |])

let private nodesAfterRegions () =
    use graph = new Graph ()
    let host = serve afterRegions listedItems
    let list = element host "ul"
    let last = element host "li[title]"
    let mounted = hydrate graph strictOptions host afterRegions listedItems

    equal
        (true, true, Some "2", "a<b>a<b>last", 5)
        (obj.ReferenceEquals (list, element host "ul"),
         obj.ReferenceEquals (last, element host "li[title]"),
         last.getAttribute "title",
         host.textContent,
         (all host "li").Length)

    mounted.Dispose ()
    equal 0. host.childNodes.length

let private queuedRaises () =
    use graph = new Graph ()
    let host = serve card "a"

    let error =
        ServerChecks.failure (fun () ->
            Hydration.hydrateWith (Scheduling.QueuedCommits (Scheduling.CollectAndContinue, ignore)) graph None strict strictOptions host card "a"
            |> ignore)

    equal (true, "Hydration supports synchronous commits only") (error :? NotSupportedException, error.Message)

let private foreignSiblings () =
    use graph = new Graph ()
    let host = host ()

    loadServed
        host
        ("\n  <p>before</p>"
         + Server.render omit card "a"
         + "<script type=\"application/json\">{}</script>\n")

    let paragraph, script = host.querySelector "p", host.querySelector "script"
    let mounted = hydrate graph strictOptions host card "a"
    (element host "button").click()
    equal "Title: a1" (element host "div").textContent
    mounted.Dispose ()

    equal
        (true, true, true, "\n  before{}\n")
        (obj.ReferenceEquals (paragraph, host.querySelector "p"),
         obj.ReferenceEquals (script, host.querySelector "script"),
         isNull (box (host.querySelector "div")),
         host.textContent)

let private carriageReturn () =
    use graph = new Graph ()
    let host = serve card "a\r\nb"
    let mutations = recordMutations host
    let mounted = hydrate graph strictOptions host card "a\r\nb"
    // A text input's value drops line breaks; its value attribute keeps them.
    equal (0, (Some "a\r\nb", "Title: a\r\nb0", "ab"), Some "a\r\nb") (mutations (), cardShown host, (input host "input").getAttribute "value")
    mounted.Dispose ()

let private otherRoot =
    Component.define (fun _ (title: string) -> title) (fun t -> t.span [||] [| t.text (fun title -> title) |])

let private singleMismatch (reports: ResizeArray<exn list>) =
    match List.ofSeq reports with
    | [ [ TemplateDom.HydrationMismatchException _ ] ] -> true
    | _ -> false

let private rootMismatch () =
    use graph = new Graph ()
    let host = serve card "a"
    let before = host.innerHTML

    let strictOutcome =
        try
            hydrate graph strictOptions host otherRoot "a"
            |> ignore

            None
        with TemplateDom.HydrationMismatchException (_, expected, found) ->
            Some (expected, found)

    equal (Some ("<span>", "div"), before) (strictOutcome, host.innerHTML)
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add lenientOptions host otherRoot "a"

    equal (true, "a", true) (singleMismatch reports, host.textContent, isNull (box (host.querySelector "div")))
    mounted.Dispose ()
    equal true (isNull (box (host.querySelector "span")))

let private linkCard =
    Component.define (fun _ (title: string) -> Api.createSignal title) (fun t ->
        t.div
            [| t.Attrs.title (t.from id) |]
            [|
                t.text "Title: "
                t.text (t.from id)
                t.a [| t.Events.onClick (fun _ _ -> ()) |] [| t.text "0" |]
                t.input [||]
            |])

let private nestedMismatchNamesNodes () =
    use graph = new Graph ()
    let host = serve card "a"

    let outcome =
        try
            hydrate graph strictOptions host linkCard "a"
            |> ignore

            None
        with TemplateDom.HydrationMismatchException (_, expected, found) ->
            Some (expected, found)

    equal (Some ("<a>", "button")) outcome

let private missingRootMarker () =
    use graph = new Graph ()
    let host = host ()
    host.innerHTML <- "<div>plain</div>"

    let strictPath =
        try
            hydrate graph strictOptions host card "a"
            |> ignore

            None
        with TemplateDom.HydrationMismatchException (path, _, _) ->
            Some path

    equal (Some "root", "<div>plain</div>") (strictPath, host.innerHTML)
    let reports = ResizeArray<exn list>()
    let mounted = Hydration.hydrate graph None reports.Add lenientOptions host card "a"
    equal (true, "plainTitle: a0") (singleMismatch reports, host.textContent)
    mounted.Dispose ()
    equal "<div>plain</div>" host.innerHTML

let private setupThrowsDuringHydration () =
    use graph = new Graph ()
    let host = serve card "a"
    let before = host.innerHTML

    let throwing =
        Component.define (fun _ (_: string) -> failwith "boom") (fun t -> t.div [||] [||])

    let message =
        try
            hydrate graph lenientOptions host throwing "a"
            |> ignore

            ""
        with error ->
            error.Message

    equal ("boom", before) (message, host.innerHTML)

let private a = { Id = 1; Text = "a" }
let private b = { Id = 2; Text = "b" }
let private c = { Id = 3; Text = "c" }

let mutable private liveItems: Signal<Listed array> = Unchecked.defaultof<_>

let private live (items: Listed array) =
    let signal = Api.createSignal items
    liveItems <- signal
    signal

let private liveList =
    Component.define (fun _ (items: Listed array) -> live items) (fun t ->
        t.ul
            [||]
            [|
                t.each (fun (item: Listed) -> item.Id) (fun (items: Signal<Listed array>) -> items.Value) listedRow
            |])

let private listsHydrate () =
    use graph = new Graph ()
    let host = serve liveList [| a; b |]
    let before = nodesOf host
    let mutations = recordMutations host
    let mounted = hydrate graph strictOptions host liveList [| a; b |]
    equal (0, true, "ab") (mutations (), sameNodes before (nodesOf host), (element host "ul").textContent)
    let first, second = (all host "li")[0], (all host "li")[1]
    graph.Run (fun () -> liveItems.Value <- [| b; a |])
    equal ("ba", true) ((element host "ul").textContent, sameNodes [| second; first |] (all host "li"))
    graph.Run (fun () -> liveItems.Value <- [| b; a; c |])
    equal ("bac", true) ((element host "ul").textContent, sameNodes [| second; first |] ((all host "li")[0..1]))
    graph.Run (fun () -> liveItems.Value <- [| b; c |])
    equal ("bc", true) ((element host "ul").textContent, obj.ReferenceEquals (second, (all host "li")[0]))
    graph.Run (fun () -> liveItems.Value <- [||])
    equal "<!--[--><!--]-->" (element host "ul").innerHTML
    mounted.Dispose ()

let private innerCell =
    Component.define (fun _ (text: unit -> string) -> text) (fun t -> t.element (HtmlTag "b") [||] [| t.text (fun text -> text ()) |])

let private nestedRow =
    Component.define (fun _ (item: unit -> Listed) -> item) (fun t ->
        t.li
            [||]
            [|
                t.each id (fun (item: unit -> Listed) -> [| (item ()).Text + "1"; (item ()).Text + "2" |]) innerCell
            |])

let private nestedList =
    Component.define (fun _ (items: Listed array) -> live items) (fun t ->
        t.ul
            [||]
            [|
                t.each (fun (item: Listed) -> item.Id) (fun (items: Signal<Listed array>) -> items.Value) nestedRow
            |])

let private nestedRegions () =
    use graph = new Graph ()
    let host = serve nestedList [| a; b |]
    let before = nodesOf host
    let mounted = hydrate graph strictOptions host nestedList [| a; b |]
    equal (true, "a1a2b1b2") (sameNodes before (nodesOf host), (element host "ul").textContent)
    graph.Run (fun () -> liveItems.Value <- [| { a with Text = "z" }; b |])
    equal "z1z2b1b2" (element host "ul").textContent
    mounted.Dispose ()

let mutable private liveTexts: Signal<string array> = Unchecked.defaultof<_>

let private liveStrings (items: string array) =
    let signal = Api.createSignal items
    liveTexts <- signal
    signal

let private textList =
    Component.define (fun _ (items: string array) -> liveStrings items) (fun t ->
        t.div [||] [| t.each id (fun (items: Signal<string array>) -> items.Value) textRow |])

[<Emit("$0.childNodes[$1]")>]
let private childAt (parent: Node) (index: int) : Node = jsNative

let private textRowSeparators () =
    use graph = new Graph ()
    let host = serve textList [| "a"; "b" |]
    equal "<!--[r--><div><!--[-->a<!--!-->b<!--]--></div><!--]-->" host.innerHTML
    let list = element host "div"
    let texts = [| childAt list 1; childAt list 3 |]
    let mounted = hydrate graph strictOptions host textList [| "a"; "b" |]

    equal ("<!--[-->ab<!--]-->", 4., true) (list.innerHTML, list.childNodes.length, sameNodes texts [| childAt list 1; childAt list 2 |])

    graph.Run (fun () -> liveTexts.Value <- [| "b"; "a"; "c" |])
    equal ("bac", true) (list.textContent, sameNodes [| texts[1]; texts[0] |] [| childAt list 1; childAt list 2 |])
    mounted.Dispose ()

    use graph = new Graph ()
    let host = serve textList [| "a"; "b" |]
    let nodes = nodesOf host

    let error =
        ServerChecks.failure (fun () ->
            hydrate graph strictOptions host textList [| "a"; "b"; "c" |]
            |> ignore)

    equal
        ("Hydration mismatch at #0: expected a text node, found <!--]-->", "<!--[r--><div><!--[-->a<!--!-->b<!--]--></div><!--]-->", true)
        (error.Message, host.innerHTML, sameNodes nodes (nodesOf host))

let private emptyList =
    Component.define (fun _ (items: string array) -> liveStrings items) (fun t ->
        t.div
            [||]
            [|
                t.each id (fun (items: Signal<string array>) -> items.Value) emptyRow
                t.span [||] [| t.text "after" |]
            |])

let private zeroRootRows () =
    use graph = new Graph ()
    let host = serve emptyList [| "a"; "b" |]
    let before = nodesOf host
    let after = element host "span"
    let mounted = hydrate graph strictOptions host emptyList [| "a"; "b" |]
    equal true (sameNodes before (nodesOf host))
    graph.Run (fun () -> liveTexts.Value <- [| "a"; "b"; "c" |])
    let list = element host "div"
    equal (6., "after", true) (list.childNodes.length, list.textContent, obj.ReferenceEquals (after, list.lastChild.Value))
    graph.Run (fun () -> liveTexts.Value <- [| "b"; "c" |])

    equal
        (5., true, "after", true)
        (list.childNodes.length, obj.ReferenceEquals (before[4], childAt list 1), list.textContent, obj.ReferenceEquals (after, list.lastChild.Value))

    mounted.Dispose ()

type private TrackedRow = { Item: unit -> Listed; Id: string }

let private rowCleanups = ResizeArray<string>()

let private trackedRow =
    Component.define
        (fun setup (item: unit -> Listed) ->
            let text = (item ()).Text
            setup.OnCleanup (fun () -> rowCleanups.Add text)
            { Item = item; Id = setup.CreateId () })
        (fun t -> t.li [| t.Attrs.id (fun (row: TrackedRow) -> Some row.Id) |] [| t.text (fun row -> (row.Item ()).Text) |])

let private trackedList =
    Component.define (fun _ (items: Listed array) -> live items) (fun t ->
        t.ul
            [||]
            [|
                t.each (fun (item: Listed) -> item.Id) (fun (items: Signal<Listed array>) -> items.Value) trackedRow
            |])

/// <summary>A live list of characters; a character's cell setup raises <c>"cell d"</c> for <c>"d"</c>.</summary>
let private failingCharacters =
    let cell =
        Component.define
            (fun _ (text: unit -> string) ->
                if text () = "d" then
                    failwith "cell d"

                text)
            (fun t -> t.element (HtmlTag "b") [||] [| t.text (fun text -> text ()) |])

    Component.define (fun _ (items: string array) -> liveStrings items) (fun t ->
        t.div [||] [| t.each id (fun (items: Signal<string array>) -> items.Value) cell |])

let private rowCountMismatch () =
    for serverItems, clientItems in [ [| a; b |], [| a; b; c |]; [| a; b; c |], [| a |] ] do
        use graph = new Graph ()
        let host = serve liveList serverItems
        let list = element host "ul"
        let start = list.firstChild.Value
        let reports = ResizeArray<exn list>()

        let mounted =
            Hydration.hydrate graph None reports.Add lenientOptions host liveList clientItems

        let expected = String.concat "" [ for item in clientItems -> item.Text ]

        equal
            (true, expected, true, true)
            (singleMismatch reports,
             list.textContent,
             obj.ReferenceEquals (list, element host "ul"),
             obj.ReferenceEquals (start, list.firstChild.Value))

        graph.Run (fun () -> liveItems.Value <- [| c; a |])
        equal "ca" list.textContent
        mounted.Dispose ()

    // A later failing pass of the recovered list is reported as after a mount.
    use graph = new Graph ()
    let host = serve failingCharacters [| "a"; "b" |]
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add lenientOptions host failingCharacters [| "a"; "b"; "c" |]

    graph.Run (fun () -> liveTexts.Value <- [| "a"; "b"; "c"; "d" |])

    equal ([ [ "Hydration mismatch at #0: expected <b>, found <!--]-->" ]; [ "cell d" ] ], "abc") (messages reports, (element host "div").textContent)

    mounted.Dispose ()

    // Recovered rows continue the row counter after the hydrating pass's rows 0 to 2.
    use graph = new Graph ()
    let host = serve trackedList [| a; b |]
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add lenientOptions host trackedList [| a; b; c |]

    equal (true, [ "r-0.r0.3-0"; "r-0.r0.4-0"; "r-0.r0.5-0" ], "abc") (singleMismatch reports, domIds host, (element host "ul").textContent)

    mounted.Dispose ()

    use graph = new Graph ()
    let host = serve liveList [| a; b |]
    let before = host.innerHTML
    let nodes = nodesOf host

    let raised =
        try
            hydrate graph strictOptions host liveList [| a |]
            |> ignore

            None
        with TemplateDom.HydrationMismatchException _ as error ->
            Some error.Message

    equal (Some "Hydration mismatch at #1: expected <!--]-->, found li", before, true) (raised, host.innerHTML, sameNodes nodes (nodesOf host))

let private regionFirstRow =
    Component.define (fun _ (item: unit -> Listed) -> item) (fun t ->
        t.fragment
            [|
                t.each id (fun (item: unit -> Listed) -> [| (item ()).Text |]) innerCell
                t.span [||] [| t.text "end" |]
            |])

let private regionFirstList =
    Component.define (fun _ (items: Listed array) -> live items) (fun t ->
        t.div
            [||]
            [|
                t.each (fun (item: Listed) -> item.Id) (fun (items: Signal<Listed array>) -> items.Value) regionFirstRow
            |])

let private rowStartsWithRegion () =
    use graph = new Graph ()
    let host = serve regionFirstList [| a; b |]

    equal
        "<!--[r--><div><!--[--><!--[--><b>a</b><!--]--><span>end</span><!--[--><b>b</b><!--]--><span>end</span><!--]--></div><!--]-->"
        host.innerHTML

    let before = nodesOf host
    let spans = all host "span"
    let mounted = hydrate graph strictOptions host regionFirstList [| a; b |]
    equal true (sameNodes before (nodesOf host))
    graph.Run (fun () -> liveItems.Value <- [| b; a |])
    equal ("bendaend", true) ((element host "div").textContent, sameNodes [| spans[1]; spans[0] |] (all host "span"))
    mounted.Dispose ()

let private reorderedItems () =
    use graph = new Graph ()
    let host = serve liveList [| a; b |]
    let serverRows = all host "li"
    let mounted = hydrate graph strictOptions host liveList [| b; a |]
    equal ("ba", true) ((element host "ul").textContent, sameNodes serverRows (all host "li"))
    graph.Run (fun () -> liveItems.Value <- [| a; b |])
    let rows = all host "li"

    equal
        ("ab", true, true)
        ((element host "ul").textContent, obj.ReferenceEquals (rows[0], serverRows[1]), obj.ReferenceEquals (rows[1], serverRows[0]))

    mounted.Dispose ()

/// <summary>A stage selector whose setup stores its stage signal in <c>holder</c>.</summary>
let private liveStaged (holder: Signal<Stage> ref) =
    Component.define
        (fun _ (stage: Stage) ->
            let signal = Api.createSignal stage
            holder.Value <- signal
            signal)
        (fun t ->
            t.div
                [||]
                [|
                    t.matchWith (fun (stage: Signal<Stage>) -> stage.Value) (function
                        | Waiting -> t.arm waitingArm ()
                        | Working progress -> t.arm workingArm progress
                        | Complete result -> t.arm completeArm result)
                |])

let private armsHydrate () =
    use graph = new Graph ()
    let stage = ref Unchecked.defaultof<Signal<Stage>>
    let staged = liveStaged stage
    let host = serve staged (Working 3)
    let before = nodesOf host
    let mutations = recordMutations host
    let mounted = hydrate graph strictOptions host staged (Working 3)
    equal (0, true, "working 3") (mutations (), sameNodes before (nodesOf host), (element host "div").textContent)
    let span = host.querySelector "span"
    graph.Run (fun () -> stage.Value.Value <- Working 4)
    equal ("working 4", true) ((element host "div").textContent, obj.ReferenceEquals (span, host.querySelector "span"))
    graph.Run (fun () -> stage.Value.Value <- Complete "x")

    equal ("complete x", 1, false) ((element host "div").textContent, (all host "span").Length, host.contains (unbox<Node> span))

    mounted.Dispose ()
    equal 0. host.childNodes.length

let private armPayloadCorrected () =
    use graph = new Graph ()
    let stage = ref Unchecked.defaultof<Signal<Stage>>
    let staged = liveStaged stage
    let host = serve staged (Working 3)
    let span = host.querySelector "span"
    let mounted = hydrate graph strictOptions host staged (Working 4)
    equal ("working 4", true) ((element host "div").textContent, obj.ReferenceEquals (span, host.querySelector "span"))
    graph.Run (fun () -> stage.Value.Value <- Working 5)
    equal ("working 5", true) ((element host "div").textContent, obj.ReferenceEquals (span, host.querySelector "span"))
    mounted.Dispose ()

let private differentArm () =
    use graph = new Graph ()
    let stage = ref Unchecked.defaultof<Signal<Stage>>
    let staged = liveStaged stage
    let host = serve staged (Working 3)
    let div = element host "div"
    let start = div.firstChild.Value
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add lenientOptions host staged Waiting

    let mismatch =
        "Hydration mismatch at #0: expected <!--[a:"
        + structuralHash waitingArm
        + "-->, found <!--[a:"
        + structuralHash workingArm
        + "-->"

    equal
        ([ [ mismatch ] ], "waiting", true, true)
        (messages reports, div.textContent, obj.ReferenceEquals (div, element host "div"), obj.ReferenceEquals (start, div.firstChild.Value))

    graph.Run (fun () -> stage.Value.Value <- Working 5)
    equal ("working 5", 1) (div.textContent, reports.Count)
    mounted.Dispose ()

    let host = serve staged (Working 3)
    let before = host.innerHTML
    let nodes = nodesOf host

    let error =
        ServerChecks.failure (fun () ->
            hydrate graph strictOptions host staged Waiting
            |> ignore)

    equal (mismatch, before, true) (error.Message, host.innerHTML, sameNodes nodes (nodesOf host))

/// <summary>The server HTML of <c>staged</c> at <c>Working 3</c>, with a <c>b</c> element before the arm's end marker.</summary>
let private servedWithExtra (staged: Component<Stage>) =
    let host = serve staged (Working 3)
    let div = element host "div"

    div.insertBefore (globalThis.document.createElement "b", unbox<Node> div.lastChild)
    |> ignore

    host

let private armExtraNode () =
    use graph = new Graph ()
    let stage = ref Unchecked.defaultof<Signal<Stage>>
    let staged = liveStaged stage
    let host = servedWithExtra staged
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add lenientOptions host staged (Working 3)

    let mismatch = "Hydration mismatch at #1: expected <!--]-->, found b"
    equal ([ [ mismatch ] ], "working 3", 0) (messages reports, (element host "div").textContent, (all host "b").Length)
    graph.Run (fun () -> stage.Value.Value <- Waiting)
    equal "waiting" (element host "div").textContent
    mounted.Dispose ()

    let host = servedWithExtra staged
    let before = host.innerHTML
    let nodes = nodesOf host

    let error =
        ServerChecks.failure (fun () ->
            hydrate graph strictOptions host staged (Working 3)
            |> ignore)

    equal (mismatch, before, true) (error.Message, host.innerHTML, sameNodes nodes (nodesOf host))

let private xArm =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.span [||] [| t.text "x" |])

let private yArm =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.span [||] [| t.text "y" |])

let private flagged =
    Component.define (fun _ (flag: bool) -> flag) (fun t ->
        t.div
            [||]
            [|
                t.branch (fun (flag: bool) -> flag) (fun flag -> if flag then t.arm xArm () else t.arm yArm ())
            |])

let private equalShapes () =
    use graph = new Graph ()
    let host = serve flagged true
    let served = host.querySelector "span"
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add lenientOptions host flagged false

    equal
        (true, "y", false, 1)
        (singleMismatch reports, (element host "div").textContent, obj.ReferenceEquals (served, host.querySelector "span"), (all host "span").Length)

    mounted.Dispose ()

let private tableArm =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.table [||] [| t.tr [||] [| t.td [||] [| t.text "row" |] |] |])

let private rejectable =
    Component.define (fun _ (table: bool) -> table) (fun t ->
        t.div
            [||]
            [|
                t.branch (fun (table: bool) -> table) (fun table -> if table then t.arm tableArm () else t.arm xArm ())
            |])

let private rejectedArmHashMismatches () =
    use graph = new Graph ()
    let host = serve rejectable false
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add lenientOptions host rejectable true

    equal (true, "row", 0, 1) (singleMismatch reports, (element host "div").textContent, (all host "span").Length, (all host "td").Length)

    mounted.Dispose ()

    let host = serve rejectable false
    let before = host.innerHTML
    let nodes = nodesOf host

    let error =
        ServerChecks.failure (fun () ->
            hydrate graph strictOptions host rejectable true
            |> ignore)

    equal
        ("Hydration mismatch at #0: expected an arm the server renders (The HTML parser restructures this markup (table-row) at table/tr), found <!--[a:"
         + structuralHash xArm
         + "-->",
         before,
         true)
        (error.Message, host.innerHTML, sameNodes nodes (nodesOf host))

let private armRow =
    Component.define (fun _ (item: unit -> Listed) -> item) (fun t ->
        t.li
            [||]
            [|
                t.branch (fun (item: unit -> Listed) -> (item ()).Text = "x") (fun picked -> if picked then t.arm xArm () else t.arm yArm ())
            |])

let private armList =
    Component.define (fun _ (items: Listed array) -> live items) (fun t ->
        t.ul
            [||]
            [|
                t.each (fun (item: Listed) -> item.Id) (fun (items: Signal<Listed array>) -> items.Value) armRow
            |])

let private armsInRows () =
    use graph = new Graph ()
    let host = serve armList [| a; b |]
    let before = nodesOf host
    let mounted = hydrate graph strictOptions host armList [| a; b |]
    equal (true, "yy") (sameNodes before (nodesOf host), (element host "ul").textContent)
    mounted.Dispose ()

    use graph = new Graph ()
    let host = serve armList [| a; b |]
    let rows = all host "li"
    let spans = all host "span"
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add lenientOptions host armList [| { a with Text = "x" }; b |]

    equal
        (true, "xy", true, false, true)
        (singleMismatch reports,
         (element host "ul").textContent,
         sameNodes rows (all host "li"),
         obj.ReferenceEquals (spans[0], (all host "span")[0]),
         obj.ReferenceEquals (spans[1], (all host "span")[1]))

    graph.Run (fun () -> liveItems.Value <- [| b; { a with Text = "y" } |])
    equal ("yy", true) ((element host "ul").textContent, sameNodes [| rows[1]; rows[0] |] (all host "li"))
    mounted.Dispose ()

    use graph = new Graph ()
    let host = serve armList [| a; b |]
    let before = host.innerHTML
    let nodes = nodesOf host

    let error =
        ServerChecks.failure (fun () ->
            hydrate graph strictOptions host armList [| { a with Text = "x" }; b |]
            |> ignore)

    equal (true, before, true) (error :? TemplateDom.HydrationMismatchException, host.innerHTML, sameNodes nodes (nodesOf host))

let private listArm =
    Component.define (fun _ (items: unit -> Listed array) -> live (items ())) (fun t ->
        t.ul
            [||]
            [|
                t.each (fun (item: Listed) -> item.Id) (fun (items: Signal<Listed array>) -> items.Value) listedRow
            |])

let private armedList =
    Component.define (fun _ (items: Listed array) -> items) (fun t ->
        t.div
            [||]
            [|
                t.matchWith (fun (items: Listed array) -> items) (fun items -> t.arm listArm items)
            |])

let private rowsInArms () =
    use graph = new Graph ()
    let host = serve armedList [| a; b |]
    let before = nodesOf host
    let mounted = hydrate graph strictOptions host armedList [| a; b |]
    equal (true, "ab") (sameNodes before (nodesOf host), (element host "ul").textContent)
    let rows = all host "li"
    graph.Run (fun () -> liveItems.Value <- [| b; a |])
    equal ("ba", true) ((element host "ul").textContent, sameNodes [| rows[1]; rows[0] |] (all host "li"))
    mounted.Dispose ()

    use graph = new Graph ()
    let host = serve armedList [| a; b |]
    let list = element host "ul"
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add lenientOptions host armedList [| a; b; c |]

    equal (true, "abc", true) (singleMismatch reports, list.textContent, obj.ReferenceEquals (list, element host "ul"))
    graph.Run (fun () -> liveItems.Value <- [| c; a |])
    equal ("ca", 1) (list.textContent, reports.Count)
    mounted.Dispose ()

    use graph = new Graph ()
    let host = serve armedList [| a; b |]
    let before = host.innerHTML
    let nodes = nodesOf host

    let error =
        ServerChecks.failure (fun () ->
            hydrate graph strictOptions host armedList [| a; b; c |]
            |> ignore)

    equal ("Hydration mismatch at #0: expected <li>, found <!--]-->", before, true) (error.Message, host.innerHTML, sameNodes nodes (nodesOf host))

let private armLog = ResizeArray<string>()

/// <summary>An arm whose setup and cleanup are logged as <c>name</c>.</summary>
let private loggedArm (name: string) =
    Component.define
        (fun setup (_: unit -> unit) ->
            armLog.Add ("setup " + name)
            setup.OnCleanup (fun () -> armLog.Add ("cleanup " + name)))
        (fun t -> t.span [||] [| t.text name |])

let private pArm = loggedArm "p"
let private qArm = loggedArm "q"

let private armSwitchCleanups () =
    use graph = new Graph ()
    let holder = ref Unchecked.defaultof<Signal<bool>>

    let tracked =
        Component.define
            (fun _ (first: bool) ->
                holder.Value <- Api.createSignal first
                holder.Value)
            (fun t ->
                t.div
                    [||]
                    [|
                        t.branch (fun (first: Signal<bool>) -> first.Value) (fun first -> if first then t.arm pArm () else t.arm qArm ())
                    |])

    let host = serve tracked true
    armLog.Clear ()
    let served = host.querySelector "span"
    let mounted = hydrate graph strictOptions host tracked true

    let hydrated =
        List.ofSeq armLog, (element host "div").textContent, obj.ReferenceEquals (served, host.querySelector "span")

    armLog.Clear ()
    graph.Run (fun () -> holder.Value.Value <- false)

    let toQ =
        List.ofSeq armLog, (element host "div").textContent, host.contains (unbox<Node> served)

    armLog.Clear ()
    graph.Run (fun () -> holder.Value.Value <- true)
    let toP = List.ofSeq armLog, (element host "div").textContent
    armLog.Clear ()
    mounted.Dispose ()

    equal
        (([ "setup p" ], "p", true), ([ "cleanup p"; "setup q" ], "q", false), ([ "cleanup q"; "setup p" ], "p"), [ "cleanup p" ])
        (hydrated, toQ, toP, List.ofSeq armLog)

let private viewHolesRerender () =
    use graph = new Graph ()

    let server =
        serve
            holed
            {
                Caption = "A"
                Body = Program.element (HtmlTag "em") [||] [| Program.text "server" |]
            }

    let serverEm = server.querySelector "em"
    let div = element server "div"

    let client =
        {
            Caption = "A"
            Body = Program.element (HtmlTag "em") [||] [| Program.text "client" |]
        }

    let mounted = hydrate graph strictOptions server holed client

    equal
        ("Aclient", false, false, true)
        (div.textContent,
         obj.ReferenceEquals (serverEm, server.querySelector "em"),
         server.contains (unbox<Node> serverEm),
         obj.ReferenceEquals (div, element server "div"))

    mounted.Dispose ()

    let host =
        serve
            holed
            {
                Caption = "A"
                Body = Program.element (HtmlTag "em") [||] [| Program.text "server" |]
            }

    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add lenientOptions host holed client

    equal ("Aclient", 0) ((element host "div").textContent, reports.Count)
    mounted.Dispose ()

/// <summary>
/// Requires <c>action</c> to raise <c>message</c> after passing <c>reported</c> to its reporter, with the host's HTML
/// and nodes as served.
/// </summary>
let private failsAsServed (host: HTMLElement) (action: (exn list -> unit) -> unit) (message: string) (reported: string list list) =
    let before = host.innerHTML
    let nodes = nodesOf host
    let reports = ResizeArray<exn list>()
    let error = ServerChecks.failure (fun () -> action reports.Add)
    equal (message, reported, before, true) (error.Message, messages reports, host.innerHTML, sameNodes nodes (nodesOf host))

/// <summary>What a boundary content's setup does with the source it creates.</summary>
type private Outcome =
    | Settles of string
    | Pends
    | Fails

/// <summary>The state of a case's boundary instances.</summary>
type private BoundaryCase =
    {
        /// <summary>What the next content setup does with its source.</summary>
        mutable Outcome: Outcome
        /// <summary>The items of the next content setup's list.</summary>
        mutable Items: string array
        /// <summary>The sources created by the content setups, in setup order.</summary>
        Sources: ResizeArray<AsyncSource<string>>
        /// <summary>The setups of the content and pending views, in order.</summary>
        Log: ResizeArray<string>
    }

let private boundaryCase () =
    {
        Outcome = Pends
        Items = [||]
        Sources = ResizeArray ()
        Log = ResizeArray ()
    }

/// <summary>A source created and logged by a content setup, given the case's outcome.</summary>
let private contentSource (case: BoundaryCase) =
    case.Log.Add "content setup"
    let source = Api.createAsyncSource<string>()
    case.Sources.Add source

    match case.Outcome with
    | Settles text -> source.Settle text
    | Fails -> source.Fail (Exception "failed")
    | Pends -> ()

    source

let private failedMessage =
    Component.define (fun _ (failure: BoundaryFailure) -> failure) (fun t ->
        t.span [||] [| t.text (fun (failure: BoundaryFailure) -> failure.Error.Message) |])

/// <summary>
/// A <c>div</c> holding a boundary: the pending view is a <c>pendingTag</c> element showing "loading", and the content a
/// <c>contentTag</c> element showing its source's value.
/// </summary>
let private boundedIn (case: BoundaryCase) (pendingTag: string) (contentTag: string) =
    let pending =
        Component.define (fun _ () -> case.Log.Add "pending setup") (fun t -> t.element (HtmlTag pendingTag) [||] [| t.text "loading" |])

    let content =
        Component.define (fun _ (_: BoundaryStatus) -> contentSource case) (fun t ->
            t.element (HtmlTag contentTag) [||] [| t.text (fun (source: AsyncSource<string>) -> source.Value) |])

    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary pending failedMessage content |])

let private bounded (case: BoundaryCase) =
    boundedIn case "span" "span"

/// <summary>
/// The server HTML of <c>definition</c> while the case's content setups give <c>server</c>; the case then gives
/// <c>client</c>, with its log and sources cleared.
/// </summary>
let private serveCase (case: BoundaryCase) (definition: Component<unit>) (server: Outcome) (client: Outcome) =
    case.Outcome <- server
    let host = serve definition ()
    case.Outcome <- client
    case.Log.Clear ()
    case.Sources.Clear ()
    host

[<Emit("$0.nodeType === 8 && ($0.data === 'ranvier:start' || $0.data === 'ranvier:end')")>]
let private isRangeComment (node: Node) : bool = jsNative

/// <summary>Every node under <c>root</c> in document order, without the range comments of nested mounts.</summary>
let private serverNodes (root: Node) =
    nodesOf root
    |> Array.filter (isRangeComment >> not)

let private contentHydratesInPlace () =
    use graph = new Graph ()
    let case = boundaryCase ()
    let definition = bounded case
    let host = serveCase case definition (Settles "server") (Settles "server")
    equal "<!--[r--><div><!--[c--><span>server</span><!--]--></div><!--]-->" host.innerHTML
    let before = nodesOf host
    let mutations = recordMutations host
    let mounted = hydrate graph strictOptions host definition ()

    // The content's nested mount inserts its two range comments around the adopted span, and writes nothing else.
    equal
        (2, true, "<!--[r--><div><!--[c--><!--ranvier:start--><span>server</span><!--ranvier:end--><!--]--></div><!--]-->", [ "content setup" ])
        (mutations (), sameNodes before (serverNodes host), host.innerHTML, List.ofSeq case.Log)

    mounted.Dispose ()
    equal 0. host.childNodes.length

    use graph = new Graph ()
    let host = serveCase case definition (Settles "server") (Settles "client")
    let span = host.querySelector "span"
    let mounted = hydrate graph strictOptions host definition ()
    equal ("client", true) ((element host "div").textContent, obj.ReferenceEquals (span, host.querySelector "span"))
    mounted.Dispose ()

let private serverContentStaysPending () =
    use graph = new Graph ()
    let case = boundaryCase ()
    let definition = bounded case
    let host = serveCase case definition (Settles "server") Pends
    let span = host.querySelector "span"
    let mounted = hydrate graph strictOptions host definition ()

    // The content bound the server's span, which is held detached; a copy of the server's nodes is the pending view.
    let pending =
        (element host "div").innerHTML, obj.ReferenceEquals (span, host.querySelector "span"), List.ofSeq case.Log

    graph.Run (fun () -> case.Sources[0].Settle "client")

    let revealed =
        (element host "div").textContent, obj.ReferenceEquals (span, host.querySelector "span")

    mounted.Dispose ()

    equal (("<!--[c--><span>server</span><!--]-->", false, [ "content setup" ]), ("client", true)) (pending, revealed)

/// <summary>A boundary whose content is a button logging its clicks with its source's value, and its mounted hook.</summary>
let private clickedContent (case: BoundaryCase) =
    let pending =
        Component.define (fun _ () -> case.Log.Add "pending setup") (fun t -> t.i [||] [| t.text "loading" |])

    let content =
        Component.define (fun _ (_: BoundaryStatus) -> contentSource case) (fun t ->
            t.button
                [|
                    t.Events.onClick (fun (source: AsyncSource<string>) _ -> case.Log.Add ("clicked " + source.Value))
                    t.Hooks.mounted (fun _ _ ->
                        case.Log.Add "mounted"
                        ignore)
                |]
                [| t.text (fun (source: AsyncSource<string>) -> source.Value) |])

    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary pending failedMessage content |])

let private pendingContentInert () =
    use graph = new Graph ()
    let case = boundaryCase ()
    let definition = clickedContent case
    let host = serveCase case definition (Settles "server") Pends
    let served = host.querySelector "button"
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add strictOptions host definition ()

    (element host "button").click()
    let pending = (element host "div").innerHTML, List.ofSeq case.Log, messages reports
    graph.Run (fun () -> case.Sources[0].Settle "client")
    (element host "button").click()

    let revealed =
        (element host "div").textContent, obj.ReferenceEquals (served, host.querySelector "button"), List.ofSeq case.Log, messages reports

    mounted.Dispose ()

    equal
        (("<!--[c--><button>server</button><!--]-->", [ "content setup" ], []), ("client", true, [ "content setup"; "mounted"; "clicked client" ], []))
        (pending, revealed)

let private serverPendingHydrates () =
    use graph = new Graph ()
    let case = boundaryCase ()
    let definition = bounded case
    let host = serveCase case definition Pends Pends
    equal "<!--[r--><div><!--[p--><span>loading</span><!--]--></div><!--]-->" host.innerHTML
    let before = nodesOf host
    let loading = host.querySelector "span"
    let mounted = hydrate graph strictOptions host definition ()

    let hydrated =
        sameNodes before (serverNodes host), (element host "div").textContent, List.ofSeq case.Log

    graph.Run (fun () -> case.Sources[0].Settle "client")
    let revealed = (element host "div").textContent, host.contains (unbox<Node> loading)
    mounted.Dispose ()

    equal ((true, "loading", [ "content setup"; "pending setup" ]), ("client", false)) (hydrated, revealed)

    // Content ready on the client replaces the server's pending view at once.
    use graph = new Graph ()
    let host = serveCase case definition Pends (Settles "client")
    let loading = host.querySelector "span"
    let mounted = hydrate graph strictOptions host definition ()

    equal ("client", false, [ "content setup" ]) ((element host "div").textContent, host.contains (unbox<Node> loading), List.ofSeq case.Log)

    mounted.Dispose ()

let private serverErrorRerenders () =
    use graph = new Graph ()
    let case = boundaryCase ()
    let definition = bounded case
    let host = serveCase case definition Fails (Settles "client")
    equal "<!--[r--><div><!--[e--><span>failed</span><!--]--></div><!--]-->" host.innerHTML
    let failed = host.querySelector "span"
    let mounted = hydrate graph strictOptions host definition ()

    equal ("client", false, [ "content setup" ]) ((element host "div").textContent, host.contains (unbox<Node> failed), List.ofSeq case.Log)

    mounted.Dispose ()

let private stateRoundTrip () =
    let json =
        "{\"a\":\"</script><!--\",\"b\":\""
        + string (char 0x2028)
        + string (char 0x2029)
        + "\"}"

    let container = host ()
    attachToBody container
    loadServed container (Server.stateScript "state" json + "<p>after</p>")
    let read = Hydration.readState "state"

    let same =
        read
        |> Option.map (fun text -> JS.JSON.stringify (JS.JSON.parse text) = JS.JSON.stringify (JS.JSON.parse json))

    let shown = (all container "script").Length, (element container "p").textContent
    detach container
    equal ((Some true, None), (1, "after")) ((same, Hydration.readState "absent"), shown)

type private IssuedItems =
    {
        RootId: string
        Live: Signal<int array>
    }

/// <summary>The structure of <c>identified</c> over a live item signal stored in <c>holder</c> by its setup.</summary>
let private liveIdentified (holder: Signal<int array> ref) =
    Component.define
        (fun setup (items: int array) ->
            let id = setup.CreateId ()
            identityLog.Add id
            identityLog.Add (setup.CreateId ())
            holder.Value <- Api.createSignal items
            { RootId = id; Live = holder.Value })
        (fun t ->
            t.div
                [| t.Attrs.id (fun (r: IssuedItems) -> r.RootId) |]
                [|
                    t.render idLeaf ignore
                    t.ul [||] [| t.each id (fun (r: IssuedItems) -> r.Live.Value) idRow |]
                    t.render idLeaf ignore
                    t.matchWith (fun (r: IssuedItems) -> r.Live.Value.Length) (fun count -> t.arm idArm count)
                |])

let private identityContinues () =
    let holder = ref Unchecked.defaultof<Signal<int array>>
    let definition = liveIdentified holder
    let target = host ()
    identityLog.Clear ()

    loadServed target (Server.render { omit with IdPrefix = "a" } definition [| 7; 8 |])

    let served = List.sort (List.ofSeq identityLog), domIds target
    let elements = all target "[id]"
    identityLog.Clear ()
    use graph = new Graph ()
    let options: Hydration.HydrationOptions = { IdPrefix = "a"; Strict = true }
    let mounted = hydrate graph options target definition [| 7; 8 |]

    let hydrated =
        List.sort (List.ofSeq identityLog), domIds target, sameNodes elements (all target "[id]")

    identityLog.Clear ()
    graph.Run (fun () -> holder.Value.Value <- [| 7; 8; 9 |])
    let continued = List.sort (List.ofSeq identityLog), domIds target
    mounted.Dispose ()

    let row (index: int) =
        [
            sprintf "a-0.r0.%d-0" index
            sprintf "a-0.r0.%d.c0-0" index
            sprintf "a-0.r0.%d.r0.0-0" index
        ]

    let shown (rows: int list) =
        [ "a-0-0"; "a-0.c0-0" ]
        @ List.collect row rows
        @ [ "a-0.c1-0"; "a-0.r1.0-0" ]

    equal ((identityIds "a", shown [ 0; 1 ]), (identityIds "a", shown [ 0; 1 ], true), (row 2, shown [ 0; 1; 2 ])) (served, hydrated, continued)

let private boundaryIdsHydrate () =
    let check ready fails =
        idContentReady <- ready
        idContentFails <- fails
        let served = servedIds idBoundary ()
        use graph = new Graph ()
        let host = serve idBoundary ()
        let before = serverNodes host
        let reports = ResizeArray<exn list>()

        let mounted =
            Hydration.hydrate graph None reports.Add strictOptions host idBoundary ()

        let shown = domIds host, sameNodes before (serverNodes host), messages reports
        mounted.Dispose ()
        idContentReady <- true
        idContentFails <- false
        served, shown

    let shown = [ "r-0-0"; "r-0.r0.1-0" ]

    equal
        [
            boundaryContentIds, (boundaryContentIds, true, [])
            shown, (shown, true, [])
            shown, (shown, false, [ [ "failed" ] ])
        ]
        [ check true false; check false false; check true true ]

type private IssuedSource =
    {
        Id: string
        Source: AsyncSource<string>
    }

/// <summary>A boundary whose content shows an id from its setup, and whose error view is a button that retries.</summary>
let private retrying (case: BoundaryCase) =
    let failed =
        Component.define (fun _ (failure: BoundaryFailure) -> failure) (fun t ->
            t.button
                [| t.Events.onClick (fun (failure: BoundaryFailure) _ -> failure.Retry ()) |]
                [| t.text (fun (failure: BoundaryFailure) -> failure.Error.Message) |])

    let pending =
        Component.define (fun _ () -> ()) (fun t -> t.i [||] [| t.text "loading" |])

    let content =
        Component.define
            (fun setup (_: BoundaryStatus) ->
                {
                    Id = setup.CreateId ()
                    Source = contentSource case
                })
            (fun t -> t.span [| t.Attrs.id (fun (issued: IssuedSource) -> Some issued.Id) |] [| t.text (fun issued -> issued.Source.Value) |])

    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary pending failed content |])

/// <summary>
/// A boundary whose pending view has <c>pendingRoots</c> roots and whose content has <c>contentRoots</c>: none, or a
/// span and a text. Content without roots reads nothing.
/// </summary>
let private rootsBoundary (case: BoundaryCase) (pendingRoots: int) (contentRoots: int) =
    let pending =
        Component.define (fun _ () -> case.Log.Add "pending setup") (fun t ->
            if pendingRoots = 0 then
                t.fragment [||]
            else
                t.fragment [| t.span [||] [| t.text "loading" |]; t.text "." |])

    let content =
        Component.define (fun _ (_: BoundaryStatus) -> contentSource case) (fun t ->
            if contentRoots = 0 then
                t.fragment [||]
            else
                t.fragment
                    [|
                        t.span [||] [| t.text (fun (source: AsyncSource<string>) -> source.Value) |]
                        t.text "!"
                    |])

    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary pending failedMessage content |])

let private boundaryRootCounts () =
    let check (pendingRoots: int) (contentRoots: int) (server: Outcome) (client: Outcome) =
        use graph = new Graph ()
        let case = boundaryCase ()
        let definition = rootsBoundary case pendingRoots contentRoots
        let host = serveCase case definition server client
        let served = (element host "div").innerHTML
        let before = nodesOf host
        let mounted = hydrate graph strictOptions host definition ()

        let shown =
            served, (element host "div").innerHTML, sameNodes before (serverNodes host), List.ofSeq case.Log

        mounted.Dispose ()
        shown

    equal
        [
            "<!--[c--><!--]-->", "<!--[c--><!--ranvier:start--><!--ranvier:end--><!--]-->", true, [ "content setup" ]
            "<!--[c--><span>x</span>!<!--]-->", "<!--[c--><!--ranvier:start--><span>x</span>!<!--ranvier:end--><!--]-->", true, [ "content setup" ]
            "<!--[p--><!--]-->", "<!--[p--><!--ranvier:start--><!--ranvier:end--><!--]-->", true, [ "content setup"; "pending setup" ]
            "<!--[p--><span>loading</span>.<!--]-->",
            "<!--[p--><!--ranvier:start--><span>loading</span>.<!--ranvier:end--><!--]-->",
            true,
            [ "content setup"; "pending setup" ]
        ]
        [
            check 2 0 (Settles "x") (Settles "x")
            check 2 2 (Settles "x") (Settles "x")
            check 0 2 Pends Pends
            check 2 2 Pends Pends
        ]

let private retryAfterServedError () =
    use graph = new Graph ()
    let case = boundaryCase ()
    let definition = retrying case
    let host = serveCase case definition Fails Fails
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add lenientOptions host definition ()

    let failed = (element host "div").textContent, domIds host, messages reports
    case.Outcome <- Settles "ok"
    (element host "button").click()
    let retried = (element host "div").textContent, domIds host
    mounted.Dispose ()
    // The content takes row 0 and the error view row 1; the retried content takes row 2.
    equal (("failed", [], [ [ "failed" ] ]), ("ok", [ "r-0.r0.2-0" ])) (failed, retried)

let private listedContent (case: BoundaryCase) =
    let content =
        Component.define
            (fun _ (_: BoundaryStatus) ->
                case.Log.Add "content setup"
                Array.copy case.Items)
            (fun t -> t.ul [||] [| t.each id (fun (items: string array) -> items) innerCell |])

    let pending =
        Component.define (fun _ () -> ()) (fun t -> t.i [||] [| t.text "loading" |])

    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary pending failedMessage content |])

let private boundaryMismatches () =
    use graph = new Graph ()
    let case = boundaryCase ()
    let client = boundedIn case "span" "b"
    let mismatch = "Hydration mismatch at #0: expected <b>, found span"

    let serveContent () =
        serveCase case (bounded case) (Settles "x") (Settles "y")

    let host = serveContent ()
    let div = element host "div"
    let reports = ResizeArray<exn list>()

    let mounted = Hydration.hydrate graph None reports.Add lenientOptions host client ()

    equal
        ([ [ mismatch ] ], "y", 0, 1, true, [ "content setup"; "content setup" ])
        (messages reports,
         div.textContent,
         (all host "span").Length,
         (all host "b").Length,
         obj.ReferenceEquals (div, element host "div"),
         List.ofSeq case.Log)

    mounted.Dispose ()

    let host = serveContent ()

    failsAsServed
        host
        (fun report ->
            Hydration.hydrate graph None report strictOptions host client ()
            |> ignore)
        mismatch
        []

    // A node after the adopted content raises at the boundary's end.
    let withExtra () =
        let host = serveCase case (bounded case) (Settles "x") (Settles "x")
        let div = element host "div"

        div.insertBefore (globalThis.document.createElement "b", unbox<Node> div.lastChild)
        |> ignore

        host

    let extra = "Hydration mismatch at #1: expected <!--]-->, found b"
    let host = withExtra ()
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add lenientOptions host (bounded case) ()

    equal ([ [ extra ] ], "x", 0) (messages reports, (element host "div").textContent, (all host "b").Length)
    mounted.Dispose ()

    let host = withExtra ()

    failsAsServed
        host
        (fun report ->
            Hydration.hydrate graph None report strictOptions host (bounded case) ()
            |> ignore)
        extra
        []

    // A server pending view of another structure is re-rendered.
    let host = serveCase case (bounded case) Pends Pends
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add lenientOptions host (boundedIn case "i" "span") ()

    let shown = messages reports, (element host "div").innerHTML
    graph.Run (fun () -> case.Sources[0].Settle "z")
    let revealed = (element host "div").textContent
    mounted.Dispose ()

    equal
        (([ [ "Hydration mismatch at #0: expected <i>, found span" ] ], "<!--[p--><!--ranvier:start--><i>loading</i><!--ranvier:end--><!--]-->"), "z")
        (shown, revealed)

    // A list mismatch inside hydrated content re-renders the list only, and the boundary keeps its content.
    let listed = listedContent case
    case.Items <- [| "a"; "b" |]
    let host = serve listed ()
    let list = element host "ul"
    case.Items <- [| "a"; "b"; "c" |]
    let reports = ResizeArray<exn list>()

    let mounted = Hydration.hydrate graph None reports.Add lenientOptions host listed ()

    equal
        ([ [ "Hydration mismatch at #0: expected <b>, found <!--]-->" ] ], "abc", true)
        (messages reports, (element host "div").textContent, obj.ReferenceEquals (list, element host "ul"))

    mounted.Dispose ()
    case.Items <- [| "a"; "b" |]
    let host = serve listed ()
    case.Items <- [| "a"; "b"; "c" |]

    failsAsServed
        host
        (fun report ->
            Hydration.hydrate graph None report strictOptions host listed ()
            |> ignore)
        "Hydration mismatch at #0: expected <b>, found <!--]-->"
        []

let private italicCell =
    Component.define (fun _ (text: unit -> string) -> text) (fun t -> t.element (HtmlTag "i") [||] [| t.text (fun text -> text ()) |])

let private italicRow =
    Component.define (fun _ (item: unit -> Listed) -> item) (fun t ->
        t.li
            [||]
            [|
                t.each id (fun (item: unit -> Listed) -> [| (item ()).Text + "1"; (item ()).Text + "2" |]) italicCell
            |])

let private italicList =
    Component.define (fun _ (items: Listed array) -> live items) (fun t ->
        t.ul
            [||]
            [|
                t.each (fun (item: Listed) -> item.Id) (fun (items: Signal<Listed array>) -> items.Value) italicRow
            |])

let private nestedRowMismatch () =
    use graph = new Graph ()
    let host = serve nestedList [| a; b |]
    let rows = all host "li"
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add lenientOptions host italicList [| a; b |]

    let mismatch = "Hydration mismatch at #0: expected <i>, found b"

    let shown (order: Node array) =
        (element host "ul").textContent, sameNodes order (all host "li"), (all host "i").Length, (all host "b").Length

    equal ([ [ mismatch ]; [ mismatch ] ], ("a1a2b1b2", true, 4, 0)) (messages reports, shown rows)
    graph.Run (fun () -> liveItems.Value <- [| b; { a with Text = "z" } |])
    equal ("b1b2z1z2", true, 4, 0) (shown [| rows[1]; rows[0] |])
    mounted.Dispose ()

    use graph = new Graph ()
    let host = serve nestedList [| a; b |]
    let before = host.innerHTML
    let nodes = nodesOf host

    let error =
        ServerChecks.failure (fun () ->
            hydrate graph strictOptions host italicList [| a; b |]
            |> ignore)

    equal (mismatch, before, true) (error.Message, host.innerHTML, sameNodes nodes (nodesOf host))

let private adoptedRowsKeepCleanupsAndIds () =
    let served = servedIds trackedList [| a; b |]
    use graph = new Graph ()
    let host = serve trackedList [| a; b |]
    rowCleanups.Clear ()
    let rows = all host "li"
    let mounted = hydrate graph strictOptions host trackedList [| a; b |]
    let hydrated = domIds host, sameNodes rows (all host "li")
    graph.Run (fun () -> liveItems.Value <- [| b; c |])

    let updated =
        domIds host, (element host "ul").textContent, List.ofSeq rowCleanups, obj.ReferenceEquals (rows[1], (all host "li")[0])

    mounted.Dispose ()

    equal
        ((served, true), ([ "r-0.r0.1-0"; "r-0.r0.2-0" ], "bc", [ "a" ], true), [ "a"; "b"; "c" ])
        (hydrated, updated, List.sort (List.ofSeq rowCleanups))

let mutable private holeFails = false

let private failingView =
    Component.define
        (fun _ () ->
            if holeFails then
                failwith "boom")
        (fun t -> t.span [||] [||])

let private holedRow =
    Component.define (fun _ (item: unit -> Listed) -> item) (fun t ->
        t.li
            [||]
            [|
                t.text (fun item -> (item ()).Text)
                t.view (fun _ -> Component.render failingView ())
            |])

let private holedList =
    Component.define (fun _ (items: Listed array) -> live items) (fun t ->
        t.ul
            [||]
            [|
                t.each (fun (item: Listed) -> item.Id) (fun (items: Signal<Listed array>) -> items.Value) holedRow
            |])

let private rowFailureRestores () =
    for options in [ strictOptions; lenientOptions ] do
        use graph = new Graph ()
        let host = serve holedList [| a; b |]
        let before = host.innerHTML
        let nodes = nodesOf host
        holeFails <- true

        let error =
            try
                ServerChecks.failure (fun () ->
                    hydrate graph options host holedList [| a; b |]
                    |> ignore)
            finally
                holeFails <- false

        equal ("boom", before, true) (error.Message, host.innerHTML, sameNodes nodes (nodesOf host))

/// <summary>
/// A list of characters at the root, and a list whose rows hold a list of their item's characters; a character's cell
/// setup raises <c>"cell " + character</c> while <c>fails character</c> holds.
/// </summary>
let private characterLists (fails: string -> bool) =
    let cell =
        Component.define
            (fun _ (text: unit -> string) ->
                if fails (text ()) then
                    failwith ("cell " + text ())

                text)
            (fun t -> t.element (HtmlTag "b") [||] [| t.text (fun text -> text ()) |])

    let row =
        Component.define (fun _ (item: unit -> Listed) -> item) (fun t ->
            t.li
                [||]
                [|
                    t.each id (fun (item: unit -> Listed) -> [| for ch in (item ()).Text -> string ch |]) cell
                |])

    let flat =
        Component.define (fun _ (items: string array) -> items) (fun t -> t.div [||] [| t.each id id cell |])

    let nested =
        Component.define (fun _ (items: Listed array) -> items) (fun t -> t.ul [||] [| t.each (fun (item: Listed) -> item.Id) id row |])

    flat, nested

let private recoveryFailureFails () =
    // The cell "c" raises at its second setup: the fresh mount after the hydrating pass's mismatch.
    let secondSetup () =
        let calls = ref 0

        fun (text: string) ->
            if text = "c" then
                calls.Value <- calls.Value + 1

            text = "c" && calls.Value = 2

    let mismatch = [ [ "Hydration mismatch at #0: expected <b>, found <!--]-->" ] ]
    use graph = new Graph ()
    let flat, _ = characterLists (secondSetup ())
    let host = serve flat [| "a"; "b" |]

    failsAsServed
        host
        (fun report ->
            Hydration.hydrate graph None report lenientOptions host flat [| "a"; "b"; "c" |]
            |> ignore)
        "cell c"
        mismatch

    let _, nested = characterLists (secondSetup ())
    let host = serve nested [| { Id = 1; Text = "ab" } |]

    failsAsServed
        host
        (fun report ->
            Hydration.hydrate graph None report lenientOptions host nested [| { Id = 1; Text = "abc" } |]
            |> ignore)
        "cell c"
        mismatch

    let throwingArm =
        Component.define (fun _ (_: unit -> unit) -> failwith "arm") (fun t -> t.span [||] [| t.text "t" |])

    let picked =
        Component.define (fun _ (thrown: bool) -> thrown) (fun t ->
            t.div
                [||]
                [|
                    t.branch (fun (thrown: bool) -> thrown) (fun thrown -> if thrown then t.arm throwingArm () else t.arm yArm ())
                |])

    let host = serve picked false

    failsAsServed
        host
        (fun report ->
            Hydration.hydrate graph None report lenientOptions host picked true
            |> ignore)
        "arm"
        [
            [
                "Hydration mismatch at #0: expected <!--[a:"
                + structuralHash throwingArm
                + "-->, found <!--[a:"
                + structuralHash yArm
                + "-->"
            ]
        ]

let private deferredSetupFailureFails () =
    for options in [ strictOptions; lenientOptions ] do
        use graph = new Graph ()
        let fails = ref false
        let _, nested = characterLists (fun _ -> fails.Value)
        let host = serve nested [| { Id = 1; Text = "ab" } |]
        fails.Value <- true

        failsAsServed
            host
            (fun report ->
                Hydration.hydrate graph None report options host nested [| { Id = 1; Text = "ab" } |]
                |> ignore)
            "cell a"
            []

let private recoveredMountsAreLive () =
    use graph = new Graph ()
    let host = serve card "a"
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add lenientOptions host appending "a"

    let recovered = messages reports, host.innerHTML
    (element host "span").click()
    let updated = host.innerHTML
    mounted.Dispose ()

    equal
        (([ [ "Hydration mismatch at #0: expected <span>, found div" ] ], "<!--[r--><!--ranvier:start--><span>a</span><!--ranvier:end--><!--]-->"),
         "<!--[r--><!--ranvier:start--><span>ax</span><!--ranvier:end--><!--]-->",
         "")
        (recovered, updated, host.innerHTML)

    let host = CheckSupport.host ()
    host.innerHTML <- "<p>plain</p>"
    let reports = ResizeArray<exn list>()
    let mounted = Hydration.hydrate graph None reports.Add lenientOptions host card "a"
    (element host "button").click()
    let shown = messages reports, cardShown host, host.textContent
    mounted.Dispose ()

    equal
        (([ [ "Hydration mismatch at root: expected <!--[r-->, found absent" ] ], (Some "a", "Title: a1", "a"), "plainTitle: a1"), "<p>plain</p>")
        (shown, host.innerHTML)

let private aggregatedMismatch () =
    let found = TemplateDom.mismatch "#0" "<span>" "div"
    let other = InvalidOperationException "other" :> exn

    let nested =
        AggregateException ("outer", [| other; AggregateException ("inner", [| other; found |]) :> exn |]) :> exn

    let messageOf (error: exn) =
        TemplateDom.findMismatch error
        |> Option.map _.Message

    equal
        [ Some found.Message; Some found.Message; None; None ]
        [
            messageOf found
            messageOf nested
            messageOf other
            messageOf (AggregateException ("outer", [| other |]) :> exn)
        ]

let private recoveredIdsUsePrefix () =
    use graph = new Graph ()
    let host = serve card "a"
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add { lenientOptions with IdPrefix = "s" } host identified [| 7; 8 |]

    let shown = singleMismatch reports, List.sort (domIds host)
    mounted.Dispose ()

    equal
        (true,
         identityIds "s"
         |> List.filter (fun id -> id <> "s-0-1"))
        shown

/// <summary>The server HTML of <c>definition</c> without its <c>n</c>-th <c>&lt;!--]--&gt;</c>, counted from 0.</summary>
let private withoutEnd (n: int) (definition: Component<'props>) (props: 'props) =
    let html = Server.render omit definition props
    let mutable index = html.IndexOf "<!--]-->"

    for _ in 1..n do
        index <- html.IndexOf ("<!--]-->", index + 1)

    html.Remove (index, 8)

let private unclosedRegions () =
    let check (served: string) (client: Component<'props>) (props: 'props) =
        use graph = new Graph ()
        let host = host ()
        loadServed host served
        let html = host.innerHTML
        let before = nodesOf host

        let error =
            ServerChecks.failure (fun () ->
                hydrate graph strictOptions host client props
                |> ignore)

        error.Message, host.innerHTML = html, sameNodes before (nodesOf host)

    let served = Server.render omit afterRegions listedItems
    let truncated = served.Substring (0, served.IndexOf "<!--]-->")

    equal
        [
            "Hydration mismatch at #0: expected <!--]-->, found absent", true, true
            "Hydration mismatch at 2/0: expected <!--]-->, found absent", true, true
            "Hydration mismatch at 0/0: expected <!--]-->, found absent", true, true
            "Hydration mismatch at root: expected <!--]-->, found absent", true, true
        ]
        [
            check truncated afterRegions listedItems
            check (withoutEnd 1 afterRegions listedItems) afterRegions listedItems
            check (withoutEnd 0 listed listedItems) listed listedItems
            check (withoutEnd 0 card "a") card "a"
        ]

    use graph = new Graph ()
    let host = host ()
    loadServed host truncated
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add lenientOptions host afterRegions listedItems

    let shown = messages reports, host.textContent, (all host "li").Length
    mounted.Dispose ()

    equal (([ [ "Hydration mismatch at #0: expected <!--]-->, found absent" ] ], "a<b>a<b>last", 5), "") (shown, host.innerHTML)

let private editable =
    Component.define (fun _ (text: string) -> Api.createSignal text) (fun t ->
        t.div
            [||]
            [|
                t.input [| t.Props.Input.value (fun (text: Signal<string>) -> text.Value) |]
                t.input [| t.Props.Input.value "fixed" |]
                t.button [| t.Events.onClick (fun (text: Signal<string>) _ -> text.Value <- "set") |] [||]
            |])

let private editedInputsKept () =
    use graph = new Graph ()
    let host = serve editable "a"
    attachToBody host
    let inputs = all host "input"

    let live, fixedInput =
        unbox<HTMLInputElement> inputs[0], unbox<HTMLInputElement> inputs[1]

    live.value <- "typed"
    fixedInput.value <- "edited"
    live.focus ()
    select inputs[0] 1 3
    let mounted = hydrate graph strictOptions host editable "a"

    let kept =
        live.value, fixedInput.value, obj.ReferenceEquals (activeElement (), live), List.ofArray (selection inputs[0])

    (element host "button").click()
    let set = live.value
    mounted.Dispose ()
    detach host
    equal (("typed", "edited", true, [ 1; 3 ]), "set") (kept, set)

let private differingInputsCorrected () =
    use graph = new Graph ()
    let host = serve editable "a"
    let mounted = hydrate graph strictOptions host editable "b"
    equal "b" (input host "input").value
    mounted.Dispose ()

type private RawState = { Markup: Signal<string> }

let private rawCard =
    Component.define (fun _ (markup: string) -> { Markup = Api.createSignal markup }) (fun t ->
        t.div
            [||]
            [|
                t.span [| t.Unsafe.html (fun r -> UnsafeHtml.Trust r.Markup.Value) |] [||]
                t.button [| t.Events.onClick (fun r _ -> r.Markup.Value <- "<i>next</i>") |] [||]
            |])

let private rawContentRewritten () =
    use graph = new Graph ()
    let host = serve rawCard "<b>x</b>"
    let content = element host "span"
    let server = host.querySelector "b"
    let mutations = recordMutations content
    let mounted = hydrate graph strictOptions host rawCard "<b>x</b>"

    let rewritten =
        mutations (), content.innerHTML, obj.ReferenceEquals (server, host.querySelector "b")

    (element host "button").click()
    let updated = content.innerHTML
    mounted.Dispose ()
    equal ((1, "<b>x</b>", false), "<i>next</i>") (rewritten, updated)

let private rawHtmlClosesElement () =
    use graph = new Graph ()
    let host = serve rawCard "a</div><div>b"
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add lenientOptions host rawCard "a</div><div>b"

    let recovered =
        singleMismatch reports, (element host "span").innerHTML, host.textContent

    (element host "button").click()
    let updated = (element host "span").innerHTML
    mounted.Dispose ()
    equal ((true, "a<div>b</div>", "ab"), "<i>next</i>") (recovered, updated)

let private placedCard =
    Component.define (fun _ (log: ResizeArray<obj>) -> log) (fun t ->
        t.div
            [||]
            [|
                t.span
                    [|
                        t.Hooks.mounted (fun (log: ResizeArray<obj>) span ->
                            log.Add (box span)
                            ignore)
                    |]
                    [| t.text "s" |]
                t.div
                    [|
                        t.Hooks.widget (fun (log: ResizeArray<obj>) widget ->
                            setText widget "widget"
                            log.Add (box widget)
                            fun () -> log.Add (box "destroyed"))
                    |]
                    [||]
            |])

let private placedOnAdopted () =
    use graph = new Graph ()
    let log = ResizeArray<obj>()
    let host = serve placedCard log

    let root, span, widget =
        host.querySelector "div", host.querySelector "span", host.querySelector ":scope > div > div"

    let served = host.innerHTML
    let mounted = hydrate graph strictOptions host placedCard log
    let hooked = List.ofSeq log

    let placed =
        match hooked with
        | [ first; second ] ->
            obj.ReferenceEquals (first, span), obj.ReferenceEquals (second, widget), obj.ReferenceEquals ((unbox<Node> first).parentNode.Value, root)
        | _ -> false, false, false

    let shown = (element host ":scope > div > div").textContent
    mounted.Dispose ()

    equal
        ("<!--[r--><div><span>s</span><div></div></div><!--]-->", (true, true, true), "widget", box "destroyed")
        (served, placed, shown, Seq.last log)

let private noted =
    Component.define (fun _ (text: string) -> Api.createSignal text) (fun t ->
        t.div
            [||]
            [|
                t.textarea [||] [| t.text (fun (text: Signal<string>) -> text.Value) |]
                t.button [| t.Events.onClick (fun (text: Signal<string>) _ -> text.Value <- "typed") |] [||]
            |])

let private emptyRcdataText () =
    use graph = new Graph ()
    let host = serve noted ""

    let area =
        host.querySelector "textarea"
        |> unbox<HTMLTextAreaElement>

    let served = area.childNodes.length
    let mounted = hydrate graph strictOptions host noted ""
    let adopted = area.childNodes.length, area.firstChild.Value.nodeType, area.value
    (element host "button").click()
    let typed = area.value
    mounted.Dispose ()
    equal (0., (1., 3., ""), "typed") (served, adopted, typed)

let private emptyAndMarked =
    Component.define (fun _ (text: string) -> text) (fun t ->
        t.span
            [||]
            [|
                t.text (fun text -> text)
                t.b [| t.Attrs.title (fun _ -> Some "x") |] [||]
            |])

let private emptyAndOther =
    Component.define (fun _ (text: string) -> text) (fun t ->
        t.span
            [||]
            [|
                t.text (fun text -> text)
                t.i [| t.Attrs.title (fun _ -> Some "x") |] [||]
            |])

let private listedThenMore =
    Component.define (fun _ (items: Listed array) -> items) (fun t ->
        t.fragment
            [|
                t.ul [||] [| t.each (fun (item: Listed) -> item.Id) id listedRow |]
                t.b [||] [||]
            |])

let private mismatchRestores () =
    let check (served: string) (client: Component<'props>) (props: 'props) =
        use graph = new Graph ()
        let host = host ()
        loadServed host served
        let before = nodesOf host

        let outcome =
            match
                ServerChecks.failure (fun () ->
                    hydrate graph strictOptions host client props
                    |> ignore)
            with
            | TemplateDom.HydrationMismatchException (path, expected, found) as error -> Some (path, expected, found, error.Message)
            | _ -> None

        outcome, host.innerHTML = served, sameNodes before (nodesOf host)

    equal
        [
            Some ("0/1", "<i>", "b", "Hydration mismatch at 0/1: expected <i>, found b"), true, true
            Some ("#1", "<!--]-->", "b", "Hydration mismatch at #1: expected <!--]-->, found b"), true, true
            Some ("#0", "a text node", "div", "Hydration mismatch at #0: expected a text node, found div"), true, true
        ]
        [
            check (Server.render omit emptyAndMarked "") emptyAndOther ""
            check (Server.render omit listedThenMore listedItems) listed listedItems
            check (Server.render omit card "a") textual "a"
        ]

let private rootMarkerAbsent () =
    use graph = new Graph ()
    let host = host ()
    host.innerHTML <- "<div>plain</div>"

    match
        ServerChecks.failure (fun () ->
            hydrate graph strictOptions host card "a"
            |> ignore)
    with
    | TemplateDom.HydrationMismatchException (path, expected, found) ->
        equal (("root", "<!--[r-->", "absent"), "<div>plain</div>") ((path, expected, found), host.innerHTML)
    | other -> failwithf "Expected HydrationMismatchException; got %O" other

/// <summary>The server HTML of <c>definition</c> with an extra <c>&lt;b&gt;</c> root before the root end marker.</summary>
let private withTrailingRoot (definition: Component<'props>) (props: 'props) =
    let html = Server.render omit definition props

    html.Substring (0, html.Length - 8)
    + "<b></b><!--]-->"

/// <summary>Runs once, in the setup of the next <c>sabotage</c> instance.</summary>
let mutable private sabotageOnce: unit -> unit = ignore

let private sabotage =
    Component.define
        (fun _ () ->
            let run = sabotageOnce
            sabotageOnce <- ignore
            run ())
        (fun t -> t.b [||] [||])

let private sabotaged =
    Component.define (fun _ (text: string) -> text) (fun t -> t.span [||] [| t.text (fun text -> text); t.render sabotage ignore |])

let private rollbackFailureReported () =
    use graph = new Graph ()
    let host = host ()
    loadServed host (withTrailingRoot sabotaged "")
    // Detaching the text node that replaced the empty-text placeholder makes the placeholder's restore fail.
    sabotageOnce <- fun () -> detach (element host "span").firstChild.Value
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add lenientOptions host sabotaged ""

    let reported =
        [
            for errors in reports ->
                [
                    for error in errors ->
                        match error with
                        | TemplateDom.HydrationMismatchException _ -> error.Message
                        | _ -> "other"
                ]
        ]

    let shown = host.innerHTML
    mounted.Dispose ()

    equal
        ([ [ "Hydration mismatch at #1: expected <!--]-->, found b"; "other" ] ],
         "<!--[r--><!--ranvier:start--><span><b></b></span><!--ranvier:end--><!--]-->")
        (reported, shown)

[<Emit("Array.from($0.childNodes).filter(node => node.nodeType === 8 && node.data === ']')")>]
let private endMarkers (parent: Node) : Node array = jsNative

let private sabotagedRow =
    Component.define
        (fun _ (item: unit -> Listed) ->
            let run = sabotageOnce
            sabotageOnce <- ignore
            run ()
            item)
        (fun t ->
            t.fragment
                [|
                    t.each id (fun (item: unit -> Listed) -> [| (item ()).Text |]) innerCell
                    t.span [||] [| t.text "end" |]
                |])

let private sabotagedList =
    Component.define (fun _ (items: Listed array) -> live items) (fun t ->
        t.div
            [||]
            [|
                t.each (fun (item: Listed) -> item.Id) (fun (items: Signal<Listed array>) -> items.Value) sabotagedRow
            |])

let private unclosedRowRegion () =
    use graph = new Graph ()
    let host = serve sabotagedList [| a; b |]
    let list = element host "div"
    let text = host.textContent
    let ends = endMarkers list

    let kept =
        nodesOf host
        |> Array.filter (fun node ->
            not (
                ends
                |> Array.exists (fun marker -> obj.ReferenceEquals (marker, node))
            ))

    // The re-raise is reachable only through a DOM mutation during hydration.
    // The first row's setup removes every end marker in the list.
    sabotageOnce <- fun () -> Array.iter detach ends

    let error =
        ServerChecks.failure (fun () ->
            hydrate graph strictOptions host sabotagedList [| a; b |]
            |> ignore)

    equal ("Hydration mismatch at #0: expected <!--]-->, found absent", text, true) (error.Message, host.textContent, sameNodes kept (nodesOf host))

/// <summary>An element whose <c>title</c> and <c>id</c> are written when the props are true.</summary>
let private attributed =
    Component.define (fun _ (on: bool) -> on) (fun t ->
        t.span
            [|
                t.Attrs.title (fun (on: bool) -> if on then Some "x" else None)
                t.Attrs.id (fun (on: bool) -> if on then Some "i" else None)
            |]
            [| t.text "s" |])

/// <summary>A template whose only content is <c>definition</c>, inlined inside a <c>div</c>.</summary>
let private inlined (definition: Component<'props>) =
    Component.define (fun _ (props: 'props) -> props) (fun t -> t.div [||] [| t.render definition id |])

/// <summary>The textarea value, the checkbox state and the select's selected index.</summary>
let private formState (host: HTMLElement) =
    (host.querySelector "textarea"
     |> unbox<HTMLTextAreaElement>)
        .value,
    (input host "input").``checked``,
    (host.querySelector "select"
     |> unbox<HTMLSelectElement>)
        .selectedIndex

let private strictRestoresValues () =
    let check (definition: Component<'props>) (served: 'props) (client: 'props) (shown: HTMLElement -> 'shown) =
        use graph = new Graph ()
        let host = host ()
        loadServed host (withTrailingRoot definition served)
        // The parsed form of the served HTML: a pre's leading line feed is dropped by parsing.
        let html = host.innerHTML
        let before = nodesOf host
        let expected = shown host

        let path =
            match
                ServerChecks.failure (fun () ->
                    hydrate graph strictOptions host definition client
                    |> ignore)
            with
            | TemplateDom.HydrationMismatchException (path, _, _) -> path
            | other -> other.Message

        equal ("#1", html, true, expected) (path, host.innerHTML, sameNodes before (nodesOf host), shown host)

    check card "a" "b" cardShown
    check editable "a" "b" (fun host -> (input host "input").value, (all host "input").Length)
    check rawCard "<b>x</b>" "<i>y</i>" (fun host -> (element host "span").innerHTML)

    let served: ServerChecks.FormState = { Text = "x"; On = true; Choice = "b" }
    let client: ServerChecks.FormState = { Text = "y"; On = false; Choice = "a" }
    check ServerChecks.formFixture served client formState
    check (inlined ServerChecks.formFixture) served client formState
    check liveList [| a; b |] [| b; a |] (fun host -> (element host "ul").textContent)
    check armedList [| a; b |] [| b; a |] (fun host -> (element host "ul").textContent)
    check attributed true false (fun host -> (element host "span").getAttribute "title")
    check (inlined attributed) true false (fun host -> (element host "span").getAttribute "id")

let private boundaryRestoresAndRecovers () =
    let case = boundaryCase ()
    let definition = bounded case

    // The server HTML of `definition` with content "server" and a trailing root; the client's content shows "client".
    let trailingHost () =
        case.Outcome <- Settles "server"
        let html = withTrailingRoot definition ()
        case.Outcome <- Settles "client"
        case.Log.Clear ()
        case.Sources.Clear ()
        let target = host ()
        loadServed target html
        target

    let mismatch = "Hydration mismatch at #1: expected <!--]-->, found b"
    use graph = new Graph ()
    let target = trailingHost ()

    failsAsServed
        target
        (fun report ->
            Hydration.hydrate graph None report strictOptions target definition ()
            |> ignore)
        mismatch
        []

    let target = trailingHost ()
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add lenientOptions target definition ()

    let shown =
        messages reports, target.textContent, (all target "span").Length, (all target "b").Length

    mounted.Dispose ()
    equal ([ [ mismatch ] ], "client", 1, 0) shown

    // With content pending on the client, the build raises the trailing root's mismatch before the content is detached.
    let target = trailingHost ()
    case.Outcome <- Pends

    failsAsServed
        target
        (fun report ->
            Hydration.hydrate graph None report strictOptions target definition ()
            |> ignore)
        mismatch
        []

    // A boundary nested in hydrated content keeps its server nodes through a Strict failure.
    let nested =
        let pending =
            Component.define (fun _ () -> ()) (fun t -> t.i [||] [| t.text "loading" |])

        let content =
            Component.define (fun _ (_: BoundaryStatus) -> ()) (fun t -> t.section [||] [| t.render definition (fun _ -> ()) |])

        Component.define (fun _ () -> ()) (fun t -> t.article [||] [| t.boundary pending failedMessage content |])

    for client in [ Settles "client"; Pends ] do
        case.Outcome <- Settles "server"
        let html = withTrailingRoot nested ()
        case.Outcome <- client
        let target = host ()
        loadServed target html

        failsAsServed
            target
            (fun report ->
                Hydration.hydrate graph None report strictOptions target nested ()
                |> ignore)
            mismatch
            []

/// <summary>A <c>bounded</c> boundary followed by a <c>b</c> element whose <c>mounted</c> hook raises "hook".</summary>
let private boundedThenFailingHook (case: BoundaryCase) =
    let inner = bounded case

    Component.define (fun _ () -> ()) (fun t ->
        t.fragment
            [|
                t.render inner (fun _ -> ())
                t.b [| t.Hooks.mounted (fun _ _ -> raise (Exception "hook")) |] [||]
            |])

let private hookFailureRestoresDetachedContent () =
    for options in [ strictOptions; lenientOptions ] do
        use graph = new Graph ()
        let case = boundaryCase ()
        let definition = boundedThenFailingHook case
        let host = serveCase case definition (Settles "server") Pends

        // The content is held detached behind its copy when the hook raises; the host gets the server's nodes back.
        failsAsServed
            host
            (fun report ->
                Hydration.hydrate graph None report options host definition ()
                |> ignore)
            "hook"
            []

/// <summary>A list whose rows each hold a <c>bounded</c> boundary.</summary>
let private boundedRows (case: BoundaryCase) =
    let inner = bounded case

    let row =
        Component.define (fun _ (item: unit -> Listed) -> item) (fun t -> t.li [||] [| t.render inner (fun _ -> ()) |])

    Component.define (fun _ (items: Listed array) -> items) (fun t -> t.ul [||] [| t.each (fun (item: Listed) -> item.Id) id row |])

let private boundariesInRows () =
    use graph = new Graph ()
    let case = boundaryCase ()
    let definition = boundedRows case
    case.Outcome <- Settles "x"
    let host = serve definition [| a; b |]
    let before = nodesOf host
    let mutations = recordMutations host
    let mounted = hydrate graph strictOptions host definition [| a; b |]

    // Each row's content is ready by the end of the build and stays in place: per row, the two range comments only.
    equal (4, true, "xx") (mutations (), sameNodes before (serverNodes host), (element host "ul").textContent)
    mounted.Dispose ()

    // Content pending on the client shows the copies of the server rows until it settles.
    case.Outcome <- Settles "x"
    let host = serve definition [| a; b |]
    let spans = all host "span"
    case.Outcome <- Pends
    case.Sources.Clear ()
    let mounted = hydrate graph strictOptions host definition [| a; b |]
    let pending = (element host "ul").textContent, sameNodes spans (all host "span")
    graph.Run (fun () -> Seq.iter (fun (source: AsyncSource<string>) -> source.Settle "y") case.Sources)
    let revealed = (element host "ul").textContent, sameNodes spans (all host "span")
    mounted.Dispose ()
    equal (("xx", false), ("yy", true)) (pending, revealed)

    case.Outcome <- Settles "x"
    let html = withTrailingRoot definition [| a; b |]
    case.Outcome <- Settles "y"
    let target = CheckSupport.host ()
    loadServed target html

    failsAsServed
        target
        (fun report ->
            Hydration.hydrate graph None report strictOptions target definition [| a; b |]
            |> ignore)
        "Hydration mismatch at #1: expected <!--]-->, found b"
        []

/// <summary>A list whose rows each hold a boundary over an input showing its source's value.</summary>
let private inputRows (case: BoundaryCase) =
    let pending =
        Component.define (fun _ () -> case.Log.Add "pending setup") (fun t -> t.i [||] [| t.text "loading" |])

    let content =
        Component.define (fun _ (_: BoundaryStatus) -> contentSource case) (fun t ->
            t.input [| t.Props.Input.value (fun (source: AsyncSource<string>) -> source.Value) |])

    let inner =
        Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary pending failedMessage content |])

    let row =
        Component.define (fun _ (item: unit -> Listed) -> item) (fun t -> t.li [||] [| t.render inner (fun _ -> ()) |])

    Component.define (fun _ (items: Listed array) -> items) (fun t -> t.ul [||] [| t.each (fun (item: Listed) -> item.Id) id row |])

let private rowBoundariesKeepFocus () =
    use graph = new Graph ()
    let case = boundaryCase ()
    let definition = inputRows case
    case.Outcome <- Settles "x"
    let host = serve definition [| a; b |]
    attachToBody host
    let before = nodesOf host
    let focused = unbox<HTMLInputElement> (all host "input").[1]
    focused.focus ()
    case.Log.Clear ()
    let mutations = recordMutations host
    let mounted = hydrate graph strictOptions host definition [| a; b |]

    let shown =
        mutations (), sameNodes before (serverNodes host), obj.ReferenceEquals (activeElement (), focused), focused.value, List.ofSeq case.Log

    mounted.Dispose ()
    detach host
    equal (4, true, true, "x", [ "content setup"; "content setup" ]) shown

type private SourcedItem =
    {
        Key: string
        Source: AsyncSource<string>
    }

/// <summary>A list of the case's items, each row showing the value of the source the root's setup created.</summary>
let private sourcedList (case: BoundaryCase) =
    let row =
        Component.define (fun _ (item: unit -> SourcedItem) -> item) (fun t ->
            t.li [||] [| t.text (fun (item: unit -> SourcedItem) -> (item ()).Source.Value) |])

    Component.define
        (fun _ () ->
            let source = contentSource case

            [| for key in case.Items -> { Key = key; Source = source } |])
        (fun t -> t.ul [||] [| t.each (fun (item: SourcedItem) -> item.Key) id row |])

let private pendingFreshRowsEmpty () =
    use graph = new Graph ()
    let case = boundaryCase ()
    let definition = sourcedList case
    case.Items <- [| "a"; "b" |]
    let host = serveCase case definition (Settles "s") Pends
    case.Items <- [| "a"; "b"; "c" |]
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add lenientOptions host definition ()

    let recovered = (element host "ul").innerHTML, messages reports
    mounted.Dispose ()
    let target = host
    target.innerHTML <- ""

    let mounted =
        Mounting.mount graph None reports.Add target None (fun context -> Program.run (Component.render definition ()) context.Backend)

    let shownByMount = (element target "ul").innerHTML
    mounted.Dispose ()

    // Recovered rows whose read is pending show an empty text, as a client mount's rows do.
    equal
        (("<!--[--><li></li><li></li><li></li><!--]-->", [ [ "Hydration mismatch at #0: expected <li>, found <!--]-->" ] ]),
         "<!--[--><li></li><li></li><li></li><!--]-->")
        (recovered, shownByMount)

/// <summary>A boundary whose content lists the characters of its source's value, each in a <c>b</c> element.</summary>
let private characterContent (case: BoundaryCase) =
    let content =
        Component.define (fun _ (_: BoundaryStatus) -> contentSource case) (fun t ->
            t.ul
                [||]
                [|
                    t.each id (fun (source: AsyncSource<string>) -> [| for ch in source.Value -> string ch |]) innerCell
                |])

    let pending =
        Component.define (fun _ () -> case.Log.Add "pending setup") (fun t -> t.i [||] [| t.text "loading" |])

    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary pending failedMessage content |])

let private pendingRowsHydrate () =
    use graph = new Graph ()
    let case = boundaryCase ()
    let definition = characterContent case
    let target = host ()
    let reports = ResizeArray<exn list>()

    let mounted =
        Mounting.mount graph None reports.Add target None (fun context -> Program.run (Component.render definition ()) context.Backend)

    let mountedPending = (element target "div").innerHTML
    graph.Run (fun () -> case.Sources[0].Settle "ab")
    let mountedShown = (element target "div").textContent, messages reports
    mounted.Dispose ()

    let host = serveCase case definition (Settles "ab") Pends
    let list = host.querySelector "ul"
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add strictOptions host definition ()

    let hydratedPending =
        (element host "div").textContent, messages reports, List.ofSeq case.Log, host.contains (unbox<Node> list)

    graph.Run (fun () -> case.Sources[0].Settle "cd")

    let hydratedShown =
        (element host "div").textContent, messages reports, host.contains (unbox<Node> list)

    mounted.Dispose ()

    // A mount shows the pending view until the list's items settle; hydration shows the server rows' copy until then.
    equal
        (("<!--[--><!--ranvier:start--><i>loading</i><!--ranvier:end--><!--]-->", ("ab", [])),
         (("ab", [], [ "content setup" ], false), ("cd", [], true)))
        ((mountedPending, mountedShown), (hydratedPending, hydratedShown))

let private rootWidget =
    Component.define (fun _ (log: ResizeArray<string>) -> log) (fun t ->
        t.div
            [|
                t.Hooks.widget (fun (log: ResizeArray<string>) _ ->
                    log.Add "attached"
                    fun () -> log.Add "destroyed")
            |]
            [||])

let private hydratedRootWidgetMoves () =
    use graph = new Graph ()
    let log = ResizeArray<string>()
    let host = serve rootWidget log
    let widget = host.querySelector "div"
    let other = CheckSupport.host ()
    let mounted = hydrate graph strictOptions host rootWidget log
    mounted.MoveBefore (other, None)
    let moved = obj.ReferenceEquals (widget, other.querySelector "div")
    mounted.Dispose ()
    equal (true, [ "attached"; "destroyed" ], 0.) (moved, List.ofSeq log, other.childNodes.length)

let private formsHydrate () =
    let shown (host: HTMLElement) =
        let area =
            host.querySelector "textarea"
            |> unbox<HTMLTextAreaElement>

        let toggle = input host "input"

        let list =
            host.querySelector "select"
            |> unbox<HTMLSelectElement>

        area.value, toggle.``checked``, list.selectedIndex, (element host "pre").textContent

    let served: ServerChecks.FormState =
        {
            Text = "\nline"
            On = true
            Choice = "b"
        }

    let differing: ServerChecks.FormState =
        {
            Text = "other"
            On = false
            Choice = "a"
        }

    let hydrateWith (client: ServerChecks.FormState) =
        use graph = new Graph ()
        let host = serve ServerChecks.formFixture served
        let mutations = recordMutations host
        let mounted = hydrate graph strictOptions host ServerChecks.formFixture client
        let outcome = mutations (), shown host
        mounted.Dispose ()
        outcome

    equal ((0, ("\nline", true, 1., "\nline")), ("other", false, 0., "other")) (hydrateWith served, snd (hydrateWith differing))

/// <summary>
/// Mounts <c>definition</c> and checks that the client's sorted id log and its DOM ids in document order equal
/// <c>expected</c>, and that the server's DOM ids equal the client's.
/// </summary>
let private sameIds (definition: Component<'props>) (props: 'props) (expected: string list) =
    let served = servedIds definition props
    use graph = new Graph ()
    let target = host ()
    identityLog.Clear ()
    let mounted = mount graph target (Component.render definition props)
    let issued = List.sort (List.ofSeq identityLog)
    let shown = domIds target
    mounted.Dispose ()
    equal (List.sort expected, expected, shown) (issued, shown, served)

let private clientHoleIdentity () =
    sameIds idHole () holeIds

let private clientNestedIdentity () =
    sameIds idNested [| 1; 2 |] nestedIds

let private clientHolesIdentity () =
    sameIds idHoles () holesIds

/// <summary>
/// Mounts <c>idBoundary</c> with its content ready, pending or failed and checks the client's id log in setup order, its
/// DOM ids against the server's, and the failures the boundary reported.
/// </summary>
let private clientBoundaryIdentity () =
    let check ready fails (expected: string list) (visible: string list) (reported: string list list) =
        idContentReady <- ready
        idContentFails <- fails
        let served = servedIds idBoundary ()
        use graph = new Graph ()
        let target = host ()
        let reports = ResizeArray<exn list>()
        identityLog.Clear ()

        let mounted =
            Mounting.mount graph None reports.Add target None (fun context -> Program.run (Component.render idBoundary ()) context.Backend)

        let issued = List.ofSeq identityLog
        let shown = domIds target
        mounted.Dispose ()
        idContentReady <- true
        idContentFails <- false

        equal (expected, visible, visible, reported) (issued, shown, served, [ for errors in reports -> [ for error in errors -> error.Message ] ])

    check true false boundaryContentIds boundaryContentIds []
    check false false boundaryPendingIds [ "r-0-0"; "r-0.r0.1-0" ] []
    check true true boundaryErrorIds [ "r-0-0"; "r-0.r0.1-0" ] [ [ "failed" ] ]

let private clientCreateIdInLaterSetup () =
    use graph = new Graph ()

    let error =
        ServerChecks.failure (fun () ->
            mount graph (host ()) (Component.render (capturedCreateId ()) ())
            |> ignore)

    equal (true, "CreateId is valid only while a component's setup runs") (error :? InvalidOperationException, error.Message)

/// <summary>Requires <c>actual</c> to equal <c>expected</c> at the journey step named <c>step</c>.</summary>
let private atStep (step: string) expected actual =
    if expected <> actual then
        failwithf "At %s: expected %A; got %A" step expected actual

[<Emit("$0.dispatchEvent(new Event($1, { bubbles: true }))")>]
let private dispatch (node: Node) (name: string) : unit = jsNative

[<Emit("$0.isConnected")>]
let private connected (node: Node) : bool = jsNative

/// <summary>Every node under <c>root</c>, in document order, that is <c>region</c> or lies outside it.</summary>
let private nodesBesides (region: Node) (root: Node) =
    nodesOf root
    |> Array.filter (fun node ->
        obj.ReferenceEquals (node, region)
        || not (region.contains node))

type private JourneyLive =
    {
        Id: string
        Name: Signal<string>
        Clicks: Signal<int>
        Items: Signal<Listed array>
        Wide: Signal<bool>
    }

type private JourneyProps =
    {
        Name: string
        Items: Listed array
        Wide: bool
    }

/// <summary>The state of a journey's page instances.</summary>
type private Journey =
    {
        /// <summary>The signals of the latest root instance.</summary>
        mutable Live: JourneyLive
        /// <summary>The setups and cleanups of the root, its rows and its arms, in order.</summary>
        Log: ResizeArray<string>
    }

type private JourneyRow = { Item: unit -> Listed; Id: string }

/// <summary>
/// A page with a click counter, a text input and a checkbox bound to its signals, a list of its items whose rows show an
/// id, and an arm that is a <c>b</c> element when the checkbox is checked and an <c>i</c> element otherwise; with the
/// structural hashes of the checked and the unchecked arm.
/// </summary>
let private journeyPage (journey: Journey) =
    let logged (name: string) (setup: Setup) =
        journey.Log.Add ("setup " + name)
        setup.OnCleanup (fun () -> journey.Log.Add ("cleanup " + name))

    let row =
        Component.define
            (fun setup (item: unit -> Listed) ->
                logged ("row " + (item ()).Text) setup
                { Item = item; Id = setup.CreateId () })
            (fun t -> t.li [| t.Attrs.id (fun (row: JourneyRow) -> Some row.Id) |] [| t.text (fun row -> (row.Item ()).Text) |])

    let wide =
        Component.define (fun setup (_: unit -> unit) -> logged "wide" setup) (fun t -> t.b [||] [| t.text "wide" |])

    let narrow =
        Component.define (fun setup (_: unit -> unit) -> logged "narrow" setup) (fun t -> t.i [||] [| t.text "narrow" |])

    let page =
        Component.define
            (fun setup (props: JourneyProps) ->
                logged "root" setup

                journey.Live <-
                    {
                        Id = setup.CreateId ()
                        Name = Api.createSignal props.Name
                        Clicks = Api.createSignal 0
                        Items = Api.createSignal props.Items
                        Wide = Api.createSignal props.Wide
                    }

                journey.Live)
            (fun t ->
                t.div
                    [|
                        t.Attrs.id (fun (r: JourneyLive) -> Some r.Id)
                        t.Attrs.title (fun (r: JourneyLive) -> Some r.Name.Value)
                    |]
                    [|
                        t.button
                            [| t.Events.onClick (fun r _ -> r.Clicks.Value <- r.Clicks.Value + 1) |]
                            [| t.text (fun r -> string r.Clicks.Value) |]
                        t.input
                            [|
                                t.Props.Input.value (fun r -> r.Name.Value)
                                t.Events.on ("input", fun r event -> r.Name.Value <- (unbox<HTMLInputElement> event.target).value)
                            |]
                        t.input
                            [|
                                t.Attrs.Input.``type`` "checkbox"
                                t.Props.Input.``checked`` (fun r -> r.Wide.Value)
                                t.Events.on ("change", fun r event -> r.Wide.Value <- (unbox<HTMLInputElement> event.target).``checked``)
                            |]
                        t.span [||] [| t.text (fun r -> r.Name.Value) |]
                        t.ul [||] [| t.each (fun (item: Listed) -> item.Id) (fun r -> r.Items.Value) row |]
                        t.section
                            [||]
                            [|
                                t.branch (fun r -> r.Wide.Value) (fun isWide -> if isWide then t.arm wide () else t.arm narrow ())
                            |]
                    |])

    page, structuralHash wide, structuralHash narrow

/// <summary>The id and text of each row under <c>host</c>.</summary>
let private rowsShown (host: HTMLElement) =
    [
        for row in all host "li" do
            let shown = unbox<HTMLElement> row
            yield shown.id, shown.textContent
    ]

/// <summary>
/// The root id and title, the button text, the text input's value, the checkbox state, the shown name, the rows and the
/// arm's text of a mounted <c>journeyPage</c>.
/// </summary>
let private journeyShown (host: HTMLElement) =
    let root = element host "div"
    let inputs = all host "input"

    root.id,
    root.getAttribute "title",
    (element host "button").textContent,
    (unbox<HTMLInputElement> inputs[0]).value,
    (unbox<HTMLInputElement> inputs[1]).``checked``,
    (element host "span").textContent,
    rowsShown host,
    (element host "section").textContent

/// <summary>Empties the journey's log, runs <c>action</c>, and returns the entries it logged.</summary>
let private logOf (journey: Journey) (action: unit -> unit) =
    journey.Log.Clear ()
    action ()
    List.ofSeq journey.Log

/// <summary>Runs <c>journey</c> on a new host attached to the document body, and detaches the host when it ends.</summary>
let private onAttachedHost (journey: HTMLElement -> unit) () =
    let host = host ()
    attachToBody host

    try
        journey host
    finally
        detach host

let private hydrationJourney (host: HTMLElement) =
    use graph = new Graph ()

    let journey =
        {
            Live = Unchecked.defaultof<_>
            Log = ResizeArray ()
        }

    let page, wideHash, narrowHash = journeyPage journey

    // Round 1: the server checked the box and selected the wide arm; the client selects the narrow arm.
    let first =
        {
            Name = "a"
            Items = [| a; b |]
            Wide = true
        }

    loadServed host (Server.render omit page first)
    let section = element host "section"
    let served = nodesBesides section host
    let servedSection = section.innerHTML
    let servedArm = unbox<Node>(host.querySelector "b")
    let rows = all host "li"
    let reports = ResizeArray<exn list>()

    let mismatch =
        "Hydration mismatch at #0: expected <!--[a:"
        + narrowHash
        + "-->, found <!--[a:"
        + wideHash
        + "-->"

    let mounted = ref Unchecked.defaultof<Mounting.Mounted>

    let hydrated =
        logOf journey (fun () -> mounted.Value <- Hydration.hydrate graph None reports.Add lenientOptions host page { first with Wide = false })

    atStep
        "round 1 hydrate"
        ([ "setup root"; "setup row a"; "setup row b"; "setup narrow" ],
         [ [ mismatch ] ],
         ("r-0-0", Some "a", "0", "a", false, "a", [ "r-0.r0.0-0", "a"; "r-0.r0.1-0", "b" ], "narrow"),
         true,
         false)
        (hydrated, messages reports, journeyShown host, sameNodes served (nodesBesides section host), connected servedArm)

    let button = element host "button"
    let text = (all host "input")[0]
    button.click ()
    (unbox<HTMLInputElement> text).value <- "typed"
    dispatch text "input"
    let typed = journeyShown host
    graph.Run (fun () -> journey.Live.Name.Value <- "set")

    atStep
        "round 1 click and type"
        (("r-0-0", Some "typed", "1", "typed", false, "typed", [ "r-0.r0.0-0", "a"; "r-0.r0.1-0", "b" ], "narrow"),
         ("r-0-0", Some "set", "1", "set", false, "set", [ "r-0.r0.0-0", "a"; "r-0.r0.1-0", "b" ], "narrow"),
         1)
        (typed, journeyShown host, reports.Count)

    let appended =
        logOf journey (fun () -> graph.Run (fun () -> journey.Live.Items.Value <- [| a; b; c |]))

    let appendedRows = all host "li"

    atStep
        "round 1 append"
        ([ "setup row c" ], [ "r-0.r0.0-0", "a"; "r-0.r0.1-0", "b"; "r-0.r0.2-0", "c" ], true, [ [ mismatch ] ])
        (appended, rowsShown host, sameNodes rows appendedRows[0..1], messages reports)

    let reordered =
        logOf journey (fun () -> graph.Run (fun () -> journey.Live.Items.Value <- [| c; a; b |]))

    atStep
        "round 1 reorder"
        ([], [ "r-0.r0.2-0", "c"; "r-0.r0.0-0", "a"; "r-0.r0.1-0", "b" ], true, [ [ mismatch ] ])
        (reordered, rowsShown host, sameNodes [| appendedRows[2]; rows[0]; rows[1] |] (all host "li"), messages reports)

    let removed =
        logOf journey (fun () -> graph.Run (fun () -> journey.Live.Items.Value <- [| c; a |]))

    atStep
        "round 1 remove"
        ([ "cleanup row b" ], [ "r-0.r0.2-0", "c"; "r-0.r0.0-0", "a" ], true, false, [ [ mismatch ] ])
        (removed, rowsShown host, sameNodes [| appendedRows[2]; rows[0] |] (all host "li"), connected rows[1], messages reports)

    // Checking the box selects the arm the server wrote.
    let recovered =
        logOf journey (fun () -> (element host "input[type=checkbox]").click())

    atStep
        "round 1 recover"
        ([ "cleanup narrow"; "setup wide" ],
         ("r-0-0", Some "set", "1", "set", true, "set", [ "r-0.r0.2-0", "c"; "r-0.r0.0-0", "a" ], "wide"),
         servedSection,
         1)
        (recovered, journeyShown host, section.innerHTML, reports.Count)

    let disposed = logOf journey (fun () -> mounted.Value.Dispose ())

    atStep
        "round 1 dispose"
        ([ "cleanup wide"; "cleanup row c"; "cleanup row a"; "cleanup root" ], 0., 1)
        (disposed, host.childNodes.length, reports.Count)

    // Round 2: the same host and graph hydrate a second server render cleanly.
    let second =
        {
            Name = "second"
            Items = [| b |]
            Wide = false
        }

    loadServed host (Server.render omit page second)
    let served = nodesOf host
    let ids = domIds host
    let secondReports = ResizeArray<exn list>()

    let hydrated =
        logOf journey (fun () -> mounted.Value <- Hydration.hydrate graph None secondReports.Add lenientOptions host page second)

    atStep
        "round 2 hydrate"
        ([ "setup root"; "setup row b"; "setup narrow" ],
         [],
         true,
         ids,
         ("r-0-0", Some "second", "0", "second", false, "second", [ "r-0.r0.0-0", "b" ], "narrow"))
        (hydrated, messages secondReports, sameNodes served (nodesOf host), domIds host, journeyShown host)

    // A row created after round 2 continues the second render's row counter.
    let continued =
        logOf journey (fun () -> graph.Run (fun () -> journey.Live.Items.Value <- [| b; c |]))

    atStep "round 2 continue" ([ "setup row c" ], [ "r-0.r0.0-0", "b"; "r-0.r0.1-0", "c" ]) (continued, rowsShown host)

    let disposed = logOf journey (fun () -> mounted.Value.Dispose ())

    atStep
        "round 2 dispose"
        ([ "cleanup narrow"; "cleanup row c"; "cleanup row b"; "cleanup root" ], 0., [], 1)
        (disposed, host.childNodes.length, messages secondReports, reports.Count)

let private retryJourney () =
    use graph = new Graph ()
    let case = boundaryCase ()
    let definition = retrying case
    let host = serveCase case definition (Settles "server") Pends
    let served = unbox<Node>(host.querySelector "span")
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add strictOptions host definition ()

    atStep
        "hydrate"
        ("<!--[c--><span id=\"r-0.r0.0-0\">server</span><!--]-->", false, [ "content setup" ], [])
        ((element host "div").innerHTML, obj.ReferenceEquals (served, host.querySelector "span"), List.ofSeq case.Log, messages reports)

    graph.Run (fun () -> case.Sources[0].Fail(Exception "failed"))

    atStep
        "fail"
        ("failed", [], false, [ "content setup" ], [ [ "failed" ] ])
        ((element host "div").textContent, domIds host, connected served, List.ofSeq case.Log, messages reports)

    case.Outcome <- Pends
    (element host "button").click()

    atStep
        "retry"
        ("loading", 0, [ "content setup"; "content setup" ], [ [ "failed" ] ])
        ((element host "div").textContent, (all host "button").Length, List.ofSeq case.Log, messages reports)

    graph.Run (fun () -> case.Sources[1].Settle "ok")

    // The content took row 0 and the error view row 1; the retried content takes row 2.
    atStep
        "settle"
        ("ok", [ "r-0.r0.2-0" ], [ "content setup"; "content setup" ], [ [ "failed" ] ])
        ((element host "div").textContent, domIds host, List.ofSeq case.Log, messages reports)

    mounted.Dispose ()
    atStep "dispose" (0., [ [ "failed" ] ]) (host.childNodes.length, messages reports)

/// <summary>A list of the items of the signal stored in <c>holder</c> by its setup, before a <c>bounded</c> boundary.</summary>
let private listBesideBoundary (case: BoundaryCase) (holder: Signal<Listed array> ref) =
    let inner = bounded case

    Component.define
        (fun _ (items: Listed array) ->
            holder.Value <- Api.createSignal items
            holder.Value)
        (fun t ->
            t.fragment
                [|
                    t.ul
                        [||]
                        [|
                            t.each (fun (item: Listed) -> item.Id) (fun (items: Signal<Listed array>) -> items.Value) listedRow
                        |]
                    t.render inner (fun _ -> ())
                |])

let private pendingListJourney () =
    use graph = new Graph ()
    let case = boundaryCase ()
    let holder = ref Unchecked.defaultof<Signal<Listed array>>
    let definition = listBesideBoundary case holder
    case.Outcome <- Settles "server"
    let host = serve definition [| a; b |]
    case.Outcome <- Pends
    case.Log.Clear ()
    case.Sources.Clear ()
    let rows = all host "li"
    let served = unbox<Node>(host.querySelector "span")
    let reports = ResizeArray<exn list>()

    let mounted =
        Hydration.hydrate graph None reports.Add strictOptions host definition [| a; b |]

    let boundary = element host "div"
    let copy = unbox<Node>(boundary.querySelector "span")

    atStep
        "hydrate"
        ("ab", true, "<!--[c--><span>server</span><!--]-->", false, [ "content setup" ], [])
        ((element host "ul").textContent,
         sameNodes rows (all host "li"),
         boundary.innerHTML,
         obj.ReferenceEquals (served, copy),
         List.ofSeq case.Log,
         messages reports)

    let mutations = recordMutations boundary
    graph.Run (fun () -> holder.Value.Value <- [| b; a; c |])
    let updated = all host "li"

    atStep
        "list update"
        ("bac", true, 0, "<!--[c--><span>server</span><!--]-->", true, [ "content setup" ], [])
        ((element host "ul").textContent,
         sameNodes [| rows[1]; rows[0] |] updated[0..1],
         mutations (),
         boundary.innerHTML,
         obj.ReferenceEquals (copy, boundary.querySelector "span"),
         List.ofSeq case.Log,
         messages reports)

    graph.Run (fun () -> case.Sources[0].Settle "client")

    atStep
        "reveal"
        ("client", true, false, [ "content setup" ], "bac", true, [])
        (boundary.textContent,
         obj.ReferenceEquals (served, boundary.querySelector "span"),
         connected copy,
         List.ofSeq case.Log,
         (element host "ul").textContent,
         sameNodes updated (all host "li"),
         messages reports)

    mounted.Dispose ()
    atStep "dispose" (0., [ "content setup" ], []) (host.childNodes.length, List.ofSeq case.Log, messages reports)

let private recoveryJourney (host: HTMLElement) =
    use graph = new Graph ()

    let journey =
        {
            Live = Unchecked.defaultof<_>
            Log = ResizeArray ()
        }

    let page, _, _ = journeyPage journey

    let props =
        {
            Name = "a"
            Items = [| a; b |]
            Wide = false
        }

    // Round 1: an extra element before the root's end marker re-renders the root.
    loadServed host (withTrailingRoot page props)
    let servedRoot = unbox<Node>(host.querySelector "div")
    let reports = ResizeArray<exn list>()
    let mounted = ref Unchecked.defaultof<Mounting.Mounted>

    let hydrated =
        logOf journey (fun () -> mounted.Value <- Hydration.hydrate graph None reports.Add lenientOptions host page props)

    atStep
        "round 1 hydrate"
        ([
            "setup root"
            "setup row a"
            "setup row b"
            "setup narrow"
            "cleanup narrow"
            "cleanup row b"
            "cleanup row a"
            "cleanup root"
            "setup root"
            "setup row a"
            "setup row b"
            "setup narrow"
         ],
         [ [ "Hydration mismatch at #1: expected <!--]-->, found b" ] ],
         ("r-0-0", Some "a", "0", "a", false, "a", [ "r-0.r0.0-0", "a"; "r-0.r0.1-0", "b" ], "narrow"),
         false,
         0)
        (hydrated, messages reports, journeyShown host, connected servedRoot, (all host "b").Length)

    let interacted =
        logOf journey (fun () ->
            (element host "button").click()
            graph.Run (fun () -> journey.Live.Items.Value <- [| b; c |]))

    atStep
        "round 1 interact"
        ([ "setup row c"; "cleanup row a" ],
         ("r-0-0", Some "a", "1", "a", false, "a", [ "r-0.r0.1-0", "b"; "r-0.r0.2-0", "c" ], "narrow"),
         [ [ "Hydration mismatch at #1: expected <!--]-->, found b" ] ])
        (interacted, journeyShown host, messages reports)

    let disposed = logOf journey (fun () -> mounted.Value.Dispose ())

    // Disposing the re-rendered root leaves the host empty, as a clean hydration's disposal does.
    atStep "round 1 dispose" ([ "cleanup narrow"; "cleanup row c"; "cleanup row b"; "cleanup root" ], "", 1) (disposed, host.innerHTML, reports.Count)

    // Round 2: a fresh server render on the same host hydrates without a report.
    loadServed host (Server.render omit page props)
    let served = nodesOf host
    let secondReports = ResizeArray<exn list>()

    let hydrated =
        logOf journey (fun () -> mounted.Value <- Hydration.hydrate graph None secondReports.Add lenientOptions host page props)

    (element host "button").click()

    atStep
        "round 2 hydrate"
        ([ "setup root"; "setup row a"; "setup row b"; "setup narrow" ],
         [],
         true,
         ("r-0-0", Some "a", "1", "a", false, "a", [ "r-0.r0.0-0", "a"; "r-0.r0.1-0", "b" ], "narrow"),
         1)
        (hydrated, messages secondReports, sameNodes served (nodesOf host), journeyShown host, reports.Count)

    mounted.Value.Dispose ()
    atStep "round 2 dispose" (0., [], 1) (host.childNodes.length, messages secondReports, reports.Count)

[<Emit("document.head.insertAdjacentHTML('beforeend', $0)")>]
let private appendHead (html: string) : unit = jsNative

[<Emit("Array.from(document.head.querySelectorAll('[data-ranvier-head]'))")>]
let private markedHead () : Node array = jsNative

[<Emit("document.head.childNodes.length")>]
let private headLength () : int = jsNative

[<Emit("document.head.querySelectorAll('[data-ranvier-head]').forEach(node => node.remove())")>]
let private clearMarkedHead () : unit = jsNative

let private outerHtml (nodes: Node array) =
    [ for node in nodes -> (unbox<Element> node).outerHTML ]

/// <summary>A meta description showing the text of the signal stored in <c>holder</c> by its setup, a canonical link, and a paragraph.</summary>
let private headPage (holder: Signal<string> ref) =
    Component.define
        (fun _ (text: string) ->
            holder.Value <- Api.createSignal text
            holder.Value)
        (fun t ->
            t.fragment
                [|
                    t.Document.meta
                        [|
                            t.Attrs.attribute ("name", "description")
                            t.Attrs.attribute ("content", fun (text: Signal<string>) -> Some text.Value)
                        |]
                    t.Document.link
                        [|
                            t.Attrs.attribute ("rel", "canonical")
                            t.Attrs.attribute ("href", "/lamp")
                        |]
                    t.p [||] [| t.text (fun (text: Signal<string>) -> text.Value) |]
                |])

let private headEntries (content: string) =
    [
        "<meta data-ranvier-head=\"meta:name=description\" name=\"description\" content=\""
        + content
        + "\">"
        "<link data-ranvier-head=\"link:canonical\" rel=\"canonical\" href=\"/lamp\">"
    ]

let private headJourney () =
    let pageHead = headLength ()

    try
        use graph = new Graph ()
        let holder = ref Unchecked.defaultof<Signal<string>>
        let definition = headPage holder
        let host = host ()

        // Round 1 adopts the served entries and removes a marked element served for another root.
        let page = Server.renderPage omit definition "one"

        appendHead (
            page.Head
            + "<meta data-ranvier-head=\"meta:name=robots\" name=\"robots\" content=\"index\">"
        )

        loadServed host page.Body
        let served = markedHead ()

        let mounted =
            Hydration.hydrate graph None strict strictOptions host definition "one"

        atStep
            "round 1 hydrate"
            (headEntries "one", true, false, "one")
            (outerHtml (markedHead ()), sameNodes served[0..1] (markedHead ()), connected served[2], (element host "p").textContent)

        graph.Run (fun () -> holder.Value.Value <- "uno")
        atStep "round 1 update" (headEntries "uno", true) (outerHtml (markedHead ()), sameNodes served[0..1] (markedHead ()))
        mounted.Dispose ()
        atStep "round 1 dispose" ([], false, false, pageHead) (outerHtml (markedHead ()), connected served[0], connected served[1], headLength ())

        // Round 2 adopts the second render's entries.
        let page = Server.renderPage omit definition "two"
        appendHead page.Head
        loadServed host page.Body
        let servedAgain = markedHead ()

        let mounted =
            Hydration.hydrate graph None strict strictOptions host definition "two"

        atStep
            "round 2 hydrate"
            (headEntries "two", true, "two")
            (outerHtml (markedHead ()), sameNodes servedAgain (markedHead ()), (element host "p").textContent)

        mounted.Dispose ()
        atStep "round 2 dispose" ([], pageHead, 0.) (outerHtml (markedHead ()), headLength (), host.childNodes.length)
    finally
        clearMarkedHead ()

/// <summary>Replaces each <c>tag</c> element under <c>root</c> with an empty HTML element named <c>x:tag</c>.</summary>
[<Emit("$0.querySelectorAll($1).forEach(e => e.replaceWith(e.ownerDocument.createElementNS('http://www.w3.org/1999/xhtml', 'x:' + $1)))")>]
let private prefixElements (root: Node) (tag: string) : unit = jsNative

let private boldRoot =
    Component.define (fun _ () -> ()) (fun t -> t.element (HtmlTag "b") [||] [| t.text "x" |])

let private titledItalic =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.element (HtmlTag "i") [| t.Attrs.title (fun (_: unit) -> "x") |] [||] |])

let private boldThenItalic =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [||]
            [|
                t.element (HtmlTag "b") [||] [| t.text (fun (_: unit) -> "x") |]
                t.element (HtmlTag "i") [| t.Attrs.title (fun (_: unit) -> "x") |] [||]
            |])

let private prefixedElementsMismatch () =
    let raised (definition: Component<unit>) (tag: string) =
        use graph = new Graph ()
        let host = serve definition ()
        prefixElements host tag

        try
            hydrate graph strictOptions host definition ()
            |> ignore

            "none"
        with error ->
            error.Message

    equal
        ("Hydration mismatch at #0: expected <b>, found x:b",
         "Hydration mismatch at 0/0: expected <i>, found x:i",
         "Hydration mismatch at 0/1: expected <i>, found x:i")
        (raised boldRoot "b", raised titledItalic "i", raised boldThenItalic "i")

/// <summary>A marked meta element appended to the document's head, whose <c>remove</c> raises <c>"stray removal"</c>.</summary>
[<Emit("(() => { const m = document.createElement('meta'); m.setAttribute('data-ranvier-head', 'stray'); m.remove = () => { throw new Error('stray removal'); }; document.head.appendChild(m); return m; })()")>]
let private strayHeadElement () : Element = jsNative

[<Emit("(delete $0.remove, $0.remove())")>]
let private removeStray (element: Element) : unit = jsNative

let private headRemovalFailureDisposes () =
    use graph = new Graph ()
    let host = serve card "a"
    let stray = strayHeadElement ()

    try
        failsAsServed
            host
            (fun report ->
                Hydration.hydrate graph None report strictOptions host card "a"
                |> ignore)
            "stray removal"
            []

        (element host "button").click()
        equal ("0", true) ((element host "button").textContent, connected (unbox<Node> stray))
    finally
        removeStray stray

let private servedEm =
    {
        Caption = "A"
        Body = Program.element (HtmlTag "em") [||] [| Program.text "server" |]
    }

let private clientEm =
    {
        Caption = "A"
        Body = Program.element (HtmlTag "em") [||] [| Program.text "client" |]
    }

#if RANVIER_VIEW_COUNTERS
/// <summary>A view hole followed by a live list of <c>Listed</c> items.</summary>
let private holedThenListed =
    Component.define (fun _ (items: Listed array) -> live items) (fun t ->
        t.div
            [||]
            [|
                t.view (fun (_: Signal<Listed array>) -> Program.text "hole")
                t.ul
                    [||]
                    [|
                        t.each (fun (item: Listed) -> item.Id) (fun (items: Signal<Listed array>) -> items.Value) listedRow
                    |]
            |])

/// <summary>
/// Whether hydrating <c>definition</c> for <c>props</c> over <c>host</c> recorded an inverse, the recorded inverses
/// still held once <c>hydrate</c> returns or raises, and whether it returned.
/// </summary>
let private heldInverses options (host: HTMLElement) (definition: Component<'props>) (props: 'props) =
    use graph = new Graph ()
    TemplateDom.HydrationCounters.recorded.Value <- 0
    TemplateDom.HydrationCounters.held.Value <- 0

    let mounted =
        try
            Some (Hydration.hydrate graph None ignore options host definition props)
        with _ ->
            None

    let counts =
        TemplateDom.HydrationCounters.recorded.Value > 0, TemplateDom.HydrationCounters.held.Value

    mounted |> Option.iter _.Dispose()
    counts, mounted.IsSome

let private rollbackLogsDiscarded () =
    let case = boundaryCase ()
    let boundedPage = bounded case

    let succeeded =
        [
            heldInverses strictOptions (serve holed servedEm) holed clientEm
            heldInverses lenientOptions (serve liveList [| a; b |]) liveList [| a; b; c |]
            heldInverses strictOptions (serveCase case boundedPage (Settles "server") (Settles "client")) boundedPage ()
            heldInverses strictOptions (serve rawCard "<b>x</b>") rawCard "<b>x</b>"
        ]

    let host = serve holedThenListed [| a; b |]
    let served = host.innerHTML
    let failed = heldInverses strictOptions host holedThenListed [| a |]

    equal ([ for _ in succeeded -> (true, 0), true ], ((true, 0), false), served) (succeeded, failed, host.innerHTML)
#endif

[<Emit("new WeakRef($0)")>]
let private weakRef (target: obj) : obj = jsNative

[<Emit("$0.deref() === undefined")>]
let private collected (reference: obj) : bool = jsNative

/// <summary>
/// Hydrates <c>definition</c> for <c>client</c> over its server HTML for <c>served</c>, and returns the check to run
/// after a garbage collection: the first server node matching <c>selector</c>, removed by the hydration, is collected
/// while the mount is live, and <c>shown host</c> is <c>expected</c>.
/// </summary>
let private removedNodeCollected
    options
    (definition: Component<'props>)
    (served: 'props)
    (client: 'props)
    (selector: string)
    (shown: HTMLElement -> string)
    (expected: string)
    : unit -> unit =
    let graph = new Graph ()
    let host = serve definition served
    let removed = weakRef (host.querySelector selector)
    let mounted = Hydration.hydrate graph None ignore options host definition client

    fun () ->
        let result = collected removed, shown host
        mounted.Dispose ()
        graph.Dispose ()
        equal (true, expected) result

/// <summary>The sources a retention fixture reads.</summary>
type private Shared =
    {
        Label: Signal<string>
        Items: Signal<Listed array>
        Loaded: AsyncSource<string>
    }

/// <summary>Sources showing "label", the items a and b, and "loaded" once <c>Loaded</c> settles.</summary>
let private sharedSources (loaded: bool) =
    let source = Api.createAsyncSource<string>()

    if loaded then
        source.Settle "loaded"

    {
        Label = Api.createSignal "label"
        Items = Api.createSignal [| a; b |]
        Loaded = source
    }

/// <summary>The sources of the retention case being set up; null while serving.</summary>
let mutable private shared: Shared = Unchecked.defaultof<_>

/// <summary>The case's sources, or new settled sources while serving.</summary>
let private currentSources () =
    if isNull (box shared) then sharedSources true else shared

/// <summary>
/// A div titled and labelled by <c>Label</c>, with a click listener, over a list of <c>Items</c> and a boundary whose
/// content is a span showing <c>Loaded</c>.
/// </summary>
let private readingPage =
    let pending =
        Component.define (fun _ () -> ()) (fun t -> t.i [||] [| t.text "loading" |])

    let content =
        Component.define (fun _ (_: BoundaryStatus) -> (currentSources ()).Loaded) (fun t ->
            t.span [||] [| t.text (fun (source: AsyncSource<string>) -> source.Value) |])

    Component.define (fun _ () -> currentSources ()) (fun t ->
        t.div
            [|
                t.Attrs.title (fun (sources: Shared) -> sources.Label.Value)
                t.Events.onClick (fun (sources: Shared) _ -> sources.Label.Value <- "clicked")
            |]
            [|
                t.text (fun (sources: Shared) -> sources.Label.Value)
                t.ul
                    [||]
                    [|
                        t.each (fun (item: Listed) -> item.Id) (fun (sources: Shared) -> sources.Items.Value) listedRow
                    |]
                t.boundary pending failedMessage content
            |])

let private retentionCycles = 32

/// <summary>
/// Runs <c>cycle</c> <c>retentionCycles</c> times on one graph, with the case's sources created on it and settled when
/// <c>loaded</c>. Each cycle mounts <c>readingPage</c>, disposes the mount and returns two objects the mount held. The
/// check, run after a garbage collection, requires all of them collected while the graph and the sources stay alive.
/// </summary>
let private disposedMountsCollected (loaded: bool) (cycle: Graph -> obj array) : unit -> unit =
    let graph = new Graph ()
    let sources = graph.Run (fun () -> sharedSources loaded)

    let held =
        [|
            for _ in 1..retentionCycles do
                shared <- sources
                let targets = cycle graph
                shared <- Unchecked.defaultof<_>

                for target in targets -> weakRef target
        |]

    fun () ->
        let alive =
            held
            |> Array.filter (collected >> not)
            |> Array.length

        graph.Run (fun () ->
            sources.Label.Value <- "after"
            sources.Items.Value <- [| c |])

        graph.Dispose ()
        equal (0, retentionCycles * 2) (alive, held.Length)

/// <summary>
/// A client mount of <c>readingPage</c>, with queued commits when <c>queued</c>, disposed; returns its div and the mount.
/// </summary>
let private clientCycle (queued: bool) (graph: Graph) : obj array =
    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render readingPage ())

    if queued then
        mounted.Flush ()

    let div = target.querySelector "div"
    mounted.Dispose ()
    [| div; mounted |]

/// <summary>
/// A hydration of <c>readingPage</c>'s server HTML, disposed; returns the server node at <c>selector</c> and the mount.
/// </summary>
let private hydratedCycle (selector: string) (graph: Graph) : obj array =
    let sources = shared
    shared <- Unchecked.defaultof<_>
    let target = serve readingPage ()
    shared <- sources
    let served = target.querySelector selector
    let mounted = hydrate graph strictOptions target readingPage ()
    mounted.Dispose ()
    [| served; mounted |]

/// <summary>
/// Mounts <c>liveList</c> for a and b, hydrating its server HTML when <c>hydrated</c>, then empties the list. The check
/// requires the removed rows' nodes collected while the mount is live.
/// </summary>
let private emptiedRowsCollected (hydrated: bool) : unit -> unit =
    let graph = new Graph ()
    let target = if hydrated then serve liveList [| a; b |] else host ()

    let mounted =
        if hydrated then
            hydrate graph strictOptions target liveList [| a; b |]
        else
            mount graph target (Component.render liveList [| a; b |])

    let rows = [| for node in all target "li" -> weakRef node |]
    let items = liveItems
    graph.Run (fun () -> items.Value <- [||])

    fun () ->
        let alive =
            rows
            |> Array.filter (collected >> not)
            |> Array.length

        let shown = (element target "ul").textContent
        mounted.Dispose ()
        graph.Dispose ()
        equal (0, 2, "") (alive, rows.Length, shown)

/// <summary>
/// Creates <c>retentionCycles</c> projections of a list signal under a root, each read by an effect, writes the list so a
/// flush runs their passes, and disposes the root. The check requires every projection collected while the graph and
/// the signal stay alive.
/// </summary>
let private passedProjectionsCollected () : unit -> unit =
    let graph = new Graph ()

    let source, held =
        graph.Run (fun () ->
            let source = Api.createSignal [| a; b |]
            let held = ResizeArray<obj>()

            let owner =
                Api.createRoot (fun owner ->
                    for _ in 1..retentionCycles do
                        let projection =
                            Api.createProjection (fun (item: Listed) -> item.Id) id (fun () -> source.Value :> seq<_>)

                        Api.createEffect (fun () -> projection.Keys |> ignore)
                        held.Add (weakRef projection)

                    owner)

            source.Value <- [| c |]
            owner.Dispose ()
            source, held.ToArray ())

    fun () ->
        let alive =
            held
            |> Array.filter (collected >> not)
            |> Array.length

        graph.Run (fun () -> source.Value <- [| a |])
        graph.Dispose ()
        equal (0, retentionCycles) (alive, held.Length)

/// <summary>
/// Retention checks for Chromium: each case mounts or hydrates and returns the check that tests/hydration.spec.mjs runs
/// after a forced garbage collection.
/// </summary>
let retentionCases: (string * (unit -> (unit -> unit))) array =
    [|
        "a view hole's removed server nodes are collected while the hydrated mount is live",
        fun () -> removedNodeCollected strictOptions holed servedEm clientEm "em" (fun host -> (element host "div").textContent) "Aclient"
        "a recovered list's removed server rows are collected while the hydrated mount is live",
        fun () -> removedNodeCollected lenientOptions liveList [| a; b |] [| a; b; c |] "li" (fun host -> (element host "ul").textContent) "abc"
        "disposed client mounts are collected while their graph and the settled sources they read live",
        fun () -> disposedMountsCollected true (clientCycle false)
        "disposed client mounts are collected while their graph and the pending source they read live",
        fun () -> disposedMountsCollected false (clientCycle false)
        "disposed client mounts with queued commits are collected while their graph and the settled sources they read live",
        fun () -> disposedMountsCollected true (clientCycle true)
        "disposed ready hydrations are collected while their graph and the settled sources they read live",
        fun () -> disposedMountsCollected true (hydratedCycle "div")
        "the detached server nodes of disposed hydrations with pending content are collected while their graph and source live",
        fun () -> disposedMountsCollected false (hydratedCycle "span")
        "projections disposed after a flush ran their passes are collected while their graph and source live", passedProjectionsCollected
        "a client mount's removed rows are collected while the mount is live", fun () -> emptiedRowsCollected false
        "a hydrated mount's removed server rows are collected while the mount is live", fun () -> emptiedRowsCollected true
    |]

let cases: (string * (unit -> unit)) array =
    strictCases
        [|
            "a client mount issues the server's ids for the same structure", clientIdentity
            "CreateId after a client setup raises", clientCreateIdAfterSetup
            "a client view hole's first template takes the row's position and a later one the authored step under it", clientHoleIdentity
            "a client mount numbers inlined children in pre-order and puts an inlined child's rows under its path, as the server does",
            clientNestedIdentity
            "each client view row counts its authored steps from 0 and the enclosing row resumes its count, as the server does", clientHolesIdentity
            "a Setup kept from one client setup raises when called during another instance's setup", clientCreateIdInLaterSetup
            "a client boundary's content takes row 0 and its pending or error view row 1, as the server's [c, [p and [e boundaries do",
            clientBoundaryIdentity
            "hydrating server HTML adopts every node", adoptsEveryNode
            "an element with a prefix is a mismatch for a template element of its local name, named at its path", prefixedElementsMismatch
            "a hydrated template updates, dispatches delegated and direct events, and disposes like a mount", updatesAndDisposes
            "hydration writes no text or attribute when values match", matchingValuesWriteNothing
            "hydration corrects differing values without a report", differingValuesCorrected
            "empty text slots hydrate through their placeholder and update", emptyTextHydrates
            "empty, one-node, multi-root and fragment templates hydrate", shapes
            "a template with regions hydrates its own nodes and adopts the server's rows between the server's markers", regionsMountWithContent
            "a region occupies two logical positions: the nodes and roots after a region's end marker are adopted", nodesAfterRegions
            "queued-commit hydration raises NotSupportedException", queuedRaises
            "hydration adopts only the root marker pair and leaves foreign siblings in place", foreignSiblings
            "a text value with CR LF hydrates without writes", carriageReturn
            "a structural mismatch outside every region re-renders the root and reports once; Strict raises and leaves the host as the server wrote it",
            rootMismatch
            "a mismatch names the expected and the found node", nestedMismatchNamesNodes
            "a host without a root marker raises under Strict and mounts fresh otherwise", missingRootMarker
            "a setup failure during hydration propagates and leaves the server HTML in place", setupThrowsDuringHydration
            "list regions hydrate, then reorder, append, remove and clear like mounted lists", listsHydrate
            "nested list regions hydrate and update", nestedRegions
            "text-adjacent row separators are removed during hydration", textRowSeparators
            "zero-root rows hydrate and keep their place", zeroRootRows
            "a list with fewer or more client items than server rows re-renders the region, reports once and reports a later failure",
            rowCountMismatch
            "a row whose first roots are a region's markers hydrates in place", rowStartsWithRegion
            "a same-count list with reordered items hydrates positionally and later moves keep keyed identity", reorderedItems
            "arms hydrate under an equal hash and keep their nodes across payload updates", armsHydrate
            "an arm of the same definition with another payload hydrates in place and corrects its values", armPayloadCorrected
            "a different selected arm re-renders the region and reports once; Strict raises", differentArm
            "a node after an adopted arm raises at the arm region's end; the region re-renders and reports once; Strict raises", armExtraNode
            "two arms with equal shape and different static text are distinguished by hash", equalShapes
            "a client arm the server plan rejects is a hash mismatch: the region re-renders and reports once; Strict raises",
            rejectedArmHashMismatches
            "arms in hydrated rows adopt under an equal hash, and a different arm re-renders only its region; Strict raises", armsInRows
            "rows in a hydrated arm adopt and reorder, and a row-count mismatch re-renders only the list; Strict raises", rowsInArms
            "arm switches after hydration run one cleanup per arm", armSwitchCleanups
            "view holes remove the server content and mount the view fresh", viewHolesRerender
            "boundaries: content hydrates in place when ready", contentHydratesInPlace
            "boundaries: server content stays as the pending view until reveal", serverContentStaysPending
            "boundaries: pending server content is an inert copy, and the reveal places the bound nodes and runs their hooks once",
            pendingContentInert
            "boundaries: a server pending view hydrates and the content reveals", serverPendingHydrates
            "boundaries: a server error view re-renders the boundary", serverErrorRerenders
            "boundaries: hydrated content takes row 0 and a pending or error view row 1, with the server's ids", boundaryIdsHydrate
            "boundaries: content and pending views of zero and two roots hydrate in place between their range comments", boundaryRootCounts
            "boundaries: a retry after a server error view mounts the content fresh and continues the region's row counter", retryAfterServedError
            "boundaries: a structural mismatch in a hydrating view re-renders that view and reports once; Strict raises", boundaryMismatches
            "boundaries: a mismatch after hydrated content restores the host under Strict and re-renders the root otherwise",
            boundaryRestoresAndRecovers
            "boundaries: a hook failure after pending content was detached restores the host's served nodes in both modes",
            hookFailureRestoresDetachedContent
            "boundaries in hydrated rows adopt their content, show copies of the server rows while pending; Strict restores the host",
            boundariesInRows
            "boundaries in hydrated rows whose content is ready by the end of hydrate keep their nodes and focus in place", rowBoundariesKeepFocus
            "recovered rows whose read is pending show an empty text, as a client mount's rows do", pendingFreshRowsEmpty
            "boundaries: a pending read of a list's items waits with the boundary in a mount and in hydrating content", pendingRowsHydrate
            "a mismatch inside a row's nested region re-renders only that region and reports once per region; Strict raises and restores the host",
            nestedRowMismatch
            "adopted rows keep their positional ids and run their cleanups when removed, and later rows continue the row counter",
            adoptedRowsKeepCleanupsAndIds
            "a row root's unclosed region raises at the root's position", unclosedRowRegion
            "a row setup failure during hydration propagates and leaves the server rows in place", rowFailureRestores
            "a failure of a recovered list's or arm's fresh mount raises from hydrate after the mismatch's one report and restores the host",
            recoveryFailureFails
            "a setup failure in a nested list's deferred pass raises from hydrate without a report and restores the host", deferredSetupFailureFails
            "a re-rendered root and a fresh mount without a root marker update and dispatch events", recoveredMountsAreLive
            "a re-rendered root issues ids under the hydration's prefix", recoveredIdsUsePrefix
            "a mismatch is found directly and among aggregated inner exceptions at any depth", aggregatedMismatch
            "a rollback failure is reported with the mismatch and the root re-renders", rollbackFailureReported
            "an unclosed region raises at its logical path and an unclosed root re-renders its content", unclosedRegions
            "inputs edited before hydration keep their values, focus and selection when the server and client values agree", editedInputsKept
            "a live input value differing from the server's is written by hydration", differingInputsCorrected
            "hydration rewrites raw content once and the slot then updates", rawContentRewritten
            "raw content that closes its element re-renders the root and reports once", rawHtmlClosesElement
            "placed hooks run on the adopted nodes after adoption, and a widget attaches to its adopted empty element", placedOnAdopted
            "an empty text slot in a textarea the server left empty hydrates into a created text node", emptyRcdataText
            "a structural mismatch names the path, the expected and the found node, and leaves the server HTML as it was", mismatchRestores
            "a host without a root marker raises a mismatch at root", rootMarkerAbsent
            "a mismatch after the first commits leaves the server's text, attributes, live values and raw content in place", strictRestoresValues
            "a widget on a hydrated root moves with its mount and is destroyed without a report", hydratedRootWidgetMoves
            "forms hydrate a leading line feed, checked and selectedIndex without writes, and write the client's differing values", formsHydrate
            "state script round-trips JSON containing </script> and U+2028", stateRoundTrip
            "CreateId is equal on server and hydrated client, and rows created after hydration continue their region's counter", identityContinues
            "journey: a hydrated page takes events and two-way input, moves its adopted rows, reports a region mismatch once, recovers by an update, disposes, and hydrates a second render on the same host and graph",
            onAttachedHost hydrationJourney
            "journey: hydrated content pending on the client fails into its error view, and a retry that pends and then settles continues the region's row counter",
            retryJourney
            "journey: a list update while a boundary's content is pending leaves its inert copy untouched, and the reveal shows the content of the one setup",
            pendingListJourney
            "journey: a re-rendered root takes events and updates and disposes, and a second render on the same host hydrates without a report",
            onAttachedHost recoveryJourney
            "journey: the head entries adopted in one round are withdrawn at disposal, and the next round adopts the newly served elements",
            headJourney
            "a failure removing the unadopted head elements disposes the mount and leaves the host and the head as served", headRemovalFailureDisposes
#if RANVIER_VIEW_COUNTERS
            "a returned hydrate holds no inverse of its edits, nested cursors included, and a raised one has run them all", rollbackLogsDiscarded
#endif
        |]

/// <summary>The text and value of the first element matching <c>selector</c> after the browser parses <c>html</c>.</summary>
let private parsed (html: string) (selector: string) : string * string =
    let container = globalThis.document.createElement "div"
    container.innerHTML <- html

    let element =
        container.querySelector selector
        |> unbox<HTMLElement>

    element.textContent, (if isNull element?value then "" else string element?value)

let private leadingLineFeeds () =
    let html =
        Server.render
            { Server.Options.defaults with
                Policy = Omit
            }
            ServerChecks.formFixture
            {
                ServerChecks.FormState.Text = "\nline"
                On = false
                Choice = "a"
            }

    equal ("\nline", "\nline") (parsed html "textarea")
    equal "\nline" (fst (parsed html "pre"))
    equal "\nstatic" (fst (parsed html "pre + pre"))

type private Shape =
    | El of string * Shape list
    | Tx of string

let rec private build (shape: Shape) : Node =
    match shape with
    | Tx text -> globalThis.document.createTextNode text :> Node
    | El (tag, children) ->
        let created = globalThis.document.createElement tag

        for child in children do
            created.appendChild (build child) |> ignore

        created :> Node

/// <summary>The template of <c>shape</c>: an element per <c>El</c> and a static text per <c>Tx</c>.</summary>
let rec private templateOf (t: TemplateBuilder<unit>) (shape: Shape) : TemplateView<unit> =
    match shape with
    | Tx text -> t.text text
    | El (tag, children) -> t.element (HtmlTag tag) [||] [| for child in children -> templateOf t child |]

/// <summary>The body's serialisation after <c>html</c> is parsed as the body of a document, as a served page is.</summary>
[<Emit("new DOMParser().parseFromString('<!doctype html><body>' + $0, 'text/html').body.innerHTML")>]
let private documentParsed (html: string) : string = jsNative

/// <summary>The serialisation of <c>shape</c> built node by node, and of that markup parsed as a document body.</summary>
let private parsedAgain (shape: Shape) =
    let built = globalThis.document.createElement "div"
    built.appendChild (build shape) |> ignore
    built.innerHTML, documentParsed built.innerHTML

/// <summary>
/// A parser fixture: each content-model rule's shape, built as DOM, serialises to markup the parser restructures, and
/// the server rejects the same shape as a template with the rule and the element path.
/// </summary>
let private rejectedMarkupRestructures () =
    let shapes =
        [
            "table-row", "table/tr", El ("table", [ El ("tr", [ El ("td", []) ]) ])
            "table-section", "div/tbody", El ("div", [ El ("tbody", []) ])
            "table-cell", "table/tbody/td", El ("table", [ El ("tbody", [ El ("td", []) ]) ])
            "table-column", "table/col", El ("table", [ El ("col", []) ])
            "table-foster", "table/#text", El ("table", [ Tx "x" ])
            "table-foster", "table/tbody/tr/div", El ("table", [ El ("tbody", [ El ("tr", [ El ("div", []) ]) ]) ])
            "table-foster", "table/colgroup/div", El ("table", [ El ("colgroup", [ El ("div", []) ]) ])
            "paragraph-close", "p/span/div", El ("p", [ El ("span", [ El ("div", []) ]) ])
            "paragraph-close", "p/li", El ("p", [ El ("li", []) ])
            "nested-anchor", "a/span/a", El ("a", [ El ("span", [ El ("a", []) ]) ])
            "nested-form", "form/div/form", El ("form", [ El ("div", [ El ("form", []) ]) ])
            "rcdata-content", "textarea/span", El ("textarea", [ El ("span", []) ])
            "nested-list-item", "ul/li/li", El ("ul", [ El ("li", [ El ("li", []) ]) ])
            "nested-list-item", "ul/li/span/li", El ("ul", [ El ("li", [ El ("span", [ El ("li", []) ]) ]) ])
            "nested-description", "dl/dd/dt", El ("dl", [ El ("dd", [ El ("dt", []) ]) ])
            "nested-heading", "h1/h2", El ("h1", [ El ("h2", []) ])
            "nested-button", "button/button", El ("button", [ El ("button", []) ])
            "nested-button", "button/span/button", El ("button", [ El ("span", [ El ("button", []) ]) ])
        ]

    let rejection (shape: Shape) =
        let definition = Component.define (fun _ () -> ()) (fun t -> templateOf t shape)

        (ServerChecks.failure (fun () ->
            Server.render Server.Options.defaults definition ()
            |> ignore))
            .Message

    let outcomes =
        [
            for rule, _, shape in shapes ->
                let built, reparsed = parsedAgain shape
                rule, built <> reparsed, rejection shape
        ]

    equal
        [
            for rule, path, _ in shapes ->
                rule,
                true,
                "The HTML parser restructures this markup ("
                + rule
                + ") at "
                + path
        ]
        outcomes

[<Emit("$0.replace(/<!--.*?-->/g, '')")>]
let private withoutComments (html: string) : string = jsNative

/// <summary>
/// Every accepted fixture's server HTML, parsed as a document body and under a div, serialises as the client mount's DOM
/// does, comments aside.
/// </summary>
let private acceptedFixturesParse () =
    let check (definition: Component<'props>) (props: 'props) =
        use graph = new Graph ()
        let html = Server.render omit definition props
        let parsed = host ()
        parsed.innerHTML <- html
        let client = host ()
        let mounted = mount graph client (Component.render definition props)
        let shown = withoutComments client.innerHTML
        mounted.Dispose ()
        (withoutComments (documentParsed html), withoutComments parsed.innerHTML), (shown, shown)

    let pairs =
        [
            check greeting greetingProps
            check named greetingProps
            check pair greetingProps
            check listed listedItems
            check carded greetingProps
            check besideText greetingProps
            check textual "a < b"
            check emptyText ""
            check staged (Working 3)
            check decades 42
            check staticTemplate ()
            check keyedPage 3
        ]
        @ [ for flags in flagCases -> check flagsTemplate flags ]

    equal [ for _, client in pairs -> client ] [ for served, _ in pairs -> served ]

let browserCases: (string * (unit -> unit)) array =
    [|
        "textarea value and pre text keep a leading line feed after parsing", leadingLineFeeds
        "each content-model rule's markup parses differently from the client DOM, and the server rejects it with that rule",
        rejectedMarkupRestructures
        "every accepted fixture's server HTML parses to the client DOM's serialisation", acceptedFixturesParse
    |]
