module Ranvier.Analyzers.Tests.Ran013

open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private findings (body: string) =
    let source =
        "module Fixture\nopen System.Threading\nopen System.Threading.Tasks\nopen Ranvier\n"
        + body

    run Analyzers.ran013Cli (context (ranvier.Force ()) source)

let private codes body =
    findings body |> List.map _.Code

[<Tests>]
let tests =
    testList
        "RAN013"
        [
            test "a write in Task.Run, with its exact message" {
                Expect.equal
                    (findings "let f (temperature: Signal<int>) = Task.Run (fun () -> temperature.Value <- 25)\n"
                     |> List.map _.Message)
                    [
                        "`temperature.Value <- 25` runs on a thread-pool thread; a write off the graph's owning thread raises under `ThreadAffinity.Guarded`. Hand the write to `graph.Dispatch (fun () -> ...)`."
                    ]
                    "one message"
            }

            test "Pump in ThreadPool.QueueUserWorkItem" {
                Expect.equal (codes "let f (graph: Graph) = ThreadPool.QueueUserWorkItem (fun _ -> graph.Pump () |> ignore)\n") [ "RAN013" ] "Pump"
            }

            test "Signal.update in Parallel.For" {
                Expect.equal
                    (codes "let f (s: Signal<int>) = Parallel.For (0, 4, fun _ -> Signal.update s ((+) 1)) |> ignore\n")
                    [ "RAN013" ]
                    "update"
            }

            test "a write in a new Thread" { Expect.equal (codes "let f (s: Signal<int>) = Thread (fun () -> s.Value <- 1)\n") [ "RAN013" ] "Thread" }

            test "a write inside graph.Dispatch inside Task.Run" {
                Expect.isEmpty
                    (codes "let f (graph: Graph) (s: Signal<int>) = Task.Run (fun () -> graph.Dispatch (fun () -> s.Value <- 1))\n")
                    "dispatched"
            }

            test "Settle inside Task.Run" {
                Expect.isEmpty (codes "let f (source: AsyncSource<int>) = Task.Run (fun () -> source.Settle 1)\n") "settle waits in the inbox"
            }

            test "a write to each signal of a collection in Parallel.ForEach" {
                Expect.equal
                    (codes
                        "let f (signals: Signal<int> list) = Parallel.ForEach (signals, fun (s: Signal<int>) -> s.Value <- 0) |> ignore
")
                    [ "RAN013" ]
                    "lambda parameter"
            }

            test "Signal.update of each signal in Parallel.ForEach" {
                Expect.equal
                    (codes
                        "let f (signals: Signal<int> list) = Parallel.ForEach (signals, fun s -> Signal.update s ((+) 1)) |> ignore
")
                    [ "RAN013" ]
                    "update target"
            }

            test "a write to a loop variable in Task.Run" {
                Expect.equal
                    (codes
                        "let f (signals: Signal<int> list) = Task.Run (fun () -> for s in signals do s.Value <- 1)
")
                    [ "RAN013" ]
                    "loop variable"
            }

            test "a write to an alias of an outer signal in Task.Run" {
                Expect.equal
                    (codes
                        "let f (shared: Signal<int>) = Task.Run (fun () -> let s = shared in s.Value <- 1)
")
                    [ "RAN013" ]
                    "alias"
            }

            test "a write in a pool task nested in the thread that owns the graph" {
                let body =
                    "let f () =
    Thread (fun () ->
        use g = new Graph ()
        let s = Signal (g, 0)
        Task.Run (fun () -> s.Value <- 1) |> ignore)
"

                Expect.equal (codes body) [ "RAN013" ] "the pool runs the write"
            }

            test "a write in Parallel.Invoke and in ContinueWith" {
                let body =
                    "let f (s: Signal<int>) (t: Task) =
    Parallel.Invoke (fun () -> s.Value <- 1)
    t.ContinueWith (fun (_: Task) -> s.Value <- 2) |> ignore
"

                Expect.equal (codes body) [ "RAN013"; "RAN013" ] "both starters"
            }

            test "the finding's range is the write" {
                let range =
                    (findings "let f (temperature: Signal<int>) = Task.Run (fun () -> temperature.Value <- 25)\n").Head.Range

                Expect.equal (range.StartLine, range.StartColumn, range.EndColumn) (5, 55, 78) "the assignment"
            }

            test "a write to a graph the thread creates" {
                Expect.isEmpty
                    (codes
                        "let f () = Thread (fun () -> let g = new Graph () in let s = Signal (g, 0) in s.Value <- 1; g.Pump () |> ignore)
")
                    "the thread owns the graph"
            }

            test "a write to a signal created through the thread's own graph's Run" {
                Expect.isEmpty
                    (codes
                        "let f () =\n    Thread (fun () ->\n        use g = new Graph ()\n        let s = g.Run (fun () -> createSignal 0)\n        s.Value <- 1)\n")
                    "g.Run on the owning thread"
            }

            test "a write inside the thread's own graph's Run" {
                Expect.isEmpty
                    (codes
                        "let f () =\n    Thread (fun () ->\n        use g = new Graph ()\n        g.Run (fun () -> let s = createSignal 0 in s.Value <- 1))\n")
                    "the owning thread"
            }

            test "a write in an ordinary lambda" {
                Expect.isEmpty (codes "let f (s: Signal<int>) = List.iter (fun x -> s.Value <- x) [ 1 ]\n") "same thread"
            }
        ]
