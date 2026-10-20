/// <summary>The motion seam without anime: motion contexts, exit holds and reflow order, through tests/departures.test.mjs.</summary>
module DepartureChecks

open System
open global.Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open CheckSupport
open Ranvier.View.Foundation.Sugar

type Rows =
    {
        Items: Signal<int array>
        Shown: Signal<bool>
    }

let private rows (items: int array) (shown: bool) =
    {
        Items = Api.createSignal items
        Shown = Api.createSignal shown
    }

/// <summary>The entries the hooks of the current case wrote, in the order written.</summary>
let private log = ResizeArray<string>()

let private taken () =
    let entries = List.ofSeq log
    log.Clear ()
    entries

let private context (name: string) (motion: MotionContext) =
    log.Add (sprintf "%s hydrated=%b inserted=%b" name motion.Hydrated motion.Inserted)

let private rowMotion (row: unit -> int) (_: HTMLLIElement) (motion: MotionContext) =
    context ("li " + string (row ())) motion
    ignore

let private shownMotion (_: Rows) (_: HTMLSpanElement) (motion: MotionContext) =
    context "span" motion
    ignore

/// <summary>A list whose rows report their motion contexts, and a shown span that reports its own.</summary>
let private contexts =
    Component.template
        (fun _ (state: Rows) -> state)
        (div () {
            ul () { forEach id (fun (s: Rows) -> s.Items.Value) (li (Hooks.motion rowMotion) { fun (row: unit -> int) -> string (row ()) }) }
            show (fun s -> s.Shown.Value) (span (Hooks.motion shownMotion) { "on" })
        })

let private innerMotion (row: unit -> int) (_: HTMLElement) (motion: MotionContext) =
    context ("inner " + string (row ())) motion
    ignore

/// <summary>Rows that each hold a list of two inner rows.</summary>
let private nested =
    Component.template
        (fun _ (state: Rows) -> state)
        (ul () {
            forEach
                id
                (fun (s: Rows) -> s.Items.Value)
                (li (Hooks.motion rowMotion) {
                    forEach
                        id
                        (fun (row: unit -> int) -> [| row () * 10; row () * 10 + 1 |])
                        (b (Hooks.motion innerMotion) { fun (row: unit -> int) -> string (row ()) })
                })
        })

let private write (graph: Graph) (mounted: Mounting.Mounted) (action: unit -> unit) =
    graph.Run action
    mounted.Flush ()

let private insertedAfterFirstPass queued () =
    use graph = new Graph ()
    log.Clear ()
    let state = graph.Run (fun () -> rows [| 1; 2 |] false)

    let mounted =
        mountTemplate queued graph strict (host ()) (Component.render contexts state)

    mounted.Flush ()
    let first = taken ()
    write graph mounted (fun () -> state.Items.Value <- [| 1; 2; 3 |])
    let added = taken ()
    write graph mounted (fun () -> state.Shown.Value <- true)
    let shown = taken ()
    mounted.Dispose ()

    equal
        ([ "li 1 hydrated=false inserted=false"; "li 2 hydrated=false inserted=false" ],
         [ "li 3 hydrated=false inserted=true" ],
         [ "span hydrated=false inserted=true" ])
        (first, added, shown)

let private shownOnFirstPass queued () =
    use graph = new Graph ()
    log.Clear ()
    let state = graph.Run (fun () -> rows [||] true)

    let mounted =
        mountTemplate queued graph strict (host ()) (Component.render contexts state)

    mounted.Flush ()
    let first = taken ()
    mounted.Dispose ()
    equal [ "span hydrated=false inserted=false" ] first

let private nestedRowsDoNotEnter queued () =
    use graph = new Graph ()
    log.Clear ()
    let state = graph.Run (fun () -> rows [| 1 |] false)

    let mounted =
        mountTemplate queued graph strict (host ()) (Component.render nested state)

    mounted.Flush ()
    log.Clear ()
    write graph mounted (fun () -> state.Items.Value <- [| 1; 2 |])
    let added = taken ()
    mounted.Dispose ()

    equal
        [
            "li 2 hydrated=false inserted=true"
            "inner 20 hydrated=false inserted=false"
            "inner 21 hydrated=false inserted=false"
        ]
        added

let private omit =
    { Server.Options.defaults with
        Policy = Omit
    }

let private hydratedThenInserted () =
    use graph = new Graph ()
    log.Clear ()
    let state = graph.Run (fun () -> rows [| 1; 2 |] true)
    let target = host ()
    loadServed target (Server.render omit contexts state)

    let mounted =
        Hydration.hydrate graph None strict { IdPrefix = "r"; Strict = true } target contexts state

    let first = taken ()
    write graph mounted (fun () -> state.Items.Value <- [| 1; 2; 3 |])
    let added = taken ()
    write graph mounted (fun () -> state.Shown.Value <- false)
    write graph mounted (fun () -> state.Shown.Value <- true)
    let shown = taken ()
    mounted.Dispose ()

    equal
        ([
            "li 1 hydrated=true inserted=false"
            "li 2 hydrated=true inserted=false"
            "span hydrated=true inserted=false"
         ],
         [ "li 3 hydrated=false inserted=true" ],
         [ "span hydrated=false inserted=true" ])
        (first, added, shown)

/// <summary>The finish of each running exit, by its element's text.</summary>
let private finishes = Collections.Generic.Dictionary<string, unit -> unit>()

let private exitOf (node: HTMLElement) (finish: unit -> unit) =
    let name = node.textContent
    log.Add ("exit " + name)
    finishes[name] <- finish
    fun () -> log.Add ("cancel " + name)

let private finish (name: string) =
    let finishing = finishes[name]
    finishes.Remove name |> ignore
    finishing ()

/// <summary>A list whose row roots have exits, and an arm switch whose arm roots have exits.</summary>
let private exiting =
    Component.template
        (fun _ (state: Rows) -> state)
        (div () {
            ul () {
                forEach
                    id
                    (fun (s: Rows) -> s.Items.Value)
                    (li (Hooks.exit (fun (_: unit -> int) (node: HTMLLIElement) finish -> exitOf node finish)) {
                        fun (row: unit -> int) -> string (row ())
                    })
            }

            showElse
                (fun s -> s.Shown.Value)
                (span (Hooks.exit (fun (_: Rows) (node: HTMLSpanElement) finish -> exitOf node finish)) { "on" })
                (em (Hooks.exit (fun (_: Rows) (node: HTMLElement) finish -> exitOf node finish)) { "off" })
        })

/// <summary>A list whose row roots' exits call <c>finish</c> at once.</summary>
let private immediate =
    Component.template
        (fun _ (state: Rows) -> state)
        (ul () {
            forEach
                id
                (fun (s: Rows) -> s.Items.Value)
                (li (
                    Hooks.exit (fun (_: unit -> int) (_: HTMLLIElement) finish ->
                        finish ()
                        ignore)
                ) {
                    fun (row: unit -> int) -> string (row ())
                })
        })

[<Emit("Array.from($0.querySelectorAll($1), n => n.textContent)")>]
let private textsOf (root: HTMLElement) (selector: string) : string array = jsNative

/// <summary>The text of each child of <c>node</c> that is not a comment.</summary>
[<Emit("Array.from($0.childNodes).filter(n => n.nodeType !== 8).map(n => n.textContent)")>]
let private childrenOf (node: Element) : string array = jsNative

let private items (target: HTMLElement) =
    List.ofArray (textsOf target "li")

let private childrenAt (selector: string) (target: HTMLElement) =
    List.ofArray (childrenOf (target.querySelector selector |> unbox<Element>))

[<Emit("globalThis.departureTick($0)")>]
let private tick (milliseconds: int) : unit = jsNative

[<Emit("$0.querySelectorAll('li')[$1]")>]
let private rowAt (target: HTMLElement) (index: int) : Node = jsNative

/// <summary>Mounts <c>exiting</c> with <c>initial</c> items and the arm shown.</summary>
let private mountExiting queued (graph: Graph) (initial: int array) =
    log.Clear ()
    finishes.Clear ()
    let state = graph.Run (fun () -> rows initial true)
    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render exiting state)

    mounted.Flush ()
    state, target, mounted

let private heldUntilFinish queued () =
    use graph = new Graph ()
    let state, target, mounted = mountExiting queued graph [| 1; 2; 3 |]
    write graph mounted (fun () -> state.Items.Value <- [| 1; 3 |])
    let held = items target, taken ()
    finish "2"
    let finished = items target
    write graph mounted (fun () -> state.Items.Value <- [| 3; 1 |])
    let reordered = items target
    mounted.Dispose ()
    equal ((([ "1"; "2"; "3" ], [ "exit 2" ]), [ "1"; "3" ]), [ "3"; "1" ]) ((held, finished), reordered)

let private reorderAndInsertWhileHeld queued () =
    use graph = new Graph ()
    let state, target, mounted = mountExiting queued graph [| 1; 2; 3; 4 |]
    write graph mounted (fun () -> state.Items.Value <- [| 1; 3; 4 |])
    write graph mounted (fun () -> state.Items.Value <- [| 4; 5; 3; 1 |])
    let reordered = childrenAt "ul" target
    write graph mounted (fun () -> state.Items.Value <- [| 4; 3; 1 |])
    write graph mounted (fun () -> state.Items.Value <- [| 6; 4; 3; 1; 7 |])
    let inserted = childrenAt "ul" target
    finish "2"
    finish "5"
    let finished = childrenAt "ul" target
    mounted.Dispose ()

    equal ([ "4"; "5"; "3"; "1"; "2" ], [ "6"; "4"; "5"; "3"; "1"; "2"; "7" ], [ "6"; "4"; "3"; "1"; "7" ]) (reordered, inserted, finished)

let private clearHoldsEveryRoot queued () =
    use graph = new Graph ()
    let state, target, mounted = mountExiting queued graph [| 1; 2; 3 |]
    write graph mounted (fun () -> state.Items.Value <- [||])
    let held = childrenAt "ul" target, taken ()
    write graph mounted (fun () -> state.Items.Value <- [| 4 |])
    let added = childrenAt "ul" target
    finish "1"
    finish "2"
    finish "3"
    let finished = childrenAt "ul" target
    mounted.Dispose ()

    equal (([ "1"; "2"; "3" ], [ "exit 1"; "exit 2"; "exit 3" ]), [ "1"; "2"; "3"; "4" ], [ "4" ]) (held, added, finished)

let private readdedKeyGetsNewRow queued () =
    use graph = new Graph ()
    let state, target, mounted = mountExiting queued graph [| 1; 2 |]
    let original = rowAt target 1
    write graph mounted (fun () -> state.Items.Value <- [| 1 |])
    write graph mounted (fun () -> state.Items.Value <- [| 1; 2 |])
    let readded = items target
    let fresh = not (obj.ReferenceEquals (rowAt target 2, original))
    finish "2"
    let finished = items target
    let kept = not (obj.ReferenceEquals (rowAt target 1, original))
    mounted.Dispose ()
    equal (([ "1"; "2"; "2" ], true), ([ "1"; "2" ], true)) ((readded, fresh), (finished, kept))

let private armSwitchKeepsOrder queued () =
    use graph = new Graph ()
    let state, target, mounted = mountExiting queued graph [||]
    write graph mounted (fun () -> state.Shown.Value <- false)
    let switched = childrenAt "div" target
    write graph mounted (fun () -> state.Shown.Value <- true)
    let back = childrenAt "div" target
    finish "on"
    finish "off"
    let finished = childrenAt "div" target
    mounted.Dispose ()

    equal ([ ""; "on"; "off" ], [ ""; "on"; "off"; "on" ], [ ""; "on" ]) (switched, back, finished)

let private disposalEndsExits queued () =
    use graph = new Graph ()
    let state, target, mounted = mountExiting queued graph [| 1; 2; 3 |]
    write graph mounted (fun () -> state.Items.Value <- [| 3 |])
    write graph mounted (fun () -> state.Shown.Value <- false)
    log.Clear ()
    mounted.Dispose ()
    let ended = List.sort (taken ()), target.childNodes.length
    finish "1"
    equal (([ "cancel 1"; "cancel 2"; "cancel on" ], 0.), []) (ended, taken ())

let private finishesAfterTimeout queued () =
    use graph = new Graph ()
    let state, target, mounted = mountExiting queued graph [| 1; 2 |]
    write graph mounted (fun () -> state.Items.Value <- [| 1 |])
    tick (Departures.safetyTimeout - 1)
    let before = items target
    tick 1
    let after = items target
    finish "2"
    let finishedLate = items target
    mounted.Dispose ()
    equal ([ "1"; "2" ], [ "1" ], [ "1" ]) (before, after, finishedLate)

type Groups =
    {
        Outer: Signal<int array>
        Inner: Signal<int array>
    }

/// <summary>The inner items of the current <c>grouped</c> case.</summary>
let mutable private sharedInner: Signal<int array> = Unchecked.defaultof<_>

/// <summary>Outer rows without exits, each holding inner rows with exits.</summary>
let private grouped =
    Component.template
        (fun _ (state: Groups) -> state)
        (ul () {
            forEach
                id
                (fun (s: Groups) -> s.Outer.Value)
                (li () {
                    ol () {
                        forEach
                            (fun (pair: int * int) -> pair)
                            (fun (row: unit -> int) -> [| for inner in sharedInner.Value -> row (), inner |])
                            (b (Hooks.exit (fun (_: unit -> int * int) (node: HTMLElement) finish -> exitOf node finish)) {
                                fun (pair: unit -> int * int) -> string (fst (pair ())) + string (snd (pair ()))
                            })
                    }
                })
        })

let private heldInsideRemovedRow queued () =
    use graph = new Graph ()
    log.Clear ()
    finishes.Clear ()

    let state =
        graph.Run (fun () ->
            {
                Outer = Api.createSignal [| 1; 2 |]
                Inner = Api.createSignal [| 1; 2 |]
            })

    sharedInner <- state.Inner
    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render grouped state)

    mounted.Flush ()
    write graph mounted (fun () -> state.Inner.Value <- [| 1 |])
    let held = textsOf target "b" |> List.ofArray, List.sort (taken ())
    write graph mounted (fun () -> state.Outer.Value <- [| 2 |])
    let removed = textsOf target "b" |> List.ofArray, taken ()
    finish "22"
    write graph mounted (fun () -> state.Inner.Value <- [| 1; 3 |])
    let later = textsOf target "b" |> List.ofArray
    mounted.Dispose ()
    let disposed = taken ()

    equal
        ((([ "11"; "12"; "21"; "22" ], [ "exit 12"; "exit 22" ]), ([ "21"; "22" ], [])), ([ "21"; "23" ], [ "cancel 12" ]))
        ((held, removed), (later, disposed))

let private finishedAtOnce queued () =
    use graph = new Graph ()
    log.Clear ()
    let state = graph.Run (fun () -> rows [| 1; 2 |] false)
    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render immediate state)

    mounted.Flush ()
    write graph mounted (fun () -> state.Items.Value <- [| 2 |])
    let removed = items target
    write graph mounted (fun () -> state.Items.Value <- [| 3; 2 |])
    let added = items target
    mounted.Dispose ()
    equal ([ "2" ], [ "3"; "2" ]) (removed, added)

let private adoptedRowsRetire () =
    use graph = new Graph ()
    log.Clear ()
    finishes.Clear ()
    let state = graph.Run (fun () -> rows [| 1; 2; 3 |] true)
    let target = host ()
    loadServed target (Server.render omit exiting state)

    let mounted =
        Hydration.hydrate graph None strict { IdPrefix = "r"; Strict = true } target exiting state

    write graph mounted (fun () -> state.Items.Value <- [| 1; 3 |])
    let held = items target, taken ()
    write graph mounted (fun () -> state.Items.Value <- [| 3; 4; 1 |])
    let reordered = childrenAt "ul" target
    finish "2"
    let finished = childrenAt "ul" target
    mounted.Dispose ()
    equal ((([ "1"; "2"; "3" ], [ "exit 2" ]), [ "3"; "4"; "1"; "2" ]), [ "3"; "4"; "1" ]) ((held, reordered), finished)

let private moveCarriesHeldRoots queued () =
    use graph = new Graph ()
    let state, target, mounted = mountExiting queued graph [| 1; 2; 3 |]
    write graph mounted (fun () -> state.Items.Value <- [| 1; 3 |])
    let destination = host ()
    mounted.MoveBefore (destination, None)
    let moved = items destination, items target
    write graph mounted (fun () -> state.Items.Value <- [| 3; 1 |])
    let reordered = childrenAt "ul" destination
    finish "2"
    let finished = items destination
    mounted.Dispose ()
    equal ((([ "1"; "2"; "3" ], []), [ "3"; "1"; "2" ]), [ "3"; "1" ]) ((moved, reordered), finished)

type Pair =
    {
        Left: Signal<int array>
        Right: Signal<int array>
        Shown: Signal<bool>
    }

let private measuring (name: string) (node: HTMLElement) =
    {
        Before = fun () -> log.Add (sprintf "%s before %s" name node.textContent)
        After = fun () -> log.Add (sprintf "%s after %s" name node.textContent)
    }

/// <summary>Two lists under one measured element, and an arm switch under another.</summary>
let private measured =
    Component.template
        (fun _ (state: Pair) -> state)
        (div () {
            ul (Hooks.reflow (fun (_: Pair) (node: HTMLUListElement) -> measuring "ul" node)) {
                forEach id (fun (s: Pair) -> s.Left.Value) (li () { fun (row: unit -> int) -> "L" + string (row ()) })
                forEach id (fun (s: Pair) -> s.Right.Value) (li () { fun (row: unit -> int) -> "R" + string (row ()) })
            }

            p (Hooks.reflow (fun (_: Pair) (node: HTMLParagraphElement) -> measuring "p" node)) {
                showElse (fun (s: Pair) -> s.Shown.Value) (b () { "on" }) (i () { "off" })
            }
        })

let private mountMeasured queued (graph: Graph) =
    log.Clear ()

    let state =
        graph.Run (fun () ->
            {
                Left = Api.createSignal [| 1 |]
                Right = Api.createSignal [| 1 |]
                Shown = Api.createSignal true
            })

    let mounted =
        mountTemplate queued graph strict (host ()) (Component.render measured state)

    mounted.Flush ()
    state, mounted

let private reflowAroundListPass queued () =
    use graph = new Graph ()
    let state, mounted = mountMeasured queued graph
    let first = taken ()
    write graph mounted (fun () -> state.Left.Value <- [| 2; 1 |])
    let pass = taken ()
    mounted.Dispose ()
    equal ([], [ "ul before L1R1"; "ul after L2L1R1" ]) (first, pass)

let private reflowAroundArmPass queued () =
    use graph = new Graph ()
    let state, mounted = mountMeasured queued graph
    write graph mounted (fun () -> state.Shown.Value <- false)
    let pass = taken ()
    mounted.Dispose ()
    equal [ "p before on"; "p after off" ] pass

/// <summary>Both lists under the measured element change in one batch: one <c>Before</c> and one <c>After</c>.</summary>
let private reflowPerFlush queued () =
    use graph = new Graph ()
    let state, mounted = mountMeasured queued graph

    write graph mounted (fun () ->
        Api.batch (fun () ->
            state.Left.Value <- [| 3 |]
            state.Right.Value <- [| 4 |]))

    let pass = taken ()
    mounted.Dispose ()

    equal [ "ul before L1R1"; "ul after L3R4" ] pass

/// <summary>Under queued commits, <c>After</c> runs once the pass's rows have their first commits.</summary>
let private reflowAfterQueuedCommits () =
    use graph = new Graph ()
    log.Clear ()
    let state = graph.Run (fun () -> rows [| 1 |] false)

    let template =
        Component.template
            (fun _ (state: Rows) -> state)
            (ul (Hooks.reflow (fun (_: Rows) (node: HTMLUListElement) -> measuring "ul" node)) {
                forEach id (fun (s: Rows) -> s.Items.Value) (li () { fun (row: unit -> int) -> string (row ()) })
            })

    let mounted =
        mountTemplate true graph strict (host ()) (Component.render template state)

    mounted.Flush ()
    graph.Run (fun () -> state.Items.Value <- [| 1; 2 |])
    let beforeFlush = taken ()
    mounted.Flush ()
    let afterFlush = taken ()
    mounted.Dispose ()
    equal ([ "ul before 1" ], [ "ul after 12" ]) (beforeFlush, afterFlush)

/// <summary>A measured list inside a shown arm.</summary>
let private measuredArm =
    Component.template
        (fun _ (state: Rows) -> state)
        (div () {
            show
                (fun (s: Rows) -> s.Shown.Value)
                (ul (Hooks.reflow (fun (_: Rows) (node: HTMLUListElement) -> measuring "ul" node)) {
                    forEach id (fun (s: Rows) -> s.Items.Value) (li () { fun (row: unit -> int) -> string (row ()) })
                })
        })

/// <summary>The measurement step of the current <c>failingMeasure</c> case that raises: "before", "after" or none.</summary>
let mutable private failingStep = ""

let private failingMeasure (_: Rows) (_: HTMLUListElement) =
    let fail (step: string) =
        if failingStep = step then
            failingStep <- ""
            raise (InvalidOperationException (step + " failed"))

    {
        Before = fun () -> fail "before"
        After = fun () -> fail "after"
    }

/// <summary>A list whose reflow raises from the step named by <c>failingStep</c>, once.</summary>
let private failingReflow =
    Component.template
        (fun _ (state: Rows) -> state)
        (ul (Hooks.reflow failingMeasure) { forEach id (fun (s: Rows) -> s.Items.Value) (li () { fun (row: unit -> int) -> string (row ()) }) })

let private reflowFailuresReported queued () =
    use graph = new Graph ()
    failingStep <- ""
    let state = graph.Run (fun () -> rows [| 1 |] false)
    let target = host ()
    let reports = ResizeArray<exn list>()

    let mounted =
        mountTemplate queued graph reports.Add target (Component.render failingReflow state)

    mounted.Flush ()
    failingStep <- "before"
    write graph mounted (fun () -> state.Items.Value <- [| 1; 2 |])
    let afterBefore = items target
    failingStep <- "after"
    write graph mounted (fun () -> state.Items.Value <- [| 1; 2; 3 |])
    let afterAfter = items target
    write graph mounted (fun () -> state.Items.Value <- [| 3; 2; 1 |])
    let later = items target
    mounted.Dispose ()

    equal
        (([ "1"; "2" ], [ "1"; "2"; "3" ], [ "3"; "2"; "1" ]), [ [ "before failed" ]; [ "after failed" ] ])
        ((afterBefore, afterAfter, later), messages reports)

let private reflowWithdrawnWithInstance queued () =
    use graph = new Graph ()
    log.Clear ()
    let state = graph.Run (fun () -> rows [| 1 |] true)
    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render measuredArm state)

    mounted.Flush ()
    let measuredList: Node = unbox (target.querySelector "ul")
    let registered = Departures.hasReflow measuredList
    write graph mounted (fun () -> state.Shown.Value <- false)
    let withdrawn = not (Departures.hasReflow measuredList)
    mounted.Dispose ()
    equal (true, true) (registered, withdrawn)

let private reflowSkipsHydration () =
    use graph = new Graph ()
    log.Clear ()

    let state =
        graph.Run (fun () ->
            {
                Left = Api.createSignal [| 1 |]
                Right = Api.createSignal [| 1 |]
                Shown = Api.createSignal true
            })

    let target = host ()
    loadServed target (Server.render omit measured state)

    let mounted =
        Hydration.hydrate graph None strict { IdPrefix = "r"; Strict = true } target measured state

    let first = taken ()
    write graph mounted (fun () -> state.Right.Value <- [||])
    let pass = taken ()
    mounted.Dispose ()
    equal ([], [ "ul before L1R1"; "ul after L1" ]) (first, pass)

let private waiting = Component.template (fun _ () -> ()) (span () { "loading" })

let private failed =
    Component.template (fun _ (failure: BoundaryFailure) -> failure) (span () { "failed" })

let private ready =
    Component.template (fun _ (status: BoundaryStatus) -> status) (em () { "ready" })

/// <summary>Rows whose exiting root holds a nested list, a shown arm and a boundary.</summary>
let private nestedContent =
    Component.template
        (fun _ (state: Rows) -> state)
        (ul () {
            forEach
                id
                (fun (s: Rows) -> s.Items.Value)
                (li (Hooks.exit (fun (_: unit -> int) (node: HTMLLIElement) finish -> exitOf node finish)) {
                    forEach id (fun (row: unit -> int) -> [| row () * 10 + 1; row () * 10 + 2 |]) (b () { fun (row: unit -> int) -> string (row ()) })
                    show (fun (_: unit -> int) -> true) (i () { "x" })
                    boundary waiting failed ready
                })
        })

[<Emit("$0.querySelectorAll('li')[$1].innerHTML")>]
let private rowMarkup (target: HTMLElement) (index: int) : string = jsNative

[<Emit("Array.from($0.querySelectorAll('li'), n => n.textContent)")>]
let private rowTexts (target: HTMLElement) : string array = jsNative

let private heldRootKeepsContent queued () =
    use graph = new Graph ()
    log.Clear ()
    finishes.Clear ()
    let state = graph.Run (fun () -> rows [| 1; 2 |] true)
    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render nestedContent state)

    mounted.Flush ()
    let before = rowMarkup target 0
    write graph mounted (fun () -> state.Items.Value <- [| 2 |])
    let held = rowMarkup target 0, taken ()
    let rowsHeld = List.ofArray (rowTexts target)
    finish "1112xready"
    let finished = List.ofArray (rowTexts target)
    mounted.Dispose ()

    equal (((before, [ "exit 1112xready" ]), [ "1112xready"; "2122xready" ]), [ "2122xready" ]) ((held, rowsHeld), finished)

/// <summary>Rows of two roots, the first with an exit that raises.</summary>
let private raisingFirstRoot =
    Component.template
        (fun _ (state: Rows) -> state)
        (div () {
            forEach
                id
                (fun (s: Rows) -> s.Items.Value)
                (fragment
                    [|
                        b (Hooks.exit (fun (_: unit -> int) (_: HTMLElement) _ -> raise (InvalidOperationException "exit failed"))) {
                            fun (row: unit -> int) -> string (row ())
                        }
                        i () { "tail" }
                    |])
        })

let private raisingExitKeepsRegion queued () =
    use graph = new Graph ()
    let state = graph.Run (fun () -> rows [| 1; 2 |] false)
    let target = host ()
    let reports = ResizeArray<exn list>()

    let mounted =
        mountTemplate queued graph reports.Add target (Component.render raisingFirstRoot state)

    mounted.Flush ()
    write graph mounted (fun () -> state.Items.Value <- [| 2 |])
    let removed = childrenAt "div" target
    write graph mounted (fun () -> state.Items.Value <- [| 2; 3 |])
    let added = childrenAt "div" target
    mounted.Dispose ()
    let reported = messages reports |> List.concat

    equal
        (([ "2"; "tail" ], [ "2"; "tail"; "3"; "tail" ]), true)
        ((removed, added),
         (not reported.IsEmpty
          && List.forall (fun message -> message = "exit failed") reported))

let private pendingMotion (row: unit -> int) (_: HTMLElement) (motion: MotionContext) =
    let name = string (row ())
    fun () -> log.Add (sprintf "cleanup %s exit pending=%b" name (motion.ExitPending ()))

/// <summary>Rows with a motion hook before an exit hook, after it, and without one.</summary>
let private pendingExits =
    Component.template
        (fun _ (state: Rows) -> state)
        (ul () {
            forEach
                id
                (fun (s: Rows) -> s.Items.Value)
                (li (Hooks.motion pendingMotion, Hooks.exit (fun (_: unit -> int) (node: HTMLLIElement) finish -> exitOf node finish)) {
                    b (Hooks.motion pendingMotion) { fun (row: unit -> int) -> string (row ()) }
                })

            forEach
                id
                (fun (s: Rows) -> if s.Shown.Value then [| 7 |] else [||])
                (li (Hooks.exit (fun (_: unit -> int) (node: HTMLLIElement) finish -> exitOf node finish), Hooks.motion pendingMotion) { "after" })
        })

let private motionCleanupSeesExit queued () =
    use graph = new Graph ()
    log.Clear ()
    finishes.Clear ()
    let state = graph.Run (fun () -> rows [| 1 |] true)
    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render pendingExits state)

    mounted.Flush ()
    write graph mounted (fun () -> state.Items.Value <- [||])
    let first = List.sort (taken ())
    write graph mounted (fun () -> state.Shown.Value <- false)
    let second = List.sort (taken ())
    mounted.Dispose ()

    equal
        ([ "cleanup 1 exit pending=false"; "cleanup 1 exit pending=true"; "exit 1" ], [ "cleanup 7 exit pending=true"; "exit after" ])
        (first, second)

#if RANVIER_VIEW_COUNTERS
/// <summary>
/// The view counters of keyed-list and arm-list operations with no motion, exit or reflow hook, run before any hook of
/// this file registers. The counts are those of 268c357, before the motion seam.
/// </summary>
let private countersWithoutHooks () =
    let validated, lookups, runs, held =
        Mounting.ListCounters.validatedRows.Value,
        Mounting.ListCounters.lookups.Value,
        Mounting.ListCounters.increasingRuns.Value,
        PlacementCounters.held.Value

    let target = host ()
    let list = KeyedRowsPerf.mountKeyed target
    list.Create 1000
    list.UpdateEvery10th ()
    list.Swap ()
    list.Remove 5
    list.Append 100
    list.Clear ()
    list.Create 200
    list.Dispose ()
    let arms = KeyedRowsPerf.mountArms target
    arms.Create 200
    arms.Switch ()
    arms.Payload ()
    arms.Clear ()
    arms.Dispose ()

    equal
        (210, 1502, 0, 0, false)
        (Mounting.ListCounters.validatedRows.Value
         - validated,
         Mounting.ListCounters.lookups.Value - lookups,
         Mounting.ListCounters.increasingRuns.Value - runs,
         PlacementCounters.held.Value - held,
         (Departures.seam.Enabled = true
          || Departures.seam.ReflowEnabled = true))
#endif

let cases: (string * (unit -> unit)) array =
    strictCases (
        Array.concat
            [
#if RANVIER_VIEW_COUNTERS
                [|
                    "keyed and arm lists with no motion hook count the work they counted before the seam", countersWithoutHooks
                |]
#endif
                templated "rows and a shown arm are inserted only after their region's first pass" insertedAfterFirstPass
                templated "an arm shown by the first pass is not inserted" shownOnFirstPass
                templated "rows created by the first pass of a region inside an inserted row are not inserted" nestedRowsDoNotEnter
                [|
                    "hydrated rows and arms are hydrated and not inserted; later ones are inserted", hydratedThenInserted
                |]
                templated "an exit holds its row's root until finish" heldUntilFinish
                templated "reorders and inserts validate while roots are held" reorderAndInsertWhileHeld
                templated "clearing a list with exits holds every root" clearHoldsEveryRoot
                templated "a key re-added while its old root leaves gets a new row" readdedKeyGetsNewRow
                templated "an arm switch leaves the old and new arm roots in order" armSwitchKeepsOrder
                templated "disposing the mount cancels and removes every held root" disposalEndsExits
                templated "an exit finishes after the safety timeout" finishesAfterTimeout
                templated "a root held inside a removed row leaves with the row and runs until disposal" heldInsideRemovedRow
                templated "an exit that finishes at once removes its root at once" finishedAtOnce
                [|
                    "a hydrated row's root with an exit is held when its row is retired", adoptedRowsRetire
                |]
                templated "moving the mount carries its held roots" moveCarriesHeldRoots
                templated "a reflow measures before a list pass and after its layout" reflowAroundListPass
                templated "a reflow measures before an arm pass and after the new arm is placed" reflowAroundArmPass
                templated "a reflow measures once per flush across the passes under its element" reflowPerFlush
                [|
                    "a reflow's After follows the pass's queued commits", reflowAfterQueuedCommits
                |]
                templated "a disposed instance withdraws its reflow registration" reflowWithdrawnWithInstance
                templated "a raising reflow Before or After is reported once and the list keeps updating" reflowFailuresReported
                templated "a held root keeps its nested lists, arms and boundaries until finish" heldRootKeepsContent
                templated "a raising exit on a row's first root removes every root and keeps the region valid" raisingExitKeepsRegion
                templated "a motion hook's cleanup reads whether an exit of its element follows" motionCleanupSeesExit
                [|
                    "a reflow skips the hydrating pass and measures later passes", reflowSkipsHydration
                |]
            ]
    )
