module Ranvier.Analyzers.Tests.Ran011

open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private findings (body: string) =
    let source =
        "module Fixture\nopen Ranvier\nopen System.Threading.Tasks\n"
        + body

    run Analyzers.ran011Cli (context (ranvier.Force ()) source)

let private codes body =
    findings body |> List.map _.Code

let private fetch = "let fetch () = createAsync (fun _ _ -> Task.FromResult 1)\n"

[<Tests>]
let tests =
    testList
        "RAN011"
        [
            test "created and read in a suspense body, with its exact message" {
                let body =
                    fetch
                    + "let view () =\n    createSuspense (fun _ -> 0) (fun () ->\n        let report = createAsync (fun _ _ -> Task.FromResult 1)\n        report.Value)\n"

                Expect.equal
                    (findings body |> List.map _.Message)
                    [
                        "`report` is created inside `createSuspense`'s body and read there; each run of the body disposes `report` and starts a new flight. Create `report` outside `createSuspense` and read it inside."
                    ]
                    "one message"
            }

            test "the finding's range is the binding identifier" {
                let range =
                    (findings
                        "let view () =
    createSuspense (fun _ -> 0) (fun () ->
        let report = createAsync (fun _ _ -> Task.FromResult 1)
        report.Value)\n")
                        .Head.Range

                Expect.equal (range.StartLine, range.StartColumn, range.EndColumn) (6, 12, 18) "report"
            }

            test "an owning memo" {
                Expect.equal
                    (codes "let m () =\n    createMemoWith (fun _ ->\n        let a = createAsync (fun _ _ -> Task.FromResult 1)\n        a.Value)\n")
                    [ "RAN011" ]
                    "createMemoWith"
            }

            test "an effect" {
                Expect.equal
                    (codes
                        "let e () =\n    createEffect (fun () ->\n        let a = createAsync (fun _ _ -> Task.FromResult 1)\n        ignore a.Status)\n")
                    [ "RAN011" ]
                    "createEffect"
            }

            test "an error boundary and a boundary" {
                let body =
                    "let b () =\n    createErrorBoundary (fun _ _ -> 0) (fun () ->\n        let a = createAsync (fun _ _ -> Task.FromResult 1)\n        a.Value)\n"
                    + "let c () =\n    createBoundary (fun _ -> 0) (fun _ _ -> 0) (fun () ->\n        let a = createAsync (fun _ _ -> Task.FromResult 1)\n        a.Value)\n"

                Expect.equal (codes body) [ "RAN011"; "RAN011" ] "both boundaries"
            }

            test "createAsyncWith" {
                Expect.equal
                    (codes
                        "let w () =\n    createAsyncWith (fun _ _ ->\n        let inner = createAsync (fun _ _ -> Task.FromResult 1)\n        Task.FromResult inner.Value)\n")
                    [ "RAN011" ]
                    "owning async"
            }

            test "created outside and read inside" {
                let body =
                    fetch
                    + "let view () =\n    let report = fetch ()\n    createSuspense (fun _ -> 0) (fun () -> report.Value)\n"

                Expect.isEmpty (codes body) "the right shape"
            }

            test "created inside and never read there" {
                let body =
                    "let view () =\n    createSuspense (fun _ -> 0) (fun () ->\n        let report = createAsync (fun _ _ -> Task.FromResult 1)\n        ignore report\n        0)\n"

                Expect.isEmpty (codes body) "not read in the body"
            }

            test "created inside a pure createMemo is RAN010's" {
                Expect.isEmpty
                    (codes "let m () =\n    createMemo (fun _ ->\n        let a = createAsync (fun _ _ -> Task.FromResult 1)\n        a.Value)\n")
                    "pure memo"
            }

            test "read inside a nested stored callback" {
                let body =
                    "let view (handlers: ResizeArray<unit -> int>) =\n    createSuspense (fun _ -> 0) (fun () ->\n        let a = createAsync (fun _ _ -> Task.FromResult 1)\n        handlers.Add (fun () -> a.Value)\n        0)\n"

                Expect.isEmpty (codes body) "the read is not in the body's run"
            }
        ]
