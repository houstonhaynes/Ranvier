module BoundaryChecks

open System
open Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View.Testing
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Dom.Scheduling
open Ranvier.View.Dom.Mounting

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

/// <summary>Renders a boundary over one async source as HTML while it is pending, settled and failed.</summary>
let private server (ops: Operations<'view>) (run: 'view -> Backend -> unit) =
    use graph = new Graph ()

    graph.Run (fun () ->
        Api.createRoot (fun _ ->
            let source = Api.createAsyncSource<string>()

            let view =
                ops.Boundary (
                    ops.Text "<loading>",
                    (fun failure -> ops.Text failure.Error.Message),
                    fun _ -> Typed.div ops [||] [| Typed.reactiveText ops (fun () -> source.Value) |]
                )

            let render () =
                Html.render Reject (run view)

            equal "<!--[-->&lt;loading&gt;<!--]-->" (render ())
            source.Settle "<&"
            equal "<!--[--><div>&lt;&amp;</div><!--]-->" (render ())
            source.Fail (Exception "<failed>")
            equal "<!--[-->&lt;failed&gt;<!--]-->" (render ())))

let private host () : HTMLElement =
    (Conformance.document ()).createElement "div"

let private modeOf queued =
    if queued then
        QueuedCommits (CollectAndContinue, ignore)
    else
        SynchronousCommits

/// <summary>Counts the node's own <c>insertBefore</c> calls; the returned function restores it and returns the count.</summary>
let private countInserts (node: Node) : unit -> int =
    let count = ref 0

    let restore =
        Conformance.intercept node "insertBefore" (fun original args ->
            count.Value <- count.Value + 1
            original args)

    fun () ->
        restore ()
        count.Value

/// <summary>Records the text of each node inserted into the node by <c>insertBefore</c>; the returned function restores it and returns the records.</summary>
let private recordInserts (node: Node) : unit -> string array =
    let texts = ResizeArray<string>()

    let restore =
        Conformance.intercept node "insertBefore" (fun original args ->
            texts.Add (
                match (unbox<Node> args[0]).textContent with
                | Some text -> text
                | None -> null
            )

            original args)

    fun () ->
        restore ()
        texts.ToArray ()

let private strict (errors: exn list) =
    failwithf "Unexpected report %A" [ for error in errors -> error.Message ]

/// <summary>
/// A boundary whose content holds a text on one source, keyed rows reading a second source, and an arm reading the
/// first. The content reveals in one insertion once both sources settle.
/// </summary>
let private initialReveal (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let first = graph.Run (fun () -> Api.createAsyncSource<string>())
    let second = graph.Run (fun () -> Api.createAsyncSource<string>())
    let log = ResizeArray<string>()
    let host = host ()

    let content (_: BoundaryStatus) =
        ops.Component (fun setup ->
            log.Add "content"
            setup.OnCleanup (fun () -> log.Add "content cleanup")

            ops.Fragment
                [|
                    Typed.reactiveText ops (fun () -> first.Value)
                    Keyed.list ops id (fun () -> [ 1; 2 ]) (fun current -> Typed.reactiveText ops (fun () -> second.Value + string (current ())))
                    Branch.select ops id (fun () -> 0) (fun _ -> Typed.reactiveText ops (fun () -> "|" + first.Value))
                |])

    let pending =
        ops.Component (fun setup ->
            log.Add "pending"
            setup.OnCleanup (fun () -> log.Add "pending cleanup")
            ops.Text "loading")

    let view =
        ops.Boundary (pending, (fun failure -> ops.Text failure.Error.Message), content)

    let mounted =
        mountWith (modeOf queued) graph None strict host None (fun context -> run view context.Backend)

    equal ("loading", [ "content"; "pending" ]) (host.textContent, List.ofSeq log)
    let inserts = countInserts host
    graph.Run (fun () -> first.Settle "A")
    mounted.Flush ()
    equal "loading" host.textContent
    graph.Run (fun () -> second.Settle "B")
    mounted.Flush ()
    equal ("AB1B2|A", 1) (host.textContent, inserts ())
    equal [ "content"; "pending"; "pending cleanup" ] (List.ofSeq log)
    mounted.Dispose ()
    equal ([ "content"; "pending"; "pending cleanup"; "content cleanup" ], "") (List.ofSeq log, host.textContent)

/// <summary>An inner boundary inside an outer boundary's content; each reveals when its own source settles.</summary>
let private nestedReveal (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued innerFirst =
    use graph = new Graph ()
    let outer = graph.Run (fun () -> Api.createAsyncSource<string>())
    let inner = graph.Run (fun () -> Api.createAsyncSource<string>())
    let host = host ()

    let failed (failure: BoundaryFailure) =
        ops.Text failure.Error.Message

    let innerView =
        ops.Boundary (ops.Text "[inner loading]", failed, (fun _ -> Typed.reactiveText ops (fun () -> inner.Value)))

    let view =
        ops.Boundary (ops.Text "[outer loading]", failed, fun _ -> ops.Fragment [| Typed.reactiveText ops (fun () -> outer.Value); innerView |])

    let mounted =
        mountWith (modeOf queued) graph None strict host None (fun context -> run view context.Backend)

    equal "[outer loading]" host.textContent

    if innerFirst then
        graph.Run (fun () -> inner.Settle "B")
        mounted.Flush ()
        equal "[outer loading]" host.textContent
        graph.Run (fun () -> outer.Settle "A")
        mounted.Flush ()
    else
        graph.Run (fun () -> outer.Settle "A")
        mounted.Flush ()
        equal "A[inner loading]" host.textContent
        graph.Run (fun () -> inner.Settle "B")
        mounted.Flush ()

    equal "AB" host.textContent
    mounted.Dispose ()

/// <summary>Disposes a mount while its boundary is pending, then settles the source.</summary>
let private disposedWhilePending (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createAsyncSource<string>())
    let log = ResizeArray<string>()
    let host = host ()

    let logged name (view: 'view) =
        ops.Component (fun setup ->
            log.Add name
            setup.OnCleanup (fun () -> log.Add (name + " cleanup"))
            view)

    let view =
        ops.Boundary (
            logged "pending" (ops.Text "loading"),
            (fun failure -> ops.Text failure.Error.Message),
            fun _ -> logged "content" (Typed.reactiveText ops (fun () -> source.Value))
        )

    let mounted =
        mountWith (modeOf queued) graph None strict host None (fun context -> run view context.Backend)

    let inserts = countInserts host
    mounted.Dispose ()
    equal ([ "content"; "pending"; "pending cleanup"; "content cleanup" ], 0.) (List.ofSeq log, host.childNodes.length)
    graph.Run (fun () -> source.Settle "late")
    mounted.Flush ()
    equal (4, 0., 0) (log.Count, host.childNodes.length, inserts ())

/// <summary>Moves a mount to another host while its boundary is pending; the content reveals there and handles clicks.</summary>
let private movedWhilePending (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createAsyncSource<string>())
    let mutable clicks = 0
    let first, second = host (), host ()

    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure -> ops.Text failure.Error.Message),
            fun _ ->
                Typed.button ops [| Typed.listener "click" (fun _ -> clicks <- clicks + 1) |] [| Typed.reactiveText ops (fun () -> source.Value) |]
        )

    let mounted =
        mountWith (modeOf queued) graph None strict first None (fun context -> run view context.Backend)

    mounted.MoveBefore (second, None)
    graph.Run (fun () -> source.Settle "moved")
    mounted.Flush ()
    equal ("", "moved") (first.textContent, second.textContent)

    (second.querySelector "button"
     |> Option.get
     |> unbox<HTMLButtonElement>)
        .click()

    equal 1 clicks
    mounted.Dispose ()

/// <summary>Removes the one row still pending before the reveal; the boundary reveals the remaining rows.</summary>
let private removedPendingRow (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let never = graph.Run (fun () -> Api.createAsyncSource<string>())
    let other = graph.Run (fun () -> Api.createAsyncSource<string>())
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let host = host ()

    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure -> ops.Text failure.Error.Message),
            fun _ ->
                Keyed.list ops id (fun () -> rows.Value) (fun current ->
                    Typed.reactiveText ops (fun () -> if current () = 2 then never.Value else other.Value))
        )

    let mounted =
        mountWith (modeOf queued) graph None strict host None (fun context -> run view context.Backend)

    graph.Run (fun () -> other.Settle "x")
    mounted.Flush ()
    equal "loading" host.textContent
    graph.Run (fun () -> rows.Value <- [ 1 ])
    mounted.Flush ()
    equal "x" host.textContent
    mounted.Dispose ()

/// <summary>Replaces the one pending row and adds a row in the same pass; the boundary reveals both rows after the pass.</summary>
let private replacedPendingRow (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let never = graph.Run (fun () -> Api.createAsyncSource<string>())
    let other = graph.Run (fun () -> Api.createAsyncSource<string>())
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let host = host ()

    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure -> ops.Text failure.Error.Message),
            fun _ ->
                Keyed.list ops id (fun () -> rows.Value) (fun current ->
                    Typed.reactiveText ops (fun () ->
                        (if current () = 2 then never.Value else other.Value)
                        + string (current ())))
        )

    let mounted =
        mountWith (modeOf queued) graph None strict host None (fun context -> run view context.Backend)

    graph.Run (fun () -> other.Settle "x")
    mounted.Flush ()
    equal "loading" host.textContent
    graph.Run (fun () -> rows.Value <- [ 3; 1 ])
    mounted.Flush ()
    equal "x3x1" host.textContent
    mounted.Dispose ()

/// <summary>Replacing a pending arm with another pending arm keeps the boundary hidden until the new arm has a value.</summary>
let private armReplacedWhilePending (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let first = graph.Run (fun () -> Api.createAsyncSource<string>())
    let second = graph.Run (fun () -> Api.createAsyncSource<string>())
    let arm = graph.Run (fun () -> Api.createSignal 0)
    let host = host ()

    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure -> ops.Text failure.Error.Message),
            fun _ ->
                Branch.select ops id (fun () -> arm.Value) (fun current ->
                    Typed.reactiveText ops (fun () -> if current () = 0 then first.Value else second.Value))
        )

    let mounted =
        mountWith (modeOf queued) graph None strict host None (fun context -> run view context.Backend)

    let inserted = recordInserts host
    graph.Run (fun () -> arm.Value <- 1)
    mounted.Flush ()
    equal "loading" host.textContent
    graph.Run (fun () -> second.Settle "B")
    mounted.Flush ()
    equal ("B", [| "B" |]) (host.textContent, inserted ())
    mounted.Dispose ()

/// <summary>Replacing the one pending row with another pending row keeps the boundary hidden until the new row has a value.</summary>
let private rowReplacedWhilePending (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let never = graph.Run (fun () -> Api.createAsyncSource<string>())
    let later = graph.Run (fun () -> Api.createAsyncSource<string>())
    let other = graph.Run (fun () -> Api.createAsyncSource<string>())
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let host = host ()

    let read row =
        match row with
        | 1 -> other.Value
        | 2 -> never.Value
        | _ -> later.Value

    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure -> ops.Text failure.Error.Message),
            fun _ -> Keyed.list ops id (fun () -> rows.Value) (fun current -> Typed.reactiveText ops (fun () -> read (current ())))
        )

    let mounted =
        mountWith (modeOf queued) graph None strict host None (fun context -> run view context.Backend)

    graph.Run (fun () -> other.Settle "x")
    mounted.Flush ()
    graph.Run (fun () -> rows.Value <- [ 1; 3 ])
    mounted.Flush ()
    equal "loading" host.textContent
    graph.Run (fun () -> later.Settle "y")
    mounted.Flush ()
    equal "xy" host.textContent
    mounted.Dispose ()

/// <summary>
/// After the reveal, a row added with a value leaves <c>IsPending</c> false, and a row added with a pending read sets it
/// until the read settles.
/// </summary>
let private rowsAddedAfterReveal (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let late = graph.Run (fun () -> Api.createAsyncSource<string>())
    let rows = graph.Run (fun () -> Api.createSignal [ 1 ])
    let seen = ResizeArray<bool>()
    let host = host ()

    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure -> ops.Text failure.Error.Message),
            fun status ->
                ops.Component (fun _ ->
                    Api.createEffect (fun () -> seen.Add (status.IsPending ()))

                    Keyed.list ops id (fun () -> rows.Value) (fun current ->
                        Typed.reactiveText ops (fun () -> if current () = 3 then late.Value else string (current ()))))
        )

    let mounted =
        mountWith (modeOf queued) graph None strict host None (fun context -> run view context.Backend)

    graph.Run (fun () -> rows.Value <- [ 1; 2 ])
    mounted.Flush ()
    equal ("12", [ false ]) (host.textContent, List.ofSeq seen)
    graph.Run (fun () -> rows.Value <- [ 1; 2; 3 ])
    mounted.Flush ()
    equal ("12", [ false; true ]) (host.textContent, List.ofSeq seen)
    graph.Run (fun () -> late.Settle "3")
    mounted.Flush ()
    mounted.Flush ()
    equal ("123", [ false; true; false ]) (host.textContent, List.ofSeq seen)
    mounted.Dispose ()

#if RANVIER_VIEW_COUNTERS
/// <summary>
/// A boundary over three ready bindings and one pending binding writes no wake while its content sets up, and one wake
/// when the pending read settles.
/// </summary>
let private setupWakes (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createAsyncSource<string>())
    let host = host ()

    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure -> ops.Text failure.Error.Message),
            fun _ ->
                ops.Fragment
                    [|
                        Typed.reactiveText ops (fun () -> "a")
                        Typed.reactiveText ops (fun () -> "b")
                        Typed.reactiveText ops (fun () -> "c")
                        Typed.reactiveText ops (fun () -> source.Value)
                    |]
        )

    Mounting.BoundaryCounters.wakes.Value <- 0

    let mounted =
        mountWith (modeOf queued) graph None strict host None (fun context -> run view context.Backend)

    equal ("loading", 0) (host.textContent, Mounting.BoundaryCounters.wakes.Value)
    graph.Run (fun () -> source.Settle "d")
    mounted.Flush ()
    equal ("abcd", 1) (host.textContent, Mounting.BoundaryCounters.wakes.Value)
    mounted.Dispose ()
#endif

let private messages (reports: ResizeArray<exn list>) =
    [ for errors in reports -> errors |> List.map _.Message ]

/// <summary>
/// Mounts <c>content</c> under a boundary whose error view shows the failure's message. The content and error views log
/// their setup and cleanup.
/// </summary>
let private underBoundary
    (ops: Operations<'view>)
    (run: 'view -> Backend -> unit)
    queued
    (graph: Graph)
    (host: HTMLElement)
    (log: ResizeArray<string>)
    (reports: ResizeArray<exn list>)
    (content: unit -> 'view)
    =
    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure ->
                ops.Component (fun setup ->
                    log.Add ("error " + failure.Error.Message)
                    setup.OnCleanup (fun () -> log.Add "error cleanup")
                    ops.Text ("failed: " + failure.Error.Message))),
            fun _ ->
                ops.Component (fun setup ->
                    log.Add "content"
                    setup.OnCleanup (fun () -> log.Add "content cleanup")
                    content ())
        )

    mountWith (modeOf queued) graph None reports.Add host None (fun context -> run view context.Backend)

let private readerFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "ok")
    let host, log, reports = host (), ResizeArray<string>(), ResizeArray<exn list>()

    let mounted =
        underBoundary ops run queued graph host log reports (fun () ->
            Typed.reactiveText ops (fun () ->
                if source.Value = "bad" then
                    failwith "reader"
                else
                    source.Value))

    equal "ok" host.textContent
    graph.Run (fun () -> source.Value <- "bad")
    mounted.Flush ()

    let expected =
        "failed: reader", [ "content"; "content cleanup"; "error reader" ], [ [ "reader" ] ]

    equal expected (host.textContent, List.ofSeq log, messages reports)
    graph.Run (fun () -> source.Value <- "bad again")
    mounted.Flush ()
    equal expected (host.textContent, List.ofSeq log, messages reports)
    mounted.Dispose ()

/// <summary>A content binding whose reader raises on its first read, during the content's setup.</summary>
let private initialReaderFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let host, log, reports = host (), ResizeArray<string>(), ResizeArray<exn list>()

    let mounted =
        underBoundary ops run queued graph host log reports (fun () -> Typed.reactiveText ops (fun () -> failwith "initial"))

    mounted.Flush ()

    equal
        ("failed: initial", [ "content"; "content cleanup"; "error initial" ], [ [ "initial" ] ])
        (host.textContent, List.ofSeq log, messages reports)

    mounted.Dispose ()

let private setterFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "ok")
    let host, log, reports = host (), ResizeArray<string>(), ResizeArray<exn list>()

    let mounted =
        underBoundary ops run queued graph host log reports (fun () ->
            Typed.div
                ops
                [|
                    Typed.reactiveProperty
                        (fun (node: HTMLDivElement) (value: string) ->
                            if value = "bad" then
                                failwith "setter"

                            node.title <- value)
                        (fun () -> source.Value)
                |]
                [| Typed.reactiveText ops (fun () -> source.Value) |])

    graph.Run (fun () -> source.Value <- "bad")
    mounted.Flush ()

    equal
        ("failed: setter", [ "content"; "content cleanup"; "error setter" ], [ [ "setter" ] ], (if queued then 1 else 0))
        (host.textContent, List.ofSeq log, messages reports, mounted.CommitErrors.Length)

    mounted.Dispose ()

let private rowSetupFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let host, log, reports = host (), ResizeArray<string>(), ResizeArray<exn list>()

    let mounted =
        underBoundary ops run queued graph host log reports (fun () ->
            Keyed.list ops id (fun () -> rows.Value) (fun current ->
                ops.Component (fun _ ->
                    if current () = 3 then
                        failwith "row setup"

                    Typed.reactiveText ops (fun () -> string (current ())))))

    equal "12" host.textContent
    graph.Run (fun () -> rows.Value <- [ 1; 2; 3 ])
    mounted.Flush ()

    equal
        ("failed: row setup", [ "content"; "content cleanup"; "error row setup" ], [ [ "row setup" ] ])
        (host.textContent, List.ofSeq log, messages reports)

    mounted.Dispose ()

let private armSetupFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let selected = graph.Run (fun () -> Api.createSignal 0)
    let host, log, reports = host (), ResizeArray<string>(), ResizeArray<exn list>()

    let mounted =
        underBoundary ops run queued graph host log reports (fun () ->
            Branch.select ops id (fun () -> selected.Value) (fun current ->
                ops.Component (fun _ ->
                    if current () = 1 then
                        failwith "arm setup"

                    ops.Text "arm")))

    equal "arm" host.textContent
    graph.Run (fun () -> selected.Value <- 1)
    mounted.Flush ()

    equal
        ("failed: arm setup", [ "content"; "content cleanup"; "error arm setup" ], [ [ "arm setup" ] ])
        (host.textContent, List.ofSeq log, messages reports)

    mounted.Dispose ()

/// <summary>
/// Two setters under one boundary fail in the same queued flush. The error view shows the first failure in commit
/// order, set by the core's effect order.
/// </summary>
let private twoQueuedFailures (ops: Operations<'view>) (run: 'view -> Backend -> unit) =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "ok")
    let host, log, reports = host (), ResizeArray<string>(), ResizeArray<exn list>()

    let failing (message: string) =
        Typed.reactiveProperty
            (fun (node: HTMLDivElement) (value: string) ->
                if value = "bad" then
                    failwith message

                node.title <- value)
            (fun () -> source.Value)

    let mounted =
        underBoundary ops run true graph host log reports (fun () -> Typed.div ops [| failing "first"; failing "second" |] [||])

    graph.Run (fun () -> source.Value <- "bad")
    mounted.Flush ()

    let committedFirst =
        reports
        |> Seq.tryHead
        |> Option.bind List.tryHead
        |> Option.map _.Message
        |> Option.defaultValue ""

    equal
        ("failed: " + committedFirst, [ "content"; "content cleanup"; "error " + committedFirst ], [ [ "first"; "second" ] ])
        (host.textContent, List.ofSeq log, [ for errors in reports -> errors |> List.map _.Message |> List.sort ])

    mounted.Dispose ()

/// <summary>A content whose setup fails while <c>broken</c>; the error view's button retries it.</summary>
let private retry (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let mutable broken = true

    let flight =
        graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<string>()))

    let setups = ResizeArray<string>()
    let reports = ResizeArray<exn list>()
    let host = host ()

    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure ->
                Typed.button ops [| Typed.listener "click" (fun _ -> failure.Retry ()) |] [| ops.Text ("retry " + failure.Error.Message) |]),
            fun _ ->
                ops.Component (fun _ ->
                    setups.Add "content"

                    if broken then
                        failwith "setup"

                    Typed.reactiveText ops (fun () -> flight.Value.Value))
        )

    let mounted =
        mountWith (modeOf queued) graph None reports.Add host None (fun context -> run view context.Backend)

    let click () =
        (host.querySelector "button"
         |> Option.get
         |> unbox<HTMLButtonElement>)
            .click()

    equal ("retry setup", 1) (host.textContent, setups.Count)
    click ()
    equal ("retry setup", 2, 2) (host.textContent, setups.Count, reports.Count)
    broken <- false
    click ()
    equal ("loading", 3) (host.textContent, setups.Count)
    graph.Run (fun () -> flight.Value.Settle "ready")
    mounted.Flush ()
    equal ("ready", 2) (host.textContent, reports.Count)
    mounted.Dispose ()

/// <summary>An inner boundary's pending view fails; the outer boundary shows its error view.</summary>
let private pendingViewFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createAsyncSource<string>())
    let reports = ResizeArray<exn list>()
    let host = host ()

    let failed prefix (failure: BoundaryFailure) =
        ops.Text (prefix + failure.Error.Message)

    let inner =
        ops.Boundary (ops.Component (fun _ -> failwith "pending view"), failed "inner: ", fun _ -> Typed.reactiveText ops (fun () -> source.Value))

    let view =
        ops.Boundary (ops.Text "outer loading", failed "outer: ", (fun _ -> inner))

    let mounted =
        mountWith (modeOf queued) graph None reports.Add host None (fun context -> run view context.Backend)

    equal ("outer: pending view", [ [ "pending view" ] ]) (host.textContent, messages reports)
    mounted.Dispose ()

/// <summary>An inner boundary's content and error view both fail; the outer boundary shows its error view.</summary>
let private errorViewFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let reports = ResizeArray<exn list>()
    let host = host ()

    let failed prefix (failure: BoundaryFailure) =
        ops.Text (prefix + failure.Error.Message)

    let inner =
        ops.Boundary (
            ops.Text "inner loading",
            (fun _ -> ops.Component (fun _ -> failwith "error view")),
            fun _ -> ops.Component (fun _ -> failwith "content")
        )

    let view =
        ops.Boundary (ops.Text "outer loading", failed "outer: ", (fun _ -> inner))

    let mounted =
        mountWith (modeOf queued) graph None reports.Add host None (fun context -> run view context.Backend)

    equal ("outer: error view", [ [ "content" ]; [ "error view" ] ]) (host.textContent, messages reports)
    mounted.Dispose ()

let private dispatchFocus (node: HTMLElement) : unit =
    node.dispatchEvent (
        EventSpec.create
            (Conformance.document ())
            {
                Type = "focus"
                Kind = Focus
                Bubbles = false
                Cancelable = false
                Init = []
            }
    )
    |> ignore

/// <summary>Runs <c>action</c> and returns the messages of the listener failures the host reported meanwhile.</summary>
let private windowErrors (action: unit -> unit) : string[] =
    (Conformance.harness ()).ListenerErrors()
    |> ignore

    action ()

    (Conformance.harness ()).ListenerErrors()
    |> Array.ofList

let private fire (eventName: string) (button: HTMLButtonElement) =
    if eventName = "focus" then
        dispatchFocus button
    else
        button.click ()

/// <summary>A button whose <c>eventName</c> handler throws <c>NotReadyException</c>, under a boundary; the error view shows and the exception is reported once.</summary>
let private handlerNotReady (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued (eventName: string) =
    use graph = new Graph ()
    let pending = graph.Run (fun () -> Api.createAsyncSource<string>())
    let host, log, reports = host (), ResizeArray<string>(), ResizeArray<exn list>()

    let mounted =
        underBoundary ops run queued graph host log reports (fun () ->
            Typed.button ops [| Typed.listener eventName (fun _ -> pending.Value |> ignore) |] [| ops.Text "press" |])

    let button =
        host.querySelector "button"
        |> Option.get
        |> unbox<HTMLButtonElement>

    let escaped = windowErrors (fun () -> fire eventName button)

    equal (1, [ "content"; "content cleanup" ], [||]) (reports.Count, List.ofSeq log |> List.truncate 2, escaped)

    equal true (host.textContent.StartsWith "failed: ")
    mounted.Dispose ()

/// <summary>A button whose <c>eventName</c> handler throws, under a boundary; click is delegated, focus is direct.</summary>
let private handlerFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued (eventName: string) =
    use graph = new Graph ()
    let host, log, reports = host (), ResizeArray<string>(), ResizeArray<exn list>()

    let mounted =
        underBoundary ops run queued graph host log reports (fun () ->
            Typed.button ops [| Typed.listener eventName (fun _ -> failwith "handler") |] [| ops.Text "press" |])

    let button =
        host.querySelector "button"
        |> Option.get
        |> unbox<HTMLButtonElement>

    let escaped = windowErrors (fun () -> fire eventName button)

    equal
        ("failed: handler", [ "content"; "content cleanup"; "error handler" ], [ [ "handler" ] ], [||])
        (host.textContent, List.ofSeq log, messages reports, escaped)

    mounted.Dispose ()

/// <summary>Refetches a revealed boundary's source: the content stays with its last value and reads IsPending.</summary>
let private pendingAfterReveal (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()

    let flight =
        graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<string>()))

    let log = ResizeArray<string>()
    let host = host ()

    let view =
        ops.Boundary (
            ops.Component (fun _ ->
                log.Add "pending"
                ops.Text "loading"),
            (fun failure -> ops.Text failure.Error.Message),
            fun status ->
                ops.Fragment
                    [|
                        Typed.reactiveText ops (fun () -> flight.Value.Value)
                        Typed.reactiveText ops (fun () -> if status.IsPending () then "*" else ".")
                    |]
        )

    let mounted =
        mountWith (modeOf queued) graph None strict host None (fun context -> run view context.Backend)

    equal "loading" host.textContent
    graph.Run (fun () -> flight.Value.Settle "one")
    mounted.Flush ()
    equal "one." host.textContent
    graph.Run (fun () -> flight.Value <- Api.createAsyncSource<string>())
    mounted.Flush ()
    equal ("one*", 1) (host.textContent, log.Count)
    graph.Run (fun () -> flight.Value.Settle "two")
    mounted.Flush ()
    // The flush that releases the waiting binding writes IsPending; its text commits in the next flush.
    mounted.Flush ()
    equal ("two.", 1) (host.textContent, log.Count)
    mounted.Dispose ()

let private templateLog () =
    Conformance.local "BoundaryChecks.templateLog" (fun () -> ResizeArray<string>())

let private templateFlight () =
    Conformance.local "BoundaryChecks.templateFlight" (fun () -> ref (Unchecked.defaultof<_>: Signal<AsyncSource<string>>))

let private templatePending =
    Component.define
        (fun setup () ->
            (templateLog ()).Add "pending"
            setup.OnCleanup (fun () -> (templateLog ()).Add "pending cleanup"))
        (fun t -> t.span [||] [| t.text "loading" |])

let private templateFailed =
    Component.define (fun _ (failure: BoundaryFailure) -> failure) (fun t ->
        t.button [| t.Events.onClick (fun failure _ -> failure.Retry ()) |] [| t.text (fun failure -> "failed: " + failure.Error.Message) |])

let private templateRow =
    Component.define (fun _ (item: unit -> int) -> item) (fun t ->
        t.span
            [||]
            [|
                t.text (fun item ->
                    (templateFlight ()).Value.Value.Value
                    + string (item ()))
            |])

let private templateArm =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.span [||] [| t.text (fun () -> "|" + (templateFlight ()).Value.Value.Value) |])

let private templateContent =
    Component.define
        (fun setup (status: BoundaryStatus) ->
            (templateLog ()).Add "content"
            setup.OnCleanup (fun () -> (templateLog ()).Add "content cleanup")
            status)
        (fun t ->
            t.div
                [||]
                [|
                    t.text (fun _ -> (templateFlight ()).Value.Value.Value)
                    t.isolated (t.text (fun _ -> (templateFlight ()).Value.Value.Value))
                    t.each id (fun _ -> [| 1; 2 |]) templateRow
                    t.branch (fun _ -> 0) (fun _ -> t.arm templateArm ())
                    t.text (fun status -> if status.IsPending () then "*" else ".")
                |])

let private guarded =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary templatePending templateFailed templateContent |])

/// <summary>A template boundary reveals rows and arms in one insertion, then keeps them through a refetch.</summary>
let private templateReveal queued () =
    use graph = new Graph ()
    (templateLog ()).Clear()
    (templateFlight ()).Value <- graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<string>()))
    let host = host ()

    let mounted =
        mountWith (modeOf queued) graph None strict host None (fun context -> Program.run (Component.render guarded ()) context.Backend)

    equal ("loading", [ "content"; "pending" ]) (host.textContent, List.ofSeq (templateLog ()))

    let outer =
        host.querySelector "div"
        |> Option.get
        |> unbox<HTMLElement>

    let inserts = countInserts outer
    graph.Run (fun () -> (templateFlight ()).Value.Value.Settle "A")
    mounted.Flush ()
    equal ("AAA1A2|A.", 1) (host.textContent, inserts ())
    equal [ "content"; "pending"; "pending cleanup" ] (List.ofSeq (templateLog ()))
    graph.Run (fun () -> (templateFlight ()).Value.Value <- Api.createAsyncSource<string>())
    mounted.Flush ()
    equal "AAA1A2|A*" host.textContent
    graph.Run (fun () -> (templateFlight ()).Value.Value.Settle "B")
    mounted.Flush ()
    // The flush that releases the waiting bindings writes IsPending; its text commits in the next flush.
    mounted.Flush ()
    equal "BBB1B2|B." host.textContent
    mounted.Dispose ()
    equal ("content cleanup", "") ((templateLog ())[(templateLog ()).Count - 1], host.textContent)

let private rowNever () =
    Conformance.local "BoundaryChecks.rowNever" (fun () -> ref (Unchecked.defaultof<_>: AsyncSource<string>))

let private rowOther () =
    Conformance.local "BoundaryChecks.rowOther" (fun () -> ref (Unchecked.defaultof<_>: AsyncSource<string>))

let private rowItems () =
    Conformance.local "BoundaryChecks.rowItems" (fun () -> ref (Unchecked.defaultof<_>: Signal<int array>))

let private rowText (item: int) =
    if item = 2 then
        (rowNever ()).Value.Value
    else
        (rowOther ()).Value.Value

let private pendingRow =
    Component.define (fun _ (item: unit -> int) -> item) (fun t ->
        t.span
            [||]
            [|
                t.text (fun item -> rowText (item ()))
                t.isolated (t.text (fun item -> rowText (item ())))
            |])

let private pendingRows =
    Component.define (fun _ (_: BoundaryStatus) -> ()) (fun t -> t.div [||] [| t.each id (fun () -> (rowItems ()).Value.Value) pendingRow |])

let private guardedRows =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary templatePending templateFailed pendingRows |])

/// <summary>Removes the one template row still pending before the reveal; the boundary reveals the remaining row.</summary>
let private templateRemovedPendingRow () =
    use graph = new Graph ()
    (rowNever ()).Value <- graph.Run (fun () -> Api.createAsyncSource<string>())
    (rowOther ()).Value <- graph.Run (fun () -> Api.createAsyncSource<string>())
    (rowItems ()).Value <- graph.Run (fun () -> Api.createSignal [| 1; 2 |])
    let host = host ()

    let mounted =
        Mounting.mount graph None strict host None (fun context -> Program.run (Component.render guardedRows ()) context.Backend)

    graph.Run (fun () -> (rowOther ()).Value.Settle "x")
    equal "loading" host.textContent
    graph.Run (fun () -> (rowItems ()).Value.Value <- [| 1 |])
    equal "xx" host.textContent
    mounted.Dispose ()

let private armSources () =
    Conformance.local "BoundaryChecks.armSources" (fun () -> ref (null: AsyncSource<string> array))

let private armChoice () =
    Conformance.local "BoundaryChecks.armChoice" (fun () -> ref (Unchecked.defaultof<_>: Signal<int>))

let private sourceArm =
    Component.define (fun _ (choice: unit -> int) -> choice) (fun t ->
        t.span [||] [| t.text (fun choice -> (armSources ()).Value[choice ()].Value) |])

let private sourceArms =
    Component.define (fun _ (_: BoundaryStatus) -> ()) (fun t ->
        t.div
            [||]
            [|
                t.branch (fun () -> (armChoice ()).Value.Value) (fun choice -> t.arm sourceArm choice)
            |])

let private guardedArms =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary templatePending templateFailed sourceArms |])

/// <summary>Replacing a pending template arm with another pending arm keeps the boundary hidden until the new arm has a value.</summary>
let private templateArmReplacedWhilePending queued () =
    use graph = new Graph ()
    (armSources ()).Value <- graph.Run (fun () -> [| Api.createAsyncSource<string>(); Api.createAsyncSource<string>() |])
    (armChoice ()).Value <- graph.Run (fun () -> Api.createSignal 0)
    let host = host ()

    let mounted =
        mountWith (modeOf queued) graph None strict host None (fun context -> Program.run (Component.render guardedArms ()) context.Backend)

    let inserted =
        recordInserts (
            host.querySelector "div"
            |> Option.get
            |> unbox<Node>
        )

    graph.Run (fun () -> (armChoice ()).Value.Value <- 1)
    mounted.Flush ()
    equal "loading" host.textContent
    graph.Run (fun () -> (armSources ()).Value[1].Settle "B")
    mounted.Flush ()
    equal ("B", [| "B" |]) (host.textContent, inserted ())
    mounted.Dispose ()

let private renderSource () =
    Conformance.local "BoundaryChecks.renderSource" (fun () -> ref (Unchecked.defaultof<_>: AsyncSource<string>))

let private isolatedSource () =
    Conformance.local "BoundaryChecks.isolatedSource" (fun () -> ref (Unchecked.defaultof<_>: AsyncSource<string>))

let private slotKinds =
    Component.define (fun _ (_: BoundaryStatus) -> ()) (fun t ->
        t.span
            [||]
            [|
                t.text (fun () -> (renderSource ()).Value.Value)
                t.isolated (t.text (fun () -> (isolatedSource ()).Value.Value))
            |])

let private guardedSlots =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary templatePending templateFailed slotKinds |])

/// <summary>A boundary over a pending render slot and a pending isolated slot reveals once both settle, in either order.</summary>
let private templateSlotKinds renderFirst () =
    use graph = new Graph ()
    (renderSource ()).Value <- graph.Run (fun () -> Api.createAsyncSource<string>())
    (isolatedSource ()).Value <- graph.Run (fun () -> Api.createAsyncSource<string>())
    let host = host ()

    let mounted =
        Mounting.mount graph None strict host None (fun context -> Program.run (Component.render guardedSlots ()) context.Backend)

    let first, second =
        if renderFirst then
            (renderSource ()).Value, (isolatedSource ()).Value
        else
            (isolatedSource ()).Value, (renderSource ()).Value

    graph.Run (fun () -> first.Settle "a")
    equal "loading" host.textContent
    graph.Run (fun () -> second.Settle "b")

    equal (if renderFirst then "ab" else "ba") host.textContent
    mounted.Dispose ()

type private Fragile = { Value: Signal<string> }

let private fragileValue () =
    Conformance.local "BoundaryChecks.fragileValue" (fun () -> ref (Unchecked.defaultof<_>: Signal<string>))

let private fragileBroken () =
    Conformance.local "BoundaryChecks.fragileBroken" (fun () -> ref false)

let private fragileContent =
    Component.define
        (fun setup (_: BoundaryStatus) ->
            if (fragileBroken ()).Value then
                failwith "setup"

            (templateLog ()).Add "content"
            setup.OnCleanup (fun () -> (templateLog ()).Add "content cleanup")
            { Value = (fragileValue ()).Value })
        (fun t ->
            t.button
                [|
                    t.Props.propertyWith (
                        (fun (_: HTMLButtonElement) (value: string) ->
                            if value = "bad" then
                                failwith "setter"),
                        (fun fragile -> fragile.Value.Value),
                        primitiveSame
                    )
                    t.Events.onClick (fun _ _ -> failwith "handler")
                    t.Events.onFocus (fun _ _ -> failwith "focus handler")
                |]
                [| t.text (fun fragile -> fragile.Value.Value) |])

let private fragile =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary templatePending templateFailed fragileContent |])

/// <summary>
/// Mounts the fragile template boundary, runs <c>trigger</c>, and expects the error view for <c>message</c> after the
/// content's cleanups ran once.
/// </summary>
let private templateFailure queued (trigger: Graph -> HTMLElement -> unit) (message: string) () =
    use graph = new Graph ()
    (templateLog ()).Clear()
    (fragileBroken ()).Value <- false
    (fragileValue ()).Value <- graph.Run (fun () -> Api.createSignal "ok")
    let host = host ()
    let reports = ResizeArray<exn list>()

    let mounted =
        mountWith (modeOf queued) graph None reports.Add host None (fun context -> Program.run (Component.render fragile ()) context.Backend)

    equal "ok" host.textContent
    trigger graph host
    mounted.Flush ()

    equal ("failed: " + message, [ [ message ] ], [ "content"; "content cleanup" ]) (host.textContent, messages reports, List.ofSeq (templateLog ()))

    mounted.Dispose ()

let private button (host: HTMLElement) =
    host.querySelector "button"
    |> Option.get
    |> unbox<HTMLButtonElement>

/// <summary>A template content whose setup fails while broken; the error view's button retries it.</summary>
let private templateRetry () =
    use graph = new Graph ()
    (fragileBroken ()).Value <- true
    (fragileValue ()).Value <- graph.Run (fun () -> Api.createSignal "ok")
    let host = host ()
    let reports = ResizeArray<exn list>()

    let mounted =
        Mounting.mount graph None reports.Add host None (fun context -> Program.run (Component.render fragile ()) context.Backend)

    equal "failed: setup" host.textContent
    (button host).click()
    equal ("failed: setup", 2) (host.textContent, reports.Count)
    (fragileBroken ()).Value <- false
    (button host).click()
    equal ("ok", 2) (host.textContent, reports.Count)
    mounted.Dispose ()

let private handlerSource () =
    Conformance.local "BoundaryChecks.handlerSource" (fun () -> ref (Unchecked.defaultof<_>: AsyncSource<string>))

let private waitingHandler =
    Component.define (fun _ (_: BoundaryStatus) -> ()) (fun t ->
        t.button
            [|
                t.Events.onClick (fun _ _ -> (handlerSource ()).Value.Value |> ignore)
                t.Events.onFocus (fun _ _ -> (handlerSource ()).Value.Value |> ignore)
            |]
            [| t.text "press" |])

let private guardedHandler =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary templatePending templateFailed waitingHandler |])

/// <summary>A template handler reading a pending source under a boundary shows the error view and is reported once.</summary>
let private templateHandlerNotReady (trigger: HTMLButtonElement -> unit) () =
    use graph = new Graph ()
    (handlerSource ()).Value <- graph.Run (fun () -> Api.createAsyncSource<string>())
    let host = host ()
    let reports = ResizeArray<exn list>()

    let mounted =
        Mounting.mount graph None reports.Add host None (fun context -> Program.run (Component.render guardedHandler ()) context.Backend)

    equal "press" host.textContent
    let escaped = windowErrors (fun () -> trigger (button host))
    equal (1, true, [||]) (reports.Count, host.textContent.StartsWith "failed: ", escaped)
    mounted.Dispose ()

let private secondHandlerLog () =
    Conformance.local "BoundaryChecks.secondHandlerLog" (fun () -> ResizeArray<string>())

let private twoHandlers =
    Component.define (fun _ (_: BoundaryStatus) -> ()) (fun t ->
        t.button
            [|
                t.Events.onClick (fun _ _ -> failwith "first")
                t.Events.onClick (fun _ _ -> (secondHandlerLog ()).Add "second")
                t.Events.onFocus (fun _ _ -> failwith "first")
                t.Events.onFocus (fun _ _ -> (secondHandlerLog ()).Add "second")
            |]
            [| t.text "press" |])

let private guardedTwoHandlers =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary templatePending templateFailed twoHandlers |])

/// <summary>Two handlers on one node and event under a boundary: the first throws and tears the content down, and the second is skipped.</summary>
let private templateSecondHandler (trigger: HTMLButtonElement -> unit) () =
    use graph = new Graph ()
    (secondHandlerLog ()).Clear()
    let host = host ()
    let reports = ResizeArray<exn list>()

    let mounted =
        Mounting.mount graph None reports.Add host None (fun context -> Program.run (Component.render guardedTwoHandlers ()) context.Backend)

    trigger (button host)

    equal ("failed: first", [ [ "first" ] ], []) (host.textContent, messages reports, List.ofSeq (secondHandlerLog ()))

    mounted.Dispose ()

let private serverSource () =
    Conformance.local "BoundaryChecks.serverSource" (fun () -> ref (Unchecked.defaultof<_>: AsyncSource<string>))

let private serverPending =
    Component.define (fun _ () -> ()) (fun t -> t.span [||] [| t.text "loading" |])

let private serverFailed =
    Component.define (fun _ (failure: BoundaryFailure) -> failure) (fun t -> t.span [||] [| t.text (fun failure -> failure.Error.Message) |])

let private serverContent =
    Component.define (fun _ (status: BoundaryStatus) -> status) (fun t -> t.span [||] [| t.text (fun _ -> (serverSource ()).Value.Value) |])

let private guardedServer =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary serverPending serverFailed serverContent |])

/// <summary>Renders the template boundary as HTML while its source is pending, settled and failed.</summary>
let private templateServer () =
    use graph = new Graph ()

    graph.Run (fun () ->
        Api.createRoot (fun _ ->
            (serverSource ()).Value <- Api.createAsyncSource<string>()

            let render () =
                Html.render Omit (Program.run (Component.render guardedServer ()))

            equal "<div><!--[p--><span>loading</span><!--]--></div>" (render ())
            (serverSource ()).Value.Settle "<A>"
            equal "<div><!--[c--><span>&lt;A&gt;</span><!--]--></div>" (render ())
            (serverSource ()).Value.Fail(Exception "<bad>")
            equal "<div><!--[e--><span>&lt;bad&gt;</span><!--]--></div>" (render ())))

/// <summary>A handler outside every boundary throws out of its dispatch.</summary>
let private handlerOutsideBoundary (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued (eventName: string) =
    use graph = new Graph ()
    let host, reports = host (), ResizeArray<exn list>()

    let view =
        Typed.button ops [| Typed.listener eventName (fun _ -> failwith "handler") |] [| ops.Text "press" |]

    let mounted =
        mountWith (modeOf queued) graph None reports.Add host None (fun context -> run view context.Backend)

    let button =
        host.querySelector "button"
        |> Option.get
        |> unbox<HTMLButtonElement>

    let escaped = windowErrors (fun () -> fire eventName button)

    equal ([| "handler" |], [], "press") (escaped, messages reports, host.textContent)
    mounted.Dispose ()

/// <summary>
/// A click handler sets a signal that fails a content setter, then throws. Synchronously, both errors are reported, in one
/// teardown. Queued, only the handler's error is reported. With <c>sameObject</c> both are one exception object, reported once.
/// </summary>
let private handlerAfterFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued sameObject =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "ok")
    let host, log, reports = host (), ResizeArray<string>(), ResizeArray<exn list>()
    let shared = Exception "shared"

    let mounted =
        underBoundary ops run queued graph host log reports (fun () ->
            Typed.button
                ops
                [|
                    Typed.reactiveProperty
                        (fun (node: HTMLButtonElement) (value: string) ->
                            if value = "bad" then
                                if sameObject then raise shared else failwith "setter"

                            node.title <- value)
                        (fun () -> source.Value)
                    Typed.listener "click" (fun _ ->
                        source.Value <- "bad"
                        if sameObject then raise shared else failwith "handler")
                |]
                [| ops.Text "press" |])

    let button =
        host.querySelector "button"
        |> Option.get
        |> unbox<HTMLButtonElement>

    let escaped = windowErrors (fun () -> button.click ())
    mounted.Flush ()

    let shown =
        reports
        |> Seq.tryHead
        |> Option.bind List.tryHead
        |> Option.map _.Message
        |> Option.defaultValue ""

    let reported =
        [
            for errors in reports do
                yield! errors |> List.map _.Message
        ]
        |> List.sort

    let expectedReports =
        if sameObject then [ "shared" ]
        elif queued then [ "handler" ]
        else [ "handler"; "setter" ]

    equal
        ("failed: " + shown, [ "content"; "content cleanup"; "error " + shown ], expectedReports, [||])
        (host.textContent, List.ofSeq log, reported, escaped)

    mounted.Dispose ()

/// <summary>A content whose setup raises one exception object on every attempt.</summary>
let private retryCachedFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let cached = Exception "cached"
    let reports = ResizeArray<exn list>()
    let host = host ()

    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure ->
                Typed.button ops [| Typed.listener "click" (fun _ -> failure.Retry ()) |] [| ops.Text ("retry " + failure.Error.Message) |]),
            fun _ -> ops.Component (fun _ -> raise cached)
        )

    let mounted =
        mountWith (modeOf queued) graph None reports.Add host None (fun context -> run view context.Backend)

    (host.querySelector "button"
     |> Option.get
     |> unbox<HTMLButtonElement>)
        .click()

    equal ("retry cached", [ [ "cached" ]; [ "cached" ] ]) (host.textContent, messages reports)
    mounted.Dispose ()

/// <summary>Two readers of one failed memo under a boundary receive the memo's exception object.</summary>
let private sharedReaderFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "ok")

    let memo =
        graph.Run (fun () ->
            Api.createMemo (fun _ ->
                if source.Value = "bad" then
                    failwith "memo"
                else
                    source.Value))

    let host, log, reports = host (), ResizeArray<string>(), ResizeArray<exn list>()

    let mounted =
        underBoundary ops run queued graph host log reports (fun () ->
            ops.Fragment
                [|
                    Typed.reactiveText ops (fun () -> memo.Value)
                    Typed.reactiveText ops (fun () -> memo.Value)
                |])

    equal "okok" host.textContent
    graph.Run (fun () -> source.Value <- "bad")
    mounted.Flush ()

    let expected =
        "failed: memo", [ "content"; "content cleanup"; "error memo" ], [ [ "memo" ] ]

    equal expected (host.textContent, List.ofSeq log, messages reports)
    graph.Run (fun () -> source.Value <- "bad again")
    mounted.Flush ()
    equal expected (host.textContent, List.ofSeq log, messages reports)
    mounted.Dispose ()

/// <summary>Setters under an inner boundary and under the outer boundary enclosing it fail in one queued flush.</summary>
let private nestedQueuedFailures (ops: Operations<'view>) (run: 'view -> Backend -> unit) =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "ok")
    let reports = ResizeArray<exn list>()
    let host = host ()

    let failing (message: string) =
        Typed.reactiveProperty
            (fun (node: HTMLDivElement) (value: string) ->
                if value = "bad" then
                    failwith message

                node.title <- value)
            (fun () -> source.Value)

    let failed prefix (failure: BoundaryFailure) =
        ops.Text (prefix + failure.Error.Message)

    let inner =
        ops.Boundary (ops.Text "inner loading", failed "inner: ", (fun _ -> Typed.div ops [| failing "inner" |] [||]))

    let view =
        ops.Boundary (ops.Text "outer loading", failed "outer: ", fun _ -> ops.Fragment [| Typed.div ops [| failing "outer" |] [||]; inner |])

    let mounted =
        mountWith (modeOf true) graph None reports.Add host None (fun context -> run view context.Backend)

    graph.Run (fun () -> source.Value <- "bad")
    mounted.Flush ()

    equal
        ("outer: outer", [ "inner"; "outer" ])
        (host.textContent,
         [
             for errors in reports do
                 for error in errors -> error.Message
         ]
         |> List.sort)

    mounted.Dispose ()

/// <summary>A setter outside a boundary that fails while <c>trigger</c> is <c>"bad"</c>, ahead of the boundary.</summary>
let private failingOutside (ops: Operations<'view>) (trigger: Signal<string>) (boundary: 'view) =
    ops.Fragment
        [|
            Typed.div
                ops
                [|
                    Typed.reactiveProperty
                        (fun (node: HTMLDivElement) (value: string) ->
                            if value = "bad" then
                                failwith "setter"

                            node.title <- value)
                        (fun () -> trigger.Value)
                |]
                [||]
            boundary
        |]

/// <summary>
/// Under <c>FailFastCancel</c>, a failure outside a revealed boundary stops the flush ahead of the commit that settles
/// a refetch: the content keeps its last value and IsPending clears.
/// </summary>
let private cancelledAfterReveal (ops: Operations<'view>) (run: 'view -> Backend -> unit) =
    use graph = new Graph ()
    let trigger = graph.Run (fun () -> Api.createSignal "ok")

    let flight =
        graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<string>()))

    let reports = ResizeArray<exn list>()
    let host = host ()

    let view =
        failingOutside
            ops
            trigger
            (ops.Boundary (
                ops.Text "loading",
                (fun failure -> ops.Text failure.Error.Message),
                fun status ->
                    ops.Fragment
                        [|
                            Typed.reactiveText ops (fun () -> flight.Value.Value)
                            Typed.reactiveText ops (fun () -> if status.IsPending () then "*" else ".")
                        |]
            ))

    let mounted =
        mountWith (QueuedCommits (FailFastCancel, ignore)) graph None reports.Add host None (fun context -> run view context.Backend)

    graph.Run (fun () -> flight.Value.Settle "1")
    mounted.Flush ()
    equal "1." host.textContent
    graph.Run (fun () -> flight.Value <- Api.createAsyncSource<string>())
    mounted.Flush ()
    equal "1*" host.textContent
    graph.Run (fun () -> trigger.Value <- "bad")
    graph.Run (fun () -> flight.Value.Settle "2")
    mounted.Flush ()
    // The flush that releases the waiting binding writes IsPending; its text commits in the next flush.
    mounted.Flush ()
    equal ("1.", [ [ "setter" ] ]) (host.textContent, messages reports)
    mounted.Dispose ()

/// <summary>
/// Under <c>FailFastCancel</c>, a failure outside a hidden boundary stops the flush ahead of the commit that settles its
/// last waiting binding: the boundary reveals the content with that binding's last value.
/// </summary>
let private cancelledBeforeReveal (ops: Operations<'view>) (run: 'view -> Backend -> unit) =
    use graph = new Graph ()
    let trigger = graph.Run (fun () -> Api.createSignal "ok")
    let first = graph.Run (fun () -> Api.createAsyncSource<string>())

    let flight =
        graph.Run (fun () ->
            let source = Api.createAsyncSource<string>()
            source.Settle "1"
            Api.createSignal source)

    let reports = ResizeArray<exn list>()
    let host = host ()

    let view =
        failingOutside
            ops
            trigger
            (ops.Boundary (
                ops.Text "loading",
                (fun failure -> ops.Text failure.Error.Message),
                fun _ ->
                    ops.Fragment
                        [|
                            Typed.reactiveText ops (fun () -> first.Value)
                            Typed.reactiveText ops (fun () -> flight.Value.Value)
                        |]
            ))

    let mounted =
        mountWith (QueuedCommits (FailFastCancel, ignore)) graph None reports.Add host None (fun context -> run view context.Backend)

    equal "loading" host.textContent
    graph.Run (fun () -> flight.Value <- Api.createAsyncSource<string>())
    graph.Run (fun () -> first.Settle "A")
    mounted.Flush ()
    equal "loading" host.textContent
    graph.Run (fun () -> trigger.Value <- "bad")
    graph.Run (fun () -> flight.Value.Settle "2")
    mounted.Flush ()
    mounted.Flush ()
    equal ("A1", [ [ "setter" ] ]) (host.textContent, messages reports)
    mounted.Dispose ()

/// <summary>
/// A second refetch starts after the reveal while a first is in flight: IsPending stays set until both settle, and each
/// value commits when its source settles.
/// </summary>
let private overlappingWaits (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()

    let settled (value: string) =
        let source = Api.createAsyncSource<string>()
        source.Settle value
        source

    let left = graph.Run (fun () -> Api.createSignal (settled "1"))
    let right = graph.Run (fun () -> Api.createSignal (settled "2"))
    let host = host ()

    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure -> ops.Text failure.Error.Message),
            fun status ->
                ops.Fragment
                    [|
                        Typed.reactiveText ops (fun () -> left.Value.Value)
                        Typed.reactiveText ops (fun () -> right.Value.Value)
                        Typed.reactiveText ops (fun () -> if status.IsPending () then "*" else ".")
                    |]
        )

    let mounted =
        mountWith (modeOf queued) graph None strict host None (fun context -> run view context.Backend)

    // A queued IsPending text commits in the flush after the one that changes it.
    let flushTwice () =
        mounted.Flush ()
        mounted.Flush ()

    equal "12." host.textContent
    graph.Run (fun () -> left.Value <- Api.createAsyncSource<string>())
    flushTwice ()
    equal "12*" host.textContent
    graph.Run (fun () -> right.Value <- Api.createAsyncSource<string>())
    flushTwice ()
    equal "12*" host.textContent
    graph.Run (fun () -> left.Value.Settle "3")
    flushTwice ()
    equal "32*" host.textContent
    graph.Run (fun () -> right.Value.Settle "4")
    flushTwice ()
    equal "34." host.textContent
    mounted.Dispose ()

let cases: Conformance.Case array =
    Conformance.ofChecks
        [|
            "tree HTML renders a boundary's pending, content or error view between markers", fun () -> server Tree.ops Tree.run
            "program HTML renders a boundary's pending, content or error view between markers", fun () -> server Program.ops Program.run
            "tree synchronous: a boundary reveals its content in one insertion once every binding, row and arm has a value",
            fun () -> initialReveal Tree.ops Tree.run false
            "tree queued: a boundary reveals its content in one insertion once every binding, row and arm has a value",
            fun () -> initialReveal Tree.ops Tree.run true
            "program synchronous: a boundary reveals its content in one insertion once every binding, row and arm has a value",
            fun () -> initialReveal Program.ops Program.run false
            "program queued: a boundary reveals its content in one insertion once every binding, row and arm has a value",
            fun () -> initialReveal Program.ops Program.run true
            "tree synchronous: a nested boundary reveals after its outer boundary", fun () -> nestedReveal Tree.ops Tree.run false false
            "program queued: a nested boundary reveals after its outer boundary", fun () -> nestedReveal Program.ops Program.run true false
            "tree queued: a nested boundary revealed first appears with its outer boundary", fun () -> nestedReveal Tree.ops Tree.run true true
            "program synchronous: a nested boundary revealed first appears with its outer boundary",
            fun () -> nestedReveal Program.ops Program.run false true
            "tree synchronous: disposal during the pending phase disposes the hidden content and the pending view",
            fun () -> disposedWhilePending Tree.ops Tree.run false
            "program queued: disposal during the pending phase disposes the hidden content and the pending view",
            fun () -> disposedWhilePending Program.ops Program.run true
            "tree queued: a boundary moved while pending reveals at its new parent and handles clicks there",
            fun () -> movedWhilePending Tree.ops Tree.run true
            "program synchronous: a boundary moved while pending reveals at its new parent and handles clicks there",
            fun () -> movedWhilePending Program.ops Program.run false
            "tree synchronous: removing the last pending row reveals the boundary", fun () -> removedPendingRow Tree.ops Tree.run false
            "program queued: removing the last pending row reveals the boundary", fun () -> removedPendingRow Program.ops Program.run true
            "tree synchronous: replacing the last pending row reveals both rows after the pass", fun () -> replacedPendingRow Tree.ops Tree.run false
            "program queued: replacing the last pending row reveals both rows after the pass",
            fun () -> replacedPendingRow Program.ops Program.run true
            "tree synchronous: replacing a pending arm with another pending arm keeps the boundary hidden",
            fun () -> armReplacedWhilePending Tree.ops Tree.run false
            "tree queued: replacing a pending arm with another pending arm keeps the boundary hidden",
            fun () -> armReplacedWhilePending Tree.ops Tree.run true
            "program synchronous: replacing a pending arm with another pending arm keeps the boundary hidden",
            fun () -> armReplacedWhilePending Program.ops Program.run false
            "program queued: replacing a pending arm with another pending arm keeps the boundary hidden",
            fun () -> armReplacedWhilePending Program.ops Program.run true
#if RANVIER_VIEW_COUNTERS
            "tree synchronous: a boundary writes no wake while its content sets up", fun () -> setupWakes Tree.ops Tree.run false
            "program queued: a boundary writes no wake while its content sets up", fun () -> setupWakes Program.ops Program.run true
#endif
            "tree synchronous: replacing the one pending row with another pending row keeps the boundary hidden",
            fun () -> rowReplacedWhilePending Tree.ops Tree.run false
            "program queued: replacing the one pending row with another pending row keeps the boundary hidden",
            fun () -> rowReplacedWhilePending Program.ops Program.run true
            "tree synchronous: rows added after the reveal set IsPending only while one of them is pending",
            fun () -> rowsAddedAfterReveal Tree.ops Tree.run false
            "program queued: rows added after the reveal set IsPending only while one of them is pending",
            fun () -> rowsAddedAfterReveal Program.ops Program.run true
            "tree synchronous: a failed reader under a boundary shows the error view, cleans up once and reports once",
            fun () -> readerFailure Tree.ops Tree.run false
            "program queued: a failed reader under a boundary shows the error view, cleans up once and reports once",
            fun () -> readerFailure Program.ops Program.run true
            "tree synchronous: a live setter failure under a boundary replaces the content with the error view",
            fun () -> setterFailure Tree.ops Tree.run false
            "program synchronous: a live setter failure under a boundary replaces the content with the error view",
            fun () -> setterFailure Program.ops Program.run false
            "tree queued: a queued setter failure under a boundary replaces the content after the flush",
            fun () -> setterFailure Tree.ops Tree.run true
            "program queued: a queued setter failure under a boundary replaces the content after the flush",
            fun () -> setterFailure Program.ops Program.run true
            "tree queued: a row setup failure under a boundary shows the error view", fun () -> rowSetupFailure Tree.ops Tree.run true
            "program synchronous: a row setup failure under a boundary shows the error view", fun () -> rowSetupFailure Program.ops Program.run false
            "tree synchronous: an arm setup failure under a boundary shows the error view", fun () -> armSetupFailure Tree.ops Tree.run false
            "program queued: an arm setup failure under a boundary shows the error view", fun () -> armSetupFailure Program.ops Program.run true
            "tree: two setters failing in one queued flush tear the content down once", fun () -> twoQueuedFailures Tree.ops Tree.run
            "program: two setters failing in one queued flush tear the content down once", fun () -> twoQueuedFailures Program.ops Program.run
            "tree synchronous: retry remounts the content with a new setup and pending phase", fun () -> retry Tree.ops Tree.run false
            "program queued: retry remounts the content with a new setup and pending phase", fun () -> retry Program.ops Program.run true
            "tree queued: a failing pending view goes to the enclosing boundary", fun () -> pendingViewFailure Tree.ops Tree.run true
            "program synchronous: a failing pending view goes to the enclosing boundary", fun () -> pendingViewFailure Program.ops Program.run false
            "tree synchronous: a failing error view goes to the enclosing boundary", fun () -> errorViewFailure Tree.ops Tree.run false
            "program queued: a failing error view goes to the enclosing boundary", fun () -> errorViewFailure Program.ops Program.run true
            "tree synchronous: a retry reports again an exception object reported before the retry",
            fun () -> retryCachedFailure Tree.ops Tree.run false
            "program queued: a retry reports again an exception object reported before the retry",
            fun () -> retryCachedFailure Program.ops Program.run true
            "tree queued: two readers of one failed memo under a boundary report it once and tear down once",
            fun () -> sharedReaderFailure Tree.ops Tree.run true
            "program synchronous: two readers of one failed memo under a boundary report it once and tear down once",
            fun () -> sharedReaderFailure Program.ops Program.run false
            "tree: an inner and an outer failure in one queued flush are each reported once", fun () -> nestedQueuedFailures Tree.ops Tree.run
            "program: an inner and an outer failure in one queued flush are each reported once",
            fun () -> nestedQueuedFailures Program.ops Program.run
            "tree synchronous: a content binding failing on its first read shows the error view",
            fun () -> initialReaderFailure Tree.ops Tree.run false
            "tree queued: a content binding failing on its first read shows the error view", fun () -> initialReaderFailure Tree.ops Tree.run true
            "program synchronous: a content binding failing on its first read shows the error view",
            fun () -> initialReaderFailure Program.ops Program.run false
            "program queued: a content binding failing on its first read shows the error view",
            fun () -> initialReaderFailure Program.ops Program.run true
            "tree synchronous: a throwing delegated handler under a boundary shows the error view",
            fun () -> handlerFailure Tree.ops Tree.run false "click"
            "program queued: a throwing delegated handler under a boundary shows the error view",
            fun () -> handlerFailure Program.ops Program.run true "click"
            "tree queued: a throwing direct handler under a boundary shows the error view", fun () -> handlerFailure Tree.ops Tree.run true "focus"
            "program synchronous: a throwing direct handler under a boundary shows the error view",
            fun () -> handlerFailure Program.ops Program.run false "focus"
            "tree synchronous: a pending read after the reveal keeps the content and sets IsPending",
            fun () -> pendingAfterReveal Tree.ops Tree.run false
            "tree queued: a pending read after the reveal keeps the content and sets IsPending", fun () -> pendingAfterReveal Tree.ops Tree.run true
            "program synchronous: a pending read after the reveal keeps the content and sets IsPending",
            fun () -> pendingAfterReveal Program.ops Program.run false
            "program queued: a pending read after the reveal keeps the content and sets IsPending",
            fun () -> pendingAfterReveal Program.ops Program.run true
            "program queued: a fail-fast flush stopped outside a revealed boundary clears IsPending after a refetch settles",
            fun () -> cancelledAfterReveal Program.ops Program.run
            "program queued: a fail-fast flush stopped outside a hidden boundary still reveals it once every source settles",
            fun () -> cancelledBeforeReveal Program.ops Program.run
            "tree synchronous: overlapping refetches after the reveal keep IsPending set until both settle",
            fun () -> overlappingWaits Tree.ops Tree.run false
            "program queued: overlapping refetches after the reveal keep IsPending set until both settle",
            fun () -> overlappingWaits Program.ops Program.run true
            "tree synchronous: a delegated handler throwing NotReadyException under a boundary shows the error view",
            fun () -> handlerNotReady Tree.ops Tree.run false "click"
            "program queued: a delegated handler throwing NotReadyException under a boundary shows the error view",
            fun () -> handlerNotReady Program.ops Program.run true "click"
            "tree queued: a direct handler throwing NotReadyException under a boundary shows the error view",
            fun () -> handlerNotReady Tree.ops Tree.run true "focus"
            "program synchronous: a direct handler throwing NotReadyException under a boundary shows the error view",
            fun () -> handlerNotReady Program.ops Program.run false "focus"
            "tree synchronous: a throwing delegated handler outside every boundary throws out of its dispatch",
            fun () -> handlerOutsideBoundary Tree.ops Tree.run false "click"
            "program queued: a throwing direct handler outside every boundary throws out of its dispatch",
            fun () -> handlerOutsideBoundary Program.ops Program.run true "focus"
            "tree synchronous: a handler exception after the error view is shown tears down once and is reported once",
            fun () -> handlerAfterFailure Tree.ops Tree.run false false
            "program synchronous: a handler exception after the error view is shown tears down once and is reported once",
            fun () -> handlerAfterFailure Program.ops Program.run false false
            "tree queued: a handler exception tears the content down before a failing setter commits and is reported once",
            fun () -> handlerAfterFailure Tree.ops Tree.run true false
            "tree synchronous: the same exception object from a handler and a setter is reported once",
            fun () -> handlerAfterFailure Tree.ops Tree.run false true
            "tree queued: the same exception object from a handler and a setter is reported once",
            fun () -> handlerAfterFailure Tree.ops Tree.run true true
            "program synchronous: the same exception object from a handler and a setter is reported once",
            fun () -> handlerAfterFailure Program.ops Program.run false true
            "template synchronous: a boundary reveals rows and arms in one insertion and keeps them through a refetch", templateReveal false
            "template queued: a boundary reveals rows and arms in one insertion and keeps them through a refetch", templateReveal true
            "template synchronous: a live setter failure under a boundary shows the error view",
            templateFailure false (fun graph _ -> graph.Run (fun () -> (fragileValue ()).Value.Value <- "bad")) "setter"
            "template queued: a live setter failure under a boundary shows the error view",
            templateFailure true (fun graph _ -> graph.Run (fun () -> (fragileValue ()).Value.Value <- "bad")) "setter"
            "template: a throwing delegated handler under a boundary shows the error view",
            templateFailure false (fun _ host -> (button host).click()) "handler"
            "template: a throwing direct handler under a boundary shows the error view",
            templateFailure false (fun _ host -> dispatchFocus (button host)) "focus handler"
            "template: a delegated handler throwing NotReadyException under a boundary shows the error view",
            templateHandlerNotReady (fun button -> button.click ())
            "template: a direct handler throwing NotReadyException under a boundary shows the error view", templateHandlerNotReady dispatchFocus
            "template: a boundary waits for a pending isolated slot after its render slots settle", templateSlotKinds true
            "template: a boundary waits for a pending render slot after its isolated slots settle", templateSlotKinds false
            "template: a delegated handler after one whose failure tore the content down is skipped",
            templateSecondHandler (fun button -> button.click ())
            "template: a direct handler after one whose failure tore the content down is skipped", templateSecondHandler dispatchFocus
            "template: retry remounts a content whose setup failed", templateRetry
            "template: removing the one pending row before the reveal reveals the remaining row", templateRemovedPendingRow
            "template synchronous: replacing a pending arm with another pending arm keeps the boundary hidden", templateArmReplacedWhilePending false
            "template queued: replacing a pending arm with another pending arm keeps the boundary hidden", templateArmReplacedWhilePending true
            "template HTML renders a boundary's pending, content or error view between markers", templateServer
        |]
