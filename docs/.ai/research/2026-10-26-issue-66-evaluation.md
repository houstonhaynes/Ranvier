# #66 evaluation: the #220 tool surface against the old surface and no MCP

Date: 2026-10-26. Branch `feat/220-mcp-detection-loop`, from `origin/master` at `6784ff01`. Implements and measures
#220 (R1–R6 of `docs/.ai/research/2026-10-26-mcp-tool-cost.md`).

## 1. Answer

On #168's round-2 task, the redesigned surface turns the MCP tools into a working diagnosis loop, and does not make
the MCP workflow cheaper than reading the source.

- `explain {node: total}` named the defect in 2 of 6 runs (w03, w06): the `offerOf` memo at `Pricing.fs:23` re-ran
  after the discount moved, and kept `{ Label = "standard"; Percent = 10 }`. In the other 4, the last dispatched message
  moved the discount into another band, the total was right again, and `explain` reported only the history after the
  total's last run. No run of the old surface called `why` or `why_not`. (Corrected: an earlier version said 6 of 6.)
- The supervisor removed the server's lock on the build output. Every old-surface run that rebuilt (3 of 3) hit the lock
  and fell back to a Release build or a scratch output folder; the new-surface runs rebuilt in place, and the round-B
  runs checked the fix live after the reload (3 of 3).
- The new surface ran in about half the old surface's wall clock (median 47–55 s against 96 s).
- The MCP runs still cost more than the baseline: median 9–10 API requests against 6, and 1.5–1.6× the tokens. The
  agents reproduced the symptom, called `explain`, and then read the same files the baseline read. #220's target
  (median calls at most the baseline's + 1, tokens at most 1.1×) is not met on this task.

Tokens follow API requests in every arm: each request re-reads a 43–52k context, and MCP results add under 1 kB each.
Weighted by price with a warm first request, the MCP arms cost 1.23–1.31× the baseline; see
`2026-10-26-mcp-token-reduction.md`.
The tools cannot beat a two-file read on a sample whose cause is visible in `Pricing.fs`. On a nine-file store task
with no failing test, the same note measures the MCP arm at 0.72× the baseline's tokens, an interval that still reaches
1.10 at five runs an arm, and the same tools called from the shell (`tools/mcp-call.fsx`) at 0.60× [0.52, 0.72].

## 2. What changed

| # | Change | Where |
|---|---|---|
| R1 | `tools/mcp-supervise.fsx` serves the application from a shadow copy of its output, restarts the new build on the next `tools/call` after a rebuild, repeats `initialize`, replays the accepted messages as one `dispatch {messages}`, prefixes the next result with a reload line, and appends the reload behaviour to the server's instructions | `tools/mcp-supervise.fsx` |
| R2 | `explain {node}`: creation site and line text, value, sources, the cause chain of the last run back to the dispatched message, why it has not re-run, `unread:` related nodes that changed and are not read, `cutoff:` upstream nodes that re-ran and kept their value | `src/Ranvier.Mcp/Diagnosis.fs` |
| R3 | `dispatch {messages: [...]}` with one line of changed exposed values per step and the final state; a rejection names its position | `McpAdapter.fs` |
| R4 | `graph {}` (≤ 2,000 characters) and `node {node, around, detail, limit, cursor}` (≤ 4,000 a page); `trace_state`, `trace_lookup`, `why`, `why_not` and `waiting_on` only with `McpAdapterOptions.RawTraceTools` | `Diagnosis.fs`, `McpAdapter.fs` |
| R5 | Compact text with `name#id` names (from `Expose.node`, a trace label, or the `let` at the creation site), `file:line` and the line's text; structured content for `snapshot`, `wait_version` and `dispatch` only | `Diagnosis.fs` |
| R6 | `ServerInstructions` (623 characters); `_meta["anthropic/alwaysLoad"]` on `dispatch` and `explain` | `McpAdapter.fs` |
| added | `Expose.messageShapes`: one line per message's arguments, in `dispatch`'s description and its malformed-argument errors | `Exposure.fs`, `Cart.fs` |

The `Exposure` keeps a journal of accepted messages with the trace seq before each, so `explain` names the message
behind a write.

## 3. Protocol

- **Task.** #168's round 2: `Pricing.fs` derives an `offer` memo whose comparer calls two offers equal when their band
  labels match; `Cart.fs` computes `total = Pricing.apply subtotal.Value offer.Value`. The scripted expectation test
  passes and the random-sequence test fails.
- **Arms.** Baseline: no MCP server. Old: the `origin/master` adapter served natively from the build output. New: this
  branch's adapter behind the supervisor. MCP arms' prompt adds one sentence naming the `ranvier-cart` server.
- **Prompt.** "The cart in `examples/Ranvier.Mcp.Sample` sometimes shows a wrong total. The total must equal the sum
  of unit price times quantity over the lines, less the discount percentage, rounded towards zero. Find the cause and
  fix it so that the build meets that rule. Do not change the rule or the tests' expectation. When you are done, state
  the cause in one sentence."
- **Workspaces.** One copy per run (`w01`–`w12`) with a fresh one-commit history. Removed: `docs/.ai`,
  `docs/superpowers`, `docs/research`, the MCP guide, `.claude/workflows`, `.claude/settings.json`, `.mcp.json`, the
  `--defect` flag and its wording, the seeded-defect and traced diagnosis tests, and `McpExplainTests.fs`. Each copy
  was built before its run.
- **Harness.** `claude -p` 2.1.310, `--model opus`, `--setting-sources project,local` (no user plugins),
  `--strict-mcp-config` with the arm's server only, `--permission-mode bypassPermissions`, stream-json transcripts.
  Three runs at a time, one per arm in round A.
- **Verdict.** After each run: the diff is saved, `tests/` is restored from the commit, and the `MCP sample cart`
  tests (scripted, random-sequence, decoders, stdio) run.
- **Rounds.** Round A: 3 runs per arm (`w01`–`w09`). Round B: 3 new-arm runs (`w10`–`w12`) after two changes that
  round A showed were needed: message argument shapes, since every round-A new-arm run's first `dispatch` guessed
  `price`/`qty` and failed; and the reload sentence in the instructions, since every round-A new-arm run reported the
  server as stale after its rebuild.

Deviations: round A started by accident, before the smoke test of native server start-up, through an import of the run
script; it ran with the intended configuration. The environment differs from #168's (no SageFs, no user plugins, native
MCP), so the absolute numbers do not compare with #168's tables.

## 4. Results

| Arm | Run | Verdict | API requests | Tool calls | MCP calls | Builds/tests | Tokens | Wall clock |
|---|---|---|---|---|---|---|---|---|
| base | w01 | pass | 7 | 6 | none | 2 | 335,508 | 39 s |
| base | w04 | pass | 6 | 5 | none | 2 | 287,525 | 42 s |
| base | w07 | pass | 6 | 5 | none | 2 | 283,724 | 55 s |
| old | w02 | pass | 7 | 6 | none | 3 | 345,404 | 96 s |
| old | w05 | pass | 8 | 12 | dispatch 5 | 2 | 401,317 | 77 s |
| old | w08 | pass | 12 | 14 | dispatch 3 | 4 | 604,317 | 124 s |
| new A | w03 | pass | 9 | 10 | dispatch 2, explain 1 | 3 | 441,537 | 72 s |
| new A | w06 | pass | 8 | 8 | dispatch 2, explain 1 | 1 | 371,767 | 50 s |
| new A | w09 | pass | 9 | 13 | dispatch 3, explain 1 | 1 | 427,196 | 55 s |
| new B | w10 | pass | 10 | 9 | dispatch 2, explain 1, snapshot 1 | 1 | 463,620 | 41 s |
| new B | w11 | pass | 9 | 8 | dispatch 2, explain 1, snapshot 1 | 1 | 432,549 | 47 s |
| new B | w12 | pass | 10 | 9 | dispatch 3, explain 1, snapshot 1 | 1 | 489,615 | 52 s |

| Arm | Median requests | Median tool calls | Median tokens | Median wall clock |
|---|---|---|---|---|
| base | 6 | 5 | 287,525 | 42 s |
| old | 8 | 12 | 401,317 | 96 s |
| new A | 9 | 10 | 427,196 | 55 s |
| new B | 10 | 9 | 463,620 | 47 s |

Every fix was in the comparer: three compare the percentage as well, five use structural equality (four through
`HashIdentity.Structural`), and four replace the comparer memo with `createMemo`. Tokens count input, cache reads, cache writes and output over unique API requests.

## 5. Findings

1. **One `explain` call gives the cause when the defect is the latest change.** In w03 and w06 the `explain` answer
   held the `cutoff:` line naming `offerOf#5`, its site and the kept offer, and the next step was the edit or a read of
   `Pricing.fs`. The answer also held a second cutoff, `lines#2` kept its value after the model changed, which is a
   selection doing its job. In w09–w12 the answer held no cutoff, and the agents found the comparer by dispatching again
   and reading `Pricing.fs`. `2026-10-26-mcp-token-reduction.md` adds `earlier cutoff:` lines and leaves selections
   out.
2. **The old surface's lock shapes the run.** Each old-arm run that rebuilt reported the locked Debug DLL and built to
   another output. One old-arm run (w02) called no MCP tool at all. With tool search on, two old-arm runs spent a
   `ToolSearch` call before their first `dispatch`; `alwaysLoad` removed that call in the new arm.
3. **Argument shapes fix the first call.** Round A's first `dispatch` failed in 3 of 3 runs; round B's succeeded in 3
   of 3, and round B read no source before its first `explain`.
4. **The reload makes live checking routine.** Round B's runs each called `snapshot` after `dotnet test` and quoted the
   fixed total from the reloaded build. Round A's runs, with the same supervisor but no announcement, told the user to
   restart the server.
5. **MCP adds steps; it does not replace reading.** The new-arm sequence is reproduce (1–2 `dispatch`), `explain`,
   2–3 source reads, edit, test, and in round B a live check. The baseline's is 2–3 reads, edit, test. The reads are
   the same in both: the agent reads the file before editing it, and `explain`'s one line does not show the comparer.

## 6. #220 acceptance criteria

| # | Criterion | Status |
|---|---|---|
| 1 | Rebuild without restart | Met by the supervisor, checked by a driven session: `dotnet build` succeeded with the server running, and the next call served the fix with two messages replayed. No automated test drives the supervisor. |
| 2 | `explain` ≤ 1,000 characters naming the node, site, line text, `discount` and the missing read; the comparer case | Met; `McpExplainTests` asserts both. The sample's line numbers moved with `shapes`, so the test checks `Cart.fs:` and the line's text. |
| 3 | Sequence dispatch, rejection by position, ≤ 200 characters a step | Met; `replay` is the supervisor's reload replay, not a tool. |
| 4 | Bounds on a 1,000-node graph | Met for `graph`, `node` and `explain`; `trace_state` is behind `RawTraceTools`. |
| 5 | Names and `file:line` in every text block; `outputSchema` | Names met. No `outputSchema`: the traced tools return text alone. |
| 6 | Instructions ≤ 1,000 characters, `alwaysLoad`, ≤ 6 tools, ≤ 2,500 B | Met; the definitions are 2,555 B with the sample's 160 B of argument shapes, under 2,500 B without. |
| 7 | Re-measure with native MCP; MCP median calls ≤ baseline + 1, tokens ≤ 1.1× | Measured; target not met (requests 10 against 6, tokens 1.6×). |
| 8 | Gates | `build.fsx -- build`, the unfiltered `build.fsx -- test` (traced stages included) and `build.fsx -- docs` passed; `format --dry-format` passed after formatting four files. The 45 MCP tests pass on the formatted tree. |

## 7. Unverified and open

- Three runs per arm on one task, on one machine with three runs at a time; the spread inside an arm (w08's 12
  requests) is as large as the differences between arms.
- Cost per run varies with prompt-cache hits across concurrent runs, so the table reports tokens, not dollars.
- The supervisor is a script run by `dotnet fsi`: start-up takes about 2 s, and a compile error in it would reach the
  client's stdout. A packed tool would remove both.
- The task that can show a benefit is still to be run: #66's harness tasks, where the symptom is only at runtime and
  the source is too large to read.

## 8. Reproduction

The harness is in `C:\Users\shaya\RiderProjects\Ranvier-lanes\eval-220`: `setup.py` makes the blinded workspaces
(`python setup.py w10:new` for one), `build.sh` builds them, `run.py` runs them, `verdict.py` judges them, and
`metrics.py` prints the tables. Transcripts are in `logs/`.
