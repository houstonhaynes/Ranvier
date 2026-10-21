module Ranvier.View.Twin.Tests.SessionTests

open System
open Expecto
open Fable.Core.TS.Dom
open Ranvier
open Ranvier.View.Twin
open Ranvier.View
open Ranvier.View.Twin.Tests.Fixtures

let private items (session: Session) =
    let list = session.Host.querySelectorAll "li"
    [ for i in 0 .. int list.length - 1 -> Bridge.nodeOf (list.item (float i)) ]

[<Tests>]
let tests =
    testList
        "Session"
        [
            testCase "a mounted component renders and a click updates it"
            <| fun () ->
                use session = Twin.mount counter 1
                Expect.equal (session.Text ()) "1" "first render"

                (session.Host.querySelector "button").Value
                |> unbox<HTMLElement>
                |> fun b -> b.click ()

                Expect.equal (session.Text ()) "2" "after the click"

            testCase "removing two middle rows keeps the other three"
            <| fun () ->
                let source = ref Unchecked.defaultof<Signal<int array>>

                use session =
                    Twin.mountView TwinMount.defaults (fun context ->
                        source.Value <- Api.createSignal [| 1; 2; 3; 4; 5 |]
                        Program.run (Ranvier.View.Template.Component.render rows source.Value) context.Backend)

                let before = items session
                session.Act (fun () -> source.Value.Value <- [| 1; 2; 5 |])
                Expect.equal (session.Text ()) "125" "text"
                let after = items session

                Expect.isTrue
                    (obj.ReferenceEquals (after[0], before[0])
                     && obj.ReferenceEquals (after[2], before[4]))
                    "nodes kept"

            testCase "sessions on two threads are isolated"
            <| fun () ->
                let results = Collections.Concurrent.ConcurrentBag<string>()
                let sessionOfFirst = ref Unchecked.defaultof<Session>
                let started = new Threading.ManualResetEventSlim ()

                let first =
                    Threading.Thread (fun () ->
                        use session = Twin.mount counter 10
                        sessionOfFirst.Value <- session
                        started.Set ()
                        Threading.Thread.Sleep 50
                        results.Add (session.Text ()))

                let second =
                    Threading.Thread (fun () ->
                        use session = Twin.mount counter 20

                        (session.Host.querySelector "button").Value
                        |> unbox<HTMLElement>
                        |> fun b -> b.click ()

                        results.Add (session.Text ()))

                first.Start ()
                started.Wait ()
                second.Start ()
                second.Join ()
                first.Join ()
                Expect.equal (Set.ofSeq results) (set [ "10"; "21" ]) "each thread saw its own document"
                Expect.throwsT<InvalidOperationException> (fun () -> sessionOfFirst.Value.Host.nodeType |> ignore) "cross-thread use"

            testCase "a handler failure under Raise ends the interaction that caused it"
            <| fun () ->
                use session = Twin.mount failing ()

                let button =
                    (session.Host.querySelector "button").Value
                    |> unbox<HTMLElement>

                Expect.throwsC (fun () -> button.click ()) (fun e ->
                    match e with
                    | TwinListenerException (eventType, _) -> Expect.equal eventType "click" "the click's listener"
                    | other -> failtestf "expected TwinListenerException, got %O" other)

            testCase "Collect keeps the reports on the session"
            <| fun () ->
                use session =
                    Twin.mountWith
                        { TwinMount.defaults with
                            Reports = Reports.Collect
                        }
                        failing
                        ()

                let button =
                    (session.Host.querySelector "button").Value
                    |> unbox<HTMLElement>

                button.click ()
                Expect.equal session.Reports.Length 1 "one report, and the click returned"
                Expect.stringContains (session.Reports |> List.concat |> List.head).Message "handler failed" "the handler's error"

                match session.Reports |> List.concat |> List.head with
                | TwinListenerException (eventType, _) -> Expect.equal eventType "click" "the event type is kept"
                | other -> failtestf "expected TwinListenerException, got %O" other

            testCase "queued commits apply when the session settles"
            <| fun () ->
                use session =
                    Twin.mountWith
                        { TwinMount.defaults with
                            Commits = Commits.Queued Ranvier.View.Dom.Scheduling.CollectAndContinue
                        }
                        counter
                        1

                session.Clock.RunMicrotasks () |> ignore

                (session.Host.querySelector "button").Value
                |> unbox<HTMLElement>
                |> fun b -> b.click ()

                Expect.equal (session.Text ()) "2" "settled after the dispatch"
        ]
