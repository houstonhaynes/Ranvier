# Ranvier.View.Dom

The browser renderer for [Ranvier.View](../Ranvier.View/README.md) components, compiled with Fable.
`Mounting.mount` builds a component into a DOM element; `Hydration.hydrate` adopts its server HTML.
Both accept the same typed components, including Sugar declarations. See the
[installation guide](https://shayanhabibi.github.io/Ranvier/guide/installation/#views) for current source and feed requirements.

```fsharp
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

`counter` is the sugar component of the [getting started guide](https://shayanhabibi.github.io/Ranvier/view/getting-started/). The guide covers [mounts and commit modes](https://shayanhabibi.github.io/Ranvier/view/lifetimes/) and [hydration](https://shayanhabibi.github.io/Ranvier/server/hydration/).

## Explicit browser authoring

For new components, use the [Sugar syntax](../Ranvier.View/README.md#view-sugar) shared by server and
browser. The APIs below expose the browser operations directly and remain useful for custom adapters
and representation-specific work. Keep their `Generated.Html.Authoring` imports separate from Sugar;
both expose names such as `Attrs` and `Props`.

Open `Ranvier.View.Dom.Generated.Html.Authoring` for overloaded modifier helpers. The initial curated surface contains ten attributes and four live properties. Each accepts a fixed value, a `unit -> 'T` reader, `Signal<'T>`, or `Memo<'T>`, with native element constraints preserved. String and ARIA attributes also accept optional values/sources; `None` removes the attribute. The explicit helpers of `Generated.Html` cover the rest of the generated surface.

```fsharp
open Ranvier.View.Dom.Generated.Html.Authoring

Html.input [| Props.Input.value nameSignal
             Attrs.Input.placeholder "Your name"
             Attrs.title (fun () -> "Hello, " + nameSignal.Value) |]
Html.button [| Props.Button.disabled busyMemo |] [| text "Save" |]
```

`Props.Input.value` updates the live input value. `Attrs.Input.value` updates its default value attribute. Boolean attributes use presence/removal; ARIA booleans serialize `false`. Overloads select a binding at compile time and inline into the existing operations. Signal/memo overloads check for null and supply a deferred reader; they create no new owner or subscription mechanism. Annotate ambiguous literals such as `None` with their intended option type. These helpers are methods, not overloaded property setters.

The playground's [reusable component example](https://github.com/shayanhabibi/Ranvier/blob/master/examples/Fable.Ranvier.Playground/ComponentsExample.fs) mounts two counters with independent state, a shared reactive label, callbacks, and static or reactive child content.

`Authoring` binds the existing operations to `Program.View`; the generated `Html.Program` module provides all 112 typed HTML constructors without an `ops` argument.

```fsharp
open global.Ranvier
open Ranvier.View.Dom.Authoring
open Ranvier.View.Dom.Components

module Html = Ranvier.View.Dom.Generated.Html.Program
module Attr = Ranvier.View.Dom.Generated.Html.Attrs
module Events = Ranvier.View.Dom.Generated.Html.Events

type CounterProps =
    { Label: Prop<string>
      InitialCount: int
      Changed: int -> unit }

let counter props =
    createComponent
        (fun context props ->
            let count = Api.createSignal props.InitialCount

            Html.section [| Attr.classStatic "counter" |]
                [| Html.h2 [||] [| propText props.Label |]
                  Html.button
                      [| Attr.Button.typeStatic "button"
                        Events.onClick (fun _ ->
                            count.Value <- count.Value + 1
                            props.Changed count.Value) |]
                      [| text "Count: "
                        reactiveText (fun () -> string count.Value) |] |])
        props
```

Setup runs once per mounted instance. Use `context.OnCleanup` for external resources. Reactive readers update their bindings under the component's owner; reading a prop in setup captures its current value. `Constant` creates fixed prop text, while `Reactive`, `Prop.ofSignal` and `Prop.ofMemo` provide explicit reactive sources.

`fragment` combines child views. `keyedArray equals key read render` retains rows by key and gives each row a reader for its current item. `branch equals key read render` retains a selected instance until its key changes. Child and modifier authoring collections are arrays, as are keyed snapshots. Treat retained authoring arrays as immutable after creating a view.

The authoring functions and generated bound constructors are inline. Fable expands them to the existing operations and helpers. The emitted-JavaScript check verifies the wrapper functions disappear; it is not a throughput benchmark. Existing representation-neutral constructors remain available under `Generated.Html` for `Tree` and other operation sets.

Run `Program.run view context.Backend` inside the existing `Mounting` callback, or pass `Program.run view` to `Representation.Html.render` for a snapshot. Browser properties and listeners require the snapshot renderer's explicit `Omit` policy.

Static text and attributes create no reactive observers; mounted updates target explicit bindings.
The Program functions construct each instance's DOM during mounting. Use `Ranvier.View` templates
when sharing components with server rendering and hydration; the
[server guide](https://shayanhabibi.github.io/Ranvier/server/) covers that path.

`Program.View` is a backend delegate that Fable emits as a plain JavaScript function. Its nominal type lets saved views compile without extra type annotations. Modifiers are typed functions applied through the shared backend. Fable reuses the erased modifier array; .NET unwraps typed modifiers into a function array. Generated Program constructors inline directly into this path. Static property writes apply directly; browser hooks retain their owned cleanup.
