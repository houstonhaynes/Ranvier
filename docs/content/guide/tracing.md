---
title: Tracing
order: 7
---

:::info
Preview — Ranvier is pre-release; its APIs may change. Tracing is a research
feature: its queries and dump schema may change before the first release.
:::

Use `Trace` to find why a node ran, why it stayed unchanged, or where it was created. A traced
build records graph operations and their source lines in a per-graph event log.

Untraced builds compile the log out; their Ranvier IL matches a build without tracing, method for
method.

## Why trace a graph

An effect's call stack shows the flush loop. The trace log connects that run to the write that
triggered it, and also explains why a node did not run. Choose a query for your question:

- **Why did this run?** A banner re-rendered after a cart edit. `Trace.why` walks from the run back
  to the write that started it, one hop per node.
- **Why did this not run?** A total stayed stale after a write. `Trace.whyNot` names the reason: an
  open batch, a node nobody reads, a check that found the upstream unchanged, or disposal.
- **Where did this node come from?** A graph holds a thousand memos. `Trace.origin` gives the file
  and line that created one, the owner chain it lives under, and its label.
- **What does the graph look like now?** `Trace.snapshot` prints the owner tree with every node's
  status, run count and sources.
- **What is mounted in the view?** [Dumping a view](../view/testing.md#dumping-a-view) shows component
  fields and rendered regions, and connects an instance field to its core node for `Trace.why`.

## Turning it on

The `RanvierTrace` MSBuild property compiles tracing in. Debug builds default to traced; every other
configuration defaults to untraced. Set the property explicitly to override either default:

```bash
dotnet build src/Ranvier -c Release -p:RanvierTrace=true
```

A traced build defines `RANVIER_TRACE` for every project in the repository, so tests and samples see
the same build as the library.

A traced build also stops runaway reruns. A flush raises `InvalidOperationException` naming the node
once a node's own runs have queued it again 1,000 times, as an effect or projection that writes a signal
it depends on, on every run, does. A node dequeued 100,000 times in a flush, as in a cycle of nodes
writing each other's dependencies, raises too. An untraced build has no limit.

### From NuGet

The traced build ships as `Ranvier.Traced`, the C# façade's as `Ranvier.CSharp.Traced`, and likewise
`Ranvier.Elmish.Traced` and `Ranvier.Query.Traced`. Each holds the same assembly, namespaces and version as
its untraced package; only the package id differs. Reference
one or the other behind a switch of your own, and define `RANVIER_TRACE` with it:

```xml
<PropertyGroup Condition="'$(RanvierTrace)' == 'true'">
    <DefineConstants>$(DefineConstants);RANVIER_TRACE</DefineConstants>
</PropertyGroup>
<ItemGroup>
    <PackageReference Include="Ranvier" Condition="'$(RanvierTrace)' != 'true'" />
    <PackageReference Include="Ranvier.Traced" Condition="'$(RanvierTrace)' == 'true'" />
</ItemGroup>
```

`dotnet build -p:RanvierTrace=true` then restores and builds against the traced package. Pass the same
switch to a separate `dotnet restore`. Referencing both packages at once fails the build: they carry the
same assembly.

:::details Packing a traced build

Packing the repository's traced build needs the explicit switch, `dotnet pack -c Release
-p:RanvierTrace=true`, which sets the `.Traced` id. A Debug build is traced by default and keeps the
untraced id, so `dotnet pack -c Debug` fails with an error naming the switch.

:::

The query functions exist only in a traced build. Code that calls them compiles in a traced build
alone; guard it with `#if RANVIER_TRACE`, or keep it in scripts that load a traced `Ranvier.dll`.
:::details Queries in F# Interactive

F# Interactive does not define `RANVIER_TRACE`. A script referencing a traced DLL calls the queries
directly.

:::

## Labels

Every node gets an identity path, built from the owner tree. A segment is the node's label if it
has one, otherwise its creation site. An unlabelled graph reads as file and line:

```text
/Cart.fsx:11 Memo ok runs 3 <- /Cart.fsx:9
```

`Trace.named` labels the first node or owner its function creates:

```fsharp
let lines = Trace.named "lines" (fun () -> createSignal [ { Sku = "tea"; Price = 4m; Qty = 1 } ])
let discount = Trace.named "discount" (fun () -> createSignal 0m)
let subtotal =
    Trace.named "subtotal" (fun () ->
        createMemo (fun _ -> lines.Value |> List.sumBy (fun l -> l.Price * decimal l.Qty)))
let total = Trace.named "total" (fun () -> createMemo (fun _ -> subtotal.Value - discount.Value))
let banner = Trace.named "banner" (fun () -> new Effect (graph, fun () -> printfn "total %M" total.Value))
```

`Trace.named` is available in every build. Untraced, it inlines to `f ()`, so a literal label costs
nothing.

A computed label, such as one per row, goes through `Trace.label`:

```fsharp
let prices =
    [ for i in 1 .. 3 ->
        let price = createSignal (i * 10)
        Trace.label (graph, price, $"price[{i}]")
        price ]
```

:::details Conditional labels and F# Interactive

`Trace.label` is a `Conditional("RANVIER_TRACE")` member. A caller built without `RANVIER_TRACE`
drops the call, and the interpolated string is never built. The label takes effect only in callers
compiled with the define; a script run in F# Interactive against a traced DLL keeps the site path.

:::

## Node identity

A node has three identities. Each holds for a different span:

| Identity | Example | Stable across |
| --- | --- | --- |
| Runtime id | `TraceNodeRef.Node`, `4` | One graph instance. Every graph numbers its nodes from 1, so ids repeat across graphs and runs. |
| Located path | `/Cart.fsx:11`, `/total/Cart.fsx:20` | Reruns of the same build with the same creation order. An edit that moves a creation site changes it. |
| Named path | `/subtotal`, `/host/child@2` | Reruns, and edits that keep the labelled tree and its creation order. |

A path is named when every segment is a label from `Trace.named` or `Trace.label`; one site segment
makes it located. Two suffixes keep paths distinct, and both repeat when the program repeats its
creation order:

- `#n` tells apart siblings with the same segment: the second takes `#1`.
- `@k` numbers the nodes created at one path, from 1, once there are two: a host memo that creates a
  child in each run gives `/host/child@1`, then `/host/child@2`. An `@k` path keeps the stability of
  its segments. A path given without `@k` finds the live holder, else the latest.

Persist a named path, not an id. `Trace.lookup` finds a node by either:

```fsharp
match Trace.lookup graph (TraceAddress.ByPath "/subtotal") with
| TraceLookup.Live node -> printfn $"{node.Path} is node {node.Node}, {node.Stability}"
| TraceLookup.Disposed (node, seq) -> printfn $"{node.Path} was disposed at #{seq}"
| TraceLookup.Checkpointed node -> printfn $"{node.Path} was created before the checkpoint"
| TraceLookup.Missing -> printfn "no such node"
| TraceLookup.Foreign -> ()
```

`Disposed` carries seq 0 when the checkpoint holds the dispose. `Missing` also covers a node the
checkpoint has released. `Trace.lookupNode graph node` looks up a node handle, and gives `Foreign` for
a node of another graph or an untraced one.

## Why did it run

After `lines.Value <- [ { Sku = "tea"; Price = 4m; Qty = 2 } ]`, the banner prints `total 8`.
`Trace.why` explains the banner's last run:

```fsharp
Trace.why graph banner |> Trace.render graph |> printfn "%s"
```

```text
why /banner run 2
  #49 RunStart /banner (Cart.fsx:13)
  #46 Mark /banner <- /total (Cart.fsx:13)
  #45 Moved /total (Cart.fsx:12) = 8
  #44 RunStart /total <- /banner (Cart.fsx:12)
  #41 Mark /total <- /subtotal (Cart.fsx:12)
  #40 Moved /subtotal (Cart.fsx:11) = 8
  #39 RunStart /subtotal <- /total (Cart.fsx:11)
  #32 Mark /subtotal <- /lines (Cart.fsx:11)
  #31 Write /lines (Cart.fsx:9) = [{ Sku = "tea"; Price = 4M; Qty = 2 }]
  root: user write #31
```

Read it from the bottom: a write to `lines` marked `subtotal`; the banner's check pulled `total`,
which pulled `subtotal`; each value moved, so the banner ran. `#n` is the event's position in the
log, and `= value` is the value a write, move or settle recorded, as one line of at most 60 characters:
its `ToString` text on .NET, its `%A` text under Fable. The chain ends at a root: a user
write, the node's creation, a pull by a reader, an async source's settle, a cause older than the
retained events (`before the checkpoint`), or a step whose cause the log does not record
(`unrecorded after #n`).

:::details Earlier runs and shorter cause chains

`Trace.whyAt graph node run` explains an earlier run by number, and `Trace.whyDepth graph depth
node` stops after `depth` steps.

:::

## Why did it not run

Writing a new line list with the same subtotal leaves the banner alone:

```fsharp
lines.Value <- [ { Sku = "tea"; Price = 2m; Qty = 4 } ]
Trace.whyNot graph banner |> Trace.render graph |> printfn "%s"
```

```text
checked clean #63 over /total
```

`subtotal` recomputed to the same value, so `total` did not move, and the check walk resolved the
banner clean.

:::details Other reasons reported by whyNot

| Reason | Meaning |
| --- | --- |
| `queued #n` | The node is scheduled and its run has not started: a batch is open, or the flush is not reached. |
| `unobserved since mark #n` | The node was marked, and nothing reads it since. A memo whose last reader was disposed shows this. |
| `checked clean #n over ...` | A check walk found every source unchanged. |
| `skipped as the running reader #n` | The node wrote a source it reads while running. |
| `not reached: propagation stopped at #n` | Propagation stopped upstream: a write that kept its value, or a run that did not move. |
| `disposed #n` | The node was disposed. |
| `no reason recorded` | The node ran after its last mark, or was never marked. |

:::

## What did each run do

The examples in this section add an async shipping quote to the cart. A new subtotal starts a new
quote, and `total` reads both:

```fsharp
let shipping = Trace.named "shipping" (fun () -> createAsync (fun _ _ -> quote subtotal.Value))
let total = Trace.named "total" (fun () -> createMemo (fun _ -> subtotal.Value + shipping.Value))
```

`Trace.history` lists every run of a node, oldest first:

```fsharp
Trace.history graph total |> Trace.render graph |> printfn "%s"
```

```text
history /total
  run 1 #15 pending moved flush 1 root: created #8
  run 2 #55 pending flush 2 root: user write #38
  run 3 #71 ok moved flush 3 = 11 root: user write #38
```

Each line gives the run number, its `RunStart`, how it ended, whether it moved the value, the flush
it started in, the value an ended run left, and the root of its `why` chain. Run 2 read a quote
still in flight and ended pending. Run 3 ran when the quote settled; its root is the write that
started the quote.

## What is it waiting on

`Trace.waitingOn` names the pending sources the node's last run read, and lists the node's flights,
newest first:

```fsharp
Trace.waitingOn graph total |> Trace.render graph |> printfn "%s"
```

```text
waiting /total
  suspended on /shipping
```

For an async memo, it lists each flight with the run that started it and its result:

```text
waiting /shipping
  flight 2 #57 run 2 settled #64
  flight 1 #26 run 1 dropped #63 superseded
```

:::details Flight outcomes and deferred runs

The first quote settled after the second one started, so its result was dropped. A flight can end
`in flight`, `settled`, `failed`, `cancelled` or `dropped`. A dropped flight is `superseded` by a
newer flight, `disposed` with its node, `suspended`: a failure that arrived while the newest run
waits on a pending source, `trailing`: a failure that arrived while a `FinishCurrent` trailing run is
owed, or `retried`: a failure that arrived after a `DropWhilePending` flight discarded a change. A settle
marked `held pending` kept its value while a newer run waits. Its `Settle` event's `Flag`
tells the two apart: 1 for a run suspended on a pending source, 2 for an owed trailing run. A change that
arrives during a `FinishCurrent` flight records `RunDeferred` in place of a `RunStart`. A change that a
`Drop` or `DropWhilePending` flight discards records `RunDropped`, with `Flag` 0 when a read discarded it
and 1 when the flight's settle did.

:::

`Trace.why` follows a settle back to the run that started the flight:

```text
  #65 Moved /shipping (Cart.fsx:10) = 3
  #64 Settle /shipping (Cart.fsx:10) = 3
  #57 FlightStart /shipping (Cart.fsx:10)
  #56 RunStart /shipping <- /banner (Cart.fsx:10)
```

An `AsyncSource` settle ends the chain at `settle #n`.

## Where did it come from

```fsharp
Trace.origin graph total |> Trace.render graph |> printfn "%s"
```

```text
/total Memo at Cart.fsx:12 #8
```

The `TraceOrigin` record also carries the owner chain up to the graph root and the run that created
the node, which tells apart a node created at startup from one re-created by each run of its owner.

:::details Creation sites and resolving paths

The site is the innermost stack frame outside Ranvier, FSharp.Core and `System.*`. `Trace.resolve
graph "/total"` goes the other way, from a path to the node id.

:::

## What the graph looks like

```fsharp
Trace.snapshot graph |> Trace.render graph |> printfn "%s"
```

```text
/
  /lines Signal fresh runs 0
  /discount Signal fresh runs 0
  /subtotal Memo ok runs 3 <- /lines
  /total Memo ok runs 2 <- /subtotal, /discount
  /banner Effect ok runs 2 <- /total
```

`Trace.render` text is for people, and its layout may change. A tool reads the
[machine-readable documents](#machine-readable-diagnostics) instead.

A `map replay` fence draws a log like this one. The page runs the example and presses each button once; Play, Step and the scrubber move through its events.

```fsharp map replay code=collapsed
let lines = createSignal [ 4; 6 ]
let discount = createSignal 0
let subtotal = createMemo (fun _ -> List.sum lines.Value)
let total = createMemo (fun _ -> subtotal.Value - discount.Value)
createEffect (fun () -> printfn $"total {total.Value}")

controls [
    button "Add a line" (fun () -> lines.Value <- lines.Value @ [ 5 ])
    button "Discount 2" (fun () -> discount.Value <- 2)
]
```

:::details Inspect the graph at an earlier event

`Trace.snapshotAt graph seq` folds the log up to an earlier event, to see the graph as it was.

:::

## Dumps

`Trace.dumpText graph` returns the log as JSON Lines: a header, the folded starting state,
then one object per event. `Trace.dump graph path` writes the same text to a file.

Graphs without timed nodes retain schema 1. Debounce and throttle select schema 2; the .NET parser
accepts both, and schema 3. `Trace.dumpTextWith graph codecs` writes schema 3, whose payloads carry
their [form](#payloads-and-codecs). `TimedState` events retain captured and published status, the window, remaining time,
and the winning capture. Their timestamp is relative to the first timed node using that clock.
The graph assigns each clock a stable `timebase` number, so independent clock origins remain
independent. OS timer handles and absolute clock origins are omitted.

Use `Trace.timing graph node` for a live timed gate, alongside `Trace.whyNot`. It returns `None`
for nodes outside this graph or other node kinds. `TraceModel.timing events nodeId` returns the
latest recorded timing state for replay, including parsed dumps. Timed captures count as runs;
expiry publication points downstream causes at the winning capture without creating a flight.
Deterministic dumps require the same inputs and callback order; native dispatch order can vary.

:::details JSON Lines dump format

```text
{"schema":1,"target":"net","graph":1,"checkpoint":null,"seqFrom":1}
{"snapshot":{"seq":0,"root":0,"nodes":[],"owners":[],"sources":[],"observers":[],"siblings":[],"incarnations":[]}}
{"seq":1,"kind":"GraphNew","node":0,"other":1,"arg":0,"flag":0,"cause":0,"payload":null}
{"seq":2,"kind":"OwnerNew","node":2,"other":1,"arg":0,"flag":1,"cause":0,"payload":"trace-sample.fsx:29"}
{"seq":3,"kind":"NodeNew","node":1,"other":0,"arg":1,"flag":0,"cause":0,"payload":"trace-sample.fsx:31"}
{"seq":4,"kind":"Label","node":1,"other":0,"arg":0,"flag":0,"cause":0,"payload":"count"}
```

:::

The verification sample produces byte-identical dumps across repeated runs with identical inputs and
event order. Single-threaded access alone does not make async completion order, source sites or payload
formatting deterministic for every program.
`Trace.events graph` returns the raw `TraceEvent[]` for your own analysis.

## Incremental reads and checkpoints

A consumer that polls the log keeps a cursor: the seq of the last event it read, 0 before the first.
`Trace.eventsSince graph seq` returns the events after `seq`, oldest first, and copies those alone.

```fsharp
let mutable cursor = 0

let drain () =
    let fresh = Trace.eventsSince graph cursor

    if fresh.Length > 0 then
        cursor <- fresh[fresh.Length - 1].Seq

    fresh
```

`Trace.checkpoint graph seq name` folds the events up to and including `seq` into a snapshot named
`name`, then drops them from the log along with the values and exceptions they hold. It runs on the
graph's thread between flushes, like a dump, and is allowed inside a `batch`. `Trace.checkpointOf graph`
returns a `TraceCheckpoint` record: its `Name`, null before the first checkpoint, and its `Snapshot`, a
`TraceSnapshot` whose `Seq` is the last dropped seq.

```fsharp
let events = Trace.events graph
Trace.checkpoint graph events[events.Length - 1].Seq "session-1"
```

After a checkpoint:

- The checkpoint releases the nodes and owners disposed before it, through `TraceModel.prune`. On
  .NET it also releases the signals and async sources created before it that the garbage collector has
  reclaimed. `Trace.snapshot` returns what it returned before, without them, and `reconcile` returns what
  it returned before. A released node is then absent: `origin` raises `ArgumentException` for it and
  `resolve` returns `None`. `origin` of a live node created before the checkpoint reads the snapshot,
  with `Run` 0. `dumpText` writes the checkpoint's name and snapshot in
  its header lines, then the retained events.
- `why`, `whyAt`, `whyDepth` and `history` end a chain whose cause was dropped at
  `BeforeCheckpoint name`. A first run still ends at `Created`, with the node's `NodeNew` seq.
- `snapshotAt` accepts a seq from the checkpoint's seq on. `whyNot` reads its reason from the retained
  events and its edges from the checkpoint and the retained events. `waitingOn` reads the retained
  events alone.
- A cursor before the checkpoint's seq has expired: `eventsSince` raises `ArgumentOutOfRangeException`,
  naming the checkpoint's seq. Resync with `Trace.eventsSince graph (Trace.checkpointOf graph).Snapshot.Seq`.
- A seq that falls between a `NodeNew` or `OwnerNew` and the `Label` recorded with it raises
  `ArgumentOutOfRangeException`; checkpoint at the label's seq instead.

`TraceModel.prune snapshot` is the pruning a checkpoint applies, available on any `TraceSnapshot`. It keeps
the live nodes and owners, the disposed ones a kept entry references as its owner, parent or host, their
edges, and the sibling and incarnation counts of the paths that can recur. Use it to shrink a snapshot
folded by your own analysis, such as `TraceModel.prune (Trace.snapshot graph)`.

C# callers use `Tracing.EventsSince`, `Tracing.Checkpoint` and `Tracing.CheckpointOf`.

## Exporting the trace

`Trace.export graph sink policy` streams the log to a sink while the graph runs. The export writes a
header of the current checkpoint and the retained events at once, then, at the end of each flush on the
graph's thread, the events recorded since. `TraceExport.Drain ()` exports a write that ran no flush; it
runs on the graph's thread between flushes, like a dump. `Trace.export` has the same thread rule, and
raises `ArgumentOutOfRangeException` when `policy.Capacity` is less than 1.
Dispose the export to end the stream; its `Completion` task completes once the sink holds every record,
and faults with the sink's error or a strict codec's `TraceCodecException`. The graph keeps running
after a fault. Called on the graph's thread, `Dispose` queues the rest of the stream before it returns,
so `Completion.Wait ()` there settles without the graph's thread. Called off it, `Completion` settles
once the graph runs the end `Dispose` dispatches.

```fsharp
let sink = TraceSinks.jsonl "session.jsonl"

let export =
    Trace.export
        graph
        sink
        {
            Capacity = 1024
            Full = TraceExportFull.Drop
            Checkpoint = TraceExportCheckpoint.Enqueued
            Codecs = TraceCodecs.ofList []
        }

// ... run the program ...
export.Dispose ()
export.Completion.Wait ()
(sink :> System.IDisposable).Dispose ()
```

The stream is a schema 3 dump, encoded by `policy.Codecs`: the `Header` record holds the header and
snapshot lines, and each `Events` record holds the lines of one batch. With no checkpoint taken during
the export, the stream's text equals `Trace.dumpTextWith graph policy.Codecs`.

`Capacity` bounds the events the export queues for the sink. `Full` sets what a full queue does:

- `TraceExportFull.Wait` keeps the batch in the log and exports it once the sink frees room. The stream
  loses no event, and the log holds the backlog. The stream's end is queued past `Capacity`.
- `TraceExportFull.Drop` drops the batch and records its seqs in a gap. Consecutive drops share one gap,
  written as the line `{"gap":{"from":F,"to":T}}` once the queue has room or the stream ends.
  `TraceExport.Dropped` counts the dropped events.

`Checkpoint` sets when the export [checkpoints](#incremental-reads-and-checkpoints) the log under the
sink's name, so the log stays bounded:

- `TraceExportCheckpoint.Never` leaves the log to the caller.
- `TraceExportCheckpoint.Enqueued` checkpoints at the events queued or dropped. Under `Drop` the log
  stays bounded behind a stalled sink.
- `TraceExportCheckpoint.Acknowledged` checkpoints at the events the sink has completed.

A checkpoint runs between flushes, once `Capacity` exported events lie past the previous one, and at
the stream's end. The log retains fewer than `Capacity` exported events. A checkpoint taken elsewhere
past the events the export has read becomes a gap in the stream.

`TraceModel.parseDump` and `TraceContract.parseDumpWith` read an exported stream. Its gap lines go to
`TraceDump.Gaps` as inclusive `(from, to)` ranges in stream order, absent from `Events`. A dump with no
gap lines parses to an empty `Gaps`. Code that constructs a `TraceDump` record itself sets `Gaps` too.
`TraceModel.why` over the parsed events ends a chain at `BeforeCheckpoint` for any missing cause older
than the event, including one inside a gap.

Two reference sinks implement `ITraceSink`:

- `TraceSinks.memory ()` holds the records in memory, on .NET and Fable. `Records` lists them, `Text`
  concatenates their lines, and `Stall` and `Release` hold back and complete the writes, for tests.
- `TraceSinks.jsonl path` writes each record's lines to a file, on .NET, creating or truncating it. A
  write completes once its text is flushed. The sink's name, and so its checkpoints' name, is its
  `Path`. Dispose the sink after the export's `Completion` to close the file.

A sink of your own implements `Name` and `Write`, which returns a task that completes once the sink
holds the record. The export writes one record at a time, in stream order.

C# callers use `Tracing.Export`, `Tracing.MemorySink` and `Tracing.JsonlSink`.

## Machine-readable diagnostics

A test, a debugger or an agent reads the graph through immutable contract values and their JSON
documents. The values hold no live object: every recorded value is a `TracePayload`, and every node is
a `TraceNodeRef` with its [identity](#node-identity).

| Query | Value | Document |
| --- | --- | --- |
| `Trace.state graph codecs` | `TraceGraphState`: nodes, owners, sources and observers | `TraceJson.state state` |
| `Trace.runs graph codecs node` | `TraceRunState list`, oldest first | `TraceJson.runs seq runs` |
| `Trace.whyWith graph codecs node` | `TraceWhy`: the last run's cause chain | `TraceJson.why seq why` |
| `Trace.whyNot graph node` | `WhyNotReason option` | `TraceJson.whyNot seq reason` |
| `Trace.waitingOn graph node` | `TraceWaiting` | `TraceJson.waiting seq waiting` |
| `Trace.lookup graph address` | `TraceLookup` | `TraceJson.lookup seq lookup` |

```fsharp
let codecs = TraceCodecs.ofList []
let seq = Trace.lastSeq graph
let state = TraceJson.state (Trace.state graph codecs)
let runs = TraceJson.runs seq (Trace.runs graph codecs total)
```

Each document wraps its body in an envelope with the schema, the kind and the read point, the seq of
the last event read. `Trace.lastSeq graph` gives it; the state document takes it from `state.Seq`:

```text
{"schema":3,"kind":"lookup","seq":64,"body":{"case":"Checkpointed","node":{"node":1,"path":"/x","stability":{"case":"Named"},"label":"x"}}}
```

Union cases are `{"case":"Name", ...fields}`, options are `null`, and maps are arrays of `[key,value]`
pairs in key order, so the same program writes the same text. On .NET, `TraceJson.readState`,
`readRuns`, `readWhy`, `readWhyNot`, `readWaiting` and `readLookup` read a document back to a
`TraceDocument` of its schema, kind, seq and body.

The state folds the checkpoint and the retained events, as `Trace.snapshot` does. After a
checkpoint, a value folded into it is `Lossy`, a cause before it ends at `BeforeCheckpoint`, and a run
before it is absent from `runs`.

`TraceRunState`, `TraceWhy` and `TraceWhyStep` repeat the labels of `TraceRun`, `Why` and `WhyStep`,
and require qualified access. An unqualified `TraceRun` or `Why` record literal keeps its type; a
literal of a contract record names it in its first label, as `{ TraceRunState.Run = 1; ... }`.

### Payloads and codecs

A payload takes one of four forms, written in documents as cases and in schema 3 dumps as a `form`:

| Form | Holds | Lossless |
| --- | --- | --- |
| `Null` | null, or an F# `None` | yes |
| `Exact` | an `int`, `string`, `bool` or `float` | yes |
| `Encoded` | the data an application codec wrote, with the codec's name and version | yes |
| `Lossy` | diagnostic text, such as `{ X = 1; Y = 2 }` | no |

A codec claims the values of one shape:

```fsharp
type Point = { X: int; Y: int }

let point: TraceCodec =
    {
        Name = "point"
        Version = 1
        Encode =
            fun value ->
                match value with
                | :? Point as p -> Some $"{p.X},{p.Y}"
                | _ -> None
        Decode =
            fun data ->
                let xy = data.Split ','
                box { X = int xy[0]; Y = int xy[1] }
    }

let codecs = TraceCodecs.ofList [ point ]
```

The first codec whose `Encode` returns `Some` encodes the value. A value claimed by none of them is
`Lossy`; under `TraceCodecs.strict codecs` it raises `TraceCodecException` instead.
`TraceCodecs.decode` returns an `Encoded` payload's value, and raises for a codec name or version the
list lacks. Under Fable, a number with an integral value in the 32-bit range is `Int`, so `2.0` is
`Int 2` there and `Float 2.0` on .NET.

`Lossy` also marks a value whose log entry is ambiguous. A lookup cell records a null value and a
suspension alike, so its null state value is `Lossy "null"`. A memo, async memo, boundary or timed
node that moved to null, and an async source settled to null, hold `Null`; a projection records no
value, and holds `None`. A checkpoint holds a null value as the text of null, so after the
checkpoint drops its `Moved` or `Settle`, the state value is `Lossy "null"`.

`Trace.dumpTextWith graph codecs` writes the log in schema 3, with each payload in its form, and
`TraceContract.parseDumpWith codecs text` decodes one back. `TraceModel.parseDump` reads schemas 1 to
3; a schema 1 or 2 payload carries no type, so `TraceDump.Payloads` gives its text as `Lossy` and a
null payload as `Null`. `parseDumpWith` keeps a schema 1 or 2 payload as its text, and a strict list
raises for it.

:::details Read a dump without its graph

`TraceContract` computes the contract values from a snapshot and the events after it. Use it to read a
dump file without its graph, with the dump's snapshot as the checkpoint:

```fsharp
let dump = TraceContract.parseDumpWith codecs (System.IO.File.ReadAllText "cart.jsonl")
let checkpoint: TraceCheckpoint = { Name = dump.Checkpoint; Snapshot = dump.Snapshot }
let state = TraceContract.state codecs checkpoint dump.Events

// The Created root with the NodeNew seq held by the dump's snapshot.
let createdRoot (id: int) (root: WhyRoot) =
    match root with
    | WhyRoot.Created 0 ->
        match dump.Snapshot.Nodes.TryFind id with
        | Some n -> WhyRoot.Created n.Created
        | None -> root
    | _ -> root

let runs =
    match TraceContract.lookup dump.Snapshot dump.Events (TraceAddress.ByPath "/subtotal") with
    | TraceLookup.Live node
    | TraceLookup.Checkpointed node
    | TraceLookup.Disposed (node, _) ->
        let history = TraceModel.history dump.Events dump.Checkpoint node.Node
        let runs = history.Runs |> List.map (fun r -> { r with Root = createdRoot node.Node r.Root })
        TraceContract.runs codecs { history with Runs = runs }
    | TraceLookup.Missing
    | TraceLookup.Foreign -> []
```

Two values read this way differ from the graph's:

- A `Lossy` payload decodes to its text, and the text encodes as `Exact (String text)`. A value that
  `Trace.state` reports as `Lossy "{ X = 1; Y = 2 }"`, and every payload of a schema 1 or 2 dump, is
  `Exact (String ...)` in `state` and `runs`. `dump.Payloads[i]` holds the form of `dump.Events[i]`,
  `Lossy` included.
- `TraceModel.history` and `TraceModel.why` end a first run at `Created 0` when its `NodeNew` is folded
  into the checkpoint. `createdRoot` above replaces that root with the snapshot node's `Created` seq,
  the root `Trace.runs` and `Trace.why` report.

`TraceContract.why codecs why` encodes a `TraceModel.why` chain the same way; map its `Root` with
`Option.map (createdRoot why.Node)` first. `TraceContract.nodeRef snapshot id` gives the `TraceNodeRef` of a node in a
snapshot, or `None`.
`TraceContract.dumpTextWith` writes a schema 3 dump from a snapshot and events.
The `TraceContract` functions are pure and run on any thread.

:::

### Schema versions

- One schema number covers dumps and documents. Documents and `dumpTextWith` write schema 3;
  `dumpText` keeps writing schema 1, or 2 with timed nodes, byte for byte.
- A reader accepts schemas 1 to 3 and raises `FormatException` for any other, for another document
  kind, and for an unknown case.
- A change that removes or renames a field, changes its meaning, or adds a field a reader must
  understand raises the schema number.
- Committed fixtures in `tests/Ranvier.Tests/fixtures/trace` pin each schema and each document kind;
  `dotnet fsi tools/trace-fixtures.fsx` rewrites them after a deliberate change.

C# callers use `Tracing.StateDocument`, `RunsDocument`, `WhyDocument`, `WhyNotDocument`,
`WaitingDocument`, `LookupDocument` and the codec builders in [C# tracing](csharp.md#tracing).

## Limits

:::warning Use tracing during development
A traced build is slower, allocates per event, and retains its log until a checkpoint drops it.
Ship against the untraced `Ranvier` package.
:::

:::details Platform, lifetime and query limits

- **Fable records and text dumps.** A Fable build records the log that [signal maps](signal-maps.md)
  draw and exposes `Trace.dumpText` and `Trace.export` with the memory sink; file-writing `Trace.dump`,
  `TraceSinks.jsonl` and `TraceModel.parseDump` are absent.
- **Development builds only.** A traced build is slower and allocates per event. Ship against the
  untraced `Ranvier` package.
- **The log retains events.** Events stay in memory until a [checkpoint](#incremental-reads-and-checkpoints)
  drops them, up to the implementation's sequence limit. Only an [export](#exporting-the-trace) with
  `Enqueued` or `Acknowledged` checkpoints drops events automatically.
- **A checkpoint retains the live graph and its recurring paths.** The checkpoint's snapshot holds the
  live nodes and owners. Beneath a live node it also holds a sibling and an incarnation count per
  distinct child path, for the node's lifetime: a host whose runs create children under ever-new labels
  grows the snapshot by one count per label.
- **A signal or async source stays until it is collected.** A checkpoint keeps it live until the garbage
  collector reclaims it; the next checkpoint then releases it and its description. A Fable build keeps
  every one for the log's lifetime.
- **Payloads retain values.** Writes, transitions and settlements can retain values or exceptions in
  event payloads. These are object references, not immutable historical snapshots: later mutation can
  affect their formatting. Retention can keep application objects alive until a checkpoint drops their events.
- **Native AOT uses the payload's `ToString`.** Traced packages support trimming and Native AOT; see
  [Installation](installation.md#native-aot-and-trimming). An application type needs an AOT-compatible
  `ToString` for useful payload text in a trimmed build.
- **Flights are async memo flights.** `waitingOn` lists the flights of an `AsyncMemo`. An
  `AsyncSource` has no flights: its settles appear as `Settle` and `Fail` events. A
  [stream memo](streams.md) records `StreamStarted`, `StreamPublished` and `StreamEnded` events instead.
- **Combinator nodes carry the site of the combinator call.** Rows and internal nodes of `filter`,
  `sortBy` and the other views share the call's file and line, told apart by `#n`, not by key.
- **Sites follow the JIT.** An inlined method can report its caller's line. `Trace.named` is the
  stable name. The repository's trace gates run with `DOTNET_TieredCompilation=0`.
- **Dumps run between flushes, on the graph thread.** `Trace.dump` and `Trace.dumpText` raise
  `InvalidOperationException` off the graph's thread or inside a flush, a discharge or a
  computation's run. Inside `batch` they succeed. The queries run at any time on the graph thread.
- **Trace locking is not graph synchronization.** The event log serializes recording, but
  `ThreadAffinity = Unchecked` does not make graph operations safe to run concurrently. The caller must
  still serialize graph access; async event order can differ between runs.
- **Labels are names, not keys.** Nodes sharing a label under one owner are told apart by creation
  order: the second takes `#1`, the third `#2`.

:::

## How the zero-cost claim is checked

:::details Verification gates for trace changes

The repository provides `tools/verify-trace.fsx` to check trace changes:

- The untraced Release IL references no trace type and equals the IL of the merge base, method by
  method. The packed DLL's public surface equals a committed baseline plus `Trace.named`,
  `Trace.label` and the timed-value members in `docs/.ai/debounce-throttle-api-additions.txt`.
- A sample using `Trace.named` or `Trace.label` compiles to the same IL as the sample without it.
- A lint checks the hook sites and `#if RANVIER_TRACE` blocks in the engine sources.
- The untraced Fable output imports nothing from the trace module.
- A sample dump is byte-identical over five runs.
- For each counter-bench scenario, the log's run, edge, observer and flush counts equal the
  library's own counters, and the edges folded from the log equal the live graph.
- With elevated rights, the counter bench shows equal allocations and instruction counts between
  the merge base and the change.

CI also runs the test suite traced, and publishes the NativeAOT smoke application traced and untraced.

:::

## Next

- [Signal maps](signal-maps.md): the log drawn live, beneath the examples in these docs.
- [Troubleshooting](troubleshooting.md): exception messages and symptoms, with their causes and fixes.
- [Getting started](getting-started.md): the graph, owners and batching the log records.
