---
title: Inspecting a running graph over MCP
order: 13
---

:::info
Spike — `Ranvier.Mcp` is an experiment for #168 and is not packed. Its names, tools and documents may change.
:::

`Ranvier.Mcp` serves a running application's graph to an MCP client, such as a coding agent. The application
chooses what the client reads and writes: named values and authorised Elmish messages. The client evaluates no code.
A traced build also serves `explain`, `graph` and `node`, which answer from the whole graph's trace log in compact
text. The nodes registered with `Expose.node` are names a client can pass as `node`, not a limit on what it can read.

The runnable sample, a cart over stdio, is
[examples/Ranvier.Mcp.Sample](https://github.com/shayanhabibi/Ranvier/tree/master/examples/Ranvier.Mcp.Sample). Its
README shows how to register it beside SageFs.

## Hosting the graph

`GraphHost.Start setup` starts a dedicated thread, builds a `Guarded` graph on it, and runs `setup` there. The host's
inbox drains on that thread, so `Graph.DispatchAsync` from a transport thread completes without a pump.

```fsharp
type Msg = Add of int

type App =
    {
        Mvu: Mvu<int, Msg>
        Exposure: Exposure
        Adapter: McpAdapter
    }

let decoders: (string * MessageDecoder<Msg>) list =
    [ "add", fun args -> Result.Ok (Add (args.GetProperty("amount").GetInt32 ())) ]

use host =
    GraphHost.Start (fun _ ->
        let mvu = Mvu.create 0 (fun (Add n) count -> count + n)
        let count = mvu.Select id
        let exposure = Exposure ()
        exposure |> Expose.value "count" (fun (n: int) -> JsonValue.Create n :> JsonNode) (fun () -> count.Value)
        exposure |> Expose.messages mvu decoders
        exposure |> Expose.node "count" count
        exposure.Start ()
        { Mvu = mvu; Exposure = exposure; Adapter = new McpAdapter (exposure) })

let adapter = host.Invoke(_.Adapter).Result
let transport = new StdioServerTransport ("counter", NullLoggerFactory.Instance)
let server = McpServer.Create (transport, adapter.Options)
server.RunAsync(CancellationToken.None).GetAwaiter().GetResult()
server.DisposeAsync().AsTask().GetAwaiter().GetResult()
host.Invoke(fun app -> app.Adapter.Dispose ()).Wait()
```

- `host.Invoke work` runs `work` on the graph thread with the setup's value and returns a task of its result, once the
  work and the effects it triggers have run. It is the only way into the graph from another thread. A `work` that
  throws faults the task. Called on the graph thread, the work runs before `Invoke` returns.
- `host.Invoke (work, cancellationToken)` cancels the task instead when the token is cancelled before the work starts.
- `host.GraphThreadId` is the managed thread id of the graph thread.
- A node touched from any other thread raises `InvalidOperationException`. `GraphHost.Start (options, setup)` builds
  the graph from `options` with the host's own dispatcher, and raises `ArgumentException` for a `ThreadAffinity` other
  than `Guarded`.
- An exception from `setup` is rethrown by `Start`, after the host thread has stopped.
- `host.Dispose ()` disposes the graph on its thread, after any work already running, and returns once the thread has
  stopped. After disposal, `Invoke` faults with `ObjectDisposedException`, and so does work still queued. It is
  idempotent.
- Construct the `McpAdapter` on the graph thread, after `exposure.Start ()`. `adapter.Options` is safe to use from any
  thread, and several servers may share it; each server's subscriptions are its own. `adapter.Dispose ()` stops
  notifying subscribers and is idempotent.

## Exposing state

Register everything inside the setup, before `exposure.Start ()`:

| Registration | Exposes |
|---|---|
| `Expose.value name encode read` | `read ()`, encoded to JSON by `encode`, as the value `name` |
| `Expose.messages mvu [ name, decoder ]` | each message for `dispatch`; a `MessageDecoder<'Msg>` is `JsonElement -> Result<'Msg, string>` |
| `Expose.messageShapes [ name, shape ]` | a one-line description of each message's arguments, shown in `dispatch`'s description and in its malformed-argument errors |
| `Expose.node name node` | `node` to the traced provenance tools; no effect in an untraced build |

Names use `[A-Za-z0-9_.~-]` and are unique within their kind. `Start` closes registration and creates one effect that
reads every value. The effect publishes a `Snapshot { Version; Values }`:

- Every value in a snapshot comes from the same flush.
- A flush that changes any encoding publishes one snapshot, one version higher. A batch publishes once.
- A memo cutoff, an equal write, or a re-run whose encodings all match the previous snapshot publishes nothing.

`exposure.Snapshot` is safe to read from any thread and does not enter the graph.

### Reading and dispatching in process

The adapter's tools are thin over `Exposure` members, which application code and tests call directly:

| Member | Result |
|---|---|
| `exposure.Dispatch (name, args)` | a task of `Ok snapshot`, published once the message and its effects have run, or `Error reason` |
| `exposure.WaitVersion (version, timeout)` | a task of `Some snapshot`, the first at or past `version`, or `None` once `timeout` elapses |
| `exposure.Published` | an event of `Publication { Snapshot; Changed }`, where `Changed` lists the names whose encoding changed, in registration order |
| `exposure.Names`, `exposure.MessageNames`, `exposure.Nodes` | the registered values, messages and name/node pairs, in registration order |

`Dispatch` and `WaitVersion` are callable from any thread after `Start`. `Dispatch` decodes on the graph thread; a
rejected message dispatches nothing, and the task faults when `update` raises. `WaitVersion` does not enter the graph.
`Published` is raised on the graph thread, inside the publishing flush, after `Snapshot` holds the new snapshot, so
subscribe to it through `host.Invoke`:

```fsharp
let run (host: GraphHost<App>) =
    task {
        do! host.Invoke (fun app ->
            app.Exposure.Published.Add (fun publication ->
                printfn "version %d changed %A" publication.Snapshot.Version publication.Changed))

        let! exposure = host.Invoke _.Exposure
        let args = JsonDocument.Parse("""{"amount": 2}""").RootElement

        match! exposure.Dispatch ("add", args) with
        | Result.Ok snapshot -> printfn "count is %O at version %d" snapshot.Values["count"] snapshot.Version
        | Result.Error reason -> printfn "rejected: %s" reason

        let! later = exposure.WaitVersion (exposure.Snapshot.Version + 1L, TimeSpan.FromSeconds 1.0)
        printfn "%A" (later |> Option.map _.Version)
    }
```

## Resources

| URI | Document |
|---|---|
| ranvier://state | `{"version": n, "values": {name: value}}` |
| ranvier://state/{name} | `{"version": n, "value": value}` |

Both support `resources/subscribe`. Each publication sends `notifications/resources/updated` for ranvier://state and
for each value whose encoding changed. The adapter pins MCP protocol `2025-11-25`, since `2026-07-28` replaces
`resources/subscribe` with `subscriptions/listen`.

## Tools

| Tool | Arguments | Result |
|---|---|---|
| `snapshot` | none | the ranvier://state document |
| `dispatch` | `message`: an exposed name, and `args`: JSON for its decoder; or `messages`: `[{message, args}, ...]` | per message, the exposed values it changed; then every exposed value at the final version |
| `wait_version` | `version`; `timeoutMs` (default 5000, at most 60000) | the first snapshot at or past `version` |

`dispatch` is listed only when messages are exposed, and its input schema enumerates them. Each decoder runs on the
graph thread, and the messages run in order, each after the previous one's effects. A step reads
`2 set_discount {"percent":10} → v3: discount 0→10`, and the last line holds every exposed value. The `snapshot` and
`wait_version` results are the JSON document as text and as structured content. `dispatch` returns text alone: Claude
Code shows a model the structured content in place of the text, which would hide the step lines.

A rejected call is a tool result with `isError: true` and the reason as text:

- `dispatch`: an unknown message, a missing or non-string `message`, a decoder `Error`, or a decoder that raises (for
  example, a missing property). The decoder's `Error` text is the reason, followed by the message's shape when one is
  described. In a sequence, the error names the rejected message's position; the messages before it stay dispatched,
  and the error lists their steps.
- `wait_version`: a non-integer `version`, a negative `timeoutMs`, or a timeout.

An exception from `update` is also a tool error; the model write it interrupted does not happen.

## Traced builds

With `RANVIER_TRACE` (Debug builds, or `-p:RanvierTrace=true`), the adapter also lists these tools. Each reads the
trace log on the graph thread and answers in compact text:

| Tool | Arguments | Answer |
|---|---|---|
| `explain` | `node`; `messages`, as in `dispatch` | the dispatched steps, then why the node holds its value, in at most a few lines |
| `graph` | none | node counts by kind, the exposed nodes, and the nodes the last message moved; at most 2,000 characters |
| `node` | `node`; `around` (0 to 2 hops, default 1); `detail` (`brief` or `full`); `limit` (default 25); `cursor` | the node and its sources and observers within `around` hops, one line each; at most 4,000 characters a page |

A node reads as `name#id`: the name registered with `Expose.node`, a `Trace.label`, or the `let` binding at the node's
creation site. Each line gives the node's kind, its creation site as `file:line`, its value and its sources, for
example `total#5 Memo Cart.fs:155 = 1200 reads [subtotal#4]`. When the source root holds exactly one file of the site's
name, the answer adds the line's text. `node` takes an exposed name, a node name, `#id` or an identity path; a miss is a
tool error that lists the exposed nodes.

`explain` answers, for one node:

- its creation line, its value and the sources it reads;
- the cause chain of its last run, from the dispatched message through each write and move;
- why it has not re-run since: checked clean against named sources, propagation stopped upstream, unobserved, or queued;
- `unread:` nodes related to it that changed after its last run and that it does not read, such as a value read with
  `Peek`;
- `cutoff:` upstream nodes that re-ran after a source moved and kept their value, with the value kept, such as a memo
  whose comparer calls a new result equal. Without one since the node's last run, `earlier cutoff:` lines name the
  latest ones before it. A node that reads a dispatched model alone, such as an `Mvu.Select`, keeps its value by design
  and is left out;
- `comparer:` when no cutoff is reported, up to two upstream nodes whose creation line, or the line after it, names a
  comparer (`createMemoWithComparer` and the other `...WithComparer` builders), with their values: the places a later
  message can be cut off.

Values are encoded without codecs, so a non-scalar value appears as its `ToString` text.

`McpAdapterOptions` configures the adapter, `new McpAdapter (exposure, options)`:

| Field | Default | Effect |
|---|---|---|
| `RawTraceTools` | `false` | also lists `trace_state`, `trace_lookup`, `why`, `why_not` and `waiting_on`, which return `TraceJson` documents; `trace_state` grows with the graph without bound |
| `SourceRoot` | the nearest directory holding `.git` at or above the current directory | the directory whose F# and C# files give a creation line's text |
| `AlwaysLoad` | `false` | marks `dispatch` and `explain` with `_meta["anthropic/alwaysLoad"]`, so Claude Code loads them without a tool search |

The server's instructions make `explain {node, messages}` the first call of a diagnosis, then the fix of the named
line. Always-loaded tools join the head of the
prompt, so a session with `AlwaysLoad` starts with a cold prompt cache unless a session with the same tools ran within
the cache lifetime: about 40,000 tokens written instead of read, against one tool-search request without it. An untraced
build lists none of the traced tools, and calling one is an unknown-tool error.

## Serving across rebuilds

A server started from a build's output directory holds its assemblies open, so a rebuild of the same output fails
while the server runs. `tools/mcp-supervise.fsx` serves the application from a copy instead:

```json
{
  "mcpServers": {
    "ranvier-cart": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["fsi", "--quiet", "tools/mcp-supervise.fsx", "examples/Ranvier.Mcp.Sample/bin/Debug/net10.0/Ranvier.Mcp.Sample.dll"]
    }
  }
}
```

On each `tools/call`, when the output's assembly is newer than the running copy, the supervisor starts the new build,
repeats the client's `initialize`, and dispatches again every message accepted so far, as one `dispatch {messages}`.
The tool result that follows a reload starts with a line naming the reload and the replay's final state. Resource
subscriptions end at a reload. A reload waits until the assembly has kept its timestamp for half a second, which costs
nothing when the build finished earlier; it then takes about 0.35 seconds for the cart sample.

## Calling the tools from the shell

`tools/mcp-call.fsx` calls one tool of the application's build output and prints the result's text:

```bash
dotnet fsi --quiet tools/mcp-call.fsx examples/Ranvier.Mcp.Sample/bin/Debug/net10.0/Ranvier.Mcp.Sample.dll explain '{"node": "total", "messages": [{"message": "add_item", "args": {"sku": "a", "unitPrice": 250, "quantity": 2}}]}'
```

Each call starts the application, runs the handshake, makes the call and stops the application, in about 1.7 seconds
for the samples. Two calls share no state, so `explain {node, messages}` and `dispatch {messages}` carry the whole
sequence; a rebuild needs no supervisor. `--list` prints the server's instructions and each tool's description and
argument names. The exit code is 1 for a tool error.

An agent reaches the tools through its shell, described in the project's `CLAUDE.md` or `AGENTS.md`, with no MCP
server configured. The tool search before the first call goes, and a check after a fix runs in the same command as the
build: `dotnet build ... && dotnet fsi --quiet tools/mcp-call.fsx ... dispatch '...'`. On the nine-file store task
this took half the requests of the baseline without tools, and 0.60× its tokens; see
[Research on agents and the MCP tools](mcp-research.md).
