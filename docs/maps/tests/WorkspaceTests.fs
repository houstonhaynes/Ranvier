module Ranvier.Docs.Maps.Tests.WorkspaceTests

open System.IO
open Expecto
open Ranvier.Docs.Maps.Authoring

let private maps = Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, ".."))

let private page key (code: string) =
    MapPage.unit
        key
        (Some $"/docs/content/{key}.md")
        1
        [
            {
                Id = "c1"
                Kind = PageCellKind.Expression
                Code = code
                Line = 4
            }
        ]

[<Tests>]
let tests =
    testSequenced
    <| testList
        "MapWorkspace"
        [
            test "custom example sources and compiler pins invalidate the cache" {
                let directory =
                    Path.Combine (
                        Path.GetTempPath (),
                        "ranvier-cache-"
                        + System.Guid.NewGuid().ToString "N"
                    )

                let examples = Path.Combine (directory, "examples")
                Directory.CreateDirectory examples |> ignore

                Directory.CreateDirectory (Path.Combine (directory, ".config"))
                |> ignore

                let project = Path.Combine (examples, "Examples.fsproj")
                let source = Path.Combine (examples, "Counter.fs")
                let tools = Path.Combine (directory, ".config", "dotnet-tools.json")

                try
                    File.WriteAllText (project, "<Project><ItemGroup><Compile Include=\"Counter.fs\" /></ItemGroup></Project>")
                    File.WriteAllText (source, "module Counter\nlet initial = 0")
                    File.WriteAllText (tools, "old")
                    let initial = MapWorkspace.inputs directory project
                    File.WriteAllText (source, "module Counter\nlet initial = 100")
                    let changed = MapWorkspace.inputs directory project
                    Expect.notEqual changed initial "custom sources outside src/docs/maps are tracked"
                    File.WriteAllText (tools, "new compiler version")
                    Expect.notEqual (MapWorkspace.inputs directory project) changed "tool manifest is tracked"
                finally
                    Directory.Delete (directory, true)
            }
            test "a page that fails to compile or render is left out, and the other pages still build" {
                let esbuild =
                    match Nacara.Plugins.Internal.EsbuildBinary.resolve () with
                    | Ok path -> path
                    | Error problem -> failtest problem

                let good = page "p0000000001" "Ranvier.Docs.Maps.PipelineFixture.counter"
                let uncompiled = page "p0000000002" "Ranvier.Docs.Maps.PipelineFixture.missing"
                let throwing = page "p0000000003" "failwith \"the fixture throws\""

                let built =
                    MapWorkspace.build
                        {
                            Workspace = Path.Combine (maps, "..", ".nacara", "ranvier-maps-test")
                            MapsProject = Path.Combine (maps, "Ranvier.Docs.Maps.fsproj")
                            Repository = Path.GetFullPath (Path.Combine (maps, "..", ".."))
                            Esbuild = esbuild
                            Minify = false
                            Packages = [ "animejs", "4.5.0" ]
                            Log = ignore
                        }
                        [ good; uncompiled; throwing ]

                let messages =
                    built.Messages
                    |> List.map (fun m ->
                        $"""%s{m.Page
                               |> Option.map _.Key
                               |> Option.defaultValue "-"}: %s{m.Text}""")
                    |> String.concat
                        "
"

                Expect.equal
                    built.Failed
                    (set [ "p0000000002"; "p0000000003" ])
                    $"the two broken pages; the build reported:
%s{messages}"

                let files = built.Files |> List.map fst
                Expect.contains files "p0000000001.js" "the good page's bundle"

                Expect.isFalse
                    (files
                     |> List.exists (fun f ->
                         f.StartsWith "p0000000002"
                         || f.StartsWith "p0000000003"))
                    "no bundle for a broken page"

                Expect.stringContains built.Fragments[MapPage.hostId good.Key "c1"] "rv-map-fixture" "the good page's server HTML"
                let uncompiledCard = built.Fragments[MapPage.hostId uncompiled.Key "c1"]
                Expect.stringContains uncompiledCard "rv-map-failed" "a failure card"
                Expect.stringContains uncompiledCard "missing" "the card holds the compiler's message"
                Expect.stringContains built.Fragments[MapPage.hostId throwing.Key "c1"] "the fixture throws" "the card holds the render failure"

                let compileError =
                    built.Messages
                    |> List.find (fun m ->
                        m.Page
                        |> Option.exists (fun p -> p.Key = uncompiled.Key))

                Expect.isTrue compileError.IsError "an error"
                Expect.equal (compileError.At |> Option.map fst) (Some 4) "placed on the cell's line"

                Expect.isTrue
                    (built.Messages
                     |> List.exists (fun m ->
                         m.IsError
                         && m.Page
                            |> Option.exists (fun p -> p.Key = throwing.Key)))
                    "the render failure is an error"
            }
        ]
