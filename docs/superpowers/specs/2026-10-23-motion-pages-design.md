# Shared page declarations and Motion generation

The user approved fixing Motion's build gap, then implementing a shared F# page declaration that generates HTML and browser startup. The public page model belongs in Ranvier.View; browser attachment belongs in Ranvier.View.Dom. The example owns filesystem output and Vite orchestration. Existing rendering, state escaping and hydration remain the underlying implementations.

## Contract

- A page declaration carries its component, an initial-props factory, document shell, server options and optional explicit state codec. No new package dependency is required. A factory runs once per render or state-free client start; reactive instances are created inside the request/mount graph.
- Rendering transfers encoded initial props when a codec is configured. Client startup requires that state and reports decoding failures; it does not silently mount an empty host. Hydration uses the declared host, id prefix and graph options with strict structural checking. Its returned handle owns graph and mount disposal, including cleanup on failure.
- A pure source generator emits a small F# browser entry referring to a validated qualified declaration name. Fable resolves its imports. Generated sources and documents are ignored build outputs, not hand-maintained source files.
- Motion declares the static landing page and all five demo pages in F#. Server generation emits their index.html documents, five browser entries and one manifest. A separate Client.fsproj compiles generated entries while the generator references the shared Motion project, avoiding a generation/build cycle.
- Build and development regenerate the pages before browser compilation. Vite consumes the manifest and requires generated documents. Development edits rebuild before reloading; failed generation is reported rather than serving an old document as current output.
- Routes hydrates the server's initial route before applying the browser hash. Browser timers, policy listeners and animation startup occur after placement. Existing explicit client-mount APIs remain available for mode-specific tests.

## Boundaries and validation

The change adds a reusable page API and a concrete integration, not a web server or a routing framework. Document rendering continues to escape state and attributes. Build paths and generated identifiers are validated. Static serving under a subpath, lifecycle cleanup, missing/invalid state, strict node reuse, clean builds, fresh development output and full Motion browser behavior are acceptance cases.

Hydration currently supports synchronous commits only; generated hydrated demos use that supported mode. Extending hydration scheduling is outside this change. No automatic pagehide disposal is introduced because back-forward-cache restores must remain functional.
