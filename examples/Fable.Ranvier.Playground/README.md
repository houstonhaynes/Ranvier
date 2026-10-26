# Fable.Ranvier playground

A browser playground for the `Ranvier.View.Dom` renderer. A shared F# page declaration generates
the HTML document and browser entry. The shell, keyed rows, load-result panel and component examples
arrive rendered from .NET and hydrate in place. Components use the typed Sugar authoring syntax.
The counter's scheduling panel deliberately owns a separate manual mount so it can demonstrate
switching commit modes and unmounting while preserving state.
Ranvier updates text, attributes and DOM properties without rebuilding the tree. Try the counter,
editable name field and unmount/remount button. Select **Use microtasks**, then **Burst +100** to
exercise coalescing. **Flush DOM** commits queued work explicitly. Changing scheduling mode remounts the demo while preserving its state.
**Burst +100** groups its writes with `Api.batch`; every other listener runs unbatched. The default
synchronous mode commits the final values before the handler returns; the microtask mode defers
subsequent DOM commits.

The Playground project references `src/Ranvier.View.Dom` as a project, so the app compiles the
repository's view sources. Add and reverse keyed rows to exercise reconciliation in the browser. The project keeps its
`src/Fable.Ranvier` reference for the fixtures under `tests/` that check that library.

## Run

Prerequisites: .NET 10 SDK, Node.js 26 or newer, npm. The repository pins Fickl 0.6.9 (a Fable fork) in its
local .NET tool manifest.

From the repository root:

```powershell
rtk proxy dotnet tool restore
Set-Location examples/Fable.Ranvier.Playground
rtk proxy npm ci
rtk proxy npm run dev
```

Open the localhost URL printed by Vite (normally `http://127.0.0.1:5173`). Edit `App.fs` to change
the demo. The development command generates the document and entry, compiles them, then starts Vite.
Source edits regenerate both outputs before a full page reload; a failed generation is reported instead
of serving stale content. Reloading resets app state. The demo's own unmount/remount control preserves
its state. Closing Vite stops any compilation it started.

To launch after installation from the repository root:

```powershell
rtk proxy npm --prefix examples/Fable.Ranvier.Playground run dev
```

RTK is the repository's command convention; without it, run the underlying `dotnet`/`npm` commands.

## Build and test

From this directory:

```powershell
rtk proxy npm run build
rtk proxy npx playwright install chromium
rtk proxy npm test
```

`build` renders HTML on .NET, generates the F# browser entry, and compiles it before Vite bundles the
page into `dist/`. `test:build` checks clean and stale outputs, failed generation and manifest handling.
`test:dom` compiles the F# behavioural
checks and runs them with Node's test runner against jsdom. The test module is imported before
DOM globals are installed to check import safety. `test:browser` compiles the fixtures and app and runs real
Chromium interactions against a separate Vite server on port 5178. It checks output updates,
input node identity/focus/caret, the disabled property, and listener cleanup across remounts.
Scheduling checks cover mutation counts, disposal before commit, setter failures and a synthetic
composition-input echo. Node checks also exercise Pending/Failed invalidation, shared graphs,
setter ownership, reentrant writes and mounting inside a graph batch.
The browser suite checks useful content with JavaScript disabled and verifies that startup retains
server-rendered nodes. It also edits and restores `App.fs` to verify generation and reload after a
source change. Run the browser suite in a checkout where `App.fs` is not being edited concurrently.
The suites can be run separately with `npm run test:dom` and `npm run test:browser`.

The private npm project pins dependencies in `package-lock.json`. Node 26 is the selected minimum
for the pinned jsdom release; no external server or web API is required.

`rtk proxy npm run bench:dom` measures synchronous versus microtask DOM updates, with and without
core batching, in headless Chromium. It validates output and work counts, saves raw samples and
a report under `docs/.ai/benchmarks/dom-scheduling/`, and closes its own Vite server on port 5179.
See [the benchmark method and results](../../docs/.ai/benchmarks/dom-scheduling/README.md).

## Layout

- `App.fs`: the shell and demo components, keyed rows, load-result panel and browser test controls.
- `ComponentsExample.fs`: typed component composition and reactive props.
- `Playground.fs`: the shared page declaration.
- `Playground.fsproj`: shared code referencing `src/Ranvier.View.Dom` and `src/Fable.Ranvier`.
- `Server/`: the .NET document and browser-entry generator.
- `Client.fsproj`: compiles the generated entry after its shared declarations.
- `vite.config.mjs` and `page-build.mjs`: manifest inputs and development generation, using
  the helper shared with Motion in `../page-build.mjs`.
- `style.css`: responsive layout and styling.
- `tests/DomChecks.fs`: compiled F# checks of library behaviour.
- `tests/dom.test.mjs`: Node/jsdom setup and test invocation.
- `tests/ScheduledChecks.fs`, `tests/scheduled.test.mjs`: compiled scheduling fixtures and Node checks.
- `tests/scheduled.html`, `tests/scheduled.spec.mjs`: browser scheduling checks.
- `tests/playground.spec.mjs`: real browser interaction checks.

`index.html`, `generated-pages/`, `generated/`, `generated-tests/` and `dist/` are generated and ignored.
Library edits are included in development regeneration.

## Deliberate manual examples

The scheduling/remount panel uses explicit mounting to demonstrate its lifecycle and commit modes.
The HTML and JavaScript under `tests/` are harnesses for individual renderer and hydration contracts;
their manual setup is part of those tests. Benchmark entries compare particular authoring and runtime
forms, so their explicit variants remain intact. New ordinary demos use shared page declarations and
modern Sugar components; see [the contributor guide](../../CONTRIBUTING.md#examples-and-playgrounds).

## Legacy PoC fixtures

The fixtures under `tests/` that exercise `src/Fable.Ranvier` (`DomChecks.fs`, `ScheduledChecks.fs`,
`scheduled.html`) cover that earlier library: HTML with a static child structure, synchronous state and
optional microtask DOM writes. The queue adds overhead; reduced mutation counts are not a measured
overall speedup. That library has no JSX, server rendering, hydration or SVG; the main playground
uses `Ranvier.View.Dom` for its server-rendered and hydrated components.
The runtime is Ranvier, not Solid; Solid 2's split effects inspired the binding approach.
Pending/error values retain the last successful binding output rather than automatically creating
a loading/error view. See the library README for its ownership and property-setter contracts.

SageFs was used to resolve generated DOM types, and the local Fable.SageFs setup was used for quick
JavaScript-shape experiments. Neither tool is a runtime or build prerequisite for this playground.
