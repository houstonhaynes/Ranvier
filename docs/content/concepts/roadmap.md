---
title: Roadmap
order: 6
---

:::warning
**Preview.** Ranvier is pre-release; its APIs may change.
:::

This page lists what the current source provides and what is being considered. Package builds and CI
coverage are described separately from the version a particular application has installed. The candidates
are not a schedule: an item may change shape, move, or be dropped.

## Implemented in current source

These are implemented and covered by tests.

- **Reactive core.** Signals, memos, effects and owners, with automatic dependency tracking, height-ordered
  updates and an equality cutoff. See [Getting started](../guide/getting-started.md).
- **Per-node equality.** Typed comparers for signals, memos, split effects and boundaries, overriding
  the graph's default value cutoff. See [Equality](../guide/equality.md#per-node-comparers).
- **Fixed-dependency bindings.** `bindSignal` and `bindMemo` copy one declared source into a target, with
  batching and owner disposal. Use an effect when the dependencies must change. See
  [Bindings](../guide/bindings.md).
- **Reverse acquisition teardown.** Cleanups and child nodes are released together in reverse acquisition
  order, including before an owning computation re-runs. See [Cleanup](../guide/cleanup.md).
- **Pending channel and boundaries.** A propagating "not ready" status, with `createSuspense`,
  `createErrorBoundary` and `createBoundary` to decide what to show in the meantime. See
  [Suspension](suspension.md).
- **Async memos.** `createAsync` with the `CancelPrevious`, `KeepLatest`, `Queue`, `FinishCurrent`, `Drop` and `DropWhilePending` flight policies, and the
  previous value passed to each flight. See [Async and pending](../guide/async-and-pending.md).
- **Debounce and throttle.** `debounce`, `throttleFirst`, `throttleLast` and `throttle` control publication
  before downstream work starts. They compose with flight policies and support injected clocks for tests.
  See [Debounce and throttle](../guide/timing.md).
- **Threading, failure and ownership contracts.** Thread affinity checks, dispatch from other threads, and
  `ManualDispatcher` for deterministic tests. See [Contracts](contracts.md).
- **Keyed collections.** Projections, index projections, lookups and selectors, with `filter`, `map`,
  `sortBy`, `groupBy`, slicing and fold views. See [Collections](../guide/collections.fsx) and
  [Aggregates](../guide/aggregates.fsx).
- **Trace log.** A traced build that records why each node ran. See [Tracing](../guide/tracing.md).
- **C# package.** `Ranvier.CSharp` with delegate-based factories, `Tracing`, `ReactiveBindings` for
  `INotifyPropertyChanged` and `INotifyDataErrorInfo`, and `ReactiveCommand`, an `ICommand` whose `CanExecute`
  and busy state come from graph nodes. `AsObservableCollection` raises `Add`, `Remove`,
  `Move` and `Replace` changes after population; initial population and reset recovery use `Reset`.
  Value-only updates consume settled-value deltas. Previous values reach C# through a seed or
  `SettledOr`/`TrySettled`, without `ValueOption`. See [C#](../guide/csharp.md).
- **Native AOT and trimming.** CI publishes untraced package smoke applications under .NET 10 for
  `linux-x64` with no trim or AOT warnings. This covers that configuration, not every traced build or
  deployment target. See [Installation](../guide/installation.md#native-aot-and-trimming).
- **F# application patterns.** `createEditable` and `createDraft` for values seeded from upstream and edited
  locally, and forms as records of signals. See [Editable values and forms](../guide/forms.md).
- **MVU bridge.** The `Ranvier.Elmish` package: `Mvu`, which reads an Elmish-style model through selector memos. See
  [Migrating from Elmish](../guide/elmish.md).
- **Editable keyed collections.** `createKeyedCollection` provides direct row edits in insertion order,
  with batched `Edit` callbacks and the existing projection operators. See
  [Direct edits](../guide/collection-updates.fsx#direct-edits).
- **Projection change readers.** `NewKeyReader` reports membership and order; `NewValueReader` also
  reports unequal settled row values. Readers have independent bounded cursors and return a reset
  on their first read or after overflow. See [Reading changes](../guide/projections.fsx#reading-changes).
- **Serialised thread affinity.** `ThreadAffinity.Serialised` admits one thread at a time on the construction
  context, for hosts such as Blazor Server. See [Serialised hosts](contracts.md#serialised-hosts) and
  [Blazor Server](../guide/blazor-server.md).
- **Failure provenance.** `ErrorOrigin` on a failed node and `CaughtFrom` on an error boundary report the node a
  failure originated in. See [Finding where a failure came from](contracts.md#finding-where-a-failure-came-from).
- **Projection construction and reconciliation.** Memo-free row factories through `createProjectionOnce`,
  an opt-in create-before-remove pass, and prefix/suffix trimming with a full-visit path for changes that
  cannot trim. The costs and ordering contracts are described in [Projections](../guide/projections.fsx).
- **Shared queries and mutations.** `Ranvier.Query` provides acquired query leases, explicit loading,
  retained data during refresh, and mutation receipts reconciled across loaded queries. See
  [Queries and mutations](../guide/queries.md) and [Dictionary navigation](../guide/query-navigation.md).
- **Fable target and package consumers.** The core and extension packages include standalone Fable source
  projects in package builds. CI runs traced/untraced compatibility checks and compiles consumers from
  built packages. Fable omits .NET thread/inbox/continuation bookkeeping and uses direct slot references;
  the .NET implementations are preserved. See [Fable (JavaScript) target](../fable/index.md).

## Under consideration

None of these is available. Each one is an open question about whether and how it fits.

- **A drop-while-running flight policy.** A policy that ignores a new run while one is in progress,
  analogous to [R3's `AwaitOperation.Drop`](https://github.com/Cysharp/R3#asyncawait-support).
  Command gating through `CanExecute` is a separate feature already available in `ReactiveCommand`.
- **More incremental collection views.** Map membership consumes upstream key deltas. Filter, sort,
  grouping and some aggregate membership paths still scan keys; further delta processing remains
  under consideration.
- **Additional async adapters.** F# `Async<'T>` and streaming `IAsyncEnumerable<'T>` adapters remain
  exploratory; their lifetime and restart contracts are not shipped. See
  [The async graph](async-graph.md#not-implemented).
- **Reusable lenses and prisms.** Deep write paths can be expressed in application code today; reusable
  focus values over collections remain under consideration.

The [Ecosystem](ecosystem.md) page lists the current gaps these items address.
