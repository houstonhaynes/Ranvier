# Phase 5 and Task 23 completion verification (2026-10-23)

The remaining Phase 5 implementation and Task 23 work are complete. This checkpoint distinguishes verified
local results from remote checks bound to a particular revision. Final-head CI and Perf results are published on
[PR #159](https://github.com/shayanhabibi/Ranvier/pull/159) and must pass before merge. The 2026-10-22 checkpoint
below is historical and superseded by this section.

## Completed implementation and documentation

- Commits `6ea3bc9b`, `c66c24df` and `a55700c0` complete the remaining inspection refinements: bounded mounted
  capture, definition/view labels, held-row keys and cleanup, nested mount prefixes, focused fragment-aware failure
  excerpts, pass guards, per-node observer checks, comment cleanup, and hydration/SVG boundary coverage.
  [The research note](../2026-10-21-dom-twin.md#phase-5-refinement-verification-2026-10-23) records each finding.
- Task 23 implementation `fa7c0cff` and measurement report `fed079a4` provide the twin/dump guide, compiled samples,
  reproducible loop and shared-case runners, and raw timing artifacts. [The measurement report](../2026-10-21-dom-twin.md#task-23-measured-development-loops-2026-10-23)
  separates process/tool costs from renderer timings and labels the bridge share as an estimate.
- Independent review verified all six loop-statistic rows, ten SageFs reset sums, shared-case statistics and the
  bridge estimate against the raw artifacts. The estimated bridge share exceeds the investigation threshold;
  generated bridge classes remain a separate Phase 7 design decision.
- Arms snapshot failure from the prior CI revision was fixed and locally verified in `ca6fd40f`.

## Current verification

- Fresh full traced/counter twin run: **1,061 passed, 12 ignored, zero failures**, unfiltered and sequential.
- Persisted focused verification: 42 Dump/Inspect/RowMetadata tests, nine shared dump cases and the fragment excerpt
  case passed. Nine golden cases passed after regeneration; scoped formatting, whitespace and XML audits passed.
- CI on `fa7c0cfff2d03835288a40abd63acd14c33b08a1` passed all checks except the Arms snapshot subsequently fixed
  in `ca6fd40f`. This is evidence for that earlier revision, not a final-head CI result.
- Fresh documentation build passed: 200 pages. Warnings remain for external-link reachability, duplicate HTML
  transforms and a JSX minification fallback; the builder reports the site correct but larger.
- Fresh Release no-define compilation, all six production hashes, exact generated-code exceptions, inspection
  absence and the real inspect positive control passed at `fed079a4`. All 14 gate-tooling tests passed.
- The completed inspect-enabled bundle adds 50,277 raw bytes, 14,435 gzip bytes and 12,051 Brotli bytes over
  combined. This development-build cost is separate from the production cost below; it supersedes the Task 22
  measurement. The production absence gate confirms the inspection implementation is eliminated without its define.

## Explicit production baseline and instruction comparison

The completion baseline is [bundle-production.json](bundle-production.json), with pristine master hashes retained
in [bundle-master-9651ffad.json](bundle-master-9651ffad.json). Historical `bundle-base.json` and `bundle-i1.json`
remain unchanged. This is an explicit baseline decision for head-placement failure routing and the additive
`Status.Stale`/`createEffectNode` core APIs, not a claim that historical hashes match.

[production-changes.json](production-changes.json) pins pristine master
`9651ffad38aed3ca3507bff5bbc09f18227645b8` and source revision `223eec64f717297839bae517d86a9f9b8d016cc9`.
It lists exactly `i1-mounting.diff` and the six `production-{api,bindings,core,projections,streams,timed}.diff`
patches. Gate 3 applies these complete patches to pristine output; other shipping body changes remain rejected.
No-define inspection absence remains a separate gate.

[production-cost.json](production-cost.json), measured with Vite 8.3.4 and Rolldown 1.2.14, records **+157 raw
bytes per bundle** and **+28–39 gzip bytes**. Combined grows from 219,718 to 219,875 raw bytes, 63,194 to 63,232
gzip bytes, and 55,322 to 55,404 Brotli bytes. These are the head-placement/core API costs with inspection disabled.

[Perf run 39884320577](https://github.com/shayanhabibi/Ranvier/actions/runs/39884320577) compared master
`9651ffad38aed3ca3507bff5bbc09f18227645b8` with `fa7c0cfff2d03835288a40abd63acd14c33b08a1` and passed.
The [report artifact](https://github.com/shayanhabibi/Ranvier/actions/runs/39884320577/artifacts/12472413287)
records all **71 Ranvier .NET instruction comparisons as Unchanged**. Exact base/head primitive medians are:

| Primitive | Base instructions | Head instructions |
|---|---:|---:|
| chain | 2,450 | 2,450 |
| cutoff | 111 | 111 |
| derive-effect | 765 | 765 |
| derive-on | 737 | 737 |
| update | 82,505 | 82,505 |
| update-on | 93,005 | 93,005 |

Both architecture verdicts have `Fails=false`: all 2,787 arm64 and 2,085 x64 metric findings are Unchanged.
Node instruction counts are reported but not gated because they depend on JIT history. Final-head CI is pending;
these measurements are bound to the revision above.

---

# Historical checkpoint (2026-10-22; superseded)

This checkpoint fixes the Phase 5 critical/important inspection findings and stops before Task 23. It is a draft checkpoint, not a merge-ready claim. The minor follow-ups are listed in the parent research note.

## Verification

- Persisted SageFs tests: Dump 16 and Inspect 9 passed. The owned session was stopped.
- Full Debug twin: 1,043 passed, 12 ignored, including normal golden verification after recording 38 text/JSON/HTML fixtures.
- Release twin with trace and counters: 1,044 passed, 12 ignored, unfiltered and sequential. The initial parallel run failed shared global-counter comparisons (1 failed, 34 errored); sequential execution prevents unrelated tests from changing those counters during assertions.
- Inspect jsdom conformance: 782 cases passed; all 92 shared artifacts compare byte-for-byte, including opaque classes with throwing methods/getters and canonical value kinds.
- Independent review approved the safe prototype classification and small-list elision fixes. Exception formatting additionally uses the trusted Fable exception prototype; the final host comparison covers its message.
- Whole solution Release build: zero errors, seven warnings. Unfiltered Release core 1,456, C# 93, and Wire 290 passed.
- Unfiltered traced Release core: 1,588 passed, 13 skipped, zero failures.
- Browser: all 31 Playwright tests passed after the app Release compile.
- Gate tooling: 12 tests passed; no-define inspection absence and the actual inspect positive control passed.
- XML compiler audit on the six edited F# files passed without FS3390. Six long-comment findings are unchanged preexisting lines. The comment-hygiene PowerShell runner was blocked by Windows execution policy; the static scan found no selected agent-comment signatures.

## Historical byte-equality gates remain blocked

The original `bundle-i1.json` baseline is retained. All six current bundle hashes differ from it. Gate 3 also rejects inherited shipping changes from `1ea136e3`: `src/Ranvier/{Core,Bindings,Projections,Streams,Timed}.js` status getters now include the stale flag; `Api.js` adds `createEffectNode`; Core also has a generated initialization-field line-number change. The exact `Mounting.js` exception passes against the pristine baseline. Existing conformance/harness files and `experiments/Ranvier.View.Testing/*` also differ from the historical baseline as expected for the expanded shared test suite; these are still rejected by the strict default allowlist.

Current SHA-256 values (measurement only, not a replacement baseline):

| Entry | SHA-256 |
|---|---|
| explicit | a8e64de7727749d118e5ba5110a6d146c9220e667497f40a7fa97f9b11680063 |
| explicit-combined | 6ee449c6c6890dfe26dab20ecc0ef6ef40ef3e5026c101934e79954e8939624d |
| sugared | 068ddbfe94115cd71fe13f3f68965d2f1546d21bdddc0ceed2f058f220492998 |
| members | 47721ac1fc4d5378c059c9fe933e8904cdbfa7c4139815ab58e26df0282645b3 |
| class | 349e3b808d20112f7481a2948103d4cdec25c84a5e1548eefa171bce4bf2fc56 |
| combined | d9ab3ed487801f8e32a01986726ec2933641bcbc2a59eb6399d6005783a483f9 |

Before merge: review an explicit exception or baseline decision for the public `Status.Stale`/`createEffectNode` changes, obtain the primitive instruction-count comparison, and pass CI. No historical hash or broad allowlist was silently replaced.
