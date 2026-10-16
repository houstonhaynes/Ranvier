module CounterBench.Tests.NodeRunsTests

open System
open System.Diagnostics
open System.IO
open Expecto
open CounterBench

let private nodeAvailable () =
    try
        NodeRuns.version "node" |> ignore
    with :? System.ComponentModel.Win32Exception ->
        skiptest "Node executable is unavailable on PATH"

let private fixture boundaries regions wrongPid body =
    nodeAvailable ()

    let directory =
        Path.Combine (
            Path.GetTempPath (),
            "ranvier-handshake-test-"
            + Guid.NewGuid().ToString "N"
        )

    Directory.CreateDirectory directory |> ignore

    try
        let script = Path.Combine (directory, "fixture.cjs")
        let result = Path.Combine (directory, "result.json")
        let pidFile = Path.Combine (directory, "pid")

        let source =
            "const fs = require('fs');\n"
            + "fs.writeFileSync(__dirname + '/pid', String(process.pid));\n"
            + "for (const line of "
            + System.Text.Json.JsonSerializer.Serialize boundaries
            + ") {\n"
            + "  fs.writeSync(1, line + '\\n'); fs.readSync(0, Buffer.alloc(1), 0, 1, null);\n}\n"
            + "const measurements = "
            + System.Text.Json.JsonSerializer.Serialize regions
            + ";\n"
            + "fs.writeFileSync(process.argv[process.argv.indexOf('--out') + 1], JSON.stringify({ProcessId: process.pid"
            + (if wrongPid then " + 1" else "")
            + ", CountersCompiled:false, Cases:[{Scenario:'fixture', Engine:'Test', Unit:'op', Measurements:measurements.map(Region => ({Region, Ops:1, Bytes:0, HeapUsedBytes:0, Gen0Collections:0, NoGcRegion:true, Counters:[]}))}]}));\n"

        File.WriteAllText (script, source)
        body script result pidFile
    finally
        Directory.Delete (directory, true)

let private expectExited pidFile =
    let pid = int (File.ReadAllText pidFile)

    let running =
        try
            use child = Process.GetProcessById pid
            not child.HasExited
        with :? ArgumentException ->
            false

    Expect.isFalse running "failed handshake child was terminated and reaped"

[<Tests>]
let tests =
    testList
        "Node handshake"
        [
            test "callback receives the real child PID and every boundary" {
                fixture [| "BEGIN 7"; "END 7"; "BEGIN 8"; "END 8" |] [| 7; 8 |] false (fun script result _ ->
                    let marks = ResizeArray<int * bool * int>()

                    let run =
                        NodeRuns.runHandshake "node" script 1 None result (fun pid beginRegion region -> marks.Add (pid, beginRegion, region))

                    Expect.equal
                        (Seq.toList marks)
                        [
                            run.ProcessId, true, 7
                            run.ProcessId, false, 7
                            run.ProcessId, true, 8
                            run.ProcessId, false, 8
                        ]
                        "ordered callback boundaries")
            }
            test "callback failure terminates and waits for a child blocked on reply" {
                fixture [| "BEGIN 7" |] [| 7 |] false (fun script result pidFile ->
                    Expect.throws
                        (fun () ->
                            NodeRuns.runHandshake "node" script 1 None result (fun _ _ _ -> failwith "callback failed")
                            |> ignore)
                        "callback failure propagates"

                    expectExited pidFile)
            }
            test "malformed and inconsistent protocols fail and clean up their children" {
                for boundaries, regions, wrongPid in
                    [
                        [| "BEGIN nope" |], [| 7 |], false
                        [| "BEGIN -1" |], [| 7 |], false
                        [| "BEGIN\t7" |], [| 7 |], false
                        [| "BEGIN 7 extra" |], [| 7 |], false
                        [| "END 7" |], [| 7 |], false
                        [| "BEGIN 7"; "BEGIN 8" |], [| 7; 8 |], false
                        [| "BEGIN 7"; "END 8" |], [| 7 |], false
                        [| "BEGIN 7"; "END 7"; "BEGIN 7" |], [| 7 |], false
                        [| "BEGIN 7"; "END 7"; "END 7" |], [| 7 |], false
                        [| "BEGIN 7" |], [| 7 |], false
                        [| "BEGIN 7"; "END 7" |], [| 8 |], false
                        [| "BEGIN 7"; "END 7" |], [| 7; 7 |], false
                        [| "BEGIN 7"; "END 7" |], [| 7 |], true
                    ] do
                    fixture boundaries regions wrongPid (fun script result pidFile ->
                        Expect.throws
                            (fun () ->
                                NodeRuns.runHandshake "node" script 1 None result (fun _ _ _ -> ())
                                |> ignore)
                            "invalid protocol rejected"

                        expectExited pidFile)
            }
        ]
    |> testSequenced
