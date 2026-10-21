/// <summary>Arm and list regions whose source reads a pending value, inside and outside a boundary, mounted, rendered and hydrated.</summary>
module PendingRegionChecks

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Scheduling
open Ranvier.View.Foundation.Sugar

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let private strict (errors: exn list) =
    failwithf "Unexpected report %A" [ for error in errors -> error.Message ]

let private host () : HTMLElement =
    globalThis.document.createElement "div"

let private modeOf queued =
    if queued then
        QueuedCommits (CollectAndContinue, ignore)
    else
        SynchronousCommits

let private countOf (root: HTMLElement) (selector: string) =
    int (root.querySelectorAll selector).length

let private textOf (root: HTMLElement) (selector: string) =
    match root.querySelector selector with
    | Some element -> element.textContent
    | None -> null

/// <summary>The region kinds under test; each reads the source's value in its condition, selector or items.</summary>
type private Kind =
    | Show
    | ShowElse
    | MatchWith
    | ForEach

let private numberArm = armOf (b () { fun (n: unit -> int) -> string (n ()) })
let private noneArm = armOf (em () { "none" })

/// <summary>The region of <c>kind</c> over the current source of <c>flight</c>.</summary>
let private region (kind: Kind) (flight: Signal<AsyncSource<int>>) : TemplateView<'inst> =
    match kind with
    | Show -> show (fun _ -> flight.Value.Value > 0) (strong () { "high" })
    | ShowElse -> showElse (fun _ -> flight.Value.Value > 0) (strong () { "high" }) (em () { "low" })
    | MatchWith ->
        matchWith
            (fun _ ->
                if flight.Value.Value > 0 then
                    Some flight.Value.Value
                else
                    None)
            (function
             | Some n -> arm numberArm n
             | None -> arm noneArm ())
    | ForEach -> forEach id (fun _ -> Array.init flight.Value.Value (fun index -> index + 1)) (i () { fun (item: unit -> int) -> string (item ()) })

/// <summary>The content text of each kind for a source value of 2, and of 0.</summary>
let private shown (kind: Kind) =
    match kind with
    | Show -> "high", ""
    | ShowElse -> "high", "low"
    | MatchWith -> "2", "none"
    | ForEach -> "12", ""

let private pendingView =
    Component.template (fun _ () -> ()) (p (Attrs.className "pending") { "pending" })

let private failedView =
    Component.template (fun _ (failure: BoundaryFailure) -> failure) (p (Attrs.className "failed") { fun failure -> failure.Error.Message })

/// <summary>A boundary whose content shows the status's <c>IsPending</c> as <c>*</c> or <c>.</c>, then the region.</summary>
let private bounded (kind: Kind) (flight: Signal<AsyncSource<int>>) =
    let content =
        Component.template
            (fun _ (status: BoundaryStatus) -> status)
            (div (Attrs.className "level") {
                fun (status: BoundaryStatus) -> if status.IsPending () then "*" else "."
                region kind flight
            })

    Component.template (fun _ () -> ()) (section () { boundary pendingView failedView content })

let private unbounded (kind: Kind) (flight: Signal<AsyncSource<int>>) =
    Component.template (fun _ () -> ()) (div (Attrs.className "level") { region kind flight })

/// <summary>The pending view count, the error view count and the content's text.</summary>
let private state (root: HTMLElement) =
    countOf root ".pending", countOf root ".failed", textOf root ".level"

let private flush (mounted: Mounting.Mounted) =
    mounted.Flush ()
    mounted.Flush ()

/// <summary>
/// The region's first read is pending: the boundary shows its pending view, then reveals the content once the source
/// settles. A refetch keeps the content and sets <c>IsPending</c> until the new source settles, to the same value or
/// another. A failed source shows the error view.
/// </summary>
let private waitsAndRefreshes (kind: Kind) queued () =
    use graph = new Graph ()
    let flight = graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<int>()))
    let target = host ()
    let high, low = shown kind

    let mounted =
        Mounting.mountWith (modeOf queued) graph None strict target None (fun context ->
            Program.run (Component.render (bounded kind flight) ()) context.Backend)

    flush mounted
    equal (1, 0, null) (state target)
    graph.Run (fun () -> flight.Value.Settle 2)
    flush mounted
    equal (0, 0, "." + high) (state target)
    graph.Run (fun () -> flight.Value <- Api.createAsyncSource<int>())
    flush mounted
    equal (0, 0, "*" + high) (state target)
    graph.Run (fun () -> flight.Value.Settle 2)
    flush mounted
    equal (0, 0, "." + high) (state target)
    graph.Run (fun () -> flight.Value <- Api.createAsyncSource<int>())
    flush mounted
    equal (0, 0, "*" + high) (state target)
    graph.Run (fun () -> flight.Value.Settle 0)
    flush mounted
    equal (0, 0, "." + low) (state target)
    graph.Run (fun () -> flight.Value <- Api.createAsyncSource<int>())
    flush mounted
    graph.Run (fun () -> flight.Value.Fail (exn "offline"))
    flush mounted
    equal (0, 1, null) (state target)
    mounted.Dispose ()
    equal "" target.textContent

/// <summary>A source that fails after a pending first read shows the error view in place of the pending view.</summary>
let private failsBeforeReveal (kind: Kind) queued () =
    use graph = new Graph ()
    let flight = graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<int>()))
    let target = host ()

    let mounted =
        Mounting.mountWith (modeOf queued) graph None strict target None (fun context ->
            Program.run (Component.render (bounded kind flight) ()) context.Backend)

    flush mounted
    let waiting = state target
    graph.Run (fun () -> flight.Value.Fail (exn "offline"))
    flush mounted
    let failed = state target, textOf target ".failed"
    mounted.Dispose ()
    equal ((1, 0, null), ((0, 1, null), "offline")) (waiting, failed)

/// <summary>Outside every boundary, a pending region source keeps the region's last rows and reports nothing.</summary>
let private keepsOutsideBoundary (kind: Kind) queued () =
    use graph = new Graph ()
    let flight = graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<int>()))
    let target = host ()
    let high, _ = shown kind

    let mounted =
        Mounting.mountWith (modeOf queued) graph None strict target None (fun context ->
            Program.run (Component.render (unbounded kind flight) ()) context.Backend)

    flush mounted
    let first = textOf target ".level"
    graph.Run (fun () -> flight.Value.Settle 2)
    flush mounted
    let settled = textOf target ".level"
    graph.Run (fun () -> flight.Value <- Api.createAsyncSource<int>())
    flush mounted
    let refetching = textOf target ".level"
    mounted.Dispose ()
    equal ("", high, high) (first, settled, refetching)

/// <summary>Hiding the arm that holds the one pending region reveals the boundary.</summary>
let private removedPendingRegion queued () =
    use graph = new Graph ()
    let flight = graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<int>()))
    let outer = graph.Run (fun () -> Api.createSignal true)
    let target = host ()

    let content =
        Component.template
            (fun _ (status: BoundaryStatus) -> status)
            (div (Attrs.className "level") {
                "["
                show (fun _ -> outer.Value) (span () { region Show flight })
                "]"
            })

    let view =
        Component.template (fun _ () -> ()) (section () { boundary pendingView failedView content })

    let mounted =
        Mounting.mountWith (modeOf queued) graph None strict target None (fun context -> Program.run (Component.render view ()) context.Backend)

    flush mounted
    let waiting = state target
    graph.Run (fun () -> outer.Value <- false)
    flush mounted
    let revealed = state target
    mounted.Dispose ()
    equal ((1, 0, null), (0, 0, "[]")) (waiting, revealed)

/// <summary>Hiding the arm that holds a refetching region after the reveal clears <c>IsPending</c>.</summary>
let private removedRefetchingRegion queued () =
    use graph = new Graph ()
    let flight = graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<int>()))
    let outer = graph.Run (fun () -> Api.createSignal true)
    let target = host ()

    let content =
        Component.template
            (fun _ (status: BoundaryStatus) -> status)
            (div (Attrs.className "level") {
                fun (status: BoundaryStatus) -> if status.IsPending () then "*" else "."
                "["
                show (fun _ -> outer.Value) (span () { region Show flight })
                "]"
            })

    let view =
        Component.template (fun _ () -> ()) (section () { boundary pendingView failedView content })

    let mounted =
        Mounting.mountWith (modeOf queued) graph None strict target None (fun context -> Program.run (Component.render view ()) context.Backend)

    graph.Run (fun () -> flight.Value.Settle 2)
    flush mounted
    graph.Run (fun () -> flight.Value <- Api.createAsyncSource<int>())
    flush mounted
    let refetching = state target
    graph.Run (fun () -> outer.Value <- false)
    flush mounted
    let hidden = state target
    mounted.Dispose ()
    equal ((0, 0, "*[high]"), (0, 0, ".[]")) (refetching, hidden)

/// <summary>The first character of the content's text, the <c>IsPending</c> marker; empty without content.</summary>
let private marker (root: HTMLElement) =
    match textOf root ".level" with
    | null -> ""
    | text -> text.Substring (0, 1)

/// <summary>
/// An arm whose component setup reads a pending value fails the setup: on the content's first pass the boundary shows
/// its error view, and after the reveal the boundary's <c>IsPending</c> stays false.
/// </summary>
let private armSetupReadsPending initial queued () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createAsyncSource<int>())
    let flag = graph.Run (fun () -> Api.createSignal initial)
    let reports = ResizeArray<exn list>()
    let target = host ()

    let readingArm =
        Component.define
            (fun _ (payload: unit -> unit) ->
                source.Value |> ignore
                payload)
            (fun _ -> b () { "armed" })

    let plainArm = armOf (em () { "plain" })

    let content =
        Component.template
            (fun _ (status: BoundaryStatus) -> status)
            (div (Attrs.className "level") {
                fun (status: BoundaryStatus) -> if status.IsPending () then "*" else "."
                matchWith (fun _ -> flag.Value) (fun shown -> if shown then arm readingArm () else arm plainArm ())
            })

    let view =
        Component.template (fun _ () -> ()) (section () { boundary pendingView failedView content })

    let mounted =
        Mounting.mountWith (modeOf queued) graph None reports.Add target None (fun context -> Program.run (Component.render view ()) context.Backend)

    flush mounted
    let mountedState = state target
    graph.Run (fun () -> flag.Value <- true)
    flush mounted
    let selected = countOf target ".pending", countOf target ".failed", marker target
    graph.Run (fun () -> source.Settle 1)
    flush mounted
    let settled = countOf target ".pending", countOf target ".failed"
    mounted.Dispose ()

    if initial then
        equal ((0, 1, null), (0, 1, ""), (0, 1)) (mountedState, selected, settled)
    else
        equal ((0, 0, ".plain"), (0, 0, "."), (0, 0)) (mountedState, selected, settled)

let private serverOptions = Server.Options.defaults

/// <summary>The server renders the pending view while the region's source is pending, the content once it settles and the error view once it fails.</summary>
let private serverStates (kind: Kind) () =
    use graph = new Graph ()
    let flight = graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<int>()))
    let view = bounded kind flight

    let render () =
        graph.Run (fun () -> Server.render serverOptions view ())

    let pending = render ()
    graph.Run (fun () -> flight.Value.Settle 2)
    let settled = render ()
    graph.Run (fun () -> flight.Value <- Api.createAsyncSource<int>())
    graph.Run (fun () -> flight.Value.Fail (exn "offline"))
    let failed = render ()

    let marker (html: string) =
        html.Substring (html.IndexOf "<section>" + 9, 6)

    equal ("<!--[p", "<!--[c", "<!--[e") (marker pending, marker settled, marker failed)
    equal true (pending.Contains "<p class=\"pending\">pending</p>")
    equal true (failed.Contains "<p class=\"failed\">offline</p>")
    equal (false, false) (settled.Contains "pending", settled.Contains "failed")

/// <summary>A pending region source outside every boundary fails the server render with <c>ServerPendingException</c>.</summary>
let private serverRejectsUnbounded (kind: Kind) () =
    use graph = new Graph ()
    let flight = graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<int>()))

    let raised =
        try
            graph.Run (fun () -> Server.render serverOptions (unbounded kind flight) ())
            |> ignore

            false
        with Server.ServerPendingException _ ->
            true

    equal true raised

let private strictOptions: Hydration.HydrationOptions =
    { IdPrefix = "r"; Strict = true }

/// <summary>
/// Hydrates the boundary from server HTML rendered with the source settled (<c>[c</c>) or pending (<c>[p</c>), while
/// the client's source is pending; the boundary reveals the content once the client's source settles.
/// </summary>
let private hydratesPending (kind: Kind) servedReady () =
    use server = new Graph ()
    let served = server.Run (fun () -> Api.createSignal (Api.createAsyncSource<int>()))

    if servedReady then
        server.Run (fun () -> served.Value.Settle 2)

    let html =
        server.Run (fun () -> Server.render serverOptions (bounded kind served) ())

    use graph = new Graph ()
    let flight = graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<int>()))
    let target = host ()
    target.innerHTML <- html
    let high, _ = shown kind

    let mounted =
        Hydration.hydrate graph None strict strictOptions target (bounded kind flight) ()

    let waiting = state target
    graph.Run (fun () -> flight.Value.Settle 2)
    flush mounted
    let revealed = state target
    mounted.Dispose ()

    // While pending, a ready server boundary shows the copy of its server content, and a pending one its pending view.
    let shownWhilePending = if servedReady then (0, 0, "." + high) else (1, 0, null)

    equal (shownWhilePending, (0, 0, "." + high)) (waiting, revealed)

let private kinds = [ Show; ShowElse; MatchWith; ForEach ]

let private nameOf kind =
    match kind with
    | Show -> "show"
    | ShowElse -> "showElse"
    | MatchWith -> "matchWith"
    | ForEach -> "forEach"

let private modeName queued =
    if queued then "queued" else "synchronous"

let cases: (string * (unit -> unit)) array =
    [|
        for kind in kinds do
            for queued in [ false; true ] do
                $"{modeName queued}: a {nameOf kind} source reading a pending value waits with its boundary, then keeps its rows through a refetch",
                waitsAndRefreshes kind queued

                $"{modeName queued}: a {nameOf kind} source reading a pending value outside every boundary keeps its last rows",
                keepsOutsideBoundary kind queued

                $"{modeName queued}: a {nameOf kind} source failing after a pending first read shows the error view", failsBeforeReveal kind queued

            $"server: a {nameOf kind} source reading a pending value renders the boundary's pending view", serverStates kind
            $"server: a {nameOf kind} source reading a pending value outside every boundary raises", serverRejectsUnbounded kind
            $"hydration: a {nameOf kind} source pending on the client reveals a ready server boundary once it settles", hydratesPending kind true
            $"hydration: a {nameOf kind} source pending on the client reveals a pending server boundary once it settles", hydratesPending kind false

        "synchronous: hiding the arm that holds the one pending region reveals the boundary", removedPendingRegion false
        "queued: hiding the arm that holds the one pending region reveals the boundary", removedPendingRegion true
        "synchronous: hiding the arm that holds a refetching region clears IsPending", removedRefetchingRegion false
        "queued: hiding the arm that holds a refetching region clears IsPending", removedRefetchingRegion true
        "synchronous: an arm setup reading a pending value on the first pass shows the error view", armSetupReadsPending true false
        "queued: an arm setup reading a pending value on the first pass shows the error view", armSetupReadsPending true true
        "synchronous: an arm setup reading a pending value after the reveal leaves IsPending false", armSetupReadsPending false false
        "queued: an arm setup reading a pending value after the reveal leaves IsPending false", armSetupReadsPending false true
    |]
