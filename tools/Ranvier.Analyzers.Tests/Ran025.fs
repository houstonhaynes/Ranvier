module Ranvier.Analyzers.Tests.Ran025

open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private header =
    "module Fixture\nopen Ranvier\nopen Ranvier.View\nopen Ranvier.View.Dom\nopen Ranvier.View.Dom.Scheduling\nopen Ranvier.View.Dom.Hydration\nlet hydrated graph host definition (failure: CommitFailure) (post: (unit -> unit) -> unit) =\n"

let private findings (body: string) =
    run Analyzers.ran025Cli (context (ranvier.Force ()) (header + body))

let private codes body =
    findings body |> List.map _.Code

let private call (mode: string) =
    $"    hydrateWith ({mode}) graph None ignore HydrationOptions.defaults host definition ()\n"

[<Tests>]
let tests =
    testList
        "RAN025"
        [
            test "a QueuedCommits construction, with its exact message" {
                Expect.equal
                    (findings (call "QueuedCommits (failure, post)")
                     |> List.map _.Message)
                    [
                        "Hydration supports synchronous commits only, so this call raises. Pass `SynchronousCommits`, or call `hydrate`."
                    ]
                    "one message"
            }

            test "the finding's range is the mode argument" {
                let body = call "QueuedCommits (failure, post)"
                let range = (findings body).Head.Range
                Expect.equal (textAt (header + body) range) "QueuedCommits (failure, post)" "the mode"
            }

            test "Microtask.mode" { Expect.equal (codes (call "Microtask.mode failure")) [ "RAN025" ] "Microtask.fs:16" }

            test "a let-bound queued mode" {
                Expect.equal
                    (codes (
                        "    let mode = QueuedCommits (failure, post)\n"
                        + call "mode"
                    ))
                    [ "RAN025" ]
                    "let-bound"
            }

            test "a project that compiles the view from source is analyzed" {
                let view =
                    "namespace Ranvier.View.Dom\nmodule Scheduling =
    type CommitMode =
        | SynchronousCommits
        | QueuedCommits of int
module Hydration =
    let hydrateWith (mode: Scheduling.CommitMode) (props: unit) = ignore mode
"

                let caller =
                    "module Fixture
open Ranvier.View.Dom
let h () = Hydration.hydrateWith (Scheduling.QueuedCommits 1) ()
"

                Expect.equal
                    (run Analyzers.ran025Cli (contextOf (plain.Force ()) [ "View.fs", view; "Fixture.fs", caller ])
                     |> List.map _.Code)
                    [ "RAN025" ]
                    "no Ranvier.View.Dom assembly reference"
            }

            test "a module-level queued mode" {
                let body =
                    "    ()\nlet browserMode: CommitMode = Microtask.mode CollectAndContinue\nlet other graph host definition =\n    hydrateWith browserMode graph None ignore HydrationOptions.defaults host definition ()\n"

                Expect.equal (codes body) [ "RAN025" ] "module value"
            }

            test "a same-file function returning QueuedCommits" {
                let body =
                    "    ()\nlet queued failure = QueuedCommits (failure, ignore)\nlet other graph host definition =\n    hydrateWith (queued CollectAndContinue) graph None ignore HydrationOptions.defaults host definition ()\n"

                Expect.equal (codes body) [ "RAN025" ] "same-file helper"
            }

            test "a conditional mode with a queued branch" {
                let body =
                    "    ()\nlet other (browser: bool) graph host definition =\n    let mode = if browser then Microtask.mode CollectAndContinue else SynchronousCommits\n    hydrateWith mode graph None ignore HydrationOptions.defaults host definition ()\n"

                Expect.equal (codes body) [ "RAN025" ] "either branch"
            }

            test "the piped form's range is the mode argument" {
                let body =
                    "    () |> hydrateWith (QueuedCommits (failure, post)) graph None ignore HydrationOptions.defaults host definition\n"

                let range = (findings body).Head.Range
                Expect.equal (textAt (header + body) range) "QueuedCommits (failure, post)" "the mode"
            }

            test "a same-file function returning SynchronousCommits" {
                let body =
                    "    ()\nlet synchronous () = SynchronousCommits\nlet other graph host definition =\n    hydrateWith (synchronous ()) graph None ignore HydrationOptions.defaults host definition ()\n"

                Expect.isEmpty (codes body) "synchronous"
            }

            test "a same-file function whose argument chooses the synchronous branch" {
                let body =
                    "    ()\nlet modeFor hydrating failure = if hydrating then SynchronousCommits else Microtask.mode failure\nlet other graph host definition =\n    hydrateWith (modeFor true CollectAndContinue) graph None ignore HydrationOptions.defaults host definition ()\n"

                Expect.isEmpty (codes body) "the arguments choose the branch"
            }

            test "SynchronousCommits" { Expect.isEmpty (codes (call "SynchronousCommits")) "supported" }

            test "a mode parameter" {
                let body =
                    "    ()\nlet other (mode: CommitMode) graph host definition =\n    hydrateWith mode graph None ignore HydrationOptions.defaults host definition ()\n"

                Expect.isEmpty (codes body) "unknown mode"
            }

            test "hydrate" {
                Expect.isEmpty (codes "    hydrate graph None ignore HydrationOptions.defaults host definition ()\n") "synchronous by definition"
            }
        ]
