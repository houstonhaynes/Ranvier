---
title: Page declarations
---

`Page.Definition` groups a component, initial props, document shell and optional state codec.
The server renders it; a generated F# entry hydrates it in the browser.

Try the [hosted browser examples](../examples/index.md), including a hydration snapshot and
instructions for running its F# backend locally.

## Declare the page

This example uses `Counter.counter` and the shared `counterProps` codec from [State transfer](state-transfer.md).
Put the declaration in a shared module named `PageDeclarations`, compiled for both .NET and Fable:

```fsharp
open Ranvier.View
open Counter
open Shared

let counterPage: Page.Definition<CounterProps> =
    { Component = counter
      InitialProps = fun () -> { Start = 0; Label = "Clicks" }
      Document =
        { Server.DocumentShell.defaults with
            Lang = Some "en"
            Scripts = [ Server.ModuleScript.Src "/generated/CounterEntry.js" ] }
      Options = { Server.Options.defaults with Policy = Omit }
      State =
        Some
            { Id = stateId
              Encode = counterProps.Encode
              Decode = fun json -> counterProps.Decode json |> Result.mapError (fun error -> error.Message) } }

let renderPage () = Page.render counterPage

let clientSource = Page.clientSource "CounterEntry" "PageDeclarations.counterPage"
```

`Page.render` calls `InitialProps` once, encodes that value and renders the component with it. The existing state
writer escapes script delimiters. For request-specific data, use a record update that supplies a factory returning
the loaded props. Create reactive state inside component setup so each request owns and disposes its graph.

`State = None` is suitable for deterministic local inputs such as `unit`. In that case the browser calls the factory
again. With a codec, browser startup requires the embedded state and reports missing or invalid data instead of
substituting another initial value. The codec is an explicit pair of functions, so Ranvier.View does not require a
particular serialization package.

For the declaration above, `Page.render` produces a counter starting at `0` plus its encoded props.
The generated browser entry reads that state and attaches the same counter's interactions.

## Generate and compile

The build integration writes the result of `renderPage ()` as HTML and `clientSource` as `CounterEntry.fs`, then
compiles the entry with Fable. The generated source calls `Ranvier.View.Dom.PageClient.start` with the declaration;
Fable resolves its imports. Module and declaration names must be supported F# identifiers, optionally qualified. The integration
connects the compiled entry URL to the document's script reference and lets its bundler rewrite assets.

Keep the generated entries in a separate client project that references the shared declarations. The server
generator references the shared project, so a clean build can generate the entries before the client compiles.

`PageClient.start` hydrates the declared host using strict structural checking, matching the server's id prefix and
graph options. It returns a `Started` handle exposing `Mounted`, `Graph` and `Dispose`. Dispose that handle when
replacing the application within a document. Startup failure disposes the owned graph. Generated startup keeps the
handle in its `page` binding; it does not install an unconditional `pagehide` handler that would break cached pages.

Hydration currently uses synchronous commits. Explicit client mounting remains available when an application needs
queued commits or deliberately has no server-rendered body.

## Motion integration

The Motion example keeps each demo declaration in its own `*Page.fs` module, with shared layout in `PageLayout.fs`
and the server catalogue in `examples/Ranvier.Motion/Server/Site.fs`. Its server generator writes six documents,
five browser entry sources and a page manifest. Vite reads that manifest rather than
maintaining another list of pages. The documents and entries are generated outputs; CSS remains authored source.

Run `npm run build` for generation, browser compilation and bundling. `npm run dev` performs the same preparation
and regenerates after source edits before reloading. Missing output and rendering failures are errors, rather than
a switch to an empty client-mounted page. Hash routing applies the browser fragment after hydrating the server's
initial route, because fragments are not sent to the server.

The playground uses the same helper: ordinary panels hydrate; the scheduling panel mounts
explicitly to demonstrate commit modes. Its generated entry also connects the F# test controls.
See the [playground README](https://github.com/shayanhabibi/Ranvier/blob/master/examples/Fable.Ranvier.Playground/README.md)
for commands and manual fixtures.

The [lower-level deployment example](deployment.md) remains useful for applications that manage their own document
shell or browser lifecycle.
