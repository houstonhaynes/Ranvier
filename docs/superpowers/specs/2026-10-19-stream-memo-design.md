# Stream memo design

Date: 2026-10-19
Branch: `feat/67-stream-memo`
Baseline: `305d00908d3f7cfbc015bd3df051c650886377dc`
Issue: [#67](https://github.com/shayanhabibi/Ranvier/issues/67). The [exploration comment](https://github.com/shayanhabibi/Ranvier/issues/67#issuecomment-6230105257) holds the prior art, the source survey and the burst probe; this design selects its option 1 with option 2's first converters.
Status: draft for review.

## Purpose and boundaries

Add an owned async node whose state is a fold over the items of a stream. A burst of items delivered off the graph thread reaches readers as one publication per inbox drain, where a loop of `AsyncSource.Settle` calls publishes once per item. The node ships the contracts the async-graph page lists as open: lifetime, restart and empty completion.

The first implementation includes:

- `createStream` and `createStreamFold`, and their C# facade factories;
- one restart policy: a change to a dependency read by `start` cancels the running stream and starts a new one from `seed`;
- converters from `IAsyncEnumerable<'T>` (.NET), `ChannelReader<'T>` (net10.0), `IObservable<'T>` (.NET and Fable) and JavaScript `AsyncIterable` (Fable);
- the counter scenarios `stream-burst` and `stream-restart`.

It excludes every-item delivery, back-pressure from the graph to the producer, `EventTarget` and `System.IO.Pipelines` converters, and any change to `AsyncSource` or `AsyncMemo` semantics. No `GraphOptions` member is added, and the graph's `FlightPolicy` does not apply to streams.

## Public interface

### Node and factories

`StreamMemo<'T>` is a read-only tracked value with the reading surface of `AsyncMemo<'T>`: `Value`, `TryValue`, `Status`, `ErrorOrigin`, graph identity, owner lifetime and `Dispose`. Its constructor is internal.

In `Api.fs`, beside `createAsync`:

```fsharp
/// The latest item of the stream `start` returns. Pending until the first item; a change to a value read by `start`
/// cancels the stream and starts a new one.
val createStream : start: (CancellationToken -> StreamSource<'T>) -> StreamMemo<'T>

/// The fold of the stream's items into `seed`. Pending until the first item or the stream's completion.
val createStreamFold :
    seed: 'S -> fold: ('S -> 'T -> 'S) -> start: (CancellationToken -> StreamSource<'T>) -> StreamMemo<'S>
```

`createStream` is `createStreamFold` with the fold `fun _ item -> item`, apart from empty completion (below).

### Stream sources

```fsharp
/// A producer of items, run once per stream start.
[<AbstractClass>]
type StreamSource<'T> =
    new: unit -> StreamSource<'T>
    /// Delivers items to `sink` until the stream ends. The task completes when the stream completes, faults when it
    /// fails, and is cancelled or faults with OperationCanceledException after `token` is cancelled.
    abstract Run: sink: StreamSink<'T> * token: CancellationToken -> Task

/// The receiver of a running stream's items.
[<Sealed>]
type StreamSink<'T> =
    /// Folds `item` into the stream's state. Callable from any thread; calls must not overlap.
    member Next: item: 'T -> unit
```

A push contract covers every source family with one node: a converter for a pull source runs the enumeration loop inside `Run`, and a push source forwards its callbacks to `Next`. `Task` maps to a JavaScript `Promise` under Fable, so the abstraction compiles on both targets.

Converters in `[<RequireQualifiedAccess>] module StreamSource` (`Collections.fs`' convention for companion modules):

| Function | Targets | Behaviour |
|---|---|---|
| `StreamSource.create : (StreamSink<'T> -> CancellationToken -> Task) -> StreamSource<'T>` | both | A source from a function |
| `StreamSource.ofAsyncEnumerable : IAsyncEnumerable<'T> -> StreamSource<'T>` | .NET | `GetAsyncEnumerator token`, a `MoveNextAsync` loop calling `Next`, then `DisposeAsync`, also on failure and cancellation |
| `StreamSource.ofChannel : ChannelReader<'T> -> StreamSource<'T>` | net10.0 | `WaitToReadAsync token`, then a `TryRead` loop that delivers every item already buffered before waiting again; the stream completes when the channel completes and fails with the writer's `Complete` exception |
| `StreamSource.ofObservable : IObservable<'T> -> StreamSource<'T>` | both | Subscribes; `OnNext` calls `Next`, `OnCompleted` completes the task, `OnError` faults it; cancellation disposes the subscription |
| `StreamSource.ofAsyncIterable : JS.AsyncIterable<'T> -> StreamSource<'T>` | Fable | `for await` over the iterator; cancellation calls the iterator's `return ()` |

`ofAsyncEnumerable` covers `IChatClient.GetStreamingResponseAsync`, `taskSeq` and `AsyncSeq` on .NET.

`ofChannel` drains a burst in one continuation: the items already in the channel are delivered by `TryRead` without an `await` between them, so they reach the graph in one drain. `System.Threading.Channels` ships in the shared framework from .NET Core 3.0 but is a separate package for netstandard2.1, and this design adds no package reference. `ofChannel` and its C# counterpart are compiled under `#if NET8_0_OR_GREATER && !FABLE_COMPILER` (excluded from netstandard2.1 and Fable), as `TimedClock.ofTimeProvider` is. A netstandard2.1 consumer can pass `reader.ReadAllAsync ()` to `ofAsyncEnumerable` when it references the package itself.

### C#

`Ranvier.CSharp`'s `Reactive` gains `Stream<T>(Func<CancellationToken, StreamSource<T>>)` and `StreamFold<TState, T>(TState seed, Func<TState, T, TState> fold, Func<CancellationToken, StreamSource<T>> start)`. A static `StreamSources` class exposes `Create`, `FromAsyncEnumerable`, `FromChannel` (net10.0) and `FromObservable`.

Before writing signatures, record the public API with `fcs_public_api`. Every new name is an explicit addition to the public API baseline; no existing signature changes.

## Observable semantics

| Question | Contract |
|---|---|
| When does it start? | On first read, as an async memo's first flight does. |
| What tracks dependencies? | Reads inside `start`, which runs synchronously on the graph thread. `Run` executes outside tracking. |
| What owns it? | The owner current at creation. A pure memo body that creates one raises `InvalidOperationException`, as for an async memo. |
| Status before the first item | `Pending`. `Value` raises `NotReadyException`; `TryValue` returns `Pending`. |
| A dependency changes | The running stream's token is cancelled, its sink is closed, and the state resets to `seed`. The node is `Pending` until the new stream's first item or completion. |
| A dependency read by `start` is pending | The node is `Pending` and starts the stream when the dependency settles. |
| Items while running | Each item is folded into the state when it is delivered. The node publishes the state at most once per graph pass (see Scheduling). |
| An equal published state | Compared with the graph's equality comparer against the last published state; an equal state notifies no reader. The transition from `Pending` to `Ready` always notifies. |
| The stream fails | The node is `Failed` with the exception; `ErrorOrigin` is the stream memo. A dependency change starts a new stream. |
| `fold` throws | As a stream failure: the token is cancelled and the node is `Failed` with the exception. |
| `start` or `Run` throws synchronously | `Failed` with the exception. |
| The stream completes after items | The node keeps its last state, `Ready`. |
| The stream completes with no items | `createStreamFold`: `Ready` with `seed`. `createStream`: `Failed` with `InvalidOperationException`, so no reader waits on an ended stream. |
| Cancellation requested by the node | Not published. A cancelled or `OperationCanceledException`-faulted task after a restart or disposal is observed and ignored. |
| An `OperationCanceledException` the node did not request | A failure. |
| Disposal | Cancels the token and closes the sink. A `Pending` node becomes `Failed` with `ObjectDisposedException`, as an async memo does. Items delivered after disposal are ignored. |
| Unobserved exceptions | None: every `Run` task is observed, including a superseded stream's. |

## Node and scheduling architecture

`StreamMemo<'T>` is a specialized node in a new `Streams.fs`, compiled after `Timed.fs`. It follows `Timed<'T>`'s structure: a `SourceList` for the reads in `start`, an `ObserverSet`, an owner link, and `IScheduled` for its wake. It is not built from an async memo plus a signal: that adds a node, an edge and a scheduling turn per publication.

### State handoff

Each started stream gets a fresh `StreamSink<'T>` holding the accumulator, a closed flag and the generation it belongs to. On .NET, `Next` off the graph thread folds under the sink's lock and reads the closed flag under it; the graph thread takes the same lock to snapshot the accumulator. On the graph thread, `Next` folds without the lock, so it must not overlap any other `Next` call. Under Fable the lock is absent. A sink closed by a restart or disposal ignores further items, so a superseded stream can never publish.

### Scheduling

- **During `Run`'s synchronous part.** Items delivered before `Run` returns fold into the accumulator. When `Run` returns, a state that received an item is published as part of the node's own execution, with no extra pass.
- **Off the graph thread (.NET).** After folding, `Next` posts one wake while none is outstanding, guarded by a `wakePosted` flag set with `Interlocked.Exchange`, as `Timed` does. The wake clears the flag before snapshotting, then enqueues the node and requests a flush; the node's execution snapshots the accumulator and publishes. Items that arrive while the wake is queued fold into the same snapshot. An item that arrives after the snapshot sees a cleared flag and posts the next wake, so no item's state is lost and at most one wake is queued.
- **On the graph thread (.NET and Fable).** `Next` follows the graph's write rule: a write on the graph thread settles before the next line runs. Outside a batch each item publishes; inside `batch`, a burst publishes once when the batch ends. The exploration proposed a microtask per burst under Fable; this design drops it, because `AsyncIterable` delivers each item on its own microtask and a microtask wake would publish per item there as well.
- **Completion and failure.** `Run`'s task outcome is applied through `Graph.Post` on the graph thread after any outstanding wake, so a completion never overtakes the final items' publication.

User code runs only in `start` (tracked, pure scope), `Run` (untracked) and `fold` (on the delivering thread, under the sink's lock on .NET when that thread is not the graph thread). `fold` must be pure and must not read or write graph nodes.

### Trace

Trace builds record the node's creation, each stream start with its generation, each publication and the stream's end (completed, failed, cancelled). The trace event numbers and the `TraceModel` shapes follow `Timed`'s additions:

- `TraceNodeKind.Stream` for the node;
- `TraceEventKind.StreamStarted`, with the generation in `Arg`;
- `TraceEventKind.StreamPublished`, with the published status in `Arg` and the state as the payload;
- `TraceEventKind.StreamEnded`, with the reason (completed, failed, cancelled by a restart, cancelled by disposal) in `Arg`.

Items folded between publications are not traced one by one: a burst would flood the log, and the publication carries the state they produced. `StreamPublished` records the number of items folded since the previous publication in `Flag`; `Other` stays a node id, as in every other event.

## Testing

Contract tests in `tests/Ranvier.Tests/Streams.fs`, run on .NET and under Fable, one per row of the semantics table:

- lazy start; tracking of reads in `start` and no tracking in `Run`;
- the enumerator or subscription is disposed exactly once on owner disposal and on each restart;
- a restart resets to `seed`, and an item from the superseded stream after the restart publishes nothing;
- failure from the stream and from `fold` publishes `Failed` with `ErrorOrigin` set to the stream memo;
- completion keeps the last state; empty completion gives `seed` for a fold and `InvalidOperationException` for `createStream`;
- an equal published state notifies no reader;
- disposal of a `Pending` node publishes `ObjectDisposedException`;
- no `TaskScheduler.UnobservedTaskException`.

Threading tests in `tests/Ranvier.Tests/Threading.fs`:

- 1,000 off-thread items delivered before a `Pump` produce one inbox item and one effect run, and the effect reads the final state;
- an item delivered during the wake's execution posts exactly one further wake, and its state is published;
- concurrent `Next` callers under the lock leave the fold consistent with sequential delivery (a stress test with a commutative fold).

Converter tests for each converter: completion, failure, cancellation mid-stream, and disposal of the underlying enumerator, subscription or iterator. For `ofChannel`: a burst written before the reader resumes reaches the graph in one inbox item, and the netstandard2.1 build compiles without the converter.

Mutation checks before review: removing the closed-sink check, the `wakePosted` guard, the reset to `seed`, and the generation check on completion must each fail a test.

## Performance gates

Counter scenarios in `bench/Ranvier.Counters/Scenarios.fs`, beside `async-resolve`, on .NET and Node:

- `stream-burst`: *B* items (B = 1, 16, 256) per drain into a stream memo read by a memo and an effect. Baseline: the same items through per-item `AsyncSource.Settle`. Reports retired instructions per item, `(m(2N) − m(N)) / N`.
- `stream-restart`: a dependency write restarts an open stream. Compared with an async memo flight under `CancelPrevious`.

The node justifies its surface when:

- effect runs per drain are exactly 1 for every *B*, where the baseline gives *B*;
- instructions per item at *B* = 256 are below the baseline's by at least the per-item flush cost the scenario measures;
- at *B* = 1, instructions per item are within 10% of the baseline.

The new scenarios do not change existing scenarios' counts; the CI gate holds for every existing scenario.

## Documentation

- `docs/content/guide/streams.md`: the factories, the converters, the semantics table, and how to choose between a stream memo, an async memo and an async source.
- `docs/content/concepts/async-graph.md`: replace the streaming item under *Not implemented* and the open-questions paragraph with a link to the guide and the restart contract.
- `docs/content/concepts/roadmap.md`: move streaming `IAsyncEnumerable<'T>` adapters to *Implemented in current source*.
- `docs/.ai/fable-compat.md`: `ofAsyncEnumerable` is .NET only; `ofChannel` is net10.0 only; `ofAsyncIterable` is Fable only.

### Signal maps

The streams guide draws its examples as [signal maps](../../content/guide/signal-maps.md), as the timing guide does. The map tooling under `docs/maps/` gains:

- **A node mark.** A stream memo draws as a dashed rounded rectangle: dashed like the other async nodes, rounded like a derived node. While a stream is open, the outline is doubled. A flight's spinning ring is not used, because a stream can stay open indefinitely and a ring that never stops tells the reader nothing.
- **Event rendering** in `docs/maps/model/MapModel.fs`, one case per trace event:

  | Event | Map | Log line |
  |---|---|---|
  | `StreamStarted` | the double outline appears | `start news (stream 2)` |
  | `StreamPublished`, ready | a surge to the observers, as `Moved` | `publish news = … (3 items)` |
  | `StreamPublished`, failed | the failed outline | `fail news: …` |
  | `StreamEnded`, completed | the double outline goes | `end news (completed)` |
  | `StreamEnded`, cancelled by a restart | a fading ring, as a dropped flight | `drop news (restarted)` |
  | `StreamEnded`, cancelled by disposal | the double outline goes | `end news (disposed)` |

  The hover card adds the generation and whether the stream is open.
- **A manual source.** `Feed<'T>()` in `docs/maps/model/Helpers.fs`, beside `Desk<'T>` and `MapClock`, is a `StreamSource<'T>` that controls drive:
  - `feed.Push item` delivers one item;
  - `feed.Burst items` delivers several items inside one `batch`, so the map shows one publication for them;
  - `feed.Complete ()` and `feed.Fail message` end the open stream;
  - each start opens a new stream, and a restart cancels the previous one, which the map draws as a drop.
- **Naming.** `MapFence`'s `create…` pattern already names `let news = createStream …` and `createStreamFold` bindings; an authoring test pins both.
- **Maps in `streams.md`:**
  1. the latest item, with **Push** and **Complete**;
  2. a fold of token deltas into a message, with **Burst** showing one publication for three items;
  3. a restart, as a `replay` timeline: a dependency write drops the open stream and starts a new one from `seed`;
  4. failure and empty completion, side by side for `createStream` and `createStreamFold`.
- **`signal-maps.md`:** the new mark in the node table, the double outline in the state table, and `Feed` beside `Desk` and `MapClock` under *Writing a map*.

Map tests in `docs/maps/tests/`: `ModelTests` for each event's rendering and log line, `AuthoringTests` for naming and `Feed`, and `ControlTests` for a replay of the restart map. The map validation in `docs/maps/validation/Generate.fsx` builds every `streams.md` fence.

## Decisions for review

1. **Sources push items into the node; the node never pulls.** Stream libraries come in two shapes. A pull source (`IAsyncEnumerable`, `AsyncIterable`) waits until its consumer asks for the next item. A push source (`IObservable`, events) calls its consumer whenever it has an item. `StreamSource<'T>` is the push shape: the node hands `Run` a sink, and the source calls `sink.Next` per item. A pull source becomes a push source with a loop (`while await MoveNextAsync () do sink.Next Current`), which `ofAsyncEnumerable` and `ofAsyncIterable` contain. A push source cannot become a pull source without a buffer between it and the consumer, which needs a size and a policy for when it fills.
   - **What it buys:** one node implementation and one set of contract tests for every source family; no buffer, so no overflow policy.
   - **What it costs:** the node cannot slow a source down. A pull loop calls `MoveNextAsync` again as soon as `Next` returns, without waiting for the graph to apply the item. Items fold into the state as fast as the source produces them, and the graph sees the latest state. That is the point of the fold: memory stays at one state, not a queue of items.
   - **If wrong:** a later every-item mode with back-pressure (the exploration's option 5) needs the node to await the graph between items. That fits inside a pull converter's loop and leaves `StreamSource<'T>` unchanged.
2. **On-graph-thread delivery publishes per item.** This keeps the core rule that a graph-thread write settles before the next line. A Fable or UI-thread burst from a synchronous observable costs one pass per item unless wrapped in `batch`.
3. **One restart policy.** `FlightPolicy` does not apply. `Queue` would block later streams behind an infinite one; `KeepLatest` and `FinishCurrent` have no meaning for a stream that never finishes.
4. **Restart resets to `seed` and goes `Pending`.** The previous state is not shown as `Ready` while the new stream warms up; a boundary's fallback receives the last value, as for an async memo.
5. **`createStream` fails on empty completion.** A `Ready` state with no item has no value to show; a fold has `seed`.
