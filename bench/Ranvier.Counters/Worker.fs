/// <summary>
/// The measured process. Every method runs fully optimised from its first
/// call. The driver reads back the JSON it writes.
/// </summary>
module CounterBench.Worker

open System
open System.IO
open System.Runtime
open System.Runtime.InteropServices
open System.Text.Json
open System.Threading
open CounterBench.Scenarios

[<DllImport("kernel32.dll")>]
extern uint32 GetCurrentThreadId()

let private threadId () =
    if OperatingSystem.IsLinux () then
        LinuxPmc.currentThreadId ()
    else
        int (GetCurrentThreadId ())

let countersCompiled =
#if RANVIER_COUNTERS
    true
#else
    false
#endif

let private resetCounters () =
#if RANVIER_COUNTERS
    Ranvier.Counters.Reset ()
#else
    ()
#endif

let private readCounters () : CounterValue[] =
#if RANVIER_COUNTERS
    Ranvier.Counters.Snapshot ()
    |> Array.map (fun (name, value) -> { Name = name; Value = value })
#else
    [||]
#endif

/// <summary>
/// Budgets tried in turn: the largest the runtime accepts is used.
/// </summary>
let private noGcBudgets = [ 240L <<< 20; 120L <<< 20; 60L <<< 20; 30L <<< 20 ]

let private enterNoGcRegion () =
    noGcBudgets
    |> List.exists (fun budget ->
        try
            GC.TryStartNoGCRegion budget
        with :? ArgumentOutOfRangeException ->
            false)

let private exitNoGcRegion () =
    if GCSettings.LatencyMode = GCLatencyMode.NoGCRegion then
        GC.EndNoGCRegion ()

/// <summary>
/// Runs <c>ops</c> operations of <c>case</c> between the markers numbered <c>region</c>.
/// </summary>
/// <remarks>
/// Each marker is followed by a one-millisecond sleep, which switches the thread out.
/// Counted intervals run from the wake after <c>Begin</c> to the sleep after <c>End</c>; sleeps and markers cancel in the N and 2N difference.
/// On Linux, <c>hardware</c> counts from before the first allocation read to after the last.
/// </remarks>
let measure (hardware: LinuxPmc.ThreadCounters option) (case: Case) (ops: int) (region: int) =
    let prepared = case.Prepare ops

    try
        GC.Collect ()
        GC.WaitForPendingFinalizers ()
        GC.Collect ()
        let noGc = enterNoGcRegion ()

        try
            let collections = GC.CollectionCount 0
            resetCounters ()
            let mutable counts = LinuxPmc.Counts.Unmeasured

            let bytes =
                Markers.Log.Begin region

                try
                    Thread.Sleep 1
                    hardware |> Option.iter (fun h -> h.Start ())

                    try
                        let before = GC.GetAllocatedBytesForCurrentThread ()
                        prepared.Run ()
                        let after = GC.GetAllocatedBytesForCurrentThread ()
                        after - before
                    finally
                        counts <-
                            hardware
                            |> Option.map (fun h -> h.Stop ())
                            |> Option.defaultValue LinuxPmc.Counts.Unmeasured
                finally
                    Markers.Log.End region
                    Thread.Sleep 1

            let collections = GC.CollectionCount 0 - collections

            {
                Ops = ops
                Region = region
                Bytes = bytes
                Gen0Collections = collections
                NoGcRegion = noGc && collections = 0
                Instructions = counts.Instructions
                Cycles = counts.Cycles
                BranchMisses = counts.BranchMisses
                CpuNs = counts.CpuNs
                Counters = readCounters ()
            }
        finally
            exitNoGcRegion ()
    finally
        prepared.Teardown ()

/// <summary>
/// Measures every case at N and 2N after one unmeasured run at N, which
/// compiles every method and initialises every static the region reaches.
/// With <c>only</c>, measures the cases whose scenario name starts with it.
/// </summary>
let run (scale: int) (only: string option) (output: string) =
    let hardware =
        if OperatingSystem.IsLinux () then
            Some (LinuxPmc.ThreadCounters.Open (Environment.ProcessId, LinuxPmc.currentThreadId ()))
        else
            None

    use hardwareOwner =
        { new IDisposable with
            member _.Dispose() =
                hardware
                |> Option.iter (fun h -> (h :> IDisposable).Dispose())
        }

    let cases =
        Scenarios.all scale
        |> List.filter (fun case ->
            only
            |> Option.forall (fun prefix -> case.Scenario.StartsWith (prefix, StringComparison.Ordinal)))

    let results =
        cases
        |> List.mapi (fun index case ->
            let warm = case.Prepare case.Ops

            try
                warm.Run ()
            finally
                warm.Teardown ()

            if index = 0 && hardware.IsSome then
                measure hardware case case.Ops -1 |> ignore

            {
                Scenario = case.Scenario
                Engine = case.Engine
                Unit = case.Unit
                Measurements =
                    [|
                        measure hardware case case.Ops (2 * index)
                        measure hardware case (2 * case.Ops) (2 * index + 1)
                    |]
            })
        |> List.toArray

    let run =
        {
            ProcessId = Environment.ProcessId
            ThreadId = threadId ()
            CountersCompiled = countersCompiled
            Cases = results
        }

    File.WriteAllText (output, JsonSerializer.Serialize run)
