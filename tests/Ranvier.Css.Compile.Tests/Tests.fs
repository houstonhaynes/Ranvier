module Ranvier.Css.Compile.Tests

open System
open System.IO
open Expecto
open Ranvier.Css.Build
open Ranvier.Css.Compile

let private repository =
    Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, "../.."))

let private fixture = Path.Combine (__SOURCE_DIRECTORY__, "Fixtures/App/App.fsproj")

let private extract options project =
    match Extractor.extract options project with
    | Ok result -> result
    | Error errors ->
        failtest (
            errors
            |> List.map Diagnostic.format
            |> String.concat "\n"
        )

let private temporary (source: string) action =
    let directory =
        Path.Combine (__SOURCE_DIRECTORY__, "out", Guid.NewGuid().ToString("N"))

    Directory.CreateDirectory directory |> ignore
    let project = Path.Combine (directory, "Fixture.fsproj")
    let core = Path.Combine (repository, "src/Ranvier.Css/Ranvier.Css.fsproj")
    let view = Path.Combine (repository, "src/Ranvier.Css.View/Ranvier.Css.View.fsproj")

    let bindings =
        Path.Combine (repository, "src/Ranvier.Css.Bindings/bin/Release/net10.0/Ranvier.Css.Bindings.dll")

    File.WriteAllText (
        project,
        $"""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include="{core}"/><ProjectReference Include="{view}"/><Reference Include="Ranvier.Css.Bindings"><HintPath>{bindings}</HintPath></Reference><Compile Include="Fixture.fs"/></ItemGroup></Project>"""
    )

    File.WriteAllText (Path.Combine (directory, "Fixture.fs"), source)
    let start = System.Diagnostics.ProcessStartInfo ("dotnet")
    start.ArgumentList.Add "restore"
    start.ArgumentList.Add "-nr:false"
    start.ArgumentList.Add project
    start.RedirectStandardOutput <- true
    start.RedirectStandardError <- true
    start.Environment["MSBUILDDISABLENODEREUSE"] <- "1"
    start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] <- "1"
    use child = System.Diagnostics.Process.Start start
    let output = child.StandardOutput.ReadToEndAsync ()
    let errors = child.StandardError.ReadToEndAsync ()
    child.WaitForExit ()

    if child.ExitCode <> 0 then
        failtest (output.Result + errors.Result)

    action project

[<Tests>]
/// <summary>Checks source discovery, deterministic lowering, side-effect isolation and evaluation diagnostics.</summary>
let tests =
    testSequenced
    <| testList
        "CSS source extraction"
        [
            testCase "extracts referenced helpers and retains every application alternative without executing initializers"
            <| fun _ ->
                let result = extract ExtractionOptions.defaults fixture

                let css =
                    result.Assets
                    |> List.map _.Content
                    |> String.concat "\n"

                Expect.stringContains css "font-size: 1rem" "Referenced library utility is reachable."
                Expect.stringContains css "margin-left: 0.5rem" "Finite helpers are evaluated."
                Expect.stringContains css "min-width: 48rem" "Responsive conditions are retained."
                Expect.stringContains css ":hover" "Nested condition is retained."
                Expect.stringContains css "opacity: var(" "Reactive values become static variable slots."
                Expect.stringContains css ":where(" "All reactive alternatives are precompiled."
                Expect.isFalse (css.Contains "unused-never-emitted") "Unused utilities are removed."

                Expect.isFalse
                    (File.Exists (Path.Combine (Environment.CurrentDirectory, "app-executed.sentinel")))
                    "Application initialization never runs."

                Expect.isFalse
                    (File.Exists (Path.Combine (Environment.CurrentDirectory, "library-executed.sentinel")))
                    "Library initialization never runs."

                let declaration name value =
                    Ranvier.Css.Style.declarations [ Ranvier.Css.Declaration.create name value ]

                let expected =
                    Ranvier.Css.Style.create
                        [
                            declaration "box-sizing" "border-box"
                            declaration "font-size" "1rem"
                            Ranvier.Css.Style.media "(min-width: 48rem)" [ declaration "font-size" "1.125rem" ]
                            declaration "margin-left" "0.25rem"
                            declaration "margin-left" "0.5rem"
                            declaration "width" "100%"
                            Ranvier.Css.Style.declarations
                                [
                                    Ranvier.Css.Declaration.create "display" "-webkit-box"
                                    Ranvier.Css.Declaration.create "display" "flex"
                                ]
                            Ranvier.Css.Style.media "(min-width: 48rem)" [ Ranvier.Css.Style.hover [ declaration "padding-left" "2rem" ] ]
                            Ranvier.Css.Style.whenActive
                                (fun () -> true)
                                [
                                    Ranvier.Css.Style.dynamic "opacity" (fun () -> failwith "Never evaluate runtime readers.")
                                ]
                        ]

                Expect.stringContains css ("." + expected.Definition.ClassName) "Extraction uses exactly the runtime's composition identity."
            testCase "repeated extraction is byte deterministic"
            <| fun _ ->
                let first = extract ExtractionOptions.defaults fixture
                let second = extract ExtractionOptions.defaults fixture
                Expect.equal second first "Source discovery and class identities are deterministic."
            testCase "supports single file and CSS module asset modes"
            <| fun _ ->
                for mode in [ OutputMode.SingleFile "app.css"; OutputMode.CssModules ] do
                    let result =
                        extract
                            { ExtractionOptions.defaults with
                                Assets = { Options.defaults with Mode = mode }
                            }
                            fixture

                    Expect.isNonEmpty result.Assets "Existing asset modes are used."
            testCase "used I/O returns a source diagnostic"
            <| fun _ ->
                temporary
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet live = utility { Declaration.create \"color\" (System.IO.File.ReadAllText \"missing\") }\nlet view () = styles { live }\n"
                <| fun project ->
                    match Extractor.extract ExtractionOptions.defaults project with
                    | Ok _ -> failtest "I/O cannot be interpreted."
                    | Error errors ->
                        Expect.stringContains errors.Head.Message "ReadAllText" "The unsupported operation is named."
                        Expect.equal errors.Head.Line 4 "The expression has its source line."
            testCase "numeric specification guards remain enforced"
            <| fun _ ->
                temporary
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet invalid = utility { Css.width (Css.px -1.) }\nlet view () = styles { invalid }\n"
                <| fun project ->
                    match Extractor.extract ExtractionOptions.defaults project with
                    | Ok _ -> failtest "Negative width must fail the generated numeric guard."
                    | Error errors -> Expect.stringContains errors.Head.Message "range" "Canonical numeric guards run."
            testCase "unbounded recursion is diagnosed"
            <| fun _ ->
                temporary
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet rec forever value = forever value\nlet invalid = utility { Declaration.create \"width\" (forever \"x\") }\nlet view () = styles { invalid }\n"
                <| fun project ->
                    match Extractor.extract ExtractionOptions.defaults project with
                    | Ok _ -> failtest "Recursive helpers must be bounded."
                    | Error errors -> Expect.stringContains errors.Head.Message "depth" "The recursion guard prevents nontermination."
            testCase "mutable state is never sampled"
            <| fun _ ->
                temporary
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet mutable size = 1.\nlet invalid = utility { Css.width (Css.px size) }\nlet view () = styles { invalid }\n"
                <| fun project ->
                    match Extractor.extract ExtractionOptions.defaults project with
                    | Ok _ -> failtest "Mutable initial values cannot stand in for application state."
                    | Error errors -> Expect.stringContains errors.Head.Message "Mutable" "Mutable inputs are identified."
            testCase "static conditional empty fragments and utility parameters compile"
            <| fun _ ->
                temporary
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet baseStyle = utility { Css.width (Css.px 12.) }\nlet wrap (selected: bool) (item: Utility) = utility { if selected then item }\nlet composed = wrap true baseStyle\nlet view () = styles { composed }\n"
                <| fun project ->
                    let result = extract ExtractionOptions.defaults project
                    Expect.stringContains result.Assets.Head.Content "width: 12px" "Source helpers accept utilities and fixed control flow."
            testCase "oversized finite loops produce bounded source diagnostics"
            <| fun _ ->
                temporary
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet invalid = utility { for index in [1 .. 10001] do Css.marginLeft (Css.px (float index)) }\nlet view () = styles { invalid }\n"
                <| fun project ->
                    match Extractor.extract ExtractionOptions.defaults project with
                    | Ok _ -> failtest "Loop bounds must prevent excessive static work."
                    | Error errors -> Expect.stringContains errors.Head.Message "bound" "Finite bounds are enforced."
            testCase "CLI failures leave prior assets untouched"
            <| fun _ ->
                temporary
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet invalid = utility { Css.width (Css.px -1.) }\nlet view () = styles { invalid }\n"
                <| fun project ->
                    let output = Path.Combine (Path.GetDirectoryName project, "assets")
                    Directory.CreateDirectory output |> ignore
                    let sentinel = Path.Combine (output, "app.css")
                    File.WriteAllText (sentinel, "prior CSS")

                    let result =
                        Program.main [| "--project"; project; "--output"; output; "--single"; "app.css" |]

                    Expect.equal result 1 "Extraction errors fail the command."
                    Expect.equal (File.ReadAllText sentinel) "prior CSS" "No output is written until validation succeeds."
            testCase "float to integer helpers preserve F# truncation"
            <| fun _ ->
                temporary
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet trunc value = float (int value)\nlet example = utility { for value in [1.8; -1.8] do Css.marginLeft (Css.px (trunc value)) }\nlet view () = styles { example }\n"
                <| fun project ->
                    let result = extract ExtractionOptions.defaults project
                    let css = result.Assets.Head.Content
                    Expect.stringContains css "margin-left: 1px" "Positive conversion truncates."
                    Expect.stringContains css "margin-left: -1px" "Negative conversion truncates toward zero."
            testCase "float ranges are diagnosed rather than rounded"
            <| fun _ ->
                temporary
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet example = utility { for value in [1.5 .. 2.5] do Css.marginLeft (Css.px value) }\nlet view () = styles { example }\n"
                <| fun project ->
                    match Extractor.extract ExtractionOptions.defaults project with
                    | Ok _ -> failtest "Only supported int32 ranges may be interpreted."
                    | Error errors -> Expect.stringContains errors.Head.Message "int32" "Non-integer range semantics are explicit."
            testCase "zero primitives and keywords retain their serialized values"
            <| fun _ ->
                temporary
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet breakpoint = minViewportWidth Css.zero\nlet example = utility { Css.opacity (Css.number 0.); breakpoint { Css.width Css.auto } }\nlet view () = styles { example }\n"
                <| fun project ->
                    let result = extract ExtractionOptions.defaults project
                    Expect.stringContains result.Assets.Head.Content "opacity: 0" "Zero numeric tokens are serialized correctly."
                    Expect.stringContains result.Assets.Head.Content "min-width: 0" "The zero production retains its literal token."
                    Expect.stringContains result.Assets.Head.Content "width: auto" "Generated keyword tokens are serialized correctly."
            testCase "root framework selection preserves referenced library targets"
            <| fun _ ->
                let result =
                    extract
                        { ExtractionOptions.defaults with
                            Framework = Some "net10.0"
                        }
                        fixture

                let css =
                    result.Assets
                    |> List.map _.Content
                    |> String.concat "\n"

                Expect.stringContains css "box-sizing: border-box" "The net8 library retains its evaluated target when the root selects net10."
        ]

[<EntryPoint>]
/// <summary>Runs the complete dedicated source-extraction suite.</summary>
let main arguments =
    runTestsWithCLIArgs [] arguments tests
