# Shared Motion Pages Implementation Plan

> **For agentic workers:** Use subagent-driven development with disjoint file ownership and a whole-branch review.

**Goal:** Generate Motion HTML and browser startup from shared F# page declarations.

**Architecture:** Ranvier.View owns the portable page model and render/source generation. Ranvier.View.Dom owns strict browser attachment. Motion owns the page registry and filesystem generator; Vite consumes its manifest.

**Tech Stack:** F#, .NET 10, Fickl/Fable, existing Vite and Node test tools.

**Spec:** ../specs/2026-10-23-motion-pages-design.md

## Global constraints

- Preserve existing unrelated working trees and changes; work on codex/motion-pages.
- Add no package dependency; use existing server rendering and safe state transfer.
- Preserve explicit mount tests; generated pages use supported synchronous hydration.
- Generated output must be reproducible and missing/failed generation must be visible.

## Review focus

- Invalid identifiers/state/paths must fail before emitting unsafe or partial startup.
- Hydration must reuse server nodes and dispose owned resources on startup failure.
- Browser-only timers, location and reduced-motion policy must not break server rendering.
- Clean and incremental builds must refresh output and retain subpath asset behavior.
- Development generation failures must not silently serve stale HTML.

## Tasks

- [x] Reproduce the missing Story generation gap with real Vite fixture tests, fix build ordering, and verify a clean Motion build.
- [x] Add Page.Definition, explicit codec, render and validated F# clientSource in Ranvier.View; add PageClient.start with owned disposal in Ranvier.View.Dom. Verify escaping, missing/invalid state, identity reuse and cleanup using twin tests and SageFs.
- [x] Migrate Motion component startup to placement lifecycle, declare six pages, and generate HTML, five entry sources and pages.json from Server. Keep existing mount test contracts.
- [x] Add separate Client.fsproj, consume generated manifest in Vite, regenerate before compile/build/dev, remove authored HTML/main.js adapters and ignore generated outputs. Test missing outputs, failed rendering, fresh edits and subpath assets.
- [ ] Document the shared page API with compiled examples; run formatting/XML audits, full relevant .NET and Motion tests, clean production build and browser checks. Obtain independent review, fix findings, publish PR and verify required checks before integration.
