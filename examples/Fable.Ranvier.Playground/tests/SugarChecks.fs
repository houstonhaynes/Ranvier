/// <summary>Sugared and explicit twins mounted side by side, driven by the same writes, and hydrated from each other's server HTML.</summary>
module SugarChecks

open System
open global.Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View
open Ranvier.View.Dom
open CheckSupport
open SugarFixtures

type private Step =
    | Write of (unit -> unit)
    | Each of (HTMLElement -> unit)

/// <summary>Native writes recorded under a root since the previous drain.</summary>
type private WriteWatch =
    abstract drain: unit -> string array
    abstract stop: unit -> unit

[<Emit("(() => { const o = new MutationObserver(() => {}); o.observe($0, { attributes: true, childList: true, characterData: true, subtree: true }); return { drain: () => o.takeRecords().map(r => r.type + ':' + (r.attributeName ?? '') + ':' + r.addedNodes.length + ':' + r.removedNodes.length), stop: () => o.disconnect() }; })()")>]
let private watchWrites (root: HTMLElement) : WriteWatch = jsNative

/// <summary>Listener registrations made through <c>addEventListener</c> since the wrap was installed.</summary>
type private ListenerWatch =
    abstract under: HTMLElement -> int
    abstract stop: unit -> unit

[<Emit("(() => { const log = []; const proto = window.EventTarget.prototype; const original = proto.addEventListener; proto.addEventListener = function (type, ...rest) { log.push([this, type]); return original.call(this, type, ...rest); }; return { under: (root) => log.filter(([target]) => target instanceof Node && root.contains(target)).length, stop: () => { proto.addEventListener = original; } }; })()")>]
let private watchListeners () : ListenerWatch = jsNative

[<Emit("globalThis.ranvierExpectCounters === true")>]
let private countersExpected () : bool = jsNative

/// <summary>The graph counters as name-value pairs; fails when counters are expected but not compiled.</summary>
let private tally () : (string * float) list =
#if RANVIER_COUNTERS
    [ for name, value in Counters.Snapshot () -> name, float value ]
#else
    if countersExpected () then
        failwith "The run expects counters; compile with RanvierCounters=true"

    []
#endif

let private delta (before: (string * float) list) (after: (string * float) list) =
    List.map2 (fun (name, was) (_, now) -> name, now - was) before after

let private click (selector: string) (root: HTMLElement) =
    (root.querySelector selector |> unbox<HTMLElement>).click()

/// <summary>
/// One twin's run: markup, reports, counter deltas, listener registrations under the host and native writes after the
/// mount and each step, plus the counter delta and writes of the disposal.
/// </summary>
type private Trace =
    {
        Shots: string list
        Reports: string list list
        Counts: (string * float) list list
        Listeners: int list
        Writes: string array list
    }

/// <summary>
/// Mounts <c>view</c> on a graph and state of its own, runs <c>steps</c> and disposes. Counters are read around the
/// mount, each step and the disposal.
/// </summary>
let private run queued (make: Graph -> 'state) (view: 'state -> Program.View) (steps: 'state -> Step list) =
    use graph = new Graph ()
    let state = make graph
    let target = host ()
    let reports = ResizeArray<exn list>()
    let writes = watchWrites target
    let listeners = watchListeners ()

    try
        let shots, counts, registrations, native =
            ResizeArray (), ResizeArray (), ResizeArray (), ResizeArray ()

        let measured (act: unit -> unit) =
            let before = tally ()
            act ()
            counts.Add (delta before (tally ()))

        let mounted = ref Unchecked.defaultof<_>

        measured (fun () ->
            mounted.Value <- mountTemplate queued graph reports.Add target (view state)
            mounted.Value.Flush ())

        let record () =
            shots.Add target.innerHTML
            registrations.Add (listeners.under target)
            native.Add (writes.drain ())

        record ()

        for step in steps state do
            measured (fun () ->
                match step with
                | Write write -> graph.Run write
                | Each act -> act target

                mounted.Value.Flush ())

            record ()

        measured mounted.Value.Dispose
        shots.Add target.innerHTML
        native.Add (writes.drain ())

        {
            Shots = List.ofSeq shots
            Reports = messages reports
            Counts = List.ofSeq counts
            Listeners = List.ofSeq registrations
            Writes = List.ofSeq native
        },
        state
    finally
        writes.stop ()
        listeners.stop ()

/// <summary>Runs the explicit twin, then the sugared twin, each on its own graph and state.</summary>
let private twins queued make explicitView sugarView steps =
    let explicitTrace, explicitState = run queued make explicitView steps
    let sugarTrace, sugarState = run queued make sugarView steps
    (explicitTrace, sugarTrace), (explicitState, sugarState)

let private same (explicitTrace: Trace, sugarTrace: Trace) =
    equal explicitTrace.Shots sugarTrace.Shots
    equal explicitTrace.Reports sugarTrace.Reports

    for index, (left, right) in List.indexed (List.zip explicitTrace.Counts sugarTrace.Counts) do
        equal (index, left) (index, right)

    equal explicitTrace.Listeners sugarTrace.Listeners

    for index, (left, right) in List.indexed (List.zip explicitTrace.Writes sugarTrace.Writes) do
        equal (index, left) (index, right)

let private pair queued make explicitView sugarView steps =
    same (fst (twins queued make explicitView sugarView steps))

let private benchClicks queued () =
    pair queued (fun _ -> ()) (fun () -> Component.render benchExplicit 3) (fun () -> Component.render benchSugar 3) (fun () ->
        [ Each (click "button"); Each (click "button") ])

let private textUpdates queued () =
    let make (graph: Graph) =
        graph.Run (fun () -> Api.createSignal "a")

    pair queued make (Component.render textsExplicit) (Component.render textsSugar) (fun name -> [ Write (fun () -> name.Value <- "b") ])

let private keyedSteps (rows: Signal<KeyedItem array>) =
    let removed = ref Unchecked.defaultof<KeyedItem>

    [
        Write (fun () -> rows.Value <- Array.rev rows.Value)
        Write (fun () ->
            removed.Value <- rows.Value[0]
            rows.Value <- rows.Value[1..])
        Write (fun () -> rows.Value <- Array.append rows.Value [| removed.Value |])
        Write (fun () -> rows.Value <- [| rows.Value[0]; rows.Value[0] |])
        Write (fun () ->
            rows.Value <-
                [|
                    {
                        Id = 2
                        Label = Api.createSignal "replaced"
                    }
                    removed.Value
                |])
        Write (fun () -> rows.Value[0].Label.Value <- "relabelled")
        Write (fun () -> rows.Value <- [||])
    ]

let private keyedRows (graph: Graph) =
    graph.Run (fun () -> Api.createSignal (keyedItems ()))

let private keyedLive queued () =
    pair queued keyedRows (Component.render keyedLiveExplicit) (Component.render keyedLiveSugar) keyedSteps

let private keyedFixed queued () =
    pair queued keyedRows (Component.render keyedFixedExplicit) (Component.render keyedFixedSugar) keyedSteps

let private liveSourceFollowsReplacement queued () =
    use graph = new Graph ()

    let first =
        graph.Run (fun () ->
            {
                KeyedItem.Id = 1
                Label = Api.createSignal "old"
            })

    let rows = graph.Run (fun () -> Api.createSignal [| first |])
    let target = host ()

    let mounted =
        mountTemplate queued graph strict target (Component.render keyedLiveSugar rows)

    let replacement =
        graph.Run (fun () ->
            {
                KeyedItem.Id = 1
                Label = Api.createSignal "new"
            })

    graph.Run (fun () -> rows.Value <- [| replacement |])
    mounted.Flush ()
    let afterReplace = target.textContent
    graph.Run (fun () -> replacement.Label.Value <- "new 2")
    mounted.Flush ()
    let afterNew = target.textContent
    graph.Run (fun () -> first.Label.Value <- "old 2")
    mounted.Flush ()
    equal ("1new", "1new 2", "1new 2") (afterReplace, afterNew, target.textContent)
    mounted.Dispose ()

let private arms queued () =
    pair
        queued
        (fun (graph: Graph) -> graph.Run (fun () -> Api.createSignal (armItems ())))
        (Component.render armsExplicit)
        (Component.render armsSugar)
        (fun rows ->
            [
                Write (fun () -> rows.Value[0].Mode.Value <- Counted 1)
                Write (fun () -> rows.Value[0].Mode.Value <- Counted 2)
                Write (fun () -> rows.Value[1].Mode.Value <- Plain)
                Write (fun () -> rows.Value <- Array.rev rows.Value)
            ])

/// <summary>The twins of a <c>Toggle</c> fixture: each has its own signals and log.</summary>
let private toggled queued (explicitDefinition: Component<Toggle>) (sugarDefinition: Component<Toggle>) () =
    let traces, (explicitState, sugarState) =
        twins
            queued
            (fun (graph: Graph) -> graph.Run (fun () -> toggle true))
            (Component.render explicitDefinition)
            (Component.render sugarDefinition)
            (fun state ->
                [
                    Write (fun () -> state.On.Value <- false)
                    Write (fun () -> state.Name.Value <- "m")
                    Write (fun () -> state.On.Value <- true)
                    Write (fun () -> state.On.Value <- false)
                ])

    same traces
    equal (List.ofSeq explicitState.Log) (List.ofSeq sugarState.Log)
    equal [ "mounted"; "cleanup"; "mounted"; "cleanup" ] (List.ofSeq sugarState.Log)

let private nested queued () =
    pair queued (fun (graph: Graph) -> graph.Run board) (Component.render nestedExplicit) (Component.render nestedSugar) (fun state ->
        [
            Write (fun () -> state.Entries.Value[0].Open.Value <- true)
            Write (fun () -> state.Entries.Value <- Array.rev state.Entries.Value)
            Write (fun () -> state.Listing.Value <- false)
            Write (fun () -> state.Listing.Value <- true)
            Write (fun () -> state.Entries.Value[1].Open.Value <- false)
        ])

/// <summary>A live <c>forEach</c> in a <c>matchWith</c> arm: the twins agree on markup, resources and the order of row mounts and cleanups.</summary>
let private nestedLive queued () =
    let traces, (explicitState, sugarState) =
        twins queued (fun (graph: Graph) -> graph.Run lined) (Component.render nestedLiveExplicit) (Component.render nestedLiveSugar) (fun state ->
            [
                Write (fun () -> state.Lines.Value <- Array.rev state.Lines.Value)
                Write (fun () -> state.Lines.Value <- state.Lines.Value[1..])
                Write (fun () -> state.Shown.Value <- false)
                Write (fun () -> state.Shown.Value <- true)
                Write (fun () -> state.Lines.Value[0].Label.Value <- "relabelled")
            ])

    same traces
    equal (List.ofSeq explicitState.Trail) (List.ofSeq sugarState.Trail)
    equal false (explicitState.Trail.Count = 0)

let private bounded queued () =
    pair queued (fun _ -> ()) (fun () -> Component.render boundedExplicit ()) (fun () -> Component.render boundedSugar ()) (fun () -> [])

[<Emit("Array.from($1.querySelectorAll($0)).forEach(node => node.click())")>]
let private clickAll (selector: string) (root: HTMLElement) : unit = jsNative

/// <summary>Clicks every button twice on both twins; each static <c>View</c> ran once.</summary>
let private mixedClicks (sugared: Component<unit>) queued () =
    pair queued (fun _ -> ()) (fun () -> Component.render MixedExplicit.root ()) (fun () -> Component.render sugared ()) (fun () ->
        [ Each (clickAll "button"); Each (clickAll "button") ])

    equal (1, 1) (Builds.counterRecord, Builds.counterClass)

/// <summary>Props are read once per instance, untracked: a later write to the caption signal leaves the child unchanged.</summary>
let private renderPropsAreUntracked queued () =
    let traces, _ =
        twins
            queued
            (fun (graph: Graph) ->
                graph.Run (fun () ->
                    {
                        Captioned.Caption = Api.createSignal "one"
                    }))
            (Component.render untrackedExplicit)
            (Component.render untrackedSugar)
            (fun state -> [ Write (fun () -> state.Caption.Value <- "two") ])

    same traces
    // Shot 0 is the mount, shot 1 the markup after the write.
    equal true ((List.item 1 (snd traces).Shots).Contains "one")

[<Emit("(() => { const w = document.createTreeWalker($0, 0xFFFFFFFF); const a = []; while (w.nextNode()) a.push(w.currentNode); return a; })()")>]
let private nodesOf (root: Node) : Node array = jsNative

[<Emit("$1.contains($0)")>]
let private within (node: Node) (root: Node) : bool = jsNative

let private omit =
    { Server.Options.defaults with
        Policy = Omit
    }

let private strictOptions: Hydration.HydrationOptions =
    { IdPrefix = "r"; Strict = true }

/// <summary>
/// Hydrates the explicit and the sugared definition from the server HTML of <c>served</c>. Both adopt exactly
/// <c>adopts</c> server nodes. After each step both retain equal numbers of the server nodes, and they show the same markup after hydration, each step and disposal.
/// </summary>
/// <remarks>A <c>Write</c> step runs once on the graph; an <c>Each</c> step runs on the explicit host, then the sugared host.</remarks>
let private hydratedSteps
    (served: Component<'props>)
    (explicitDefinition: Component<'props>)
    (sugarDefinition: Component<'props>)
    (props: 'props)
    (steps: Step list)
    (adopts: int)
    (graph: Graph)
    =
    let html = Server.render omit served props
    let left, right = host (), host ()
    left.innerHTML <- html
    right.innerHTML <- html
    let leftNodes, rightNodes = nodesOf left, nodesOf right

    let explicitMount =
        Hydration.hydrate graph None strict strictOptions left explicitDefinition props

    let sugarMount =
        Hydration.hydrate graph None strict strictOptions right sugarDefinition props

    let adopted (before: Node array) (root: HTMLElement) =
        before
        |> Array.filter (fun node -> within node root)
        |> Array.length

    equal adopts leftNodes.Length
    equal adopts (adopted leftNodes left)
    equal adopts (adopted rightNodes right)
    equal left.innerHTML right.innerHTML

    for step in steps do
        match step with
        | Write write -> graph.Run write
        | Each act ->
            act left
            act right

        equal (adopted leftNodes left) (adopted rightNodes right)
        equal left.innerHTML right.innerHTML

    explicitMount.Dispose ()
    sugarMount.Dispose ()
    equal left.innerHTML right.innerHTML

let private hydratedTwins served explicitDefinition sugarDefinition props (writes: (unit -> unit) list) adopts graph =
    hydratedSteps served explicitDefinition sugarDefinition props (List.map Write writes) adopts graph

let private demoState (graph: Graph) =
    graph.Run (fun () ->
        {
            PlaygroundSugar.DemoState.Count = Api.createSignal 0
            PlaygroundSugar.DemoState.Name = Api.createSignal "Ada"
        })

let private demoStyles: (string * Component<PlaygroundSugar.DemoState>) array =
    [|
        "template", PlaygroundSugar.demoSugar
        "record", PlaygroundSugar.demoMembers
        "class", PlaygroundSugar.demoClass
    |]

/// <summary>The number of server nodes each hydrated pair adopts.</summary>
module private Adopted =
    /// <summary>All 6 server nodes.</summary>
    let bench = 6
    /// <summary>All 21 server nodes.</summary>
    let keyed = 21
    /// <summary>All 22 server nodes.</summary>
    let arms = 22
    /// <summary>All 8 server nodes.</summary>
    let show = 8
    /// <summary>All 7 server nodes.</summary>
    let showElse = 7
    /// <summary>All 19 server nodes.</summary>
    let nested = 19
    /// <summary>All 59 server nodes: two root markers, the root div and two panels of 28 (section, h2 and its text, five counters of 5).</summary>
    let mixed = 59
    /// <summary>All 38 server nodes of the demo screen.</summary>
    let demo = 38
    /// <summary>All 30 server nodes of the hidden tag views: two root markers, the div, two hidden shows of 3, an arm of 3, a counter arm of 7, two void tags, a counter of 5 and two rows with their markers.</summary>
    let tagViews = 30
    /// <summary>All 3 server nodes: two root markers and the image.</summary>
    let voidRoot = 3
    /// <summary>All 7 server nodes: two root markers and a counter of 5.</summary>
    let tagRoot = 7

/// <summary>Each pair hydrated from the explicit twin's HTML and from the sugared twin's HTML.</summary>
let private hydration () =
    for fromSugar in [ false; true ] do
        use graph = new Graph ()

        let pick (explicitDefinition: Component<'p>) (sugarDefinition: Component<'p>) =
            if fromSugar then sugarDefinition else explicitDefinition

        hydratedTwins (pick benchExplicit benchSugar) benchExplicit benchSugar 3 [] Adopted.bench graph
        let rows = graph.Run (fun () -> Api.createSignal (keyedItems ()))

        hydratedTwins
            (pick keyedLiveExplicit keyedLiveSugar)
            keyedLiveExplicit
            keyedLiveSugar
            rows
            [ fun () -> rows.Value <- Array.rev rows.Value ]
            Adopted.keyed
            graph

        let armed = graph.Run (fun () -> Api.createSignal (armItems ()))
        hydratedTwins (pick armsExplicit armsSugar) armsExplicit armsSugar armed [ fun () -> armed.Value[1].Mode.Value <- Plain ] Adopted.arms graph
        let state = graph.Run (fun () -> toggle false)

        hydratedTwins
            (pick showExplicit showSugar)
            showExplicit
            showSugar
            state
            [ (fun () -> state.On.Value <- true); (fun () -> state.On.Value <- false) ]
            Adopted.show
            graph

        let elseState = graph.Run (fun () -> toggle false)

        hydratedTwins
            (pick showElseExplicit showElseSugar)
            showElseExplicit
            showElseSugar
            elseState
            [
                (fun () -> elseState.On.Value <- true)
                (fun () -> elseState.On.Value <- false)
            ]
            Adopted.showElse
            graph

        let tagState = graph.Run (fun () -> toggle false)

        hydratedTwins
            (pick tagViewsExplicit tagViewsSugar)
            tagViewsExplicit
            tagViewsSugar
            tagState
            [
                (fun () -> tagState.On.Value <- true)
                (fun () -> tagState.On.Value <- false)
            ]
            Adopted.tagViews
            graph

        hydratedTwins (pick voidRootExplicit voidRootSugar) voidRootExplicit voidRootSugar () [] Adopted.voidRoot graph
        hydratedTwins (pick tagRootExplicit tagRootSugar) tagRootExplicit tagRootSugar () [] Adopted.tagRoot graph
        let listed = graph.Run board

        hydratedTwins
            (pick nestedExplicit nestedSugar)
            nestedExplicit
            nestedSugar
            listed
            [ fun () -> listed.Entries.Value[0].Open.Value <- true ]
            Adopted.nested
            graph

        hydratedSteps
            (pick MixedExplicit.root mixedSugar)
            MixedExplicit.root
            mixedSugar
            ()
            [ Each (clickAll "button"); Each (clickAll "button") ]
            Adopted.mixed
            graph

        for _, definition in demoStyles do
            let state = demoState graph

            hydratedTwins
                (pick PlaygroundSugar.demoExplicit definition)
                PlaygroundSugar.demoExplicit
                definition
                state
                [ fun () -> state.Count.Value <- 4 ]
                Adopted.demo
                graph

/// <summary>The elements under <c>root</c> that carry event handlers, as one <c>position:handler keys</c> entry each.</summary>
[<Emit("Array.from($0.querySelectorAll('*')).map((node, index) => [index, Object.getOwnPropertyNames(node).filter(name => name.startsWith('$$ranvier')).join(',')]).filter(([, keys]) => keys !== '').map(([index, keys]) => index + ':' + keys).join(' ')")>]
let private handlerSites (root: HTMLElement) : string = jsNative

/// <summary>The production keyed fixture and its sugared rewrite, driven by the same operations; markup and handler sites agree after each.</summary>
let private keyedFixtures () =
    let left, right = host (), host ()
    let explicitOps = KeyedRowsPerf.mountKeyed left
    let sugarOps = KeyedRowsSugarPerf.mountKeyed right

    let both (step: KeyedRowsPerf.KeyedOperations -> unit) =
        step explicitOps
        step sugarOps
        equal left.innerHTML right.innerHTML
        equal (handlerSites left) (handlerSites right)

    both (fun ops -> ops.Create 20)
    both (fun ops -> ops.Append 5)
    both (fun ops -> ops.UpdateEvery10th ())
    both (fun ops -> ops.Remove 3)
    both (fun ops -> ops.Create 1000)
    both (fun ops -> ops.Swap ())
    both (fun ops -> ops.Clear ())
    explicitOps.Dispose ()
    sugarOps.Dispose ()

/// <summary>The production arm fixture and its sugared rewrite, driven by the same operations; markup and handler sites agree after each.</summary>
let private armFixtures () =
    let left, right = host (), host ()
    let explicitOps = KeyedRowsPerf.mountArms left
    let sugarOps = KeyedRowsSugarPerf.mountArms right

    let both (step: KeyedRowsPerf.ArmOperations -> unit) =
        step explicitOps
        step sugarOps
        equal left.innerHTML right.innerHTML
        equal (handlerSites left) (handlerSites right)

    both (fun ops -> ops.Create 12)
    both (fun ops -> ops.Switch ())
    both (fun ops -> ops.Payload ())
    both (fun ops -> ops.Switch ())
    both (fun ops -> ops.Clear ())
    explicitOps.Dispose ()
    sugarOps.Dispose ()

let private demoLayout () =
    use graph = new Graph ()
    let state = demoState graph

    for name, definition in demoStyles do
        equal (name, layoutSignature PlaygroundSugar.demoExplicit.Root) (name, layoutSignature definition.Root)
        equal (name, Server.render omit PlaygroundSugar.demoExplicit state) (name, Server.render omit definition state)

[<Emit("(() => { const field = $2.querySelector($0); field.value = $1; field.dispatchEvent(new Event('input', { bubbles: true })); })()")>]
let private typeInto (selector: string) (text: string) (root: HTMLElement) : unit = jsNative

let private demoClicks (definition: Component<PlaygroundSugar.DemoState>) queued () =
    pair queued demoState (Component.render PlaygroundSugar.demoExplicit) (Component.render definition) (fun state ->
        [
            Each (click "[data-testid=reset]")
            Each (click "[data-testid=increment]")
            Each (click "[data-testid=burst]")
            Each (click "[data-testid=reset]")
            Each (typeInto "[data-testid=name]" "Lin")
            Write (fun () -> state.Name.Value <- "Grace")
        ])

let cases: (string * (unit -> unit)) array =
    strictCases (
        Array.concat
            [
                templated "sugared benchmark row clicks like its twin" benchClicks
                templated "sugared text forms update like their twin" textUpdates
                templated "forEach reorders, removes, reinserts, rejects duplicates and replaces like its twin" keyedLive
                templated "forEachFixed reorders, removes, reinserts, rejects duplicates and replaces like its twin" keyedFixed
                templated "a live forEach row reads the replacement item's source" liveSourceFollowsReplacement
                templated "armOf arms switch and update their payload like their twins" arms
                templated "show toggles with cleanups like its twin" (fun queued -> toggled queued showExplicit showSugar)
                templated "showElse toggles with cleanups like its twin" (fun queued -> toggled queued showElseExplicit showElseSugar)
                templated "void and component tags as show content, arms, fragment children and rows update like their twins" (fun queued ->
                    toggled queued tagViewsExplicit tagViewsSugar)
                templated "show in forEachFixed in an armOf arm updates like its twin" nested
                templated "a live forEach in an armOf arm mounts and cleans up rows like its twin" nestedLive
                templated "a boundary around sugared content renders like its twin" bounded
                templated "the mixed tree (free root) updates like its twin" (mixedClicks mixedSugar)
                templated "the mixed tree (t.render root) updates like its twin" (mixedClicks mixedRendered)
                templated "render reads its props once per instance, untracked" renderPropsAreUntracked
                [|
                    "the sugared keyed benchmark fixture renders like the production fixture", keyedFixtures
                    "the sugared arm benchmark fixture renders like the production fixture", armFixtures
                |]
                [|
                    "every demo style shares the explicit screen's layout and server HTML", demoLayout
                |]
                for name, definition in demoStyles do
                    templated (sprintf "the %s demo screen updates like the explicit screen" name) (demoClicks definition)
                [| "sugared and explicit twins hydrate each other's server HTML", hydration |]
            ]
    )
