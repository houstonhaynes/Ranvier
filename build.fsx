#r "nuget: Partas.Build, 0.8.1"
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

    let projects = Repo.Project.AllProjects ()

    let sourceProjects =
        projects
        |> List.filter _.RelativePath.StartsWith("src")

    let testProjects =
        projects
        |> List.filter _.RelativePath.StartsWith("tests")

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

    let apiKey =
        Baked.NuGet.apiKey.option
        |> Input.alias "--api-key"
        |> Input.alias "-k"

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
                            !!"**/**/bin" ++ "temp" -- "bin" -- "**/node_modules/**"
                            |> Shell.cleanDirs
                        }
                    )
                }
        }

    let fableClean =
        input {
            let! quick = Options.quick

            return
                stage "fable clean" {
                    when' (not quick)
                    workingDir Repo.FileSystem.``.``
                    run "dotnet fable clean -e .fs.js --yes"
                }
        }

    let format (formatInput: InputSpec<bool>) (dryRun: InputSpec<bool>) =
        input {
            let! format = formatInput
            and! dryRun = dryRun

            let commandString =
                Spec.formattingSourceFiles
                |> Seq.map (sprintf "\"%s\"")
                |> String.concat " "
                |> if dryRun then
                       sprintf "dotnet fantomas %s --check"
                   else
                       sprintf "dotnet fantomas %s"

            return
                stage "format" {
                    when' (format || dryRun)
                    quiet
                    run commandString
                }
        }

    let build =
        input {
            let! config = Options.config

            return
                stage "build" {
                    quiet
                    parallel'

                    for { Name = name; Path = project } in Spec.sourceProjects do
                        stage $"build {name}" { run (cmd $"dotnet build {project} -c {config} -v q") }
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
                            stage "watch" { run (cmd $"dotnet fable watch {project} -e .fs.js -c {config} -o output -s --optimize") }
                    else
                        for project in projects do
                            stage $"compile {project}" { run (cmd $"dotnet fable {project} -e .fs.js -c {config} --noCache -o output") }
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
                        | [] -> Spec.sourceProjects |> List.map _.Path
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
                    }

                    projects
                    |> function
                        | [] -> Spec.sourceProjects |> List.map _.Path
                        | projects -> projects
                    |> List.map (fun project ->
                        stage $"pack traced {project}" {
                            run (cmd $"dotnet pack {project} -c {config} -p:RanvierTrace=true -v q -o {Repo.VirtualFileSystem.bin.ToString ()}")
                        })
                }
        }

    let publish =
        input {
            let! apiKey = Options.apiKey

            let packages =
                System.IO.Path.Combine (Repo.VirtualFileSystem.bin.ToString (), "*.nupkg")

            return
                stage "publish" {
                    quiet
                    failIfIgnored
                    when' apiKey.IsSome
                    run (cmd $"dotnet nuget push {packages} -k {apiKey.Value} -s https://api.nuget.org/v3/index.json --skip-duplicate")
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
                        run "dotnet fable fable/Ranvier.Tests.Fable -e .fs.js -o dist/tests -c Release"
                    }
                    // RanvierTrace compiles the trace log into the Release build.
                    stage "compile traced tests" {
                        quiet
                        envVars [ ("RanvierTrace", "true") ]
                        run "dotnet fable fable/Ranvier.Tests.Fable -e .fs.js -o dist/tests-traced -c Release"
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
                        run "dotnet fable docs/.nacara/map-validation/Checks.fsproj -o docs/.nacara/map-validation/out -e .mjs --noCache"
                        run "node docs/.nacara/map-validation/out/Main.mjs"
                    }

                    stage "perf data" {
                        run "git fetch origin perf-data"
                        run "git worktree add --detach .docs-perf-data origin/perf-data"
                        run "node" ".github/scripts/perf-site-data.cjs .docs-perf-data/runs docs/static/perf-data/index.json"
                        run "git" "worktree remove --force .docs-perf-data"
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
            Diagnostics.ProcessStartInfo (
                "git",
                WorkingDirectory = __REPOSITORY_DIRECTORY__,
                RedirectStandardOutput = true,
                UseShellExecute = false
            )

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
        |> fun listing -> listing.Split ('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
        |> Array.filter (fun file -> file.EndsWith (".fsproj", StringComparison.Ordinal))
        |> Array.choose (fun file ->
            let version =
                Text.RegularExpressions.Regex.Match (gitOutput [ "show"; sha + ":" + file ], @"<Version>\s*([^<\s]+)\s*</Version>")

            if version.Success then
                Some (Path.GetFileNameWithoutExtension file + "=" + version.Groups[1].Value)
            else
                None)
        |> Array.toList
        |> List.collect (fun entry -> [ "--package"; entry ])

    let private readJson file =
        JsonDocument.Parse (File.ReadAllText file)

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

                            run (
                                dotnet
                                    [
                                        "fable"
                                        "fable/Ranvier.Counters"
                                        "-e"
                                        ".fs.js"
                                        "-o"
                                        combine output ("fable/" + folder)
                                        "--noCache"
                                        "--configuration"
                                        "Release"
                                    ]
                            )
                        }

                    stage "portable build metadata" {
                        workingDir source

                        runOperation (
                            executeCapture (dotnet [ "fable"; "--version" ])
                            |> Operation.bind (fun version ->
                                effect (fun () ->
                                    use tools = readJson (combine source ".config/dotnet-tools.json")

                                    let compiler =
                                        tools.RootElement.GetProperty("tools").GetProperty("fable").GetProperty("version").GetString()

                                    require (version.Stdout.Contains compiler) "Resolved Fable compiler differs from the side's tool manifest."

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

    command "test" {
        alias "tests"
        description "Runs the test suite"
        Stage.restore
        Stage.clean
        Stage.fableClean
        Stage.format (InputSpec.ofInput Options.format) (InputSpec.ofInput Options.dryFormat)
        Stage.runTests
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
