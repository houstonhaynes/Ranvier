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
| 1 | [#164](https://github.com/shayanhabibi/Ranvier/issues/164) perf(trace): traced/untraced performance and memory baselines | P0 foundation | — | implement | todo |
| 2 | [#167](https://github.com/shayanhabibi/Ranvier/issues/167) feat(graph): public awaitable graph dispatch | P0 | — | implement | todo |
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
| issue/69 | #69 | https://github.com/shayanhabibi/Ranvier/pull/175 | integration/issues |
| issue/70 | #70 | https://github.com/shayanhabibi/Ranvier/pull/174 | integration/issues |

## Log

Each iteration appends one line: date, issue, outcome, commit, next step.

- 2026-10-24 #69 research note written — docs/.ai/research/2026-10-24-issue-69.md — b6125ef5 — next: user decides scope
- 2026-10-24 #70 research note written — docs/.ai/research/2026-10-24-issue-70.md — 92585c4e — next: user decides scope
- 2026-10-24 #68 research note written — docs/.ai/research/2026-10-24-issue-68.md — c0e402fe — next: user decides scope
