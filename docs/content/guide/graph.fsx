(**
---
title: Graphs
---
*)
(*** hide ***)
#load "../../literate.fsx"

open Ranvier
open Ranvier.Docs.Maps

(**
Every node belongs to a `Graph`{fsharp}.

`new Graph()`{fsharp} uses `GraphOptions.Default`{fsharp}. A graph contains the dependency network,
root owner and dispatcher; disposing it releases its owned computations.

## The Active Graph

The `create*` factories resolve `Graph.Current`{fsharp}. Activate a graph around their calls;
dispose the activation handle to restore the previous graph.

:::details Example

The activation applies to the calls inside its scope:
*)
(*** title=graph.fsx ***)
// Creating a graph does not automatically activate it
let graph = new Graph ()
let graph1 = new Graph ()
assert Graph.TryCurrent.IsNone

// You can manually activate a graph
let active = graph.Activate (): System.IDisposable
assert (Graph.Current = graph)

// The next graph is appended to the stack
let active1 = graph1.Activate ()
assert (Graph.Current = graph1)

// The `Run` member activates the graph, runs the body, and
// then pops the graph from the stack.
graph.Run (fun () -> assert (Graph.Current = graph))
assert (Graph.Current = graph1)

// Dispose an 'activate' instance to pop it from the active
// graph stack
active1.Dispose ()
assert (Graph.Current = graph)
(**
:::

:::warning Standard API functions require an active graph

Any call to an `Api`{fsharp} function without an active graph will throw
`InvalidOperationException`{fsharp}.
```fsharp
let outsideMessage =
    try
        createSignal 0 |> ignore
        "no exception"
    with :? InvalidOperationException as e ->
        e.Message

printfn "%s" outsideMessage
```

```text title=Console frame=terminal
No ambient graph on this thread.

Activate one with `use _ = graph.Activate ()`,
or construct nodes against an explicit graph.
```
:::


## Threads and Graphs

With the default `Guarded` affinity, an activation applies only on the activating thread.
`Unchecked` and `Serialised` activations flow with the execution context. Graph access must still
obey the [threading contract](threading.md).

```fsharp
let inner, restored, onOtherThread =
    use outerGraph = new Graph ()
    use innerGraph = new Graph ()
    use _ = outerGraph.Activate ()

    let inner =
        use _ = innerGraph.Activate ()
        obj.ReferenceEquals (Graph.Current, innerGraph)

    let restored = obj.ReferenceEquals (Graph.Current, outerGraph)

    let mutable onOtherThread = true
    let thread = Threading.Thread (fun () -> onOtherThread <- Graph.TryCurrent.IsSome)
    thread.Start ()
    thread.Join ()

    inner, restored, onOtherThread

printfn "inner active: %b, outer restored: %b, visible on another thread: %b" inner restored onOtherThread
```

```text title="Console" frame=terminal
inner active: true, outer restored: true, visible on another thread: false
```

## C#

`Graph.Run`{fsharp} is an F# extension member.

From C#, `Ranvier.CSharp`{fsharp} provides it as an extension method; see [C#](csharp.md).
*)

(*** hide ***)
active.Dispose ()
graph.Dispose ()
