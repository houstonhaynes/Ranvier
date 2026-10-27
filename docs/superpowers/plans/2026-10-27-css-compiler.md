# CSS compiler and reactive styling implementation plan

**Goal:** Complete the specification-driven CSS compiler discussed in the shared Ranvier research chat, including reactive values, automatic class names, modular assets and lifted single-file output.

**Source:** https://chatgpt.com/share/6adf97f8-ee4c-83ec-b45e-afbe14ff1805 (recovered full discussion on 2026-10-27). The user authorized implementation without design questions.

**Architecture:** Generated specification bindings use a shared portable value/declaration runtime. A semantic stylesheet tree preserves order, nesting, conditions, layers and descriptors. Component styles lower into that tree with content-derived class names. Reactive readers become custom-property bindings; static rules never change on signal updates. A separate build library walks explicitly declared style dependencies and emits assets. A View adapter uses existing tracked template attributes and SSR document links.

## Requirements and evidence

- [x] Portable `Ranvier.Css` values and declarations interoperate with generated bindings; generated compiler checks reject dimension mismatches.
- [x] Stylesheet AST supports ordered declarations (including fallbacks and importance), selectors, media/supports/container conditions, layers, nesting, keyframes, descriptor at-rules, statement at-rules, and explicit unsafe syntax.
- [x] Component style definitions automatically derive deterministic class names, independent of registration order; equal definitions deduplicate without changing cascade order.
- [x] Dynamic property helpers are generated for every property. Readers lower to CSS variables, run per instance under existing Ranvier tracking, update without rule injection, and clean up with the component.
- [x] Explicit typed custom variables support references, optional fallbacks, bindings and restricted `@property` registrations.
- [x] Multiple authoring methods: declaration lists, a computation expression, explicit stylesheet rules, and component-scoped style nodes.
- [x] Explicit module graph supports transitive dependencies, rejects conflicting module identities/cycles, and preserves dependency/cascade order.
- [x] `Ranvier.Css.Build` emits regular per-module CSS, CSS Modules suitable for a real bundler, or one lifted CSS asset. Configurable filenames/public base URLs and deterministic manifests identify all assets and class mappings.
- [x] Build-time extraction executes an explicit F# style manifest entry point; generated bindings are delivered as ordinary source/library artifacts rather than generated during every application build.
- [x] View helpers apply static classes and reactive/inline declarations; SSR references the emitted assets and renders current variable values. Real component update/disposal tests and server/client identity checks prove the adapter.
- [x] Full generated corpus compilation, runtime regressions, emitted CSS checks with a browser-grade parser, browser/Fable reactive integration, documented executable examples, and CI run the acceptance path.

## Execution order

1. Add portable values, variables, AST, deterministic component styles and pure regression tests. Exercise pure logic in SageFs before the CLI gate.
2. Add build graph, output options and asset writer with ordering/deduplication/real file tests.
3. Add generated runtime mode and generated reactive property methods; compile a generated consumer against the shared runtime.
4. Add View integration and explicit extraction example, execute SSR and browser reactivity/disposal checks.
5. Review against every requirement above, document boundaries, run full affected suites and compile/output gates. Only then mark the goal complete.

## Rulings

- Preserve the canonical grammar API already generated; add ergonomic/runtime-facing methods rather than replace it with a small hand-maintained property subset.
- Static extraction uses explicit application-owned manifests, as recommended in the original discussion; arbitrary runtime F# expressions are not silently evaluated or guessed by a compiler transform.
- A changing value changes a CSS custom property, never the class identity or stylesheet contents. Unbounded dynamic variants therefore cannot leak new rules.
- Pinned specification coverage remains explicit; unresolved/prose-only spec constraints continue to require named unsafe escape hatches.

## Progress ledger

Previous turn: progress — implemented and verified spec ingestion, latest consolidation, reference resolution and standalone bindings. Remaining compiler/reactive/assets/View requirements were absent from that worktree and are being implemented here.

## Completion evidence

Implemented the portable runtime, shared generated binding library, View adapter, asset compiler, explicit extraction example and CI acceptance steps. The ordinary generated corpus contains 4,882 definitions and 865 property entries with static/reactive helpers. CSS-wide keywords derive from the current `all` production, including `revert-rule`; a synthetic future keyword test proves this is data-driven.

Acceptance checks executed successfully on 2026-10-27:

- Complete CSS test suite: 90 passed, none failed or ignored. Pure runtime checks also passed in SageFs.
- `verify-bindings.mjs`: exact generated source/manifest freshness, complete Release corpus compilation and execution, negative dimension and obsolete API compilation, ranges/cardinality/identifier/string contracts, and property helper coverage.
- `verify-assets.mjs`: real emission for all three modes, Lightning CSS parsing, CSS Modules export composition, ordered SSR asset links and initial variables.
- Full generated library and browser consumers compiled with Fickl/Fable. DOM and Chromium checks passed under both commit modes, including native computed widths, inline CSS-wide semantics, update/disposal, stable rules and matching server/browser identities.
- Handwritten F# formatting and XML documentation audit passed with zero findings.
- Shared runtime/bindings and View adapter Release builds also passed for .NET 8 with zero warnings or errors.
- Independent review findings were fixed and re-reviewed: CSS-wide variable semantics, statement preambles/namespace environments, case-insensitive filename collisions, and safe adjacent style deduplication. No remaining concrete blocker was reported.

Boundaries are explicit in the runtime/build/generator READMEs. The pinned corpus retains 160 prose-defined productions and two unresolved upstream references through named unsafe routes. Latest selection means newest supplied specification level, with reproducible locked inputs. CSS-wide values use inline property bindings; unsupported stylesheet lifting returns diagnostics. CSS Modules require the documented processing configuration. Extraction uses explicit application-owned manifests; it does not inspect arbitrary application expressions.

Work is preserved in the attached `css-spec-consumer` worktree for review; no publication or merge is part of this implementation task.
