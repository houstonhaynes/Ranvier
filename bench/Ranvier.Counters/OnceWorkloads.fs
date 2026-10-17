/// <summary>
/// Memo-free keyed workloads, compiled into both the .NET counter bench and its Fable harness. The Fable harness
/// loads this module only after measuring every case of <c>Scenarios.all</c>.
/// </summary>
module CounterBench.OnceWorkloads

open System
open Ranvier
open CounterBench.Workloads

/// <summary>Written by every reaction, so no reaction's result is provably unused.</summary>
let mutable private sink = 0

let private teardown (owner: Owner) (graph: Graph) () =
    owner.Dispose ()
    (graph :> IDisposable).Dispose()

/// <summary>
/// <c>keyedPass</c> on the view's list path: memo-free rows (<c>createProjectionOnce</c> with <c>CreateFirst</c>), each
/// row's factory creating one effect over its item, and one effect over the source reading the keys untracked. Every
/// write shares the initial list's items.
/// </summary>
let keyedOncePass (edit: KeyedItem[] -> KeyedItem[]) (n: int) =
    let graph = newGraph ()
    let initial = Array.init Rows (fun i -> { Id = i; Value = i })
    let edited = edit initial

    let writes =
        Array.init n (fun op -> Array.copy (if op % 2 = 0 then edited else initial))

    let owner, source =
        graph.CreateRoot (fun owner ->
            use _active = graph.Activate ()
            let source = createSignal initial

            let rows =
                createProjectionOnce
                    ProjectionOptions.CreateFirst
                    (fun item -> item.Id)
                    (fun item ->
                        createEffect (fun () -> sink <- (item ()).Value)
                        ref 0)
                    (fun () -> source.Value :> seq<_>)

            createEffect (fun () ->
                source.Value |> ignore
                sink <- untrack (fun () -> rows.Keys.Length))

            owner, source)

    {
        Run =
            fun () ->
                for write in writes do
                    source.Value <- write
        Teardown = teardown owner graph
    }
