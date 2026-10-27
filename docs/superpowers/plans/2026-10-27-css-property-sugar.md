# Generated unified CSS property sugar

The user requested short, typed property helpers and unified static/reader overloads like the View sugar. Existing authorization skips design questions.

- Record safe conversion paths through resolved references, choices and transparent wrappers in the binding manifest. Preserve canonical range/required checks; stop at structural grammar nodes and cycles.
- Generate the default short surface directly in `Ranvier.Css`, exposing `Css.px`, shared keyword values, and overloaded `Css.width value` / `Css.width reader` members for each property. Retain the canonical API under `Ranvier.Css.Generated` for structural expressions. The user explicitly selected sugar as the default.
- Keep property-specific types and explicit unsafe boundaries. Generate deterministic names and retain obsolete annotations. CSS-wide values support static declarations, with dynamic inline declarations available through the existing View adapter.
- Replace verbose example/browser width chains with the sugar. Add compiler-positive and compiler-negative checks, bounded/recursive/collision fixtures and deterministic-generation tests.
- Regenerate the ordinary library, run the complete CSS suite and corpus compiler checks, then verify real Fable/browser updates and all CSS asset modes. Audit documentation/formatting and review the change.

## Completed

Implemented the default `Ranvier.Css.Css` surface, with inline shared primitives/keywords and unified static/reader property overloads. The canonical grammar surface remains `Ranvier.Css.Generated.Css`. Per-property auto-open extensions keep the complete generated corpus within compiler stack limits while preserving the short public calls. Singleton `||` alternatives and repetitions are lowered only through valid canonical factory calls; required pairs stay explicit.

Verification on 2026-10-27:

- Full CSS suite: 94 passed, none failed or ignored.
- `verify-bindings.mjs`: source and manifest freshness, full Release compilation/execution, short units/percentages/keywords/colors, cross-assembly extension discovery, reactive readers, bounded static and reactive values, negative dimension/keyword/string/CSS-wide-reader compilation, and all previous canonical checks passed.
- Full Fickl/Fable compilation passed. DOM and native Chromium checks passed under both commit modes, using the short helpers for width and inline CSS-wide colors. Class identities, stylesheet contents and disposal behavior are preserved.
- `verify-assets.mjs`: separate regular CSS, actual CSS Modules processing and combined output all passed, including example compilation, SSR links and initial variables.
- Handwritten source formatting, XML documentation audit and comment hygiene passed. Read-only independent review found no concrete blocker in traversal, inline extensions, naming or obsolete behavior.

SageFs friction: `send_fsharp_code` loading the generator and evaluating a generated fixture crashed its host with exit -1073741571; a fresh own host later stalled on the same load/evaluation path. The evaluation was cancelled and only the task's own session was stopped. The unified overload layout was verified in a small CLI F# experiment, followed by the complete compiler/runtime gates above. No daemon or unrelated session was stopped.

Changes are preserved uncommitted in the existing attached worktree.
