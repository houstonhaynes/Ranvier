// An external async caller reads and writes a graph owned by a dedicated thread, through Graph.DispatchAsync.
// Needs a Release build:
//   dotnet build src/Ranvier -c Release
// Run: dotnet fsi samples/dispatch-async.fsx
#if INTERACTIVE
#r "../src/Ranvier/bin/Release/net10.0/Ranvier.dll"
#endif

#if !INTERACTIVE
module DispatchAsyncSample
#endif

open System.Threading
open System.Threading.Tasks
open Ranvier

/// <summary>A dispatcher that wakes the owner thread when the graph posts a drain.</summary>
type WakingDispatcher() =
    let wake = new AutoResetEvent (false)
    member _.Wake = wake

    interface IGraphDispatcher with
        member _.Post _ =
            wake.Set () |> ignore

/// <summary>The graph the owner thread holds, and the nodes the caller reaches through it.</summary>
type Owned =
    {
        Graph: Graph
        Count: Signal<int>
        Doubled: Memo<int>
    }

/// <summary>
/// Starts a thread that owns a graph and drains its inbox each time the dispatcher wakes it, until <c>stop</c> is
/// cancelled. The task completes once the graph exists.
/// </summary>
let startOwner (stop: CancellationToken) : Task<Owned> * Thread =
    let ready =
        TaskCompletionSource<Owned> TaskCreationOptions.RunContinuationsAsynchronously

    let dispatcher = WakingDispatcher ()

    let owner =
        Thread (
            (fun () ->
                use graph = new Graph (GraphOptions.Default.WithDispatcher dispatcher)
                use _ = graph.Activate ()
                let count = createSignal 1
                let doubled = createMemo (fun _ -> count.Value * 2)

                ready.SetResult
                    {
                        Graph = graph
                        Count = count
                        Doubled = doubled
                    }

                while not stop.IsCancellationRequested do
                    WaitHandle.WaitAny [| dispatcher.Wake :> WaitHandle; stop.WaitHandle |]
                    |> ignore

                    graph.Pump () |> ignore),
            IsBackground = true
        )

    owner.Start ()
    ready.Task, owner

/// <summary>
/// Reads the memo, writes the signal and reads the memo again, each through <c>DispatchAsync</c>, then dispatches work
/// that throws. Returns both reads and the failure's message.
/// </summary>
let caller (owned: Owned) : Task<int * int * string option> =
    task {
        let graph = owned.Graph
        let! before = graph.DispatchAsync (fun () -> owned.Doubled.Value)
        do! graph.DispatchAsync (fun () -> owned.Count.Value <- 21)
        let! after = graph.DispatchAsync (fun () -> owned.Doubled.Value)

        let! failure =
            task {
                try
                    let! _ = graph.DispatchAsync (fun () -> failwith "the work failed": int)
                    return None
                with ex ->
                    return Some ex.Message
            }

        return before, after, failure
    }

/// <summary>Runs the caller on the thread pool against a freshly started owner, then stops the owner.</summary>
let run () =
    use stop = new CancellationTokenSource ()
    let ready, owner = startOwner stop.Token
    let owned = ready.Result

    let before, after, failure =
        (Task.Run<int * int * string option>(fun () -> caller owned)).Result

    stop.Cancel ()
    owner.Join ()
    printfn "before: %d, after: %d, failure: %A" before after failure
    before, after, failure

#if INTERACTIVE
run () |> ignore
#endif
