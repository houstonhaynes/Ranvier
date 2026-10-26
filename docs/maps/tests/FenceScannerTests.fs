module Ranvier.Docs.Maps.Tests.FenceScannerTests

open Expecto
open Ranvier.Docs.Maps.Authoring

[<Tests>]
let tests =
    testList
        "Ranvier fences"
        [
            test "code-only cells compile but have no browser entry" {
                let scan =
                    FenceScanner.scan
                        "p0123456789"
                        "```fsharp ranvier show=code id=hidden\nMy.counter\n```\n```fsharp ranvier id=visible\nMy.other\n```\n"

                let page = MapPage.unit "p0123456789" None 1 scan.Cells
                Expect.equal (MapPage.rendered page |> List.map _.Id) [ "visible" ] "only the mounted cell gets an entry"
                Expect.stringContains page.Code "My.counter" "hidden code still compiles"
                Expect.isFalse (scan.Body.Contains "rvm-p0123456789-hidden") "no hidden host"
            }
            test "nested bindings stay inside a component expression" {
                let scan =
                    FenceScanner.scan
                        "p0123456789"
                        "```fsharp ranvier\nComponent.define\n    (fun _ () ->\n        let count = createSignal 0\n        count)\n    build\n```\n"

                Expect.equal scan.Cells.Head.Kind PageCellKind.Expression "the inner let is not a page declaration"
                Expect.stringContains scan.Body "rv-map-host" "the component gets a host"
            }
            test "an indented fence is code within the executable fence" {
                let scan =
                    FenceScanner.scan "p0123456789" "```fsharp ranvier\nfirst\n    ```\nlast\n```\n"

                Expect.stringContains scan.Cells.Head.Code "last" "four-space indentation cannot close a fence"
            }
            test "a component expression becomes a mount with its source line" {
                let scan =
                    FenceScanner.scan "p0123456789" "Intro\n\n```fsharp ranvier id=counter\nmyCounter\n```\n"

                Expect.isEmpty scan.Problems "valid fence"
                Expect.equal scan.Cells.Length 1 "one component"
                Expect.equal scan.Cells.Head.Line 4 "body-relative code line"
                Expect.equal scan.Cells.Head.Kind PageCellKind.Expression "expression"
                Expect.stringContains scan.Body "rvm-p0123456789-counter" "mount host"
            }
            test "an ordinary outer fence protects nested example fences" {
                let body = "````markdown\n```fsharp ranvier\nexample\n```\n````\n"
                let scan = FenceScanner.scan "p0123456789" body
                Expect.isEmpty scan.Cells "documentation is not executed"
                Expect.equal scan.Body body "preserves ordinary code"
            }
            test "setup declarations compile before a named render" {
                let scan =
                    FenceScanner.scan
                        "p0123456789"
                        "```fsharp ranvier setup\nlet count = 1\n```\n```fsharp ranvier render=counter show=output\nlet counter = make count\n```\n"

                Expect.equal (scan.Cells |> List.map _.Kind) [ PageCellKind.Declarations; PageCellKind.Render "counter" ] "page order"
                Expect.isFalse (scan.Body.Contains "let count") "setup is hidden"
                Expect.isFalse (scan.Body.Contains "let counter") "output hides code"
            }
            test "duplicate normalized ids and unterminated fences are diagnostics" {
                let scan =
                    FenceScanner.scan "p0123456789" "```fsharp ranvier id=a-b\none\n```\n```fsharp ranvier id=a_b\ntwo"

                Expect.equal scan.Problems.Length 2 "collision and missing close"
            }
            test "a map fence compiles the scenario and retains disclosure flags" {
                let scan =
                    FenceScanner.scan "p0123456789" "~~~fsharp map code=collapsed\nlet count = createSignal 1\ncontrols []\n~~~\n"

                Expect.isEmpty scan.Problems "valid map"

                match scan.Cells.Head.Kind with
                | PageCellKind.Map (_, flags) -> Expect.isFalse flags.CodeOpen "collapsed code"
                | kind -> failtestf "expected map, got %A" kind

                Expect.stringContains scan.Body "rv-map-card" "map card"
            }
        ]
