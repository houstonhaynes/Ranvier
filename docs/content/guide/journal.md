---
title: Input journal and replay
order: 8
---

:::info
Preview — Ranvier is pre-release; its APIs may change. The input journal is a research feature: its entry
format and replay rules may change before the first release.
:::

Record external signal writes, async-source outcomes and MVU messages with an input journal.
Replay them into a matching graph in one of two modes:

- **Diagnostic:** apply recorded outcomes to reproduce the run.
- **Re-enactment:** re-run attributed async work and use its new outcomes.

Journaling works in an untraced build. Traced diagnostic replay can also reproduce the trace;
see [Diagnostic replay](#diagnostic-replay) for its conditions.

For a complete example, [replay a production shop bug](https://ranvier-org.github.io/Ranvier.Persistence/guide/shop-replay/): capture overlapping async shipping
requests in an untraced Release build, then reproduce the incident in a traced Debug build with zero live calls.

## Recording

Pass an `IInputJournal` in the graph's options. `MemoryJournal` keeps the entries in a list:

```fsharp
let journal = MemoryJournal "cart"
let graph = new Graph (GraphOptions.Default.WithJournal journal)
```

With `Journal = None`, the default, a graph records nothing. Every input site then costs one field
load and a null check.

`MemoryJournal.Entries` returns the entries recorded so far, oldest first, and `Clear` removes them.
Another sink implements `IInputJournal`: `Stream` names the journal in every entry, and `Record`
runs on the graph's thread as each entry is recorded. An exception raised by `Record` propagates to
the input that made the entry.

```fsharp
type ConsoleJournal(stream: string) =
    interface IInputJournal with
        member _.Stream = stream
        member _.Record entry = printfn "%d %O %A %O" entry.Seq entry.Kind entry.Key entry.Payload
```

Each entry is a `JournalEntry`:

| Field | Meaning |
| --- | --- |
| `Schema` | The entry format version, 3. |
| `Stream` | The `Stream` of the journal that recorded the entry. |
| `Seq` | The 1-based position of the entry in its graph's journal; 0 for `Setup`. |
| `Kind` | `Write`, `Settle`, `Fail`, `Message`, `Flush` or `Setup`. |
| `Key` | The application key of the input; `None` for an unkeyed input, for `Flush` and for `Setup`. |
| `PayloadType` | The runtime type name of the value. |
| `Payload` | `Encoded (codec, version, data)`, `NonReplayable payloadType`, or `Empty` for `Flush`. |
| `Origin` | `External`, or `Produced (started, index, call)` for an input of the application's attributed async work, identified by the entry recorded before the work started and the input's call within the work. |

A `Flush` entry ends each flush pass that followed a recorded entry. Replay flushes at the same
points, so effects run as often as they did in the recorded run.

A `Setup` entry, with `Seq` 0, precedes the first entry of a graph that started async work before its
first input: its setup work. Its data lists the works in start order, `effect` for an effect's run and
`work` for other work, each followed by the quoted key of the input that started it when it has one,
such as `effect, work "cart"` for an effect and the initial commands of the `Mvu` journaled as `cart`.
`Journal.name key work` gives a work captured with `Journal.work` a key.

### What is recorded

An input is recorded when it arrives from outside every computation: a write from an event handler, a
settle from a completed task, a dispatched message. An input made by a running computation is derived
from the inputs before it, and is not recorded:

- a write or settle made in the body of a memo, effect or async memo, including under `untrack`;
- the model write and the synchronous commands of a journaled MVU message;
- the synchronous part of an `Mvu` command, including an initial command of `Mvu.withCmd`;
- an input made in the body passed to `Journal.derived`.

An equal write is recorded, as the traced log records it.

### Async work

An input that the application's own async work makes after an `await` is recorded with
`Origin = Produced (started, index, call)`: a re-run of that work makes the input again. `started` is
the `Seq` of the last non-`Flush` entry recorded before the work started, 0 before the first entry, and
`index` the work's 0-based position among the works started after that entry. The pair depends only on
the entries before the work, so a work re-run after the same entry carries it whatever else the graph
ran. `call` is the input's 0-based position among the work's calls: each `Journal.produce` call or
command dispatch that made an input, recorded or not, and each input made in an effect's
continuation outside the effect's synchronous part. A
command that dispatches `Loading` synchronously and `Loaded` after an `await` records only `Loaded`,
as call 1. The journal attributes three kinds of work:

- an `Mvu` command: a message it dispatches after its synchronous part;
- on .NET, an effect run: a write, settle, failure or message made in a continuation of the effect's
  body, including one marshalled through `Graph.Dispatch`;
- work captured with `Journal.work`: an input passed to `Journal.produce`.

An effect's continuation on .NET needs no journal code:

```fsharp
createEffect (fun () ->
    let q = query.Value

    task {
        let! hits = service.Search q
        results.Settle hits
    }
    |> ignore)
```

Fable has no async context to carry the effect run into the continuation, so under Fable capture the
work in the synchronous part and pass each later input through `Journal.produce`. The same code
records the same entries on .NET:

```fsharp
createEffect (fun () ->
    let q = query.Value
    let work = Journal.work graph

    task {
        let! hits = service.Search q
        Journal.produce work (fun () -> results.Settle hits)
    }
    |> ignore)
```

`Journal.produce` runs the input on the graph's thread, and queues it when called from another
thread. An input made outside every attributed work, such as a socket push or an event handler's
write after an `await`, is recorded as `External`: replay applies it, and nothing re-runs it.

## Keys and codecs

A replay finds each input by its application key, a string stable across runs. `Journal.key`
registers a signal, `Journal.keySource` an async source:

```fsharp
let intCodec: JournalCodec<int> =
    { Name = "int"
      Version = 1
      Encode = string
      Decode = fun text ->
          match System.Int32.TryParse text with
          | true, value -> Ok value
          | _ -> Error ("not an int: " + text) }

let count = createSignal 0 |> Journal.key "count" intCodec
let load = AsyncSource<int> graph |> Journal.keySource "load" intCodec
```

A `JournalCodec<'T>` is a record of functions. `Name` and `Version` are recorded with each value, and
replay requires the target's codec to carry the same pair. A Ranvier.Wire codec adapts in one
expression:

```fsharp
let lineCodec (wire: WireCodec<CartLine>) : JournalCodec<CartLine> =
    { Name = "cart-line"
      Version = 1
      Encode = wire.Encode
      Decode = wire.Decode >> Result.mapError string }
```

A write to an unkeyed input, or a value its codec fails to encode, is recorded as `NonReplayable`.
The entry stays in the journal, and a replay that reaches it fails.

A key is unique on its graph, and a node holds one key. The registration belongs to the innermost
enclosing scope, as an `onCleanup` does: when the scope is disposed, or its computation re-runs, the
key is free for another node. On a graph with a journal, `Journal.key` raises
`InvalidOperationException` in the body of a pure computation, such as a `createMemo` memo. In a
traced build `Journal.key` also labels an unlabelled node with the key.

The settles of an async memo are not recorded: an async memo's flight is a computation over its
inputs. Route an external result through a keyed `AsyncSource` to journal it.

## MVU messages

`Mvu.journal` registers an MVU loop's messages:

```fsharp
let app =
    Mvu.withCmd init update
    |> Mvu.journal "cart" cartMsgCodec
```

A message dispatched from outside every computation records one `Message` entry. A message a command
dispatches later, after an `await`, records a `Produced` entry; the command needs no journal code.
The synchronous dispatches of the initial commands of `Mvu.withCmd` are derived from the
construction and record nothing; the target's construction makes them again.
Another adapter registers its message site with `Journal.messages`, wraps each external message in
`Journal.recordMessage`, and passes the messages of its async work through `Journal.produce`. A
replay applies a `Message` entry by calling the site's dispatch function:

```fsharp
let site = Journal.messages "chat" chatMsgCodec post graph
let send msg = Journal.recordMessage site msg (fun () -> post msg)
```

`recordMessage` records the message when it arrives from outside every computation, then runs the
apply function; the writes and messages made while it runs are derived and record nothing. With no
journal on the graph, the site records nothing and `recordMessage` only applies the message.

## Replay

`Journal.replay mode graph entries` applies the entries to `graph` in `Seq` order, on the graph's
thread. The target graph needs a journal, with the inputs registered under the recorded keys: build it
with the same code as the recorded run.

```fsharp
let replayed = MemoryJournal "cart-replay"
let fresh = new Graph (GraphOptions.Default.WithJournal replayed)
buildCart fresh
Journal.replay ReplayMode.Diagnostic fresh journal.Entries
```

The entries between two `Flush` entries run in one batch; a segment of one entry runs unbatched. The
replay's own inputs are recorded on the target's journal.

### Diagnostic replay

`ReplayMode.Diagnostic` reproduces the recorded run. Every entry is applied: async sources settle
with the recorded values, a recorded failure arrives as a `JournalReplayedFailure` carrying the
recorded message, and the `Produced` messages and writes are applied in their recorded order. The
attributed async work still runs. A work started during the replay is identified by the replayed
entry applied before it and its position after that entry, so a re-run work carries its recorded
`(started, index)` whatever the target ran before the replay, including an earlier replay into the
same graph. An input of a re-run work is dropped while the replayed entries hold an unmatched
`Produced` entry of the same work and call, during the replay or after it returns, including an
input of a `Journal.produce` call or a command's dispatch that runs synchronously because the
awaited task has already completed. The work's other inputs are live: a synchronous dispatch that
the recorded run made before its `await`, and the dispatch of a command still in flight when the
journal was cut, reach `update`. Setup work, started before a graph's first entry, such as an
initial command of `Mvu.withCmd`, carries `started = 0` and is matched by its position among the
target's setup work. A replay whose entries hold an input of setup work requires the journal's
`Setup` entry among them and a target with no recorded entry (see
[Matching target](#matching-target)). The inputs of other work started on the target outside the
replay, before it or after it, are live.
A gateway that also wants to skip its external call reads `Journal.replaying` in its synchronous part:

```fsharp
createEffect (fun () ->
    let q = query.Value
    let work = Journal.work graph

    if Journal.replaying graph <> Some ReplayMode.Diagnostic then
        startSearch q (fun hits -> Journal.produce work (fun () -> results.Settle hits)))
```

Under a traced build, a diagnostic replay of a run whose batches each end in a flush gives the recorded
run's `Trace.snapshot` and the `Trace.why` of every effect run.

### Re-enactment

`ReplayMode.Reenact` applies the `External` entries, writes, settles, failures and messages alike,
and skips the `Produced` entries. The application's async work runs again and makes those inputs,
and a changed result flows through the graph. The re-run inputs arrive when the work completes, after
the replay returns. Use it to check a recorded session against a new service or a changed `update`.

### Matching target

A replay requires a matching target: a graph whose setup work begins with the setup work of the
`Setup` entry, the same works in the same order. The recorded inputs of a setup work are attributed
to the target's setup work at the same position, so each setup work that made a recorded input must
also match the target's setup work at that position alone. Before it applies any entry, a replay in
either mode compares the two, and raises `JournalReplayException` when they differ, naming the
recorded and the found setup work:

```text
Journal entry 0 (unkeyed, setup) cannot be replayed: the journal recorded 1 setup work
[work "initial"], and the target started 2 setup works [effect, work "initial"], which does not
begin with the recorded setup work
```

`Journal.setupWorksOf graph` lists a graph's setup work descriptions in start order, such as
`[| "effect"; "work \"initial\"" |]`, and is empty for a graph with no journal. Compare it with the
description list in the exception to find the setup work the target added or reordered.

A setup work is told apart from another by its kind and its name. Unnamed works of one kind, such as
two effects or two `Journal.work` calls without `Journal.name`, have equal descriptions, and a
target whose setup work holds the description of a producing setup work at a later position also
raises:

```text
Journal entry 0 (unkeyed, setup) cannot be replayed: the journal recorded 1 setup work [work],
and the target started 2 setup works [work, work], which match the recorded setup work at more
than one position; Journal.name tells the setup works apart
```

When the journal holds an input of a setup work, a matching target has also recorded no entry before
the replay: the live setup work of a target that already took an input is not tied to the recorded
one. A replay into such a target raises:

```text
Journal entry 0 (unkeyed, setup) cannot be replayed: the journal's setup work [work "initial"]
made a recorded input, and the target recorded an entry before the replay
```

Entries that hold an input of setup work must hold the journal's `Setup` entry too. A replay of
such entries without it, such as the entries after a position, raises on the first `Produced` entry
of setup work, whatever the target:

```text
Journal entry 2 (key "initial", System.Int32) cannot be replayed: the entries hold an input of
setup work and no Setup entry; a tail replay needs the journal's Setup entry
```

A second replay into one graph raises when its entries hold an input of setup work. A journal whose
setup work made no recorded input, such as one whose only setup work is an effect, replays into a
target that has already taken inputs, including an earlier replay into the same graph. Entries with
no `Setup` entry and no input of setup work replay into every target.

Build the target with the code that built the recorded graph, in the same order, and replay before
any other input to it. An extra work created before the application changes the position of every
setup work after it, and the replay raises whenever the shift reaches a setup work that made a
recorded input. Work started after that code, such as an extra effect
created once the application is built, leaves the target matching when its description differs
from the description of every producing setup work; name the setup works with `Journal.name` to make
it differ.

### Exactly once

In both modes, in a matching target, each recorded input reaches the graph once, provided every
input of the re-run async work is attributed. Entries that hold an input of setup work match only
with their `Setup` entry; without it the replay raises before it applies any entry. An unattributed
input of re-run work, such as an effect continuation's settle under Fable without `Journal.produce`,
is recorded as `External`: the replay applies it, and the re-run work makes it again.

### Errors

A replay raises `JournalReplayException` on an entry it cannot apply, naming its `Seq`, `Key` and
`PayloadType`, with the `Reason`:

- the target's setup work does not begin with the recorded setup work of the `Setup` entry, or
  matches a producing setup work at more than one position;
- the entries hold an input of setup work and no `Setup` entry;
- the entries hold an input of setup work, and the target recorded an entry before the replay;
- the entry is `NonReplayable`;
- the entry has no key, or carries an `Empty` payload;
- its key is not registered on the target graph;
- the key's input takes no entry of that kind, such as a `Settle` for a signal;
- the codec name or version differs from the key's codec;
- the payload fails to decode.

The entries of a batch are decoded before the batch runs. A failing entry leaves the earlier batches
applied and its own batch unapplied. A replay into a graph with no journal raises
`InvalidOperationException`.

## Restore

A restore rebuilds a graph from a stored-state snapshot and replays the journal entries recorded
after it. The snapshot comes from a Ranvier.Persistence session; see
[restoring from a snapshot](https://ranvier-org.github.io/Ranvier.Persistence/guide/journal-restore/)
in the Ranvier.Persistence documentation.

## Fable

The journal, `Mvu.journal` and both replay modes behave the same under Fable, with two differences,
both from Fable having no async context:

- an effect's continuation is not attributed to the effect run: pass its inputs through
  `Journal.produce`;
- a write or settle after an `await` in an async memo's body is recorded as an `External` input,
  where .NET records nothing. Pass it through `Journal.produce`, with the work captured before the
  `await`, to record it as `Produced` and apply it once in either mode.

See [Fable](../fable/index.md).
