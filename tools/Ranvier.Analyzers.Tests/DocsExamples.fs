/// Compiles the examples of each rule page through its rule: a `Triggers` example reports the page's code, a `Fixed`
/// example reports nothing.
module Ranvier.Analyzers.Tests.DocsExamples

open System.IO
open System.Text.RegularExpressions
open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private block (text: string) (heading: string) =
    let section =
        Regex.Match (text, $"### {Regex.Escape heading}\n+```fsharp\n(.*?)\n```", RegexOptions.Singleline)

    if section.Success then
        Some (section.Groups[1].Value + "\n")
    else
        None

/// The fenced F# blocks under the page's `### Triggers` and `### Fixed` headings, each with its heading.
let examples (page: string) : (string * string) list =
    let text = File.ReadAllText(page).Replace("\r\n", "\n")

    [
        for heading in [ "Triggers"; "Fixed" ] do
            match block text heading with
            | Some source -> heading, source
            | None -> ()
    ]

/// The block under a `### Shared: <file>` heading, compiled before each example: the file name and its source.
let shared (page: string) : (string * string) option =
    let text = File.ReadAllText(page).Replace("\r\n", "\n")
    let heading = Regex.Match (text, "### Shared: (\\S+)")

    if heading.Success then
        let file = heading.Groups[1].Value

        block text $"Shared: {file}"
        |> Option.map (fun source -> file, source)
    else
        None

let private pages () =
    Directory.GetFiles (Path.Combine (repository, "docs/content/analyzers"), "ran*.md")
    |> Array.sort

[<Tests>]
let tests =
    testList
        "Docs examples"
        [
            for page in pages () do
                let code = (Path.GetFileNameWithoutExtension page).ToUpperInvariant()

                test code {
                    let rule =
                        Analyzers.rules
                        |> List.find (fun rule -> rule.Code = code)

                    let blocks = examples page
                    Expect.equal (blocks |> List.map fst) [ "Triggers"; "Fixed" ] "both examples"

                    for heading, source in blocks do
                        let compiled =
                            match shared page with
                            | Some file -> contextOf (ranvier.Force ()) [ file; "Example.fs", source ]
                            | None -> context (ranvier.Force ()) source

                        let codes = Analyzers.runCli rule compiled |> List.map _.Code

                        if heading = "Triggers" then
                            Expect.contains codes code "the example triggers the rule"
                        else
                            Expect.isEmpty codes "the fixed example is clean"
                }
        ]
