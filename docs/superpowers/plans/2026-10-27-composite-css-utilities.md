# Composite CSS Utilities Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement task-by-task. Track evidence and decisions below.

**Goal:** Implement top-level composable typed F# utilities, reusable conditional builders, reactive application, and automatic static CSS extraction without application execution or manual registration.

**Architecture:** Portable utilities compose ordered style nodes; static and instance-dependent builder fragments keep top-level static declarations free of F# value-restriction issues. Browser conditions become CSS selectors/queries, application conditions become zero-specificity root class gates, and continuous readers use existing custom-property slots. A separate FCS build tool discovers typed style applications and interprets a documented deterministic subset of F# into the same model, preserving conditional alternatives without executing application initializers.

**Tech Stack:** F# / .NET 10 and net8 portable libraries; existing Ranvier.View and asset compiler; FSharp.Compiler.Service 43.12.403 for build-only extraction; Expecto, SageFs and existing browser/Fable checks.

**Spec:** `docs/superpowers/specs/2026-10-27-composite-css-utilities-design.md`

## Global Constraints

- Utilities are top-level declarations, used without registration. Preserve application order and CSS cascade semantics.
- Runtime work selects classes and updates typed variables; all stylesheet rules are static.
- Include all reactive alternatives. Uncertain reachability retains possible definitions; unsupported used evaluation produces source-located diagnostics.
- Preserve explicit scope, fallback declarations, shorthand/longhand order and importance. Never add hidden specificity.
- Reuse the existing worktree and retain previous uncommitted CSS work. Commands use RTK.
- Use SageFs for pure experiments, XML docs for public/internal declarations, and full build/test commands as acceptance gates.

## Review Focus

- Opposite application orders on different elements must have opposite winners without relying on HTML class order.
- Nested application conditions must gate descendants at the receiving root and avoid reading inactive variable branches.
- Independent condition flags must not require enumeration of their Cartesian product.
- Extracting one used utility must not execute an adjacent module initializer, signal reader, file access or unused throwing definition.
- Static and reactive utilities must compose across F# modules and references without generic value restriction or duplicate attribute writers.

## Task 1: Portable composition and conditional lowering

**Files:** modify `src/Ranvier.Css/Styles.fs`; create `src/Ranvier.Css/Utilities.fs`; modify its fsproj; create `tests/Ranvier.Css.Spec.Tests/UtilityTests.fs` and wire the test project/runner.

**Interfaces:** retain existing `Style.create`, `Styled.Definition` and variable rendering. Add static `Utility`, instance-dependent `Utility<'instance>`, `UtilityBuilder`, browser `UtilityConditionBuilder`, conditional application builder, and default auto-open `utility`, `minViewportWidth`, `hover`, `focus`, `media`, `supports`, `container`, scope and application-condition entry points. Static utility conversion returns `Styled<'instance>` without reading an instance; reactive conversion retains its typed readers. `Style.classNames instance style` returns root and active gate classes.

- [x] RED: prove responsive builder is missing in SageFs; prove the static/reactive CE overload shape avoids value restrictions.
- [x] Implement immutable composition fragments, overloads, nested browser conditions and typed query validation.
- [x] Extend style lowering with guarded branches, stable root gate identities and conditional readers; preserve previous APIs.
- [x] Verify ordered red/blue compositions differ; fallback/shorthand/importance order remains; gates add zero specificity and target the root before descendant suffixes; eight flags produce linear rules; inactive readers never execute.
- [x] Verify the exact agreed static `sm`/`md`/`lg`, `utility` nesting, finite helper loops and typed reactive properties.

## Task 2: Automatic build discovery and bounded deterministic evaluation

**Files:** create `tools/Ranvier.Css.Compile/` project, typed-source discovery/evaluator/CLI and README; create dedicated extraction tests/fixtures, including a referenced utility library; add solution/project entries.

**Interfaces:** consume utilities and existing `Compiler.compile` options. Expose project extraction with source-located diagnostics and produce existing `Compilation`/CSS manifest outputs. CLI accepts project, output directory and existing output modes, with no utility registry. Pin FCS 43.12.403 to match the SDK's exact FSharp.Core dependency.

- [x] RED: extraction fixture uses top-level utilities in styles applications, references a helper/library utility, contains an unused throwing declaration and a module initializer that would write a sentinel file; no CSS tool exists.
- [x] Load checked project implementation ASTs with assembly contents retained; index declarations and discover style applications by resolved symbols.
- [x] Interpret supported constants, bindings, function application, pure helper control flow and utility/CSS constructors. Ignore application initializers, defer runtime readers and diagnose unsupported used computations at their F# source ranges.
- [x] Follow referenced-source dependencies, include runtime alternatives and conservatively retain possible style roots on uncertainty. Document referenced-source/compiled-library limits explicitly; never silently omit unresolved styles.
- [x] Compile application compositions with the existing asset compiler. Preserve all output modes and write only after validation.
- [x] Verify no sentinel execution, deterministic repeated outputs, source-located unsupported live-state/I/O diagnostics and removal of proven-unused styles.

## Task 3: View sugar and reactive application

**Files:** modify `src/Ranvier.Css.View/Bindings.fs`, add attribute application builder in `UtilityBindings.fs`; extend view tests and browser CSS fixture. Existing View element constructors remain unchanged.

**Interfaces:** `styles { ... }` returns the existing attribute Part array for constructors, supporting `div (styles { card }) { ... }` and `input (styles { field })`. `CssView` applies the compiled ordered composition with a single class/style writer. Browser and server use the same identities and initial values.

- [x] RED: compile the agreed attribute syntax on both ordinary and void elements and assert tracked flags/values change classes/variables.
- [x] Add attribute style builder integration without changing existing view/text CE behavior.
- [x] Verify SSR, updates, disposal, nested conditions and continuous values; no rules/classes are regenerated on value updates.
- [x] Verify styles composition in opposite orders and mixed existing view children/attributes.
- [x] Merge generated classes with static/reactive user class attributes and property class names, preserving both constructor orders and void elements. Verify one tracked writer, SSR equivalence, HTML token semantics and disposal.

## Task 4: Runnable example, CI and final verification

**Files:** extend CSS example or add composite utilities example; update runtime/build documentation, CI and acceptance scripts as needed.

- [x] Provide a complete generated-bindings example using responsive conditions, nested utilities, runtime flags/typed variables and automatic CLI extraction.
- [x] Run the complete CSS tests and extraction tests; validate Fable and real browser behavior where new runtime paths require it.
- [x] Run formatting, XML documentation, comment hygiene and whitespace audits.
- [x] Obtain independent whole-feature review; fix correctness findings with regression coverage.
- [x] Record exact verification evidence and any supported-subset limits, then report the concrete result.

Final verification: Release Fable compilation succeeded; the final generated browser build passed both DOM and Chromium mount/update/disposal checks in both commit modes, including merged manual/reactive classes. The complete CSS suite passed 122 tests and the extraction suite passed 14 tests. Full View representation checks, binding freshness/type checks, importer checks, and all three utility CSS output modes passed. Supported compile-time evaluation limits are documented in the compiler README.

## Execution ledger

- Initial: user explicitly requested plan followed by implementation; proceed without another design gate.
- Initial: worktree already contains earlier uncommitted CSS work. Preserve its state and make all new changes reviewable in place.
- Initial: connected Serena/Rider cannot access this checkout. Installed FsLangMCP is being used via an isolated stdio bridge for semantic checks; SageFs bare session `2129084f` is owned by this task.
- Feasibility: SageFs confirmed separate static and instance-dependent CE fragments can combine without generic value-restriction errors. Missing responsive builder reproduced before implementation.
- Task 1: portable builders and guarded lowering passed pure SageFs checks and the complete CSS suite: `rtk dotnet test tests/Ranvier.Css.Spec.Tests -c Release` -> 115 passed, two existing Streams warnings. Ten builder tests, four conditional lowering tests and four view tests cover the new behavior.
- Task 3: additive element-content carrier, static/reactive styles applications and single-writer diagnostics implemented. Release View build and four real-assembly SageFs view checks passed. Real browser coverage added; Fable/browser gate running.
- User correction: styles belong in element attributes so void elements can accept them. Primary syntax is `div (styles { card }) { ... }` / `input (styles { field })`. The provisional child-content carrier is removed; existing tag constructors and child CEs remain unchanged.
- User requirement: utility classes must append/prepend declared user classes rather than overwrite them. Merge declared class sources at the common element-part boundary; preserve static fast paths, reactive dependencies and SSR behavior.
- Final gate caught a regression in existing View representation checks: ordinary duplicate class writers must still be rejected. Class merging is restricted to explicit composable utility-class contributions, preserving the existing diagnostic for unrelated duplicate attributes. The full representation suite provided the failing case before the correction.
- Task 2 complete: FCS typed-source interpreter and CLI passed the parent's fresh dedicated extraction gate: 14 tests, zero warnings. Source project references include a net8 library under a net10 root, and per-reference framework selection is tested. Binary-only implementations require source references; unsupported used code returns diagnostics.
- Review corrections verified: reactive empty CE branches, canonical optional-component sugar, integer truncation versus rounding, unsupported floating ranges, ordinary duplicate class diagnostics, and omitted versus explicitly empty class attributes.
- Full CSS suite after class composition: 122 tests pass. Full View representation/server/sugar/Arms checks pass after the scoped class marker correction.
- Actual example builds with zero warnings. `verify-utilities.mjs` passes automatic extraction, Lightning CSS parsing, CSS Modules exports, user class preservation, SSR manifest links and variable identities in all three layouts. The example emits 851 bytes of ordinary CSS and 1,043 bytes as a CSS Modules entry before processing.
- Existing importer compatibility: all 13 Node importer tests and compiled default/custom namespace CSS round trips pass after binding regeneration. Complete-corpus generated bindings freshness, positive compilation and negative type/range tests pass.
- New F# files pass formatting and XML documentation audits; source comment hygiene and whitespace checks pass. Existing unrelated Template documentation line-length findings are unchanged.
