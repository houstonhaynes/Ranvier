/// <summary>Explicit and sugared twins of each sugar form, shared by the .NET and Fable checks.</summary>
module SugarFixtures

open System
open global.Ranvier
open Fable.Core.TS
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar

let private slotText (slot: Slot) =
    let target =
        match slot.Target with
        | AttributeTarget name -> "attr:" + name
        | PropertyTarget _ -> "prop"
        | TextTarget -> "text"
        | ContentTarget -> "content"

    let mode =
        match slot.Mode with
        | RenderMode -> "render"
        | IsolatedMode -> "isolated"
        | DirectMode -> "direct"

    target + "/" + mode

/// <summary>The skeleton layout of <c>root</c> as text: markup, root count, owners, inlined children and each target with its path and kind; item-row regions include their row's signature.</summary>
let rec layoutSignature (root: TemplateNode) : string =
    let layout = skeletonLayout root

    let describe (node: TemplateNode) =
        match node with
        | ElementNode (tag, attributes, properties, slots, events, placed, _) ->
            sprintf
                "element %s attrs=%A props=%d slots=%A events=%A placed=%d"
                (name tag)
                attributes
                properties.Length
                (Array.map slotText slots)
                (Array.map fst events)
                placed.Length
        | TextSlotNode slot -> "textslot " + slotText slot
        | RegionNode region ->
            match region.Row with
            | ItemRow (row, _) -> "region item {" + layoutSignature row.Root + "}"
            | ArmRow -> "region arm"
            | ViewRow -> "region view"
            | BoundaryRow -> "region boundary"
        | ChildNode (child, _) -> "child {" + layoutSignature child.Root + "}"
        | HeadNode head -> "head " + head.Key
        | TextNode value -> "text " + value
        | FragmentNode _ -> "fragment"

    String.concat
        "\n"
        [
            yield layout.Markup
            yield sprintf "roots=%d owners=%A children=%d" layout.RootCount layout.Owners layout.Children.Length
            for node, path in layout.Targets -> sprintf "%A %s" path (describe node)
        ]

let private omit =
    { Server.Options.defaults with
        Policy = Omit
    }

type BenchRow =
    {
        Index: int
        Source: Signal<string>
        Disabled: Signal<bool>
        Local: Signal<int>
    }

let private benchSetup (_: Setup) (index: int) =
    {
        Index = index
        Source = Api.createSignal "s<1>"
        Disabled = Api.createSignal false
        Local = Api.createSignal 7
    }

let benchExplicit =
    Component.define benchSetup (fun t ->
        t.div
            [|
                t.Attrs.id (fun (r: BenchRow) -> "row-" + string r.Index)
                t.Attrs.className "panel"
                t.Attrs.title (t.from (fun (r: BenchRow) -> r.Source))
            |]
            [|
                t.input [| t.Props.Input.value (t.from (fun (r: BenchRow) -> r.Source)) |]
                t.button
                    [|
                        t.Props.Button.disabled (t.from (fun (r: BenchRow) -> r.Disabled))
                        t.Events.onClick (fun (r: BenchRow) _ -> r.Local.Value <- r.Local.Value + 1)
                    |]
                    [| t.text (fun (r: BenchRow) -> string r.Local.Value) |]
            |])

let benchSugar =
    Component.template
        benchSetup
        (div (Attrs.id (fun r -> "row-" + string r.Index), Attrs.className "panel", Attrs.title (from (fun r -> r.Source))) {
            input (Props.Input.value (from (fun r -> r.Source)))

            button (Props.Button.disabled (from (fun r -> r.Disabled)), Events.onClick (fun r _ -> r.Local.Value <- r.Local.Value + 1)) {
                fun r -> string r.Local.Value
            }
        })

type Texts = { Name: Signal<string> }

let textsExplicit =
    Component.define (fun _ (name: Signal<string>) -> { Name = name }) (fun t ->
        t.div
            [||]
            [|
                t.text "Hello "
                t.text (fun (r: Texts) -> r.Name.Value)
                t.text (t.from (fun (r: Texts) -> r.Name))
                t.br [||]
                t.span [||] [||]
            |])

let textsSugar =
    Component.template
        (fun _ (name: Signal<string>) -> { Name = name })
        (div () {
            "Hello "
            fun (r: Texts) -> r.Name.Value
            from (fun (r: Texts) -> r.Name)
            br ()
            span () { }
        })

let private flags = [ "a"; "b" ]

/// <summary>Document order across for, if, yield! and adjacent texts; the <c>t.text</c> yields mix explicit members into a free body.</summary>
let semantics =
    Component.define (fun _ () -> ()) (fun t ->
        div () {
            "start"

            for flag in flags do
                span (Attrs.className flag) { flag }

            if flags.Length = 2 then
                "wide"

            yield! [| t.text "spliced"; t.text "two" |]
            span () { () }
            span () { }
        })

let semanticsMarkup =
    "<div>start<span class=\"a\">a</span><span class=\"b\">b</span>wide<!--!-->spliced<!--!-->two<span></span><span></span></div>"

/// <summary>A body lambda that ignores its parameter.</summary>
let ignoredParameter =
    Component.template (fun _ () -> ()) (span () { fun _ -> "x" })

let literalText = Component.template (fun _ () -> ()) (span () { "x" })

let private definitionGraph = new Graph ()
let definitionCounter = definitionGraph.Run (fun () -> Api.createSignal 1)

/// <summary>A body line that reads a module-level signal while the definition is built.</summary>
let definitionTimeRead =
    Component.template (fun _ () -> ()) (span () { string definitionCounter.Value })

// tagsExplicit and tagsFree: free tags with t. parts, as a ParamArray of two parts, a void tag and option'.
let tagsExplicit =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [| t.Attrs.className "x"; t.Attrs.id "y" |]
            [|
                t.span [||] [| t.text "a" |]
                t.br [||]
                t.select [||] [| t.option [||] [| t.text "o" |] |]
            |])

let tagsFree =
    Component.define (fun _ () -> ()) (fun t ->
        div (t.Attrs.className "x", t.Attrs.id "y") {
            span () { "a" }
            br ()
            select () { option' () { "o" } }
        })

// headExplicit and headFree: base' renders a base element.
let headExplicit =
    Component.define (fun _ () -> ()) (fun t -> t.head [||] [| t.``base`` [| t.Attrs.attribute ("href", "/") |] |])

let headFree =
    Component.define (fun _ () -> ()) (fun t -> head () { base' (t.Attrs.attribute ("href", "/")) })

let elementPairs: (string * TemplateComponent * TemplateComponent) array =
    [|
        "benchmark row", benchExplicit, benchSugar
        "text forms and void tags", textsExplicit, textsSugar
        "free tags with t. parts", tagsExplicit, tagsFree
        "base' in head", headExplicit, headFree
    |]

let elementServerPairs () : (string * string * string) array =
    let graph = new Graph ()
    let name = graph.Run (fun () -> Api.createSignal "<Ada>")

    [|
        "benchmark row", Server.render omit benchExplicit 3, Server.render omit benchSugar 3
        "text forms and void tags", Server.render omit textsExplicit name, Server.render omit textsSugar name
        "free tags with t. parts", Server.render omit tagsExplicit (), Server.render omit tagsFree ()
        "base' in head", Server.render omit headExplicit (), Server.render omit headFree ()
    |]

type KeyedItem = { Id: int; Label: Signal<string> }

let keyedItems () : KeyedItem array =
    [|
        {
            Id = 1
            Label = Api.createSignal "a<"
        }
        { Id = 2; Label = Api.createSignal "b" }
        { Id = 3; Label = Api.createSignal "c" }
    |]

let private liveRow =
    Component.define (fun _ (item: unit -> KeyedItem) -> item) (fun t ->
        t.tr
            [||]
            [|
                t.td [| t.Attrs.className "id" |] [| t.text (fun (row: unit -> KeyedItem) -> string (row ()).Id) |]
                t.td [||] [| t.text (t.from (fun (row: unit -> KeyedItem) -> (row ()).Label)) |]
            |])

let keyedLiveExplicit =
    Component.define (fun _ (rows: Signal<KeyedItem array>) -> rows) (fun t ->
        t.table
            [||]
            [|
                t.tbody
                    [||]
                    [|
                        t.each (fun (row: KeyedItem) -> row.Id) (fun (rows: Signal<KeyedItem array>) -> rows.Value) liveRow
                    |]
            |])

let keyedLiveSugar =
    Component.template
        (fun _ (rows: Signal<KeyedItem array>) -> rows)
        (table () {
            tbody () {
                forEach
                    (fun (row: KeyedItem) -> row.Id)
                    (fun (rows: Signal<KeyedItem array>) -> rows.Value)
                    (tr () {
                        td (Attrs.className "id") { fun (row: unit -> KeyedItem) -> string (row ()).Id }
                        td () { from (fun (row: unit -> KeyedItem) -> (row ()).Label) }
                    })
            }
        })

let private fixedRow =
    Component.define (fun _ (item: unit -> KeyedItem) -> item ()) (fun t ->
        t.tr
            [||]
            [|
                t.td [| t.Attrs.className "id" |] [| t.text (fun (row: KeyedItem) -> string row.Id) |]
                t.td [||] [| t.text (t.from (fun (row: KeyedItem) -> row.Label)) |]
            |])

let keyedFixedExplicit =
    Component.define (fun _ (rows: Signal<KeyedItem array>) -> rows) (fun t ->
        t.table
            [||]
            [|
                t.tbody
                    [||]
                    [|
                        t.each (fun (row: KeyedItem) -> row.Id) (fun (rows: Signal<KeyedItem array>) -> rows.Value) fixedRow
                    |]
            |])

let keyedFixedSugar =
    Component.template
        (fun _ (rows: Signal<KeyedItem array>) -> rows)
        (table () {
            tbody () {
                forEachFixed
                    (fun (row: KeyedItem) -> row.Id)
                    (fun (rows: Signal<KeyedItem array>) -> rows.Value)
                    (tr () {
                        td (Attrs.className "id") { fun (row: KeyedItem) -> string row.Id }
                        td () { from (fun (row: KeyedItem) -> row.Label) }
                    })
            }
        })

type ArmMode =
    | Plain
    | Counted of int

type ArmItem = { Id: int; Mode: Signal<ArmMode> }

let armItems () : ArmItem array =
    [|
        {
            Id = 1
            Mode = Api.createSignal Plain
        }
        {
            Id = 2
            Mode = Api.createSignal (Counted 4)
        }
    |]

let private plainArm =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.span [| t.Attrs.className "a" |] [| t.text "A" |])

let private countedArm =
    Component.define (fun _ (payload: unit -> int) -> payload) (fun t -> t.b [||] [| t.text (fun (payload: unit -> int) -> string (payload ())) |])

let private armRow =
    Component.define (fun _ (item: unit -> ArmItem) -> item ()) (fun t ->
        t.tr
            [||]
            [|
                t.td [||] [| t.text (fun (row: ArmItem) -> string row.Id) |]
                t.td
                    [||]
                    [|
                        t.matchWith (fun (row: ArmItem) -> row.Mode.Value) (function
                            | Plain -> t.arm plainArm ()
                            | Counted count -> t.arm countedArm count)
                    |]
            |])

let armsExplicit =
    Component.define (fun _ (rows: Signal<ArmItem array>) -> rows) (fun t ->
        t.table
            [||]
            [|
                t.tbody
                    [||]
                    [|
                        t.each (fun (row: ArmItem) -> row.Id) (fun (rows: Signal<ArmItem array>) -> rows.Value) armRow
                    |]
            |])

let private plainFree = armOf (span (Attrs.className "a") { "A" })
let private countedFree = armOf (b () { fun payload -> string (payload ()) })

let armsSugar =
    Component.template
        (fun _ (rows: Signal<ArmItem array>) -> rows)
        (table () {
            tbody () {
                forEachFixed
                    (fun (row: ArmItem) -> row.Id)
                    (fun (rows: Signal<ArmItem array>) -> rows.Value)
                    (tr () {
                        td () { fun (row: ArmItem) -> string row.Id }

                        td () {
                            matchWith (fun (row: ArmItem) -> row.Mode.Value) (function
                                | Plain -> arm plainFree ()
                                | Counted count -> arm countedFree count)
                        }
                    })
            }
        })

type Toggle =
    {
        On: Signal<bool>
        Name: Signal<string>
        Log: ResizeArray<string>
    }

let toggle (on: bool) =
    {
        On = Api.createSignal on
        Name = Api.createSignal "n"
        Log = ResizeArray ()
    }

/// <summary>Logs the content's placement and its cleanup.</summary>
let private logged (state: Toggle) (_: Dom.HTMLSpanElement) =
    state.Log.Add "mounted"
    fun () -> state.Log.Add "cleanup"

let private shownArm =
    Component.define (fun _ (current: unit -> Toggle) -> current ()) (fun t ->
        t.span [| t.Hooks.mounted logged |] [| t.text (t.from (fun (s: Toggle) -> s.Name)) |])

let private hiddenArm =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.fragment [||])

let private elseArm =
    Component.define (fun _ (current: unit -> Toggle) -> current ()) (fun t -> t.em [||] [| t.text "off" |])

let showExplicit =
    Component.define (fun _ (state: Toggle) -> state) (fun t ->
        t.div
            [||]
            [|
                t.text "before"
                t.matchWith (fun (s: Toggle) -> if s.On.Value then ValueSome s else ValueNone) (function
                    | ValueSome s -> t.arm shownArm s
                    | ValueNone -> t.arm hiddenArm ())
                t.text "after"
            |])

let showSugar =
    Component.template
        (fun _ (state: Toggle) -> state)
        (div () {
            "before"
            show (fun s -> s.On.Value) (span (Hooks.mounted logged) { from (fun (s: Toggle) -> s.Name) })
            "after"
        })

let showElseExplicit =
    Component.define (fun _ (state: Toggle) -> state) (fun t ->
        t.div
            [||]
            [|
                t.matchWith (fun (s: Toggle) -> if s.On.Value then Choice1Of2 s else Choice2Of2 s) (function
                    | Choice1Of2 s -> t.arm shownArm s
                    | Choice2Of2 s -> t.arm elseArm s)
            |])

let showElseSugar =
    Component.template
        (fun _ (state: Toggle) -> state)
        (div () { showElse (fun s -> s.On.Value) (span (Hooks.mounted logged) { from (fun (s: Toggle) -> s.Name) }) (em () { "off" }) })

type Entry =
    {
        Id: int
        Label: Signal<string>
        Open: Signal<bool>
    }

type Board =
    {
        Listing: Signal<bool>
        Entries: Signal<Entry array>
    }

let board () =
    {
        Listing = Api.createSignal true
        Entries =
            Api.createSignal
                [|
                    {
                        Id = 1
                        Label = Api.createSignal "one"
                        Open = Api.createSignal false
                    }
                    {
                        Id = 2
                        Label = Api.createSignal "two"
                        Open = Api.createSignal true
                    }
                |]
    }

let private openArm =
    Component.define (fun _ (current: unit -> Entry) -> current ()) (fun t -> t.span [||] [| t.text "open" |])

let private closedArm =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.fragment [||])

let private entryRow =
    Component.define (fun _ (item: unit -> Entry) -> item ()) (fun t ->
        t.li
            [||]
            [|
                t.text (t.from (fun (e: Entry) -> e.Label))
                t.matchWith (fun (e: Entry) -> if e.Open.Value then ValueSome e else ValueNone) (function
                    | ValueSome e -> t.arm openArm e
                    | ValueNone -> t.arm closedArm ())
            |])

let private listingArm =
    Component.define (fun _ (current: unit -> Board) -> current) (fun t ->
        t.ul
            [||]
            [|
                t.each (fun (e: Entry) -> e.Id) (fun (current: unit -> Board) -> (current ()).Entries.Value) entryRow
            |])

let private hiddenBoardArm =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.p [||] [| t.text "hidden" |])

let nestedExplicit =
    Component.define (fun _ (state: Board) -> state) (fun t ->
        t.div
            [||]
            [|
                t.matchWith (fun (b: Board) -> if b.Listing.Value then Choice1Of2 b else Choice2Of2 ()) (function
                    | Choice1Of2 b -> t.arm listingArm b
                    | Choice2Of2 () -> t.arm hiddenBoardArm ())
            |])

let private listingFree =
    armOf (
        ul () {
            forEachFixed
                (fun (e: Entry) -> e.Id)
                (fun (current: unit -> Board) -> (current ()).Entries.Value)
                (li () {
                    from (fun (e: Entry) -> e.Label)
                    show (fun e -> e.Open.Value) (span () { "open" })
                })
        }
    )

let private hiddenBoardFree = armOf (p () { "hidden" })

let nestedSugar =
    Component.template
        (fun _ (state: Board) -> state)
        (div () {
            matchWith
                (fun (board: Board) ->
                    if board.Listing.Value then
                        Choice1Of2 board
                    else
                        Choice2Of2 ())
                (function
                 | Choice1Of2 board -> arm listingFree board
                 | Choice2Of2 () -> arm hiddenBoardFree ())
        })

type Line =
    {
        Id: int
        Label: Signal<string>
        Trail: ResizeArray<string>
    }

type Lined =
    {
        Shown: Signal<bool>
        Lines: Signal<Line array>
        Trail: ResizeArray<string>
    }

let lined () =
    let trail = ResizeArray ()

    let line id label =
        {
            Line.Id = id
            Label = Api.createSignal label
            Trail = trail
        }

    {
        Lined.Shown = Api.createSignal true
        Lines = Api.createSignal [| line 1 "a"; line 2 "b"; line 3 "c" |]
        Trail = trail
    }

/// <summary>Logs a line's mount and cleanup in its trail.</summary>
let private trailed (row: unit -> Line) (_: Dom.HTMLSpanElement) =
    let line = row ()
    line.Trail.Add (sprintf "mounted %d" line.Id)
    fun () -> line.Trail.Add (sprintf "cleanup %d" line.Id)

let private liveLine =
    Component.define (fun _ (row: unit -> Line) -> row) (fun t ->
        t.li
            [||]
            [|
                t.span [| t.Hooks.mounted trailed |] [| t.text (t.from (fun (row: unit -> Line) -> (row ()).Label)) |]
            |])

let private linesArm =
    Component.define (fun _ (current: unit -> Lined) -> current) (fun t ->
        t.ul
            [||]
            [|
                t.each (fun (line: Line) -> line.Id) (fun (current: unit -> Lined) -> (current ()).Lines.Value) liveLine
            |])

let nestedLiveExplicit =
    Component.define (fun _ (state: Lined) -> state) (fun t ->
        t.div
            [||]
            [|
                t.matchWith (fun (s: Lined) -> if s.Shown.Value then Choice1Of2 s else Choice2Of2 ()) (function
                    | Choice1Of2 s -> t.arm linesArm s
                    | Choice2Of2 () -> t.arm hiddenBoardArm ())
            |])

let private linesFree =
    armOf (
        ul () {
            forEach
                (fun (line: Line) -> line.Id)
                (fun (current: unit -> Lined) -> (current ()).Lines.Value)
                (li () { span (Hooks.mounted trailed) { from (fun (row: unit -> Line) -> (row ()).Label) } })
        }
    )

let private hiddenLinesFree = armOf (p () { "hidden" })

let nestedLiveSugar =
    Component.template
        (fun _ (state: Lined) -> state)
        (div () {
            matchWith (fun (s: Lined) -> if s.Shown.Value then Choice1Of2 s else Choice2Of2 ()) (function
                | Choice1Of2 s -> arm linesFree s
                | Choice2Of2 () -> arm hiddenLinesFree ())
        })

let private pendingView =
    Component.define (fun _ () -> ()) (fun t -> t.span [||] [| t.text "loading" |])

let private failedView =
    Component.define (fun _ (failure: BoundaryFailure) -> failure) (fun t -> t.span [||] [| t.text (fun (f: BoundaryFailure) -> f.Error.Message) |])

let private readyExplicit =
    Component.define (fun _ (status: BoundaryStatus) -> status) (fun t -> t.span [| t.Attrs.className "ready" |] [| t.text "ready" |])

let private readySugar =
    Component.template (fun _ (status: BoundaryStatus) -> status) (span (Attrs.className "ready") { "ready" })

let boundedExplicit =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary pendingView failedView readyExplicit |])

let boundedSugar =
    Component.template (fun _ () -> ()) (div () { boundary pendingView failedView readySugar })

let flowPairs: (string * TemplateComponent * TemplateComponent) array =
    [|
        "forEach", keyedLiveExplicit, keyedLiveSugar
        "forEachFixed", keyedFixedExplicit, keyedFixedSugar
        "armOf in forEachFixed", armsExplicit, armsSugar
        "show", showExplicit, showSugar
        "showElse", showElseExplicit, showElseSugar
        "show in forEachFixed in an armOf arm", nestedExplicit, nestedSugar
        "boundary around sugared content", boundedExplicit, boundedSugar
    |]

let flowServerPairs () : (string * string * string) array =
    let graph = new Graph ()

    let keyed, arms, shown, hidden, listed =
        graph.Run (fun () -> Api.createSignal (keyedItems ()), Api.createSignal (armItems ()), toggle true, toggle false, board ())

    [|
        "forEach", Server.render omit keyedLiveExplicit keyed, Server.render omit keyedLiveSugar keyed
        "forEachFixed", Server.render omit keyedFixedExplicit keyed, Server.render omit keyedFixedSugar keyed
        "armOf in forEachFixed", Server.render omit armsExplicit arms, Server.render omit armsSugar arms
        "show on", Server.render omit showExplicit shown, Server.render omit showSugar shown
        "show off", Server.render omit showExplicit hidden, Server.render omit showSugar hidden
        "showElse on", Server.render omit showElseExplicit shown, Server.render omit showElseSugar shown
        "showElse off", Server.render omit showElseExplicit hidden, Server.render omit showElseSugar hidden
        "nested", Server.render omit nestedExplicit listed, Server.render omit nestedSugar listed
        "boundary", Server.render omit boundedExplicit (), Server.render omit boundedSugar ()
    |]

// Declarations: a counter in record, class and template form, a tag class, two parents and the mixed tree.

/// <summary>How many times each static <c>View</c> ran.</summary>
module Builds =
    let mutable counterRecord = 0
    let mutable counterClass = 0

type CounterProps = { Start: int; Caption: string }

type CounterRecord =
    {
        Count: Signal<int>
        Caption: string
    }

    static member Create(_: Setup, props: CounterProps) =
        {
            Count = Api.createSignal props.Start
            Caption = props.Caption
        }

    static member View =
        Builds.counterRecord <- Builds.counterRecord + 1

        div (Attrs.className "counter") {
            span (Attrs.className "caption") { fun c -> c.Caption }

            button (Attrs.attribute ("type", "button"), Events.onClick (fun c _ -> c.Count.Value <- c.Count.Value + 1)) {
                fun c -> string c.Count.Value
            }
        }

let counterRecord = Component.ofMembers<CounterRecord, _>()

type CounterClass(setup: Setup, props: CounterProps) =
    member val Count = Api.createSignal props.Start
    member _.Caption = props.Caption

    static member View: TemplateView<CounterClass> =
        Builds.counterClass <- Builds.counterClass + 1

        div (Attrs.className "counter") {
            span (Attrs.className "caption") { fun (c: CounterClass) -> c.Caption }

            button (Attrs.attribute ("type", "button"), Events.onClick (fun (c: CounterClass) _ -> c.Count.Value <- c.Count.Value + 1)) {
                fun (c: CounterClass) -> string c.Count.Value
            }
        }

let counterClass = Component.ofClass CounterClass

let counterTemplate =
    Component.template
        (fun _ (props: CounterProps) ->
            {
                Count = Api.createSignal props.Start
                Caption = props.Caption
            })
        (div (Attrs.className "counter") {
            span (Attrs.className "caption") { fun c -> c.Caption }

            button (Attrs.attribute ("type", "button"), Events.onClick (fun c _ -> c.Count.Value <- c.Count.Value + 1)) {
                fun c -> string c.Count.Value
            }
        })

/// <summary>The record counter as a tag: a reader or fixed props under one name.</summary>
type counterOf<'i> private (props: 'i -> CounterProps, primary: unit) =
    inherit ComponentTag<CounterProps, 'i>(counterRecord, props)
    new(props: 'i -> CounterProps) = counterOf<'i>(props, ())
    new(props: CounterProps) = counterOf<'i>((fun _ -> props), ())

type PanelProps = { Title: string }

type PanelRecord =
    {
        Title: string
        Hits: Signal<int>
    }

    static member Create(_: Setup, props: PanelProps) =
        {
            Title = props.Title
            Hits = Api.createSignal 0
        }

    static member View =
        section (Attrs.className "panel") {
            h2 () { fun p -> p.Title }
            render counterRecord (fun p -> { Start = 1; Caption = p.Title })
            render counterClass (fun p -> { Start = 2; Caption = p.Title + "!" })
            renderOf counterTemplate { Start = 3; Caption = "fixed" }
            counterOf (fun p -> { Start = 4; Caption = p.Title + "?" })
            counterOf { Start = 5; Caption = "tag" }
        }

let panelRecord = Component.ofMembers<PanelRecord, _>()

type PanelClass(setup: Setup, props: PanelProps) =
    member _.Title = props.Title
    member val Hits = Api.createSignal 0

    static member View: TemplateView<PanelClass> =
        section (Attrs.className "panel") {
            h2 () { fun (p: PanelClass) -> p.Title }
            render counterRecord (fun (p: PanelClass) -> { Start = 1; Caption = p.Title })
            render counterClass (fun (p: PanelClass) -> { Start = 2; Caption = p.Title + "!" })
            renderOf counterTemplate { Start = 3; Caption = "fixed" }
            counterOf (fun (p: PanelClass) -> { Start = 4; Caption = p.Title + "?" })
            counterOf { Start = 5; Caption = "tag" }
        }

let panelClass = Component.ofClass PanelClass

/// <summary>The mixed tree's root as free tags.</summary>
let mixedSugar =
    Component.template
        (fun _ () -> ())
        (div (Attrs.className "mixed") {
            renderOf panelRecord { Title = "R" }
            renderOf panelClass { Title = "C" }
        })

/// <summary>The mixed tree's root, explicit, over the sugared parents.</summary>
let mixedRendered =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [| t.Attrs.className "mixed" |]
            [|
                t.render panelRecord (fun _ -> { Title = "R" })
                t.render panelClass (fun _ -> { Title = "C" })
            |])

/// <summary>The explicit twin of the mixed tree.</summary>
module MixedExplicit =
    type Counter = { Count: Signal<int>; Caption: string }

    let counter =
        Component.define
            (fun _ (props: CounterProps) ->
                {
                    Count = Api.createSignal props.Start
                    Caption = props.Caption
                })
            (fun t ->
                t.div
                    [| t.Attrs.className "counter" |]
                    [|
                        t.span [| t.Attrs.className "caption" |] [| t.text (fun (c: Counter) -> c.Caption) |]
                        t.button
                            [|
                                t.Attrs.attribute ("type", "button")
                                t.Events.onClick (fun (c: Counter) _ -> c.Count.Value <- c.Count.Value + 1)
                            |]
                            [| t.text (fun (c: Counter) -> string c.Count.Value) |]
                    |])

    type Panel = { Title: string; Hits: Signal<int> }

    let panel =
        Component.define
            (fun _ (props: PanelProps) ->
                {
                    Title = props.Title
                    Hits = Api.createSignal 0
                })
            (fun t ->
                t.section
                    [| t.Attrs.className "panel" |]
                    [|
                        t.h2 [||] [| t.text (fun (p: Panel) -> p.Title) |]
                        t.render counter (fun p -> { Start = 1; Caption = p.Title })
                        t.render counter (fun p -> { Start = 2; Caption = p.Title + "!" })
                        t.render counter (fun _ -> { Start = 3; Caption = "fixed" })
                        t.render counter (fun p -> { Start = 4; Caption = p.Title + "?" })
                        t.render counter (fun _ -> { Start = 5; Caption = "tag" })
                    |])

    let root =
        Component.define (fun _ () -> ()) (fun t ->
            t.div
                [| t.Attrs.className "mixed" |]
                [|
                    t.render panel (fun _ -> { PanelProps.Title = "R" })
                    t.render panel (fun _ -> { PanelProps.Title = "C" })
                |])

let declPairs: (string * TemplateComponent * TemplateComponent) array =
    [|
        "mixed tree, free root", MixedExplicit.root, mixedSugar
        "mixed tree, t.render root", MixedExplicit.root, mixedRendered
        "record counter", MixedExplicit.counter, counterRecord
        "class counter", MixedExplicit.counter, counterClass
        "template counter", MixedExplicit.counter, counterTemplate
    |]

let declServerPairs () : (string * string * string) array =
    let graph = new Graph ()
    let counted = { Start = 7; Caption = "<c>" }

    graph.Run (fun () ->
        [|
            "mixed tree, free root", Server.render omit MixedExplicit.root (), Server.render omit mixedSugar ()
            "mixed tree, t.render root", Server.render omit MixedExplicit.root (), Server.render omit mixedRendered ()
            "record counter", Server.render omit MixedExplicit.counter counted, Server.render omit counterRecord counted
            "class counter", Server.render omit MixedExplicit.counter counted, Server.render omit counterClass counted
            "template counter", Server.render omit MixedExplicit.counter counted, Server.render omit counterTemplate counted
        |])

/// <summary>A parent whose child props read a signal: the props are read once per instance, untracked.</summary>
type Captioned = { Caption: Signal<string> }

let untrackedExplicit =
    Component.define (fun _ (state: Captioned) -> state) (fun t ->
        t.div
            [||]
            [|
                t.render MixedExplicit.counter (fun (s: Captioned) -> { Start = 0; Caption = s.Caption.Value })
            |])

let untrackedSugar =
    Component.template
        (fun _ (state: Captioned) -> state)
        (div () { render counterRecord (fun (s: Captioned) -> { Start = 0; Caption = s.Caption.Value }) })

// Void and component tags as views outside an element body, through VoidElement.View: show content, armOf views,
// fragment children, row views and template roots.

/// <summary>Logs the image's placement and its cleanup.</summary>
let private loggedImage (state: Toggle) (_: Dom.HTMLImageElement) =
    state.Log.Add "mounted"
    fun () -> state.Log.Add "cleanup"

let private shownImage =
    Component.define (fun _ (current: unit -> Toggle) -> current ()) (fun t ->
        t.img
            [|
                t.Hooks.mounted loggedImage
                t.Attrs.title (t.from (fun (s: Toggle) -> s.Name))
            |])

let private shownCounter =
    Component.define (fun _ (current: unit -> Toggle) -> current ()) (fun t ->
        t.render MixedExplicit.counter (fun _ -> { Start = 6; Caption = "shown" }))

let private breakArm =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.br [||])

let private ruleArm =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.hr [||])

let private counterArm =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.render MixedExplicit.counter (fun _ -> { Start = 7; Caption = "arm" }))

let private pictureRow =
    Component.define (fun _ (current: unit -> int) -> current ()) (fun t -> t.img [| t.Attrs.title (fun (n: int) -> string n) |])

let tagViewsExplicit =
    Component.define (fun _ (state: Toggle) -> state) (fun t ->
        t.div
            [||]
            [|
                t.matchWith (fun (s: Toggle) -> if s.On.Value then ValueSome s else ValueNone) (function
                    | ValueSome s -> t.arm shownImage s
                    | ValueNone -> t.arm hiddenArm ())
                t.matchWith (fun (s: Toggle) -> if s.On.Value then ValueSome s else ValueNone) (function
                    | ValueSome s -> t.arm shownCounter s
                    | ValueNone -> t.arm hiddenArm ())
                t.matchWith (fun (s: Toggle) -> s.On.Value) (fun on -> if on then t.arm breakArm () else t.arm ruleArm ())
                t.matchWith (fun (s: Toggle) -> s.On.Value) (fun _ -> t.arm counterArm ())
                t.fragment
                    [|
                        t.br [||]
                        t.hr [||]
                        t.render MixedExplicit.counter (fun _ -> { Start = 8; Caption = "f" })
                    |]
                t.each (fun (n: int) -> n) (fun _ -> [| 1; 2 |]) pictureRow
            |])

let private breakFree = armOf (br ()).View
let private ruleFree = armOf (hr ()).View
let private counterFree = armOf (counterOf { Start = 7; Caption = "arm" }).View

let tagViewsSugar =
    Component.template
        (fun _ (state: Toggle) -> state)
        (div () {
            show (fun s -> s.On.Value) (img (Hooks.mounted loggedImage, Attrs.title (from (fun (s: Toggle) -> s.Name)))).View
            show (fun s -> s.On.Value) (counterOf { Start = 6; Caption = "shown" }).View
            matchWith (fun s -> s.On.Value) (fun on -> if on then arm breakFree () else arm ruleFree ())
            matchWith (fun s -> s.On.Value) (fun _ -> arm counterFree ())
            fragment [| (br ()).View; (hr ()).View; (counterOf { Start = 8; Caption = "f" }).View |]
            forEachFixed (fun (n: int) -> n) (fun _ -> [| 1; 2 |]) (img (Attrs.title (fun (n: int) -> string n))).View
        })

let voidRootExplicit =
    Component.define (fun _ () -> ()) (fun t -> t.img [| t.Attrs.className "root" |])

let voidRootSugar =
    Component.template (fun _ () -> ()) (img (Attrs.className "root")).View

let tagRootExplicit =
    Component.define (fun _ () -> ()) (fun t -> t.render MixedExplicit.counter (fun _ -> { Start = 9; Caption = "root" }))

let tagRootSugar =
    Component.template (fun _ () -> ()) (counterOf { Start = 9; Caption = "root" }).View

let tagViewPairs: (string * TemplateComponent * TemplateComponent) array =
    [|
        "void and component tags in show, armOf, fragment and forEachFixed", tagViewsExplicit, tagViewsSugar
        "a void tag as the root", voidRootExplicit, voidRootSugar
        "a component tag as the root", tagRootExplicit, tagRootSugar
    |]

let tagViewServerPairs () : (string * string * string) array =
    let graph = new Graph ()
    let shown, hidden = graph.Run (fun () -> toggle true, toggle false)

    [|
        "tag views on", Server.render omit tagViewsExplicit shown, Server.render omit tagViewsSugar shown
        "tag views off", Server.render omit tagViewsExplicit hidden, Server.render omit tagViewsSugar hidden
        "a void tag as the root", Server.render omit voidRootExplicit (), Server.render omit voidRootSugar ()
        "a component tag as the root", Server.render omit tagRootExplicit (), Server.render omit tagRootSugar ()
    |]

/// <summary>Fails when a hook the server omits runs.</summary>
let private serverRan (part: string) : 'a =
    failwithf "The server ran a %s" part

/// <summary>Lines in a list with motion and exit hooks on each row root, a reflow hook on the list's parent and a motion and exit hook on a shown arm.</summary>
let motionHooked =
    Component.template
        (fun _ (state: Lined) -> state)
        (div () {
            ul (Hooks.reflow (fun _ _ -> serverRan "reflow hook")) {
                forEach
                    (fun (line: Line) -> line.Id)
                    (fun (s: Lined) -> s.Lines.Value)
                    (li (
                        Hooks.motion (System.Func<_, _, _, _>(fun _ _ _ -> serverRan "motion hook")),
                        Hooks.exit (System.Func<_, _, _, _>(fun _ _ _ -> serverRan "exit hook"))
                    ) {
                        from (fun (row: unit -> Line) -> (row ()).Label)
                    })
            }

            show
                (fun s -> s.Shown.Value)
                (span (
                    Hooks.motion (System.Func<_, _, _, _>(fun _ _ _ -> serverRan "motion hook")),
                    Hooks.exit (System.Func<_, _, _, _>(fun _ _ _ -> serverRan "exit hook"))
                ) {
                    "shown"
                })
        })

/// <summary><c>motionHooked</c> without its hooks.</summary>
let motionPlain =
    Component.template
        (fun _ (state: Lined) -> state)
        (div () {
            ul () {
                forEach (fun (line: Line) -> line.Id) (fun (s: Lined) -> s.Lines.Value) (li () { from (fun (row: unit -> Line) -> (row ()).Label) })
            }

            show (fun s -> s.Shown.Value) (span () { "shown" })
        })

/// <summary>The server HTML of <c>motionPlain</c> and <c>motionHooked</c> under <c>Reject</c> and <c>Omit</c>, shown and hidden.</summary>
let motionServerPairs () : (string * string * string) array =
    use graph = new Graph ()

    let shown, hidden =
        graph.Run (fun () ->
            let shown = lined ()
            let hidden = lined ()
            hidden.Shown.Value <- false
            shown, hidden)

    [|
        for policyName, options in [ "Reject", Server.Options.defaults; "Omit", omit ] do
            for stateName, state in [ "shown", shown; "hidden", hidden ] do
                policyName + " " + stateName, Server.render options motionPlain state, Server.render options motionHooked state
    |]
