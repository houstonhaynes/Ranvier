#r "nuget: Partas.Build, 0.8.2"
#r "nuget: Partas.Build.Baked, 0.3.0"
#r "nuget: Partas.TypeProvider.BuildHelper, 0.2.5"
#r "nuget: Fake.IO.FileSystem"
#r "nuget: Str"

open Partas.Build
open Partas.TypeProvider.BuildHelper
open Fake.IO.Globbing.Operators
open Fake.IO
open Str

[<Literal>]
let __REPOSITORY_DIRECTORY__ = __SOURCE_DIRECTORY__

type Repo = BuildHelperProvider<__REPOSITORY_DIRECTORY__, "bin/", capabilityFullOverride=true>

let inline funApply value fn = fn value

module Spec =
    let formattingSourceFiles =
        !!"**/*.fs"
        -- "**/obj/**/*.*"
        -- "**/AssemblyInfo.fs"
        -- "**/fable-modules/**/*.*"
        -- "**/fable_modules/**/*.*"
        -- "src/Ranvier.Wire/Vendor/**/*.*"

    let projects = Repo.Project.AllProjects ()

    let sourceProjects =
        projects
        |> List.filter _.RelativePath.StartsWith("src")

    /// The source projects in build order. Each tier references source projects of earlier tiers only; the first holds
    /// the projects that reference none.
    let sourceTiers =
        let fullPath (path: string) =
            System.IO.Path.GetFullPath (System.IO.Path.Combine (__REPOSITORY_DIRECTORY__, path))

        let sources =
            sourceProjects
            |> List.map (fun project -> fullPath project.Path)
            |> Set.ofList

        let references (project: string) =
            let directory = System.IO.Path.GetDirectoryName project

            System.Text.RegularExpressions.Regex.Matches (System.IO.File.ReadAllText project, "<ProjectReference Include=\"([^\"]+)\"")
            |> Seq.map (fun found -> fullPath (System.IO.Path.Combine (directory, found.Groups[1].Value.Replace('\\', '/'))))
            |> Seq.filter sources.Contains
            |> List.ofSeq

        let rec depth (project: string) : int =
            match references project with
            | [] -> 0
            | referenced -> 1 + (referenced |> List.map depth |> List.max)

        sourceProjects
        |> List.groupBy (fun project -> depth (fullPath project.Path))
        |> List.sortBy fst
        |> List.map snd

    /// The source projects that pack: every one except those with `<IsPackable>false</IsPackable>`.
    let packableProjects =
        sourceProjects
        |> List.filter (fun project -> not ((System.IO.File.ReadAllText project.Path).Contains "<IsPackable>false</IsPackable>"))

    let testProjects =
        projects
        |> List.filter _.RelativePath.StartsWith("tests")

    /// The signal map model reads the trace log, so its tests compile only in a traced build.
    let mapsTests = "docs/maps/tests"

    /// The projects `analyze` checks, by group; each type-checks under the analyzers CLI. Fable-only projects are left
    /// out, and so is examples/Ranvier.FuncUI.Workload/Workload.fsproj, which references a FuncUI checkout through
    /// $(FuncUIRoot). The `library` group also runs the traced pass over src/Ranvier.
    let analyzedGroups =
        [
            "library",
            [
                "src/Ranvier/Ranvier.fsproj"
                "src/Ranvier.Query/Ranvier.Query.fsproj"
                "src/Ranvier.Elmish/Ranvier.Elmish.fsproj"
                "src/Ranvier.Wire/Ranvier.Wire.fsproj"
                "src/Fable.Ranvier/Fable.Ranvier.fsproj"
                "src/Fable.Ranvier.Anime/Fable.Ranvier.Anime.fsproj"
                "tests/Ranvier.Tests/Ranvier.Tests.fsproj"
                "tests/Ranvier.Wire.Tests/Ranvier.Wire.Tests.fsproj"
            ]
            "view",
            [
                "src/Ranvier.View/Ranvier.View.fsproj"
                "src/Ranvier.View.Dom/Ranvier.View.Dom.fsproj"
                "src/Ranvier.View.Anime/Ranvier.View.Anime.fsproj"
                "experiments/Ranvier.View.Arms/Ranvier.View.Arms.fsproj"
                "examples/Fable.Ranvier.Playground/Playground.fsproj"
                "examples/Fable.Ranvier.Playground/tests/DomTests.fsproj"
                "docs/samples/view/Ranvier.Docs.ViewSamples.fsproj"
            ]
            "examples",
            [
                "examples/Ranvier.Motion/Motion.fsproj"
                "examples/Ranvier.Motion/Server/ServerChecks.fsproj"
                "examples/Ranvier.Motion/tests/MotionTests.fsproj"
                "examples/Ranvier.Hydration/Shared.fsproj"
                "examples/Ranvier.Hydration/Server/Server.fsproj"
                "examples/Ranvier.Query.Dictionary/Ranvier.Query.Dictionary.fsproj"
                "examples/Ranvier.FuncUI.Workload/Workload.Model.fsproj"
            ]
        ]

    /// The Motion example pages, which the docs site serves under examples/motion/.
    let motionExamples =
        System.IO.Path.Combine (__REPOSITORY_DIRECTORY__, "examples/Ranvier.Motion")

    let sourceProjectsMap =
        sourceProjects
        |> List.map (_.Name >> Str.replaceChar '.' '-' >> Str.toLower)
        |> List.zip
        |> funApply sourceProjects
        |> Map.ofList

module Options =
    let config = Baked.Dotnet.configOrRelease

    let quick =
        Input.option<bool> "--quick"
        |> Input.alias "-q"
        |> Input.description "Skips installs and restores"

    let format =
        Input.option<bool> "--format"
        |> Input.alias "-f"
        |> Input.description "Formats the source files prior to building/testing/publishing"

    let dryFormat =
        Input.option<bool> "--dry-format"
        |> Input.description "Checks if the source files require formatting"

    let skipTests =
        Input.option<bool> "--skip-tests"
        |> Input.description "Skips running the test suite"

    let watch =
        Input.option<bool> "--watch"
        |> Input.description "Runs the operation in watch mode"

    let npmCleanInstall =
        Input.option<bool> "--clean-install"
        |> Input.alias "--ci"
        |> Input.description "Runs npm install with --ci"

    let dryPublish =
        Input.option<bool> "--dry-publish"
        |> Input.description "Lists the packages publish would push, and pushes none"

    let apiKey =
        Baked.NuGet.apiKey.option
        |> Input.alias "--api-key"
        |> Input.alias "-k"

    let analyzeGroups =
        Input.option<string list> "--group"
        |> Input.arity Arity.ZeroOrMore
        |> Input.allowMultipleArgumentsPerToken
        |> Input.acceptOnlyFromAmong (
            Spec.analyzedGroups
            |> List.map fst
            |> List.toArray
        )
        |> Input.description "The analyzed project groups; every group when omitted"

    let analyzeBuild =
        Input.option<bool> "--build"
        |> Input.description "Builds the analyzed projects first"

    let projects =
        Input.option<string list> "--project"
        |> Input.alias "-p"
        |> Input.arity Arity.ZeroOrMore
        |> Input.allowMultipleArgumentsPerToken
        |> Input.acceptOnlyFromAmong (Spec.sourceProjectsMap |> Map.keys |> Seq.toArray)
        |> Input.customParser (fun res ->
            res.Tokens
            |> Seq.toList
            |> List.map (
                _.Value
                >> Map.find
                >> funApply Spec.sourceProjectsMap
                >> _.Path
            ))

module Stage =
    let restore =
        input {
            let! quick = Options.quick

            return
                stage "restore" {
                    quiet
                    workingDir Repo.FileSystem.``.``
                    when' (not quick)
                    parallel'
                    stage "restore solution" { run $"dotnet restore {Repo.Project.SolutionFile} -v q" }
                    stage "restore tools" { run "dotnet tool restore -v q" }
                }
        }

    let clean =
        input {
            let! quick = Options.quick

            return
                stage "clean" {
                    when' (not quick)

                    run (
                        async {
                            !!"**/**/bin" ++ "temp"
                            -- "bin"
                            -- "**/node_modules/**"
                            |> Shell.cleanDirs
                        }
                    )
                }
        }

    let fableClean =
        input {
            let! quick = Options.quick

            return
                stage "fickl clean" {
                    when' (not quick)
                    workingDir Repo.FileSystem.``.``
                    run "dotnet fickl clean -e .fs.js --yes"
                }
        }

    let format (formatInput: InputSpec<bool>) (dryRun: InputSpec<bool>) =
        input {
            let! format = formatInput
            and! dryRun = dryRun

            // Relative paths in batches, so each command stays under the Windows command-line limit in a deep worktree.
            let commands =
                Spec.formattingSourceFiles
                |> Seq.map (fun path -> sprintf "\"%s\"" (System.IO.Path.GetRelativePath (__REPOSITORY_DIRECTORY__, path)))
                |> Seq.fold
                    (fun (batches: string list list) file ->
                        match batches with
                        | current :: rest when
                            (current |> List.sumBy (fun f -> f.Length + 1))
                            + file.Length
                                <
                                24000
                            ->
                            (file :: current) :: rest
                        | _ -> [ file ] :: batches)
                    []
                |> List.rev
                |> List.map (List.rev >> String.concat " ")
                |> List.map (
                    if dryRun then
                        sprintf "dotnet fantomas %s --check"
                    else
                        sprintf "dotnet fantomas %s"
                )

            return
                stage "format" {
                    when' (format || dryRun)
                    quiet
                    workingDir __REPOSITORY_DIRECTORY__

                    for index, command in List.indexed commands do
                        stage $"format batch {index + 1}" { run command }
                }
        }

    let build =
        input {
            let! config = Options.config

            // The core builds once, first; each later tier then builds in parallel against the outputs of the tiers before it.
            let core, packages =
                match Spec.sourceTiers with
                | core :: packages -> core, packages
                | [] -> [], []

            return
                stage "build" {
                    quiet

                    for { Name = name; Path = project } in core do
                        stage $"build {name}" { run (cmd $"dotnet build {project} -c {config} -v q") }

                    for tier, projects in List.indexed packages do
                        stage $"build packages, tier {tier + 1}" {
                            parallel'

                            for { Name = name; Path = project } in projects do
                                stage $"build {name}" { run (cmd $"dotnet build {project} -c {config} -v q --no-dependencies") }
                        }
                }
        }

    let compile =
        input {
            let! watch = Options.watch
            and! projects = Options.projects
            and! config = Options.config

            let projects =
                if List.isEmpty projects then
                    Spec.sourceProjects |> List.map _.Path
                else
                    projects

            return
                stage "compile" {
                    quiet

                    if watch then
                        for project in projects do
                            stage "watch" { run (cmd $"dotnet fickl watch {project} -e .fs.js -c {config} -o output -s --optimize") }
                    else
                        for project in projects do
                            stage $"compile {project}" { run (cmd $"dotnet fickl {project} -e .fs.js -c {config} --noCache -o output") }
                }
        }

    let pack =
        input {
            let! projects = Options.projects
            and! config = Options.config

            return
                stage "pack" {
                    quiet

                    projects
                    |> function
                        | [] -> Spec.packableProjects |> List.map _.Path
                        | projects -> projects
                    |> List.map (fun project ->
                        stage $"pack {project}" {
                            run (cmd $"dotnet pack {project} -c {config} -v q --no-build --no-restore -o {Repo.VirtualFileSystem.bin.ToString ()}")
                        })
                }
        }

    /// Packs each source project again as its `.Traced` package: the same assemblies built with RanvierTrace=true.
    /// Runs after `pack`, since both builds share one output directory.
    let packTraced =
        input {
            let! projects = Options.projects
            and! config = Options.config
            and! skipTests = Options.skipTests

            return
                stage "pack traced" {
                    quiet

                    stage "test traced" {
                        when' (not skipTests)

                        for { Name = name; Path = path } in Spec.testProjects do
                            stage $"test traced {name}" { run (cmd $"dotnet test {path} -c {config} -p:RanvierTrace=true -v q") }

                        stage "test traced Ranvier.Docs.Maps.Tests" {
                            run (cmd $"dotnet test {Spec.mapsTests} -c {config} -p:RanvierTrace=true -v q")
                        }
                    }

                    projects
                    |> function
                        | [] -> Spec.packableProjects |> List.map _.Path
                        | projects -> projects
                    |> List.map (fun project ->
                        stage $"pack traced {project}" {
                            run (cmd $"dotnet pack {project} -c {config} -p:RanvierTrace=true -v q -o {Repo.VirtualFileSystem.bin.ToString ()}")
                        })
                }
        }

    /// Packs the analyzers into the output directory; publish pushes them with the libraries.
    let packAnalyzers =
        input {
            let! config = Options.config

            return
                stage "pack analyzers" {
                    quiet

                    run (
                        cmd
                            $"dotnet pack tools/Ranvier.Analyzers/Ranvier.Analyzers.fsproj -c {config} -v q -o {Repo.VirtualFileSystem.bin.ToString ()}"
                    )
                }
        }

    /// Pushes the packages of the output directory whose dependencies all resolve on nuget.org, on a source nuget.config
    /// maps to them, or in the same push, with the key masked
    /// in every printed form of the step, then fails naming each package it held back.
    /// `--dry-publish` lists the packages and what a push would accept or refuse, fails when there are none, and pushes
    /// nothing, whatever key is set. Without a key or `--dry-publish`, the stage is inactive and fails the pipeline.
    let publish =
        input {
            let! apiKey = Options.apiKey
            and! dryPublish = Options.dryPublish

            let directory = Repo.VirtualFileSystem.bin.ToString ()
            let packages = System.IO.Path.Combine (directory, "*.nupkg")
            // The packages .github/scripts/feed.cjs refuses: a dependency resolves on no applicable feed and not in this push.
            let refused = System.IO.Path.Combine (directory, "refused")

            return
                stage "publish" {
                    quiet
                    failIfIgnored
                    when' (dryPublish || apiKey.IsSome)

                    if dryPublish then
                        stage "dry publish" {
                            run (fun _ ->
                                let files =
                                    System.IO.Directory.GetFiles (directory, "*.nupkg")
                                    |> Array.sort

                                if Array.isEmpty files then
                                    failwith $"No package matches {packages}."

                                for file in files do
                                    printfn $"Packed {System.IO.Path.GetFileName file}")

                            run "node" $".github/scripts/feed.cjs {directory} --dry"
                        }
                    else
                        whenSome apiKey (fun key ->
                            stage "push" {
                                run (fun _ ->
                                    if System.IO.Directory.Exists refused then
                                        System.IO.Directory.Delete (refused, true))

                                run "node" $".github/scripts/feed.cjs {directory} --refuse {refused}"

                                run (
                                    cmd $"dotnet nuget push {packages} -s https://api.nuget.org/v3/index.json --skip-duplicate"
                                    |> Cmd.secretOption "-k" key
                                )

                                run (fun _ ->
                                    let held =
                                        if System.IO.Directory.Exists refused then
                                            System.IO.Directory.GetFiles (refused, "*.nupkg")
                                            |> Array.map System.IO.Path.GetFileName
                                        else
                                            [||]

                                    if not (Array.isEmpty held) then
                                        failwith $"""Not pushed, a dependency is missing from every applicable feed: {String.concat ", " held}""")
                            })
                }
        }

    let runTests =
        input {
            let! config = Options.config
            and! skipTests = Options.skipTests

            return
                stage "run tests" {
                    quiet
                    when' (not skipTests)

                    for { Name = name; Path = path } in Spec.testProjects do
                        stage $"run {name}" { run (cmd $"dotnet test {path} -c {config} -v q") }
                }
        }

    /// Rebuilds Ranvier traced into the output directory the untraced pack reads; it must not run between `build` and `pack`.
    let runMapsTests =
        input {
            let! config = Options.config
            and! skipTests = Options.skipTests

            return
                stage "run Ranvier.Docs.Maps.Tests" {
                    quiet
                    when' (not skipTests)
                    run (cmd $"dotnet test {Spec.mapsTests} -c {config} -p:RanvierTrace=true -v q")
                }
        }

    let testFable =
        input {
            let! quick = Options.quick
            and! cleanInstall = Options.npmCleanInstall

            return
                stage "test fable" {
                    workingDir Repo.FileSystem.``.``

                    stage "npm install" {
                        quiet
                        when' (not quick)
                        run (if cleanInstall then "npm ci" else "npm install")
                    }

                    stage "compile tests" {
                        quiet
                        run "dotnet fickl fable/Ranvier.Tests.Fable -e .fs.js -o dist/tests -c Release"
                    }
                    // RanvierTrace compiles the trace log into the Release build.
                    stage "compile traced tests" {
                        quiet
                        envVars [ ("RanvierTrace", "true") ]
                        run "dotnet fickl fable/Ranvier.Tests.Fable -e .fs.js -o dist/tests-traced -c Release"
                    }

                    stage "run and report" { run "node fable/Ranvier.Tests.Fable/Report.mjs" }
                }
        }

    let generateDocs =
        input {
            let! watch = Options.watch

            and! hotReload =
                Input.option<bool> "--hot-reload"
                |> Input.description "Runs dotnet in 'watch' mode. Changes to the doc setup will cause the server to restart."

            return
                stage "docs" {
                    quiet

                    stage "literate dependencies" {
                        run "dotnet build docs/maps/model/Ranvier.Docs.MapModel.fsproj -c Debug -p:RanvierTrace=true -v q"
                        run "dotnet fsi --nologo docs/maps/tests/fixtures/literate.fsx"
                    }

                    stage "map expectations" {
                        run "dotnet fsi docs/maps/validation/Generate.fsx"
                        envVars [ ("RanvierTrace", "true") ]
                        run "dotnet fickl docs/.nacara/map-validation/Checks.fsproj -o docs/.nacara/map-validation/out -e .mjs --noCache"
                        run "node docs/.nacara/map-validation/out/Main.mjs"
                    }

                    stage "perf data" {
                        run "git fetch origin perf-data"
                        run "git worktree add --detach .docs-perf-data origin/perf-data"
                        run "node" ".github/scripts/perf-site-data.cjs .docs-perf-data/runs docs/static/perf-data/index.json"
                        run "git" "worktree remove --force .docs-perf-data"
                    }

                    stage "browser examples" {
                        when' (not watch)

                        for name, directory in
                            [
                                "motion", Spec.motionExamples
                                "playground", System.IO.Path.Combine (__REPOSITORY_DIRECTORY__, "examples/Fable.Ranvier.Playground")
                                "hydration", System.IO.Path.Combine (__REPOSITORY_DIRECTORY__, "examples/Ranvier.Hydration")
                            ] do
                            stage $"{name} examples" {
                                workingDir directory
                                run "npm ci"
                                run "npm run build"
                            }
                    }

                    run (
                        Cmd.ofString "dotnet"
                        |> Cmd.args (
                            if hotReload then
                                [ "watch"; "--no-hot-reload" ]
                            else
                                List.singleton "run"
                        )
                        |> Cmd.args [ "--project"; "docs/docs.fsproj"; "--" ]
                        |> Cmd.arg (if watch then "watch" else "build")
                    )
                }
        }

module private Analyze =
    /// The CLI arguments common to both runs: the built analyzers, every RAN code an error, and a SARIF report.
    let private flags (report: string) =
        [
            "--analyzers-path"
            "tools/Ranvier.Analyzers/bin/Release/net10.0"
            "-c"
            "Release"
            "--fail-on-type-errors"
            "--treat-as-error"
            "RAN*"
            "--code-root"
            "."
            "--report"
            report
        ]

    /// Builds the analyzers, then runs them over the projects of the chosen `Spec.analyzedGroups` with every RAN code an
    /// error, writing a SARIF report per group to artifacts/analyzers. With `--build` the stage builds the analyzed
    /// projects first; without it, the solution must be built already.
    let stage =
        input {
            let! groups = Options.analyzeGroups
            and! build = Options.analyzeBuild

            let chosen =
                Spec.analyzedGroups
                |> List.filter (fun (name, _) -> List.isEmpty groups || List.contains name groups)

            return
                stage "analyze" {
                    stage "build analyzers" { run (cmd $"dotnet build tools/Ranvier.Analyzers/Ranvier.Analyzers.fsproj -c Release -v q") }

                    // The CLI reads a project's references from its build outputs.
                    for name, projects in chosen do
                        // One parallel build of the group, through a solution of its projects.
                        if build then
                            stage $"build the {name} projects" {
                                run (
                                    Cmd.ofList
                                        "dotnet"
                                        [
                                            "new"
                                            "sln"
                                            "--format"
                                            "slnx"
                                            "--name"
                                            name
                                            "--output"
                                            "artifacts/analyzers/build"
                                            "--force"
                                        ]
                                )

                                run (
                                    Cmd.ofList
                                        "dotnet"
                                        ([ "sln"; $"artifacts/analyzers/build/{name}.slnx"; "add" ]
                                         @ projects)
                                )

                                run (
                                    Cmd.ofList
                                        "dotnet"
                                        [
                                            "build"
                                            $"artifacts/analyzers/build/{name}.slnx"
                                            "-c"
                                            "Release"
                                            "-m"
                                            "-v"
                                            "q"
                                        ]
                                )
                            }
                        elif name = "examples" then
                            stage "build projects outside the solution" {
                                run (cmd $"dotnet build examples/Ranvier.Query.Dictionary/Ranvier.Query.Dictionary.fsproj -c Release -v q")
                            }

                        stage $"run analyzers over the {name} projects" {
                            run (fun _ ->
                                System.IO.Directory.CreateDirectory "artifacts/analyzers"
                                |> ignore)

                            run (
                                Cmd.ofList
                                    "dotnet"
                                    ([ "fsharp-analyzers"; "--project" ]
                                     @ projects
                                     @ [ "--exclude-files"; "**/Generated/**" ]
                                     @ flags $"artifacts/analyzers/ranvier-{name}.sarif")
                            )
                        }

                        // Checks the library again with RanvierTrace=true, the configuration of the docs precompile:
                        // the [<Conditional("RANVIER_TRACE")>] Tracer calls are in the typed tree only then.
                        if name = "library" then
                            stage "run analyzers over the traced library" {
                                run (
                                    Cmd.ofList
                                        "dotnet"
                                        ([
                                            "fsharp-analyzers"
                                            "--project"
                                            "src/Ranvier/Ranvier.fsproj"
                                            "--property"
                                            "RanvierTrace=true"
                                         ]
                                         @ flags "artifacts/analyzers/ranvier-traced.sarif")
                                )
                            }
                }
        }

module private Perf =
    open System
    open System.IO
    open System.Text.Json

    let private argument name description =
        Input.argument<string> name
        |> Input.description description

    let private absolute path =
        Path.GetFullPath (path, __REPOSITORY_DIRECTORY__)

    let private combine path leaf =
        Path.Combine (path, leaf)

    let private args files =
        files
        |> List.collect (fun file -> [ "--ab"; absolute file ])

    let private git arguments =
        Cmd.ofList "git" arguments

    let private dotnet arguments =
        Cmd.ofList "dotnet" arguments

    let private effect action =
        Operation.ofAsync (async { return action () })

    let private require condition message =
        if not condition then
            failwith message

    let private gitOutput (arguments: string list) =
        let info =
            Diagnostics.ProcessStartInfo ("git", WorkingDirectory = __REPOSITORY_DIRECTORY__, RedirectStandardOutput = true, UseShellExecute = false)

        for argument in arguments do
            info.ArgumentList.Add argument

        use git = Diagnostics.Process.Start info
        let output = git.StandardOutput.ReadToEnd ()
        git.WaitForExit ()
        require (git.ExitCode = 0) ("git " + String.concat " " arguments + " failed")
        output

    /// <summary>The <c>--package Name=Version</c> arguments for each <c>src</c> project that declares a <c>&lt;Version&gt;</c> at the revision.</summary>
    let private packageArguments sha =
        gitOutput [ "ls-tree"; "-r"; "--name-only"; sha; "--"; "src" ]
        |> fun listing ->
            listing.Split (
                '\n',
                StringSplitOptions.RemoveEmptyEntries
                ||| StringSplitOptions.TrimEntries
            )
        |> Array.filter (fun file -> file.EndsWith (".fsproj", StringComparison.Ordinal))
        |> Array.choose (fun file ->
            let version =
                Text.RegularExpressions.Regex.Match (gitOutput [ "show"; sha + ":" + file ], @"<Version>\s*([^<\s]+)\s*</Version>")

            if version.Success then
                Some (
                    Path.GetFileNameWithoutExtension file
                    + "="
                    + version.Groups[1].Value
                )
            else
                None)
        |> Array.toList
        |> List.collect (fun entry -> [ "--package"; entry ])

    let private readJson file =
        JsonDocument.Parse (File.ReadAllText file)

    /// <summary>
    /// The F#-to-JavaScript compiler command and version a revision's tool manifest declares: <c>fickl</c> when the
    /// manifest has it, otherwise <c>fable</c> (revisions before the switch to Fickl).
    /// </summary>
    let private compilerTool source =
        use manifest = readJson (combine source ".config/dotnet-tools.json")
        let tools = manifest.RootElement.GetProperty "tools"
        let mutable entry = Unchecked.defaultof<JsonElement>

        let command =
            if tools.TryGetProperty ("fickl", &entry) then
                "fickl"
            else
                entry <- tools.GetProperty "fable"
                "fable"

        command, entry.GetProperty("version").GetString()

    let private tool output =
        combine output "../head/worker/Ranvier.Counters.dll"
        |> Path.GetFullPath

    let build =
        input {
            let! reference = argument "REF" "Git revision to build"
            and! directory = argument "DIR" "Fresh output directory for the worktree and builds"
            let output = absolute directory
            let source = combine output "src"

            return
                stage "perf build" {
                    run (fun _ ->
                        require (not (Directory.Exists source)) "Perf source directory already exists; choose a fresh output directory."
                        Directory.CreateDirectory output |> ignore)

                    run (git [ "worktree"; "add"; "--detach"; source; reference ])

                    stage "side tools" {
                        workingDir source
                        run (dotnet [ "tool"; "restore" ])
                    }

                    for counters, folder in [ true, "counters-worker"; false, "worker" ] do
                        stage folder {
                            workingDir source

                            run (
                                dotnet
                                    [
                                        "build"
                                        combine source "bench/Ranvier.Counters"
                                        "-c"
                                        "Release"
                                        "-p:RanvierTrace=false"
                                        "-p:RanvierCounters=" + string counters
                                        "-o"
                                        combine output folder
                                    ]
                            )
                        }

                    for counters, folder in [ false, "plain"; true, "counters" ] do
                        stage ("fable " + folder) {
                            workingDir source
                            envVars [ "RanvierCounters", string counters; "RanvierTrace", "false" ]

                            runOperation (
                                effect (fun () -> compilerTool source)
                                |> Operation.bind (fun (command, _) ->
                                    execute (
                                        dotnet
                                            [
                                                command
                                                "fable/Ranvier.Counters"
                                                "-e"
                                                ".fs.js"
                                                "-o"
                                                combine output ("fable/" + folder)
                                                "--noCache"
                                                "--configuration"
                                                "Release"
                                            ]
                                    ))
                            )
                        }

                    stage "portable build metadata" {
                        workingDir source

                        runOperation (
                            effect (fun () -> compilerTool source)
                            |> Operation.bind (fun (command, version) ->
                                executeCapture (dotnet [ command; "--help" ])
                                |> Operation.map (fun help -> version, help))
                            |> Operation.bind (fun (manifestVersion, help) ->
                                effect (fun () ->
                                    let compiler =
                                        help.Stdout.Split ('\n')
                                        |> Array.tryFind (fun line -> line.Contains "F# to JavaScript compiler")
                                        |> Option.map (fun line -> line.Substring(0, line.IndexOf ':').Trim())
                                        |> Option.defaultValue ""

                                    require (compiler.Contains manifestVersion) "Resolved Fable compiler differs from the side's tool manifest."

                                    let libraryVersions =
                                        Directory.GetFiles (combine output "fable/plain", "package.json", SearchOption.AllDirectories)
                                        |> Array.choose (fun file ->
                                            use package = readJson file
                                            let mutable name = Unchecked.defaultof<JsonElement>

                                            if
                                                package.RootElement.TryGetProperty ("name", &name)
                                                && name.GetString () = "@fable-org/fable-library-js"
                                            then
                                                Some (package.RootElement.GetProperty("version").GetString())
                                            else
                                                None)
                                        |> Array.distinct

                                    require (libraryVersions.Length = 1) "Expected one generated fable-library version."
                                    use runtime = readJson (combine output "worker/Ranvier.Counters.runtimeconfig.json")

                                    let framework =
                                        runtime.RootElement.GetProperty("runtimeOptions").GetProperty("framework")

                                    let target = Version.Parse (framework.GetProperty("version").GetString())

                                    require
                                        (framework.GetProperty("name").GetString() = "Microsoft.NETCore.App"
                                         && target.Major = Environment.Version.Major
                                         && target.Minor = Environment.Version.Minor)
                                        "Worker runtime differs from build host; cannot label its actual runtime."

                                    File.WriteAllText (
                                        combine output "versions.json",
                                        JsonSerializer.Serialize
                                            {|
                                                Dotnet = string Environment.Version
                                                Fable = compiler
                                                FableLibrary = libraryVersions[0]
                                            |}
                                    )

                                    File.WriteAllText (combine output "fable/package.json", "{\"type\":\"module\"}")))
                        )
                    }
                }
        }

    /// <summary>The first line a process writes to standard output, trimmed.</summary>
    let private firstLine (command: string) (arguments: string list) =
        let info =
            Diagnostics.ProcessStartInfo (
                command,
                WorkingDirectory = __REPOSITORY_DIRECTORY__,
                RedirectStandardOutput = true,
                UseShellExecute = false
            )

        for argument in arguments do
            info.ArgumentList.Add argument

        use child = Diagnostics.Process.Start info
        let output = child.StandardOutput.ReadToEnd ()
        child.WaitForExit ()

        require
            (child.ExitCode = 0)
            (command
             + " "
             + String.concat " " arguments
             + " failed")

        let lines = output.Split '\n'
        lines[0].Trim()

    /// <summary>The processor's model name, or an empty string when the OS reports none.</summary>
    let private cpuName () =
        if OperatingSystem.IsWindows () then
            Environment.GetEnvironmentVariable "PROCESSOR_IDENTIFIER"
        elif OperatingSystem.IsLinux () then
            File.ReadAllLines "/proc/cpuinfo"
            |> Array.tryFind (fun line -> line.StartsWith "model name")
            |> Option.map (fun line -> line.Substring(line.IndexOf ':' + 1).Trim())
            |> Option.defaultValue ""
        elif OperatingSystem.IsMacOS () then
            firstLine "sysctl" [ "-n"; "machdep.cpu.brand_string" ]
        else
            ""

    let private variants = [ false, "untraced"; true, "traced" ]

    /// <summary>
    /// Builds the counter bench and its Fable harness untraced and traced, runs the counter scenarios and the trace
    /// growth probe of both on .NET and Node.js, runs the Playground's keyed-row harness on an untraced and a traced
    /// Chromium bundle, runs the BenchmarkDotNet <c>Trace</c> category, and writes <c>versions.json</c> and
    /// <c>report.md</c> into OUT.
    /// </summary>
    let trace =
        input {
            let! directory =
                argument "OUT" "Fresh output directory for the builds, measurements and report"

            and! short =
                Input.option<bool> "--short"
                |> Input.description "Runs BenchmarkDotNet's short job and one Chromium process: a smoke run, not a baseline"

            and! noChromium =
                Input.option<bool> "--no-chromium"
                |> Input.description "Skips the Chromium keyed-row runs"

            let output = absolute directory

            let playground =
                combine __REPOSITORY_DIRECTORY__ "examples/Fable.Ranvier.Playground"

            let chromiumModes = "--modes=ranvier,ranvier-sugar"

            let chromiumRuns =
                if short then
                    [ "--processes=1"; "--rounds=5" ]
                else
                    [ "--processes=3"; "--rounds=15" ]

            let worker variant =
                combine output ("dotnet/" + variant + "/Ranvier.Counters.dll")

            let fable variant =
                combine output ("fable/" + variant)

            let pmc = if OperatingSystem.IsLinux () then [] else [ "--no-pmc" ]
            let shortJob = if short then [ "--short" ] else []
            let job = if short then "short" else "default"

            return
                stage "perf trace" {
                    workingDir __REPOSITORY_DIRECTORY__

                    run (fun _ ->
                        require
                            (not (Directory.Exists output)
                             || Array.isEmpty (Directory.GetFileSystemEntries output))
                            "The perf trace output directory is not empty; choose a fresh one."

                        Directory.CreateDirectory output |> ignore)

                    run (dotnet [ "tool"; "restore" ])

                    for traced, variant in variants do
                        stage ("dotnet " + variant) {
                            run (
                                dotnet
                                    [
                                        "build"
                                        "bench/Ranvier.Counters"
                                        "-c"
                                        "Release"
                                        "-p:RanvierTrace="
                                        + (string traced).ToLowerInvariant()
                                        "-p:RanvierCounters=false"
                                        "-o"
                                        combine output ("dotnet/" + variant)
                                    ]
                            )
                        }

                        stage ("fable " + variant) {
                            envVars
                                [
                                    "RanvierCounters", "false"
                                    "RanvierTrace", (string traced).ToLowerInvariant()
                                ]

                            runOperation (
                                effect (fun () -> compilerTool __REPOSITORY_DIRECTORY__)
                                |> Operation.bind (fun (command, _) ->
                                    execute (
                                        dotnet
                                            [
                                                command
                                                "fable/Ranvier.Counters"
                                                "-e"
                                                ".fs.js"
                                                "-o"
                                                combine (fable variant) "plain"
                                                "--noCache"
                                                "--configuration"
                                                "Release"
                                            ]
                                    ))
                            )
                        }

                    stage "fable package" { run (fun _ -> File.WriteAllText (combine output "fable/package.json", "{\"type\":\"module\"}")) }

                    for _, variant in variants do
                        stage ("counters " + variant) {
                            run (
                                dotnet (
                                    [
                                        worker variant
                                        "--fable"
                                        fable variant
                                        "--out"
                                        combine output ("counters/" + variant)
                                    ]
                                    @ pmc
                                )
                            )
                        }

                        stage ("growth " + variant) {
                            run (fun _ ->
                                Directory.CreateDirectory (combine output "growth")
                                |> ignore)

                            run (
                                dotnet
                                    [
                                        worker variant
                                        "trace-growth"
                                        "--out"
                                        combine output ("growth/dotnet-" + variant + ".json")
                                    ]
                            )

                            run (
                                Cmd.ofList
                                    "node"
                                    [
                                        "--expose-gc"
                                        "--single-threaded"
                                        combine (fable variant) "plain/Main.fs.js"
                                        "--trace-growth"
                                        "--out"
                                        combine output ("growth/node-" + variant + ".json")
                                    ]
                            )
                        }

                    if not noChromium then
                        stage "chromium packages" {
                            workingDir playground
                            run "npm ci"
                        }

                        // Each variant compiles the Playground's Fable output, so the keyed-heap bundle follows the
                        // keyed-rows build of the same variant.
                        for traced, variant in variants do
                            stage ("chromium " + variant) {
                                workingDir playground
                                envVars [ ("RanvierTrace", (string traced).ToLowerInvariant()) ]

                                run (
                                    Cmd.ofList
                                        "node"
                                        ([
                                            "benchmarks/frameworks/keyed-rows.mjs"
                                            "--build"
                                            "--dist=generated-keyed-" + variant
                                            chromiumModes
                                            "--out=" + combine output "chromium"
                                            "--label=rows-" + variant
                                         ]
                                         @ chromiumRuns)
                                )

                                run (
                                    Cmd.ofList
                                        "node"
                                        [
                                            "benchmarks/frameworks/keyed-heap.mjs"
                                            chromiumModes
                                            (if short then "--processes=1" else "--processes=2")
                                            "--out=" + combine output "chromium"
                                            "--label=heap-" + variant
                                        ]
                                )
                            }

                    stage "benchmarks" {
                        run (
                            dotnet (
                                [
                                    "run"
                                    "--project"
                                    "bench/Ranvier.Benchmarks"
                                    "-c"
                                    "Release"
                                    "--"
                                    "--anyCategories"
                                    "Trace"
                                    "--filter"
                                    "*"
                                    "--artifacts"
                                    combine output "benchmarks"
                                ]
                                @ shortJob
                            )
                        )
                    }

                    stage "versions" {
                        run (fun _ ->
                            let compiler, compilerVersion = compilerTool __REPOSITORY_DIRECTORY__

                            let fableLibrary =
                                Directory.GetFiles (fable "untraced", "package.json", SearchOption.AllDirectories)
                                |> Array.tryPick (fun file ->
                                    use package = readJson file
                                    let mutable name = Unchecked.defaultof<JsonElement>

                                    if
                                        package.RootElement.TryGetProperty ("name", &name)
                                        && name.GetString () = "@fable-org/fable-library-js"
                                    then
                                        Some (package.RootElement.GetProperty("version").GetString())
                                    else
                                        None)
                                |> Option.defaultValue ""

                            let revision = (gitOutput [ "rev-parse"; "HEAD" ]).Trim()

                            let dirty =
                                (gitOutput [ "status"; "--porcelain"; "--untracked-files=no" ]).Trim()
                                <> ""

                            File.WriteAllText (
                                combine output "versions.json",
                                JsonSerializer.Serialize (
                                    {|
                                        Source = if dirty then revision + "-dirty" else revision
                                        Dotnet = string Environment.Version
                                        Fable = compiler + " " + compilerVersion
                                        FableLibrary = fableLibrary
                                        Node = firstLine "node" [ "--version" ]
                                        OS = Runtime.InteropServices.RuntimeInformation.OSDescription
                                        CPU = cpuName ()
                                        LogicalProcessors = Environment.ProcessorCount
                                        BenchmarkJob = job
                                    |},
                                    JsonSerializerOptions (WriteIndented = true)
                                )
                            ))
                    }

                    stage "report" { run (dotnet [ worker "untraced"; "trace-report"; "--dir"; output ]) }
                }
        }

    let ab =
        input {
            let! leg = argument "LEG" "Measurement leg: arm64-counters or x64-timing"
            and! baseSha = argument "BASE_SHA" "Commit of the base build"
            and! baseDir = argument "BASE_DIR" "perf build output of the base"
            and! headSha = argument "HEAD_SHA" "Commit of the head build"
            and! headDir = argument "HEAD_DIR" "perf build output of the head"
            and! output = argument "OUT" "File to write the measurements to"

            return
                stage "perf alternating measurements" {
                    run (
                        dotnet
                            [
                                combine (absolute headDir) "worker/Ranvier.Counters.dll"
                                "ab"
                                "--leg"
                                leg
                                "--base"
                                baseSha
                                absolute baseDir
                                "--head"
                                headSha
                                absolute headDir
                                "--out"
                                absolute output
                            ]
                    )
                }
        }

    let compare =
        input {
            let! baseSha = argument "BASE_SHA" "Base commit the measurements must name"
            and! headSha = argument "HEAD_SHA" "Head commit the measurements must name"

            and! label =
                argument "LABEL" "Whether the pull request carries the perf-accepted label: true or false"

            and! directory = argument "OUT" "Directory to write verdict.json and comment.md to"

            and! files =
                Input.argument<string list> "AB"
                |> Input.description "perf ab measurement files, one per leg"

            let output = absolute directory
            let verdictFile = combine output "verdict.json"

            return
                stage "performance verdict" {
                    run (fun _ ->
                        Directory.CreateDirectory output |> ignore

                        for file in [ verdictFile; combine output "comment.md" ] do
                            File.Delete file

                        require (not files.IsEmpty) "At least one AB artifact is required."

                        for file in files do
                            use artifact = readJson (absolute file)

                            require
                                (artifact.RootElement.GetProperty("Base").GetString() = baseSha
                                 && artifact.RootElement.GetProperty("Head").GetString() = headSha)
                                "AB artifact does not match requested immutable revisions.")

                    run (
                        git
                            [
                                "log"
                                "-z"
                                "--format=%B"
                                "--output=" + combine output "messages.bin"
                                baseSha + ".." + headSha
                                "--"
                            ]
                    )

                    runOperation (
                        attemptCapture (
                            dotnet (
                                [ tool output; "compare" ]
                                @ args files
                                @ [
                                    "--label"
                                    label
                                    "--messages"
                                    combine output "messages.bin"
                                    "--out"
                                    output
                                ]
                            )
                        )
                        |> Operation.bind (fun result ->
                            require (result.ExitCode = 0 || result.ExitCode = 1) ("Compare process failed: " + result.Stderr)

                            require
                                (File.Exists verdictFile
                                 && File.Exists (combine output "comment.md"))
                                ("Compare produced no fresh report: "
                                 + result.Stderr)

                            use report = readJson verdictFile
                            let acceptance = report.RootElement.GetProperty ("Acceptance")

                            let verdicts =
                                report.RootElement.GetProperty("Verdicts").EnumerateArray()
                                |> Seq.toArray

                            require (verdicts.Length = files.Length) "Verdict count differs from the measurement artifacts."

                            let failed =
                                acceptance.GetProperty("Unknown").GetArrayLength() > 0
                                || verdicts
                                   |> Array.exists (fun v -> v.GetProperty("Fails").GetBoolean())

                            require ((result.ExitCode = 1) = failed) "Compare exit code contradicts its fresh verdict."

                            let expectedLegs =
                                files
                                |> List.map (fun f -> use j = readJson (absolute f) in j.RootElement.GetProperty("Leg").GetString())
                                |> Set.ofList

                            require
                                (expectedLegs =
                                    (verdicts
                                     |> Array.map (fun v -> v.GetProperty("Leg").GetString())
                                     |> Set.ofArray))
                                "Unexpected verdict legs."

                            verdicts
                            |> Array.collect (fun v ->
                                v.GetProperty("Warnings").EnumerateArray()
                                |> Seq.map _.GetString()
                                |> Seq.toArray)
                            |> Array.map (fun warning ->
                                annotate
                                    { Annotation.warning warning with
                                        Title = ValueSome "Performance (x64 timing)"
                                    })
                            |> Array.toList
                            |> Operation.sequence
                            |> Operation.map (fun _ -> require (not failed) "Performance verdict rejected; see the report artifact."))
                    )
                }
        }

    let record =
        input {
            let! sha = argument "SHA" "Commit to record"

            and! file =
                argument "OUT_FILE" "Run file to write, under the history's runs directory"

            and! files =
                Input.argument<string list> "AB"
                |> Input.description "perf ab measurement files, one per leg"

            let output = absolute file
            let directory = Path.GetDirectoryName output
            let message = output + ".message"

            return
                stage "record performance history" {
                    run (fun _ -> Directory.CreateDirectory directory |> ignore)
                    run (git [ "log"; "-1"; "--format=%B"; "--output=" + message; sha; "--" ])

                    runOperation (
                        executeCapture (git [ "rev-parse"; sha + "^1" ])
                        |> Operation.bind (fun parent ->
                            executeCapture (git [ "log"; "-1"; "--format=%cI"; sha; "--" ])
                            |> Operation.bind (fun date ->
                                executeCapture (git [ "log"; "-1"; "--format=%s"; sha; "--" ])
                                |> Operation.bind (fun subject ->
                                    execute (
                                        dotnet (
                                            [
                                                combine __REPOSITORY_DIRECTORY__ "out/head/worker/Ranvier.Counters.dll"
                                                "record"
                                            ]
                                            @ args files
                                            @ [
                                                "--commit"
                                                sha
                                                "--parent"
                                                parent.Stdout.Trim ()
                                                "--date"
                                                date.Stdout.Trim ()
                                                "--subject"
                                                subject.Stdout.TrimEnd ('\r', '\n')
                                                "--message"
                                                message
                                                "--out"
                                                output
                                            ]
                                            @ packageArguments sha
                                        )
                                    ))))
                    )

                    run (fun _ -> File.Delete message)

                    run (
                        dotnet
                            [
                                combine __REPOSITORY_DIRECTORY__ "out/head/worker/Ranvier.Counters.dll"
                                "timeline"
                                "--runs"
                                directory
                                "--out"
                                combine directory "../timeline.md"
                                |> Path.GetFullPath
                            ]
                    )
                }
        }

exit
<| rootCommandOfScript {
    name "build.fsx"
    description "Build CLI"
    workingDir __REPOSITORY_DIRECTORY__

    command "perf" {
        description "CI: counter-bench measurements, verdicts and history"
        hidden

        command "build" {
            description "Builds the counter bench of a revision in a fresh worktree"
            Perf.build
        }

        command "trace" {
            description "Measures traced against untraced builds on .NET and Node.js and writes the report"
            Perf.trace
        }

        command "ab" {
            description "Measures a base and a head build in alternating processes"
            Perf.ab
        }

        command "compare" {
            description "Applies the regression verdict to the measurements and writes the report"
            Perf.compare
        }

        command "record" {
            description "Records a commit's measurements in the performance history and rebuilds its timeline"
            Perf.record
        }
    }

    command "build" {
        description "Builds the solution"
        Stage.restore
        Stage.build
    }

    command "compile" {
        description "Compiles the Fable projects to JavaScript"
        Stage.restore
        Stage.clean
        Stage.fableClean
        Stage.format (InputSpec.ofInput Options.format) (InputSpec.ofInput Options.dryFormat)
        Stage.compile
    }

    command "publish" {
        description "Publishes the solution to NuGet"
        Stage.restore
        Stage.clean
        Stage.fableClean
        Stage.format (InputSpec.ofInput Options.format) (InputSpec.ofInput Options.dryFormat)
        Stage.build
        Stage.runTests
        Stage.fableClean
        Stage.pack
        Stage.packTraced
        Stage.packAnalyzers
        Stage.publish
    }

    command "bump" {
        description "Bumps the version of the project"

        Baked.SemVer.Stages.bumpArgument (
            InputSpec.ofInput Options.projects
            |> InputSpec.map (function
                | [] -> Spec.sourceProjects |> List.map _.Path
                | paths -> paths)
        )
    }

    command "docs" {
        description "Generates the documentation (--watch to serve it)"
        Stage.restore
        Stage.generateDocs
    }

    command "analyze" {
        description "Runs the Ranvier analyzers over the repository's projects, every finding an error; build first, or pass --build"
        Stage.restore
        Analyze.stage
    }

    command "test" {
        alias "tests"
        description "Runs the test suite"
        Stage.restore
        Stage.clean
        Stage.fableClean
        Stage.format (InputSpec.ofInput Options.format) (InputSpec.ofInput Options.dryFormat)
        Stage.runTests
        Stage.runMapsTests
    }

    command "test-fable" {
        description "Runs the test suite under Fable and Node.js, untraced and traced, and writes docs/.ai/fable-compat.md"
        Stage.restore
        Stage.testFable
    }

    command "format" {
        alias "apply-style"
        description "Formats the source files"
        Stage.restore
        Stage.clean
        Stage.format (InputSpec.ret true) (InputSpec.ofInput Options.dryFormat)
    }
}
