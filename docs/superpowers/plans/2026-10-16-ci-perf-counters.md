# CI instruction counters implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pull requests against `master` are measured against their merge base on an arm64 runner with hardware instruction counters and fail on unaccepted regressions; pushes to `master` record their figures on a `perf-data` branch with a generated timeline.

**Architecture:** The existing counter bench gains a Linux backend (`perf_event_open` for the measured thread, `/proc` for thread CPU time) and three subcommands: `ab` (alternating base/head processes), `compare` (verdict and comment) and `record`/`timeline` (history). `build.fsx` commands on Partas.Build 0.8.1 drive them; two workflows run them, one read-only on pull requests and one with write permission that runs no pull-request code.

**Tech Stack:** F# on .NET 10, System.Text.Json, P/Invoke to libc, Expecto 11.0.0-alpha8, Fable 5.18 and Node 26 for the Fable harness, Partas.Build 0.8.1, GitHub Actions (`ubuntu-24.04-arm`, `ubuntu-latest`).

**Spec:** [`docs/superpowers/specs/2026-10-16-ci-perf-counters-design.md`](../specs/2026-10-16-ci-perf-counters-design.md)

## Implementation status

Tasks 1–6 are implemented, committed and independently reviewed. The Linux backend rejects incomplete hardware samples; alternating runs distinguish base/head sides even at the same SHA. Comparison reports retain all measurements, scoped override reasons and GC flags. History retains raw figures and explicit medians.

The user included the Partas.Build prerequisite in this work. Latest master was merged, Partas.Build 0.8.1 and Partas.Build.Baked 0.3.0 were installed, and root `nuget.config` configures the public Cloudsmith feed. The config is included in the solution's `/.environment/` folder. Cold-cache script bootstrap and redirected-output execution passed.

Task 7 integrates the commands and workflows. Task 8 requires authorization to push and perform GitHub checks; existing PR #64 should be reused. The privileged `workflow_run` reporter can run only after its workflow exists on the default branch, so live comment/history verification follows the user's merge. Local x64 checks do not establish arm64 performance stability.

The code sketches below describe the original plan. The implementation uses canonical leg names `arm64-counters` and `x64-timing`, explicit side/GC metadata and required-field validation, and the actual Partas.Build 0.8.1 applicative input and literal-command APIs.

## Global Constraints

- Branch `feat/ci-perf-counters` from `master`; the PR goes to `master`. Worktree `C:\Users\shaya\RiderProjects\Ranvier-ci-perf`.
- **Prerequisite (completed in this work):** `build.fsx` uses Partas.Build 0.8.1 (Cloudsmith) and runs under redirected output. Latest master is merged; the workflows consume the repository's root `nuget.config`.
- `src/Ranvier` is not changed. All code lives in `bench/Ranvier.Counters`, `bench/Ranvier.Counters.Tests`, `build.fsx` and `.github/workflows`.
- `counters.ps1` on Windows keeps its behaviour and output byte for byte in format (figures differ by run).
- Gate: instructions on `ubuntu-24.04-arm`, engines whose name starts with `Ranvier`; regression when the head median exceeds the base median by more than **max(2%, 20 instructions per operation)** and every head process exceeds every base process. x64: thread CPU time, threshold 15%, separation rule, warning only.
- Five processes per commit per runtime, order B H B H B H B H B H; library counters from one process per commit per runtime.
- Override: label `perf-accepted` (accepts all), or trailer `Perf-Accepted: <names|all> — <reason>` (separator ` — ` or ` -- `) in any commit between merge base and head; a name matching no scenario fails the check.
- Comment marker: `<!-- ranvier-perf -->`. History branch: `perf-data` with `runs/<sha>.json` and `timeline.md`.
- Comments follow `.claude/rules/comments.md`; no `FOR-REVIEW` comments committed. Formatting: `dotnet fantomas` (pinned 8.0.5) on changed `.fs`/`.fsx` files.
- Commits: conventional, one line, no body, no attribution.
- Push, PR creation and branch deletion need the user's confirmation at the step that does them.

## Review Focus

1. A base commit whose worker cannot measure on Linux (master before this PR calls `kernel32.GetCurrentThreadId`) must yield "no baseline" for .NET, not a failed check. Pinned in Task 4 (`ab` catches a failing base worker) and Task 5 (a runtime without base gate figures produces no regression).
2. A trailer naming a scenario that exists only in the head (a new scenario) is a known name, not a typo. Pinned in Task 5.
3. Measuring around `prepared.Run` must not move the `.NET` `Bytes` figure: the counter reads (P/Invoke, `/proc` read) sit outside the allocation window. Pinned in Task 2.
4. A label event re-running the verdict must not re-measure when figures for the same head SHA exist, and must measure when they do not. Pinned in Task 7 (workflow `find` job) and checked in Task 8.
5. `perf-report.yml` must comment only on a PR whose head SHA equals the triggering run's head SHA, so a fork's artifact cannot direct a comment elsewhere. Pinned in Task 7, checked in Task 8.

---

### Task 1: Linux counters for one thread

**Files:**
- Create: `bench/Ranvier.Counters/LinuxPmc.fs`
- Modify: `bench/Ranvier.Counters/Ranvier.Counters.fsproj` (compile `LinuxPmc.fs` after `Markers.fs`)
- Create: `bench/Ranvier.Counters.Tests/Ranvier.Counters.Tests.fsproj`, `bench/Ranvier.Counters.Tests/LinuxPmcTests.fs`, `bench/Ranvier.Counters.Tests/Main.fs`
- Modify: `Ranvier.slnx` (add the test project next to `bench/Ranvier.Counters`)

**Interfaces:**
- Produces: module `CounterBench.LinuxPmc` with `type Counts = { Instructions: int64; Cycles: int64; BranchMisses: int64; CpuNs: int64 }` (-1 = not measured), `Counts.Unmeasured`, `attributes: uint64 -> byte[]`, `parseSchedstat: string -> int64`, `currentThreadId: unit -> int`, `type ThreadCounters` with `static Open: pid: int * tid: int -> ThreadCounters`, `Hardware: bool`, `Start: unit -> unit`, `Stop: unit -> Counts`, `IDisposable`; `hardwareAvailable: unit -> bool`.

- [ ] **Step 1: Create the test project.**

`bench/Ranvier.Counters.Tests/Ranvier.Counters.Tests.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <LangVersion>latest</LangVersion>
        <IsPackable>false</IsPackable>
        <GenerateProgramFile>false</GenerateProgramFile>
        <GenerateDocumentationFile>false</GenerateDocumentationFile>
    </PropertyGroup>

    <ItemGroup>
        <Compile Include="LinuxPmcTests.fs" />
        <Compile Include="Main.fs" />
    </ItemGroup>

    <ItemGroup>
        <PackageReference Include="Expecto" Version="11.0.0-alpha8" />
        <PackageReference Include="YoloDev.Expecto.TestSdk" Version="0.15.5" />
        <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.4.0" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\Ranvier.Counters\Ranvier.Counters.fsproj" />
    </ItemGroup>

</Project>
```

`bench/Ranvier.Counters.Tests/Main.fs`:

```fsharp
module CounterBench.Tests.Main

open Expecto

[<EntryPoint>]
let main argv = runTestsInAssemblyWithCLIArgs [] argv
```

- [ ] **Step 2: Write the failing tests** in `bench/Ranvier.Counters.Tests/LinuxPmcTests.fs`:

```fsharp
module CounterBench.Tests.LinuxPmcTests

open System
open Expecto
open CounterBench

let private linuxOnly () =
    if not (OperatingSystem.IsLinux ()) then
        skiptest "Linux only"

[<Tests>]
let tests =
    testList "LinuxPmc" [
        test "attributes encode a disabled user-mode hardware counter" {
            let bytes = LinuxPmc.attributes 1UL
            Expect.equal bytes.Length 112 "PERF_ATTR_SIZE_VER5"
            Expect.equal (BitConverter.ToUInt32 (bytes, 0)) 0u "PERF_TYPE_HARDWARE"
            Expect.equal (BitConverter.ToUInt32 (bytes, 4)) 112u "size"
            Expect.equal (BitConverter.ToUInt64 (bytes, 8)) 1UL "config"
            Expect.equal (BitConverter.ToUInt64 (bytes, 40)) 0x61UL "disabled, exclude_kernel, exclude_hv"
        }

        test "schedstat's first field is the thread's CPU time" {
            Expect.equal (LinuxPmc.parseSchedstat "123456 789 10\n") 123456L "nanoseconds on CPU"
        }

        test "a thread's counters grow with the work between Start and Stop" {
            linuxOnly ()
            use counters = LinuxPmc.ThreadCounters.Open (Environment.ProcessId, LinuxPmc.currentThreadId ())

            let measure (n: int) =
                counters.Start ()
                let mutable s = 0L
                for i in 1..n do
                    s <- s + int64 i
                let counts = counters.Stop ()
                GC.KeepAlive s
                counts

            let small = measure 1_000_000
            let large = measure 2_000_000
            Expect.isGreaterThan large.CpuNs 0L "CPU time is read from /proc"

            if counters.Hardware then
                Expect.isGreaterThan large.Instructions small.Instructions "twice the loop counts more"
            else
                Expect.equal large.Instructions -1L "no hardware counters reads -1"
        }
    ]
```

- [ ] **Step 3: Run to verify it fails.** Add the project to `Ranvier.slnx` (`<Project Path="bench/Ranvier.Counters.Tests/Ranvier.Counters.Tests.fsproj" />` after the `bench/Ranvier.Counters` line). Run: `dotnet test bench/Ranvier.Counters.Tests -c Release`. Expected: build FAIL, `The namespace or module 'LinuxPmc' is not defined`.

- [ ] **Step 4: Implement** `bench/Ranvier.Counters/LinuxPmc.fs` and add `<Compile Include="LinuxPmc.fs" />` after `Markers.fs` in `Ranvier.Counters.fsproj`:

```fsharp
/// <summary>
/// User-mode hardware counters and CPU time of one Linux thread, read through
/// <c>perf_event_open</c> and <c>/proc</c>.
/// </summary>
module CounterBench.LinuxPmc

open System
open System.IO
open System.Runtime.InteropServices

/// <summary>
/// Counts over one region. A counter the machine does not offer reads -1.
/// </summary>
type Counts =
    {
        Instructions: int64
        Cycles: int64
        BranchMisses: int64
        CpuNs: int64
    }

    static member Unmeasured =
        {
            Instructions = -1L
            Cycles = -1L
            BranchMisses = -1L
            CpuNs = -1L
        }

module private Native =
    [<DllImport("libc", EntryPoint = "syscall", SetLastError = true)>]
    extern int64 perfEventOpen(int64 number, byte[] attr, int pid, int cpu, int groupFd, unativeint flags)

    [<DllImport("libc", EntryPoint = "syscall")>]
    extern int64 gettid(int64 number)

    [<DllImport("libc", SetLastError = true)>]
    extern int ioctl(int fd, unativeint request, nativeint arg)

    [<DllImport("libc", SetLastError = true)>]
    extern nativeint read(int fd, byte[] buffer, unativeint count)

    [<DllImport("libc")>]
    extern int close(int fd)

let private PerfEventOpen, Gettid =
    match RuntimeInformation.ProcessArchitecture with
    | Architecture.X64 -> 298L, 186L
    | Architecture.Arm64 -> 241L, 178L
    | _ -> -1L, -1L

let private Enable = 0x2400un
let private Disable = 0x2401un
let private Reset = 0x2403un

/// <summary>
/// <c>perf_event_attr</c> (112 bytes) for the hardware event <c>config</c>: created
/// disabled, counting user mode only.
/// </summary>
let attributes (config: uint64) : byte[] =
    let bytes = Array.zeroCreate<byte> 112
    BitConverter.TryWriteBytes (Span<byte> (bytes, 4, 4), 112u) |> ignore
    BitConverter.TryWriteBytes (Span<byte> (bytes, 8, 8), config) |> ignore
    BitConverter.TryWriteBytes (Span<byte> (bytes, 40, 8), 0x61UL) |> ignore
    bytes

/// <summary>
/// The first field of <c>/proc/&lt;pid&gt;/task/&lt;tid&gt;/schedstat</c>: nanoseconds on CPU.
/// </summary>
let parseSchedstat (text: string) =
    Int64.Parse (text.Split(' ', StringSplitOptions.RemoveEmptyEntries).[0])

let currentThreadId () = int (Native.gettid Gettid)

let private hardwareEvents = [| 1UL; 0UL; 5UL |]

/// <summary>
/// Instructions, cycles and branch misses of thread <c>tid</c> of process <c>pid</c>, and
/// its CPU time. <c>Hardware</c> is false when the machine offers no hardware counters;
/// the counts then hold CPU time only.
/// </summary>
[<Sealed>]
type ThreadCounters private (pid: int, tid: int, fds: int[]) =
    let schedstat = $"/proc/%d{pid}/task/%d{tid}/schedstat"
    let buffer = Array.zeroCreate<byte> 8
    let mutable cpuStart = 0L

    static member Open (pid: int, tid: int) =
        let opened =
            hardwareEvents
            |> Array.map (fun config -> int (Native.perfEventOpen (PerfEventOpen, attributes config, tid, -1, -1, 0un)))

        if opened |> Array.forall (fun fd -> fd >= 0) then
            new ThreadCounters (pid, tid, opened)
        else
            for fd in opened do
                if fd >= 0 then
                    Native.close fd |> ignore

            new ThreadCounters (pid, tid, [||])

    member _.Hardware = fds.Length > 0

    member _.Start () =
        cpuStart <- parseSchedstat (File.ReadAllText schedstat)

        for fd in fds do
            Native.ioctl (fd, Reset, 0n) |> ignore
            Native.ioctl (fd, Enable, 0n) |> ignore

    member _.Stop () : Counts =
        for fd in fds do
            Native.ioctl (fd, Disable, 0n) |> ignore

        let value (index: int) =
            if fds.Length = 0 then
                -1L
            else
                Native.read (fds[index], buffer, 8un) |> ignore
                BitConverter.ToInt64 (buffer, 0)

        let instructions = value 0
        let cycles = value 1
        let branchMisses = value 2

        {
            Instructions = instructions
            Cycles = cycles
            BranchMisses = branchMisses
            CpuNs = parseSchedstat (File.ReadAllText schedstat) - cpuStart
        }

    interface IDisposable with
        member _.Dispose () =
            for fd in fds do
                Native.close fd |> ignore

/// <summary>
/// True when this machine offers hardware counters to this process.
/// </summary>
let hardwareAvailable () =
    OperatingSystem.IsLinux ()
    && (use counters = ThreadCounters.Open (Environment.ProcessId, currentThreadId ())
        counters.Hardware)
```

- [ ] **Step 5: Run the tests.** `dotnet test bench/Ranvier.Counters.Tests -c Release`. Expected on Windows: 2 passed, 1 skipped. If WSL has a .NET 10 SDK (`wsl -e dotnet --version`), also run `wsl -e dotnet test bench/Ranvier.Counters.Tests -c Release` from the worktree's `/mnt/c/...` path: expected 3 passed (Hardware is likely false under WSL; the CPU-time assertion still runs). The hardware path is checked on the arm64 runner in Task 8.

- [ ] **Step 6: Format and commit.** `dotnet fantomas bench/Ranvier.Counters/LinuxPmc.fs bench/Ranvier.Counters.Tests`. Commit `feat(bench): read Linux thread counters through perf_event_open`.

### Task 2: The .NET worker measures itself on Linux

**Files:**
- Modify: `bench/Ranvier.Counters/Model.fs` (`Measurement`)
- Modify: `bench/Ranvier.Counters/Worker.fs` (`GetCurrentThreadId`, `measure`, `run`)
- Test: `bench/Ranvier.Counters.Tests/WorkerTests.fs` (add to the test fsproj before `Main.fs`)

**Interfaces:**
- Consumes: `LinuxPmc.ThreadCounters`, `LinuxPmc.Counts`, `LinuxPmc.currentThreadId` (Task 1).
- Produces: `Measurement` gains `Instructions: int64; Cycles: int64; BranchMisses: int64; CpuNs: int64` (-1 when not measured); `Worker.measure: LinuxPmc.ThreadCounters option -> Case -> int -> int -> Measurement`.

- [ ] **Step 1: Write the failing test** `bench/Ranvier.Counters.Tests/WorkerTests.fs`:

```fsharp
module CounterBench.Tests.WorkerTests

open System
open Expecto
open CounterBench
open CounterBench.Scenarios

let private allocating: Case =
    {
        Scenario = "test-allocate"
        Engine = "Test"
        Unit = "one array"
        Ops = 1000
        Prepare =
            fun ops ->
                {
                    Run =
                        fun () ->
                            for _ in 1..ops do
                                GC.KeepAlive (Array.zeroCreate<byte> 16)
                    Teardown = ignore
                }
    }

[<Tests>]
let tests =
    testList "Worker.measure" [
        test "without Linux counters every hardware figure reads -1" {
            let m = Worker.measure None allocating 1000 0
            Expect.equal m.Instructions -1L "instructions"
            Expect.equal m.CpuNs -1L "cpu time"
        }

        test "Linux counters leave the allocation figure as without them" {
            if not (OperatingSystem.IsLinux ()) then
                skiptest "Linux only"

            use counters = LinuxPmc.ThreadCounters.Open (Environment.ProcessId, LinuxPmc.currentThreadId ())
            let without = Worker.measure None allocating 1000 0
            let with' = Worker.measure (Some counters) allocating 1000 1
            Expect.equal with'.Bytes without.Bytes "counter reads sit outside the allocation window"
            Expect.isGreaterThan with'.CpuNs 0L "cpu time measured"
        }
    ]
```

- [ ] **Step 2: Run to verify it fails.** `dotnet test bench/Ranvier.Counters.Tests -c Release`. Expected: build FAIL (`measure` takes a `Case` first; `Instructions` is not a field).

- [ ] **Step 3: Implement.** In `Model.fs`, add to `Measurement` after `NoGcRegion`:

```fsharp
        /// <summary>
        /// User-mode instructions over the region on Linux; -1 elsewhere or when the
        /// machine offers no hardware counters. <c>Cycles</c> and <c>BranchMisses</c> likewise.
        /// </summary>
        Instructions: int64
        Cycles: int64
        BranchMisses: int64
        /// <summary>
        /// The measured thread's CPU time over the region on Linux, in nanoseconds; -1 elsewhere.
        /// </summary>
        CpuNs: int64
```

In `Worker.fs`, replace the `GetCurrentThreadId` use and `measure`:

```fsharp
let private threadId () =
    if OperatingSystem.IsLinux () then
        LinuxPmc.currentThreadId ()
    else
        int (GetCurrentThreadId ())

let measure (hardware: LinuxPmc.ThreadCounters option) (case: Case) (ops: int) (region: int) =
    let prepared = case.Prepare ops
    GC.Collect ()
    GC.WaitForPendingFinalizers ()
    GC.Collect ()
    let noGc = enterNoGcRegion ()
    let collections = GC.CollectionCount 0
    resetCounters ()

    Markers.Log.Begin region
    Thread.Sleep 1
    hardware |> Option.iter (fun h -> h.Start ())
    let before = GC.GetAllocatedBytesForCurrentThread ()
    prepared.Run ()
    let after = GC.GetAllocatedBytesForCurrentThread ()

    let counts =
        hardware
        |> Option.map (fun h -> h.Stop ())
        |> Option.defaultValue LinuxPmc.Counts.Unmeasured

    Markers.Log.End region
    Thread.Sleep 1

    let collections = GC.CollectionCount 0 - collections
    let counters = readCounters ()
    exitNoGcRegion ()
    prepared.Teardown ()

    {
        Ops = ops
        Region = region
        Bytes = after - before
        Gen0Collections = collections
        NoGcRegion = noGc && collections = 0
        Instructions = counts.Instructions
        Cycles = counts.Cycles
        BranchMisses = counts.BranchMisses
        CpuNs = counts.CpuNs
        Counters = counters
    }
```

In `Worker.run`, open the counters once and pass them; use `threadId ()`:

```fsharp
    use hardware =
        if OperatingSystem.IsLinux () then
            LinuxPmc.ThreadCounters.Open (Environment.ProcessId, LinuxPmc.currentThreadId ())
        else
            null

    let hardware = Option.ofObj hardware
```

then `measure hardware case case.Ops (2 * index)` and `measure hardware case (2 * case.Ops) (2 * index + 1)`, and `ThreadId = threadId ()`. Keep the existing `<remarks>` on `measure`; add one sentence: "On Linux, `hardware` counts from before the first allocation read to after the last."

- [ ] **Step 4: Run the tests.** `dotnet test bench/Ranvier.Counters.Tests -c Release`. Expected on Windows: 3 passed, 2 skipped (WSL, if available: all 5 passed). Then `./counters.ps1 -NoPmc -Only cutoff -Out $env:TEMP\counters-check` from the worktree. Expected: exit 0 and a report whose `cutoff` rows match the format of `docs/.ai/benchmarks/counters/4939e9d.md` on `poc/fable-ranvier-dom`.

- [ ] **Step 5: Format and commit** `feat(bench): measure the .NET worker's own thread on Linux`.

### Task 3: The driver's processor counters on Linux

**Files:**
- Modify: `bench/Ranvier.Counters/LinuxPmc.fs` (add `RegionRecorder`)
- Modify: `bench/Ranvier.Counters/NodeRuns.fs` (`runHandshake`'s `mark` takes the process id)
- Modify: `bench/Ranvier.Counters/Report.fs` (add `linuxRegionCounts`)
- Modify: `bench/Ranvier.Counters/Program.fs` (`mark`, `measure`, `measureNode`, `main`'s elevation and `--list-sources` cases)
- Test: `bench/Ranvier.Counters.Tests/LinuxPmcTests.fs` (append)

**Interfaces:**
- Consumes: Tasks 1–2.
- Produces: `LinuxPmc.RegionRecorder` with `Mark: pid: int -> isBegin: bool -> region: int -> unit`, `Regions: IReadOnlyDictionary<int, Counts>`, `IDisposable`; `LinuxPmc.toRegionCounts: region: int -> Counts -> Pmc.RegionCounts` is NOT possible (Pmc compiles after LinuxPmc), so `Report.linuxRegionCounts: WorkerRun -> Pmc.RegionCounts[]` and `Report.linuxNodeRegionCounts: IReadOnlyDictionary<int, LinuxPmc.Counts> -> Pmc.RegionCounts[]`; `NodeRuns.runHandshake ... (mark: int -> bool -> int -> unit)`.

- [ ] **Step 1: Write the failing test** (append to the `LinuxPmc` list):

```fsharp
        test "a region recorder keeps the counts of each marked region" {
            linuxOnly ()
            use recorder = new LinuxPmc.RegionRecorder ()
            let pid = Environment.ProcessId
            let tid = LinuxPmc.currentThreadId ()
            recorder.MarkThread pid tid true 7
            recorder.MarkThread pid tid false 7
            Expect.isTrue (recorder.Regions.ContainsKey 7) "region 7 recorded"
        }
```

- [ ] **Step 2: Run to verify it fails.** Expected: build FAIL, `RegionRecorder` not defined.

- [ ] **Step 3: Implement** `RegionRecorder` at the end of `LinuxPmc.fs`:

```fsharp
/// <summary>
/// Counts per region of one thread of another process, opened at the first mark.
/// </summary>
[<Sealed>]
type RegionRecorder () =
    let regions = System.Collections.Generic.Dictionary<int, Counts> ()
    let mutable counters: ThreadCounters option = None

    member _.Regions = regions :> System.Collections.Generic.IReadOnlyDictionary<int, Counts>

    member _.MarkThread (pid: int) (tid: int) (isBegin: bool) (region: int) =
        let c =
            match counters with
            | Some c -> c
            | None ->
                let c = ThreadCounters.Open (pid, tid)
                counters <- Some c
                c

        if isBegin then c.Start () else regions[region] <- c.Stop ()

    /// <summary>
    /// Marks a region of the main thread of process <c>pid</c>.
    /// </summary>
    member this.Mark (pid: int) (isBegin: bool) (region: int) = this.MarkThread pid pid isBegin region

    interface IDisposable with
        member _.Dispose () =
            counters |> Option.iter (fun c -> (c :> IDisposable).Dispose ())
```

In `NodeRuns.runHandshake`, change the parameter to `(mark: int -> bool -> int -> unit)` and the call to `mark p.Id isBegin (int region)`; update its doc comment: "`mark` receives the Node process id and each region boundary, `true` for a `BEGIN`, while the harness waits for the reply."

In `Report.fs`, after `type Run`:

```fsharp
/// <summary>
/// Region counts from a Linux worker's own measurements, in the default source order.
/// </summary>
let linuxRegionCounts (run: WorkerRun) : Pmc.RegionCounts[] =
    run.Cases
    |> Array.collect (fun case ->
        case.Measurements
        |> Array.map (fun m ->
            {
                Pmc.RegionCounts.Region = m.Region
                Pmc.RegionCounts.Values = [| uint64 m.Instructions; uint64 m.Cycles; uint64 m.BranchMisses |]
                Pmc.RegionCounts.Intervals = 1
            }))

/// <summary>
/// Region counts of a Node process's main thread, recorded by a <c>LinuxPmc.RegionRecorder</c>.
/// </summary>
let linuxNodeRegionCounts (regions: System.Collections.Generic.IReadOnlyDictionary<int, LinuxPmc.Counts>) : Pmc.RegionCounts[] =
    regions
    |> Seq.sortBy (fun pair -> pair.Key)
    |> Seq.map (fun pair ->
        {
            Pmc.RegionCounts.Region = pair.Key
            Pmc.RegionCounts.Values = [| uint64 pair.Value.Instructions; uint64 pair.Value.Cycles; uint64 pair.Value.BranchMisses |]
            Pmc.RegionCounts.Intervals = 1
        })
    |> Seq.toArray
```

In `Program.fs`:
- `mark` becomes `let private mark (_: int) (isBegin: bool) (region: int) = ...` (body unchanged).
- In `measure`, when `options.Pmc && OperatingSystem.IsLinux ()`: run the worker without `Pmc.start`, and set `Report.Pmc = Some (Report.linuxRegionCounts worker)`.
- In `measureNode`'s `pmc`, when `OperatingSystem.IsLinux ()`: per repeat, `use recorder = new LinuxPmc.RegionRecorder ()`, run `NodeRuns.runHandshake ... recorder.Mark`, and build `{ Run = run; MainThread = Report.linuxNodeRegionCounts recorder.Regions; AllThreads = Report.linuxNodeRegionCounts recorder.Regions }`. The `mode` text on Linux: `"perf_event_open user-mode counters (" + sources + "); main and all: the main thread"`.
- In `main`, before the existing elevation case:

```fsharp
    | None when options.Pmc && OperatingSystem.IsLinux () && options.Sources <> Pmc.DefaultSources ->
        eprintfn "On Linux the processor counters are fixed: %s." (String.Join (", ", Pmc.DefaultSources))
        2
    | None when options.Pmc && OperatingSystem.IsLinux () && not (LinuxPmc.hardwareAvailable ()) ->
        eprintfn "This machine offers no hardware counters to perf_event_open. Pass --no-pmc, or lower kernel.perf_event_paranoid."
        2
```

and guard the existing elevation case with `OperatingSystem.IsWindows ()`. `--list-sources` on Linux prints the three default source names when `LinuxPmc.hardwareAvailable ()`, otherwise nothing.

- [ ] **Step 4: Run.** `dotnet test bench/Ranvier.Counters.Tests -c Release` (Windows: new test skipped). `./counters.ps1 -NoPmc -Only cutoff -Out $env:TEMP\counters-check` (exit 0). With WSL and .NET: `wsl -e dotnet run --project bench/Ranvier.Counters -c Release -- --only cutoff --out /tmp/c`; expected exit 2 with the "no hardware counters" message, or a report when WSL exposes the PMU.

- [ ] **Step 5: Format and commit** `feat(bench): collect processor counters with perf_event_open on Linux`.

### Task 4: The `ab` subcommand

**Files:**
- Create: `bench/Ranvier.Counters/Workers.fs` (moved from `Program.fs`: `workerEnvironment`, `workerAt`, `runWorker`, made public), compiled after `NodeRuns.fs`
- Create: `bench/Ranvier.Counters/AbModel.fs` (compiled after `Model.fs`), `bench/Ranvier.Counters/Ab.fs` (after `Workers.fs`)
- Modify: `bench/Ranvier.Counters/Program.fs` (use `Workers`; dispatch `ab`)
- Test: `bench/Ranvier.Counters.Tests/AbTests.fs`

**Interfaces:**
- Consumes: `WorkerRun`, `NodeRuns.NodeRun`, `NodeRuns.runHandshake`, `NodeRuns.run`, `LinuxPmc.RegionRecorder`.
- Produces (namespace `CounterBench`, `AbModel.fs`):

```fsharp
type Metric = { Name: string; Value: float }
type Label = { Name: string; Value: string }
type ScenarioFigures = { Scenario: string; Engine: string; Metrics: Metric[] }
/// One process of one commit and runtime ("dotnet" or "node").
type ProcessFigures = { Commit: string; Runtime: string; Process: int; Scenarios: ScenarioFigures[] }
type AbRun =
    {
        Leg: string
        Machine: string
        Base: string
        Head: string
        Versions: Label[]
        /// Runtimes measured at the head only, with the reason.
        NoBaseline: Label[]
        Processes: ProcessFigures[]
        /// Library counters, one process per commit and runtime; metric names start with "lib:".
        Counters: ProcessFigures[]
    }
```

- `Ab.dotnetFigures: commit: string -> index: int -> WorkerRun -> ProcessFigures`, `Ab.nodeFigures: commit: string -> index: int -> NodeRuns.NodeRun -> IReadOnlyDictionary<int, LinuxPmc.Counts> -> ProcessFigures`, `Ab.main: string list -> int`.
- Metric names: `instructions`, `cycles`, `branch_misses`, `cpu_ns` (omitted when the measurement reads -1), `bytes`, `lib:<counter>`. Every value is `(m(2N) - m(N)) / N`.
- A side directory holds `worker/Ranvier.Counters.dll`, `counters-worker/Ranvier.Counters.dll`, `fable/plain/Main.fs.js`, `fable/counters/Main.fs.js` (Task 7 builds it).

- [ ] **Step 1: Write the failing tests** `bench/Ranvier.Counters.Tests/AbTests.fs`:

```fsharp
module CounterBench.Tests.AbTests

open System.Collections.Generic
open Expecto
open CounterBench

let private m ops region bytes instructions =
    {
        Ops = ops
        Region = region
        Bytes = bytes
        Gen0Collections = 0
        NoGcRegion = true
        Instructions = instructions
        Cycles = -1L
        BranchMisses = -1L
        CpuNs = -1L
        Counters = [| { CounterValue.Name = "EffectRuns"; Value = int64 ops } |]
    }

let private worker =
    {
        ProcessId = 1
        ThreadId = 1
        CountersCompiled = false
        Cases =
            [|
                {
                    Scenario = "update"
                    Engine = "Ranvier"
                    Unit = "write"
                    Measurements = [| m 50 0 1000L 10_000L; m 100 1 2000L 15_000L |]
                }
            |]
    }

let private metric (f: ProcessFigures) name =
    f.Scenarios[0].Metrics |> Array.tryFind (fun x -> x.Name = name) |> Option.map _.Value

[<Tests>]
let tests =
    testList "Ab" [
        test "a .NET figure is the 2N measurement minus the N one, per N" {
            let f = Ab.dotnetFigures "abc" 0 worker
            Expect.equal (metric f "instructions") (Some 100.0) "(15000 - 10000) / 50"
            Expect.equal (metric f "bytes") (Some 20.0) "(2000 - 1000) / 50"
        }

        test "an unmeasured counter is omitted, not reported as -1" {
            let f = Ab.dotnetFigures "abc" 0 worker
            Expect.equal (metric f "cycles") None "cycles read -1"
        }

        test "library counters become lib: metrics" {
            let f = Ab.dotnetCounterFigures "abc" worker
            Expect.equal (metric f "lib:EffectRuns") (Some 1.0) "(100 - 50) / 50"
        }

        test "a Node figure takes its hardware counts from the recorded regions" {
            let run: NodeRuns.NodeRun =
                {
                    ProcessId = 2
                    CountersCompiled = false
                    Cases =
                        [|
                            {
                                Scenario = "update"
                                Engine = "Ranvier"
                                Unit = "write"
                                Measurements =
                                    [|
                                        { Ops = 50; Region = 0; Bytes = 100.0; HeapUsedBytes = 0.0; Gen0Collections = 0; NoGcRegion = true; Counters = [||] }
                                        { Ops = 100; Region = 1; Bytes = 300.0; HeapUsedBytes = 0.0; Gen0Collections = 0; NoGcRegion = true; Counters = [||] }
                                    |]
                            }
                        |]
                }

            let regions = Dictionary<int, LinuxPmc.Counts> ()
            regions[0] <- { LinuxPmc.Counts.Unmeasured with Instructions = 1_000L; CpuNs = 500L }
            regions[1] <- { LinuxPmc.Counts.Unmeasured with Instructions = 3_000L; CpuNs = 1_500L }
            let f = Ab.nodeFigures "abc" 0 run regions
            Expect.equal (metric f "instructions") (Some 40.0) "(3000 - 1000) / 50"
            Expect.equal (metric f "cpu_ns") (Some 20.0) "(1500 - 500) / 50"
            Expect.equal (metric f "bytes") (Some 4.0) "(300 - 100) / 50"
        }
    ]
```

- [ ] **Step 2: Run to verify it fails.** Expected: build FAIL, `Ab` not defined.

- [ ] **Step 3: Implement.**

`Workers.fs`: move `workerEnvironment` (with its doc comment), `workerAt` and `runWorker` from `Program.fs` unchanged into `module CounterBench.Workers`, all public. `Program.fs` opens `CounterBench.Workers` and keeps `selfWorker`.

`Ab.fs`:

```fsharp
/// <summary>
/// Alternating measurements of a base and a head build, for the CI comparison.
/// </summary>
module CounterBench.Ab

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text.Json

let private perOp (small: float) (large: float) (n: int) = (large - small) / float n

let private metricsOf (pairs: (string * float * float) list) (n: int) =
    pairs
    |> List.choose (fun (name, small, large) ->
        if small < 0.0 || large < 0.0 then None else Some { Name = name; Value = perOp small large n })
    |> List.toArray

let dotnetFigures (commit: string) (index: int) (run: WorkerRun) : ProcessFigures =
    {
        Commit = commit
        Runtime = "dotnet"
        Process = index
        Scenarios =
            run.Cases
            |> Array.map (fun case ->
                let a, b = case.Measurements[0], case.Measurements[1]

                {
                    Scenario = case.Scenario
                    Engine = case.Engine
                    Metrics =
                        metricsOf
                            [
                                "instructions", float a.Instructions, float b.Instructions
                                "cycles", float a.Cycles, float b.Cycles
                                "branch_misses", float a.BranchMisses, float b.BranchMisses
                                "cpu_ns", float a.CpuNs, float b.CpuNs
                                "bytes", float a.Bytes, float b.Bytes
                            ]
                            a.Ops
                })
    }

let dotnetCounterFigures (commit: string) (run: WorkerRun) : ProcessFigures =
    {
        Commit = commit
        Runtime = "dotnet"
        Process = 0
        Scenarios =
            run.Cases
            |> Array.map (fun case ->
                let a, b = case.Measurements[0], case.Measurements[1]

                {
                    Scenario = case.Scenario
                    Engine = case.Engine
                    Metrics =
                        b.Counters
                        |> Array.map (fun c ->
                            let small =
                                a.Counters
                                |> Array.tryFind (fun x -> x.Name = c.Name)
                                |> Option.map _.Value
                                |> Option.defaultValue 0L

                            { Name = "lib:" + c.Name; Value = perOp (float small) (float c.Value) a.Ops })
                })
    }

let nodeFigures (commit: string) (index: int) (run: NodeRuns.NodeRun) (regions: IReadOnlyDictionary<int, LinuxPmc.Counts>) : ProcessFigures =
    let counts region =
        match regions.TryGetValue region with
        | true, c -> c
        | _ -> LinuxPmc.Counts.Unmeasured

    {
        Commit = commit
        Runtime = "node"
        Process = index
        Scenarios =
            run.Cases
            |> Array.map (fun case ->
                let a, b = case.Measurements[0], case.Measurements[1]
                let ca, cb = counts a.Region, counts b.Region

                {
                    Scenario = case.Scenario
                    Engine = case.Engine
                    Metrics =
                        metricsOf
                            [
                                "instructions", float ca.Instructions, float cb.Instructions
                                "cycles", float ca.Cycles, float cb.Cycles
                                "branch_misses", float ca.BranchMisses, float cb.BranchMisses
                                "cpu_ns", float ca.CpuNs, float cb.CpuNs
                                "bytes", a.Bytes, b.Bytes
                            ]
                            a.Ops
                })
    }

let nodeCounterFigures (commit: string) (run: NodeRuns.NodeRun) : ProcessFigures =
    {
        Commit = commit
        Runtime = "node"
        Process = 0
        Scenarios =
            run.Cases
            |> Array.map (fun case ->
                let a, b = case.Measurements[0], case.Measurements[1]

                {
                    Scenario = case.Scenario
                    Engine = case.Engine
                    Metrics =
                        b.Counters
                        |> Array.map (fun c ->
                            let small =
                                a.Counters
                                |> Array.tryFind (fun x -> x.Name = c.Name)
                                |> Option.map _.Value
                                |> Option.defaultValue 0.0

                            { Name = "lib:" + c.Name; Value = perOp small c.Value a.Ops })
                })
    }

type private Side = { Commit: string; Directory: string }

type private Options =
    {
        Leg: string
        Base: Side option
        Head: Side option
        Rounds: int
        Scale: int
        Only: string option
        Node: string
        Out: string
    }

let rec private parse (o: Options) (args: string list) =
    match args with
    | [] -> o
    | "--leg" :: leg :: rest -> parse { o with Leg = leg } rest
    | "--base" :: sha :: dir :: rest -> parse { o with Base = Some { Commit = sha; Directory = Path.GetFullPath dir } } rest
    | "--head" :: sha :: dir :: rest -> parse { o with Head = Some { Commit = sha; Directory = Path.GetFullPath dir } } rest
    | "--rounds" :: k :: rest -> parse { o with Rounds = int k } rest
    | "--scale" :: k :: rest -> parse { o with Scale = int k } rest
    | "--only" :: p :: rest -> parse { o with Only = Some p } rest
    | "--node" :: p :: rest -> parse { o with Node = p } rest
    | "--out" :: f :: rest -> parse { o with Out = f } rest
    | unknown :: _ -> failwith $"Unknown ab argument '%s{unknown}'."

let private scratch (name: string) =
    let dir = Path.Combine (Path.GetTempPath (), $"ranvier-ab-%d{Environment.ProcessId}-%s{name}")
    Directory.CreateDirectory dir |> ignore
    dir

let private machine () =
    try
        let info = ProcessStartInfo ("lscpu", RedirectStandardOutput = true, UseShellExecute = false)
        use p = Process.Start info
        let text = p.StandardOutput.ReadToEnd ()
        p.WaitForExit ()

        text.Split '\n'
        |> Array.tryFind (fun l -> l.StartsWith "Model name:")
        |> Option.map (fun l -> l.Substring(11).Trim ())
        |> Option.defaultValue Runtime.InteropServices.RuntimeInformation.OSDescription
    with _ ->
        Runtime.InteropServices.RuntimeInformation.OSDescription

let private dotnetRun (o: Options) (side: Side) (worker: string) (name: string) =
    let file = Path.Combine (scratch name, "worker.json")
    Workers.runWorker (Workers.workerAt (Path.Combine (side.Directory, worker, "Ranvier.Counters.dll"))) o.Scale o.Only file

let private nodeRun (o: Options) (side: Side) (name: string) =
    let script = Path.Combine (side.Directory, "fable", "plain", "Main.fs.js")
    use recorder = new LinuxPmc.RegionRecorder ()
    let run = NodeRuns.runHandshake o.Node script o.Scale o.Only (Path.Combine (scratch name, "node.json")) recorder.Mark
    run, Dictionary<int, LinuxPmc.Counts> (recorder.Regions) :> IReadOnlyDictionary<_, _>

/// <summary>
/// Measures base and head alternately, <c>--rounds</c> processes each per runtime, then one
/// library-counter process each per runtime, and writes an <c>AbRun</c> to <c>--out</c>.
/// A base that fails to run is recorded in <c>NoBaseline</c>; a failing head fails the command.
/// </summary>
let main (args: string list) =
    let o =
        parse
            {
                Leg = ""
                Base = None
                Head = None
                Rounds = 5
                Scale = 1
                Only = None
                Node = "node"
                Out = ""
            }
            args

    let baseSide = o.Base |> Option.defaultWith (fun () -> failwith "ab needs --base <sha> <dir>.")
    let head = o.Head |> Option.defaultWith (fun () -> failwith "ab needs --head <sha> <dir>.")
    let processes = ResizeArray<ProcessFigures> ()
    let counters = ResizeArray<ProcessFigures> ()
    let noBaseline = Dictionary<string, string> ()

    let attempt (runtime: string) (side: Side) (body: unit -> unit) =
        if side = baseSide && noBaseline.ContainsKey runtime then
            ()
        else
            try
                body ()
            with e when side = baseSide ->
                eprintfn "Base %s could not be measured: %s" runtime e.Message
                noBaseline[runtime] <- e.Message

    for runtime in [ "dotnet"; "node" ] do
        for round in 0 .. o.Rounds - 1 do
            for side in [ baseSide; head ] do
                attempt runtime side (fun () ->
                    let name = $"%s{runtime}-%s{side.Commit}-%d{round}"

                    if runtime = "dotnet" then
                        processes.Add (dotnetFigures side.Commit round (dotnetRun o side "worker" name))
                    else
                        let run, regions = nodeRun o side name
                        processes.Add (nodeFigures side.Commit round run regions))

    for side in [ baseSide; head ] do
        attempt "dotnet" side (fun () ->
            counters.Add (dotnetCounterFigures side.Commit (dotnetRun o side "counters-worker" $"dotnet-counters-%s{side.Commit}")))

        attempt "node" side (fun () ->
            let script = Path.Combine (side.Directory, "fable", "counters", "Main.fs.js")
            let file = Path.Combine (scratch $"node-counters-%s{side.Commit}", "node.json")
            counters.Add (nodeCounterFigures side.Commit (NodeRuns.run o.Node script o.Scale o.Only file)))

    let run =
        {
            Leg = o.Leg
            Machine = machine ()
            Base = baseSide.Commit
            Head = head.Commit
            Versions = [| { Name = "Node.js"; Value = NodeRuns.version o.Node }; { Name = ".NET"; Value = string Environment.Version } |]
            NoBaseline = noBaseline |> Seq.map (fun p -> { Label.Name = p.Key; Value = p.Value }) |> Seq.toArray
            Processes = processes.ToArray ()
            Counters = counters.ToArray ()
        }

    File.WriteAllText (o.Out, JsonSerializer.Serialize (run, JsonSerializerOptions (WriteIndented = true)))
    0
```

In `Program.main`, dispatch before parsing options:

```fsharp
[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    | "ab" :: rest -> Ab.main rest
    | _ -> legacyMain argv
```

where `legacyMain` is the existing body of `main`, renamed, without `[<EntryPoint>]`. Add `ab` to `usage`: `"  ab --leg <name> --base <sha> <dir> --head <sha> <dir> --out <file> [--rounds k] [--scale k] [--only prefix] [--node path]\n                    Alternate base and head builds; see docs/superpowers/specs/2026-10-16-ci-perf-counters-design.md."`

- [ ] **Step 4: Run.** `dotnet test bench/Ranvier.Counters.Tests -c Release` (Ab tests pass). `./counters.ps1 -NoPmc -Only cutoff -Out $env:TEMP\counters-check` still exits 0.

- [ ] **Step 5: Format and commit** `feat(bench): add the ab subcommand for alternating base and head runs`.

### Task 5: The verdict and the `compare` subcommand

**Files:**
- Create: `bench/Ranvier.Counters/Compare.fs` (after `Ab.fs`)
- Modify: `bench/Ranvier.Counters/Program.fs` (dispatch `compare`)
- Test: `bench/Ranvier.Counters.Tests/CompareTests.fs`

**Interfaces:**
- Consumes: `AbRun`, `ProcessFigures` (Task 4).
- Produces (module `CounterBench.Compare`):

```fsharp
type Kind = Regressed | Improved | Unchanged | Added | Removed | CounterChanged
type Finding = { Runtime: string; Scenario: string; Engine: string; Metric: string; Base: float[]; Head: float[]; Kind: Kind; Delta: float; Accepted: bool }
type Acceptance = { All: bool; Names: string list; Reasons: string list; Unknown: string list; Source: string }
type Verdict = { Leg: string; Gate: string; Fails: bool; Findings: Finding[]; NoBaseline: Label[]; Warnings: string[] }
val median: float[] -> float
val classify: threshold: float -> floor: float -> baseValues: float[] -> headValues: float[] -> Kind
val parseTrailers: messages: string seq -> (string list * string) list
val acceptance: label: bool -> messages: string seq -> known: Set<string> -> Acceptance
val verdict: gate: string -> threshold: float -> floor: float -> failing: bool -> Acceptance -> AbRun -> Verdict
val markdown: Acceptance -> Verdict list -> string
val main: string list -> int
```

- [ ] **Step 1: Write the failing tests** `bench/Ranvier.Counters.Tests/CompareTests.fs`:

```fsharp
module CounterBench.Tests.CompareTests

open Expecto
open CounterBench
open CounterBench.Compare

let private five (x: float) = Array.replicate 5 x
let private around (x: float) = [| x * 0.999; x * 0.9995; x; x * 1.0005; x * 1.001 |]

let private proc commit runtime i (scenario, engine, value) =
    {
        Commit = commit
        Runtime = runtime
        Process = i
        Scenarios = [| { Scenario = scenario; Engine = engine; Metrics = [| { Name = "instructions"; Value = value } |] } |]
    }

let private run (baseRows: (string * string * float[]) list) (headRows: (string * string * float[]) list) =
    let expand commit rows =
        [| for (s, e, values: float[]) in rows do
               for i in 0 .. values.Length - 1 do
                   proc commit "node" i (s, e, values[i]) |]

    {
        Leg = "arm64"
        Machine = "test"
        Base = "b"
        Head = "h"
        Versions = [||]
        NoBaseline = [||]
        Processes = Array.append (expand "b" baseRows) (expand "h" headRows)
        Counters = [||]
    }

let private none = acceptance false [] Set.empty

[<Tests>]
let tests =
    testList "Compare" [
        test "a separated rise above 2% regresses" {
            Expect.equal (classify 0.02 20.0 (around 10_000.0) (around 10_250.0)) Regressed "+2.5%"
        }
        test "a separated rise of 1.9% is unchanged" {
            Expect.equal (classify 0.02 20.0 (around 10_000.0) (around 10_190.0)) Unchanged "+1.9%"
        }
        test "a rise with one head process below one base process is unchanged" {
            let h = [| 9_990.0; 10_300.0; 10_300.0; 10_300.0; 10_300.0 |]
            Expect.equal (classify 0.02 20.0 (five 10_000.0) h) Unchanged "not separated"
        }
        test "the absolute floor holds small scenarios" {
            Expect.equal (classify 0.02 20.0 (five 300.0) (five 315.0)) Unchanged "+15 instructions"
            Expect.equal (classify 0.02 20.0 (five 300.0) (five 325.0)) Regressed "+25 instructions"
        }
        test "a separated fall below the threshold improves" {
            Expect.equal (classify 0.02 20.0 (around 10_000.0) (around 9_000.0)) Improved "-10%"
        }
        test "trailers name scenarios and a reason, with either separator" {
            let parsed =
                parseTrailers [ "fix: x\n\nPerf-Accepted: app-detail, keyed-remove — eager disposal"; "Perf-Accepted: all -- rewrite" ]

            Expect.equal parsed [ ([ "app-detail"; "keyed-remove" ], "eager disposal"); ([ "all" ], "rewrite") ] "two trailers"
        }
        test "a trailer naming an unknown scenario is reported" {
            let a = acceptance false [ "Perf-Accepted: keyed-remov — typo" ] (Set.ofList [ "keyed-remove" ])
            Expect.equal a.Unknown [ "keyed-remov" ] "unknown name"
        }
        test "a scenario only in the head is a known name" {
            let r = run [ "a", "Ranvier", five 100.0 ] [ "a", "Ranvier", five 100.0; "new", "Ranvier", five 50.0 ]
            let known = Compare.scenarioNames r
            let a = acceptance false [ "Perf-Accepted: new — added" ] known
            Expect.isEmpty a.Unknown "new is known"
        }
        test "an unaccepted Ranvier regression fails; an accepted one does not" {
            let r = run [ "x", "Ranvier", around 10_000.0 ] [ "x", "Ranvier", around 11_000.0 ]
            Expect.isTrue (verdict "instructions" 0.02 20.0 true none r).Fails "fails"
            let a = acceptance false [ "Perf-Accepted: x — expected" ] (Set.ofList [ "x" ])
            Expect.isFalse (verdict "instructions" 0.02 20.0 true a r).Fails "accepted"
            Expect.isFalse (verdict "instructions" 0.02 20.0 true (acceptance true [] Set.empty) r).Fails "label"
        }
        test "a reference engine's regression does not fail" {
            let r = run [ "x", "Fable.Ripple", around 10_000.0 ] [ "x", "Fable.Ripple", around 11_000.0 ]
            Expect.isFalse (verdict "instructions" 0.02 20.0 true none r).Fails "reference engine"
        }
        test "added and removed scenarios are listed and do not fail" {
            let r = run [ "gone", "Ranvier", five 1.0 ] [ "new", "Ranvier", five 1.0 ]
            let v = verdict "instructions" 0.02 20.0 true none r
            Expect.isFalse v.Fails "no failure"
            Expect.sequenceEqual (v.Findings |> Array.map _.Kind |> Array.sort) [| Added; Removed |] "kinds"
        }
        test "a runtime without base figures fails nothing" {
            let r = { run [] [ "x", "Ranvier", five 1_000.0 ] with NoBaseline = [| { Name = "node"; Value = "old worker" } |] }
            Expect.isFalse (verdict "instructions" 0.02 20.0 true none r).Fails "no baseline"
        }
        test "a non-failing leg turns regressions into warnings" {
            let r = run [ "x", "Ranvier", around 10_000.0 ] [ "x", "Ranvier", around 12_000.0 ]
            let v = verdict "instructions" 0.15 0.0 false none r
            Expect.isFalse v.Fails "warning leg"
            Expect.hasLength v.Warnings 1 "one warning"
        }
        test "the comment starts with the marker" {
            let r = run [ "x", "Ranvier", five 1.0 ] [ "x", "Ranvier", five 1.0 ]
            let text = markdown none [ verdict "instructions" 0.02 20.0 true none r ]
            Expect.stringStarts text "<!-- ranvier-perf -->" "marker"
        }
    ]
```

- [ ] **Step 2: Run to verify it fails.** Expected: build FAIL, `Compare` not defined.

- [ ] **Step 3: Implement** `bench/Ranvier.Counters/Compare.fs`:

```fsharp
/// <summary>
/// The CI verdict over an <c>AbRun</c>: regressions, improvements, library-counter
/// changes, and the overrides that accept a regression.
/// </summary>
module CounterBench.Compare

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

type Kind =
    | Regressed
    | Improved
    | Unchanged
    | Added
    | Removed
    | CounterChanged

type Finding =
    {
        Runtime: string
        Scenario: string
        Engine: string
        Metric: string
        Base: float[]
        Head: float[]
        Kind: Kind
        /// <summary>Head median over base median, minus one.</summary>
        Delta: float
        Accepted: bool
    }

type Acceptance =
    {
        All: bool
        Names: string list
        Reasons: string list
        /// <summary>Names in a trailer that match no scenario of the base or the head.</summary>
        Unknown: string list
        Source: string
    }

type Verdict =
    {
        Leg: string
        Gate: string
        Fails: bool
        Findings: Finding[]
        NoBaseline: Label[]
        Warnings: string[]
    }

let median (xs: float[]) =
    let s = Array.sort xs
    let n = s.Length
    if n % 2 = 1 then s[n / 2] else (s[n / 2 - 1] + s[n / 2]) / 2.0

/// <summary>
/// <c>Regressed</c> when the head median exceeds the base median by more than
/// <c>max(threshold × |base|, floor)</c> and every head value exceeds every base value;
/// <c>Improved</c> for the mirror image; otherwise <c>Unchanged</c>.
/// </summary>
let classify (threshold: float) (floor: float) (baseValues: float[]) (headValues: float[]) =
    let b, h = median baseValues, median headValues
    let limit = max (threshold * abs b) floor

    if h - b > limit && Array.min headValues > Array.max baseValues then Regressed
    elif b - h > limit && Array.max headValues < Array.min baseValues then Improved
    else Unchanged

let private trailer =
    Regex (@"^Perf-Accepted:\s*(.+?)\s+(?:—|--)\s+(.+?)\s*$", RegexOptions.Multiline)

let parseTrailers (messages: string seq) =
    [
        for message in messages do
            for m in trailer.Matches message do
                let names =
                    m.Groups[1].Value.Split (',', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                    |> List.ofArray

                names, m.Groups[2].Value
    ]

let acceptance (label: bool) (messages: string seq) (known: Set<string>) =
    let parsed = parseTrailers messages
    let names = parsed |> List.collect fst |> List.distinct
    let all = label || List.contains "all" names

    {
        All = all
        Names = names |> List.filter ((<>) "all")
        Reasons = parsed |> List.map snd
        Unknown = names |> List.filter (fun n -> n <> "all" && not (known.Contains n))
        Source =
            if label then "the perf-accepted label"
            elif parsed.IsEmpty then ""
            else "Perf-Accepted trailers"
    }

let scenarioNames (run: AbRun) =
    run.Processes
    |> Seq.collect (fun p -> p.Scenarios |> Seq.map _.Scenario)
    |> Set.ofSeq

let private values (run: AbRun) (commit: string) (runtime: string) (scenario: string) (engine: string) (metric: string) (source: ProcessFigures[]) =
    source
    |> Array.filter (fun p -> p.Commit = commit && p.Runtime = runtime)
    |> Array.choose (fun p ->
        p.Scenarios
        |> Array.tryFind (fun s -> s.Scenario = scenario && s.Engine = engine)
        |> Option.bind (fun s -> s.Metrics |> Array.tryFind (fun m -> m.Name = metric))
        |> Option.map _.Value)

let private isRanvier (engine: string) = engine.StartsWith ("Ranvier", StringComparison.Ordinal)

let verdict (gate: string) (threshold: float) (floor: float) (failing: bool) (accept: Acceptance) (run: AbRun) =
    let noBaseline = run.NoBaseline |> Array.map _.Name |> Set.ofArray

    let keys (commit: string) =
        run.Processes
        |> Array.filter (fun p -> p.Commit = commit)
        |> Array.collect (fun p -> p.Scenarios |> Array.map (fun s -> p.Runtime, s.Scenario, s.Engine))
        |> Set.ofArray

    let baseKeys, headKeys = keys run.Base, keys run.Head
    let accepted (scenario: string) = accept.All || List.contains scenario accept.Names

    let finding runtime scenario engine metric (b: float[]) (h: float[]) kind =
        {
            Runtime = runtime
            Scenario = scenario
            Engine = engine
            Metric = metric
            Base = b
            Head = h
            Kind = kind
            Delta = (if b.Length > 0 && h.Length > 0 && median b <> 0.0 then median h / median b - 1.0 else 0.0)
            Accepted = kind = Regressed && accepted scenario
        }

    let gated =
        [|
            for (runtime, scenario, engine) in Set.union baseKeys headKeys do
                if not (noBaseline.Contains runtime) then
                    let b = values run run.Base runtime scenario engine gate run.Processes
                    let h = values run run.Head runtime scenario engine gate run.Processes

                    match baseKeys.Contains (runtime, scenario, engine), headKeys.Contains (runtime, scenario, engine) with
                    | true, false -> yield finding runtime scenario engine gate b h Removed
                    | false, true -> yield finding runtime scenario engine gate b h Added
                    | _ when b.Length = 0 || h.Length = 0 -> ()
                    | _ ->
                        match classify threshold floor b h with
                        | Unchanged -> ()
                        | kind when isRanvier engine -> yield finding runtime scenario engine gate b h kind
                        | _ -> ()
        |]

    let counterChanges =
        [|
            let names (commit: string) =
                run.Counters
                |> Array.filter (fun p -> p.Commit = commit)
                |> Array.collect (fun p -> p.Scenarios |> Array.collect (fun s -> s.Metrics |> Array.map (fun m -> p.Runtime, s.Scenario, s.Engine, m.Name)))
                |> Set.ofArray

            for (runtime, scenario, engine, metric) in Set.intersect (names run.Base) (names run.Head) do
                let b = values run run.Base runtime scenario engine metric run.Counters
                let h = values run run.Head runtime scenario engine metric run.Counters

                if abs (median h - median b) > 1e-9 then
                    yield finding runtime scenario engine metric b h CounterChanged
        |]

    let regressions = gated |> Array.filter (fun f -> f.Kind = Regressed && not f.Accepted)

    {
        Leg = run.Leg
        Gate = gate
        Fails = failing && (regressions.Length > 0 || not accept.Unknown.IsEmpty)
        Findings = Array.append gated counterChanges
        NoBaseline = run.NoBaseline
        Warnings =
            if failing then
                [||]
            else
                regressions
                |> Array.map (fun f -> $"%s{run.Leg} %s{f.Runtime} %s{f.Scenario} (%s{f.Engine}): %s{gate} %+.1f{f.Delta * 100.0}%%")
    }

let private formatFigure (x: float) = x.ToString ("N0", Globalization.CultureInfo.InvariantCulture)

let markdown (accept: Acceptance) (verdicts: Verdict list) =
    let sb = StringBuilder ()
    let line (s: string) = sb.AppendLine s |> ignore
    line "<!-- ranvier-perf -->"
    let fails = verdicts |> List.exists _.Fails
    line (if fails then "## Performance: regression" else "## Performance: no unaccepted regression")
    line ""

    if not accept.Unknown.IsEmpty then
        line $"""Unknown scenario names in Perf-Accepted: %s{String.Join (", ", accept.Unknown)}."""
        line ""

    if accept.Source <> "" then
        line $"""Accepted by %s{accept.Source}: %s{String.Join ("; ", accept.Reasons)}"""
        line ""

    for v in verdicts do
        line $"### %s{v.Leg} (%s{v.Gate})"
        line ""

        for nb in v.NoBaseline do
            line $"- %s{nb.Name}: no baseline (%s{nb.Value})"

        let section (title: string) (kinds: Kind list) =
            let rows = v.Findings |> Array.filter (fun f -> List.contains f.Kind kinds)

            if rows.Length > 0 then
                line $"**%s{title}**"
                line ""
                line "| Runtime | Scenario | Engine | Metric | Base | Head | Change | |"
                line "|---|---|---|---|---:|---:|---:|---|"

                for f in rows do
                    let b = if f.Base.Length > 0 then formatFigure (median f.Base) else "—"
                    let h = if f.Head.Length > 0 then formatFigure (median f.Head) else "—"
                    let note = if f.Accepted then "accepted" else ""
                    line $"| %s{f.Runtime} | %s{f.Scenario} | %s{f.Engine} | %s{f.Metric} | %s{b} | %s{h} | %+.1f{f.Delta * 100.0}%% | %s{note} |"

                line ""

        section "Regressions" [ Regressed ]
        section "Improvements" [ Improved ]
        section "Library counter changes" [ CounterChanged ]
        section "Added and removed scenarios" [ Added; Removed ]

    sb.ToString ()

type private Options =
    {
        Ab: string list
        Label: bool
        Messages: string option
        Out: string
    }

let rec private parse (o: Options) (args: string list) =
    match args with
    | [] -> o
    | "--ab" :: file :: rest -> parse { o with Ab = o.Ab @ [ file ] } rest
    | "--label" :: value :: rest -> parse { o with Label = Boolean.Parse value } rest
    | "--messages" :: file :: rest -> parse { o with Messages = Some file } rest
    | "--out" :: dir :: rest -> parse { o with Out = dir } rest
    | unknown :: _ -> failwith $"Unknown compare argument '%s{unknown}'."

/// <summary>
/// Reads each <c>--ab</c> run, applies the gate of its leg (arm64: instructions, failing;
/// any other leg: cpu_ns at 15%, warning only), writes <c>verdict.json</c> and <c>comment.md</c>
/// to <c>--out</c>, and exits 1 when a verdict fails.
/// </summary>
let main (args: string list) =
    let o = parse { Ab = []; Label = false; Messages = None; Out = "." } args
    let runs = o.Ab |> List.map (fun f -> JsonSerializer.Deserialize<AbRun> (File.ReadAllText f))
    let known = runs |> List.map scenarioNames |> Set.unionMany

    let messages =
        o.Messages
        |> Option.map (fun f -> (File.ReadAllText f).Split ("\u0000", StringSplitOptions.RemoveEmptyEntries) |> Seq.ofArray)
        |> Option.defaultValue Seq.empty

    let accept = acceptance o.Label messages known

    let verdicts =
        runs
        |> List.map (fun r ->
            if r.Leg = "arm64" then
                verdict "instructions" 0.02 20.0 true accept r
            else
                verdict "cpu_ns" 0.15 0.0 false accept r)

    Directory.CreateDirectory o.Out |> ignore
    File.WriteAllText (Path.Combine (o.Out, "verdict.json"), JsonSerializer.Serialize (verdicts, JsonSerializerOptions (WriteIndented = true)))
    File.WriteAllText (Path.Combine (o.Out, "comment.md"), markdown accept verdicts)
    if verdicts |> List.exists _.Fails then 1 else 0
```

`--messages` reads a file of commit messages separated by NUL, as `git log -z --format=%B` writes them. Dispatch `| "compare" :: rest -> Compare.main rest` in `Program.main`, and add it to `usage`.

Note: System.Text.Json serializes F# unions only with a converter. Before Step 4, serialize `Kind` as its name: give `Finding` the field type `Kind` and add `[<JsonConverter(typeof<JsonStringEnumConverter>)>]`-free handling by mapping `Kind` to a string in the written JSON: write `verdicts |> List.map (fun v -> {| v with Findings = v.Findings |> Array.map (fun f -> {| f with Kind = string f.Kind |}) |})`. Record this in the code as the serialized shape; nothing reads `verdict.json` back as a `Verdict`.

- [ ] **Step 4: Run.** `dotnet test bench/Ranvier.Counters.Tests -c Release`. Expected: every Compare test passes.

- [ ] **Step 5: Format and commit** `feat(bench): add the compare subcommand and the CI verdict`.

### Task 6: Recorded runs and the timeline

**Files:**
- Create: `bench/Ranvier.Counters/History.fs` (after `Compare.fs`)
- Modify: `bench/Ranvier.Counters/Program.fs` (dispatch `record`, `timeline`)
- Test: `bench/Ranvier.Counters.Tests/HistoryTests.fs`

**Interfaces:**
- Consumes: `AbRun`, `Compare.median`, `Compare.parseTrailers`.
- Produces (module `CounterBench.History`): `type RecordedRun = { Commit: string; Parent: string; Date: DateTime; Subject: string; Accepted: string list; Reasons: string list; Runs: AbRun[] }`; `timeline: RecordedRun[] -> string`; `main: string list -> int` handling `record --ab <file>... --commit <sha> --parent <sha> --date <iso> --subject <text> --message <file> --out <file>` and `timeline --runs <dir> --out <file>`.

- [ ] **Step 1: Write the failing tests** `bench/Ranvier.Counters.Tests/HistoryTests.fs`:

```fsharp
module CounterBench.Tests.HistoryTests

open System
open Expecto
open CounterBench

let private ab head (rows: (string * float) list) =
    {
        Leg = "arm64"
        Machine = "test"
        Base = "p"
        Head = head
        Versions = [||]
        NoBaseline = [||]
        Processes =
            [| for i in 0..4 ->
                   {
                       Commit = head
                       Runtime = "node"
                       Process = i
                       Scenarios = [| for (s, v) in rows -> { Scenario = s; Engine = "Ranvier"; Metrics = [| { Name = "instructions"; Value = v } |] } |]
                   } |]
        Counters = [||]
    }

let private recorded commit date rows =
    {
        History.RecordedRun.Commit = commit
        Parent = "p"
        Date = date
        Subject = "subject " + commit
        Accepted = []
        Reasons = []
        Runs = [| ab commit rows |]
    }

[<Tests>]
let tests =
    testList "History" [
        test "the timeline lists commits oldest first with the head medians" {
            let text =
                History.timeline
                    [|
                        recorded "bbbbbbb" (DateTime (2026, 10, 17)) [ "cutoff", 36.0 ]
                        recorded "aaaaaaa" (DateTime (2026, 10, 16)) [ "cutoff", 69.0 ]
                    |]

            let a, b = text.IndexOf "aaaaaaa", text.IndexOf "bbbbbbb"
            Expect.isTrue (a > 0 && b > a) "oldest first"
            Expect.stringContains text "| 69 |" "first median"
            Expect.stringContains text "| 36 |" "second median"
        }

        test "a scenario missing from a run shows a dash" {
            let text =
                History.timeline
                    [|
                        recorded "aaaaaaa" (DateTime (2026, 10, 16)) [ "cutoff", 69.0 ]
                        recorded "bbbbbbb" (DateTime (2026, 10, 17)) [ "cutoff", 36.0; "create", 9_000.0 ]
                    |]

            Expect.stringContains text "## create" "new scenario section"
            Expect.stringContains text "| — |" "dash for the commit without it"
        }
    ]
```

- [ ] **Step 2: Run to verify it fails.** Expected: build FAIL, `History` not defined.

- [ ] **Step 3: Implement** `bench/Ranvier.Counters/History.fs`:

```fsharp
/// <summary>
/// Runs recorded on <c>perf-data</c> and the timeline built from them.
/// </summary>
module CounterBench.History

open System
open System.IO
open System.Text
open System.Text.Json

type RecordedRun =
    {
        Commit: string
        Parent: string
        Date: DateTime
        Subject: string
        Accepted: string list
        Reasons: string list
        Runs: AbRun[]
    }

let private headMedian (run: AbRun) (runtime: string) (scenario: string) (engine: string) =
    let xs =
        run.Processes
        |> Array.filter (fun p -> p.Commit = run.Head && p.Runtime = runtime)
        |> Array.choose (fun p ->
            p.Scenarios
            |> Array.tryFind (fun s -> s.Scenario = scenario && s.Engine = engine)
            |> Option.bind (fun s -> s.Metrics |> Array.tryFind (fun m -> m.Name = "instructions"))
            |> Option.map _.Value)

    if xs.Length = 0 then None else Some (Compare.median xs)

/// <summary>
/// One section per scenario: rows are recorded commits, oldest first; columns are
/// runtime and engine; cells are arm64 head medians of instructions per operation.
/// </summary>
let timeline (runs: RecordedRun[]) =
    let runs = runs |> Array.sortBy _.Date
    let arm (r: RecordedRun) = r.Runs |> Array.tryFind (fun a -> a.Leg = "arm64")

    let columns =
        runs
        |> Array.choose arm
        |> Array.collect (fun a -> a.Processes |> Array.collect (fun p -> p.Scenarios |> Array.map (fun s -> s.Scenario, (p.Runtime, s.Engine))))
        |> Array.distinct

    let scenarios = columns |> Array.map fst |> Array.distinct |> Array.sort
    let sb = StringBuilder ()
    let line (s: string) = sb.AppendLine s |> ignore
    line "# Instruction counts by commit (arm64, instructions per operation, median of 5 processes)"
    line ""

    for scenario in scenarios do
        let cols = columns |> Array.filter (fun (s, _) -> s = scenario) |> Array.map snd |> Array.sort
        line $"## %s{scenario}"
        line ""
        line ("| Commit | Date | Subject | " + String.Join (" | ", cols |> Array.map (fun (rt, e) -> $"%s{rt} %s{e}")) + " |")
        line ("|---|---|---|" + String.replicate cols.Length "---:|")

        for r in runs do
            let cells =
                cols
                |> Array.map (fun (rt, e) ->
                    arm r
                    |> Option.bind (fun a -> headMedian a rt scenario e)
                    |> Option.map (fun v -> v.ToString ("N0", Globalization.CultureInfo.InvariantCulture))
                    |> Option.defaultValue "—")

            let accepted = if List.contains scenario r.Accepted || List.contains "all" r.Accepted then " (accepted)" else ""
            line $"""| `%s{r.Commit.Substring (0, min 7 r.Commit.Length)}` | %s{r.Date.ToString "yyyy-MM-dd"} | %s{r.Subject}%s{accepted} | %s{String.Join (" | ", cells)} |"""

        line ""

    sb.ToString ()

let private options (args: string list) =
    let rec go (acc: Map<string, string list>) (args: string list) =
        match args with
        | [] -> acc
        | key :: value :: rest when key.StartsWith "--" ->
            go (acc |> Map.change key (fun v -> Some ((Option.defaultValue [] v) @ [ value ]))) rest
        | unknown :: _ -> failwith $"Unknown argument '%s{unknown}'."

    go Map.empty args

let main (args: string list) =
    let json = JsonSerializerOptions (WriteIndented = true)

    match args with
    | "record" :: rest ->
        let o = options rest
        let one key = o[key] |> List.head
        let message = File.ReadAllText (one "--message")
        let trailers = Compare.parseTrailers [ message ]

        let run =
            {
                Commit = one "--commit"
                Parent = one "--parent"
                Date = DateTime.Parse (one "--date", Globalization.CultureInfo.InvariantCulture).ToUniversalTime ()
                Subject = one "--subject"
                Accepted = trailers |> List.collect fst
                Reasons = trailers |> List.map snd
                Runs = o["--ab"] |> List.map (fun f -> JsonSerializer.Deserialize<AbRun> (File.ReadAllText f)) |> List.toArray
            }

        File.WriteAllText (one "--out", JsonSerializer.Serialize (run, json))
        0
    | "timeline" :: rest ->
        let o = options rest
        let dir = o["--runs"] |> List.head

        let runs =
            Directory.GetFiles (dir, "*.json")
            |> Array.map (fun f -> JsonSerializer.Deserialize<RecordedRun> (File.ReadAllText f))

        File.WriteAllText (o["--out"] |> List.head, timeline runs)
        0
    | _ -> failwith "History.main takes record or timeline."
```

F# `string list` round-trips through System.Text.Json as a JSON array. Dispatch `| ("record" | "timeline") :: _ -> History.main (List.ofArray argv)` in `Program.main`; add both to `usage`.

- [ ] **Step 4: Run.** `dotnet test bench/Ranvier.Counters.Tests -c Release`. Expected: History tests pass; the record path is exercised in Task 8.

- [ ] **Step 5: Format and commit** `feat(bench): record CI runs and build the instruction timeline`.

### Task 7: `build.fsx` commands and the workflows

**Files:**
- Modify: `build.fsx` (a `Perf` module and four commands) — after merging `master` with the Partas.Build 0.8.1 upgrade
- Create: `.github/workflows/perf.yml`, `.github/workflows/perf-report.yml`

**Interfaces:**
- Consumes: `ab`, `compare`, `record`, `timeline` (Tasks 4–6).
- Produces: `dotnet fsi build.fsx -- perf-build REF DIR`, `perf-ab LEG BASE_SHA BASE_DIR HEAD_SHA HEAD_DIR OUT`, `perf-compare BASE_SHA HEAD_SHA LABEL OUT AB...`, `perf-record SHA OUT_FILE AB...`.

- [ ] **Step 1: Merge `master`** into the branch (`chore: merge master into feat/ci-perf-counters`) and confirm `build.fsx` references Partas.Build 0.8.1. Read how the Cloudsmith source is configured; write it down in the workflow comments only if the workflows need a step for it.

- [ ] **Step 2: Add the commands to `build.fsx`.** Follow the shape of the existing commands (`input { let! ... return stage ... }`, `command "name" { ... }` in the root command). Add `module Perf` before the root command:

```fsharp
module Perf =
    let private argument name = Input.argument<string> name

    /// <summary>A build of REF in DIR: worktree, both .NET workers, both Fable harnesses.</summary>
    let build = input {
        let! reference = argument "REF"
        let! dir = argument "DIR"
        let src = $"{dir}/src"
        return stage $"perf build {reference}" {
            run $"git worktree add --force --detach {src} {reference}"
            stage "tools" {
                workingDir src
                run "dotnet tool restore"
            }
            run $"dotnet build {src}/bench/Ranvier.Counters -c Release -p:RanvierTrace=false -p:RanvierCounters=true -o {dir}/counters-worker"
            run $"dotnet build {src}/bench/Ranvier.Counters -c Release -p:RanvierTrace=false -p:RanvierCounters=false -o {dir}/worker"
            stage "fable plain" {
                workingDir src
                run $"dotnet fable fable/Ranvier.Counters -e .fs.js -o {dir}/fable/plain --noCache --configuration Release"
            }
            stage "fable counters" {
                workingDir src
                envVars [ "RanvierCounters", "true" ]
                run $"dotnet fable fable/Ranvier.Counters -e .fs.js -o {dir}/fable/counters --noCache --configuration Release"
            }
        }
    }

    let ab = input {
        let! leg = argument "LEG"
        let! baseSha = argument "BASE_SHA"
        let! baseDir = argument "BASE_DIR"
        let! headSha = argument "HEAD_SHA"
        let! headDir = argument "HEAD_DIR"
        let! out = argument "OUT"
        return stage "perf ab" {
            run $"dotnet {headDir}/worker/Ranvier.Counters.dll ab --leg {leg} --base {baseSha} {baseDir} --head {headSha} {headDir} --out {out}"
        }
    }

    let compare = input {
        let! baseSha = argument "BASE_SHA"
        let! headSha = argument "HEAD_SHA"
        let! label = argument "LABEL"
        let! out = argument "OUT"
        let! abFiles = Input.argument<string list> "AB"
        let abArgs = abFiles |> List.map (fun f -> $"--ab {f}") |> String.concat " "
        return stage "perf compare" {
            run $"git log -z --format=%%B {baseSha}..{headSha} --output={out}/messages.bin"
            run $"dotnet {out}/../head/worker/Ranvier.Counters.dll compare {abArgs} --label {label} --messages {out}/messages.bin --out {out}"
        }
    }

    let record = input {
        let! sha = argument "SHA"
        let! outFile = argument "OUT_FILE"
        let! abFiles = Input.argument<string list> "AB"
        let abArgs = abFiles |> List.map (fun f -> $"--ab {f}") |> String.concat " "
        return stage "perf record" {
            run $"git log -1 --format=%%B {sha} --output=perf-message.txt"
            run (cmd $"dotnet out/head/worker/Ranvier.Counters.dll record {abArgs} --commit {sha} --parent {sha}^ --date $(git log -1 --format=%%cI {sha}) --subject \"$(git log -1 --format=%%s {sha})\" --message perf-message.txt --out {outFile}")
        }
    }
```

Then register `command "perf-build" { Perf.build }`, `command "perf-ab" { Perf.ab }`, `command "perf-compare" { Perf.compare }`, `command "perf-record" { Perf.record }` in the root command. Where 0.8.1 does not run a step through a shell (so `$(...)` does not expand), compute `--parent`, `--date` and `--subject` in F# with `System.Diagnostics.Process` before returning the stage, and pass them as literal arguments; the `compare` stage writes warnings from `{out}/verdict.json` as Partas.Build annotations:

```fsharp
            runOperation (annotate {
                Annotation.warning message with
                    Title = ValueSome "Performance (x64 timing)"
            })
```

one per entry of each verdict's `Warnings`, read after the compare step. Check every operation used here against `docs/content/Build/CAPABILITIES.md` of Partas.Build 0.8.1 (`run`, `stage`, `workingDir`, `envVars`, `runOperation`, `annotate`, `Input.argument`); replace any that differ with the documented form.

Run locally: `dotnet fsi build.fsx -- perf-build HEAD out/head` then `dotnet fsi build.fsx -- perf-build HEAD~1 out/base` (Windows). Expected: both directories hold `worker/Ranvier.Counters.dll`, `counters-worker/Ranvier.Counters.dll`, `fable/plain/Main.fs.js`, `fable/counters/Main.fs.js`. Remove the worktrees after: `git worktree remove --force out/head/src`, same for base.

- [ ] **Step 3: Write `.github/workflows/perf.yml`:**

```yaml
name: Perf

on:
  pull_request:
    types: [opened, synchronize, reopened, labeled, unlabeled]
  push:
    branches: [master]

permissions:
  contents: read
  actions: read

concurrency:
  group: perf-${{ github.ref }}
  cancel-in-progress: true

jobs:
  find:
    runs-on: ubuntu-latest
    outputs:
      run: ${{ steps.previous.outputs.run }}
      base: ${{ steps.refs.outputs.base }}
      head: ${{ steps.refs.outputs.head }}
    steps:
      - uses: actions/checkout@v7
        with:
          fetch-depth: 0
      - id: refs
        run: |
          if [ "${{ github.event_name }}" = "pull_request" ]; then
            head="${{ github.event.pull_request.head.sha }}"
            base=$(git merge-base "origin/${{ github.base_ref }}" "$head")
          else
            head="${{ github.sha }}"
            base=$(git rev-parse "$head^1")
          fi
          echo "head=$head" >> "$GITHUB_OUTPUT"
          echo "base=$base" >> "$GITHUB_OUTPUT"
      # A label change reuses the figures of an earlier run for the same head.
      - id: previous
        if: github.event.action == 'labeled' || github.event.action == 'unlabeled'
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          run=$(gh run list --workflow perf.yml --commit "${{ steps.refs.outputs.head }}" --status success --json databaseId --jq '.[0].databaseId // empty')
          echo "run=$run" >> "$GITHUB_OUTPUT"

  measure:
    needs: find
    if: needs.find.outputs.run == ''
    strategy:
      fail-fast: false
      matrix:
        include:
          - leg: arm64
            os: ubuntu-24.04-arm
          - leg: x64
            os: ubuntu-latest
    runs-on: ${{ matrix.os }}
    timeout-minutes: 60
    steps:
      - uses: actions/checkout@v7
        with:
          fetch-depth: 0
      - uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json
      - uses: actions/setup-node@v5
        with:
          node-version: '26'
      - run: sudo sysctl -w kernel.perf_event_paranoid=-1
      - run: dotnet fsi build.fsx -- perf-build ${{ needs.find.outputs.base }} out/base
      - run: dotnet fsi build.fsx -- perf-build ${{ needs.find.outputs.head }} out/head
      - run: dotnet fsi build.fsx -- perf-ab ${{ matrix.leg }} ${{ needs.find.outputs.base }} out/base ${{ needs.find.outputs.head }} out/head out/ab-${{ matrix.leg }}.json
      - uses: actions/upload-artifact@v4
        with:
          name: ab-${{ matrix.leg }}
          path: out/ab-${{ matrix.leg }}.json

  verdict:
    needs: [find, measure]
    if: always() && needs.find.result == 'success' && (needs.measure.result == 'success' || needs.measure.result == 'skipped')
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          fetch-depth: 0
      - uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json
      - uses: actions/download-artifact@v4
        with:
          pattern: ab-*
          merge-multiple: true
          path: out
          run-id: ${{ needs.find.outputs.run || github.run_id }}
          github-token: ${{ github.token }}
      - run: dotnet build bench/Ranvier.Counters -c Release -p:RanvierTrace=false -o out/head/worker
      - id: compare
        run: dotnet fsi build.fsx -- perf-compare ${{ needs.find.outputs.base }} ${{ needs.find.outputs.head }} ${{ contains(github.event.pull_request.labels.*.name, 'perf-accepted') }} out/report out/ab-arm64.json out/ab-x64.json
      - if: always()
        run: |
          echo "${{ github.event_name }}" > out/report/kind
          echo "${{ github.event.pull_request.number }}" > out/report/pr
          echo "${{ needs.find.outputs.head }}" > out/report/head
          cp out/ab-*.json out/report/
          cat out/report/comment.md >> "$GITHUB_STEP_SUMMARY"
      - if: always()
        uses: actions/upload-artifact@v4
        with:
          name: perf-report
          path: out/report
```

`perf-compare` exits 1 on a failing verdict, which fails the `verdict` job, which is the check. The `verdict` job builds the tool from the checked-out head (the PR's code, under read-only permissions).

- [ ] **Step 4: Write `.github/workflows/perf-report.yml`:**

```yaml
name: Perf report

on:
  workflow_run:
    workflows: [Perf]
    types: [completed]

permissions:
  contents: write
  pull-requests: write
  actions: read

jobs:
  report:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/download-artifact@v4
        with:
          name: perf-report
          path: report
          run-id: ${{ github.event.workflow_run.id }}
          github-token: ${{ github.token }}
      # Only the artifact is read; no code from the pull request runs here.
      - name: Comment on the pull request
        if: github.event.workflow_run.event == 'pull_request'
        env:
          GH_TOKEN: ${{ github.token }}
          REPO: ${{ github.repository }}
          RUN_HEAD: ${{ github.event.workflow_run.head_sha }}
        run: |
          pr=$(tr -dc '0-9' < report/pr)
          [ -n "$pr" ] || exit 0
          pr_head=$(gh api "repos/$REPO/pulls/$pr" --jq .head.sha)
          if [ "$pr_head" != "$RUN_HEAD" ]; then
            echo "PR $pr head $pr_head is not the measured head $RUN_HEAD; not commenting."
            exit 0
          fi
          id=$(gh api "repos/$REPO/issues/$pr/comments" --paginate --jq '.[] | select(.body | startswith("<!-- ranvier-perf -->")) | .id' | head -1)
          if [ -n "$id" ]; then
            gh api -X PATCH "repos/$REPO/issues/comments/$id" -F body=@report/comment.md
          else
            gh api -X POST "repos/$REPO/issues/$pr/comments" -F body=@report/comment.md
          fi
      - name: Check out master at the measured commit
        if: github.event.workflow_run.event == 'push' && github.event.workflow_run.head_branch == 'master'
        uses: actions/checkout@v7
        with:
          ref: ${{ github.event.workflow_run.head_sha }}
          fetch-depth: 0
          path: src
      - uses: actions/setup-dotnet@v6
        if: github.event.workflow_run.event == 'push' && github.event.workflow_run.head_branch == 'master'
        with:
          global-json-file: src/global.json
      - name: Record the run on perf-data
        if: github.event.workflow_run.event == 'push' && github.event.workflow_run.head_branch == 'master'
        working-directory: src
        env:
          SHA: ${{ github.event.workflow_run.head_sha }}
        run: |
          dotnet build bench/Ranvier.Counters -c Release -p:RanvierTrace=false -o out/head/worker
          git fetch origin perf-data || true
          if git rev-parse --verify origin/perf-data >/dev/null 2>&1; then
            git worktree add ../data origin/perf-data
            git -C ../data switch -c perf-data
          else
            git worktree add --detach ../data
            git -C ../data switch --orphan perf-data
          fi
          mkdir -p ../data/runs
          dotnet fsi build.fsx -- perf-record "$SHA" "../data/runs/$SHA.json" ../report/ab-arm64.json ../report/ab-x64.json
          dotnet out/head/worker/Ranvier.Counters.dll timeline --runs ../data/runs --out ../data/timeline.md
          cd ../data
          git config user.name "github-actions[bot]"
          git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
          git add runs timeline.md
          git commit -m "perf: record $SHA"
          git push origin perf-data
```

- [ ] **Step 5: Validate the YAML** with `actionlint` if installed (`actionlint .github/workflows/perf.yml .github/workflows/perf-report.yml`); otherwise parse both with `node -e "require('yaml')"` is not available, so check them with `dotnet fsi -e` skipped — use `python -c "import yaml,sys; [yaml.safe_load(open(f)) for f in sys.argv[1:]]" .github/workflows/perf.yml .github/workflows/perf-report.yml`. Expected: no error.

- [ ] **Step 6: Format and commit** `ci: measure pull requests and record master with instruction counters`.

### Task 8: Prove it on GitHub

**Files:** none new (fixes go to the files of Tasks 1–7).

- [ ] **Step 1: Ask the user** before pushing `feat/ci-perf-counters` and opening a PR against `master`. Then push (`git push -u origin feat/ci-perf-counters`) and open the PR with `gh pr create --base master` (body: summary and a link to the spec; no "Generated with Claude Code" line).
- [ ] **Step 2: Check the first run** (`gh run list --workflow perf.yml --branch feat/ci-perf-counters`). Expected:
  - `measure (arm64)`: .NET reported as "no baseline" (the merge base's worker calls `kernel32`); Node measured at both commits with non-zero `instructions` in every region (the merge base's Fable harness speaks the same handshake).
  - `measure (x64)`: `cpu_ns` and `bytes` present; no `instructions`.
  - `verdict`: the comment artifact starts with `<!-- ranvier-perf -->`; the check passes unless Node regresses, which on this PR (bench-only changes) would be a defect to investigate.
  - `perf-report`: a comment appears on the PR.
- [ ] **Step 3: Same-commit check.** Run the arm64 leg with base = head once (`workflow_dispatch` is not configured; instead run locally on the runner output is not possible, so push an empty commit `chore: rerun perf` only if Step 2's Node comparison shows a regression or improvement on a bench-only PR — that is the same-code check, since `src` is identical at base and head). Expected: no Ranvier regression or improvement at the gate.
- [ ] **Step 4: Label path.** Add and remove the `perf-accepted` label (`gh pr edit <n> --add-label perf-accepted`; create the label first with `gh label create perf-accepted --color FBCA04 --description "Accepts measured performance regressions"`, after asking). Expected: the `verdict` job reruns without `measure` (`find` reports the earlier run), and the comment notes the label.
- [ ] **Step 5:** Ask the user before deleting the probe branch: `git push origin --delete spike/ci-pmc-probe` and `git worktree remove` of the probe worktree.
- [ ] **Step 6:** After the PR merges (the user merges), confirm the first `perf-data` commit and `timeline.md` exist (`gh api repos/ranvier-org/Ranvier/contents/timeline.md?ref=perf-data`).
