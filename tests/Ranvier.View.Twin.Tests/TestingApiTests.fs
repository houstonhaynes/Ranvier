module Ranvier.View.Twin.Tests.TestingApiTests

open Expecto
open Ranvier.View.Twin
open Ranvier.View.Testing

[<Tests>]
let tests =
    testList
        "Testing API"
        [
            testCase "a conformance case that leaves a listener on the body fails"
            <| fun () ->
                let scratch =
                    System.IO.Path.Combine (
                        System.IO.Path.GetTempPath (),
                        "ranvier-leftover-"
                        + string (System.Guid.NewGuid ())
                    )

                let leaking =
                    Conformance.ofChecks
                        [|
                            "leaks",
                            fun () ->
                                let body = (Conformance.document ()).body
                                body.addEventListener ("click", listener = Fable.Core.U2.Case1 (fun (_: Fable.Core.TS.Dom.Event) -> ()))
                        |]
                    |> Array.head
                    |> snd

                try
                    Expect.throwsC (fun () -> ConformanceTests.runIn scratch leaking) (fun e ->
                        Expect.stringContains e.Message "body 'click' listener" "named")
                finally
                    if System.IO.Directory.Exists scratch then
                        System.IO.Directory.Delete (scratch, true)

            testCase "disposing an event source out of order or on another thread raises"
            <| fun () ->
                let source =
                    { new EventSpec.IEventSource with
                        member _.Create _ =
                            Unchecked.defaultof<_>
                    }

                let first = EventSpec.install source
                let second = EventSpec.install source
                Expect.throwsT<System.InvalidOperationException> (fun () -> first.Dispose ()) "out of order"
                let failure = ref null

                let thread =
                    System.Threading.Thread (fun () ->
                        try
                            second.Dispose ()
                        with e ->
                            failure.Value <- e)

                thread.Start ()
                thread.Join ()
                Expect.isTrue (failure.Value :? System.InvalidOperationException) "another thread"
                second.Dispose ()
                first.Dispose ()

            testCase "check clicks only when the state differs"
            <| fun () ->
                let host = TwinHost.Create ()
                use _ = host.Document
                let root = host.Document.CreateElement "div"
                Html.parseInto root "<input type=\"checkbox\" checked>"
                let clicks = ref 0

                Events.listen root "click" true (fun _ -> clicks.Value <- clicks.Value + 1)
                |> ignore

                Interact.check root.Native "input" true
                Expect.equal clicks.Value 0 "already checked"
                Interact.check root.Native "input" false
                Expect.equal clicks.Value 1 "one click"

            testCase "input fires beforeinput and skips the write when it is cancelled"
            <| fun () ->
                let host = TwinHost.Create ()
                use _ = host.Document
                let root = host.Document.CreateElement "div"
                Html.parseInto root "<input>"

                Events.listen root "beforeinput" true (fun e -> (Events.eventOf e).PreventDefault())
                |> ignore

                Interact.input root.Native "input" "x"
                Expect.equal (root.FirstChild :?> TwinElement).Value "" "not written"

            testCase "select sets the value, then fires input and change"
            <| fun () ->
                let host = TwinHost.Create ()
                use _ = host.Document
                let root = host.Document.CreateElement "div"
                Html.parseInto root "<select><option value=\"a\">A</option><option value=\"b\">B</option></select>"
                let log = ResizeArray<string>()

                Events.listen root "input" true (fun _ -> log.Add "input")
                |> ignore

                Events.listen root "change" true (fun _ -> log.Add "change")
                |> ignore

                Interact.select root.Native "select" "b"

                Expect.equal ((root.FirstChild :?> TwinElement).Value, List.ofSeq log) ("b", [ "input"; "change" ]) "selected"
        ]
