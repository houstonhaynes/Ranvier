/// <summary>
/// The DOM members the conformance cases use on the twin equal <c>members.txt</c>. The test runs every twin case on the test's
/// thread inside a served-member recording, so the set is the same whatever other tests run alongside or are filtered out.
/// </summary>
module Ranvier.View.Twin.Tests.MembersTests

open System
open System.IO
open Expecto
open Ranvier.View.Twin
open Ranvier.View.Testing

let private recorded () =
    Path.Combine (TwinHarness.repository (), "experiments", "Ranvier.View.Twin", "members.txt")

let private servedByCases () : string list =
    let scratch =
        Path.Combine (Path.GetTempPath (), "ranvier-members-" + string (Guid.NewGuid ()))

    try
        Bridge.servedDuring (fun () ->
            for moduleName, cases in ConformanceCases.modules do
                for name, run in cases do
                    if not (Conformance.skippedOn "twin" name) then
                        // A failing case fails in the conformance list; the members it used before failing count here.
                        try
                            ConformanceTests.runIn (Path.Combine (scratch, moduleName, TwinHarness.slug name)) run
                        with _ ->
                            ())
    finally
        if Directory.Exists scratch then
            Directory.Delete (scratch, true)

[<Tests>]
let tests =
    testList
        "Bridge coverage"
        [
            testCase "the members the conformance cases use equal members.txt"
            <| fun () ->
                let served = servedByCases ()
                let path = recorded ()

                if Environment.GetEnvironmentVariable "RANVIER_UPDATE_MEMBERS" = "1" then
                    File.WriteAllLines (path, served)
                else
                    let expected = File.ReadAllLines path |> List.ofArray
                    Expect.equal served expected "rerun with RANVIER_UPDATE_MEMBERS=1 to record a changed set"
        ]
