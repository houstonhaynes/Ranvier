---
title: Getting started with views
---

Build a counter component, compile it with Fable and mount it into a page. The finished app is the
same shape as the [playground](https://github.com/shayanhabibi/Ranvier/tree/master/examples/Fable.Ranvier.Playground),
which is the runnable reference for everything in this section.

## Project

Reference `Ranvier.View.Dom`; it brings in `Ranvier.View` and `Ranvier`. The view packages are
currently available from source. Clone the repository beside your application:

```bash
git clone https://github.com/shayanhabibi/Ranvier.git
```

With `Counter/` and `Ranvier/` in the same directory, use this `Counter/Counter.fsproj`.
Add the Xantham feed from [Installation](../guide/installation.md#views) to the application's
`nuget.config` so the DOM type dependency restores.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Counter.fs" />
    <Compile Include="Main.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Ranvier/src/Ranvier.View.Dom/Ranvier.View.Dom.fsproj" />
    <PackageReference Update="FSharp.Core" Version="10.1.401" />
  </ItemGroup>
</Project>
```

Install the compiler as a local tool. These docs use Fickl 0.6.9, a Fable compiler fork:

```bash
dotnet new tool-manifest
dotnet tool install fickl --version 0.6.9
```

## The component

`Counter.fs` declares the props, the instance and the template on one record type:

```fsharp
module Counter

open global.Ranvier
open Ranvier.View
open Ranvier.View.Sugar

type CounterProps = { Start: int; Label: string }

type Counter =
    { Count: Signal<int>; Label: string }

    static member Create(_: Setup, props: CounterProps) =
        { Count = createSignal props.Start; Label = props.Label }

    static member View =
        div (Attrs.className "counter") {
            span (Attrs.className "label") { fun c -> c.Label }

            button (Attrs.attribute ("type", "button"), Events.onClick (fun c _ -> c.Count.Value <- c.Count.Value + 1)) {
                fun c -> string c.Count.Value
            }
        }

let counter = Component.ofMembers<Counter, _> ()
```

- `Create` is the **setup**. It runs once for each mounted instance, untracked, and returns the
  instance. The signal it creates belongs to the instance's owner and is disposed with it.
- `View` is the **template**. `Component.ofMembers` reads it once, when `counter` is bound, and
  every instance shares it.
- A string inside an element body is static text. A `fun c -> ...` is a text binding: it reads the
  instance and re-runs when a signal it read changes.
- `Events.onClick` receives the instance and the event.

The three open lines are the same in every view file: `Ranvier` for signals, `Ranvier.View` for the
template types, and `Ranvier.View.Sugar` for the lowercase tags, `Attrs`, `Events` and
`Component.ofMembers`. [Sugar](sugar.md) describes that vocabulary and its rules.

:::details The same component without the sugar
Every sugar form lowers to `Component.define setup build`, where `build` receives a
`TemplateBuilder` named `t` by convention. This definition renders the same HTML and mounts the
same resources:

```fsharp
type Counter = { Count: Signal<int>; Label: string }

let counter =
    Component.define
        (fun _ (props: CounterProps) -> { Count = createSignal props.Start; Label = props.Label })
        (fun t ->
            t.div
                [| t.Attrs.className "counter" |]
                [|
                    t.span [| t.Attrs.className "label" |] [| t.text (fun (c: Counter) -> c.Label) |]
                    t.button
                        [|
                            t.Attrs.attribute ("type", "button")
                            t.Events.onClick (fun (c: Counter) _ -> c.Count.Value <- c.Count.Value + 1)
                        |]
                        [| t.text (fun (c: Counter) -> string c.Count.Value) |]
                |])
```
:::

## Mounting

`Main.fs` creates a [graph](../guide/graph.fsx), mounts the counter into `#app` and returns a
function that disposes both:

```fsharp
module Main

open Fable.Core
open Fable.Core.TS.Dom
open global.Ranvier
open Ranvier.View
open Ranvier.View.Dom
open Counter

[<Global "document">]
let private document: Document = jsNative

let start () : unit -> unit =
    let graph = new Graph ()
    let host = document.getElementById "app" |> Option.get

    let report (errors: exn list) =
        for error in errors do
            JS.console.error ("Ranvier mount failure", error)

    let mounted =
        Mounting.mount graph None report host None (fun context ->
            Program.run (Component.render counter { Start = 0; Label = "Clicks" }) context.Backend)

    fun () ->
        mounted.Dispose ()
        graph.Dispose ()
```

`Mounting.mount graph parentOwner report host before build` builds the view under a new root owner
of `graph` and inserts it into `host` before `before` (`None` appends). `report` receives failures
that happen after the call returns, such as a throwing binding. The returned `Mounted` removes the
view's nodes, effects and listeners when disposed. [Mounts and lifetimes](lifetimes.md) covers the
arguments and the commit modes.

The mounted DOM is the template's HTML between two comment markers that delimit the mount:

```html
<!--ranvier:start--><div class="counter"><span class="label">Clicks</span><button type="button">0</button></div><!--ranvier:end-->
```

## The page

Compile the project to JavaScript and serve it with any bundler. With Vite:

```bash
npm install --save-dev vite
dotnet fickl Counter.fsproj --outDir generated
npx vite
```

```html
<!doctype html>
<html>
  <body>
    <main id="app"></main>
    <script type="module" src="/main.js"></script>
  </body>
</html>
```

```js
// main.js
import { start } from "./generated/Main.js";

const dispose = start();
if (import.meta.hot) import.meta.hot.dispose(() => dispose());
```

Each click writes `Count`; the button's text node changes, and the button keeps its identity.

## Try a component in the live editor

Press **Run**, then click the rendered button. Change the starting value or the increment and run
again to replace the preview. The first run on a page loads the browser compiler. Later runs on
the same page reuse the checker.

```fsharp live
open global.Ranvier
open Ranvier.View

let counter =
    Component.define
        (fun _ () -> createSignal 0)
        (fun t ->
            t.button
                [| t.Events.onClick (fun (count: Signal<int>) _ -> count.Value <- count.Value + 1) |]
                [| t.text (fun (count: Signal<int>) -> string count.Value) |])

let mounted = Ranvier.Docs.Maps.MapMount.body counter
```

`MapMount.body` is the docs' preview helper: it creates a graph, mounts the component into the
editor's output and returns a disposable handle. Your application uses `Mounting.mount` as above.

The editor supports ordinary Ranvier.View components and DOM bindings. Packages requiring a
compiler plugin, including `Ranvier.View.Arms` and its `Arms.With` / `Arms.Match` forms, are excluded.
Use the [standard conditional arms](arms.md) in the editor, or a local project for plugin forms.

## Next

- Render the same component on the server and hydrate it: [Server rendering](../server/index.md).
- Add rows and conditional content: [Lists and keyed rows](collections.md), [Conditional arms](arms.md).
- Declare components as classes or plain functions: [Components](components.md).
