# Native backend design: Ranvier's DOM and view on Ranvier.WebCore

> **Status 2026-10-28:** draft, for review. Written against Ranvier.WebCore ABI 20 (branch `abi-v20` of
> `ranvier-org/Ranvier.WebCore`: the hand-written ABI plus 1,002 generated `rv_dom_*` functions). The prototype it
> replaces lives on the local evidence branch `codex/native-webcore-feasibility` (at commit `31687183`), not on
> master: `evidence/webcore-host/ranvier` and gates 4–19 of `evidence/ABI-EXPERIMENT.md` there. "Gate N" below refers
> to that file. Measured facts carry their source; estimates are marked (E).

## Core and view state

Required by `.claude/rules/view-uses-core.md`.

**Core primitives used:**

- The graph's dispatcher: the host thread owns the graph and the WebCore page. `SynchronizationContextDispatcher`
  posts drains to the host loop's synchronization context; no second scheduler.
- `Owner`, `onCleanup`: a mount's owner releases its native listeners and its claimed node references, as the
  prototype's gate 6 release policy did.
- `batch` / `flush`, the commit `Queue` and every view primitive (projections, arm memos, `MountLifetime`): unchanged.
  The backend sits below `Native.IHost` and the `Fable.Core.TS.Dom` interfaces, so the view's code paths are the
  ones it runs on the twin and in the browser.

**View-owned state (new):** all of it is DOM state, which the rule assigns to the view.

- The wrapper table: native node handle → managed wrapper, so one native node has one managed object (the view keys
  weak tables, expandos and reference comparisons on it).
- Each wrapper's `ExpandoSlots` (the existing `IExpandoHost` seam).
- Native listener registrations: the `RvListener*` behind each `Listen` removal.
- The host loop's microtask, timer and animation-frame queues.

No reconciler, key matcher or commit queue is added.

## Goal

An F# app runs Ranvier's view (`Ranvier.View.Dom`: `Mounting.mount`, `Hydration.hydrate`, the sugar) and Ranvier's
DOM API (`Fable.Ranvier`: `Dom.*`, `Mount.mount`) on .NET, against a WebCore page from the `Ranvier.WebCore.Native`
NuGet package, unchanged: the same components that run in the browser. The app forwards OS input and paints the page
into its window.

## Non-goals

- **Page JavaScript.** Scripts stay off; the components are F#.
- **A window toolkit.** The backend paints into an RGBA buffer and takes input events. Putting the pixels on screen
  (WinUI, Avalonia, SDL, a raw HWND) is the app's or a later package's.
- **Every DOM member.** Members outside the generated scope (`gen/dom.json` of Ranvier.WebCore) throw
  `NotSupportedException` naming the member. The scope grows in the WebCore repo.
- **Reflection at run time.** The prototype's `DispatchProxy` bridge is not carried over: it is neither trimmable
  nor AOT-compatible, and it was 66–76% of the keyed benchmark's time (gate 19).

## Decisions

1. **Generated wrapper classes over the `Fable.Core.TS.Dom` interfaces.** A generator reads the interfaces of
   `Xantham.Fable.Core.TS` and the generated ABI's member list, and writes one class per interface on the
   inheritance chain of the in-scope interfaces (`NodeObject`, `ElementObject : NodeObject`,
   `HTMLElementObject : ElementObject`, …). Each class implements only its own interface's members; F# takes the
   inherited interfaces' members from the base class (checked in a scratch script). A member maps to
   `rv_dom_<interface>_<member>` by IDL name; an unmapped member throws. Sizes from the interfaces:
   `HTMLInputElement` has 300 properties and 121 methods over 16 interfaces, so hand-written classes are not an
   option.
2. **Hot paths make one P/Invoke each.** On master the view reads and moves nodes through the `Fable.Core.TS.Dom`
   members (`Native.IHost` keeps only the document, the clock, `DeleteBetween`, `Listen` and the `currentTarget`
   override), so the generated members serve the view's per-node operations, each one ABI call. `DeleteBetween` and
   `Listen` call the hand-written ABI directly. (The evidence branch's `Native.IHost` also carried walk, insert, data
   and attribute members, which its prototype host served from the hand-written ABI.)
3. **Identity is the handle.** Two ABI handles to one node compare equal (`RanvierWebCoreAbi.h`). The wrapper table
   maps a handle to a weak reference to its wrapper; a wrapper holds one retained reference. A wrapper collected by the
   GC queues its release to the host thread, drained each loop cycle (the ABI is thread-bound).
4. **Managed interop comes from the WebCore repo.** `tests/abi/Native.fs` and `NativeDom.fs` are generated from the
   headers there. The backend compiles copies of them, refreshed by a script, until the WebCore repo ships them as a
   managed package (decision 1 for the user, below).
5. **`Fable.Ranvier` gets a .NET global seam.** Its `Global.document` and `Global.queueMicrotask` are JavaScript
   globals. On .NET they read an installed provider, which the backend installs with its host. The Fable output is
   unchanged.
6. **One host type.** `WebCoreHost` owns the `RvHost`, implements `Native.IHost` and `Native.IExpandoHost`, runs the
   loop (`rv_run_loop_cycle`, timers, frame callbacks), and exposes resize, input, paint and dirty-rectangle members
   for the app's window.

## Projects

- `src/Ranvier.View.WebCore` (namespace `Ranvier.View.WebCore`, net10.0, trimmable and AOT-compatible): references
  `Ranvier.View.Dom`, `Fable.Ranvier` and `Ranvier.WebCore.Native`.
  - `Interop/`: the copied P/Invoke declarations.
  - `Generated/Objects.fs`: the wrapper classes.
  - `Objects.fs`: the wrapper table, string and handle helpers, the base `NativeObject`.
  - `Events.fs`: native listeners and event wrappers, `currentTarget` overrides.
  - `Host.fs`: `WebCoreHost`.
- `tools/Ranvier.View.WebCore.Generate`: the generator, run by hand; its output is committed.
- `tests/Ranvier.View.WebCore.Tests`: Expecto tests that mount real components into a `WebCoreHost` and read the
  DOM and pixels back; plus the conformance modules the prototype ran (426 cases, gate 5) once the backend carries
  them.

## Testing

1. The wrapper table: identity, release on collection, release on owner disposal.
2. Each IHost member against the twin's behaviour.
3. Mounting a sugar component, a keyed list and an arm, and reading the DOM back; then the conformance modules.
4. `Fable.Ranvier`'s `Mount.mount` with `Dom.element`, `Dom.on` and `Dom.reactiveText`.
5. Events: a click forwarded through `rv_host_mouse` reaches a component's handler, with `preventDefault` visible to
   the page.
6. Pixels: a mounted component paints, and a signal write changes the painted pixels.
7. AOT: a published smoke app with `PublishAot` runs the mount and paint.

## Decisions for the user

1. **Where the managed interop lives:** a `Ranvier.WebCore` managed package from the WebCore repo, versioned with the
   ABI (recommended), or copies in Ranvier.
2. **Package name:** `Ranvier.View.WebCore` (recommended), or `Ranvier.Native`.
3. **`Fable.Ranvier`'s global seam** (decision 5) changes a production source file.

## Risks

- The generated ABI has no unions of nodes, sequences or variadic arguments (`append(...nodes)`), so members using
  them throw until the WebCore generator maps them; the view's own paths do not use them (E, checked by the tests).
- Finalizer-queued releases keep a node alive until the next loop cycle; a loop that stops cycling holds them.
