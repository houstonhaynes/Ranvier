/// Each rule's time over Core.fs and Template.fs, the two largest files of the gate: 250 ms per rule per file, and
/// 500 ms for the shared walk that every rule reads. The rules run ungated, so the view rules are timed over
/// Template.fs as well.
module Ranvier.Analyzers.Tests.Budget

open System.Diagnostics
open System.IO
open System.Xml.Linq
open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Rule
open Ranvier.Analyzers.Tests.Options

/// The unconditional `Compile` items of `project`, a path relative to the repository: each item's path relative to the
/// project, and its text.
let sourcesOf (project: string) : (string * string) list =
    let path = Path.Combine (repository, project)
    let directory = Path.GetDirectoryName path

    [
        for item in XDocument.Load(path).Descendants(XName.Get "Compile") do
            if isNull (item.Attribute (XName.Get "Condition")) then
                let relative = item.Attribute(XName.Get "Include").Value
                relative, File.ReadAllText (Path.Combine (directory, relative))
    ]

let private timed (label: string) (budget: int64) (action: unit -> unit) =
    let clock = Stopwatch.StartNew ()
    action ()
    clock.Stop ()
    printfn $"Budget: {label}: {clock.ElapsedMilliseconds} ms"
    Expect.isLessThanOrEqual clock.ElapsedMilliseconds budget $"{label}: {clock.ElapsedMilliseconds} ms"

let private budgetOver (project: string) (file: string) (references: string list) =
    let sources = sourcesOf project

    let upTo =
        sources
        |> List.findIndex (fun (path, _) -> Path.GetFileName path = file)

    let context = contextOf (withReferences references) (List.take (upTo + 1) sources)
    let tree = context.TypedTree.Value
    // The first walk of any tree compiles the walker; a small tree takes that cost outside the timed walk.
    let warm =
        Options.context (plain.Force ()) "module Warm\nlet warm = [ 1 ] |> List.map id\n"

    Index.ofFile warm.TypedTree.Value |> ignore

    timed $"index of {file}" 500L (fun () -> Index.ofFile tree |> ignore)

    let input =
        {
            FileName = context.FileName
            Source = context.SourceText
            Tree = tree
            Index = Index.ofFile tree
            Options = context.ProjectOptions
            Project = Some context.CheckProjectResults
        }

    for rule in Analyzers.rules do
        timed $"{rule.Code} over {file}" 250L (fun () -> rule.Run input |> ignore)

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Budget"
        [
            test "every rule over Core.fs" { budgetOver "src/Ranvier/Ranvier.fsproj" "Core.fs" [ "Fable.Core.dll" ] }

            test "every rule over Template.fs" {
                budgetOver
                    "src/Ranvier.View/Ranvier.View.fsproj"
                    "Template.fs"
                    [
                        "Ranvier.dll"
                        "Fable.Core.dll"
                        "Xantham.Fable.Core.dll"
                        "Xantham.Fable.Core.TS.dll"
                    ]
            }
        ]
