module Ranvier.Analyzers.Tests.Ran002

open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private findings (source: string) =
    run Analyzers.ran002Cli (context (ranvier.Force ()) source)

/// The findings in the last of `files`, and that file's source with the first finding's fix applied, re-checked.
let private fixedIn (files: (string * string) list) =
    let messages = run Analyzers.ran002Cli (contextOf (ranvier.Force ()) files)
    let name, source = List.last files
    let repaired = applyFix source (messages.Head.Fixes |> List.exactlyOne)

    let after =
        run
            Analyzers.ran002Cli
            (contextOf
                (ranvier.Force ())
                (List.take (files.Length - 1) files
                 @ [ name, repaired ]))

    messages, repaired, after

let private policy =
    """
module Fixture
type Policy =
    | Queue
    | Drop of int
    member this.IsUserDefined = true
"""

[<Tests>]
let tests =
    testList
        "RAN002"
        [
            test "a tester of a case without fields, with its exact message" {
                let messages =
                    findings (
                        policy
                        + "let isQueue (policy: Policy) = policy.IsQueue\n"
                    )

                Expect.equal
                    (messages |> List.map _.Message)
                    [
                        "`policy.IsQueue` calls a compiler-generated union tester, which a Fable precompiled assembly does not declare. Write `(match policy with Policy.Queue -> true | _ -> false)`."
                    ]
                    "one message"
            }

            test "a tester of a case with fields" {
                let messages =
                    findings (
                        policy
                        + "let isDrop (policy: Policy) = policy.IsDrop\n"
                    )

                Expect.equal (messages |> List.map _.Code) [ "RAN002" ] "one finding"
                Expect.stringContains messages.Head.Message "Policy.Drop _ -> true" "the field wildcard"
            }

            test "a tester of a struct union" {
                let source =
                    "module Fixture\n[<Struct>]\ntype Flag =\n    | On\n    | Off\nlet on (flag: Flag) = flag.IsOn\n"

                Expect.equal (findings source |> List.map _.Code) [ "RAN002" ] "one finding"
            }

            test "a tester of a generic union" {
                let source =
                    "module Fixture\ntype Outcome<'T> =\n    | Done of 'T\n    | Failed of exn\nlet finished (o: Outcome<int>) = o.IsDone\n"

                Expect.equal (findings source |> List.map _.Code) [ "RAN002" ] "one finding"
            }

            test "a tester of an internal union" {
                let source =
                    "module Fixture\ntype internal Mode =\n    | Fast\n    | Slow\nlet internal fast (mode: Mode) = mode.IsFast\n"

                Expect.equal (findings source |> List.map _.Code) [ "RAN002" ] "one finding"
            }

            test "the regression seed of 2cf84be fires" {
                let source =
                    """
module Fixture
type FlightPolicy =
    | CancelPrevious
    | FinishCurrent
let trace (graph: obj, id: int, gen: int, owed: bool, finish: bool) = ()
let drop (policy: FlightPolicy) graph id gen owed = trace (graph, id, gen, owed, policy.IsFinishCurrent)
"""

                Expect.equal (findings source |> List.map _.Code) [ "RAN002" ] "Core.fs:4545 before 2cf84be"
            }

            test "a user-written IsX member is not a generated tester" {
                Expect.isEmpty
                    (findings (
                        policy
                        + "let custom (policy: Policy) = policy.IsUserDefined\n"
                    ))
                    "no finding"
            }

            test "Option.IsSome is not a generated tester" { Expect.isEmpty (findings "module Fixture\nlet some = (Some 1).IsSome\n") "no finding" }

            test "a tester of a union from a referenced assembly is out of scope" {
                Expect.isEmpty (findings "module Fixture\nlet queued (policy: Ranvier.FlightPolicy) = policy.IsQueue\n") "Ranvier's FlightPolicy"
            }

            test "a project without Fable.Core is not analyzed" {
                let source =
                    policy
                    + "let isQueue (policy: Policy) = policy.IsQueue\n"

                Expect.isEmpty (run Analyzers.ran002Cli (context (plain.Force ()) source)) "no Fable.Core reference"
            }

            test "the finding's range is the tester call" {
                let messages =
                    findings (
                        policy
                        + "let isQueue (policy: Policy) = policy.IsQueue\n"
                    )

                let range = messages.Head.Range
                Expect.equal (range.StartLine, range.StartColumn, range.EndColumn) (7, 31, 45) "policy.IsQueue"
            }

            test "a union in a nested module, used inside the module and from a sibling" {
                let source =
                    "module Fixture
module Inner =
    type Policy =
        | Queue
        | Drop of int
    let inside (p: Policy) = p.IsQueue
module Other =
    let outside (p: Inner.Policy) = p.IsDrop
"

                let messages = findings source
                Expect.equal messages.Length 2 "both testers"
                Expect.stringContains messages[0].Message "(match p with Policy.Queue -> true | _ -> false)" "inside"
                Expect.stringContains messages[1].Message "(match p with Inner.Policy.Drop _ -> true | _ -> false)" "sibling"

                let repaired =
                    messages
                    |> List.rev
                    |> List.fold (fun text m -> applyFix text m.Fixes.Head) source

                Expect.isEmpty (findings repaired) "both fixes compile and clear the findings"
            }

            test "a namespace-level union used in the same file, after a file that declares the same namespace" {
                let messages, repaired, after =
                    fixedIn
                        [
                            "Earlier.fs",
                            "namespace Probe.Ns
type Other =
    | A
    | B
"
                            "Fixture.fs",
                            "namespace Probe.Ns
type Mode =
    | Fast
    | Slow
module Use =
    let fast (m: Mode) = m.IsFast
"
                        ]

                Expect.stringContains messages.Head.Message "(match m with Mode.Fast -> true | _ -> false)" "namespace scope"
                Expect.stringContains repaired "Mode.Fast" "fixed"
                Expect.isEmpty after "the fix compiles and clears the finding"
            }

            test "a namespace-level union used from a sibling namespace" {
                let messages, _, after =
                    fixedIn
                        [
                            "Defs.fs",
                            "namespace Probe.Core
type Mode =
    | Fast
    | Slow
"
                            "Fixture.fs",
                            "namespace Probe.Use
module Check =
    let fast (m: Probe.Core.Mode) = m.IsFast
"
                        ]

                Expect.stringContains
                    messages.Head.Message
                    "(match m with Probe.Core.Mode.Fast -> true | _ -> false)"
                    "a namespace the code does not open"

                Expect.isEmpty after "the fix compiles and clears the finding"
            }

            test "a union declared in another file of the project" {
                let messages, _, after =
                    fixedIn
                        [
                            "Defs.fs",
                            "module Defs
type Policy =
    | Queue
    | Drop of int
"
                            "Fixture.fs",
                            "module Use
let queued (p: Defs.Policy) = p.IsQueue\n"
                        ]

                Expect.stringContains messages.Head.Message "(match p with Defs.Policy.Queue -> true | _ -> false)" "other file"
                Expect.isEmpty after "the fix compiles and clears the finding"
            }

            test "the _.IsCase shorthand, fixed with a function" {
                let source =
                    policy
                    + "let queued (ps: Policy list) = ps |> List.filter _.IsQueue\n"

                let messages = findings source

                Expect.equal
                    (messages |> List.map _.Message)
                    [
                        "`Policy.IsQueue` calls a compiler-generated union tester, which a Fable precompiled assembly does not declare. Write `(function Policy.Queue -> true | _ -> false)`."
                    ]
                    "the message names the union"

                let repaired = applyFix source messages.Head.Fixes.Head
                Expect.stringContains repaired "List.filter (function Policy.Queue -> true | _ -> false)" "a function"
                Expect.isEmpty (findings repaired) "the fix compiles and clears the finding"
            }

            test "a union in a companion module with the Module suffix, used outside and inside it" {
                let source =
                    "module Fixture
type Policy = { Name: string }
module Policy =
    type Kind =
        | Fast
        | Slow
    let slow (k: Kind) = k.IsSlow
let slowOutside (k: Policy.Kind) = k.IsSlow
"

                let messages = findings source
                Expect.equal messages.Length 2 "both testers"
                Expect.stringContains messages[0].Message "(match k with Kind.Slow -> true | _ -> false)" "inside"
                Expect.stringContains messages[1].Message "(match k with Policy.Kind.Slow -> true | _ -> false)" "outside"

                let repaired =
                    messages
                    |> List.rev
                    |> List.fold (fun text m -> applyFix text m.Fixes.Head) source

                Expect.isEmpty (findings repaired) "both fixes compile and clear the findings"
            }

            test "a union in an explicit ModuleSuffix module" {
                let source =
                    "module Fixture
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Shapes =
    type Kind =
        | Fast
        | Slow
    let slow (k: Kind) = k.IsSlow
let slowOutside (k: Shapes.Kind) = k.IsSlow
"

                let messages = findings source
                Expect.stringContains messages[1].Message "(match k with Shapes.Kind.Slow -> true | _ -> false)" "source name"

                let repaired =
                    messages
                    |> List.rev
                    |> List.fold (fun text m -> applyFix text m.Fixes.Head) source

                Expect.isEmpty (findings repaired) "both fixes compile and clear the findings"
            }

            test "the chained shorthand _.Inner.IsCase, fixed with a lambda" {
                let source =
                    policy
                    + "type Box = { Inner: Policy }
let queued (bs: Box list) = bs |> List.filter _.Inner.IsQueue\n"

                let messages = findings source
                Expect.stringContains messages.Head.Message "`Policy.IsQueue`" "the union's tester"
                let repaired = applyFix source messages.Head.Fixes.Head

                Expect.stringContains repaired "(fun value -> match value.Inner with Policy.Queue -> true | _ -> false)" "a lambda"

                Expect.isEmpty (findings repaired) "the fix compiles and clears the finding"
            }

            test "a parenthesised receiver keeps its parentheses in the message" {
                let messages =
                    findings (
                        policy
                        + "let first (ps: Policy list) = (List.head ps).IsQueue\n"
                    )

                Expect.stringContains messages.Head.Message "`(List.head ps).IsQueue`" "the call as written"
            }

            test "the compiler the tool manifest declares decides the rule" {
                let source =
                    policy
                    + "let isQueue (policy: Policy) = policy.IsQueue\n"

                let codesWith tools =
                    run Analyzers.ran002Cli (contextIn tools (ranvier.Force ()) [ "Fixture.fs", source ])
                    |> List.map _.Code

                Expect.equal (codesWith (Some (manifest "fable" "5.21.0"))) [ "RAN002" ] "Fable 5.21 drops the testers"
                Expect.isEmpty (codesWith (Some (manifest "fable" "5.22.0"))) "Fable 5.22 declares them"
                Expect.isEmpty (codesWith (Some (manifest "fickl" "0.3.0"))) "Fickl 0.3.0 declares them"
                Expect.isEmpty (codesWith None) "no signal that Fable compiles the project"

                Expect.equal
                    (run Analyzers.ran002Cli (contextIn None (optedIn (ranvier.Force ())) [ "Fixture.fs", source ])
                     |> List.map _.Code)
                    [ "RAN002" ]
                    "opted in, with no declared compiler"

                Expect.isEmpty
                    (run Analyzers.ran002Cli (contextIn (Some (manifest "fickl" "0.3.0")) (optedIn (ranvier.Force ())) [ "Fixture.fs", source ]))
                    "opted in, with a declared Fickl"

                Expect.equal (codesWith (Some (manifest "fable" "5.22.0-alpha.1"))) [ "RAN002" ] "a prerelease of 5.22 precedes the fix"

                Expect.isEmpty
                    (codesWith (Some "// comment\n{ \"tools\": { \"Fable\": { \"version\": \"5.22.0\", }, }, }"))
                    "comments, trailing commas and key case are read"
            }

            test "a nested manifest without a compiler defers to the root manifest" {
                let source =
                    policy
                    + "let isQueue (policy: Policy) = policy.IsQueue\n"

                let codesIn innerIsRoot =
                    let inner =
                        $"{{ \"version\": 1, \"isRoot\": {innerIsRoot}, \"tools\": {{ \"paket\": {{ \"version\": \"9.0.0\" }} }} }}"

                    run
                        Analyzers.ran002Cli
                        (contextLayout [ "", manifest "fickl" "0.3.0"; "sub", inner ] "sub" (ranvier.Force ()) [ "Fixture.fs", source ])
                    |> List.map _.Code

                Expect.isEmpty (codesIn "false") "the root manifest declares Fickl"
                Expect.isEmpty (codesIn "true") "a root manifest without a compiler ends the search"
            }

            test "an edited tool manifest takes effect in the same process" {
                let source =
                    policy
                    + "let isQueue (policy: Policy) = policy.IsQueue\n"

                let context =
                    contextIn (Some (manifest "fable" "5.21.0")) (ranvier.Force ()) [ "Fixture.fs", source ]

                Expect.equal (run Analyzers.ran002Cli context |> List.map _.Code) [ "RAN002" ] "Fable 5.21"

                let file =
                    System.IO.Path.Combine (System.IO.Path.GetDirectoryName context.ProjectOptions.ProjectFileName, ".config", "dotnet-tools.json")

                System.IO.File.WriteAllText (file, manifest "fickl" "0.3.0")
                System.IO.File.SetLastWriteTimeUtc (file, System.DateTime.UtcNow.AddMinutes 1.0)
                Expect.isEmpty (run Analyzers.ran002Cli context) "the manifest now declares Fickl"
            }

            test "a tester inside a quotation is reported" {
                Expect.equal
                    (findings (
                        policy
                        + "let quoted = <@ fun (p: Policy) -> p.IsDrop @>
"
                     )
                     |> List.map _.Code)
                    [ "RAN002" ]
                    "pinned: testers in quotations are reported"
            }

            test "the editor entry reports the tester, and an ignore-file comment silences it" {
                let cli =
                    context
                        (ranvier.Force ())
                        (policy
                         + "let isQueue (policy: Policy) = policy.IsQueue\n")

                let editor (ignores: Map<string, FSharp.Analyzers.SDK.AnalyzerIgnoreRange list>) : FSharp.Analyzers.SDK.EditorContext =
                    {
                        FileName = cli.FileName
                        SourceText = cli.SourceText
                        ParseFileResults = cli.ParseFileResults
                        CheckFileResults = Some cli.CheckFileResults
                        TypedTree = cli.TypedTree
                        CheckProjectResults = Some cli.CheckProjectResults
                        ProjectOptions = cli.ProjectOptions
                        AnalyzerIgnoreRanges = ignores
                    }

                let editorRun context =
                    Analyzers.ran002Editor context
                    |> Async.RunSynchronously

                Expect.equal (editorRun (editor Map.empty) |> List.map _.Code) [ "RAN002" ] "reported"

                Expect.isEmpty (editorRun (editor (Map [ "RAN002", [ FSharp.Analyzers.SDK.AnalyzerIgnoreRange.File ] ]))) "ignore-file"
            }

            test "the fix type-checks and clears the finding" {
                let source =
                    policy
                    + "let isDrop (policy: Policy) = policy.IsDrop\n"

                let fix = (findings source).Head.Fixes |> List.exactlyOne
                let repaired = applyFix source fix
                Expect.stringContains repaired "(match policy with Policy.Drop _ -> true | _ -> false)" "the match"
                Expect.isEmpty (findings repaired) "no finding after the fix"
            }
        ]
