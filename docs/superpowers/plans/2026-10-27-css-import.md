# CSS to Ranvier.Css import tool

Consume real CSS using pinned PostCSS and CSSTree parsers, then generate a deterministic F# module using the default sugar. Reuse a machine-readable sugar map from the specification generator rather than independently guessing generated names or accepting unsafe type conversions.

Preserve selectors, declaration fallback order, importance, nested rules, at-rules and comments. Emit explicit raw declarations for values without a supported typed conversion and opaque rules when the existing tree cannot preserve a construct's ordering. Report every fallback with source locations; strict typed mode rejects them before writing artifacts. Never hoist declarations or silently discard unknown syntax.

Add the minimal explicit opaque-rule escape to the portable tree; CSS Modules must diagnose opaque blocks they cannot safely rewrite. Verify parser behavior, CLI output preservation, compiled generated F# and CSS AST round trips. Run the full CSS tests, source/docs audits, and an independent review before completion.

## Completion evidence

Implemented `tools/Ranvier.Css.Import` with pinned PostCSS and CSSTree dependencies, a CLI, fallback reports and strict typed mode. The binding generator now exports authoritative helper names, accepted types and numeric range constraints. Generated F# binding source remains unchanged.

Validation completed:

- All 13 importer tests pass.
- All 97 CSS specification/runtime/build tests pass.
- Generated F# compiles for the default and a custom binding namespace.
- A complex CSS fixture round-trips through compiled F#, the runtime tree and asset compiler to an equivalent parsed CSS tree.
- Invalid syntax, strict-mode failures and path collisions preserve existing output files.
- Independent review findings about numeric constraints, escaped at-rules and custom namespace imports were corrected and re-reviewed without a remaining blocker.

Complex values remain explicit raw declarations; constructs that require exact source ordering remain opaque rules. Opaque rules are rejected by CSS Modules and by multi-module single-file compilation because those modes cannot safely rewrite their contents. No browser-support claim is made by parser acceptance.
