# Priorities

Work order for the open GitHub issues, as of 2026-10-24. The ralph loop takes the first row whose
status is `todo` or `in-progress`, and updates the row when it finishes an iteration.

Order follows the priority and dependency sections of the issues themselves: the milestone 3
(Agent-observable reactive core) P0 items first, ordered by their stated dependencies, then the P1
spike that consumes them, then unmilestoned perf work. The milestone 1 (1.0.0-rc, due 2026-10-31)
items are exploratory, with no acceptance criteria; the loop produces a research note and a
proposed scope for each, and does not implement them.

| # | Issue | Priority | Depends on | Kind | Status |
| --- | --- | --- | --- | --- | --- |
| 1 | [#164](https://github.com/shayanhabibi/Ranvier/issues/164) perf(trace): traced/untraced performance and memory baselines | P0 foundation | — | implement | done |
| 2 | [#167](https://github.com/shayanhabibi/Ranvier/issues/167) feat(graph): public awaitable graph dispatch | P0 | — | implement | done |
| 3 | [#165](https://github.com/shayanhabibi/Ranvier/issues/165) feat(trace): incremental event cursors and checkpointed truncation | P0 | #164 baseline | implement | todo |
| 4 | [#166](https://github.com/shayanhabibi/Ranvier/issues/166) feat(trace): stable diagnostics and provenance contracts | P0 | #165 | implement | todo |
| 5 | [#168](https://github.com/shayanhabibi/Ranvier/issues/168) spike(mcp): agent-observable graph | P1 | #165, #166, #167 | implement | todo |
| 6 | [#157](https://github.com/shayanhabibi/Ranvier/issues/157) perf(arms): restore the 5% Chromium target | — | quiet machine for timings | implement | todo |
| 7 | [#69](https://github.com/shayanhabibi/Ranvier/issues/69) Myriad expansion for traced and untraced primitives | 1.0.0-rc | — | research | done |
| 8 | [#65](https://github.com/shayanhabibi/Ranvier/issues/65) event sourcing adapters for traced builds | 1.0.0-rc | #165, #166 | research | todo |
| 9 | [#66](https://github.com/shayanhabibi/Ranvier/issues/66) core MCP tooling | 1.0.0-rc | #168 | research | todo |
| 10 | [#68](https://github.com/shayanhabibi/Ranvier/issues/68) persistent memory cache adapters | 1.0.0-rc | — | research | done |
| 11 | [#70](https://github.com/shayanhabibi/Ranvier/issues/70) exploratory use cases in LLM stacks | — | — | research | done |

Statuses: `todo`, `in-progress` (an iteration left work; see the log), `done` (merged into
`integration/issues`), `blocked` (the log names what it needs from the user).

## Conflicts with the issue tracker

- Milestone 1 is due 2026-10-31 and its four issues have no acceptance criteria. Milestone 3's P0
  items are concrete and #65, #66 and #69 depend on them, so they rank higher here. Moving
  #65/#66/#68/#69 out of the 1.0.0-rc milestone, or moving its due date, is the user's call.
- #157 needs paired Chromium timings on a quiet machine. The loop implements and runs the
  correctness gates, and marks it `blocked` on the timing run.

## Stack

All iterations run in the `Ranvier-integration` worktree. Each issue gets an `issue/<n>` branch cut
from the tip of the stack; an iteration whose gate passes pushes the branch and opens or updates a
PR on the branch below it. Merge bottom-up.

| Branch | Issue | PR | Base |
| --- | --- | --- | --- |
| integration/issues | — | https://github.com/shayanhabibi/Ranvier/pull/170 | master |
| issue/164 | #164 | https://github.com/shayanhabibi/Ranvier/pull/172 | integration/issues |
| issue/167 | #167 | https://github.com/shayanhabibi/Ranvier/pull/176 | integration/issues |
| issue/69 | #69 | https://github.com/shayanhabibi/Ranvier/pull/175 | integration/issues |
| issue/70 | #70 | https://github.com/shayanhabibi/Ranvier/pull/174 | integration/issues |

## Log

Each iteration appends one line: date, issue, outcome, commit, next step.

- 2026-10-24, #164: plan steps 1 to 13 done on `issue/164`; baseline at `f48a3a7` in `docs/.ai/benchmarks/tracing/f48a3a7/`, budget in `docs/content/benchmarks/tracing.md`. Gates passed: `build.fsx -- test` (unfiltered), the traced test stage (five test projects and `docs/maps/tests` with `-p:RanvierTrace=true`), `bench/Ranvier.Counters.Tests` (81), and `counters.ps1 -NoPmc` parity against `integration/issues` (`fdfaa4c`): identical bytes/op and library counters in 71 .NET and 51 Node.js Ranvier rows. Commit: the gate record on `issue/164`. Next: push `issue/164` and open its PR on `integration/issues`.
- 2026-10-24 #164 in-progress — plan steps 1 to 13 done but the format gate fails on a build-script bug (fantomas command line over the Windows 32,767-character limit; glob misses fable_modules) — 785594e8 — next: fix Stage.format in build.fsx to batch files and exclude fable_modules, then rerun the gate
- 2026-10-24 #164 done — format gate passes after batching the fantomas command line (cherry-pick of 88feefcd from #171) and excluding fable_modules — 44d33371 — next: #167
- 2026-10-24 #69 research note written — docs/.ai/research/2026-10-24-issue-69.md — b6125ef5 — next: user decides scope
- 2026-10-24 #70 research note written — docs/.ai/research/2026-10-24-issue-70.md — 92585c4e — next: user decides scope
- 2026-10-24 #68 research note written — docs/.ai/research/2026-10-24-issue-68.md — c0e402fe — next: user decides scope
- 2026-10-24 #167 gate passed — plan steps 1 to 12 done on `issue/167` after review fixes (disposal faults queued `DispatchAsync` tasks; `CancellationToken` overload; batching, `Serialised`, Fable, AOT, C# and sample coverage; contracts.md and async-graph.md). Gates: `build.fsx -- test` unfiltered (Ranvier.Tests 1476, CSharp 95, Twin 1067, Wire 290, Maps 76; 0 failed), traced Ranvier.Tests (1608 passed, 13 skipped), `build.fsx -- test-fable` (untraced and traced, inline and promise), `format --dry-format`, NativeAOT publish win-x64 traced and untraced with no IL warnings — 4d62da4b — next: integrate `issue/167` on `issue/164`
- 2026-10-24 #167 in-progress — gate failed: netstandard2.1 build of `src/Ranvier/Core.fs(1555)` rejects `SetCanceled token` (FS0501; overload is .NET 5+), net10.0 build and tests pass — 9c1707f0 — next: replace `SetCanceled token` with `TrySetCanceled token` (or a parameterless call) for netstandard2.1, then re-run the gate
- 2026-10-24 #167 gate passed — netstandard2.1 fix (`TrySetCanceled token`) in; gates under `gate-lock.ps1 shared`: `build.fsx -- test` unfiltered (Ranvier.Tests 1476, CSharp 95, Twin 1067, Wire 290, Maps 76; 0 failed), traced test stage (Ranvier.Tests 1608 + 13 skipped, CSharp 103, Twin 1069, Wire 290, Maps 76; 0 failed), `test-fable`, `format --dry-format`, `build.fsx -- build` (net10.0 and netstandard2.1 outputs), NativeAOT publish win-x64 untraced and traced with no IL warnings and all checks passed — ffc10c89 — next: integrate `issue/167` on `issue/164`
- 2026-10-24 #167 done — `issue/167` rebased onto `issue/164` (8bae422c, docs-only changes), pushed, PR https://github.com/shayanhabibi/Ranvier/pull/176 on `issue/164` — 24dea685 — next: #165
- 2026-10-24 #167 done — `DispatchAsync` contract scoped: thread-pool continuations apply only to queued items, inline-call continuations run on the graph thread (new Threading test) — e9b8bef7 — next: push `issue/167` to update PR #176
