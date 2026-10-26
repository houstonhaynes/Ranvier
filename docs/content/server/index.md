---
title: Server rendering
order: 1
---

:::info Alpha
`Server` lives in `Ranvier.View`; `Hydration` lives in `Ranvier.View.Dom`. These alpha projects
are not currently published on nuget.org: use [project references or local packages](../guide/installation.md#views).
`Ranvier.Wire` supplies state-transfer codecs.
:::

A [template component](../view/components.md) renders HTML on the server; the browser runs setup
again and adopts matching nodes to make them interactive.

![Render a shared component on the server, transfer its props as JSON, then hydrate the same component in the browser.](/Ranvier/diagrams/view-server-hydration.svg)

The three steps are:

1. **Render.** `Server.render` runs the component's setup in a new graph, writes its template as HTML,
   and disposes the graph. It runs on .NET and under Fable.
2. **Transfer.** The page carries the props the client needs, encoded as JSON in a
   `<script type="application/json">` element.
3. **Hydrate.** In the browser, `Hydration.hydrate` runs the same component over the server's nodes:
   setup runs again, each binding writes its first value only where it differs from the server's node, and the
   mount keeps the server's elements.

```fsharp
// Server
let html = Server.render { Server.Options.defaults with Policy = Omit } counter { Start = 0; Label = "Clicks" }
// <!--[r--><div class="counter"><span class="label">Clicks</span><button type="button">0</button></div><!--]-->

// Client
let mounted = Hydration.hydrate graph None report Hydration.HydrationOptions.defaults host counter props
```

## Pages

- [Rendering](rendering.md): `Server.render`, `renderTo`, `renderPage` and `renderDocument`, the HTML they write, escaping,
  browser-only parts and pending reads.
- [Hydration](hydration.md): what the client adopts, mismatches and `Strict`, ids, boundaries and the
  document head.
- [State transfer](state-transfer.md): `Server.stateScript`, `Hydration.readState` and `Ranvier.Wire`
  codecs.
- [Deployment](deployment.md): one component project compiled for the server and the client, and the
  document `Server.renderDocument` writes around it.
- [Performance](performance.md): what the server and hydration benchmarks measure, and the figures.

- [Page declarations](pages.md): generate the document and browser entry from a shared definition.
- [F# HTTP backends](backends.md): serve the same page with Giraffe, Oxpecker or Suave.

## Server rendering and other hosts

- **Blazor Server** keeps one graph per circuit and renders through Blazor's components. See
  [Blazor Server](../guide/blazor-server.md). `Server.render` is independent of it: each call owns a
  graph for one request.
- **Elmish** components render on the server like any other component. See
  [Elmish components](../view/elmish.md#on-the-server).
