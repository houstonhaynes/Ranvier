---
title: Streams
order: 8
---

> **Preview** — Ranvier is pre-release; its APIs may change.

## CI instruction counts
**Library comparison** shows the latest recorded engine measurements; **Trends** follows scenarios
across commits. Choose a runtime, metric or commit, filter scenarios, and toggle libraries in the
legend. The site remembers your tab; runtime and metric selections carry between tabs.
See [chart controls and uncertainty](counters.md#interactive-chart-controls) for bands, relative values
and library-counter markers.

<div data-perf-tabs data-perf-data="streams"><p role="status">Loading recorded measurements…</p></div>
<link rel="stylesheet" href="/Ranvier/benchmarks/perf-charts.css">
<script src="/Ranvier/benchmarks/perf-charts.js"></script>

A [stream](../guide/streams.md) folds items from a long-running source into a value the graph reads. These scenarios measure Ranvier alone, on .NET and Node.js, and compare a stream with the nearest construction from other Ranvier primitives. Each variant is its own line. See [Benchmarks](index.md) for configuration, environment and caveats, and [Instruction counts](counters.md) for the counter methodology.

## stream-burst

`stream-burst-1`, `stream-burst-16` and `stream-burst-256` deliver 1, 16 or 256 items per round. A memo reads the delivered value and an effect reads the memo.

| Variant | One round |
| --- | --- |
| `Ranvier (stream, batched)` | Delivers the items to a `createStreamFold` that sums them, inside one `Batch`. |
| `Ranvier (AsyncSource, batched)` | Settles an `AsyncSource` with each running total, inside one `Batch`. |
| `Ranvier (AsyncSource, unbatched)` | Settles an `AsyncSource` with each running total, one settle per item. |

The batched variants publish once per round; the unbatched variant publishes once per item.

## stream-restart

Each operation writes a signal read by the node's start, which restarts the node. An effect reads the node.

| Variant | One operation |
| --- | --- |
| `Ranvier (stream)` | Restarts a `createStream`, then delivers one item to the new run. |
| `Ranvier (async memo, CancelPrevious)` | Restarts an async memo whose flights stay open, cancelling the previous flight. |
