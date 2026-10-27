---
title: Typed CSS
order: 16
---

Write CSS with the same F# values you use to compose views. The short `Css` helpers check
property/value compatibility, utilities compose those declarations, and `styles` applies them
as element attributes. The build compiler emits static stylesheets.

```fsharp
open Ranvier.Css
open Ranvier.Css.View
open Ranvier.View.Sugar

let field = utility {
    Css.width (Css.percent 100.)
    Css.padding (Css.rem 0.75)
    Css.color Css.black
    focus { Css.color Css.blue }
}

// Inside a component's view:
// input (styles { field })
// div (styles { field }) { "Content" }
```

`styles` belongs in the constructor's attributes, so it works on void elements such as `input`
and `img` as well as elements with children. It generates stable class names and merges them
with declared user classes.

## Install

The CSS libraries target .NET 8 and .NET 10 and include Fable sources. Reference both the
generated property bindings and the view adapter:

```bash
dotnet add package Ranvier.Css.Bindings --prerelease
dotnet add package Ranvier.Css.View --prerelease
```

`Ranvier.Css` is their portable runtime dependency. `Ranvier.Css.Build` is the separate .NET
asset compiler library. The view adapter also depends on the view packages; retain the
[Xantham package source](../guide/installation.md#views) required by those packages.

For a source checkout, reference `src/Ranvier.Css.Bindings/Ranvier.Css.Bindings.fsproj` and
`src/Ranvier.Css.View/Ranvier.Css.View.fsproj`. See [extracting and serving CSS](css-build.md)
for the build command: adding a class to HTML does not load its stylesheet.

## Typed properties

Open `Ranvier.Css` for the default, shorter API:

```fsharp
let panel = utility {
    Css.display Css.block
    Css.width (Css.px 320.)
    Css.maxWidth (Css.percent 100.)
    Css.padding (Css.rem 1.)
    Css.fontSize (Css.rem 1.125)
    Css.opacity (Css.number 0.9)
}
```

Values carry grammar types: `Css.width (Css.deg 90.)` does not compile. Constructors also
validate supported numeric ranges. Values such as lengths, percentages, numbers and keywords
are shared where the property's grammar permits them; arbitrary strings are not silently
accepted as typed values.

Complex grammar expressions remain available through the detailed API at
`Ranvier.Css.Generated.Css`. Alias that module when you need its explicit grammar constructors:

```fsharp
module Canonical = Ranvier.Css.Generated.Css
```

Keep ordinary property authoring on the short `Css` helpers. The detailed API preserves required
groups, repetitions and other structures that cannot safely become one-value overloads.

## Reactive values

Use a reader when a property varies per component instance:

```fsharp
open Ranvier

type Meter = { Width: Signal<float> }

let meter = utility {
    Css.width (fun (state: Meter) -> Css.px state.Width.Value)
    Css.maxWidth (Css.percent 100.)
}
```

The emitted rule uses a generated custom property, for example `width:var(--r-…-0)`.
The view binding writes the current value to the element and tracks the signals read by that
reader. Updating `Width` updates the variable; it does not create a stylesheet rule. Disposal
releases the binding. Server rendering writes the current value with the same generated class
identity used by the browser.

Read signals **inside** the reader. Reading a signal while creating a top-level declaration
captures a snapshot instead of establishing a per-instance binding.

## User classes

Combine ordinary attributes and style attributes using the existing array API:

```fsharp
// Inside a component's view:
// div (Array.append [| Attrs.attribute ("class", "card analytics-target") |] (styles { panel })) {
//     "Both user classes and the generated style class are retained"
// }
```

The class merger preserves static and reactive user classes, deduplicates repeated tokens, and
updates the combined value when a contributing reader changes. Removing a conditional utility
class preserves the user's classes. CSS precedence comes from rule specificity and stylesheet
order, not the order of tokens in the HTML `class` attribute.

## CSS-wide keywords and escape hatches

Static CSS-wide declarations such as `Css.color Css.initial` are supported. For a reactive
choice of `initial`, `inherit`, `unset` or other CSS-wide keywords, use `CssView.inlineStyle`
with a reader returning declarations: routing those keywords through a custom property changes
their cascade meaning. Reserved F# names such as `inherit` require double-backtick escaping.

`Declaration.create` and explicitly unsafe canonical constructors allow unchecked CSS tokens.
Use them deliberately when the typed grammar cannot express the value. They do not validate
the value's CSS grammar.

## What the specs guarantee

Bindings are generated from the locked Webref corpus. Consolidation chooses the latest
same-series specification and retains superseded definitions with obsolete annotations. The
manifest records provenance, unresolved references and prose-defined productions.

The current corpus contains 4,882 definitions, including 160 productions without a parsed
grammar and two unresolved upstream references. Consuming every specification does not mean
every prose constraint or browser-support rule can be checked by F# types. Runtime rendering,
computed values and browser support remain CSS concerns.

Continue with [composite utilities](css-utilities.md) or [extracting and serving CSS](css-build.md).
