# Reducing the token cost of the Ranvier MCP loop

Date: 2026-10-26. Follows `2026-10-26-issue-66-evaluation.md` on `feat/220-mcp-detection-loop`; data in
`Ranvier-lanes/eval-220` (runs w01–w15, d01–d08, h01–h10, e01–e12, c01–c08), the cache probes below, and `replay.py`.

## 1. Answer

Calling the tools from the shell beats the MCP server. With `tools/mcp-call.fsx` described in `CLAUDE.md` and no
MCP server, the store task cost 0.60× the baseline's input-equivalent tokens [0.52, 0.72] in half its requests, with
every run passing; the interval excludes 1.0 at four runs an arm. On the cart task the shell client is at parity:
1.09× [0.99, 1.21], against 1.17× for the MCP tools (section 6).

On the store task, a nine-file sample with no failing test, the MCP arm cost 0.72× the baseline's input-equivalent
tokens and took 0.63× its wall clock, with every run passing. With five runs an arm, the 95% intervals reach 1.10 and
1.02, so the result is not conclusive under #66's decision rule. On the two-file cart task the MCP arm still costs
more: 1.17× [1.01, 1.24].

The cost of a run is its number of requests. Each request re-reads about 45k tokens of fixed context; the MCP tool
results add 40–220 tokens each and the tool definitions about 730. The tools save cost only where they replace
requests: on the store task, baseline agents wrote and ran their own reproduction tests, and MCP agents called
`explain` once.

Dropping `_meta["anthropic/alwaysLoad"]` saves about 33k input-equivalent tokens on each session that starts with a cold
cache (section 4).

## 2. Measure

Input-equivalent tokens weight input 1, cache read 0.1, cache write 1.25 and output 5, the ratio of their prices, with
the first request recomputed on a warm cache (28,964 tokens read). Totals come from the `result` record of the
stream-json log. The per-message `usage` of a stream holds a partial output count, about a twentieth of the real one,
and the figures of the first version of this note undercounted output by about 10k a run. `cost.py` computes the
corrected figures.

| Arm | Runs | Median requests | Median input-equivalent tokens | Output share | Median wall clock |
|---|---|---|---|---|---|
| cart, base | w01, w04, w07 | 6 | 65,096 | 13% | 42 s |
| cart, old | w02, w05, w08 | 8 | 83,688 | 15% | 96 s |
| cart, new A | w03, w06, w09 | 9 | 84,982 | 15% | 55 s |
| cart, new B | w10–w12 | 10 | 80,250 | 13% | 47 s |
| cart, new C | w13–w15 | 9 | 80,393 | 14% | 55 s |
| cart, base (round D) | d01, d03, d05, d07 | 7 | 65,034 | 12% | 43 s |
| cart, new D | d02, d04, d06, d08 | 8 | 76,108 | 14% | 44 s |
| store, base | h01, h03, h05, h07, h09 | 10 | 117,120 | 21% | 76 s |
| store, new D | h02, h04, h06, h08, h10 | 8 | 83,788 | 15% | 48 s |

Round D ran one session at a time with the arms interleaved, so every session after the first read the shared prefix
from the cache. Rounds B and C ran three at once and started cold.

Paired ratios, new D over base of the same round, with a bootstrap 95% interval over 5,000 resamples:

| Task | Tokens | Wall clock | Requests |
|---|---|---|---|
| store | 0.72 [0.50, 1.10] | 0.63 [0.30, 1.02] | 0.80 [0.57, 1.20] |
| cart | 1.17 [1.01, 1.24] | 1.03 [0.92, 1.27] | 1.14 [0.94, 1.29] |

## 3. Tool results without a model

`replay.py` replays the MCP calls recorded in a run against a freshly built server and reports each result's size and
latency. Over the calls of w03–w15:

| Tool | Calls | Median size | Median latency |
|---|---|---|---|
| `dispatch` | 20 | 270 characters | 7 ms |
| `explain` | 10 | 656 characters | 68 ms |
| `snapshot` | 5 | 131 characters | 2 ms |

`tools/list` is 2,564 characters. Compressing schemas or results (TOON, TRON, mcp-compressor) acts on under 1.5k tokens
a run, and code-execution modes act on intermediate results of this size; neither can move the total.

The supervisor starts in 1.75 s (2.25 s before its settle wait counted from the file's write time), against 0.2 s for
the application alone; most of it is `dotnet fsi`. A reload after a build that finished 1.5 s earlier takes 0.34 s
(0.88 s before). Tool time is 10–23 s of a run's wall clock, mostly `dotnet build` and `dotnet test`; the rest is API
time.

## 4. Prompt cache

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
instructions leave the shared prefix intact.

## 5. Rounds C and D: changes that target requests

Round C:

- `dispatch` returns text alone. Claude Code shows `structuredContent` to the model in place of the text block, so in
  rounds A and B the agent saw only the final snapshot JSON. The supervisor also removes `structuredContent` from a
  result it prefixes.
- `explain {node, messages}` dispatches the messages first.
- `explain` reports `earlier cutoff:` lines when no cutoff follows the node's last run.

Round C passed 3 of 3 with no change in cost.

Round D, after the selection filter and with `AlwaysLoad` off:

- With no cutoff to report, `explain` lists up to two upstream nodes whose creation line names a comparer, with their
  values.
- The server instructions make `explain {node, messages}` the first call of a diagnosis.
- The supervisor's instructions no longer suggest checking a fix live.

All 18 round-D runs passed. On the cart task the agents still loaded the tools, read both files, called `explain`, edited,
and checked the fix with `snapshot` and `dispatch` although nothing suggested it: 8 requests against the baseline's 7.
On the store task every MCP agent read the source, called `explain {node: shipping}` with two `add_item` messages, and
fixed the comparer from the `cutoff:` line, which names the quote memo, the weight change and the fee it kept. All
five baseline agents wrote a reproduction into the test project (`tests-changed` in their verdicts) and ran it; one
MCP agent (h04) added a test after its fix.

## 6. Round E: the tools from the shell

`tools/mcp-call.fsx <app.dll> <tool> '<json>'` starts the build output, runs the MCP handshake, makes one call and
prints the text, in about 1.7 s. The cli arms configure no MCP server; the workspace's `CLAUDE.md` gives the command,
the server's instructions in prose, and two habits: run `--list` in the same command as the first reads, and check a
fix in the same command as the build. The prompt's MCP sentence becomes "The store's diagnosis tools are described in
CLAUDE.md." The hal arm is round D's MCP arm with `AlwaysLoad` on. Twenty runs, one at a time, interleaved, every run
passing. Ratios of means over back-to-back pairs, with a bootstrap 95% interval:

| Task | Arm | Runs | Median requests | Median tokens | Tokens | Requests | Wall clock |
|---|---|---|---|---|---|---|---|
| store | base | e01, e04, e07, e10 | 11.5 | 123,712 | 1 | 1 | 1 |
| store | cli | e02, e05, e08, e11 | 6 | 74,248 | 0.60 [0.52, 0.72] | 0.51 [0.44, 0.63] | 0.81 [0.43, 1.66] |
| store | MCP, `AlwaysLoad` | e03, e06, e09, e12 | 8.5 | 95,814 | 0.88 [0.57, 1.32] | 0.89 [0.56, 1.49] | 0.77 [0.38, 1.10] |
| cart | base | c01, c03, c05, c07 | 6 | 59,980 | 1 | 1 | 1 |
| cart | cli | c02, c04, c06, c08 | 6 | 67,104 | 1.09 [0.99, 1.21] | 0.96 [0.86, 1.10] | 1.29 [1.09, 1.46] |

- The store cli runs share one shape: `--list`, read the seven modules, `explain shipping` with a reproducing
  sequence, edit and build with the check chained, answer; e05 also ran a fuzz script. That is 5–7 requests, against
  round D's 8 for the MCP arm, which spent one on the tool search and one on a separate live check.
- No store cli run wrote a test. Three of four baseline runs and two of four `AlwaysLoad` runs did (`tests-changed`),
  and those runs are the long ones: e09 took 18 requests.
- `AlwaysLoad` removed the tool search in all four runs; with that spread, four runs cannot separate it from round D's
  MCP arm.
- On the cart task the cli arm reads both files as the baseline does, and adds the `--list` output (about 2.5k
  characters) and `explain`; wall clock grows by the 1.7 s start of each call.

The cli arm's `CLAUDE.md` asks for chained commands, which the MCP arm cannot follow, so the comparison measures the
method, not the transport alone.

## 7. Correction to the #66 evaluation

The evaluation says `explain` named the comparer cutoff in 6 of 6 runs. The transcripts show 2 of 6 (w03, w06). In
w09–w12 the last dispatched message left the total right, and `explain` reported only the history after the last run.

## 8. Recommendations

1. Keep `AlwaysLoad` off by default.
2. Keep `dispatch` text-only, and strip structured content wherever a text block carries the meaning.
3. Measure from the `result` record, with a warm first request and serial runs; the per-message usage undercounts
   output.
4. Spend agent runs on tasks where reading the source does not reveal the cause. Run the store task at 8 or more runs an
   arm, and add #66's runtime-only and larger-codebase tasks, before applying the decision rule.
5. Offer agents the tools through the shell: `tools/mcp-call.fsx` and a `CLAUDE.md` section. It removes the tool
   search and the separate live check, and it needs no supervisor. Keep the MCP server for clients without a shell.
6. A compiled client would start in about 0.3 s instead of 1.7 s on each call; not built.
7. Not measured: the cli arm at 8 or more runs, a cli arm without the chaining habits, and a project whose
   `CLAUDE.md` holds other instructions too.
