module Ranvier.View.Twin.Tests.GoldenTests

open System
open System.IO
open Expecto

// The reviewed dumps, one file per artefact of each DumpChecks case.
let private goldens () =
    Path.Combine (TwinHarness.repository (), "tests", "Ranvier.View.Twin.Tests", "dumps")

let private updating =
    Environment.GetEnvironmentVariable "RANVIER_UPDATE_DUMPS" = "1"

[<Tests>]
let tests =
    testList
        "Golden"
        [
            for name, run in DumpChecks.cases do
                testCase name
                <| fun () ->
                    let directory =
                        Path.Combine (Path.GetTempPath (), "ranvier-golden-" + Guid.NewGuid().ToString "N")

                    try
                        ConformanceTests.runIn directory run

                        for artefact in Directory.GetFiles directory |> Array.sort do
                            let golden =
                                Path.Combine (
                                    goldens (),
                                    TwinHarness.slug name
                                    + "-"
                                    + Path.GetFileName artefact
                                )

                            let actual = File.ReadAllText artefact

                            if updating then
                                Directory.CreateDirectory (goldens ()) |> ignore
                                File.WriteAllText (golden, actual)
                            else
                                Expect.isTrue (File.Exists golden) $"{Path.GetFileName golden} is recorded; rerun with RANVIER_UPDATE_DUMPS=1"
                                Expect.equal actual (File.ReadAllText golden) (Path.GetFileName golden)
                    finally
                        if Directory.Exists directory then
                            Directory.Delete (directory, true)
        ]
