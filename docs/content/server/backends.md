---
title: F# HTTP backends
description: Serve the same Ranvier page with Giraffe, Oxpecker or Suave.
---

The [hydration example](https://github.com/shayanhabibi/Ranvier/tree/master/examples/Ranvier.Hydration)
uses Giraffe for its HTTP server. The Oxpecker and Suave companion sources below serve the same
shared page and browser bundle. The component, codec and hydration entry stay in
`examples/Ranvier.Hydration`; each adapter translates the request into props and sends the
rendered document as HTML.

## Build the shared browser bundle

From the repository root, with the .NET SDK from `global.json` and Node 26 or newer:

```shell
dotnet tool restore
npm --prefix examples/Ranvier.Hydration ci
npm --prefix examples/Ranvier.Hydration run build
```

This writes `examples/Ranvier.Hydration/dist`, including the browser entry at `assets/client.js`
and `style.css`. All three servers use those assets. The document's relative asset URLs work
at the root of each local server and under the documentation's `/Ranvier/examples/hydration/` path.

`HydrationPage.fromQuery greeting start` produces the request props. An empty greeting defaults
to `Hello from F#`; greetings are limited to 200 UTF-16 code units. A missing, invalid or out-of-range
start value defaults to `3`; accepted values are `0` through `1000000`.
`HydrationPage.render props` renders the page and embeds those same props for hydration.

## Giraffe

The main example uses Giraffe 8.3.0. Its handler passes the rendered string to `htmlString`:

```fsharp
let page: HttpHandler =
    fun next ctx ->
        let props =
            HydrationPage.fromQuery
                (string ctx.Request.Query["greeting"])
                (string ctx.Request.Query["start"])

        htmlString (HydrationPage.render props) next ctx
```

Run the [complete Giraffe source](https://github.com/shayanhabibi/Ranvier/tree/master/examples/Ranvier.Hydration/Server):

```shell
npm --prefix examples/Ranvier.Hydration start
```

Open [the local Giraffe page](http://127.0.0.1:5080/?greeting=Hello%20from%20Giraffe&start=7).
See [Giraffe's documentation](https://github.com/giraffe-fsharp/Giraffe/blob/master/DOCUMENTATION.md)
for its handler composition and response helpers.

## Oxpecker

Oxpecker's `EndpointHandler` receives the context directly. The adapter uses Oxpecker 2.1.1:

```fsharp
let page: EndpointHandler =
    fun ctx ->
        let props =
            HydrationPage.fromQuery
                (ctx.TryGetQueryValue "greeting" |> Option.defaultValue "")
                (ctx.TryGetQueryValue "start" |> Option.defaultValue "")

        ctx.Response.ContentType <- "text/html; charset=utf-8"
        ctx.Response.WriteAsync (HydrationPage.render props)
```

The [complete Oxpecker Program.fs](https://github.com/shayanhabibi/Ranvier/blob/master/docs/samples/server-backends/Oxpecker/Program.fs)
registers routing and Oxpecker, serves `/assets` through ASP.NET Core static-file middleware,
and exposes the stylesheet separately. Its small project references the shared hydration project:

```shell
dotnet run --project docs/samples/server-backends/Oxpecker
```

Open [the local Oxpecker page](http://127.0.0.1:5081/?greeting=Hello%20from%20Oxpecker&start=7).
When adapting the source to an existing ASP.NET Core F# project, add the package with
`dotnet add package Oxpecker --version 2.1.1` and reference the shared page project.
See the [official Oxpecker guide](https://github.com/Lanayx/Oxpecker/blob/develop/src/Oxpecker/README.md)
for endpoint routing, query values and HTML responses.

## Suave

Suave's `WebPart` reads query parameters from the request and returns a response through its
combinators. The adapter uses Suave 3.7.2:

```fsharp
let page: WebPart =
    fun ctx ->
        let query name =
            match ctx.request.queryParam name with
            | Choice1Of2 value -> value
            | Choice2Of2 _ -> ""

        let props = HydrationPage.fromQuery (query "greeting") (query "start")
        (OK (HydrationPage.render props) >=> setMimeType "text/html; charset=utf-8") ctx
```

The [complete Suave Program.fs](https://github.com/shayanhabibi/Ranvier/blob/master/docs/samples/server-backends/Suave/Program.fs)
serves the rendered page at `/`, allows the built `/assets/` files and stylesheet, and binds to
the loopback address:

```shell
dotnet run --project docs/samples/server-backends/Suave
```

Open [the local Suave page](http://127.0.0.1:5082/?greeting=Hello%20from%20Suave&start=7).
When adapting the source to an existing F# project, add `Suave` with
`dotnet add package Suave --version 3.7.2` and reference the shared page project.
Suave documents [request and response handling](https://suave.io/docs/request-response.html)
and [static files](https://suave.io/docs/file-handling.html).

## Check the server and browser boundary

Request a URL with a greeting and starting count, then view its HTML source: both values are
already present before JavaScript runs. Change the count in the browser, then reload; the
server supplies the query's starting value again. The local interaction is owned by the browser;
these examples do not store counter changes on the server.

The companion programs accept an optional absolute path to the built `dist` directory as their
first application argument. This is useful when copying the source outside this checkout.
Both serve the live route before any static HTML, so the exported snapshot cannot replace a
request-specific page.

The [hosted preview](../examples/index.md#server-hydration-preview) is a build-time snapshot on
GitHub Pages. Running one of these local servers demonstrates fresh HTML for each request.
See [page declarations](pages.md) and [state transfer](state-transfer.md) for the shared Ranvier APIs.
