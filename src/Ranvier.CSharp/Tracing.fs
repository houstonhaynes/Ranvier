namespace Ranvier.CSharp

open System
open System.Collections.Generic
open System.Diagnostics
open Ranvier

/// <summary>Labels and provenance queries over a graph's event log, as text.</summary>
/// <remarks>
/// <c>Named</c> and <c>Label</c> are present in every build. The queries are present only when Ranvier is built with
/// <c>RanvierTrace=true</c>; call them from code under <c>#if RANVIER_TRACE</c>.
/// </remarks>
/// <example>
/// <code lang="csharp">
/// var graph = new Graph();
/// graph.Run(() =>
/// {
///     var count = Tracing.Named("count", () => Signal(1));
///     var doubled = Tracing.Named("doubled", () => Memo(() => count.Value * 2));
///     _ = doubled.Value;
///     count.Value = 2;
///     _ = doubled.Value;
/// #if RANVIER_TRACE
///     Console.WriteLine(Tracing.Why(graph, doubled));
/// #endif
/// });
/// </code>
/// </example>
[<AbstractClass; Sealed>]
type Tracing =

    /// <summary>Runs <c>body</c>. A traced build also labels the first node or owner it creates on this thread.</summary>
    static member Named<'T>(label: string, body: Func<'T>) : 'T =
        Trace.named label body.Invoke

    /// <summary>Runs <c>body</c>. A traced build also labels the first node or owner it creates on this thread.</summary>
    static member Named(label: string, body: Action) : unit =
        Trace.named label body.Invoke

    /// <summary>Labels <c>node</c> in <c>graph</c>'s log with <c>text</c>, which may be computed.</summary>
    /// <remarks>
    /// The label replaces the node's path segment. A caller compiled without <c>RANVIER_TRACE</c> drops the call along
    /// with its argument expressions.
    /// </remarks>
    [<Conditional("RANVIER_TRACE")>]
    static member Label(graph: Graph, node: INode, text: string) : unit =
        Trace.label (graph, node, text)

#if RANVIER_TRACE
    /// <summary>A copy of the events retained in <c>graph</c>'s log, oldest first.</summary>
    /// <remarks>The events up to the checkpoint's seq are folded into <c>CheckpointOf(graph)</c>.</remarks>
    static member Events(graph: Graph) : TraceEvent[] =
        Trace.events graph

    /// <summary>A copy of the events in <c>graph</c>'s log with <c>Seq</c> greater than <c>seq</c>, oldest first.</summary>
    /// <remarks>
    /// A cursor is the seq of the last event read, 0 before the first. A consumer with an expired cursor resyncs from
    /// <c>CheckpointOf(graph)</c> and the events since its seq.
    /// </remarks>
    /// <exception cref="T:System.ArgumentOutOfRangeException">
    /// <c>seq</c> is negative, past the log's last seq, or before the checkpoint's seq.
    /// </exception>
    static member EventsSince(graph: Graph, seq: int) : TraceEvent[] =
        Trace.eventsSince graph seq

    /// <summary>
    /// Folds the events in <c>graph</c>'s log up to and including seq <c>seq</c> into the checkpoint named
    /// <c>name</c>, and drops them from the log.
    /// </summary>
    /// <remarks>
    /// The dropped events release their payloads. A <c>seq</c> equal to the checkpoint's seq renames the checkpoint.
    /// </remarks>
    /// <exception cref="T:System.ArgumentNullException"><c>name</c> is null.</exception>
    /// <exception cref="T:System.ArgumentOutOfRangeException">
    /// <c>seq</c> is before the checkpoint's seq or past the log's last seq, or is the seq of a <c>NodeNew</c> or an
    /// <c>OwnerNew</c> whose <c>Label</c> follows it.
    /// </exception>
    /// <exception cref="T:System.InvalidOperationException">
    /// Called off the graph's thread, or while a flush, a discharge or a run is in progress. A batch is allowed.
    /// </exception>
    static member Checkpoint(graph: Graph, seq: int, name: string) : unit =
        Trace.checkpoint graph seq name

    /// <summary>The checkpoint of <c>graph</c>'s log: its name, null before the first, and its folded snapshot.</summary>
    static member CheckpointOf(graph: Graph) : TraceCheckpoint =
        Trace.checkpointOf graph

    /// <summary>The creation record of <c>node</c>: its identity path, kind, <c>file:line</c> site and seq.</summary>
    /// <exception cref="T:System.ArgumentException"><c>graph</c>'s log holds no <c>NodeNew</c> for <c>node</c>.</exception>
    static member Origin(graph: Graph, node: INode) : string =
        Trace.render graph (Trace.origin graph node)

    /// <summary>The cause chain of <c>node</c>'s last run, from its <c>RunStart</c> back to the write that caused it.</summary>
    /// <exception cref="T:System.ArgumentException"><c>graph</c>'s log holds no <c>RunStart</c> for <c>node</c>.</exception>
    static member Why(graph: Graph, node: INode) : string =
        Trace.render graph (Trace.why graph node)

    /// <summary>The cause chain of run number <c>run</c>, from 1, of <c>node</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>graph</c>'s log holds no such run.</exception>
    static member Why(graph: Graph, node: INode, run: int) : string =
        Trace.render graph (Trace.whyAt graph node run)

    /// <summary>The first <c>depth</c> steps of the cause chain of <c>node</c>'s last run.</summary>
    /// <exception cref="T:System.ArgumentException"><c>graph</c>'s log holds no <c>RunStart</c> for <c>node</c>.</exception>
    static member WhyDepth(graph: Graph, node: INode, depth: int) : string =
        Trace.render graph (Trace.whyDepth graph depth node)

    /// <summary>Why <c>node</c> has not run since its last run ended, or a line saying the log shows no reason.</summary>
    static member WhyNot(graph: Graph, node: INode) : string =
        Trace.render graph (Trace.whyNot graph node)

    /// <summary>Every recorded run of <c>node</c>, one line per run, with its status, movement, cause and flush.</summary>
    static member History(graph: Graph, node: INode) : string =
        Trace.render graph (Trace.history graph node)

    /// <summary>The pending sources read by <c>node</c>'s last run, and the node's flights with their results.</summary>
    static member WaitingOn(graph: Graph, node: INode) : string =
        Trace.render graph (Trace.waitingOn graph node)

    /// <summary>The live nodes and owners folded from every event in <c>graph</c>'s log.</summary>
    static member Snapshot(graph: Graph) : string =
        Trace.render graph (Trace.snapshot graph)

    /// <summary>The live nodes and owners folded from the events in <c>graph</c>'s log up to and including <c>seq</c>.</summary>
    static member Snapshot(graph: Graph, seq: int) : string =
        Trace.render graph (Trace.snapshotAt graph seq)

    /// <summary>
    /// The node id at identity path <c>path</c>: the <c>@k</c>-th holder, else the live holder, else the latest; null for an
    /// unknown path.
    /// </summary>
    static member Resolve(graph: Graph, path: string) : Nullable<int> =
        match Trace.resolve graph path with
        | Some id -> Nullable id
        | None -> Nullable ()

    /// <summary>The node at identity path <c>path</c>, resolved as for <c>Resolve</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>path</c> is unknown to <c>graph</c>'s log.</exception>
    static member private At(graph: Graph, path: string) : INode =
        match Trace.resolve graph path with
        | Some id ->
            { new INode with
                member _.Id = id
                member _.Status = Status.None
            }
        | None -> raise (ArgumentException ($"The graph's trace log holds no node at path '{path}'.", nameof path))

    /// <summary><c>Origin</c> of the node at identity path <c>path</c>, such as a node labelled by <c>Named</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>path</c> is unknown, or the log holds no <c>NodeNew</c> for its node.</exception>
    static member Origin(graph: Graph, path: string) : string =
        Tracing.Origin (graph, Tracing.At (graph, path))

    /// <summary><c>Why</c> of the node at identity path <c>path</c>, such as an <c>EffectOn</c> labelled by <c>Named</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>path</c> is unknown, or the log holds no <c>RunStart</c> for its node.</exception>
    static member Why(graph: Graph, path: string) : string =
        Tracing.Why (graph, Tracing.At (graph, path))

    /// <summary>The cause chain of run number <c>run</c>, from 1, of the node at identity path <c>path</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>path</c> is unknown, or the log holds no such run.</exception>
    static member Why(graph: Graph, path: string, run: int) : string =
        Tracing.Why (graph, Tracing.At (graph, path), run)

    /// <summary><c>WhyDepth</c> of the node at identity path <c>path</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>path</c> is unknown, or the log holds no <c>RunStart</c> for its node.</exception>
    static member WhyDepth(graph: Graph, path: string, depth: int) : string =
        Tracing.WhyDepth (graph, Tracing.At (graph, path), depth)

    /// <summary><c>WhyNot</c> of the node at identity path <c>path</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>path</c> is unknown to <c>graph</c>'s log.</exception>
    static member WhyNot(graph: Graph, path: string) : string =
        Tracing.WhyNot (graph, Tracing.At (graph, path))

    /// <summary><c>History</c> of the node at identity path <c>path</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>path</c> is unknown to <c>graph</c>'s log.</exception>
    static member History(graph: Graph, path: string) : string =
        Tracing.History (graph, Tracing.At (graph, path))

    /// <summary><c>WaitingOn</c> of the node at identity path <c>path</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>path</c> is unknown to <c>graph</c>'s log.</exception>
    static member WaitingOn(graph: Graph, path: string) : string =
        Tracing.WaitingOn (graph, Tracing.At (graph, path))

    /// <summary>
    /// The differences between <c>graph</c>'s live source lists and observer sets and those folded from its log, one
    /// line per node; empty when they match.
    /// </summary>
    static member Reconcile(graph: Graph) : IReadOnlyList<string> =
        Trace.reconcile graph |> List.toArray :> IReadOnlyList<string>

    /// <summary>The JSONL dump of <c>graph</c>'s log, schema 1.</summary>
    /// <exception cref="T:System.InvalidOperationException">
    /// Called off the graph's thread, or while a flush, a discharge or a run is in progress. A <c>Batch</c> is allowed.
    /// </exception>
    static member DumpText(graph: Graph) : string =
        Trace.dumpText graph

    /// <summary>Writes the JSONL dump of <c>graph</c>'s log, schema 1, to <c>path</c>, and returns the full path.</summary>
    /// <exception cref="T:System.InvalidOperationException">As for <c>DumpText</c>.</exception>
    static member Dump(graph: Graph, path: string) : string =
        Trace.dump graph path

    /// <summary>The JSONL dump of <c>graph</c>'s log in schema 3, with each payload encoded by <c>codecs</c>.</summary>
    /// <exception cref="T:System.InvalidOperationException">As for <c>DumpText</c>.</exception>
    /// <exception cref="T:Ranvier.TraceCodecException"><c>codecs</c> is strict and no codec claims a payload.</exception>
    static member DumpText(graph: Graph, codecs: TraceCodecs) : string =
        Trace.dumpTextWith graph codecs

    /// <summary>
    /// An application codec named <c>name</c> at <c>version</c>. <c>encode</c> returns a value's data, or null for a
    /// value of another shape; <c>decode</c> returns the value of data written by the same version.
    /// </summary>
    static member Codec(name: string, version: int, encode: Func<obj, string>, decode: Func<string, obj>) : TraceCodec =
        {
            Name = name
            Version = version
            Encode = fun value -> Option.ofObj (encode.Invoke value)
            Decode = decode.Invoke
        }

    /// <summary>The codec list of <c>codecs</c>, tried in order. A value claimed by none of them encodes as <c>Lossy</c>.</summary>
    static member Codecs([<ParamArray>] codecs: TraceCodec[]) : TraceCodecs =
        TraceCodecs.ofList (List.ofArray codecs)

    /// <summary>
    /// The codec list of <c>codecs</c>, tried in order. A value claimed by none of them raises
    /// <c>TraceCodecException</c>.
    /// </summary>
    static member StrictCodecs([<ParamArray>] codecs: TraceCodec[]) : TraceCodecs =
        TraceCodecs.strict (Tracing.Codecs codecs)

    /// <summary>The seq of the last event recorded in <c>graph</c>'s log, which is the read point of each document.</summary>
    static member LastSeq(graph: Graph) : int =
        Trace.lastSeq graph

    /// <summary>
    /// The schema 3 <c>state</c> document of <c>graph</c>: every node with its ref, status and value, the owners and
    /// the edges, folded from the checkpoint and the retained events.
    /// </summary>
    /// <remarks>A retained value is encoded by <c>codecs</c>; a value folded into the checkpoint is <c>Lossy</c>.</remarks>
    /// <exception cref="T:Ranvier.TraceCodecException"><c>codecs</c> is strict and no codec claims a retained value.</exception>
    static member StateDocument(graph: Graph, codecs: TraceCodecs) : string =
        TraceJson.state (Trace.state graph codecs)

    /// <summary>The schema 3 <c>runs</c> document of <c>node</c>'s retained runs, oldest first.</summary>
    /// <exception cref="T:Ranvier.TraceCodecException"><c>codecs</c> is strict and no codec claims a run's value.</exception>
    static member RunsDocument(graph: Graph, codecs: TraceCodecs, node: INode) : string =
        TraceJson.runs (Trace.lastSeq graph) (Trace.runs graph codecs node)

    /// <summary>The schema 3 <c>why</c> document of the cause chain of <c>node</c>'s last run.</summary>
    /// <exception cref="T:System.ArgumentException"><c>graph</c>'s log holds no <c>RunStart</c> for <c>node</c>.</exception>
    /// <exception cref="T:Ranvier.TraceCodecException"><c>codecs</c> is strict and no codec claims a step's value.</exception>
    static member WhyDocument(graph: Graph, codecs: TraceCodecs, node: INode) : string =
        TraceJson.why (Trace.lastSeq graph) (Trace.whyWith graph codecs node)

    /// <summary>The schema 3 <c>whyNot</c> document of <c>node</c>: its reason, or a null body when the log shows none.</summary>
    static member WhyNotDocument(graph: Graph, node: INode) : string =
        TraceJson.whyNot (Trace.lastSeq graph) (Trace.whyNot graph node)

    /// <summary>The schema 3 <c>waiting</c> document of <c>node</c>: the pending sources and flights of its last run.</summary>
    static member WaitingDocument(graph: Graph, node: INode) : string =
        TraceJson.waiting (Trace.lastSeq graph) (Trace.waitingOn graph node)

    /// <summary>The schema 3 <c>lookup</c> document of <c>node</c>, <c>Foreign</c> for a node of another graph.</summary>
    static member LookupDocument(graph: Graph, node: INode) : string =
        TraceJson.lookup (Trace.lastSeq graph) (Trace.lookupNode graph node)

    /// <summary>The schema 3 <c>lookup</c> document of the node at identity path <c>path</c>.</summary>
    static member LookupDocument(graph: Graph, path: string) : string =
        TraceJson.lookup (Trace.lastSeq graph) (Trace.lookup graph (TraceAddress.ByPath path))

    /// <summary><c>RunsDocument</c> of the node at identity path <c>path</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>path</c> is unknown to <c>graph</c>'s log.</exception>
    static member RunsDocument(graph: Graph, codecs: TraceCodecs, path: string) : string =
        Tracing.RunsDocument (graph, codecs, Tracing.At (graph, path))

    /// <summary><c>WhyDocument</c> of the node at identity path <c>path</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>path</c> is unknown, or the log holds no <c>RunStart</c> for its node.</exception>
    static member WhyDocument(graph: Graph, codecs: TraceCodecs, path: string) : string =
        Tracing.WhyDocument (graph, codecs, Tracing.At (graph, path))

    /// <summary><c>WhyNotDocument</c> of the node at identity path <c>path</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>path</c> is unknown to <c>graph</c>'s log.</exception>
    static member WhyNotDocument(graph: Graph, path: string) : string =
        Tracing.WhyNotDocument (graph, Tracing.At (graph, path))

    /// <summary><c>WaitingDocument</c> of the node at identity path <c>path</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>path</c> is unknown to <c>graph</c>'s log.</exception>
    static member WaitingDocument(graph: Graph, path: string) : string =
        Tracing.WaitingDocument (graph, Tracing.At (graph, path))
#endif
