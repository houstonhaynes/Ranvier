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

## Numeric expressions

Use the same `Css` helpers for numeric literals, arithmetic and math functions:

```fsharp
let calculated = utility {
    Css.width (Css.percent 100. - Css.rem 2.)
    Css.maxWidth (Css.min [Css.px 960.; Css.px 1280.])
    Css.fontSize (Css.clamp(Css.rem 1., Css.rem 1.25, Css.rem 2.))
    Css.opacity (Css.sin Css.pi)
}
```

The surface includes comparison, rounding, remainder, trigonometric, exponential and sign
functions, together with `e`, `pi`, `infinity`, `negativeInfinity` and `nan`. Function arguments
retain their dimensions. For example, `Css.sin (Css.px 10.)` does not compile.

Mixed length/percentage arguments work directly in tuple helpers, such as
`Css.min(Css.percent 100., Css.rem 60.)`. F# lists need one element type; use
`Css.lengthPercentage` when a list combines lengths and percentages:

```fsharp
let capped = utility {
    Css.width (Css.min [
        Css.lengthPercentage (Css.percent 100.)
        Css.lengthPercentage (Css.rem 60.)
    ])
}
```

Operators preserve dimensions. Multiplication and division support compound intermediate
dimensions, so `Css.px 20. / Css.px 40.` is a number, while a length multiplied by a length
cannot be assigned to `width`. An angle cannot be added to a length. The generated expression
types and property overloads come from the same generator and package as the other bindings.

For canonical grammar constructors that require a typed value, use a checked adapter such as
`Css.angle`, `Css.length`, `Css.number` or `Css.integer`. The adapter preserves the calculation:

```fsharp
let turn = Css.angle (Css.deg 30. + Css.deg 15.)
let turned = utility {
    Css.rotate turn
}
```

These adapters also work inside reactive readers. They check the destination production's
dimensions and percentage rules, and retain browser evaluation of the expression.

Calculations remain CSS expressions. The browser evaluates relative units, layout-dependent
percentages, special constants, integer rounding and computed-value range handling. A negative
literal can fail a constructor's range check; a calculation retains its expression for CSS's
computed-value rules.

Percentages retain their provenance even when division cancels their dimensions. For compound
arithmetic that needs an explicit percentage basis, use a basis helper:

```fsharp
let proportional = utility {
    Css.width (Css.lengthPercent 50. * Css.px 2. / Css.px 1.)
}
```

The property must permit that percentage basis. A number-only property does not acquire
percentage support merely because an expression eventually produces a number.

Typed numeric custom properties can participate in the same expressions:

```fsharp
let gutter = Css.lengthVariable "--gutter"

let variableWidth = utility {
    Css.width (Css.variable(gutter, fallback = Css.rem 1.) + Css.px 8.)
}
```

The handle declares the expected numeric type. It does not register an `@property` rule or
prove that every external declaration assigning the variable follows that type; substitution
and invalid-at-computed-value behavior remain browser responsibilities.

Newer draft forms include `Css.progress`, `Css.random`, `Css.calcMix`, `Css.siblingCount()`,
`Css.siblingIndex()`, structured interpolation and intrinsic sizing:

```fsharp
let intrinsicPanel = utility {
    Css.width (Css.calcSize(
        Css.intrinsicKeyword "auto",
        Css.intrinsicSize + Css.px 16.
    ))
}
```

`intrinsicSize` represents the scoped `size` keyword. It cannot be assigned directly as a
normal numeric result. The generated property overload must admit `calc-size()`, and its
destination grammar must admit the chosen intrinsic basis. Browser support for draft functions
varies; generating valid syntax does not add browser support.

## Reactive values

Use a reader when a property varies per component instance:

```fsharp
open Ranvier

type Meter = { Width: Signal<float> }

let meter = utility {
    Css.width (fun (state: Meter) -> Css.px state.Width.Value + Css.rem 2.)
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
