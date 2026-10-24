# Ranvier Signal Maps Implementation Plan

## Core and view state

**Core primitives used:**

- `createSignal` / `createMemo` in component setup: the displayed scene, cursor, playback state, speed, caption,
  log, explanation, inspection text, error, and the control list. A memo over the displayed scene computes
  `SceneView.project`.
- `each` / `forEach` / `forEachFixed` (core projections): nodes, edges, collection boxes, column labels, flight dots,
  log entries, timeline ticks, controls and speed options, keyed by node id, edge pair, dot sequence number or index.
- `show` / `showElse` / `matchWith` (a memo with an owner per arm): the timeline bar, live versus replayed controls,
  and the error panel.
- `Setup.OnCleanup` on the map instance's owner: disposes the scenario `Graph`, cancels playback timers, animation
  frames and running animations, and removes source-line highlights.
- `batch`: groups the writes of one replay frame so the view commits once per frame.
- A separate `Graph` per scenario run: the traced scenario graph, kept apart from the view's graph so view updates
  never appear in the trace.

**View-owned state:**

- DOM nodes created by the templates and their attribute and property writes.
- anime.js animatables and animations on node groups, shapes and dots: animation state written to native nodes,
  created in mounted hooks and cancelled by their cleanup.
- Source-line highlight classes on the card's highlighted code lines: an ownership claim on native nodes outside the
  map's own template, taken through a lent hook and released by its cleanup.
- Browser timer and animation-frame handles for playback pacing and trace polling: no core equivalent; released by
  instance cleanup. Each frame reads the scenario graph's new trace events and its bound controls' values with `Peek`.
  An effect on the scenario graph would itself be traced and drawn on the map, so the view reads that graph from
  outside instead.
- Server HTML for each map host, rendered by `Server.renderPage`.

No view-side ownership tree, key matcher, reconciler, selection cache or commit queue is introduced.

## Overview

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkboxes for tracking.

**Status:** In progress. The user asked for implementation on 2026-10-24; steps are ticked as they land.

**Goal:** Render the documentation's interactive signal maps with Ranvier.View, preserving their controls, trace playback, source highlighting, and authoring experience.

**Architecture:** Keep the existing trace model, replay rules, and layout behavior. Author the controls and SVG tree with Ranvier.View Sugar and keyed reactive lists; retain native DOM access only for animation and source-code highlighting. A documentation-local Nacara plugin generates shared page definitions, server fragments, and browser startup for each documentation page.

**Tech stack:** F#, .NET 10, Ranvier.View, Fickl, anime.js, Nacara, Expecto, Playwright, existing static documentation hosting.

**Spec:** The scope, contracts, and acceptance criteria below record the user request and read-only feasibility audit; no separate specification is required for this plan.

## Global constraints

- Scope is signal maps. Keep the unrelated Solid landing-page animation and its dependencies where still needed.
- Controls, SVG nodes, edges, groups, flight dots, logs, and timeline ticks must be real Ranvier-owned views. Wrapping the current imperative renderer in one widget is insufficient.
- Use modern Sugar syntax. Generate browser startup with `Page.clientSource`; do not handwrite per-map JavaScript startup or HTML renderers.
- Preserve existing `fsharp map` flags, labels, source spans, diagnostics, code disclosure, live editing, and trace-model validation.
- Keep maps and highlighted source in the same document, not iframes. Preserve static hosting below `/Ranvier/` without adding a server runtime.
- Follow repository F# semantic tooling, SageFs, and XML-comment instructions. Serialize builds/resets in a shared worktree and release owned sessions and leases.

## Feasibility evidence and remaining proof

The source audit below motivated the migration; the implementation notes record what the built integration showed.

| Observed source behavior | Consequence |
| --- | --- |
| `docs/maps/SignalMap.fs` constructs about 1,300 lines of imperative browser UI; its Solid boundary is the return type and cleanup registration. | Removing the Solid dependency is straightforward, but a genuine declarative migration requires replacing DOM construction. |
| Model, replay, layout, controls, and authoring already live in separate modules with tests. | Preserve these contracts and extract the renderer's remaining pure geometry calculation. |
| `MapFence` emits scenario code, a render expression, and source spans. | Keep scenario generation and span mapping; change the render result from a native element to a component. |
| The Solid plugin generates Solid wrappers and startup. | Changing the return type alone cannot work; maps need their own loader. |
| Nacara exposes transforms, routed-page hooks, asset transforms, `Page.WithData`, and `HookContext.Write`. | A docs-local plugin can own generation and tracked outputs without modifying Nacara. |
| `LiveExample` compiles on the reader's Run action; it does not provide build-time SSR. | Preserve it independently for the editable example. |
| Nuglify strips comments and collapses whitespace by default during production builds. | Insert hydratable fragments after minification, or their markers and text boundaries can be destroyed. |

Unproven risks include .NET initialization of Fable/browser imports, generated-project compilation and bundling, lifecycle timing during hydration, cached page metadata, and preservation of diagnostics. The first integration slice must resolve these before expanding the renderer migration.

## Shared contracts

- Keep `SignalMap source policy bindings timeline grouping` and `SignalMapWithSpeed speed source policy bindings timeline grouping` in `SignalMapComponent`, with the existing argument types and a new return type of `Component<unit>`.
- Keep `MapFenceOutput.Code`, `.Render`, and `.Spans`. `Render` remains an expression calling one of those functions. Scenario generation and validation retain their current behavior.
- The generated module, not the map library, owns each `Page.Definition<unit>`. It uses unique, deterministic `Document.HostId` and `Options.IdPrefix` values per cell, unit initial props, and no state codec.
- Render the initial empty scene and deterministic timeline shell on the server. Start the scenario graph only in a mounted hook. Server rendering must not execute scenario actions or allocate browser resources.
- Export fragments with `Server.renderPage page.Options page.Component (page.InitialProps()).Body`; obtain startup from `Page.clientSource`. Bundle entries per documentation page so a page does not start maps whose hosts are absent.
- Use `.rv-map-card` and `.rv-map-host`, with `data-ranvier-map-page` and `data-ranvier-map-cell` metadata. Source highlighting and code disclosure must use the same card contract.
- Support ordinary components through `fsharp ranvier render=Thermo.Thermometer` or expression fences. The expression returns `Component<unit>`; a function-valued example calls its factory explicitly and has no `SolidComponent` attribute.

## Review focus

1. Production minification must retain hydration markers and text boundaries; test production output, not only watch mode.
2. Multiple maps must have distinct IDs, graphs, controls, and cleanup; resetting one must not affect another.
3. External signal writes must refresh bound controls without overwriting untouched in-progress text or number edits.
4. Reset, disposal, or replay replacement must prevent old timers, frames, or animation completions from mutating the new map.
5. Cached Markdown transforms must retain flags and source spans, including useful diagnostics for invalid map code.

## File boundaries and sequence

Renderer and plugin work can proceed independently after the shared contract is proven. They join at the production documentation gate. The renderer and plugin are the two largest tasks; example migration and test wiring are smaller. These are relative effort estimates, not delivery-time commitments.

### Task 1: Prove one native map through the documentation pipeline

**Files:** `docs/Maps.fs`, `docs/Site.fs`, `docs/maps/Ranvier.Docs.Maps.fsproj`; generated projects under `docs/.nacara/ranvier-maps/`.

**Interface:** A `Component<unit>` expression becomes one unique page definition, SSR fragment, and generated client entry.

- [x] Add a minimal deterministic Sugar fixture and render it on .NET without invoking browser imports or scenario actions.
- [x] Generate separate Shared, Exporter, and Client project directories to avoid shared `obj` collisions. Use the repository-pinned Fickl and existing project references.
- [x] Compile the generated client and bundle one page through the docs-local plugin. Write outputs through `HookContext.Write`.
- [x] Verify two fixtures on the same page hydrate without mismatch reports or duplicate IDs, with assets served below `/Ranvier/`.
- [x] Verify the same fixture in a production build after Nuglify, then checkpoint the working integration before migrating the full renderer.

### Task 2: Extract a pure scene projection

**Files:** create `docs/maps/model/SceneView.fs`; modify `docs/maps/model/Ranvier.Docs.MapModel.fsproj`; create `docs/maps/tests/SceneViewTests.fs` and include it in the test project.

**Interface:** `SceneView.project : Scene -> SceneView.Projection`. The projection contains stable node IDs, positions and labels, edge source/target IDs and paths, collection rectangles/column labels, and SVG dimensions. It contains no DOM nodes or animation handles.

- [x] Add cases for an empty scene, ordinary dependency chain, expanded collection rows/members, collapsed grouping, and removed nodes. Assert finite dimensions, stable identity, correct member-to-row external ports, and no overlapping collection bounds.
- [x] Extract the existing geometry from `SignalMap.sync`, preserving its layer ordering, port alignment, box extents, and spacing rather than redesigning layout.
- [x] Keep model/replay changes out of this extraction. Run the full traced map suite and compare representative projections with the current renderer's geometry.

### Task 3: Replace DOM construction with a Ranvier component

**Files:** `docs/maps/SignalMap.fs`, optional `docs/maps/MapAnimation.fs`, and `docs/maps/Ranvier.Docs.Maps.fsproj`.

**Interface:** The two public component factories above consume the unchanged map source and the pure projection from Task 2.

- [x] Reference Ranvier.View and remove the maps library's Partas.Solid reference. Make top-level initialization .NET-safe; browser operations occur only in browser lifecycle hooks.
- [x] Create display signals during component setup. Keep the scenario's traced `Graph` separate from the view's graph so UI updates do not appear as diagram events.
- [x] Author the full control bar, inspection/error panels, logs, timeline, and SVG structure with Sugar. Use keyed lists for dynamic nodes, edges, boxes, dots, controls, and ticks; preserve row identity during updates.
- [x] Preserve hover/focus inspection, Enter/click cause explanation, all five control kinds, external binding refresh, Play/Step/scrub/Reset, speed range `0.25..4`, selected speed across Reset, and reduced-motion behavior.
- [x] Keep Anime position/cue effects and source-line highlighting in small mounted/lent adapters. Animation completion removes a dot through its reactive list, not by deleting a view-owned node.
- [x] Own the scenario graph, animation handles, animation frames, replay/flash timers, and source-highlight cleanup through the component lifetime. Guard callbacks from an earlier reset or disposed graph.
- [x] Test deterministic SSR without scenario execution, multiple component instances, and cleanup. Run control tests covering external writes and unfinished edits before browser integration.

### Task 4: Complete authoring, caching, and code-card integration

**Files:** `docs/Maps.fs`, `docs/Site.fs`, `docs/maps/authoring/MapCode.fs`, `docs/maps/authoring/MapFence.fs` if needed, `docs/maps/tests/AuthoringTests.fs`, `docs/theme/maps.css`.

**Interface:** Existing map fences and new ordinary Ranvier component fences produce the card/host and generated-page contracts above.

- [x] Transform fences before Markdown rendering. Reusing the pinned public `SolidScan.scanWith` parser at build time is acceptable; ensure its output cannot be claimed by the Solid client loader.
- [x] Store per-page cell metadata with `Page.WithData`, including flags, spans, and bindings; do not depend solely on a mutable dictionary populated by uncached transforms.
- [x] Compile/export after pages are routed. Generate highlighted code and native disclosure before Nuglify; inject hydratable host contents after Nuglify. Preserve no-JavaScript disclosure and keyboard operation.
- [x] Preserve diagnostic span mapping: generated declaration offsets, the existing `Indent = Int32.MaxValue` body convention, page body offsets, and adjusted columns. Avoid introducing `#line` directives that Fable diagnostics cannot place.
- [x] Test every existing flag, invalid/missing controls, automatic labels, code height/disclosure, cached transforms, nested pages, and stable output paths. Keep existing authoring tests passing; add tests for native card ownership and generated host uniqueness.

### Task 5: Migrate documented entry points and establish the browser gate

**Files:** `docs/content/guide/signal-maps.md`, `docs/literate.fsx`, `docs/maps/tests/fixtures/literate.fsx`, browser test files under `docs/maps/tests/`, and existing docs/CI test wiring as needed.

- [x] Convert the bespoke Solid thermometer example to the native Ranvier fence. Update the editable F# example to explicitly mount the returned component with a retained lifetime, using the documented Ranvier mounting API. Keep that explicit editor lifecycle distinct from generated documentation startup.
- [x] Update literate references and explanations. Retain the existing Fable/Node trace-expectation validator unchanged unless an import path requires adjustment.
- [x] Add a persistent Playwright smoke suite using the repository's existing Playwright version. Serve production `docs/output` under `/Ranvier/`; test representative live, replayed, async/timed, and collection maps.
- [x] Cover all controls, speed/step/scrub/reset, external binding changes, source highlighting, disclosure, keyboard inspection, reduced motion, multiple maps, and disposal/reset during pending work. Assert no console errors or hydration mismatch reports.
- [x] Confirm the unrelated Solid landing animation and editable Run action still work, and that native map bundles contain no Solid renderer startup.

### Implementation notes

- Geometry: on the guide, aggregates and landing pages, the native maps' view boxes, node positions and edge paths
  equal the published Solid renderer's, map for map.
- Controls: the DOM twin tests text, checkbox and button controls, including an external write beside an unfinished
  edit. The twin does not sanitise number and range values, so those two controls are covered by the browser suite.
- Editable example: the browser compiler of `LiveExample` has no `Fable.Core.TS` assembly, so a snippet cannot name the
  DOM types `Mounting.mount` takes. `MapMount.body` in the maps library calls `Mounting.mount` for it and returns the
  handle the snippet keeps.
- Browser callbacks (timers, animation frames, animation completions) activate the view's graph before writing display
  signals; event handlers already run inside it.
- Closed after the independent review: lines inside a string literal of an expression cell are copied as written, so
  a multi-line literal keeps its value; every generated line maps to a page line, the module header to the first
  fence; and a page whose module fails to compile or whose cell throws on the server is left out of the bundle. Its
  hosts show a failure card, every other page is rendered and hydrated, and the build still fails with the mapped
  diagnostic, reported once the pages are written.
- `build.fsx -- format` passes relative paths in batches, so it starts in a deep worktree. Its glob still includes
  untracked generated sources (`dist/`, `docs/.nacara/`); CI checks tracked files only.

## Acceptance gates

All five gates passed on 2026-10-24; PR #171 records the counts.

1. `rtk dotnet test docs/maps/tests -c Release -p:RanvierTrace=true` — full model, authoring, control, and new projection/render tests pass.
2. Build the native maps library and generated exporter/client with tracing enabled; verify both .NET rendering and Fickl compilation. Run formatting and XML-comment checks on changed F# files.
3. `rtk dotnet fsi build.fsx -- docs` — existing literate fixture and Fable/Node map expectations pass; production documentation and map assets are generated successfully.
4. Run the persistent browser suite against that production output, including `/Ranvier/` paths and at least two maps on one page. Record passed test count and any remaining warnings.
5. Independently review the renderer lifetime, source-span mapping, cached transform behavior, and generated startup. Check that no imperative whole-map widget or handwritten startup remains.

The plan is implemented: the gates pass and the preserved behaviors above are demonstrated by the .NET, twin and
browser suites.
