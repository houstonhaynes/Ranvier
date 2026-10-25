/// <summary>Server render workloads: each operation renders one 100-row keyed table as one request.</summary>
module CounterBench.ServerWorkloads

open System
open Ranvier.View
open Ranvier.View.Representation

let private options =
    { Server.Options.defaults with
        Policy = Omit
    }

/// <summary>
/// A workload of <c>n</c> server renders of the keyed fixture at 100 rows, each a request inside one graph from
/// <c>Workloads.newGraph</c>; the teardown disposes the graph.
/// </summary>
let serverRender (n: int) : Workloads.Workload =
    let graph = Workloads.newGraph ()

    {
        Run =
            fun () ->
                for _ in 1..n do
                    let sink = HtmlSink ()
                    Server.renderOn graph null options ServerFixtures.keyedPage 100 sink
                    sink.ToString () |> ignore
        Teardown = fun () -> (graph :> IDisposable).Dispose()
    }
