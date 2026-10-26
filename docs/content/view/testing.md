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

Use jsdom for DOM bindings and node identity; use the twin for fast .NET interaction checks.
Keep layout, focus and browser integration checks in Chromium.

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

### Dumps outside the twin

`Debug` in `Ranvier.View.Dom` dumps any mounted view. .NET builds always include it. In a client
development build, compile with `--define RANVIER_VIEW_INSPECT`; leave the define off for production
client builds, which then omit `Debug` and the inspection probes. `Debug.dump mounted` returns the
text under `Debug.DumpOptions.defaults`. Capture the dump as data with `Debug.capture`, then render it:

```fsharp
open Ranvier.View.Dom

let report (mounted: Mounting.Mounted) =
    let dump = Debug.capture { Debug.DumpOptions.defaults with Values = false; MaxRows = 4 } mounted
    Debug.text dump, Debug.json dump
```

| Function | Result |
| --- | --- |
| `Debug.capture : DumpOptions -> Mounting.Mounted -> Dump` | The mount's nodes, with a `mount` header line for its phase, commit mode and failure count. |
| `Debug.captureNode : DumpOptions -> Graph -> Node -> Dump` | The nodes under a DOM node, such as one subtree of a mount, without the header. |
| `Debug.text dump` | The text under the options the dump was captured with. |
| `Debug.textWith options dump` | The text under other options, such as a smaller `MaxChars` or `Head = false`. |
| `Debug.json dump` | The JSON form. |
| `Debug.dump mounted` | The text of `Debug.capture Debug.DumpOptions.defaults mounted`. |
| `Debug.excerpt node` | Up to 40 lines around a DOM node, from its nearest enclosing instance, after a line break. Empty outside a registered mount, off its graph's thread or during a flush. |

Capture on the graph's thread, between flushes. `capture` raises `InvalidOperationException` off that
thread or inside a flush; `captureNode` raises it off the thread, and inside a flush when the subtree
holds a mount. `excerpt` returns an empty string instead, so it can decorate an error message.

The options bound the capture and its text:

| Option | Default | Effect |
| --- | --- | --- |
| `MaxRows` | `12` | Rows shown per list; the rest become one line with the elided keys' range. |
| `MaxText` | `80` | Characters of a text node or value before it is cut, with its full length noted. |
| `MaxItems` | `5` | Items shown of a collection or record value. |
| `MaxDepth` | `64` | Nesting depth of a formatted value. |
| `MaxChars` | `65536` | Characters of text; the remaining lines become a count of omitted lines. |
| `Values` | `true` | Instance fields and their values. `false` keeps the instance lines alone. |
| `Head` | `true` | The `head` section: the document title and the head entries the view manages. |
| `NodeIds` | `false` | Core node ids on field lines and, in a traced build, their trace paths. |

`MaxRows`, `MaxItems`, `MaxDepth` and `Values` apply when the dump is captured.
`textWith` can shorten or reformat a captured dump but cannot restore rows or fields the capture left out.

Use an instance-field path from the dump with `Debug.nodeAt (mounted, "r-0.Count")` to retrieve the
core node. Pass that node to `Trace.why` to investigate its last run; see
[Tracing](../guide/tracing.md). Trace queries require a traced build.

### Dump data and JSON

A `Dump` holds the `Mount` header text, the `Root` nodes, the `Head` entries, the `Options` it was
captured with, and the `TracePaths` of its node ids. Each `DumpNode` case is one line kind of the text:

| Case | Holds |
| --- | --- |
| `Element` | Tag, attributes, differing form properties and children. |
| `Text` | The text and whether a binding writes it. |
| `Instance` | An `InstanceDump`: component name, instance path, definition hash and `FieldDump` fields, each with its name, kind, shown value, status and node id. Then the children. |
| `List` | Region path, keyed rows, an `Elided` count and key range when rows were cut, and whether the region is stale. |
| `Arm` | Region path, selected key, definition label, children and whether the selection is stale. |
| `Boundary` | Region path, a `BoundaryDump` (`ready`, `pending` or `failed`, the waiting count and a reason) and children. |
| `Hole` | Region path and children. |
| `Leaving`, `LeavingRow` | A departing node, and a departing row with its key. |
| `Truncated` | The count of omitted lines. |

`Debug.json` writes keys in a fixed order. The top level is
`{"schema":"ranvier-view-dump/1","mount":…,"root":[…],"head":[…]}`; `mount` is null for a
`captureNode` dump, otherwise `{"prefix","phase","commits","failures"}`. Each node carries a `kind`
(`element`, `text`, `instance`, `list`, `arm`, `boundary`, `hole`, `leaving` or `truncated`) and the
fields of its case in camel case, such as `tag`, `attributes`, `properties` and `children` for an element,
and `name`, `kind`, `value` and `status` for a field. Attributes, properties and `head` are arrays of
`[name, value]` pairs. Fields add `node`, and `trace` in a traced build, when `NodeIds` is set.
