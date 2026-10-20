---
title: Conditional arms
---

An **arm** is a component selected by a value of the instance. The region shows one arm at a time:
when the selection changes to another arm, the previous arm's cleanups run, its nodes leave, and the
new arm is set up in its place. When the selection keeps the same arm with a new payload, the arm
stays mounted and its bindings read the new payload.

The samples use the opens from [Templates and bindings](templates.md).

## `show` and `showElse`

```fsharp
type Disclosure = { Open: Signal<bool>; Body: string }

let disclosure =
    Component.template (fun _ (d: Disclosure) -> d) (
        div () {
            button (Events.onClick (fun d _ -> d.Open.Value <- not d.Open.Value)) { "Details" }
            show (fun d -> d.Open.Value) (p () { fun d -> d.Body })
        }
    )

let status =
    Component.template (fun _ (online: Signal<bool>) -> online) (
        span () {
            showElse (fun (online: Signal<bool>) -> online.Value) (b () { "online" }) (i () { "offline" })
        }
    )
```

`show condition content` renders `content` while `condition` holds. The content's instance is the
enclosing instance. A change to `false` runs the content's cleanups and removes its nodes; a change
back to `true` sets the content up again.

`showElse condition content fallback` renders one of two views. Each change of the condition remounts.

## `matchWith` over a union

```fsharp
type Load =
    | Idle
    | Loading of percent: int
    | Loaded of string

let private idleArm = armOf (span () { "Not started" })
let private loadingArm = armOf (progress (Attrs.attribute ("max", "100"), Attrs.attribute ("value", fun p -> Some (string (p ())))) { })
let private loadedArm = armOf (pre () { fun text -> text () })

let loader =
    Component.template (fun _ (state: Signal<Load>) -> state) (
        div (Attrs.className "loader") {
            matchWith (fun (state: Signal<Load>) -> state.Value) (function
                | Idle -> arm idleArm ()
                | Loading percent -> arm loadingArm percent
                | Loaded text -> arm loadedArm text)
        }
    )
```

- `armOf view` defines an arm whose instance is a reader of its payload, `unit -> 'payload`. Its
  payload type comes from the `arm` call that selects it.
- `arm definition payload` selects that arm with a payload.
- `matchWith read arms` reads a value from the instance and maps it to an arm. Writing `arms` as a
  `function` match lets the compiler check that every case has an arm.

Arms are compared by definition. `Loading 10` followed by `Loading 70` keeps the progress element and
updates its `value`; `Loading 70` followed by `Loaded "done"` remounts.

**Bind arm definitions with `let`**, at module level or before the template, as above. An `armOf`
written inside the `arms` function, or held in a `static member` property, creates a new definition on
every evaluation and remounts the arm each time. Fable Debug builds raise "a template definition was
created while a region was read" for a definition created inside the arms function.

## `branch` over a key

```fsharp
let private pageArm = armOf (h1 () { fun page -> "Page " + string (page ()) })

let pager =
    Component.template (fun _ (page: Signal<int>) -> page) (
        main () { branch (fun (page: Signal<int>) -> page.Value) (fun page -> arm pageArm page) }
    )
```

`branch key arms` remounts the arm whenever the key changes under `=`, even when the selected
definition stays the same. Use it when a new key must start with fresh setup state, such as a page
whose form state must not carry over to the next page.

## Server HTML

An arm region's start marker carries the selected arm's structural hash:

```html
<div class="loader"><!--[a:220727aa--><span>Not started</span><!--]--></div>
```

[Hydration](../server/hydration.md) adopts the server's arm when the client selects an arm with the
same hash; otherwise it re-renders that region alone and reports the mismatch. An arm with no nodes
renders the placeholder comment `<!--ranvier:row-->`.

## The explicit form

An arm definition is a component whose props are `unit -> 'payload`; `t.arm`, `t.matchWith` and
`t.branch` take it:

```fsharp
let idleExplicit =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.span [||] [| t.text "Not started" |])

let loadedExplicit =
    Component.define (fun _ (text: unit -> string) -> text) (fun t -> t.pre [||] [| t.text (fun (text: unit -> string) -> text ()) |])

let loaderExplicit =
    Component.define (fun _ (state: Signal<Load>) -> state) (fun t ->
        t.div
            [| t.Attrs.className "loader" |]
            [|
                t.matchWith (fun (state: Signal<Load>) -> state.Value) (function
                    | Idle
                    | Loading _ -> t.arm idleExplicit ()
                    | Loaded text -> t.arm loadedExplicit text)
            |])
```

`armOf view` is `Component.define (fun _ payload -> payload) (fun _ -> view)`. `show` is `matchWith`
over a `ValueOption` with a shared arm of zero nodes for the hidden case, and `showElse` is `matchWith`
over a `Choice`.
