# Playground Pages Implementation Plan

> **For agentic workers:** Use delegated implementation with disjoint file ownership and an independent final review.

**Goal:** Generate the main playground from shared F# declarations and make ordinary examples use current authoring syntax.

**Architecture:** Portable page/component declarations are shared by .NET rendering and Fable startup. Build integration owns generated files and Vite orchestration. Explicit manual mounting remains only at documented teaching boundaries.

**Spec:** ../specs/2026-10-23-playground-pages-design.md

## Tasks

- [x] Inventory ordinary examples versus deliberate manual fixtures; assign migrations and record exceptions.
- [x] Convert the main playground's ordinary components to typed Sugar declarations and expose a shared `Page.Definition`; preserve its scheduling demonstrations and test controls.
- [x] Generate playground HTML and browser startup, separate shared/server/client projects, reuse page-build orchestration, and remove handwritten application adapters.
- [x] Modernize other ordinary examples, preserving benchmark and compiler fixture contracts.
- [x] Update the playground documentation and add regression coverage for generated content, hydration identity and development regeneration.
- [ ] Run full affected suites, production builds, formatting/XML audits and independent review; publish and verify CI before integration.

## Review focus

- Server content must not disappear into empty widget placeholders without an explicit teaching reason.
- Generated startup must own and clean up the root, including failed hydration and remount behavior.
- Development changes must regenerate HTML and client output consistently; shutdown must stop owned compilers on Windows and Linux.
- Source fixtures intentionally testing underlying operations must remain meaningful.
- Preserve unrelated local changes and serialize builds that share project outputs.
