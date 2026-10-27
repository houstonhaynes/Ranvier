# Tailwind syntax adoption investigation

Investigated 27 October 2026. Official documentation currently identifies itself as v4.3. A live `npm view tailwindcss version` query returned 4.3.3, and the local probe installed that exact version. This note distinguishes upstream facts from the proposed Ranvier integration.

## Finding

Tailwind syntax can be adopted. The recommended compatibility path is a build-time bridge: explicit class candidates plus the project's Tailwind entry stylesheet → Tailwind compiler → CSS → the existing Ranvier.Css importer. Keep Tailwind responsible for utility semantics; keep the importer responsible for expressing the resulting CSS with typed sugar where supported and explicit CSS fallbacks elsewhere.

This is a recommendation based on the upstream behavior below, not an implemented production integration. A small independently implemented syntax subset is feasible, but should have a separate compatibility contract rather than claiming full Tailwind support.

## What must be parsed and resolved

Tailwind candidates include static utilities (`flex`), functional utilities (`w-1/2`), slash modifiers (`bg-red-500/50`), trailing important markers (`p-4!`), arbitrary utilities (`[color:red]`), and variant chains. Its parser receives a design system; parsing and resolving available utility roots are coupled. Variants include static (`hover`), functional (`aria-disabled`), compound (`group-*`, `peer-*`, `has-*`), and arbitrary selectors (`[&_p]`). The implementation uses delimiter-aware segmentation, rather than blindly splitting every colon or slash. Prefixes are interpreted through the design system as well. [Candidate parser and types](https://github.com/tailwindlabs/tailwindcss/blob/main/packages/tailwindcss/src/candidate.ts)

Arbitrary syntax adds another language inside the token: `top-[117px]`, `[mask-type:luminance]`, `fill-(--brand-color)`, selector variants, and type hints such as `text-(length:--size)`. Underscores normally decode to spaces, but URLs and escaped underscores have special treatment. A regular expression over prefixes cannot preserve those rules reliably. [Arbitrary values, properties, variants and ambiguity rules](https://tailwindcss.com/docs/adding-custom-styles)

Named utilities are not defined by CSS specifications alone. Theme variables determine available colors, fonts, breakpoints and other utility families. Tailwind also emits ordinary CSS variables for those design tokens; compiling a class without retaining its required theme definitions can leave it ineffective. [Theme variables](https://tailwindcss.com/docs/theme)

Custom `@utility`, `@custom-variant`, legacy JavaScript `@plugin`, and `@config` inputs extend the language and its meaning. Compatibility therefore requires the project's actual entry stylesheet and dependencies, not merely a class string. JavaScript configuration options from v3 are not all supported in v4. [Functions and directives](https://tailwindcss.com/docs/functions-and-directives)

## Compiler integration boundary

The package root exports `compile(css, options)`, returning `build(candidates: string[]): string`, together with source metadata. Core compilation accepts stylesheet/module loader callbacks. Candidate parsing is an internal source module, absent from the package export map. Access to the design system is explicitly named `__unstable__loadDesignSystem`. Thus the CSS-producing compiler is the preferable adapter boundary; direct candidate/design-system access would require tighter version coupling. This is an assessment of the exported source, not a promise from upstream that the JavaScript compiler API never changes. Pin versions and test the adapter. [Compiler implementation](https://github.com/tailwindlabs/tailwindcss/blob/main/packages/tailwindcss/src/index.ts), [package exports](https://github.com/tailwindlabs/tailwindcss/blob/main/packages/tailwindcss/package.json)

`@tailwindcss/node` wraps compilation with filesystem/module resolution and dependency callbacks. It is a practical Node build adapter for real entry stylesheets, avoiding a new implementation of Tailwind's resolution behavior. Keep it behind a small versioned bridge rather than exposing its types as Ranvier's public domain model. [Node compile wrapper](https://github.com/tailwindlabs/tailwindcss/blob/main/packages/%40tailwindcss-node/src/compile.ts)

The compiler sorts rules by variant and property order, with further tie-breaking. It also skips invalid candidates. Therefore class-list order must not be translated into a sequence of declarations whose last item wins. A bridge must preserve the compiler's stylesheet order and provide separate diagnostics for missing candidates. Compiling each candidate independently and concatenating outputs would also lose the global sorting contract. [Candidate compilation and ordering](https://github.com/tailwindlabs/tailwindcss/blob/main/packages/tailwindcss/src/compile.ts)

## Source discovery, reactive values and asset behavior

Tailwind scans source as plain text. Complete literal class names can be discovered in F# sources, but interpolated names such as `bg-{color}-500` cannot. Explicit candidates avoid source-language extraction issues. The documented `source(none)` plus `@source inline(...)` path can feed an explicit candidate inventory through supported CSS configuration; brace expansion is supported there. [Source detection and explicit candidates](https://tailwindcss.com/docs/detecting-classes-in-source-files)

Recommended Ranvier behavior: use statically known candidate sets for reactive class switching. For continuously changing dimensions/colors, retain the existing typed reactive CSS-variable mechanism, optionally referenced by an arbitrary Tailwind utility. Do not imply that a build can compile class names first created at runtime. This recommendation follows the source-discovery constraint above.

A full `@import "tailwindcss"` includes theme, Preflight resets, utilities, and layer ordering. Preflight can be excluded by importing theme and utilities separately. Make that a visible build option: importing Tailwind syntax should not silently change headings, lists, margins or other global defaults. Retain layer declarations even when omitting the reset. [Preflight and selective imports](https://tailwindcss.com/docs/preflight)

Recommended asset behavior: initially preserve compiled class tokens and ship one ordered Tailwind stylesheet, alongside generated Ranvier F# rules if requested. Class renaming or conversion to automatic Ranvier class names requires selector-aware rewriting plus references to group/peer markers and arbitrary selectors. Do not infer that importing CSS into the domain model automatically provides safe renaming or per-component splitting. Keep shared theme variables, registrations, keyframes and other support rules with their consumers.

## Narrow native alternative

A Ranvier-owned convenience grammar could cover selected utilities and simple state/responsive wrappers, then emit existing typed declarations directly. Its mapping must explicitly define spacing scales, colors, breakpoints, conflicts and unsupported syntax. Reuse the CSS binding manifest for value validation, but recognize that CSS specs cannot supply Tailwind's theme vocabulary or utility expansion rules. Prefer this only if the desired product is a compact Ranvier DSL with intentionally limited Tailwind resemblance.

## Local proof findings

An isolated probe installed `tailwindcss@4.3.3` with lifecycle scripts disabled under the importer's ignored `out/tailwind-probe` directory. No production dependencies or implementation changed.

The probe compiled the default Tailwind entry stylesheet plus a custom theme token, utility and variant, supplying 18 candidates together:

```text
flex p-4 w-[240px] md:hover:bg-red-500/50 [&>svg]:size-4
group-hover:opacity-50 peer-checked:block dark:text-white @sm:grid
-mt-2 font-bold! [--brand:#123456] bg-[var(--brand)] animate-spin
made-up-utility bg-brand content-auto theme-dark:block
```

Observed results:

- Compilation emitted 6,783 bytes of CSS, including custom theme/utility/variant output, `@property` and animation keyframes. The intentionally unknown `made-up-utility` was silently omitted.
- Reversing the candidate input list produced identical CSS, confirming that candidate input order does not define cascade order in this fixture.
- The existing importer generated 53 typed sugar declarations and 51 raw declarations, with zero opaque-rule fallbacks. These counts include Preflight and support rules, not just utility rules. They are fixture coverage, not a general Tailwind typing percentage.
- Generated F# compiled and ran against the real binding library. Both `Stylesheet.render` and regular per-module asset compilation succeeded.
- A normalized PostCSS/CSSTree comparison found the rendered CSS tree equal to the Tailwind compiler output, including rule order, selectors, declaration priority, comments and at-rules. Equivalent numeric spellings were normalized.

This proves the compiler-to-importer path for the tested fixture. It does not prove arbitrary plugin compatibility, browser rendering, CSS Modules class rewriting or automatic Ranvier class renaming. Those require dedicated integration tests. Strict typed import cannot currently accept this fixture because Tailwind's variable/function/descriptor values require explicit raw declarations.

Suggested production scope: a build-time adapter accepting an entry stylesheet and explicit candidate inventory, with optional source scanning and reset inclusion; one shared ordered stylesheet; an optional generated F# rules module; and diagnostics that distinguish unsupported candidates from valid marker classes. Keep compiler versions pinned and make theme/plugin dependency changes invalidate the build cache.
