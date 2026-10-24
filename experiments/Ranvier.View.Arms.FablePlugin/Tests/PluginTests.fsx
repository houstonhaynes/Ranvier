// Compiles the plugin fixtures with dotnet fickl: the emitted sites against expected/Sites.js, and each bad fixture
// against its diagnostic (exit code 1, file, line, exact message). Needs the plugin built in Release.
//   dotnet fsi experiments/Ranvier.View.Arms.FablePlugin/Tests/PluginTests.fsx            checks
//   dotnet fsi experiments/Ranvier.View.Arms.FablePlugin/Tests/PluginTests.fsx --update   rewrites expected/Sites.js
// Exits 1 on a failure.
open System
open System.Diagnostics
open System.IO
open System.Text.RegularExpressions

let here = __SOURCE_DIRECTORY__
let repo = Path.GetFullPath (Path.Combine (here, "..", "..", ".."))
let project = Path.Combine (here, "Fixtures", "PluginFixtures.fsproj")
let update = fsi.CommandLineArgs |> Array.contains "--update"
let scratch = Path.Combine (Path.GetTempPath (), "ranvier-arms-plugin-tests")
let failures = ResizeArray<string>()

let check (name: string) (ok: bool) (detail: string) =
    if ok then
        printfn "PASS %s" name
    else
        failures.Add (name + ": " + detail)
        printfn "FAIL %s" name

/// Runs a process from the repository root; its exit code and its whole output.
let exec (file: string) (args: string list) =
    let info =
        ProcessStartInfo (file, args, WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true)

    use run = Process.Start info
    let output = run.StandardOutput.ReadToEndAsync ()
    let errors = run.StandardError.ReadToEndAsync ()
    run.WaitForExit ()
    run.ExitCode, output.Result + errors.Result

/// Runs dotnet fickl on a fixtures project with the defines and extra arguments, into its own folder.
let fableOf (fixtures: string) (name: string) (defines: string list) (extra: string list) =
    let outDir = Path.Combine (scratch, name)

    let code, log =
        exec
            "dotnet"
            ([
                "fickl"
                fixtures
                "--outDir"
                outDir
                "--configuration"
                "Release"
                "--noCache"
             ]
             @ (defines
                |> List.collect (fun define -> [ "--define"; define ]))
             @ extra)

    code, outDir, log

let fable name defines extra =
    fableOf project name defines extra

/// <summary>
/// Line endings unified, the fable-library folder's version dropped, and each site suffix (L<line>C<column>) replaced
/// by its ordinal in the file.
/// </summary>
let normalise (text: string) =
    let ordinals = Collections.Generic.Dictionary<string, int>()

    Regex.Replace (
        Regex.Replace (text.Replace ("\r\n", "\n"), @"fable-library-js\.\d+\.\d+\.\d+", "fable-library-js"),
        @"L\d+C\d+",
        fun found ->
            if not (ordinals.ContainsKey found.Value) then
                ordinals[found.Value] <- ordinals.Count + 1

            "S" + string ordinals[found.Value]
    )

let code, outDir, log = fable "good" [] [ "--sourceMaps" ]
check "the good fixtures compile" (code = 0) log

if code = 0 then
    let emitted = Path.Combine (outDir, "Sites.js")
    let expected = Path.Combine (here, "expected", "Sites.js")

    if update then
        Directory.CreateDirectory (Path.GetDirectoryName expected)
        |> ignore

        File.WriteAllText (expected, normalise (File.ReadAllText emitted))
        printfn "UPDATED %s" expected
    else
        check
            "the emitted sites equal expected/Sites.js"
            (File.Exists expected
             && normalise (File.ReadAllText emitted) = normalise (File.ReadAllText expected))
            ("diff " + emitted + " " + expected)

    let parsed, parseLog = exec "node" [ "--check"; emitted ]
    check "the emitted sites parse as JavaScript" (parsed = 0) parseLog

    // Each prologue maps back to the line of the reader it opens (spec, IDE experience).
    let mapped, mapLog = exec "node" [ Path.Combine (here, "sourcemap.mjs"); emitted ]

    let prologues = Path.Combine (here, "expected", "prologues.txt")

    if update && mapped = 0 then
        File.WriteAllText (prologues, normalise mapLog)
        printfn "UPDATED %s" prologues
    else
        check
            "each prologue maps to the line of its reader's first pattern-variable reference (expected/prologues.txt)"
            (mapped = 0
             && File.Exists prologues
             && normalise mapLog = normalise (File.ReadAllText prologues))
            mapLog

// The reported column is asserted where the range is the identifier or the call (AM001, AM003, AM004); for AM002 and
// AM007 the range is the expression Fable holds, so only the line is asserted.
let bad =
    [
        "BAD_AM001",
        "AM001.fs",
        17,
        Some 67,
        "Arms.Match: 'text' is read outside a reader of the instance. Read it inside a lambda over the instance, such as (fun _ -> text)."
        "BAD_AM001_SIMPLE",
        "AM001-simple.fs",
        17,
        Some 48,
        "Arms.Match: 'text' is read outside a reader of the instance. Read it inside a lambda over the instance, such as (fun _ -> text)."
        "BAD_AM002",
        "AM002.fs",
        17,
        None,
        "Arms.Match: 'text' is read inside a lambda over 'unit -> string', which is not the instance type 'Signal<Load>'. Pass the value to the nested view through its items or props."
        "BAD_AM002_WILDCARD",
        "AM002-wildcard.fs",
        17,
        None,
        "Arms.Match: 'text' is read inside a lambda over 'unit -> string', which is not the instance type 'Signal<Load>'. Pass the value to the nested view through its items or props."
        "BAD_AM002_WILDCARD_SIMPLE",
        "AM002-wildcard-simple.fs",
        17,
        None,
        "Arms.Match: 'text' is read inside a lambda over 'unit -> string', which is not the instance type 'Signal<Load>'. Pass the value to the nested view through its items or props."
        "BAD_AM003", "AM003.fs", 16, Some 72, "Arms.Match takes a function literal as its second argument, such as (function | Case x -> ...)."
        "BAD_AM003_LOCAL",
        "AM003-local.fs",
        19,
        None,
        "Arms.Match takes a function literal as its second argument, such as (function | Case x -> ...)."
        "BAD_AM004_INT",
        "AM004.fs",
        10,
        Some 62,
        "Arms.Match: the instance type 'int' is not an object type. An arm site keys each arm's payload by its instance; use a record, class, union or function instance."
        "BAD_AM004_STRING",
        "AM004.fs",
        13,
        Some 65,
        "Arms.Match: the instance type 'string' is not an object type. An arm site keys each arm's payload by its instance; use a record, class, union or function instance."
        "BAD_AM004_UNIT",
        "AM004.fs",
        16,
        Some 63,
        "Arms.Match: the instance type 'unit' is not an object type. An arm site keys each arm's payload by its instance; use a record, class, union or function instance."
        "BAD_AM004_OPTION",
        "AM004.fs",
        19,
        Some 69,
        "Arms.Match: the instance type 'int option' is not an object type. An arm site keys each arm's payload by its instance; use a record, class, union or function instance."
        "BAD_AM004_STRUCT",
        "AM004.fs",
        22,
        Some 64,
        "Arms.Match: the instance type 'Point' is not an object type. An arm site keys each arm's payload by its instance; use a record, class, union or function instance."
        "BAD_AM007", "AM007.fs", 18, None, "Arms.Match: this pattern form is not supported."
    ]

/// Whether some line of the log holds the message at file(line,column), or at file(line, when the column is None.
let reportedAt (log: string) (file: string) (line: int) (column: int option) (message: string) =
    let at = Regex (Regex.Escape file + @"\((\d+),(\d+)\)")

    log.Split '\n'
    |> Array.exists (fun entry ->
        let found = at.Match entry

        entry.Contains message
        && found.Success
        && found.Groups[1].Value = string line
        && (column
            |> Option.forall (fun column -> found.Groups[2].Value = string column)))

for define, file, line, column, message in bad do
    let code, _, log = fable (define.ToLowerInvariant ()) [ define ] []

    check
        (sprintf "%s fails at %s(%d) with its message" define file line)
        (code = 1
         && reportedAt log file line column message)
        log

// A project that references Ranvier.View.Arms without importing the .targets fails at its call with AM005.
let missing, _, missingLog =
    fableOf (Path.Combine (here, "NoTargets", "NoTargets.fsproj")) "no-targets" [] []

check
    "a project without the .targets fails at its Arms.Match call with AM005"
    (missing = 1
     && reportedAt
         missingLog
         "Site.fs"
         12
         (Some 13)
         "This construct is deprecated. Arms.Match needs the Ranvier.View.Arms Fable plugin; import Ranvier.View.Arms.targets in the project compiled by Fable.")
    missingLog

if failures.Count > 0 then
    for failure in failures do
        eprintfn "%s" failure

    exit 1
