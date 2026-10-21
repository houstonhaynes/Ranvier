// The external facts the analyzers rely on: Fable's inlining of module-mutable snapshots (RAN001), the Fable
// precompile failure on generated union testers (RAN002), and the MSBuild integration of RanvierAnalyze. Exits
// non-zero when a fact no longer holds: retire or rescope the rule that describes it. Run from any directory:
// dotnet fsi tools/Ranvier.Analyzers.Truth/Truth.fsx
open System
open System.Diagnostics
open System.IO
open System.Text.RegularExpressions

let root = Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, "../.."))
let output = Path.Combine (root, "artifacts/analyzers/truth")

/// Runs `dotnet` with `arguments` in the repository root; the exit code and the combined output.
let dotnet (arguments: string list) : int * string =
    let info =
        ProcessStartInfo ("dotnet", WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false)

    for argument in arguments do
        info.ArgumentList.Add argument

    use proc = Process.Start info
    let out = proc.StandardOutput.ReadToEndAsync ()
    let err = proc.StandardError.ReadToEndAsync ()
    proc.WaitForExit ()
    proc.ExitCode, out.Result + err.Result

let failures = ResizeArray<string>()

let check (name: string) (holds: bool) (detail: string) =
    if holds then
        printfn "ok   %s" name
    else
        failures.Add name
        printfn "FAIL %s\n%s" name detail

let inlining () =
    let target = Path.Combine (output, "inlining")

    let code, log =
        dotnet
            [
                "fickl"
                "tools/Ranvier.Analyzers.Truth/Inlining"
                "-c"
                "Release"
                "--noCache"
                "-e"
                ".js"
                "-o"
                target
            ]

    let js =
        if code = 0 then
            File.ReadAllText (Path.Combine (target, "Probe.js"))
        else
            ""

    check "Fickl compiles the inlining fixture" (code = 0) log

    check
        "RAN001: Fickl Release (Fable 5.22) inlines each single-use snapshot (snapshot, closure, tuple)"
        (Regex.Matches(js, Regex.Escape "childSetups() - childSetups()").Count
         >= 3)
        js

    check "RAN001: Fickl keeps a let mutable snapshot" (js.Contains "let held = childSetups()") js

/// Precompiles the fixture project `name` with `compiler` (the arguments that start the compiler); the exit code and
/// the log.
let precompileWith (compiler: string list) (name: string) =
    dotnet (
        compiler
        @ [
            "precompile"
            "tools/Ranvier.Analyzers.Truth/" + name
            "--noCache"
            "-o"
            Path.Combine (output, String.Join ("-", compiler) + "-" + name)
        ]
    )

/// Fable 5.18.0, the release whose precompile of the library failed (#138), fetched for the run.
let fable518 = [ "dnx"; "fable@5.18.0"; "--yes"; "--" ]

/// The repository's compiler, from the tool manifest.
let fickl = [ "fickl" ]

let precompile () =
    let shapes =
        [
            "Testers", "a public union"
            "InternalTesters", "an internal union"
            "PrivateTesters", "a private union"
            "InternalStructTesters", "an internal struct union"
        ]

    for name, shape in shapes do
        let code, log = precompileWith fable518 name

        check $"RAN002: the Fable 5.18 precompile fails on a tester of {shape}" (code <> 0 && log.Contains "MethodDefNotFound") log

    let generic, genericLog = precompileWith fable518 "GenericTesters"
    check "the docs' generic-union claim holds: the Fable 5.18 precompile accepts a generic union tester" (generic = 0) genericLog

    for name, shape in shapes do
        let code, log = precompileWith fickl name
        check $"Fickl precompiles a tester of {shape}" (code = 0) log

let build () =
    let analyzerCode, analyzerLog =
        dotnet
            [
                "build"
                "tools/Ranvier.Analyzers/Ranvier.Analyzers.fsproj"
                "-c"
                "Release"
                "-v"
                "q"
            ]

    check "the analyzers build" (analyzerCode = 0) analyzerLog
    let project = "tools/Ranvier.Analyzers.Truth/Build/Build.fsproj"

    let code, log = dotnet [ "build"; project; "-c"; "Release"; "--no-incremental" ]

    check
        "RanvierAnalyze reports RAN001 as an MSBuild warning"
        (code = 0
         && log.Contains "B.fs(4,8): warning RAN001")
        log

    check "an ignore-line-next comment silences its line" (not (log.Contains "B.fs(11,")) log

    let quiet, quietLog =
        dotnet
            [
                "build"
                project
                "-c"
                "Release"
                "--no-incremental"
                "-v"
                "d"
                "-p:MSBuildWarningsAsMessages=RAN001"
            ]

    // Detailed verbosity prints the demoted finding as `message RAN001`.
    check
        "MSBuildWarningsAsMessages demotes RAN001"
        (quiet = 0
         && quietLog.Contains "B.fs(4,8): message RAN001"
         && not (quietLog.Contains "warning RAN001"))
        quietLog

    let strict, strictLog =
        dotnet
            [
                "build"
                project
                "-c"
                "Release"
                "--no-incremental"
                "-p:RanvierAnalyzersFlags=--treat-as-error RAN001"
            ]

    check "an error-promoted RAN001 fails the build" (strict <> 0 && strictLog.Contains "error RAN001") strictLog

dotnet [ "tool"; "restore" ] |> ignore
inlining ()
precompile ()
build ()

if failures.Count > 0 then
    eprintfn "%d truth checks failed: %s" failures.Count (String.Join (", ", failures))
    exit 1
