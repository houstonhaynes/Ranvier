---
title: Ecosystem
order: 4
---

:::warning
**Preview.** Ranvier is pre-release; its APIs may change.
:::

Ranvier combines fine-grained dependency tracking with a propagating [pending channel](suspension.md).
Signals hold state, memos derive it, and boundaries turn loading and failures into display values.
It targets .NET and Fable, with an F# API and a C# facade.

## Related libraries

- [FSharp.Data.Adaptive](https://fsprojects.github.io/FSharp.Data.Adaptive/) provides adaptive sets,
  maps and indexed lists with an established incremental operator family. Compare the operators
  and update patterns your application needs. The [desktop walkthrough](../blog/desktop-adaptive.md)
  shows both APIs.
- [SignalsDotnet](https://github.com/fedeAlterio/SignalsDotnet) connects signals to .NET MVVM and R3.
  Its async API exposes `IsComputing` and configurable exception handling; Ranvier propagates pending
  and failed reads through dependent computations to a boundary.
- [R3](https://github.com/Cysharp/R3) and [System.Reactive](https://github.com/dotnet/reactive) compose
  event streams with a broad operator vocabulary. Ranvier settles a graph of derived state.
  Streams can represent state too; the diamond example below shows where their delivery contracts differ.
- [Fable.Ripple](https://github.com/fable-hub/Fable.Ripple) offers fine-grained Fable reactivity with
  its own DOM layer, forms and router. Compare its
  [application-managed async state](https://github.com/fable-hub/Fable.Ripple/blob/main/docs/content/ripple-dom/async-data.md)
  with Ranvier's pending propagation.
- [Solid's async design](https://github.com/solidjs/solid/tree/next/documentation/solid-2.0)
  informs Ranvier's pending channel and boundaries. The projects have separate renderers,
  schedulers and APIs.

Use each project's own documentation for current versions and integrations.

## Collection tradeoffs

Ranvier's [editable keyed sources](../guide/collection-updates.fsx#direct-edits) update an existing
row without rescanning a source list or replacing the key array. Map views consume membership
deltas and retain unchanged rows. Key readers report membership and order; value readers observe
rows and report unequal settled values. The C# `AsObservableCollection` adapter consumes these
value deltas for replacements.

Filter, sort, grouping and some aggregate membership paths still scan keys. Changed ranks require
a full sort; source membership changes copy key order. Whole-list sources reconcile their input
when it changes. Readers coalesce changes between reads and recover from bounded-log overflow
with a reset. See [view costs](../guide/collection-views.fsx) and
[reader costs](../guide/projections.fsx#reading-changes).

Adaptive offers a broader established collection API; Ranvier combines keyed rows with ownership,
pending and failed states. Delta support alone does not establish change-proportional costs for
every operator, or comparative performance.

## Diamonds without glitches

A diamond is two derived values that read one source, and a third value that reads both. In Ranvier, one
write to `a` runs `d` once, and `d` reads `b` and `c` from the same write.

```fsharp
let a = createSignal 1
let b = createMemo (fun _ -> a.Value + 1)
let c = createMemo (fun _ -> a.Value * 10)
let d = createMemo (fun _ -> b.Value + c.Value)
createEffect (fun () -> printfn "d = %d" d.Value)

a.Value <- 2
a.Value <- 3
```

```text
d = 12
d = 23
d = 34
```

In the following synchronous System.Reactive subscription topology, `CombineLatest` emits once per leg. The first emission after each
write combines the new `b` with the old `c`, a state the source never had.

```fsharp
let a = new BehaviorSubject<int> (1)
let b = a.Select (fun x -> x + 1)
let c = a.Select (fun x -> x * 10)
Observable.CombineLatest(b, c, fun b c -> b + c).Subscribe (printfn "d = %d")

a.OnNext 2
a.OnNext 3
```

```text
d = 12
d = 13
d = 23
d = 24
d = 34
```

These intermediate emissions follow `CombineLatest`'s stream contract. This example does not imply every
Rx architecture produces inconsistent state: an application can derive both branches from one combined
state emission. Buffering or scheduling also changes delivery, but is not by itself a graph-consistency
guarantee. Ranvier updates this derived graph in height order, and `d` runs after both inputs. Two writes that
belong together go in one [`batch`](../guide/batch.md).

## Owners instead of hooks

Ranvier node identity belongs to the node object and does not depend on a render-time hook call position.
An owning body, such as `createMemoWith` or an effect, may create owned nodes inside branches and loops;
each run may create a different set. Pure memo bodies have stricter creation rules. Owned nodes belong to
the [owner](contracts.md#ownership) current at creation, and its next run disposes them. Signals and async
sources are unowned. State that must survive a body's re-run lives outside that body.

## Rendering and host integration

- **Browser and server HTML:** [Ranvier.View](../view/index.md) uses Ranvier signals directly.
  Templates support DOM mounting, [server rendering](../server/index.md) and hydration.
- **XAML view models:** [Ranvier.CSharp](../guide/csharp.md#binding-to-xaml) supplies
  `ReactiveBindings`, `ReactiveCommand` and `AsObservableCollection`. Existing WPF, Avalonia, MAUI,
  Uno and WinUI hosts render through their own toolkits. The [Avalonia workbench](../guide/examples.md)
  demonstrates these adapters.
- **Blazor Server:** one graph per circuit with `ThreadAffinity.Serialised` follows the renderer's
  context across pool threads. See [Blazor Server](../guide/blazor-server.md).
- **Shared models:** the same F# core can run on .NET and through Fable. Read the
  [target differences](../fable/index.md), especially equality, promise delivery and post-await reads.
- **Deterministic tests:** [manual dispatch](../guide/testing.md) lets tests choose when flights
  settle and assert `Pending`, `Ready` and `Failed` without a UI thread.

The [roadmap](roadmap.md) separates available features from exploratory work.
