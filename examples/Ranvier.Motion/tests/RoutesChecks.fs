/// <summary>The routes shell: its parsing and update, and the mounted shell switched on the test clock.</summary>
module RoutesChecks

open global.Ranvier
open Ranvier.View.Foundation
open Routes
open Harness

let private routing (_: Clock) =
    let roundTrip =
        routes
        |> Array.map (fun route -> parse (hashOf route))

    let start =
        {
            Route = Home
            Theme = Daylight
            Visits = 0
        }

    let moved = updateShell (Navigated Gallery) start
    let again = updateShell (Navigated Gallery) moved

    equal
        (routes, [ Home; Home; Settings ], (Gallery, 1), true, [| "Coral"; "Amber"; "Rose" |])
        (roundTrip,
         [ parse ""; parse "#/nowhere"; parse "settings" ],
         (moved.Route, moved.Visits),
         obj.ReferenceEquals (again, moved),
         shownBy Warm
         |> Array.map (fun swatch -> swatch.Name))

let private mounted queued (route: Route) =
    let graph = new Graph ()
    let target = host ()
    let page = Routes.mount (modeOf queued) graph report target route

    owned (fun () ->
        page.Mount.Dispose ()
        graph.Dispose ())

    page.Mount.Flush ()
    graph, target, page

let private go (graph: Graph) (page: Routes.Mounted) (route: Route) =
    write graph page.Mount (fun () -> page.Shell.Loop.Dispatch (Navigated route))

let private close (_: Graph) (_: Routes.Mounted) =
    release ()

let private switchCrosses queued (clock: Clock) =
    let graph, target, page = mounted queued Home

    let first =
        attributesOf target ".page" "data-route", styleOf (find target ".page-body") "transform"

    let leaving = find target ".page"
    go graph page Gallery
    let both = attributesOf target ".page" "data-route", styleOf leaving "position"
    run clock 16. 120.
    let fading = styleOf leaving "opacity"
    run clock 16. 300.

    let after =
        attributesOf target ".page" "data-route", textOf (find target ".shell-foot")

    close graph page

    equal
        (([| "home" |], ""), ([| "home"; "gallery" |], "absolute"), true, ([| "gallery" |], "Gallery · 1 route changes"))
        (first, both, fading <> "" && fading <> "0" && fading <> "1", after)

let private rapidSwitches queued (clock: Clock) =
    let graph, target, page = mounted queued Home

    let order =
        [
            Gallery
            Settings
            About
            Home
            Settings
            Gallery
            About
            Settings
            Home
            About
        ]

    let mutable most = 0

    for route in order do
        go graph page route
        most <- max most (countOf target ".page")
        run clock 16. 32.

    run clock 16. 1000.
    let left = attributesOf target ".page" "data-route"
    let visits = graph.Run (fun () -> page.Shell.Visits.Value)
    close graph page
    equal (true, [| "about" |], 10) (most > 2, left, visits)

let private pagesOwnLoops queued (clock: Clock) =
    let graph, target, page = mounted queued Home
    click (find target "button.increment")
    click (find target "button.increment")
    page.Mount.Flush ()
    let counted = textOf (find target ".count")
    go graph page Settings
    run clock 16. 400.
    click (find target ".chip[data-theme=\"dusk\"]")
    page.Mount.Flush ()
    let theme = attributeOf (find target ".shell") "data-theme"
    go graph page Home
    run clock 16. 400.

    let reset =
        textOf (find target ".count"), attributeOf (find target ".shell") "data-theme"

    close graph page
    equal ("2", "dusk", ("0", "dusk")) (counted, theme, reset)

let private filterScales queued (clock: Clock) =
    let graph, target, page = mounted queued Gallery
    click (find target ".chip[data-filter=\"cool\"]")
    page.Mount.Flush ()
    let held = countOf target ".swatch"
    run clock 16. 300.
    let cool = textsOf target ".swatch"
    close graph page
    equal (6, [| "Indigo"; "Mint"; "Sky" |]) (held, cool)

let cases: Case list =
    [
        case "routes parse from hashes, navigation counts changes, and filters pick swatches" routing
        yield! modes "a switch pins the leaving page while it fades and leaves the next page" switchCrosses
        yield! modes "ten rapid switches leave only the last page" rapidSwitches
        yield! modes "pages run their own loops: the counter resets, the theme reaches the shell" pagesOwnLoops
        yield! modes "a gallery filter holds hidden swatches for their exit" filterScales
    ]
