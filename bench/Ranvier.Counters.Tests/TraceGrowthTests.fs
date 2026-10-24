module CounterBench.Tests.TraceGrowthTests

open Expecto
open CounterBench
open CounterBench.TraceGrowth

let private checkpoint (updates: int) (events: int) (retained: float) : Checkpoint =
    {
        Workload = "memo"
        Updates = updates
        Events = events
        RetainedBytes = retained
        EventsMs = 0.
        SnapshotMs = 0.
        DumpTextMs = 0.
        TailReadMs = 0.
        TailEvents = 0
    }

let private summary (checkpoints: Checkpoint[]) =
    match summarise checkpoints with
    | Ok summary -> summary
    | Error message -> failtest message

[<Tests>]
let tests =
    testList
        "TraceGrowth.summarise"
        [
            test "an exact linear series gives its exact slopes" {
                let s =
                    summary
                        [|
                            for updates in [ 1000; 10000; 100000 ] -> checkpoint updates (14 * updates + 16) (1100. * float updates + 3000.)
                        |]

                Expect.equal s.Workload "memo" "workload"
                Expect.equal s.Checkpoints 3 "checkpoints"
                Expect.floatClose Accuracy.high s.EventSlope 14. "events per update, marginal"
                Expect.floatClose Accuracy.high s.RetainedSlope 1100. "retained bytes per update, marginal"
                Expect.floatClose Accuracy.high s.BytesPerEvent (1100. / 14.) "retained bytes per event"
                Expect.floatClose Accuracy.high s.EventsPerUpdate (float (14 * 100000 + 16) / 100000.) "events per update at the last checkpoint"
            }

            test "an untraced series of zero events gives zero slopes" {
                let s =
                    summary [| for updates in [ 1000; 10000; 100000 ] -> checkpoint updates 0 5000. |]

                Expect.equal s.EventSlope 0. "event slope"
                Expect.equal s.RetainedSlope 0. "retained slope"
                Expect.equal s.BytesPerEvent 0. "bytes per event"
                Expect.equal s.EventsPerUpdate 0. "events per update"
            }

            test "the checkpoints may arrive in any order" {
                let s = summary [| checkpoint 100000 1400000 0.; checkpoint 1000 14000 0. |]

                Expect.floatClose Accuracy.high s.EventsPerUpdate 14. "events per update at the most updates"
            }

            test "fewer than two checkpoints is an error" {
                Expect.isError (summarise [||]) "no checkpoints"
                Expect.isError (summarise [| checkpoint 1000 14000 0. |]) "one checkpoint"
            }

            test "checkpoints at one update count are an error" {
                Expect.isError (summarise [| checkpoint 1000 14000 0.; checkpoint 1000 14000 0. |]) "one update count"
            }

            test "checkpoints of two workloads are an error" {
                Expect.isError
                    (summarise
                        [|
                            checkpoint 1000 14000 0.
                            { checkpoint 10000 140000 0. with
                                Workload = "fanout"
                            }
                        |])
                    "two workloads"
            }
        ]

[<Tests>]
let probeTests =
    testList
        "TraceGrowth.probe"
        [
            test "the JSON written for a small run round-trips through the reader" {
                let run =
                    TraceGrowth.run TraceGrowth.dotnetHost "dotnet" [ "memo", [| 100; 300 |]; "fanout", [| 100; 200 |] ]

                Expect.equal
                    (run.Checkpoints
                     |> Array.map (fun c -> c.Workload, c.Updates))
                    [| "memo", 100; "memo", 300; "fanout", 100; "fanout", 200 |]
                    "checkpoints in plan order"

                Expect.equal (run.Summaries |> Array.map _.Workload) [| "memo"; "fanout" |] "one summary per workload"

                for c in run.Checkpoints do
                    Expect.equal (c.Events > 0) run.Traced "events exactly when traced"
                    Expect.equal (c.TailEvents > 0) run.Traced "tail events exactly when traced"

                let file =
                    System.IO.Path.Combine (
                        System.IO.Path.GetTempPath (),
                        "ranvier-trace-growth-"
                        + string (System.Guid.NewGuid ())
                        + ".json"
                    )

                try
                    TraceGrowth.write file run
                    Expect.equal (TraceGrowth.read file) run "read back"
                finally
                    System.IO.File.Delete file
            }

            test "checkpoints closer than the tail read are refused" {
                Expect.throws
                    (fun () ->
                        TraceGrowth.run TraceGrowth.dotnetHost "dotnet" [ "memo", [| 100; 150 |] ]
                        |> ignore)
                    "gap under TailUpdates"

                Expect.throws
                    (fun () ->
                        TraceGrowth.run TraceGrowth.dotnetHost "dotnet" [ "memo", [| 50; 300 |] ]
                        |> ignore)
                    "first checkpoint under TailUpdates"
            }

            test "the reader accepts the Fable harness's JSON" {
                let json =
                    """{"Runtime":"node","Traced":true,"Checkpoints":[{"Workload":"memo","Updates":1000,"Events":14017,"RetainedBytes":1941904,"EventsMs":0.0029,"SnapshotMs":7.1,"DumpTextMs":6.2,"TailReadMs":0.0034,"TailEvents":1400}],"Summaries":[]}"""

                let file =
                    System.IO.Path.Combine (
                        System.IO.Path.GetTempPath (),
                        "ranvier-trace-growth-node-"
                        + string (System.Guid.NewGuid ())
                        + ".json"
                    )

                try
                    System.IO.File.WriteAllText (file, json)
                    let growth = TraceGrowth.read file
                    Expect.equal growth.Runtime "node" "runtime"
                    Expect.equal growth.Checkpoints[0].Events 14017 "events"
                    Expect.equal growth.Checkpoints[0].RetainedBytes 1941904. "retained bytes"
                finally
                    System.IO.File.Delete file
            }
        ]
