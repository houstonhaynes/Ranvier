# Server hydration example

Provide one runnable F# web server that renders request-specific Ranvier state and hydrates the same DOM in the browser. Select the primary backend using recent NuGet download trends. Giraffe leads the compared Giraffe, Suave, Oxpecker, Saturn and Falco packages across both four-week and twelve-week snapshot differences on 2026-10-23.

The example lives in `examples/Ranvier.Hydration`. A modern Sugar view and shared `Page.Definition` own the document and serialized props. `Page.clientSource` generates browser startup. A Giraffe handler supplies each request's greeting and initial counter; the browser resumes that state and handles counter events. Keep state isolated across requests and encode untrusted text through the renderer and state codec.

Provide complete equivalent Oxpecker and Suave server source that reuses the same shared page and browser assets. Validate those sources without maintaining duplicate UI applications.

Publish the deterministic static export, main playground, and existing Motion demos on the existing GitHub Pages documentation host. Add an Examples menu and a small lazy preview. Explain that the hosted hydration preview is rendered at build time; the runnable server demonstrates request-time rendering. No paid hosting is needed.

Acceptance: a clean build generates HTML and startup, live HTTP responses carry distinct request state, no-JavaScript rendering is useful, hydration retains DOM node identity, counter events work, alternate source compiles, documentation output contains the demos and valid nested asset links, and CI exercises the new example.
