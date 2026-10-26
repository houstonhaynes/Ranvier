---
title: Overview
order: 1
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

Build reactive state for .NET: update a value, and the computations that depend on it update too.

* **Signals** hold values you can read and write.
* **Memos** derive values from tracked reads.
* **Effects** run when a value they read changes.
* **Bindings** copy one fixed signal or memo into an external target.

:::tip Async state travels through the graph
A node waiting on an async dependency is **pending**. Its readers can report loading, keep a
previous result, or use a boundary's fallback. This pending channel follows Solid 2.0 and is
independent of whether a value is out of date.
:::

## A taste

```fsharp
open Ranvier

let graph = new Graph ()

let doubled =
    graph.Run (fun () ->
        let count = createSignal 1
        let doubled = createMemo (fun _ -> count.Value * 2)
        createEffect (fun () -> printfn "doubled = %d" doubled.Value)
        count.Value <- 5
        doubled)
```

```text
doubled = 2
doubled = 10
```

## Choose a starting point

- **Derived state:** [Signals](signals.fsx), [Memos](memos.md), [Effects](effects.md) and
  [Bindings](bindings.md) connect writable state to cached calculations and external targets.
- **Async work:** [Async and pending](async-and-pending.md) covers requests, streams, loading,
  errors and dispatch. [Boundaries](boundaries.md) supply values while a request is unavailable.
- **Collections:** [Projections](projections.fsx) preserve rows by key;
  [direct edits](collection-updates.fsx#direct-edits) update them without replacing the source list.
- **UI:** [Views](../view/index.md) and [Server rendering](../server/index.md) share templates
  across browser DOM, server HTML and hydration. [C# bindings](csharp.md#binding-to-xaml) connect
  native XAML view models to the graph.
- **Lifetime:** [Roots](roots.md) and [Cleanup](cleanup.md) stop computations and subscriptions
  when their owner ends.

## Targets

`net10.0`, `netstandard2.1` and [Fable (JavaScript)](../fable/index.md).

## Guide

- [Installation](installation.md): preview packages, supported targets and Native AOT.
- [Getting started](getting-started.md): a short walkthrough of a reactive graph.
- [Examples](examples.md): complete F# and C# console and Avalonia applications for real workflows.
- [Ranvier or Adaptive for a desktop app?](../blog/desktop-adaptive.md): a blog walkthrough with a complete F# window and comparisons of state, bindings, collections, async work and lifetime.
- [Graphs](graph.fsx), [Signals](signals.fsx), [Memos](memos.md) and [Effects](effects.md): the core primitives.
- [Fixed-dependency bindings](bindings.md): copy one signal or memo into a target, with batching and owner disposal.
- [Equality](equality.md): graph defaults and typed comparers for individual nodes.
- [Roots and owners](roots.md), [Cleanup](cleanup.md), [Untrack](untrack.md) and [Batch](batch.md): lifetime and tracking controls.
- [Editable values](editable.md) and [Drafts](drafts.md): local edits over upstream state.
- [Async and pending](async-and-pending.md): in-flight values, async memos, boundaries, and threading.
- [Debounce and throttle](timing.md): control admission before downstream or async work starts.
- [Streams](streams.md): fold a stream's items into a node that restarts when its inputs change.
- [Testing async state](testing.md): deterministic tests that decide when each flight lands.
- [Blazor Server](blazor-server.md): one graph per circuit, with `ThreadAffinity.Serialised`.
- [Collections](collections.fsx): projections, editable keyed sources, change readers, lookups and collection views.
- [Aggregates](aggregates.fsx): totals, counts and folds over collection rows.
- [Forms](forms.md): records of reactive fields and batched resets.
- [Tracing](tracing.md): why a node ran or did not run, where it was created, and the graph as it stands.
- [Persisted inputs](persistence.md): restore signals and collections before building the graph.
- [MCP inspection](mcp.md): expose selected values and messages from a running application.
- [Input journal and replay](journal.md): record a graph's external inputs, and replay them to reproduce or re-enact a run.
- [Signal maps](signal-maps.md): how to read the live graph maps beneath the examples, and how to write one.
- [Troubleshooting](troubleshooting.md): each exception message and common symptom, with its cause and fix.
- [Migrating from Elmish](elmish.md): an MVU model read through selector memos, one view at a time.
- [Queries and mutations](queries.md): shared remote queries, page leases and atomic reconciliation of partial data.
- [C#](csharp.md): C# factories, XAML bindings and reactive commands.
- [Views](../view/index.md) and [Server rendering](../server/index.md): template components for the browser DOM, server HTML and hydration, in the alpha `Ranvier.View` packages.

## Source and package status

The guide describes the current source. [Installation](installation.md) distinguishes published
previews from local package builds. [Roadmap](../concepts/roadmap.md) lists implemented areas and
ideas under consideration.
