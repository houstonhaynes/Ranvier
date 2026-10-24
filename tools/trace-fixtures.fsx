// Rewrites the committed trace fixtures in tests/Ranvier.Tests/fixtures/trace from the programs that pin them: the
// schema 1, 2 and 3 dumps and the six contract documents. Run it after a deliberate change to the trace output or
// to a fixture program, then review the fixture diff before committing it.
//
//   dotnet fsi tools/trace-fixtures.fsx
//
// It runs the traced fixture tests once with RANVIER_WRITE_FIXTURES=1 to write the files, then once without it to
// check them. Exit code 0 when both runs pass, 1 otherwise.

open System.Diagnostics
open System.IO

let root = Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, ".."))
let project = Path.Combine (root, "tests", "Ranvier.Tests", "Ranvier.Tests.fsproj")

let run (write: bool) (args: string) =
    let info =
        ProcessStartInfo ("dotnet", args, WorkingDirectory = root, UseShellExecute = false)

    info.Environment["RanvierTrace"] <- "true"
    info.Environment["RANVIER_WRITE_FIXTURES"] <- if write then "1" else ""
    use p = Process.Start info
    p.WaitForExit ()
    p.ExitCode

let tests (write: bool) =
    [ "TraceDump"; "Trace fixtures" ]
    |> List.map (fun list -> run write $"run --project \"{project}\" -c Debug --no-build -- --filter-test-list \"{list}\"")
    |> List.max

let code =
    if
        run false $"build \"{project}\" -c Debug -v q"
        <> 0
    then
        1
    else
        // The write run's parse tests may read a fixture before it is written, so only the check run counts.
        tests true |> ignore
        tests false

exit (if code = 0 then 0 else 1)
