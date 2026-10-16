/// <summary>
/// Runs of the Fable harness in <c>fable/Ranvier.Counters</c> under Node.js,
/// and the JSON it writes.
/// </summary>
module CounterBench.NodeRuns

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text.Json

type NodeCounter = { Name: string; Value: float }

type NodeMeasurement =
    {
        Ops: int
        Region: int
        /// <summary>
        /// Growth of the heap spaces holding JavaScript objects over the
        /// region: new, old and both large-object spaces.
        /// </summary>
        Bytes: float
        /// <summary>
        /// Growth of <c>process.memoryUsage().heapUsed</c> over the region, which
        /// adds the code and trusted spaces.
        /// </summary>
        HeapUsedBytes: float
        /// <summary>
        /// Garbage collections during the region.
        /// </summary>
        Gen0Collections: int
        NoGcRegion: bool
        /// <summary>
        /// <c>Ranvier.Counters.Snapshot()</c> after the region. Empty when
        /// the harness was compiled without <c>RanvierCounters</c>.
        /// </summary>
        Counters: NodeCounter[]
    }

type NodeCase =
    {
        Scenario: string
        Engine: string
        Unit: string
        /// <summary>
        /// The measurement at N, then the measurement at 2N.
        /// </summary>
        Measurements: NodeMeasurement[]
    }

type NodeRun =
    {
        ProcessId: int
        CountersCompiled: bool
        Cases: NodeCase[]
    }

/// <summary>
/// A run with processor counter totals per region: for the main thread, then
/// for every thread of the process.
/// </summary>
type NodePmcRun =
    {
        Run: NodeRun
        MainThread: Pmc.RegionCounts[]
        AllThreads: Pmc.RegionCounts[]
    }

/// <summary>
/// V8 flags for every run. V8 compiles and collects on the main thread, and
/// the heap after a region is the same in every run.
/// </summary>
let flags = [ "--expose-gc"; "--single-threaded" ]

/// <summary>
/// The harness entry point of the build under <c>output</c>, <c>plain</c> or <c>counters</c>.
/// </summary>
let script (output: string) (build: string) =
    Path.Combine (output, build, "Main.fs.js")

let private startInfo (node: string) (script: string) (args: string list) =
    let info = ProcessStartInfo node

    for arg in flags @ [ script ] @ args do
        info.ArgumentList.Add arg

    info.UseShellExecute <- false
    info

let private finish (p: Process) (resultFile: string) =
    p.WaitForExit ()

    if p.ExitCode <> 0 then
        failwith $"node exited with %d{p.ExitCode}."

    JsonSerializer.Deserialize<NodeRun>(File.ReadAllText resultFile)

/// <summary>The harness arguments selecting the scenarios whose name starts with <c>only</c>.</summary>
let private onlyArgs (only: string option) =
    only
    |> Option.map (fun prefix -> [ "--only"; prefix ])
    |> Option.defaultValue []

/// <summary>
/// Runs the harness <c>script</c> to completion.
/// </summary>
let run (node: string) (script: string) (scale: int) (only: string option) (resultFile: string) =
    use p =
        Process.Start (
            startInfo
                node
                script
                ([ "--out"; resultFile; "--scale"; string scale ]
                 @ onlyArgs only)
        )

    finish p resultFile

/// <summary>
/// Runs the harness <c>script</c> with <c>--handshake</c>. <c>mark</c> receives the Node process ID and each region
/// boundary, <c>true</c> for a <c>BEGIN</c>, while the harness waits for the reply. Fails
/// unless every measured region was marked at both ends.
/// </summary>
let runHandshake (node: string) (script: string) (scale: int) (only: string option) (resultFile: string) (mark: int -> bool -> int -> unit) =
    let info =
        startInfo
            node
            script
            ([ "--out"; resultFile; "--scale"; string scale; "--handshake" ]
             @ onlyArgs only)

    info.RedirectStandardInput <- true
    info.RedirectStandardOutput <- true
    use p = Process.Start info

    let reply () =
        p.StandardInput.Write '\n'
        p.StandardInput.Flush ()

    try
        let completed = HashSet<int>()
        let mutable active = None
        let mutable line = p.StandardOutput.ReadLine ()

        while not (isNull line) do
            match line.Split ' ' with
            | [| ("BEGIN" | "END") as marker; text |] ->
                let region =
                    match Int32.TryParse text with
                    | true, region when region >= 0 -> region
                    | _ -> failwith $"Invalid region: %s{line}."

                let isBegin = marker = "BEGIN"

                if isBegin then
                    if active.IsSome || completed.Contains region then
                        failwith $"Overlapping or repeated region: %s{line}."

                    active <- Some region
                else
                    if active <> Some region then
                        failwith $"Unmatched region end: %s{line}."

                    active <- None
                    completed.Add region |> ignore

                mark p.Id isBegin region
                reply ()
            | _ when
                line.StartsWith ("BEGIN", StringComparison.Ordinal)
                || line.StartsWith ("END", StringComparison.Ordinal)
                ->
                failwith $"Invalid region boundary: %s{line}."
            | _ -> eprintfn "%s" line

            line <- p.StandardOutput.ReadLine ()

        if active.IsSome then
            failwith "Node exited with an unfinished region."

        let run = finish p resultFile

        if run.ProcessId <> p.Id then
            failwith "The Node result process ID does not match the measured process."

        let measured = HashSet<int>()

        for case in run.Cases do
            for m in case.Measurements do
                if m.Region < 0 || not (measured.Add m.Region) then
                    failwith "The Node result contains an invalid or repeated measurement region."

        if not (completed.SetEquals measured) then
            failwith "The measured regions must exactly match the completed handshake regions."

        run
    with _ ->
        try
            if not p.HasExited then
                p.Kill true
        with :? InvalidOperationException when p.HasExited ->
            ()

        p.WaitForExit ()
        reraise ()

/// <summary>
/// The Node.js version <c>node</c> reports.
/// </summary>
let version (node: string) =
    let info = ProcessStartInfo (node, "--version")
    info.RedirectStandardOutput <- true
    info.UseShellExecute <- false
    use p = Process.Start info
    let output = p.StandardOutput.ReadToEnd ()
    p.WaitForExit ()
    output.Trim ()
