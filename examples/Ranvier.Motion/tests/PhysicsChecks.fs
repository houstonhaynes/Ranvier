/// <summary>The physics playground mounted under jsdom on the test clock.</summary>
module PhysicsChecks

open Fable.Core
open global.Ranvier
open Ranvier.Anime
open Ranvier.View
open Physics
open Harness

[<Emit("globalThis.motionDraggables.find(d => d.$target === $0)")>]
let private draggableOf (element: obj) : Draggable = jsNative

[<Emit("Number(($0.style.transform.match(/translate\((-?[\d.]+)px/) || [0, 0])[1])")>]
let private shiftOf (element: obj) : float = jsNative

let private mounted queued =
    let graph = new Graph ()
    let target = host ()
    let page = Physics.mount (modeOf queued) graph report target

    owned (fun () ->
        page.Mount.Dispose ()
        graph.Dispose ())

    page.Mount.Flush ()
    graph, target, page

let private close (_: Graph) (_: Physics.Mounted) =
    release ()

let private followersTrail queued (clock: Clock) =
    let graph, target, page = mounted queued
    write graph page.Mount (fun () -> page.Playground.Puck.Value <- struct (120., 40.))
    advance clock 0.
    run clock 16. 160.
    let lead = shiftOf (find target "[data-follower=\"0\"]")
    let last = shiftOf (find target "[data-follower=\"4\"]")
    run clock 16. 900.

    let settled =
        shiftOf (find target "[data-follower=\"0\"]"), shiftOf (find target "[data-follower=\"4\"]")

    let puck = draggableOf (find target ".puck")
    let moved = puck.x, puck.y
    close graph page
    equal (true, (120., 120.), (120., 40.)) (lead > last && last > 0., settled, moved)

let private springsHome queued (clock: Clock) =
    let graph, target, page = mounted queued
    write graph page.Mount (fun () -> page.Playground.Puck.Value <- struct (90., -30.))
    graph.Run (fun () -> springHome page.Playground)
    advance clock 0.
    run clock 16. 80.
    let struct (midway, _) = graph.Run (fun () -> page.Playground.Puck.Value)
    run clock 16. 3000.
    let home = graph.Run (fun () -> page.Playground.Puck.Value)
    let puck = draggableOf (find target ".puck")
    let draggable = puck.x, puck.y
    close graph page
    equal (true, struct (0., 0.), (0., 0.)) (midway > 0. && midway < 90., home, draggable)

let private scrubSeeks queued (clock: Clock) =
    let graph, target, page = mounted queued
    let timeline = page.Playground.Timeline.Value
    let start = timeline.progress
    write graph page.Mount (fun () -> page.Playground.Scrub.Value <- 0.5)
    let half = timeline.progress, textsOf target ".scrubber .readout"
    let dot = find target "[data-dot=\"1\"]"
    let moved = styleOf dot "transform"
    write graph page.Mount (fun () -> page.Playground.Scrub.Value <- 0.)
    let back = timeline.progress
    close graph page
    equal (0., (0.5, [| "50%" |]), true, 0.) (start, half, moved <> "", back)

let private policySwitchesEnter queued (clock: Clock) =
    let graph, target, page = mounted queued
    write graph page.Mount (fun () -> page.Playground.Tips.Value <- true)
    advance clock 0.
    advance clock 16.
    let full = styleOf (find target ".tips") "opacity"
    write graph page.Mount (fun () -> page.Playground.Tips.Value <- false)
    run clock 16. 300.
    write graph page.Mount (fun () -> page.Playground.Policy.Value <- Reactive.Reduced)

    let checkedBox =
        Page.query target ".reduced"
        |> unbox<TS.Dom.HTMLInputElement>

    write graph page.Mount (fun () -> page.Playground.Tips.Value <- true)
    advance clock 0.
    advance clock 16.
    let reduced = styleOf (find target ".tips") "opacity"
    write graph page.Mount (fun () -> page.Playground.Puck.Value <- struct (50., 50.))
    graph.Run (fun () -> springHome page.Playground)
    let jumped = graph.Run (fun () -> page.Playground.Puck.Value)
    close graph page
    equal (true, true, "", struct (0., 0.)) (full <> "" && full <> "1", checkedBox.``checked``, reduced, jumped)

[<Emit("$0.onGrab($0)")>]
let private grab (draggable: Draggable) : unit = jsNative

let private puckOf (graph: Graph) (page: Physics.Mounted) =
    graph.Run (fun () -> Api.untrack (fun () -> page.Playground.Puck.Value))

let private grabStopsSpring queued (clock: Clock) =
    let graph, target, page = mounted queued
    write graph page.Mount (fun () -> page.Playground.Puck.Value <- struct (120., 40.))
    graph.Run (fun () -> springHome page.Playground)
    let spring = page.Playground.Spring.Running
    advance clock 0.
    run clock 16. 80.
    grab (draggableOf (find target ".puck"))
    page.Mount.Flush ()
    let held = puckOf graph page
    run clock 16. 1500.
    let after = puckOf graph page
    let struct (x, _) = held
    equal (true, true, null, held) (x > 0. && x < 120., spring.cancelled, page.Playground.Spring.Running, after)

let private repeatReplaces queued (clock: Clock) =
    let graph, _, page = mounted queued
    write graph page.Mount (fun () -> page.Playground.Puck.Value <- struct (120., 40.))
    graph.Run (fun () -> springHome page.Playground)
    let first = page.Playground.Spring.Running
    advance clock 0.
    run clock 16. 80.
    graph.Run (fun () -> springHome page.Playground)
    let second = page.Playground.Spring.Running
    run clock 16. 3000.
    let home = puckOf graph page
    equal (true, false, true, struct (0., 0.)) (first.cancelled, not second.completed, obj.ReferenceEquals (first, second) |> not, home)

let private disposalStopsSpring queued (clock: Clock) =
    let graph, _, page = mounted queued
    write graph page.Mount (fun () -> page.Playground.Puck.Value <- struct (120., 40.))
    graph.Run (fun () -> springHome page.Playground)
    let spring = page.Playground.Spring.Running
    advance clock 0.
    run clock 16. 80.
    release ()
    run clock 16. 500.
    equal (true, true) (spring.cancelled, spring.progress < 1.)

let cases: Case list =
    [
        yield! modes "followers trail the puck, each slower, and settle on it" followersTrail
        yield! modes "spring home moves the puck back through its position signal" springsHome
        yield! modes "a grab mid-spring cancels the spring and the puck stays where it was grabbed" grabStopsSpring
        yield! modes "a second spring home cancels the first and still lands home" repeatReplaces
        yield! modes "disposing the page mid-spring cancels the spring" disposalStopsSpring
        yield! modes "the scrubber's signal seeks the timeline forward and back" scrubSeeks
        yield! modes "the reduced policy skips the tips' rise and makes spring home a jump" policySwitchesEnter
    ]
