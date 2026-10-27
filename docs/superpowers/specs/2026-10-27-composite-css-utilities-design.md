# Composite CSS utilities

Status: implemented following user approval; verification evidence is recorded in the implementation plan.

## Intent

Provide reusable typed F# style compositions that can include other compositions, apply them under browser and application conditions, and compile ahead of time into efficient CSS for Ranvier.Views. The desired capability is composition and conditional application; Tailwind-compatible spelling is not a requirement.

The existing specification-generated CSS sugar remains the property/value API. The existing CSS domain model, view integration and asset compiler are the integration targets. The CSS importer can supply compositions later; adopting the Tailwind compiler is not required for this feature.

## Agreed authoring contract

- Utilities are top-level F# declarations. Components reference and compose them directly, without maintaining a separate registration list.
- A utility is a reusable typed composition. It may include other utilities and accept typed parameters, including utility-valued parameters where supported by the composition model.
- Conditions wrap complete compositions and may appear inside other utilities. Nested conditions mean conjunction.
- Browser conditions include selectors/states, viewport and container queries, feature queries and themes. Application conditions may depend on reactive values.
- A condition can combine browser and application state, such as hover on a wide viewport while an application flag is enabled.
- Fixed parameter values can contribute to static CSS. Continuously changing values use typed CSS custom properties. Finite reactive alternatives select precompiled styles.
- Ordinary F# helpers, calculations and iteration are permitted where their results can be resolved deterministically at build time. Unsupported computation produces an actionable build diagnostic; it must not silently cause runtime CSS generation.

The user approved the authoring direction below: named condition builders wrap declarations or other utilities; `utility` composes them; a `styles` block produces element attributes. The user clarified that attribute application is required so void elements accept styles. These are agreed target syntax examples; exact overloads and compiler constraints must be verified before claiming they compile.

```fsharp
open Ranvier.Css

let sm = minViewportWidth (Css.rem 40.)
let md = minViewportWidth (Css.rem 48.)
let lg = minViewportWidth (Css.rem 64.)

let bodyText = utility {
    Css.fontSize (Css.rem 1.)
    md { Css.fontSize (Css.rem 1.125) }
    lg { Css.fontSize (Css.rem 1.25) }
}

let contentWidth = utility {
    Css.width (Css.percent 100.)
    sm { Css.maxWidth (Css.rem 36.) }
    md { Css.maxWidth (Css.rem 48.) }
    lg { Css.maxWidth (Css.rem 64.) }
}

let spacious = utility {
    Css.padding (Css.rem 2.)
    Css.gap (Css.rem 1.5)
}

let card = utility {
    bodyText
    contentWidth
    Css.padding (Css.rem 1.)
    md { spacious }
}
```

A view application is `div (styles { card }) { "Responsive content" }`; a void element uses `input (styles { field })`. Nested browser conditions use the same shape, for example `md { hover { spacious } }`. Breakpoint thresholds are application-defined. Minimum-width conditions overlap, so all three can match at a large viewport; ordinary cascade and authored order determine the result. No resize subscription is required.

## Composition and cascade

Within the same condition and scope, later composition supplies later declarations. A derived utility can include a base utility and override selected properties by composing later declarations.

Applying multiple utilities to an element is also ordered composition. Applying `blueText` then `redText` must differ from applying `redText` then `blueText` when their equal-condition, equal-specificity declarations conflict. The compiler must implement this through stylesheet semantics; HTML class attribute token order alone does not change the cascade. This was explicitly confirmed in discussion decision 11.

Normal CSS cascade behavior remains authoritative across conditions: importance, layers, specificity and source order retain their CSS meanings. Composition order breaks source-order ties; it does not implicitly defeat a more specific selector. Do not add hidden specificity boosts to make a later unconditional declaration beat an earlier hover selector.

Preserve declaration sequences, including intentional fallback declarations. Shorthands and longhands interact according to CSS; simple deduplication by property name is insufficient. Optimization must preserve observable cascade behavior when independently reusable utilities are applied together.

Application condition changes must produce the same style result as selecting the applicable branches of the authored composition. Optimization must not make that result depend on generated class-name spelling or incidental traversal order.

## Scope

The default target is the element receiving the utility. Descendants, pseudo-elements, siblings and other relationships require explicit scope operations. Global selectors and global support rules must also be explicit; applying a utility must not silently introduce a reset.

Themes can be represented through reusable typed conditions and values. The initial design does not require a new textual theme language or a built-in Tailwind theme vocabulary.

## Build discovery and evaluation

The build discovers top-level utility definitions and their references from components. It follows utility dependencies and retains every alternative reachable through reactive conditions. There is no manual registration requirement.

Remove utilities only when the compiler can prove they are unused. If a reference cannot be resolved precisely, conservatively retain the possible definitions. Reachability uncertainty and inability to evaluate a used definition are different: the former may increase emitted CSS; the latter must produce a build diagnostic if it prevents static compilation.

Do not start the application, instantiate live components, subscribe to signals, or read current application state to discover styles. Reactive sources are runtime inputs to compiled bindings, not values to sample during the build.

Deterministic style evaluation may depend on declared build configuration, typed constants and supported helper computations. Ambient time, randomness, network access and mutable application state are not implicit style inputs. The compiler need not solve general F# program evaluation or prove arbitrary programs pure. Its supported evaluation boundary must be documented and tested; unsupported expressions must be identified at their source locations.

The mechanism for inspecting F# declarations/references and evaluating supported definitions is a technical decision for the builder. An initial feasibility step must establish it against real projects, helper functions and referenced libraries before committing to a broad implementation. A solution requiring users to maintain a separate registry would change the agreed product contract and must return for discussion.

## Generated CSS and runtime behavior

All CSS rules for supported utility definitions are generated ahead of time. Runtime work is limited to selecting existing classes, maintaining their bindings and updating typed custom properties. Do not compile styles, generate rules or insert stylesheets as reactive values change.

Generated class names are private implementation details. Users retain the typed utility references; they do not need to depend on class hashes or construct generated class names as strings.

The user additionally requires generated classes to compose with separately supplied user class names. Static and reactive user class attributes, including property-based class names, must survive utility application and condition updates. Merge class sources into one deterministic writer; preserve case-sensitive tokens and HTML whitespace semantics. Server rendering and browser updates must agree. This concerns declared class sources; arbitrary imperative DOM mutations are outside the binding contract.

Minimal runtime work is a requirement. Reduce stylesheet size where doing so preserves semantics; minimizing the number of classes on an element is secondary. The compiler may share declarations or emit composite classes, but must preserve cascade, scope, conditional behavior and required support rules. Do not require enumeration of every possible continuous value or every independent Boolean combination.

Reuse the asset compiler's supported file outputs, including a single CSS file and module-oriented output, subject to semantic preservation. Shared theme values, keyframes, registrations and other support rules remain with their consumers. Explicit global rules must not be accidentally scoped or renamed. Exact file partitioning, cache layout, hashing and selector rewriting are builder decisions, with dedicated tests.

## Diagnostics and implementation decisions

Build failures must identify the utility and the source expression that cannot be compiled. Cyclic composition, invalid static values and unsupported evaluation must not silently yield missing or runtime-generated CSS. Existing raw CSS/import escape behavior remains explicit and must not be presented as typed validation.

The following are builder decisions, not additional user agreements:

- Public API spelling, internal node types and serialized build artifacts.
- Supported initial F# evaluation forms and the analysis implementation, provided the boundary above holds.
- Conservative reachability granularity when exact references are unavailable.
- Optimizer strategy, stable identifier algorithm and caching.
- Debug CSS formatting, development diagnostics and source mapping implementation.

Start with correctness and useful diagnostics, then optimize against measured fixtures. No numeric size or runtime target has been agreed; measurements should report CSS bytes, generated rule/class counts and reactive update work.

## Acceptance evidence

The implementation plan must include checks demonstrating:

1. A top-level utility is consumed by a component without registration; a composed utility follows its dependencies across modules and a referenced library.
2. Proven-unused definitions disappear, while all reactive alternatives remain available.
3. Later composition overrides equal-condition declarations without breaking fallback sequences, shorthand/longhand behavior, importance or selector specificity.
4. Nested application/browser conditions behave as conjunction, and independently changing application inputs select the correct precompiled styles.
5. Fixed and reactive typed parameters produce correct CSS; continuous updates change variables without adding rules.
6. Descendant, pseudo-element and relationship scopes remain explicit; global rules are preserved intentionally.
7. Supported ordinary F# helper calculations compile deterministically; unsupported/live-state evaluation produces source-located errors.
8. Output is deterministic and integrates with existing asset modes and Ranvier.Views, including server-rendered initial styles and reactive updates.
9. Rule sharing preserves the cascade across multiple independently applied utilities and does not create an unnecessary combinatorial explosion.

## Boundaries

This design does not request Tailwind syntax compatibility, a built-in utility catalog, runtime CSS compilation, automatic execution of application code, or unrestricted evaluation of arbitrary F#. A future Tailwind/CSS import frontend may target the same composition model without defining its semantics.

All eleven discussion decisions are settled, and the user approved the condition-builder, utility and view-application syntax direction. The consolidated document records those agreements; no implementation has been made. A concrete implementation plan remains the next development step.
