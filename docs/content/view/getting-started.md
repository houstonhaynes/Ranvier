---
title: Getting started with views
---

Build a counter component, compile it with Fable and mount it into a page. The finished app is the
same shape as the [playground](https://github.com/ranvier-org/Ranvier/tree/master/examples/Fable.Ranvier.Playground),
which is the runnable reference for everything in this section.

## Project

Reference `Ranvier.View.Dom`; it brings in `Ranvier.View` and `Ranvier`.
Use this `Counter/Counter.fsproj`:
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
    <PackageReference Include="Ranvier.View.Dom" Version="0.1.0-alpha.1" />
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

Press **Run**, then add a cup: one signal updates the quantity and a memo calculates the total.
Reset clears the cart. Try changing the price from `4` to `6`, the starting quantity, or the inline
styles, then run again. The first run loads the browser compiler; later runs reuse the checker.

```fsharp live
open global.Ranvier
open Ranvier.View
open Ranvier.View.Sugar

let price = 4

type CoffeeCart =
    { Count: Signal<int>
      Total: Memo<int> }

    static member Create(_: Setup, _: unit) =
        let count = createSignal 0
        { Count = count; Total = createMemo (fun _ -> count.Value * price) }

    static member View =
        div (Attrs.attribute ("style", "max-width:320px;padding:16px;border-radius:18px;background:#172b26;color:#f3faf5;font-family:system-ui;line-height:1.3")) {
            p (Attrs.attribute ("style", "margin:0;color:#a4d7b8;font-size:12px;letter-spacing:2px")) { "THE REACTIVE CAFÉ" }
            h2 (Attrs.attribute ("style", "margin:6px 0 3px;font-size:22px;color:inherit")) { "Coffee for the crew" }
            p (Attrs.attribute ("style", "margin:0 0 8px;color:#c6d9ce;font-size:13px")) { sprintf "Freshly brewed. $%d a cup." price }
            div (Attrs.attribute ("style", "display:flex;justify-content:space-between;align-items:baseline;margin-bottom:8px")) {
                span () { fun c -> sprintf "%d %s" c.Count.Value (if c.Count.Value = 1 then "cup" else "cups") }
                strong (Attrs.attribute ("style", "font-size:28px"), Attrs.aria ("live", "polite")) {
                    fun c -> sprintf "$%d" c.Total.Value
                }
            }
            div (Attrs.attribute ("style", "display:flex;gap:10px")) {
                button (
                    Attrs.attribute ("type", "button"),
                    Attrs.attribute ("style", "padding:8px 14px;border:0;border-radius:10px;background:#b9f3ce;color:#172b26;font:inherit;cursor:pointer"),
                    Events.onClick (fun c _ -> c.Count.Value <- c.Count.Value + 1)
                ) { "Add a cup" }
                button (
                    Attrs.attribute ("type", "button"),
                    Attrs.attribute ("style", "padding:8px 14px;border:1px solid #729783;border-radius:10px;background:transparent;color:inherit;font:inherit;cursor:pointer"),
                    Props.Button.disabled (fun c -> c.Count.Value = 0),
                    Events.onClick (fun c _ -> c.Count.Value <- 0)
                ) { "Reset" }
            }
        }

let coffeeCart = Component.ofMembers<CoffeeCart, _> ()

let mounted = Ranvier.Docs.Maps.MapMount.body coffeeCart
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
