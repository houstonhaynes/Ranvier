module Ranvier.Mcp.Tests.GraphHostTests

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open Ranvier
open Ranvier.Mcp

/// Waits for <c>task</c> to finish, and returns its exception, or null when it ran to completion.
let private faultOf (task: Task) =
    try
        task.Wait (TimeSpan.FromSeconds 10.) |> ignore

        null
    with :? AggregateException as ex ->
        ex.InnerException

[<Tests>]
let tests =
    testList
        "GraphHost"
        [
            test "setup and Invoke run on the host's graph thread, and Invoke returns the work's result" {
                let setupThread = ref 0

                use host =
                    GraphHost.Start (fun graph ->
                        setupThread.Value <- Environment.CurrentManagedThreadId
                        Signal (graph, 20))

                let observed =
                    host.Invoke(fun signal -> Environment.CurrentManagedThreadId, signal.Value + 1).Result

                Expect.notEqual host.GraphThreadId Environment.CurrentManagedThreadId "the graph has its own thread"
                Expect.equal setupThread.Value host.GraphThreadId "setup ran on the graph thread"
                Expect.equal observed (host.GraphThreadId, 21) "the work ran on the graph thread and returned its result"
            }

            test "Invoke faults with the work's exception" {
                use host = GraphHost.Start (fun _ -> ())

                let fault = faultOf (host.Invoke (fun () -> raise (FormatException "bad work")))

                Expect.isTrue (fault :? FormatException) $"the task carries the work's exception, got {fault}"
                Expect.equal (host.Invoke(fun () -> 3).Result) 3 "later invocations still run"
            }

            test "a fault in setup is raised by Start" {
                Expect.throwsT<ArgumentException>
                    (fun () ->
                        GraphHost.Start (fun _ -> raise (ArgumentException "bad setup"))
                        |> ignore)
                    "Start raises the setup's exception"
            }

            test "disposal faults pending invocations without running them, and later invocations fault at the call" {
                let host = GraphHost.Start (fun graph -> Signal (graph, 0))
                use entered = new ManualResetEventSlim ()
                use release = new ManualResetEventSlim ()

                let blocker =
                    host.Invoke (fun _ ->
                        entered.Set ()
                        release.Wait ())

                Expect.isTrue (entered.Wait (TimeSpan.FromSeconds 10.)) "the blocking work started"

                let pending = host.Invoke (fun signal -> signal.Value <- 1)

                let disposal = Task.Run (fun () -> (host :> IDisposable).Dispose())

                SpinWait.SpinUntil ((fun () -> host.IsDisposing), TimeSpan.FromSeconds 10.)
                |> ignore

                release.Set ()

                Expect.isTrue (disposal.Wait (TimeSpan.FromSeconds 10.)) "disposal finished"
                Expect.isNull (faultOf blocker) "the running work completed"

                let fault = faultOf pending
                Expect.isTrue (fault :? ObjectDisposedException) $"the pending invocation faulted, got {fault}"

                let late = faultOf (host.Invoke (fun signal -> signal.Value))
                Expect.isTrue (late :? ObjectDisposedException) $"an invocation after disposal faults, got {late}"
            }

            test "Start with options rejects a thread affinity other than Guarded, and runs no setup" {
                for affinity in [ Serialised; Unchecked ] do
                    let ran = ref false

                    Expect.throwsT<ArgumentException>
                        (fun () ->
                            GraphHost.Start (
                                { GraphOptions.Default with
                                    ThreadAffinity = affinity
                                },
                                fun _ -> ran.Value <- true
                            )
                            |> ignore)
                        $"Start rejects {affinity}"

                    Expect.isFalse ran.Value $"setup did not run under {affinity}"
            }

            test "Start with Guarded options hosts a graph that raises on a pool-thread touch" {
                let leaked = ref Unchecked.defaultof<Signal<int>>

                use host =
                    GraphHost.Start (
                        { GraphOptions.Default with
                            ThreadAffinity = Guarded
                        },
                        fun graph ->
                            leaked.Value <- Signal (graph, 0)
                            leaked.Value
                    )

                let fault = faultOf (Task.Run (fun () -> leaked.Value.Value <- 5))
                Expect.isTrue (fault :? InvalidOperationException) $"a pool-thread write raises, got {fault}"
                Expect.equal (host.Invoke(fun signal -> signal.Value).Result) 0 "the signal kept its value"
            }

            test "a touch from a pool thread raises under the host's Guarded graph" {
                let leaked = ref Unchecked.defaultof<Signal<int>>

                use host =
                    GraphHost.Start (fun graph ->
                        leaked.Value <- Signal (graph, 0)
                        leaked.Value)

                let signal = leaked.Value

                let write = Task.Run (fun () -> signal.Value <- 5)

                let fault = faultOf write
                Expect.isTrue (fault :? InvalidOperationException) $"a pool-thread write raises, got {fault}"
                Expect.equal (host.Invoke(fun signal -> signal.Value).Result) 0 "the signal kept its value"
            }
        ]
