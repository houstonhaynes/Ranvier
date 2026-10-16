/// <summary>User-mode hardware counters and CPU nanoseconds for a Linux thread.</summary>
module CounterBench.LinuxPmc

open System
open System.IO
open System.Globalization
open System.Runtime.InteropServices

/// <summary>Counts over a measured region; unavailable measurements have value -1.</summary>
type Counts =
    {
        Instructions: int64
        Cycles: int64
        BranchMisses: int64
        CpuNs: int64
    }

    /// <summary>All measurements set to -1.</summary>
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

    [<DllImport("libc", EntryPoint = "gettid", SetLastError = true)>]
    extern int gettid()

    [<DllImport("libc", SetLastError = true)>]
    extern int ioctl(int fd, unativeint request, nativeint arg)

    [<DllImport("libc", SetLastError = true)>]
    extern nativeint read(int fd, byte[] buffer, unativeint count)

    [<DllImport("libc")>]
    extern int close(int fd)

let private perfEventOpen =
    match RuntimeInformation.ProcessArchitecture with
    | Architecture.X64 -> Some 298L
    | Architecture.Arm64 -> Some 241L
    | _ -> None

/// <summary>Encodes a disabled, user-mode hardware event as a 112-byte perf_event_attr.</summary>
let attributes (config: uint64) : byte[] =
    let bytes = Array.zeroCreate<byte> 112
    BitConverter.GetBytes(112u).CopyTo(bytes, 4)
    BitConverter.GetBytes(config).CopyTo(bytes, 8)
    BitConverter.GetBytes(3UL).CopyTo(bytes, 32)
    BitConverter.GetBytes(0x61UL).CopyTo(bytes, 40)
    bytes

/// <summary>Parses CPU nanoseconds from the first whitespace-separated schedstat field.</summary>
let parseSchedstat (text: string) =
    Int64.Parse (text.Split((null: char[]), StringSplitOptions.RemoveEmptyEntries).[0], CultureInfo.InvariantCulture)

/// <summary>Returns the raw count for positive, equal enabled/running deltas; incomplete regions return -1.</summary>
let internal regionCount (value: int64) (enabledStart: uint64) (runningStart: uint64) (enabledEnd: uint64) (runningEnd: uint64) =
    if
        value < 0L
        || enabledEnd <= enabledStart
        || runningEnd <= runningStart
        || enabledEnd - enabledStart
           <> runningEnd - runningStart
    then
        -1L
    else
        value

/// <summary>Returns the calling Linux thread's native ID.</summary>
let currentThreadId () =
    if not (OperatingSystem.IsLinux ()) then
        raise (PlatformNotSupportedException ("Linux thread IDs require Linux."))

    let tid = Native.gettid ()

    if tid <= 0 then
        raise (IOException ("gettid failed."))

    tid

/// <summary>Owns hardware counters and reads CPU time for a Linux thread.</summary>
/// <remarks>Call Start and Stop on the measuring thread. Dispose releases native descriptors and is idempotent.</remarks>
[<Sealed>]
type ThreadCounters private (pid: int, tid: int, opened: int[]) =
    let schedstat = $"/proc/{pid}/task/{tid}/schedstat"
    let mutable fds = opened
    let mutable disposed = false
    let mutable running = false
    let mutable cpuStart = 0L
    let timingStart = Array.zeroCreate<uint64 * uint64> 3
    let buffer = Array.zeroCreate<byte> 24

    let release () =
        for fd in fds do
            Native.close fd |> ignore

        fds <- [||]

    let ensureAlive () =
        if disposed then
            raise (ObjectDisposedException (nameof ThreadCounters))

    let command request =
        let mutable succeeded = true

        for fd in fds do
            if Native.ioctl (fd, request, 0n) <> 0 then
                succeeded <- false

        if not succeeded then
            release ()

    let readCounter fd =
        if Native.read (fd, buffer, 24un) = 24n then
            Some (BitConverter.ToInt64 (buffer, 0), BitConverter.ToUInt64 (buffer, 8), BitConverter.ToUInt64 (buffer, 16))
        else
            None

    /// <summary>Opens counters for positive process and thread IDs; unavailable hardware falls back to CPU time.</summary>
    static member Open(pid: int, tid: int) =
        if not (OperatingSystem.IsLinux ()) then
            raise (PlatformNotSupportedException ("Thread counters require Linux."))

        if pid <= 0 then
            invalidArg (nameof pid) "A positive process ID is required."

        if tid <= 0 then
            invalidArg (nameof tid) "A positive thread ID is required."

        let opened =
            match perfEventOpen with
            | Some syscall ->
                [| 1UL; 0UL; 5UL |]
                |> Array.map (fun config -> int (Native.perfEventOpen (syscall, attributes config, tid, -1, -1, 8un)))
            | None -> [||]

        if opened |> Array.exists (fun fd -> fd < 0) then
            for fd in opened do
                if fd >= 0 then
                    Native.close fd |> ignore

            new ThreadCounters (pid, tid, [||])
        else
            new ThreadCounters (pid, tid, opened)

    /// <summary>Whether all three hardware events remain available.</summary>
    member _.Hardware = not disposed && fds.Length = 3

    /// <summary>Starts a fresh region; a failed hardware reset or enable switches to CPU time.</summary>
    member _.Start() =
        ensureAlive ()

        if running then
            invalidOp "A counter region is already running."

        cpuStart <- parseSchedstat (File.ReadAllText schedstat)
        command 0x2403un
        let mutable valid = true

        for index in 0 .. fds.Length - 1 do
            match readCounter fds[index] with
            | Some (_, enabled, running) -> timingStart[index] <- enabled, running
            | None -> valid <- false

        if not valid then
            release ()

        command 0x2400un
        running <- true

    /// <summary>Stops the region; failed reads or partial PMU runtime produce -1 for all hardware measurements.</summary>
    member _.Stop() : Counts =
        ensureAlive ()

        if not running then
            invalidOp "A counter region has not been started."

        running <- false
        command 0x2401un
        let cpuEnd = parseSchedstat (File.ReadAllText schedstat)
        let values = Array.create 3 -1L

        if fds.Length = 3 then
            let mutable valid = true

            for index in 0..2 do
                match readCounter fds[index] with
                | Some (value, enabled, running) ->
                    let enabledStart, runningStart = timingStart[index]
                    values[index] <- regionCount value enabledStart runningStart enabled running

                    if values[index] < 0L then
                        valid <- false
                | None -> valid <- false

            if not valid then
                release ()
                Array.fill values 0 3 -1L

        {
            Instructions = values[0]
            Cycles = values[1]
            BranchMisses = values[2]
            CpuNs = cpuEnd - cpuStart
        }

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                release ()

/// <summary>Whether this process can open all three Linux hardware counters.</summary>
let hardwareAvailable () =
    OperatingSystem.IsLinux ()
    && (use counters = ThreadCounters.Open (Environment.ProcessId, currentThreadId ())
        counters.Hardware)

/// <summary>Owns sequential region measurements for a fixed Linux process and thread.</summary>
/// <remarks>Region IDs must be nonnegative and unique. Dispose releases descriptors and is idempotent.</remarks>
[<Sealed>]
type RegionRecorder() =
    let regions = System.Collections.Generic.Dictionary<int, Counts>()
    let view = System.Collections.ObjectModel.ReadOnlyDictionary<int, Counts> regions
    let mutable counters: ThreadCounters option = None
    let mutable target: (int * int) option = None
    let mutable active: int option = None
    let mutable disposed = false

    /// <summary>Completed regions, exposed through a read-only view.</summary>
    member _.Regions = view :> System.Collections.Generic.IReadOnlyDictionary<int, Counts>

    /// <summary>Marks a boundary; rejects changed targets, overlapping regions and unmatched ends.</summary>
    member _.MarkThread (pid: int) (tid: int) (isBegin: bool) (region: int) =
        if disposed then
            raise (ObjectDisposedException (nameof RegionRecorder))

        if pid <= 0 || tid <= 0 then
            invalidArg (nameof pid) "Positive process and thread IDs are required."

        if region < 0 then
            invalidArg (nameof region) "A nonnegative region ID is required."

        match target with
        | Some previous when previous <> (pid, tid) -> invalidOp "The recorder target cannot change."
        | _ -> ()

        if isBegin then
            if active.IsSome then
                invalidOp "A region is already running."

            if regions.ContainsKey region then
                invalidOp "A region can only be recorded once."

            let c =
                match counters with
                | Some c -> c
                | None ->
                    let c = ThreadCounters.Open (pid, tid)
                    counters <- Some c
                    target <- Some (pid, tid)
                    c

            c.Start ()
            active <- Some region
        else
            if active <> Some region then
                invalidOp "The end must match the active region."

            let counts = counters.Value.Stop ()
            active <- None
            regions.Add (region, counts)

    /// <summary>Marks a boundary on the main thread of the specified process.</summary>
    member this.Mark (pid: int) (isBegin: bool) (region: int) =
        this.MarkThread pid pid isBegin region

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true

                counters
                |> Option.iter (fun c -> (c :> IDisposable).Dispose())
