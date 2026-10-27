---
title: Views
order: 1
---

:::info Alpha
The view packages share version `0.1.0-alpha.2`; their APIs may change.
See [installation and the dependency feed](../guide/installation.md#views).
:::

Ranvier binds [signals](../guide/signals.fsx) and [memos](../guide/memos.md) to HTML. A **template
component** has a template built once and setup run once per instance. Reactive reads update bound
text, attributes and properties while the surrounding DOM stays in place.

One definition supports three paths:

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

With `{ Start = 2; Label = "Clicks" }`, the component shows `Clicks` and a button labelled `2`.
Clicking changes it to `3`. `Create` owns the count; `View` binds it to the button.
Try this in the [browser editor](getting-started.md).

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

- [Typed CSS](css.md): generated property helpers, reactive values and preserved user classes.
- [Composite CSS utilities](css-utilities.md): reusable breakpoints, compositions and application conditions.
- [Extracting and serving CSS](css-build.md): static extraction, CSS Modules and single-file output.
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

[Native views](native.md) describes the in-development WebCore host and its current limits.
