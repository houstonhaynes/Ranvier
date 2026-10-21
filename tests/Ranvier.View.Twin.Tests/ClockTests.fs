module Ranvier.View.Twin.Tests.ClockTests

open Expecto
open Ranvier.View.Twin

[<Tests>]
let tests =
    testList
        "Clock"
        [
            testCase "timers run by due time, then insertion, with microtasks after each"
            <| fun () ->
                let clock = TwinClock ()
                let log = ResizeArray<string>()

                clock.SetTimeout (
                    (fun () ->
                        log.Add "b"
                        clock.Post (fun () -> log.Add "b-micro")),
                    10.0
                )
                |> ignore

                clock.SetTimeout ((fun () -> log.Add "a"), 5.0)
                |> ignore

                clock.SetTimeout ((fun () -> log.Add "c"), 10.0)
                |> ignore

                clock.AdvanceBy 10.0
                Expect.equal (List.ofSeq log) [ "a"; "b"; "b-micro"; "c" ] "order"
                Expect.equal clock.Now 10.0 "now"

            testCase "frames run at each FrameMs boundary crossed"
            <| fun () ->
                let clock = TwinClock ()
                let stamps = ResizeArray<float>()

                let rec frame (t: float) =
                    stamps.Add t

                    if stamps.Count < 3 then
                        clock.RequestAnimationFrame frame |> ignore

                clock.RequestAnimationFrame frame |> ignore
                clock.AdvanceBy 40.0
                Expect.equal (List.ofSeq stamps) [ 16.0; 32.0 ] "two boundaries"
                clock.Frame ()
                Expect.equal (List.ofSeq stamps) [ 16.0; 32.0; 40.0 ] "Frame runs at Now"

            testCase "real time never advances the clock"
            <| fun () ->
                let clock = TwinClock ()
                System.Threading.Thread.Sleep 20
                Expect.equal clock.Now 0.0 "unchanged"

            testCase "Settle raises when a queue keeps refilling"
            <| fun () ->
                let clock = TwinClock ()
                use graph = new Ranvier.Graph ()

                let rec again () =
                    clock.Post again

                clock.Post again

                Expect.throwsC (fun () -> clock.Settle (graph, 100)) (fun e ->
                    match e with
                    | TwinRunawayException (queue, _) -> Expect.equal queue "microtasks" "queue"
                    | other -> failtestf "unexpected %O" other)

            testCase "the TimeProvider reads twin time"
            <| fun () ->
                let clock = TwinClock ()
                clock.AdvanceBy 1500.0
                Expect.equal (clock.TimeProvider.GetUtcNow ()) (System.DateTimeOffset.UnixEpoch.AddMilliseconds 1500.0) "now"

            testCase "Settle raises for a microtask that a timer keeps reposting"
            <| fun () ->
                let clock = TwinClock ()
                use graph = new Ranvier.Graph ()
                // Bounded so the unfixed clock returns instead of spinning: it reposts two million times.
                let left = ref 2_000_000

                let rec again () =
                    left.Value <- left.Value - 1

                    if left.Value > 0 then
                        clock.Post again

                clock.SetTimeout ((fun () -> clock.Post again), 0.0)
                |> ignore

                Expect.throwsC (fun () -> clock.Settle (graph, 100)) (fun e ->
                    match e with
                    | TwinRunawayException (queue, _) -> Expect.equal queue "microtasks" "queue"
                    | other -> failtestf "unexpected %O" other)

            testCase "Settle skips a due timer that an earlier timer of the round cleared"
            <| fun () ->
                let clock = TwinClock ()
                use graph = new Ranvier.Graph ()
                let log = ResizeArray<string>()
                let b = ref 0

                clock.SetTimeout (
                    (fun () ->
                        log.Add "a"
                        clock.ClearTimeout b.Value),
                    0.0
                )
                |> ignore

                b.Value <- clock.SetTimeout ((fun () -> log.Add "b"), 0.0)
                clock.Settle graph
                Expect.equal (List.ofSeq log) [ "a" ] "b was cleared"

            testCase "AdvanceBy rejects a negative duration"
            <| fun () ->
                let clock = TwinClock ()
                Expect.throwsT<System.ArgumentOutOfRangeException> (fun () -> clock.AdvanceBy -1.0) "negative"
                Expect.equal clock.Now 0.0 "unchanged"

            testCase "a periodic timer disposed in its own callback runs once"
            <| fun () ->
                let clock = TwinClock ()
                let runs = ref 0
                let timer = ref Unchecked.defaultof<System.Threading.ITimer>

                timer.Value <-
                    clock.TimeProvider.CreateTimer (
                        (fun _ ->
                            runs.Value <- runs.Value + 1
                            timer.Value.Dispose ()),
                        null,
                        System.TimeSpan.FromMilliseconds 10.0,
                        System.TimeSpan.FromMilliseconds 10.0
                    )

                clock.AdvanceBy 100.0
                Expect.equal runs.Value 1 "disposed"

            testCase "a periodic timer changed in its own callback follows the new schedule"
            <| fun () ->
                let clock = TwinClock ()
                let stamps = ResizeArray<float>()
                let timer = ref Unchecked.defaultof<System.Threading.ITimer>

                timer.Value <-
                    clock.TimeProvider.CreateTimer (
                        (fun _ ->
                            stamps.Add clock.Now

                            if stamps.Count = 1 then
                                timer.Value.Change (System.TimeSpan.FromMilliseconds 30.0, System.TimeSpan.FromMilliseconds 30.0)
                                |> ignore),
                        null,
                        System.TimeSpan.FromMilliseconds 10.0,
                        System.TimeSpan.FromMilliseconds 10.0
                    )

                clock.AdvanceBy 70.0
                Expect.equal (List.ofSeq stamps) [ 10.0; 40.0; 70.0 ] "rescheduled once"

            testCase "a clock used from another thread raises"
            <| fun () ->
                let clock = TwinClock ()
                let failure = ref null

                let thread =
                    System.Threading.Thread (fun () ->
                        try
                            clock.Post ignore
                        with e ->
                            failure.Value <- e)

                thread.Start ()
                thread.Join ()
                Expect.isTrue (failure.Value :? System.InvalidOperationException) "owner thread"
        ]
