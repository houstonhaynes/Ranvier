/// <summary>
/// A three-lane board run by an Elmish loop: cards enter and leave with presence, move between lanes with FLIP on the
/// board, and drop into a lane when dragged.
/// </summary>
module Kanban

open global.Ranvier
open Ranvier.Anime
open Ranvier.Elmish
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Scheduling
open Ranvier.View.Foundation.Sugar
open Ranvier.View.Anime

type Card = { Id: int; Title: string; Column: int }

type Model =
    {
        Cards: Card array
        NextId: int
        Seed: int
        /// <summary>Earlier card arrays, latest first, at most 32.</summary>
        History: Card array list
    }

type Msg =
    | Add of column: int
    | Delete of id: int
    | Move of id: int * column: int
    | Shuffle
    | Clear
    | Undo

let lanes = [| "Backlog"; "In progress"; "Shipped" |]

let private titles =
    [|
        "Sketch the board"
        "Wire the reducer"
        "Pick an easing"
        "Measure the bundle"
        "Hydrate the story"
        "Spring the puck"
        "Morph the chart"
        "Route the shell"
        "Review the seam"
        "Trim the layout"
        "Tune the stagger"
        "Ship the demo"
    |]

let private cardOf (id: int) (column: int) =
    {
        Id = id
        Title = titles[(id - 1) % titles.Length]
        Column = column
    }

let init =
    {
        Cards = [| cardOf 1 0; cardOf 2 0; cardOf 3 1; cardOf 4 1; cardOf 5 2 |]
        NextId = 6
        Seed = 7
        History = []
    }

let private commit (model: Model) (cards: Card array) =
    { model with
        Cards = cards
        History = List.truncate 32 (model.Cards :: model.History)
    }

let update (msg: Msg) (model: Model) : Model =
    match msg with
    | Add column ->
        { commit model (Array.append model.Cards [| cardOf model.NextId column |]) with
            NextId = model.NextId + 1
        }
    | Delete id when
        model.Cards
        |> Array.exists (fun card -> card.Id = id)
        ->
        commit
            model
            (model.Cards
             |> Array.filter (fun card -> card.Id <> id))
    | Move (id, column) ->
        match
            model.Cards
            |> Array.tryFind (fun card -> card.Id = id)
        with
        | Some card when
            card.Column <> column
            && column >= 0
            && column < lanes.Length
            ->
            let rest =
                model.Cards
                |> Array.filter (fun other -> other.Id <> id)

            commit model (Array.append rest [| { card with Column = column } |])
        | _ -> model
    | Shuffle when model.Cards.Length > 1 ->
        let cards = Array.copy model.Cards
        let mutable seed = model.Seed

        for index in cards.Length - 1 .. -1 .. 1 do
            seed <- Page.nextSeed seed
            let other = seed % (index + 1)
            let card = cards[index]
            cards[index] <- cards[other]
            cards[other] <- card

        let placed =
            cards
            |> Array.map (fun card ->
                seed <- Page.nextSeed seed

                { card with
                    Column = seed % lanes.Length
                })

        { commit model placed with Seed = seed }
    | Clear when model.Cards.Length > 0 -> commit model [||]
    | Undo ->
        match model.History with
        | previous :: rest ->
            { model with
                Cards = previous
                History = rest
            }
        | [] -> model
    | Delete _
    | Shuffle
    | Clear -> model

/// <summary>A card with its row in its lane, counted in board order.</summary>
type Slot = { Card: Card; Row: int }

let slots (cards: Card array) : Slot array =
    let counts = Array.zeroCreate lanes.Length

    cards
    |> Array.map (fun card ->
        let row = counts[card.Column]
        counts[card.Column] <- row + 1
        { Card = card; Row = row })

let counts (cards: Card array) : int array =
    Array.init lanes.Length (fun lane ->
        cards
        |> Array.filter (fun card -> card.Column = lane)
        |> Array.length)

/// <summary>The board's loop and the memos its view reads.</summary>
[<NoEquality; NoComparison>]
type Board =
    {
        Loop: Mvu<Model, Msg>
        Slots: Memo<Slot array>
        Counts: Memo<int array>
        Total: Memo<int>
        CanUndo: Memo<bool>
        /// <summary>The board element once placed, for the lane width a drop is measured against.</summary>
        Element: obj ref
    }

/// <summary>A board over a new loop starting at <c>start</c>, in the current graph's scope.</summary>
let createBoard (start: Model) : Board =
    let loop = Mvu.create start update

    {
        Loop = loop
        Slots = loop.Select (fun model -> slots model.Cards)
        Counts = loop.Select (fun model -> counts model.Cards)
        Total = loop.Select (fun model -> model.Cards.Length)
        CanUndo = loop.Select (fun model -> not model.History.IsEmpty)
        Element = ref null
    }

/// <summary>A card row's instance: its current slot, and the drag position and state it shares with its draggable.</summary>
[<NoEquality; NoComparison>]
type CardRow =
    {
        Current: unit -> Slot
        Position: Signal<struct (float * float)>
        Dragging: Signal<bool>
        /// <summary>The spring returning the card to its slot; a grab or the row's disposal cancels it.</summary>
        Spring: MotionSlot
        /// <summary>The card's element once placed, whose drop box starts its FLIP into another lane.</summary>
        Element: obj ref
    }

/// <summary>The lane a card dragged by <c>dx</c> pixels lands in, from lane <c>column</c> on lanes <c>width</c> wide.</summary>
let dropLane (column: int) (width: float) (dx: float) =
    if width <= 0. then
        column
    else
        max 0 (min (lanes.Length - 1) (column + int (System.Math.Round (dx / width))))

let private enterCard (_: CardRow) (element: Fable.Core.TS.Dom.Element) =
    Anime.animate (element, Anim (opacity = Value.fromTo (0, 1), scale = Value.fromTo (0.85, 1), duration = 320, ease = Ease.outBack))

let private leaveCard (_: CardRow) (element: Fable.Core.TS.Dom.Element) =
    Anime.animate (element, Anim (opacity = 0, scale = 0.8, duration = 240, ease = Ease.inQuad))

// Returns the card to its slot along a spring, through the position signal the draggable follows.
let private springHome (graph: Graph) (row: CardRow) (struct (x, y)) =
    let point = Page.point x y

    row.Spring.Play (fun () ->
        Anime.animate (
            Targets.object point,
            Anim (
                x = 0,
                y = 0,
                ease = Ease.spring (stiffness = 260., damping = 20.),
                onUpdate = fun _ -> graph.Batch (fun () -> row.Position.Value <- struct (point.x, point.y))
            )
        ))

let private cardRow (board: Board) =
    Component.template
        (fun _ (current: unit -> Slot) ->
            let graph = Graph.Current

            let row =
                {
                    Current = current
                    Position = Api.createSignal (struct (0., 0.))
                    Dragging = Api.createSignal false
                    Spring = Motion.slot ()
                    Element = ref null
                }

            Api.createEffectOn (fun () -> row.Dragging.Value) (fun dragging ->
                let struct (dx, dy) = Api.untrack (fun () -> row.Position.Value)

                if not dragging && (dx <> 0. || dy <> 0.) then
                    let slot = Api.untrack current

                    let width =
                        Page.widthOf board.Element.Value
                        / float lanes.Length

                    let lane = dropLane slot.Card.Column width dx

                    if lane = slot.Card.Column then
                        springHome graph row (struct (dx, dy))
                    else
                        Motion.flipFrom (unbox row.Element.Value)
                        row.Position.Value <- struct (0., 0.)
                        board.Loop.Dispatch (Move (slot.Card.Id, lane)))

            row)
        (li (
            Attrs.className (fun (row: CardRow) ->
                if row.Dragging.Value then
                    "card-slot dragging"
                else
                    "card-slot"),
            Attrs.data ("id", fun (row: CardRow) -> Some (string (row.Current ()).Card.Id)),
            Attrs.data ("lane", fun (row: CardRow) -> Some (string (row.Current ()).Card.Column)),
            Motion.presence enterCard leaveCard,
            Motion.drag (DraggableParams (velocityMultiplier = 0., cursor = false)) (fun (row: CardRow) ->
                {
                    Position = row.Position
                    Dragging = ValueSome row.Dragging
                    Slot = ValueSome row.Spring
                }),
            Hooks.mounted (fun (row: CardRow) element ->
                row.Element.Value <- box element
                fun () -> row.Element.Value <- null)
        ) {
            div (Attrs.className "card") {
                div (Attrs.className "card-top") {
                    span (Attrs.className "card-id") { fun (row: CardRow) -> "#" + string (row.Current ()).Card.Id }

                    button (
                        Attrs.className "icon delete",
                        Attrs.title "Delete",
                        Events.onClick (fun (row: CardRow) _ -> board.Loop.Dispatch (Delete (row.Current ()).Card.Id))
                    ) {
                        "×"
                    }
                }

                p (Attrs.className "card-title") { fun (row: CardRow) -> (row.Current ()).Card.Title }

                div (Attrs.className "card-moves") {
                    button (
                        Attrs.className "icon left",
                        Attrs.title "Move left",
                        Attrs.Button.disabled (fun (row: CardRow) -> (row.Current ()).Card.Column = 0),
                        Events.onClick (fun (row: CardRow) _ ->
                            let card = (row.Current ()).Card
                            board.Loop.Dispatch (Move (card.Id, card.Column - 1)))
                    ) {
                        "←"
                    }

                    button (
                        Attrs.className "icon right",
                        Attrs.title "Move right",
                        Attrs.Button.disabled (fun (row: CardRow) -> (row.Current ()).Card.Column = lanes.Length - 1),
                        Events.onClick (fun (row: CardRow) _ ->
                            let card = (row.Current ()).Card
                            board.Loop.Dispatch (Move (card.Id, card.Column + 1)))
                    ) {
                        "→"
                    }
                }
            }
        })

/// <summary>The board's component over <c>board</c>; build it once per board.</summary>
let view (board: Board) : Component<unit> =
    let row = cardRow board

    Component.template
        (fun _ () -> board)
        (section (Attrs.className "kanban") {
            header (Attrs.className "toolbar") {
                button (Attrs.className "primary add", Events.onClick (fun (b: Board) _ -> b.Loop.Dispatch (Add 0))) { "Add card" }
                button (Attrs.className "shuffle", Events.onClick (fun (b: Board) _ -> b.Loop.Dispatch Shuffle)) { "Shuffle" }
                button (Attrs.className "clear", Events.onClick (fun (b: Board) _ -> b.Loop.Dispatch Clear)) { "Clear" }

                button (
                    Attrs.className "undo",
                    Attrs.Button.disabled (fun (b: Board) -> not b.CanUndo.Value),
                    Events.onClick (fun (b: Board) _ -> b.Loop.Dispatch Undo)
                ) {
                    "Undo"
                }

                span (Attrs.className "meta") { fun (b: Board) -> sprintf "%d cards" b.Total.Value }
            }

            div (Attrs.className "board-frame") {
                div (Attrs.className "lanes") {
                    for lane in 0 .. lanes.Length - 1 do
                        div (Attrs.className "lane") {
                            div (Attrs.className "lane-head") {
                                h2 () { lanes[lane] }
                                span (Attrs.className "count") { fun (b: Board) -> string b.Counts.Value[lane] }

                                button (
                                    Attrs.className "icon add-to",
                                    Attrs.title ("Add to " + lanes[lane]),
                                    Events.onClick (fun (b: Board) _ -> b.Loop.Dispatch (Add lane))
                                ) {
                                    "+"
                                }
                            }
                        }
                }

                ul (
                    Attrs.className "board",
                    Motion.flip (LayoutParams (duration = 420, ease = Ease.outQuart)),
                    Hooks.mounted (fun (b: Board) element ->
                        b.Element.Value <- box element
                        fun () -> b.Element.Value <- null)
                ) {
                    each (fun (slot: Slot) -> slot.Card.Id) (fun (b: Board) -> b.Slots.Value) row
                }

                show (fun (b: Board) -> b.Total.Value = 0) (p (Attrs.className "empty", Motion.fade) { "The board is clear. Add a card, or undo." })
            }
        })

/// <summary>A mounted board and the handle its tests read.</summary>
[<NoEquality; NoComparison>]
type Mounted =
    {
        Board: Board
        Mount: Mounting.Mounted
    }

/// <summary>Mounts a board at <c>start</c> into <c>host</c> under <c>mode</c>.</summary>
let mount (mode: CommitMode) (graph: Graph) (report: exn list -> unit) (host: Fable.Core.TS.Dom.HTMLElement) (start: Model) : Mounted =
    let board = graph.Run (fun () -> createBoard start)
    let definition = view board

    let mounted =
        Mounting.mountWith mode graph None report host None (fun context -> Program.run (Component.render definition ()) context.Backend)

    { Board = board; Mount = mounted }

/// <summary>The browser entry: mounts the board into <c>#app</c> and publishes <c>window.__motion</c>.</summary>
let start () : unit -> unit =
    let graph = new Graph ()
    let mounted = mount Page.browserMode graph Page.report (Page.host ()) init
    let loop = mounted.Board.Loop

    let dispatch msg =
        graph.Run (fun () -> loop.Dispatch msg)

    Page.expose
        {|
            graph = graph
            board = mounted.Board
            cards = fun () -> graph.Run (fun () -> Api.untrack (fun () -> loop.Model.Cards))
            add = fun (column: int) -> dispatch (Add column)
            remove = fun (id: int) -> dispatch (Delete id)
            move = System.Func<int, int, unit>(fun id column -> dispatch (Move (id, column)))
            shuffle = fun () -> dispatch Shuffle
            clear = fun () -> dispatch Clear
            undo = fun () -> dispatch Undo
        |}

    fun () ->
        mounted.Mount.Dispose ()
        graph.Dispose ()
