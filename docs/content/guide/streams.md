---
title: Streams
order: 13
description: Fold a stream of items into a node that restarts when its inputs change.
---

A stream memo folds the items of a running stream into one state. Readers see the latest state; the
stream starts on the first read and restarts when a value read by its `start` function changes.

## Choosing a node

| Node | Items | Result | Use it when |
| --- | --- | --- | --- |
| `createAsyncSource` | One, settled by hand | The settled value | Application code decides when the value arrives. |
| `createAsync` | One per flight | The latest flight's result | A request is computed from the graph's values. |
| `createStream` | Many | The latest item | Only the newest item matters. |
| `createStreamFold` | Many | A state folded from a seed | Items accumulate, such as token deltas. |

`createStream` and `createStreamFold` take `start`, a function from a `CancellationToken` to a
`StreamSource<'T>`. `start` runs once per stream start, and the values it reads are the node's
dependencies.

## The latest item

`createStream` is pending until the first item. Each later item replaces the state. The
`Feed<'T>` in these examples is a source the buttons drive; see
[Writing a map](signal-maps.md#writing-a-map).

```fsharp map
let feed = Feed<string>()
let news = createStream (fun _ -> feed.Source)
createEffect (fun () -> printfn $"headline {news.Value}")

controls [
    button "Push" (fun () -> feed.Push "rates hold")
    button "Complete" (fun () -> feed.Complete ())
]
```

Press **Push**. The stream memo leaves pending and the effect runs. **Complete** ends the stream
and keeps the last item as the node's value.

A stream memo has a dashed rounded outline on the map, doubled while its stream is open.

## Folding token deltas

`createStreamFold seed fold start` folds each item into the state with `fold`. The node is pending
until the first item or the stream's completion.

```fsharp map
let feed = Feed<string>()
let message = createStreamFold "" (fun text delta -> text + delta) (fun _ -> feed.Source)
createEffect (fun () -> printfn $"message {message.Value}")

controls [
    button "Burst" (fun () -> feed.Burst [ "Hel"; "lo, "; "world" ])
    button "Push" (fun () -> feed.Push "!")
]
```

**Burst** delivers three items inside one `batch`. The map logs a single publication that reports
three items, and the effect runs once. The published item count is the number of items folded since
the previous publication.

A published state equal to the previous one, under the graph's equality policy, does not notify
readers.

## Restarts

A change to a value read by `start` cancels the stream's token, closes the stream and starts a new
one from the seed. The restart is lazy: it happens on the next read of the node, which an effect
performs when the change reaches it. Until the new stream delivers an item or completes, the node
is pending.

```fsharp map replay
let feed = Feed<int>()
let room = createSignal "lobby"
let count = createStreamFold 0 (fun n _ -> n + 1) (fun _ -> room.Value |> ignore; feed.Source)
createEffect (fun () -> printfn $"messages {count.Value}")

controls [
    button "Message" (fun () -> feed.Push 1)
    button "Switch room" (fun () -> room.Value <- "kitchen")
    button "Message" (fun () -> feed.Push 1)
]
```

Play the replay. The count reaches 1, **Switch room** returns the node to pending, and the next
message counts from 0 again. A replaced stream's later items are ignored.

Only reads made while `start` runs are dependencies. A pending read in `start` keeps the node
pending, and the stream starts when that source settles. The source's `Run` method executes
untracked, and reading a pending source there fails the stream.

## Failure and empty completion

A failure fails the node: reads raise and `TryValue` returns `Failed`. A restart clears the
failure. These conditions fail the node:

- the source's task faults, or an observable reports an error;
- `fold` throws, which also closes the stream;
- `start` throws or returns null;
- the source's `Run` throws or returns null;
- a `createStream` stream completes without an item, with `InvalidOperationException`.

A `createStreamFold` stream that completes without an item publishes its seed.

```fsharp map
let latestFeed = Feed<int>()
let foldFeed = Feed<int>()
let latest = createStream (fun _ -> latestFeed.Source)
let total = createStreamFold 0 (+) (fun _ -> foldFeed.Source)
createEffect (fun () -> printfn $"latest {latest.TryValue} total {total.TryValue}")

controls [
    button "Complete both" (fun () -> latestFeed.Complete (); foldFeed.Complete ())
    button "Fail both" (fun () -> latestFeed.Fail "offline"; foldFeed.Fail "offline")
]
```

Press **Complete both**: `latest` fails and `total` publishes 0. In a fresh map, **Fail both**
fails both nodes with the message.

Disposing the node cancels the stream. A node that was still pending becomes failed with
`ObjectDisposedException`.

## Sources

A `StreamSource<'T>` runs once per stream start and delivers items to a `StreamSink<'T>`. The
`StreamSource` module builds sources from common producers. Each converter subscribes or enumerates
again on every start. Cancelling the token ends the run: `ofObservable`, `ofChannel` and
`ofAsyncIterable` fault their task with `OperationCanceledException`, and `ofAsyncEnumerable` passes
the token to the enumerator.

`StreamSource.create` wraps a function from the sink and token to a `Task`. The task completes when
the stream completes and faults when it fails:

```fsharp
open System.Threading.Tasks
open Ranvier

let ticks =
    StreamSource.create (fun sink token ->
        task {
            for n in 1 .. 3 do
                do! Task.Delay (100, token)
                sink.Next n
        } :> Task)

let total = createStreamFold 0 (fun sum n -> sum + n) (fun _ -> ticks)
```

`StreamSource.ofAsyncEnumerable` enumerates an `IAsyncEnumerable<'T>` with the stream's token and
disposes the enumerator when the stream ends. It is available on .NET:

```fsharp
let lines = createStream (fun token -> StreamSource.ofAsyncEnumerable (readLines token))
```

Here `readLines` is the application's function returning an `IAsyncEnumerable<string>`.

`StreamSource.ofChannel` reads a `ChannelReader<'T>`. It is available in the net10.0 build, and not
on netstandard2.1 or under Fable. The stream completes when the channel completes, and fails with
the exception passed to the writer's `Complete`. Once the token is cancelled the converter reads no
further items, so after a restart the unread items stay in the channel for the new stream:

```fsharp
let events = createStream (fun _ -> StreamSource.ofChannel channel.Reader)
```

`StreamSource.ofObservable` subscribes to an `IObservable<'T>`. `OnNext` delivers, `OnCompleted`
completes and `OnError` fails:

```fsharp
let quotes = createStream (fun _ -> StreamSource.ofObservable priceChanges)
```

`StreamSource.ofAsyncIterable` iterates a JavaScript async iterable and is available under Fable
only. Cancellation calls the iterator's `return ()`:

```fsharp
let chunks = createStreamFold "" (+) (fun _ -> StreamSource.ofAsyncIterable (readChunks ()))
```

From C#, `Reactive.Stream` and `Reactive.StreamFold` create the nodes, and `StreamSources` builds
the sources: `Create`, `FromObservable`, `FromAsyncEnumerable` and, in the net10.0 build,
`FromChannel`.

```csharp
using Ranvier;
using Ranvier.CSharp;

using var graph = new Graph();
graph.Run(() =>
{
    var room = Reactive.Signal("lobby");
    var count = Reactive.StreamFold(
        0,
        (total, message) => total + 1,
        token => StreamSources.FromAsyncEnumerable(Messages(room.Value, token)));
});
```

Here `Messages` is the application's method returning an `IAsyncEnumerable<string>`.

## Threads

A source may deliver items from any thread. Calls to `sink.Next` from other threads may overlap each
other; a call on the graph thread must not overlap any other call.

- Items delivered on the graph thread before `Run` returns its task are folded and published with
  the start. They schedule no wake.
- Items delivered off the graph thread request one wake while none is outstanding. The graph
  publishes the latest state once per drain of its inbox, however many items arrived since. With
  manual dispatch, call `graph.Pump()` on the owning thread; see
  [Threading and dispatch](threading.md).
- Items delivered on the graph thread publish each, or once per `batch`.

`fold` runs on the delivering thread, under a lock when that thread is not the graph thread, and
must be pure. It receives the state and the
item and returns the next state.

## The contract

| Topic | Behavior |
| --- | --- |
| Start | On the first read. `start` runs with its reads tracked. |
| Restart | A change to a value read by `start` restarts the stream on the next read, from the seed. |
| Pending | From each start until the first item or completion, and while `start` waits on a pending read. |
| Cancellation | A restart, completion, a failure and disposal cancel the token. |
| Latest item | `createStream` publishes each item as the state. |
| Fold | `createStreamFold` publishes `fold state item`, beginning from the seed. |
| Publication | At most once per graph pass. The trace and the map report the items folded since the previous publication. |
| Cutoff | A state equal to the published one does not notify readers. |
| Completion | The last state stays published. Without an item, `createStream` fails and `createStreamFold` publishes the seed. |
| Failure | A faulted source, a throwing `fold`, `start` or `Run`, and a null source or task fail the node. |
| Disposal | Cancels the stream; a pending node fails with `ObjectDisposedException`. |
| `Peek` | The last published state, read without starting the stream. |
| `Runs` | The number of stream starts. |

## Performance

Instruction counts for item bursts and restarts, compared with async sources and async memos and followed across commits, are on
[Benchmarks → Streams](../benchmarks/streams.md#ci-instruction-counts).
