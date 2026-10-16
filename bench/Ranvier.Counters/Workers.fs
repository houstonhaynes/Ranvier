/// <summary>Worker process launch settings shared by the legacy driver and alternating runs.</summary>
module CounterBench.Workers

open System
open System.Diagnostics
open System.IO
open System.Text.Json

/// <summary>
/// Disables tiering, PGO and ReadyToRun so workers compile methods fully on first call; uses workstation GC.
/// </summary>
let workerEnvironment =
    [
        "DOTNET_TieredCompilation", "0"
        "DOTNET_TieredPGO", "0"
        "DOTNET_ReadyToRun", "0"
        "DOTNET_gcServer", "0"
    ]

/// <summary>Resolves an existing worker; DLLs run under <c>dotnet</c>, other files execute directly.</summary>
let workerAt (path: string) =
    if not (File.Exists path) then
        failwith $"No counter worker at {path}."

    if Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase) then
        "dotnet", [ path ]
    else
        path, []

/// <summary>Starts a worker with stable JIT settings, waits for success, and reads its JSON transport.</summary>
let runWorker (host: string, prefix: string list) (scale: int) (only: string option) (resultFile: string) =
    let info = ProcessStartInfo host

    for arg in
        prefix
        @ [ "--worker"; resultFile; "--scale"; string scale ]
        @ (only
           |> Option.map (fun p -> [ "--only"; p ])
           |> Option.defaultValue []) do
        info.ArgumentList.Add arg

    info.UseShellExecute <- false

    for name, value in workerEnvironment do
        info.Environment[name] <- value

    use p = Process.Start info

    try
        p.WaitForExit ()

        if p.ExitCode <> 0 then
            failwith $"The worker exited with {p.ExitCode}."

        JsonSerializer.Deserialize<WorkerRun>(File.ReadAllText resultFile)
    finally
        if not p.HasExited then
            p.Kill true
            p.WaitForExit ()
