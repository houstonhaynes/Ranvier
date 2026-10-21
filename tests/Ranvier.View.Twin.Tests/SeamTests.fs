module Ranvier.View.Twin.Tests.SeamTests

open Expecto
open Ranvier.View

[<Tests>]
let tests =
    testList
        "Departures seam"
        [
            testCase "the process seam is an object on .NET"
            <| fun () -> Expect.isNotNull (box Departures.seam) "seam"

            testCase "a fresh seam starts with every flag off"
            <| fun () ->
                let seam = Departures.createSeam ()
                Expect.isFalse seam.Enabled "exits off"
                Expect.isFalse seam.ReflowEnabled "reflows off"
                Expect.isNull (box seam.NextLive) "operations null"

            testCase "a fresh seam's operations are settable"
            <| fun () ->
                let seam = Departures.createSeam ()
                seam.NextLive <- System.Func<_, _> id
                seam.Enabled <- true
                Expect.isNotNull (box seam.NextLive) "set"
                Expect.isTrue seam.Enabled "flag set"
        ]
