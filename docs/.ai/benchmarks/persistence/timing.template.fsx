// Times each selected case at N and 2N with a Stopwatch, best of 3 regions each, and prints
// "scenario|engine|ns per op" lines: (t(2N) - t(N)) / N.
#I @"WORKER"
#r @"WORKER\Ranvier.dll"
#r @"WORKER\Ranvier.Elmish.dll"
#r @"WORKER\Ranvier.View.dll"
#r @"WORKER\FSharp.Data.Adaptive.dll"
#r @"WORKER\R3.dll"
#r @"WORKER\Ranvier.Counters.dll"

open System
open System.Diagnostics
open CounterBench.Scenarios

let selected = (Environment.GetEnvironmentVariable "BENCH_ONLY").Split ','

let time (case: Case) (ops: int) =
    let prepared = case.Prepare ops

    try
        GC.Collect ()
        GC.WaitForPendingFinalizers ()
        GC.Collect ()
        let watch = Stopwatch.StartNew ()
        prepared.Run ()
        watch.Elapsed.TotalNanoseconds
    finally
        prepared.Teardown ()

for case in all 1 do
    if selected |> Array.contains case.Scenario then
        time case case.Ops |> ignore

        let best ops =
            List.init 3 (fun _ -> time case ops) |> List.min

        let n = best case.Ops
        let n2 = best (2 * case.Ops)
        printfn "%s|%s|%.1f" case.Scenario case.Engine ((n2 - n) / float case.Ops)
