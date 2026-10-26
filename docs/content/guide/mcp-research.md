---
title: Research on agents and the MCP tools
order: 14
---

:::info
Research record — the measurements behind [Inspecting a running graph over MCP](mcp.md). The figures come from
`claude -p` sessions with Opus on two sample tasks; the full notes are in
[docs/.ai/research](https://github.com/shayanhabibi/Ranvier/tree/master/docs/.ai/research).
:::

`Ranvier.Mcp` is measured by one question: does a coding agent with the tools fix a defect for fewer tokens and in less
time than the same agent with the source and tests alone? This page records the experiments, the current findings and
the recommendations that follow from them.

## Findings

- **Cost follows the number of model requests.** Each request re-reads the context, about 45,000 tokens before the
  agent does any work. A tool result adds 40–220 tokens and the tool definitions about 730, so compressing results or
  schemas cannot move the total. A tool saves cost only when it replaces requests.
- **On a two-file task the tools add requests.** When the cause is visible in the source, agents read the source
  anyway and call the tools in addition: the MCP arm costs 1.17× the baseline's tokens.
- **On a nine-file task the tools replace requests.** With no failing test to point at the cause, baseline agents write
  and run their own reproduction tests. Agents with the tools call `explain` once and fix the line its `cutoff:` names.
- **The shell beats the MCP server.** Called through `tools/mcp-call.fsx` from the agent's shell, the same tools cost
  0.60× the baseline's tokens on the nine-file task, in half its requests. The MCP server costs 0.72×, with an interval
  too wide to call.
- **Always-loaded tools cost a cold cache.** Tools marked `_meta["anthropic/alwaysLoad"]` join the head of the prompt,
  so a session starts by writing about 40,000 tokens to the cache instead of reading them.

## The experiments

### The first comparison (#168)

The spike served snapshots, `dispatch`, and the raw trace tools `why`, `why_not`, `waiting_on`, `trace_state` and
`trace_lookup`. Two rounds, five runs an arm, on a cart sample whose property test fails: a `Peek` in `Cart.fs`, then a
comparer in `Pricing.fs`. Every run of both arms fixed the defect, and the MCP arm cost about twice the baseline's
tokens in round 1 and 47% more in round 2.

Total tokens fit `67.6k + 65.5k × tool calls` with r² = 0.93. Of the MCP arm's 57 calls, 31 managed the server's
lifecycle (starting it, retrying the connection, stopping it to rebuild), 18 reproduced a symptom the failing test
already showed, and 8 asked a provenance question.

### The redesigned tools (#220)

The redesign removes calls:

- `tools/mcp-supervise.fsx` serves the application across rebuilds, so the server needs no restart.
- `explain {node, messages}` answers why a node holds its value in one call: the message behind its last run, and the
  `cutoff:` nodes that re-ran and kept their value, with each creation line.
- `dispatch {messages}` runs a sequence and lists each step's changes.
- `graph` and `node` replace the raw trace documents with pages of at most 4,000 characters.

On #168's comparer task, `explain` named the defect in one call in 2 of 6 runs, and the redesigned arm ran in about
half the old arm's wall clock. It still took 8 requests against the baseline's 7.

### The nine-file store task

The store task hides a comparer defect in a nine-file sample with no failing test: the shipping fee keeps its value
after the cart's weight changes. Runs went one at a time with the arms interleaved, on a warm prompt cache. Ratios are
paired ratios of means over the baseline, with bootstrap 95% intervals. Input-equivalent tokens weight cache reads at
0.1, cache writes at 1.25 and output at 5, the ratio of their prices.

| Task | Arm | Runs an arm | Input-equivalent tokens | Median requests |
|---|---|---|---|---|
| store | MCP server | 5 | 0.72× [0.50, 1.10] | 8 vs 10 |
| store | MCP server, `AlwaysLoad` | 4 | 0.88× [0.57, 1.32] | 8.5 vs 11.5 |
| store | shell client | 4 | **0.60×** [0.52, 0.72] | 6 vs 11.5 |
| cart | MCP server | 4 | 1.17× [1.01, 1.24] | 8 vs 7 |
| cart | shell client | 4 | 1.09× [0.99, 1.21] | 6 vs 6 |

Every run passed an independent verdict: the run's test changes reverted, then the task's expectation tests built and
run against its fix.

The shell-client runs share one shape: list the tools with the first reads, read the modules, call `explain shipping`
with a reproducing sequence, then edit and build with the check chained to the build in one command. The MCP arm
spends two more requests, one on the client's tool search and one on a separate live check. No shell-client run wrote a
test; three of four baseline runs did, and those are the long runs.

The shell client's `CLAUDE.md` section asks the agent to chain commands, which an MCP client cannot do. The comparison
measures the method, not the transport alone.

## Recommendations

1. **Give agents the tools through the shell.** Describe `tools/mcp-call.fsx` and the server's instructions in the
   project's `CLAUDE.md` or `AGENTS.md`; see [Calling the tools from the shell](mcp.md). Keep the MCP server for
   clients without a shell.
2. **Keep `AlwaysLoad` off.** The tool search it removes costs less than the cold cache it causes.
3. **Point the tools at defects the source hides.** On a task where reading two files reveals the cause, the tools add
   cost.
4. **Measure from the `result` record, with serial runs and a warm first request.** The per-message usage of a
   stream-json log undercounts output about twenty-fold, and parallel runs start with a cold cache.

## Open questions

Under the decision rule recorded for #66, a tool arm benefits on a task when its tokens or wall clock are at most 0.8×
the baseline's, with the 95% interval below 1.0, and its success rate is no lower. The shell client meets the rule on
the store task at four runs an arm. Not yet measured:

- the store task at 8 or more runs an arm;
- a shell-client arm without the chaining habits;
- #66's runtime-only task and a larger codebase;
- a compiled client, which would start in about 0.3 seconds a call instead of 1.7.
