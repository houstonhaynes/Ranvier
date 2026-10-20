# View boundaries and live setter failures — design

Date: 2026-10-19
Branch: `poc/fable-ranvier-dom`
Baseline: `31fc732` (plus the research commits after it)
Plan: [Task 5](../plans/2026-10-06-fable-ranvier-framework.md#task-5--minimal-bindings-boundaries-and-scheduling), open item 5.
Status: approved 2026-10-19. The user accepted the recommendation of every **Decision**: 1 (a), 2 (a), 3 (keep throwing), 4 (a).

## Core and view state

Required by `.claude/rules/view-uses-core.md`; added 2026-10-19 with the implementation plan. The user agreed on 2026-10-19 that the waiting count and failure routing live in the view: core suspense catches pending reads only inside its own body, and a binding reads in its own effect. If core gains owner-scoped suspense, the view moves to it.

**Core primitives used:**

- `Api.createRoot`, `Api.runWithOwner`, `Owner` and `MountLifetime` (`Lifetime.fs`, over core owners): the boundary region owns one root (`RegionSite.Own`); its content, pending view and error view are each a nested mount whose root is a child of that root. Disposing the region disposes all three.
- `Api.onCleanup`: a binding under a boundary unregisters its pending read when its owner is disposed.
- `Api.createEffect` with an `Api.createSignal` (`wake`): the boundary's reveal and teardown run in this effect, after the effect that requested them.
- `Api.createSignal false`: `BoundaryStatus.IsPending`, a tracked reader.
- `NotReadyException` (core async sources and async memos): what a pending read is.
- `MountLifetime.Capture`: the teardown window that collects the content's cleanup errors for the failure report.

**View-owned state:**

- The waiting count of a boundary, and a `Waiting` bit in each binding's state word. Core suspense (`createSuspense`, `createBoundary`) catches pending reads inside its own body only; a binding reads in its own effect, outside every core boundary body (spec, "What happens today"). The approved spec places this count in the view.
- The content's detached fragment and the row shown between the markers: DOM nodes and their layout.
- The failures a boundary received before its effect runs, and the failures held while its content sets up.
- The scope of each commit slot. The commit `Queue` is view state already under review (rule file, last paragraph); this plan adds one field to its slots.
- A `$$ranvierScope` property on template event nodes under a boundary: event delegation state.

## Purpose and boundaries

Task 5 has two open contracts:

1. **Structural view boundaries with coordinated reveal.** A region that shows a pending view while its content waits for data, reveals the content once it is ready, and replaces it with an error view when something under it fails.
2. **Live DOM setter failures and deferred commit reporting.** What happens when a binding's DOM write throws after the mount has been set up, synchronously or from the queued commit flush.

The two contracts meet: a setter failure is one of the failures a boundary catches.

Out of scope:

- server HTML and hydration of boundaries (Task 9); this design states only what the HTML renderer draws;
- grouped reveal across sibling boundaries (`SuspenseList`-style ordering);
- transitions that keep old content on screen while new content loads in the background;
- any change to core (`src/`). Everything here is view-side, under `.claude/rules/view-uses-core.md`.

## What happens today

Read from the code at `31fc732`:

- **A binding reads in its own effect.** `startBinding` (`Mounting.fs`) creates one core effect per binding. Its reads are not inside any core boundary's body, so a core `createBoundary` cannot catch them. The [structural composition proof](../../research/2026-10-11-structural-boundary-composition.md) works only because its shared computation reads the async values inside the boundary body.
- **Pending reads.** A reader that raises `NotReadyException` leaves the binding's last committed value on screen. Nothing records that the binding is waiting.
- **Failed reads.** `reportReaderFailure` sends the exception to the mount's `report` callback, once per exception object until the reader recovers. The binding keeps its last value.
- **Setter failures during setup** unwind the mount: cleanups run, the nodes are removed, and the exception is rethrown from `mountWith` (`BindingIntegrationChecks.fs` "initial synchronous/queued DOM setter failure unwinds setup").
- **Live synchronous setter failures** in Tree and Program bindings (and templates under queued commits, which expand through them) escape `commit` (the `with` only handles `SettingUp`). The exception lands on the core effect's `Error`, where nothing reads it, and the mount's `report` callback is never called. The committed value is unchanged, so an equal later value retries the write. Template DOM commits (`TemplateDom.commit`) already report live setter failures through `report` (the `laterSetterFailure` test).
- **Queued setter failures** are collected in `Queue.Errors` after each flush (`CollectAndContinue`), or stop the flush (`FailFastCancel`). Nothing reports them; a caller must read `Errors`.
- **Row and arm setup failures** after the initial mount leave the region empty and report through `report`.

The first item of Testing below pins each of these as a test before any change, so the design starts from measured behaviour.

## Boundary scope

A **boundary** is a view-side scope with three parts: the content, a pending view and an error view. Its descendants are every binding, row, arm, listener and nested component mounted inside its content, up to the next boundary.

The scope is carried by `MountState`, which every nested mount already reaches through its parent chain. A binding finds its nearest boundary once, when it is created, and keeps the reference; nothing is looked up per run. Bindings outside every boundary see the mount itself as their scope, which keeps today's `report` behaviour.

### Authoring

Templates:

```fsharp
t.boundary Loading.definition ErrorPanel.definition Profile.definition
// pending: Component<unit>, failed: Component<BoundaryFailure>, content: Component<BoundaryStatus>
```

`BoundaryFailure` carries `Error: exn` and `Retry: unit -> unit`. Program and Tree authoring get `ops.Boundary (pending, failed, content)` with the same meaning. The pending and error views are ordinary views; a failure inside them goes to the enclosing boundary.

## Pending and reveal

- A binding whose reader raises `NotReadyException` registers itself as waiting with its scope, and unregisters on its next successful read. Rows, arms and nested components register through their bindings; they need no rule of their own.
- **Initial reveal.** The content mounts at once, into a detached fragment that belongs to the boundary: setup runs, effects run and listeners attach. The pending view is shown between the boundary's markers. When the waiting count first reaches zero, the boundary disposes the pending view and inserts the content's nodes, in one DOM operation.
- **Coordinated:** every binding under the boundary waits for the others. Content never appears half filled.
- **Nested boundaries** reveal on their own. An inner boundary's descendants register with the inner boundary only, so an outer boundary can reveal while an inner one still shows its pending view.

**Decision 1: pending after the first reveal.**

- (a) *Keep the content (recommended).* Later pending reads leave the revealed content on screen with its last values; the boundary exposes `IsPending: unit -> bool`, a tracked reader the content can use for a spinner. This is what Solid 2's `Loading` does after its first reveal, and what core `createSuspense` allows by handing its fallback the last value.
- (b) *Hide it again.* The boundary moves the content back into its fragment and shows the pending view whenever the count rises above zero. That makes any refetch flash the loading view.

## Failures

A boundary catches every failure under its content:

- a reader failure (today's `reportReaderFailure` path);
- a live setter failure, synchronous or queued (next section);
- a row, arm or nested component setup failure after the initial mount;
- an exception thrown by an event handler mounted under the content.

On the first failure, the boundary:

1. disposes the content: its cleanups run and its nodes leave, through the existing teardown window, so cleanup errors are reported with the failure;
2. mounts the error view with a `BoundaryFailure`;
3. reports the failure through its own scope's `report` as well, so logging keeps working.

`Retry` disposes the error view and mounts the content again from the start: new setup and a new pending phase. A failure before the first reveal works the same way: the pending view is replaced by the error view.

**Decision 2: event handler exceptions.**

- (a) *Caught by the boundary (recommended).* An exception thrown by a handler under the content goes to the boundary like any other failure, so a broken button shows the error view. React's error boundaries do not catch handler exceptions; the handler's state is the application's, and the next render may never come.
- (b) *Not caught.* Handler exceptions propagate to the browser as today, and the boundary covers rendering only.

**Decision 3: a failure at mount setup with no boundary.** Today an initial setter failure unwinds the whole mount and throws from `mountWith`. Keep that (recommended): a boundary changes it only for content under the boundary, where setup failure shows the error view instead of throwing.

## Live setter failures

Under any scope, with or without a boundary:

- **Synchronous.** `commit` catches the exception. The committed value stays the previous one, so a later equal value retries the write. The core effect does not fail; its `Error` stays clear. The failure goes to the binding's scope: a boundary shows its error view; the mount without a boundary calls `report [ error ]`.
- **Queued.** The flush collects failures as today. After the flush, each scope receives its failures in one `report` call, in commit order; a boundary receiving any shows its error view. `FailFastCancel` cancels the remaining writes as today, and the cancelled writes are not reported. `Queue.Errors` stays, for tests.
- **One report per exception object.** The deduplication already used for reader failures applies to setter failures too.

**Decision 4: does a live setter failure under a boundary tear the content down?**

- (a) *Yes (recommended).* A DOM write that throws leaves that node in an unknown state, so the content is replaced by the error view, the same as for any other failure.
- (b) *No.* Report it and keep the content; only reader and setup failures switch to the error view.

## Server HTML

The HTML renderer has no async waiting. It renders the content when every read under the boundary is ready, the pending view when a read is pending, and the error view when a read fails, with region markers around the chosen view. Hydration, and choosing the right view on the client, belong to Task 9.

## Implementation outline

- `Mounting.fs`: a `BoundaryScope` beside `MountState`, holding the waiting count, the content and pending lifetimes, the detached fragment, the error view and its `report`. `MountState` gains its nearest scope; nested `MountState`s inherit it.
- `startBinding` and `Binding.startQueued`: register and unregister on pending reads; route reader and setter failures to the scope; catch live setter failures.
- `Queue`: report each flush's failures to their slots' scopes. A slot records its scope when reserved.
- Event dispatch (`delegateFrom`, `TemplateDom.dispatch`, direct listeners): catch a handler exception and route it to the handler's scope.
- `Template.fs`: `t.boundary`, a region kind beside `matchWith`, `branch` and `each`. `Typed.fs`/`Authoring.fs`: `ops.Boundary`.
- `TemplateDom.fs`: the boundary region materialises like an arm region.

## Testing

All in `examples/Fable.Ranvier.Playground/tests/`, in jsdom and the Chromium range harness, for Tree, Program and template authoring, and for synchronous and queued commits:

1. **Today's behaviour, pinned before any change:** a live synchronous setter failure is not reported today, and the effect holds it; a queued setter failure reaches only `Queue.Errors`. These tests are then updated to the new contract, so the diff shows the change.
2. Initial reveal waits for every binding under the boundary, including bindings in rows and arms; one DOM insertion reveals the content.
3. A nested boundary reveals on its own.
4. After the first reveal, a later pending read follows Decision 1.
5. Each failure kind (reader, synchronous setter, queued setter, row setup, arm setup, event handler under Decision 2) shows the error view, runs the content's cleanups once, and reports once.
6. `Retry` remounts the content with a new setup and a new pending phase; retrying while the cause persists shows the error view again.
7. A failure inside the pending or error view goes to the enclosing boundary.
8. Without a boundary, live setter failures reach the mount's `report`, once per exception object; an equal later value retries the write.
9. Disposal of the boundary during the pending phase disposes the hidden content and the pending view; nothing leaks into the document.
10. Server HTML renders the content, pending or error view as above, escaped, with markers.

Mutation checks before review: removing the waiting registration, the reveal's count check, the setter `catch`, the queued per-scope report and the handler `catch` must each fail a test.

## Performance

- A binding outside every boundary pays one scope reference stored at creation, and nothing per run. The keyed benchmarks (`keyed-rows.mjs`) and `run.mjs --template-comparison` must show overlapping ranges before and after for every operation that uses no boundary.
- New measurements: mounting 1, 16 and 128 rows under one boundary that reveals after one async source settles, against Solid 2's `Loading` around the same content.
- Retained heap per row under a boundary is reported, not gated.

## Decisions (approved 2026-10-19)

1. Pending after the first reveal: keep the content and expose `IsPending` (recommended), or hide it again.
2. Event handler exceptions: caught by the boundary (recommended), or not.
3. A setup failure with no boundary keeps throwing from `mountWith` (recommended).
4. A live setter failure under a boundary replaces the content with the error view (recommended), or is only reported.
