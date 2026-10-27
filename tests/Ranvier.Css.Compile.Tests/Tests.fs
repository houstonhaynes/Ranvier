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
            testCase "calculation operators match runtime output in every asset mode without sampling readers"
            <| fun _ ->
                temporary
                    """module Fixture
open Ranvier.Css
open Ranvier.Css.View
let spacing value = utility { Css.width (Css.rem value + Css.px 8.) }
let example = utility {
    spacing 2.
    Css.height (Css.percent 100. - Css.rem 2.)
    Css.opacity (Css.rem 4. / Css.rem 8.)
    Css.zIndex (Css.number 2. + Css.number 1.)
    Css.marginLeft (-(Css.px 4. * (Css.number 2. + Css.number 1.)))
    Css.paddingLeft (2. * Css.rem 1. / 2.)
    Css.width (Css.lengthPercent 50. * Css.rem 4. / Css.rem 2.)
    Css.width (Css.min (Css.px 2., Css.px 3.))
    Css.height (Css.clamp (Css.px 4., Some (Css.px 2.), None))
    Css.paddingLeft (Css.round (Css.px 3., Css.px 2., strategy = Ranvier.Css.Spec.RoundingStrategy.Up))
    Css.marginLeft (Css.roundLineWidth (Css.px 1.2))
    Css.opacity (Css.sin Css.pi)
    Css.opacity (Css.progress (Css.px 1., Css.px 0., Css.px 2., noClamp = true))
    Css.width (Css.random (Css.px 1., Css.px 2., key = CalculationRandomKey.Fixed 0.5))
    Css.width (Css.calcMix [ Css.px 1., Some 50.; Css.px 3., None ])
    Css.zIndex (Css.siblingIndex ())
    Css.width (Css.calcInterpolate (Css.interpolationProgress (Css.number 0.5), [
        Css.interpolationStop (Css.interpolationPosition (Css.number 0.), Css.px 10.)
        Css.interpolationStop (Css.interpolationPosition (Css.number 1.), Css.px 20.)
    ], easing = CalculationEasing.Keyword "ease"))
    Css.width (Css.calcSize (Css.intrinsicKeyword "auto", Css.intrinsicSize + Css.px 2.))
    Css.width (Css.calcSize (Css.intrinsicFitContent (Css.px 20.), Css.intrinsicSize + Css.px 2.))
    Css.width (Css.calcSize (Css.intrinsicStretch (Css.px 20.), Css.intrinsicSize + Css.px 2.))
    Css.width (fun (_: bool) -> if true then failwith "Reader executed during extraction" else Css.rem 3. + Css.px 1.)
}
let view () = styles { example }
do failwith "Initializer executed during extraction"
"""
                <| fun project ->
                    let literal production unitName value =
                        Ranvier.Css.CalculationRuntime.literal production unitName value

                    let binary operation left right =
                        Ranvier.Css.CalculationRuntime.binary operation left right

                    let call name values =
                        Ranvier.Css.CalculationRuntime.functionCall name values

                    let arguments name values =
                        Ranvier.Css.CalculationRuntime.functionArguments name values

                    let arg value =
                        Ranvier.Css.CalculationArgument.Value value

                    let px value =
                        literal "length" "px" value

                    let length =
                        Ranvier.Css.Spec.NumericSemantics.dimension Ranvier.Css.Spec.NumericDimension.Length

                    let number = Ranvier.Css.Spec.NumericSemantics.number
                    let context = Ranvier.Css.Spec.NumericSemantics.dimensionOnlyContext

                    let lengthContext =
                        { context with
                            PercentagesAllowed = true
                            PercentageBasis = Some Ranvier.Css.Spec.NumericDimension.Length
                        }

                    let declaration name context expected value =
                        Ranvier.Css.Style.declarations [ Ranvier.Css.CalculationRuntime.declaration name context expected value ]

                    let expected =
                        Ranvier.Css.Style.create
                            [
                                declaration "width" lengthContext length (binary "+" (literal "length" "rem" 2.) (literal "length" "px" 8.))
                                declaration "height" lengthContext length (binary "-" (literal "percentage" "%" 100.) (literal "length" "rem" 2.))
                                declaration "opacity" context number (binary "/" (literal "length" "rem" 4.) (literal "length" "rem" 8.))
                                declaration "z-index" context number (binary "+" (literal "integer" "" 2.) (literal "integer" "" 1.))
                                declaration
                                    "margin-left"
                                    lengthContext
                                    length
                                    (Ranvier.Css.CalculationRuntime.negate (
                                        binary "*" (literal "length" "px" 4.) (binary "+" (literal "number" "" 2.) (literal "number" "" 1.))
                                    ))
                                declaration
                                    "padding-left"
                                    lengthContext
                                    length
                                    (binary "/" (binary "*" (literal "number" "" 2.) (literal "length" "rem" 1.)) (literal "number" "" 2.))
                                declaration
                                    "width"
                                    lengthContext
                                    length
                                    (binary
                                        "/"
                                        (binary "*" (literal "length-percentage" "%" 50.) (literal "length" "rem" 4.))
                                        (literal "length" "rem" 2.))
                                declaration "width" lengthContext length (call "min" [ px 2.; px 3. ])
                                declaration
                                    "height"
                                    lengthContext
                                    length
                                    (arguments "clamp" [ arg (px 2.); arg (px 4.); Ranvier.Css.CalculationArgument.NoneBound ])
                                declaration
                                    "padding-left"
                                    lengthContext
                                    length
                                    (arguments
                                        "round"
                                        [
                                            Ranvier.Css.CalculationArgument.Strategy Ranvier.Css.Spec.RoundingStrategy.Up
                                            arg (px 3.)
                                            arg (px 2.)
                                        ])
                                declaration
                                    "margin-left"
                                    lengthContext
                                    length
                                    (arguments
                                        "round"
                                        [
                                            Ranvier.Css.CalculationArgument.Strategy Ranvier.Css.Spec.RoundingStrategy.LineWidth
                                            arg (px 1.2)
                                        ])
                                declaration "opacity" context number (call "sin" [ Ranvier.Css.CalculationRuntime.constant "pi" ])
                                declaration
                                    "opacity"
                                    context
                                    number
                                    (arguments
                                        "progress"
                                        [
                                            Ranvier.Css.CalculationArgument.NoClamp
                                            arg (px 1.)
                                            arg (px 0.)
                                            arg (px 2.)
                                        ])
                                declaration
                                    "width"
                                    lengthContext
                                    length
                                    (arguments
                                        "random"
                                        [
                                            Ranvier.Css.CalculationArgument.RandomKey (Ranvier.Css.CalculationRandomKey.Fixed 0.5)
                                            arg (px 1.)
                                            arg (px 2.)
                                        ])
                                declaration
                                    "width"
                                    lengthContext
                                    length
                                    (arguments
                                        "calc-mix"
                                        [
                                            Ranvier.Css.CalculationArgument.WeightedValue (px 1., Some 50.)
                                            Ranvier.Css.CalculationArgument.WeightedValue (px 3., None)
                                        ])
                                declaration "z-index" context number (call "sibling-index" [])
                                declaration
                                    "width"
                                    lengthContext
                                    length
                                    (Ranvier.Css.CalculationRuntime.interpolate
                                        (Ranvier.Css.CalculationProgress.Value (literal "number" "" 0.5))
                                        (Some (Ranvier.Css.CalculationEasing.Keyword "ease"))
                                        None
                                        [
                                            {
                                                Positions = [ literal "number" "" 0. ]
                                                Value = px 10.
                                                EasingBefore = None
                                            }
                                            {
                                                Positions = [ literal "number" "" 1. ]
                                                Value = px 20.
                                                EasingBefore = None
                                            }
                                        ])
                                Ranvier.Css.Style.declarations
                                    [
                                        Ranvier.Css.CalculationRuntime.declarationIntrinsic
                                            "width"
                                            [ "auto" ]
                                            (Ranvier.Css.CalculationRuntime.intrinsic
                                                (Ranvier.Css.IntrinsicBasis.Keyword "auto")
                                                (binary "+" Ranvier.Css.CalculationRuntime.size (px 2.)))
                                    ]
                                Ranvier.Css.Style.declarations
                                    [
                                        Ranvier.Css.CalculationRuntime.declarationIntrinsic
                                            "width"
                                            [ "fit-content()" ]
                                            (Ranvier.Css.CalculationRuntime.intrinsic
                                                (Ranvier.Css.IntrinsicBasis.FitContent (px 20.))
                                                (binary "+" Ranvier.Css.CalculationRuntime.size (px 2.)))
                                    ]
                                Ranvier.Css.Style.declarations
                                    [
                                        Ranvier.Css.CalculationRuntime.declarationIntrinsic
                                            "width"
                                            [ "stretch()" ]
                                            (Ranvier.Css.CalculationRuntime.intrinsic
                                                (Ranvier.Css.IntrinsicBasis.Stretch (px 20.))
                                                (binary "+" Ranvier.Css.CalculationRuntime.size (px 2.)))
                                    ]
                                Ranvier.Css.Style.dynamicWithKeywords
                                    [ "initial"; "inherit"; "unset"; "revert"; "revert-layer"; "revert-rule" ]
                                    "width"
                                    (fun () -> failwith "Runtime comparison must not sample a reader")
                            ]

                    let expectedModule =
                        {
                            Name =
                                "Fixture-"
                                + Ranvier.Css.Identity.hash "Fixture/Fixture.fs"
                            Dependencies = []
                            Rules = []
                            Styles = [ expected.Definition ]
                        }

                    for mode in
                        [
                            Options.defaults.Mode
                            OutputMode.SingleFile "app.css"
                            OutputMode.CssModules
                        ] do
                        let assets = { Options.defaults with Mode = mode }

                        let result =
                            extract
                                { ExtractionOptions.defaults with
                                    Assets = assets
                                }
                                project

                        match Compiler.compile assets [ expectedModule ] with
                        | Ok runtime -> Expect.equal result runtime "Extracted bytes and identities match the runtime calculation tree."
                        | Error errors -> failtest (String.concat "\n" errors)
            testCase "checked numeric adapters reach canonical unary functions in every asset mode"
            <| fun _ ->
                use manifest =
                    System.Text.Json.JsonDocument.Parse (
                        File.ReadAllText (Path.Combine (repository, "src/Ranvier.Css.Bindings/Generated/Css.manifest.json"))
                    )

                let factories =
                    manifest.RootElement.GetProperty("extractionFactories").EnumerateArray()
                    |> Seq.toList

                let field (name: string) (item: System.Text.Json.JsonElement) =
                    item.GetProperty(name).GetString()

                let invocation item argument =
                    "global."
                    + field "owner" item
                    + "."
                    + field "memberName" item
                    + " ("
                    + argument
                    + ")"

                let angleMarker =
                    manifest.RootElement
                        .GetProperty("numericSemantics")
                        .GetProperty("runtime")
                        .GetProperty("valueAdapters")
                        .GetProperty("helpers")
                        .EnumerateArray()
                    |> Seq.find (fun item -> field "memberName" item = "angle")
                    |> field "marker"

                let rotate =
                    factories
                    |> List.find (fun item ->
                        field "memberName" item = "create"
                        && field "functionName" item = "rotate")

                let rec bridge visited marker expression =
                    if marker = field "inputMarker" rotate then
                        Some expression
                    elif Set.contains marker visited then
                        None
                    else
                        factories
                        |> List.filter (fun item ->
                            field "kind" item = "copy"
                            && field "inputMarker" item = marker)
                        |> List.tryPick (fun item -> bridge (Set.add marker visited) (field "outputMarker" item) (invocation item expression))

                let argument =
                    bridge Set.empty angleMarker "Css.angle (Css.deg 10. + Css.deg 20.)"
                    |> Option.defaultWith (fun () -> failtest "The unary rotate grammar has an allowlisted angle path.")

                let factory = invocation rotate argument

                temporary
                    $"""module Fixture
open Ranvier.Css
open Ranvier.Css.View
let example = utility {{
    Css.transform ({factory})
    Css.rotate (Css.angle (Css.deg 10. + Css.deg 20.))
    Css.zIndex (Css.integer (Css.number 2. + Css.number 1.))
}}
let view () = styles {{ example }}
do failwith "Canonical numeric bridge initializers must never execute"
"""
                <| fun project ->
                    let expected =
                        Ranvier.Css.Style.create
                            [
                                Ranvier.Css.Style.declarations [ Ranvier.Css.Declaration.create "transform" "rotate(calc(10deg + 20deg))" ]
                                Ranvier.Css.Style.declarations [ Ranvier.Css.Declaration.create "rotate" "calc(10deg + 20deg)" ]
                                Ranvier.Css.Style.declarations [ Ranvier.Css.Declaration.create "z-index" "calc(2 + 1)" ]
                            ]

                    let expectedModule =
                        {
                            Name =
                                "Fixture-"
                                + Ranvier.Css.Identity.hash "Fixture/Fixture.fs"
                            Dependencies = []
                            Rules = []
                            Styles = [ expected.Definition ]
                        }

                    for mode in
                        [
                            Options.defaults.Mode
                            OutputMode.SingleFile "app.css"
                            OutputMode.CssModules
                        ] do
                        let assets = { Options.defaults with Mode = mode }

                        let result =
                            extract
                                { ExtractionOptions.defaults with
                                    Assets = assets
                                }
                                project

                        match Compiler.compile assets [ expectedModule ] with
                        | Ok runtime -> Expect.equal result runtime "Canonical adapters preserve calculation bytes and style identities."
                        | Error errors -> failtest (String.concat "\n" errors)
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
            testCase "typed numeric custom properties retain references and fallbacks without sampling values"
            <| fun _ ->
                use manifest =
                    System.Text.Json.JsonDocument.Parse (
                        File.ReadAllText (Path.Combine (repository, "src/Ranvier.Css.Bindings/Generated/Css.manifest.json"))
                    )

                let marker =
                    manifest.RootElement.GetProperty("definitions").EnumerateArray()
                    |> Seq.find (fun item ->
                        item.GetProperty("kind").GetString() = "Type"
                        && item.GetProperty("name").GetString() = "length"
                        && item.GetProperty("scopes").GetArrayLength() = 0)
                    |> fun item -> item.GetProperty("marker").GetString()

                temporary
                    $"""module Fixture
open Ranvier.Css
open Ranvier.Css.View
let spacing = Css.lengthVariable "--spacing"
let coreSpacing: CustomProperty<global.Ranvier.Css.Generated.Css.Markers.{marker}> = CustomProperty.create "--core-spacing"
let alias = Css.lengthVariable (CustomProperty.name coreSpacing)
let example = utility {{
    Css.width (Css.variable (spacing, fallback = Css.rem 2.) + Css.px 8.)
    Css.height (Css.variable alias)
}}
let view () = styles {{ example }}
do failwith "Numeric variable initializers must never execute"
"""
                <| fun project ->
                    let result = extract ExtractionOptions.defaults project

                    let length =
                        Ranvier.Css.Spec.NumericSemantics.dimension Ranvier.Css.Spec.NumericDimension.Length

                    let context = Ranvier.Css.Spec.NumericSemantics.dimensionOnlyContext

                    let width =
                        Ranvier.Css.CalculationRuntime.binary
                            "+"
                            (Ranvier.Css.CalculationRuntime.reference
                                "length"
                                "--spacing"
                                (Some (Ranvier.Css.CalculationRuntime.literal "length" "rem" 2.)))
                            (Ranvier.Css.CalculationRuntime.literal "length" "px" 8.)

                    let height = Ranvier.Css.CalculationRuntime.reference "length" "--core-spacing" None

                    let expected =
                        Ranvier.Css.Style.create
                            [
                                Ranvier.Css.Style.declarations [ Ranvier.Css.CalculationRuntime.declaration "width" context length width ]
                                Ranvier.Css.Style.declarations [ Ranvier.Css.CalculationRuntime.declaration "height" context length height ]
                            ]

                    let css =
                        result.Assets
                        |> List.map _.Content
                        |> String.concat "\n"

                    Expect.stringContains css ("." + expected.Definition.ClassName) "Reference identity matches the runtime tree"

                    Expect.stringContains
                        css
                        (Ranvier.Css.CalculationRuntime.render width)
                        "Fallback arithmetic is serialized without sampling the variable"

                    Expect.stringContains
                        css
                        (Ranvier.Css.CalculationRuntime.render height)
                        "Core custom-property constructors and names remain deterministic"
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
                    "module Fixture\nopen Ranvier.Css\nopen Ranvier.Css.View\nlet breakpoint = minViewportWidth Css.zero\nlet example = utility { Css.opacity (Css.number 0.); Css.color Css.tan; Css.cursor Css.progress; Css.borderImageRepeat Css.round; breakpoint { Css.width Css.auto } }\nlet view () = styles { example }\n"
                <| fun project ->
                    let result = extract ExtractionOptions.defaults project
                    Expect.stringContains result.Assets.Head.Content "opacity: 0" "Zero numeric tokens are serialized correctly."
                    Expect.stringContains result.Assets.Head.Content "min-width: 0" "The zero production retains its literal token."
                    Expect.stringContains result.Assets.Head.Content "width: auto" "Generated keyword tokens are serialized correctly."
                    Expect.stringContains result.Assets.Head.Content "color: tan" "A keyword getter is distinct from the tangent function."
                    Expect.stringContains result.Assets.Head.Content "cursor: progress" "A keyword getter is distinct from the progress function."

                    Expect.stringContains
                        result.Assets.Head.Content
                        "border-image-repeat: round"
                        "A keyword getter is distinct from the rounding function."
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
