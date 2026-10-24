module Ranvier.Docs.Maps.Tests.PageTests

open System
open Expecto
open Ranvier.View
open Ranvier.Docs.Maps
open Ranvier.Docs.Maps.Authoring

let private omit =
    { Server.Options.defaults with
        Policy = Omit
    }

let private fixtureCell id : PageCell =
    {
        Id = id
        Kind = PageCellKind.Expression
        Code = "Ranvier.Docs.Maps.PipelineFixture.counter"
        Line = 4
    }

let private unit' cells =
    MapPage.unit "p0123456789" (Some "/docs/content/page.md") 3 cells

[<Tests>]
let tests =
    testList
        "Pages"
        [
            test "the pipeline fixture renders deterministically on .NET" {
                let first = Server.render omit PipelineFixture.counter ()
                let second = Server.render omit PipelineFixture.counter ()

                Expect.equal
                    first
                    """<!--[r--><div class="rv-map-fixture"><button type="button" class="rv-map-fixture__count">0</button></div><!--]-->"""
                    "server HTML"

                Expect.equal second first "a second render"
            }

            test "a map renders its empty scene and controls shell without running its scenario" {
                let mutable ran = 0

                let scenario _ =
                    ran <- ran + 1
                    controls []

                let live =
                    SignalMap (Live scenario) Ranvier.FlightPolicy.CancelPrevious [||] false Grouping.Expand

                let replay =
                    SignalMapWithSpeed 0.5 (Replayed scenario) Ranvier.FlightPolicy.Queue [||] true Grouping.Expand

                let liveHtml = Server.render omit live ()
                let replayHtml = Server.render omit replay ()

                Expect.equal ran 0 "no scenario runs on the server"
                Expect.equal (Server.render omit live ()) liveHtml "a second render"
                Expect.stringContains liveHtml "class=\"rv-map\"" "the map's root"
                Expect.stringContains liveHtml "rv-map__button--reset" "a live map's Reset"
                Expect.isFalse (liveHtml.Contains "rv-map__timeline") "a live map without a timeline"
                Expect.stringContains replayHtml "rv-map__timeline" "a replay's timeline"
                Expect.stringContains replayHtml ">Play</button>" "a paused replay"
                Expect.stringContains replayHtml "--rv-map-speed: 0.5" "the initial speed"
                Expect.stringContains replayHtml "<option value=\"0.5\" selected=\"\">0.5×</option>" "the selected speed"
                Expect.isFalse (replayHtml.Contains "rv-map-node") "no nodes before the scenario runs"
            }

            test "two maps of one definition render independently, and their speeds are validated" {
                let map =
                    SignalMap (Live (fun _ -> controls [])) Ranvier.FlightPolicy.CancelPrevious [||] true Grouping.Collapse

                let pair =
                    Component.define (fun _ () -> ()) (fun t -> t.fragment [| t.render map ignore; t.render map ignore |])

                let html = Server.render omit pair ()
                let single = Server.render omit map ()
                Expect.equal (html.Split "class=\"rv-map\"").Length 3 "two roots"
                Expect.isGreaterThan html.Length single.Length "both rendered"

                Expect.throwsT<System.ArgumentException>
                    (fun () ->
                        SignalMapWithSpeed 5.0 (Live (fun _ -> controls [])) Ranvier.FlightPolicy.CancelPrevious [||] true Grouping.Expand
                        |> ignore)
                    "a speed above 4"
            }

            test "each rendered cell gets a distinct host and id prefix" {
                let page = unit' [ fixtureCell "c1"; fixtureCell "c2" ]

                let hosts =
                    page.Cells
                    |> List.map (fun cell -> MapPage.hostId page.Key cell.Id)

                Expect.equal hosts [ "rvm-p0123456789-c1"; "rvm-p0123456789-c2" ] "host ids"
                Expect.stringContains page.Code "HostId = \"rvm-p0123456789-c1\"" "the first definition's host"
                Expect.stringContains page.Code "IdPrefix = \"rvm-p0123456789-c2\"" "the second definition's prefix"
                Expect.stringContains page.Code "let page_c1: Ranvier.View.Page.Definition<unit>" "a definition per cell"
                Expect.stringContains page.Code "State = None" "no state codec"
            }

            test "a cell's code maps back to its body line and column" {
                let page = unit' [ fixtureCell "c1" ]
                let lines = page.Code.Split '\n'

                let line =
                    lines
                    |> Array.findIndex (fun l -> l.Contains "PipelineFixture.counter")

                Expect.equal (MapPage.locate page.Spans (line + 1) 9) (Some (4, 5)) "the fence line, moved left by the indent"

                let definition =
                    lines
                    |> Array.findIndex (fun l -> l.StartsWith "let page_c1")

                Expect.equal (MapPage.locate page.Spans (definition + 1) 9) (Some (3, 1)) "generated lines blame the opening line"
            }

            test "a map cell compiles its scenario and renders its SignalMap call" {
                let code =
                    "let count = createSignal 1\ncontrols [ button \"Add\" (fun () -> count.Value <- 2) ]"

                match MapFence.compile "c1" [ "timeline" ] code, MapFlags.parse [ "timeline" ] with
                | Ok output, Ok flags ->
                    let page =
                        unit'
                            [
                                {
                                    Id = "c1"
                                    Kind = PageCellKind.Map (output, flags)
                                    Code = code
                                    Line = 10
                                }
                            ]

                    Expect.stringContains page.Code "module Map_c1 =" "the scenario module"

                    Expect.stringContains
                        page.Code
                        ("let Cell_c1: Ranvier.View.Template.Component<unit> = "
                         + output.Render)
                        "the render call"
                | _ -> failtest "the fence compiles"
            }

            test "a page's map flags and diagnostics come from its stored unit, so a cached page keeps them" {
                let code =
                    "let count = createSignal 1
let doubled = createMemo (fun _ -> count.Value * 2)
controls [ button \"Add\" (fun () -> count.Value <- 2) ]"

                let raw = [ "replay"; "code=collapsed"; "code-max-height=24rem" ]

                match MapFence.compile "c2" raw code, MapFlags.parse raw with
                | Ok output, Ok parsed ->
                    let make () =
                        unit'
                            [
                                fixtureCell "c1"
                                {
                                    Id = "c2"
                                    Kind = PageCellKind.Map (output, parsed)
                                    Code = code
                                    Line = 20
                                }
                            ]

                    let page = make ()
                    Expect.equal (make ()) page "the same page yields the same unit"
                    Expect.equal (MapPage.flags [ page ]) (Map.ofList [ ("p0123456789", "c2"), parsed ]) "the map's flags"

                    let lines = page.Code.Split '\n'

                    let doubled =
                        lines
                        |> Array.findIndex (fun l -> l.TrimStart().StartsWith "let doubled")

                    Expect.equal
                        (MapPage.locate page.Spans (doubled + 1) 26)
                        (Some (21, 26 - 8))
                        "a scenario line maps to its fence line, moved left by the scenario's indent"

                    Expect.equal (List.map snd (MapPage.flags [ page ] |> Map.toList)) [ parsed ] "one map cell"
                | _ -> failtest "the fence compiles"
            }

            test "page keys name stable modules, hosts and entries" {
                Expect.equal (MapPage.moduleName "p0123456789") "Ranvier.Docs.MapPages.P0123456789" "the module"
                Expect.equal (MapPage.fileName "p0123456789") "P0123456789.fs" "the file"
                Expect.equal (MapPage.entryName "p0123456789" "map-1") "P0123456789_map_1" "the entry"
                Expect.equal (MapPage.hostId "p0123456789" "map-1") "rvm-p0123456789-map-1" "the host"
            }

            test "every generated line maps to a page line, and the header blames the first fence" {
                let page = unit' [ fixtureCell "c1"; { fixtureCell "c2" with Line = 9 } ]
                let count = (page.Code.TrimEnd '\n').Split('\n').Length

                for line in 1..count do
                    Expect.isSome (MapPage.locate page.Spans line 1) $"line {line} maps"

                Expect.equal (MapPage.locate page.Spans 1 8) (Some (3, 1)) "the module line blames the first fence's opening line"
                Expect.equal (MapPage.locate page.Spans 3 1) (Some (3, 1)) "an open line blames it too"
            }

            test "an expression cell keeps a multi-line string literal's text exactly" {
                let code =
                    String.concat
                        "\n"
                        [
                            "let note = \"\"\"first"
                            "    second"
                            "  "
                            "third\"\"\""
                            "Ranvier.Docs.Maps.PipelineFixture.counter"
                        ]

                let page =
                    unit'
                        [
                            {
                                Id = "c1"
                                Kind = PageCellKind.Expression
                                Code = code
                                Line = 4
                            }
                        ]

                Expect.stringContains page.Code "let note = \"\"\"first\n    second\n  \nthird\"\"\"\n" "the literal's lines are copied as written"
                Expect.stringContains page.Code "\n    Ranvier.Docs.Maps.PipelineFixture.counter" "code outside the literal is indented"

                let lines = page.Code.Split '\n'

                let second =
                    lines
                    |> Array.findIndex (fun l -> l = "    second")

                Expect.equal (MapPage.locate page.Spans (second + 1) 5) (Some (5, 5)) "a literal line keeps its columns"

                let first =
                    lines
                    |> Array.findIndex (fun l -> l.Contains "let note")

                Expect.equal (MapPage.locate page.Spans (first + 1) 9) (Some (4, 5)) "a code line moves left by the indent"
            }

            test "declarations render no host and a named component renders its value" {
                let page =
                    unit'
                        [
                            {
                                Id = "c1"
                                Kind = PageCellKind.Declarations
                                Code = "let start = 1"
                                Line = 2
                            }
                            {
                                Id = "c2"
                                Kind = PageCellKind.Render "Ranvier.Docs.Maps.PipelineFixture.counter"
                                Code = "let other = 2"
                                Line = 6
                            }
                        ]

                Expect.isFalse (page.Code.Contains "page_c1") "declarations have no page"

                Expect.stringContains
                    page.Code
                    "let Cell_c2: Ranvier.View.Template.Component<unit> = Ranvier.Docs.Maps.PipelineFixture.counter"
                    "the named component"

                Expect.equal (MapPage.rendered page |> List.map _.Id) [ "c2" ] "rendered cells"
            }

            test "the scanner's placeholders become map hosts and cards" {
                let body =
                    String.concat
                        "\n"
                        [
                            "<div class=\"partas-solid-card partas-solid-card--map\">"
                            ""
                            "<div class=\"partas-solid\" data-partas-page=\"p0123456789\" data-partas-cell=\"c1\"></div>"
                            ""
                            "</div>"
                            "<div class=\"partas-solid partas-solid--inline\" data-partas-page=\"p0123456789\" data-partas-cell=\"c2\"></div>"
                        ]

                let rewritten = MapPage.hosts body
                Expect.isFalse (rewritten.Contains "partas-solid") "the Solid loader claims nothing"
                Expect.stringContains rewritten "<div class=\"rv-map-card\">" "the card"

                Expect.stringContains
                    rewritten
                    "<div class=\"rv-map-host\" id=\"rvm-p0123456789-c1\" data-ranvier-map-page=\"p0123456789\" data-ranvier-map-cell=\"c1\"></div>"
                    "the host"

                Expect.stringContains rewritten "class=\"rv-map-host rv-map-host--inline\" id=\"rvm-p0123456789-c2\"" "an inline host"
            }

            test "the exporter lists every page definition and each page's entry imports its cells" {
                let first = unit' [ fixtureCell "c1"; fixtureCell "c2" ]
                let program = MapPage.exporter [ first ]

                Expect.stringContains
                    program
                    "\"p0123456789\", \"c2\", \"Ranvier.Docs.MapPages.P0123456789.page_c2\", (fun () -> Ranvier.Docs.MapPages.P0123456789.page_c2)"
                    "the second definition"

                let path (cell: PageCell) =
                    Some (
                        "./entries/"
                        + MapPage.entryName first.Key cell.Id
                        + ".js"
                    )

                Expect.equal
                    (MapPage.pageEntry path first)
                    (Some
                        "const starts = [\n  () => import(\"./entries/P0123456789_c1.js\"),\n  () => import(\"./entries/P0123456789_c2.js\"),\n];\nfor (const start of starts) start().catch(error => console.error(error));\n")
                    "each cell starts on its own"

                Expect.isNone (MapPage.pageEntry (fun _ -> None) first) "a cell without a compiled entry"
            }

            test "fragment hosts are filled after minification and minified attributes still match" {
                let html =
                    "<main><div class=rv-map-host id=rvm-p0123456789-c1 data-ranvier-map-page=p0123456789 data-ranvier-map-cell=c1></div>"
                    + "<div class=\"rv-map-host\" id=\"rvm-p0123456789-c2\"></div></main></body>"

                let fragments =
                    Map.ofList [ "rvm-p0123456789-c1", "<!--[r-->one<!--]-->"; "rvm-p0123456789-c2", "two" ]

                let filled =
                    MapPage.fill (fun host -> Map.tryFind host fragments) (fun key -> Some $"/Ranvier/_ranvier/maps/{key}.js") html

                Expect.stringContains filled "data-ranvier-map-cell=c1><!--[r-->one<!--]--></div>" "the first host"
                Expect.stringContains filled "id=\"rvm-p0123456789-c2\">two</div>" "the second host"

                Expect.stringContains
                    filled
                    "<script type=\"module\" src=\"/Ranvier/_ranvier/maps/p0123456789.js\"></script></body>"
                    "one page script"

                Expect.equal (MapPage.fill (fun _ -> None) (fun _ -> None) "<p>plain</p>") "<p>plain</p>" "a page without hosts"
            }
        ]
