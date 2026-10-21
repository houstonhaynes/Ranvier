// Compiles every ```fsharp block of the view sugar docs page against DomTests.fsproj, one file per `## ` section.
// Usage: dotnet fsi tools/Ranvier.Dom.Generate/CheckDocs.fsx [readme path]
// Without an argument, checks src/Ranvier.View/README.md.
open System
open System.Diagnostics
open System.IO
open System.Text.RegularExpressions

let root = Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, "..", ".."))

let readme =
    if fsi.CommandLineArgs.Length > 1 then
        Path.GetFullPath fsi.CommandLineArgs[1]
    else
        Path.Combine (root, "src/Ranvier.View/README.md")

let project =
    Path.Combine(root, "examples/Fable.Ranvier.Playground/tests/DomTests.fsproj").Replace('\\', '/')

// The open lines the docs page assumes for every sample.
let opens =
    """open global.Ranvier
open Ranvier.View
open Ranvier.View.Sugar
"""

let fail (message: string) =
    eprintfn "FAIL %s" message
    exit 1

let text = File.ReadAllText(readme).Replace("\r\n", "\n")

let sections =
    Regex.Split (text, @"(?m)^## ")
    |> Array.skip 1
    |> Array.map (fun section ->
        let title = section.Substring (0, section.IndexOf '\n')

        let blocks =
            [
                for m in Regex.Matches (section, @"```fsharp\n(.*?)```", RegexOptions.Singleline) -> m.Groups[1].Value
            ]

        title, blocks)
    |> Array.filter (fun (_, blocks) -> not blocks.IsEmpty)

if sections.Length = 0 then
    fail $"{readme} holds no fsharp block"

let dotnet (args: string list) =
    let info =
        ProcessStartInfo ("dotnet", args, RedirectStandardOutput = true, RedirectStandardError = true)

    use proc = Process.Start info
    let stderr = proc.StandardError.ReadToEndAsync ()
    let stdout = proc.StandardOutput.ReadToEnd ()
    proc.WaitForExit ()
    proc.ExitCode, stdout + stderr.Result

let directory =
    Directory.CreateDirectory (Path.Combine (Path.GetTempPath (), "ranvier-sugar-docs-" + Path.GetRandomFileName ()))

let code, output =
    try
        let files =
            sections
            |> Array.mapi (fun index (title, blocks) ->
                let name = $"Docs{index + 1}.fs"

                File.WriteAllText (
                    Path.Combine (directory.FullName, name),
                    $"module Docs{index + 1}\n// {title}\n"
                    + opens
                    + "\n"
                    + String.concat "\n" blocks
                )

                printfn "%s: %s (%d blocks)" name title blocks.Length
                name)

        let compiles =
            files
            |> Array.map (fun file -> $"<Compile Include=\"{file}\" />")
            |> String.concat ""

        let fsproj = Path.Combine (directory.FullName, "Check.fsproj")

        File.WriteAllText (
            fsproj,
            $"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><GenerateDocumentationFile>false</GenerateDocumentationFile></PropertyGroup>
  <ItemGroup>{compiles}<ProjectReference Include="{project}" /><PackageReference Update="FSharp.Core" Version="10.1.401" /></ItemGroup>
</Project>
"""
        )

        let restored, restoreOutput =
            dotnet [ "restore"; fsproj; "--configfile"; Path.Combine (root, "nuget.config") ]

        if restored <> 0 then
            restored, restoreOutput
        else
            dotnet [ "build"; fsproj; "--no-restore"; "--configuration"; "Release" ]
    finally
        try
            directory.Delete true
        with _ ->
            ()

let diagnostics =
    output.Split '\n'
    |> Array.filter (fun line ->
        line.Contains "error FS"
        || line.Contains "warning FS")
    |> Array.distinct

for line in diagnostics do
    printfn "%s" (line.Trim ())

// A failure without FS diagnostics (restore, SDK or project errors) prints the tail of the dotnet output.
if code <> 0 && diagnostics.Length = 0 then
    for line in
        output.Split '\n'
        |> Array.rev
        |> Array.truncate 40
        |> Array.rev do
        printfn "%s" (line.TrimEnd ())

if code = 0 then
    printfn "PASS: %d section files compile" sections.Length
else
    fail $"the docs samples do not compile (dotnet exit {code})"
