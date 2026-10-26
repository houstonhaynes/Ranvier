---
title: Concepts
order: 1
---

:::warning
**Preview.** Ranvier is pre-release; its APIs may change.
:::

Understand dependency tracking, pending state and ownership here. For API walkthroughs, start
with the [guide](../guide/index.md).

Ranvier is a fine-grained reactive computation library for .NET, written in F#. It provides a dependency graph
of **signals** (settable sources), **memos** (cached derived values) and **effects** (side effects that re-run
when something they read changes). Dependencies are tracked automatically: a computation depends on whatever
it read during its last run. Derived values update in height order, so a computation does not observe a mix
of old and new inputs. An equality cutoff stops propagation when a recomputed value is equal to the previous
one.

The **pending channel** carries a value's not-ready status through dependent reads to a boundary.
A memo can use ordinary value expressions while the graph tracks loading state:

```fsharp
open Ranvier

let user = createAsyncSource<string> ()
let greeting = createMemo (fun _ -> "Hello, " + user.Value)

greeting.TryValue   // Pending
user.Settle "Ada"
greeting.TryValue   // Ready "Hello, Ada"
```

This example, like the others in this section, assumes that a graph is active on the current thread.

`greeting` does not check whether `user` has arrived. It reads `user.Value` like any other value, and the
graph marks it Pending until the source settles.

## Pages in this section

- [Suspension](suspension.md) covers what happens when a computation reads a value that is not ready. It
  explains the pending channel, why a read throws instead of returning a marker, how boundaries stop the
  channel with a fallback, and how errors and recovery use the same path.
- [The async graph on .NET](async-graph.md) covers tasks as computation results, superseded flights, thread
  ownership and dispatch, disposal, and what changes when the same core compiles to JavaScript through
  Fable.
- [Contracts](contracts.md) states which threads may touch a graph, what a failure does to the nodes that
  read it, and who owns a node created inside a computation.
- [Ecosystem](ecosystem.md) covers where Ranvier sits beside FSharp.Data.Adaptive, SignalsDotnet, R3 and
  System.Reactive, Fable.Ripple and Solid, and when one of those is the better choice.
- [Roadmap](roadmap.md) lists implemented features, package-build coverage and ideas under consideration.

## Reading design notes

**Implemented** describes current behavior; **considered and rejected** explains an evaluated
alternative; **exploratory** marks ideas unavailable to use. Stopwatch measurements describe
one machine and one path, rather than application performance or comparisons with other libraries.
