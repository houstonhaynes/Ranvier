# Anime CSS bridge implementation plan

**Goal:** Animate numeric typed CSS custom properties through existing Anime parameters and reactive ownership.

**Architecture:** Fable.Ranvier.Anime references the small Ranvier.Css runtime. Typed `Anim.With` overloads retain the variable's grammar marker and validate numeric token metadata before passing serialized values to Anime. CSS expressions consume the animated variables in the browser.

**Scope:** Numeric custom properties first; expression evaluation remains the browser's job. JavaScript Anime animation works without registering a property. WAAPI registration and direct expression interpolation are separate work.

- [x] Expose `RuntimeApi.tryNumericValue : Value<'syntax> -> (float * string) option` with tests for literal metadata, copies, expressions and unsafe tokens.
- [x] Add `Anim.With(variable, value)`, `Anim.With(variable, start, finish)` and `Anim.With(variable, frames)` overloads in `src/Fable.Ranvier.Anime/Css.fs`. Validate every frame before mutating parameters, require matching unit suffixes, reject empty frames and nonnumeric tokens. Preserve normal parameters and other custom properties.
- [x] Add `Reactive.driveCss read element variable parameters` using the existing `AnimatableProp` timing object. Seed the first read exactly, retarget later reads through Anime's existing animatable object, reject unit changes before retargeting, and revert on owner disposal. Reuse the graph's effect and cleanup facilities; render animation frames without writing signals.
- [x] Add `Motion.followCss variable parameters read` as an element attribute, preserving the existing Motion exit lifecycle and cleanup. Share numeric checks with the core Anime bridge.
- [x] Verify compile-time marker mismatches, numeric endpoints/keyframes, variable-based expressions, reactive retargeting and disposal under the real Anime engine. Cover the existing Motion lifecycle with typed parameters. Add a concise usage example and document endpoint limitations.
- [x] Run pinned formatting, XML audit, meaningful Fable/browser tests and required repository gates. Review the final diff before opening the implementation PR.

**Review focus:** matching syntax with mismatched units; unsafe/compound endpoint values; preserving user classes and unrelated parameters; rapid target changes; restoring an existing custom property and its priority on disposal.

**Execution:** Implement inline, with failing checks before each behavior and REPL proofs for type/shape questions. The user has already requested implementation without further design questions.

**Review:** A separate reviewer found expression-valued defaults corrupting Anime's numeric interpolation in `Motion.followCss`. The regression failed in both commit modes before the fix. Seeding the first numeric token before creating the animatable resolved the failure; the full 68-case view suite passes and restores the original expression and priority. Reviewer verified the fix and reported no further concrete findings.

**Verification:** Anime 34/34 including actual generated sugars under Fable, view 68/68, server twins 9/9, five compiler-rejected dimension mismatches, native Chromium expression/pseudo-element checks, and bundle budgets pass. The full repository suite passed 3,470 tests with 12 existing skips; all three analyzer/build groups passed. XML audit reported zero findings. The standalone sample built without warnings and extracted CSS preserving `calc(var(--card-gap, 0px) * 2 + 10px)`. SageFs proved metadata provenance and generic overload shape; its reload build hit existing `TraceModel.fs` missing `Json` diagnostics, so persisted assemblies were checked through a normal Release build for that step.
