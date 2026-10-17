/// <summary>
/// A fixed workload that runs no Ranvier code, compiled into both the .NET counter bench and its Fable harness. Every
/// worker process measures it last, as a reference for the state of the process and the machine.
/// </summary>
module CounterBench.Control

open System.Collections.Generic

/// <summary>The scenario name of the control in every artifact.</summary>
[<Literal>]
let Scenario = "control"

[<Literal>]
let Size = 256

/// <summary>Written by every operation, so no operation's result is provably unused.</summary>
let mutable private sink = 0

/// <summary>
/// Prepares <c>n</c> operations. One operation refills a <c>Size</c>-entry dictionary with closures over a fixed
/// array, sums the closures' results, and sorts a copy of the array. The dictionary and the closures are built once.
/// </summary>
let prepare (n: int) : Workloads.Workload =
    let values = Array.init Size (fun i -> i * 7919 % Size)
    let reads = Array.init Size (fun i -> fun () -> values[i] + i)
    let table = Dictionary<int, unit -> int>()

    {
        Run =
            fun () ->
                for _ in 1..n do
                    table.Clear ()

                    for i in 0 .. reads.Length - 1 do
                        table[values[i]] <- reads[i]

                    let mutable total = 0

                    for KeyValue (_, read) in table do
                        total <- total + read ()

                    let sorted = Array.sort values
                    sink <- total + sorted[Size - 1]
        Teardown = ignore
    }
