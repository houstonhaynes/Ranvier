---
title: Components
---

A component is a `Component<'props>`: a **setup** function `Setup -> 'props -> 'inst` that runs once
per instance, and a **template** over `'inst` built once per definition. Every declaration style
below produces this value with one call to `Component.define`. Components of every style mount, render
on the server and hydrate the same way, and mix freely in one tree.

The samples share this props record and these opens:

```fsharp
open global.Ranvier
open Ranvier.View
open Ranvier.View.Sugar

type CounterProps = { Start: int; Caption: string }
```

## Declaration styles

| Rank | Style | Use it for | Instance annotations |
| --- | --- | --- | --- |
| 1 | A record with static `Create` and `View`, `Component.ofMembers` | Most components. | None. |
| 2 | `Component.template setup view` | Small and one-off components. | None when the instance is a record. |
| 3 | A class with a constructor and a static `View`, `Component.ofClass` | Constructor-style setup and private members. | Every instance lambda. |
| 4 | `Component.define setup (fun t -> ...)` | Templates that need `t.` members, or top-down typing. | None inside `t.` members. |

The README's
[Declaring components](https://github.com/shayanhabibi/Ranvier/blob/master/src/Ranvier.View/README.md#declaring-components)
section is the reference for each style, with the same counter written four ways.

### Record with `Create` and `View`

```fsharp
type Counter =
    { Count: Signal<int>; Caption: string }

    static member Create(_: Setup, props: CounterProps) =
        { Count = createSignal props.Start; Caption = props.Caption }

    static member View =
        div (Attrs.className "counter") {
            span (Attrs.className "caption") { fun c -> c.Caption }

            button (Attrs.attribute ("type", "button"), Events.onClick (fun c _ -> c.Count.Value <- c.Count.Value + 1)) {
                fun c -> string c.Count.Value
            }
        }

let counter = Component.ofMembers<Counter, _> ()
```

Inside a record's own static members, a field label resolves to that record: the lambdas need no
annotation, even for `Caption`, which `CounterProps` also declares. `Component.ofMembers<T, _> ()`
requires `static member Create: Setup * 'props -> T` and `static member View: TemplateView<T>`, and
reports FS0001 naming a missing member.

`View` is a property: each read builds a new template. `Component.ofMembers` reads it once, when the
definition is created. Read `View` only through the helper.

### `Component.template`

```fsharp
let badge =
    Component.template
        (fun _ (label: string) -> label)
        (span (Attrs.className "badge") { fun label -> label })
```

The view argument is evaluated once, where the definition is bound.

### Class with a constructor

```fsharp
type Switch(setup: Setup, initial: bool) =
    let on = createSignal initial

    member _.On = on

    static member View: TemplateView<Switch> =
        button (Attrs.attribute ("type", "button"), Events.onClick (fun (s: Switch) _ -> s.On.Value <- not s.On.Value)) {
            fun (s: Switch) -> if s.On.Value then "On" else "Off"
        }

let switch = Component.ofClass Switch
```

The constructor is the setup, and its `let` bindings are private to the instance. Annotate every lambda
that reads the instance with the class type; the annotation on `View` moves a missing-annotation error
to the line that lacks one. Under Fable each instance's setup allocates one two-element array for the
constructor's arguments, and each member read is a getter call.

### `Component.define`

```fsharp
type CounterState = { Count: Signal<int>; Caption: string }

let counterExplicit =
    Component.define
        (fun _ (props: CounterProps) -> { Count = createSignal props.Start; Caption = props.Caption })
        (fun t ->
            t.div
                [| t.Attrs.className "counter" |]
                [|
                    t.span [| t.Attrs.className "caption" |] [| t.text (fun (c: CounterState) -> c.Caption) |]
                    t.button
                        [|
                            t.Attrs.attribute ("type", "button")
                            t.Events.onClick (fun (c: CounterState) _ -> c.Count.Value <- c.Count.Value + 1)
                        |]
                        [| t.text (fun (c: CounterState) -> string c.Count.Value) |]
                |])
```

`build` runs once and receives a `TemplateBuilder<'inst>`. Free tags work inside `build` too, and
`t.` members carry the instance type into their lambdas. This is the form every other style lowers to.

## Setup

`setup` runs untracked, once per instance, under the instance's own [owner](../guide/roots.md).
Signals, memos and effects it creates belong to the instance and are disposed with it. `Setup` has two
members:

| Member | Meaning |
| --- | --- |
| `OnCleanup (fun () -> ...)` | Registers a cleanup that runs when the instance is disposed, before its nodes leave the document. |
| `CreateId ()` | A new id for this instance, unique within the render or mount. |

```fsharp
type Labelled =
    { Id: string; Text: string }

    static member Create(setup: Setup, text: string) =
        let id = setup.CreateId ()
        setup.OnCleanup (fun () -> printfn "disposed %s" id)
        { Id = id; Text = text }

    static member View =
        div () {
            label (Attrs.attribute ("for", fun l -> Some l.Id)) { fun l -> l.Text }
            input (Attrs.id (fun l -> l.Id))
        }

let labelled = Component.ofMembers<Labelled, _> ()
```

```html
<div><label for="r-0-0">Name</label><input id="r-0-0"></div>
```

On the server and in a mount with `SynchronousCommits` (the default), ids are positional: the root
prefix (`r` by default), the instance's path in the tree and a counter within the instance. A server
render and a hydration of the same template issue the same ids, and a hydrated `for` attribute matches
its input. A positional `CreateId` raises `InvalidOperationException` when called outside its own
instance's setup: after that setup returns, or during another instance's setup. A mount with
`QueuedCommits` issues ids that count across the mount, and these can differ from the server's.

Reads of a prop inside setup are a snapshot. Pass a `Signal`, a `Memo` or a reader in the props when
the child must follow a value.

## Child components

`render definition props` inlines a child whose props are read from the parent instance.
`renderOf definition value` inlines a child with fixed props.

```fsharp
type PanelProps = { Title: string }

type Panel =
    { Title: string; Hits: Signal<int> }

    static member Create(_: Setup, props: PanelProps) =
        { Title = props.Title; Hits = createSignal 0 }

    static member View =
        section (Attrs.className "panel") {
            h2 () { fun p -> p.Title }
            render counter (fun p -> { Start = 1; Caption = p.Title })
            renderOf switch true
            renderOf badge "fixed"
        }

let panel = Component.ofMembers<Panel, _> ()
```

- The child's markup joins the parent's template: one parent instance materialises its children in
  the same copy of static HTML.
- `render`'s props reader runs once per parent instance, untracked. A child that follows a parent
  value receives the signal or memo itself.
- The child runs its own setup under its own owner, and is disposed with the parent.
- In a class parent, annotate `render`'s props lambda with the class type.

### A component as a tag

A four-line class over `ComponentTag` gives a component a tag of its own, which takes either a props
reader or fixed props:

```fsharp
type badgeOf<'i> private (label: 'i -> string, primary: unit) =
    inherit ComponentTag<string, 'i>(badge, label)
    new(label: 'i -> string) = badgeOf<'i> (label, ())
    new(label: string) = badgeOf<'i> ((fun _ -> label), ())

let labels =
    Component.template (fun _ (count: int) -> count) (
        p () {
            badgeOf "new"
            badgeOf (fun count -> string count + " left")
        }
    )

let soldOut = Component.template (fun _ () -> ()) (badgeOf "sold out").View
```

Keep the shape exactly: a private primary constructor with a trailing `unit` parameter, and public
secondary constructors. Fable 5.18.0 compiles this shape, and the generated tags use it too. `render`
and `renderOf` cover the same cases without a class.

A component tag is a void element: inside a body it is yielded as it is, and anywhere else that takes
a view it is used through `View`, as `soldOut` above.

## Definitions are values

A definition is created once and reused: its identity is the component's identity. Bind every
definition with `let`, at module level or before the template that uses it. A definition created
inside a binding, an arm selector or a row builds a new template on every evaluation; Fable Debug
builds raise "a template definition was created while a region was read; bind it in the template's
build function" for a definition created inside a list or arm read.

## Rendering a component

| Call | Result |
| --- | --- |
| `Component.render definition props` | A `Program.View` that a mount runs: `Program.run (Component.render counter props) context.Backend`. See [Mounts and lifetimes](lifetimes.md). |
| `Server.render options definition props` | HTML. See [Server rendering](../server/rendering.md). |
| `Hydration.hydrate ... definition props` | A mount over server HTML. See [Hydration](../server/hydration.md). |
| `Component.renderWith ops definition props` | The instance expanded into another representation's `Operations`. |
