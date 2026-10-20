module GeneratedChecks

open Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Fable.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Mounting
open Ranvier.View.Foundation.Template

module H = Ranvier.View.Foundation.Generated.Html

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let private query (host: Element) (selector: string) : HTMLElement =
    host.querySelector (selector) |> Option.get

[<Emit("$0.dispatchEvent(new ($0.ownerDocument.defaultView.InputEvent)('input', { bubbles: true, data: 'x', inputType: 'insertText' }))")>]
let private dispatchInput (node: Node) : unit = jsNative

let private parseHtml (host: HTMLElement) (html: string) : unit =
    host.innerHTML <- html

let private lifecycle (ops: Operations<'view>) run queued generated =
    use graph = new Graph ()
    let value = graph.Run (fun () -> Api.createSignal "first")
    let checkedValue = graph.Run (fun () -> Api.createSignal false)
    let mutable events = 0
    let mutable reads = 0

    let read () =
        reads <- reads + 1
        value.Value

    let onInput (event: InputEvent) =
        equal "insertText" event.inputType
        events <- events + 1
        value.Value <- "from event"

    let view =
        if generated then
            H.input
                ops
                [|
                    H.Input.titleStatic "static"
                    H.Input.valueReactive read
                    H.Input.checkedReactive (fun () -> checkedValue.Value)
                    H.Input.onInput onInput
                |]
        else
            Typed.input
                ops
                [|
                    Typed.staticProperty (fun (node: HTMLInputElement) value -> node.title <- value) "static"
                    Typed.reactiveProperty (fun (node: HTMLInputElement) value -> node.value <- value) read
                    Typed.reactiveProperty (fun (node: HTMLInputElement) value -> node.``checked`` <- value) (fun () -> checkedValue.Value)
                    Typed.listener "input" (fun event -> onInput (unbox event))
                |]

    let hosts = [| Dom.element "section" [] []; Dom.element "section" [] [] |]

    let mode =
        if queued then
            Scheduling.QueuedCommits (Scheduling.CollectAndContinue, ignore)
        else
            Scheduling.SynchronousCommits

    let mounts =
        hosts
        |> Array.map (fun host -> mountWith mode graph None ignore host None (fun ctx -> run view ctx.Backend))

    let inputs =
        hosts
        |> Array.map (fun host -> query host "input" :?> HTMLInputElement)

    try
        equal false (obj.ReferenceEquals (inputs[0], inputs[1]))
        equal 2 reads

        for input in inputs do
            equal "first" input.value
            equal "static" input.title
            equal false input.``checked``
            equal None (H.Input.files input)
            equal None (H.Input.form input)

        graph.Run (fun () ->
            value.Value <- "second"
            checkedValue.Value <- true)

        if queued then
            equal "first" inputs[0].value

        for mount in mounts do
            mount.Flush ()

        for input in inputs do
            equal "second" input.value
            equal true input.``checked``
            equal None (input.getAttribute "checked")

        graph.Run (fun () -> checkedValue.Value <- false)

        for mount in mounts do
            mount.Flush ()

        equal false inputs[0].``checked``
        dispatchInput inputs[0]

        for mount in mounts do
            mount.Flush ()

        equal 1 events
        equal "from event" inputs[1].value
        let beforeReads = reads
        graph.Run (fun () -> value.Value <- "cancelled")
        let retained = inputs[0].value
        mounts[0].Dispose()
        mounts[0].Flush()
        equal retained inputs[0].value
        dispatchInput inputs[0]
        equal 1 events
        mounts[1].Flush()
        equal "cancelled" inputs[1].value
        equal (beforeReads + 2) reads
    finally
        for mount in mounts do
            mount.Dispose ()

    for host in hosts do
        equal 0. host.childNodes.length

let private tagsAndSelect () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []

    let view =
        H.div
            Tree.ops
            [||]
            [|
                H.button Tree.ops [||] [| Tree.ops.Text "go" |]
                H.select
                    Tree.ops
                    [| H.Select.valueStatic "b" |]
                    [|
                        H.option Tree.ops [| H.Option.valueStatic "a" |] [| Tree.ops.Text "A" |]
                        H.option Tree.ops [| H.Option.valueStatic "b" |] [| Tree.ops.Text "B" |]
                    |]
            |]

    let mounted =
        mount graph None ignore host None (fun ctx -> Tree.run view ctx.Backend)

    try
        equal "b" ((query host "select" :?> HTMLSelectElement).value)
        equal "goAB" host.textContent
    finally
        mounted.Dispose ()

let private portable () =
    let view =
        H.div Tree.ops [| Typed.id "generated" |] [| H.option Tree.ops [||] [| Tree.ops.Text "<&" |] |]

    equal "<div id=\"generated\"><option>&lt;&amp;</option></div>" (Html.render Reject (Tree.run view))

let private expandedElements () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []

    let view =
        H.article
            Tree.ops
            [| H.Properties.HTMLElement.titleStatic "expanded" |]
            [|
                H.br Tree.ops [||]
                H.img Tree.ops [| H.Img.altStatic "sample" |]
                H.textarea Tree.ops [| H.Textarea.rowsStatic 3. |] [||]
                H.template Tree.ops [||] [| H.span Tree.ops [||] [| Tree.ops.Text "inert" |] |]
            |]

    let mounted =
        mount graph None ignore host None (fun ctx -> Tree.run view ctx.Backend)

    try
        equal "expanded" ((query host "article").title)
        equal "sample" ((query host "img" :?> HTMLImageElement).alt)
        equal 3. ((query host "textarea" :?> HTMLTextAreaElement).rows)
        let template = query host "template" :?> HTMLTemplateElement
        equal "inert" template.content.textContent
        equal 0. template.childNodes.length
    finally
        mounted.Dispose ()

    equal 0. host.childNodes.length

let private expandedPortable () =
    let view =
        H.article
            Tree.ops
            [||]
            [|
                H.br Tree.ops [||]
                H.img Tree.ops [||]
                H.textarea Tree.ops [||] [| Tree.ops.Text "<&" |]
            |]

    equal "<article><br><img><textarea>&lt;&amp;</textarea></article>" (Html.render Reject (Tree.run view))

    for tag in
        [
            "script"
            "style"
            "iframe"
            "noscript"
            "xmp"
            "noembed"
            "noframes"
            "plaintext"
        ] do
        let mutable rejected = false

        try
            Html.render Reject (Tree.run (Tree.ops.Element (HtmlTag tag) [||] [| Tree.ops.Text "<&" |]))
            |> ignore
        with :? System.InvalidOperationException ->
            rejected <- true

        equal true rejected

let private leadingNewlineRoundtrip (ops: Operations<'view>) run =
    use graph = new Graph ()

    for tag in [ "pre"; "textarea"; "listing" ] do
        for text in [ "\nhello"; "\r\nhello" ] do
            let view =
                ops.Element (HtmlTag tag) [||] [| ops.Text ""; ops.Bind (=) (fun () -> text) |]

            let parsed, direct = Dom.element "section" [] [], Dom.element "section" [] []
            let mounted = mount graph None ignore direct None (fun ctx -> run view ctx.Backend)

            try
                parseHtml parsed (Html.render Reject (run view))
                equal direct.textContent parsed.textContent
                equal text parsed.textContent
            finally
                mounted.Dispose ()

[<Emit("$0.dispatchEvent(new ($0.ownerDocument.defaultView.FocusEvent)('blur'))")>]
let private dispatchBlur (node: Node) : unit = jsNative

/// <summary>Runs <c>action</c> and returns the messages of the errors reported to the node's window meanwhile; each report is marked handled.</summary>
[<Emit("(() => { const view = $0.ownerDocument.defaultView; const seen = []; const listener = e => { seen.push(e.error ? e.error.message : String(e.message)); e.preventDefault(); }; view.addEventListener('error', listener); try { $1(); } finally { view.removeEventListener('error', listener); } return seen; })()")>]
let private windowErrors (node: Node) (action: unit -> unit) : string array = jsNative

type BurstProps =
    {
        Count: Signal<int>
        /// <summary>True to write inside <c>Api.batch</c>.</summary>
        Batched: bool
        /// <summary>True to raise "burst" after the writes.</summary>
        Failing: bool
        /// <summary>True to handle a direct <c>blur</c> listener instead of the delegated <c>click</c>.</summary>
        Blur: bool
    }

type Authoring =
    | TreeAuthoring
    | ProgramAuthoring
    | TemplateAuthoring

let private authoringName authoring =
    match authoring with
    | TreeAuthoring -> "tree"
    | ProgramAuthoring -> "program"
    | TemplateAuthoring -> "template"

/// <summary>Starts counting the data writes of the first text node under <c>button</c>.</summary>
[<Emit("(() => { const text = $0.firstChild; const observer = new ($0.ownerDocument.defaultView.MutationObserver)(() => {}); observer.observe(text, { characterData: true }); return observer; })()")>]
let private observeWrites (button: Node) : obj = jsNative

/// <summary>Returns the number of data writes observed since the previous call, or since <c>observeWrites</c>.</summary>
[<Emit("$0.takeRecords().length")>]
let private takeWrites (observer: obj) : int = jsNative

/// <summary>Adds 100 to the count, inside <c>Api.batch</c> when <c>Batched</c>, then raises "burst" when <c>Failing</c>.</summary>
let private burst (props: BurstProps) =
    let body () =
        for _ in 1..100 do
            props.Count.Value <- props.Count.Value + 1

        if props.Failing then
            failwith "burst"

    if props.Batched then Api.batch body else body ()

/// <summary>A button whose title shows the count through a text binding and whose generated listener runs <c>burst</c>.</summary>
let private authoredBurst (ops: Operations<'view>) (props: BurstProps) : 'view =
    let handler _ =
        burst props

    Typed.button
        ops
        [|
            if props.Blur then
                H.Events.onBlur handler
            else
                H.Events.onClick handler
        |]
        [| Typed.reactiveText ops (fun () -> string props.Count.Value) |]

let private burstButton =
    Component.define (fun _ (props: BurstProps) -> props) (fun t ->
        t.button
            [|
                t.Events.onClick (fun props _ ->
                    if not props.Blur then
                        burst props)
                t.Events.onBlur (fun props _ ->
                    if props.Blur then
                        burst props)
            |]
            [| t.text (fun props -> string props.Count.Value) |])

let mutable private guardedProps: BurstProps = Unchecked.defaultof<_>

let private burstContent =
    Component.define (fun _ (_: BoundaryStatus) -> guardedProps) (fun t -> t.render burstButton id)

let private burstPending =
    Component.define (fun _ () -> ()) (fun t -> t.span [||] [| t.text "loading" |])

let private burstFailed =
    Component.define (fun _ (failure: BoundaryFailure) -> failure) (fun t ->
        t.span [||] [| t.text (fun failure -> "failed: " + failure.Error.Message) |])

let private guardedBurst =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary burstPending burstFailed burstContent |])

let private guardedView (ops: Operations<'view>) (content: 'view) =
    ops.Boundary (ops.Text "loading", (fun failure -> ops.Text ("failed: " + failure.Error.Message)), (fun _ -> content))

let private mountBurst authoring queued (graph: Graph) report (host: HTMLElement) (props: BurstProps) guarded =
    guardedProps <- props

    let mode =
        if queued then
            Scheduling.QueuedCommits (Scheduling.CollectAndContinue, ignore)
        else
            Scheduling.SynchronousCommits

    mountWith mode graph None report host None (fun context ->
        match authoring with
        | TreeAuthoring ->
            let view = authoredBurst Tree.ops props
            Tree.run (if guarded then guardedView Tree.ops view else view) context.Backend
        | ProgramAuthoring ->
            let view = authoredBurst Program.ops props
            Program.run (if guarded then guardedView Program.ops view else view) context.Backend
        | TemplateAuthoring ->
            Program.run
                (if guarded then
                     Component.render guardedBurst ()
                 else
                     Component.render burstButton props)
                context.Backend)

let private burstHost () : HTMLElement =
    globalThis.document.createElement "div"

let private burstButtonOf (host: HTMLElement) =
    host.querySelector "button"
    |> unbox<HTMLButtonElement>

let private unexpected (errors: exn list) =
    failwithf "Unexpected report %A" [ for error in errors -> error.Message ]

/// <summary>Fires the burst listener once; returns the button text when the dispatch returns, the button text after a flush, and the number of text writes.</summary>
let private fireBurst authoring queued batched blur =
    use graph = new Graph ()
    let count = graph.Run (fun () -> Api.createSignal 0)
    let host = burstHost ()

    let props =
        {
            Count = count
            Batched = batched
            Failing = false
            Blur = blur
        }

    let mounted = mountBurst authoring queued graph unexpected host props false
    let button = burstButtonOf host
    let writes = observeWrites button

    if blur then dispatchBlur button else button.click ()

    let atReturn = button.textContent
    mounted.Flush ()
    let result = atReturn, button.textContent, takeWrites writes
    mounted.Dispose ()
    result

let private unbatchedBurst authoring queued () =
    equal (if queued then ("0", "100", 1) else ("100", "100", 100)) (fireBurst authoring queued false false)

let private batchedBurst authoring queued () =
    equal ((if queued then "0" else "100"), "100", 1) (fireBurst authoring queued true false)

let private directBurst authoring queued () =
    equal (fireBurst authoring queued true false) (fireBurst authoring queued true true)

let private outerBatch authoring queued () =
    use graph = new Graph ()
    let count = graph.Run (fun () -> Api.createSignal 0)
    let host = burstHost ()

    let props =
        {
            Count = count
            Batched = false
            Failing = false
            Blur = false
        }

    let mounted = mountBurst authoring queued graph unexpected host props false
    let button = burstButtonOf host
    let writes = observeWrites button

    let inside =
        graph.Run (fun () ->
            Api.batch (fun () ->
                button.click ()
                button.textContent))

    mounted.Flush ()
    equal ("0", "100", 1) (inside, button.textContent, takeWrites writes)
    mounted.Dispose ()

let private failingBurst authoring queued guarded () =
    use graph = new Graph ()
    let count = graph.Run (fun () -> Api.createSignal 0)
    let host = burstHost ()
    let reports = ResizeArray<exn list>()

    let props =
        {
            Count = count
            Batched = true
            Failing = true
            Blur = false
        }

    let mounted = mountBurst authoring queued graph reports.Add host props guarded
    let writes = observeWrites (burstButtonOf host)
    let escaped = windowErrors host (fun () -> (burstButtonOf host).click())
    mounted.Flush ()
    let total = graph.Run (fun () -> count.Value)
    let messages = [ for errors in reports -> [ for error in errors -> error.Message ] ]

    if guarded then
        equal (100, (if queued then 0 else 1), "failed: burst", [||], [ [ "burst" ] ]) (total, takeWrites writes, host.textContent, escaped, messages)
    else
        equal (100, 1, [| "burst" |], []) (total, takeWrites writes, escaped, messages)

    mounted.Dispose ()

let cases =
    [|
        for queued in [ false; true ] do
            for generated in [ false; true ] do
                yield
                    sprintf
                        "tree %s %s properties and listener ownership"
                        (if queued then "queued" else "sync")
                        (if generated then "generated" else "handwritten"),
                    fun () -> lifecycle Tree.ops Tree.run queued generated

                yield
                    sprintf
                        "program %s %s properties and listener ownership"
                        (if queued then "queued" else "sync")
                        (if generated then "generated" else "handwritten"),
                    fun () -> lifecycle Program.ops Program.run queued generated
        yield "generated five-tag subset and select initialization", tagsAndSelect
        yield "generated tags render portable escaped HTML", portable
        yield "expanded tags mount void elements and template content", expandedElements
        yield "expanded HTML snapshots preserve void and raw-text policies", expandedPortable
        yield "tree initial line feeds roundtrip through the HTML parser", fun () -> leadingNewlineRoundtrip Tree.ops Tree.run
        yield "program initial line feeds roundtrip through the HTML parser", fun () -> leadingNewlineRoundtrip Program.ops Program.run

        for authoring in [ TreeAuthoring; ProgramAuthoring; TemplateAuthoring ] do
            for queued in [ false; true ] do
                let prefix =
                    sprintf "%s %s" (authoringName authoring) (if queued then "queued" else "sync")

                yield
                    prefix
                    + ": a generated listener without a batch commits each write",
                    unbatchedBurst authoring queued

                yield
                    prefix
                    + ": a generated listener's writes inside Api.batch commit once",
                    batchedBurst authoring queued

                yield
                    prefix
                    + ": a listener dispatched inside an outer Api.batch commits after the batch",
                    outerBatch authoring queued

                yield
                    prefix
                    + ": a handler that throws inside its batch commits the writes it made and its failure goes to the boundary",
                    failingBurst authoring queued true

                yield
                    prefix
                    + ": a handler that throws inside its batch outside every boundary raises from the dispatch",
                    failingBurst authoring queued false

                yield
                    prefix
                    + ": a delegated and a direct generated listener behave the same inside a batch",
                    directBurst authoring queued
    |]
