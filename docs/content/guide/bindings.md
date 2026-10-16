---
title: Fixed-dependency bindings
---

`bindSignal source target write` and `bindMemo source target write` copy one source into a target.
The source is fixed when the binding is created. Use this for a view property or another external
target whose value comes from one signal or memo.

```fsharp
open Ranvier

type Label () =
    member val Text = "" with get, set

use graph = new Graph ()
use active = graph.Activate ()
let name = createSignal "Ada"
let label = Label ()
bindSignal name label (fun target value -> target.Text <- value)
flush ()
printfn "%s" label.Text // Ada
name.Value <- "Grace"
printfn "%s" label.Text // Grace
```

The first write runs in the next flush. Outside a batch or an existing flush, the graph requests
that flush as part of creating the binding. A binding created in a batch waits for its flush,
and several source writes before that flush produce one write of the current value.

## Choose a binding, memo or effect

- Bind a signal when its value can be copied directly to the target.
- Derive a value with a [memo](memos.md), then use `bindMemo` when the target needs that value.
- Use an [effect](effects.md) for work whose dependencies depend on tracked reads in its body.

Reads inside `write` are untracked: reading a second signal there does not make it a dependency.
`write` must not create reactive nodes or register cleanup. Create those resources in the enclosing
scope, and use [cleanup](cleanup.md) for their lifetime.

## Equality, pending values and failures

A source's equality cutoff applies before the binding writes. An equal signal write does nothing;
a memo that recomputes to an equal value leaves the target unchanged. Configure equality on the
source, using the [graph policy or a per-node comparer](equality.md).

While a memo is pending or failed, its target keeps its last value. A memo that has never settled
does not write an initial value until it becomes ready. A throwing writer does not stop other
queued work; the next source change can run the writer again.

## Lifetime

Both factories return `unit`. The binding belongs to the owner current at creation, and disposal
of that owner releases the source dependency. A binding created in an effect's run is disposed
before the next run. Use a [root](roots.md) when several bindings should be stopped together.

For measurements against equivalent effects, see the method and limitations in the
[binding benchmarks](../benchmarks/effects.md#bindingbenchmarks).
