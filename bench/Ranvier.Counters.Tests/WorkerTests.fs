module CounterBench.Tests.WorkerTests

open System
open System.Runtime
open System.IO
open System.Text.Json
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
    testList
        "Worker.measure"
        [
            test "fresh Linux workers retain warm scale-invariant first-region instruction differentials" {
                if
                    not (OperatingSystem.IsLinux ())
                    || not (LinuxPmc.hardwareAvailable ())
                then
                    skiptest "Linux hardware counters required"

                let directory =
                    Path.Combine (Path.GetTempPath (), "ranvier-jit-test-" + Guid.NewGuid().ToString("N"))

                Directory.CreateDirectory directory |> ignore

                try
                    let figure scale =
                        let run =
                            Workers.runWorker
                                (Workers.workerAt typeof<WorkerRun>.Assembly.Location)
                                scale
                                (Some "cutoff")
                                (Path.Combine (directory, $"{scale}.json"))

                        let first = run.Cases[0]
                        Expect.equal (first.Measurements |> Array.map _.Region) [| 0; 1 |] "discarded warmup does not leak into retained regions"
                        let a, b = first.Measurements[0], first.Measurements[1]
                        Expect.isGreaterThan a.Instructions 0L "N instructions"
                        Expect.isGreaterThan b.Instructions 0L "2N instructions"

                        let value =
                            float (b.Instructions - a.Instructions)
                            / float a.Ops

                        Expect.isGreaterThan value 0. "first differential is positive"
                        value

                    let one, two = figure 1, figure 2
                    Expect.isLessThan (abs (one - two) / max one two) 0.10 "normalized first-region counts agree across scales"
                finally
                    Directory.Delete (directory, true)
            }
            test "empty Linux selection publishes no warmup measurement" {
                if not (OperatingSystem.IsLinux ()) then
                    skiptest "Linux only"

                let output = Path.GetTempFileName ()

                try
                    Worker.run 1 (Some "no-such-counter-scenario") output
                    let run = JsonSerializer.Deserialize<WorkerRun>(File.ReadAllText output)
                    Expect.isEmpty run.Cases "no selected case"
                finally
                    File.Delete output
            }
            test "without Linux counters every native figure reads -1" {
                let m = Worker.measure None allocating 1000 0
                Expect.equal (m.Instructions, m.Cycles, m.BranchMisses, m.CpuNs) (-1L, -1L, -1L, -1L) "unmeasured"
            }
            test "Linux counter reads leave allocation bytes unchanged" {
                if not (OperatingSystem.IsLinux ()) then
                    skiptest "Linux only"

                use counters =
                    LinuxPmc.ThreadCounters.Open (Environment.ProcessId, LinuxPmc.currentThreadId ())

                let accountingWork =
                    { allocating with
                        Prepare =
                            fun ops ->
                                let work = allocating.Prepare ops

                                { work with
                                    Run =
                                        fun () ->
                                            work.Run ()
                                            Threading.Thread.Sleep 1
                                }
                    }

                let without = Worker.measure None accountingWork 1_000_000 0
                let measured = Worker.measure (Some counters) accountingWork 1_000_000 1
                Expect.equal measured.Bytes without.Bytes "counter reads sit outside allocation reads"
                Expect.isGreaterThan measured.CpuNs 0L "a substantial allocation workload records thread CPU"
            }
            test "failed work tears down and exits the no-GC region" {
                let mutable disposed = false

                let broken =
                    { allocating with
                        Prepare =
                            fun _ ->
                                {
                                    Run = (fun () -> failwith "work failed")
                                    Teardown = (fun () -> disposed <- true)
                                }
                    }

                Expect.throws (fun () -> Worker.measure None broken 1 0 |> ignore) "work failure propagates"
                Expect.isTrue disposed "teardown runs on failure"
                Expect.notEqual GCSettings.LatencyMode GCLatencyMode.NoGCRegion "no-GC region closed"
            }
            test "failed Linux work leaves counters reusable" {
                if not (OperatingSystem.IsLinux ()) then
                    skiptest "Linux only"

                use counters =
                    LinuxPmc.ThreadCounters.Open (Environment.ProcessId, LinuxPmc.currentThreadId ())

                let broken =
                    { allocating with
                        Prepare =
                            fun _ ->
                                {
                                    Run = (fun () -> failwith "work failed")
                                    Teardown = ignore
                                }
                    }

                Expect.throws
                    (fun () ->
                        Worker.measure (Some counters) broken 1 0
                        |> ignore)
                    "work failure propagates"

                let next = Worker.measure (Some counters) allocating 1000 1
                Expect.isGreaterThanOrEqual next.CpuNs 0L "next measurement succeeds"
            }
            test "legacy report JSON retains its original measurement shape while transport includes Linux fields" {
                let measurement = Worker.measure None allocating 1 0

                let names (options: JsonSerializerOptions) =
                    use json =
                        JsonDocument.Parse (
                            JsonSerializer.Serialize (
                                {|
                                    Runs = [| {| Measurements = [| measurement |] |} |]
                                |},
                                options
                            )
                        )

                    (json.RootElement.GetProperty("Runs").[0].GetProperty("Measurements").[0]).EnumerateObject()
                    |> Seq.map (fun p -> p.Name)
                    |> Seq.toList

                Expect.equal
                    (names (Program.reportJsonOptions ()))
                    [ "Ops"; "Region"; "Bytes"; "Gen0Collections"; "NoGcRegion"; "Counters" ]
                    "legacy ordered shape"

                Expect.containsAll
                    (names (JsonSerializerOptions ()))
                    [ "Instructions"; "Cycles"; "BranchMisses"; "CpuNs" ]
                    "transport fields retained"
            }
        ]
    |> testSequenced
