# Specification-driven CSS for Ranvier

`Ranvier.Css` provides portable values, ordered declarations, a stylesheet tree, deterministic component classes and reactive custom-property slots. `Ranvier.Css.Bindings` contains the ordinary generated F# source for every definition in the pinned CSS corpus. `Ranvier.Css.View` connects those styles to Ranvier templates. `Ranvier.Css.Build` is the separate .NET asset compiler.

Install `Ranvier.Css.Bindings` and `Ranvier.Css.View` from NuGet with `--prerelease`, or reference their projects from a checkout. The CSS numeric expression helpers are available in `0.1.0-alpha.2`; the view adapter requires `Ranvier.View` `0.1.0-alpha.2` or later for class contribution merging. The runtime, bindings and view adapter include Fable sources. `Ranvier.Css.Build` is .NET-only. Applications compile their normal F# references; the specification generator is a library-maintenance tool, not an application build prerequisite.

Read the [typed CSS guide](https://shayanhabibi.github.io/Ranvier/view/css/), [composite utility examples](https://shayanhabibi.github.io/Ranvier/view/css-utilities/) and [asset extraction guide](https://shayanhabibi.github.io/Ranvier/view/css-build/).

## Component styles

```fsharp
open Ranvier
open Ranvier.Css
open Ranvier.Css.View
open Ranvier.View

let progress : Styled<Signal<float>> =
    styled {
        Css.width (fun (signal: Signal<float>) -> Css.px signal.Value)
        Css.display Css.block
        Style.hover [Style.declarations [Css.width (Css.px 320.0)]]
        Style.media "(width >= 768px)" [Style.declarations [Css.width (Css.px 640.0)]]
    }

let component =
    Component.define
        (fun _ initial -> Api.createSignal initial)
        (fun t ->
            t.element (HtmlTag "div")
                (CssView.apply t.Attrs progress)
                [|t.text "Reactive CSS width"|])
```

The class name depends on the static CSS structure, never on signal values, object identities or registration order. Dynamic properties become static `var(--r-...)` references. Their instance readers use Ranvier's existing tracked template attribute binding. A signal update changes the element's custom properties; it does not create rules or class names. Disposal releases the existing component binding. SSR writes the current values using the same class identity. The DOM acceptance checks run real generated property helpers under both commit modes; a Chromium gate also checks native computed values and inline CSS-wide keyword semantics.

The default `Css` type lives directly in `Ranvier.Css`. Each property has overloaded static methods: `Css.width (Css.px 42.0)` creates a declaration, while `Css.width (fun state -> Css.px state.Width.Value)` creates a tracked style node. Shared primitives include `px`, `rem`, `percent`, `number`, `integer`, `deg` and `ms`; shared keyword values include `auto`, `block` and `red`. Generated nominal marker types prevent an angle, unrelated keyword or raw string from being accepted as width. Conversion paths use the canonical grammar factories, including their range checks. Structural productions such as required pairs are not flattened into single-value overloads. Reader parameters may need an instance type annotation, as in the example.

The detailed grammar API remains under `Ranvier.Css.Generated.Css`; alias it with `module Canonical = Ranvier.Css.Generated.Css` when a structural expression needs explicit construction. Its property modules retain `declaration`, `constant`, `reactive`, and `reactiveImportant`. `reactiveImportant` fixes importance in the stylesheet, rather than placing `!important` on a custom-property value. Opening `Ranvier.Css.Generated` selects that longer canonical API instead of the default sugar.

CSS-wide property keywords (`initial`, `inherit`, `unset`, `revert`, `revert-layer`, `revert-rule`) work as static declarations, for example `Css.color Css.initial`. F# reserved keyword names such as `inherit` require double-backtick escaping. For changing CSS-wide values, use `CssView.inlineStyle` with a reader returning these declarations. The sugar deliberately supplies no variable-backed reader overload for these keyword markers: substituting them through a variable changes their cascade meaning. The generated keyword registry comes from the current `all` property specification.

## Authoring methods

Use plain declaration lists, `declarations { ... }`, plain `StyleNode` lists passed to `Style.create`, or `styled { ... }`. `styled` accepts both static property declarations and reader style nodes from the unified helpers. `Style.declarations` groups static declarations inside nested node lists. Loops in these builders expand the static definition; changing collections belong in application state and variable readers.

For a global stylesheet use `Rule.style`, `Rule.media`, `Rule.supports`, `Rule.container`, `Rule.layer`, `Rule.keyframes`, `Rule.nested`, `Rule.descriptors`, and `Rule.statement`. `stylesheet { ... }` yields ordered rules. The tree preserves repeated property fallbacks, declaration importance and rule order. Generic at-rules and explicitly unchecked selector/token factories keep emerging syntax expressible. These constructors do not constitute a general CSS text parser.

## Composite utilities

Top-level utilities compose typed declarations and other utilities in authored order. Reusable conditions wrap either:

```fsharp
open Ranvier.Css
open Ranvier.Css.View
open Ranvier.View.Sugar

let md = minViewportWidth (Css.rem 48.)
let spacious = utility {
    Css.padding (Css.rem 2.)
    Css.gap (Css.rem 1.5)
}
let card = utility {
    Css.fontSize (Css.rem 1.)
    md { spacious }
    hover { Css.color Css.blue }
}

// Within a component's view:
// div (styles { card }) { "Content" }
// input (styles { card })
```

`media`, `supports`, `container`, `hover`, `focus`, `focusWithin`, `descendant`, `pseudoElement`, `selectorScope` and `theme` also create condition builders. `minViewportWidth` and `maxViewportWidth` validate fixed nonnegative length thresholds. Nested conditions mean conjunction. Minimum-width ranges overlap; normal CSS cascade and source order determine the winning declarations. Selector scopes are explicit, and descendant selectors must retain the receiving root.

`whenActive (fun instance -> instance.Enabled.Value) { ... }` gates an entire composition. Static utilities are nongeneric values reusable across component instance types; application predicates and generated property readers infer a typed `Utility<'instance>`. Runtime gates use precompiled zero-specificity root classes. Inactive branches skip nested readers and release their reactive dependencies. Value readers retain the existing typed custom-property behavior.

`styles { ... }` returns element attribute parts, supporting regular and void constructors. Generated classes merge with declared static/reactive user classes. A composed application's private class identity reflects declaration order, so reversing conflicting utilities reverses equal-specificity winners without relying on HTML class token order.

The [project compiler](../../tools/Ranvier.Css.Compile/README.md) discovers applications and emits existing asset layouts without executing application initializers or instance readers. It interprets a documented deterministic F# subset and reports unsupported used expressions. See the [complete example](../../examples/Ranvier.Css.Utilities.Example/README.md).

## Typed custom properties

`CustomProperty<'syntax>` shares the generated value's grammar marker. Create a variable with `CustomProperty.create "--progress"`, use `CustomProperty.reference` or `referenceWithFallback` in typed property constructors, and bind it through `Style.bind variable reader`. `Rule.register` emits `@property` with a supported registration syntax, inheritance flag and initial value. The caller must ensure the initial value meets the registration's computational-independence and grammar requirements.

## View options

- `CssView.apply attrs style` applies a generated class and current variables.
- `CssView.applyMany attrs styles` combines several styles with one writer for each attribute.
- `CssView.applyWithInline attrs extraClasses styles reader` combines existing class names, styles and additional inline declarations.
- `CssView.className` and `CssView.variables` expose those parts separately.
- `CssView.inlineStyle attrs reader` supports fully dynamic inline declaration lists, including CSS-wide keywords.
- `CssView.withStylesheets urls shell` appends ordered, deduplicated asset links to an SSR document shell.

Compose style parts through these combined helpers rather than attach competing writers for the same `class` or `style` attribute. Signal and memo reads belong inside readers; reading their values while defining a style captures a static snapshot.

See [asset compilation](../Ranvier.Css.Build/README.md) and the [runnable example](../../examples/Ranvier.Css.Example/README.md).

## Specification boundaries

The generated library covers 4,882 definitions from 122 locked raw extracts plus the canonical Webref package. Current definitions prefer newer same-series specifications; superseded items have compiler obsolescence attributes. The manifest records 160 prose-defined productions and two unresolved upstream `anchored-feature` references. Explicit unsafe constructors remain available for those gaps. Grammar typing does not infer every prose constraint, computed-value rule, property-specific identifier exclusion or browser support policy. Typed values remain distinct dimensions; a normal width constructor rejects an angle at compile time.
