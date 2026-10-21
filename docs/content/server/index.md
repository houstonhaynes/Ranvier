---
title: Server rendering
order: 1
---

:::info Alpha
`Server` is in the `Ranvier.View` package and `Hydration` in `Ranvier.View.Dom`, both alpha releases
whose APIs may change. `Ranvier.Wire` holds the codecs for state transfer. See
[Installation](../guide/installation.md#views).
:::

A [template component](../view/components.md) renders to HTML on the server and becomes interactive
in the browser without being rebuilt:

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

## Server rendering and other hosts

- **Blazor Server** keeps one graph per circuit and renders through Blazor's components. See
  [Blazor Server](../guide/blazor-server.md). `Server.render` is independent of it: each call owns a
  graph for one request.
- **Elmish** components render on the server like any other component. See
  [Elmish components](../view/elmish.md#on-the-server).
