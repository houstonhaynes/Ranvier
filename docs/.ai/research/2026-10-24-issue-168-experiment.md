# #168 validation experiment: does graph introspection over MCP help an agent repair a reactive defect?

Status: **done; accepted by the user on 2026-10-25 as #168's comparative evaluation.** Twenty runs in two rounds,
five per workflow per round, on blinded copies of the sample. Every run in both workflows succeeded, named the cause
and fixed it in one repair iteration. The MCP-introspection workflow cost more at the median (about twice the tokens
in round 1, 47% more in round 2) and gave no measurable benefit on this sample, where the cause is reachable from a
failing expectation test and a read of two short files. See [Runs](#runs) and [Round 2](#round-2-a-non-local-defect)
for the measurements, the protocol deviations and the limits.

## Question

Given a reactive defect in a running Ranvier application, does an agent that can query the running graph through
`Ranvier.Mcp` (snapshot, dispatch, and the traced `why` / `why_not` / `trace_state` tools) diagnose and repair it
with fewer tool calls, fewer repair iterations, fewer tokens or less wall-clock time than an agent with source, build
and tests alone?

## Subject

`examples/Ranvier.Mcp.Sample`, a cart held in an `Mvu`, with `lines`, `subtotal`, `discount` and `total` exposed.

- **Expectation**, checked independently from published snapshots by `tests/Ranvier.Mcp.Tests/SampleTests.fs`: after
  any message sequence, `total` equals the sum of `unitPrice * quantity` over `lines`, less `discount` percent, rounded
  towards zero.
- **Seeded defect** (`--defect`, `Cart.create true`): the `total` memo reads the discount with `Peek`, which is
  untracked. A discount change leaves `total` stale until the next line change. The code reads correctly at a glance:
  `discounted subtotal.Value discount.Peek`.

## Protocol

Two workflows, the same model, the same prompt, and a fresh checkout of the branch for each run.

- **Baseline:** the repository source, `dotnet build`, `dotnet test`, and SageFs.
- **Experimental:** the baseline, plus the sample served as `ranvier-cart` over stdio from a traced Debug build with
  `--defect` (the `.mcp.json` entry in the sample README, with `"--defect"` added to `args`).

Prompt, identical in both workflows:

> The cart in `examples/Ranvier.Mcp.Sample`, run with `--defect`, sometimes shows a wrong total. The total must equal
> the sum of unit price times quantity over the lines, less the discount percentage, rounded towards zero. Find the
> cause and fix it so that the `--defect` build meets that rule. Do not change the rule, the tests' expectation, or
> the healthy (non-`--defect`) build.

Per run, record: success (the independent verdict below), tool calls, repair iterations (edits followed by a check),
total tokens, wall-clock time, and whether the agent's stated cause names the untracked read.

**Independent verdict:** after the run, `SampleTests.fs`'s expectation checks run against `Cart.create true` (the
scripted and the random-sequence tests with `connect true`). The seeded-defect test (`the --defect cart leaves the
total stale after a discount change`) is expected to fail after a correct repair, and is excluded from the verdict.

At least five runs per workflow; report every run, the failures included. A single success in either workflow
supports no conclusion.

## The debugging task, reproduced

The task is reproducible without an agent: an automated in-process MCP client drives the `--defect` cart, observes the
violation, and reaches the cause through the trace tools. `SampleTests.fs` holds the checks (traced stage: `MCP sample
cart diagnosis (traced)`). The transcript below came from the same calls in a SageFs session over a traced Debug build
on 2026-10-25 (documents shortened; node ids: 1 the model signal, 2 `lines`, 3 `discount`, 4 `subtotal`, 5 `total`,
6 the publication effect).

1. `dispatch add_item {sku: tea, unitPrice: 450, quantity: 2}` returns version 2 with `subtotal: 900, total: 900`.
2. `dispatch set_discount {percent: 10}` returns version 3 with `discount: 10, subtotal: 900, total: 900`. The
   expectation gives 810: the violation is visible in one snapshot.
3. `why {node: total}` returns a chain for run 2 of node 5 whose root is `UserWrite 41`, the `add_item` write; the
   steps pass through nodes 4, 2 and 1 only. The discount write did not cause the total's last run.
4. `why_not {node: total}` returns `{"case":"CheckedClean","resolvedAt":96,"upstream":[4]}`: the total was checked
   clean against the subtotal alone.
5. `trace_state` holds `"sources":[[2,[1]],[3,[1]],[4,[2]],[5,[4]],...]`: node 5 (`total`) reads only node 4
   (`subtotal`). In the healthy build its sources are 4 and 3.

Steps 4 and 5 name the cause: `total` holds no dependency on `discount`, so the defect is an untracked read inside the
`total` memo. The agent still has to find the read in the source; see finding 1.

## Runs

Ten runs on 2026-10-25, five per workflow, all with the same model and the protocol's prompt (workflow run
`wf_e1435be2-b07`). Tool calls, tokens and wall clock come from each run's transcript; repair iterations are the
agent's own count; success and "cause named" are the verdict agent's.

| Workflow | Run | Success | Tool calls (bridge) | Repair iterations | Tokens | Wall clock | Cause named |
|---|---|---|---|---|---|---|---|
| Baseline | b1 | yes | 5 | 1 | 388,336 | 97 s | yes |
| Baseline | b2 | yes | 11 | 1 | 715,213 | 147 s | yes |
| Baseline | b3 | yes | 5 | 1 | 392,053 | 67 s | yes |
| Baseline | b4 | yes | 6 | 1 | 471,205 | 113 s | yes |
| Baseline | b5 | yes | 11 | 1 | 724,556 | 78 s | yes |
| Experimental | m1 | yes | 18 (7) | 1 | 1,279,331 | 188 s | yes |
| Experimental | m2 | yes | 16 (5) | 1 | 1,007,621 | 165 s | yes |
| Experimental | m3 | yes | 11 (5) | 1 | 724,891 | 112 s | yes |
| Experimental | m4 | yes | 12 (5) | 1 | 915,638 | 121 s | yes |
| Experimental | m5 | yes | 11 (5) | 1 | 722,935 | 82 s | yes |

Medians: baseline 6 tool calls, 471k tokens, 97 s; experimental 12 tool calls (5 of them through the bridge), 916k
tokens, 121 s. Tokens count input, output and cache reads and writes.

### Result

On this task the MCP workflow gives no measurable benefit and costs about twice the tokens and tool calls. Every run
in both workflows succeeded, named the untracked read and fixed it in one iteration. Eight of the ten runs report
spotting `discount.Peek` while reading `Cart.fs`, before any test run or MCP call; b1 read `Cart.fs` and the tests and
first saw the symptom in the failing expectation tests. Four of the five experimental runs then used the bridge to
confirm the symptom (`set_discount` leaves `total` unchanged) and, in some runs, `why` or `why_not` to confirm the
missing edge; m5 reports finding the symptom over MCP first.

The task is too easy to separate the workflows: the defect is one token in a 150-line file whose only reactive
reads are the three memos, and the sample's own expectation tests fail on it. The result says the tools add overhead
when the cause is visible in the source; it says nothing about defects whose cause is far from the symptom.

### Deviations from the protocol

- **Blinded copies.** The branch's `Cart.fs` selects the defect with an `if defect then … Peek else … Value` branch
  and a remark naming it, which would give the cause away. Each run worked in its own copy of `5c10704a` with only
  the `Peek` memo (the `defect` flag ignored, `create (_: bool)`), the defect wording removed from `Program.fs` and
  the README, the seeded-defect and trace-diagnosis tests removed from `SampleTests.fs`, this note, the #168 plan,
  `PRIORITIES.md` and the ralph scripts deleted, and a fresh one-commit git history.
- **MCP through a bridge.** Workflow agents cannot register an MCP server, so the experimental runs reached the
  `--defect` cart through a CLI bridge that holds one stdio session (`tools`, `call`, `read`, `resources`, `stop`)
  rather than native client tools.
- **Prompt leak.** The workflow harness relayed the user's request to every run agent verbatim, which says the runs
  are a comparative experiment over a "seeded defect" (not what or where it is).
- **Healthy build.** Because the blinded `create` ignores its flag, the healthy and `--defect` builds ran the same
  code; the prompt's "do not change the healthy build" could not be checked, and the expectation tests (which connect
  with the healthy flag) failed before every fix.
- **Shared machine.** Builds ran under the lanes' shared build lock beside other lanes, two baseline and two
  experimental runs at a time.
- **Verdict.** The verdict agent restored the blinded `SampleTests.fs`, rebuilt, and ran the scripted and
  random-sequence expectation tests; it also checked the diff for changes to the rule or the tests. No run changed
  either; every fix was `discount.Peek` to `discount.Value`.

### What a discriminating run needs

A defect whose cause is not visible at the symptom: for example an untracked read inside a helper in another file, a
read behind a condition the failing sequence reaches only sometimes, or a memo that depends on the wrong one of two
similar sources; plus a harness that does not relay the experiment's framing to the runs.

## Round 2: a non-local defect

Round 1 could not separate the workflows, so round 2 (2026-10-25) moved the defect away from the symptom and kept
the experiment's framing out of the runs.

- **Defect.** A new `examples/Ranvier.Mcp.Sample/Pricing.fs` derives an `offer` memo (`{ Label; Percent }`) from the
  discount, with a comparer that calls two offers equal when their labels match (`none` for 0, `standard` below 20,
  `promo` from 20). `Cart.fs` reads correctly: `total = Pricing.apply subtotal.Value offer.Value`. A discount change
  that stays in one band is dropped at the offer memo, so the total is wrong only sometimes: the scripted expectation
  test passes and the random-sequence test fails. No `Peek` remains in the sample.
- **Isolation.** Each run worked in a neutrally named copy (`ws/w01` to `ws/w10`, baseline odd, experimental even)
  with a fresh one-commit history. The runs were started as plain agents with only the protocol's prompt and the
  environment notes; their transcripts show no relayed request and no read of the experiment's files.
- **Everything else** as in round 1: the same prompt, the bridge for the experimental runs, the shared build lock,
  four runs at a time, and an independent verdict per run against the restored expectation tests.

| Workflow | Run | Success | Tool calls (bridge) | Repair iterations | Tokens | Wall clock | Cause named | First saw the symptom |
|---|---|---|---|---|---|---|---|---|
| Baseline | w01 | yes | 12 | 1 | 810,040 | 158 s | yes | expectation tests |
| Baseline | w03 | yes | 7 | 1 | 548,970 | 126 s | yes | reading the code |
| Baseline | w05 | yes | 9 | 1 | 713,200 | 119 s | yes | expectation tests |
| Baseline | w07 | yes | 11 | 1 | 898,657 | 173 s | yes | expectation tests |
| Baseline | w09 | yes | 11 | 1 | 721,315 | 159 s | yes | expectation tests |
| Experimental | w02 | yes | 16 (8) | 1 | 1,139,928 | 205 s | yes | MCP, then `why_not` |
| Experimental | w04 | yes | 15 (5) | 1 | 1,074,861 | 176 s | yes | expectation tests, then MCP and `why` |
| Experimental | w06 | yes | 12 (5) | 1 | 956,026 | 144 s | yes | MCP, then `why_not` |
| Experimental | w08 | yes | 13 (7) | 1 | 880,485 | 135 s | yes | MCP |
| Experimental | w10 | yes | 14 (6) | 1 | 1,063,369 | 170 s | yes | MCP |

Medians: baseline 11 tool calls, 721k tokens, 158 s; experimental 14 tool calls, 1,063k tokens, 170 s. Every fix
was in the comparer: seven compare `Percent` as well, one uses structural equality, one drops the comparer. w09 counts
one rejected edit attempt before its single effective edit. Two experimental runs (w06, w10) wrote a check script to
the session's temporary folder instead of their own copy.

### Result

Moving the defect one file away from the symptom did not change the outcome. All ten runs succeeded in one
iteration and named the comparer. The baseline runs reached the cause from the failing random-sequence test and a
read of the two sample files; the experimental runs reached it from a live reproduction (`set_discount 10` then `15`
leaves the total at 900) and `why_not total` (`CheckedClean`), which points at the offer memo. The experimental
workflow cost about 47% more tokens, 3 more tool calls and 12 s more wall clock at the median.

Across both rounds the trace tools gave a correct and direct account of the cause, and agents used them as intended,
but on a sample of two short files with a failing property test they replaced a read the baseline made anyway. The
experiment supports no claim that MCP introspection speeds up repair at this scale. A case where it could: a defect
with no failing test at hand, in a codebase too large to read, where the symptom is only visible in the running
application.

## Findings so far

1. **Identity paths point at the host, not the application.** Every node the sample creates in `GraphHost.Start`'s
   setup reports a path in `GraphHost.fs` (`/GraphHost.fs:84`, `/GraphHost.fs:84#1`), `Mvu.fs` or `Exposure.fs`,
   never in `Cart.fs`. An agent cannot go from a `why` step to the source line by path. The names registered with
   `Expose.node` are the only bridge, so an application should expose every node an agent may need to locate. A fix
   belongs in the trace's call-site capture (#165/#166), not the adapter.
2. **Model values are `Lossy`.** With no codecs, a record value appears as `Lossy` text (`{ Lines = [...]; … }`),
   truncated. Scalars are `Exact`. Enough for this task; a task that turns on a record field would need codecs.
3. **The violation is visible without tracing.** The snapshot after `set_discount` already breaks the expectation.
   The trace tools add the cause (the missing source edge), not the symptom. The experiment should separate the two
   in its measurements: time to the symptom and time to the cause.
4. **Protocol pin.** SDK 2.3.0 negotiates `2026-07-28` by default, which drops `resources/subscribe`. The adapter pins
   `2025-11-25`; a client that needs `2026-07-28` gets no subscriptions until the adapter serves
   `subscriptions/listen`.
5. **The trace tools read the whole graph.** `Expose.node` names the nodes a `node` argument accepts, but
   `trace_state` lists every node, `trace_lookup` resolves any identity `path`, and a `why` chain names each node it
   passes through. The experimental workflow therefore sees nodes the sample did not expose, such as the cells `Mvu`
   creates. The guide and the adapter docs state this boundary.
