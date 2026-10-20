# Queued commits on core effects — 2026-10-19

Plan item 4 asked whether the view's queued commit `Queue` (`experiments/Ranvier.View.Foundation/Scheduling.fs`) can become ordinary binding effects under a microtask `IGraphDispatcher`. It cannot without a core change. The `Queue` stays in the view. Code read at `31fc732` (`poc/fable-ranvier-dom`).

## What the queued mode does today

- A queued binding is already a core effect (`Binding.startQueued`). The effect tracks its reads synchronously on every flush, as a synchronous binding does. Instead of writing the DOM, it stores a commit closure in its `Slot`, the latest write winning.
- The mount's `Queue` posts one flush through the `post` callback it was given (`Microtask.mode` passes `queueMicrotask`). The flush runs `Api.flush ()`, then runs each live slot's commit untracked, with the `CollectAndContinue` or `FailFastCancel` policy.
- The first settled value of a binding is written at once. A suspended or failed read cancels the slot's older write.

## Why a dispatcher cannot defer the writes

- `IGraphDispatcher.Post` receives only the inbox drain for work posted from another thread (`Graph.Post`, `src/Ranvier/Core.fs`). Under Fable, `Graph.Post` runs the work inline and the dispatcher is not called.
- A signal write outside a batch flushes the effect queue before the write returns (`Graph.RequestFlush`). The contracts state this for every node: a write on the graph thread settles before the next line runs.
- So under any dispatcher, binding effects run in the write's flush. Deferring their DOM writes to a microtask needs one of:
  - a core deferred-flush mode that runs effects on a posted flush. That changes when every effect on the graph runs, not only DOM writes, and it would be a `GraphOptions` member with its own contract;
  - a per-effect deferral in core, which is what `Slot` and `Queue` already are, kept in the view.

## What the `Queue` carries that core effects do not

- A per-mount choice: one mount can queue its writes while other code on the same graph stays synchronous.
- A failure policy over a batch of writes: `CollectAndContinue` reports every failed write; `FailFastCancel` cancels the writes after the first failure.
- Latest-wins coalescing per binding between flushes, which a deferred effect flush would not give by itself: an effect that runs twice runs its body twice.

## Recommendation

Keep the `Queue` in the view and close plan item 4. Revisit it only if a core deferred-flush mode is proposed for another reason, such as scheduling effects on an animation frame. It would then need the per-mount choice and the failure policy above.

One related fact: a queued mount expands a template through `definition.Expand`, not through `TemplateDom.materialise` (`MountState`, `Mounting.fs`). Queued mounts therefore do not get the compiled template path.
