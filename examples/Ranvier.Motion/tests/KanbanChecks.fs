/// <summary>The kanban board's update, its lane arithmetic, and the mounted board on the test clock.</summary>
module KanbanChecks

open Fable.Core
open global.Ranvier
open Ranvier.Anime
open Ranvier.View.Foundation
open Kanban
open Harness

[<Emit("globalThis.motionDraggables.find(d => d.$target === $0)")>]
let private draggableOf (element: obj) : Draggable = jsNative

[<Emit("$0.onGrab($0)")>]
let private grab (draggable: Draggable) : unit = jsNative

[<Emit("$0.onRelease($0)")>]
let private letGo (draggable: Draggable) : unit = jsNative

let private columnsOf (model: Model) =
    model.Cards
    |> Array.map (fun card -> card.Id, card.Column)
    |> List.ofArray

let private apply (messages: Msg list) =
    List.fold (fun model msg -> update msg model) init messages

let private updateEdits (_: Clock) =
    let added = apply [ Add 2 ]
    let deleted = apply [ Delete 3 ]
    let moved = apply [ Move (1, 2) ]
    let undone = apply [ Move (1, 2); Delete 2; Undo; Undo ]

    equal
        ([ 1, 0; 2, 0; 3, 1; 4, 1; 5, 2; 6, 2 ], [ 1, 0; 2, 0; 4, 1; 5, 2 ], [ 2, 0; 3, 1; 4, 1; 5, 2; 1, 2 ], columnsOf init, true)
        (columnsOf added, columnsOf deleted, columnsOf moved, columnsOf undone, undone.History.IsEmpty)

let private updateIgnores (_: Clock) =
    let unchanged =
        [ Delete 99; Move (1, 0); Move (1, 3); Move (99, 1); Undo ]
        |> List.map (fun msg -> obj.ReferenceEquals (update msg init, init))

    let empty = apply [ Clear ]
    equal ([ true; true; true; true; true ], obj.ReferenceEquals (update Clear empty, empty)) (unchanged, true)

let private shuffleIsSeeded (_: Clock) =
    let first = apply [ Shuffle ]
    let again = apply [ Shuffle ]

    let ids =
        first.Cards
        |> Array.map (fun card -> card.Id)
        |> Array.sort

    equal (columnsOf first, [| 1; 2; 3; 4; 5 |], true, init.Seed <> first.Seed) (columnsOf again, ids, columnsOf first <> columnsOf init, true)

let private laneArithmetic (_: Clock) =
    let placed =
        slots (apply [ Move (1, 1) ]).Cards
        |> Array.map (fun slot -> slot.Card.Id, slot.Row)

    equal
        ([| 2, 0; 3, 0; 4, 1; 5, 0; 1, 2 |], [| 1; 3; 1 |], [ 0; 1; 2; 2; 0; 1 ])
        (placed,
         counts (apply [ Move (2, 1) ]).Cards,
         [
             dropLane 0 300. 100.
             dropLane 0 300. 160.
             dropLane 1 300. 400.
             dropLane 2 300. 900.
             dropLane 0 300. -500.
             dropLane 1 0. 900.
         ])

let private mounted queued =
    let graph = new Graph ()
    let target = host ()
    let page = Kanban.mount (modeOf queued) graph report target init

    owned (fun () ->
        page.Mount.Dispose ()
        graph.Dispose ())

    page.Mount.Flush ()
    graph, target, page

let private dispatch (graph: Graph) (page: Kanban.Mounted) (msg: Msg) =
    write graph page.Mount (fun () -> page.Board.Loop.Dispatch msg)

let private close (_: Graph) (_: Kanban.Mounted) =
    release ()

let private rendersBoard queued (clock: Clock) =
    let graph, target, page = mounted queued
    advance clock 0.
    let counts = textsOf target ".lane .count"
    let lanes = attributesOf target ".card-slot" "data-lane"
    let titles = textsOf target ".card-title"
    let still = attributesOf target ".card" "style"
    close graph page

    equal
        ([| "2"; "2"; "1" |],
         [| "0"; "0"; "1"; "1"; "2" |],
         [|
             "Sketch the board"
             "Wire the reducer"
             "Pick an easing"
             "Measure the bundle"
             "Hydrate the story"
         |],
         Array.create 5 null)
        (counts, lanes, titles, still)

let private deleteHolds queued (clock: Clock) =
    let graph, target, page = mounted queued
    let leaving = find target ".card-slot[data-id=\"3\"]"
    dispatch graph page (Delete 3)
    advance clock 0.

    let held =
        connected leaving, styleOf leaving "position", textsOf target ".lane .count"

    run clock 16. 120.
    let fading = styleOf leaving "opacity"
    run clock 16. 200.
    let gone = connected leaving, countOf target ".card-slot"
    close graph page
    equal ((true, "absolute", [| "2"; "1"; "1" |]), true, (false, 4)) (held, fading <> "" && fading <> "0" && fading <> "1", gone)

let private addEnters queued (clock: Clock) =
    let graph, target, page = mounted queued
    dispatch graph page (Add 1)
    let card = find target ".card-slot[data-id=\"6\"]"
    advance clock 0.
    advance clock 16.
    let start = styleOf card "opacity", styleOf card "transform"
    run clock 16. 600.
    let finish = styleOf card "opacity", styleOf card "transform"
    let lane = attributeOf card "data-lane"
    close graph page

    equal
        (true, ("1", "translate(0px,0px) scale(1)"), "1")
        (fst start <> "1"
         && (snd start).Contains "scale(0.",
         finish,
         lane)

let private clearAndUndo queued (clock: Clock) =
    let graph, target, page = mounted queued
    dispatch graph page Clear
    let held = countOf target ".card-slot", countOf target ".empty"
    run clock 16. 400.
    let cleared = countOf target ".card-slot", countOf target ".empty"
    dispatch graph page Undo
    run clock 16. 400.

    let restored =
        countOf target ".card-slot", countOf target ".empty", textsOf target ".lane .count"

    close graph page
    equal ((5, 1), (0, 1), (5, 0, [| "2"; "2"; "1" |])) (held, cleared, restored)

let private columnOf (graph: Graph) (page: Kanban.Mounted) (id: int) =
    graph.Run (fun () ->
        page.Board.Loop.Model.Cards
        |> Array.find (fun card -> card.Id = id))
    |> fun card -> card.Column

let private dragDrops (clock: Clock) =
    let graph, target, page = mounted false
    fixProperty (find target ".board") "clientWidth" (box 900.)
    let slot = find target ".card-slot[data-id=\"2\"]"
    let draggable = draggableOf slot
    grab draggable
    draggable.x <- 310.
    advance clock 16.
    letGo draggable
    page.Mount.Flush ()
    let movedTo = columnOf graph page 2
    let reset = struct (draggable.x, draggable.y)
    let near = draggableOf (find target ".card-slot[data-id=\"1\"]")
    grab near
    near.x <- 60.
    advance clock 16.
    letGo near
    run clock 16. 2000.
    let stayed = columnOf graph page 1
    let home = struct (near.x, near.y)
    close graph page
    equal (1, struct (0., 0.), 0, struct (0., 0.)) (movedTo, reset, stayed, home)

let private regrabStopsSpring (clock: Clock) =
    let graph, target, page = mounted false
    fixProperty (find target ".board") "clientWidth" (box 900.)
    let near = draggableOf (find target ".card-slot[data-id=\"1\"]")
    grab near
    near.x <- 60.
    advance clock 16.
    letGo near
    page.Mount.Flush ()
    advance clock 0.
    run clock 16. 80.
    grab near
    page.Mount.Flush ()
    let held = near.x
    run clock 16. 1500.
    let after = near.x
    close graph page
    equal (true, held) (held > 0. && held < 60., after)

let cases: Case list =
    [
        case "update: add, delete and move edit the cards, and undo walks back" updateEdits
        case "update: no-op messages return the same model" updateIgnores
        case "update: shuffle is seeded, keeps every card and moves some" shuffleIsSeeded
        case "slots number cards per lane, counts tally lanes, and a drop rounds to the nearest lane" laneArithmetic
        yield! modes "the board renders its lanes and cards, and first-pass cards do not enter" rendersBoard
        yield! modes "delete pins the card and holds it until its exit completes" deleteHolds
        yield! modes "add fades and scales the new card in" addEnters
        yield! modes "clear holds every card until its exit, and undo brings them back" clearAndUndo
        case "drag: a release one lane over moves the card, a release in its lane springs home" dragDrops
        case "drag: a grab while the card springs home cancels the spring" regrabStopsSpring
    ]
