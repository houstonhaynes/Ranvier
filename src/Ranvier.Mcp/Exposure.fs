namespace Ranvier.Mcp

open System
open System.Collections.Generic
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open Ranvier
open Ranvier.Elmish

/// <summary>An immutable published state: the exposed values, encoded, at one version.</summary>
/// <remarks>Every value in a snapshot comes from the same flush. Version 0 is the empty state before the first publication.</remarks>
type Snapshot =
    {
        Version: int64
        Values: Map<string, JsonElement>
    }

/// <summary>A new snapshot, and the names of the values whose encoding differs from the previous snapshot.</summary>
type Publication =
    {
        Snapshot: Snapshot
        /// <summary>In registration order.</summary>
        Changed: string list
    }

/// <summary>Decodes a message's JSON arguments; <c>Error</c> rejects the message with a reason.</summary>
type MessageDecoder<'Msg> = JsonElement -> Result<'Msg, string>

/// <summary>An exposed message: the action that decodes arguments and, on success, dispatches the message.</summary>
type internal ExposedMessage = JsonElement -> Result<unit, string>

/// <summary>An exposed value: its name and the reader that encodes its current value.</summary>
type internal ExposedValue =
    { Name: string; Read: unit -> JsonNode }

/// <summary>
/// The graph state an adapter may show: named values registered before <c>Start</c>, published as versioned snapshots.
/// </summary>
/// <remarks>
/// <para>
/// <c>Start</c> creates one effect, under the current owner, that reads every exposed value. A flush that changes any
/// encoding publishes one snapshot with a version one higher; a re-run whose encodings all equal the previous snapshot's
/// publishes nothing, as does a memo cutoff, which stops the effect re-running at all. A batch produces one publication.
/// </para>
/// <para>
/// <c>Snapshot</c> is safe to read from any thread and never enters the graph. Registration and <c>Start</c> run on the
/// graph thread. A reader that raises fails the publication effect, and the snapshot keeps its previous value.
/// </para>
/// </remarks>
[<Sealed>]
type Exposure() =
    static let validName = Regex ("^[A-Za-z0-9_.~-]+$", RegexOptions.CultureInvariant)

    static let nullElement = JsonDocument.Parse("null").RootElement.Clone()

    static let encodeText (node: JsonNode) =
        match node with
        | null -> "null"
        | node -> node.ToJsonString ()

    static let parse (text: string) =
        if text = "null" then
            nullElement
        else
            use document = JsonDocument.Parse text
            document.RootElement.Clone ()

    let entries = ResizeArray<ExposedValue>()
    let names = HashSet<string>(StringComparer.Ordinal)
    let messages = Dictionary<string, ExposedMessage>(StringComparer.Ordinal)
    let messageNames = ResizeArray<string>()
    let waiters = ResizeArray<struct (int64 * TaskCompletionSource<Snapshot>)>()
    let mutable graph = Unchecked.defaultof<Graph>
    let nodes = ResizeArray<KeyValuePair<string, INode>>()
    let published = Event<Publication>()
    let mutable started = false
    let mutable snapshot = { Version = 0L; Values = Map.empty }
    let mutable lastTexts: string array = null

    let checkName (name: string) =
        if started then
            invalidOp $"Exposure has started; '{name}' must be exposed before Start."

        if isNull name || not (validName.IsMatch name) then
            invalidArg (nameof name) $"'{name}' is not an exposed name: use [A-Za-z0-9_.~-]+."

    let release (current: Snapshot) =
        let reached =
            lock waiters (fun () ->
                let reached =
                    waiters
                    |> Seq.filter (fun struct (version, _) -> version <= current.Version)
                    |> Array.ofSeq

                waiters.RemoveAll (fun struct (version, _) -> version <= current.Version)
                |> ignore

                reached)

        for struct (_, waiter) in reached do
            waiter.TrySetResult current |> ignore


    let publish () =
        let texts =
            entries
            |> Seq.map (fun entry -> encodeText (entry.Read ()))
            |> Array.ofSeq

        let changed =
            [
                for index in 0 .. texts.Length - 1 do
                    if
                        isNull lastTexts
                        || lastTexts[index] <> texts[index]
                    then
                        entries[index].Name
            ]

        if not changed.IsEmpty then
            let previous = snapshot

            let values =
                (previous.Values, Seq.indexed entries)
                ||> Seq.fold (fun values (index, entry) ->
                    if
                        isNull lastTexts
                        || lastTexts[index] <> texts[index]
                    then
                        Map.add entry.Name (parse texts[index]) values
                    else
                        values)

            let next =
                {
                    Version = previous.Version + 1L
                    Values = values
                }

            lastTexts <- texts
            Volatile.Write (&snapshot, next)
            release next
            published.Trigger { Snapshot = next; Changed = changed }

    /// <summary>The latest published snapshot. Readable from any thread.</summary>
    member _.Snapshot = Volatile.Read &snapshot

    /// <summary>The exposed value names, in registration order.</summary>
    member _.Names = entries |> Seq.map _.Name |> List.ofSeq

    /// <summary>
    /// Raised on the graph thread, inside the publishing flush, after <c>Snapshot</c> holds the new snapshot.
    /// </summary>
    member _.Published = published.Publish

    /// <summary>Exposes <c>read</c>'s value under <c>name</c>, encoded by <c>encode</c>.</summary>
    /// <exception cref="T:System.InvalidOperationException">Called after <c>Start</c>.</exception>
    /// <exception cref="T:System.ArgumentException">The name is taken, or holds a character outside <c>[A-Za-z0-9_.~-]</c>.</exception>
    member _.Value(name: string, encode: 'T -> JsonNode, read: unit -> 'T) =
        checkName name

        if not (names.Add name) then
            invalidArg (nameof name) $"'{name}' is already exposed."

        entries.Add
            {
                Name = name
                Read = fun () -> encode (read ())
            }

    /// <summary>The exposed message names, in registration order.</summary>
    member _.MessageNames = List.ofSeq messageNames

    /// <summary>The nodes a provenance tool's <c>node</c> argument can name, in registration order.</summary>
    member _.Nodes = List.ofSeq nodes

    /// <summary>
    /// Names <c>node</c> as <c>name</c> for the <c>node</c> argument of a traced build's provenance tools. The registration
    /// is a name, not an access limit: the traced tools read the whole graph's trace log. An untraced build serves no
    /// provenance tools, and the registration has no effect there.
    /// </summary>
    /// <exception cref="T:System.InvalidOperationException">Called after <c>Start</c>.</exception>
    /// <exception cref="T:System.ArgumentException">
    /// The node name is taken, or holds a character outside <c>[A-Za-z0-9_.~-]</c>.
    /// </exception>
    member _.Node(name: string, node: INode) =
        checkName name

        if
            nodes
            |> Seq.exists (fun (KeyValue (taken, _)) -> taken = name)
        then
            invalidArg (nameof name) $"The node '{name}' is already exposed."

        nodes.Add (KeyValuePair (name, node))

    /// <summary>
    /// Runs <c>work</c> with the graph captured at <c>Start</c>, through <c>Graph.DispatchAsync</c>. Callable from any
    /// thread after <c>Start</c>.
    /// </summary>
    member internal _.Enter(work: Graph -> 'T) : Task<'T> =
        if not started then
            invalidOp "Exposure has not started."

        graph.DispatchAsync (Func<'T>(fun () -> work graph))

    /// <summary>
    /// Authorises each decoder's message for <c>Dispatch</c> under its name: a dispatch decodes the arguments and, on
    /// <c>Ok</c>, dispatches the message to <c>mvu</c>.
    /// </summary>
    /// <exception cref="T:System.InvalidOperationException">Called after <c>Start</c>.</exception>
    /// <exception cref="T:System.ArgumentException">
    /// A message name is taken, or holds a character outside <c>[A-Za-z0-9_.~-]</c>.
    /// </exception>
    member _.Messages(mvu: Mvu<'Model, 'Msg>, decoders: seq<string * MessageDecoder<'Msg>>) =
        for name, decode in decoders do
            checkName name

            if messages.ContainsKey name then
                invalidArg (nameof decoders) $"The message '{name}' is already exposed."

            messages[name] <-
                fun args ->
                    let decoded =
                        try
                            decode args
                        with ex ->
                            Result.Error $"The arguments of '{name}' are malformed: {ex.Message}"

                    decoded |> Result.map mvu.Dispatch

            messageNames.Add name

    /// <summary>
    /// Decodes <c>args</c> with the decoder exposed under <c>name</c> and dispatches the message, on the graph thread.
    /// The task holds the snapshot published once the dispatch and its effects have run, or the reason the message was
    /// rejected. A rejected message dispatches nothing. Callable from any thread after <c>Start</c>.
    /// </summary>
    /// <remarks>
    /// A decoder that raises rejects the message. The task faults when <c>update</c> raises, and with
    /// <c>ObjectDisposedException</c> once the graph is disposed.
    /// </remarks>
    /// <exception cref="T:System.InvalidOperationException">Called before <c>Start</c>.</exception>
    member this.Dispatch(name: string, args: JsonElement) : Task<Result<Snapshot, string>> =
        if not started then
            invalidOp "Exposure has not started."

        match messages.TryGetValue name with
        | false, _ ->
            let known = String.Join (", ", messageNames)
            Task.FromResult (Result.Error $"Unknown message '{name}'. Exposed messages: {known}.")
        | true, apply ->
            task {
                let! outcome = graph.DispatchAsync (Func<Result<unit, string>>(fun () -> apply args))
                return outcome |> Result.map (fun () -> this.Snapshot)
            }

    /// <summary>
    /// A task holding the first published snapshot whose version is at least <c>version</c>, or <c>None</c> once
    /// <c>timeout</c> elapses first. Callable from any thread; it does not enter the graph.
    /// </summary>
    member this.WaitVersion(version: int64, timeout: TimeSpan) : Task<Snapshot option> =
        let waiter =
            TaskCompletionSource<Snapshot>(TaskCreationOptions.RunContinuationsAsynchronously)

        lock waiters (fun () -> waiters.Add (struct (version, waiter)))
        release this.Snapshot

        task {
            let! first = Task.WhenAny (waiter.Task, Task.Delay timeout)

            if obj.ReferenceEquals (first, waiter.Task) then
                return Some waiter.Task.Result
            else
                lock waiters (fun () ->
                    waiters.RemoveAll (fun struct (_, candidate) -> obj.ReferenceEquals (candidate, waiter))
                    |> ignore)

                return None
        }

    /// <summary>
    /// Closes registration and creates the publication effect under the current owner of the ambient graph, which
    /// publishes the first snapshot.
    /// </summary>
    /// <exception cref="T:System.InvalidOperationException">Called twice, or with no ambient graph.</exception>
    member _.Start() =
        if started then
            invalidOp "Exposure has already started."

        graph <- Graph.Current
        started <- true
        createEffect publish

/// <summary>Registration functions over an <c>Exposure</c>, for use in a host's setup.</summary>
[<RequireQualifiedAccess>]
module Expose =
    /// <summary>Exposes <c>read</c>'s value under <c>name</c>, encoded by <c>encode</c>.</summary>
    let value (name: string) (encode: 'T -> JsonNode) (read: unit -> 'T) (exposure: Exposure) =
        exposure.Value (name, encode, read)

    /// <summary>Authorises each decoder's message, under its name, for dispatch to <c>mvu</c>.</summary>
    let messages (mvu: Mvu<'Model, 'Msg>) (decoders: (string * MessageDecoder<'Msg>) list) (exposure: Exposure) =
        exposure.Messages (mvu, decoders)

    /// <summary>Names <c>node</c> as <c>name</c> for the <c>node</c> argument of a traced build's provenance tools.</summary>
    let node (name: string) (node: INode) (exposure: Exposure) =
        exposure.Node (name, node)
