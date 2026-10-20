---
title: Deployment
---

A server-rendered app compiles one set of component files twice: on .NET for the server, and with Fable
for the browser. This page assembles the counter from [Getting started](../view/getting-started.md)
into three projects, an ASP.NET Core server that renders the page and serves the client bundle, and a
Fable client that hydrates it.

## Projects

```text
Shared/   Shared.fsproj   Counter.fs, Shared.fs
Server/   Server.fsproj   ServerPage.fs, Program.fs, wwwroot/main.js (built by the client)
Client/   Client.fsproj   Main.fs, main.js, vite.config.js, package.json
```

| Project | Compiled by | References | Holds |
| --- | --- | --- | --- |
| `Shared` | .NET and Fable | `Ranvier.View.Portable`, `Ranvier.Wire` | `Counter.fs` from [Getting started](../view/getting-started.md#the-component) and `Shared.fs`, the codec from [State transfer](state-transfer.md#a-codec-shared-by-both-sides). |
| `Server` | .NET, `Microsoft.NET.Sdk.Web` | `Shared` | The page function and the endpoint. |
| `Client` | Fable, then Vite | `Shared`, `Ranvier.View.Foundation` | The hydration entry point. |

`Ranvier.View.Portable` holds `Server` and every template type; `Ranvier.View.Foundation` holds
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
    <ProjectReference Include="../Ranvier/experiments/Ranvier.View.Portable/Ranvier.View.Portable.fsproj" />
    <ProjectReference Include="../Ranvier/src/Ranvier.Wire/Ranvier.Wire.fsproj" />
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
    <ProjectReference Include="../Ranvier/experiments/Ranvier.View.Foundation/Ranvier.View.Foundation.fsproj" />
    <PackageReference Update="FSharp.Core" Version="10.1.401" />
  </ItemGroup>
</Project>
```

The `../Ranvier/` paths assume a checkout of the repository beside the app.

## The page

```fsharp
module ServerPage

open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Counter
open Shared

let options = { Server.Options.defaults with Policy = Omit }

let page (props: CounterProps) : string =
    let rendered = Server.renderPage options counter props

    $"""<!doctype html>
<html>
<head><meta charset="utf-8">{rendered.Head}</head>
<body>
<div id="app">{rendered.Body}</div>
{Server.stateScript stateId (counterProps.Encode props)}
<script type="module">import "/main.js";</script>
</body>
</html>"""
```

- The host element, `#app`, holds only the body: hydration's root marker is its first child.
- The state script sits outside the host.
- The client bundle loads after the body.

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

Each `Server.render` and `renderPage` call creates and disposes its own graph, and concurrent requests
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
<div id="app"><!--[r--><div class="counter"><span class="label">Clicks</span><button type="button">0</button></div><!--]--></div>
<script type="application/json" id="counter-props">{"start":0,"label":"Clicks"}</script>
<script type="module">import "/main.js";</script>
```

`/main.js` hydrates the counter: the button the server wrote stays in the document and counts clicks.

## Choosing a shape

| Shape | Use it when |
| --- | --- |
| Server render and hydrate | The first paint should come from the server, and the page then runs in the browser. |
| Client mount only | The page needs no server HTML: mount with [`Mounting.mount`](../view/lifetimes.md) into an empty host. |
| Server render only | The page is static: render with `Server.render` and send no client bundle. |
| [Blazor Server](../guide/blazor-server.md) | The UI runs on the server over a circuit, with Ranvier holding the circuit's state. |
