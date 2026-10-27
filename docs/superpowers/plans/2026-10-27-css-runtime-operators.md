# CSS runtime operators implementation plan

> **For agentic workers:** Use superpowers:subagent-driven-development or superpowers:executing-plans. Execute without design questions, as authorized by the user.

**Goal:** Generate typed runtime arithmetic from #233's numeric domain, integrate static and reactive property sugar and static extraction, and open a stacked PR against `codex/css-math-expressions`.

**Architecture:** Share the pure numeric domain with the portable runtime. An opaque calculation tree supplies serialization and contextual validation. The existing binding generator emits `CalculationExpression<unit, hint>`, numeric wrappers, operators, functions and property overloads into the same binding source. The default `Css` surface unifies primitives, calculations and properties; no separate `Calc` API or expression package.

**Tech stack:** F#, Fable, the current specification generator, FCS static extractor, Expecto.

**Spec:** The user's request in this chat and `2026-10-27-css-numeric-semantics.md`. Keep #233's domain policies authoritative.

## Global constraints

- Work in the existing isolated CSS checkout on `codex/css-runtime-operators`, based on #233 head `64c38a51cf28e974f420176454f6bbb51f29d85f`.
- Preserve existing canonical constructor signatures and value behavior. Default numeric `Css` helpers return wrappers that also inherit the corresponding `Value` grammar marker.
- Complete numeric-expression support includes named math functions, constants, every numeric unit family, percentage typing, and current draft contextual forms. Browser layout and computed-value evaluation remain browser responsibilities. Strings and URLs are outside this expression task.
- Static operators construct calculations, preserve grouping and hints, and never eagerly evaluate arithmetic. Property context validates the completed type. Out-of-range calculations retain computed-value handling.
- Preserve reactive tracking, variable slots, class identity and runtime/extractor serialization agreement. Extraction must never execute application readers or initializers.
- Compile on .NET 8/10 and Fable. Negative compilation cases must reject angle-as-width, keywords, incompatible addition and disallowed percentage contexts.
- Generate arithmetic signatures from the domain operator policies and primitive/property contexts. Check generated sources into the repository for bootstrap builds.
- Raw percentage helpers support ordinary arithmetic and the common dimension/percentage sums. Explicit dimension percentage helpers provide a basis for compound percentage algebra.

## Task 1: Portable calculation tree

- [x] Move the pure `NumericSemantics.fs` from the tool into `src/Ranvier.Css`, preserving its namespace; remove its tool compilation and add the runtime project reference to the tool. Extend the domain with current numeric draft policies and constants.
- [x] Add `Calculations.fs` after Values.fs. Public opaque `Calculation` and `CalculationRuntime` functions: `literal production unitName value`, `binary operation left right`, `negate value`, `render value`, `infer context value`, `declaration name context expected value`. Operations are strings `+`, `-`, `*`, `/`; reject unknown operations/productions. Numeric terminals are finite, percentage types resolve using context. Inference uses the shared domain and preserves compound intermediate units; declaration validates the final result before serialization.
- [x] RED/GREEN pure tests for parentheses, subtraction/division grouping, all dimensions, cancellation, percentage contexts, unitless zero, invalid units/type results, deferred range handling and reactive serialization. Use SageFs for the pure loop.

## Task 2: Generated expression API and property sugar

- [x] Extend the existing binding generator to emit numeric types, operators and unified `Css` helpers in its existing output. Generate measure names from `NumericSemantics.dimensions`, operators from domain policies, and primitive wrappers from canonical metadata. No separate generated output or package.
- [x] Hint states: `CalculationPlain`, `CalculationPercentage`, and `CalculationPercentBasis<basis>`. Plain arithmetic preserves measures; raw percentage arithmetic retains its hint; dimension-plus-raw-percentage creates the corresponding basis hint; established bases propagate and must agree. Provide explicit basis percentage primitives for arbitrary compound algebra. No operation can discard a percentage hint.
- [x] Use `NumericProjection` contexts to emit `Css` static and reader expression overloads. CLI signatures that erase to the same shape receive distinct optional marker types; callers supply only the expression/reader. Accept ordinary, mixed and raw percentage forms only where the domain permits their contextual result. Merge duplicate paths deterministically and preserve obsolete annotations.
- [x] Add compilation and rendering fixtures for `Css.width (Css.rem 2.0 + Css.px 8.0)`, `Css.width (Css.percent 100.0 - Css.rem 2.0)`, dimension cancellation, number/integer properties, math functions, multi-unit properties and reactive readers. Negative fixtures cover the global constraints.

## Task 3: Static extraction

- [x] Extend metadata with generated calculation primitive signatures and accepted expression result contexts.
- [x] Interpret unified Css primitives, functions, constants and expression operators into the shared opaque calculation tree. Use the same contextual declaration and serializer for extraction. Support generated overload defaults without invoking arbitrary application code.
- [x] Compare all output modes against runtime output; exercise composed utilities and reactive readers whose bodies would throw if executed at build time.

## Task 4: Documentation and acceptance

- [x] Document actual public syntax and percentage context rules with static, reactive and extracted examples. Explicitly show basis helpers for compound percentage algebra.
- [x] Regenerate the corpus bindings/manifest and runtime expressions. Verify determinism, all positive/negative compilation cases, .NET 8/10 and Fable behavior, full repository tests, required analyzer groups, XML docs and source comments.
- [x] Obtain a fresh review and fix actionable findings.

After verification, commit and push, then open and attach a PR with base `codex/css-math-expressions`. Preserve the worktree for PR feedback; do not merge either PR or publish packages as part of this request.

## Review focus

- Percentage hints must survive cancellation and cannot admit mixed percentages at independent-alternative or percentage-free sinks.
- Measure erasure must not create ambiguous property overloads or break Fable.
- Shared expressions and reactive readers must retain useful inference and reader tracking.
- Serialization must preserve subtraction/division grouping and be identical in extraction and runtime.
- Domain, generated operators, metadata and checked-in outputs must agree; unknown policies fail explicitly.

## Implementation notes

The runtime now shares schema version 2 of the numeric domain, including current Values 4/5 policies, typed variable sources, intrinsic sizing and structured interpolation. Checked numeric adapters bridge expressions into canonical grammar values. Opaque marker-typed storage lets numeric values preserve existing value semantics on both .NET and Fable. The extractor interprets an explicit generated factory allowlist; unsupported factories remain diagnostics.
