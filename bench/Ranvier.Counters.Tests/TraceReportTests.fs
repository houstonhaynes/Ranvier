module CounterBench.Tests.TraceReportTests

open System.Text.Json
open Expecto
open CounterBench
open CounterBench.TraceGrowth
open CounterBench.TraceReport

let private flag (value: bool) =
    if value then "true" else "false"

let private dotnetRun (rows: (string * string * float * bool) list) =
    let body =
        rows
        |> List.map (fun (scenario, engine, bytes, gcFree) ->
            $"""{{"Scenario":"%s{scenario}","Engine":"%s{engine}","BytesPerOp":%g{bytes},"GcFree":%s{flag gcFree}}}""")
        |> String.concat ","

    (JsonDocument.Parse ("{\"Rows\":[" + body + "]}")).RootElement

let private nodeRun (rows: (string * string * float * bool) list) =
    let body =
        rows
        |> List.map (fun (scenario, engine, bytes, gcFree) ->
            $"""{{"Scenario":"%s{scenario}","Engine":"%s{engine}","Bytes":{{"Median":%g{bytes},"Min":0,"Max":0}},"GcFree":%s{flag gcFree}}}""")
        |> String.concat ","

    (JsonDocument.Parse (
        "{\"Rows\":[],\"Fable\":{\"Rows\":["
        + body
        + "]}}"
    ))
        .RootElement

let private row scenario untraced traced gcFree : AllocationRow =
    {
        Runtime = "dotnet"
        Scenario = scenario
        Engine = "Ranvier"
        Untraced = untraced
        Traced = traced
        GcFree = gcFree
    }

let private checkpoint updates events retained ms : Checkpoint =
    {
        Workload = "memo"
        Updates = updates
        Events = events
        RetainedBytes = retained
        EventsMs = ms
        SnapshotMs = ms
        DumpTextMs = ms
        TailReadMs = ms
        TailEvents = if events = 0 then 0 else 1400
    }

let private growth traced =
    let checkpoints =
        if traced then
            [| checkpoint 1000 14016 900000. 0.5; checkpoint 10000 140016 9000000. 5. |]
        else
            [| checkpoint 1000 0 7000. 0.; checkpoint 10000 0 7000. 0. |]

    {
        Runtime = "dotnet"
        Traced = traced
        Checkpoints = checkpoints
        Summaries =
            match summarise checkpoints with
            | Ok s -> [| s |]
            | Error e -> failtest e
    }

let private keyedRows (summaries: (string * string * float) list) (heaps: (string * int * float) list) =
    let summary =
        summaries
        |> List.map (fun (operation, mode, ms) -> $"""{{"operation":"%s{operation}","mode":"%s{mode}","medianMs":%g{ms}}}""")
        |> String.concat ","

    let heap =
        heaps
        |> List.map (fun (mode, count, bytes) -> $"""{{"mode":"%s{mode}","count":%d{count},"retainedBytesPerRow":%g{bytes}}}""")
        |> String.concat ","

    (JsonDocument.Parse ($"""{{"chromium":"140.0","summaries":[%s{summary}],"heaps":[%s{heap}]}}""")).RootElement

let private keyedHeap (results: (string * int * float) list) =
    let body =
        results
        |> List.map (fun (mode, rows, bytes) -> $"""{{"mode":"%s{mode}","rows":%d{rows},"jsBytesPerRow":%g{bytes}}}""")
        |> String.concat ","

    (JsonDocument.Parse ($"""{{"results":[%s{body}]}}""")).RootElement

[<Tests>]
let tests =
    testList
        "TraceReport"
        [
            test "allocation rows pair Ranvier scenarios across the variants" {
                let untraced =
                    dotnetRun
                        [
                            "memo", "Ranvier", 100., true
                            "memo", "R3", 50., true
                            "gone", "Ranvier", 1., true
                        ]

                let traced = dotnetRun [ "memo", "Ranvier", 250., true; "memo", "R3", 50., true ]

                Expect.equal (TraceReport.allocationRows untraced traced) [| row "memo" 100. 250. true |] "one Ranvier row in both variants"
            }

            test "a row is GC-free only when both variants are" {
                let rows =
                    TraceReport.allocationRows
                        (dotnetRun [ "a", "Ranvier", 1., true; "b", "Ranvier", 1., false ])
                        (dotnetRun [ "a", "Ranvier", 2., false; "b", "Ranvier", 2., true ])

                Expect.equal (rows |> Array.map _.GcFree) [| false; false |] "either variant collecting"
            }

            test "allocation rows read the Node.js section by its median" {
                let rows =
                    TraceReport.allocationRows (nodeRun [ "memo", "Ranvier", 40., true ]) (nodeRun [ "memo", "Ranvier", 120., true ])

                Expect.equal
                    (rows
                     |> Array.map (fun r -> r.Runtime, r.Untraced, r.Traced))
                    [| "node", 40., 120. |]
                    "node row"
            }

            test "the report holds versions, ratios, growth and export rows" {
                let text =
                    TraceReport.markdown
                        [| "Source", "abc1234"; "OS", "TestOS" |]
                        [| row "memo" 100. 250. true |]
                        [| growth false; growth true |]
                        None
                        [| "TraceCostBenchmarks", "| Method | Mean |" |]

                Expect.stringContains text "| Source | abc1234 |" "versions"
                Expect.stringContains text "| dotnet | memo | Ranvier | 100 | 250 | 150 | 2.50 | yes |" "allocation ratio"
                Expect.stringContains text "| dotnet | memo | 14.0 | 0.0 | 900.0 | 900.0 | 64.3 |" "growth summary"
                Expect.stringContains text "| dotnet | memo | 10000 | 140016 | 5.000 | 5.000 | 5.000 | 5.000 | 1400 |" "export row"
                Expect.stringContains text "| Method | Mean |" "BenchmarkDotNet table"
            }

            test "Chromium timings pair operations and modes across the variants" {
                let timings =
                    TraceReport.chromiumTimings
                        (keyedRows [ "create1k", "ranvier", 2.; "swap1k", "ranvier", 1.; "create1k", "solid", 9. ] [])
                        (keyedRows [ "create1k", "ranvier", 3.; "create1k", "solid", 9. ] [])

                Expect.equal
                    timings
                    [|
                        {
                            Operation = "create1k"
                            Mode = "ranvier"
                            UntracedMs = 2.
                            TracedMs = 3.
                        }
                        {
                            Operation = "create1k"
                            Mode = "solid"
                            UntracedMs = 9.
                            TracedMs = 9.
                        }
                    |]
                    "operations in both variants"
            }

            test "Chromium heaps take the median of the replicates of each mode and row count" {
                let heaps =
                    TraceReport.chromiumHeaps
                        (keyedRows [] [ "ranvier", 1000, 900.; "ranvier", 1000, 1000.; "ranvier", 1000, 1100. ])
                        (keyedRows [] [ "ranvier", 1000, 2000.; "ranvier", 1000, 1800.; "ranvier", 1000, 2200. ])

                Expect.equal
                    heaps
                    [|
                        {
                            Measure = "retained heap"
                            Mode = "ranvier"
                            Rows = 1000
                            Untraced = 1000.
                            Traced = 2000.
                        }
                    |]
                    "keyed-rows heap"

                let objects =
                    TraceReport.chromiumHeaps (keyedHeap [ "ranvier", 10000, 300. ]) (keyedHeap [ "ranvier", 10000, 450. ])

                Expect.equal
                    (objects
                     |> Array.map (fun h -> h.Measure, h.Rows, h.Untraced, h.Traced))
                    [| "new JS objects", 10000, 300., 450. |]
                    "keyed-heap objects"
            }

            test "the report holds the Chromium rows" {
                let chromium =
                    {
                        Version = "140.0"
                        Timings =
                            [|
                                {
                                    Operation = "create1k"
                                    Mode = "ranvier"
                                    UntracedMs = 2.
                                    TracedMs = 3.
                                }
                            |]
                        Heaps =
                            [|
                                {
                                    Measure = "retained heap"
                                    Mode = "ranvier"
                                    Rows = 1000
                                    Untraced = 1000.
                                    Traced = 2500.
                                }
                            |]
                    }

                let text = TraceReport.markdown [||] [||] [||] (Some chromium) [||]

                Expect.stringContains text "Chromium 140.0" "version"
                Expect.stringContains text "| create1k | ranvier | 2.000 | 3.000 | 1.50 |" "timing ratio"
                Expect.stringContains text "| retained heap | ranvier | 1000 | 1000 | 2500 | 1500 | 2.50 |" "heap ratio"
            }

            test "a zero untraced allocation has no ratio" {
                let text = TraceReport.markdown [||] [| row "signal" 0. 123. true |] [||] None [||]

                Expect.stringContains text "| dotnet | signal | Ranvier | 0 | 123 | 123 | n/a | yes |" "no ratio"
            }

            test "a row with a collection inside a region has no ratio" {
                let text =
                    TraceReport.markdown [||] [| row "timed" 10. -200. false |] [||] None [||]

                Expect.stringContains text "| dotnet | timed | Ranvier | 10 | -200 | -210 | n/a | no |" "collected"
            }
        ]
