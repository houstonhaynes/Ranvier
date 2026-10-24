namespace Ranvier.Docs.Maps

open System
open System.Collections.Generic
open Fable.Core
open global.Ranvier
open Ranvier.Anime
open Ranvier.View
open Ranvier.View.Sugar

/// <summary>The browser operations of a mounted map; each runs only in a browser hook or handler.</summary>
module internal Browser =
    [<Emit("setTimeout($1, $0)")>]
    let later (milliseconds: int) (run: unit -> unit) : float = jsNative

    [<Emit("clearTimeout($0)")>]
    let cancelLater (handle: float) : unit = jsNative

    [<Emit("requestAnimationFrame(() => $0())")>]
    let frame (run: unit -> unit) : float = jsNative

    [<Emit("cancelAnimationFrame($0)")>]
    let cancelFrame (handle: float) : unit = jsNative

    [<Emit("globalThis.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches === true")>]
    let reducedMotion () : bool = jsNative

    [<Emit("$0.classList.add($1)")>]
    let addClass (element: obj) (name: string) : unit = jsNative

    [<Emit("$0.classList.remove($1)")>]
    let removeClass (element: obj) (name: string) : unit = jsNative

    /// <summary>Forces style recalculation, so a removed and re-added class restarts its animation.</summary>
    [<Emit("void $0.getBoundingClientRect()")>]
    let reflow (element: obj) : unit = jsNative

    /// <summary>The highlighted code lines of the map card holding <c>element</c>, or an empty array outside a card.</summary>
    [<Emit("Array.from($0.closest('.rv-map-card')?.querySelectorAll('.nacara-code__line') ?? [])")>]
    let codeLines (element: obj) : obj array = jsNative

    [<Emit("$0.querySelector($1)")>]
    let query (element: obj) (selector: string) : obj = jsNative

    [<Emit("$0.scrollTop = $0.scrollHeight")>]
    let scrollToEnd (element: obj) : unit = jsNative

/// <summary>A bound input's value; <c>Ranvier.View</c> declares an <c>InputValue</c> operation of its own.</summary>
type internal Shown = InputValue

/// <summary>A widget of a map's control bar.</summary>
[<RequireQualifiedAccess>]
type internal WidgetKind =
    | Press
    | Range of min: int * max: int
    | NumberField
    | TextField
    | CheckBox

/// <summary>A control of the bar: its widget, the value it shows, and the writes it makes.</summary>
[<NoEquality; NoComparison>]
type internal ControlItem =
    {
        Key: int
        Label: string
        Kind: WidgetKind
        /// <summary>The value the widget shows, read reactively.</summary>
        Value: unit -> InputValue
        /// <summary>Applies a value the reader entered.</summary>
        Write: InputValue -> unit
        Press: unit -> unit
    }

/// <summary>A control row's instance: its item and the shown value, which changes only when the value differs.</summary>
[<NoEquality; NoComparison>]
type internal ControlRow =
    {
        Current: unit -> ControlItem
        Shown: Memo<InputValue>
    }

/// <summary>The control bar's rows, one widget per control.</summary>
module internal ControlBar =
    let private shownText (row: ControlRow) =
        match row.Shown.Value with
        | Shown.Integer value -> string value
        | Shown.Number value -> string value
        | Shown.Text value -> value
        | Shown.Toggle value -> string value

    let private inputOf (event: TS.Dom.Event) =
        match event.target with
        | Some target -> unbox<TS.Dom.HTMLInputElement> target
        | None -> invalidOp "An input event has no target."

    let private inputValue (event: TS.Dom.Event) =
        (inputOf event).value

    let private pressArm =
        armOf (
            button (
                Attrs.attribute ("type", "button"),
                Attrs.className "rv-map__button",
                Events.onClick (fun (row: unit -> ControlRow) _ -> (row().Current()).Press())
            ) {
                fun (row: unit -> ControlRow) -> (row().Current()).Label
            }
        )

    let private caption () =
        span (Attrs.className "rv-map__input-label") { fun (row: unit -> ControlRow) -> (row().Current()).Label }

    let private rangeArm =
        armOf (
            label (Attrs.className "rv-map__input rv-map__input--slider") {
                caption ()

                input (
                    Attrs.className "rv-map__input-field",
                    Attrs.attribute ("type", "range"),
                    Attrs.attribute (
                        "min",
                        fun (row: unit -> ControlRow) ->
                            match (row().Current()).Kind with
                            | WidgetKind.Range (min, _) -> Some (string min)
                            | _ -> None
                    ),
                    Attrs.attribute (
                        "max",
                        fun (row: unit -> ControlRow) ->
                            match (row().Current()).Kind with
                            | WidgetKind.Range (_, max) -> Some (string max)
                            | _ -> None
                    ),
                    Attrs.attribute ("step", "1"),
                    Props.Input.value (fun (row: unit -> ControlRow) -> shownText (row ())),
                    Events.onInput (fun (row: unit -> ControlRow) event ->
                        match Int32.TryParse (inputValue event) with
                        | true, value -> (row().Current()).Write(Shown.Integer value)
                        | _ -> ())
                )

                output (Attrs.className "rv-map__input-value") { fun (row: unit -> ControlRow) -> shownText (row ()) }
            }
        )

    let private numberArm =
        armOf (
            label (Attrs.className "rv-map__input rv-map__input--number") {
                caption ()

                input (
                    Attrs.className "rv-map__input-field",
                    Attrs.attribute ("type", "number"),
                    Attrs.attribute ("step", "any"),
                    Props.Input.value (fun (row: unit -> ControlRow) -> shownText (row ())),
                    Events.onChange (fun (row: unit -> ControlRow) event ->
                        Controls.parseNumber (inputValue event)
                        |> Option.iter (fun value -> (row().Current()).Write(Shown.Number value)))
                )
            }
        )

    let private textArm =
        armOf (
            label (Attrs.className "rv-map__input rv-map__input--text") {
                caption ()

                input (
                    Attrs.className "rv-map__input-field",
                    Attrs.attribute ("type", "text"),
                    Props.Input.value (fun (row: unit -> ControlRow) -> shownText (row ())),
                    Events.onChange (fun (row: unit -> ControlRow) event -> (row().Current()).Write(Shown.Text (inputValue event)))
                )
            }
        )

    let private checkArm =
        armOf (
            label (Attrs.className "rv-map__input rv-map__input--toggle") {
                caption ()

                input (
                    Attrs.className "rv-map__input-field",
                    Attrs.attribute ("type", "checkbox"),
                    Props.Input.``checked`` (fun (row: unit -> ControlRow) ->
                        match row().Shown.Value with
                        | Shown.Toggle value -> value
                        | _ -> false),
                    Events.onChange (fun (row: unit -> ControlRow) event -> (row().Current()).Write(Shown.Toggle (inputOf event).``checked``))
                )
            }
        )

    /// <summary>One control's widget, chosen by its kind.</summary>
    let row: Component<unit -> ControlItem> =
        Component.template
            (fun _ (current: unit -> ControlItem) ->
                {
                    Current = current
                    Shown = createMemo (fun _ -> (current ()).Value())
                })
            (matchWith (fun (row: ControlRow) -> row) (fun row ->
                match (row.Current ()).Kind with
                | WidgetKind.Press -> arm pressArm row
                | WidgetKind.Range _ -> arm rangeArm row
                | WidgetKind.NumberField -> arm numberArm row
                | WidgetKind.TextField -> arm textArm row
                | WidgetKind.CheckBox -> arm checkArm row))

    /// <summary>The bar's controls, keyed by control.</summary>
    let rows (items: 'inst -> ControlItem array) : TemplateView<'inst> =
        each (fun (item: ControlItem) -> item.Key) items row

/// <summary>How a map draws a node: its marks, text and state classes.</summary>
type internal NodeLook =
    {
        Id: int
        X: float
        Y: float
        Classes: string
        /// <summary>The radius of the flight ring.</summary>
        Ring: string
        /// <summary>The path data of the node's mark.</summary>
        Body: string
        Outline: string * string * string * string
        /// <summary>The name's x, y and text anchor.</summary>
        Name: string * string * string
        /// <summary>The value's x, y and text anchor.</summary>
        ValueAt: string * string * string
        Caption: string
        Value: string
        Timing: string
        Label: string
    }

/// <summary>What a map's rows report to their map instance.</summary>
[<NoEquality; NoComparison>]
type internal MapActions =
    {
        Inspect: int -> unit
        Leave: unit -> unit
        Explain: int -> unit
        /// <summary>Registers a placed node's shape, value text and cue group; the result unregisters them.</summary>
        Node: int -> obj -> obj -> obj -> (unit -> unit)
        /// <summary>Moves a node's group to its position, at once on its first placement.</summary>
        Move: int -> obj -> float * float -> bool -> unit
        /// <summary>Registers a placed edge's path; the result unregisters it.</summary>
        Edge: string -> obj -> (unit -> unit)
        /// <summary>Starts a dot's travel along its edge; the result cancels it.</summary>
        Travel: int -> string -> bool -> obj -> (unit -> unit)
    }

[<NoEquality; NoComparison>]
type internal NodeItem = { Look: NodeLook; Actions: MapActions }

[<NoEquality; NoComparison>]
type internal EdgeItem =
    {
        Key: string
        Path: string
        Actions: MapActions
    }

/// <summary>A mark travelling an edge.</summary>
[<NoEquality; NoComparison>]
type internal DotItem =
    {
        Id: int
        Edge: string
        Bright: bool
        Actions: MapActions
    }

type internal LogLine =
    { Id: int; Text: string; Class: string }

type internal Tick = { Index: int; Left: string }

[<RequireQualifiedAccess>]
module internal MapLook =
    let hint = "Hover a node for its state; click it for why it last ran."

    let kindName (kind: TraceNodeKind) =
        match kind with
        | TraceNodeKind.Signal -> "signal"
        | TraceNodeKind.Memo -> "memo"
        | TraceNodeKind.Effect -> "effect"
        | TraceNodeKind.AsyncMemo -> "async memo"
        | TraceNodeKind.AsyncSource -> "async source"
        | TraceNodeKind.Boundary -> "boundary"
        | TraceNodeKind.Projection -> "projection"
        | TraceNodeKind.Timed -> "timed"
        | TraceNodeKind.Binding -> "binding"
        | TraceNodeKind.Stream -> "stream"
        | _ -> "node"

    let shapeOf (kind: TraceNodeKind) =
        match kind with
        | TraceNodeKind.Signal -> "signal"
        | TraceNodeKind.Effect
        | TraceNodeKind.Binding -> "effect"
        | TraceNodeKind.Timed -> "timed"
        | TraceNodeKind.AsyncMemo
        | TraceNodeKind.AsyncSource -> "async"
        | TraceNodeKind.Stream -> "stream"
        | _ -> "memo"

    let statusName (status: TraceNodeStatus) =
        match status with
        | TraceNodeStatus.Fresh -> "fresh"
        | TraceNodeStatus.Running -> "running"
        | TraceNodeStatus.Disposed -> "disposed"
        | TraceNodeStatus.Ended RunStatus.Ok -> "ok"
        | TraceNodeStatus.Ended RunStatus.Pending -> "pending"
        | TraceNodeStatus.Ended RunStatus.Error -> "error"
        | TraceNodeStatus.Ended _ -> "abandoned"

    let badgeTo (length: int) (text: string) =
        if text.Length > length then
            text.Substring (0, length - 1) + "…"
        else
            text

    /// <summary>A node's path, kind, state, value, runs, stream, wait, error and timing, as its hover label reads.</summary>
    let describe (scene: Scene) (n: TraceSnapshotNode) =
        let parts =
            [
                TraceModel.pathOf scene.Snapshot n.Id
                kindName n.Kind
                match n.Status with
                | _ when MapModel.pending scene n.Id -> "pending"
                | _ when scene.Errors.ContainsKey n.Id -> "failed"
                | _ when
                    n.Kind = TraceNodeKind.Signal
                    || n.Kind = TraceNodeKind.AsyncSource
                    ->
                    "source"
                | TraceNodeStatus.Ended RunStatus.Pending -> "settled"
                | status -> statusName status
                match n.Value with
                | Some v -> "= " + v
                | None -> ()
                $"runs {n.Runs}"
                match scene.Generations.TryFind n.Id with
                | Some generation ->
                    $"stream {generation}"
                    if scene.OpenStreams.Contains n.Id then "open" else "closed"
                | None -> ()
                match scene.Waiting.TryFind n.Id with
                | Some s -> "waiting on " + TraceModel.pathOf scene.Snapshot s
                | None -> ()
                match scene.Errors.TryFind n.Id with
                | Some e -> "error: " + e
                | None -> ()
                match scene.Timing.TryFind n.Id with
                | Some state ->
                    state.Mode

                    "captured: "
                    + (scene.Captured.TryFind n.Id
                       |> Option.defaultValue "uninitialized")

                    if state.WindowOpen then
                        $"window open ({state.RemainingMilliseconds:g} ms at recorded event)"
                    else
                        "window closed"
                | None -> ()
            ]

        String.Join (" · ", parts)

    /// <summary>The cause chain of a node's last run within <c>events</c>, or why it has none.</summary>
    let explain (scene: Scene) (events: TraceEvent array) (id: int) =
        try
            TraceModel.renderWhy scene.Snapshot (TraceModel.why events null id 0)
        with _ ->
            match
                scene.Snapshot.Nodes.TryFind id
                |> Option.map _.Kind
            with
            | Some TraceNodeKind.Signal
            | Some TraceNodeKind.AsyncSource ->
                MapModel.name scene id
                + " is a source: it changes when written or settled, and never runs."
            | _ -> MapModel.name scene id + " has not run."

    let private rounded (x: float) (y: float) (width: float) (height: float) (r: float) =
        $"M{x + r} {y} H{x + width - r} A{r} {r} 0 0 1 {x + width} {y + r} V{y + height - r} A{r} {r} 0 0 1 {x + width - r} {y + height} H{x + r} A{r} {r} 0 0 1 {x} {y + height - r} V{y + r} A{r} {r} 0 0 1 {x + r} {y} Z"

    let private disc (r: float) =
        $"M{-r} 0 A{r} {r} 0 1 0 {r} 0 A{r} {r} 0 1 0 {-r} 0 Z"

    /// <summary>The look of a drawn node in <c>scene</c>.</summary>
    let node (scene: Scene) (drawn: SceneView.Node) : NodeLook =
        let n = scene.Snapshot.Nodes[drawn.Id]
        let shape = shapeOf n.Kind

        let role =
            match drawn.Place with
            | Row _ -> " rv-map-node--row"
            | Member _ -> " rv-map-node--member"
            | _ -> ""

        let small = role <> ""
        let timing = scene.Timing.TryFind drawn.Id

        let states =
            [
                if timing |> Option.exists _.WindowOpen then
                    "is-timing"
                if n.Status = TraceNodeStatus.Running then
                    "is-running"
                if MapModel.pending scene drawn.Id then
                    "is-pending"
                if
                    n.Status = TraceNodeStatus.Fresh
                    && n.Kind <> TraceNodeKind.Signal
                    && n.Kind <> TraceNodeKind.AsyncSource
                then
                    "is-fresh"
                if scene.Flights.ContainsKey drawn.Id then
                    "is-flight"
                if scene.OpenStreams.Contains drawn.Id then
                    "is-open"
                if scene.Waiting.ContainsKey drawn.Id then
                    "is-waiting"
                if scene.Errors.ContainsKey drawn.Id then
                    "is-failed"
            ]

        let body =
            match drawn.Place, shape with
            | Row _, _ -> rounded -38.0 -12.0 76.0 24.0 12.0
            | _, "signal" -> disc (if small then 11.0 else 16.0)
            | _, "async" -> disc (if small then 12.0 else 17.0)
            | _, "effect" when small -> "M0 -13 L13 0 L0 13 L-13 0 Z"
            | _, "effect" -> "M0 -19 L19 0 L0 19 L-19 0 Z"
            | _ when small -> rounded -14.0 -10.0 28.0 20.0 6.0
            | _ -> rounded -23.0 -15.0 46.0 30.0 9.0

        let badge =
            match drawn.Place with
            | Row _ -> badgeTo 6
            | _ -> badgeTo 16

        {
            Id = drawn.Id
            X = drawn.X
            Y = drawn.Y
            Classes =
                String.concat
                    " "
                    ("rv-map-node rv-map-node--" + shape + role
                     :: states)
            Ring = if small then "18" else "25"
            Body = body
            Outline =
                if small then
                    "-17", "-13", "34", "26"
                else
                    "-26", "-18", "52", "36"
            Name =
                match drawn.Place with
                | Row _ -> "-30", "4", "start"
                | _ -> "0", "36", "middle"
            ValueAt =
                match drawn.Place with
                | Row _ -> "30", "4", "end"
                | _ -> "0", "-26", "middle"
            Caption = drawn.Caption
            Value =
                n.Value
                |> Option.map badge
                |> Option.defaultValue ""
            Timing =
                match timing with
                | Some state when state.WindowOpen ->
                    "◷ captured "
                    + (scene.Captured.TryFind drawn.Id
                       |> Option.map badge
                       |> Option.defaultValue "")
                | Some state -> state.Mode
                | None -> ""
            Label = describe scene n
        }

    let speedText (value: float) =
        value.ToString (Globalization.CultureInfo.InvariantCulture)

/// <summary>The settings of one map definition.</summary>
[<NoEquality; NoComparison>]
type internal MapSettings =
    {
        Speed: float
        Source: MapSource
        Policy: FlightPolicy
        Bindings: (string * int * int) array
        /// <summary>Whether the map shows its timeline; a replay always does.</summary>
        Timeline: bool
        Grouping: Grouping
    }

/// <summary>
/// A map instance: the displayed state as view signals, and the scenario run, playback and animation behind them.
/// </summary>
/// <remarks>
/// Constructed in the instance's setup, which allocates signals only. <c>Mount</c> runs the scenario once the root is
/// placed, in the browser; its cleanup disposes the scenario graph and cancels every timer, frame and animation.
/// </remarks>
type internal MapMachine(settings: MapSettings) as this =
    let start =
        { MapModel.start with
            Grouping = settings.Grouping
        }

    let scene = createSignal start
    let cursor = createSignal -1
    let count = createSignal 0
    let setup = createSignal 0
    let playing = createSignal (not settings.Timeline)
    let speed = createSignal settings.Speed
    let caption = createSignal ""
    let log = createSignal ([||]: LogLine array)
    let why = createSignal ""
    let inspect = createSignal MapLook.hint
    let error = createSignal ""
    let controls = createSignal ([||]: ControlItem array)

    let values =
        createSignal (Map.empty: Microsoft.FSharp.Collections.Map<int, InputValue>)

    let dots = createSignal ([||]: DotItem array)
    let version = createSignal 0

    let history = ResizeArray<Frame>()
    let captions = ResizeArray<int * string option>()
    let refreshers = ResizeArray<unit -> unit>()
    let lit = Dictionary<int, obj list>()
    let shapes = Dictionary<int, obj * obj * obj>()
    let movers = Dictionary<int, Animatable>()
    let paths = Dictionary<string, obj>()
    let timers = HashSet<float>()
    let flashes = ResizeArray<JSAnimation>()
    let mutable root: obj = null
    let mutable reduced = false
    let mutable scheduled = false
    let mutable timer = 0.0
    let mutable frame = 0.0
    let mutable tail = start
    let mutable read = 0
    let mutable graph: Graph option = None
    let mutable run = 0
    let mutable mounted = false
    let mutable nextLog = 0
    let mutable nextDot = 0
    let mutable nextControl = 0

    // The view's graph, active in browser callbacks that write the display signals.
    let view = Graph.Current

    let inView (body: unit -> unit) =
        use _ = view.Activate ()
        body ()

    let duration milliseconds =
        Replay.duration speed.Peek milliseconds

    let later (milliseconds: int) (body: unit -> unit) =
        let mutable handle = 0.0

        handle <-
            Browser.later milliseconds (fun () ->
                timers.Remove handle |> ignore
                inView body)

        timers.Add handle |> ignore
        handle

    let cancel (handle: float) =
        if timers.Remove handle then
            Browser.cancelLater handle

    let flash (element: obj) (name: string) (milliseconds: int) =
        Browser.removeClass element name
        Browser.reflow element
        Browser.addClass element name

        later milliseconds (fun () -> Browser.removeClass element name)
        |> ignore

    let fail (text: string) =
        batch (fun () ->
            error.Value <- text
            playing.Value <- false)

    let unlight (node: int) =
        match lit.TryGetValue node with
        | true, lines ->
            lit.Remove node |> ignore

            // A line stays lit while another running node's binding holds it.
            for line in lines do
                if
                    not (
                        lit.Values
                        |> Seq.exists (List.exists (fun other -> obj.ReferenceEquals (other, line)))
                    )
                then
                    Browser.removeClass line "rv-map-line--run"
        | _ -> ()

    let unlightAll () =
        for node in List.ofSeq lit.Keys do
            unlight node

    let light (node: int) =
        let name = MapModel.name scene.Peek node

        match
            settings.Bindings
            |> Array.tryFind (fun (b, _, _) -> b = name)
        with
        | Some (_, first, last) when not (isNull root) ->
            let all = Browser.codeLines root

            let lines =
                [
                    for i in first - 1 .. last - 1 do
                        if i >= 0 && i < all.Length then
                            all[i]
                ]

            for line in lines do
                Browser.addClass line "rv-map-line--run"

            lit[node] <- lines
        | _ -> ()

    let say (text: string) (cls: string) =
        nextLog <- nextLog + 1
        why.Value <- ""

        let lines =
            Array.append
                log.Peek
                [|
                    {
                        Id = nextLog
                        Text = text
                        Class = cls
                    }
                |]

        log.Value <-
            if lines.Length > 8 then
                lines[lines.Length - 8 ..]
            else
                lines

    let note (frame: Frame) =
        match frame.Cue with
        | Cue.Quiet -> ()
        | Cue.Failed _ -> say frame.Log "is-error"
        | _ -> say frame.Log ""

    let refresh () =
        caption.Value <-
            Replay.captionAt captions cursor.Peek
            |> Option.defaultValue ""

    let travel (s: int) (t: int) (bright: bool) =
        let key = $"{s}>{t}"

        if paths.ContainsKey key then
            nextDot <- nextDot + 1

            dots.Value <-
                Array.append
                    dots.Peek
                    [|
                        {
                            Id = nextDot
                            Edge = key
                            Bright = bright
                            Actions = this.Actions
                        }
                    |]

    let play (cue: Cue) =
        let shape id =
            match shapes.TryGetValue id with
            | true, elements -> Some elements
            | _ -> None

        match cue with
        | Cue.Flash id ->
            shape id
            |> Option.iter (fun (body, _, fx) ->
                let mutable animation = Unchecked.defaultof<JSAnimation>

                animation <-
                    Anime.animate (
                        unbox<TS.Dom.Element> body,
                        Anim (
                            scale = Value.keys [| Value.op_Implicit 1.0; Value.op_Implicit 1.35; Value.op_Implicit 1.0 |],
                            duration = duration 420,
                            ease = Ease.outQuad,
                            onComplete = fun _ -> flashes.Remove animation |> ignore
                        )
                    )

                flashes.Add animation

                flash fx "is-flash" (duration 700))
        | Cue.Pulse (s, t) -> travel s t false
        | Cue.Surge (s, targets) ->
            for t in targets do
                travel s t true

            shape s
            |> Option.iter (fun (_, value, _) -> flash value "is-swap" (duration 600))
        | Cue.Drop id ->
            shape id
            |> Option.iter (fun (_, _, fx) -> flash fx "is-dropped" (duration 700))
        | Cue.Settled id ->
            shape id
            |> Option.iter (fun (_, _, fx) -> flash fx "is-settled" (duration 800))
        | Cue.Failed id ->
            shape id
            |> Option.iter (fun (_, _, fx) -> flash fx "is-failing" (duration 800))
        | _ -> ()

    let show (frame: Frame) =
        batch (fun () ->
            scene.Value <- frame.After

            match frame.Cue with
            | Cue.Ring id -> light id
            | Cue.Rest id -> unlight id
            | _ -> ()

            note frame)

        if not reduced then
            play frame.Cue

    let rec tick (generation: int) () =
        scheduled <- false

        try
            if playing.Peek && generation = run && mounted then
                let mutable go = true

                while go && cursor.Peek + 1 < history.Count do
                    cursor.Value <- cursor.Peek + 1
                    let frame = history[cursor.Peek]
                    show frame

                    if frame.Cue <> Cue.Quiet && not reduced then
                        go <- false

                if
                    cursor.Peek + 1 >= history.Count
                    && settings.Timeline
                then
                    playing.Value <- false

                refresh ()

                if cursor.Peek + 1 < history.Count then
                    schedule ()
        with ex ->
            fail ("The map stopped: " + ex.Message)

    and schedule () =
        if not scheduled && mounted then
            scheduled <- true
            let backlog = history.Count - cursor.Peek - 1

            let gap =
                if reduced then
                    0
                else
                    duration (max 40 (180 - 10 * backlog))

            timer <- later gap (tick run)

    let changed () =
        batch (fun () ->
            count.Value <- history.Count
            version.Value <- version.Peek + 1)

    let append (frames: Frame array) =
        if frames.Length > 0 then
            history.AddRange frames
            tail <- (Array.last frames).After
            changed ()
            refresh ()

            if playing.Peek then
                schedule ()

    let jump (index: int) =
        playing.Value <- false
        unlightAll ()

        batch (fun () ->
            cursor.Value <- MapModel.clampCursor setup.Peek history.Count index
            scene.Value <- MapModel.stateAt start (history.ToArray ()) cursor.Peek)

        refresh ()

    let stopAll () =
        for handle in List.ofSeq timers do
            cancel handle

        for animation in List.ofSeq flashes do
            animation.cancel () |> ignore

        flashes.Clear ()

    let clear () =
        stopAll ()
        scheduled <- false
        history.Clear ()
        captions.Clear ()
        refreshers.Clear ()
        tail <- start
        read <- 0
        unlightAll ()

        batch (fun () ->
            caption.Value <- ""
            cursor.Value <- -1
            setup.Value <- 0
            log.Value <- [||]
            why.Value <- ""
            error.Value <- ""
            dots.Value <- [||]
            controls.Value <- [||]
            values.Value <- Map.empty
            scene.Value <- start
            changed ())

    // Takes the frames recorded so far as setup: drawn at once, logged, and left off the timeline.
    let baseline (g: Graph) =
        let events = Trace.events g
        read <- events.Length
        let frames = MapModel.frames start events
        history.AddRange frames
        tail <- MapModel.stateAt start frames (frames.Length - 1)

        batch (fun () ->
            setup.Value <- frames.Length
            cursor.Value <- frames.Length - 1
            scene.Value <- tail

            for frame in frames do
                note frame

            changed ())

        refresh ()

    let drain (g: Graph) =
        let events = Trace.events g

        if events.Length > read then
            let fresh = events[read..]
            read <- events.Length
            append (MapModel.frames tail fresh)

    let current (g: Graph) (generation: int) =
        mounted
        && generation = run
        && graph
           |> Option.exists (fun live -> obj.ReferenceEquals (live, g))

    // A live map's item for a control; `attempt` runs a write with the scenario graph active.
    let item (attempt: string -> (unit -> unit) -> unit) (control: Control) : ControlItem =
        nextControl <- nextControl + 1
        let key = nextControl

        let display (value: InputValue) =
            values.Value <- Map.add key value values.Peek

        let write body =
            playing.Value <- true
            attempt control.Label body

        let kind, initial, set =
            match control.Widget with
            | Widget.Button press -> WidgetKind.Press, Shown.Toggle false, (fun _ -> write press)
            | Widget.Slider (min, max, first, set) ->
                WidgetKind.Range (min, max),
                Shown.Integer first,
                (function
                | Shown.Integer value -> write (fun () -> set value)
                | _ -> ())
            | Widget.Number (first, set) ->
                WidgetKind.NumberField,
                Shown.Number first,
                (function
                | Shown.Number value -> write (fun () -> set value)
                | _ -> ())
            | Widget.Text (first, set) ->
                WidgetKind.TextField,
                Shown.Text first,
                (function
                | Shown.Text value -> write (fun () -> set value)
                | _ -> ())
            | Widget.Toggle (first, set) ->
                WidgetKind.CheckBox,
                Shown.Toggle first,
                (function
                | Shown.Toggle value -> write (fun () -> set value)
                | _ -> ())

        display initial

        control.Binding
        |> Option.iter (fun binding -> refreshers.Add (Controls.follow binding display))

        {
            Key = key
            Label = control.Label
            Kind = kind
            Value =
                fun () ->
                    values.Value
                    |> Map.tryFind key
                    |> Option.defaultValue initial
            Write =
                fun value ->
                    display value
                    set value
            Press = fun () -> set initial
        }

    let restart () =
        clear ()
        run <- run + 1
        let generation = run

        graph
        |> Option.iter (fun g -> (g :> IDisposable).Dispose())

        let g =
            new Graph (
                { GraphOptions.Default with
                    FlightPolicy = settings.Policy
                }
            )

        graph <- Some g

        let attempt (label: string) (body: unit -> unit) =
            try
                use _ = g.Activate ()
                body ()
            with ex ->
                say $"{label} threw: {ex.Message}" "is-error"

        try
            match settings.Source with
            | Replayed scenario ->
                let steps =
                    scenario g
                    |> List.collect (fun c -> c.Steps |> List.map (fun s -> c.Label, s))

                baseline g
                playing.Value <- false

                // One step per task, in order; a step's log line follows every event recorded before it.
                let rec stepFrom (rest: (string * Step) list) =
                    match rest with
                    | (label, step) :: rest when current g generation ->
                        drain g
                        captions.Add (history.Count, step.Caption)
                        append [| MapModel.said tail (step.Log |> Option.defaultValue label) |]

                        try
                            do
                                use _ = g.Activate ()
                                step.Run ()

                            later 0 (fun () ->
                                if current g generation then
                                    try
                                        drain g

                                        do
                                            use _ = g.Activate ()
                                            untrack (fun () -> Controls.check step)

                                        stepFrom rest
                                    with ex ->
                                        fail $"{label}: {ex.Message}")
                            |> ignore
                        with ex ->
                            fail $"{label}: {ex.Message}"
                    | _ -> ()

                stepFrom steps
            | Live scenario ->
                let made = scenario g
                baseline g
                playing.Value <- true

                controls.Value <- made |> List.map (item attempt) |> Array.ofList
        with ex ->
            fail ("The example threw: " + ex.Message)

    let rec poll () =
        if mounted then
            try
                graph |> Option.iter drain

                for refresh in refreshers do
                    refresh ()

                frame <- Browser.frame (fun () -> inView poll)
            with ex ->
                fail ("The map stopped: " + ex.Message)

    let move (id: int) (group: obj) (x: float, y: float) (first: bool) =
        if first || reduced then
            Utils.set (unbox<TS.Dom.Element> group, Anim (x = x, y = y))
            |> ignore
        else
            let mover =
                match movers.TryGetValue id with
                | true, mover -> mover
                | _ ->
                    let created =
                        Anime.animatable (unbox<TS.Dom.Element> group, AnimatableParams (x = 360, y = 360, ease = Ease.outQuart))

                    movers[id] <- created
                    created

            mover.set ("x", x) |> ignore
            mover.set ("y", y) |> ignore

    member val internal Actions: MapActions =
        {
            Inspect =
                fun id ->
                    match scene.Peek.Snapshot.Nodes.TryFind id with
                    | Some n -> inspect.Value <- MapLook.describe scene.Peek n
                    | None -> ()
            Leave = fun () -> inspect.Value <- MapLook.hint
            Explain =
                fun id ->
                    let events =
                        [|
                            for i in 0 .. cursor.Peek do
                                history[i].Event
                        |]

                    why.Value <- MapLook.explain scene.Peek events id
            Node =
                fun id body value fx ->
                    shapes[id] <- (body, value, fx)

                    fun () ->
                        match shapes.TryGetValue id with
                        | true, (b, _, _) when obj.ReferenceEquals (b, body) ->
                            shapes.Remove id |> ignore

                            match movers.TryGetValue id with
                            | true, mover ->
                                mover.revert () |> ignore
                                movers.Remove id |> ignore
                            | _ -> ()

                            unlight id
                        | _ -> ()
            Move = move
            Edge =
                fun key element ->
                    paths[key] <- element

                    fun () ->
                        match paths.TryGetValue key with
                        | true, e when obj.ReferenceEquals (e, element) -> paths.Remove key |> ignore
                        | _ -> ()
            Travel =
                fun id edge bright element ->
                    let remove () =
                        dots.Value <- dots.Peek |> Array.filter (fun d -> d.Id <> id)

                    match paths.TryGetValue edge with
                    | true, track when not reduced && mounted ->
                        let along = Svg.motionPath (unbox<TS.Dom.Element> track)

                        let animation =
                            Anime.animate (
                                unbox<TS.Dom.Element> element,
                                Anim (
                                    translateX = along.translateX,
                                    translateY = along.translateY,
                                    duration = duration (if bright then 460 else 380),
                                    ease = Ease.inOutSine,
                                    onComplete = fun _ -> inView remove
                                )
                            )

                        fun () -> animation.cancel () |> ignore
                    | _ ->
                        later 0 remove |> ignore
                        ignore
        }

    member val Projection = createMemo (fun _ -> SceneView.project scene.Value)
    member _.Cursor = cursor
    member _.Count = count
    member _.Setup = setup
    member _.Playing = playing
    member _.Speed = speed
    member _.Caption = caption
    member _.Log = log
    member _.Why = why
    member _.Inspect = inspect
    member _.Error = error
    member _.Controls = controls
    member _.Dots = dots

    /// <summary>The ticks of the recorded events after setup, positioned along the scrub bar.</summary>
    member val Ticks =
        createMemo (fun _ ->
            version.Value |> ignore
            let first = setup.Value
            let total = count.Value

            [|
                for i in 0 .. total - 1 do
                    match MapModel.tickAt first total i with
                    | Some at when history[i].Cue <> Cue.Quiet ->
                        {
                            Index = i
                            Left = $"left: {at * 100.0}%%"
                        }
                    | _ -> ()
            |])

    member this.Nodes =
        let drawn = scene.Value

        this.Projection.Value.Nodes
        |> List.map (fun n ->
            {
                Look = MapLook.node drawn n
                Actions = this.Actions
            })
        |> Array.ofList

    member this.Edges =
        this.Projection.Value.Edges
        |> List.map (fun e ->
            {
                Key = $"{e.Source}>{e.Target}"
                Path = e.Path
                Actions = this.Actions
            })
        |> Array.ofList

    /// <summary>Runs the scenario once the map's root is placed; the result ends the run.</summary>
    member _.Mount(element: obj) : unit -> unit =
        root <- element
        mounted <- true
        reduced <- Browser.reducedMotion ()

        try
            restart ()
            poll ()
        with ex ->
            fail ("The map stopped: " + ex.Message)

        fun () ->
            mounted <- false
            run <- run + 1

            stopAll ()
            Browser.cancelFrame frame
            unlightAll ()

            graph
            |> Option.iter (fun g -> (g :> IDisposable).Dispose())

            graph <- None

    /// <summary>Rebuilds the scenario on a fresh graph.</summary>
    member _.Reset() =
        if mounted then
            restart ()

    member _.Rewind() = jump -1

    member _.Scrub(index: int) =
        jump index

    member _.Toggle() =
        if playing.Peek then
            playing.Value <- false
        else
            if cursor.Peek + 1 >= history.Count then
                jump -1

            playing.Value <- true
            schedule ()

        refresh ()

    member _.Step() =
        playing.Value <- false
        let mutable go = true

        while go && cursor.Peek + 1 < history.Count do
            cursor.Value <- cursor.Peek + 1
            show history[cursor.Peek]

            if history[cursor.Peek].Cue <> Cue.Quiet then
                go <- false

        refresh ()

    member _.ChangeSpeed(value: float) =
        if value >= 0.25 && value <= 4.0 then
            speed.Value <- value
            cancel timer
            scheduled <- false

            if playing.Peek && cursor.Peek + 1 < history.Count then
                schedule ()

/// <summary>The map's rows and template.</summary>
module internal MapView =
    let private targetOf (event: TS.Dom.Event) : obj =
        match event.target with
        | Some target -> box target
        | None -> invalidOp "An input event has no target."

    let private inputValue (event: TS.Dom.Event) =
        (unbox<TS.Dom.HTMLInputElement>(targetOf event)).value

    let private nodeRow: TemplateView<unit -> NodeItem> =
        g (
            Attrs.className (fun (n: unit -> NodeItem) -> (n ()).Look.Classes),
            Attrs.attribute ("tabindex", "0"),
            Attrs.attribute ("aria-label", (fun (n: unit -> NodeItem) -> Some (n ()).Look.Label)),
            Events.onMouseenter (fun (n: unit -> NodeItem) _ ->
                let item = n ()
                item.Actions.Inspect item.Look.Id),
            Events.onMouseleave (fun (n: unit -> NodeItem) _ -> (n ()).Actions.Leave()),
            Events.onClick (fun (n: unit -> NodeItem) _ ->
                let item = n ()
                item.Actions.Explain item.Look.Id),
            Events.onKeydown (fun (n: unit -> NodeItem) event ->
                if event.key = "Enter" then
                    let item = n ()
                    item.Actions.Explain item.Look.Id),
            Hooks.mounted (fun (n: unit -> NodeItem) group ->
                let first = ref true

                createEffectOn
                    (fun () ->
                        let look = (n ()).Look
                        look.X, look.Y)
                    (fun at ->
                        let item = n ()
                        item.Actions.Move item.Look.Id (box group) at first.Value
                        first.Value <- false)

                ignore)
        ) {
            g (
                Attrs.className "rv-map-node__cue",
                Hooks.mounted (fun (n: unit -> NodeItem) fx ->
                    let item = n ()

                    item.Actions.Node
                        item.Look.Id
                        (Browser.query (box fx) ".rv-map-node__shape")
                        (Browser.query (box fx) ".rv-map-node__value")
                        (box fx))
            ) {
                circle (Attrs.className "rv-map-node__flight", Attrs.attribute ("r", (fun (n: unit -> NodeItem) -> Some (n ()).Look.Ring))) { () }

                rect (
                    Attrs.className "rv-map-node__open",
                    Attrs.attribute (
                        "x",
                        (fun (n: unit -> NodeItem) ->
                            let x, _, _, _ = (n ()).Look.Outline
                            Some x)
                    ),
                    Attrs.attribute (
                        "y",
                        (fun (n: unit -> NodeItem) ->
                            let _, y, _, _ = (n ()).Look.Outline
                            Some y)
                    ),
                    Attrs.attribute (
                        "width",
                        (fun (n: unit -> NodeItem) ->
                            let _, _, width, _ = (n ()).Look.Outline
                            Some width)
                    ),
                    Attrs.attribute (
                        "height",
                        (fun (n: unit -> NodeItem) ->
                            let _, _, _, height = (n ()).Look.Outline
                            Some height)
                    ),
                    Attrs.attribute ("rx", "12")
                ) {
                    ()
                }

                path (Attrs.className "rv-map-node__shape", Attrs.attribute ("d", (fun (n: unit -> NodeItem) -> Some (n ()).Look.Body))) { () }

                text' (
                    Attrs.className "rv-map-node__name",
                    Attrs.attribute (
                        "x",
                        (fun (n: unit -> NodeItem) ->
                            let x, _, _ = (n ()).Look.Name
                            Some x)
                    ),
                    Attrs.attribute (
                        "y",
                        (fun (n: unit -> NodeItem) ->
                            let _, y, _ = (n ()).Look.Name
                            Some y)
                    ),
                    Attrs.attribute (
                        "text-anchor",
                        (fun (n: unit -> NodeItem) ->
                            let _, _, anchor = (n ()).Look.Name
                            Some anchor)
                    )
                ) {
                    fun (n: unit -> NodeItem) -> (n ()).Look.Caption
                }

                text' (
                    Attrs.className "rv-map-node__value",
                    Attrs.attribute (
                        "x",
                        (fun (n: unit -> NodeItem) ->
                            let x, _, _ = (n ()).Look.ValueAt
                            Some x)
                    ),
                    Attrs.attribute (
                        "y",
                        (fun (n: unit -> NodeItem) ->
                            let _, y, _ = (n ()).Look.ValueAt
                            Some y)
                    ),
                    Attrs.attribute (
                        "text-anchor",
                        (fun (n: unit -> NodeItem) ->
                            let _, _, anchor = (n ()).Look.ValueAt
                            Some anchor)
                    )
                ) {
                    fun (n: unit -> NodeItem) -> (n ()).Look.Value
                }

                text' (Attrs.className "rv-map-node__timing", Attrs.attribute ("y", "51"), Attrs.attribute ("text-anchor", "middle")) {
                    fun (n: unit -> NodeItem) -> (n ()).Look.Timing
                }
            }
        }

    let private edgeRow: TemplateView<unit -> EdgeItem> =
        path (
            Attrs.className "rv-map-edge",
            Attrs.attribute ("d", (fun (e: unit -> EdgeItem) -> Some (e ()).Path)),
            Hooks.mounted (fun (e: unit -> EdgeItem) element ->
                let item = e ()
                item.Actions.Edge item.Key (box element))
        ) {
            ()
        }

    let private dotRow: TemplateView<unit -> DotItem> =
        circle (
            Attrs.className (fun (d: unit -> DotItem) ->
                if (d ()).Bright then
                    "rv-map-dot rv-map-dot--bright"
                else
                    "rv-map-dot"),
            Attrs.attribute ("r", (fun (d: unit -> DotItem) -> Some (if (d ()).Bright then "4.5" else "3.5"))),
            Hooks.mounted (fun (d: unit -> DotItem) element ->
                let item = d ()
                item.Actions.Travel item.Id item.Edge item.Bright (box element))
        ) {
            ()
        }

    let private boxRow: TemplateView<unit -> SceneView.Box> =
        rect (
            Attrs.className "rv-map-group",
            Attrs.attribute ("x", (fun (b: unit -> SceneView.Box) -> Some (string (b ()).X))),
            Attrs.attribute ("y", (fun (b: unit -> SceneView.Box) -> Some (string (b ()).Y))),
            Attrs.attribute ("width", (fun (b: unit -> SceneView.Box) -> Some (string (b ()).Width))),
            Attrs.attribute ("height", (fun (b: unit -> SceneView.Box) -> Some (string (b ()).Height))),
            Attrs.attribute ("rx", "12")
        ) {
            ()
        }

    let private columnRow: TemplateView<unit -> SceneView.Column> =
        text' (
            Attrs.className "rv-map-group__column",
            Attrs.attribute ("x", (fun (c: unit -> SceneView.Column) -> Some (string (c ()).X))),
            Attrs.attribute ("y", (fun (c: unit -> SceneView.Column) -> Some (string (c ()).Y))),
            Attrs.attribute ("text-anchor", "middle")
        ) {
            fun (c: unit -> SceneView.Column) -> (c ()).Text
        }

    let private logRow: TemplateView<unit -> LogLine> =
        li (Attrs.className (fun (l: unit -> LogLine) -> (l ()).Class)) { fun (l: unit -> LogLine) -> (l ()).Text }

    let private tickRow: TemplateView<unit -> Tick> =
        span (Attrs.className "rv-map__tick", Attrs.attribute ("style", (fun (t: unit -> Tick) -> Some (t ()).Left))) { () }

    let private resetButton (press: MapMachine -> unit) =
        button (
            Attrs.attribute ("type", "button"),
            Attrs.className "rv-map__button rv-map__button--reset",
            Events.onClick (fun (m: MapMachine) _ -> press m)
        ) {
            "Reset"
        }

    let private speeds (initial: float) =
        [ 0.25; 0.5; 1.0; 1.5; 2.0; 4.0; initial ]
        |> List.distinct
        |> List.sort

    let private timelineRow (settings: MapSettings) =
        div (Attrs.className "rv-map__timeline") {
            button (Attrs.attribute ("type", "button"), Attrs.className "rv-map__button", Events.onClick (fun (m: MapMachine) _ -> m.Toggle ())) {
                fun (m: MapMachine) -> if m.Playing.Value then "Pause" else "Play"
            }

            button (Attrs.attribute ("type", "button"), Attrs.className "rv-map__button", Events.onClick (fun (m: MapMachine) _ -> m.Step ())) {
                "Step"
            }

            label (Attrs.className "rv-map__speed") {
                span (Attrs.className "rv-map__speed-label") { "Speed" }

                select (
                    Attrs.className "rv-map__speed-select",
                    Events.onChange (fun (m: MapMachine) event ->
                        let chosen = (unbox<TS.Dom.HTMLSelectElement>(targetOf event)).value

                        match Double.TryParse (chosen, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
                        | true, value -> m.ChangeSpeed value
                        | _ -> ())
                ) {
                    for value in speeds settings.Speed do
                        option' (
                            Attrs.attribute ("value", MapLook.speedText value),
                            Attrs.attribute ("selected", (fun (_: MapMachine) -> if value = settings.Speed then Some "" else None))
                        ) {
                            MapLook.speedText value + "×"
                        }
                }
            }

            div (Attrs.className "rv-map__track") {
                div (Attrs.className "rv-map__ticks") { forEach (fun (t: Tick) -> t.Index) (fun (m: MapMachine) -> m.Ticks.Value) tickRow }

                input (
                    Attrs.className "rv-map__scrub",
                    Attrs.attribute ("type", "range"),
                    Attrs.attribute ("step", "1"),
                    Attrs.attribute ("aria-label", "Event"),
                    Attrs.attribute ("min", (fun (m: MapMachine) -> Some (string m.Setup.Value))),
                    Attrs.attribute ("max", (fun (m: MapMachine) -> Some (string m.Count.Value))),
                    Props.Input.value (fun (m: MapMachine) -> string (m.Cursor.Value + 1)),
                    Events.onInput (fun (m: MapMachine) event ->
                        match Int32.TryParse (inputValue event) with
                        | true, index -> m.Scrub (index - 1)
                        | _ -> ())
                )
            }
        }

    /// <summary>The template of a map with <c>settings</c>.</summary>
    let template (settings: MapSettings) : TemplateView<MapMachine> =
        let replayed =
            match settings.Source with
            | Replayed _ -> true
            | Live _ -> false

        div (
            Attrs.className (fun (m: MapMachine) ->
                if m.Error.Value = "" then
                    "rv-map"
                else
                    "rv-map rv-map--failed"),
            Attrs.attribute (
                "style",
                (fun (m: MapMachine) ->
                    Some (
                        "--rv-map-speed: "
                        + MapLook.speedText m.Speed.Value
                    ))
            ),
            Hooks.mounted (fun (m: MapMachine) root -> m.Mount (box root))
        ) {
            div (Attrs.className "rv-map__stage") {
                svg (
                    Attrs.className "rv-map__svg",
                    Attrs.attribute ("role", "img"),
                    Attrs.attribute ("aria-label", "Signal map"),
                    Attrs.attribute (
                        "viewBox",
                        (fun (m: MapMachine) ->
                            let projection = m.Projection.Value
                            Some $"0 0 {projection.Width} {projection.Height}")
                    )
                ) {
                    g (Attrs.className "rv-map__groups") {
                        forEach (fun (b: SceneView.Box) -> b.Host) (fun (m: MapMachine) -> Array.ofList m.Projection.Value.Boxes) boxRow

                        forEach
                            (fun (c: SceneView.Column) -> c.Host, c.Index)
                            (fun (m: MapMachine) -> Array.ofList m.Projection.Value.Columns)
                            columnRow
                    }

                    g (Attrs.className "rv-map__edges") { forEach (fun (e: EdgeItem) -> e.Key) (fun (m: MapMachine) -> m.Edges) edgeRow }
                    g (Attrs.className "rv-map__dots") { forEach (fun (d: DotItem) -> d.Id) (fun (m: MapMachine) -> m.Dots.Value) dotRow }
                    g (Attrs.className "rv-map__nodes") { forEach (fun (n: NodeItem) -> n.Look.Id) (fun (m: MapMachine) -> m.Nodes) nodeRow }
                }

                div (Attrs.className "rv-map__inspect") { fun (m: MapMachine) -> m.Inspect.Value }
            }

            div (Attrs.className "rv-map__bar") {
                div (Attrs.className "rv-map__controls") {
                    ControlBar.rows (fun (m: MapMachine) -> m.Controls.Value)

                    if not replayed then
                        resetButton (fun m -> m.Reset ())
                }

                if settings.Timeline then
                    timelineRow settings

                if replayed then
                    resetButton (fun m -> m.Rewind ())
            }

            p (Attrs.className "rv-map__caption", Attrs.attribute ("aria-live", "polite")) { fun (m: MapMachine) -> m.Caption.Value }

            ol (
                Attrs.className "rv-map__log",
                Attrs.attribute ("aria-live", "polite"),
                Hooks.mounted (fun (m: MapMachine) element ->
                    createEffectOn (fun () -> m.Log.Value) (fun _ -> Browser.scrollToEnd (box element))
                    ignore)
            ) {
                forEach (fun (l: LogLine) -> l.Id) (fun (m: MapMachine) -> m.Log.Value) logRow
            }

            pre (Attrs.className "rv-map__why") { fun (m: MapMachine) -> m.Why.Value }
            div (Attrs.className "rv-map__error", Attrs.attribute ("role", "alert")) { fun (m: MapMachine) -> m.Error.Value }
        }

[<AutoOpen>]
module SignalMapComponent =

    /// <summary>
    /// A traced graph with controls, a log and an optional timeline; playback starts at <c>initialSpeed</c>, from 0.25 to 4.
    /// Its graph runs under <c>policy</c>, and its collections draw as <c>grouping</c>.
    /// </summary>
    /// <remarks>
    /// The server renders the empty map and its controls' shell; the scenario runs once the map is placed in a browser,
    /// on a graph of its own, and each Reset or disposal disposes that graph. Placed in a <c>rv-map-card</c>, the map
    /// highlights the lines of the binding whose node runs. A binding is (label, first line, last line), with lines
    /// counted from 1 in the card's code block.
    /// </remarks>
    /// <exception cref="T:System.ArgumentException">The initial speed is outside 0.25 to 4, or is not finite.</exception>
    let SignalMapWithSpeed
        (initialSpeed: float)
        (source: MapSource)
        (policy: FlightPolicy)
        (bindings: (string * int * int)[])
        (timeline: bool)
        (grouping: Grouping)
        : Component<unit> =
        if not (initialSpeed >= 0.25 && initialSpeed <= 4.0) then
            invalidArg (nameof initialSpeed) "Playback speed must be from 0.25 to 4."

        let settings =
            {
                Speed = initialSpeed
                Source = source
                Policy = policy
                Bindings = bindings
                Timeline =
                    timeline
                    || (match source with
                        | Replayed _ -> true
                        | Live _ -> false)
                Grouping = grouping
            }

        Component.template (fun _ () -> MapMachine settings) (MapView.template settings)

    /// <summary>A signal map starting at normal playback speed; the timeline's Speed control adjusts it.</summary>
    let SignalMap
        (source: MapSource)
        (policy: FlightPolicy)
        (bindings: (string * int * int)[])
        (timeline: bool)
        (grouping: Grouping)
        : Component<unit> =
        SignalMapWithSpeed 1.0 source policy bindings timeline grouping

/// <summary>A signal map mounted in the page on a graph of its own, for code compiled outside a page declaration.</summary>
[<RequireQualifiedAccess>]
module MapMount =
    [<Emit("document.body")>]
    let private documentBody () : TS.Dom.Element = jsNative

    /// <summary>
    /// Mounts <c>map</c> at the end of the document body with <c>Ranvier.View.Dom.Mounting.mount</c>; disposing the result
    /// removes the map and disposes its graph.
    /// </summary>
    /// <remarks>Mount failures after the call are written to the console.</remarks>
    let body (map: Component<unit>) : IDisposable =
        let graph = new Graph ()

        try
            let mounted =
                Ranvier.View.Dom.Mounting.mount graph None (fun errors -> JS.console.error errors) (documentBody ()) None (fun context ->
                    Program.run (Component.render map ()) context.Backend)

            { new IDisposable with
                member _.Dispose() =
                    try
                        mounted.Dispose ()
                    finally
                        graph.Dispose ()
            }
        with _ ->
            graph.Dispose ()
            reraise ()
