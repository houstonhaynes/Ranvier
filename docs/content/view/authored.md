---
title: Other authoring layers
---

Use template components for shared server rendering and hydration. **Authored views** build DOM per
instance; **`Fable.Ranvier`** provides a smaller proof-of-concept helper layer.

## Authored views

An authored view is a `Program.View`: a function over the mount's backend. `Generated.Html.Program`
holds a constructor for each of the 112 HTML elements, `Generated.Html.Attrs` and the per-element
modules hold modifiers, and `Ranvier.View.Dom.Authoring` holds text, components, keyed arrays,
branches and boundaries.

```fsharp
open global.Ranvier
open Ranvier.View
open Ranvier.View.Dom.Authoring

module H = Ranvier.View.Dom.Generated.Html
module Html = Ranvier.View.Dom.Generated.Html.Program
module Attr = Ranvier.View.Dom.Generated.Html.Attrs

let rowsView (rows: Signal<(int * string) array>) : Program.View =
    Html.section
        [| Attr.classStatic "rows" |]
        [|
            Html.button
                [|
                    H.Button.onClick (fun _ ->
                        let next = rows.Value.Length + 1
                        rows.Value <- Array.append rows.Value [| next, "Item " + string next |])
                |]
                [| text "Add row" |]
            keyedArray fst (fun () -> rows.Value) (fun current ->
                Html.div [| Attr.idStatic ("row-" + string (fst (current ()))) |] [| reactiveText (fun () -> snd (current ())) |])
        |]
```

With an initial row `(1, "Alpha")`, the example shows **Add row** and `Alpha`. Clicking the button
adds `Item 2`; existing keyed rows stay mounted.

| Function | Meaning |
| --- | --- |
| `text value`, `reactiveText read` | Fixed and tracked text. |
| `fragment children` | Children in order. |
| `createComponent setup props` | A component whose setup runs once per mounted instance. |
| `keyedArray key read render` | Keyed rows; each row's `render` receives a reader of its current item. |
| `branch key read render` | One instance, kept while its key is equal under `=`. |
| `boundary pending failed content` | A boundary over authored views. |

`Ranvier.View.Dom.Generated.Html` requires qualified access; alias it as above.
`Generated.Html.Authoring` adds overloaded `Attrs` and `Props` modules for the authored layer, whose
members accept a value, a `unit -> 'T` reader, a `Signal<'T>` or a `Memo<'T>`. Keep that open out of
files that open the [sugar](sugar.md).

Run an authored view in a mount with `Program.run view context.Backend`, the same call that runs
`Component.render`. A static snapshot comes from `Representation.Html.render`, written in full beside the `Html` alias:

```fsharp
let snapshot (rows: Signal<(int * string) array>) : string =
    Ranvier.View.Representation.Html.render Omit (Program.run (rowsView rows))
```

```html
<section class="rows"><button>Add row</button><div id="row-1">Alpha</div></section>
```

### Authored views inside templates

A template embeds a `Program.View` through a view hole, `t.view`:

```fsharp
open Ranvier.View
open Ranvier.View.Sugar

type Card = { Caption: string; Body: Program.View }

let card =
    Component.define (fun _ (card: Card) -> card) (fun t ->
        div (Attrs.className "card") {
            h3 () { fun c -> c.Caption }
            t.view (fun c -> c.Body)
        })
```

The reader runs once per instance, untracked. The server writes the view between region markers;
hydration mounts a view hole fresh between those markers rather than adopting its nodes, and an
authored component's `CreateId` issues ids that can differ between server and client.

## Fable.Ranvier

`src/Fable.Ranvier` is a proof-of-concept library of DOM helpers. Nodes are created with ordinary F#
functions, and each binding is one Ranvier effect:

```fsharp
open Fable.Core.TS.Dom
open global.Ranvier
open Fable.Ranvier

let graph = new Graph ()
let count = graph.Run (fun () -> createSignal 0)
let host = Global.document.getElementById("app").Value

let mounted =
    Mount.mount graph host (fun () ->
        Dom.element "button"
            [ Dom.on "click" (fun _ -> count.Value <- count.Value + 1) ]
            [ Dom.reactiveText (fun () -> $"Count: {count.Value}") ]
        :> Node)
```

- `Mount.mount graph host factory` appends the factory's single root node and returns an
  `IDisposable` that removes the node and stops its bindings and listeners.
- Event handlers are batched by default: the DOM updates once when the handler's batch finishes.
- `Mount.mountWith { DomOptions.Default with Scheduling = Microtask } graph host factory` also defers
  DOM writes to a microtask.

The library has static child structure only: lists, conditional content, server rendering and
hydration belong to template components. Its [README](https://github.com/ranvier-org/Ranvier/blob/master/src/Fable.Ranvier/README.md)
documents each helper and the scheduling options.
