---
title: Testing views
---

A view has two kinds of test. **HTML checks** render a component with `Server.render` and compare the
string. **DOM checks** mount a component into a document and drive it with events. The DOM twin runs
these checks on .NET; jsdom runs them under Node after compilation with Fable.

The samples check the counter from [Getting started](getting-started.md).

## HTML checks on .NET

```fsharp
open Ranvier.View
open Counter

let private omit = { Server.Options.defaults with Policy = Omit }

let rendersStartValue () =
    let html = Server.render omit counter { Start = 3; Label = "Cups" }

    let expected =
        """<!--[r--><div class="counter"><span class="label">Cups</span><button type="button">3</button></div><!--]-->"""

    if html <> expected then
        failwithf "Expected %s; got %s" expected html
```

`Server.render` creates and disposes its own graph and runs every setup and cleanup: an HTML check
also covers setup failures and cleanup order. Use the `Omit` policy for components with handlers or
hooks. [Server rendering](../server/rendering.md) describes the output, including the markers that
lists, arms and boundaries write.

The same check compiles with Fable and runs under Node, which compares the two targets' output.

## DOM checks under Node

A DOM check is an F# function compiled with Fable. It mounts into a jsdom document, acts, and asserts:

```fsharp
open Fable.Core
open Fable.Core.TS.Dom
open global.Ranvier
open Ranvier.View
open Ranvier.View.Dom
open Counter

[<Global "document">]
let private document: Document = jsNative

let private expect (expected: string) (actual: string) =
    if expected <> actual then
        failwithf "Expected %s; got %s" expected actual

let clickIncrements () =
    use graph = new Graph ()
    let host = document.createElement "div"
    document.body.appendChild host |> ignore

    let mounted =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context ->
            Program.run (Component.render counter { Start = 0; Label = "Clicks" }) context.Backend)

    let button = unbox<HTMLElement> (host.querySelector "button").Value
    button.click ()
    button.click ()
    expect "2" button.textContent
    mounted.Dispose ()
    expect "" host.innerHTML

let cases = [| "a click increments the count", clickIncrements |]
```

Node's test runner installs the jsdom globals and runs each case:

```js
// counter.test.mjs
import test, { after } from "node:test";
import { JSDOM } from "jsdom";

const dom = new JSDOM("<!doctype html><body></body>");
for (const name of ["window", "document", "Node", "Event", "MutationObserver"])
  globalThis[name] = name === "window" ? dom.window : dom.window[name];
after(() => dom.window.close());

const { cases } = await import("./generated-tests/CounterChecks.js");
for (const [name, check] of cases) test(name, () => check());
```

```bash
dotnet fable tests/Tests.fsproj --outDir generated-tests
node --test counter.test.mjs
```

Import the compiled module after the globals are installed: the view modules read `document` when a
mount runs.

- **Signal writes** made outside a handler run inside the graph: `graph.Run (fun () -> todos.Value <- next)`.
- **Queued commits** need `mounted.Flush ()` before an assertion, or an awaited microtask.
- **Async reads** are deterministic with the core's test helpers; see [Testing async state](../guide/testing.md).
- **Hydration** checks render with `Server.render` in the same process, write the HTML into a host, and
  call `Hydration.hydrate`. Assert that the server's nodes are the ones the mount kept: the hydrated
  button is the same object as the server button.

The playground's [`tests/`](https://github.com/shayanhabibi/Ranvier/tree/master/examples/Fable.Ranvier.Playground/tests)
directory runs this setup for the whole view layer, with Chromium checks through Playwright for focus,
caret and layout behaviour that jsdom does not model.

## Twin checks on .NET

Reference `experiments/Ranvier.View.Twin/Ranvier.View.Twin.fsproj` from a .NET test project. The twin
and its testing helpers are experimental project references. Keep the twin out of Fable client projects.
It runs the real view renderer against a document whose events and clock are controlled by the test.

```fsharp
open Ranvier.View.Twin
open Ranvier.View.Testing
open Counter

let twinClickIncrements () =
    use session = Twin.mount counter { Start = 0; Label = "Clicks" }
    Interact.click session.Host "button"
    Interact.click session.Host "button"
    Expect.text session.Host "button" "2"
```

Dispose the session after each test to release its mount, graph and document. `Session.Html ()`
includes region markers; `Session.Text ()` returns the host's text. Query and expectation failures
include a bounded view dump to show the surrounding state.

The clock advances only when requested. `session.AdvanceBy 100.0` runs due timers and animation
frames, then settles pending work. `session.Clock.RunMicrotasks ()` drains microtasks;
`session.Clock.Settle session.Graph` also pumps graph work until both queues are idle.
`session.Act (fun () -> ...)` writes signals inside the graph and settles afterwards. Event dispatch
settles automatically. For queued commits, create the session with
`Twin.mountWith { TwinMount.defaults with Commits = Commits.Queued Scheduling.CollectAndContinue }`;
settle the clock after the initial mount before querying its children. `Scheduling` is in
`Ranvier.View.Dom`.

Keep layout, computed styles, scrolling, caret, selection and IME tests in Chromium. The twin raises
`NotSupportedException` for unsupported DOM members such as geometry queries. It supports HTML
elements and a subset of HTML parsing; use explicit `tbody` elements in tables. Custom elements,
Shadow DOM, mutation observers and SVG interfaces need a browser. Motion callbacks run on the twin;
the anime JavaScript engine needs the browser.

View code still follows its host language's rules. Number formatting, string comparison, integer
overflow and promise ordering can differ between .NET and JavaScript. Use explicit formatting for
text that must match both hosts, and run shared conformance checks under jsdom as well as the twin.

## Dumping a view

`Session.Dump ()` shows the mounted view between updates, on its graph's thread. It includes component
instances and their fields, bound text, keyed rows, the selected arm, boundary state, holes, departing
rows and document-head entries. Fields display cached values and statuses such as `pending` or
`stale`. Reading a dump leaves reactive computations unevaluated.

```fsharp
open Ranvier.View.Dom
open Ranvier.View.Twin
open Counter

let dumpCounter () =
    use session = Twin.mount counter { Start = 3; Label = "Clicks" }
    session.Dump { Debug.DumpOptions.defaults with MaxRows = 12; MaxChars = 4096 }
```

Element lines show attributes and differing form properties. An `instance` line introduces fields;
`list`, `row`, `arm`, `boundary`, `hole` and `leaving` lines describe the regions that HTML alone cannot
explain. Elision lines show omitted rows or content. `MaxDepth`, `MaxItems`, `MaxText`, `MaxRows` and
`MaxChars` bound the output.

Definition labels use the root element's tag, or `fragment`, followed by a stable structural hash.
They identify the compiled template shape; the renderer does not retain authored F# binding names.

In a client development build, compile with `--define RANVIER_VIEW_INSPECT` and call
`Debug.dump Debug.DumpOptions.defaults mounted`. Capture structured data with `Debug.capture`, then
use `Debug.text` or `Debug.json`; JSON carries the schema name `ranvier-view-dump/1`.

Set `NodeIds = true` to include node identifiers and, in a traced build, their trace paths. Use an
instance-field path from the dump with `Debug.nodeAt (mounted, "r-0.Count")` to retrieve the core node.
Pass that node to `Trace.why` to investigate its last run; see [Tracing](../guide/tracing.md). Trace
queries require a traced build. A client build without `RANVIER_VIEW_INSPECT` omits inspection support.
