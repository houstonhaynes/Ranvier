# Reducing the token cost of the Ranvier MCP loop

Date: 2026-10-26. Follows `2026-10-26-issue-66-evaluation.md` on `feat/220-mcp-detection-loop`; data in
`Ranvier-lanes/eval-220` (runs w01–w15) and the cache probes below.

## 1. Answer

One change saves tokens: drop `_meta["anthropic/alwaysLoad"]`. A session whose tools are always loaded starts with a
cold prompt cache unless another session with the same tool set ran within the cache lifetime: its first request writes
about 43k tokens instead of reading the 34.5k shared prefix, about 33k input-equivalent tokens per cold session. Tool
search costs one extra request, about 5k.

The changes that remove requests did not lower the cost on this task. The cost is 9–10 requests a run. Each request
re-reads about 45k tokens of fixed context, and the MCP results add under 1k each.

## 2. Measure

Raw token sums count cache reads at full weight. Input-equivalent tokens weight input 1, cache read 0.1, cache write
1.25 and output 5, the ratio of their prices. The medians, with each run's first request recomputed on a warm cache:

| Arm | Runs | Median requests | Median input-equivalent tokens |
|---|---|---|---|
| base | w01, w04, w07 | 6 | 56,186 |
| old | w02, w05, w08 | 8 | 70,773 |
| new A | w03, w06, w09 | 9 | 70,484 |
| new B | w10–w12 | 10 | 70,585 |
| new C | w13–w15 | 9 | 69,988 |

The MCP arms cost 1.25× the baseline, not the 1.5–1.6× of the raw sums. A request costs about 5–6k
input-equivalent tokens in every arm.

## 3. Prompt cache

`claude -p "Reply with OK."` in an empty folder, the first request's cache read and cache write:

| Configuration | First run | Second run |
|---|---|---|
| no MCP | 29,089 / 10,176 | 34,513 / 4,753 |
| old adapter (no `alwaysLoad`, no instructions) | 34,513 / 4,923 | 34,513 / 4,925 |
| new adapter | 0 / 40,192 | 35,032 / 5,160 |
| new adapter, `alwaysLoad` removed | 34,511 / 5,194 | 34,511 / 5,193 |
| new adapter, instructions removed | 35,032 / 4,853 (warm from the row above) | 35,032 / 4,854 |
| new adapter, both removed | 34,511 / 4,889 | 34,511 / 4,890 |

Always-loaded tools join the tool block at the head of the prompt, so the prefix diverges from the first byte. Server
instructions leave the shared prefix intact. Every round-B and round-C run started cold (`cr=0`) because the three ran
at once; that adds about 33k input-equivalent tokens to each raw total.

## 4. Round C: changes that target requests

Changes, run as round C:

- `dispatch` returns text alone. Claude Code shows `structuredContent` to the model in place of the text block, so in
  rounds A and B the agent saw only the final snapshot JSON: the step lines and the supervisor's reload note never
  reached it. The supervisor now also removes `structuredContent` from a result it prefixes.
- `explain {node, messages}` dispatches the messages first.
- `explain` reports `earlier cutoff:` lines when no cutoff follows the node's last run.

Result: 3 of 3 pass, median 9 requests, 69,988 input-equivalent tokens, no change. The agents used `explain
{messages}`, then still dispatched once more to confirm and read `Pricing.fs` to edit it. The step lines were used: w14
named the cause from `set_discount → discount 15→10` with no total change. The earlier cutoffs named only
`mvu.Select` selections in round C, because no reproduction moved the discount inside a band before `explain`.

## 5. Correction to the #66 evaluation

The evaluation says `explain` named the comparer cutoff in 6 of 6 runs. The transcripts show 2 of 6 (w03, w06). In
w09–w12 the last dispatched message left the total right, and `explain` reported only the history after the last run.

## 6. Recommendations

Items 1–3 are on the branch, after round C.

1. `McpAdapterOptions.AlwaysLoad`, off by default.
2. Keep `dispatch` text-only, and strip structured content wherever a text block carries the meaning.
3. `explain` leaves out the cutoffs of a node that reads a dispatched model alone (an `Mvu.Select`), which cuts off
   by design. Round C ran before this filter.
4. Report input-equivalent tokens with a warm first request in future evaluations.
5. Requests stay the cost driver. Fewer requests need a task where the agent cannot read the cause in two files, which
   is #66's harder task set.
