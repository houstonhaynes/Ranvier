---
title: Testing views
---

A view has two kinds of test. **HTML checks** render a component with `Server.render` and compare the
string; they run on .NET in any test framework and need no DOM. **DOM checks** mount a component into
a document under Node with jsdom, compiled with Fable, and drive it with events.

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
