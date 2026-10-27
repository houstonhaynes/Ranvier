module Ranvier.Css.Spec.GenerationTests

open System
open System.IO
open System.Text
open System.Text.Json
open Expecto
open Ranvier.Css.Spec

let private catalogue () =
    let json =
        """{"properties":[{"name":"width","href":"https://example.test/width","syntax":"auto | <length [0,100]>"}],"types":[{"name":"length","href":"https://example.test/length"}],"functions":[],"selectors":[],"atrules":[]}"""

    match Webref.consume "generation" (Encoding.UTF8.GetBytes json) with
    | Ok value -> value
    | Error errors -> failtestf "%A" errors

let private generate catalogue =
    match Generation.generate "Example.Generated" catalogue with
    | Ok value -> value
    | Error errors -> failtestf "%A" errors

/// <summary>Checks deterministic generation, coverage, obsolescence, and output collision protection.</summary>
let tests =
    testList
        "CSS binding generation"
        [
            testCase "sugar fills provably empty optional sequence components through canonical factories"
            <| fun _ ->
                let json =
                    """{"properties":[{"name":"gap","href":"https://example.test/gap","syntax":"<length [0,100]> <length [0,100]>?"},{"name":"prefix","href":"https://example.test/prefix","syntax":"auto? <length>"},{"name":"nested","href":"https://example.test/nested","syntax":"<length> [auto? none?]"},{"name":"choice","href":"https://example.test/choice","syntax":"<length> [auto? | none]"},{"name":"unordered","href":"https://example.test/unordered","syntax":"auto? && <length>"},{"name":"pair","href":"https://example.test/pair","syntax":"<length> <length>"},{"name":"required","href":"https://example.test/required","syntax":"<length> [auto?]!"}],"types":[{"name":"length","href":"https://example.test/length"}],"functions":[],"selectors":[],"atrules":[]}"""

                let input =
                    Webref.consume "optional-components" (Encoding.UTF8.GetBytes json)
                    |> Result.toOption
                    |> Option.get

                let output =
                    RuntimeGeneration.generate "Example.Generated" input
                    |> Result.toOption
                    |> Option.get

                use manifest = JsonDocument.Parse output.Manifest

                let definition name =
                    manifest.RootElement.GetProperty("definitions").EnumerateArray()
                    |> Seq.find (fun item -> item.GetProperty("name").GetString() = name)

                for name in [ "gap"; "prefix"; "nested"; "choice"; "unordered" ] do
                    let paths = (definition name).GetProperty("ergonomicValues")
                    Expect.equal (paths.GetArrayLength ()) 2 (name + " accepts a standalone length")

                    let path =
                        paths.EnumerateArray ()
                        |> Seq.find (fun item ->
                            item.GetProperty("expression").GetString()
                            <> "value")

                    Expect.stringContains (path.GetProperty("expression").GetString()) " []" "Empty repetition uses its checked canonical factory"

                for name in [ "pair"; "required" ] do
                    Expect.equal
                        ((definition name).GetProperty("ergonomicValues").GetArrayLength())
                        1
                        "Required components cannot be silently omitted"

                let gapPath =
                    (definition "gap").GetProperty("ergonomicValues").EnumerateArray()
                    |> Seq.find (fun item -> item.GetProperty("ranges").GetArrayLength() > 0)

                let gapRange = gapPath.GetProperty("ranges")[0]
                Expect.equal (gapRange.GetProperty("maximum").GetProperty("value").GetString()) "100" "Numeric guard remains authoritative"

                let gapSugar =
                    manifest.RootElement.GetProperty("sugar").GetProperty("properties").EnumerateArray()
                    |> Seq.find (fun item -> item.GetProperty("cssName").GetString() = "gap")

                Expect.equal (gapSugar.GetProperty("acceptedValues").GetArrayLength()) 2 "Importer receives the same safe conversion coverage"
            testCase "sugar manifest exports canonical numeric guards for CSS import"
            <| fun _ ->
                let output =
                    RuntimeGeneration.generate "Example.Generated" (catalogue ())
                    |> Result.toOption
                    |> Option.get

                use manifest = JsonDocument.Parse output.Manifest
                let sugar = manifest.RootElement.GetProperty ("sugar")
                Expect.equal (sugar.GetProperty("schemaVersion").GetInt32()) 2 "Guard-aware schema"

                let width =
                    sugar.GetProperty("properties").EnumerateArray()
                    |> Seq.find (fun property -> property.GetProperty("cssName").GetString() = "width")

                let bounded =
                    width.GetProperty("acceptedValues").EnumerateArray()
                    |> Seq.find (fun value -> value.GetProperty("ranges").GetArrayLength() > 0)

                let range = bounded.GetProperty("ranges")[0]
                Expect.equal (range.GetProperty("minimum").GetProperty("value").GetString()) "0" "Lower bound retained"
                Expect.equal (range.GetProperty("maximum").GetProperty("value").GetString()) "100" "Upper bound retained"
            testCase "unified sugar exposes typed values and readers in the shorter namespace"
            <| fun _ ->
                let output =
                    match RuntimeGeneration.generate "Example.Generated" (catalogue ()) with
                    | Ok output -> output
                    | Error errors -> failtestf "%A" errors

                Expect.stringContains output.Source "namespace ``Example``\n" "Default short namespace"
                Expect.stringContains output.Source "static member inline ``px``" "Shared typed length primitive"
                Expect.stringContains output.Source "static member inline ``width`` (value:" "Unified static overload"
                Expect.stringContains output.Source "static member inline ``width`` (read:" "Unified reader overload"
                Expect.stringContains output.Source "static member inline ``auto``" "Shared typed keyword"
                use manifest = JsonDocument.Parse output.Manifest

                let width =
                    manifest.RootElement.GetProperty("definitions").EnumerateArray()
                    |> Seq.find (fun item -> item.GetProperty("name").GetString() = "width")

                let paths =
                    width.GetProperty("ergonomicValues").EnumerateArray()
                    |> Seq.toList

                Expect.equal paths.Length 2 "Root and referenced length have safe conversion paths"

                Expect.isTrue
                    (paths
                     |> List.exists (fun path -> path.GetProperty("expression").GetString().Contains("Syntax.")))
                    "Conversion goes through checked canonical factories"
            testCase "sugar terminates recursive aliases and preserves structural requirements"
            <| fun _ ->
                let json =
                    """{"properties":[{"name":"recursive","href":"https://example.test/recursive","syntax":"<a>"},{"name":"pair","href":"https://example.test/pair","syntax":"<length>{2}"}],"types":[{"name":"a","href":"https://example.test/a","syntax":"auto | <b>"},{"name":"b","href":"https://example.test/b","syntax":"<a> | <length>"},{"name":"length","href":"https://example.test/length"}],"functions":[],"selectors":[],"atrules":[]}"""

                let input =
                    Webref.consume "recursive-sugar" (Encoding.UTF8.GetBytes json)
                    |> Result.toOption
                    |> Option.get

                let output = generate input
                use manifest = JsonDocument.Parse output.Manifest

                let definitions =
                    manifest.RootElement.GetProperty("definitions").EnumerateArray()
                    |> Seq.toList

                let paths name =
                    definitions
                    |> List.find (fun item -> item.GetProperty("name").GetString() = name)
                    |> fun item -> item.GetProperty("ergonomicValues").GetArrayLength()

                Expect.equal (paths "recursive") 4 "Root, aliases and primitive remain finite and reachable"
                Expect.equal (paths "pair") 1 "A required pair never accepts a lone length"

                Expect.equal
                    output
                    (generate
                        { input with
                            Features = List.rev input.Features
                        })
                    "Stable conversion paths"
            testCase "sugar permits singleton alternatives and repetitions only when the grammar does"
            <| fun _ ->
                let json =
                    """{"properties":[{"name":"display","href":"https://example.test/display","syntax":"block || flex"},{"name":"padding","href":"https://example.test/padding","syntax":"<length>{1,4}"}],"types":[{"name":"length","href":"https://example.test/length"}],"functions":[],"selectors":[],"atrules":[]}"""

                let input =
                    Webref.consume "singletons" (Encoding.UTF8.GetBytes json)
                    |> Result.toOption
                    |> Option.get

                use manifest = JsonDocument.Parse (generate input).Manifest

                let definitions =
                    manifest.RootElement.GetProperty("definitions").EnumerateArray()
                    |> Seq.toList

                let display =
                    definitions
                    |> List.find (fun item -> item.GetProperty("name").GetString() = "display")

                Expect.equal (display.GetProperty("ergonomicKeywords").GetArrayLength()) 2 "Either unordered keyword can stand alone"

                let padding =
                    definitions
                    |> List.find (fun item -> item.GetProperty("name").GetString() = "padding")

                Expect.equal (padding.GetProperty("ergonomicValues").GetArrayLength()) 2 "A one-to-four repetition accepts one length"
            testCase "short property naming collisions remain deterministic and distinct"
            <| fun _ ->
                let json =
                    """{"properties":[{"name":"foo-bar","href":"https://example.test/a","syntax":"auto"},{"name":"fooBar","href":"https://example.test/b","syntax":"none"}],"types":[],"functions":[],"selectors":[],"atrules":[]}"""

                let input =
                    Webref.consume "collisions" (Encoding.UTF8.GetBytes json)
                    |> Result.toOption
                    |> Option.get

                let short catalogue =
                    RuntimeGeneration.generate "Example.Generated" catalogue
                    |> Result.toOption
                    |> Option.get

                let output = short input
                Expect.stringContains output.Source "``fooBar`` (_:" "First normalized property remains short"
                Expect.stringContains output.Source "``fooBar_" "Colliding property has a distinct stable name"

                Expect.equal
                    output
                    (short
                        { input with
                            Features = List.rev input.Features
                        })
                    "Input ordering cannot change collision names"
            testCase "CSS-wide keyword helpers follow the latest all-property grammar"
            <| fun _ ->
                let input = catalogue ()

                let tree =
                    match Grammar.parse "initial | future-wide" with
                    | Ok tree -> tree
                    | Error error -> failtestf "%A" error

                let all =
                    { input.Features.Head with
                        Id =
                            { input.Features.Head.Id with
                                Name = "all"
                            }
                        Grammar = GrammarStatus.Parsed ("initial | future-wide", tree)
                    }

                let output =
                    generate
                        { input with
                            Features = all :: input.Features
                        }

                Expect.stringContains output.Source "globalFutureWide" "Future spec keyword helper"
                use manifest = JsonDocument.Parse output.Manifest

                Expect.equal
                    (manifest.RootElement.GetProperty("cssWideKeywords").EnumerateArray()
                     |> Seq.map _.GetString()
                     |> Seq.toList)
                    [ "future-wide"; "initial" ]
                    "Registry derives from current all grammar"
            testCase "generation is deterministic across catalogue ordering"
            <| fun _ ->
                let input = catalogue ()

                Expect.equal
                    (generate input)
                    (generate
                        { input with
                            Features = List.rev input.Features
                        })
                    "Stable artifacts"
            testCase "manifest exposes opaque definitions and dependencies"
            <| fun _ ->
                let output = generate (catalogue ())
                Expect.equal output.FeatureCount 2 "Definitions"
                Expect.equal output.OpaqueCount 1 "Prose primitive"
                Expect.equal output.UnresolvedCount 0 "Length resolves"
                use manifest = JsonDocument.Parse output.Manifest
                Expect.equal (manifest.RootElement.GetProperty("references").GetArrayLength()) 1 "Reference map"
            testCase "superseded features emit compiler obsolescence"
            <| fun _ ->
                let input = catalogue ()

                let old =
                    { input.Features.Head with
                        Metadata = Map.ofList [ "supersededBy", "\"https://example.test/latest\"" ]
                    }

                let output =
                    generate
                        { input with
                            Features = old :: input.Features.Tail
                        }

                Expect.stringContains output.Source "System.Obsolete(" "F# warnings"
            testCase "namespace keywords are escaped and malformed namespaces rejected"
            <| fun _ ->
                match Generation.generate "type.module" (catalogue ()) with
                | Ok output -> Expect.stringContains output.Source "namespace ``type``.``module``" "Escaped F# segments"
                | Error errors -> failtestf "%A" errors

                for ns in [ null; ""; "A.B;printfn"; "A..B" ] do
                    Expect.isError (Generation.generate ns (catalogue ())) "Invalid namespace"
            testCase "duplicate identities cannot produce conflicting generated types"
            <| fun _ ->
                let input = catalogue ()

                Expect.isError
                    (Generation.generate
                        "Example"
                        { input with
                            Features = input.Features @ input.Features
                        })
                    "Duplicates"
            testCase "manifest collisions preserve all inputs and outputs"
            <| fun _ ->
                let directory =
                    Path.Combine (Path.GetTempPath (), "ranvier-css-" + Guid.NewGuid().ToString("N"))

                Directory.CreateDirectory directory |> ignore

                try
                    let input = Path.Combine (directory, "input.json")
                    let output = Path.Combine (directory, "model.manifest.json")
                    let bindings = Path.Combine (directory, "model.fs")
                    File.WriteAllText (input, "original-input")
                    File.WriteAllText (output, "original-output")
                    Expect.equal (Cli.run [| "--input"; input; "--output"; output; "--bindings"; bindings |]) 64 "Manifest/model collision"
                    Expect.equal (File.ReadAllText output) "original-output" "Output preserved"
                    let manifestInput = Path.Combine (directory, "input.manifest.json")
                    File.WriteAllText (manifestInput, "original-input")

                    Expect.equal
                        (Cli.run
                            [|
                                "--input"
                                manifestInput
                                "--output"
                                output
                                "--bindings"
                                Path.Combine (directory, "input.fs")
                            |])
                        64
                        "Manifest/input collision"

                    Expect.equal (File.ReadAllText manifestInput) "original-input" "Input preserved"
                finally
                    Directory.Delete (directory, true)
        ]
