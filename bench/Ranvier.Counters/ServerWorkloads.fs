/// <summary>Server render workloads: each operation renders one 100-row keyed table as one request.</summary>
module CounterBench.ServerWorkloads

/// <summary>A workload of <c>n</c> server renders of the keyed fixture at 100 rows.</summary>
let serverRender (n: int) : Workloads.Workload =
    {
        Run =
            fun () ->
                for _ in 1..n do
                    ServerFixtures.renderKeyedPage 100 |> ignore
        Teardown = ignore
    }
