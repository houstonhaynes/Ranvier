/// <summary>Arms.Match sites mounted in jsdom: cells per arm, events, value payloads, rows, hydration and exits.</summary>
module ArmsChecks

open System
open global.Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Representation
open Ranvier.View.Template
open CheckSupport
open TwinTrace
open Ranvier.View.Sugar
open Ranvier.View.Arms
open ArmsFixtures

/// <summary>The text of the first element matching the selector, or null.</summary>
[<Emit("($0.querySelector($1)?.textContent ?? null)")>]
let private textOf (target: HTMLElement) (selector: string) : string = jsNative

/// <summary>The texts of the elements matching the selector, in document order.</summary>
[<Emit("Array.from($0.querySelectorAll($1), e => e.textContent)")>]
let private textsOf (target: HTMLElement) (selector: string) : string array = jsNative

/// <summary>The first element matching the selector, or null.</summary>
[<Emit("$0.querySelector($1)")>]
let private queryNode (target: HTMLElement) (selector: string) : obj = jsNative

let private write (graph: Graph) (mounted: Mounting.Mounted list) (change: unit -> unit) =
    graph.Run change

    for mount in mounted do
        mount.Flush ()

/// <summary>Two mounts of one site with one instance: disposing either leaves the other reading live values (Testing 4).</summary>
let private twiceShown queued () =
    use graph = new Graph ()
    let state = graph.Run (fun () -> Api.createSignal (Loaded ("a", 1)))
    let first, second = host (), host ()
    let a = mountTemplate queued graph strict first (Component.render loader state)
    let b = mountTemplate queued graph strict second (Component.render loader state)
    a.Flush ()
    b.Flush ()
    equal ("a x1", "a x1") (textOf first "pre", textOf second "pre")
    write graph [ a; b ] (fun () -> state.Value <- Loaded ("b", 2))
    equal ("b x2", "b x2") (textOf first "pre", textOf second "pre")
    b.Dispose ()
    write graph [ a ] (fun () -> state.Value <- Loaded ("c", 3))
    equal "c x3" (textOf first "pre")
    let third = host ()
    let c = mountTemplate queued graph strict third (Component.render loader state)
    c.Flush ()
    write graph [ a; c ] (fun () -> state.Value <- Loading 5)
    write graph [ a; c ] (fun () -> state.Value <- Loaded ("d", 4))
    equal ("d x4", "d x4") (textOf first "pre", textOf third "pre")
    a.Dispose ()
    write graph [ c ] (fun () -> state.Value <- Loaded ("e", 5))
    equal "e x5" (textOf third "pre")
    c.Dispose ()

// Rows whose item type is the instance type: the row reader is rewritten as a reader of the site and receives the row
// item (spec Risk 3).
let private itemRows =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                (fun (state: Signal<Load>) -> state.Value),
                function
                | Loaded (text, _) ->
                    ul () {
                        // The row item is the instance: the fixture covers a reader that receives the row item.
                        // fsharpanalyzer: ignore-line-next RAN021
                        forEachFixed id (fun (state: Signal<Load>) -> [| state |]) (li () { fun (_: Signal<Load>) -> text })
                    }
                | _ -> span () { "Other" }
            )
        })

/// <summary>A reader that receives a value other than its arm's values raises AM006: the full text in Debug, the short text in Release.</summary>
let private noArm () =
    let message =
        try
            use graph = new Graph ()
            let state = graph.Run (fun () -> Api.createSignal (Loaded ("a", 1)))

            Server.render Server.Options.defaults itemRows state
            |> ignore

            ""
        with error ->
            error.Message

#if DEBUG
    equal Ranvier.View.Arms.Diagnostics.noArm message
#else
    equal "Ranvier AM006: shayanhabibi.github.io/Ranvier/view/arms/#am006" message
#endif

/// <summary>Under Fable, <c>Arms.Prepare</c> returns and a site mounted after it reads its payload.</summary>
let private prepares queued () =
    Arms.Prepare ()
    Arms.Prepare ()
    use graph = new Graph ()
    let state = graph.Run (fun () -> Api.createSignal (Loaded ("a", 1)))
    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render loader state)

    mounted.Flush ()
    equal "a x1" (textOf target "pre")
    mounted.Dispose ()

/// <summary>An arm hidden and shown again for the same instance follows later writes (Review Focus 2).</summary>
let private remountAfterHide queued () =
    use graph = new Graph ()

    let shown =
        graph.Run (fun () ->
            {
                Visible = Api.createSignal true
                State = Api.createSignal (Loaded ("a", 1))
            })

    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render shownLoader shown)

    mounted.Flush ()
    write graph [ mounted ] (fun () -> shown.State.Value <- Loaded ("b", 2))
    write graph [ mounted ] (fun () -> shown.Visible.Value <- false)
    equal null (textOf target "pre")
    write graph [ mounted ] (fun () -> shown.Visible.Value <- true)
    equal "b x2" (textOf target "pre")
    write graph [ mounted ] (fun () -> shown.State.Value <- Loaded ("c", 3))
    equal "c x3" (textOf target "pre")
    mounted.Dispose ()

let private initial (graph: Graph) =
    graph.Run (fun () -> Api.createSignal (Loading 10))

let private loadWrites (state: Signal<Load>) =
    [ for step in loadSteps -> Write (fun () -> state.Value <- step) ]

/// <summary>The probe's nine-step trace of each Load fixture equals its twin's (Testing 3).</summary>
let private traces queued () =
    for twin, site in
        [
            loaderTwin, loader
            richTwin, rich
            rowsTwin, rows
            rowsTupleTwin, rowsTuple
            tupledTwin, tupled
            wholeTwin, whole
            formsTwin, forms
            functionsTwin, functions
            helperTwin, helper
            splicedTwin, spliced
            localsTwin, locals
            statusedTwin, statused
            cardedTwin, carded
            rowedTwin, rowed
        ] do
        pair queued initial (Component.render twin) (Component.render site) loadWrites

/// <summary>Every value-type payload case on the client equals its twin (Testing 6).</summary>
let private valueTraces queued () =
    let make (graph: Graph) =
        graph.Run (fun () -> Api.createSignal (Count 0))

    let steps (state: Signal<Value>) =
        [
            for value in
                [
                    Ratio 1.5
                    Ratio 2.5
                    Money 2.25m
                    Flag true
                    Flag false
                    Letter 'q'
                    At { X = 3; Y = 4 }
                    At { X = 5; Y = 6 }
                    Maybe (ValueSome 7)
                    Maybe ValueNone
                    Count 9
                ] -> Write (fun () -> state.Value <- value)
        ]

    pair queued make (Component.render valuesTwin) (Component.render values) steps

/// <summary>Three nested sites over one instance follow writes to each site's source as their twin does.</summary>
let private deepTrace queued () =
    let make (graph: Graph) =
        graph.Run (fun () ->
            ({
                State = Api.createSignal (Loaded ("t", 1))
                Mode = Api.createSignal 1
                Open = Api.createSignal true
            }
            : Deck))

    pair queued make (Component.render deepTwin) (Component.render deep) (fun deck ->
        [
            Write (fun () -> deck.State.Value <- Loaded ("u", 2))
            Write (fun () -> deck.Mode.Value <- 2)
            Write (fun () -> deck.Open.Value <- false)
            Write (fun () -> deck.Open.Value <- true)
            Write (fun () -> deck.Mode.Value <- 0)
            Write (fun () -> deck.Mode.Value <- 3)
            Write (fun () -> deck.State.Value <- Idle)
            Write (fun () -> deck.State.Value <- Loaded ("v", 1))
        ])

let private editorState (graph: Graph) =
    graph.Run (fun () ->
        ({
            State = Api.createSignal (Loaded ("a", 1))
            Saved = Api.createSignal ""
        }
        : Editor))

/// <summary>An event handler reads its arm's payload at event time (Testing 5).</summary>
let private eventsReadLatest queued () =
    use graph = new Graph ()
    let state = editorState graph
    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render editor state)

    mounted.Flush ()

    let press () =
        click "button" target
        mounted.Flush ()

    write graph [ mounted ] (fun () -> state.State.Value <- Loaded ("b", 1))
    write graph [ mounted ] (fun () -> state.State.Value <- Loaded ("c", 1))
    press ()
    equal "c" (graph.Run (fun () -> state.Saved.Value))
    write graph [ mounted ] (fun () -> state.State.Value <- Idle)
    write graph [ mounted ] (fun () -> state.State.Value <- Loaded ("d", 1))
    press ()
    equal "d" (graph.Run (fun () -> state.Saved.Value))
    mounted.Dispose ()

let private editorTrace queued () =
    pair queued editorState (Component.render editorTwin) (Component.render editor) (fun state ->
        [
            Write (fun () -> state.State.Value <- Loaded ("b", 1))
            Each (click "button")
            Write (fun () -> state.State.Value <- Idle)
            Write (fun () -> state.State.Value <- Loaded ("c", 2))
            Each (click "button")
        ])

/// <summary>Rows fed by the items reader keep their elements across payload updates and show the new payload (Testing 7).</summary>
let private rowsKeep queued () =
    use graph = new Graph ()
    let state = graph.Run (fun () -> Api.createSignal (Loaded ("a", 2)))
    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render rowsTuple state)

    mounted.Flush ()
    let first = queryNode target "li"
    write graph [ mounted ] (fun () -> state.Value <- Loaded ("b", 2))
    equal (true, "b0b1") (obj.ReferenceEquals (first, queryNode target "li"), textOf target "ul")
    mounted.Dispose ()

type private Row = { Id: int; State: Signal<Load> }

let private rowList =
    Component.template
        (fun _ (rows: Signal<Row array>) -> rows)
        (ul () {
            forEachFixed
                (fun (row: Row) -> row.Id)
                (fun (rows: Signal<Row array>) -> rows.Value)
                (li () {
                    Arms.Match (
                        (fun (row: Row) -> row.State.Value),
                        function
                        | Loaded (text, _) -> b () { fun _ -> text }
                        | _ -> span () { "Other" }
                    )
                })
        })

[<Emit("Array.from($0.querySelectorAll('li'))")>]
let private rowNodes (target: HTMLElement) : obj array = jsNative

[<Emit("$0.firstElementChild")>]
let private firstChildOf (node: obj) : obj = jsNative

[<Emit("$0 === $1")>]
let private sameNode (left: obj) (right: obj) : bool = jsNative

[<Emit("Array.from($0.querySelectorAll('li'), n => n.textContent)")>]
let private rowTexts (target: HTMLElement) : string array = jsNative

/// <summary>One site per row: after a reorder each row shows its own payload, and a write reaches its row only (Review Focus 4).</summary>
let private siteInRows queued () =
    use graph = new Graph ()

    let rows =
        graph.Run (fun () ->
            Api.createSignal
                [|
                    for id, text in [ 1, "a"; 2, "b"; 3, "c" ] ->
                        {
                            Id = id
                            State = Api.createSignal (Loaded (text, id))
                        }
                |])

    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render rowList rows)

    mounted.Flush ()
    equal [| "a"; "b"; "c" |] (rowTexts target)
    let before = rowNodes target
    write graph [ mounted ] (fun () -> rows.Value <- Array.rev rows.Value)
    equal [| "c"; "b"; "a" |] (rowTexts target)
    let reordered = rowNodes target
    equal (true, true, true) (sameNode reordered[0] before[2], sameNode reordered[1] before[1], sameNode reordered[2] before[0])
    let payloadNodes = [| for row in reordered -> firstChildOf row |]
    write graph [ mounted ] (fun () -> rows.Value[2].State.Value <- Loaded ("z", 1))
    equal [| "c"; "b"; "z" |] (rowTexts target)
    let after = rowNodes target

    equal
        (true, true, true, true, true)
        (sameNode after[0] reordered[0],
         sameNode after[1] reordered[1],
         sameNode after[2] reordered[2],
         sameNode (firstChildOf after[0]) payloadNodes[0],
         sameNode (firstChildOf after[1]) payloadNodes[1])

    write graph [ mounted ] (fun () -> rows.Value[0].State.Value <- Idle)
    equal [| "Other"; "b"; "z" |] (rowTexts target)
    mounted.Dispose ()

/// <summary>A batch that leaves a target and returns to it shows the last payload and reports nothing (Review Focus 5).</summary>
let private switchBackInOneBatch queued () =
    use graph = new Graph ()
    let state = graph.Run (fun () -> Api.createSignal (Loaded ("a", 1)))
    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render loader state)

    mounted.Flush ()
    let before = queryNode target "pre"

    write graph [ mounted ] (fun () ->
        Api.batch (fun () ->
            state.Value <- Idle
            state.Value <- Loaded ("b", 2)))

    equal ("b x2", true) (textOf target "pre", sameNode before (queryNode target "pre"))
    mounted.Dispose ()

[<Emit("$0.firstElementChild")>]
let private firstElement (target: HTMLElement) : obj = jsNative

/// <summary>The records of child-list writes that inserted nodes, from a write watch's drain.</summary>
let private insertions (watch: WriteWatch) =
    [
        for record in watch.drain () do
            if
                record.StartsWith "childList:"
                && not (record.Split(':').[2] = "0")
            then
                record
    ]

/// <summary>
/// Hydrates the served HTML under Strict; the lines are whether the served root element was adopted, the markup, and
/// the node insertions made by the hydration.
/// </summary>
let private strictRunner (served: string) =
    { new Runner with
        member _.Run(definition, make) =
            use graph = new Graph ()
            let props = graph.Run make
            let target = host ()
            loadServed target served
            let root = firstElement target
            let watch = watchWrites target

            let mounted =
                Hydration.hydrate
                    graph
                    None
                    strict
                    { Hydration.HydrationOptions.defaults with
                        Strict = true
                    }
                    target
                    definition
                    props

            let inserted = insertions watch
            watch.stop ()

            let lines =
                [
                    sprintf "adopted=%b" (obj.ReferenceEquals (root, firstElement target))
                    target.innerHTML
                    sprintf "insertions=%A" inserted
                ]

            mounted.Dispose ()
            lines
    }

/// <summary>
/// Each golden line's fixture state hydrates its .NET server HTML under Strict: the site adopts the served root, inserts
/// no node, and leaves the markup its twin leaves.
/// </summary>
let private hydratesLine (fixture: string) (state: string) (served: string) () =
    let case =
        all
        |> List.find (fun case -> case.Fixture = fixture && case.State = state)

    let site, twin = case.Run (strictRunner served)
    equal [ "adopted=true"; "insertions=[]" ] [ twin.Head; List.last twin ]
    equal twin site

/// <summary>
/// The hand-lowered array loader hydrates a loader golden line's .NET server HTML under Strict and leaves the markup the
/// Arms.Match loader and its twin leave.
/// </summary>
let private arrayHydratesLine (state: string) (served: string) () =
    let case =
        arrayShape
        |> List.find (fun case -> case.State = state)

    let matched =
        all
        |> List.find (fun other -> other.Fixture = "loader" && other.State = state)

    let array, _ = case.Run (strictRunner served)
    let site, twin = matched.Run (strictRunner served)

    equal [ "adopted=true"; "insertions=[]" ] [ array.Head; List.last array ]
    equal (twin, twin) (site, array)

let private loadFixtures =
    [
        "loader", loader, loaderTwin
        "rich", rich, richTwin
        "rows", rows, rowsTwin
        "rowsTuple", rowsTuple, rowsTupleTwin
        "tupled", tupled, tupledTwin
        "whole", whole, wholeTwin
        "functions", functions, functionsTwin
        "helper", helper, helperTwin
        "spliced", spliced, splicedTwin
        "locals", locals, localsTwin
        "statused", statused, statusedTwin
        "carded", carded, cardedTwin
        "rowed", rowed, rowedTwin
    ]

let private laterWrites = [ Loaded ("later", 4); Loading 5; Loaded ("again", 1) ]

/// <summary>
/// Hydrates the served HTML with the client at <c>initial</c>, then the three later writes; the lines are the reports,
/// whether the served root element was adopted, and the markup after the hydration and after each write.
/// </summary>
let private hydrateLoad (options: Hydration.HydrationOptions) (served: string) (initial: Load) (definition: Component<Signal<Load>>) =
    use graph = new Graph ()
    let state = graph.Run (fun () -> Api.createSignal initial)
    let target = host ()
    loadServed target served
    let root = firstElement target
    let reports = ResizeArray<string>()

    let report (errors: exn list) =
        for error in errors do
            reports.Add error.Message

    let mounted = Hydration.hydrate graph None report options target definition state

    let shots =
        ResizeArray
            [
                sprintf "reports=%A" (List.ofSeq reports)
                sprintf "adopted=%b %s" (obj.ReferenceEquals (root, firstElement target)) target.innerHTML
            ]

    for step in laterWrites do
        graph.Run (fun () -> state.Value <- step)
        mounted.Flush ()
        shots.Add target.innerHTML

    mounted.Dispose ()
    shots.Add (sprintf "reports=%A" (List.ofSeq reports))
    List.ofSeq shots

let private strictOptions =
    { Hydration.HydrationOptions.defaults with
        Strict = true
    }

/// <summary>A Load fixture hydrates its served <c>Loaded done 3</c> under Strict, then three writes, as its twin does.</summary>
let private hydratesThenWrites (site: Component<Signal<Load>>) (twin: Component<Signal<Load>>) (served: string) () =
    let twinShots = hydrateLoad strictOptions served (Loaded ("done", 3)) twin
    equal true (twinShots[1].StartsWith "adopted=true")
    equal twinShots (hydrateLoad strictOptions served (Loaded ("done", 3)) site)

/// <summary>
/// A Load fixture hydrates its served <c>Loaded done 3</c> with the client at <c>Idle</c> and Strict off: the reports, the
/// markup and the three later writes equal its twin's.
/// </summary>
let private fallsBack (site: Component<Signal<Load>>) (twin: Component<Signal<Load>>) (served: string) () =
    let twinShots = hydrateLoad Hydration.HydrationOptions.defaults served Idle twin
    equal false (twinShots.Head = "reports=[]")
    equal twinShots (hydrateLoad Hydration.HydrationOptions.defaults served Idle site)

/// <summary>The message of the mismatch raised by a Strict hydration with the client at <c>Idle</c>, and the host's markup after it.</summary>
let private strictMismatch (served: string) (definition: Component<Signal<Load>>) =
    use graph = new Graph ()
    let state = graph.Run (fun () -> Api.createSignal Idle)
    let target = host ()
    loadServed target served
    let before = target.innerHTML

    let raised =
        try
            Hydration.hydrate graph None strict strictOptions target definition state
            |> ignore

            "none"
        with TemplateDom.HydrationMismatchException _ as error ->
            error.Message

    raised, target.innerHTML = before

/// <summary>Under Strict, the site and its twin raise the same mismatch for a client at <c>Idle</c> and leave the host as served.</summary>
let private raisesMismatch (site: Component<Signal<Load>>) (twin: Component<Signal<Load>>) (served: string) () =
    let twinRaised = strictMismatch served twin
    equal (false, true) (fst twinRaised = "none", snd twinRaised)
    equal twinRaised (strictMismatch served site)

/// <summary>
/// The hydration cases over the golden lines (fixture, state, served HTML): every line under Strict, and for each Load
/// fixture's <c>Loaded done 3</c> the writes after hydration, the non-Strict fallback and the Strict mismatch.
/// </summary>
let hydrations (lines: (string * string * string) array) =
    let servedOf fixture =
        lines
        |> Array.pick (fun (f, state, served) ->
            if f = fixture && state = "Loaded done 3" then
                Some served
            else
                None)

    strictCases (
        Array.concat
            [
                [|
                    for fixture, state, served in lines ->
                        sprintf "%s %s hydrates its .NET server HTML under Strict like its twin" fixture state, hydratesLine fixture state served
                |]
                [|
                    for fixture, state, served in lines do
                        if fixture = "loader" then
                            sprintf "the hand-lowered array loader hydrates loader %s under Strict like Arms.Match" state,
                            arrayHydratesLine state served
                |]
                [|
                    for name, site, twin in loadFixtures do
                        let served = servedOf name
                        sprintf "%s hydrates Loaded done 3 under Strict and updates like its twin" name, hydratesThenWrites site twin served
                        sprintf "%s falls back from a mismatch and updates like its twin" name, fallsBack site twin served
                        sprintf "%s raises the twin's mismatch under Strict" name, raisesMismatch site twin served
                |]
            ]
    )

let private finishes = ResizeArray<unit -> unit>()
let private exitReads = ResizeArray<string * int>()

/// <summary>A site whose Loaded body holds its root on exit until <c>finish</c> runs; the exit records its pattern variables.</summary>
let private held =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                (fun (state: Signal<Load>) -> state.Value),
                function
                | Loaded (text, count) ->
                    pre (
                        Hooks.exit (
                            Func<_, _, _, _>(fun (_: Signal<Load>) (_: HTMLPreElement) finish ->
                                exitReads.Add (text, count)
                                finishes.Add finish
                                ignore)
                        )
                    ) {
                        fun _ -> text
                        " x"
                        fun _ -> string count
                    }
                | _ -> span () { "Other" }
            )
        })

/// <summary>A helper over the concrete instance type whose root is the view built in a case body.</summary>
let private framed (content: TemplateView<Signal<Load>>) : TemplateView<Signal<Load>> = content

/// <summary>The site of <c>held</c> with its Loaded body passed to the concrete helper <c>framed</c>.</summary>
let private heldFramed =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                (fun (state: Signal<Load>) -> state.Value),
                function
                | Loaded (text, count) ->
                    framed (
                        pre (
                            Hooks.exit (
                                Func<_, _, _, _>(fun (_: Signal<Load>) (_: HTMLPreElement) finish ->
                                    exitReads.Add (text, count)
                                    finishes.Add finish
                                    ignore)
                            )
                        ) {
                            fun _ -> text
                            " x"
                            fun _ -> string count
                        }
                    )
                | _ -> span () { "Other" }
            )
        })

/// <summary>
/// The exit of an outgoing arm, run after the arm's cleanup, reads its own case's values, also while another arm of the
/// same target is live (Testing 9).
/// </summary>
let private heldExit (held: Component<Signal<Load>>) queued () =
    finishes.Clear ()
    exitReads.Clear ()
    use graph = new Graph ()
    let state = graph.Run (fun () -> Api.createSignal (Loaded ("a", 1)))
    let target = host ()
    let mounted = mountTemplate queued graph strict target (Component.render held state)
    mounted.Flush ()
    write graph [ mounted ] (fun () -> state.Value <- Idle)
    equal ([ "a", 1 ], [| "a x1" |], "Other") (List.ofSeq exitReads, textsOf target "pre", textOf target "span")
    write graph [ mounted ] (fun () -> state.Value <- Loaded ("b", 2))
    equal ([ "a", 1 ], [| "a x1"; "b x2" |]) (List.ofSeq exitReads, Array.sort (textsOf target "pre"))
    write graph [ mounted ] (fun () -> state.Value <- Loading 3)
    equal ([ "a", 1; "b", 2 ], [| "a x1"; "b x2" |], "Other") (List.ofSeq exitReads, Array.sort (textsOf target "pre"), textOf target "span")
    let pending = List.ofSeq finishes
    finishes.Clear ()

    for finish in pending do
        finish ()

    mounted.Flush ()
    equal (null, "Other") (textOf target "pre", textOf target "span")
    mounted.Dispose ()

/// <summary>One site per keyed row whose Loaded body holds its root on exit until <c>finish</c> runs.</summary>
let private heldRows =
    Component.template
        (fun _ (rows: Signal<Row array>) -> rows)
        (ul () {
            forEachFixed
                (fun (row: Row) -> row.Id)
                (fun (rows: Signal<Row array>) -> rows.Value)
                (li () {
                    Arms.Match (
                        (fun (row: Row) -> row.State.Value),
                        function
                        | Loaded (text, count) ->
                            pre (
                                Hooks.exit (
                                    Func<_, _, _, _>(fun (_: Row) (_: HTMLPreElement) finish ->
                                        exitReads.Add (text, count)
                                        finishes.Add finish
                                        ignore)
                                )
                            ) {
                                fun _ -> text
                                " x"
                                fun _ -> string count
                            }
                        | _ -> span () { "Other" }
                    )
                })
        })

/// <summary>A helper over the concrete row type whose root is the view built in a case body.</summary>
let private rowFramed (content: TemplateView<Row>) : TemplateView<Row> = content

/// <summary>The rows of <c>heldRows</c> with each Loaded body passed to the concrete helper <c>rowFramed</c>.</summary>
let private heldRowsFramed =
    Component.template
        (fun _ (rows: Signal<Row array>) -> rows)
        (ul () {
            forEachFixed
                (fun (row: Row) -> row.Id)
                (fun (rows: Signal<Row array>) -> rows.Value)
                (li () {
                    Arms.Match (
                        (fun (row: Row) -> row.State.Value),
                        function
                        | Loaded (text, count) ->
                            rowFramed (
                                pre (
                                    Hooks.exit (
                                        Func<_, _, _, _>(fun (_: Row) (_: HTMLPreElement) finish ->
                                            exitReads.Add (text, count)
                                            finishes.Add finish
                                            ignore)
                                    )
                                ) {
                                    fun _ -> text
                                    " x"
                                    fun _ -> string count
                                }
                            )
                        | _ -> span () { "Other" }
                    )
                })
        })

/// <summary>
/// A held arm of a keyed row reads its own row's values while another row's payload changes and its own row remounts the
/// same target (Testing 9).
/// </summary>
let private heldRowReadsOwnRow (heldRows: Component<Signal<Row array>>) queued () =
    finishes.Clear ()
    exitReads.Clear ()
    use graph = new Graph ()

    let rows =
        graph.Run (fun () ->
            Api.createSignal
                [|
                    for id, text in [ 1, "a"; 2, "b" ] ->
                        {
                            Id = id
                            State = Api.createSignal (Loaded (text, id))
                        }
                |])

    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render heldRows rows)

    mounted.Flush ()
    equal [| "a x1"; "b x2" |] (textsOf target "pre")
    write graph [ mounted ] (fun () -> rows.Value[0].State.Value <- Idle)
    equal ([ "a", 1 ], [| "a x1"; "b x2" |], [| "Other" |]) (List.ofSeq exitReads, textsOf target "pre", textsOf target "span")
    write graph [ mounted ] (fun () -> rows.Value[1].State.Value <- Loaded ("c", 3))
    equal [| "a x1"; "c x3" |] (textsOf target "pre")
    write graph [ mounted ] (fun () -> rows.Value[0].State.Value <- Loaded ("d", 4))
    equal [| "a x1"; "c x3"; "d x4" |] (Array.sort (textsOf target "pre"))
    write graph [ mounted ] (fun () -> rows.Value[1].State.Value <- Idle)
    equal ([ "a", 1; "c", 3 ], [| "a x1"; "c x3"; "d x4" |]) (List.ofSeq exitReads, Array.sort (textsOf target "pre"))
    let pending = List.ofSeq finishes
    finishes.Clear ()

    for finish in pending do
        finish ()

    mounted.Flush ()
    equal [| "d x4"; "Other" |] (rowTexts target)
    mounted.Dispose ()

[<Emit("$0.querySelectorAll($1).length")>]
let private countOf (target: HTMLElement) (selector: string) : int = jsNative

let private pendingView =
    Component.template (fun _ () -> ()) (p (Attrs.className "pending") { "pending" })

let private failedView =
    Component.template (fun _ (failure: BoundaryFailure) -> failure) (p (Attrs.className "failed") { fun failure -> failure.Error.Message })

/// <summary>A boundary whose content is an Arms.Match site over an async value.</summary>
let private bounded (flight: Signal<AsyncSource<int>>) =
    let content =
        Component.template
            (fun _ (status: BoundaryStatus) -> status)
            (div (Attrs.className "level") {
                Arms.Match (
                    (fun (_: BoundaryStatus) -> flight.Value.Value),
                    function
                    | 0 -> em () { "none" }
                    | n -> b () { fun _ -> string n }
                )
            })

    Component.template (fun _ () -> ()) (section () { boundary pendingView failedView content })

/// <summary>The armOf twin of <c>bounded</c>.</summary>
let private boundedTwin (flight: Signal<AsyncSource<int>>) =
    let none = armOf (em () { "none" })
    let some = armOf (b () { fun (payload: unit -> int) -> string (payload ()) })

    let content =
        Component.template
            (fun _ (status: BoundaryStatus) -> status)
            (div (Attrs.className "level") {
                matchWith (fun (_: BoundaryStatus) -> flight.Value.Value) (function
                    | 0 -> arm none ()
                    | n -> arm some n)
            })

    Component.template (fun _ () -> ()) (section () { boundary pendingView failedView content })

/// <summary>
/// The pending, failed and level states of a boundary around the view after each step: pending, settled, a new source
/// pending, settled to the other target, and a new source failed; the reported messages; the host's text after disposal.
/// </summary>
let private boundaryStates queued (view: Signal<AsyncSource<int>> -> Component<unit>) =
    use graph = new Graph ()
    let flight = graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<int>()))
    let target = host ()
    let reports = ResizeArray<string>()

    let report (errors: exn list) =
        for error in errors do
            reports.Add error.Message

    let mounted =
        Mounting.mountWith (modeOf queued) graph None report target None (fun context ->
            Program.run (Component.render (view flight) ()) context.Backend)

    let state () =
        mounted.Flush ()
        mounted.Flush ()
        countOf target ".pending", countOf target ".failed", textOf target ".level"

    let states =
        [
            state ()
            (graph.Run (fun () -> flight.Value.Settle 2)
             state ())
            (graph.Run (fun () -> flight.Value <- Api.createAsyncSource<int>())
             state ())
            (graph.Run (fun () -> flight.Value.Settle 0)
             state ())
            (graph.Run (fun () -> flight.Value <- Api.createAsyncSource<int>())
             state ())
            (graph.Run (fun () -> flight.Value.Fail (exn "lost"))
             state ())
        ]

    mounted.Dispose ()
    states, List.ofSeq reports, target.textContent

/// <summary>A read of a pending, settled or failed source behaves with the boundary as its matchWith twin does (Testing 10).</summary>
let private pendingSource queued () =
    let twin = boundaryStates queued boundedTwin
    let states, reports, _ = twin
    equal ((1, 0, null), (0, 0, "2"), (0, 1, null), [ "lost" ]) (states.Head, states[1], List.last states, List.distinct reports)
    equal twin (boundaryStates queued bounded)

let private exitOnlyReads = ResizeArray<string>()

/// <summary>A site whose Loaded body reads its pattern variables only in its exit, which finishes at once.</summary>
let private exitOnly =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                (fun (state: Signal<Load>) -> state.Value),
                function
                | Loaded (text, count) ->
                    pre (
                        Hooks.exit (
                            Func<_, _, _, _>(fun (_: Signal<Load>) (_: HTMLPreElement) finish ->
                                exitOnlyReads.Add (sprintf "%s %d" text count)
                                finish ()
                                ignore)
                        )
                    ) {
                        "held"
                    }
                | _ -> span () { "Other" }
            )
        })

/// <summary>The site of <c>exitOnly</c> with its Loaded body passed to the concrete helper <c>framed</c>.</summary>
let private exitOnlyFramed =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                (fun (state: Signal<Load>) -> state.Value),
                function
                | Loaded (text, count) ->
                    framed (
                        pre (
                            Hooks.exit (
                                Func<_, _, _, _>(fun (_: Signal<Load>) (_: HTMLPreElement) finish ->
                                    exitOnlyReads.Add (sprintf "%s %d" text count)
                                    finish ()
                                    ignore)
                            )
                        ) {
                            "held"
                        }
                    )
                | _ -> span () { "Other" }
            )
        })

let private exitOnlyLoaded =
    armOf (
        pre (
            Hooks.exit (
                Func<_, _, _, _>(fun (payload: unit -> string * int) (_: HTMLPreElement) finish ->
                    let text, count = payload ()
                    exitOnlyReads.Add (sprintf "%s %d" text count)
                    finish ()
                    ignore)
            )
        ) {
            "held"
        }
    )

let private exitOnlyOther = armOf (span () { "Other" })

let private exitOnlyTwin =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            matchWith (fun (state: Signal<Load>) -> state.Value) (function
                | Loaded (text, count) -> arm exitOnlyLoaded (text, count)
                | _ -> arm exitOnlyOther ())
        })

/// <summary>The exit reads of the definition over a payload change with no read, two departures and a remount.</summary>
let private exitReadsOf queued (definition: Component<Signal<Load>>) =
    exitOnlyReads.Clear ()
    use graph = new Graph ()
    let state = graph.Run (fun () -> Api.createSignal (Loaded ("a", 1)))
    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render definition state)

    mounted.Flush ()

    for step in [ Loaded ("b", 2); Idle; Loaded ("c", 3); Idle ] do
        write graph [ mounted ] (fun () -> state.Value <- step)

    mounted.Dispose ()
    List.ofSeq exitOnlyReads

/// <summary>An arm whose only readers are in its exit reads its own case's latest values, as its twin does (Testing 9).</summary>
let private exitOnlyReadsOwnCase queued () =
    let twin = exitReadsOf queued exitOnlyTwin
    equal [ "b 2"; "c 3" ] twin
    equal twin (exitReadsOf queued exitOnly)
    equal twin (exitReadsOf queued exitOnlyFramed)

let private handlerClicks = ResizeArray<string>()
let private handlerHolds = ResizeArray<unit -> unit>()

[<Emit("$0.querySelectorAll('button').forEach(b => b.click())")>]
let private clickAll (target: HTMLElement) : unit = jsNative

/// <summary>A site whose Loaded body reads its pattern variables only in a click handler, its root held by its exit.</summary>
let private handlerOnly =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                (fun (state: Signal<Load>) -> state.Value),
                function
                | Loaded (text, count) ->
                    button (
                        Events.onClick (fun (_: Signal<Load>) _ -> handlerClicks.Add (sprintf "%s %d" text count)),
                        Hooks.exit (
                            Func<_, _, _, _>(fun (_: Signal<Load>) (_: HTMLButtonElement) finish ->
                                handlerHolds.Add finish
                                ignore)
                        )
                    ) {
                        "go"
                    }
                | _ -> span () { "Other" }
            )
        })

/// <summary>The site of <c>handlerOnly</c> with its Loaded body passed to the concrete helper <c>framed</c>.</summary>
let private handlerOnlyFramed =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                (fun (state: Signal<Load>) -> state.Value),
                function
                | Loaded (text, count) ->
                    framed (
                        button (
                            Events.onClick (fun (_: Signal<Load>) _ -> handlerClicks.Add (sprintf "%s %d" text count)),
                            Hooks.exit (
                                Func<_, _, _, _>(fun (_: Signal<Load>) (_: HTMLButtonElement) finish ->
                                    handlerHolds.Add finish
                                    ignore)
                            )
                        ) {
                            "go"
                        }
                    )
                | _ -> span () { "Other" }
            )
        })

let private handlerLoaded =
    armOf (
        button (
            Events.onClick (fun (payload: unit -> string * int) _ ->
                let text, count = payload ()
                handlerClicks.Add (sprintf "%s %d" text count)),
            Hooks.exit (
                Func<_, _, _, _>(fun (_: unit -> string * int) (_: HTMLButtonElement) finish ->
                    handlerHolds.Add finish
                    ignore)
            )
        ) {
            "go"
        }
    )

let private handlerTwin =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            matchWith (fun (state: Signal<Load>) -> state.Value) (function
                | Loaded (text, count) -> arm handlerLoaded (text, count)
                | _ -> arm exitOnlyOther ())
        })

/// <summary>The clicks recorded by every button after each step: held, remounted on the same target, held again.</summary>
let private clicksOf queued (definition: Component<Signal<Load>>) =
    handlerClicks.Clear ()
    handlerHolds.Clear ()
    use graph = new Graph ()
    let state = graph.Run (fun () -> Api.createSignal (Loaded ("a", 1)))
    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render definition state)

    mounted.Flush ()
    let shots = ResizeArray<string list>()

    for step in [ Idle; Loaded ("b", 2); Idle ] do
        write graph [ mounted ] (fun () -> state.Value <- step)
        handlerClicks.Clear ()
        clickAll target
        shots.Add (List.sort (List.ofSeq handlerClicks))

    for finish in List.ofSeq handlerHolds do
        finish ()

    mounted.Dispose ()
    List.ofSeq shots

/// <summary>
/// An arm whose only readers are a click handler reads its own case's values after a remount of the same target on the
/// instance; a held root's handlers no longer run, as with its twin.
/// </summary>
let private handlerOnlyReadsOwnCase queued () =
    let twin = clicksOf queued handlerTwin
    equal [ []; [ "b 2" ]; [] ] twin
    equal twin (clicksOf queued handlerOnly)
    equal twin (clicksOf queued handlerOnlyFramed)

let cases: (string * (unit -> unit)) array =
    strictCases (
        Array.concat
            [
                templated "one site shown twice for one instance reads live values through either disposal" twiceShown
                templated "an arm hidden and shown again follows later writes" remountAfterHide
                templated "Arms.Prepare returns under Fable" prepares
                [|
                    "a row reader over the instance type that receives its row item raises AM006", noArm
                |]
                templated "each Load fixture's nine-step trace equals its twin's" traces
                templated "value-type payload fields equal their twin" valueTraces
                templated "three nested sites' trace equals its twin's" deepTrace
                templated "an event handler reads its arm's payload at event time" eventsReadLatest
                templated "an event handler's trace equals its twin's" editorTrace
                templated "rows fed by the items reader keep their elements across payload updates" rowsKeep
                templated "one site per keyed row follows its own row through a reorder" siteInRows
                templated "a batch that leaves a target and returns to it shows the last payload" switchBackInOneBatch
                templated "an outgoing arm's exit reads its own case's values after the arm's cleanup" (heldExit held)
                templated "an outgoing arm's exit inside a concrete helper reads its own case's values after the arm's cleanup" (heldExit heldFramed)
                templated "a held arm of a keyed row reads its own row's values" (heldRowReadsOwnRow heldRows)
                templated "a held arm of a keyed row inside a concrete helper reads its own row's values" (heldRowReadsOwnRow heldRowsFramed)
                templated "a source read inside a boundary pends, settles and fails as its twin does" pendingSource
                templated "an arm read only by its exit reads its own case's latest values" exitOnlyReadsOwnCase
                templated "an arm read only by a click handler reads its own case's values through a remount" handlerOnlyReadsOwnCase
            ]
    )
