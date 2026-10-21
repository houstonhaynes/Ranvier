---
title: Views
order: 1
---

:::info Alpha
The view packages are alpha releases, version 0.1.0-alpha.1; their APIs may change. See
[Installation](../guide/installation.md#views) for the packages and their package source.
:::

Ranvier's view layer renders [signals](../guide/signals.fsx), [memos](../guide/memos.md) and
[effects](../guide/effects.md) as HTML. A view is a **template component**: a template built once
per definition, and a setup function that runs once per mounted instance. Each dynamic part of the
template reads the instance and writes one DOM node, attribute or property when the values it reads
change; the rest of the tree stays in place.

The same component definition renders in three places:

* **In the browser**, compiled with [Fable](../fable/index.md), mounted into a DOM element.
* **On the server**, on .NET or under Node, rendered to an HTML string. See [Server rendering](../server/index.md).
* **Over server HTML**, where the client [hydrates](../server/hydration.md) the server's nodes in place.

## A taste

```fsharp
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

`Create` runs once per instance and returns the instance record. `View` is the template: each
`fun c -> ...` inside it is a binding that reads the instance. A click writes the signal, and the
render effect writes the button's text.

## Layers and status

| Piece | Package or project | Status |
| --- | --- | --- |
| Template components, server HTML writer | `Ranvier.View` | Alpha. Compiles for .NET and Fable. |
| Builder sugar (free tags, control flow, declarations) | `Ranvier.View` (`Ranvier.View.Sugar`) | Alpha; the primary authoring style. See [Sugar](sugar.md). |
| DOM mounting, hydration, document head | `Ranvier.View.Dom` | Alpha. Runs under Fable. |
| Authored views (`Program.View`, `Generated.Html.Program`) | `Ranvier.View.Dom` | Alpha. See [Other authoring layers](authored.md). |
| DOM helper library | `src/Fable.Ranvier` | Proof of concept, not packaged. See [Other authoring layers](authored.md). |
| anime.js bindings, owners and signals for animations | `Fable.Ranvier.Anime` | Alpha. Compiles for .NET and Fable. See [Animation](motion.md). |
| Motion template parts | `Ranvier.View.Anime` | Alpha. See [Motion parts](motion-parts.md). |
| Wire codecs for state transfer | `Ranvier.Wire` | Preview. See [State transfer](../server/state-transfer.md). |
| Runnable reference | `examples/Fable.Ranvier.Playground` | Vite app and test suite. |

## Pages

- [Getting started](getting-started.md): a counter, from project setup to a mounted DOM tree.
- [Templates and bindings](templates.md): elements, text, attributes, properties and how bindings re-run.
- [Components](components.md): the four declaration styles, setup, ids and child components.
- [Lists and keyed rows](collections.md): `forEach`, `forEachFixed` and `each`.
- [Conditional arms](arms.md): `show`, `showElse`, `matchWith` and `branch`.
- [Boundaries](boundaries.md): pending and error views for async reads.
- [Events and hooks](events.md): handlers, element references, widgets and lent elements.
- [Document head](document-head.md): titles, meta and link entries owned by components.
- [Animation](motion.md): typed anime.js parameters, values, eases, staggers and timelines.
- [Owning and driving animations](motion-reactive.md): `own`, `track`, `scrub`, `drive` and the motion policy.
- [Motion parts](motion-parts.md): enter, exit, FLIP and signal-driven animation in templates.
- [Testing and costs](motion-testing.md): a manual anime clock in tests, and the bundle cost of animation.
- [Mounts and lifetimes](lifetimes.md): mounting, commit modes, failures and disposal.
- [Elmish components](elmish.md): an `Mvu` loop per component.
- [Sugar](sugar.md): the free-tag vocabulary, its inference rule and its compile errors.
- [Testing views](testing.md): HTML checks on .NET and DOM checks under Node.
- [Other authoring layers](authored.md): `Program.View` and `Fable.Ranvier`.
- [Troubleshooting](troubleshooting.md): error messages and their causes.
- [API reference](reference.md): the modules behind each page.

The [Server](../server/index.md) section covers `Server.render`, hydration, state transfer,
deployment and performance.
