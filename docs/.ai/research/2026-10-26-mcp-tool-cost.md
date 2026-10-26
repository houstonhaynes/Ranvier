# Why the Ranvier MCP workflow costs more than source and tests alone

Status: research note, 2026-10-26. Branch `research/mcp-tool-cost`, cut from `origin/master` at `6784ff01`. Read
alongside the #168 experiment [R1] and the #66 scope note [R2]. Product code is unchanged.

## 1. Question

In #168's comparison, the MCP workflow lost on wall time and tokens in both rounds. Is the MCP message format
(descriptions, inputs, response bodies) the cause, and what would make the tools cheaper than the source-and-tests
workflow?

## 2. Answer

**The format is not the cause.** Tokens follow the number of model turns, and the MCP workflow takes more turns.

- Across the 20 runs, total tokens fit `67.6k + 65.5k × tool calls` with r² = 0.93. Wall clock fits
  `47 s + 8.0 s × tool calls` (r = 0.75). One extra call costs about 65k tokens and 8 s, whatever it returns.
- About 95% of every run's tokens are cache reads. Each turn re-reads the context, which starts at 67–75k tokens
  (system prompt, tool definitions, the prompt) before the agent does anything.
- Across the 10 MCP runs, the MCP results total 56k characters (about 16k tokens). Re-read on every later turn,
  they account for about 67k of the arm's 9.77M tokens: **0.7%**. The turns that MCP calls caused account for
  about 3.9M tokens: **40%**.
- Of the 57 MCP calls, 31 managed the server's lifecycle (start, list tools, retry the connection, stop to rebuild,
  restart), 18 reproduced or re-verified the symptom, and 8 asked a provenance question. `trace_state`,
  `trace_lookup`, `waiting_on`, `wait_version` and the resources were never used.

So the user's reading is right: the runs spent their calls on server housekeeping and on reproductions the failing
tests already gave, and a provenance answer needed two calls (`why`, then `why_not`) plus a source read to become
a location. The cheapest fixes remove calls: a server that survives a rebuild, one causal query that names the
source line, and a dispatch that runs a sequence. A smaller format then keeps large graphs under the client's limits.
On the two-file sample, those fixes bring the MCP arm to roughly the baseline's call count, not below it (section 6).

## 3. Evidence

### 3.1 The comparison

The only agent-debugging comparison in the repository is #168's, accepted by the user on 2026-10-25 [R1]. #66's
preliminary pass (one task, three arms, native MCP) has not run: no evaluation note exists on any branch, and
`PRIORITIES.md` records none. Two rounds, five runs per arm per round, the same model and prompt:

| Round | Arm | Median tool calls | Median tokens | Median wall clock |
|---|---|---|---|---|
| 1 (`Peek` in `Cart.fs`) | baseline | 6 | 471k | 97 s |
| 1 | MCP | 12 (5 through the bridge) | 916k | 121 s |
| 2 (comparer in `Pricing.fs`) | baseline | 11 | 721k | 158 s |
| 2 | MCP | 14 | 1,063k | 170 s |

Every run in both arms succeeded in one repair iteration. MCP runs reached the server through a CLI bridge
(`mcp-bridge.mjs serve|tools|call|stop`), not native client tools.

### 3.2 Where the tokens went

The run transcripts are in the session that ran the experiment (`7b25c7be…`; round 1 under
`subagents/workflows/wf_e1435be2-b07/`, round 2 under `subagents/`). The usage totals reproduce the note's figures
exactly (round 2 MCP: 5,114,669). Each call is classified by its command, and charged with the context of the
turn it caused plus the re-reads of its result by later turns (`classify.py`, section 9).

| Category | MCP arm, 10 runs: calls | Result chars | Tokens charged | Share of arm | Baseline arm, 10 runs: calls | Share |
|---|---|---|---|---|---|---|
| Read and edit source | 48 | 186k | 4.03M | 41% | 45 | 52% |
| Build and test | 22 | 15k | 1.59M | 16% | 33 | 41% |
| MCP lifecycle (serve, tools, stop, retries) | 31 | 20k | 1.91M | 20% | — | — |
| MCP state (dispatch, snapshot) | 18 | 16k | 1.35M | 14% | — | — |
| MCP trace (`why`, `why_not`) | 8 | 21k | 0.66M | 7% | — | — |

The MCP arm made 11 fewer build-and-test calls than the baseline and 3 more source reads, then added 57 MCP calls.
The MCP calls replaced some test runs and none of the source reads. In round 1, eight of ten runs report spotting
the defect while reading `Cart.fs`, before any test or MCP call [R1]; in round 2's w02 the agent read both sample
files before its first MCP call. Every fix needed the file open for the edit, and the provenance answer named node
ids, so a source read was still needed to find the line.

The ten MCP runs made 20 `serve` and 20 `stop` calls: on average the server started twice per run, once to
reproduce and again after the fix. In w02 the stop came before the rebuild, because the running server holds the
sample's DLL, which the rebuild overwrites.

### 3.3 Message sizes

Measured over stdio against the traced `--defect` cart (`drive.py`), after `add_item ×2` and `set_discount 10`:

| Item | Text bytes | Wire bytes (JSON-RPC line) | ≈ tokens of text (÷3.5) |
|---|---|---|---|
| `tools/list`, 8 tools | 2,198 (definitions) | 2,359 | 630 |
| `snapshot` | 71 | 297 | 20 |
| `dispatch` | 117–164 | 429–563 | 35–50 |
| `why total` | 855 | 2,675 | 245 |
| `why_not total` | 101 | 387 | 30 |
| `waiting_on total` | 83 | 341 | 25 |
| `trace_lookup total` | 145 | 536 | 40 |
| `trace_state` (6 nodes) | 1,881 | 5,628 | 540 |

The wire is about 3× the text, because each result carries the document twice: as an escaped string in
`content[0].text` and as `structuredContent`. The spec recommends the duplication for compatibility [P3]. The
bridge printed the whole result, so the agents read both copies.

Tool definitions add about 630 tokens per turn, under 1% of the 67–75k starting context. The tool count is not a
cost on this evidence.

Scaling, over synthetic graphs of `width` chains of `depth` memos with five writes (`scale.fsx`, in-process client):

| Nodes | `why` text | `trace_state` text | `trace_state` wire |
|---|---|---|---|
| 6 | 1,231 | 2,070 | 6,256 |
| 22 | 1,236 | 6,621 | 19,678 |
| 102 | 2,636 | 29,577 | 87,190 |
| 502 | 6,211 | 147,320 | 430,676 |
| 1,002 | 5,036 | 294,543 | 860,122 |

`trace_state` costs about 294 bytes of text per node and is unbounded. `why` grows with the chain's depth (three
steps per node on the path), not with the graph. Claude Code saves any text result over 50,000 characters to a file
and replaces it with the file's path [P4]. `trace_state` crosses that at about 170 nodes, so on a real application
it costs a second call to read the file, then 25k+ tokens on every later turn.

### 3.4 What the documents make the agent do

The `why` document for the cart (abridged):

```json
{"schema":3,"kind":"why","seq":130,"body":{"node":5,"run":3,"steps":[
 {"seq":99,"kind":"RunStart","node":5,"other":6,"value":null},
 {"seq":94,"kind":"Mark","node":5,"other":4,"value":null},
 {"seq":93,"kind":"Moved","node":4,"other":0,"value":{"case":"Exact","value":{"case":"Int","value":1200}}}, …],
 "root":{"case":"UserWrite","write":73}}}
```

- **Ids without names or locations.** Steps name nodes by number. `label` is null on every node: the names given
  to `Expose.node` reach the tool's `enum`, not the trace. Mapping `5` to `total` and to `/Cart.fs:155` takes
  `trace_state` or `trace_lookup`, or a read of the source. #193 now makes paths name the application's line;
  the `why` document still does not carry them.
- **The answer is split across tools.** "Why is `total` stale?" is answered by `why` (its last run came from the
  `add_item` write), `why_not` (`CheckedClean` against `[4]`) and `trace_state`'s source edges (`5` reads only `4`).
  The agent assembles the conclusion; the server holds every fact needed to state it.
- **Envelope and case wrappers.** `{"case":"Exact","value":{"case":"Int","value":1200}}` spends 46 bytes on `1200`.
  Compact lines carry the same `trace_state` in 385 bytes instead of 1,855 (4.8×) and the `why` chain in 453
  instead of 831 (1.8×); a three-sentence server-side explanation of the defect is 280 bytes (`compact.py`).

On the cart those savings are about 500–1,000 tokens per run, under 0.2%. They matter at scale (3.3), not here.

## 4. Prior art

| Source | Practice | Bearing |
|---|---|---|
| Anthropic, "Writing effective tools for AI agents" [P1] | Tools that "handle multiple discrete operations" (`schedule_event`, `search_logs`, `get_customer_context`); "return only high signal information"; resolve UUIDs to meaningful names; "pagination, range selection, filtering, and/or truncation" with sensible defaults; a `response_format` enum (concise 72 vs detailed 206 tokens); Claude Code caps responses at 25,000 tokens. | One causal query instead of three; names and locations instead of ids; bounded state. |
| Anthropic, "Code execution with MCP" [P2] | "Every intermediate result must pass through the model"; filtering in code cut one workflow from 150,000 to 2,000 tokens. | Compute the conclusion in the server; return the conclusion. |
| Anthropic, "Effective context engineering for AI agents" [P5] | Just-in-time retrieval: agents "maintain lightweight identifiers" and load data on demand; "the smallest set of high-signal tokens". | The progressive-disclosure design of R4: ids and paths first, detail on request. |
| MCP 2025-11-25, Tools [P3] | `structuredContent` with a text copy for compatibility; optional `outputSchema`; resource links; `tools/list` pagination; tool errors as actionable feedback. | Keep JSON in `structuredContent` for programs; make the text block the compact, model-facing form. |
| Claude Code MCP docs [P4] | 10,000-token warning, 25,000-token default cap; text over 50,000 characters goes to a file; `_meta["anthropic/maxResultSizeChars"]`; tool search defers definitions and loads them through `ToolSearch`; `_meta["anthropic/alwaysLoad"]`; server instructions guide search. | Bound every result below 50k characters; mark the core tools always-loaded; write server instructions. |
| Chrome DevTools MCP [P6] | `list_network_requests` / `list_console_messages` paginate (`pageSize`, `pageIdx`); `get_network_request {reqid}` returns one item in full; a trace stops with insights, and `performance_analyze_insight` expands one; `take_snapshot` is a text a11y tree with uids; `--slim` reduces the tool set. | List with ids, then fetch one; summarise server-side, expand on request. |
| Playwright MCP [P7] | Text accessibility snapshot with refs; `depth` limits the tree; `browser_find` is "cheaper than capturing the whole snapshot"; `filename` saves a snapshot instead of returning it; the README recommends its CLI for coding agents because MCP loads "verbose accessibility trees into the model context". | Depth-limited views and a find-by-text tool instead of a whole-state dump. |
| Serena [P8] | `get_symbols_overview(depth)` → `find_symbol(name_path, depth, include_body=False, include_info, max_matches, max_answer_chars)`; over `max_matches` it returns "a shortened result … which allows refining the search". | The slice → fidelity ladder the user asked about, with a refusal that says how to narrow. |

The common pattern: an overview with stable short handles, a narrow slice around one handle, then full fidelity
for one item. Each step is bounded, and an oversized answer says how to narrow it instead of spilling.

## 5. Recommendations, ranked by expected savings

Savings use the fit of 3.2: one avoided call ≈ 65k tokens and 8 s. "Per run" is the MCP arm's mean over the ten
runs (5.7 MCP calls: 3.1 lifecycle, 1.8 state, 0.8 trace).

| # | Change | Calls saved per run | Tokens saved per run | Notes |
|---|---|---|---|---|
| R1 | **The server outlives a rebuild.** Serve the application from a shadow copy, so `dotnet build` can overwrite the output, and reload it on a new build (a `reload` tool, or a watch on the output), replaying the message journal (#184) so the agent's reproduction survives. Register the server natively, as #66 decided. | 2–3 | 130–200k | Removes the stop/restart pair and the bridge's serve/tools/retry calls. A native server that locks its DLL is worse than the bridge: the agent cannot restart it. |
| R2 | **One causal query.** `explain {node}` returns, in one call: the node's value and location, why it last ran (the root write and the message that caused it), why it did not re-run since (the `why_not` reason), and the changed sources it does not read (`discount changed at version 4; total reads [subtotal]`). Names, `file:line` and the source line's text in every step. | 1–2 | 65–130k | Replaces `why` + `why_not` + an id lookup, and the source read that locates the line. |
| R3 | **Dispatch a sequence; verify by replay.** `dispatch` takes `messages: [...]` and returns, per step, the exposed values that changed. After a reload, `replay` re-runs the journaled sequence and returns the same per-step changes, so before and after compare in one call. | ~1 | ~65k | 18 state calls in 10 runs; agents already batched dispatches in shell loops, so the gain is the re-verification after the fix. |
| R4 | **Progressive disclosure instead of `trace_state`.** `graph {}`: counts by kind, the exposed nodes with ids, names, paths and values, bounded at 2,000 characters. `node {id or name, around: 0–2, detail: "brief"/"full"}`: the node and its sources and observers to `around` hops, as compact lines; `full` adds runs, status and owner. Lists take `limit` and return a `cursor`; an over-limit answer returns the first page and says which argument narrows it. `trace_lookup` and `waiting_on` fold into `node`. | 0 on the cart; 1 per state read on graphs over ~170 nodes | 25k+ per later turn at 1,000 nodes | Unused in the runs, so no saving on the sample; it bounds the cost on real applications. Tools go from 8 to 6 (`snapshot`, `dispatch`, `explain`, `graph`, `node`, plus `reload`/`replay`), with `wait_version` behind an option. |
| R5 | **Compact text, JSON alongside.** The text block holds compact lines (`#5 total Memo /Cart.fs:155 =1200 reads [#4 subtotal]`); `structuredContent` keeps the `TraceJson` document, described by an `outputSchema`. `Expose.node` names become trace labels. | 0 | 0.5–1k on the cart; 4.8× on state at any size | Least important on this evidence. Removes the second copy from the model's view only where the client shows text alone. |
| R6 | **Server instructions and always-loaded core tools.** Set `ServerInstructions` (when to use the server, the order: reproduce with `dispatch`, ask `explain`, read the named line) and `_meta["anthropic/alwaysLoad"]` on `dispatch` and `explain`. | ~1 in native Claude Code | ~65k | With tool search on, the first use of a deferred tool costs a `ToolSearch` turn [P4]. |

**Not recommended:** removing `structuredContent` (the spec's compatibility guidance, and programs read it);
cutting the tool count for its own sake (definitions cost under 1%); `response_format` on every tool (R4's `detail`
covers the one tool whose size varies).

## 6. Expected outcome

R1–R3 and R6 remove an estimated 4–6 of the MCP arm's 5.7 MCP calls per run while keeping its reproduction and
causal answer. On the cart's round-2 task that projects the MCP arm from a median of 14 calls to 9–10, against the
baseline's 11: parity, within the runs' spread. The sample's failing property test and two short files give the
baseline a minimal path, so tool design alone cannot show a large win there. #66's harness tasks (no failing test,
cause in another file, symptom only at runtime) are where a one-call causal answer can replace several reads and
test runs; R1–R6 make that measurement fair instead of charging the MCP arm for server housekeeping.

## 7. Acceptance criteria for an implement issue

Scope: `src/Ranvier.Mcp`, `examples/Ranvier.Mcp.Sample`, `tests/Ranvier.Mcp.Tests`, `docs/content/guide/mcp.md`.
`src/Ranvier`'s public API is unchanged, except that the trace may accept labels for exposed nodes.

1. **Rebuild without restart (R1).** With the sample served over stdio, `dotnet build examples/Ranvier.Mcp.Sample`
   succeeds. The next tool call (or `reload`) serves the new build, and the exposed state after it equals the state
   reached by replaying the journal from before the reload. A test drives: start, dispatch, rebuild a changed
   `Cart.fs` (or swap an assembly in a fixture), reload, assert the new behaviour and the replayed version.
2. **`explain` (R2).** On the `--defect` cart after `add_item` and `set_discount 10`, one `explain {node: total}`
   call returns text of at most 1,000 characters that names `total`, `/Cart.fs:155` and the line's text, `discount`,
   and states that `total` does not read `discount`. On the round-2 comparer defect it names the `offer` memo, its
   path and the comparer cutoff. A traced test asserts both, against the text block.
3. **Sequence dispatch and replay (R3).** `dispatch {messages: [...]}` applies the messages in order, stops at the
   first rejected message with a tool error naming its index, and returns per step only the exposed values that
   changed. `replay` returns the same shape. Each step of the cart's script is at most 200 characters.
4. **Bounded output (R4).** On a 1,000-node traced graph (as in `scale.fsx`), no tool result's text exceeds 20,000
   characters; `graph {}` is at most 2,000; `node {around: 1}` is at most 4,000. An over-limit list returns a
   `cursor` and a sentence naming the narrowing argument. `trace_state` is removed, or kept only behind an
   `Exposure` option documented as unbounded.
5. **Names in every document (R5).** Every node registered with `Expose.node` appears by name, with its
   `file:line`, in every text block that mentions it. `structuredContent` holds the `TraceJson` document, and each
   traced tool declares an `outputSchema`.
6. **Instructions and loading (R6).** `ServerInstructions` is set (at most 1,000 characters). `dispatch` and
   `explain` carry `_meta["anthropic/alwaysLoad"] = true`. `tools/list` holds at most 6 tools and 2,500 bytes of
   definitions in a traced build.
7. **Re-measure.** Run #66's preliminary pass with native MCP on the round-2 task (three arms, three runs each) and
   record per run: tool calls by category (the classification of 3.2), tokens, wall clock. Target: the MCP arm's
   median tool calls at most the baseline's + 1, and median tokens at most 1.1× the baseline's. The report lands in
   `docs/.ai/research/<date>-issue-66-evaluation.md`.
8. Gates: `format --dry-format`, `build.fsx -- build`, `build.fsx -- test` unfiltered, the traced Mcp stage, and
   `build.fsx -- docs`.

## 8. Open questions for the user

1. **Issue placement.** File R1–R6 as one implement issue under #66 (milestone "Agent-observable reactive core"), or
   split R1 (server lifecycle) from R2–R6 (tool surface)? R1 is the largest saving and the most design work.
2. **Reload mechanism.** Shadow copy with a collectible `AssemblyLoadContext` keeps one process; a supervisor that
   restarts a child process is simpler and survives type changes. Which does the user prefer to evaluate first?
3. **`trace_state`.** Remove it, or keep it behind an option for programs that want the whole state?

## 9. Method and reproduction

Scripts are in the shared notes folder of the session group that produced this note, `mcp-tool-cost/`:

- `drive.py` — drives `Ranvier.Mcp.Sample.exe --defect` (traced Debug build of `tests/Ranvier.Mcp.Tests`) over
  stdio and prints the bytes of each response.
- `scale.fsx` — builds `width × depth` memo graphs in a `GraphHost`, connects an in-process client, and prints the
  sizes of `snapshot`, `why`, `why_not` and `trace_state`.
- `compact.py` — renders the cart's `trace_state` and `why` as compact lines and compares sizes.
- `transcripts.py`, `classify.py` — per-run usage, call categories and token attribution from the experiment's
  transcripts.

Token figures for transcripts are the API's usage counts. Token figures for payloads are estimates at 3.5
characters per token; no tokenizer was available.

## Sources

Accessed 2026-10-26.

- [R1] #168 experiment, `docs/.ai/research/2026-10-24-issue-168-experiment.md`
- [R2] #66 scope note, `docs/.ai/research/2026-10-25-issue-66.md`; issue #66:
  https://github.com/shayanhabibi/Ranvier/issues/66
- [P1] Anthropic Engineering, "Writing effective tools for AI agents":
  https://www.anthropic.com/engineering/writing-tools-for-agents
- [P2] Anthropic Engineering, "Code execution with MCP: building more efficient AI agents":
  https://www.anthropic.com/engineering/code-execution-with-mcp
- [P3] MCP specification 2025-11-25, Tools: https://modelcontextprotocol.io/specification/2025-11-25/server/tools
- [P4] Claude Code docs, MCP (output limits, `anthropic/maxResultSizeChars`, tool search, `anthropic/alwaysLoad`):
  https://code.claude.com/docs/en/mcp
- [P5] Anthropic Engineering, "Effective context engineering for AI agents":
  https://www.anthropic.com/engineering/effective-context-engineering-for-ai-agents
- [P6] Chrome DevTools MCP, tool reference:
  https://github.com/ChromeDevTools/chrome-devtools-mcp/blob/main/docs/tool-reference.md
- [P7] Playwright MCP README: https://github.com/microsoft/playwright-mcp
- [P8] Serena, `symbol_tools.py`: https://github.com/oraios/serena/blob/main/src/serena/tools/symbol_tools.py
