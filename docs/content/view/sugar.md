---
title: Sugar
---

`open Ranvier.View.Sugar` supplies lowercase tags, `Attrs`, `Props`, `Events`, `Hooks`, control flow
and component helpers. It produces the same template tree, server HTML and mounted resources as
the [explicit builder](templates.md#the-explicit-form).

The [view sugar README](https://github.com/shayanhabibi/Ranvier/blob/master/src/Ranvier.View/README.md)
contains the full vocabulary, diagnostics and costs.

## Opening the sugar

```fsharp
open global.Ranvier
open Ranvier.View
open Ranvier.View.Sugar
```

`open Ranvier.View` brings in the template types and the generated members of `Attrs`, `Props` and
`Events`, and `global.Ranvier` brings in `Signal`, `Memo` and `Api`. Two scope rules hold in a file that opens the
sugar:

- **A file uses either the sugar or `Generated.Html.Authoring`.** Both declare `Attrs` and `Props`.
  With `Authoring` opened after the sugar, `div (Attrs.className "x") { }` fails with FS0041; opened
  before it, the sugar's names shadow `Authoring`'s.
- **A local value named like a tag shadows the tag.** `let p = "note"` followed by `p () { "x" }` fails
  with FS0003. One-letter tags (`a`, `b`, `i`, `p`, `q`, `s`, `u`) meet this most often.

`Ranvier.View.Dom.Generated.Html`, the [authored layer's](authored.md) module of the same tag
names, requires qualified access (`open` of it fails with FS0892). An alias,
`module H = Ranvier.View.Dom.Generated.Html`, compiles beside the sugar's tags.

See [Opening the sugar](https://github.com/shayanhabibi/Ranvier/blob/master/src/Ranvier.View/README.md#opening-the-sugar)
in the README.

## Tags

Each of the 112 HTML tags is a class named by its lowercase tag name. `base'` and `option'` take an
apostrophe, because `base` is an F# keyword and a generic `option` would shadow `'T option`.

| Form | Meaning |
| --- | --- |
| `div (parts) { children }` | An element. `()` for no parts, `{ }` for no children. |
| `input (parts)` | A void element. It takes no body: `input () { "x" }` fails with FS0708. |
| `"text"` in a body | Static text. |
| `fun i -> ...` in a body | A text binding; `text read` is the same. |
| `from (fun i -> i.Signal)` | A binding over one signal or memo, for text or a part. |
| `for`, `if` in a body | Static structure, evaluated once while the template is built. |

A tag's parts are typed by its DOM interface: `span (Props.Input.value "x")` fails with FS0041. A void
tag is yielded as it is inside a body; anywhere else that takes a view, such as a template root or the
content of `show`, write `(img (parts)).View`. A [component tag](components.md#a-component-as-a-tag)
is used the same way.
[Templates and bindings](templates.md) walks through each kind of part.

The SVG tags `svg`, `g`, `path`, `circle`, `rect`, `text'`, `use'`, `title'` and the others create
SVG elements under both commit modes; `text'`, `use'` and `title'` take an apostrophe because `text`
is the text slot, `use` an F# keyword and `title` the HTML tag. See [SVG](motion-parts.md#svg) and
[Element names](templates.md#element-names).

## The inference rule

Each lambda in a sugared template infers its instance type from its own body. A lambda that reads a
record field declared only by the instance's record needs no annotation; annotate the instance type on
a lambda that reads a class member (a `Signal`, a `Memo`, a class component) or a record label that a
later record also declares. A missing annotation is reported at the element (FS0001, FS0193 or FS0041).
The README's
[Inference rule](https://github.com/shayanhabibi/Ranvier/blob/master/src/Ranvier.View/README.md#inference-rule)
states the rule exactly, with a table of cases.

Inside `Component.define setup (fun t -> ...)`, `t.` members carry the instance type into their
lambdas and mix with free tags, which suits a view that would otherwise need many annotations.
`t.isolated`, `t.view`, `t.Unsafe` and `t.Document` are written the same way; see
[Explicit-only members](templates.md#explicit-only-members).

## Control flow and components

| Forms | Page |
| --- | --- |
| `forEach`, `forEachFixed`, `each` | [Lists and keyed rows](collections.md) |
| `show`, `showElse`, `matchWith`, `branch`, `armOf`, `arm` | [Conditional arms](arms.md) |
| `boundary` | [Boundaries](boundaries.md) |
| `Component.ofMembers`, `Component.template`, `Component.ofClass`, `Component.define` | [Components](components.md#declaration-styles) |
| `render`, `renderOf`, `ComponentTag` | [Components](components.md#child-components) |
| An `Mvu` per component | [Elmish components](elmish.md) |

Bind definitions with `let`; declaration helpers read `View` once, and row bindings receive only
the row instance. Follow the linked pages for examples and diagnostics.

## Costs

Every sugar form except `Component.ofClass` mounts the same owners, effects, listeners and DOM writes as
its explicit twin. The README's
[Costs](https://github.com/shayanhabibi/Ranvier/blob/master/src/Ranvier.View/README.md#costs)
section gives the bundle size of each style and the per-instance cost of `Component.ofClass`.
