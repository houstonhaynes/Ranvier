module PlacementChecks

open System
open Ranvier
open Fable.Core.TS.Dom
open Ranvier.View.Testing
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Dom.Scheduling
open Ranvier.View.Dom.Mounting
open CheckSupport

/// <summary>A div whose mounted hook records whether the div is in the host when the hook and its cleanup run.</summary>
let private placedInHost (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let log = ResizeArray<string>()
    let target = host ()

    let view =
        Typed.div
            ops
            [|
                Typed.mounted (fun (node: HTMLDivElement) ->
                    log.Add (sprintf "hook %b" (target.contains node))
                    fun () -> log.Add (sprintf "cleanup %b" (target.contains node)))
            |]
            [||]

    let mounted = mountView ops run queued graph strict target view
    equal [ "hook true" ] (List.ofSeq log)
    mounted.Dispose ()
    equal ([ "hook true"; "cleanup true" ], 0.) (List.ofSeq log, target.childNodes.length)

let private browserHookUnplaced (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let log = ResizeArray<string>()
    let target = host ()

    let view =
        Typed.div
            ops
            [|
                Typed.browser (fun (node: HTMLDivElement) ->
                    log.Add (sprintf "browser %b" (target.contains node))
                    ignore)
                Typed.mounted (fun (node: HTMLDivElement) ->
                    log.Add (sprintf "mounted %b" (target.contains node))
                    ignore)
            |]
            [||]

    let mounted = mountView ops run queued graph strict target view
    equal [ "browser false"; "mounted true" ] (List.ofSeq log)
    mounted.Dispose ()

/// <summary>A component's div, an inlined child component's span and two list rows, each with a hook that logs its name.</summary>
let private setupOrder (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let log = ResizeArray<string>()
    let target = host ()

    let hook (name: string) : Typed.Modifier<'native> =
        Typed.mounted (fun _ ->
            log.Add name
            ignore)

    let view =
        ops.Component (fun _ ->
            Typed.div
                ops
                [| hook "parent" |]
                [|
                    ops.Component (fun _ -> span ops [| hook "child" |] [||])
                    Keyed.list ops id (fun () -> [ 1; 2 ]) (fun current -> span ops [| hook ("row " + string (current ())) |] [||])
                |])

    let mounted = mountView ops run queued graph strict target view
    equal [ "parent"; "child"; "row 1"; "row 2" ] (List.ofSeq log)
    mounted.Dispose ()

/// <summary>A component's div holding an inlined child component's span followed by its own span, each logging its name from a hook.</summary>
let private inlinedThenSibling (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let log = ResizeArray<string>()
    let target = host ()

    let hook (name: string) : Typed.Modifier<'native> =
        Typed.mounted (fun _ ->
            log.Add name
            ignore)

    let view =
        ops.Component (fun _ ->
            Typed.div
                ops
                [| hook "parent" |]
                [|
                    ops.Component (fun _ -> span ops [| hook "child" |] [||])
                    span ops [| hook "after" |] [||]
                |])

    let mounted = mountView ops run queued graph strict target view
    equal [ "parent"; "after"; "child" ] (List.ofSeq log)
    mounted.Dispose ()

/// <summary>
/// Inlined children two levels deep, each followed by a span of its parent: every instance's hooks run before its inlined
/// children's, in setup order.
/// </summary>
let private inlinedNested (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let log = ResizeArray<string>()
    let target = host ()

    let hook (name: string) : Typed.Modifier<'native> =
        Typed.mounted (fun _ ->
            log.Add name
            ignore)

    let grandchild = ops.Component (fun _ -> span ops [| hook "grandchild" |] [||])

    let child =
        ops.Component (fun _ -> Typed.div ops [| hook "child" |] [| grandchild; span ops [| hook "child after" |] [||] |])

    let second = ops.Component (fun _ -> span ops [| hook "second" |] [||])

    let view =
        ops.Component (fun _ -> Typed.div ops [| hook "parent" |] [| child; second; span ops [| hook "after" |] [||] |])

    let mounted = mountView ops run queued graph strict target view

    equal [ "parent"; "after"; "child"; "child after"; "grandchild"; "second" ] (List.ofSeq log)

    mounted.Dispose ()

/// <summary>
/// A div holding a list followed by a span, each row holding an inner list followed by a span; every element logs its
/// name from a hook. The rows of a pass run their own hooks before the inner lists' rows.
/// </summary>
let private rowsAfterSiblings (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let log = ResizeArray<string>()
    let target = host ()

    let hook (name: string) : Typed.Modifier<'native> =
        Typed.mounted (fun _ ->
            log.Add name
            ignore)

    let view =
        Typed.div
            ops
            [| hook "parent" |]
            [|
                Keyed.list ops id (fun () -> [ 1; 2 ]) (fun current ->
                    let item = current ()

                    Typed.div
                        ops
                        [| hook (sprintf "row %d" item) |]
                        [|
                            Keyed.list ops id (fun () -> [ 10 * item ]) (fun inner -> span ops [| hook (sprintf "inner %d" (inner ())) |] [||])
                            span ops [| hook (sprintf "row %d after" item) |] [||]
                        |])
                span ops [| hook "after" |] [||]
            |]

    let mounted = mountView ops run queued graph strict target view

    equal
        [
            "parent"
            "after"
            "row 1"
            "row 1 after"
            "row 2"
            "row 2 after"
            "inner 10"
            "inner 20"
        ]
        (List.ofSeq log)

    mounted.Dispose ()

/// <summary>
/// A list whose rows log their hooks: a row added after the mount runs its hook in the host, and inside one batch the
/// list pass runs once with the final items.
/// </summary>
let private rowsAfterLayout (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal [ 1 ])
    let log = ResizeArray<string>()
    let target = host ()

    let view =
        Keyed.list ops id (fun () -> rows.Value) (fun current ->
            let item = current ()

            span
                ops
                [|
                    Typed.mounted (fun (node: HTMLSpanElement) ->
                        log.Add (sprintf "row %d %b" item (target.contains node))
                        ignore)
                |]
                [| ops.Text (string item) |])

    let mounted = mountView ops run queued graph strict target view
    graph.Run (fun () -> rows.Value <- [ 1; 2 ])
    mounted.Flush ()
    equal [ "row 1 true"; "row 2 true" ] (List.ofSeq log)

    graph.Run (fun () ->
        Api.batch (fun () ->
            rows.Value <- [ 1; 2; 3 ]
            rows.Value <- [ 1; 2 ]))

    mounted.Flush ()
    equal ([ "row 1 true"; "row 2 true" ], "12") (List.ofSeq log, target.textContent)
    mounted.Dispose ()

let private armsReplace (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let arm = graph.Run (fun () -> Api.createSignal 0)
    let log = ResizeArray<string>()
    let target = host ()

    let view =
        Branch.select ops id (fun () -> arm.Value) (fun current ->
            let key = current ()

            span
                ops
                [|
                    Typed.mounted (fun (node: HTMLSpanElement) ->
                        log.Add (sprintf "arm %d %b" key (target.contains node))
                        fun () -> log.Add (sprintf "arm cleanup %d" key))
                |]
                [| ops.Text (string key) |])

    let mounted = mountView ops run queued graph strict target view
    graph.Run (fun () -> arm.Value <- 1)
    mounted.Flush ()
    equal [ "arm 0 true"; "arm cleanup 0"; "arm 1 true" ] (List.ofSeq log)
    mounted.Dispose ()

/// <summary>A div with a hook that succeeds and a hook that raises during the mount.</summary>
let private initialHookFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let log = ResizeArray<string>()
    let target = host ()

    let view =
        Typed.div
            ops
            [|
                Typed.mounted (fun (_: HTMLDivElement) ->
                    log.Add "first"
                    fun () -> log.Add "first cleanup")
                Typed.mounted (fun (_: HTMLDivElement) -> failwith "hook")
            |]
            [||]

    let raised =
        try
            mountView ops run queued graph strict target view
            |> ignore

            None
        with error ->
            Some error.Message

    equal (Some "hook", [ "first"; "first cleanup" ], 0.) (raised, List.ofSeq log, target.childNodes.length)

/// <summary>A second list row whose hook raises after the mount: the failure is reported and the row stays.</summary>
let private laterHookFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal [ 1 ])
    let reports = ResizeArray<exn list>()
    let target = host ()

    let view =
        Keyed.list ops id (fun () -> rows.Value) (fun current ->
            let item = current ()

            span
                ops
                [|
                    Typed.mounted (fun (_: HTMLSpanElement) -> if item = 2 then failwith "row 2" else ignore)
                |]
                [| ops.Text (string item) |])

    let mounted = mountView ops run queued graph reports.Add target view
    graph.Run (fun () -> rows.Value <- [ 1; 2 ])
    mounted.Flush ()
    equal ("12", [ [ "row 2" ] ]) (target.textContent, messages reports)
    mounted.Dispose ()

let private mountInBatch (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let log = ResizeArray<string>()
    let reports = ResizeArray<exn list>()
    let target = host ()
    let mounted = ref Unchecked.defaultof<Mounted>

    let view =
        Typed.div
            ops
            [|
                Typed.mounted (fun (node: HTMLDivElement) ->
                    log.Add (sprintf "hook %b" (target.contains node))
                    ignore)
                Typed.mounted (fun (_: HTMLDivElement) -> failwith "late")
            |]
            [||]

    graph.Run (fun () ->
        Api.batch (fun () ->
            mounted.Value <- mountView ops run queued graph reports.Add target view
            log.Add "returned"))

    equal ([ "returned"; "hook true" ], [ [ "late" ] ]) (List.ofSeq log, messages reports)
    mounted.Value.Dispose ()

let private nodeRefLifetime (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let field: NodeRef<HTMLInputElement> = NodeRef.create ()
    let seen = ResizeArray<bool>()
    let target = host ()

    let view =
        Typed.input
            ops
            [|
                Typed.ref field
                Typed.browser (fun (_: HTMLInputElement) ->
                    seen.Add field.Current.IsSome
                    ignore)
            |]

    let mounted = mountView ops run queued graph strict target view

    let placed =
        match field.Current with
        | Some node -> obj.ReferenceEquals (node, target.querySelector "input" |> Option.get)
        | None -> false

    mounted.Dispose ()
    equal ([ false ], true, false) (List.ofSeq seen, placed, field.Current.IsSome)

/// <summary>Two rows sharing one NodeRef: removing the earlier row leaves the reference on the later row's element.</summary>
let private sharedNodeRef (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let shared: NodeRef<HTMLSpanElement> = NodeRef.create ()
    let target = host ()

    let view =
        Keyed.list ops id (fun () -> rows.Value) (fun current -> span ops [| Typed.ref shared |] [| ops.Text (string (current ())) |])

    let mounted = mountView ops run queued graph strict target view

    let text () =
        shared.Current |> Option.map _.textContent

    let before = text ()
    graph.Run (fun () -> rows.Value <- [ 2 ])
    mounted.Flush ()
    let after = text ()
    mounted.Dispose ()
    equal (Some "2", Some "2", None) (before, after, text ())

let private movesKeepHooks (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let runs = ref 0
    let first, second = host (), host ()

    let view =
        Keyed.list ops id (fun () -> rows.Value) (fun current ->
            span
                ops
                [|
                    Typed.mounted (fun (_: HTMLSpanElement) ->
                        runs.Value <- runs.Value + 1
                        ignore)
                |]
                [| ops.Text (string (current ())) |])

    let mounted = mountView ops run queued graph strict first view
    graph.Run (fun () -> rows.Value <- [ 2; 1 ])
    mounted.Flush ()
    mounted.MoveBefore (second, None)
    equal (2, "21") (runs.Value, second.textContent)
    mounted.Dispose ()

/// <summary>A hook that reads and writes a signal read by a text binding of the same view.</summary>
let private hookWritesBinding (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let focused = graph.Run (fun () -> Api.createSignal "no")
    let runs = ref 0
    let target = host ()

    let view =
        Typed.div
            ops
            [|
                Typed.mounted (fun (_: HTMLDivElement) ->
                    runs.Value <- runs.Value + 1

                    if focused.Value = "no" then
                        focused.Value <- "yes"

                    ignore)
            |]
            [| Typed.reactiveText ops (fun () -> focused.Value) |]

    let mounted = mountView ops run queued graph strict target view
    mounted.Flush ()
    equal ("yes", 1) (target.textContent, runs.Value)
    graph.Run (fun () -> focused.Value <- "again")
    mounted.Flush ()
    equal ("again", 1) (target.textContent, runs.Value)
    mounted.Dispose ()

let private serverHook (ops: Operations<'view>) (run: 'view -> Backend -> unit) =
    let view =
        Typed.div ops [| Typed.mounted (fun (_: HTMLDivElement) -> ignore) |] [| ops.Text "x" |]

    let rejected =
        try
            Html.render Reject (run view) |> ignore
            None
        with error ->
            Some error.Message

    equal
        ("<div>x</div>", Some true)
        (Html.render Omit (run view),
         rejected
         |> Option.map (fun message -> message.StartsWith "Browser capability requires explicit Omit policy"))

let private hiddenReveal (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createAsyncSource<string>())
    let log = ResizeArray<string>()
    let target = host ()

    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure -> ops.Text failure.Error.Message),
            fun _ ->
                span
                    ops
                    [|
                        Typed.mounted (fun (node: HTMLSpanElement) ->
                            log.Add (sprintf "hook %b" (target.contains node))
                            fun () -> log.Add "cleanup")
                    |]
                    [| Typed.reactiveText ops (fun () -> source.Value) |]
        )

    let mounted = mountView ops run queued graph strict target view
    equal ([], "loading") (List.ofSeq log, target.textContent)
    graph.Run (fun () -> source.Settle "ready")
    mounted.Flush ()
    equal ([ "hook true" ], "ready") (List.ofSeq log, target.textContent)
    mounted.Dispose ()
    equal [ "hook true"; "cleanup" ] (List.ofSeq log)

/// <summary>A content torn down by a failure before its reveal, then retried and revealed.</summary>
let private tornDownAndRetried (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()

    let flight =
        graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<string>()))

    let log = ResizeArray<string>()
    let reports = ResizeArray<exn list>()
    let retry = ref ignore
    let generation = ref 0
    let target = host ()

    let content (_: BoundaryStatus) =
        ops.Component (fun _ ->
            generation.Value <- generation.Value + 1
            let current = generation.Value

            span
                ops
                [|
                    Typed.mounted (fun (node: HTMLSpanElement) ->
                        log.Add (sprintf "hook %d %b" current (target.contains node))
                        fun () -> log.Add (sprintf "cleanup %d" current))
                |]
                [| Typed.reactiveText ops (fun () -> flight.Value.Value) |])

    let failed (failure: BoundaryFailure) =
        ops.Component (fun _ ->
            retry.Value <- failure.Retry
            ops.Text ("failed " + failure.Error.Message))

    let mounted =
        mountView ops run queued graph reports.Add target (ops.Boundary (ops.Text "loading", failed, content))

    graph.Run (fun () -> flight.Value.Fail (Exception "first"))
    mounted.Flush ()
    equal ([], "failed first", [ [ "first" ] ]) (List.ofSeq log, target.textContent, messages reports)
    graph.Run (fun () -> flight.Value <- Api.createAsyncSource<string>())
    retry.Value ()
    equal ([], "loading") (List.ofSeq log, target.textContent)
    graph.Run (fun () -> flight.Value.Settle "ready")
    mounted.Flush ()
    equal ([ "hook 2 true" ], "ready") (List.ofSeq log, target.textContent)
    mounted.Dispose ()
    equal [ "hook 2 true"; "cleanup 2" ] (List.ofSeq log)

let private viewHooks (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createAsyncSource<string>())
    let broken = graph.Run (fun () -> Api.createSignal false)
    let log = ResizeArray<string>()
    let reports = ResizeArray<exn list>()
    let target = host ()

    let logged (name: string) =
        Typed.mounted (fun (node: HTMLSpanElement) ->
            log.Add (sprintf "%s %b" name (target.contains node))
            fun () -> log.Add (name + " cleanup"))

    let view =
        ops.Boundary (
            span ops [| logged "pending" |] [| ops.Text "loading" |],
            (fun failure -> span ops [| logged "error" |] [| ops.Text failure.Error.Message |]),
            fun _ -> Typed.reactiveText ops (fun () -> if broken.Value then failwith "broken" else source.Value)
        )

    let mounted = mountView ops run queued graph reports.Add target view
    equal [ "pending true" ] (List.ofSeq log)
    graph.Run (fun () -> source.Settle "ready")
    mounted.Flush ()
    equal [ "pending true"; "pending cleanup" ] (List.ofSeq log)
    graph.Run (fun () -> broken.Value <- true)
    mounted.Flush ()

    equal ([ "pending true"; "pending cleanup"; "error true" ], [ [ "broken" ] ]) (List.ofSeq log, messages reports)

    mounted.Dispose ()
    equal "error cleanup" log[log.Count - 1]

let private laterFailureUnderBoundary (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal [ 1 ])
    let reports = ResizeArray<exn list>()
    let target = host ()

    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure -> ops.Text ("failed: " + failure.Error.Message)),
            fun _ ->
                Keyed.list ops id (fun () -> rows.Value) (fun current ->
                    let item = current ()

                    span
                        ops
                        [|
                            Typed.mounted (fun (_: HTMLSpanElement) -> if item = 2 then failwith "row 2" else ignore)
                        |]
                        [| ops.Text (string item) |])
        )

    let mounted = mountView ops run queued graph reports.Add target view
    graph.Run (fun () -> rows.Value <- [ 1; 2 ])
    mounted.Flush ()
    equal ("failed: row 2", [ [ "row 2" ] ]) (target.textContent, messages reports)
    mounted.Dispose ()

let private hiddenNodeRef (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let field: NodeRef<HTMLInputElement> = NodeRef.create ()
    let source = graph.Run (fun () -> Api.createAsyncSource<string>())
    let target = host ()

    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure -> ops.Text failure.Error.Message),
            fun _ ->
                ops.Fragment
                    [|
                        Typed.input ops [| Typed.ref field |]
                        Typed.reactiveText ops (fun () -> source.Value)
                    |]
        )

    let mounted = mountView ops run queued graph strict target view
    let hidden = field.Current.IsSome
    graph.Run (fun () -> source.Settle "ready")
    mounted.Flush ()
    let shown = field.Current.IsSome
    mounted.Dispose ()
    equal (false, true, false) (hidden, shown, field.Current.IsSome)

/// <summary>A boundary in a list row added after the mount; its content's hook runs at the boundary's reveal.</summary>
let private boundaryInLaterRow (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal [ 1 ])
    let source = graph.Run (fun () -> Api.createAsyncSource<string>())
    let log = ResizeArray<string>()
    let target = host ()

    let view =
        Keyed.list ops id (fun () -> rows.Value) (fun current ->
            if current () = 1 then
                ops.Text "first"
            else
                ops.Boundary (
                    ops.Text "loading",
                    (fun failure -> ops.Text failure.Error.Message),
                    fun _ ->
                        span
                            ops
                            [|
                                Typed.mounted (fun (node: HTMLSpanElement) ->
                                    log.Add (sprintf "content %b" (target.contains node))
                                    ignore)
                            |]
                            [| Typed.reactiveText ops (fun () -> source.Value) |]
                ))

    let mounted = mountView ops run queued graph strict target view
    graph.Run (fun () -> rows.Value <- [ 1; 2 ])
    mounted.Flush ()
    equal ([], "firstloading") (List.ofSeq log, target.textContent)
    graph.Run (fun () -> source.Settle "ready")
    mounted.Flush ()
    equal ([ "content true" ], "firstready") (List.ofSeq log, target.textContent)
    mounted.Dispose ()

#if RANVIER_VIEW_COUNTERS
/// <summary>A boundary content with a mounted hook, torn down by a failure before its reveal.</summary>
let private heldAfterTeardown (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createAsyncSource<string>())
    let reports = ResizeArray<exn list>()
    let target = host ()
    let before = PlacementCounters.held.Value

    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure -> ops.Text ("failed " + failure.Error.Message)),
            fun _ -> span ops [| Typed.mounted (fun (_: HTMLSpanElement) -> ignore) |] [| Typed.reactiveText ops (fun () -> source.Value) |]
        )

    let mounted = mountView ops run queued graph reports.Add target view
    let hidden = PlacementCounters.held.Value - before
    graph.Run (fun () -> source.Fail (Exception "first"))
    mounted.Flush ()
    let tornDown = PlacementCounters.held.Value - before
    let text = target.textContent
    mounted.Dispose ()
    equal (1, 0, "failed first", [ [ "first" ] ]) (hidden, tornDown, text, messages reports)

/// <summary>A boundary content with a mounted hook, disposed with its mount while still pending.</summary>
let private heldAfterPendingDispose (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createAsyncSource<string>())
    let log = ResizeArray<string>()
    let target = host ()
    let before = PlacementCounters.held.Value

    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure -> ops.Text failure.Error.Message),
            fun _ ->
                span
                    ops
                    [|
                        Typed.mounted (fun (_: HTMLSpanElement) ->
                            log.Add "hook"
                            ignore)
                    |]
                    [| Typed.reactiveText ops (fun () -> source.Value) |]
        )

    let mounted = mountView ops run queued graph strict target view
    let hidden = PlacementCounters.held.Value - before
    mounted.Dispose ()
    equal (1, 0, []) (hidden, PlacementCounters.held.Value - before, List.ofSeq log)
#endif

/// <summary>A boundary in a boundary content; the inner content settles while the outer content is pending.</summary>
let private nestedBoundary (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let outer = graph.Run (fun () -> Api.createAsyncSource<string>())
    let inner = graph.Run (fun () -> Api.createAsyncSource<string>())
    let log = ResizeArray<string>()
    let target = host ()

    let view =
        ops.Boundary (
            ops.Text "outer loading",
            (fun failure -> ops.Text failure.Error.Message),
            fun _ ->
                ops.Fragment
                    [|
                        Typed.reactiveText ops (fun () -> outer.Value)
                        ops.Boundary (
                            ops.Text "inner loading",
                            (fun failure -> ops.Text failure.Error.Message),
                            fun _ ->
                                span
                                    ops
                                    [|
                                        Typed.mounted (fun (node: HTMLSpanElement) ->
                                            log.Add (sprintf "inner %b" (target.contains node))
                                            ignore)
                                    |]
                                    [| Typed.reactiveText ops (fun () -> inner.Value) |]
                        )
                    |]
        )

    let mounted = mountView ops run queued graph strict target view
    graph.Run (fun () -> inner.Settle "b")
    mounted.Flush ()
    equal ([], "outer loading") (List.ofSeq log, target.textContent)
    graph.Run (fun () -> outer.Settle "a")
    mounted.Flush ()
    equal ([ "inner true" ], "ab") (List.ofSeq log, target.textContent)
    mounted.Dispose ()

type Probe =
    {
        Log: ResizeArray<string>
        Host: HTMLElement
    }

let private placedDiv =
    Component.define (fun _ (probe: Probe) -> probe) (fun t ->
        t.div
            [|
                t.Hooks.mounted (fun probe (node: HTMLDivElement) ->
                    probe.Log.Add (sprintf "hook %b" (probe.Host.contains node))
                    fun () -> probe.Log.Add (sprintf "cleanup %b" (probe.Host.contains node)))
            |]
            [||])

let private templatePlacedInHost queued () =
    use graph = new Graph ()
    let probe = { Log = ResizeArray (); Host = host () }

    let mounted =
        mountTemplate queued graph strict probe.Host (Component.render placedDiv probe)

    equal [ "hook true" ] (List.ofSeq probe.Log)
    mounted.Dispose ()
    equal ([ "hook true"; "cleanup true" ], 0.) (List.ofSeq probe.Log, probe.Host.childNodes.length)

let private orderLog () =
    Conformance.local "PlacementChecks.orderLog" (fun () -> ResizeArray<string>())

let private orderChild =
    Component.define (fun _ () -> ()) (fun t ->
        t.span
            [|
                t.Hooks.mounted (fun _ (_: HTMLSpanElement) ->
                    (orderLog ()).Add "child"
                    ignore)
            |]
            [||])

let private orderRow =
    Component.define (fun _ (item: unit -> int) -> item) (fun t ->
        t.span
            [|
                t.Hooks.mounted (fun item (_: HTMLSpanElement) ->
                    (orderLog ()).Add("row " + string (item ()))
                    ignore)
            |]
            [||])

let private orderParent =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [|
                t.Hooks.mounted (fun _ (_: HTMLDivElement) ->
                    (orderLog ()).Add "parent"
                    ignore)
            |]
            [| t.render orderChild id; t.each id (fun _ -> [| 1; 2 |]) orderRow |])

let private templateSetupOrder queued () =
    use graph = new Graph ()
    (orderLog ()).Clear()

    let mounted =
        mountTemplate queued graph strict (host ()) (Component.render orderParent ())

    equal [ "parent"; "child"; "row 1"; "row 2" ] (List.ofSeq (orderLog ()))
    mounted.Dispose ()

/// <summary>A span whose mounted hook logs <c>name</c>.</summary>
let private orderMark (name: string) =
    Component.define (fun _ () -> ()) (fun t ->
        t.span
            [|
                t.Hooks.mounted (fun _ (_: HTMLSpanElement) ->
                    (orderLog ()).Add name
                    ignore)
            |]
            [||])

/// <summary>A div whose mounted hook logs <c>name</c>, holding <c>children</c> followed by a span logging <c>after</c>.</summary>
let private orderHolder (name: string) (after: string) (children: TemplateBuilder<unit> -> TemplateView<unit> array) =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [|
                t.Hooks.mounted (fun _ (_: HTMLDivElement) ->
                    (orderLog ()).Add name
                    ignore)
            |]
            [|
                yield! children t
                t.span
                    [|
                        t.Hooks.mounted (fun _ (_: HTMLSpanElement) ->
                            (orderLog ()).Add after
                            ignore)
                    |]
                    [||]
            |])

let private orderInlinedThenSibling =
    orderHolder "parent" "after" (fun t -> [| t.render (orderMark "child") id |])

let private orderNestedChild =
    orderHolder "child" "child after" (fun t -> [| t.render (orderMark "grandchild") id |])

let private orderNested =
    orderHolder "parent" "after" (fun t -> [| t.render orderNestedChild id; t.render (orderMark "second") id |])

let private holeMark = orderMark "hole"

let private orderHoleThenChild =
    orderHolder "parent" "after" (fun t ->
        [|
            t.view (fun _ -> Component.render holeMark ())
            t.render (orderMark "child") id
        |])

let private templateOrder (view: Component<unit>) (expected: string list) queued () =
    use graph = new Graph ()
    (orderLog ()).Clear()

    let mounted = mountTemplate queued graph strict (host ()) (Component.render view ())

    let seen = List.ofSeq (orderLog ())
    mounted.Dispose ()
    equal expected seen

let private orderInner =
    Component.define (fun _ (item: unit -> int) -> item) (fun t ->
        t.span
            [|
                t.Hooks.mounted (fun item (_: HTMLSpanElement) ->
                    (orderLog ()).Add("inner " + string (item ()))
                    ignore)
            |]
            [||])

let private orderOuterRow =
    Component.define (fun _ (item: unit -> int) -> item) (fun t ->
        t.div
            [|
                t.Hooks.mounted (fun item (_: HTMLDivElement) ->
                    (orderLog ()).Add("row " + string (item ()))
                    ignore)
            |]
            [|
                t.each id (fun (item: unit -> int) -> [| 10 * item () |]) orderInner
                t.span
                    [|
                        t.Hooks.mounted (fun item (_: HTMLSpanElement) ->
                            (orderLog ()).Add("row " + string (item ()) + " after")
                            ignore)
                    |]
                    [||]
            |])

let private orderSiblings =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [|
                t.Hooks.mounted (fun _ (_: HTMLDivElement) ->
                    (orderLog ()).Add "parent"
                    ignore)
            |]
            [|
                t.each id (fun _ -> [| 1; 2 |]) orderOuterRow
                t.span
                    [|
                        t.Hooks.mounted (fun _ (_: HTMLSpanElement) ->
                            (orderLog ()).Add "after"
                            ignore)
                    |]
                    [||]
            |])

let private templateRowsAfterSiblings queued () =
    use graph = new Graph ()
    (orderLog ()).Clear()

    let mounted =
        mountTemplate queued graph strict (host ()) (Component.render orderSiblings ())

    equal
        [
            "parent"
            "after"
            "row 1"
            "row 1 after"
            "row 2"
            "row 2 after"
            "inner 10"
            "inner 20"
        ]
        (List.ofSeq (orderLog ()))

    mounted.Dispose ()

let private rowsProbe () =
    Conformance.local "PlacementChecks.rowsProbe" (fun () -> ref (Unchecked.defaultof<_>: Probe))

let private hookedRow =
    Component.define (fun _ (item: unit -> int) -> item) (fun t ->
        t.span
            [|
                t.Hooks.mounted (fun item (node: HTMLSpanElement) ->
                    (rowsProbe ()).Value.Log.Add(sprintf "row %d %b" (item ()) ((rowsProbe ()).Value.Host.contains node))
                    ignore)
            |]
            [| t.text (fun item -> string (item ())) |])

let private hookedRows =
    Component.define (fun _ (items: Signal<int list>) -> items) (fun t ->
        t.div
            [||]
            [|
                t.each id (fun (items: Signal<int list>) -> Array.ofList items.Value) hookedRow
            |])

let private templateRowsAfterLayout queued () =
    use graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal [ 1 ])
    (rowsProbe ()).Value <- { Log = ResizeArray (); Host = host () }

    let mounted =
        mountTemplate queued graph strict (rowsProbe ()).Value.Host (Component.render hookedRows rows)

    graph.Run (fun () -> rows.Value <- [ 1; 2 ])
    mounted.Flush ()
    equal [ "row 1 true"; "row 2 true" ] (List.ofSeq (rowsProbe ()).Value.Log)

    graph.Run (fun () ->
        Api.batch (fun () ->
            rows.Value <- [ 1; 2; 3 ]
            rows.Value <- [ 1; 2 ]))

    mounted.Flush ()
    equal ([ "row 1 true"; "row 2 true" ], "12") (List.ofSeq (rowsProbe ()).Value.Log, (rowsProbe ()).Value.Host.textContent)
    mounted.Dispose ()

let private armProbe () =
    Conformance.local "PlacementChecks.armProbe" (fun () -> ref (Unchecked.defaultof<_>: Probe))

let private armSpan =
    Component.define (fun _ (payload: unit -> int) -> payload) (fun t ->
        t.span
            [|
                t.Hooks.mounted (fun payload (node: HTMLSpanElement) ->
                    let key = payload ()
                    (armProbe ()).Value.Log.Add(sprintf "arm %d %b" key ((armProbe ()).Value.Host.contains node))
                    fun () -> (armProbe ()).Value.Log.Add(sprintf "arm cleanup %d" key))
            |]
            [| t.text (fun payload -> string (payload ())) |])

let private armHost =
    Component.define (fun _ (arm: Signal<int>) -> arm) (fun t ->
        t.div
            [||]
            [|
                t.branch (fun (arm: Signal<int>) -> arm.Value) (fun key -> t.arm armSpan key)
            |])

let private templateArmsReplace queued () =
    use graph = new Graph ()
    let arm = graph.Run (fun () -> Api.createSignal 0)
    (armProbe ()).Value <- { Log = ResizeArray (); Host = host () }

    let mounted =
        mountTemplate queued graph strict (armProbe ()).Value.Host (Component.render armHost arm)

    graph.Run (fun () -> arm.Value <- 1)
    mounted.Flush ()
    equal [ "arm 0 true"; "arm cleanup 0"; "arm 1 true" ] (List.ofSeq (armProbe ()).Value.Log)
    mounted.Dispose ()

let private reselect () =
    Conformance.local "PlacementChecks.reselect" (fun () -> ref (Unchecked.defaultof<_>: Signal<int>))

let private reselectingSpan =
    Component.define
        (fun _ (payload: unit -> int) ->
            if payload () = 1 then
                (reselect ()).Value.Value <- 2

            payload)
        (fun t ->
            t.span
                [|
                    t.Hooks.mounted (fun payload (node: HTMLSpanElement) ->
                        let key = payload ()
                        (armProbe ()).Value.Log.Add(sprintf "arm %d %b" key ((armProbe ()).Value.Host.contains node))
                        fun () -> (armProbe ()).Value.Log.Add(sprintf "arm cleanup %d" key))
                |]
                [| t.text (fun payload -> string (payload ())) |])

let private reselectingHost =
    Component.define (fun _ (arm: Signal<int>) -> arm) (fun t ->
        t.div
            [||]
            [|
                t.branch (fun (arm: Signal<int>) -> arm.Value) (fun key -> t.arm reselectingSpan key)
            |])

let private reentrantArmSelection queued () =
    use graph = new Graph ()
    (reselect ()).Value <- graph.Run (fun () -> Api.createSignal 1)
    (armProbe ()).Value <- { Log = ResizeArray (); Host = host () }

    let mounted =
        mountTemplate queued graph strict (armProbe ()).Value.Host (Component.render reselectingHost (reselect ()).Value)

    let first = List.ofSeq (armProbe ()).Value.Log, (armProbe ()).Value.Host.textContent
    graph.Run (fun () -> (reselect ()).Value.Value <- 0)
    mounted.Flush ()
    graph.Run (fun () -> (reselect ()).Value.Value <- 1)
    mounted.Flush ()

    let second =
        List.ofSeq (armProbe ()).Value.Log, (armProbe ()).Value.Host.textContent

    graph.Run (fun () -> (reselect ()).Value.Value <- 3)
    mounted.Flush ()

    equal
        (([ "arm 2 true" ], "2"),
         ([ "arm 2 true"; "arm cleanup 2"; "arm 0 true"; "arm cleanup 0"; "arm 2 true" ], "2"),
         [
             "arm 2 true"
             "arm cleanup 2"
             "arm 0 true"
             "arm cleanup 0"
             "arm 2 true"
             "arm cleanup 2"
             "arm 3 true"
         ],
         "3",
         1.)
        (first,
         second,
         List.ofSeq (armProbe ()).Value.Log,
         (armProbe ()).Value.Host.textContent,
         ((armProbe ()).Value.Host.querySelectorAll "span").length)

    mounted.Dispose ()

let private failingDiv =
    Component.define (fun _ (log: ResizeArray<string>) -> log) (fun t ->
        t.div
            [|
                t.Hooks.mounted (fun log (_: HTMLDivElement) ->
                    log.Add "first"
                    fun () -> log.Add "first cleanup")
                t.Hooks.mounted (fun _ (_: HTMLDivElement) -> failwith "hook")
            |]
            [||])

let private templateInitialHookFailure queued () =
    use graph = new Graph ()
    let log = ResizeArray<string>()
    let target = host ()

    let raised =
        try
            mountTemplate queued graph strict target (Component.render failingDiv log)
            |> ignore

            None
        with error ->
            Some error.Message

    equal (Some "hook", [ "first"; "first cleanup" ], 0.) (raised, List.ofSeq log, target.childNodes.length)

let private failingRow =
    Component.define (fun _ (item: unit -> int) -> item) (fun t ->
        t.span
            [|
                t.Hooks.mounted (fun item (_: HTMLSpanElement) -> if item () = 2 then failwith "row 2" else ignore)
            |]
            [| t.text (fun item -> string (item ())) |])

let private failingRows =
    Component.define (fun _ (items: Signal<int list>) -> items) (fun t ->
        t.div
            [||]
            [|
                t.each id (fun (items: Signal<int list>) -> Array.ofList items.Value) failingRow
            |])

let private templateLaterHookFailure queued () =
    use graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal [ 1 ])
    let reports = ResizeArray<exn list>()
    let target = host ()

    let mounted =
        mountTemplate queued graph reports.Add target (Component.render failingRows rows)

    graph.Run (fun () -> rows.Value <- [ 1; 2 ])
    mounted.Flush ()
    equal ("12", [ [ "row 2" ] ]) (target.textContent, messages reports)
    mounted.Dispose ()

let private batchedDiv =
    Component.define (fun _ (probe: Probe) -> probe) (fun t ->
        t.div
            [|
                t.Hooks.mounted (fun probe (node: HTMLDivElement) ->
                    probe.Log.Add (sprintf "hook %b" (probe.Host.contains node))
                    ignore)
                t.Hooks.mounted (fun _ (_: HTMLDivElement) -> failwith "late")
            |]
            [||])

let private templateMountInBatch queued () =
    use graph = new Graph ()
    let probe = { Log = ResizeArray (); Host = host () }
    let reports = ResizeArray<exn list>()
    let mounted = ref Unchecked.defaultof<Mounted>

    graph.Run (fun () ->
        Api.batch (fun () ->
            mounted.Value <- mountTemplate queued graph reports.Add probe.Host (Component.render batchedDiv probe)
            probe.Log.Add "returned"))

    equal ([ "returned"; "hook true" ], [ [ "late" ] ]) (List.ofSeq probe.Log, messages reports)
    mounted.Value.Dispose ()

type Field =
    {
        Field: NodeRef<HTMLInputElement>
        Seen: ResizeArray<bool>
    }

let private referenced =
    Component.define
        (fun _ (field: Field) ->
            field.Seen.Add field.Field.Current.IsSome
            field)
        (fun t -> t.input [| t.Hooks.ref (fun field -> field.Field) |])

let private templateNodeRef queued () =
    use graph = new Graph ()

    let field =
        {
            Field = NodeRef.create ()
            Seen = ResizeArray ()
        }

    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render referenced field)

    let placed =
        match field.Field.Current with
        | Some node -> obj.ReferenceEquals (node, target.querySelector "input" |> Option.get)
        | None -> false

    mounted.Dispose ()
    equal ([ false ], true, false) (List.ofSeq field.Seen, placed, field.Field.Current.IsSome)

let private moveRuns () =
    Conformance.local "PlacementChecks.moveRuns" (fun () -> ref 0)

let private countedRow =
    Component.define (fun _ (item: unit -> int) -> item) (fun t ->
        t.span
            [|
                t.Hooks.mounted (fun _ (_: HTMLSpanElement) ->
                    (moveRuns ()).Value <- (moveRuns ()).Value + 1
                    ignore)
            |]
            [| t.text (fun item -> string (item ())) |])

let private countedRows =
    Component.define (fun _ (items: Signal<int list>) -> items) (fun t ->
        t.div
            [||]
            [|
                t.each id (fun (items: Signal<int list>) -> Array.ofList items.Value) countedRow
            |])

let private templateMovesKeepHooks queued () =
    use graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let first, second = host (), host ()
    (moveRuns ()).Value <- 0

    let mounted =
        mountTemplate queued graph strict first (Component.render countedRows rows)

    graph.Run (fun () -> rows.Value <- [ 2; 1 ])
    mounted.Flush ()
    mounted.MoveBefore (second, None)
    equal (2, "21") ((moveRuns ()).Value, second.textContent)
    mounted.Dispose ()

type RefRow =
    {
        Item: unit -> int
        Button: NodeRef<HTMLButtonElement>
    }

let private refReads () =
    Conformance.local "PlacementChecks.refReads" (fun () -> ResizeArray<string>())

let private refRow =
    Component.define
        (fun _ (item: unit -> int) ->
            {
                Item = item
                Button = NodeRef.create ()
            })
        (fun t ->
            t.button
                [|
                    t.Attrs.attribute ("data-item", (fun (row: RefRow) -> Some (string (row.Item ()))))
                    t.Hooks.ref (fun row -> row.Button)
                    t.Events.onClick (fun row _ ->
                        (refReads ())
                            .Add(
                                match row.Button.Current with
                                | Some node ->
                                    node.getAttribute "data-item"
                                    |> Option.defaultValue "absent"
                                | None -> "none"
                            ))
                |]
                [||])

let private refRows =
    Component.define (fun _ (items: Signal<int list>) -> items) (fun t ->
        t.div
            [||]
            [|
                t.each id (fun (items: Signal<int list>) -> Array.ofList items.Value) refRow
            |])

/// <summary>Reverses two rows whose handlers read their row's NodeRef, then clicks the first button: the second row's element before the reorder.</summary>
let private templateRefAcrossReorder queued () =
    use graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let target = host ()
    (refReads ()).Clear()

    let mounted =
        mountTemplate queued graph strict target (Component.render refRows rows)

    let second = target.querySelectorAll("button").[1]
    graph.Run (fun () -> rows.Value <- [ 2; 1 ])
    mounted.Flush ()

    let first =
        target.querySelector "button"
        |> Option.get
        |> unbox<HTMLButtonElement>

    first.click ()
    equal ([ "2" ], true) (List.ofSeq (refReads ()), obj.ReferenceEquals (first, second))
    mounted.Dispose ()

let private serverHooked =
    Component.define (fun _ () -> ()) (fun t -> t.div [| t.Hooks.mounted (fun _ (_: HTMLDivElement) -> ignore) |] [| t.text "x" |])

let private templateServerHook () =
    let render policy =
        Html.render policy (Program.run (Component.render serverHooked ()))

    let rejected =
        try
            render Reject |> ignore
            None
        with error ->
            Some error.Message

    equal
        ("<div>x</div>", Some true)
        (render Omit,
         rejected
         |> Option.map (fun message -> message.StartsWith "Browser capability requires explicit Omit policy"))

let private hiddenSource () =
    Conformance.local "PlacementChecks.hiddenSource" (fun () -> ref (Unchecked.defaultof<_>: Signal<AsyncSource<string>>))

let private hiddenLog () =
    Conformance.local "PlacementChecks.hiddenLog" (fun () -> ResizeArray<string>())

let private hiddenHost () =
    Conformance.local "PlacementChecks.hiddenHost" (fun () -> ref (Unchecked.defaultof<_>: HTMLElement))

let private hiddenContent =
    Component.define (fun _ (_: BoundaryStatus) -> ()) (fun t ->
        t.span
            [|
                t.Hooks.mounted (fun _ (node: HTMLSpanElement) ->
                    (hiddenLog ()).Add(sprintf "hook %b" ((hiddenHost ()).Value.contains node))
                    fun () -> (hiddenLog ()).Add "cleanup")
            |]
            [| t.text (fun () -> (hiddenSource ()).Value.Value.Value) |])

let private hiddenPending =
    Component.define (fun _ () -> ()) (fun t ->
        t.span
            [|
                t.Hooks.mounted (fun _ (node: HTMLSpanElement) ->
                    (hiddenLog ()).Add(sprintf "pending %b" ((hiddenHost ()).Value.contains node))
                    fun () -> (hiddenLog ()).Add "pending cleanup")
            |]
            [| t.text "loading" |])

let private hiddenFailed =
    Component.define (fun _ (failure: BoundaryFailure) -> failure) (fun t ->
        t.button
            [|
                t.Hooks.mounted (fun _ (_: HTMLButtonElement) ->
                    (hiddenLog ()).Add "error"
                    fun () -> (hiddenLog ()).Add "error cleanup")
                t.Events.onClick (fun failure _ -> failure.Retry ())
            |]
            [| t.text (fun failure -> "failed " + failure.Error.Message) |])

let private hiddenGuarded =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary hiddenPending hiddenFailed hiddenContent |])

/// <summary>A template content torn down before its reveal, retried from its error view's button, then revealed.</summary>
let private templateHiddenLifecycle queued () =
    use graph = new Graph ()
    (hiddenLog ()).Clear()
    (hiddenSource ()).Value <- graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<string>()))
    (hiddenHost ()).Value <- host ()
    let reports = ResizeArray<exn list>()

    let mounted =
        mountTemplate queued graph reports.Add (hiddenHost ()).Value (Component.render hiddenGuarded ())

    equal [ "pending true" ] (List.ofSeq (hiddenLog ()))
    graph.Run (fun () -> (hiddenSource ()).Value.Value.Fail(Exception "first"))
    mounted.Flush ()
    equal ([ "pending true"; "pending cleanup"; "error" ], "failed first") (List.ofSeq (hiddenLog ()), (hiddenHost ()).Value.textContent)
    graph.Run (fun () -> (hiddenSource ()).Value.Value <- Api.createAsyncSource<string>())

    ((hiddenHost ()).Value.querySelector "button"
     |> Option.get
     |> unbox<HTMLButtonElement>)
        .click()

    graph.Run (fun () -> (hiddenSource ()).Value.Value.Settle "ready")
    mounted.Flush ()

    equal
        ([
            "pending true"
            "pending cleanup"
            "error"
            "error cleanup"
            "pending true"
            "pending cleanup"
            "hook true"
         ],
         "ready",
         [ [ "first" ] ])
        (List.ofSeq (hiddenLog ()), (hiddenHost ()).Value.textContent, messages reports)

    mounted.Dispose ()
    equal "cleanup" ((hiddenLog ())[(hiddenLog ()).Count - 1])

let private hiddenField () =
    Conformance.local "PlacementChecks.hiddenField" (fun () -> ref (Unchecked.defaultof<_>: Field))

let private hiddenInput =
    Component.define (fun _ (_: BoundaryStatus) -> ()) (fun t ->
        t.span
            [||]
            [|
                t.input
                    [|
                        t.Hooks.ref (fun _ -> (hiddenField ()).Value.Field)
                        t.Hooks.mounted (fun _ (_: HTMLInputElement) ->
                            (hiddenField ()).Value.Seen.Add (hiddenField ()).Value.Field.Current.IsSome
                            ignore)
                    |]
                t.text (fun () -> (hiddenSource ()).Value.Value.Value)
            |])

let private hiddenInputGuarded =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary hiddenPending hiddenFailed hiddenInput |])

/// <summary>A template input with a ref and a mounted hook, in a boundary content pending at the mount and revealed later.</summary>
let private templateHiddenNodeRef queued () =
    use graph = new Graph ()
    (hiddenLog ()).Clear()
    (hiddenSource ()).Value <- graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<string>()))

    (hiddenField ()).Value <-
        {
            Field = NodeRef.create ()
            Seen = ResizeArray ()
        }

    let target = host ()
    (hiddenHost ()).Value <- target

    let mounted =
        mountTemplate queued graph strict target (Component.render hiddenInputGuarded ())

    let hidden = (hiddenField ()).Value.Field.Current.IsSome
    graph.Run (fun () -> (hiddenSource ()).Value.Value.Settle "ready")
    mounted.Flush ()

    let shown =
        match (hiddenField ()).Value.Field.Current with
        | Some node -> obj.ReferenceEquals (node, target.querySelector "input" |> Option.get)
        | None -> false

    mounted.Dispose ()

    equal (false, [ true ], true, false) (hidden, List.ofSeq (hiddenField ()).Value.Seen, shown, (hiddenField ()).Value.Field.Current.IsSome)

/// <summary>An input in a host attached to the document; its mounted hook focuses and measures it.</summary>
let private focusAndMeasure () =
    use graph = new Graph ()
    let target = host ()
    attachToBody target
    let width = ref 0.

    let view =
        Typed.input
            Program.ops
            [|
                Typed.attribute "style" "width: 120px"
                Typed.mounted (fun (input: HTMLInputElement) ->
                    input.focus ()
                    width.Value <- input.getBoundingClientRect().width
                    ignore)
            |]

    let mounted =
        mountWith SynchronousCommits graph None strict target None (fun context -> Program.run view context.Backend)

    let focused =
        obj.ReferenceEquals (activeElement (), target.querySelector "input" |> Option.get)

    mounted.Dispose ()
    target.remove ()
    equal (true, true) (focused, width.Value > 0.)

/// <summary>A pass's container is placed by its <c>Place</c>; a later request raises, and the next <c>Begin</c> allocates a new container.</summary>
let private passRequestAfterPlace () =
    let pass = PlacementPass null
    let first = pass.Container
    pass.Place ()

    let message =
        try
            pass.Container |> ignore
            "no error"
        with error ->
            error.Message

    pass.Begin ()
    let next = pass.Container

    equal
        ("A pass placement container was requested after its Place", true, false, false)
        (message, first.IsPlaced, obj.ReferenceEquals (first, next), next.IsPlaced)

let private checks: (string * (unit -> unit)) array =
    strictCases
    <| Array.concat
        [
            both
                "a mounted hook runs after the element is in the host and its cleanup runs before the element leaves"
                (placedInHost Tree.ops Tree.run)
                (placedInHost Program.ops Program.run)
            both
                "setup-time browser hooks run on an unplaced element"
                (browserHookUnplaced Tree.ops Tree.run)
                (browserHookUnplaced Program.ops Program.run)
            both
                "hooks run in setup order: an instance, its inlined children, then region rows"
                (setupOrder Tree.ops Tree.run)
                (setupOrder Program.ops Program.run)
            both
                "hooks run in setup order: an instance's elements, including those after an inlined child, then the child's"
                (inlinedThenSibling Tree.ops Tree.run)
                (inlinedThenSibling Program.ops Program.run)
            both
                "hooks run in setup order: each instance before its inlined children, two levels deep"
                (inlinedNested Tree.ops Tree.run)
                (inlinedNested Program.ops Program.run)
            both
                "hooks run in setup order: an instance's elements, including those after a region, then the region's rows"
                (rowsAfterSiblings Tree.ops Tree.run)
                (rowsAfterSiblings Program.ops Program.run)
            both
                "a row added by a list pass runs its hook after the pass lays it out, and a batch that adds and removes a row creates none"
                (rowsAfterLayout Tree.ops Tree.run)
                (rowsAfterLayout Program.ops Program.run)
            both
                "an arm's hook runs after the arm is moved in; a replaced arm's cleanup runs before the next arm's hook"
                (armsReplace Tree.ops Tree.run)
                (armsReplace Program.ops Program.run)
            both
                "a hook failure during mountWith unwinds the mount and rethrows"
                (initialHookFailure Tree.ops Tree.run)
                (initialHookFailure Program.ops Program.run)
            both
                "a later hook failure goes to report once and the instance stays"
                (laterHookFailure Tree.ops Tree.run)
                (laterHookFailure Program.ops Program.run)
            both
                "a mount inside Api.batch runs its hooks after the batch and reports their failures"
                (mountInBatch Tree.ops Tree.run)
                (mountInBatch Program.ops Program.run)
            both
                "NodeRef.Current is None during setup, Some while placed and None after disposal"
                (nodeRefLifetime Tree.ops Tree.run)
                (nodeRefLifetime Program.ops Program.run)
            both
                "a NodeRef shared by two rows keeps the remaining row's element when the other row leaves"
                (sharedNodeRef Tree.ops Tree.run)
                (sharedNodeRef Program.ops Program.run)
            both "moving a mount or reordering rows does not re-run hooks" (movesKeepHooks Tree.ops Tree.run) (movesKeepHooks Program.ops Program.run)
            both
                "a mounted hook that writes a signal read by a binding updates the binding and runs once"
                (hookWritesBinding Tree.ops Tree.run)
                (hookWritesBinding Program.ops Program.run)
            both "hooks under a hidden boundary content run at reveal" (hiddenReveal Tree.ops Tree.run) (hiddenReveal Program.ops Program.run)
            both
                "content torn down before reveal runs no hook and no hook cleanup, and a retried content runs its hooks at its reveal"
                (tornDownAndRetried Tree.ops Tree.run)
                (tornDownAndRetried Program.ops Program.run)
            both
                "pending and error view hooks run while shown and clean up when retired"
                (viewHooks Tree.ops Tree.run)
                (viewHooks Program.ops Program.run)
            both
                "a later hook failure under a boundary shows the error view"
                (laterFailureUnderBoundary Tree.ops Tree.run)
                (laterFailureUnderBoundary Program.ops Program.run)
            both "NodeRef.Current is None while hidden by a boundary" (hiddenNodeRef Tree.ops Tree.run) (hiddenNodeRef Program.ops Program.run)
            both
                "a boundary in a row added later runs its content hooks at its own reveal"
                (boundaryInLaterRow Tree.ops Tree.run)
                (boundaryInLaterRow Program.ops Program.run)
#if RANVIER_VIEW_COUNTERS
            both
                "a content torn down before its reveal leaves the held-hook count at its prior value"
                (heldAfterTeardown Tree.ops Tree.run)
                (heldAfterTeardown Program.ops Program.run)
            both
                "a content disposed while pending leaves the held-hook count at its prior value and runs no hook"
                (heldAfterPendingDispose Tree.ops Tree.run)
                (heldAfterPendingDispose Program.ops Program.run)
#endif
            both
                "an inner boundary settled while its outer content is pending runs its content hooks at the outer reveal"
                (nestedBoundary Tree.ops Tree.run)
                (nestedBoundary Program.ops Program.run)
            [|
                "tree: a mounted hook raises under Reject and writes nothing under Omit", (fun () -> serverHook Tree.ops Tree.run)
                "program: a mounted hook raises under Reject and writes nothing under Omit", (fun () -> serverHook Program.ops Program.run)
            |]
            templated "a mounted hook runs after the element is in the host and its cleanup runs before the element leaves" templatePlacedInHost
            templated "hooks run in setup order: an instance, its inlined children, then region rows" templateSetupOrder
            templated
                "hooks run in setup order: an instance's elements, including those after a region, then the region's rows"
                templateRowsAfterSiblings
            templated
                "hooks run in setup order: an instance's elements, including those after an inlined child, then the child's"
                (templateOrder orderInlinedThenSibling [ "parent"; "after"; "child" ])
            templated
                "hooks run in setup order: each instance before its inlined children, two levels deep"
                (templateOrder orderNested [ "parent"; "after"; "child"; "child after"; "grandchild"; "second" ])
            templated
                "hooks run in setup order: an instance's elements and its later inlined child, then its view hole's view"
                (templateOrder orderHoleThenChild [ "parent"; "after"; "child"; "hole" ])
            templated
                "a row added by a list pass runs its hook after the pass lays it out, and a batch that adds and removes a row creates none"
                templateRowsAfterLayout
            templated "an arm's hook runs after the arm is moved in; a replaced arm's cleanup runs before the next arm's hook" templateArmsReplace
            templated
                "an arm whose setup selects another arm is replaced before its hooks run, and each placed arm's hook runs once, in selection order"
                reentrantArmSelection
            templated "a hook failure during mountWith unwinds the mount and rethrows" templateInitialHookFailure
            templated "a later hook failure goes to report once and the instance stays" templateLaterHookFailure
            templated "a mount inside Api.batch runs its hooks after the batch and reports their failures" templateMountInBatch
            templated "NodeRef.Current is None during setup, Some while placed and None after disposal" templateNodeRef
            templated "moving a mount or reordering rows does not re-run hooks" templateMovesKeepHooks
            templated "a NodeRef read from a handler after a keyed reorder holds its row's element" templateRefAcrossReorder
            templated
                "hooks under hidden content run at reveal, content torn down before reveal runs none, and view hooks run while shown"
                templateHiddenLifecycle
            templated "NodeRef.Current is None while hidden by a boundary" templateHiddenNodeRef
            [|
                "template: a mounted hook raises under Reject and writes nothing under Omit", templateServerHook
            |]
            [|
                "a pass container requested after its Place raises, and the next Begin allocates a new one", passRequestAfterPlace
            |]
        ]

let private browserChecks: (string * (unit -> unit)) array =
    strictCases [| "a mounted hook can focus and measure its element", focusAndMeasure |]

let cases: Conformance.Case array = Conformance.ofChecks checks

let browserCases: Conformance.Case array = Conformance.ofChecks browserChecks
