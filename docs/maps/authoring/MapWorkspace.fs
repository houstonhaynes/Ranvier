namespace Ranvier.Docs.Maps.Authoring

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions

/// <summary>A compiler or exporter diagnostic, placed on its page when it came from a cell.</summary>
type WorkspaceMessage =
    {
        Page: PageUnit option
        /// <summary>The page body line and column.</summary>
        At: (int * int) option
        Text: string
        IsError: bool
    }

/// <summary>Where a workspace is generated and what it builds with.</summary>
type WorkspaceOptions =
    {
        /// <summary>The directory holding the generated Shared, Exporter and Client projects.</summary>
        Workspace: string
        /// <summary>The maps library the page modules reference.</summary>
        MapsProject: string
        /// <summary>The repository, whose sources decide whether a build is current.</summary>
        Repository: string
        /// <summary>The esbuild executable.</summary>
        Esbuild: string
        Minify: bool
        /// <summary>The npm packages the compiled client imports.</summary>
        Packages: (string * string) list
        /// <summary>Told what is taking the time.</summary>
        Log: string -> unit
    }

/// <summary>What a build of the workspace produced.</summary>
type WorkspaceBuilt =
    {
        /// <summary>The server HTML of each host id; each host of a failed page holds a failure card.</summary>
        Fragments: Map<string, string>
        /// <summary>The bundle's files, by path relative to the bundle directory; one entry per page that built.</summary>
        Files: (string * string) list
        Messages: WorkspaceMessage list
        /// <summary>The keys of the pages left out of the bundle by their own errors.</summary>
        Failed: Set<string>
    }

/// <summary>
/// The generated projects of the documentation's component fences: a shared project of page modules, a .NET exporter
/// that writes each cell's server HTML and browser entry, and a Fable client bundled per page.
/// </summary>
/// <remarks>
/// A page whose module fails to compile or whose cell fails to render is left out and the rest rebuilt, so every other
/// page still gets its server HTML and bundle.
/// </remarks>
[<RequireQualifiedAccess>]
module MapWorkspace =
    let private sharedProject (mapsProject: string) =
        $"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="pages/*.fs" />
    <ProjectReference Include="%s{mapsProject}" />
  </ItemGroup>
</Project>
"""

    let private exporterProject =
        """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Program.fs" />
    <ProjectReference Include="../Shared/Shared.fsproj" />
  </ItemGroup>
</Project>
"""

    let private clientProject =
        """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../out/entries/*.fs" />
    <ProjectReference Include="../Shared/Shared.fsproj" />
  </ItemGroup>
</Project>
"""

    let private packageJson (packages: (string * string) list) =
        let dependencies =
            packages
            |> List.map (fun (name, version) -> $"    \"%s{name}\": \"%s{version}\"")
            |> String.concat ",\n"

        $"{{\n  \"name\": \"ranvier-docs-maps\",\n  \"private\": true,\n  \"type\": \"module\",\n  \"dependencies\": {{\n%s{dependencies}\n  }}\n}}\n"

    let private hash (text: string) =
        SHA256.HashData (Encoding.UTF8.GetBytes text)
        |> Convert.ToHexString

    /// <summary>Writes <c>content</c> when the file's content differs.</summary>
    let private write (path: string) (content: string) =
        Directory.CreateDirectory (Path.GetDirectoryName path)
        |> ignore

        if
            not (File.Exists path)
            || File.ReadAllText path <> content
        then
            File.WriteAllText (path, content)

    let private run (directory: string) (environment: (string * string) list) (file: string) (arguments: string list) =
        let info =
            ProcessStartInfo (
                file,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = directory
            )

        for argument in arguments do
            info.ArgumentList.Add argument

        // Long-lived MSBuild nodes would hold the redirected output open after the build ends.
        info.Environment["MSBUILDDISABLENODEREUSE"] <- "1"
        info.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] <- "0"

        for name, value in environment do
            info.Environment[name] <- value

        use proc = new Process (StartInfo = info)
        let output = StringBuilder ()

        let collect =
            DataReceivedEventHandler (fun _ line ->
                if not (isNull line.Data) then
                    lock output (fun () -> output.AppendLine line.Data |> ignore))

        proc.OutputDataReceived.AddHandler collect
        proc.ErrorDataReceived.AddHandler collect
        proc.Start () |> ignore
        proc.BeginOutputReadLine ()
        proc.BeginErrorReadLine ()

        if not (proc.WaitForExit (int (TimeSpan.FromMinutes 15.).TotalMilliseconds)) then
            proc.Kill true
            failwithf "%s timed out" file

        // Waits briefly for the output still in flight, without waiting on processes the command left running.
        proc.WaitForExit 2000 |> ignore
        proc.ExitCode, lock output (fun () -> output.ToString ())

    let private diagnostic =
        Regex (
            @"^\s*(?<file>.+?\.fs)\((?<line>\d+),(?<column>\d+)\)(?::\s*\(\d+,\d+\))?:?\s*(?<severity>error|warning)\s+(?<code>[A-Za-z]+\d*)\s*:\s*(?<text>.*?)(?:\s*\[[^\]]*\])?\s*$",
            RegexOptions.Compiled
        )

    /// <summary>Places compiler diagnostics on the pages whose modules they name; warnings outside a cell are dropped.</summary>
    let private messages (units: PageUnit list) (output: string) =
        output.Split '\n'
        |> Array.choose (fun line ->
            let matched = diagnostic.Match (line.TrimEnd '\r')

            if not matched.Success then
                None
            else
                let file = Path.GetFileName matched.Groups["file"].Value
                let isError = matched.Groups["severity"].Value = "error"
                let code = matched.Groups["code"].Value
                let lineNumber = matched.Groups["line"].Value
                let path = matched.Groups["file"].Value
                // The compiler separates a message's lines with control characters.
                let message = Regex.Replace (matched.Groups["text"].Value, @"[\x00-\x1f\s]+", " ")
                let text = $"%s{code}: %s{message}"

                let page =
                    units
                    |> List.tryFind (fun unit ->
                        MapPage.fileName unit.Key = file
                        || file.StartsWith ("P" + unit.Key.Substring 1 + "_"))

                let at =
                    page
                    |> Option.filter (fun unit -> MapPage.fileName unit.Key = file)
                    |> Option.bind (fun unit -> MapPage.locate unit.Spans (int matched.Groups["line"].Value) (int matched.Groups["column"].Value))

                match page, at with
                | Some _, Some _ ->
                    Some
                        {
                            Page = page
                            At = at
                            Text = text
                            IsError = isError
                        }
                | _ when isError ->
                    Some
                        {
                            Page = page
                            At = None
                            Text = $"%s{path}(%s{lineNumber}): %s{text}"
                            IsError = true
                        }
                | _ -> None)
        |> Array.distinct
        |> List.ofArray

    let private failure (step: string) (output: string) =
        {
            Page = None
            At = None
            Text = $"%s{step} failed:\n%s{output.Trim ()}"
            IsError = true
        }

    /// <summary>The files whose change requires a rebuild: the map libraries and every source project they reference.</summary>
    let private inputs (repository: string) =
        let skip (path: string) =
            let parts = path.Split ([| '\\'; '/' |])

            parts
            |> Array.exists (fun part ->
                part = "bin"
                || part = "obj"
                || part = "node_modules"
                || part = "tests")

        [ Path.Combine (repository, "docs", "maps"); Path.Combine (repository, "src") ]
        |> List.filter Directory.Exists
        |> List.collect (fun directory ->
            Directory.EnumerateFiles (directory, "*", SearchOption.AllDirectories)
            |> Seq.filter (fun path ->
                not (skip (Path.GetRelativePath (directory, path)))
                && (path.EndsWith ".fs"
                    || path.EndsWith ".fsproj"
                    || path.EndsWith ".props"
                    || path.EndsWith ".targets"))
            |> List.ofSeq)
        |> List.sort
        |> List.map (fun path ->
            let info = FileInfo path
            $"%s{path}|%d{info.Length}|%d{info.LastWriteTimeUtc.Ticks}")
        |> String.concat "\n"

    let private readTree (directory: string) =
        if Directory.Exists directory then
            Directory.EnumerateFiles (directory, "*", SearchOption.AllDirectories)
            |> Seq.sort
            |> Seq.map (fun path -> (Path.GetRelativePath (directory, path)).Replace('\\', '/'), File.ReadAllText path)
            |> List.ofSeq
        else
            []

    let private reset (directory: string) =
        if Directory.Exists directory then
            Directory.Delete (directory, true)

        Directory.CreateDirectory directory |> ignore

    /// <summary>The keys of the pages that <c>found</c> blames errors on.</summary>
    let private blamed (found: WorkspaceMessage list) =
        found
        |> List.filter _.IsError
        |> List.choose (fun message -> message.Page |> Option.map _.Key)
        |> Set.ofList

    /// <summary>The npm executable on the path; a relative npm.cmd resolves its own script against the working directory.</summary>
    let private npm () =
        let name = if OperatingSystem.IsWindows () then "npm.cmd" else "npm"

        (Environment.GetEnvironmentVariable "PATH").Split Path.PathSeparator
        |> Array.map (fun directory -> Path.Combine (directory.Trim '"', name))
        |> Array.tryFind File.Exists
        |> Option.defaultValue name

    /// <summary>
    /// Generates the workspace, builds what changed, and reads the server HTML and bundle. A page whose own errors stop
    /// its module compiling or its cells rendering is left out, and the remaining pages are built again.
    /// </summary>
    /// <param name="options">Where the workspace is and what it builds with.</param>
    /// <param name="units">Every page with component cells.</param>
    let build (options: WorkspaceOptions) (units: PageUnit list) : WorkspaceBuilt =
        let workspace = options.Workspace
        let log = options.Log
        let out = Path.Combine (workspace, "out")
        let bundle = Path.Combine (out, "bundle")
        let fragments = Path.Combine (out, "fragments")
        let entries = Path.Combine (out, "entries")
        let pages = Path.Combine (workspace, "Shared", "pages")
        let trace = [ "RanvierTrace", "true" ]
        let stamp = Path.Combine (workspace, "fingerprint")

        let shared =
            sharedProject (Path.GetRelativePath (Path.Combine (workspace, "Shared"), options.MapsProject))

        // Writes the projects and the modules of `live`, removing every other page's module.
        let generate (live: PageUnit list) =
            write (Path.Combine (workspace, "Shared", "Shared.fsproj")) shared
            write (Path.Combine (workspace, "Exporter", "Exporter.fsproj")) exporterProject
            write (Path.Combine (workspace, "Exporter", "Program.fs")) (MapPage.exporter live)
            write (Path.Combine (workspace, "Client", "Client.fsproj")) clientProject
            write (Path.Combine (workspace, "package.json")) (packageJson options.Packages)

            let current =
                live
                |> List.map (fun unit -> MapPage.fileName unit.Key)
                |> Set.ofList

            if Directory.Exists pages then
                for file in Directory.GetFiles (pages, "*.fs") do
                    if not (current.Contains (Path.GetFileName file)) then
                        File.Delete file

            for unit in live do
                write (Path.Combine (pages, MapPage.fileName unit.Key)) unit.Code

        let fingerprint =
            [
                MapPage.exporter units
                yield! units |> List.map _.Code
                packageJson options.Packages
                string options.Minify
                shared
                inputs options.Repository
            ]
            |> String.concat "\n\u0000"
            |> hash

        generate units

        let fresh =
            File.Exists stamp
            && File.ReadAllText stamp = fingerprint
            && Directory.Exists bundle

        // Builds and runs the exporter over `live`, leaving out pages whose modules fail to compile.
        let rec export (live: PageUnit list) (found: WorkspaceMessage list) =
            generate live
            log "building the map exporter"

            let code, output =
                run
                    workspace
                    trace
                    "dotnet"
                    [
                        "build"
                        "Exporter/Exporter.fsproj"
                        "-c"
                        "Release"
                        "-p:RanvierTrace=true"
                        "-v"
                        "q"
                        "-nologo"
                        "-clp:NoSummary"
                    ]

            if code <> 0 then
                let errors = messages live output |> List.filter _.IsError
                let pages = blamed errors

                if
                    errors.IsEmpty
                    || errors.Length > (errors |> List.filter (fun e -> e.Page.IsSome)).Length
                then
                    Error (
                        found
                        @ (if errors.IsEmpty then
                               [ failure "dotnet build" output ]
                           else
                               errors)
                    )
                else
                    export
                        (live
                         |> List.filter (fun unit -> not (pages.Contains unit.Key)))
                        (found @ errors)
            else
                reset out
                log "rendering the maps"

                let _, output =
                    run workspace [] "dotnet" [ Path.Combine ("Exporter", "bin", "Release", "net10.0", "Exporter.dll"); out ]

                let exported =
                    output.Split '\n'
                    |> Array.choose (fun line ->
                        match (line.TrimEnd '\r').Split '\t' with
                        | [| "ranvier-map"; key; cell; text |] ->
                            let page = live |> List.tryFind (fun unit -> unit.Key = key)

                            let at =
                                page
                                |> Option.bind (fun unit -> unit.Cells |> List.tryFind (fun c -> c.Id = cell))
                                |> Option.map (fun c -> c.Line - 1, 1)

                            Some
                                {
                                    Page = page
                                    At = at
                                    Text = $"rendering %s{cell} on the server: %s{text}"
                                    IsError = true
                                }
                        | _ -> None)
                    |> List.ofArray

                let failedPages = blamed exported

                Ok (
                    live
                    |> List.filter (fun unit -> not (failedPages.Contains unit.Key)),
                    found @ exported
                )

        // Compiles the client of `live`, leaving out pages whose modules or entries fail under Fable.
        let rec compile (live: PageUnit list) (found: WorkspaceMessage list) =
            let keep =
                live
                |> List.collect (fun unit ->
                    MapPage.rendered unit
                    |> List.map (fun cell -> MapPage.entryName unit.Key cell.Id + ".fs"))
                |> Set.ofList

            if Directory.Exists entries then
                for file in Directory.GetFiles (entries, "*.fs") do
                    if not (keep.Contains (Path.GetFileName file)) then
                        File.Delete file

            generate live
            log "compiling the maps with Fable"

            let code, output =
                run
                    workspace
                    trace
                    "dotnet"
                    [
                        "fickl"
                        "Client/Client.fsproj"
                        "-o"
                        Path.Combine (out, "js")
                        "-e"
                        ".js"
                        "-c"
                        "Release"
                    ]

            let errors = messages live output |> List.filter _.IsError

            if code = 0 && errors.IsEmpty then
                Ok (live, found)
            elif
                not errors.IsEmpty
                && errors |> List.forall (fun e -> e.Page.IsSome)
            then
                let pages = blamed errors

                compile
                    (live
                     |> List.filter (fun unit -> not (pages.Contains unit.Key)))
                    (found @ errors)
            else
                Error (
                    found
                    @ (if errors.IsEmpty then
                           [ failure "fickl" output ]
                       else
                           errors)
                )

        let installed () =
            let marker = Path.Combine (workspace, "node_modules", ".package-hash")
            let wanted = hash (packageJson options.Packages)

            if
                File.Exists marker
                && File.ReadAllText marker = wanted
            then
                Ok ()
            else
                log "installing the maps' npm packages"

                let code, output =
                    run workspace [] (npm ()) [ "install"; "--no-audit"; "--no-fund" ]

                if code = 0 then
                    File.WriteAllText (marker, wanted)
                    Ok ()
                else
                    Error [ failure "npm install" output ]

        let bundleFor (live: PageUnit list) =
            let compiledEntries =
                Directory.EnumerateFiles (Path.Combine (out, "js"), "*.js", SearchOption.AllDirectories)
                |> Seq.filter (fun path -> path.Replace('\\', '/').Contains "/entries/")
                |> Seq.map (fun path -> Path.GetFileNameWithoutExtension path, path)
                |> Map.ofSeq

            let pageEntries = Path.Combine (workspace, "pages")
            reset pageEntries

            let missing =
                [
                    for unit in live do
                        let path (cell: PageCell) =
                            compiledEntries
                            |> Map.tryFind (MapPage.entryName unit.Key cell.Id)
                            |> Option.map (fun path ->
                                "./"
                                + (Path.GetRelativePath (pageEntries, path)).Replace('\\', '/'))

                        match MapPage.pageEntry path unit with
                        | None -> unit.Key
                        | Some entry -> write (Path.Combine (pageEntries, unit.Key + ".js")) entry
                ]

            if not missing.IsEmpty then
                Error
                    [
                        failure "fickl" $"""no compiled entries for %s{String.concat ", " missing}"""
                    ]
            else
                log "bundling the maps"

                let files =
                    Directory.GetFiles (pageEntries, "*.js")
                    |> Array.sort
                    |> List.ofArray

                reset bundle

                if files.IsEmpty then
                    Ok ()
                else
                    let code, output =
                        run
                            workspace
                            []
                            options.Esbuild
                            [
                                yield! files
                                "--bundle"
                                "--splitting"
                                "--format=esm"
                                $"--outdir=%s{bundle}"
                                "--entry-names=[name]"
                                "--chunk-names=chunks/[name]-[hash]"
                                "--log-level=warning"
                                if options.Minify then
                                    "--minify"
                            ]

                    if code = 0 then
                        Ok ()
                    else
                        Error [ failure "esbuild" output ]

        let outcome =
            if fresh then
                Ok ([], units)
            else
                if File.Exists stamp then
                    File.Delete stamp

                match export units [] with
                | Error found -> Error found
                | Ok (live, found) ->
                    // Fickl is a local tool of the repository; a build outside build.fsx may not have restored it.
                    let restored, output = run options.Repository [] "dotnet" [ "tool"; "restore" ]

                    match
                        (if restored = 0 then
                             compile live found
                         else
                             Error (found @ [ failure "dotnet tool restore" output ]))
                    with
                    | Error found -> Error found
                    | Ok (live, found) ->
                        match
                            installed ()
                            |> Result.bind (fun () -> bundleFor live)
                        with
                        | Error problems -> Error (found @ problems)
                        | Ok () ->
                            if (blamed found).IsEmpty then
                                File.WriteAllText (stamp, fingerprint)

                            Ok (found, live)

        match outcome with
        | Error found ->
            {
                Fragments = Map.empty
                Files = []
                Messages = found
                Failed = units |> List.map _.Key |> Set.ofList
            }
        | Ok (found, live) ->
            let built = live |> List.map _.Key |> Set.ofList

            let failed =
                units
                |> List.map _.Key
                |> Set.ofList
                |> fun all -> Set.difference all built

            let rendered =
                readTree fragments
                |> List.map (fun (name, html) -> Path.GetFileNameWithoutExtension name, html)
                |> List.filter (fun (host, _) ->
                    built
                    |> Set.exists (fun key -> host.StartsWith (MapPage.hostId key "")))
                |> Map.ofList

            let cards =
                [
                    for unit in units do
                        if failed.Contains unit.Key then
                            let text =
                                found
                                |> List.filter (fun m ->
                                    m.Page
                                    |> Option.exists (fun p -> p.Key = unit.Key))
                                |> List.map _.Text
                                |> String.concat "\n"

                            for cell in MapPage.rendered unit do
                                MapPage.hostId unit.Key cell.Id, MapPage.failureCard text
                ]

            {
                Fragments = Map.fold (fun all host html -> Map.add host html all) rendered (Map.ofList cards)
                Files = readTree bundle
                Messages = found
                Failed = failed
            }
