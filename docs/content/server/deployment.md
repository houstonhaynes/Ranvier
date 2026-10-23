---
title: Deployment
---

A server-rendered app compiles one set of component files twice: on .NET for the server, and with Fable
for the browser. This page assembles the counter from [Getting started](../view/getting-started.md)
into three projects, an ASP.NET Core server that renders the page and serves the client bundle, and a
Fable client that hydrates it.

For generated HTML and browser startup from a shared F# declaration, see [Page declarations](pages.md).
The example below shows the lower-level rendering and hydration calls.

## Projects

```text
Shared/   Shared.fsproj   Counter.fs, Shared.fs
Server/   Server.fsproj   ServerPage.fs, Program.fs, wwwroot/main.js (built by the client)
Client/   Client.fsproj   Main.fs, main.js, vite.config.js, package.json
```

| Project | Compiled by | References | Holds |
| --- | --- | --- | --- |
| `Shared` | .NET and Fable | `Ranvier.View`, `Ranvier.Wire` | `Counter.fs` from [Getting started](../view/getting-started.md#the-component) and `Shared.fs`, the codec from [State transfer](state-transfer.md#a-codec-shared-by-both-sides). |
| `Server` | .NET, `Microsoft.NET.Sdk.Web` | `Shared` | The page function and the endpoint. |
| `Client` | Fable, then Vite | `Shared`, `Ranvier.View.Dom` | The hydration entry point. |

The `Ranvier.View` package holds `Server` and every template type; `Ranvier.View.Dom` holds
`Mounting` and `Hydration`, which run under Fable. `Shared.fs` is the state-transfer sample under
`module Shared` with `open Counter`, and exports `counterProps` and `stateId`.

```xml
<!-- Shared/Shared.fsproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Counter.fs" />
    <Compile Include="Shared.fs" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Ranvier.View" Version="0.1.0-alpha.1" />
    <PackageReference Include="Ranvier.Wire" Version="0.1.0-preview.1" />
    <PackageReference Update="FSharp.Core" Version="10.1.401" />
  </ItemGroup>
</Project>
```

```xml
<!-- Server/Server.fsproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="ServerPage.fs" />
    <Compile Include="Program.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Shared/Shared.fsproj" />
    <PackageReference Update="FSharp.Core" Version="10.1.401" />
  </ItemGroup>
</Project>
```

```xml
<!-- Client/Client.fsproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Main.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Shared/Shared.fsproj" />
    <PackageReference Include="Ranvier.View.Dom" Version="0.1.0-alpha.1" />
    <PackageReference Update="FSharp.Core" Version="10.1.401" />
  </ItemGroup>
</Project>
```

The view packages are on nuget.org; the app's `nuget.config` adds the temporary Xantham Cloudsmith feed
for their `Xantham.Fable.Core.TS` dependency, as [Installation](../guide/installation.md#views) shows.

## The page

```fsharp
module ServerPage

open Ranvier.View
open Counter
open Shared

let options = { Server.Options.defaults with Policy = Omit }

let page (props: CounterProps) : string =
    let document =
        { Server.DocumentShell.defaults with
            Lang = Some "en"
            State = [ stateId, counterProps.Encode props ]
            Scripts = [ Server.ModuleScript.Src "/main.js" ] }

    Server.renderDocument options document counter props
```

`Server.renderDocument options document definition props` renders the component as `renderPage` does
and writes the whole document around it:

- the component's [head entries](../view/document-head.md) inside `<head>`, after
  `<meta charset="utf-8">`;
- the host element, `<div id="app">`, holding only the body, so hydration's root marker is its first
  child;
- each state element after the host, outside it;
- each script after the state elements, so the client bundle loads after the body.

| `Server.DocumentShell` field | Default | Writes |
| --- | --- | --- |
| `Lang` | `None` | The `lang` attribute of `<html>`. |
| `HostId` | `"app"` | The id of the host element. |
| `State` | `[]` | One [`stateScript`](state-transfer.md#writing-the-state) element per id and JSON pair. |
| `Stylesheets` | `[]` | One `<link rel="stylesheet">` per URL, after the head entries. |
| `Scripts` | `[]` | `Server.ModuleScript.Src src` writes `<script type="module" src>`; `Server.ModuleScript.Inline text` writes the text inside `<script type="module">`. |
| `Nonce` | `None` | The `nonce` attribute of every script, state and stylesheet element, for a `Content-Security-Policy` of `script-src` or `style-src 'nonce-...'`. |
| `HtmlAttributes`, `BodyAttributes` | `[]` | Further attributes of `<html>` and `<body>`. An `id` among them joins the document's ids. |

Every attribute value is escaped. Script and stylesheet URLs are trusted author URLs, written as given
apart from escaping: pass only URLs the application controls. `Some null` for `Lang` or `Nonce` writes
nothing, as `None` does.

`renderDocument` raises `ArgumentException` before the render for:

- an id that is null, empty or contains ASCII whitespace (space, tab, line feed, form feed, carriage
  return);
- an id written twice among the host, the state elements and the `id` attributes of `<html>` and
  `<body>`;
- a state element whose JSON is null;
- an attribute name that a template rejects, or one that an element repeats;
- a nonce that is empty or holds a character other than ASCII letters, digits and `+ / = - _`;
- inline module text containing `</script` in any letter case, or `<!--`.

A failed render raises as [`renderPage`](rendering.md#failures) does.
`Server.renderDocumentTo options document definition props write` passes the same document to `write`
in segments, after a successful render.

## The endpoint

`Server/Program.fs` serves the page at `/` and the bundle from `wwwroot`:

```fsharp
module Program

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open ServerPage

[<EntryPoint>]
let main args =
    let app = WebApplication.CreateBuilder(args).Build ()

    app.UseStaticFiles () |> ignore

    app.MapGet ("/", Func<IResult> (fun () -> Results.Content (page { Start = 0; Label = "Clicks" }, "text/html")))
    |> ignore

    app.Run ()
    0
```

Each `Server.render`, `renderPage` and `renderDocument` call creates and disposes its own graph, and concurrent requests
each keep their own reactive state. State shared between requests (a cache, a configuration signal) is
read as it is; see [Rendering](rendering.md#a-render-is-one-request).

## The client

`Client/Main.fs` is the [hydration entry point](hydration.md#hydrating-a-component) under
`module Main`, with `open Counter` and `open Shared`. `main.js` calls it:

```js
// Client/main.js
import { start } from "./generated/Main.js";

start();
```

Vite bundles `main.js` into the server's `wwwroot` under a fixed name:

```js
// Client/vite.config.js
export default {
  build: {
    outDir: "../Server/wwwroot",
    emptyOutDir: true,
    rollupOptions: {
      input: "main.js",
      output: { entryFileNames: "main.js" },
    },
  },
};
```

## Build and run

```bash
cd Client
npm install --save-dev vite
dotnet fable Client.fsproj --outDir generated
npx vite build
cd ../Server
dotnet run
```

`GET /` returns the server-rendered counter with its props:

```html
<!doctype html><html lang="en"><head><meta charset="utf-8"></head><body><div id="app"><!--[r--><div class="counter"><span class="label">Clicks</span><button type="button">0</button></div><!--]--></div><script type="application/json" id="counter-props">{"start":0,"label":"Clicks"}</script><script type="module" src="/main.js"></script></body></html>
```

`/main.js` hydrates the counter: the button the server wrote stays in the document and counts clicks.

## Choosing a shape

| Shape | Use it when |
| --- | --- |
| Server render and hydrate | The first paint should come from the server, and the page then runs in the browser. |
| Client mount only | The page needs no server HTML: mount with [`Mounting.mount`](../view/lifetimes.md) into an empty host. |
| Server render only | The page is static: render with `Server.renderDocument` and no `Scripts`. |
| [Blazor Server](../guide/blazor-server.md) | The UI runs on the server over a circuit, with Ranvier holding the circuit's state. |
