module Ranvier.Analyzers.Tests.Ran001

open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private loops =
    "module Loops\nlet mutable childSetups = 0\nlet setup () = childSetups <- childSetups + 1\n"

let private findingsWith options (probe: string) =
    run Analyzers.ran001Cli (contextOf options [ "Loops.fs", loops; "Probe.fs", probe ])

let private findings (probe: string) =
    findingsWith (ranvier.Force ()) probe

let private snapshot =
    "module Probe\nlet snapshot () =\n    let setups = Loops.childSetups\n    Loops.setup ()\n    Loops.childSetups - setups\n"

[<Tests>]
let tests =
    testList
        "RAN001"
        [
            test "a single-use snapshot in another file, with its exact message" {
                Expect.equal
                    (findings snapshot |> List.map _.Message)
                    [
                        "Fable Release compiles `setups` as a second read of `Loops.childSetups` at its single use (line 5), so writes between the two lines change it. Declare it `let mutable setups` to keep the snapshot."
                    ]
                    "one message"
            }

            test "the use inside a closure" {
                let probe =
                    "module Probe\nlet closure () =\n    let setups = Loops.childSetups\n    Loops.setup ()\n    fun () -> Loops.childSetups - setups\n"

                Expect.equal (findings probe |> List.map _.Code) [ "RAN001" ] "one finding"
            }

            test "a tuple pattern, without a fix" {
                let probe =
                    "module Probe\nlet tupled () =\n    let a, b = Loops.childSetups, 1\n    Loops.setup ()\n    Loops.childSetups - a + b\n"

                let messages = findings probe
                Expect.equal (messages |> List.map _.Code) [ "RAN001" ] "one finding"
                Expect.isEmpty messages.Head.Fixes "no fix for a tuple pattern"
            }

            test "the regression seed of ElmishNestedChecks.fs:29-30 fires" {
                let probe =
                    "module Probe\nlet loops () =\n    let setups = Loops.childSetups\n    let target = obj ()\n    Loops.setup ()\n    target, Loops.childSetups - setups\n"

                Expect.equal (findings probe |> List.map _.Code) [ "RAN001" ] "let mutable changed to let"
            }

            test "a mutable of the same file is not inlined" {
                let probe =
                    "module Probe\nlet mutable count = 0\nlet snapshot () =\n    let seen = count\n    count <- 1\n    count - seen\n"

                Expect.isEmpty (findings probe) "same file"
            }

            test "two uses are not inlined" {
                let probe =
                    "module Probe\nlet twice () =\n    let setups = Loops.childSetups\n    Loops.setup ()\n    setups + (Loops.childSetups - setups)\n"

                Expect.isEmpty (findings probe) "two uses"
            }

            test "a let mutable snapshot is kept" { Expect.isEmpty (findings (snapshot.Replace ("let setups", "let mutable setups"))) "let mutable" }

            test "a wrapped read is kept" { Expect.isEmpty (findings (snapshot.Replace ("= Loops.childSetups", "= Loops.childSetups + 0"))) "+ 0" }

            test "a mutable record field is kept" {
                let records =
                    "module Loops\ntype Counter = { mutable Count: int }\nlet state = { Count = 0 }\nlet setup () = state.Count <- state.Count + 1\n"

                let probe =
                    "module Probe\nlet field () =\n    let seen = Loops.state.Count\n    Loops.setup ()\n    Loops.state.Count - seen\n"

                Expect.isEmpty (run Analyzers.ran001Cli (contextOf (ranvier.Force ()) [ "Loops.fs", records; "Probe.fs", probe ])) "record field"
            }

            test "a project without Fable.Core is not analyzed" { Expect.isEmpty (findingsWith (plain.Force ()) snapshot) "no Fable.Core reference" }

            test "the finding's range is the binding identifier" {
                let range = (findings snapshot).Head.Range
                Expect.equal (range.StartLine, range.StartColumn, range.EndColumn) (3, 8, 14) "setups"
            }

            test "a parenthesised pattern is reported without a fix" {
                let probe =
                    snapshot.Replace ("let setups = Loops.childSetups", "let (setups: int) = Loops.childSetups")

                let messages = findings probe
                Expect.equal (messages |> List.map _.Code) [ "RAN001" ] "one finding"
                Expect.isEmpty messages.Head.Fixes "no fix inside parentheses"
            }

            test "a value bound by a match case is not reported" {
                let probe =
                    "module Probe
let matched () =
    match Loops.childSetups with
    | seen ->
        Loops.setup ()
        Loops.childSetups - seen
"

                Expect.isEmpty (findings probe) "no let binds it"
            }

            test "the rule runs whatever compiler the manifest declares" {
                let codesWith tools =
                    run Analyzers.ran001Cli (contextIn tools (ranvier.Force ()) [ "Loops.fs", loops; "Probe.fs", snapshot ])
                    |> List.map _.Code

                Expect.equal (codesWith (Some (manifest "fickl" "0.3.0"))) [ "RAN001" ] "Fickl 0.3.0 inlines"
                Expect.equal (codesWith (Some (manifest "fable" "5.22.0"))) [ "RAN001" ] "Fable 5.22 inlines"
                Expect.isEmpty (codesWith None) "no signal that Fable compiles the project"
            }

            test "an opt-in define or a direct Fable package reference enables the rule without a manifest" {
                let files = [ "Loops.fs", loops; "Probe.fs", snapshot ]

                Expect.equal
                    (run Analyzers.ran001Cli (contextIn None (optedIn (ranvier.Force ())) files)
                     |> List.map _.Code)
                    [ "RAN001" ]
                    "RanvierFableAnalyzers=true"

                Expect.equal
                    (run Analyzers.ran001Cli (writeProjectFile (contextIn None (ranvier.Force ()) files) "Fable.Core")
                     |> List.map _.Code)
                    [ "RAN001" ]
                    "a direct Fable.Core reference"

                Expect.isEmpty
                    (run Analyzers.ran001Cli (writeProjectFile (contextIn None (ranvier.Force ()) files) "Ranvier"))
                    "Ranvier alone brings Fable.Core transitively"
            }

            test "the fix type-checks and clears the finding" {
                let fix = (findings snapshot).Head.Fixes |> List.exactlyOne
                let repaired = applyFix snapshot fix
                Expect.stringContains repaired "let mutable setups = Loops.childSetups" "mutable inserted"
                Expect.isEmpty (findings repaired) "no finding after the fix"
            }
        ]
