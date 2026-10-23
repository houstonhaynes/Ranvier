# Server Hydration Implementation Plan

> **For agentic workers:** Use subagent-driven development with separate ownership of the example, documentation, and build integration.

**Goal:** Ship a runnable Giraffe hydration example, equivalent backend source, and free hosted documentation previews.

**Architecture:** Share the Ranvier page and state codec across server and generated browser entry. Bundle assets once for the live host and deterministic static export. Extend the existing documentation asset-copy pipeline.

**Tech Stack:** F#, .NET 10, Ranvier.View, Giraffe, Fickl, Vite, Playwright, Nacara, GitHub Pages.

**Spec:** `docs/superpowers/specs/2026-10-23-server-hydration.md`

## Global constraints

- Modern Sugar syntax; no handwritten HTML or browser startup.
- Existing user checkout remains untouched; work in the attached server-hydration worktree.
- Serialize .NET builds and Sage resets within the worktree.
- Source equivalents for Oxpecker and Suave reuse the shared UI.

## Review focus

- Query text containing HTML and script delimiters remains inert after hydration.
- Concurrent requests do not share mutable graph state.
- Static asset links work below the documentation subpath.
- Missing or malformed serialized state fails rather than silently changing initial props.
- Sample commands work from the documented directory after a clean checkout.

## Tasks

- [x] Implement `examples/Ranvier.Hydration` shared page, generated startup, Giraffe host, export and package scripts. Test useful SSR, request isolation, escaping, retained DOM nodes and counter interaction.
- [x] Add complete Oxpecker and Suave server source and validate package/API compatibility against the shared page.
- [x] Extend `docs/Site.fs`, documentation navigation and examples landing page with hosted demos and a lazy preview. Check output asset paths.
- [x] Extend `build.fsx` documentation generation and CI to build/test the hydration example and publish all static demos.
- [x] Run production builds, full example tests, docs generation and independent review. Verify all three demos through the generated documentation's URL prefix.
- [ ] Complete final formatting, create the PR and verify CI before integration.
