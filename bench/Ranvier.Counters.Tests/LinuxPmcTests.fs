module CounterBench.Tests.LinuxPmcTests

open System
open Expecto
open CounterBench

let private linuxOnly () =
    if not (OperatingSystem.IsLinux ()) then
        skiptest "Linux only"

[<Tests>]
let tests =
    testList
        "LinuxPmc"
        [
            test "Linux report conversion preserves source order and rejects unavailable counters" {
                let regions = System.Collections.Generic.Dictionary<int, LinuxPmc.Counts>()

                let counts: LinuxPmc.Counts =
                    {
                        Instructions = 11L
                        Cycles = 22L
                        BranchMisses = 33L
                        CpuNs = 44L
                    }

                regions.Add (8, counts)
                regions.Add (3, counts)
                let converted = Report.linuxNodeRegionCounts regions
                Expect.equal (converted |> Array.map (fun r -> r.Region)) [| 3; 8 |] "sorted region IDs"
                Expect.equal converted[0].Values [| 11UL; 22UL; 33UL |] "default source order"
                Expect.equal converted[0].Intervals 1 "one measured interval"

                for bad in
                    [
                        { counts with Instructions = -1L }
                        { counts with Cycles = -1L }
                        { counts with BranchMisses = -1L }
                    ] do
                    regions[3] <- bad

                    Expect.throwsT<InvalidOperationException>
                        (fun () -> Report.linuxNodeRegionCounts regions |> ignore)
                        "unavailable never becomes uint64"
            }
            test "Linux worker conversion requires every hardware source in every region" {
                let m: Measurement =
                    {
                        Ops = 1
                        Region = 7
                        Bytes = 0L
                        Gen0Collections = 0
                        NoGcRegion = true
                        Counters = [||]
                        Instructions = 11L
                        Cycles = 22L
                        BranchMisses = 33L
                        CpuNs = 44L
                    }

                let worker measurement : WorkerRun =
                    {
                        ProcessId = 1
                        ThreadId = 1
                        CountersCompiled = false
                        Cases =
                            [|
                                {
                                    Scenario = "fixture"
                                    Engine = "Test"
                                    Unit = "op"
                                    Measurements = [| m; measurement |]
                                }
                            |]
                    }

                let converted = Report.linuxRegionCounts (worker { m with Region = 8 })
                Expect.equal (converted |> Array.map (fun r -> r.Region)) [| 7; 8 |] "worker region IDs"
                Expect.equal converted[1].Values [| 11UL; 22UL; 33UL |] "worker source order"

                for bad in
                    [
                        { m with Instructions = -1L }
                        { m with Cycles = -1L }
                        { m with BranchMisses = -1L }
                    ] do
                    Expect.throwsT<InvalidOperationException>
                        (fun () -> Report.linuxRegionCounts (worker bad) |> ignore)
                        "partial worker hardware fails"
            }
            test "region lifecycle rejects unmatched ends and use after disposal" {
                use recorder = new LinuxPmc.RegionRecorder ()
                Expect.throws (fun () -> recorder.Mark 1 false 0) "end requires a begin"
                Expect.throws (fun () -> recorder.Mark 1 true -1) "nonnegative region"
                (recorder :> IDisposable).Dispose()
                (recorder :> IDisposable).Dispose()
                Expect.throwsT<ObjectDisposedException> (fun () -> recorder.Mark 1 true 0) "disposed recorder"
            }
            test "a region recorder keeps counts and enforces its target and boundaries" {
                linuxOnly ()
                use recorder = new LinuxPmc.RegionRecorder ()
                let pid = Environment.ProcessId
                let tid = LinuxPmc.currentThreadId ()
                recorder.MarkThread pid tid true 7
                Expect.throws (fun () -> recorder.MarkThread pid tid true 8) "nested begin"
                Expect.throws (fun () -> recorder.MarkThread (pid + 1) tid false 7) "different process"
                Expect.throws (fun () -> recorder.MarkThread pid (tid + 1) false 7) "different thread"
                Expect.throws (fun () -> recorder.MarkThread pid tid false 8) "different end"
                recorder.MarkThread pid tid false 7
                Expect.isTrue (recorder.Regions.ContainsKey 7) "region recorded"

                let writable =
                    recorder.Regions :?> System.Collections.Generic.IDictionary<int, LinuxPmc.Counts>

                Expect.throwsT<NotSupportedException> (fun () -> writable.Add (9, LinuxPmc.Counts.Unmeasured)) "view rejects mutation"
                Expect.throws (fun () -> recorder.MarkThread pid tid true 7) "repeated region"
                Expect.throws (fun () -> recorder.MarkThread pid tid false 7) "repeated end"
            }
            test "attributes encode a disabled user-mode hardware counter" {
                let bytes = LinuxPmc.attributes 1UL
                Expect.equal bytes.Length 112 "PERF_ATTR_SIZE_VER5"
                Expect.equal (BitConverter.ToUInt32 (bytes, 0)) 0u "PERF_TYPE_HARDWARE"
                Expect.equal (BitConverter.ToUInt32 (bytes, 4)) 112u "size"
                Expect.equal (BitConverter.ToUInt64 (bytes, 8)) 1UL "config"
                Expect.equal (BitConverter.ToUInt64 (bytes, 32)) 3UL "enabled and running times"
                Expect.equal (BitConverter.ToUInt64 (bytes, 40)) 0x61UL "flags"
            }
            test "schedstat first field is thread CPU nanoseconds" {
                Expect.equal (LinuxPmc.parseSchedstat "123456 789 10\n") 123456L "CPU time"
                Expect.equal (LinuxPmc.parseSchedstat "  123456\t789 10\n") 123456L "whitespace"
            }
            test "hardware validity uses region timing deltas" {
                Expect.equal (LinuxPmc.regionCount 123L 100UL 80UL 150UL 130UL) 123L "previous multiplexing is outside this region"
                Expect.equal (LinuxPmc.regionCount 123L 100UL 80UL 150UL 120UL) -1L "partial runtime"
                Expect.equal (LinuxPmc.regionCount 123L 100UL 80UL 100UL 80UL) -1L "zero runtime"
                Expect.equal (LinuxPmc.regionCount 123L 100UL 80UL 150UL 80UL) -1L "never scheduled"
                Expect.equal (LinuxPmc.regionCount 123L 100UL 80UL 90UL 130UL) -1L "decreasing enabled time"
                Expect.equal (LinuxPmc.regionCount 123L 100UL 80UL 150UL 70UL) -1L "decreasing running time"
            }
            test "unmeasured counts use minus one" {
                let counts = LinuxPmc.Counts.Unmeasured
                Expect.equal (counts.Instructions, counts.Cycles, counts.BranchMisses, counts.CpuNs) (-1L, -1L, -1L, -1L) "sentinels"
            }
            test "thread counters measure work and disposal is idempotent" {
                linuxOnly ()

                use counters =
                    LinuxPmc.ThreadCounters.Open (Environment.ProcessId, LinuxPmc.currentThreadId ())

                let measure n =
                    counters.Start ()
                    let mutable sum = 0L

                    for i in 1..n do
                        sum <- sum + int64 i

                    Threading.Thread.Sleep 1
                    let counts = counters.Stop ()
                    GC.KeepAlive sum
                    counts

                let small = measure 1_000_000
                let large = measure 10_000_000
                Expect.isGreaterThan large.CpuNs 0L "CPU time"

                if counters.Hardware then
                    Expect.isGreaterThan large.Instructions small.Instructions "more work"
                else
                    Expect.equal large.Instructions -1L "unavailable PMU"

                (counters :> IDisposable).Dispose()
                (counters :> IDisposable).Dispose()
                Expect.throwsT<ObjectDisposedException> (fun () -> counters.Start ()) "disposed counter"
            }
        ]
