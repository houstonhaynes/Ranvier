/// Project options, contexts and source edits for the rule fixtures.
module Ranvier.Analyzers.Tests.Options

open System
open System.IO
open FSharp.Analyzers.SDK
open FSharp.Analyzers.SDK.Testing
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Text

/// The repository root: the nearest ancestor of the output directory that holds Ranvier.slnx.
let repository: string =
    let rec up (directory: DirectoryInfo) =
        if File.Exists (Path.Combine (directory.FullName, "Ranvier.slnx")) then
            directory.FullName
        else
            up directory.Parent

    up (DirectoryInfo AppContext.BaseDirectory)

let private baseOptions =
    lazy
        (mkOptionsFromProject "net10.0" []
         |> Async.AwaitTask
         |> Async.RunSynchronously)

/// The base project options with a `-r:` option for each of `assemblies` that the output directory holds.
let withReferences (assemblies: string list) : FSharpProjectOptions =
    let options = baseOptions.Force ()

    { options with
        OtherOptions =
            Array.append
                options.OtherOptions
                [|
                    for assembly in assemblies do
                        let path = Path.Combine (AppContext.BaseDirectory, assembly)

                        if File.Exists path then
                            "-r:" + path
                |]
    }

/// Options that reference Ranvier, the view assemblies and Fable.Core.
let ranvier: Lazy<FSharpProjectOptions> =
    lazy
        (withReferences
            [
                "Ranvier.dll"
                "Ranvier.View.Portable.dll"
                "Ranvier.View.Foundation.dll"
                "Fable.Core.dll"
                "Xantham.Fable.Core.dll"
                "Xantham.Fable.Core.TS.dll"
            ])

/// Options that reference no Ranvier or Fable assembly.
let plain: Lazy<FSharpProjectOptions> = lazy (withReferences [])

let private checker = lazy (FSharpChecker.Create (keepAssemblyContents = true))

/// A tool manifest that declares `command` at `version`.
let manifest (command: string) (version: string) : string =
    $"{{ \"version\": 1, \"isRoot\": true, \"tools\": {{ \"{command}\": {{ \"version\": \"{version}\", \"commands\": [ \"{command}\" ] }} }} }}"

/// The context of the last of `files`, each a relative path and its source, compiled in order as one project in a
/// fresh temporary directory whose `.config/dotnet-tools.json` is `tools`, if any; raises on a type error.
let rec contextIn (tools: string option) (options: FSharpProjectOptions) (files: (string * string) list) : CliContext =
    contextLayout [ for text in Option.toList tools -> "", text ] "" options files

/// The context of the last of `files` in a project at `projectDirectory` below a fresh temporary directory, with a
/// `.config/dotnet-tools.json` for each of `manifests`, a directory relative to the temporary one and its text.
and contextLayout
    (manifests: (string * string) list)
    (projectDirectory: string)
    (options: FSharpProjectOptions)
    (files: (string * string) list)
    : CliContext =
    let root =
        Path.Combine (Path.GetTempPath (), "ranvier-analyzers-tests", Guid.NewGuid().ToString "N")

    for relative, text in manifests do
        let config = Path.Combine (root, relative, ".config")
        Directory.CreateDirectory config |> ignore
        File.WriteAllText (Path.Combine (config, "dotnet-tools.json"), text)

    let directory = Path.Combine (root, projectDirectory)
    Directory.CreateDirectory directory |> ignore

    let paths =
        [|
            for name, source in files ->
                let path = Path.Combine (directory, name)

                Directory.CreateDirectory (Path.GetDirectoryName path)
                |> ignore

                File.WriteAllText (path, source)
                path
        |]

    let project =
        { options with
            ProjectFileName = Path.Combine (directory, "Fixture.fsproj")
            SourceFiles = paths
            OtherOptions =
                Array.append
                    (options.OtherOptions
                     |> Array.filter (fun option -> not (option.EndsWith (".fs", StringComparison.Ordinal))))
                    paths
        }

    let checker = checker.Force ()

    let projectResults =
        checker.ParseAndCheckProject project
        |> Async.RunSynchronously

    let errors =
        projectResults.Diagnostics
        |> Array.filter (fun diagnostic -> diagnostic.Severity = FSharpDiagnosticSeverity.Error)

    if errors.Length > 0 then
        raise (Testing.CompilerDiagnosticErrors errors)

    let target = Array.last paths
    let sourceText = SourceText.ofString (File.ReadAllText target)

    let parseResults, answer =
        checker.ParseAndCheckFileInProject (target, 0, sourceText, project)
        |> Async.RunSynchronously

    match answer with
    | FSharpCheckFileAnswer.Succeeded fileResults ->
        Utils.createContext projectResults target sourceText (parseResults, fileResults) (AnalyzerProjectOptions.BackgroundCompilerOptions project)
    | FSharpCheckFileAnswer.Aborted -> failwith $"The check of {target} was aborted"

/// `options` with the compile symbol that `RanvierFableAnalyzers=true` defines.
let optedIn (options: FSharpProjectOptions) : FSharpProjectOptions =
    { options with
        OtherOptions =
            Array.append
                options.OtherOptions
                [|
                    "--define:"
                    + Ranvier.Analyzers.Symbols.Compilers.OptInDefine
                |]
    }

/// Writes a project file that references the package `package` at the project file of `context`.
let writeProjectFile (context: CliContext) (package: string) : CliContext =
    File.WriteAllText (
        context.ProjectOptions.ProjectFileName,
        $"<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"{package}\" Version=\"1.0.0\" /></ItemGroup></Project>"
    )

    context

/// The context of the last of `files` in a project whose manifest declares Fable 5.18.0.
let contextOf (options: FSharpProjectOptions) (files: (string * string) list) : CliContext =
    contextIn (Some (manifest "fable" "5.18.0")) options files

/// The context of `source` compiled as the single file of a project; raises on a type error.
let context (options: FSharpProjectOptions) (source: string) : CliContext =
    contextOf options [ "Fixture.fs", source ]

/// The findings of `analyzer` in `context`.
let run (analyzer: Analyzer<CliContext>) (context: CliContext) : Message list =
    analyzer context |> Async.RunSynchronously

/// `source` with `fix` applied, after checking that `fix.FromText` is the text at its range; line ends are normalised
/// to `\n`.
let applyFix (source: string) (fix: Fix) : string =
    let normal = source.Replace ("\r\n", "\n")
    let lines = normal.Split '\n'

    let offset (line: int) (column: int) =
        (lines
         |> Array.take (line - 1)
         |> Array.sumBy (fun text -> text.Length + 1))
        + column

    let start = offset fix.FromRange.StartLine fix.FromRange.StartColumn
    let finish = offset fix.FromRange.EndLine fix.FromRange.EndColumn

    if
        normal.Substring (start, finish - start)
        <> fix.FromText
    then
        failwith $"The fix's FromText {fix.FromText} differs from the text at its range"

    normal.Substring (0, start)
    + fix.ToText
    + normal.Substring finish

/// The text of `range` in `source`; line ends are normalised to `\n`.
let textAt (source: string) (range: FSharp.Compiler.Text.range) : string =
    let lines = source.Replace("\r\n", "\n").Split '\n'

    if range.StartLine = range.EndLine then
        lines[range.StartLine - 1].Substring(range.StartColumn, range.EndColumn - range.StartColumn)
    else
        lines[range.StartLine - 1].Substring range.StartColumn
