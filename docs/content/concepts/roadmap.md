---
title: Roadmap
order: 6
---

:::warning
**Preview.** Ranvier is pre-release; its APIs may change.
:::

The guide describes current source. See [Installation](../guide/installation.md) for published
versions and local builds. Exploratory items below have no delivery schedule.

## Implemented in current source

- **Reactive graph:** signals, lazy memos, effects, equality cutoffs and fixed-source bindings.
  See [Getting started](../guide/getting-started.md), [Equality](../guide/equality.md) and
  [Bindings](../guide/bindings.md).
- **Ownership:** roots and owning computations dispose children and cleanups in reverse acquisition
  order. [Contracts](contracts.md) covers lifetime, failure propagation and thread access.
- **Async state:** pending propagation, boundaries, async sources, task flights with per-node
  policies and restarting stream memos. [Async and pending](../guide/async-and-pending.md)
  links the individual APIs. [Timed values](../guide/timing.md) control admission independently.
- **Collections:** keyed and index projections, direct edits, lookups, selectors, views and aggregates.
  Change readers have bounded independent cursors and reset recovery. See
  [Collections](../guide/collections.fsx) and [Aggregates](../guide/aggregates.fsx) for contracts and costs.
- **Editable state:** [editable values](../guide/editable.md), [drafts](../guide/drafts.md) and
  [forms](../guide/forms.md), plus an [MVU bridge](../guide/elmish.md) over selector memos.
- **Remote data:** [Ranvier.Query](../guide/queries.md) shares acquired query leases, retains refresh
  data and reconciles mutation receipts across loaded queries.
- **Persistence and replay:** [persisted inputs](../guide/persistence.md) restore signals and
  collections; the [input journal](../guide/journal.md) records external inputs for diagnostic
  replay and re-enactment. Read the restore limitations before combining snapshots and journal tails.
- **Diagnostics:** [tracing](../guide/tracing.md) records node creation, causal runs, pending work
  and failure provenance, with bounded-log checkpoints and export.
- **C# integration:** delegate factories, XAML property/error bindings, commands and observable
  collections. See [C#](../guide/csharp.md).
- **Views:** shared templates support DOM mounting, server HTML and hydration, with motion and
  testing helpers. See [Views](../view/index.md), [Server rendering](../server/index.md) and
  [Testing views](../view/testing.md).
- **Fable and deployment:** local package builds include standalone Fable source projects; CI
  checks packed consumers. The core, C#, Elmish and Query packages have traced/untraced .NET 10
  `linux-x64` trim/AOT smoke checks. See [Installation](../guide/installation.md) for scope and
  [Fable](../fable/index.md) for target differences.

## Experimental source

[Ranvier.Mcp](../guide/mcp.md) exposes selected values and authorised Elmish messages from a
running graph. It is a source experiment and is not packed; traced provenance queries can inspect
the whole graph.

## Under consideration

- **Further collection incrementality:** filter, sort, grouping and some aggregate membership
  paths still scan keys.
- **An F# `Async<'T>` adapter:** cold-workflow lifetime and restart contracts remain exploratory.
  See [The async graph](async-graph.md#not-implemented).
- **Reusable lenses and prisms:** application code can express deep writes; reusable focus paths
  over collections remain open.
- **Additional storage adapters:** persistence supplies memory, file and browser local-storage
  stores; database-backed stores remain an extension point.
- **Mixed traced/untraced primitives:** let individual graph branches pay for tracing
  ([#69](https://github.com/ranvier-org/Ranvier/issues/69)).

See [Ecosystem](ecosystem.md) for collection tradeoffs and host integrations.
