# .NET DOM twin and `Debug.dump` design

> **Status 2026-10-21:** approved, with the ten decisions of §9. Implementation follows
> [the plan](../plans/2026-10-21-dom-twin-debug-dump.md) on branch `feat/dom-twin`. Every claim about current code cites
> `file:line` at `0a7fe326`; the merge of `origin/master` at `2cf84be` (#138) changed `src/Ranvier/Core.fs` after line
> 4542 only, so every citation holds at the merge commit `8311aa1`. Two scratch probes back the feasibility claims
> (§2.3); their code is reproduced in Appendix A and is not committed.

Date: 2026-10-21. Inputs: `experiments/Ranvier.View.Foundation/*.fs`, `experiments/Ranvier.View.Portable/*.fs`,
`src/Ranvier/{Core,Types,TraceApi}.fs`, `docs/content/view/{testing,events}.md`, `docs/content/server/rendering.md`,
`docs/content/guide/{tracing,testing}.md`, the [view sugar v2 spec](2026-10-20-view-sugar-v2-design.md), the
[motion spec](2026-10-21-motion-design.md), `examples/Fable.Ranvier.Playground/{package.json,tests/,benchmarks/frameworks/sugar-size*.mjs}`,
`.claude/rules/{view-uses-core,comments}.md` and `AGENTS.md`.

**Terminology.** The sugar specs use "twin" for a sugared view's explicit equivalent. In this spec, **DOM twin** (or
**twin**) means the .NET document that stands in for a browser DOM; "explicit twin" keeps its sugar-spec meaning.

## Core and view state

Required by `.claude/rules/view-uses-core.md`.

**Core primitives used:**

- `Graph`, `Owner`, `createRoot`, `onCleanup`: unchanged. The twin runs the real mount code (§3.1), so every mount,
  region, arm and boundary keeps the core primitives it uses today.
- `ManualDispatcher` and `Graph.Pump`: the twin's `Settle` pumps the graph's inbox on the test thread
  (`docs/content/guide/testing.md:30-45`).
- `TimedClock.ofTimeProvider`: the twin clock exposes a `TimeProvider`, so debounce and throttle run on twin time
  (`docs/content/guide/testing.md:238`).
- `Signal.Peek`, `Memo.Peek` and `INode.Status`: `Debug.dump` reads values through these only. `Peek` is the untracked
  read of the cached value (`src/Ranvier/Core.fs:3010`, and for memos "without recomputing", `Core.fs:3645-3648`);
  `Status` carries the pending, error and uninitialised flags (`src/Ranvier/Types.fs:10-14`, `:155-157`).
- `Trace` queries, under `RANVIER_TRACE` only: a dump line names a node's trace path for follow-up queries (§4.6).

**View-owned state (new):**

- **The twin document** (`Ranvier.View.Twin`): DOM nodes, attributes, listeners, form-control state and expando
  slots. This is DOM state, which the rule assigns to the view; the core has no DOM.
- **The twin clock's queues**: microtasks, timers and animation frames. These model the host event loop, which has
  no core equivalent; the core's own scheduling (`batch`, `flush`, the dispatcher) runs unchanged inside it.
- **Expando tables on .NET** (`Native.fs`, §3.2): the per-node slots that Fable writes as JavaScript properties
  (`$$ranvier`, `$$ranvierScope`, `$$ranvierWidget`, `$$ranvier:<event>`). Same state as today, different storage.
- **The inspection registry** (`Inspect.fs`, compiled into Fable builds with `RANVIER_VIEW_INSPECT` and into every .NET build, §4.2): one weak entry per
  region start marker and per template instance root, holding closures that read region state the mount already
  keeps. It keeps no copy of keys, selections or statuses; each read goes to the region's own variables.

No view-side reconciler, key matcher, selection cache or commit queue is added. The queued commit `Queue`, which the
rule lists as under review (`.claude/rules/view-uses-core.md:37`), is reused as is, with the twin clock as its `post`.

## 1. Purpose

An agent changing a Ranvier view today has two loops (`docs/content/view/testing.md:5-7`):

- **HTML checks on .NET** (`Server.render` and a string compare): fast, but they cover first render only. No events,
  no commits, no arms switching, no keyed reconciliation, no hydration.
- **DOM checks under Node**: complete, but each change goes through `dotnet fable` of `DomTests.fsproj` (which links
  every Foundation and Portable source, `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj:11-102`) and then
  `node --test` with jsdom (`package.json:14`). Failures surface as JavaScript stack traces over generated code.

This spec adds:

- **A. The DOM twin.** Mounting, synchronous and queued commits, arms (`matchWith`, `branch`, `show`), `forEach` keyed
  reconciliation, boundaries, events, document head, hydration over server HTML and the Departures seam all run on
  .NET, in `dotnet test` and in a SageFs session, with a scripted interaction and assertion API and a deterministic
  clock. The twin serialises to HTML comparable with jsdom and with `Server.render`.
- **B. `Debug.dump`.** A text and JSON dump of a mounted view, on the twin and in a real client: elements, arm keys
  and the active arm, `forEach` keys, live signal and memo values read untracked, boundary states, departures in
  flight, document head entries and component instances.

### Success criteria

1. **Same code, two hosts.** The twin runs the Foundation and Portable sources that the client bundle runs. The twin
   reimplements the DOM only; no mount, region, hydration or departure logic is duplicated (§3.1).
2. **Conformance.** One shared suite of F# cases runs under jsdom (Fable) and on the twin (.NET). Every case passes on
   both, and the per-step artefacts (host HTML in jsdom form, dump text) are byte-equal between the two runs (§6.3).
3. **Bundle 0 B.** Client bundles that do not call `Debug.dump` are byte-identical before and after the change: the
   `sugar-size.mjs` minified entries hash equal at the merge base and on the branch (§7.1).
4. **Loop time.** Edit-to-result for one view check is measured on both loops; the twin loop in SageFs is the faster
   one by the factor recorded in §7.2 (no target is set before the phase 0 baseline exists).
5. **Dump non-interference.** A dump changes no core counter, observer count or trace event (§6.5).
6. **Dump stability.** The same view and actions give byte-equal dump text on .NET and under Fable, and across runs.

## 2. What the current code allows

### 2.1 Foundation compiles for .NET and does not run there

`Ranvier.View.Foundation.fsproj` targets `net10.0` (`:3`) and is part of `Ranvier.slnx`; it builds clean on .NET
(probe 2, Appendix A.2). Its DOM work reaches the browser through three channels:

| Channel | What it is | On .NET today |
|---|---|---|
| `Fable.Core.TS.Dom` interface calls (`node.appendChild`, `parent.insertBefore`, `element.setAttribute`) | .NET interfaces from `Xantham.Fable.Core.TS` 0.2.1 | Compile; run against any object implementing the interface |
| `[<Emit>]` helpers and `emitJsExpr` blocks | JavaScript text inlined at each call site | Throw: the body is `jsNative` |
| `[<Global>]` values (`document`, `globalThis`) | Module-level values | Throw at module initialisation |

Counts at `0a7fe326` (`grep -c "Emit("`, `grep -c emitJsExpr`, `[<Global`):

| File | `Emit` | `emitJsExpr` | `Global` | Notes |
|---|---:|---:|---:|---|
| `Foundation/Mounting.fs` (4168 lines) | 38 | 2 | 2 | delegation listener (`:131-139`); JS `Map`/`Set`/`WeakSet` (`:101-128`, `:222-239`, `:301-311`); array splice and slice (`:183-217`); `Range.deleteContents` (`:172-173`) |
| `Foundation/TemplateDom.fs` (1540) | 29 | 0 | 1 | node tests (`:156-170`); expandos (`:367-372`, `:727-728`); element snapshots for hydration rollback (`:748-760`); `event?type` (`:376`) |
| `Foundation/DocumentHead.fs` (389) | 13 | 1 | 0 | head queries (`:12-40`); `keepHead` with a `MutationObserver` path and a copy path (`:55-122`) |
| `Foundation/Hydration.fs` (314) | 7 | 0 | 0 | marker tests (`:26-48`); `getElementById` (`:297-301`) |
| `Foundation/Placement.fs` | 2 | 0 | 0 | `:222-227` |
| `Foundation/Prepared.fs`, `Microtask.fs`, `DomEvents.fs` | 0 / 0 / 1 | 0 | 1 / 1 / 0 | `queueMicrotask` (`Microtask.fs:6-10`); `addEventListener` (`DomEvents.fs:5-7`) |
| `Portable/Departures.fs` | 13 | 1 | 0 | `WeakMap`/`WeakSet` (`:55-74`); `setTimeout` (`:88-92`); `seam` is `null` on .NET (`:49-53`) |
| `Portable/Typed.fs`, `Portable/Template.fs`, `Foundation/Scheduling.fs` | 3 / 1 / 1 | 0 | 0 | each already has a `#else` .NET branch (`Typed.fs:93-137`, `Template.fs:248-254`, `Scheduling.fs:23-27`) |

Probe 2 confirms the failure mode: `Scheduling.Queue` constructs on .NET, and calling `Mounting.mount` raises
`TypeInitializationException: You've hit dummy code used for Fable bindings`. Mounting already carries six
`#if FABLE_COMPILER` / `#else` pairs for option comparisons (`Mounting.fs:622-626`, `:731-735`, `:980-987`,
`:1007-1019`, `:1113-1123`, `:1416-1423`), and TemplateDom one for aggregate errors (`TemplateDom.fs:226-236`); these
.NET branches compile today and have never run.

### 2.2 The existing seams, mapped

- **`Representation.Backend`** (`Portable/Representation.fs:643-725`) is the representation seam between view
  definitions and a renderer. It has three implementations: `HtmlBackend` (server, `:749`), `MountState` (client,
  `Mounting.fs:2165`) and the baseline `Browser.fs` interpreter (linked by `DomTests.fsproj` only, absent from the
  Foundation project's compile list). A twin written as a fourth `Backend` would reimplement `MountState`.
- **`TemplateDom.MountServices`** (`TemplateDom.fs:663-704`) is the seam between template materialisation and the
  mount: append, setup state, delegation, listeners, failure reporting, scope, placement, head, identity, hydration
  cursor and nested regions. It abstracts mount services, not DOM operations: `TemplateDom` writes the DOM directly
  through Emit helpers and interface calls (`:720-760`, `:1134-1144`). It is not the twin's seam.
- **The `Fable.Core.TS.Dom` interfaces** are the DOM seam that already exists. Every non-Emit DOM call in Foundation
  is an interface call, and the generated property writers cast to these interfaces on .NET
  (`Portable/Generated/HtmlTemplate.fs:4342-4345`: `(node :> Dom.HTMLInputElement).checked <- value`). Event handlers
  receive `unbox event` on .NET (`Typed.fs:131-137`, `Template.fs:2211-2212`).

### 2.3 Feasibility probes

- **Probe 1, DOM interfaces are proxyable** (Appendix A.1). Every `Fable.Core.TS.Dom` type tested is a .NET
  interface: `Node` (53 methods with inherited ones), `Element` (274), `HTMLElement` (546), `HTMLInputElement`
  (653, 15 inherited interfaces), `Document` (493), `Event` (25), `InputEvent` (34). `DispatchProxy.Create` returns an
  `HTMLInputElement` whose property setters arrive as `set_checked` and `set_value`, and the proxy also satisfies
  `:? ARIAMixin`. A twin node can therefore present itself as the exact interface the generated writers and user
  handlers cast to.
- **Probe 2, Foundation on .NET** (Appendix A.2): §2.1.

## 3. Design A: the DOM twin

### 3.1 Approach: run the real mount code on a .NET DOM (adopted)

The twin is a .NET implementation of the DOM subset Foundation uses, presented through the `Fable.Core.TS.Dom`
interfaces. Foundation's Emit helpers gain .NET bodies that call the same interfaces. Mounting, TemplateDom,
Hydration, DocumentHead, Placement and Departures then run unchanged on .NET.

**Rejected: a twin `Backend` that reimplements mounting.** `Mounting.fs` is 4168 lines holding the keyed pass
(increasing-run moves, `:314-352`), owned ranges, region validation, the boundary state machine (`:1667-2160`),
hydration adoption with rollback and the departure hooks. A reimplementation would diverge from the client on the
paths agents most need to see: reconciliation order, boundary reveal and hydration fallback. A conformance suite would
detect divergence; it would not prevent it.

**Rejected: abstracting the DOM behind a new F# interface used on both targets.** Replacing interface calls and Emit
helpers with calls through an F# `IDom` would change the Fable output and cost bundle bytes on every DOM operation.

**Divergence that remains.** The .NET bodies of the Emit helpers are a second implementation of about 105 one-line
JavaScript expressions and four `emitJsExpr` blocks. The one-liners are mechanical (`$0.nextSibling`, `new Map()`).
The four blocks are not: the delegated listener (`Mounting.fs:131-134`), its removal (`:136-139`), `keepHead`
(`DocumentHead.fs:55-122`) and the Departures seam object (`Departures.fs:49-50`). Five `[<Emit>]` helpers hold
statement blocks rather than one expression and are ported with the same care: `overlaps` (`Mounting.fs:147-148`),
`deleteBetween` (`:172-173`) and the hydration rollback snapshots `keepData`, `keepAttributes` and `keepElement`
(`TemplateDom.fs:747-760`). The conformance suite (§6.3) covers each with named cases.

### 3.2 `Native.fs`: one file of host primitives

A new first file of `Ranvier.View.Portable`, `Native.fs` (module `internal Ranvier.View.Foundation.Native`), holds
every host primitive that is now an Emit helper, `emitJsExpr` value or `[<Global>]` value in Portable and Foundation
and has no .NET body. Helpers that already carry a `#else` branch (`Typed.fs`, `Template.fs`, `Scheduling.fs`,
`TemplateDom.fs:226-236`) stay in their files. It lives in Portable because `Departures.fs` needs it; Portable's
`InternalsVisibleTo` list gains `Ranvier.View.Twin` and `Ranvier.View.Twin.Tests`.

- **Under `FABLE_COMPILER`**, each primitive is the current `[<Emit>]` declaration, moved verbatim. Emit bodies are
  inlined at their call sites, so the emitted JavaScript is unchanged; the byte-identity gate (§7.1) verifies this.
  Duplicates across files (`nextSibling` in four files, `remove()` in three) collapse into one declaration.
- **Under .NET**, each primitive is F# over the DOM interfaces, or over .NET collections:

```fsharp
module internal Ranvier.View.Foundation.Native

#if !FABLE_COMPILER
/// <summary>The host of the DOM globals on the current thread; the DOM twin installs one per document.</summary>
type IHost =
    abstract Document: Fable.Core.TS.Dom.Document
    abstract QueueMicrotask: (unit -> unit) -> unit
    abstract SetTimeout: (unit -> unit) * int -> obj
    abstract ClearTimeout: obj -> unit
    abstract RequestAnimationFrame: (float -> unit) -> obj
    abstract CancelAnimationFrame: obj -> unit
    /// <summary>The removal of the nodes strictly between two siblings, as <c>Range.deleteContents</c> performs it.</summary>
    abstract DeleteBetween: Node * Node -> unit

/// <summary>Installs <c>host</c> for the current thread until the returned value is disposed.</summary>
val install: IHost -> System.IDisposable
#endif

val document: unit -> Document              // Fable: [<Emit("document")>]
val nextOrNull: Node -> Node                // Fable: [<Emit("$0.nextSibling")>]
val isComment: Node -> bool                 // Fable: [<Emit("$0.nodeType === 8")>]
val expando: obj -> string -> obj           // Fable: [<Emit("$0[$1]")>]; .NET: ConditionalWeakTable
val setExpando: obj -> string -> obj -> unit
val map: unit -> NativeMap                  // Fable: new Map(); .NET: Dictionary with SameValueZero keys
val weakMap: unit -> NativeWeakMap          // Fable: new WeakMap(); .NET: ConditionalWeakTable
// … one entry per current Emit helper, grouped by node, expando, collection, array, event and timer primitives.
```

- **`document ()` replaces the module-level `[<Global>]` values** in `Mounting.fs:16-20`, `TemplateDom.fs:11-12`,
  `Prepared.fs:20-21` and `Microtask.fs:6-7`. Under Fable it emits the bare global, as today. On .NET it reads the
  installed host and raises `InvalidOperationException "No DOM host is installed on this thread; create a
  TwinDocument first"` without one. Module initialisation then succeeds on .NET.
- **Map and set keys use SameValueZero on .NET.** A JavaScript `Map` compares strings, numbers and booleans by value
  and every other key by reference; a .NET `Dictionary<obj, _>` with the default comparer compares F# records
  structurally. `Native` supplies a comparer with the JavaScript rule, so `rowMap` (`Mounting.fs:222-230`) behaves the
  same on both hosts.
- **The delegated listener is ported as one F# function** with the walk of `Mounting.fs:134`: start at the target or
  above the inner host recorded on the event, present each handled node as `currentTarget`, run a node's direct
  handler or dispatch to its template state, stop when `cancelBubble` is set or at the host. The twin event exposes a
  settable current target for it.
- **`keepHead` on .NET takes the copy path** of `DocumentHead.fs:89-95` (the path used when the document has no
  window). The twin document has no `MutationObserver`; jsdom runs the observer path. The conformance cases of
  `DocumentChecks.fs` cover both.
- **`Departures.seam` on .NET is an object** implementing `Seam`, not `null` (`Departures.fs:49-53`), and the registry
  tables and the 10 s safety timer use `Native.weakMap`, `Native.weakSet` and the host's timers.

`Native` has no public surface. Server rendering on .NET touches none of it: `Server.fs` and `HtmlBackend` use no
Emit helper, and Departures' tables are plain .NET collections that need no host.

### 3.3 Projects and placement

| Project | Targets | Contents |
|---|---|---|
| `experiments/Ranvier.View.Portable` (changed) | .NET, Fable | `Native.fs` added first |
| `experiments/Ranvier.View.Foundation` (changed) | .NET, Fable | Emit helpers replaced by `Native` calls; `Inspect.fs` and `Debug.fs` added (§4) |
| `experiments/Ranvier.View.Twin` (new) | .NET only | the twin DOM, the interface bridge, the HTML parser and serialisers, the clock, the session API |
| `experiments/Ranvier.View.Testing` (new) | .NET, Fable | selectors, interactions, expectations and the conformance case format, written against the DOM interfaces |
| `tests/Ranvier.View.Twin.Tests` (new) | .NET | Expecto: twin unit tests, and the conformance cases run on the twin |
| `examples/Fable.Ranvier.Playground/tests` (changed) | Fable | the conformance cases run under jsdom by a new `conformance.test.mjs` |

The twin and testing projects sit in `experiments/` beside the view and move with it when the view graduates, as
`Ranvier.View.Anime` does in the motion spec. Both are `IsPackable=false` until then. No Fable client project
references `Ranvier.View.Twin`.

### 3.4 The twin DOM

```fsharp
namespace Ranvier.View.Twin

[<RequireQualifiedAccess>]
type NodeKind = Element | Text | Comment | Fragment | Document

/// <summary>A node of a twin document. Each node has one bridged object, the value Foundation sees.</summary>
[<AbstractClass; AllowNullLiteral>]
type TwinNode =
    member Kind: NodeKind
    member Parent: TwinNode
    member FirstChild: TwinNode
    member LastChild: TwinNode
    member NextSibling: TwinNode
    member PreviousSibling: TwinNode
    member Document: TwinDocument
    /// <summary>The node as <c>Fable.Core.TS.Dom</c> sees it: the same object on every read.</summary>
    member Native: Fable.Core.TS.Dom.Node

and [<Sealed>] TwinElement =
    inherit TwinNode
    member LocalName: string
    /// <summary>The attributes in insertion order, as <c>element.attributes</c> lists them.</summary>
    member Attributes: (string * string) list
    /// <summary>The content fragment of a <c>template</c> element; null for every other element.</summary>
    member Content: TwinNode
    member Value: string        // input, textarea, select, option: the live value, with the dirty flag of HTML forms
    member Checked: bool
    member Selected: bool
```

- **Bridge.** One `DispatchProxy` subclass, `NodeProxy`, wraps each node; the proxy interface is chosen by kind and
  tag (`input` → `HTMLInputElement`, `template` → `HTMLTemplateElement`, `title` → `HTMLTitleElement`, any other HTML
  tag → the interface its generated constructor names in `Generated/HtmlTemplate.fs`, else `HTMLElement`; text →
  `Text`; comment → `Comment`). Calls dispatch through a table keyed by interface member, built once. Each entry
  converts between the binding's .NET types and twin values: `Node option` for nullable returns, `float` for numbers,
  `string option` for nullable strings. Event objects are proxies of the event interface their type implies
  (`click`, `dblclick`, `pointer*` → `PointerEvent`; `mouse*` → `MouseEvent`; `key*` → `KeyboardEvent`; `input`,
  `beforeinput` → `InputEvent`; `focus*` → `FocusEvent`; otherwise `Event`).
- **Members implemented** are those Foundation and Portable call. The current set, by `grep` over the mount sources:
  `parentNode`, `nodeType`, `firstChild`, `nextSibling`, `appendChild`, `data`, `setAttribute`, `childNodes`, `value`,
  `insertBefore`, `attributes`, `removeAttribute`, `remove`, `nodeName`, `head`, `lastChild`, `textContent`,
  `localName`, `content`, `createTextNode`, `createElement`, `ownerDocument`, `getAttribute`, `selected`,
  `querySelectorAll`, `prefix`, `cloneNode`, `checked`, `type`, `title`, `target`, `replaceChildren`,
  `removeEventListener`, `querySelector`, `innerHTML`, `createDocumentFragment`, `cancelBubble`, `addEventListener`,
  `replaceWith`, `replaceChild`, `removeChild`, `isConnected`, `importNode`, `hasAttribute`, `getElementById`,
  `defaultValue`, `contains`, `createComment`, plus `previousSibling`, `parentElement`, `children`, `id`, `className`,
  `tagName`, `outerHTML`, `click`, `focus`, `blur`, `dispatchEvent`, `preventDefault`, `defaultPrevented`,
  `stopPropagation`, `stopImmediatePropagation`, `currentTarget` and the form-control properties the generated
  `Props` modules write. A call to any other member raises
  `NotSupportedException "The DOM twin does not implement HTMLElement.offsetWidth"` with the interface and member.
- **Form controls** follow HTML's value modes: `value` reads the `value` attribute until the property is written,
  then the dirty value; `defaultValue` and `defaultChecked` reflect the attributes; `checked` and `selected` keep dirty
  flags. Hydration's property comparison (`TemplateDom.fs:744-745`, `:767-787`) depends on this split.
- **Connectedness.** `isConnected` is true for nodes under the document. Placed hooks and head entries observe it.

### 3.5 HTML parsing and serialisation

The twin parses HTML in three places: `template.innerHTML` during template compilation (`TemplateDom.fs:618-619`),
`UnsafeHtml` content slots (`TemplateDom.fs:735`) and server HTML for hydration.

- **Parser scope.** Tags, quoted and unquoted attributes, comments (the markers of
  `docs/content/server/rendering.md`), text, void elements, raw-text elements (`script`, `style`), RCDATA elements
  (`textarea`, `title`), the leading line feed dropped in `pre`, `listing` and `textarea`, `template` content
  fragments, numeric character references and the named references `amp`, `lt`, `gt`, `quot`, `apos` and `nbsp`. This
  covers all Server output: the server rejects markup the browser's parser would restructure
  (`docs/content/server/rendering.md`, "Markup the parser would change"), and its escaper writes only `&amp;`, `&lt;`,
  `&gt;`, `&quot;` and `&#13;` (`Representation.fs:277-283`).
- **Outside the scope**, the parser raises `TwinParseException` naming the construct and its line and column: an
  implied end tag (`<p>` closed by a block), foster parenting, a named reference outside the set, a `<table>` without
  `<tbody>`. A test that needs such markup runs under jsdom.
- **Two serialisers:**
  - `Html.outer` / `Html.inner`: the HTML fragment serialisation algorithm, as jsdom 30.1.1 produces it through
    parse5 8.0.1 and `entities` (text escapes `& < >` and U+00A0; attribute values escape `& "` and U+00A0;
    the Playground's `node_modules/entities/dist/escape.js:154-171`). Twin and jsdom host HTML compare byte for byte.
  - `Html.server`: the same tree written with `Html.escape`, Server's escaper (`Representation.fs:356`). For a
    parsed `Server.render` output `s`, `Html.server (parse s) = s` (§6.4).

### 3.6 Selectors

`Ranvier.View.Testing.Selector` is one F# selector engine over the DOM interfaces, used by the testing API on both
hosts and by the twin's own `querySelector` and `querySelectorAll`. jsdom and the twin then match identically.

- **Supported:** type, universal, `#id`, `.class`, `[attr]`, `[attr=v]`, `[attr~=v]`, `[attr^=v]`, `[attr$=v]`,
  `[attr*=v]` (quoted or bare values), `:checked`, `:disabled`, `:first-child`, `:last-child`, `:nth-child(n)`,
  `:not(simple)`, descendant and child combinators, and selector lists.
- **Text matching** is a separate query, not a pseudo-class: `Query.byText "Save"` returns the innermost element whose
  whitespace-normalised `textContent` equals the text.
- **Unsupported syntax** raises `SelectorException` with the selector and the column.

### 3.7 Events

- **Dispatch** follows the DOM's three phases: capture listeners from the root to the target's parent, target
  listeners, then bubble listeners when the event bubbles. `stopPropagation`, `stopImmediatePropagation`,
  `cancelBubble`, `preventDefault` (when `cancelable`) and `defaultPrevented` behave as in the DOM standard; the
  delegated listener reads `cancelBubble` (`Mounting.fs:134`).
- **Listener failures** are collected, as jsdom reports them to its virtual console: a raising listener does not stop
  the dispatch, and the dispatch returns normally. The conformance harness exposes the collected messages on both hosts
  and records them as an artefact. A session under `Reports.Raise` raises the first as `TwinListenerException` when the
  outermost dispatch ends; under `Reports.Collect` it adds each to `Session.Reports`.
- **Activation behaviour**, the only default actions modelled:
  - `click ()` on a checkbox or radio toggles `checked` before dispatch, restores it when the click is cancelled, and
    fires `input` then `change` when it is not;
  - `click ()` on a submit button inside a `form` fires `submit` on the form unless the click is cancelled;
  - `click ()` on a `label` with a control forwards a click to the control.
- **Focus** is a document-level active element: `focus ()` and `blur ()` fire `focus`, `focusin`, `blur` and
  `focusout` in the DOM order, and `:focus` is not a supported selector.
- **Scripted interactions** (§3.9) define each gesture as an explicit event sequence, so the same gesture produces the
  same events under jsdom.

### 3.8 The deterministic clock

```fsharp
namespace Ranvier.View.Twin

/// <summary>The twin's event loop: microtasks, timers and animation frames, run only when the test asks.</summary>
[<Sealed>]
type TwinClock =
    /// <summary>Milliseconds since the document was created.</summary>
    member Now: float
    /// <summary>A <c>TimeProvider</c> over <c>Now</c>, for <c>TimedClock.ofTimeProvider</c>.</summary>
    member TimeProvider: System.TimeProvider
    /// <summary>Queues a microtask; the <c>post</c> of <c>QueuedCommits</c> on the twin.</summary>
    member Post: (unit -> unit) -> unit
    /// <summary>Runs microtasks, including those they queue, until none remain; returns the number run.</summary>
    member RunMicrotasks: unit -> int
    /// <summary>
    /// Advances <c>Now</c> by <c>ms</c>, running each due timer in due order and each animation frame at every
    /// multiple of <c>FrameMs</c> crossed, with the microtask queue drained after each callback.
    /// </summary>
    member AdvanceBy: ms: float -> unit
    /// <summary>Runs the animation frames queued now, at <c>Now</c>, and drains microtasks.</summary>
    member Frame: unit -> unit
    member FrameMs: float with get, set   // 16 by default
    member Pending: ClockPending
    /// <summary>
    /// Pumps the graph's inbox, drains microtasks and runs zero-delay timers until all three are empty; raises
    /// <c>TwinRunawayException</c> naming the busiest queue after <c>maxRounds</c> rounds.
    /// </summary>
    member Settle: graph: Ranvier.Graph * ?maxRounds: int -> unit

and ClockPending = { Microtasks: int; Timers: int; Frames: int; NextTimerAt: float option }
```

- The clock is the twin host's `QueueMicrotask`, `SetTimeout` and `RequestAnimationFrame` (§3.2). Real time never
  advances a twin document.
- **Queued commits** mount with `QueuedCommits (failure, clock.Post)`: the existing mode takes `post` as a value
  (`Scheduling.fs:202-204`, `Mounting.fs:4136-4161`), so no scheduling code changes.
- **Async state** follows the core's .NET model: a `ManualDispatcher` graph and `Settle`, which calls `graph.Pump`.
  JavaScript promise interleaving is not emulated; `docs/content/guide/testing.md:214-225` already documents the
  difference between the two hosts.
- **Departures' safety timeout** (`Departures.fs:88-92`, 10 s) is a twin timer, so `AdvanceBy 10000.` finishes a held
  exit.

### 3.9 Session, interaction and assertion API

**Session (`Ranvier.View.Twin`, .NET):**

```fsharp
[<RequireQualifiedAccess>]
type Commits =
    | Synchronous
    | Queued of Ranvier.View.Foundation.Scheduling.CommitFailure

/// <summary>What a session does with the failures the mount passes to <c>report</c>.</summary>
[<RequireQualifiedAccess>]
type Reports =
    /// <summary>Collects them in <c>Session.Reports</c>.</summary>
    | Collect
    /// <summary>Raises <c>TwinReportException</c> from the call during which they arrived.</summary>
    | Raise

type TwinMount =
    {
        Commits: Commits
        Reports: Reports
        GraphOptions: Ranvier.GraphOptions      // a ManualDispatcher by default
        /// <summary>The host element's HTML; the view mounts at its end.</summary>
        Host: string
        /// <summary>The head's HTML before the mount.</summary>
        Head: string
    }

module TwinMount =
    val defaults: TwinMount   // Synchronous, Raise, ManualDispatcher, "<div></div>", ""

[<Sealed>]
type Session =
    member Document: TwinDocument
    member Host: Fable.Core.TS.Dom.HTMLElement
    member Graph: Ranvier.Graph
    member Clock: TwinClock
    member Mounted: Ranvier.View.Foundation.Mounting.Mounted
    member Reports: exn list list
    /// <summary>The host's children in jsdom's serialisation, markers included.</summary>
    member Html: unit -> string
    member Text: unit -> string
    /// <summary>Runs <c>action</c> inside the graph and settles the clock.</summary>
    member Act: action: (unit -> unit) -> unit
    member Dump: ?options: Ranvier.View.Foundation.Debug.DumpOptions -> string
    interface System.IDisposable   // disposes the mount, the graph and the document, in that order

[<RequireQualifiedAccess>]
module Twin =
    val mount: definition: Template.Component<'props> -> props: 'props -> Session
    val mountWith: TwinMount -> Template.Component<'props> -> 'props -> Session
    /// <summary>Mounts authored content through <c>Mounting.mountWith</c>.</summary>
    val mountView: TwinMount -> build: (Mounting.BuildContext -> unit) -> Session
    /// <summary>
    /// Renders with <c>Server.renderPage</c>, writes the body into the host and the head into the document, and
    /// hydrates with <c>Hydration.hydrateWith SynchronousCommits</c>, the one mode hydration supports.
    /// </summary>
    val hydrate: Server.Options -> Hydration.HydrationOptions -> Template.Component<'props> -> 'props -> Session
    /// <summary>Hydrates the given server HTML instead of rendering it.</summary>
    val hydrateHtml: TwinMount -> Hydration.HydrationOptions -> body: string -> Template.Component<'props> -> 'props -> Session
```

A twin document belongs to the thread that creates it, as a graph does. Tests run in parallel on separate threads,
each with its own document; the host is thread-static.

**Testing API (`Ranvier.View.Testing`, both hosts).** Every function takes the root node, so the same call runs on a
twin session (`session.Host`) and on a jsdom host:

```fsharp
module Ranvier.View.Testing.Query
val query: root: Node -> selector: string -> Element option
val queryAll: root: Node -> selector: string -> Element array
/// <summary>The single match; raises <c>QueryException</c> with the match count and a dump of <c>root</c> otherwise.</summary>
val get: root: Node -> selector: string -> Element
val byText: root: Node -> text: string -> Element

module Ranvier.View.Testing.Interact
/// <summary>Calls <c>click ()</c> on the single match: activation behaviour, then a bubbling <c>click</c>.</summary>
val click: root: Node -> selector: string -> unit
/// <summary>Fires <c>beforeinput</c>; unless it is cancelled, writes <c>value</c> and fires a bubbling <c>input</c>.</summary>
val input: root: Node -> selector: string -> value: string -> unit
/// <summary>Fires a bubbling <c>change</c>.</summary>
val change: root: Node -> selector: string -> unit
val check: root: Node -> selector: string -> isChecked: bool -> unit   // clicks only when the state differs
val select: root: Node -> selector: string -> value: string -> unit    // sets the select's value, then input and change
val key: root: Node -> selector: string -> key: string -> unit         // keydown, then keyup
/// <summary>Dispatches <c>spec</c> on the single match; returns false when a listener cancelled it.</summary>
val dispatch: root: Node -> selector: string -> spec: EventSpec -> bool

type EventKind = Plain | Mouse | Pointer | Keyboard | Input | Focus
type EventSpec = { Type: string; Kind: EventKind; Bubbles: bool; Cancelable: bool; Init: (string * obj) list }

module Ranvier.View.Testing.Expect
val text: root: Node -> selector: string -> expected: string -> unit
val count: root: Node -> selector: string -> expected: int -> unit
val attribute: root: Node -> selector: string -> name: string -> expected: string option -> unit
val value: root: Node -> selector: string -> expected: string -> unit
val html: root: Node -> expected: string -> unit                         // jsdom serialisation of root's children
```

A failed expectation raises `ExpectationException` whose message holds the selector, the expected and actual values
and a `Debug.dump` of the nearest enclosing instance, truncated to 40 lines. Under Fable the dump part appears only in
builds with `RANVIER_VIEW_INSPECT`.

**Conformance case format (both hosts):**

```fsharp
module Ranvier.View.Testing.Conformance

/// <summary>The host-specific operations a case uses: the document, the clock, and the artefact sink.</summary>
type Harness =
    abstract Platform: string                        // "jsdom" or "twin"
    abstract Document: Document
    abstract Post: (unit -> unit) -> unit            // QueuedCommits post
    abstract RunMicrotasks: unit -> unit
    abstract AdvanceBy: float -> unit
    /// <summary>Records a named artefact (host HTML, dump text) for the cross-host comparison.</summary>
    abstract Record: name: string * text: string -> unit

type Case = string * (Harness -> unit)
```

The jsdom runner (`conformance.test.mjs`) builds a `Harness` over jsdom and `node:test` mock timers, and writes the
artefacts to `generated-conformance/jsdom/`. The .NET runner builds one over a `TwinDocument` and writes
`generated-conformance/twin/`.

### 3.10 SageFs

The SageFs session loads `tests/Ranvier.View.Twin.Tests/Ranvier.View.Twin.Tests.fsproj`, which references the twin,
the testing library, Foundation, Portable, the core and the conformance cases. An agent loop is then:

```fsharp
open Ranvier.View.Twin
open Ranvier.View.Testing;;
let s = Twin.mount Board.view { Columns = sample };;
Interact.click s.Host "button.add";;
s.Dump ();;
Expect.count s.Host "li.card" 4;;
```

Editing a view file and running `hard_reset_fsi_session rebuild=true` reloads it. The twin avoids `use` inside
`task { }` so it runs under the SDK's `FSharp.Core` in a session (`AGENTS.md:74-78`, SageFs#141), and it reads no
path from `AppContext.BaseDirectory` (SageFs#142). A plain `.fsx` outside SageFs loads the same assemblies with `#r`
from the test project's output.

### 3.11 Out of scope for the twin

- Layout and geometry (`offsetWidth`, `getBoundingClientRect`, scrolling, `IntersectionObserver`, `ResizeObserver`):
  `NotSupportedException`. Playwright keeps them (`docs/content/view/testing.md:107-109`).
- CSS: no stylesheets, no computed style, no `:hover` or `:focus`.
- Real animation: anime-backed `Ranvier.Anime` calls are `[<Import>]` bindings (`src/Fable.Ranvier.Anime/Anime.fs:9-76`)
  and throw on .NET. The motion seam hooks (`Hooks.motion`, `Hooks.exit`, `Hooks.reflow`) run on the twin with plain
  callbacks, as `DepartureChecks.fs` writes them. A fake anime engine is deferred (decision 9).
- `MutationObserver`, `Shadow DOM`, custom elements, `iframe`, namespaces other than HTML (SVG elements parse and
  serialise as plain elements with their attributes; `SVGElement` interfaces are not bridged in phase 2).
- Browser behaviours jsdom also lacks: caret, selection ranges, IME.

## 4. Design B: `Debug.dump`

### 4.1 Where the state lives today

A dump needs state that the DOM does not show. At `0a7fe326` that state is in closures and private fields:

- list regions: the projection and the last pass's keys and rows (`Mounting.fs:3193-3216`);
- arm regions: the item memo, the selection memo, the arm owner and `armKey` (`Mounting.fs:3640-3656`);
- boundaries: `content`, `shown`, `waiting`, `revealed`, `failed`, `generation` and the held failures
  (`Mounting.fs:1700-1719`);
- template instances: the state array attached to delegated event nodes only (`TemplateDom.fs:1064-1071`);
- departures: `exits`, `leaving` and `pending` weak tables (`Departures.fs:55-74`);
- document head: the marked head elements, visible in the DOM through `data-ranvier-head`.

No registry reaches the first four, so a dump needs registration call sites in Mounting and TemplateDom. Call sites
cost bytes; the motion seam's flag tests alone cost 447 B gzip on the explicit entry (motion spec, §Bundle budget,
`2026-10-21-motion-design.md:323`).

### 4.2 Registration under `RANVIER_VIEW_INSPECT`

All registration code is inside `#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER`. A Fable build without the define
holds none of it; every .NET build holds it, since only the twin runs Foundation on .NET.

`Inspect.fs` (new, Foundation, after `Placement.fs`) keeps one weak table keyed by node:

```fsharp
module internal Ranvier.View.Foundation.Inspect

#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
[<NoEquality; NoComparison>]
type Probe =
    /// <summary>A list region: the keys of its laid-out rows, in order, and each row's first node.</summary>
    | ListProbe of path: InstancePath * rows: (unit -> (obj * Node) array)
    /// <summary>An arm region: the selected key and definition, or null when empty.</summary>
    | ArmProbe of path: InstancePath * selected: (unit -> struct (obj * TemplateComponent))
    | BoundaryProbe of path: InstancePath * state: (unit -> BoundaryProbeState)
    | HoleProbe of path: InstancePath
    /// <summary>A template instance: its definition, instance value, owner and path; keyed by its first root node.</summary>
    | InstanceProbe of TemplateComponent * instance: obj * owner: Ranvier.Owner * path: InstancePath
    /// <summary>A mount: its phase and commit mode; keyed by its range's start marker.</summary>
    | MountProbe of phase: (unit -> Phase) * queued: bool

and BoundaryProbeState =
    | Ready
    | Pending of waiting: int * revealed: bool
    | Failed of error: exn * generation: int

/// <summary>Registers <c>probe</c> for <c>node</c> until the returned cleanup runs.</summary>
val register: node: Node -> probe: Probe -> (unit -> unit)
val probeOf: node: Node -> Probe option
#endif
```

- Call sites: `ListRegion`, `ArmRegion`, `BoundaryRegion` and `HoleRegion` register at their start marker;
  `TemplateDom.bind` registers each instance at its first root; `MountState.Run` registers the mount at its range's
  start. Each cleanup joins the owner that already owns the region or instance, so a disposed region leaves the
  table when its owner does.
- The probe closures read the region's variables; they hold no copy. The table is a `WeakMap` under Fable and a
  `ConditionalWeakTable` on .NET, so a dropped node does not stay reachable through it.
- **Activation.** A Fable build receives the define as `--define RANVIER_VIEW_INSPECT`, as `test:dom` passes
  `RANVIER_VIEW_COUNTERS` today (`package.json:14`); the MSBuild property `RanvierViewInspect=true` adds it to
  `DefineConstants` for the project's own tooling. Debug configurations default it on, as `RanvierTrace` defaults
  traced in Debug (`docs/content/guide/tracing.md:31-46`); Release defaults it off (decision 3).

### 4.3 API

```fsharp
module Ranvier.View.Foundation.Debug

#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
type DumpOptions =
    {
        /// <summary>Rows shown per list; the rest are elided as one line with the elided keys' range.</summary>
        MaxRows: int                // 12: the first 9 and the last 3
        /// <summary>Characters of a text node or value before it is cut with its full length noted.</summary>
        MaxText: int                // 80
        /// <summary>Items shown of a collection value.</summary>
        MaxItems: int               // 5
        MaxDepth: int               // 64
        /// <summary>Total characters of the dump; the remainder is replaced by a count of omitted lines.</summary>
        MaxChars: int               // 65536
        Values: bool                // instance fields and their values
        Markers: bool               // region and row markers as comment lines
        Head: bool                  // the document head section
        NodeIds: bool               // core node ids and, under RANVIER_TRACE, trace paths
    }

module DumpOptions =
    val defaults: DumpOptions

/// <summary>A mounted view as data; the text and JSON forms are renderings of it.</summary>
[<NoEquality; NoComparison>]
type DumpNode =
    | Element of tag: string * attributes: (string * string) array * properties: (string * string) array * children: DumpNode array
    | Text of value: string * bound: bool
    | Instance of InstanceDump * children: DumpNode array
    | List of path: string * rows: (string * DumpNode array) array * elided: Elided option
    | Arm of path: string * key: string * definition: string * children: DumpNode array
    | Boundary of path: string * state: BoundaryDump * children: DumpNode array
    | Hole of path: string * children: DumpNode array
    | Leaving of DumpNode
    | Truncated of lines: int

and InstanceDump = { Name: string; Path: string; Hash: string; Fields: FieldDump array }
and FieldDump = { Name: string; Kind: string; Value: string; Status: string; Node: string }
and BoundaryDump = { State: string; Waiting: int; Reason: string }
and Elided = { Count: int; FirstKey: string; LastKey: string }

[<NoEquality; NoComparison>]
type Dump = { Mount: string; Root: DumpNode array; Head: (string * string) array }

/// <summary>The dump of <c>mounted</c>; raises <c>InvalidOperationException</c> during a flush or off the graph's thread.</summary>
val capture: options: DumpOptions -> mounted: Mounting.Mounted -> Dump
/// <summary>The dump of the nodes under <c>root</c>, with the probes registered on them.</summary>
val captureNode: options: DumpOptions -> graph: Ranvier.Graph -> root: Node -> Dump
val text: Dump -> string
/// <summary>JSON with <c>"schema": "ranvier-view-dump/1"</c>; keys in a fixed order.</summary>
val json: Dump -> string
/// <summary><c>text (capture DumpOptions.defaults mounted)</c>.</summary>
val dump: mounted: Mounting.Mounted -> string
#endif
```

`Mounted` gains an internal `Range` accessor (first and last node) for `capture`. `Session.Dump` on the twin calls
`Debug.dump`; in a client, an inspect build calls the same function, or exposes it on `window` from the app.

### 4.4 The text form

```text
mount r  phase=Attached  commits=queued  failures=0
  instance Board  path=r-0  hash=8fe1b6e4
    columns  memo    [|{Id=1; …}; {Id=2; …}; {Id=3; …}|] (3)
    adding   signal  false
    query    asyncmemo  pending
  <div class="board">
    <h2>
      "Cards: "
      ="4"
    list  path=r-0.r0  rows=4
      row 1
        instance Card  path=r-0.r0.0  hash=1c9e0a42
          title  signal  "Write spec"
        <li class="card" data-id="1">
          ="Write spec"
      row 2  leaving
        <li class="card" data-id="2">
      … 2 rows (keys 3 … 4)
    arm  path=r-0.r1  key="edit"  def=EditPanel#a1b2c3d4
      <form>
    boundary  path=r-0.r2  state=pending  waiting=1
      <p>
        "Loading"
head
  title  key=title  "Board (4)"
  meta   key=description  name="description" content="Four cards"
```

- **Order** is DOM order; instance fields are in declaration order (F# record fields on .NET, own enumerable property
  order under Fable, the same order).
- **Names.** An instance's name is its instance value's type name; the hash is `Template.structuralHash`
  (`Template.fs:1409`), the arm hash the server writes. Minified Fable builds mangle type names; an inspect build is a
  development build, and the hash identifies the definition in every build.
- **Values.** Strings are quoted and cut at `MaxText`; numbers use `Template.numberText` (`Template.fs:52-110`), the
  ECMAScript form on both hosts; options, lists and arrays show `MaxItems`; records show field names; other objects
  show their type name. A bound text node is written `="…"`, a static one `"…"`.
- **Status.** A field holding a core node shows `pending`, `error: <message>` or its peeked value, from `Status` and
  `Peek`. An uninitialised memo shows `uninitialised`.
- **Elision.** Lists show the first 9 and last 3 rows by default; the elided rows are one line with their count and
  the first and last elided key.

### 4.5 Reading without subscribing

- `capture` runs under `Api.untrack` and reads core nodes only through `Peek` and `Status`. It never reads `Value`,
  `TryValue` or `TrackStatus`, so it creates no edge, runs no memo and starts no async flight.
- Instance fields are read as stored values: F# record fields, anonymous record fields and tuple items on .NET through
  `FSharp.Reflection`, own properties under Fable. Class instances show public properties whose values are core nodes;
  their getters run untracked.
- `capture` raises `InvalidOperationException "Debug.dump ran inside a flush"` when called during a view pass of any
  mount it walks (a keyed reconcile, an arm switch, a boundary's reveal or teardown, a hole's render, or a queued
  commit flush), counted by a pass depth each mount keeps under the inspect define. It raises
  `InvalidOperationException "Debug.dump ran off the graph's thread"` when `Graph.IsOnGraphThread` is false.
  `Trace.dumpText` (`src/Ranvier/TraceApi.fs:278-302`) also detects a core flush through the trace log and the
  internal `Graph.CurrentComputation`; the core exposes neither to the view, so a dump called from inside a core
  computation that is not a view pass is not detected and reads the last-pass values that `Peek` returns. A core-wide
  check is a core change and needs its own decision.
- Region probes read the region's variables, which are last-pass values. A region marked stale but not yet
  recomputed shows its last pass and the line suffix `stale`.

### 4.6 Relation to tracing

`Trace` answers graph questions: why a node ran, why not, where it came from, what the owner tree holds
(`docs/content/guide/tracing.md:17-30`). `Debug.dump` answers view questions: which rows, arm and boundary state are
on screen and which values they show. The two connect through node identity:

- With `NodeIds = true`, each field line adds the core node id; under `RANVIER_TRACE` it adds the trace path
  (`Trace.snapshot`, `TraceApi.fs:227`), so `Trace.why graph node` follows from a dump line.
- `Debug.nodeAt (mounted, "r-0.columns")` returns the field's `INode` for `Trace` queries.
- A dump records nothing in the trace log, and `Trace.label` is not called by it; the field name is the dump's label.
- The JSON form sits beside `Trace.dumpText`'s JSONL as a second machine format, with its own schema name.

## 5. Error handling

| Failure | Behaviour |
|---|---|
| A DOM member outside the twin's set | `NotSupportedException` naming the interface and member |
| HTML outside the parser's scope | `TwinParseException` with the construct, line and column |
| Selector syntax outside §3.6 | `SelectorException` with the selector and column |
| A query that requires one match and finds 0 or several | `QueryException` with the selector, the count and a dump excerpt |
| A failed expectation | `ExpectationException` with expected, actual and a dump excerpt |
| A mount report under `Reports.Raise` | `TwinReportException` carrying the reported exceptions, raised from the `Act`, interaction or mount call during which they arrived |
| `Settle` exceeding `maxRounds` (default 10 000) | `TwinRunawayException` naming the queue that kept refilling and its last callback's origin |
| A `Native` global read with no host on the thread | `InvalidOperationException` naming `TwinDocument` |
| A twin document used from another thread | `InvalidOperationException`, as the graph's thread guard does |
| `Debug.capture` during a view pass or off-thread | `InvalidOperationException` (§4.5); a dump inside a core computation that is not a view pass is not detected |
| A listener that raises during a dispatch | collected; under `Reports.Raise`, `TwinListenerException` when the outermost dispatch ends (§3.7) |
| `Debug` called from a Fable build without the define | compile error: the module is absent, as `Trace` queries are in untraced builds (`tracing.md:77-78`) |

## 6. Testing strategy

### 6.1 Twin unit tests (`tests/Ranvier.View.Twin.Tests`, Expecto)

Tree operations and their invariants; the bridge's type conversions for each member family; form-control value modes;
event phases, cancellation and activation; the clock's ordering (microtasks after each callback, timers by due time
then insertion, frames on `FrameMs` boundaries); parser and serialiser round trips; selector matching.

### 6.2 Bridge coverage

The bridge records every member it serves during the .NET conformance run. The run fails when a member in the record
has no implementation, and the recorded set is committed as `Ranvier.View.Twin/members.txt`; a change to the set shows
in review.

### 6.3 Shared conformance suite

- **Cases.** The existing Playground check modules move onto `Ranvier.View.Testing`, file by file, replacing their
  `[<Global "document">]` values with `harness.Document`: `RangeChecks`, `TemplateChecks`, `TemplateCatalogueChecks`,
  `DelegationChecks`, `BoundaryChecks`, `PendingRegionChecks`, `PlacementChecks`, `DocumentChecks`, `HydrationChecks`,
  `DepartureChecks`, `SugarChecks`, `OwnedAsyncChecks` and `UnsafeHtmlChecks`. Each module exports `cases: Case array`.
  Modules that measure (`*Perf.fs`, `*Bench.fs`) stay jsdom-only, and so does `DomChecks`, which checks the `Dom` module
  of `src/Fable.Ranvier` rather than the view sources.
- **Host queues.** Both harnesses own a post queue: `Post` appends to it and `RunMicrotasks` drains it, so queued
  commits run at the same step on both hosts. Under jsdom, `AdvanceBy` ticks `node:test` mock timers. JavaScript
  promise continuations are outside both queues; the `OwnedAsyncChecks` cases that await a promise carry the `[jsdom]`
  tag.
- **Runs.** `npm run test:conformance` compiles the cases with Fable and runs them under jsdom; `dotnet test
  tests/Ranvier.View.Twin.Tests` runs the same cases on the twin.
- **Comparison.** Each case records the host HTML after every interaction and, in inspect builds, the dump text. A
  CI step compares `generated-conformance/jsdom/` with `generated-conformance/twin/` file by file and fails on the
  first byte difference, printing a unified diff.
- **Divergence cases** named for each hand-ported block: delegated dispatch with nested hosts, `stopPropagation` in a
  handler and in a native listener between target and host (`docs/content/view/events.md:57-68`); head restore after a
  failed hydration (`keepHead`); held exits across reorder, clear and disposal; SameValueZero keys with structurally
  equal records.
- **Platform-specific cases** carry a `[jsdom]` or `[twin]` tag in their name and are skipped on the other host, with
  the reason in the case; the list is reviewed with each phase.

### 6.4 Server parity

For every fixture in `Portable/Checks/{ServerFixtures,SugarFixtures}.fs`: `Server.render` on .NET, parse on the twin,
then `Html.server` of the parsed tree equals the rendered string; `Twin.hydrate` with `Strict = true` reports
nothing and keeps the server's nodes (the hydrated button is the same object as the parsed one, as
`docs/content/view/testing.md:103-105` checks under jsdom).

### 6.5 `Debug.dump`

- **Golden texts** for the Board, keyed list, arm, boundary (ready, pending, failed), hole, departure and head
  fixtures, committed under `tests/Ranvier.View.Twin.Tests/dumps/`, compared on the twin and, in the inspect build,
  under jsdom; the two hosts' texts are byte-equal.
- **Non-interference**: with `RANVIER_COUNTERS`, `Counters.Snapshot ()` is equal before and after `capture`; every
  probed node's observer count is unchanged; under `RANVIER_TRACE`, the trace log holds no new event; a stale memo is
  still stale after the dump.
- **Elision and truncation**: a 1000-row list dumps 12 rows and one elision line; `MaxChars` ends the text with the
  omitted line count.
- **JSON**: the JSON form parses, and its fields match the text form for every golden fixture.

## 7. Measurement plan

### 7.1 Bundle: 0 B

The repository measures bundles with `benchmarks/frameworks/sugar-size.mjs`: Vite builds of the `explicit`,
`explicit-combined`, `sugared`, `members`, `class` and `combined` entries, minified and unminified, reported raw,
gzip and Brotli, with sugar detected by module region in the unminified builds
(`sugar-size.mjs:1-5`, `:21-31`, `sugar-size.config.mjs`). Gates:

1. **Byte identity.** For each minified entry, the SHA-256 of `bundle.js` at the merge base equals the branch's.
   Phase 1 (`Native.fs`) and phase 5 (`Inspect`, `Debug`) each pass it with the define off.
2. **Absence.** The unminified `explicit` and `combined` bundles contain no `Inspect`, `Debug` or `Probe`
   declarations, checked as `frameCheck` checks sugar names.
3. **Generated JavaScript.** The Fable output of `DomTests.fsproj` at the merge base and on the branch differs only in
   files for `Native` itself and import lines; a diff script lists the differing files.
4. **Inspect cost, reported.** A new `inspect` entry (the `combined` rows built with `--define RANVIER_VIEW_INSPECT`,
   calling `Debug.dump`) reports its delta over `combined`, raw, gzip and Brotli. It is not a budget; the number goes
   into `docs/research/2026-10-21-dom-twin.md`.

`Ranvier.View.Twin` is never referenced by a Fable project, so it adds no bytes to any client bundle by construction.

### 7.2 Loop time

Phase 0 records the baseline; phase 6 records the twin. Both on the same machine, median and spread of 10 runs, cold
and warm:

| Loop | Steps measured |
|---|---|
| jsdom | edit a fixture; `dotnet fable tests/DomTests.fsproj` (one-shot and `watch` incremental); `node --test` of one check file |
| twin, `dotnet test` | edit; `dotnet test tests/Ranvier.View.Twin.Tests --filter` one case |
| twin, SageFs | edit; `hard_reset_fsi_session rebuild=true`; evaluate the case |
| twin, SageFs, no edit | evaluate one interaction and one dump in a warm session |

Per-case execution time is also recorded for a 1000-row keyed mount, a full reorder and a hydration of the Board
fixture, twin against jsdom; the bridge's per-call overhead is measured separately (decision 1 names the threshold).

## 8. Risks

1. **Hand-ported JavaScript diverges** (most likely). The delegated listener, `keepHead` and the Departures seam are
   rewritten in F# for .NET. Mitigation: the divergence cases of §6.3 and the artefact diff; each block's JavaScript
   and F# sit next to each other in `Native.fs`, so a change to one is visible in the same diff.
2. **Never-run .NET branches.** The `#else` branches already in `Mounting.fs` and `Template.fs` (§2.1) first execute
   on the twin; a latent bug there reads as a twin bug. The conformance suite surfaces these in phase 2.
3. **Platform semantics outside the view.** Number formatting, string comparison, integer overflow and promise
   ordering differ between .NET and JavaScript. Framework code is covered by conformance; user view code that formats
   floats with `string` can render differently on the twin. The docs page lists the known differences.
4. **`Native.fs` changes Fable output.** Moving Emit helpers between modules keeps call sites inlined, but a moved
   module-level value (`delegatedEvents`, `claims`, `sharedListeners`) becomes an import from another module.
   Gate 1 of §7.1 catches any byte; if values cannot move without a byte change, they stay in their files with a
   local `#if`.
5. **Bridge overhead.** `DispatchProxy` dispatch is a reflective call per DOM operation. For tests this is expected to
   stay small against the reactive work; if the 1000-row measurement shows the bridge dominating, generated bridge
   classes replace the proxies (decision 1).
6. **jsdom version drift.** The `Html.outer` serialiser follows jsdom 30.1.1's escaping. A jsdom upgrade that changes
   serialisation fails the artefact diff, which is the intended signal.
7. **Inspect builds drift from release builds.** Registration call sites exist only with the define, so a bug in a
   call site shows in Debug and inspect builds only. The conformance suite runs under jsdom both with and without the
   define.

## 9. Decisions

The user approved each of these on 2026-10-21.

1. **Bridge implementation.** The bridge is one `DispatchProxy` subclass over the Xantham interfaces (probe 1); it
   needs no generator change. Generated bridge classes replace it only if §7.2 shows the bridge above a quarter of a
   case's time.
2. **HTML parser.** The twin has its own subset parser, which raises `TwinParseException` outside its scope. Server
   output lies within that scope by construction. AngleSharp may serve later as a test-only oracle for the parser; it
   is not a dependency.
3. **`RANVIER_VIEW_INSPECT` default.** On in Debug, off in Release, as `RanvierTrace` is. The size gates build Release.
4. **`Native.fs` placement.** Portable, internal, with `InternalsVisibleTo` for Foundation, the twin and the twin's
   tests.
5. **Conformance migration.** The existing Playground check modules move onto the harness, one module per change. Each
   move keeps the module's jsdom run green before its twin run is added, and the module's old `*.test.mjs` runner is
   deleted once its cases run in `conformance.test.mjs`.
6. **Animation frames.** Frames run on `Frame ()` and on `AdvanceBy` at each `FrameMs` boundary crossed, as
   Playwright's `page.clock.runFor` does in the motion spec (`2026-10-21-motion-design.md`, Testing §4).
7. **Component names in dumps.** An instance is named by its instance value's type name and its definition's
   structural hash. `Component.named` is added only if dumps in inspect builds prove unreadable.
8. **Report policy default.** `Reports.Raise`, so an unexpected failure ends the step during which it arrived. Tests of
   failure paths opt into `Reports.Collect`.
9. **A fake anime engine.** Deferred until after phase 6, as `#if !FABLE_COMPILER` bodies in `Fable.Ranvier.Anime`
   that record calls and complete on the twin clock, 0 B under Fable by construction. Until then, plain-callback hooks
   cover the motion seam.
10. **Packaging.** `Ranvier.View.Twin` and `Ranvier.View.Testing` stay unpublished while the view is in
    `experiments/`; apps in the repository reference the projects.

### Rulings made while planning

- **`DomChecks` stays jsdom-only.** It checks `src/Fable.Ranvier`'s `Dom` module, which is not a view source and keeps
  module-level `[<Global>]` values. `RangeChecks` takes its place in the first conformance set.
- **The harness and both runners land in phase 2**, with the first three modules; phase 3 adds queries,
  interactions, expectations, the artefact diff and the next five modules.
- **Already-dual helpers and module-level values stay in their files** (§3.2, risk 4): the module-level tables `delegatedEvents`, `claims`, `sharedListeners` and `hostCounts` keep their place with .NET initialisers; the `[<Global "document">]` values do move, to `Native.document ()`.
- **Call sites call `Native.<name>` directly.** A file-local inline alias makes Fable bind arguments to temporaries, which changes the emitted JavaScript (found in phase 0).
- **`PackagingChecks` exempts `Native.fs`** from its no-document-Emit rule and checks its structure instead: every Emit inside a `#if FABLE_COMPILER` block with an `#else` defining the same names, pending the user's objection.
- **The dump's flush check covers the mount's commit flush only** (§4.5).
- **Hydration is synchronous on the twin**, as on the client: `hydrateWith` raises `NotSupportedException` for
  `QueuedCommits` (`Hydration.fs:143`).

## 10. Phased implementation outline

Each phase lands with its gates green and is reviewable alone.

0. **Baseline and probes.** Record the jsdom loop times (§7.2) and the `sugar-size.mjs` hashes at the merge base.
   Port `Hydration.fs`'s seven Emit helpers into a draft `Native.fs` and show gate 1 holds for them. Copy Appendix A's
   probes and results into `docs/research/2026-10-21-dom-twin.md`.
1. **`Native.fs`.** Move every Emit helper, `emitJsExpr` value and `[<Global>]` value of Portable and Foundation;
   write the .NET bodies; give `Departures.seam` a .NET object. Gates: §7.1 gates 1 and 3; the full jsdom suite;
   Foundation's .NET module initialisation succeeds with no host.
2. **Twin DOM.** Nodes, bridge, form controls, events, the subset parser, both serialisers, selectors, the clock, and
   `Twin.mount` with synchronous commits, the conformance harness, `conformance.test.mjs` and the .NET runner. Tests:
   §6.1, §6.2; the first conformance modules (`RangeChecks`, `TemplateChecks`, `DelegationChecks`) on both hosts.
3. **Testing library and artefact diff.** `Ranvier.View.Testing` queries, interactions and expectations; the
   artefact recording and its diff in CI. Move `BoundaryChecks`, `PlacementChecks`,
   `SugarChecks`, `TemplateCatalogueChecks`, `UnsafeHtmlChecks`.
4. **Queued commits, async, head, hydration, departures.** `Commits.Queued` on the clock; `Settle` with a
   `ManualDispatcher`; `Twin.hydrate`; §6.4 server parity. Move `DocumentChecks`, `HydrationChecks`,
   `PendingRegionChecks`, `OwnedAsyncChecks`, `DepartureChecks`.
5. **Inspect and `Debug.dump`.** `Inspect.fs` and its call sites under the define, `Debug.fs`, `Session.Dump`,
   `Debug.nodeAt`, expectation messages with dump excerpts. Tests: §6.5 on both hosts; §7.1 gates 1, 2 and 4.
6. **Agent workflow and docs.** SageFs session instructions; `docs/content/view/testing.md` gains "Twin checks on
   .NET" and "Dumping a view"; `docs/content/guide/tracing.md` links the dump; the research note records §7.2.
7. **Follow-ons, each on its own decision.** Generated bridge classes (decision 1), a fake anime engine
   (decision 9), `SVGElement` bridging.

## Appendix A. Probes

Both ran on 2026-10-21 with .NET SDK 11.0.100-rc.1 `dotnet fsi`, against `Fable.Core` 5.3.0,
`Xantham.Fable.Core` 0.2.0 and `Xantham.Fable.Core.TS` 0.2.1 from the NuGet cache.

### A.1 DOM interfaces and `DispatchProxy`

```fsharp
#r "nuget-cache/fable.core/5.3.0/lib/netstandard2.0/Fable.Core.dll"
#r "nuget-cache/xantham.fable.core/0.2.0/lib/net8.0/Xantham.Fable.Core.dll"
#r "nuget-cache/xantham.fable.core.ts/0.2.1/lib/net8.0/Xantham.Fable.Core.TS.dll"
open System
open System.Reflection
let asm = typeof<Fable.Core.TS.Dom.Node>.Assembly
for n in [ "Node"; "Element"; "HTMLElement"; "HTMLInputElement"; "Event"; "InputEvent"; "EventTarget"; "Document" ] do
    let t = asm.GetTypes () |> Array.find (fun t -> t.FullName = "Fable.Core.TS+Dom+" + n)
    let all = t.GetInterfaces ()
    let methods = (t :: List.ofArray all) |> List.sumBy (fun i -> i.GetMethods().Length)
    printfn "%s interface=%b inherits=%d methods(incl inherited)=%d" n t.IsInterface all.Length methods
type P() =
    inherit DispatchProxy()
    override _.Invoke(m, _) =
        printfn "invoked %s" m.Name
        if m.ReturnType = typeof<Void> then null
        elif m.ReturnType.IsValueType then Activator.CreateInstance m.ReturnType
        else null
let p = DispatchProxy.Create<Fable.Core.TS.Dom.HTMLInputElement, P> ()
p.``checked`` <- true
p.value <- "x"
printfn "%A" ((p :> Fable.Core.TS.Dom.Node) :? Fable.Core.TS.Dom.ARIAMixin)
```

Output:

```text
Node interface=true inherits=1 methods(incl inherited)=53
Element interface=true inherits=8 methods(incl inherited)=274
HTMLElement interface=true inherits=13 methods(incl inherited)=546
HTMLInputElement interface=true inherits=15 methods(incl inherited)=653
Event interface=true inherits=0 methods(incl inherited)=25
InputEvent interface=true inherits=2 methods(incl inherited)=34
EventTarget interface=true inherits=0 methods(incl inherited)=4
Document interface=true inherits=8 methods(incl inherited)=493
invoked set_checked
invoked set_value
true
```

### A.2 Foundation on .NET

`dotnet build experiments/Ranvier.View.Foundation/Ranvier.View.Foundation.fsproj -c Release`: 0 warnings, 0 errors.
Then, referencing the build output:

```fsharp
let tryIt name f =
    try f () |> ignore; printfn "%s: ok" name
    with e ->
        let rec root (e: exn) = if isNull e.InnerException then e.Message else root e.InnerException
        printfn "%s: %s: %s" name (e.GetType().Name) (root e)
tryIt "Scheduling.Queue" (fun () ->
    let g = new Graph ()
    Scheduling.Queue (g, Scheduling.CollectAndContinue, (fun f -> f ())) |> box)
tryIt "Mounting.mount" (fun () ->
    let g = new Graph ()
    Mounting.mount g None ignore Unchecked.defaultof<_> None ignore |> box)
```

Output:

```text
Scheduling.Queue: ok
Mounting.mount: TypeInitializationException: You've hit dummy code used for Fable bindings. This probably means you're compiling Fable code to .NET by mistake, please check.
```
