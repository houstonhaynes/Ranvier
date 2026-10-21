module Ranvier.Analyzers.Tests.Ran015

open Expecto
open FSharp.Analyzers.SDK
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private findings (body: string) =
    let source =
        "module Fixture\nopen System.Threading.Tasks\nopen Ranvier\n"
        + body

    run Analyzers.ran015Cli (context (ranvier.Force ()) source)

[<Tests>]
let tests =
    testList
        "RAN015"
        [
            test "a wildcard token, with its exact message and Hint severity" {
                let messages =
                    findings "let a () = createAsyncWithPolicy FlightPolicy.CancelPrevious (fun _ _ -> Task.FromResult 1)\n"

                Expect.equal
                    (messages
                     |> List.map (fun m -> m.Severity, m.Message))
                    [
                        Severity.Hint,
                        "This async memo's body ignores its CancellationToken, so `CancelPrevious` cancels nothing and acts as `KeepLatest`. Pass the token to the awaited work, or use `KeepLatest`."
                    ]
                    "one hint"
            }

            test "a named token that is never used" {
                Expect.equal
                    (findings "let a () = createAsyncWithPolicy FlightPolicy.CancelPrevious (fun _ token -> Task.FromResult 1)\n"
                     |> List.map _.Code)
                    [ "RAN015" ]
                    "unused"
            }

            test "the finding's range is the token parameter" {
                let source =
                    "let a () = createAsyncWithPolicy FlightPolicy.CancelPrevious (fun _ token -> Task.FromResult 1)\n"

                let range = (findings source).Head.Range

                Expect.equal
                    (textAt
                        ("module Fixture\nopen System.Threading.Tasks\nopen Ranvier\n"
                         + source)
                        range)
                    "token"
                    "the token"
            }

            test "the owning form" {
                Expect.equal
                    (findings "let a () = createOwningAsyncWithPolicy FlightPolicy.CancelPrevious (fun _ _ -> Task.FromResult 1)\n"
                     |> List.map _.Code)
                    [ "RAN015" ]
                    "owning"
            }

            test "a token passed to the work" {
                Expect.isEmpty
                    (findings
                        "let a () = createAsyncWithPolicy FlightPolicy.CancelPrevious (fun _ token -> (Task.Delay (10, token)).ContinueWith (fun (_: Task) -> 1))\n")
                    "used"
            }

            test "KeepLatest" {
                Expect.isEmpty
                    (findings "let a () = createAsyncWithPolicy FlightPolicy.KeepLatest (fun _ _ -> Task.FromResult 1)\n")
                    "no cancellation promised"
            }

            test "a policy parameter" {
                Expect.isEmpty
                    (findings "let a (policy: FlightPolicy) = createAsyncWithPolicy policy (fun _ _ -> Task.FromResult 1)\n")
                    "unknown policy"
            }
        ]
