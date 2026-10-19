# Stream memo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `createStream` and `createStreamFold`: an owned async node whose state is a fold over a stream's items, published once per graph pass, with converters from `IAsyncEnumerable`, `ChannelReader`, `IObservable` and JavaScript `AsyncIterable`.

**Architecture:** A specialized node `StreamMemo<'S>` in a new `src/Ranvier/Streams.fs`, compiled after `Timed.fs`. It starts lazily on first read like `AsyncMemo`, tracks the reads in `start`, and folds items into a per-stream `StreamRun` under a lock. A delivered item requests one wake (an `Interlocked` flag, as `Timed` does); the wake schedules the node, and its `Execute` publishes the latest state. Completion and failure reach the graph through `Graph.Post`, after any queued wake.

**Tech Stack:** F# (net10.0, netstandard2.1, Fable 5), Expecto (.NET) and Fable.Mocha through `fable/Ranvier.Tests.Fable`, BenchmarkDotNet-free instruction counters (`bench/Ranvier.Counters`), the signal-map tooling under `docs/maps/`.

**Spec:** `docs/superpowers/specs/2026-10-19-stream-memo-design.md` (approved 2026-10-19). Read it before any task; it is the authority where this plan is silent.

## Global Constraints

- Targets: `net10.0;netstandard2.1` and Fable. No new package reference anywhere.
- `StreamSource.ofAsyncEnumerable` and `StreamSources.FromAsyncEnumerable`: `#if !FABLE_COMPILER`.
- `StreamSource.ofChannel` and `StreamSources.FromChannel`: `#if NET8_0_OR_GREATER && !FABLE_COMPILER` (the directive `TimedClock.ofTimeProvider` uses).
- `StreamSource.ofAsyncIterable`: `#if FABLE_COMPILER`.
- Public API: additions only. Every new public name is added to `docs/.ai/public-api-baseline.txt`; no existing signature changes.
- `FlightPolicy` does not apply to streams; no `GraphOptions` member is added.
- Trace hooks are single statements, outside `#if RANVIER_TRACE`, as `Tracer.TimedEvent` calls are; the hooks themselves are `[<Conditional("RANVIER_TRACE")>]` static members of `Tracer`.
- Comments follow `.claude/rules/comments.md`: contracts, not justifications; no `FOR-REVIEW` left at commit.
- Before each commit: `dotnet fantomas <changed .fs files>`; `git diff --cached --check`; never skip hooks. If `docs/.ai/fable-compat.md` changes only in line endings, `git checkout` it.
- Commit messages: one conventional line (`feat(core): …`, `test(core): …`, `docs(…): …`), a blank line, then:
  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
  ```
- Push with `git push origin HEAD:feat/67-stream-memo`. Never rebase or force-push; update from master by merging `origin/master` in.
- Shell commands are prefixed with `rtk`, except `node`, `npm`, `npx`, `dotnet`, `gh`, `wsl`.
- Gates (run from the worktree root): `dotnet fsi build.fsx -- test` (.NET), then `npm ci` and `dotnet fsi build.fsx -- test-fable` (Fable). A filtered run never counts as the gate.

## Rulings on the spec

- **Wake, then publish in `Execute`.** The spec's scheduling section is implemented as written: a wake enqueues the node (`IScheduled`) and requests a flush; `Execute` snapshots and publishes. On the graph thread outside a batch the flush runs at once, so each item publishes; inside `batch` the flush runs once at the end.
- **Restart is lazy.** A dependency change marks the node dirty and notifies its readers, as `AsyncMemo.MarkDirty` does; the old stream is cancelled when the node is next read (`Launch`). `Execute` publishes nothing while the node is not `Clean`, so the abandoned stream's items never reach readers. Cancelling inside `MarkDirty` would run cancellation callbacks in the middle of propagation.
- **Item count in `Flag`.** `StreamPublished` records the items folded since the previous publication in `Flag` (the spec was updated to match).
- **`stream-burst` delivers on the graph thread inside `batch`.** The counters run one thread, so the off-thread coalescing is pinned by the threading tests (one inbox item, one effect run). The counter scenario compares the per-item cost of a stream fold against `AsyncSource.Settle` inside one batch, and against unbatched settles.

## Review Focus

1. A `start` that returns the same `StreamSource` object on every restart: each start must call `Run` again with a fresh sink and token, and the old sink must ignore items.
2. A misbehaving source that calls `Next` after its task completed: the items are ignored and nothing is republished.
3. `Run` throwing synchronously, and `Run` returning `null`: both publish `Failed`, the first with the thrown exception, the second with `InvalidOperationException`.
4. An effect that disposes the stream memo while reacting to its publication: no further publication, no exception escapes the flush.
5. A `fold` that throws on the second item: `Failed` with that exception, the token is cancelled, and a dependency change starts a fresh stream from `seed`.

Each line has a test in Task 1 or Task 2, named after it.

## File Structure

| File | Responsibility |
|---|---|
| `src/Ranvier/Platform.fs` (modify) | `Platform.whenDone`: observe a non-generic `Task`'s outcome; `Platform.completion`: a task completed or faulted by hand, on both targets |
| `src/Ranvier/Streams.fs` (create) | `StreamSink<'T>`, `StreamSource<'T>`, internal `IStreamRun<'S>` and `StreamRun<'S,'T>`, `StreamMemo<'S>`, the `StreamSource` module of converters |
| `src/Ranvier/Ranvier.fsproj` (modify) | compile `Streams.fs` after `Timed.fs` |
| `src/Ranvier/Api.fs` (modify) | `createStream`, `createStreamFold` |
| `src/Ranvier/TraceEvents.fs`, `Trace.fs`, `TraceModel.fs` (modify) | `TraceNodeKind.Stream`, three event kinds, `TraceStreamEnd`, the hooks, the model's handling |
| `src/Ranvier.CSharp/Reactive.fs`, `src/Ranvier.CSharp/StreamSources.fs` (modify/create) | C# factories and converters |
| `tests/Ranvier.Tests/Streams.fs` (create), `TestFiles.props`, `fable/Ranvier.Tests.Fable/Main.fs` (modify) | contract, threading, converter and trace tests |
| `tests/Ranvier.CSharp.Tests/StreamTests.cs` (create) | C# facade tests |
| `bench/Ranvier.Counters/Scenarios.fs` (modify) | `stream-burst`, `stream-restart` |
| `docs/maps/model/Helpers.fs`, `MapModel.fs`, `docs/maps/SignalMap.fs`, `docs/maps/tests/*.fs` (modify) | `Feed<'T>`, stream rendering, map tests |
| `docs/content/guide/streams.md` (create), `signal-maps.md`, `concepts/async-graph.md`, `concepts/roadmap.md`, `docs/.ai/fable-compat.md`, `docs/.ai/public-api-baseline.txt` (modify) | documentation |

---

### Task 1: The stream memo on the graph thread

**Files:**
- Modify: `src/Ranvier/Platform.fs` (beside `whenSettled`, ~line 136)
- Create: `src/Ranvier/Streams.fs`
- Modify: `src/Ranvier/Ranvier.fsproj:61` (after `<Compile Include="Timed.fs" />`)
- Modify: `src/Ranvier/Api.fs` (after `createAsyncSource`, ~line 388)
- Create: `tests/Ranvier.Tests/Streams.fs`
- Modify: `tests/Ranvier.Tests/TestFiles.props` (after `Timed.fs`), `fable/Ranvier.Tests.Fable/Main.fs` (after the `Timed.fs` list)

**Interfaces:**
- Produces:
  - `type StreamSink<'T>` with `member Next: item: 'T -> unit`
  - `[<AbstractClass>] type StreamSource<'T>` with `abstract Run: sink: StreamSink<'T> * token: CancellationToken -> Task`
  - `type StreamMemo<'S>` with `Graph`, `Status`, `Runs`, `Peek`, `ErrorOrigin`, `Value`, `TryValue`, `Dispose`; `static member internal Create<'T>: Graph * 'S * ('S -> 'T -> 'S) * (CancellationToken -> StreamSource<'T>) * emptyFails: bool -> StreamMemo<'S>`
  - `Api.createStream : (CancellationToken -> StreamSource<'T>) -> StreamMemo<'T>`
  - `Api.createStreamFold : 'S -> ('S -> 'T -> 'S) -> (CancellationToken -> StreamSource<'T>) -> StreamMemo<'S>`
  - `Platform.whenDone : Task -> (FlightOutcome<unit> -> unit) -> unit`
  - Trace hooks called here and implemented in Task 4: `Tracer.StreamNew (graph, id, owner)`, `Tracer.StreamStarted (graph, id, generation)`, `Tracer.StreamPublished (graph, id, status, count, payload)`, `Tracer.StreamEnded (graph, id, reason)`. In this task, add them to `Trace.fs` as `[<Conditional("RANVIER_TRACE")>]` static members whose bodies are `()` in both branches; Task 4 fills them.

- [ ] **Step 1: Write the failing contract tests**

Create `tests/Ranvier.Tests/Streams.fs`:

```fsharp
module Ranvier.Tests.Streams

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open Ranvier

/// <summary>One start of a <c>ManualStream</c>: its sink, its token and the task the test completes.</summary>
type StreamStart<'T> =
    {
        Sink: StreamSink<'T>
        Token: CancellationToken
        Done: TaskCompletionSource<unit>
    }

/// <summary>A stream source the test drives: every start records its sink, token and completion.</summary>
type ManualStream<'T>() =
    let starts = ResizeArray<StreamStart<'T>>()

    /// <summary>A source whose every <c>Run</c> records a start; the same object serves every restart.</summary>
    member val Source =
        { new StreamSource<'T>() with
            member _.Run(sink, token) =
                let start =
                    {
                        Sink = sink
                        Token = token
                        Done = TaskCompletionSource<unit>()
                    }

                starts.Add start
                start.Done.Task :> Task
        }

    member _.Starts = starts.Count
    member _.Latest = starts[starts.Count - 1]
    member _.At(index: int) = starts[index]
    member this.Push(item: 'T) = this.Latest.Sink.Next item
    member this.Complete() = this.Latest.Done.SetResult ()
    member this.Fail(error: exn) = this.Latest.Done.SetException error

let private reading (memo: StreamMemo<'S>) = memo.TryValue

[<Tests>]
let tests =
    testList
        "Stream memos"
        [
            testCase "a stream starts on its first read, not at creation"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStream (fun _ -> feed.Source))
                Expect.equal feed.Starts 0 "not started at creation"
                Expect.equal (reading memo) Pending "pending before the first item"
                Expect.equal feed.Starts 1 "started by the read"

            testCase "an item publishes the latest state; a fold accumulates"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<string>()
                let latest = graph.Run (fun () -> Api.createStream (fun _ -> feed.Source))
                let joined = graph.Run (fun () -> Api.createStreamFold "" (fun state item -> state + item) (fun _ -> feed.Source))
                reading latest |> ignore
                reading joined |> ignore
                feed.At(0).Sink.Next "a"
                feed.At(0).Sink.Next "b"
                feed.At(1).Sink.Next "x"
                feed.At(1).Sink.Next "y"
                Expect.equal (reading latest) (Ready "b") "latest item"
                Expect.equal (reading joined) (Ready "xy") "folded items"

            testCase "an effect sees one run per item on the graph thread, and one per batch"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> feed.Source))
                let seen = ResizeArray<int>()

                new Effect (graph, fun () ->
                    match memo.TryValue with
                    | Ready v -> seen.Add v
                    | _ -> ())
                |> ignore

                feed.Push 1
                feed.Push 2
                graph.Run (fun () -> Api.batch (fun () -> feed.Push 3; feed.Push 4))
                Expect.sequenceEqual seen [ 1; 3; 10 ] "one run per item outside a batch, one for the batch"

            testCase "items delivered while Run has not returned publish when it returns"
            <| fun _ ->
                use graph = new Graph ()

                let eager =
                    { new StreamSource<int>() with
                        member _.Run(sink, _) =
                            sink.Next 1
                            sink.Next 2
                            TaskCompletionSource<unit>().Task :> Task
                    }

                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> eager))
                Expect.equal (reading memo) (Ready 3) "the synchronous prefix is published by the first read"

            testCase "completion keeps the last state"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStream (fun _ -> feed.Source))
                reading memo |> ignore
                feed.Push 5
                feed.Complete ()
                Expect.equal (reading memo) (Ready 5) "ready with the last item"

            testCase "empty completion gives seed for a fold and InvalidOperationException for createStream"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let latest = graph.Run (fun () -> Api.createStream (fun _ -> feed.Source))
                let folded = graph.Run (fun () -> Api.createStreamFold 7 (+) (fun _ -> feed.Source))
                reading latest |> ignore
                reading folded |> ignore
                feed.At(0).Done.SetResult ()
                feed.At(1).Done.SetResult ()

                match reading latest with
                | Failed (:? InvalidOperationException) -> ()
                | other -> failtestf "expected InvalidOperationException, got %A" other

                Expect.equal (reading folded) (Ready 7) "seed"

            testCase "a stream failure publishes Failed with the stream memo as origin"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStream (fun _ -> feed.Source))
                reading memo |> ignore
                feed.Push 1
                let boom = Exception "boom"
                feed.Fail boom

                match reading memo with
                | Failed error -> Expect.equal error.Message "boom" "the stream's exception"
                | other -> failtestf "expected Failed, got %A" other

                Expect.isTrue (obj.ReferenceEquals (memo.ErrorOrigin, memo)) "origin"

            testCase "a dependency change cancels the stream and restarts it from seed"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let topic = Signal (graph, "a")
                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> topic.Value |> ignore; feed.Source))
                reading memo |> ignore
                feed.Push 4
                let first = feed.Latest
                topic.Value <- "b"
                Expect.equal (reading memo) Pending "pending until the new stream's first item"
                Expect.isTrue first.Token.IsCancellationRequested "the old token is cancelled"
                Expect.equal feed.Starts 2 "restarted"
                first.Sink.Next 100
                feed.Push 1
                Expect.equal (reading memo) (Ready 1) "reset to seed; the old sink's item is ignored"

            testCase "a start that returns the same StreamSource object on every restart"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let topic = Signal (graph, 0)
                let memo = graph.Run (fun () -> Api.createStream (fun _ -> topic.Value |> ignore; feed.Source))
                reading memo |> ignore
                topic.Value <- 1
                reading memo |> ignore
                Expect.isFalse (obj.ReferenceEquals (feed.At(0).Sink, feed.At(1).Sink)) "a fresh sink per start"
                feed.At(0).Sink.Next 9
                Expect.equal (reading memo) Pending "the first start's sink is closed"
                feed.At(1).Sink.Next 2
                Expect.equal (reading memo) (Ready 2) "the second start's sink publishes"

            testCase "a pending read in start keeps the node pending and starts it when the read settles"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let gate = AsyncSource<int>(graph)
                let memo = graph.Run (fun () -> Api.createStream (fun _ -> gate.Value |> ignore; feed.Source))
                let mutable runs = 0
                new Effect (graph, fun () -> memo.TryValue |> ignore; runs <- runs + 1) |> ignore
                Expect.equal feed.Starts 0 "start suspended on the gate"
                gate.Settle 1
                Expect.equal feed.Starts 1 "started once the gate settled"

            testCase "a misbehaving source's items after completion are ignored"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStream (fun _ -> feed.Source))
                reading memo |> ignore
                feed.Push 1
                feed.Complete ()
                feed.Push 2
                Expect.equal (reading memo) (Ready 1) "nothing republished after completion"

            testCase "Run throwing synchronously, and Run returning null, both publish Failed"
            <| fun _ ->
                use graph = new Graph ()

                let throwing =
                    { new StreamSource<int>() with
                        member _.Run(_, _) = raise (Exception "sync")
                    }

                let nothing =
                    { new StreamSource<int>() with
                        member _.Run(_, _) = null
                    }

                let a = graph.Run (fun () -> Api.createStream (fun _ -> throwing))
                let b = graph.Run (fun () -> Api.createStream (fun _ -> nothing))

                match reading a, reading b with
                | Failed error, Failed (:? InvalidOperationException) -> Expect.equal error.Message "sync" "thrown"
                | other -> failtestf "expected two failures, got %A" other

            testCase "a fold that throws on the second item"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let topic = Signal (graph, 0)

                let memo =
                    graph.Run (fun () ->
                        Api.createStreamFold 0 (fun state item -> if item = 2 then failwith "fold" else state + item) (fun _ ->
                            topic.Value |> ignore
                            feed.Source))

                reading memo |> ignore
                feed.Push 1
                feed.Push 2

                match reading memo with
                | Failed error -> Expect.equal error.Message "fold" "fold's exception"
                | other -> failtestf "expected Failed, got %A" other

                Expect.isTrue feed.Latest.Token.IsCancellationRequested "token cancelled"
                topic.Value <- 1
                reading memo |> ignore
                feed.Push 3
                Expect.equal (reading memo) (Ready 3) "a fresh stream from seed"

            testCase "an equal published state notifies no reader"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStream (fun _ -> feed.Source))
                let mutable runs = 0
                new Effect (graph, fun () -> memo.TryValue |> ignore; runs <- runs + 1) |> ignore
                feed.Push 1
                let after = runs
                feed.Push 1
                Expect.equal runs after "equal state, no run"

            testCase "disposing a pending stream memo publishes ObjectDisposedException and cancels the token"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStream (fun _ -> feed.Source))
                reading memo |> ignore
                memo.Dispose ()
                Expect.isTrue feed.Latest.Token.IsCancellationRequested "cancelled"

                match reading memo with
                | Failed (:? ObjectDisposedException) -> ()
                | other -> failtestf "expected ObjectDisposedException, got %A" other

                feed.Push 1
                Expect.isTrue (memo.TryValue.IsFailed) "items after disposal are ignored"

            testCase "owner disposal disposes the stream memo"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()

                let owner, memo =
                    graph.Run (fun () -> Api.createRoot (fun owner -> owner, Api.createStream (fun _ -> feed.Source)))

                reading memo |> ignore
                owner.Dispose ()
                Expect.isTrue feed.Latest.Token.IsCancellationRequested "cancelled with its owner"

            testCase "an effect that disposes the stream memo while reacting to its publication"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStream (fun _ -> feed.Source))
                let seen = ResizeArray<int>()

                new Effect (graph, fun () ->
                    match memo.TryValue with
                    | Ready v ->
                        seen.Add v
                        memo.Dispose ()
                    | _ -> ())
                |> ignore

                feed.Push 1
                feed.Push 2
                Expect.sequenceEqual seen [ 1 ] "no publication after disposal"

            testCase "a pure memo that creates a stream memo fails"
            <| fun _ ->
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let bad = graph.Run (fun () -> Api.createMemo (fun _ -> Api.createStream (fun _ -> feed.Source) |> ignore; 0))

                Expect.throwsT<InvalidOperationException> (fun () -> graph.Run (fun () -> bad.Value) |> ignore) "owned node in a pure body"
        ]
```

Add `<Compile Include="$(MSBuildThisFileDirectory)Streams.fs" Link="Streams.fs" />` after the `Timed.fs` line of `TestFiles.props`, and to `fable/Ranvier.Tests.Fable/Main.fs` after the `"Timed.fs"` list:

```fsharp
            testList "Streams.fs" [ Ranvier.Tests.Streams.tests ]
```

If a name used above differs in the codebase (`Reading.IsFailed`, `Api.batch`, `Api.createRoot` returning the body's value), use the existing member and keep the assertion. Under Fable, completion arrives through `Platform.whenDone`; follow `Async.fs`'s `Flight<'T>` pattern, which settles `TaskCompletionSource` tasks on the test's own thread and asserts synchronously in both targets. A test that cannot assert synchronously under Fable goes under `#if !FABLE_COMPILER`, with a comment naming why, as `Async.fs` does.

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet build tests/Ranvier.Tests -c Release`
Expected: FAIL to compile with `The value or constructor 'createStream' is not defined` and `The type 'StreamSource' is not defined`.

- [ ] **Step 3: Add `Platform.whenDone`**

In `src/Ranvier/Platform.fs`, after `whenSettled`:

```fsharp
    /// <summary>
    /// Calls <c>settle</c> with the outcome of <c>work</c>: inline on the completing thread on .NET, on a microtask
    /// under Fable. Observes <c>work</c> at once, so a rejection is handled.
    /// </summary>
    let whenDone (work: Task) (settle: FlightOutcome<unit> -> unit) : unit =
#if FABLE_COMPILER
        thenBoth (unbox<Task<unit>> work) (fun _ -> settle (FlightOutcome.Completed ())) (fun reason -> settle (rejected reason))
        |> ignore
#else
        work.ContinueWith (
            Action<Task>(fun t ->
                settle (
                    if t.IsCanceled then FlightOutcome.Canceled (cancellation t)
                    elif t.IsFaulted then FlightOutcome.Faulted (t.Exception.GetBaseException ())
                    else FlightOutcome.Completed ()
                )),
            TaskContinuationOptions.ExecuteSynchronously
        )
        |> ignore
#endif
```

`cancellation`, `rejected` and `thenBoth` are the private helpers `settled` and `whenSettled` already use; if `cancellation` takes `Task<'T>`, give it a `Task` overload in the same place.

- [ ] **Step 4: Create `src/Ranvier/Streams.fs`**

```fsharp
namespace Ranvier

open System
open System.Threading
open System.Threading.Tasks

/// <summary>The receiver of a running stream's items.</summary>
[<Sealed>]
type StreamSink<'T> internal (deliver: 'T -> unit) =
    /// <summary>Folds <c>item</c> into the stream's state. Callable from any thread; calls must not overlap.</summary>
    member _.Next(item: 'T) = deliver item

/// <summary>A producer of items, run once per stream start.</summary>
[<AbstractClass>]
type StreamSource<'T>() =
    /// <summary>
    /// Delivers items to <c>sink</c> until the stream ends. The task completes when the stream completes, faults when it
    /// fails, and is cancelled or faults with <c>OperationCanceledException</c> after <c>token</c> is cancelled.
    /// </summary>
    abstract Run: sink: StreamSink<'T> * token: CancellationToken -> Task

/// <summary>The state of one started stream, shared by its producer and the graph thread.</summary>
type internal IStreamRun<'S> =
    /// <summary>The state, and the number of items folded since the previous snapshot.</summary>
    abstract Snapshot: unit -> struct ('S * int)
    /// <summary>True once an item has been folded.</summary>
    abstract Received: bool
    /// <summary>Ignores every later item.</summary>
    abstract Close: unit -> unit

/// <summary>A started stream's accumulator. Items fold under a lock on the delivering thread.</summary>
[<Sealed>]
type internal StreamRun<'S, 'T>(seed: 'S, fold: 'S -> 'T -> 'S, wake: unit -> unit, failed: exn -> unit) =
    let gate = obj ()
    let mutable state = seed
    let mutable folded = 0
    let mutable received = false
    let mutable closed = false
    let mutable foldError: exn = null

    /// <summary>Folds <c>item</c> and requests a wake; a throwing fold closes the run and reports the exception.</summary>
    member _.Deliver(item: 'T) =
        let outcome =
            lock gate (fun () ->
                if closed then
                    0
                else
                    try
                        state <- fold state item
                        folded <- folded + 1
                        received <- true
                        1
                    with error ->
                        closed <- true
                        foldError <- error
                        2)

        if outcome = 1 then wake ()
        elif outcome = 2 then failed foldError

    interface IStreamRun<'S> with
        member _.Snapshot() =
            lock gate (fun () ->
                let count = folded
                folded <- 0
                struct (state, count))

        member _.Received = lock gate (fun () -> received)

        member _.Close() =
            lock gate (fun () -> closed <- true)

/// <summary>Why a stream ended, recorded by <c>StreamEnded</c>.</summary>
type internal StreamEnd =
    | Completed = 0
    | Failed = 1
    | Restarted = 2
    | Disposed = 3

/// <summary>
/// The latest state of a stream: a fold of its items into a seed, published at most once per graph pass.
/// </summary>
/// <remarks>
/// Starts on its first read. A change to a value read by <c>start</c> cancels the stream and starts a new one from the
/// seed; the node is pending until the new stream's first item or completion. Disposal cancels the stream, and a
/// pending node becomes failed with <c>ObjectDisposedException</c>.
/// </remarks>
[<Sealed>]
type StreamMemo<'S>
    internal
    (
        graph: Graph,
        seed: 'S,
        emptyFails: bool,
        launch: (unit -> unit) -> (exn -> unit) -> CancellationToken -> struct (IStreamRun<'S> * (unit -> Task))
    ) as this =
    let id = graph.NextId ()
    let observers = ObserverSet ()
    do Tracer.Bind (observers, graph, id)
    let sources = SourceList ()
    do Tracer.Bind (sources, graph, id)
    let equal = graph.Options.Equality.Comparer<'S>()
    let mutable freshness = Freshness.Dirty
    let mutable status = Status.Pending ||| Status.Uninitialized
    let mutable value = Unchecked.defaultof<'S>
    let mutable failure: Failure = null
    let mutable runs = 0
    let mutable disposed = false
    let mutable violated = false
    let mutable link: OwnerLink = null
    let mutable generation = 0
    let mutable run: IStreamRun<'S> = Unchecked.defaultof<_>
    let mutable cts: CancellationTokenSource = null
    let mutable running = false
    let mutable launching = false
    let mutable queued = false
    let mutable wakePosted = 0

    let wake (gen: int) () =
#if FABLE_COMPILER
        wakePosted <- 0
#else
        Interlocked.Exchange (&wakePosted, 0) |> ignore
#endif
        if not disposed && gen = generation then
            this.Enqueue ()
            graph.RequestFlush ()

    member private this.Attach() =
        link <- graph.CurrentOwner.AttachLinked this
        Tracer.StreamNew (graph, id, link.Owner)

    member private this.Enqueue() =
        if not queued then
            queued <- true
            graph.Schedule (this :> IScheduled)

    /// <summary>Requests one wake while none is outstanding; items folded during <c>Run</c>'s synchronous part need none.</summary>
    member private _.Request(gen: int) =
        if not (launching && graph.IsOnGraphThread) then
#if FABLE_COMPILER
            let previous = wakePosted
            wakePosted <- 1
#else
            let previous = Interlocked.Exchange (&wakePosted, 1)
#endif
            if previous = 0 then
                graph.Post (wake gen)

    member private _.Notify() =
        observers.NotifyDirtyExcept graph.CurrentComputation
        Tracer.Notified graph
        graph.RequestFlush ()

    /// <summary>Closes the open stream and cancels its token.</summary>
    member private _.Stop(reason: StreamEnd) =
        if not (Platform.isNull (box run)) then
            run.Close ()

        if running then
            running <- false
            Tracer.StreamEnded (graph, id, int reason)

        if not (Platform.isNull cts) then
            let source = cts
            cts <- null
            source.Cancel ()
            source.Dispose ()

    member private this.Fail(error: exn) =
        this.Stop StreamEnd.Failed
        let next = graph.FailureOf (error, this, failure)

        let moved =
            status <> Status.Error
            || Failure.Moved (next, failure)

        failure <- next
        status <- Status.Error
        Tracer.StreamPublished (graph, id, int (byte status), 0, box error)

        if moved then
            this.Notify ()

    member private this.Publish(state: 'S, count: int, notify: bool) =
        try
            let moved =
                status <> Status.None
                || not (equal.Equals (value, state))

            value <- state
            status <- Status.None
            failure <- null
            Tracer.StreamPublished (graph, id, int (byte status), count, box state)

            if moved && notify then
                this.Notify ()
        with error ->
            this.Fail error

    member private this.PublishLatest(notify: bool) =
        let struct (state, count) = run.Snapshot ()
        this.Publish (state, count, notify)

    member private this.Ended(gen: int, outcome: Platform.FlightOutcome<unit>) =
        if not disposed && gen = generation && running then
            match outcome with
            | Platform.FlightOutcome.Completed _ ->
                let received = run.Received
                this.Stop StreamEnd.Completed

                if received then
                    this.PublishLatest true
                elif emptyFails then
                    this.Fail (InvalidOperationException "The stream completed without an item.")
                else
                    this.Publish (seed, 0, true)
            | Platform.FlightOutcome.Faulted error
            | Platform.FlightOutcome.Canceled error -> this.Fail error

    member private this.FoldFailed(gen: int, error: exn) =
        if not disposed && gen = generation && running then
            this.Fail error

    member private this.Launch() =
        freshness <- Freshness.Clean
        this.Stop StreamEnd.Restarted
        generation <- generation + 1
        let gen = generation
        runs <- runs + 1
        Tracer.RunStart (graph, id, runs)
        failure <- null
        status <- Status.Pending ||| (status &&& Status.Uninitialized)
        sources.BeginRun ()
        let source = new CancellationTokenSource ()
        cts <- source

        try
            try
                let struct (started, body) =
                    graph.RunHosted (
                        this :> IComputation,
                        fun () -> launch (fun () -> this.Request gen) (fun error -> graph.Post (fun () -> this.FoldFailed (gen, error))) source.Token
                    )

                if violated then
                    raise (InvalidOperationException (ScopeMessages.forMode ScopeMode.Pure))

                run <- started
                running <- true
                Tracer.StreamStarted (graph, id, gen)
                launching <- true

                let work =
                    try
                        graph.RunUntracked body
                    finally
                        launching <- false

                if Platform.isNull work then
                    raise (InvalidOperationException "StreamSource.Run returned null.")

                Platform.whenDone work (fun outcome -> graph.Post (fun () -> this.Ended (gen, outcome)))

                if gen = generation && running && started.Received then
                    this.PublishLatest false
            finally
                if disposed then
                    sources.Clear (this :> IComputation)
                else
                    sources.EndRun (this :> IComputation)
        with
        | error when violated ->
            violated <- false
            this.Fail (ScopeMessages.failure ScopeMode.Pure error)
        | NotReadyException _ -> this.Stop StreamEnd.Restarted
        | error -> this.Fail error

    member private this.EnsureCurrent() =
        if freshness <> Freshness.Clean && not disposed then
            this.Pull ()

    member private this.Pull() =
        graph.EnterPull ()

        try
            if freshness = Freshness.Check then
                Tracer.CheckStart (graph, id)
                let mutable i = 0

                while freshness = Freshness.Check && i < sources.Count do
                    sources.SourceAt(i).UpdateIfNecessary()
                    i <- i + 1

                Tracer.CheckResolved (graph, id, (freshness = Freshness.Dirty))

                if freshness = Freshness.Check then
                    freshness <- Freshness.Clean

            if freshness = Freshness.Dirty then
                this.Launch ()
        finally
            graph.ExitPull ()

    static member internal Create<'T>
        (graph: Graph, seed: 'S, fold: 'S -> 'T -> 'S, start: CancellationToken -> StreamSource<'T>, emptyFails: bool)
        =
        if Platform.isNull (box fold) then
            nullArg (nameof fold)

        if Platform.isNull (box start) then
            nullArg (nameof start)

        let launch wake failed (token: CancellationToken) =
            let source = start token

            if Platform.isNull (box source) then
                raise (InvalidOperationException "The stream's start function returned null.")

            let started = StreamRun<'S, 'T> (seed, fold, wake, failed)
            struct (started :> IStreamRun<'S>, (fun () -> source.Run (StreamSink<'T> started.Deliver, token)))

        let memo = new StreamMemo<'S> (graph, seed, emptyFails, launch)
        memo.Attach ()
        memo

    interface INode with
        member _.Id = id
        member _.Status = status

    interface ISource with
        member _.AddObserver observer =
            observers.Add observer

        member _.RemoveObserver observer =
            observers.Remove observer

        member this.UpdateIfNecessary() =
            this.EnsureCurrent ()

    interface IComputation with
        member this.AddSource source =
            sources.Add (this :> IComputation, source)

        member _.MarkDirty() =
            if freshness <> Freshness.Dirty then
                freshness <- Freshness.Dirty
                observers.NotifyDirty ()

        member _.MarkCheck() =
            if freshness = Freshness.Clean then
                freshness <- Freshness.Check
                observers.NotifyCheck ()

    interface IScheduled with
        member this.Execute() =
            queued <- false

            if not disposed && running && freshness = Freshness.Clean then
                this.PublishLatest true

    interface IScopeHost with
        member _.Scope =
            violated <- true
            raise (InvalidOperationException (ScopeMessages.forMode ScopeMode.Pure))

    interface IOwned with
        member this.Release() =
            link <- null
            this.Dispose ()

    /// <summary>The graph that owns this value.</summary>
    member _.Graph = graph
    /// <summary>The published state.</summary>
    member _.Status = status
    /// <summary>The number of stream starts, including starts that suspended or failed.</summary>
    member _.Runs = runs
    /// <summary>The last published state, read without tracking, starting the stream or raising.</summary>
    member _.Peek = value
    /// <summary>The node the current failure originated in while <c>Status</c> has <c>Error</c>, otherwise null.</summary>
    member _.ErrorOrigin: INode = Failure.OriginOf failure

    /// <summary>Tracks and reads the published state; raises while it is pending or failed.</summary>
    member this.Value: 'S =
        this.EnsureCurrent ()
        graph.Track (this :> ISource)

        if status.HasFlag Status.Pending then
            raise (graph.NotReady (this :> INode))

        if status.HasFlag Status.Error then
            graph.Raise failure

        value

    /// <summary>Tracks and returns the published ready, pending or failed state.</summary>
    member this.TryValue: Reading<'S> =
        this.EnsureCurrent ()
        graph.Track (this :> ISource)

        if status.HasFlag Status.Pending then Pending
        elif status.HasFlag Status.Error then Failed failure.Error
        else Ready value

    /// <summary>Cancels the stream and detaches from its sources; repeated disposal is harmless.</summary>
    member this.Dispose() =
        graph.Entered (
            "Disposing a node",
            fun () ->
                if not disposed then
                    disposed <- true
                    Tracer.NodeDispose (graph, id)

                    if not (Platform.isNull link) then
                        link.Detach ()
                        link <- null

                    sources.Clear (this :> IComputation)
                    this.Stop StreamEnd.Disposed

                    if status.HasFlag Status.Pending then
                        failure <- Failure (ObjectDisposedException (this.GetType().Name), (this :> INode))
                        status <- Status.Error
                        this.Notify ()
        )

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()
```

In `Ranvier.fsproj`, add `<Compile Include="Streams.fs" />` after `<Compile Include="Timed.fs" />`.

If an internal member named here differs (`graph.Schedule`, `Failure.Moved`, `ScopeMessages.failure`), the shape in `Timed.fs` and `AsyncMemo` is the reference: use the member those files call for the same purpose.

- [ ] **Step 5: Add the factories to `Api.fs`**

After `createAsyncSource`:

```fsharp
    /// <summary>
    /// The latest item of the stream <c>start</c> returns. Pending until the first item; a change to a value read by
    /// <c>start</c> cancels the stream and starts a new one.
    /// </summary>
    /// <remarks>
    /// Starts on its first read. A stream that completes without an item fails with <c>InvalidOperationException</c>.
    /// Items delivered off the graph thread publish once per inbox drain; items on the graph thread publish each,
    /// or once per <c>batch</c>.
    /// </remarks>
    let createStream (start: CancellationToken -> StreamSource<'T>) =
        StreamMemo<'T>.Create(Graph.Current, Unchecked.defaultof<'T>, (fun _ item -> item), start, true)

    /// <summary>The fold of the stream's items into <c>seed</c>. Pending until the first item or the stream's completion.</summary>
    /// <remarks>
    /// <c>fold</c> runs on the delivering thread and must be pure. A stream that completes without an item publishes
    /// <c>seed</c>. A restart begins again from <c>seed</c>.
    /// </remarks>
    let createStreamFold (seed: 'S) (fold: 'S -> 'T -> 'S) (start: CancellationToken -> StreamSource<'T>) =
        StreamMemo<'S>.Create(Graph.Current, seed, fold, start, false)
```

Add the four `Tracer` stubs to `Trace.fs` beside `TimedNew`, each `[<Conditional("RANVIER_TRACE")>]` with body `()` under both branches of `#if RANVIER_TRACE`.

- [ ] **Step 6: Run the tests**

Run: `dotnet fsi build.fsx -- test`
Expected: PASS, including every `Stream memos` case.

Run: `npm ci` then `dotnet fsi build.fsx -- test-fable`
Expected: PASS.

- [ ] **Step 7: Mutation checks**

Apply each change, run `dotnet fsi build.fsx -- test`, confirm a `Stream memos` test fails, and restore:
1. `StreamRun.Deliver`: drop `if closed then 0 else` (always fold).
2. `Launch`: replace `StreamRun<'S, 'T> (seed, …)` with a run that keeps the previous state (pass `value` instead of `seed` through a captured field).
3. `Ended`: drop `gen = generation &&`.
4. `Execute`: drop `freshness = Freshness.Clean &&`.
Record the four results in the task report; the commit message stays one line.

- [ ] **Step 8: Commit**

```bash
dotnet fantomas src/Ranvier/Streams.fs src/Ranvier/Api.fs src/Ranvier/Platform.fs src/Ranvier/Trace.fs tests/Ranvier.Tests/Streams.fs fable/Ranvier.Tests.Fable/Main.fs
rtk git add src/Ranvier tests/Ranvier.Tests fable/Ranvier.Tests.Fable
git diff --cached --check
git commit -F - <<'EOF'
feat(core): add the stream memo, createStream and createStreamFold (#67)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
```

---

### Task 2: Off-thread delivery

**Files:**
- Modify: `tests/Ranvier.Tests/Streams.fs` (a second test list)
- Modify: `fable/Ranvier.Tests.Fable/Main.fs` (no change: the list is .NET only)

**Interfaces:**
- Consumes: Task 1's `ManualStream<'T>`, `Api.createStream`, `Api.createStreamFold`.

- [ ] **Step 1: Write the tests**

Append to `tests/Ranvier.Tests/Streams.fs`:

```fsharp
#if !FABLE_COMPILER
open System.Threading

/// <summary>Runs <c>body</c> on a dedicated thread and waits for it.</summary>
let private offThread (body: unit -> unit) =
    let mutable failure: exn = null

    let thread =
        Thread (
            (fun () ->
                try
                    body ()
                with error ->
                    failure <- error),
            IsBackground = true
        )

    thread.Start ()
    thread.Join ()

    if not (isNull failure) then
        raise failure

let private manualGraph () =
    new Graph (
        { GraphOptions.Default with
            Dispatcher = Some (ManualDispatcher () :> IGraphDispatcher)
        }
    )

[<Tests>]
let threadingTests =
    testList
        "Stream memos off the graph thread"
        [
            testCase "1,000 off-thread items before a pump give one inbox item and one effect run"
            <| fun _ ->
                use graph = manualGraph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> feed.Source))
                let seen = ResizeArray<int>()

                new Effect (graph, fun () ->
                    match memo.TryValue with
                    | Ready v -> seen.Add v
                    | _ -> ())
                |> ignore

                offThread (fun () ->
                    for i in 1..1000 do
                        feed.Push i)

                Expect.equal graph.PendingWork 1 "one wake queued"
                graph.Pump () |> ignore
                Expect.sequenceEqual seen [ 500500 ] "one run with the final state"

            testCase "an item delivered while the wake runs posts exactly one further wake"
            <| fun _ ->
                use graph = manualGraph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> feed.Source))
                let seen = ResizeArray<int>()
                let mutable injected = false

                new Effect (graph, fun () ->
                    match memo.TryValue with
                    | Ready v ->
                        seen.Add v

                        if not injected then
                            injected <- true
                            offThread (fun () -> feed.Push 10)
                    | _ -> ())
                |> ignore

                offThread (fun () -> feed.Push 1)
                graph.Pump () |> ignore
                Expect.equal graph.PendingWork 1 "the item during the wake posted one wake"
                graph.Pump () |> ignore
                Expect.sequenceEqual seen [ 1; 11 ] "the later item's state is published"

            testCase "completion after off-thread items publishes the final state first"
            <| fun _ ->
                use graph = manualGraph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStream (fun _ -> feed.Source))
                graph.Run (fun () -> memo.TryValue) |> ignore

                offThread (fun () ->
                    feed.Push 1
                    feed.Push 2
                    feed.Complete ())

                graph.Pump () |> ignore
                Expect.equal (graph.Run (fun () -> memo.TryValue)) (Ready 2) "completion did not overtake the items"

            testCase "concurrent Next callers leave a commutative fold equal to sequential delivery"
            <| fun _ ->
                use graph = manualGraph ()
                let feed = ManualStream<int>()
                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> feed.Source))
                graph.Run (fun () -> memo.TryValue) |> ignore
                let sink = feed.Latest.Sink

                let workers =
                    [| for w in 0..3 ->
                           Thread (
                               (fun () ->
                                   for i in 1..10000 do
                                       sink.Next (w * 10000 + i)),
                               IsBackground = true
                           ) |]

                for t in workers do
                    t.Start ()

                for t in workers do
                    t.Join ()

                graph.Pump () |> ignore
                let expected = Seq.sum (seq { for w in 0..3 do for i in 1..10000 -> w * 10000 + i })
                Expect.equal (graph.Run (fun () -> memo.TryValue)) (Ready expected) "every item folded once"
        ]
#endif
```

Register the list in `tests/Ranvier.Tests/Main.fs` the way the file registers other `[<Tests>]` values (Expecto discovers `[<Tests>]` in the .NET runner; check `Main.fs` and add the list if it is enumerated by hand).

- [ ] **Step 2: Run the tests**

Run: `dotnet fsi build.fsx -- test`
Expected: PASS. If the first test sees more than one pending item, the `wakePosted` guard is not effective: fix the node, not the test.

- [ ] **Step 3: Mutation checks**

1. `Request`: replace the `Interlocked.Exchange` guard with an unconditional `graph.Post (wake gen)`. Expected: the first test fails (`PendingWork` 1000).
2. `wake`: move `Interlocked.Exchange (&wakePosted, 0)` after `this.Enqueue ()` and `graph.RequestFlush ()`. Expected: the second test fails (the later item posts no wake).
Restore both.

- [ ] **Step 4: Commit**

```bash
dotnet fantomas tests/Ranvier.Tests/Streams.fs
rtk git add tests/Ranvier.Tests
git diff --cached --check
git commit -F - <<'EOF'
test(core): pin the stream memo's off-thread coalescing (#67)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
```

---

### Task 3: Converters

**Files:**
- Modify: `src/Ranvier/Platform.fs` (a `Completion` helper)
- Modify: `src/Ranvier/Streams.fs` (the `StreamSource` module, at the end)
- Modify: `tests/Ranvier.Tests/Streams.fs` (a converter test list)
- Modify: `fable/Ranvier.Tests.Fable/Main.fs` (add `Ranvier.Tests.Streams.converterTests`)

**Interfaces:**
- Consumes: `StreamSource<'T>`, `StreamSink<'T>`.
- Produces: `StreamSource.create`, `StreamSource.ofAsyncEnumerable` (.NET), `StreamSource.ofChannel` (net10.0), `StreamSource.ofObservable`, `StreamSource.ofAsyncIterable` (Fable); `Platform.completion<'T> () : Completion<'T>` with `Task`, `Resolve`, `Reject`.

- [ ] **Step 1: Write the failing converter tests**

```fsharp
type private Recorder<'T>() =
    let observers = ResizeArray<IObserver<'T>>()
    member val Unsubscribed = 0 with get, set
    member _.Observer = observers[observers.Count - 1]

    interface IObservable<'T> with
        member this.Subscribe observer =
            observers.Add observer

            { new IDisposable with
                member _.Dispose() =
                    this.Unsubscribed <- this.Unsubscribed + 1
            }

[<Tests>]
let converterTests =
    testList
        "Stream sources"
        [
            testCase "ofObservable: OnNext publishes, OnCompleted keeps the state, restart unsubscribes once"
            <| fun _ ->
                use graph = new Graph ()
                let subject = Recorder<int>()
                let topic = Signal (graph, 0)
                let memo = graph.Run (fun () -> Api.createStream (fun _ -> topic.Value |> ignore; StreamSource.ofObservable subject))
                graph.Run (fun () -> memo.TryValue) |> ignore
                subject.Observer.OnNext 3
                Expect.equal (graph.Run (fun () -> memo.TryValue)) (Ready 3) "item"
                topic.Value <- 1
                graph.Run (fun () -> memo.TryValue) |> ignore
                Expect.equal subject.Unsubscribed 1 "the restart disposed the first subscription"
                subject.Observer.OnCompleted ()

                match graph.Run (fun () -> memo.TryValue) with
                | Failed (:? InvalidOperationException) -> ()
                | other -> failtestf "empty second stream: expected InvalidOperationException, got %A" other

            testCase "ofObservable: OnError publishes Failed"
            <| fun _ ->
                use graph = new Graph ()
                let subject = Recorder<int>()
                let memo = graph.Run (fun () -> Api.createStream (fun _ -> StreamSource.ofObservable subject))
                graph.Run (fun () -> memo.TryValue) |> ignore
                subject.Observer.OnError (Exception "rx")

                match graph.Run (fun () -> memo.TryValue) with
                | Failed error -> Expect.equal error.Message "rx" "error"
                | other -> failtestf "expected Failed, got %A" other

            testCase "create: the function receives the sink and token"
            <| fun _ ->
                use graph = new Graph ()
                let mutable token = CancellationToken.None

                let memo =
                    graph.Run (fun () ->
                        Api.createStream (fun _ ->
                            StreamSource.create (fun sink t ->
                                token <- t
                                sink.Next 1
                                TaskCompletionSource<unit>().Task :> Task)))

                Expect.equal (graph.Run (fun () -> memo.TryValue)) (Ready 1) "item"
                memo.Dispose ()
                Expect.isTrue token.IsCancellationRequested "token"
#if !FABLE_COMPILER
            testCase "ofAsyncEnumerable: items, completion, and DisposeAsync exactly once on cancellation"
            <| fun _ ->
                use graph = new Graph ()
                let mutable disposedCount = 0
                let gate = TaskCompletionSource<bool>()

                let items =
                    { new Collections.Generic.IAsyncEnumerable<int> with
                        member _.GetAsyncEnumerator token =
                            let mutable i = 0

                            { new Collections.Generic.IAsyncEnumerator<int> with
                                member _.Current = i

                                member _.MoveNextAsync() =
                                    i <- i + 1
                                    if i <= 2 then ValueTask<bool> true else ValueTask<bool> gate.Task

                                member _.DisposeAsync() =
                                    disposedCount <- disposedCount + 1
                                    ValueTask ()
                            }
                    }

                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> StreamSource.ofAsyncEnumerable items))
                Expect.equal (graph.Run (fun () -> memo.TryValue)) (Ready 3) "the synchronous burst"
                memo.Dispose ()
                gate.SetResult false
                Expect.equal disposedCount 1 "DisposeAsync once"
#endif
#if NET8_0_OR_GREATER && !FABLE_COMPILER
            testCase "ofChannel: items buffered before the first read publish with that read"
            <| fun _ ->
                use graph = new Graph ()
                let channel = Channels.Channel.CreateUnbounded<int>()

                for i in 1..100 do
                    channel.Writer.TryWrite i |> ignore

                channel.Writer.Complete ()
                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> StreamSource.ofChannel channel.Reader))
                Expect.equal (graph.Run (fun () -> memo.TryValue)) (Ready 5050) "every buffered item, then completion"

            testCase "ofChannel: an off-thread burst queues one wake and the completion"
            <| fun _ ->
                use graph = new Graph ({ GraphOptions.Default with Dispatcher = Some (ManualDispatcher () :> IGraphDispatcher) })
                // Synchronous continuations run the reader on the writer's thread, so the test needs no waiting.
                let channel = Channels.Channel.CreateUnbounded<int>(Channels.UnboundedChannelOptions (AllowSynchronousContinuations = true))
                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> StreamSource.ofChannel channel.Reader))
                Expect.equal (graph.Run (fun () -> memo.TryValue)) Pending "waiting on an empty channel"

                let writer =
                    Thread (
                        (fun () ->
                            for i in 1..100 do
                                channel.Writer.TryWrite i |> ignore

                            channel.Writer.Complete ()),
                        IsBackground = true
                    )

                writer.Start ()
                writer.Join ()
                Expect.equal graph.PendingWork 2 "one wake for 100 items, and the completion"
                graph.Pump () |> ignore
                Expect.equal (graph.Run (fun () -> memo.TryValue)) (Ready 5050) "every item"
#endif
        ]
```

Add `testList "Streams.fs (sources)" [ Ranvier.Tests.Streams.converterTests ]` to `fable/Ranvier.Tests.Fable/Main.fs`.

- [ ] **Step 2: Run to see them fail**

Run: `dotnet build tests/Ranvier.Tests -c Release`
Expected: FAIL, `The value, namespace, type or module 'StreamSource' is not defined` for `StreamSource.ofObservable`.

- [ ] **Step 3: Add `Platform.completion`**

In `Platform.fs`, beside `deferred`, for both targets:

```fsharp
    /// <summary>A task completed or faulted by hand, once.</summary>
    type Completion<'T> = { Task: Task<'T>; Resolve: 'T -> unit; Reject: exn -> unit }

#if FABLE_COMPILER
    [<Emit("new Promise((resolve, reject) => $0(resolve, reject))")>]
    let private promiseWith (executor: ('T -> unit) -> (exn -> unit) -> unit) : Task<'T> = jsNative

    let completion<'T> () : Completion<'T> =
        let mutable resolver = Unchecked.defaultof<'T -> unit>
        let mutable rejecter = Unchecked.defaultof<exn -> unit>
        let task = promiseWith (fun resolve reject -> resolver <- resolve; rejecter <- reject)
        { Task = task; Resolve = resolver; Reject = rejecter }
#else
    let completion<'T> () : Completion<'T> =
        // Continuations run inline on the resolving thread, so a resolution on the graph thread applies before it returns.
        let source = TaskCompletionSource<'T>()
        { Task = source.Task; Resolve = source.TrySetResult >> ignore; Reject = source.TrySetException >> ignore }
#endif
```

The `Emit` receives a curried F# function; if Fable passes it uncurried, declare `executor: Func<…>` and adapt, as the existing `promiseOf` does.

- [ ] **Step 4: Add the converters at the end of `Streams.fs`**

```fsharp
/// <summary>Stream sources from common item producers.</summary>
[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module StreamSource =
    /// <summary>A source whose every start calls <c>run</c>.</summary>
    /// <exception cref="T:System.ArgumentNullException"><c>run</c> is null.</exception>
    let create (run: StreamSink<'T> -> CancellationToken -> Task) : StreamSource<'T> =
        if Platform.isNull (box run) then
            nullArg (nameof run)

        { new StreamSource<'T>() with
            member _.Run(sink, token) = run sink token
        }

    /// <summary>Subscribes on each start: <c>OnNext</c> delivers, <c>OnCompleted</c> completes, <c>OnError</c> fails.</summary>
    /// <remarks>Cancellation disposes the subscription and completes the task.</remarks>
    let ofObservable (items: IObservable<'T>) : StreamSource<'T> =
        if Platform.isNull (box items) then
            nullArg (nameof items)

        create (fun sink token ->
            let finished = Platform.completion<unit> ()

            let subscription =
                items.Subscribe
                    { new IObserver<'T> with
                        member _.OnNext item = sink.Next item
                        member _.OnCompleted() = finished.Resolve ()
                        member _.OnError error = finished.Reject error
                    }

            token.Register (fun () ->
                subscription.Dispose ()
                finished.Resolve ())
            |> ignore

            finished.Task :> Task)

#if !FABLE_COMPILER
    /// <summary>
    /// Enumerates on each start with the stream's token. Items already available are delivered without an
    /// intervening continuation. <c>DisposeAsync</c> runs once, on completion, failure or cancellation.
    /// </summary>
    let ofAsyncEnumerable (items: Collections.Generic.IAsyncEnumerable<'T>) : StreamSource<'T> =
        if Platform.isNull (box items) then
            nullArg (nameof items)

        create (fun sink token ->
            let finished = Platform.completion<unit> ()
            let enumerator = items.GetAsyncEnumerator token

            let finish (error: exn) =
                let disposal =
                    try
                        enumerator.DisposeAsync().AsTask ()
                    with disposeError ->
                        Task.FromException disposeError

                disposal.ContinueWith (
                    Action<Task>(fun d ->
                        if not (isNull error) then finished.Reject error
                        elif d.IsFaulted then finished.Reject (d.Exception.GetBaseException ())
                        else finished.Resolve ()),
                    TaskContinuationOptions.ExecuteSynchronously
                )
                |> ignore

            let rec pump () =
                try
                    let mutable next = enumerator.MoveNextAsync ()
                    let mutable go = true

                    while go && next.IsCompleted do
                        if next.Result then
                            sink.Next enumerator.Current
                            next <- enumerator.MoveNextAsync ()
                        else
                            go <- false
                            finish null

                    if go then
                        next.AsTask().ContinueWith (
                            Action<Task<bool>>(fun t ->
                                if t.IsFaulted then finish (t.Exception.GetBaseException ())
                                elif t.IsCanceled then finish (OperationCanceledException token)
                                elif t.Result then
                                    sink.Next enumerator.Current
                                    pump ()
                                else
                                    finish null),
                            TaskContinuationOptions.ExecuteSynchronously
                        )
                        |> ignore
                with error ->
                    finish error

            pump ()
            finished.Task :> Task)
#endif

#if NET8_0_OR_GREATER && !FABLE_COMPILER
    /// <summary>
    /// Reads on each start: waits with the stream's token, then delivers every buffered item before waiting again. The
    /// stream completes when the channel completes, and fails with the exception the writer completed it with.
    /// </summary>
    let ofChannel (reader: Channels.ChannelReader<'T>) : StreamSource<'T> =
        if Platform.isNull (box reader) then
            nullArg (nameof reader)

        create (fun sink token ->
            let finished = Platform.completion<unit> ()

            let rec wait () =
                reader.WaitToReadAsync(token).AsTask().ContinueWith (
                    Action<Task<bool>>(fun t ->
                        if t.IsCanceled then finished.Reject (OperationCanceledException token)
                        elif t.IsFaulted then finished.Reject (t.Exception.GetBaseException ())
                        elif t.Result then
                            let mutable item = Unchecked.defaultof<'T>

                            while reader.TryRead &item do
                                sink.Next item

                            wait ()
                        else
                            finished.Resolve ()),
                    TaskContinuationOptions.ExecuteSynchronously
                )
                |> ignore

            wait ()
            finished.Task :> Task)
#endif

#if FABLE_COMPILER
    [<Fable.Core.Emit("$0[Symbol.asyncIterator]()")>]
    let private iteratorOf (items: Fable.Core.JS.AsyncIterable<'T>) : obj = Fable.Core.Util.jsNative

    [<Fable.Core.Emit("$0.next()")>]
    let private nextOf (iterator: obj) : Task<obj> = Fable.Core.Util.jsNative

    [<Fable.Core.Emit("($0.return ? $0.return() : undefined)")>]
    let private returnOf (iterator: obj) : unit = Fable.Core.Util.jsNative

    [<Fable.Core.Emit("$0.done")>]
    let private isDone (result: obj) : bool = Fable.Core.Util.jsNative

    [<Fable.Core.Emit("$0.value")>]
    let private valueOf (result: obj) : 'T = Fable.Core.Util.jsNative

    /// <summary>Iterates on each start; cancellation calls the iterator's <c>return ()</c>.</summary>
    let ofAsyncIterable (items: Fable.Core.JS.AsyncIterable<'T>) : StreamSource<'T> =
        if Platform.isNull (box items) then
            nullArg (nameof items)

        create (fun sink token ->
            let finished = Platform.completion<unit> ()
            let iterator = iteratorOf items
            let mutable stopped = false

            token.Register (fun () ->
                stopped <- true
                returnOf iterator
                finished.Resolve ())
            |> ignore

            let rec step () =
                Platform.whenSettled (nextOf iterator) (fun outcome ->
                    if not stopped then
                        match outcome with
                        | Platform.FlightOutcome.Completed result when isDone result -> finished.Resolve ()
                        | Platform.FlightOutcome.Completed result ->
                            sink.Next (valueOf<'T> result)
                            step ()
                        | Platform.FlightOutcome.Faulted error
                        | Platform.FlightOutcome.Canceled error -> finished.Reject error)
                |> ignore

            step ()
            finished.Task :> Task)
#endif
```

If `Fable.Core.JS.AsyncIterable` is non-generic in Fable.Core 5.3.0, take `Fable.Core.JS.AsyncIterable` and keep `valueOf<'T>`; the public signature is then `ofAsyncIterable<'T> : JS.AsyncIterable -> StreamSource<'T>`. Record which in the task report.

- [ ] **Step 5: Run the tests**

Run: `dotnet fsi build.fsx -- test`, then `npm ci` and `dotnet fsi build.fsx -- test-fable`
Expected: PASS on both.

- [ ] **Step 6: Commit**

```bash
dotnet fantomas src/Ranvier/Streams.fs src/Ranvier/Platform.fs tests/Ranvier.Tests/Streams.fs fable/Ranvier.Tests.Fable/Main.fs
rtk git add src/Ranvier tests/Ranvier.Tests fable/Ranvier.Tests.Fable
git diff --cached --check
git commit -F - <<'EOF'
feat(core): add stream sources from observables, async enumerables, channels and async iterables (#67)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
```

---

### Task 4: Trace events

**Files:**
- Modify: `src/Ranvier/TraceEvents.fs` (kinds after `TimedState = 55`; `TraceNodeKind.Stream`; names in `eventKind`/`nodeKind`)
- Modify: `src/Ranvier/Trace.fs` (fill the four hooks)
- Modify: `src/Ranvier/TraceModel.fs` (handle the events beside `TimedPublished`, ~line 1148)
- Modify: `tests/Ranvier.Tests/Streams.fs` (a trace test list, `untracedOnly`-style gating as `Timed.traceTests` uses)
- Modify: `fable/Ranvier.Tests.Fable/Main.fs`

**Interfaces:**
- Produces: `TraceNodeKind.Stream = 13`; `TraceEventKind.StreamStarted = 60`, `StreamPublished = 61`, `StreamEnded = 62`; `type TraceStreamEnd = Completed = 0 | Failed = 1 | Restarted = 2 | Disposed = 3` (public, beside `TraceDropReason`). `StreamPublished`: `Arg` the status byte, `Flag` the items folded since the previous publication, `Payload` the state or the exception. `StreamEnded`: `Arg` a `TraceStreamEnd`. `StreamStarted`: `Arg` the generation.

- [ ] **Step 1: Write the failing trace test**

Read how `Timed.traceTests` builds a traced graph and reads events (`tests/Ranvier.Tests/Timed.fs`), and write the same shape:

```fsharp
[<Tests>]
let traceTests =
    testList
        "Stream memo traces"
        [
            testCase "a start, a burst, a restart and a completion are traced"
            <| fun _ ->
                // Build the graph and read its log exactly as Timed.traceTests does.
                use graph = new Graph ()
                let feed = ManualStream<int>()
                let topic = Signal (graph, 0)
                let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> topic.Value |> ignore; feed.Source))
                graph.Run (fun () -> memo.TryValue) |> ignore
                graph.Run (fun () -> Api.batch (fun () -> feed.Push 1; feed.Push 2; feed.Push 3))
                topic.Value <- 1
                graph.Run (fun () -> memo.TryValue) |> ignore
                feed.Push 5
                feed.Complete ()
                let events = Trace.events graph |> List.filter (fun e -> int e.Kind >= 60 && int e.Kind <= 62)

                let summary =
                    events |> List.map (fun e -> e.Kind, e.Arg, e.Flag)

                Expect.equal
                    summary
                    [
                        TraceEventKind.StreamStarted, 1, 0
                        TraceEventKind.StreamPublished, 0, 3
                        TraceEventKind.StreamEnded, int TraceStreamEnd.Restarted, 0
                        TraceEventKind.StreamStarted, 2, 0
                        TraceEventKind.StreamPublished, 0, 1
                        TraceEventKind.StreamEnded, int TraceStreamEnd.Completed, 0
                        TraceEventKind.StreamPublished, 0, 0
                    ]
                    "events in order"
        ]
```

Replace `Trace.events graph` with the accessor `Timed.traceTests` uses, and wrap the list with the same gating (`untracedOnly` or `#if RANVIER_TRACE`) it uses.

- [ ] **Step 2: Run the traced suite to see it fail**

Run: `dotnet test tests/Ranvier.Tests -c Release -p:RanvierTrace=true --filter "Stream memo traces"` (a filtered run is fine for red; the gate in Step 5 is unfiltered)
Expected: FAIL, no events with kinds 60–62.

- [ ] **Step 3: Add the kinds and hooks**

`TraceEvents.fs`, after `TimedState = 55`:

```fsharp
    /// <summary>A stream memo started a stream. <c>Arg</c>: the generation, from 1.</summary>
    | StreamStarted = 60
    /// <summary>
    /// A stream memo published. <c>Arg</c>: its <c>Status</c> byte. <c>Flag</c>: the items folded since the previous
    /// publication. <c>Payload</c>: the state, or the exception.
    /// </summary>
    | StreamPublished = 61
    /// <summary>A stream ended. <c>Arg</c>: a <c>TraceStreamEnd</c>.</summary>
    | StreamEnded = 62
```

Add `| Stream = 13` to `TraceNodeKind`, the three names to the kind-name function (`| TraceEventKind.StreamStarted -> "StreamStarted"` and so on), `Stream` to `nodeKind`, and:

```fsharp
/// <summary>Why a stream ended.</summary>
type TraceStreamEnd =
    | Completed = 0
    | Failed = 1
    | Restarted = 2
    | Disposed = 3
```

`Trace.fs`, the hooks' traced branches:

```fsharp
    [<Conditional("RANVIER_TRACE")>]
    static member StreamNew(graph: obj, id: int, owner: obj) =
#if RANVIER_TRACE
        Tracer.Node (graph, id, TraceNodeKind.Stream, owner)
#else
        ()
#endif

    [<Conditional("RANVIER_TRACE")>]
    static member StreamStarted(graph: obj, id: int, generation: int) =
#if RANVIER_TRACE
        let log = Tracer.LogOf graph
        log.Append (TraceEventKind.StreamStarted, id, 0, generation, 0, log.OpenRun id, null) |> ignore
#else
        ()
#endif

    [<Conditional("RANVIER_TRACE")>]
    static member StreamPublished(graph: obj, id: int, status: int, count: int, payload: obj) =
#if RANVIER_TRACE
        let log = Tracer.LogOf graph
        log.Append (TraceEventKind.StreamPublished, id, 0, status, count, log.OpenRun id, payload) |> ignore
#else
        ()
#endif

    [<Conditional("RANVIER_TRACE")>]
    static member StreamEnded(graph: obj, id: int, reason: int) =
#if RANVIER_TRACE
        let log = Tracer.LogOf graph
        log.Append (TraceEventKind.StreamEnded, id, 0, reason, 0, log.OpenRun id, null) |> ignore
#else
        ()
#endif
```

Change `Streams.fs`'s internal `StreamEnd` enum to use `TraceStreamEnd` directly and delete `StreamEnd`.

`TraceModel.fs`, beside `TimedPublished`:

```fsharp
            | TraceEventKind.StreamPublished ->
                updateNode e.Node (fun n ->
                    if n.Status = TraceNodeStatus.Disposed then
                        n
                    else
                        { n with
                            Status =
                                TraceNodeStatus.Ended (
                                    if e.Arg &&& 1 <> 0 then RunStatus.Pending
                                    elif e.Arg &&& 2 <> 0 then RunStatus.Error
                                    else RunStatus.Ok
                                )
                        })
```

and add `TraceNodeKind.Stream` beside `TraceNodeKind.Timed` in the `RunStart`/`RunEnd` exemptions above it, so a stream's status follows its publications. Where `TraceModel` renders an event as text (search for `TimedPublished` in `TraceModel.fs` and `TraceApi.fs`), add the stream events with the log lines of the spec's signal-map table: `start <node> (stream <n>)`, `publish <node> = <value> (<n> items)`, `fail <node>: <error>`, `end <node> (<reason>)`, `drop <node> (restarted)`.

- [ ] **Step 4: Run the traced test**

Run: `dotnet test tests/Ranvier.Tests -c Release -p:RanvierTrace=true --filter "Stream memo traces"`
Expected: PASS.

- [ ] **Step 5: Gates and commit**

Run: `dotnet fsi build.fsx -- test` (it runs untraced and traced), then `dotnet fsi build.fsx -- test-fable`.
Expected: PASS.

```bash
dotnet fantomas src/Ranvier/TraceEvents.fs src/Ranvier/Trace.fs src/Ranvier/TraceModel.fs src/Ranvier/Streams.fs tests/Ranvier.Tests/Streams.fs
rtk git add src/Ranvier tests/Ranvier.Tests fable/Ranvier.Tests.Fable
git diff --cached --check
git commit -F - <<'EOF'
feat(trace): record stream starts, publications and ends (#67)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
```

---

### Task 5: C# facade and public API baseline

**Files:**
- Modify: `src/Ranvier.CSharp/Reactive.fs` (after `AsyncSource`, ~line 220)
- Create: `src/Ranvier.CSharp/StreamSources.fs`; modify `src/Ranvier.CSharp/Ranvier.CSharp.fsproj` (compile it before `Reactive.fs` or after, matching the file's dependencies)
- Create: `tests/Ranvier.CSharp.Tests/StreamTests.cs`
- Modify: `docs/.ai/public-api-baseline.txt`

**Interfaces:**
- Consumes: `Api.createStream`, `Api.createStreamFold`, the `StreamSource` module.
- Produces: `Reactive.Stream<T>(Func<CancellationToken, StreamSource<T>>)`, `Reactive.StreamFold<TState, T>(TState, Func<TState, T, TState>, Func<CancellationToken, StreamSource<T>>)`, `StreamSources.Create`, `FromAsyncEnumerable`, `FromChannel` (net10.0), `FromObservable`.

- [ ] **Step 1: Write the failing C# test**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Ranvier;
using Xunit;

public class StreamTests
{
    [Fact]
    public void StreamFoldPublishesFoldedItems()
    {
        using var graph = new Graph();
        StreamSink<int>? sink = null;
        var memo = graph.Run(() => Reactive.StreamFold(0, (int s, int x) => s + x,
            _ => StreamSources.Create<int>((s, _) => { sink = s; return new TaskCompletionSource().Task; })));
        Assert.True(graph.Run(() => memo.TryValue).IsPending);
        sink!.Next(2);
        sink.Next(3);
        Assert.Equal(5, graph.Run(() => memo.Value));
    }
}
```

Match the project's test framework and `Reading` accessors (read `tests/Ranvier.CSharp.Tests/AsyncTests.cs` first and use what it uses for `IsPending`).

- [ ] **Step 2: Run to see it fail**

Run: `dotnet test tests/Ranvier.CSharp.Tests -c Release`
Expected: FAIL to compile: `'Reactive' does not contain a definition for 'StreamFold'`.

- [ ] **Step 3: Implement**

`Reactive.fs`:

```fsharp
    /// <summary>The latest item of the stream <c>start</c> returns; a change to a value read by <c>start</c> restarts it.</summary>
    static member Stream<'T>(start: Func<CancellationToken, StreamSource<'T>>) : StreamMemo<'T> =
        Api.createStream start.Invoke

    /// <summary>The fold of the stream's items into <c>seed</c>; a restart begins again from <c>seed</c>.</summary>
    static member StreamFold<'S, 'T>(seed: 'S, fold: Func<'S, 'T, 'S>, start: Func<CancellationToken, StreamSource<'T>>) : StreamMemo<'S> =
        Api.createStreamFold seed (fun state item -> fold.Invoke (state, item)) start.Invoke
```

`StreamSources.fs`:

```fsharp
namespace Ranvier

open System
open System.Threading
open System.Threading.Tasks

/// <summary>Stream sources for C#.</summary>
[<AbstractClass; Sealed>]
type StreamSources =
    /// <summary>A source whose every start calls <c>run</c>.</summary>
    static member Create<'T>(run: Func<StreamSink<'T>, CancellationToken, Task>) : StreamSource<'T> =
        StreamSource.create (fun sink token -> run.Invoke (sink, token))

    /// <summary>Subscribes on each start.</summary>
    static member FromObservable<'T>(items: IObservable<'T>) : StreamSource<'T> = StreamSource.ofObservable items

    /// <summary>Enumerates on each start with the stream's token.</summary>
    static member FromAsyncEnumerable<'T>(items: Collections.Generic.IAsyncEnumerable<'T>) : StreamSource<'T> =
        StreamSource.ofAsyncEnumerable items

#if NET8_0_OR_GREATER
    /// <summary>Reads on each start, delivering every buffered item before waiting again.</summary>
    static member FromChannel<'T>(reader: Channels.ChannelReader<'T>) : StreamSource<'T> = StreamSource.ofChannel reader
#endif
```

Use the namespace the other `Ranvier.CSharp` files use.

- [ ] **Step 4: Update the API baseline**

Regenerate the listing the way #91 did (`docs/.ai/public-api-baseline.txt`; read the file's header for the command, or use `fcs_public_api` on `src/Ranvier` and `src/Ranvier.CSharp`). The diff must contain only additions: `StreamSink`, `StreamSource`, `StreamSourceModule` members, `StreamMemo`, `createStream`, `createStreamFold`, `TraceStreamEnd`, the three `TraceEventKind` cases, `TraceNodeKind.Stream`, `Reactive.Stream`, `Reactive.StreamFold`, `StreamSources`. Any removed or changed line is a bug to fix, not to accept.

- [ ] **Step 5: Run and commit**

Run: `dotnet fsi build.fsx -- test`
Expected: PASS, the C# suite included.

```bash
dotnet fantomas src/Ranvier.CSharp/Reactive.fs src/Ranvier.CSharp/StreamSources.fs
rtk git add src/Ranvier.CSharp tests/Ranvier.CSharp.Tests docs/.ai/public-api-baseline.txt
git diff --cached --check
git commit -F - <<'EOF'
feat(csharp): add Reactive.Stream, Reactive.StreamFold and StreamSources (#67)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
```

---

### Task 6: Counter scenarios

**Files:**
- Modify: `bench/Ranvier.Counters/Scenarios.fs` (workloads beside `Ranvier.flight` ~line 492; cases beside `"async-resolve"` ~line 1030)

**Interfaces:**
- Consumes: `Api.createStreamFold`, `StreamSource.create`, `StreamSink<'T>`.

- [ ] **Step 1: Add the workloads**

In the module that holds `flight`:

```fsharp
    /// <summary>Delivers <c>burst</c> items per round into a stream fold read by a memo and an effect, inside one batch.</summary>
    let streamBurst (burst: int) (n: int) =
        let graph = new Graph ()
        let mutable sink = Unchecked.defaultof<StreamSink<int>>

        let source =
            StreamSource.create (fun s _ ->
                sink <- s
                TaskCompletionSource<unit>().Task :> Task)

        let memo = graph.Run (fun () -> Api.createStreamFold 0 (+) (fun _ -> source))
        let doubled = new Memo<int> (graph, fun _ -> memo.Value * 2)
        let mutable seen = 0
        new Effect (graph, fun () -> seen <- doubled.Value) |> ignore
        sink.Next 0

        {
            Run =
                fun () ->
                    for _ in 1..n do
                        graph.Batch (fun () ->
                            for i in 1..burst do
                                sink.Next i)
            Teardown = fun () -> graph.Dispose ()
        }

    /// <summary>The baseline for <c>streamBurst</c>: <c>burst</c> async-source settles per round, batched or not.</summary>
    let settleBurst (batched: bool) (burst: int) (n: int) =
        let graph = new Graph ()
        let source = AsyncSource<int>(graph)
        let doubled = new Memo<int> (graph, fun _ -> source.Value * 2)
        let mutable seen = 0
        new Effect (graph, fun () -> seen <- doubled.Value) |> ignore
        source.Settle 0
        let mutable total = 0

        let round () =
            for i in 1..burst do
                total <- total + i
                source.Settle total

        {
            Run =
                fun () ->
                    for _ in 1..n do
                        if batched then graph.Batch round else round ()
            Teardown = fun () -> graph.Dispose ()
        }

    /// <summary>A dependency write restarts an open stream, which then receives one item.</summary>
    let streamRestart (n: int) =
        let graph = new Graph ()
        let trigger = Signal (graph, 0)
        let mutable sink = Unchecked.defaultof<StreamSink<int>>

        let source =
            StreamSource.create (fun s _ ->
                sink <- s
                TaskCompletionSource<unit>().Task :> Task)

        let memo = graph.Run (fun () -> Api.createStream (fun _ -> trigger.Value |> ignore; source))
        new Effect (graph, fun () -> memo.TryValue |> ignore) |> ignore
        let next = ref 0

        {
            Run =
                fun () ->
                    for _ in 1..n do
                        next.Value <- next.Value + 1
                        trigger.Value <- next.Value
                        sink.Next next.Value
            Teardown = fun () -> graph.Dispose ()
        }
```

Use the batch and memo constructors the file already uses (`graph.Batch` or `Api.batch` inside `graph.Run`; `new Memo<int> (graph, …)` or `Memo (graph, …)`), and the record type the file calls `Prepared`.

In `all`, after `"async-recover"`:

```fsharp
        for burst in [ 1; 16; 256 ] do
            yield!
                scenario
                    $"stream-burst-%d{burst}"
                    $"deliver %d{burst} items per round into a stream fold read by a memo and an effect"
                    ((if burst = 256 then 20 else 500) * scale)
                    [
                        "Ranvier (stream, batched)", Ranvier.streamBurst burst
                        "Ranvier (AsyncSource, batched)", Ranvier.settleBurst true burst
                        "Ranvier (AsyncSource, unbatched)", Ranvier.settleBurst false burst
                    ]
        yield!
            scenario
                "stream-restart"
                "write a stream's dependency, restarting it, then deliver one item"
                (500 * scale)
                [ "Ranvier", Ranvier.streamRestart ]
```

- [ ] **Step 2: Run the counters locally**

Run the counters the way `docs/content/benchmarks/counters.md` describes for a local run (its "Running locally" section), on .NET and Node, with `--scenario stream-burst-256` (or the harness's filter flag) first, then all scenarios.
Expected: the new scenarios report; existing scenarios' counts match master's within the harness's noise rule.

- [ ] **Step 3: Commit**

```bash
dotnet fantomas bench/Ranvier.Counters/Scenarios.fs
rtk git add bench/Ranvier.Counters
git diff --cached --check
git commit -F - <<'EOF'
bench(counters): add stream-burst and stream-restart scenarios (#67)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
```

---

### Task 7: Signal maps

**Files:**
- Modify: `docs/maps/model/Helpers.fs` (`Feed<'T>` after `Desk<'T>`, ~line 175)
- Modify: `docs/maps/model/MapModel.fs` (`cueOf` ~line 294, `logOf` ~line 321, node naming ~line 190)
- Modify: `docs/maps/SignalMap.fs` (`kindName`, `shapeOf` ~line 99; the stream mark and the open-stream double outline in the node drawing and its CSS)
- Modify: `docs/maps/tests/ModelTests.fs`, `AuthoringTests.fs`, `ControlTests.fs`

**Interfaces:**
- Consumes: Task 4's event kinds and `TraceStreamEnd`; `StreamSource.create`.
- Produces: `Feed<'T>` with `Source: StreamSource<'T>`, `Push: 'T -> unit`, `Burst: 'T list -> unit`, `Complete: unit -> unit`, `Fail: string -> unit`, `Starts: int`.

- [ ] **Step 1: Write the failing model test**

In `docs/maps/tests/ModelTests.fs`, beside the timed event tests, add a test that runs a traced scenario and checks the cues and log lines. Follow the file's existing helper for "run a scenario, collect the scene's cues and log":

```fsharp
testCase "a stream's start, burst, restart and completion render as the spec's table" <| fun _ ->
    let feed = Feed<int>()
    let lines, cues =
        runScenario (fun graph ->
            let topic = Signal (graph, 0)
            let news = Trace.named "news" (fun () -> Api.createStreamFold 0 (+) (fun _ -> topic.Value |> ignore; feed.Source))
            Api.createEffect (fun () -> news.TryValue |> ignore)
            feed.Burst [ 1; 2; 3 ]
            topic.Value <- 1
            feed.Push 4
            feed.Complete ()
            [])
    Expect.containsAll lines [ "start news (stream 1)"; "publish news = 6 (3 items)"; "drop news (restarted)"; "start news (stream 2)"; "end news (completed)" ] "log lines"
```

Replace `runScenario` with the helper `ModelTests.fs` uses for the timed cases.

- [ ] **Step 2: Run to see it fail**

Run: `dotnet test docs/maps/tests -c Release`
Expected: FAIL to compile: `Feed` is not defined.

- [ ] **Step 3: Add `Feed<'T>`**

```fsharp
/// <summary>A stream source that controls drive. Each start opens a new stream; a restart cancels the previous one.</summary>
type Feed<'T>() =
    let mutable sink: StreamSink<'T> = Unchecked.defaultof<_>
    let mutable finished: TaskCompletionSource<unit> = null
    let mutable starts = 0

    /// <summary>The source to return from <c>createStream</c>'s or <c>createStreamFold</c>'s start function.</summary>
    member val Source =
        StreamSource.create (fun s token ->
            starts <- starts + 1
            sink <- s
            let done' = TaskCompletionSource<unit>()
            finished <- done'
            token.Register (fun () -> done'.TrySetResult () |> ignore) |> ignore
            done'.Task :> Task)

    /// <summary>Delivers one item to the open stream.</summary>
    member _.Push(item: 'T) =
        if not (isNull (box sink)) then sink.Next item

    /// <summary>Delivers several items inside one batch, so the map shows one publication for them.</summary>
    member this.Burst(items: 'T list) =
        Api.batch (fun () -> for item in items do this.Push item)

    /// <summary>Completes the open stream.</summary>
    member _.Complete() =
        if not (isNull finished) then finished.TrySetResult () |> ignore

    /// <summary>Fails the open stream with <c>message</c>.</summary>
    member _.Fail(message: string) =
        if not (isNull finished) then finished.TrySetException (Exception message) |> ignore

    /// <summary>The streams started.</summary>
    member _.Starts = starts
```

Under Fable, fable-library's `TaskCompletionSource` lacks the `Try…` members (see `Desk`'s comment); use `SetResult`/`SetException` behind a `completed` flag under `#if FABLE_COMPILER`, as `Desk` does for cancellation.

- [ ] **Step 4: Render the events**

`MapModel.fs`, in `cueOf` before `| _ -> Quiet`:

```fsharp
        | TraceEventKind.StreamStarted -> Opened node
        | TraceEventKind.StreamPublished when e.Arg = int (byte Status.Error) -> Failed node
        | TraceEventKind.StreamPublished -> Surge (node, observers ())
        | TraceEventKind.StreamEnded when e.Arg = int TraceStreamEnd.Restarted -> Drop node
        | TraceEventKind.StreamEnded -> Closed node
```

Add `Opened of int` and `Closed of int` to the cue type beside `Flight`/`Drop`, and in `advance`/the scene, keep a set of open stream nodes (`OpenStreams: Set<int>`), adding on `Opened` and removing on `Closed` and `Drop`; `SignalMap.fs` draws a doubled outline (a second stroke offset by 3 px, the same colour) for nodes in that set.

In `logOf`:

```fsharp
        | TraceEventKind.StreamStarted -> $"start %s{node} (stream %d{e.Arg})"
        | TraceEventKind.StreamPublished when e.Arg = int (byte Status.Error) -> $"fail %s{node}: %s{payload e}"
        | TraceEventKind.StreamPublished when e.Flag = 1 -> $"publish %s{node} = %s{payload e} (1 item)"
        | TraceEventKind.StreamPublished -> $"publish %s{node} = %s{payload e} (%d{e.Flag} items)"
        | TraceEventKind.StreamEnded when e.Arg = int TraceStreamEnd.Restarted -> $"drop %s{node} (restarted)"
        | TraceEventKind.StreamEnded ->
            let reason = (string (enum<TraceStreamEnd> e.Arg)).ToLowerInvariant ()
            $"end %s{node} (%s{reason})"
```

`SignalMap.fs`: `| TraceNodeKind.Stream -> "stream"` in `kindName`, and `| TraceNodeKind.Stream -> "stream"` in `shapeOf`; the `stream` shape is the `memo` rounded rectangle with the dashed stroke the `async` shape uses. The node's hover card adds `stream <generation>` and `open`/`closed` from the scene. `MapModel.fs`'s node-kind naming (~line 190) gets `| TraceNodeKind.Stream -> "stream memo"`.

- [ ] **Step 5: Authoring and control tests**

`AuthoringTests.fs`: a fence with `let news = createStream (fun _ -> feed.Source)` and `let total = createStreamFold 0 (+) (fun _ -> feed.Source)` names both nodes (`MapFence`'s `create…` pattern already matches; the test pins it). `ControlTests.fs`: a `replay` of the restart map (Task 8's third fence) plays back with the expected log lines in order.

- [ ] **Step 6: Run and commit**

Run: `dotnet test docs/maps/tests -c Release`
Expected: PASS.

```bash
dotnet fantomas docs/maps/model/Helpers.fs docs/maps/model/MapModel.fs docs/maps/SignalMap.fs docs/maps/tests/ModelTests.fs docs/maps/tests/AuthoringTests.fs docs/maps/tests/ControlTests.fs
rtk git add docs/maps
git diff --cached --check
git commit -F - <<'EOF'
feat(docs): draw stream memos on signal maps, with a Feed source for examples (#67)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
```

---

### Task 8: Documentation

**Files:**
- Create: `docs/content/guide/streams.md`
- Modify: `docs/content/guide/signal-maps.md` (node and state tables; *Writing a map*)
- Modify: `docs/content/concepts/async-graph.md` (*Not implemented*, ~lines 82–98)
- Modify: `docs/content/concepts/roadmap.md` (*Additional async adapters*, ~lines 86–88)
- Modify: `docs/.ai/fable-compat.md`
- Modify: `docs/content/guide/index.md` (if it lists guide pages)

- [ ] **Step 1: Write `streams.md`**

Front matter as `timing.md` has it (`title: Streams`, an `order` after timing, a one-line `description`). Sections, in this order:

1. **Choosing a node.** A table: async source (settled by hand), async memo (one result per flight), stream memo (many items, a running state).
2. **The latest item** with the first map:

   ```fsharp map
   let feed = Feed<string>()
   let news = createStream (fun _ -> feed.Source)
   createEffect (fun () -> printfn $"headline {news.Value}")

   controls [
       button "Push" (fun () -> feed.Push "rates hold")
       button "Complete" (fun () -> feed.Complete ())
   ]
   ```
3. **Folding token deltas**, with the second map:

   ```fsharp map
   let feed = Feed<string>()
   let message = createStreamFold "" (fun text delta -> text + delta) (fun _ -> feed.Source)
   createEffect (fun () -> printfn $"message {message.Value}")

   controls [
       button "Burst" (fun () -> feed.Burst [ "Hel"; "lo, "; "world" ])
       button "Push" (fun () -> feed.Push "!")
   ]
   ```
4. **Restarts**, a `replay` timeline:

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
5. **Failure and empty completion**, two maps side by side or one with both nodes:

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
6. **Sources:** `StreamSource.create`, `ofAsyncEnumerable`, `ofChannel` (net10.0), `ofObservable`, `ofAsyncIterable` (Fable), each with a short F# example; the C# `Reactive.Stream`/`StreamFold` and `StreamSources` names.
7. **Threads:** the scheduling rules from the spec (synchronous prefix, off-thread coalescing per drain, graph-thread delivery per item or per `batch`), and that `fold` runs on the delivering thread and must be pure.
8. **The contract:** the spec's semantics table, rewritten as a reference table.

If a map's helpers (`controls`, `button`) need `Feed` exported to the map prelude, add it where `Desk` is exported (search `docs/maps/authoring/` for `Desk`).

- [ ] **Step 2: Update the other pages**

- `signal-maps.md`: add `| Dashed rounded rectangle | Stream memo |` to the node table, `| Doubled outline | A stream is open |` to the state table, and a paragraph under *Writing a map*: "`Feed<'T>()` supplies a stream source for examples: `feed.Push item`, `feed.Burst items` (one publication), `feed.Complete ()`, `feed.Fail message`." Link the streams guide.
- `async-graph.md`: remove the streaming bullet and the "Streaming lifetime, restart policies and empty-stream behavior remain design questions…" paragraph; add one sentence linking `../guide/streams.md`: "Stream memos fold a stream's items into a state and restart when `start`'s reads change; see [Streams](../guide/streams.md)."
- `roadmap.md`: move "streaming `IAsyncEnumerable<'T>` adapters" from *Under consideration* to *Implemented in current source*, naming `createStream`, `createStreamFold` and the converters.
- `docs/.ai/fable-compat.md`: `ofAsyncEnumerable` is .NET only; `ofChannel` is net10.0 only; `ofAsyncIterable` is Fable only.

- [ ] **Step 3: Build the docs and validate the maps**

Run the docs build and the map validation as the repo's docs workflow does (`.github/workflows/docs.yml`; the map fences are built by `docs/maps/validation/Generate.fsx`).
Expected: every `streams.md` fence compiles and runs; no broken links.

- [ ] **Step 4: Final gates**

Run, unfiltered: `dotnet fsi build.fsx -- test`; `npm ci`; `dotnet fsi build.fsx -- test-fable`; `dotnet test docs/maps/tests -c Release`.
Expected: PASS on all. The local `verify-trace` IL gate is known to fail locally because of the embedded source path; CI runs it.

- [ ] **Step 5: Commit, push and open the PR**

```bash
rtk git add docs
git diff --cached --check
git commit -F - <<'EOF'
docs(guide): add the streams guide with signal maps, and update the async pages (#67)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:feat/67-stream-memo
```

Open the PR against `master` with `gh pr create`, title `feat(core): add the stream memo (#67)`, body summarising the API, the tests, the counter results, and ending with the 🤖 Generated with [Claude Code](https://claude.com/claude-code) line and the session URL. Do not merge: merging needs the user's approval.
