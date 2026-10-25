# Ranvier.Mcp.Sample

A shopping cart held in a Ranvier `Mvu`, served over MCP on stdin and stdout by `Ranvier.Mcp`. An MCP client
reads the cart's published state, dispatches the cart's messages, and, in a traced build, asks why a value
changed or did not.

This is the sample of the #168 spike. `Ranvier.Mcp` is not packed yet.

## What it exposes

| Kind | Name | Meaning |
|------|------|---------|
| value | `lines` | `[{ "sku", "unitPrice", "quantity" }]`, prices in cents |
| value | `subtotal` | the sum of the line prices |
| value | `discount` | the discount percentage, 0 to 100 |
| value | `total` | the subtotal less the discount, rounded towards zero |
| message | `add_item` | `{ "sku": string, "unitPrice": int, "quantity": int }`; quantity at least 1 |
| message | `remove_item` | `{ "sku": string }` |
| message | `set_discount` | `{ "percent": int }`, 0 to 100 |
| node | `subtotal`, `discount`, `total` | targets of the traced provenance tools |

Each value is a resource, `ranvier://state/<name>`, and `ranvier://state` holds them all at one version. The
tools are `snapshot`, `dispatch`, `wait_version` and, in a traced build only, `explain`, `graph` and `node`. The adapter contract is in
[docs/content/guide/mcp.md](../../docs/content/guide/mcp.md).

The expectation the cart must meet: after any sequence of messages, `total` equals the sum of
`unitPrice * quantity` over `lines`, less `discount` percent, rounded towards zero.
`tests/Ranvier.Mcp.Tests/SampleTests.fs` checks it from published snapshots alone.

## Run it

From the repository root:

```sh
dotnet build examples/Ranvier.Mcp.Sample -c Release
dotnet examples/Ranvier.Mcp.Sample/bin/Release/net10.0/Ranvier.Mcp.Sample.dll
```

The process speaks MCP (JSON-RPC, protocol `2025-11-25`) on stdin and stdout and exits when stdin closes.
Nothing else is written to stdout.

`--defect` builds the cart with a seeded defect: the `total` memo reads the discount untracked, so a discount
change leaves the total stale until the next line change. The validation experiment
([docs/.ai/research/2026-10-24-issue-168-experiment.md](../../docs/.ai/research/2026-10-24-issue-168-experiment.md))
uses it.

### The traced build

The provenance tools exist only when Ranvier is built with `RANVIER_TRACE`. Debug builds are traced by default;
a Release build is traced with `-p:RanvierTrace=true`:

```sh
dotnet build examples/Ranvier.Mcp.Sample -c Release -p:RanvierTrace=true
```

An untraced build lists none of the trace tools, and a call to one is an unknown-tool error.

## Run it beside SageFs

Register the sample as a second stdio server next to `sagefs` in the client's `.mcp.json`. The repository's
`.mcp.json` already holds the `sagefs` entry; the `ranvier-cart` entry is the addition:

```json
{
  "mcpServers": {
    "sagefs": {
      "type": "stdio",
      "command": "sagefs",
      "args": ["mcp"],
      "env": {}
    },
    "ranvier-cart": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["fsi", "--quiet", "tools/mcp-supervise.fsx", "examples/Ranvier.Mcp.Sample/bin/Debug/net10.0/Ranvier.Mcp.Sample.dll"],
      "env": {}
    }
  }
}
```

Build the sample before the client starts the server; the Debug path above is the traced build. The supervisor serves
a copy of the build, so `dotnet build` and `dotnet test` work while the server runs, and the next tool call serves the
new build with the dispatched messages replayed. Add
`"--defect"` to `args` to serve the defective cart.

The two servers own different things:

- **SageFs** owns evaluation: loading the projects, F# sessions, the REPL, hot reload and test runs. It sees the
  source and runs code you send it.
- **The Ranvier adapter** owns one running application's graph: the values the application chose to expose,
  the messages it authorised, and, traced, the provenance of the whole graph. It evaluates no code. The traced
  tools read every node in the trace log; the exposed node names are only the names they accept as `node`.

An agent edits and checks code through SageFs, then restarts the sample (the client restarts a stdio server
when it reconnects) and observes the running cart through `ranvier-cart`.
