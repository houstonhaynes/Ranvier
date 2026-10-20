# Server HTML and hydration — design

Date: 2026-10-19
Branch: `poc/fable-ranvier-dom`
Baseline: `7ffa5f9` (template boundaries, pending registration and handler failures implemented)
Plan: [Task 9](../plans/2026-10-06-fable-ranvier-framework.md#task-9--server-html-and-hydration-follow-on), open item 7. Contract C09 ([brief](2026-10-06-fable-ranvier-framework-brief.md#c09--portability-ssr-and-hydration)); risks G02, G09, G11, G12, G13 ([variants and risks](2026-10-06-fable-ranvier-framework-variants-and-risks.md)).
Status: approved 2026-10-19. The user agreed the `CreateId` decision (a positional instance path plus a per-region row counter, held by the view); the other decisions marked (review) stand as written.
Amended after approval, by the implementation's review rounds:
- `695515c`: What happens today. The markers sentence names the two row cases the hydration markers add: a row with no roots, and two rows meeting text to text.
- `b872d0b`: Malformed HTML normalisation. The parser rules gain `select` and `optgroup` children, nested `option`, nested list and description items, nested headings and nested buttons.
- `5094c96`: Mismatch policy. A root start marker without its end marker, and a host without a root marker, mount the root fresh at the end of the host.
- `8806e25`: Boundaries and Decision 8. Pending `[c` content binds the server nodes, holds them detached, and shows an inert copy of them as the pending view.
- `8b8339d`: Testing. Test 32's names map to `editedInputsKept`, `formsHydrate` and `emptyRcdataText`.
- `af3b462`: Testing. Test 34, the journeys.
- `c1c0296`: Boundaries and Decision 8. Pending content is decided at the end of `hydrate`; ready content keeps its nodes; focus inside detached content is lost; each `[c` boundary's server nodes are cloned once.
- `42d4c30`: Boundaries. Pending content is decided at the end of the root mount's build.
- The final review's fixes: out of scope gains several roots per document; Testing notes that test 30's authored root cannot be built, and adds test 35.

## Core and view state

Required by `.claude/rules/view-uses-core.md`.

**Core primitives used:**

| Primitive | Job in this design |
|---|---|
| `Graph`, `GraphOptions` (`ThreadAffinity = Guarded`) | One graph per server request. Every signal, memo, async value and owner created during the request belongs to it. Disposed when the request's render returns or throws. |
| `Api.createRoot`, `Owner`, `Api.runWithOwner`, `Api.onCleanup` | The request root; one child owner per component instance, per row and per boundary attempt on the server; the same owners as a mount on the client during hydration. |
| `MountLifetime` (`Lifetime.fs`, over core owners) | Child-first teardown and error collection for the server's component owners, as on the client. |
| `Api.untrack` | Server slot reads and setups run untracked: one read per slot, no subscription. |
| `NotReadyException` | What a pending read is, on the server as on the client. |
| `Projection.ensureUniqueKeys` | The server's duplicate-key check, with the core's exception and message. |
| `createProjection*`, `createMemoWithComparer`, `createEffect` | Unchanged client regions: a hydrated list region runs the same projection, a hydrated arm the same memo, a hydrated slot the same render effect. Hydration changes only where their first pass finds its nodes. |
| `AsyncValue.Settled`, `createAsyncSource` | Not used by the renderer. Named in [State transfer](#state-transfer) as the application's means to seed values before hydration. |

**View-owned state:**

| State | Why the core cannot hold it |
|---|---|
| The server plan of a definition: static markup segments interleaved with slot, region, child and placeholder operations | Template tables and server HTML. |
| The structural hash of a definition, written into arm markers | Template tables. |
| The content-model table used to reject markup the HTML parser would restructure | Server HTML. |
| The hydration cursor: the next server node a mount adopts, and the logical-sibling walk over region markers | DOM nodes and their layout. |
| Hydration markers: root, region, boundary-view and arm markers, empty-text and zero-row placeholders, row separators | DOM nodes. |
| The positional instance path behind `Setup.CreateId`, and each region's row counter | **Agreed by the user, 2026-10-19** ([Decision 3](#decisions)). The core has no instance identity that is equal on .NET and under Fable and independent of creation order. The core alternative is an owner path id, which has the same ordering problem described in [Deterministic identity](#deterministic-identity). |

## Purpose and boundaries

Two deliverables, in order:

1. **Server HTML.** A request renders a template component to an HTML string on .NET (and synchronously under Fable on Node), with escaped text and attributes, serialised portable properties, deterministic identity, request-local scopes and explicit pending/error output. Its first content is indexable without client JavaScript.
2. **Hydration.** The client attaches a template component's bindings, listeners, regions and boundaries to that server output, adopting its nodes instead of cloning skeletons, with an explicit mismatch policy.

Out of scope: streaming, event replay before hydration, selective or lazy hydration (separate later proposals); hydration of authored `Program`/`Tree` views outside templates; queued-commit hydration; the raw-HTML escape hatch (G13), whose later design inherits the [marker constraint](#raw-html); several hydrated roots in one document, whose first `hydrate` removes the unadopted head elements served for the later roots; any change to `src/Ranvier`.

## What happens today

Read from the code at `7ffa5f9`:

- **Two server writers.** `Representation.Html.render policy build` interprets authored views through a string-building `Backend`. `Template.renderHtml` writes a template instance with `writeMarkup` in `Server` mode, the function that also produces the DOM skeleton, so markers and static escaping agree. Region rows and boundaries render back through the `Html.render` backend.
- **Per-render tree walk.** `writeMarkup` calls `flatten`, which builds an F# list of every node, on every instance render. Static segments are recomputed for every row.
- **Markers.** A region writes `<!--[-->` rows `<!--]-->`; adjacent text nodes are separated by `<!--!-->`; a boundary writes `<!--[-->` and the chosen view `<!--]-->`. Rows carry no markers of their own: a row with no roots writes nothing, and two rows meeting text-to-text are not separated ([Hydration markers](#hydration-markers) adds both). Authored `Keyed`/`Switch` in `Html.render` write no markers.
- **Escaping.** `Html.escape` replaces `&`, `<`, `>`, `\r` (and `"` for attributes) with five chained `Replace` calls, allocating on every call. Attribute names are validated (`validateAttribute` rejects `on*`, `srcdoc`, uppercase); raw-text elements are rejected.
- **Properties and browser parts.** `rejectBrowser = true` (policy `Reject`) raises on any property, property slot, listener or browser hook; `Omit` drops them. No property has an HTML meaning.
- **Lifetime.** `Html.render`'s `Component` runs its setup, then its cleanups immediately after its subtree is written; cleanup after completion raises. No graph or owner exists: a setup that calls `Api.createSignal` fails unless the caller activated a graph.
- **Boundaries.** `Html.render`'s `Boundary` renders the content into a buffer; `NotReadyException` writes the pending view, any other exception the error view with `Retry = ignore`. The content's setups have already run and cleaned up.
- **Text edge cases.** A text slot whose value is empty writes nothing, so the parsed HTML has no node where the skeleton has one. `Html.render` doubles a leading line feed in `pre`/`textarea`/`listing`; `writeMarkup` does not. A void element's children are dropped without error.
- **Packaging.** The portable files (`Representation.fs`, `Scope.fs`, `Lifetime.fs`, `Typed.fs`, `Template.fs`, `Generated/HtmlTemplate.fs`) compile on .NET (`RepresentationChecks.fsproj`), but every project referencing them also references `src/Fable.Ranvier`, whose `Dom.fs` calls `document`. `Typed.fs` contains `listenDirect`, an `Emit` of `addEventListener`.
- **Shared definition state.** `TemplateComponent.ArmReader` is assigned lazily by `t.arm`, which runs inside region readers, so two concurrent server requests can assign it at once. `TemplateDefinition.Compiled` is DOM-only.

## Where the server renderer lives

**Decision 4 (review).** The split is portable versus DOM, in two projects:

- `experiments/Ranvier.View.Portable/`: `Lifetime.fs`, `Scope.fs`, `Representation.fs`, `Typed.fs` (without `listenDirect`), `Template.fs`, `Generated/HtmlTemplate.fs`, and the new `Server.fs`. References `src/Ranvier`, `Fable.Core` and `Xantham.Fable.Core.TS` (type bindings for the typed native constraints; no call into them is reachable). It does not reference `src/Fable.Ranvier`.
- `experiments/Ranvier.View.Foundation/` keeps the DOM: `TemplateDom.fs`, `Mounting.fs`, `Browser.fs`, `Prepared.fs`, `Microtask.fs`, `Scheduling.fs`, `Binding.fs`, `Generated/Html.fs`, `Components.fs`, `Authoring.fs`, plus the new `Hydration.fs`. It references the portable project and receives `listenDirect`.

The server renderer is part of the portable project: it is portable code with no browser dependency, and a third project would duplicate the reference set without isolating anything further. A server application references `Ranvier.View.Portable` only; a shared view module compiles against the same assembly on the server and from source under Fable on the client.

A packaging test fails when the portable project references `Fable.Ranvier` or compiles a file containing `Emit` code that touches `document`, `window` or `addEventListener`.

## Server rendering

### Entry point

```fsharp
module Server =
    type Options =
        { Policy: BrowserPolicy          // Reject or Omit, as today
          IdPrefix: string               // root prefix for Setup.CreateId; must match the client's
          GraphOptions: GraphOptions }   // ThreadAffinity Guarded by default

    /// Renders a template component as one hydratable root; raises when a read outside every boundary is pending or fails.
    val render: Options -> Component<'props> -> 'props -> string
    val renderTo: Options -> Component<'props> -> 'props -> (string -> unit) -> unit
```

`render` creates the request graph, activates it, creates the request root, renders, then disposes the root and the graph in a `finally`. The output is wrapped in a root marker pair (`<!--[r-->` … `<!--]-->`). `Html.render` stays for authored views and tests; its backend becomes the renderer of authored content inside view holes.

### Server plan

Each definition compiles once into a `ServerPlan`: an array of operations over the flattened template, with adjacent static markup merged into one string.

| Operation | Writes |
|---|---|
| `Static of string` | Tags, static attributes, static text, `<!--!-->` separators, region markers of static kind. |
| `Text of Slot` | The escaped value, or the empty-text placeholder. |
| `Attribute of string * Slot` | ` name="value"`, or nothing for `None`. |
| `Property of PortableProperty * Slot` (or a static value) | The property's serialisation, or nothing / an error under the [fallback](#portable-properties-and-the-browser-only-fallback). |
| `Region of int * Region` | Region markers and rows. |
| `Enter of child index` / `Leave` | Inlined child setup under its own owner; restores the enclosing instance. |

The plan is published on the definition with `Interlocked.CompareExchange`: concurrent first renders may each build a plan, one is kept, and both are equal. `ArmReader` is published the same way. The plan replaces `writeMarkup`'s `Server` mode; `skeletonLayout` keeps the `Skeleton` mode. The equivalence test pins the plan's output byte-for-byte against today's writer for every template fixture before the markers below change it.

### Escaping

- Text: `&` → `&amp;`, `<` → `&lt;`, `>` → `&gt;`, `\r` → `&#13;`.
- Attribute values, always double-quoted: the text set plus `"` → `&quot;`.
- One scan finds the first character to escape; a string with none is returned unchanged with no allocation. Otherwise one `StringBuilder` (or one array join under Fable) writes the result.
- Attribute and tag names are validated at definition time, as today. No name is written unvalidated.
- Escaped text can never form a comment, so text can never forge a marker.
- `pre`, `textarea` and `listing` get a second line feed when their first child text starts with one, in the plan as in `Html.render`.
- Output is byte-identical on .NET and under Fable for the escaping corpus.

### Portable properties and the browser-only fallback

A property slot's writer is an opaque `Func<obj, obj, unit>`. The portable form attaches serialisation metadata where the part is constructed:

```fsharp
type PropertySerialization =
    | AsAttribute of name: string * write: Func<obj, string option>  // value → attribute value, None omits
    | AsChildText                                                    // textarea value
    | BrowserOnly
type PortableProperty = { Write: Func<obj, obj, unit>; Html: PropertySerialization }
```

`SlotTarget.PropertyTarget` and `Part.StaticProperty` carry a `PortableProperty`. The catalogue generator (`tools/Ranvier.Dom.Generate`) emits the metadata for reflected properties from a table it owns: `className` → `class`, `id`, `title`, `hidden`, `htmlFor` → `for`, `tabIndex`, `disabled`, `required`, `readOnly`, `multiple`, `checked` → `checked` presence, `value` (input, button, data, option…) → `value`, `selected` → `selected` presence, `textarea.value` → child text, `aria*` → `aria-*`. Every other property is `BrowserOnly`. A handwritten `propertyStatic`/`propertyWith` is `BrowserOnly` unless its caller passes the metadata.

Fallback, under the existing `BrowserPolicy`:

- `BrowserOnly` property, listener, `Browser` hook: `Reject` raises naming the element and the part; `Omit` writes nothing. Listeners and hooks are never invoked on the server under either policy.
- `select.value` is `BrowserOnly`; a server-visible selection is written through `option`'s `selected`.
- A serialised property whose attribute the same element also writes statically raises at plan compile time: one element, one source for each attribute.

### Deterministic identity

`Setup` gains `CreateId: unit -> string`, valid during setup only (as `OnCleanup` is on the server today). An id is `prefix` + the instance's positional path + a per-instance counter, for example `a-0.c1.r0.3-0`:

- the root instance's path is empty;
- an inlined child's step is `c` + its index in the definition's child list;
- a region row's step is `r` + the region's index + `.` + the row's creation index in that region.

The path depends on definition structure and row order only, not on setup order. Server setup order differs from client setup order (the server sets up an inlined child when the writer reaches it; `materialise` sets up every inlined child before any region), so a single counter would diverge. Rows created on the client after hydration continue their region's counter, and every client-created path ends in a step the server never issued, so ids do not collide. The path is computed only when `CreateId` is called; the instance stores its parent and step in the setup context it already receives, with no per-row allocation when `CreateId` is unused. **Decision 3 (review).**

Keys remain the application's; the renderer adds no identity to them.

### Request-local scopes and concurrent requests

- One `Graph` per `render`. Every node a setup creates belongs to it; nothing the renderer creates outlives the call.
- Each component instance, inlined child, row and boundary attempt runs under its own `MountLifetime` child owner of the request root. Cleanups run child-first when the root is disposed at the end of the render, or when a boundary attempt is abandoned.
- Setup runs untracked; slot readers run untracked, once each.
- Core effects created during setup run as the core defines them, inside the request graph, and are disposed with it. Browser work belongs in `Browser` hooks and listeners, which the server never runs (G11).
- A signal captured from outside the request (module level, or another graph) is shared state (G02). The renderer reads it as it is; the concurrency tests pin the observed core behaviour for each `ThreadAffinity`, and the documentation names it.
- Under Fable on Node, the graph's activation is a module global, so `render` is supported synchronously only.

### Pending and errors against boundaries

Server output fits the [boundary design](2026-10-19-view-boundaries-design.md): the content when every read under the boundary is ready, the pending view when one is pending, the error view when one fails.

- A boundary renders its content under a fresh attempt owner into a buffer. On success the buffer is written. On `NotReadyException` the attempt owner is disposed, the buffer dropped, and the pending view rendered. On any other exception the attempt owner is disposed and the error view rendered with `{ Error = error; Retry = ignore }`. A failure inside the pending or error view goes to the enclosing boundary.
- The start marker records the chosen view: `<!--[c-->` content, `<!--[p-->` pending, `<!--[e-->` error.
- A pending read outside every boundary makes `render` raise `ServerPendingException` (carrying the core's `NotReadyException`), after the request root is disposed. Any other failure outside every boundary propagates after disposal. This matches Decision 3 of the boundary design: a failure with no boundary throws.
- **Decision 1 (review).** The server renders in one synchronous pass. Data needed for first content is loaded by the application before `render` and passed as props or seeded sources; a boundary whose data is not ready emits its pending view. A settle-and-render mode (await the request's in-flight async values, then render once) is a later phase that needs a core primitive to await a graph's in-flight values; streaming is a separate proposal.

### Malformed HTML normalisation

The HTML parser restructures some markup: `tr` directly in `table` gains an implied `tbody`, text inside table parts is foster-parented, a block element closes an open `p`, `a` inside `a` and `form` inside `form` are split, a `select` drops a child other than `option`, `optgroup` and `hr`, an `optgroup` a child other than `option`, and an `option` inside an `option` closes the outer one (parser-version dependent). An `li` inside an open `li`, and a `dd` or `dt` inside an open `dd` or `dt`, closes the outer item, also through `div`, `span` and other non-special elements; a heading as the direct child of a heading closes it; a `button` inside a `button` closes the outer one. The client never parses rows in context: it clones each row's skeleton from a template element and appends it. Server HTML is parsed in context, so these cases diverge between server and client.

The renderer rejects them instead of correcting them:

- Static nesting is checked once, when the server plan compiles, against a content-model table derived from the tree-construction rules above. A violation raises with the element path.
- Dynamic nesting (a region's row roots inside the region's parent element) is checked once per region and row definition pair, on first render, and cached in the plan.
- Void elements with children and `template` elements raise at plan compile time.
- Raw-text elements stay rejected, as today.

### Forms

- `input` `value` and `checked`: the attribute parts write the default; the live property parts serialise to the same attribute through their metadata.
- `textarea`: the value is the element's escaped child text, with the leading line-feed rule.
- `select`: the selected `option` carries `selected`; `select.value` is browser-only.
- `button`/`input` inside `form`: written as authored; the content-model check covers nested forms.

## Hydration markers

| Marker | Meaning |
|---|---|
| `<!--[r-->` … `<!--]-->` | A hydratable root. The client locates it in the host; whitespace outside it is ignored. |
| `<!--[-->` … `<!--]-->` | List region or view hole (unchanged). |
| `<!--[a:HASH-->` … `<!--]-->` | Arm region (`matchWith`, `branch`); `HASH` is the selected arm definition's structural hash. |
| `<!--[c-->`, `<!--[p-->`, `<!--[e-->` … `<!--]-->` | Boundary, with the view the server chose. |
| `<!--!-->` | Separator between adjacent text nodes (unchanged), and between two rows when one ends and the next starts with text. |
| `<!--_-->` | An empty text slot; the client replaces it with an empty text node. |
| `<!--ranvier:row-->` | A row with no root nodes; the same comment the client creates for such rows. |

A start marker is any comment whose data begins with `[`; the end marker is `]`. Markers nest. The structural hash is FNV-1a over a canonical serialisation of the definition's skeleton markup (tags, static attributes, static text, marker kinds), computed identically on .NET and under Fable and cached with the plan.

Markers are always written. Region markers exist today; the additions cost a few bytes per arm, boundary and empty text slot.

### Raw HTML

When the raw-HTML escape hatch is designed, its content must not contain a comment beginning with `[` or equal to `]`, or the logical walk below loses its place. That design will either reject such content on the server or delimit raw regions by length.

## Hydration

### Entry point

```fsharp
val hydrate: graph: Graph -> parentOwner: Owner option -> report: (exn list -> unit)
             -> options: HydrationOptions -> host: Node -> definition: Component<'props> -> props: 'props -> Mounted
type HydrationOptions = { IdPrefix: string; Strict: bool }
```

The result is an ordinary `Mounted`: disposal, later updates and every list, arm and boundary operation behave as for `mountWith SynchronousCommits`. `QueuedCommits` hydration raises `NotSupportedException`. The root must be a template component.

### Adoption

Hydration runs the template path with a cursor instead of a clone:

- `materialise` in hydration mode takes the next `RootCount` logical siblings from the cursor as the instance's roots, then runs the definition's existing walk program over them with a logical `NextSibling`: at a start marker it moves to the matching end marker. A region therefore occupies two logical positions, as in the skeleton, and the compiled walk is reused unchanged.
- Each collected node is checked as `validateParsed` checks a parsed skeleton: element tag, text node (or the `<!--_-->` placeholder, replaced by an empty text node), region start marker of the expected kind.
- `MountServices.Append` claims the node for the mount's range without moving it. The range's entries, delegation and claims are those of an ordinary mount.
- Static properties are written. Render and isolated effects start as in a mount; their first commit reads the DOM before writing: text writes only when `node.data` differs, attributes only when `getAttribute` differs, serialised properties only when the server-written attribute differs from the client's serialisation (**Decision 5 (review)**), browser-only properties always. The slot's previous value is the client's value in every case.
- Listeners attach and delegated events register on the host, as in a mount. Events dispatched before hydration completes are lost (no replay).

### Regions

- **List rows.** The region's projection runs its first pass as in a mount; each row's materialise adopts from the cursor between the region's markers instead of the staging fragment. A `<!--!-->` row separator is removed when found between rows. After the last row the cursor must be at the end marker. A zero-root row adopts its `<!--ranvier:row-->` comment.
- **Arms.** The arm memo selects as in a mount. The start marker's hash must equal the selected definition's hash, then the arm adopts its rows.
- **View holes.** The content between the markers is removed and the view mounted fresh. Authored `Program`/`Tree` views are not hydrated.
- **Boundaries.**
  - `[c` and the content's reads are ready on the client by the end of the root mount's build: the content hydrates in place and keeps its nodes; no pending phase.
  - `[c` and a content read is still pending on the client at the end of the root mount's build: the content's one setup binds the server nodes, which are then held detached; a copy of the server nodes, taken before the content's first commits, stays between the markers as the boundary's inert pending view; the reveal replaces the copy with the bound nodes. Content whose read is settled by a `mounted` hook is detached at the end of the build and revealed within the same `hydrate`.
  - `[p`: the pending view hydrates in place; the content mounts detached and reveals as usual.
  - `[e`: the nodes between the markers are removed and the boundary mounts fresh, with a new content setup.

### Mismatch policy

**Decision 2 (review).**

- **Value differences** (text, attribute and serialised property values, list items with equal row shape) are corrected by the first commit and not reported.
- **Structural differences** (a missing or different element, a wrong region kind, a different arm hash, extra or missing rows, a missing root marker) raise a `HydrationMismatchException` carrying the logical path, the expected node and the found node. It is caught at the nearest enclosing region, reported once through `report`, the region's server nodes between its markers are removed, and the region mounts fresh. A mismatch outside every region clears the root marker pair's content and mounts the root fresh.
- A root start marker without a matching end marker is a structural mismatch at `#k` (the first unclosed start marker among the root's logical positions) or at `root`. Every node after the root start marker is the root's content: it is removed, and the root mounts fresh at the end of the host. Foreign siblings after an unclosed root marker are removed with it. A host without a root marker mounts the root fresh at the end of the host, after its children.
- `Strict = true` raises the exception from `hydrate` instead, after disposing what was created. Tests use it.
- Structure is replaced by region, not patched node by node.

### Differing selections

Server and client select different arms when their state differs (time, locale, a browser-only read). The arm hash detects a different definition, including definitions whose shapes coincide; the region re-renders under the mismatch policy. Two arms of the same definition with different payloads hydrate in place, and the payload's slot values are corrected by the first commit. A list whose items differ but whose row count matches hydrates positionally: each adopted row is the client's row for the client's item, and its slot values are corrected.

## State transfer

State transfer is explicit and application-owned.

- The server writes application state with `Server.stateScript id json`: `<script type="application/json" id="…">`, with `<` written as `\u003c`, and U+2028/U+2029 as `\u2028`/`\u2029`. The application chooses the serialiser.
- The client reads it with `Hydration.readState id` (the element's `textContent`) and passes the decoded value as the root's props, or writes it into sources before `hydrate`.
- An async value the server resolved before rendering is seeded on the client by the application: a `createAsyncSource` settled with the transferred value, or a prop. A seeding option on `createAsync`, if one is wanted, belongs in the core.
- Exceptions are not transferred; an `[e` boundary re-runs on the client.

## Decisions

1. Decision: one synchronous server pass; pending boundaries emit their pending view, and data for first content is loaded before `render` — it keeps the renderer free of async waiting and of a core addition, and indexability is the application's explicit choice of what to load — if wrong, data-driven pages index their loading views until a settle-and-render phase and a core await primitive are added. **(review)**
2. Decision: value mismatches are corrected silently by the first commit; structural mismatches are reported once and re-render the nearest region (or the root); `Strict` raises — React-style trust in matching output without its cost of whole-root fallback, and no node-level patching to maintain — if wrong, silent value correction hides state-transfer bugs, or region re-renders lose focus and scroll in the replaced region. **(review)**
3. Decision: `Setup.CreateId`, valid during setup, derived from a positional instance path and a per-region row counter held by the view — server and client set up instances in different orders, so only a structural path agrees, and the core has no identity independent of creation order — if wrong, the view keeps identity state without the agreement `.claude/rules/view-uses-core.md` requires, or a core owner id would be needed and changes the API of `Setup`. **(review)**
4. Decision: two projects, portable (including the server renderer) and DOM; no separate `Ranvier.View.Html` project; `Xantham.Fable.Core.TS` stays a type-only reference of the portable project — the server renderer has no dependency the portable model lacks, and the typed catalogue needs the native element types — if wrong, a later package split moves files again, and server deployments carry the TS bindings assembly. **(review)**
5. Decision: during hydration a serialised property is written only when the server-written attribute differs from the client's serialisation, so a control the user edited before hydration keeps the edit — fewer writes, and no lost typing without event replay — if wrong, the application's state and the control disagree until the next write or input event, where overwriting would have shown the application's value. **(review)**
6. Decision: hydration markers are always written; no non-hydratable output mode — one output format to test, and the added bytes are small — if wrong, static-only pages carry comments they never need.
7. Decision: arm start markers carry an FNV-1a structural hash of the selected definition — definitions have no identity shared across .NET and Fable, and shape checks alone accept two arms with equal shape and different static text — if wrong, hash collisions hydrate a wrong arm (bounded by also checking shape), or hash computation adds plan-compile cost.
8. Decision: boundary start markers record the chosen view; server content with a pending client shows a copy of the server nodes as the pending view; a server error view re-renders on the client — the error object cannot be transferred, and keeping server content avoids a flash of the loading view — if wrong, content shows server values until reveal with no interactivity, and is then replaced; focus inside it is lost when hydration detaches the bound nodes; each `[c` boundary's server nodes are cloned once per hydration.
9. Decision: an empty text slot writes `<!--_-->` — the parser creates no node for empty text, and the walk needs one node at that position — if wrong, one comment per empty slot in output.
10. Decision: a separator `<!--!-->` between rows whose boundary is text-to-text, removed during hydration — the parser merges adjacent text — if wrong, a removal per such boundary during hydration.
11. Decision: zero-root rows emit `<!--ranvier:row-->` on the server — the client already uses that comment, so adoption is uniform — if wrong, none beyond a fixed comment string becoming protocol.
12. Decision: markup the parser restructures is rejected, statically at plan compile and dynamically once per region and row pair, not auto-corrected — corrections such as inserting `tbody` would produce a server DOM the client does not create — if wrong, authors must write `tbody` and similar explicitly, and the rule table needs maintenance as the HTML spec changes.
13. Decision: void elements with children raise instead of dropping the children — dropped content is silent data loss — if wrong, existing fixtures that rely on dropping break.
14. Decision: portable property metadata is attached where the part is constructed, emitted by the catalogue generator, not looked up by writer identity — writers are shared closures without stable identity, and the generator already knows each property — if wrong, handwritten property parts are browser-only until their authors add metadata.
15. Decision: per-request `Graph` with `Guarded` affinity, and core effects created in setup run as the core defines — no new graph mode, and effect semantics stay one contract — if wrong, effects with external side effects (timers, analytics) run once per request; a deferred-effect graph option would be a core change.
16. Decision: a pending or failing read outside every boundary makes `render` raise after disposing the request — matches the client's failure without a boundary — if wrong, a page with one unguarded async read returns no HTML at all.
17. Decision: server `BoundaryFailure.Retry` is a no-op — the server has one pass — if wrong, none; an error view that calls `Retry` during render does nothing.
18. Decision: server plans and `ArmReader` publish through `Interlocked.CompareExchange` — concurrent first renders build equal values, so the first published wins without a lock — if wrong, duplicate plan builds on cold start only.
19. Decision: hydration supports `SynchronousCommits` only — queued mounts expand templates through the default expansion, not the compiled path hydration extends — if wrong, applications using queued commits mount client-side only until queued hydration is designed.
20. Decision: only template components hydrate; view holes and authored content re-render on the client — authored backends write no row markers, and templates are the production path — if wrong, authored-view pages pay a client render of their holes.
21. Decision: state transfer is an application-supplied JSON script with `<` escaped; no automatic serialisation of async values — global constraints forbid implicit retention, and a view-side value cache would duplicate core async state — if wrong, applications write the seeding code for every server-loaded value.
22. Decision: server rendering under Fable on Node is synchronous only — the core's activation is a module global under Fable, so interleaved async requests would share it — if wrong, Node servers cannot await inside a render until the core's Fable activation changes.
23. Decision: hydration's first commit compares DOM text and attributes before writing — a read and compare is cheaper than a write with its mutation and style invalidation — if wrong, one read per slot more than a plain write when most values differ.

## Testing

.NET tests in the portable test project (`RepresentationChecks` moves to reference `Ranvier.View.Portable`); jsdom tests in `examples/Fable.Ranvier.Playground/tests/` (`ServerChecks.fs`, `HydrationChecks.fs`, registered in `DomTests.fsproj` and in `server.test.mjs` / `hydration.test.mjs`); Chromium tests in `hydration.spec.mjs`. Every server fixture runs on .NET and under Fable and must produce byte-identical output.

Pinned before any change:

1. `renderHtml` output for every existing template fixture, as golden strings ("server plan matches the current writer").
2. Today's empty-text, void-children and `tr`-in-`table` output, recorded so the diff shows each change.

Server HTML:

3. "escape corpus is byte-identical on .NET and Fable": `<`, `&`, `>`, `\r`, `"`, `'`, surrogate pairs, `<!--]-->` in text, `</script>` in attributes.
4. "escape returns the input string when nothing needs escaping" (reference equality on .NET).
5. "attribute and tag names outside the validated set fail at definition time" (`onclick`, `srcdoc`, uppercase, empty).
6. "serialised properties write their attributes; browser-only properties raise under Reject and write nothing under Omit".
7. "listeners and browser hooks are never invoked on the server" (recording hooks, both policies).
8. "a property and a static attribute writing the same attribute fail at plan compile".
9. "textarea value and pre text keep a leading line feed after parsing" (Chromium round-trip).
10. "two concurrent requests render their own props": two threads, a `Barrier` inside setup interleaves them; each output contains only its own values; each request graph is disposed.
11. "a module-level signal read by two requests is shared": pins the documented behaviour for `Guarded` and `Unchecked` affinity.
12. "setup failure disposes the request": cleanups registered before a later sibling's throw run once, child-first; the graph has no live owner; the exception propagates.
13. "a boundary's abandoned content attempt runs its cleanups before the pending view is written" and the same for the error view.
14. "a pending read outside every boundary raises ServerPendingException after disposal".
15. "boundary markers record content, pending and error views"; "a failure inside the pending view goes to the enclosing boundary".
16. "CreateId is equal on server and client for inlined children before and after regions, nested rows and arms"; "CreateId after setup raises".
17. "content-model violations fail at plan compile": each rule in the table, with its element path; "a row definition with tr roots inside a table region fails on first render".
18. "void elements with children and template elements fail at plan compile".
19. "duplicate keys fail the server render with the core's exception".

Hydration (jsdom unless noted; each with `Strict = true` for the matching case):

20. "hydrating server HTML adopts every node": node identity before and after `hydrate` is equal for elements, text, markers; no node is created.
21. "a hydrated template updates, dispatches delegated and direct events, and disposes like a mount".
22. "hydration writes no text or attribute when values match" (MutationObserver records none) and "corrects differing values without a report".
23. "empty text slots hydrate through their placeholder and update".
24. "empty, one-node, multi-root and fragment templates hydrate"; "zero-root rows hydrate and keep their place".
25. "list regions hydrate, then reorder, append, remove and clear like mounted lists", including nested regions and text-adjacent row separators.
26. "arms hydrate under an equal hash; a different selected arm re-renders the region and reports once"; "two arms with equal shape and different static text are distinguished by hash".
27. "a list with fewer or more client items than server rows re-renders the region and reports once".
28. Boundaries: "content hydrates in place when ready", "server content stays as the pending view until reveal", "a server pending view hydrates and the content reveals", "a server error view re-renders the boundary".
29. "a structural mismatch outside every region re-renders the root"; "Strict raises and leaves the host as the server wrote it".
30. "queued-commit hydration raises NotSupportedException"; "an authored root raises".
    - "An authored root raises" has no test: `hydrate` takes a `Component<'props>`, which is always a template, so an authored root cannot be passed.
31. "state script round-trips JSON containing </script> and U+2028".
32. Forms (Chromium): "an input edited before hydration keeps its value when the server and client values agree"; "checkbox, textarea and select hydrate their server state"; "focus inside a hydrated input survives hydration".
    - The first and third names are `editedInputsKept` ("inputs edited before hydration keep their values, focus and selection when the server and client values agree"), and the second is `formsHydrate` ("forms hydrate a leading line feed, checked and selectedIndex without writes, and write the client's differing values"). Both run in Chromium through `hydrationCases`.
    - The empty text slot in an RCDATA element (preflight ruling C14, test name `textareaEmptySlot`) is `emptyRcdataText` ("an empty text slot in a textarea the server left empty hydrates into a created text node"), in Chromium through `hydrationCases`.
33. Normalisation (Chromium): for each content-model rule, the rejected markup parses differently from the client DOM, and every accepted fixture's server HTML parses to the client DOM's serialisation.
34. Journeys (jsdom, and Chromium through `hydrationCases`), each on one host and one graph, with the visible DOM and the exact reports asserted at every step: `hydrationJourney` (events and two-way input, row moves, a region mismatch reported once and recovered by an update, disposal, then a second render hydrated cleanly with its own row counter), `retryJourney` (pending content fails into its error view, and a retry continues the row counter), `pendingListJourney` (a list update while `[c` content is pending leaves the inert copy untouched), `recoveryJourney` (a re-rendered root takes interaction and disposes, then a second render hydrates cleanly) and `headJourney` (head entries adopted, withdrawn at disposal, and adopted afresh in the next round).
35. Rollback retention: "a returned hydrate holds no inverse of its edits, nested cursors included, and a raised one has run them all" (jsdom, through the DOM test build's counters); "a view hole's removed server nodes are collected while the hydrated mount is live" and "a recovered list's removed server rows are collected while the hydrated mount is live" (Chromium, a `WeakRef` after a forced collection); "a failure removing the unadopted head elements disposes the mount and leaves the host and the head as served".

Mutation checks before review: removing the logical-sibling skip, the arm hash check, the empty-text placeholder, the row separator, the region-level mismatch catch, the attempt-owner disposal and the escape fast path must each fail a test.

## Performance

First content:

- .NET (BenchmarkDotNet, beside `SnapshotBench`): render time and allocated bytes per render of the component fixture at 1/16/128 rows and the keyed fixture at 1k/10k rows, before phase S1 (today's writer) and after each server phase. HTML bytes per row reported.
- Node, Fable-compiled: the same fixtures against Solid 2 (`@solidjs/web` 2.0.0-rc.14, already installed) `renderToString` with the compiler's SSR output. Ripple has no server renderer; no adapter.
- Counter scenarios for server render join the CI instruction counters.

Hydration (Chromium, `run.mjs` protocol: shuffled rounds, forced GC before each sample):

- hydrate 1/16/128 component rows and 1k/10k keyed rows, against Ranvier's own mount of the same view and against Solid 2's `hydrate` of its own server output;
- time from `hydrate` call to the first handled click;
- retained heap after hydration against after mount (target: equal within noise);
- target: hydration time at or below mount time for every fixture, since adoption replaces node creation. Other ratios are reported, not gated.

## Phases

Server HTML:

- **S0 — Project split and pins.** Create `Ranvier.View.Portable`, move the portable files and `listenDirect`, repoint references; packaging test; golden pins (tests 1–2).
- **S1 — Server plan and escaping.** `ServerPlan` with merged static segments, `CompareExchange` publication, escape fast path; tests 3–5; first-content benchmark baseline and after.
- **S2 — Request scope and identity.** `Server.render` with request graph, root, per-instance owners, disposal on success and failure; `Setup.CreateId`; tests 10–12, 16, 19.
- **S3 — Portable properties and forms.** `PortableProperty` metadata in the generator and parts; fallback under both policies; textarea, select, checkbox, leading line feed; tests 6–9.
- **S4 — Boundaries, markers and normalisation.** Boundary attempts with owner disposal and view markers, `ServerPendingException`, root/arm/placeholder/separator markers and the structural hash, content-model and void checks; tests 13–15, 17–18.
- **S5 — First-content benchmarks.** .NET and Node fixtures against Solid 2; counter scenarios.

Hydration:

- **H1 — Adoption of a single template.** `hydrate` entry, cursor, logical walk, claimed nodes, compare-then-write first commit, events, root-level mismatch and `Strict`; tests 20–24, 29–30.
- **H2 — Regions.** List rows (separators, zero-root rows), arms (hash), view holes, nested regions, region-level mismatch; tests 25–27.
- **H3 — Boundaries.** The four boundary cases; test 28.
- **H4 — State transfer, identity and forms.** State script, `readState`, `CreateId` continuation on the client, form and normalisation Chromium tests; tests 16 (client side), 31–33.
- **H5 — Hydration benchmarks.** Against mount and Solid 2; heap after hydration.
