---
title: Mounts and lifetimes
---

A **mount** owns a DOM region, its component instances, effects and listeners. Its handle disposes
them together. Mounting runs under Fable through `Ranvier.View.Dom.Mounting`.

## Mounting a component

```fsharp
let mounted =
    Mounting.mount graph None report host None (fun context ->
        Program.run (Component.render counter { Start = 0; Label = "Clicks" }) context.Backend)
```

`Mounting.mount graph parentOwner report host before build`:

| Argument | Meaning |
| --- | --- |
| `graph` | The [graph](../guide/graph.fsx) the mount's owners and effects belong to. |
| `parentOwner` | `None` for a root of `graph`; `Some owner` to make the mount a child of `owner`, disposed with it. |
| `report` | Receives `exn list` for failures outside every boundary after the build; see [Failures](#failures). |
| `host` | The element the mount inserts into. |
| `before` | `Some node` inserts before a child of `host`; `None` appends. |
| `build` | Runs once, untracked, with a `BuildContext`, and creates the mount's roots. |

`build` receives a `BuildContext`:

- `context.Backend` is the backend that `Program.run view context.Backend` writes into. Each
  `Program.run` call adds roots in order, and one mount can hold several views.
- `context.Native (node, Owned)` adds an existing DOM node as a root. An `Owned` node is removed when
  the mount is disposed; a `Borrowed` node returns to its original parent and position.
- `context.OnCleanup cleanup` registers a cleanup on the mount's root owner.

The view sits between `<!--ranvier:start-->` and `<!--ranvier:end-->` comments in `host`. Existing
children of `host` stay in place.

## The `Mounted` handle

| Member | Meaning |
| --- | --- |
| `Dispose ()` | Runs every cleanup of the mount, then removes its nodes and listeners. |
| `Phase` | `SettingUp`, `Attached` or `Disposed`. |
| `Failures` | The failures recorded by the mount's teardown. |
| `Flush ()` | Runs pending graph work and, with queued commits, commits the queue now. |
| `CommitErrors` | With queued commits, the setter failures of the last nonempty flush. |
| `MoveBefore (parent, before)` | Moves an attached mount's nodes to another position. |

Disposing the graph also disposes every mount on it.

Calling `mounted.Dispose ()` removes the counter and its listeners. Unrelated host children remain.

## Instances and owners

Every instance (a component, an inlined child, a row, an arm, a boundary's content) runs its setup
under an owner of its own, created under its parent's owner. Removing a row, switching an arm or
disposing a mount disposes that owner:

1. Cleanups run while the instance's nodes are still in the document: `Setup.OnCleanup` callbacks,
   hook cleanups, and the cleanups of signals, memos and effects created in setup, in the core's
   [reverse acquisition order](../guide/cleanup.md).
2. The nodes leave the document after every cleanup of the disposed tree has run.

A switched arm runs the previous arm's cleanups and removes its nodes before the new arm's setup runs.
A list change that replaces a row sets up the new row before the replaced row's cleanups run.

## Commit modes

`Mounting.mountWith mode ...` takes the same arguments after a `CommitMode`:

| Mode | DOM writes |
| --- | --- |
| `SynchronousCommits` (what `mount` uses) | In the effect that read the new value: before the signal write returns, or at the end of the enclosing batch. |
| `QueuedCommits (failure, post)` | Queued per binding; `post` schedules one flush. `Microtask.mode failure` posts to `queueMicrotask`. |

```fsharp
let mounted =
    Mounting.mountWith (Microtask.mode Scheduling.CollectAndContinue) graph None report host None (fun context ->
        Program.run (Component.render counter { Start = 0; Label = "Clicks" }) context.Backend)
```

Under queued commits, each binding has one slot in the mount's queue: repeated writes in one turn
replace the slot's pending value, and a flush writes each slot once. The reactive computation still
runs at once; only the DOM write waits. `CollectAndContinue` commits every slot and
collects setter failures in `CommitErrors`; `FailFastCancel` stops at the first failure and cancels
the remaining slots of that flush.

Hydration requires `SynchronousCommits`. Under `QueuedCommits`, `Setup.CreateId` issues ids in another
format, which can differ from the server's; see [Components](components.md#setup).

## Failures

| When | Where it goes |
| --- | --- |
| `build`, or a setup outside every boundary, throws while mounting | The mount is disposed and the exception propagates from `mount`. |
| A hook throws during the first placement, outside every boundary | The mount is disposed and the exception propagates once the placement ends. |
| A binding read, a property setter or a handler throws later, under a boundary | The boundary's [error view](boundaries.md). |
| The same, outside every boundary | `report`, with the binding keeping its last value. |
| A mount created inside a batch or computation runs its hooks late, and one throws | `report`. |

## Mounts inside mounts

A widget hook can start a second mount in its element and dispose it in the hook's cleanup. Nested
mounts can use a different graph or commit mode: the playground's shell mounts its demo into a widget
element with a commit mode chosen at runtime.
