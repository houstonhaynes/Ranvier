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
    Component.define benchSetup (fun t ->
        t.Div (
            t.Attrs.id (fun (r: BenchRow) -> "row-" + string r.Index),
            t.Attrs.className "panel",
            t.Attrs.title (t.from (fun (r: BenchRow) -> r.Source))
        ) {
            t.Input (t.Props.Input.value (t.from (fun (r: BenchRow) -> r.Source)))

            t.Button (
                t.Props.Button.disabled (t.from (fun (r: BenchRow) -> r.Disabled)),
                t.Events.onClick (fun (r: BenchRow) _ -> r.Local.Value <- r.Local.Value + 1)
            ) {
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
    Component.define (fun _ (name: Signal<string>) -> { Name = name }) (fun t ->
        t.Div () {
            "Hello "
            fun (r: Texts) -> r.Name.Value
            t.from (fun (r: Texts) -> r.Name)
            t.Br ()
            t.Span () { }
        })

let private flags = [ "a"; "b" ]

let semantics =
    Component.define (fun _ () -> ()) (fun t ->
        t.Div () {
            "start"

            for flag in flags do
                t.Span (t.Attrs.className flag) { flag }

            if flags.Length = 2 then
                "wide"

            yield! [| t.text "spliced"; t.text "two" |]
            t.Span () { () }
            t.Span () { }
        })

let semanticsMarkup =
    "<div>start<span class=\"a\">a</span><span class=\"b\">b</span>wide<!--!-->spliced<!--!-->two<span></span><span></span></div>"

/// <summary>A body lambda that ignores its parameter.</summary>
let ignoredParameter =
    Component.define (fun _ () -> ()) (fun t -> t.Span () { fun _ -> "x" })

let literalText = Component.define (fun _ () -> ()) (fun t -> t.Span () { "x" })

let private definitionGraph = new Graph ()
let definitionCounter = definitionGraph.Run (fun () -> Api.createSignal 1)

/// <summary>A body line that reads a module-level signal while the definition is built.</summary>
let definitionTimeRead =
    Component.define (fun _ () -> ()) (fun t -> t.Span () { string definitionCounter.Value })

let elementPairs: (string * TemplateComponent * TemplateComponent) array =
    [|
        "benchmark row", benchExplicit, benchSugar
        "text forms and void tags", textsExplicit, textsSugar
    |]

let elementServerPairs () : (string * string * string) array =
    let graph = new Graph ()
    let name = graph.Run (fun () -> Api.createSignal "<Ada>")

    [|
        "benchmark row", Server.render omit benchExplicit 3, Server.render omit benchSugar 3
        "text forms and void tags", Server.render omit textsExplicit name, Server.render omit textsSugar name
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
    Component.define (fun _ (rows: Signal<KeyedItem array>) -> rows) (fun t ->
        t.Table () {
            t.Tbody () {
                t.forEach (fun (row: KeyedItem) -> row.Id) (fun (rows: Signal<KeyedItem array>) -> rows.Value) (fun r ->
                    r.Tr () {
                        r.Td (r.Attrs.className "id") { r.text (fun (row: unit -> KeyedItem) -> string (row ()).Id) }
                        r.Td () { r.from (fun (row: unit -> KeyedItem) -> (row ()).Label) }
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
    Component.define (fun _ (rows: Signal<KeyedItem array>) -> rows) (fun t ->
        t.Table () {
            t.Tbody () {
                t.forEachFixed (fun (row: KeyedItem) -> row.Id) (fun (rows: Signal<KeyedItem array>) -> rows.Value) (fun r ->
                    r.Tr () {
                        r.Td (r.Attrs.className "id") { r.text (fun (row: KeyedItem) -> string row.Id) }
                        r.Td () { r.from (fun (row: KeyedItem) -> row.Label) }
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

let armsSugar =
    Component.define (fun _ (rows: Signal<ArmItem array>) -> rows) (fun t ->
        let plain =
            t.armOf (fun (a: TemplateBuilder<unit -> unit>) -> a.Span (a.Attrs.className "a") { "A" })

        let counted =
            t.armOf (fun (a: TemplateBuilder<unit -> int>) -> a.B () { fun payload -> string (payload ()) })

        t.Table () {
            t.Tbody () {
                t.forEachFixed (fun (row: ArmItem) -> row.Id) (fun (rows: Signal<ArmItem array>) -> rows.Value) (fun r ->
                    r.Tr () {
                        r.Td () { r.text (fun (row: ArmItem) -> string row.Id) }

                        r.Td () {
                            r.matchWith (fun (row: ArmItem) -> row.Mode.Value) (function
                                | Plain -> r.arm plain ()
                                | Counted count -> r.arm counted count)
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
    Component.define (fun _ (state: Toggle) -> state) (fun t ->
        t.Div () {
            "before"
            t.show (fun s -> s.On.Value) (fun c -> c.Span (c.Hooks.mounted logged) { c.from (fun (s: Toggle) -> s.Name) })
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
    Component.define (fun _ (state: Toggle) -> state) (fun t ->
        t.Div () {
            t.showElse (fun s -> s.On.Value) (fun c -> c.Span (c.Hooks.mounted logged) { c.from (fun (s: Toggle) -> s.Name) }) (fun c ->
                c.Em () { "off" })
        })

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

let nestedSugar =
    Component.define (fun _ (state: Board) -> state) (fun t ->
        let listing =
            t.armOf (fun (a: TemplateBuilder<unit -> Board>) ->
                a.Ul () {
                    a.forEachFixed (fun (e: Entry) -> e.Id) (fun (current: unit -> Board) -> (current ()).Entries.Value) (fun r ->
                        r.Li () {
                            r.from (fun (e: Entry) -> e.Label)
                            r.show (fun e -> e.Open.Value) (fun c -> c.Span () { "open" })
                        })
                })

        let hidden = t.armOf (fun (a: TemplateBuilder<unit -> unit>) -> a.P () { "hidden" })

        t.Div () {
            t.matchWith (fun (b: Board) -> if b.Listing.Value then Choice1Of2 b else Choice2Of2 ()) (function
                | Choice1Of2 b -> t.arm listing b
                | Choice2Of2 () -> t.arm hidden ())
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

let nestedLiveSugar =
    Component.define (fun _ (state: Lined) -> state) (fun t ->
        let listing =
            t.armOf (fun (a: TemplateBuilder<unit -> Lined>) ->
                a.Ul () {
                    a.forEach (fun (line: Line) -> line.Id) (fun (current: unit -> Lined) -> (current ()).Lines.Value) (fun r ->
                        r.Li () { r.Span (r.Hooks.mounted trailed) { r.from (fun (row: unit -> Line) -> (row ()).Label) } })
                })

        let hidden = t.armOf (fun (a: TemplateBuilder<unit -> unit>) -> a.P () { "hidden" })

        t.Div () {
            t.matchWith (fun (s: Lined) -> if s.Shown.Value then Choice1Of2 s else Choice2Of2 ()) (function
                | Choice1Of2 s -> t.arm listing s
                | Choice2Of2 () -> t.arm hidden ())
        })

let private pendingView =
    Component.define (fun _ () -> ()) (fun t -> t.span [||] [| t.text "loading" |])

let private failedView =
    Component.define (fun _ (failure: BoundaryFailure) -> failure) (fun t -> t.span [||] [| t.text (fun (f: BoundaryFailure) -> f.Error.Message) |])

let private readyExplicit =
    Component.define (fun _ (status: BoundaryStatus) -> status) (fun t -> t.span [| t.Attrs.className "ready" |] [| t.text "ready" |])

let private readySugar =
    Component.define (fun _ (status: BoundaryStatus) -> status) (fun t -> t.Span (t.Attrs.className "ready") { "ready" })

let boundedExplicit =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary pendingView failedView readyExplicit |])

let boundedSugar =
    Component.define (fun _ () -> ()) (fun t -> t.Div () { t.boundary pendingView failedView readySugar })

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
