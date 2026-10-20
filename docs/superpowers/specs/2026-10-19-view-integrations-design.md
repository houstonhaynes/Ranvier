# Mounted hooks, integrations, raw HTML, document metadata and explicit batches — design

Date: 2026-10-19
Branch: `poc/fable-ranvier-dom`
Baseline: `8b1ea3d` (view boundaries implemented and measured)
Plan: [Task 7](../plans/2026-10-06-fable-ranvier-framework.md#task-7--typed-dom-events-lifecycle-and-owned-async-helpers), its open items, and open item 6 of [Where we stand](../plans/2026-10-06-fable-ranvier-framework.md#open-in-proposed-order). Contracts C07, C08 ([brief](2026-10-06-fable-ranvier-framework-brief.md)); risks G04, G11, G12, G13, G15 ([variants and risks](2026-10-06-fable-ranvier-framework-variants-and-risks.md)).
Companion: [server HTML and hydration design](2026-10-19-server-html-hydration-design.md). Every server and hydration statement below is written against it.
Status: approved 2026-10-19 with every recommended answer. The user agreed both (agreement) decisions under `.claude/rules/view-uses-core.md`: the view-held placement list and the cross-graph head-entry stack.

## Core and view state

Required by `.claude/rules/view-uses-core.md`.

**Core primitives used:**

| Primitive | Job in this design |
|---|---|
| `Api.createEffect`, `Api.untrack`, `Api.runWithOwner` | A mounted hook runs as a one-shot untracked effect, created when its element is placed, under the owner of the instance that declared it. The core decides when it runs: at once outside a batch and outside every body, after the current body inside a list pass or a boundary's effect, after the outermost batch inside `Api.batch`. |
| `Owner`, `Api.onCleanup` | A hook's cleanup, a `NodeRef` reset, a widget's destroy and a document entry's withdrawal are cleanups of the instance's owner. A disposed owner's pending hook effect is disposed with it and never runs. |
| `MountLifetime` (`Lifetime.fs`, over core owners) | Cleanups run while the nodes are still in place; cleanup failures (a lent node found moved) reach the teardown report once. |
| `Api.batch` (`Graph.RunBatch`) | The explicit batch inside or around a generated listener (part 5). The view adds no batching of its own. |
| `NotReadyException`, `BindingState`/`ViewScope` (view, existing) | A raw-HTML or title reader that is pending registers with its boundary like a binding. |
| `Projection.ensureUniqueKeys`, the arm memo, projections | Unchanged; placement is a step after their passes lay rows out. |

**View-owned state:**

| State | Why the core cannot hold it |
|---|---|
| The **placement list** of each placement container (a mount, a region pass, a hidden boundary content): the hooks registered by instances whose nodes are not yet in the mount's host. **(agreement, Decision 1)** | DOM layout. The core has no notion of a node being placed; the list holds hooks only until their nodes are placed, then hands each to a core effect. It schedules nothing itself. |
| The node in a `NodeRef` | A DOM node. |
| The parent recorded when a lent node's hook ran | DOM layout, for the release check. |
| The last string written by a raw-HTML slot | The last value written to a DOM target. |
| The **document metadata registry**: per key, the live entries in activation order, their head elements and the document's title before the first entry. **(agreement, Decision 13)** | DOM nodes in `document.head`. Entries come from mounts on different graphs, and no core primitive spans graphs; a signal and memo per key would need one graph. |
| The server request's metadata collector | Server HTML; created per request, as the companion design's request scope is. |

Nothing here duplicates a keyed reconciler, an ownership tree or a commit queue. The two **(agreement)** items are the points where a reviewer could read view state as a scheduler (the placement list) or a selection cache (the metadata registry); both are raised rather than assumed.

## Purpose and boundaries

The open items of Task 7, in one design:

1. **Mounted access hooks**: code that runs once an element is in the mount's host, with its cleanup, and access to the element from handlers.
2. **Owned and borrowed integrations**: a third-party widget that owns an element's content, and one that borrows a Ranvier element.
3. **Raw HTML** as a separately named unsafe-content boundary.
4. **Portable document metadata** (`title`, `meta`, `link`) with a server fallback.
5. **A generated listener inside an explicit batch**: the test the plan lists as open, and the contract it pins.
6. **The playground screen migration**: what each screen on the old view path needs.

Out of scope: moved/resized notifications for placed elements; portals (rendering into another host); streaming and head streaming; Trusted Types enforcement (Decision 11 leaves room for it); sugar over hooks (Task 10); per-property portable metadata, which the companion design specifies (`PortableProperty`, phase S3) and this design reuses unchanged; any change to `src/Ranvier`.

## What happens today

Read from the code at `8b1ea3d`:

- **Setup-time browser hook only.** `Backend.Browser: (obj -> (unit -> unit)) -> unit` (`Representation.fs`), exposed as `Typed.browser`, runs during setup: `MountState.Browser` calls the hook on `currentElement ()` and registers the returned cleanup with the current owner. The element is detached or in a staging fragment then, so it cannot be measured or focused (G11). Under a boundary it runs while the content is hidden. `Html.render` rejects it under `Reject` and drops it under `Omit`; `Prepared` and `Browser.fs` reject it.
- **Templates have no hook part.** `Part<'native, 'inst>` has `StaticAttribute`, `StaticProperty`, `DynamicPart`, `EventPart` and `NoPart`. A handler reaches its element only through `event.currentTarget`; an instance cannot hold its own nodes, because setup returns the instance before the skeleton is cloned (`materialise`).
- **Attachment.** `MountState.Run` attaches the range (`range.Attach`) and calls `root.MarkAttached ()` after the build. A nested mount (`MountRow`: authored rows, view holes, boundary views) runs `Run` against a fresh fragment, so its own `MarkAttached` says nothing about the outer host. Template rows are built into a `RowBuilder` staging fragment and laid out by the region pass; arms are moved in with `Row.MoveBefore`; boundary content is mounted detached and moved in at `ViewBoundary.Reveal`.
- **Native nodes.** `BuildContext.Native (node, Owned | Borrowed)` adds an external node as a top-level root of a mount; `Borrowed` restores it to its original parent on release. Anywhere else, `MountState.Native` raises "Native roots require top-level placement". `NativeOwnership` names Ranvier's relation to an external node.
- **Raw HTML.** No API. The generator excludes the `innerHTML`, `outerHTML`, `textContent`, `innerText`, `outerText`, `srcdoc` and `files` setters (`tools/Ranvier.Dom.Generate/Program.fs`, `excluded`; `check-types.py` "DOM replacement setter excluded"). `validateAttribute` rejects `on*` and `srcdoc`. Raw-text elements are rejected by `writeMarkup` and `Html.render`. `PropertyBuilder`'s doc says writers must leave child nodes intact.
- **Metadata.** `t.title`, `t.meta` and `t.link` (`Generated/HtmlTemplate.fs`) render those elements in place, inside the mount's host. Nothing writes `document.title` or `document.head`. `t.title` is also the `title` attribute part.
- **Listeners and batches.** Foundation never batches a handler: template dispatch (`TemplateDom.dispatchFor`, `listenDirect`) and `Typed.ownedHandler` run `graph.Run (runWithOwner owner (untrack handler))`. The old path, `Fable.Ranvier.Dom.on`, wraps the handler in `batch` when `DomOptions.BatchEvents` is set, the default. No Foundation test dispatches a generated listener inside or around `Api.batch`; `GeneratedChecks.lifecycle` covers generated listeners under both commit modes without a batch. Core pins "a batch that throws still flushes the writes it made" (`tests/Ranvier.Tests/Reentrancy.fs`).
- **Queued mounts.** `MountState.Template` expands templates through `definition.Expand` (Program operations) under `QueuedCommits`, so every template feature below needs a `Backend` or `Operations` form as well as its compiled one.

## Placement: one mechanism for parts 1, 2 and 4

An instance's element is **placed** when the element is a descendant of its mount's host, every enclosing region has laid out its row, and the root mount has attached its range to the host. The host itself may be detached (tests mount into detached sections); placement does not check `isConnected` (Decision 2).

```fsharp
/// The hooks of instances whose nodes are not yet placed.
type internal Placement =
    /// A container whose hooks forward to parent until parent is placed; null parent for a root mount.
    new: parent: Placement -> Placement
    /// Holds the hook until the container is placed, or creates its effect at once when it is.
    member Add: owner: Owner * hook: (unit -> unit) -> unit
    /// Marks the container placed and creates the effect of every held hook, in the order added.
    member Place: unit -> unit
    /// Drops every held hook: their instances were disposed unplaced.
    member Discard: unit -> unit
```

Where `Place` is called:

| Container | Placed when |
|---|---|
| Root mount (`MountState.Run`) | After `range.Attach` and `MarkAttached`. |
| A region pass (list rows, arm, view hole) | After the pass lays its new rows out between the markers (after `Row.MoveBefore` for an arm and a hole). During the root's setup, the pass forwards to the root. |
| Boundary content | At `Reveal`, after `place content`. A content torn down before reveal calls `Discard`. |
| Boundary pending and error views | In `Show`, after `place row`. |
| A nested mount (`MountRow`) | Never on its own `Run`: its container is a child of the region pass or boundary that placed it. `MountState` takes the parent `Placement`. |

`MountServices` gains `Placed: Owner * (unit -> unit) -> unit`; `RowBuilder` forwards to its site's pass container, and `MountState`'s own implementation to its container.

Each hook becomes `Api.runWithOwner owner (fun () -> Api.createEffect (fun () -> Api.untrack run))`, where `run` calls the hook inside a `try` and routes a failure (below). The hook's returned cleanup is registered with `Api.onCleanup` inside the effect, so it runs when the owner is disposed. The effect reads nothing tracked and never re-runs.

Failures:

- During `mountWith`, before it returns: the first failure unwinds the mount and is rethrown from `mountWith`, as a setup failure is (boundary Decision 3). The placement's remaining hooks still run, including those after the failing one; every hook that ran has its cleanup run by the unwind.
- Afterwards: the failure goes to the instance's scope, `Fail [ error ]`. Under a boundary the content is replaced by the error view (the boundary design's rule for every failure under content); outside every boundary the mount's `report` receives it once per exception object. The instance stays (Decision 4).
- A mount created inside an outer `Api.batch` or a computation body runs its hooks after the batch or the body, after `mountWith` has returned: their failures take the second path. A nested `mountWith` inside a placed hook, such as a widget's, is such a mount: its hook failures go to its own `report` and are not rethrown.

Moves (`Mounted.MoveBefore`, keyed reorders, `Relocate`) do not re-run hooks. A row whose nodes were placed once and are later moved by a pass is not placed again.

## Part 1: mounted access hooks

### API

```fsharp
// Template.fs
/// The element of a placed instance. Current is Some while the element is placed, None during setup, while hidden by a boundary, and after disposal.
[<Sealed>]
type NodeRef<'native> =
    member Current: 'native option

module NodeRef =
    val create: unit -> NodeRef<'native>

/// Hook parts: code that runs once the element is placed in the mount's host.
[<Sealed>]
type HookBuilder<'inst> =
    /// Runs once the element is placed; the returned cleanup runs when the instance is disposed, before its nodes leave.
    member mounted: hook: ('inst -> 'native -> (unit -> unit)) -> Part<'native, 'inst>
    /// Sets the instance's reference while the element is placed.
    member ref: select: ('inst -> NodeRef<'native>) -> Part<'native, 'inst>

type TemplateBuilder<'inst> with
    member Hooks: HookBuilder<'inst>

// Representation.fs
type PlacedKind =
    | Mounted
    | Lent        // part 2
    | Widget      // part 2

type Backend with
    /// Registers a hook for the element currently receiving modifiers, run once the element is placed.
    abstract Placed: PlacedKind * (obj -> (unit -> unit)) -> unit   // default: raises NotSupportedException

// Typed.fs
val mounted: hook: ('native -> (unit -> unit)) -> Modifier<'native>
val ref: target: NodeRef<'native> -> Modifier<'native>
```

`Part` gains `PlacedPart of PlacedKind * Func<obj, obj, (unit -> unit)>`; `TemplateNode.ElementNode` gains an array of them; the compiled `Tables` gain `Placed: (int * PlacedKind * Func<obj, obj, (unit -> unit)>) array`, bound in `bind` after events. `expandNode` maps a placed part to `backend.Placed`, so queued mounts behave the same. `ref` is a `mounted` hook that sets `Current` and resets it in its cleanup; it needs no mechanism of its own. `NodeRef.Current` is a plain field: reading it tracks nothing (Decision 6).

`Typed.browser` stays as the setup-time hook: it runs during setup on an unplaced node and suits listeners and node-local state. Its doc gains that sentence; `mounted` is the hook for focus, measurement and widgets (Decision 5).

### Usage

```fsharp
type Search = { Query: Signal<string>; Field: NodeRef<HTMLInputElement> }

let search =
    Component.define (fun _ (initial: string) -> { Query = Api.createSignal initial; Field = NodeRef.create () }) (fun t ->
        t.div
            [||]
            [|
                t.input
                    [|
                        t.Props.Input.value (t.from (fun s -> s.Query))
                        t.Hooks.ref (fun s -> s.Field)
                        t.Hooks.mounted (fun _ input ->
                            input.focus ()
                            ignore)
                    |]
                t.button
                    [| t.Events.onClick (fun s _ -> s.Field.Current |> Option.iter (fun input -> input.select ())) |]
                    [| t.text "Select all" |]
            |])
```

### Lifecycle and disposal

- Order (Decision 7), the same in template, Program and Tree authoring under both commit modes: an instance's own hooks and head entries in document order of their elements, including elements declared after an inlined child or a region; then its inlined children's, each child's own before its children's, in setup order; then its regions' rows. An authored component (`ops.Component`) and an authored row are instances. A nested instance's head entry therefore activates after its parent's and is shown for a shared key (Decision 14).
- The hook sees every binding's first settled value: synchronous bindings commit during setup, and a queued binding writes its first settled value at once (`Binding.startQueuedIn`). A binding whose first read was pending has written nothing.
- Cleanup runs once, when the instance's owner is disposed, before its nodes leave the host (`MountLifetime`'s child-first teardown). A hook that never ran has no cleanup.
- An instance disposed between registration and placement never runs its hook: its pending effect is disposed with its owner, and a hidden boundary content is discarded.

### Boundaries and pending views

- Hooks under hidden content run at reveal, not at the content's setup. Under the content's retry, the new content's hooks run at its reveal.
- After the first reveal, a later pending read keeps the content (boundary Decision 1), and its hooks do not run again.
- Hooks in the pending and error views run when the view is shown; their cleanups run when the view is retired.
- A hook failure under content is a failure under the boundary: the content is torn down (the cleanups of hooks that ran included) and the error view is shown.

### Server and hydration

A placed part is a browser part: the server never runs it; `Reject` raises naming the element and the part, `Omit` writes nothing (companion design, "browser-only fallback", listeners and `Browser` hooks). Under hydration the root container is placed when adoption completes, so hooks run on the server's nodes (companion design H1).

### Tests (`PlacementChecks.fs`, `placement.test.mjs`; Chromium in `placement.spec.mjs`)

Each in template, Program and Tree authoring, under `SynchronousCommits` and `QueuedCommits`:

1. "a mounted hook runs after the element is in the host and its cleanup runs before the element leaves" (`parentNode` checks inside the hook and the cleanup).
2. "setup-time browser hooks run on an unplaced element" (pins today's `Typed.browser`).
3. "hooks run in setup order: an instance, its inlined children, then region rows"; "hooks run in setup order: an instance's elements, including those after a region, then the region's rows" (an element after the list, and an inner list in each row); "hooks run in setup order: an instance's elements, including those after an inlined child, then the child's"; "hooks run in setup order: each instance before its inlined children, two levels deep".
4. "a row added by a list pass runs its hook after the pass lays it out"; "a row created and removed in one batch never runs its hook".
5. "an arm's hook runs after the arm is moved in; a replaced arm's cleanup runs before the next arm's hook".
6. "hooks under a hidden boundary content run at reveal"; "content torn down before reveal runs no hook and no hook cleanup"; "a retried content runs its hooks again at its reveal".
7. "pending and error view hooks run while shown and clean up when retired".
8. "a hook failure during mountWith unwinds the mount and rethrows"; "a later hook failure goes to the boundary, or to report once without one".
9. "a mount inside Api.batch runs its hooks after the batch".
10. "NodeRef.Current is None during setup, Some while placed, None after disposal and while hidden"; "a NodeRef shared by two rows keeps the remaining row's element when the other row leaves" (a cleanup resets the reference only while it holds the cleanup's element).
11. "moving a mount or reordering rows does not re-run hooks".
12. "a mounted hook raises under Reject and writes nothing under Omit".
13. Chromium: "a mounted hook can focus and measure its element" (`getBoundingClientRect` width above zero).

Mutation checks: creating the effect at registration instead of placement, dropping the boundary's `Discard`, forwarding a nested mount's container to its own `Run`, and dropping the failure `try` must each fail a test.

## Part 2: owned and borrowed integrations

Two contracts, named from the integration's side:

- **Widget** (the widget owns the element's content): Ranvier creates the element, writes only the element's declared attributes, properties and listeners, and never writes its children. The widget renders into it once placed and destroys its content in its cleanup.
- **Lent** (the integration borrows a Ranvier element): the integration may add listeners, attributes, classes and properties that no Ranvier part declares; it does not move or remove the element and does not touch Ranvier's children of it. Its cleanup undoes what it added.

The names differ from `NativeOwnership` (`Owned`/`Borrowed`), which describes Ranvier adopting an external node as a mount root and stays unchanged (Decision 10).

### API

```fsharp
type HookBuilder<'inst> with
    /// The widget owns the element's content once placed; the element has no template children. The cleanup destroys the widget.
    member widget: attach: ('inst -> 'native -> (unit -> unit)) -> Part<'native, 'inst>
    /// The integration borrows the element once placed; the cleanup removes what it added. The element must keep its parent.
    member lend: attach: ('inst -> 'native -> (unit -> unit)) -> Part<'native, 'inst>

// Typed.fs
val widget: attach: ('native -> (unit -> unit)) -> Modifier<'native>
val lend: attach: ('native -> (unit -> unit)) -> Modifier<'native>
```

Both are placed parts (`PlacedKind.Widget`, `PlacedKind.Lent`) on the part 1 mechanism. Definition-time checks (`elementNode`): an element with a `widget` part has no children and at most one content part (`widget` or raw HTML, part 3); otherwise `ArgumentException`. The authored backend checks the same after the element's children run (`MountState.Element`).

### Usage

```fsharp
let chart =
    Component.define (fun _ (points: unit -> float array) -> points) (fun t ->
        t.div
            [|
                t.Attrs.className "chart"
                t.Hooks.widget (fun points node ->
                    let instance = ChartLibrary.create (node, points ())
                    Api.createEffect (fun () -> instance.update (points ()))
                    fun () -> instance.destroy ())
            |]
            [||])

let masked =
    Component.define (fun _ () -> ()) (fun t ->
        t.input [| t.Hooks.lend (fun _ input -> let mask = InputMask.apply input in fun () -> mask.remove ()) |])
```

The effect inside the widget hook belongs to the hook's effect scope, under the instance's owner, and is disposed with it.

A nested Ranvier mount is a widget too, which the playground shell needs (screen 1):

```fsharp
t.Hooks.widget (fun shell host ->
    let demo = Mounting.mountWith shell.Mode shell.Graph (Some (Api.getOwner ())) shell.Report host None shell.BuildDemo
    fun () -> demo.Dispose ())
```

### Lifecycle and disposal

- `attach` runs once the element is placed; the cleanup runs before the element leaves the host, so a widget's `destroy` sees its nodes attached (G04).
- The element and everything left in it leave with the element's removal; Ranvier removes nothing inside it individually.
- Delegated events: the mount's host listener walks up from a target inside widget content and finds no Ranvier state until it reaches a Ranvier node; widget listeners are the widget's. A nested Ranvier mount inside a widget element registers its own host listener on that element, as nested hosts do today.
- **Release check for lent and widget elements** (Decision 9): the hook records the element's parent when it runs; after its cleanup, if the element is not a root of a row or mount and its parent differs, a `InvalidOperationException` "An integration moved its element" joins the teardown report. Roots are already validated by `OwnedRange.Validate` and region validation before structural operations.
- Keyed moves move the element with its row; a widget's content moves with it (an `iframe` reloads). No move notification is offered.

### Boundaries and pending views

As part 1: a widget under hidden content is attached at reveal and destroyed with the content on teardown; a widget's `attach` failure under content shows the error view.

### Server and hydration

The element is written with its attributes and no children. The placed part follows the browser-part rule (`Reject` raises, `Omit` omits the hook, the element is still written). Under `Omit`, `Html.render` raises for an authored widget element with children or a second widget part, with the client mount's messages. Under hydration the empty element is adopted and the widget attaches after adoption; a widget element must not be filled on the server.

### Tests (`IntegrationChecks.fs`, `integration.test.mjs`; Chromium in `integration.spec.mjs`)

1. "a widget renders into its element once placed and its destroy runs while the element is attached".
2. "an element with a widget part and template children fails at definition time" (template) and "at mount" (authored).
3. "widget content is removed with the element and never validated as Ranvier content" (a row reorder and a clear with foreign nodes inside the widget).
4. "a nested mount inside a widget element updates, dispatches delegated events and disposes with its owner".
5. "a lent element keeps attributes added by the integration and Ranvier's slots keep updating".
6. "an integration that moves its element is reported after its cleanup"; "a lent row root moved by a keyed reorder is not reported".
7. Boundary: "a widget under hidden content attaches at reveal and is destroyed on teardown"; "a widget attach failure shows the error view".
8. "a widget element renders empty on the server; Reject raises and Omit writes the element"; "a widget element with children or a second content part raises under Omit" (Tree and Program).
9. Chromium: "a widget measuring its element on attach sees layout" and "focus inside a widget survives an unrelated list update".

Mutation checks: removing the no-children check, the release check, and running `attach` at setup must each fail a test.

## Part 3: raw HTML as an unsafe-content boundary

Raw HTML is the content of one element, React's `dangerouslySetInnerHTML` model, not a marker region (Decision 11):

- The element's children are parsed by the browser in the element's own context (`element.innerHTML <- value`), so a `tr` inside a `tbody` element parses as it would on the server.
- On the client, scripts in the content are inserted inert by the fragment parser; inline event handler attributes (`<img onerror>`) and `javascript:` URLs **do** run. In server HTML the content is part of the document, and every script, handler and URL in it runs. The content is not sanitised. The caller supplies trusted or sanitised content.
- No region markers are written, so the companion design's raw-HTML marker constraint (no comment starting with `[` or equal to `]`) does not arise: the hydration walk never enters the element.

### API

```fsharp
/// Markup inserted without escaping. Create it only from trusted or sanitised content.
[<Sealed>]
type UnsafeHtml =
    member Markup: string
    static member Trust: markup: string -> UnsafeHtml

[<Sealed>]
type UnsafeBuilder<'inst> =
    /// The element's content, parsed as HTML without escaping. The element has no template children.
    member html: content: UnsafeHtml -> Part<'native, 'inst>
    /// The element's content, re-parsed when the reader returns different markup.
    member html: read: ('inst -> UnsafeHtml) -> Part<'native, 'inst>

type TemplateBuilder<'inst> with
    member Unsafe: UnsafeBuilder<'inst>
```

The part is a slot with a new target, `SlotTarget.ContentTarget`, written by `node.innerHTML <- markup` in `TemplateDom.write`, with string equality on `Markup` as its `Same`. A static `UnsafeHtml` is a slot whose reader returns the constant, so the first commit writes it. Definition-time checks: the element has no children and one content part; the tag is not void, raw-text (`rawText`), `template`, `textarea` or `title`. `expandNode` expands the part to a reactive property with an internal `innerHTML` writer, for queued mounts. Program and Tree authoring get no raw-HTML operation: an authored view embeds a template component for it (Decision 12).

### Usage

```fsharp
let article =
    Component.define (fun _ (body: unit -> UnsafeHtml) -> body) (fun t ->
        t.div [| t.Attrs.className "prose"; t.Unsafe.html (fun body -> body ()) |] [||])

// the trust decision stays at the call site, where a review can find it:
Component.render article (fun () -> UnsafeHtml.Trust (Sanitizer.clean markdownHtml))
```

### Lifecycle and disposal

- The content is DOM state of the element: replaced wholesale on a different value, removed with the element. Ranvier keeps no node of it.
- A reader is a render slot: pending reads wait under a boundary, read failures and write failures follow the boundary design's rules (a live setter failure under a boundary shows the error view).
- External changes inside the element are not validated.

### Server and hydration

- Written verbatim between the element's tags. Raw HTML has an HTML meaning, so it is written under both `Reject` and `Omit`.
- Markup the server parser closes early (`a</div><div>b`) parses differently from the client's in-context parse. That is a structural difference the companion design's mismatch policy re-renders at the nearest region; the API doc states that the markup must be a well-formed fragment for its element.
- Hydration writes the content again on the first commit, the companion design's Decision 2 rule for value differences, since the client cannot compare its markup with the parsed server nodes (Decision 15).
- A server plan operation `Content of Slot` joins the companion design's `ServerPlan` table; its golden pins (S1, test 1) include a raw-HTML fixture.

### Tests (`UnsafeHtmlChecks.fs`, `unsafe-html.test.mjs`)

1. "raw HTML replaces the element's content and is re-parsed only when the markup changes" (MutationObserver counts).
2. "raw HTML inside a tbody element parses table rows in context" (jsdom and Chromium).
3. "an element with raw HTML and template children fails at definition time"; "void, raw-text, template, textarea and title elements reject raw HTML".
4. "script elements in raw HTML do not run; an onerror attribute does" (Chromium; documents the contract).
5. "a pending raw-HTML reader keeps its boundary pending; a failing one shows the error view".
6. "server HTML writes raw content verbatim under Reject and Omit, and text slots beside it stay escaped"; "a raw-HTML reader returning null fails naming the null value, on the client and the server" (`ArgumentException` "An UnsafeHtml reader returned null").
7. "hydration rewrites raw content once and the slot then updates".
8. Generator: "innerHTML, outerHTML, textContent, innerText, outerText and srcdoc setters stay excluded" (existing `check-types.py` case, extended).

Mutation checks: removing the equality skip and the no-children check must fail a test.

## Part 4: portable document metadata

### API

```fsharp
/// Entries for the document's head: inactive until the instance is placed, withdrawn when it is disposed.
[<Sealed>]
type DocumentBuilder<'inst> =
    member title: value: string -> TemplateView<'inst>
    member title: read: ('inst -> string) -> TemplateView<'inst>
    /// A meta element; the key is its static name, property, http-equiv or charset attribute.
    member meta: parts: Part<HTMLMetaElement, 'inst> array -> TemplateView<'inst>
    /// A link element; keyed by `rel` when it is canonical, otherwise by an explicit key, otherwise unkeyed.
    member link: parts: Part<HTMLLinkElement, 'inst> array -> TemplateView<'inst>
    member link: key: string * parts: Part<HTMLLinkElement, 'inst> array -> TemplateView<'inst>

type TemplateBuilder<'inst> with
    member Document: DocumentBuilder<'inst>

// Representation.fs
type Backend with
    /// Activates a document entry for the current instance; default raises NotSupportedException.
    abstract Head: HeadEntry -> unit
```

`TemplateNode` gains `HeadNode of HeadEntry`, zero skeleton width: it writes no skeleton markup and is not a walk target. `SkeletonLayout` gains `Heads: (HeadEntry * int) array` (entry, owning part). Parts other than static and dynamic attributes raise at definition time; a key attribute (`name`, `property`, `http-equiv`, `charset`) must be static. `expandNode` maps a head node to `ops.Embed (fun backend -> backend.Head entry)`.

`t.Document` is a new builder name; the generator test asserts no generated group takes it, as `t.title`, `t.meta` and `t.link` stay the in-place element and attribute members.

### Usage

```fsharp
let productPage =
    Component.define (fun _ (product: unit -> Product) -> product) (fun t ->
        t.fragment
            [|
                t.Document.title (fun p -> (p ()).Name + " · Shop")
                t.Document.meta [| t.Attrs.attribute ("name", "description"); t.Attrs.attribute ("content", fun p -> Some (p ()).Summary) |]
                t.Document.link ("canonical", [| t.Attrs.attribute ("rel", "canonical"); t.Attrs.attribute ("href", fun p -> Some (p ()).Url) |])
                t.h1 [||] [| t.text (fun p -> (p ()).Name) |]
            |])
```

### Client semantics

- An entry activates when its instance is placed (the part 1 mechanism) and withdraws in its owner's cleanup.
- Per key, the most recently activated live entry is shown; withdrawing it shows the previous live one; withdrawing the last restores the document's title as it was before the first title entry (Decision 14). Unkeyed links are each shown while live.
- A title entry writes `document.title` in its own effect while shown; meta and link entries own one head element each, attached while shown, with their attribute slots bound as template attributes.
- The registry adopts a head element carrying `data-ranvier-head` with the same key instead of creating one (Decision 16).
- Keys: `title`; `meta:charset` or `meta:<attribute>=<value>`; `link:canonical` for a static canonical `rel`; `link:key:<key>` for an explicit link key. An explicit key and a canonical link always have distinct keys. The registry writes `data-ranvier-head`; an entry part naming that attribute raises at definition time.

### Server fallback

Consistent with the companion design:

- `Server.render` keeps its signature. A new entry point returns the head with the body:

  ```fsharp
  type Page = { Body: string; Head: string }
  val renderPage: Server.Options -> Component<'props> -> 'props -> Page
  ```

- The collector is created per request with the request graph (phase S2); nothing is module state.
- Entries are written in first-activation order of their keys; for a key, the last written entry wins. Text and attributes use the companion design's escaping (S1); names are validated by `validateAttribute`. Each element carries `data-ranvier-head`; the title element too.
- A boundary's abandoned content attempt drops the entries it added (the collector is truncated to its length before the attempt, beside the attempt owner's disposal, S4).
- `Html.render` (authored server) has no head output: `Omit` drops an entry, `Reject` raises "Document metadata requires Server.renderPage".
- Hydration: title compare-then-write (companion Decision 23); meta and link adopt their server elements by key; after `hydrate` returns, `data-ranvier-head` elements no client entry adopted are removed (companion phase H4).

### Boundaries and pending views

Entries under hidden content activate at reveal, so a loading page keeps the previous title until its content shows; a pending view's own entries are live while it is shown. Teardown withdraws the content's entries before the error view's activate. A pending title reader waits like a binding.

### Tests (`DocumentChecks.fs`, `document.test.mjs`; .NET and Fable server tests in the companion design's `ServerChecks.fs`)

1. "a title entry sets document.title once placed and restores the previous title when disposed".
2. "the most recent entry for a key is shown and the previous one returns when it withdraws" (two nested components, meta by name); "an inlined child's title entry is shown over its parent's later title entry" (template, Program and Tree, both commit modes).
3. "unkeyed links are each present while live; a canonical link is keyed"; "a link with the explicit key canonical and a canonical link are both shown".
4. "a reactive title updates document.title and its pending read waits under a boundary".
5. "entries under hidden content activate at reveal and withdraw on teardown".
6. "event and property parts on a document entry fail at definition time; a dynamic key attribute and a data-ranvier-head attribute fail".
7. "two concurrent server requests collect only their own entries" (companion test 10's harness).
8. "an abandoned boundary attempt's entries are not written".
9. "renderPage escapes titles and attribute values and marks every element".
10. "hydration adopts marked head elements by key and removes unadopted ones"; "a client mount adopts marked elements without hydrate".

## Part 5: a generated listener inside an explicit batch

### Contract

A listener is never batched by the view (global constraints: no silent scheduling). A handler that groups writes calls `Api.batch` itself; the core defers effects to the end of the outermost batch. The doc of `TemplateParts.event`, `EventBuilder.on`, `Typed.listener` and the generated `on*` members gains one sentence saying so. No helper is added (Decision 17).

```fsharp
t.button [| t.Events.onClick (fun s _ -> Api.batch (fun () -> for _ in 1..100 do s.Count.Value <- s.Count.Value + 1)) |] [| t.text "Burst +100" |]
```

### Tests (`GeneratedChecks.fs`, new cases beside `lifecycle`)

For Tree, Program and template authoring, each under `SynchronousCommits` and `QueuedCommits (CollectAndContinue, post)`, with a generated `onClick` and a text binding observed by a counting writer:

1. "a generated listener without a batch commits each write" — synchronous: 100 writes, final value when `click` returns; queued: the queue coalesces to 1 write after flush. Pins the absence of implicit batching.
2. "a generated listener's writes inside Api.batch commit once" — synchronous: 1 write, final value when `click` returns; queued: unchanged at return, 1 write after flush.
3. "a listener dispatched inside an outer Api.batch commits after the batch" — `graph.Run (fun () -> Api.batch (fun () -> button.click (); assert unchanged))`, then 1 write.
4. "a handler that throws inside its batch commits the writes it made and its failure goes to the boundary" — matches core's "a batch that throws still flushes the writes it made"; without a boundary the exception propagates from the dispatch as today.
5. "a delegated and a direct generated listener behave the same inside a batch" (`click` is delegated; `blur` direct).

These tests change no runtime code. They pin the contract the screen migration relies on.

## Part 6: playground screen migration

`App.fs` has five screens. Two use the old path (`src/Fable.Ranvier` `Mount`/`Dom`); three already use Foundation.

| Screen | Path today | What it needs |
|---|---|---|
| 1. Shell (header, intro, scheduling bar, mount bar, empty state, footer) | Old: `Mount.mount graph host`, `Dom.element`/`text`/`reactiveText`/`bindAttribute`/`on`; `demoHost` and `foundationHost` are native child nodes of the old tree | A template component mounted with `Mounting.mountWith SynchronousCommits`; two `t.Hooks.widget` elements that create and dispose the demo and Foundation mounts (part 2), since those choose their own commit modes and graphs; reactive `data-active` and `hidden` attributes (available); the scheduling toggle and Flush buttons call `Mounted.Flush` on the demo mount; the watch test edits the literal `Dom.text "Create once."` and must edit the new template's text instead. |
| 2. Demo (counter and input panels, `demo-root`) | Old: `Mount.mountWith { DomOptions.Default with Scheduling = Microtask \| Synchronous }`, `BatchEvents` on by default | A template component; the commit mode swapped between `SynchronousCommits` and `Microtask.mode CollectAndContinue` on remount; `Api.batch` in the Burst handler (part 5), or `default synchronous mode batches a burst` (`["100", 1]`) fails; `createMemo` for doubled in setup; `t.Props.Button.disabled` and `t.Props.Input.value` (the old `setInputValue` skipped an equal live value; the template slot compares with its committed value, so the input tests in Chromium must stay green); `data-parity`, `aria-live`; state signals stay on `graph`, outside the component, so remount keeps them. |
| 3. Foundation keyed rows (`#foundation-demo`) | Foundation, authored Program with `View.keyedArray`, in the queued Foundation mount | Nothing required. Optional: a template `each` version; under `QueuedCommits` it expands through Program operations, not the compiled path. |
| 4. Reusable components (`ComponentsExample.fs`) | Foundation, authored `createComponent` | Nothing required. |
| 5. Load result (`#foundation-load`) | Foundation mount; core `createBoundary` with hand-written `IsWaiting`/`Caught` status | Nothing required. Its test pins retained node identity across pending, failure and recovery, which a view boundary's teardown replaces (boundary Decision 4), so it stays the core-boundary demo. Optional: a separate view-boundary panel with its own test (Decision 19). |

After screens 1 and 2 move, `App.fs` no longer opens `Fable.Ranvier`; `Playground.fsproj` keeps the project reference, which Foundation needs. The README's description of the old PoC and its "batched events" sentence change with it. The test fixtures under `tests/` that exercise `src/Fable.Ranvier` (`DomChecks.fs`, `ScheduledChecks.fs`, `scheduled.html`) test that library and stay.

Migration tests: every existing `playground.spec.mjs` test passes unchanged except the watch test's edited literal; the scheduling-status texts stay; a new Chromium test checks that the demo widget mount is disposed when the shell is (the Vite dispose path).

## Build order

1. **Part 5** — tests and doc sentences only; pins the batch contract before anything changes.
2. **Placement and part 1** — `Placement`, `Backend.Placed`, `PlacedPart`, `NodeRef`; boundaries' `Reveal`/`Show`/`Discard` wiring.
3. **Part 2** — `widget` and `lend` on the placement mechanism, the no-children check and the release check.
4. **Part 3** — independent of placement; the `ContentTarget` slot and its server operation, landing before the companion design's S1 golden pins.
5. **Part 4 client half** — on placement; the server half joins the companion design's S2 (collector), S4 (attempt truncation) and H4 (adoption).
6. **Part 6** — screens 2 then 1; needs parts 1, 2 and 5.

## Performance

- A template with no placed, content or head part pays one empty-array length check per part in `bind`, and no allocation per row: a container's hook lists are created on first `Add`. Containers are allocated whether or not a hook exists: one `Placement` per root mount, per hole, per boundary content attempt and per shown boundary view, and one `PlacementPass` per list or arm region with one container per pass that requests one; an authored row mount also looks up its `HeadDocument`. The measurements show this cost neutral. The keyed benchmarks (`keyed-rows.mjs`) and `run.mjs --template-comparison` must show overlapping ranges before and after, as the boundary design required.
- New measurement, reported not gated: mounting 1, 16 and 128 rows each with one `mounted` hook, against Solid 2's `onSettled` in the same rows.
- Raw HTML and document entries: micro-measurements only; neither is on a list row path.

## Decisions

1. Decision: the placement list is view-owned and holds hooks only until their nodes are placed; the run itself is a core effect — the core has no DOM placement, and the alternative (hooks as core effects created at setup) runs them before the nodes are placed — if wrong, a placement notification belongs in the core and this list duplicates it. **(agreement)**
2. Decision: placed means inside the mount's host with every enclosing region laid out, not `isConnected` — tests and applications mount into detached hosts, and a host's own attachment is the application's — if wrong, measuring hooks in a detached host read zeros.
3. Decision: hooks run as core effects, so a batch defers them and a list pass runs them after its layout — one scheduling contract for every graph callback — if wrong, a direct call after placement would run inside a batch before its writes commit.
4. Decision: a hook failure during `mountWith` unwinds and rethrows; afterwards it goes to the scope and the instance stays — matches boundary Decision 3 and the reader-failure rule — if wrong, a broken row keeps half-initialised widget state on screen without a boundary.
5. Decision: keep `Typed.browser` as the setup-time hook and add `mounted`; templates get only `mounted` — existing tests and listeners on detached nodes use it — if wrong, two hooks with similar names confuse authors; renaming `browser` to `setup` is the alternative.
6. Decision: `NodeRef.Current` is a plain, untracked field — a node is DOM state, and a tracked ref re-runs readers on every placement — if wrong, a reader that needs the node reactively must read a signal the hook writes.
7. Decision: hooks run in setup order (instance, inlined children, rows) — the order bindings already run in — if wrong, a parent hook cannot see its children's widgets initialised (React runs effects bottom-up).
8. Decision: `widget` and `lend` are placed parts, not new node kinds — one mechanism, typed tags from the catalogue keep working — if wrong, the no-children rule is a run-time check, not a type.
9. Decision: after an integration's cleanup, a moved non-root element is reported through the teardown — one parent comparison per integration — if wrong, a false report where an integration legitimately reparents and restores across a move.
10. Decision: keep `NativeOwnership` (`Owned`/`Borrowed`) for adopting external nodes and name integrations `widget`/`lend` — the existing names describe Ranvier's side, these describe the integration's — if wrong, three ownership words for related ideas.
11. Decision: raw HTML is an element's whole content, parsed by `innerHTML` on that element, behind an `UnsafeHtml.Trust` value — in-context parsing, inert scripts on the client (server HTML runs them), no hydration markers, and one greppable trust point — if wrong, mixed raw and template siblings need a wrapper element; a marker region with the companion design's comment constraint is the alternative.
12. Decision: raw HTML and document entries are template-only; authored views embed a template component — templates are the production path, and authored server output (`Html.render`) has no head channel — if wrong, authored apps need an embedding for each.
13. Decision: the document metadata registry is view-owned, per document and across graphs — its state is head elements, and no core primitive spans graphs — if wrong, the per-key selection is a selection cache the rule asks to put in the core. **(agreement)**
14. Decision: per key the most recently activated live entry shows, and withdrawal restores the previous — nested screens set their own title and the outer one returns on close (`@solidjs/meta` behaves so) — if wrong, setup-order activation within one mount decides between siblings, which differs from server write order.
15. Decision: hydration rewrites raw content on the first commit — the client cannot compare markup with parsed nodes, and the companion design corrects value differences — if wrong, images in raw content re-decode once per hydration.
16. Decision: the client registry adopts `data-ranvier-head` elements by key with or without `hydrate` — no duplicate head elements when a server page is mounted client-side — if wrong, an application's own marked elements are adopted.
17. Decision: no batching helper; handlers call `Api.batch` — the global constraints forbid silent scheduling and a helper is sugar (Task 10) — if wrong, every handler with several writes repeats the call.
18. Decision: the playground shell embeds the demo and Foundation mounts through `widget` elements — commit mode and graph are per mount, so a region cannot switch them — if wrong, the shell must expose host nodes another way, such as `NodeRef` plus mounts created outside the template.
19. Decision: the load-result panel stays on the core boundary — its test pins node identity a view boundary replaces — if wrong, the playground shows no view boundary.

## Open questions

1. **Placement list (Decision 1, agreement).** May the view keep a per-container list of hooks waiting for placement, or should the core gain an owner-scoped "after placement" notification? Recommended: the view list; it holds DOM layout timing only and every hook runs as a core effect.
2. **Document registry (Decision 13, agreement).** May the view keep a cross-graph, per-key stack of head entries? Recommended: yes; the alternative is one graph per document, which applications with several graphs (the playground has two) do not have.
3. **Hook failure after the initial mount (Decision 4).** Report and keep the instance, or dispose it as a row setup failure? Recommended: report and keep; a boundary gives the dispose-and-replace behaviour where it is wanted.
4. **Hook order (Decision 7).** Setup order or bottom-up? Recommended: setup order, documented and pinned by test 3; revisit if a widget composition needs children first.
5. **Release check (Decision 9).** Include the parent comparison for `widget` and `lend` elements? Recommended: yes, for non-root elements only.
6. **Raw HTML form (Decision 11).** Element content or marker region? Recommended: element content.
7. **Trusted Types.** Should `UnsafeHtml.Trust` accept a browser `TrustedHTML` as well as a string? Recommended: later; the single trust point makes the addition local.
8. **Raw HTML under hydration (Decision 15).** Rewrite or trust the server nodes? Recommended: rewrite, consistent with the companion design's Decision 2.
9. **Head on the server (part 4).** A new `renderPage` returning `{ Body; Head }`, or a head sink in `Server.Options`? Recommended: `renderPage`, leaving `render` as the companion design defines it.
10. **Head adoption without hydrate (Decision 16).** Recommended: yes, by key, only for elements carrying `data-ranvier-head`.
11. **Batch helper (Decision 17).** Recommended: none in Task 7; reconsider with sugar in Task 10.
12. **Screens 1 and 2: templates or authored Program?** Recommended: templates, the production path; the queued demo mount then exercises template expansion through Program operations, which the plan records as how queued mounts work.
13. **Load-result panel (Decision 19).** Recommended: keep it on the core boundary and add no view-boundary panel in this task; add one with the boundary documentation.
