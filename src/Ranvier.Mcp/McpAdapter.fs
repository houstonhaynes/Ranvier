namespace Ranvier.Mcp

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open ModelContextProtocol
open ModelContextProtocol.Protocol
open ModelContextProtocol.Server
open Ranvier

/// <summary>A tool the adapter lists and runs.</summary>
type internal AdapterTool =
    {
        Tool: Tool
        Run: CallToolRequestParams -> CancellationToken -> Task<CallToolResult>
    }

/// <summary>The URIs and JSON documents of the adapter's resources.</summary>
[<RequireQualifiedAccess>]
module internal Resources =
    [<Literal>]
    let State = "ranvier://state"

    let valueUri (name: string) =
        $"{State}/{name}"

    let private values (snapshot: Snapshot) =
        let values = JsonObject ()

        for KeyValue (name, value) in snapshot.Values do
            values[name] <- JsonNode.Parse (value.GetRawText ())

        values

    /// <summary>The whole snapshot: <c>{"version": n, "values": {...}}</c>.</summary>
    let state (snapshot: Snapshot) =
        let document = JsonObject ()
        document["version"] <- JsonValue.Create snapshot.Version
        document["values"] <- values snapshot
        document.ToJsonString ()

    /// <summary>One value: <c>{"version": n, "value": ...}</c>.</summary>
    let value (snapshot: Snapshot) (name: string) =
        let document = JsonObject ()
        document["version"] <- JsonValue.Create snapshot.Version
        document["value"] <- JsonNode.Parse (snapshot.Values[name].GetRawText())
        document.ToJsonString ()

/// <summary>Options of an <c>McpAdapter</c>.</summary>
type McpAdapterOptions =
    {
        /// <summary>
        /// In a traced build, also lists <c>trace_state</c>, <c>trace_lookup</c>, <c>why</c>, <c>why_not</c> and
        /// <c>waiting_on</c>, which return <c>TraceJson</c> documents. <c>trace_state</c> grows with the graph, without
        /// bound.
        /// </summary>
        RawTraceTools: bool
        /// <summary>The directory whose F# and C# files give the text of a node's creation line.</summary>
        SourceRoot: string
        /// <summary>
        /// Marks <c>dispatch</c> and <c>explain</c> with <c>_meta["anthropic/alwaysLoad"]</c>, so Claude Code loads them
        /// without a tool search. The tools then join the head of the prompt: a session starts with a cold prompt cache
        /// unless a session with the same tools ran within the cache lifetime.
        /// </summary>
        AlwaysLoad: bool
    }

    /// <summary>
    /// No raw trace tools, no always-loaded tools, and as the source root the nearest directory holding <c>.git</c> at or above the current
    /// directory, else the current directory.
    /// </summary>
    static member Default =
        let start = Environment.CurrentDirectory

        let rec repository (directory: IO.DirectoryInfo) =
            match directory with
            | null -> start
            | directory when IO.Path.Exists (IO.Path.Combine (directory.FullName, ".git")) -> directory.FullName
            | directory -> repository directory.Parent

        {
            RawTraceTools = false
            SourceRoot = repository (IO.DirectoryInfo start)
            AlwaysLoad = false
        }

/// <summary>
/// An MCP server surface over an <c>Exposure</c>: resources for the published state, subscriptions with
/// <c>notifications/resources/updated</c>, and the <c>snapshot</c>, <c>dispatch</c> and <c>wait_version</c> tools.
/// </summary>
/// <remarks>
/// <para>
/// Resources: <c>ranvier://state</c> holds the whole snapshot, and <c>ranvier://state/{name}</c> one exposed value with
/// the snapshot's version. Reads come from the published snapshot and never enter the graph.
/// </para>
/// <para>
/// <c>dispatch</c> (listed when the exposure has messages) takes <c>{"message": name, "args": json}</c> or
/// <c>{"messages": [{"message": name, "args": json}, ...]}</c> and runs <c>Exposure.Dispatch</c> for each message in
/// order. Its text lists, per message, the exposed values whose encoding changed, then every exposed value at the final
/// version, as text alone. An unknown message, a decoder <c>Error</c>, a decoder that
/// raises, or a missing <c>message</c> is a tool error naming the message's position: the messages before it stay
/// dispatched, and it and those after it are not. <c>wait_version</c> (<c>{"version": n, "timeoutMs": ms}</c>) returns
/// the first snapshot at or past <c>n</c>, or a tool error on timeout.
/// </para>
/// <para>
/// A traced build (<c>RANVIER_TRACE</c>) also lists <c>explain</c>, <c>graph</c> and <c>node</c>, which answer in
/// compact text from the whole graph's trace log, on the graph thread. A node reads as <c>name#id</c>, named by
/// <c>Expose.node</c>, a trace label, or the <c>let</c> binding at its creation site, with the site's <c>file:line</c>
/// and, when the source root holds the file, the line's text. <c>explain {node, messages}</c> dispatches the messages, then answers why a node holds its value;
/// <c>graph</c> is an overview of at most 2,000 characters; <c>node {node, around, detail, limit, cursor}</c> lists a
/// node's neighbourhood a page at a time, each page at most 4,000 characters. With
/// <c>McpAdapterOptions.RawTraceTools</c> it lists the <c>TraceJson</c> tools too. An untraced build lists none of them.
/// </para>
/// <para>
/// Each publication notifies the subscribers of <c>ranvier://state</c> and of each value whose encoding changed, in
/// publication order. A memo cutoff, an equal write, or a re-run that re-encodes every value identically sends nothing;
/// a batch sends one notification per changed resource.
/// </para>
/// <para>
/// Construct the adapter on the graph thread, after <c>Exposure.Start</c>. <c>Options</c> is safe to use from any
/// thread, and several servers may share it; each server's subscriptions are its own.
/// </para>
/// </remarks>
[<Sealed>]
type McpAdapter(exposure: Exposure, options: McpAdapterOptions) =
    let names = exposure.Names

    let known =
        Set.ofList (
            Resources.State
            :: List.map Resources.valueUri names
        )

    let subscriptions =
        ConcurrentDictionary<McpServer, ConcurrentDictionary<string, byte>>(HashIdentity.Reference)

    let outbox =
        Channel.CreateUnbounded<struct (McpServer * string)>(UnboundedChannelOptions (SingleReader = true))

    // On the pool: a loop started here would capture the graph thread's context.
    let sender =
        Task.Run<unit>(fun () ->
            task {
                let reader = outbox.Reader
                let mutable reading = true

                while reading do
                    let! more = reader.WaitToReadAsync ()

                    if not more then
                        reading <- false
                    else
                        let mutable item = Unchecked.defaultof<_>

                        while reader.TryRead &item do
                            let struct (server, uri) = item

                            try
                                do!
                                    server.SendNotificationAsync (
                                        NotificationMethods.ResourceUpdatedNotification,
                                        ResourceUpdatedNotificationParams (Uri = uri)
                                    )
                            with _ ->
                                // The session has ended: its subscriptions go with it.
                                subscriptions.TryRemove server |> ignore
            })

    let notify (publication: Publication) =
        let uris =
            Resources.State
            :: (publication.Changed |> List.map Resources.valueUri)

        for KeyValue (server, subscribed) in subscriptions do
            for uri in uris do
                if subscribed.ContainsKey uri then
                    outbox.Writer.TryWrite (struct (server, uri))
                    |> ignore

    let subscription = exposure.Published.Subscribe notify

    let notFound (uri: string) =
        McpProtocolException ($"Unknown resource '{uri}'.", McpErrorCode.ResourceNotFound)

    let json (text: string) =
        use document = JsonDocument.Parse text
        document.RootElement.Clone ()

    let emptySchema = json """{"type":"object","properties":{}}"""

    let nullElement = json "null"

    let blocks (text: string) =
        ResizeArray<ContentBlock> [ TextContentBlock (Text = text) :> ContentBlock ]

    /// A result holding a JSON document as text and as structured content.
    let success (text: string) =
        CallToolResult (Content = blocks text, StructuredContent = Nullable (json text))

    /// A result holding text alone.
    let plain (text: string) =
        CallToolResult (Content = blocks text)

    /// A tool error: the client sees the reason.
    let failure (reason: string) =
        CallToolResult (Content = blocks reason, IsError = Nullable true)

    let clip (limit: int) (text: string) =
        if text.Length <= limit then
            text
        else
            text.Substring (0, limit - 1) + "…"

    let alwaysLoad () =
        if options.AlwaysLoad then
            JsonObject (
                [
                    KeyValuePair<string, JsonNode>("anthropic/alwaysLoad", JsonValue.Create true)
                ]
            )
        else
            null

    let argument (request: CallToolRequestParams) (name: string) =
        match request.Arguments with
        | null -> None
        | arguments ->
            match arguments.TryGetValue name with
            | true, value -> Some value
            | _ -> None

    let snapshotTool =
        {
            Tool =
                Tool (
                    Name = "snapshot",
                    Description = "The latest published state: {\"version\": n, \"values\": {name: value}}.",
                    InputSchema = emptySchema
                )
            Run = fun _ _ -> Task.FromResult (success (Resources.state exposure.Snapshot))
        }

    let dispatchSchema =
        let messages = JsonArray ()

        for name in exposure.MessageNames do
            messages.Add (JsonValue.Create name)

        let schema = JsonObject ()
        schema["type"] <- JsonValue.Create "object"

        schema["properties"] <-
            JsonObject (
                [
                    KeyValuePair<string, JsonNode>(
                        "message",
                        JsonObject (
                            [
                                KeyValuePair<string, JsonNode>("type", JsonValue.Create "string")
                                KeyValuePair<string, JsonNode>("enum", messages)
                            ]
                        )
                    )
                    KeyValuePair<string, JsonNode>(
                        "args",
                        JsonObject (
                            [
                                KeyValuePair<string, JsonNode>("description", JsonValue.Create "The message's arguments.")
                            ]
                        )
                    )
                ]
            )

        let step = JsonObject ()
        step["type"] <- JsonValue.Create "object"

        step["properties"] <-
            JsonObject (
                [
                    KeyValuePair<string, JsonNode>("message", JsonObject ([ KeyValuePair<string, JsonNode>("enum", messages.DeepClone ()) ]))
                    KeyValuePair<string, JsonNode>("args", JsonObject ())
                ]
            )

        step["required"] <- JsonArray (JsonValue.Create "message")

        schema["properties"].AsObject()["messages"] <-
            JsonObject (
                [
                    KeyValuePair<string, JsonNode>("type", JsonValue.Create "array")
                    KeyValuePair<string, JsonNode>("description", JsonValue.Create "Messages to dispatch in order.")
                    KeyValuePair<string, JsonNode>("items", step)
                ]
            )

        // Either 'message' or 'messages': the run checks which.
        schema.Remove "required" |> ignore
        json (schema.ToJsonString ())

    /// The described messages' argument shapes, as a sentence of the dispatch description.
    let shapesText =
        let described =
            exposure.MessageNames
            |> List.choose (fun name ->
                match exposure.Shapes.TryGetValue name with
                | true, shape -> Some $"{name} {shape}"
                | _ -> None)

        if described.IsEmpty then
            ""
        else
            " Args: " + String.Join ("; ", described) + "."

    let render (value: JsonElement) =
        clip 80 (value.GetRawText ())

    /// Every exposed value at the snapshot's version, on one line.
    let stateLine (snapshot: Snapshot) =
        let values =
            names
            |> List.map (fun name ->
                match snapshot.Values.TryFind name with
                | Some value -> $"{name}={render value}"
                | None -> $"{name}=?")

        $"v{snapshot.Version} "
        + String.Join (" ", values)

    /// The exposed values whose encoding differs between the snapshots, as `name old→new`.
    let changes (before: Snapshot) (after: Snapshot) =
        let changed =
            names
            |> List.choose (fun name ->
                let was = before.Values.TryFind name

                match after.Values.TryFind name with
                | Some now when
                    (was |> Option.map _.GetRawText())
                    <> Some (now.GetRawText ())
                    ->
                    let old =
                        was
                        |> Option.map render
                        |> Option.defaultValue "∅"

                    Some $"{name} {old}→{render now}"
                | _ -> None)

        if changed.IsEmpty then
            "no exposed value changed"
        else
            String.Join ("; ", changed)

    /// The messages a dispatch request names, with their arguments.
    let messagesOf (request: CallToolRequestParams) =
        let usage =
            "dispatch takes a string 'message' with its 'args', or 'messages': [{\"message\": name, \"args\": json}, ...]."

        match argument request "messages", argument request "message" with
        | Some list, None when list.ValueKind = JsonValueKind.Array ->
            let items = List.ofSeq (list.EnumerateArray ())

            let parsed =
                items
                |> List.map (fun item ->
                    if item.ValueKind <> JsonValueKind.Object then
                        None
                    else
                        match item.TryGetProperty "message" with
                        | true, message when message.ValueKind = JsonValueKind.String ->
                            let args =
                                match item.TryGetProperty "args" with
                                | true, args -> args.Clone ()
                                | _ -> nullElement

                            Some (message.GetString (), args)
                        | _ -> None)

            if items.IsEmpty || List.contains None parsed then
                Result.Error usage
            else
                Result.Ok (List.choose id parsed)
        | None, Some message when message.ValueKind = JsonValueKind.String ->
            let args =
                argument request "args"
                |> Option.defaultValue nullElement

            Result.Ok [ message.GetString (), args ]
        | _ -> Result.Error usage

    /// Dispatches the messages in order up to the first rejection: each step's changed values and the final state, or
    /// the rejection with the steps before it.
    let dispatchAll (messages: (string * JsonElement) list) =
        task {
            let steps = ResizeArray<string>()
            let mutable before = exposure.Snapshot
            let mutable rejected = None
            let mutable remaining = messages

            while rejected.IsNone && not remaining.IsEmpty do
                let name, args = remaining.Head
                remaining <- remaining.Tail
                let position = steps.Count + 1

                let! outcome =
                    task {
                        try
                            return! exposure.Dispatch (name, args)
                        with ex ->
                            return Result.Error $"The dispatch failed: {ex.Message}"
                    }

                match outcome with
                | Result.Ok after ->
                    steps.Add (clip 200 $"{position} {name} {clip 60 (args.GetRawText ())} → v{after.Version}: {changes before after}")
                    before <- after
                | Result.Error reason -> rejected <- Some (position, name, reason)

            match rejected with
            | None ->
                return
                    Result.Ok (
                        String.Join ("\n", steps)
                        + "\n"
                        + stateLine before
                    )
            | Some (position, name, reason) ->
                let applied =
                    if steps.Count = 0 then
                        ""
                    else
                        $" Messages 1–{steps.Count} were dispatched:\n"
                        + String.Join ("\n", steps)
                        + "\n"
                        + stateLine before

                let prefix =
                    if messages.Length = 1 then
                        ""
                    else
                        $"Message {position} ({name}) was rejected: "

                return Result.Error (prefix + reason + applied)
        }

    // Text alone: a client that shows structured content in place of the text would lose the step lines.
    let dispatchTool =
        {
            Tool =
                Tool (
                    Name = "dispatch",
                    Description =
                        "Dispatches messages in order ('message' with 'args', or 'messages'): each one's changed exposed values, then the final state. A rejection names its position; earlier ones stay dispatched."
                        + shapesText,
                    InputSchema = dispatchSchema,
                    Meta = alwaysLoad ()
                )
            Run =
                fun request _ ->
                    task {
                        match messagesOf request with
                        | Result.Error usage -> return failure usage
                        | Result.Ok messages ->
                            match! dispatchAll messages with
                            | Result.Ok text -> return plain text
                            | Result.Error text -> return failure text
                    }
        }

    let maximumWait = 60000

    let waitVersionTool =
        {
            Tool =
                Tool (
                    Name = "wait_version",
                    Description = $"Waits for the snapshot at 'version' or later; fails after 'timeoutMs' (default 5000, at most {maximumWait}).",
                    InputSchema =
                        json
                            """{"type":"object","properties":{"version":{"type":"integer"},"timeoutMs":{"type":"integer","minimum":0}},"required":["version"]}"""
                )
            Run =
                fun request _ ->
                    task {
                        let integer name =
                            argument request name
                            |> Option.bind (fun value ->
                                match value.ValueKind with
                                | JsonValueKind.Number ->
                                    match value.TryGetInt64 () with
                                    | true, number -> Some number
                                    | false, _ -> None
                                | _ -> None)

                        let timeout =
                            match argument request "timeoutMs" with
                            | None -> Some 5000L
                            | Some _ ->
                                integer "timeoutMs"
                                |> Option.filter (fun ms -> ms >= 0L)

                        match integer "version", timeout with
                        | Some version, Some ms ->
                            let ms = min ms (int64 maximumWait)

                            match! exposure.WaitVersion (version, TimeSpan.FromMilliseconds (float ms)) with
                            | Some snapshot -> return success (Resources.state snapshot)
                            | None ->
                                return
                                    failure $"wait_version timed out after {ms} ms at version {exposure.Snapshot.Version}, before version {version}."
                        | None, _ -> return failure "wait_version takes an integer 'version'."
                        | _, None -> return failure "'timeoutMs' must be a non-negative integer."
                    }
        }

#if RANVIER_TRACE
    let codecs = TraceCodecs.ofList []

    let nodeNames = exposure.Nodes |> List.map _.Key

    let nodeSchema (extra: string) =
        let names = JsonArray ()

        for name in nodeNames do
            names.Add (JsonValue.Create name)

        let node = JsonObject ()
        node["type"] <- JsonValue.Create "string"
        node["enum"] <- names
        let properties = JsonObject ()
        properties["node"] <- node

        if extra <> "" then
            properties[extra] <- JsonObject ([ KeyValuePair<string, JsonNode>("type", JsonValue.Create "string") ])

        let schema = JsonObject ()
        schema["type"] <- JsonValue.Create "object"
        schema["properties"] <- properties
        json (schema.ToJsonString ())

    /// The exposed node a request names, or the reason it names none.
    let nodeOf (tool: string) (request: CallToolRequestParams) =
        match argument request "node" with
        | Some value when value.ValueKind = JsonValueKind.String ->
            let name = value.GetString ()

            match
                exposure.Nodes
                |> List.tryFind (fun entry -> entry.Key = name)
            with
            | Some entry -> Result.Ok entry.Value
            | None ->
                let known = String.Join (", ", nodeNames)
                Result.Error $"Unknown node '{name}'. Exposed nodes: {known}."
        | _ -> Result.Error $"{tool} takes a string 'node' naming an exposed node."

    /// A tool whose query runs on the graph thread and returns the text the result holds.
    let tracedWith (result: string -> CallToolResult) (tool: Tool) (query: Graph -> CallToolRequestParams -> Result<string, string>) =
        {
            Tool = tool
            Run =
                fun request _ ->
                    task {
                        try
                            match! exposure.Enter (fun graph -> query graph request) with
                            | Result.Ok text -> return result text
                            | Result.Error reason -> return failure reason
                        with ex ->
                            return failure $"{tool.Name} failed: {ex.Message}"
                    }
        }

    /// A tool whose query runs on the graph thread and returns a TraceJson document.
    let traced (name: string) (description: string) (schema: JsonElement) query =
        tracedWith success (Tool (Name = name, Description = description, InputSchema = schema)) query

    let sources = SourceLines options.SourceRoot

    let diagnosis (graph: Graph) =
        Diagnosis (graph, exposure.Nodes, exposure.Journal, sources)

    let exposedList = String.Join (", ", nodeNames)

    let text (request: CallToolRequestParams) (name: string) =
        match argument request name with
        | Some value when value.ValueKind = JsonValueKind.String -> Some (value.GetString ())
        | Some value when value.ValueKind = JsonValueKind.Number -> Some (value.GetRawText ())
        | _ -> None

    let integer (request: CallToolRequestParams) (name: string) (fallback: int) (low: int) (high: int) =
        match argument request name with
        | Some value when value.ValueKind = JsonValueKind.Number ->
            match value.TryGetInt32 () with
            | true, number -> Result.Ok (max low (min high number))
            | _ -> Result.Error $"'{name}' must be an integer."
        | Some value when value.ValueKind = JsonValueKind.String ->
            match Int32.TryParse (value.GetString ()) with
            | true, number -> Result.Ok (max low (min high number))
            | _ -> Result.Error $"'{name}' must be an integer."
        | _ -> Result.Ok fallback

    let nodeArgument =
        $"An exposed node ({exposedList}), a name or #id from graph, or a path."

    /// A schema whose required string 'node' takes any node, followed by the JSON text of further properties.
    let nodeSchemaOf (more: string) =
        let description = JsonValue.Create(nodeArgument).ToJsonString()

        json (
            """{"type":"object","properties":{"node":{"type":"string","description":"""
            + description
            + "}"
            + more
            + """},"required":["node"]}"""
        )

    let explainMessages = ""","messages":{"type":"array"}"""

    let explainTool =
        {
            Tool =
                Tool (
                    Name = "explain",
                    Description =
                        "Why a node holds its value: its creation line, reads, the message behind its last run, why it has not re-run, cutoffs that kept a value, and changed nodes it does not read. 'messages', as in dispatch, run first.",
                    InputSchema = nodeSchemaOf explainMessages,
                    Meta = alwaysLoad ()
                )
            Run =
                fun request _ ->
                    task {
                        let explain () =
                            exposure.Enter (fun graph ->
                                match text request "node" with
                                | Some node ->
                                    let d = diagnosis graph
                                    d.Resolve node |> Result.map d.Explain
                                | None -> Result.Error "explain takes a string 'node'.")

                        try
                            let! dispatched =
                                match argument request "messages" with
                                | None -> Task.FromResult (Result.Ok "")
                                | Some _ ->
                                    task {
                                        match messagesOf request with
                                        | Result.Error usage -> return Result.Error usage
                                        | Result.Ok messages ->
                                            let! outcome = dispatchAll messages
                                            return outcome |> Result.map (fun text -> text + "\n")
                                    }

                            match dispatched with
                            | Result.Error reason -> return failure reason
                            | Result.Ok steps ->
                                match! explain () with
                                | Result.Ok text -> return plain (steps + text)
                                | Result.Error reason -> return failure reason
                        with ex ->
                            return failure $"explain failed: {ex.Message}"
                    }
        }

    let graphTool =
        tracedWith
            plain
            (Tool (
                Name = "graph",
                Description = "Node counts, exposed nodes with sites, values and sources, and the last message's moves.",
                InputSchema = emptySchema
            ))
            (fun graph _ -> Result.Ok ((diagnosis graph).Overview 2000))

    let nodeTool =
        tracedWith
            plain
            (Tool (
                Name = "node",
                Description =
                    "A node and its sources and observers within 'around' hops (0-2, default 1), a line each; 'detail' full adds runs and source lines; pages of 'limit' lines continue at 'cursor'.",
                InputSchema =
                    nodeSchemaOf
                        ""","around":{"type":"integer","minimum":0,"maximum":2},"detail":{"type":"string","enum":["brief","full"]},"limit":{"type":"integer","minimum":1,"maximum":100},"cursor":{"type":"string"}"""
            ))
            (fun graph request ->
                match text request "node" with
                | None -> Result.Error "node takes a string 'node'."
                | Some node ->
                    let d = diagnosis graph

                    match
                        d.Resolve node, integer request "around" 1 0 2, integer request "limit" 25 1 100, integer request "cursor" 0 0 Int32.MaxValue
                    with
                    | Result.Ok id, Result.Ok around, Result.Ok limit, Result.Ok cursor ->
                        let full = text request "detail" = Some "full"
                        Result.Ok (d.Neighbourhood (id, around, full, cursor, limit, 4000))
                    | Result.Error reason, _, _, _
                    | _, Result.Error reason, _, _
                    | _, _, Result.Error reason, _
                    | _, _, _, Result.Error reason -> Result.Error reason)

    let onNode (tool: string) (query: Graph -> int -> INode -> string) =
        fun (graph: Graph) request ->
            nodeOf tool request
            |> Result.map (query graph (Trace.lastSeq graph))

    let traceTools =
        [
            traced
                "trace_state"
                "The graph state folded from the trace log: every node, owner, source edge and observer (TraceJson 'state' document)."
                emptySchema
                (fun graph _ -> Result.Ok (TraceJson.state (Trace.state graph codecs)))
            traced
                "trace_lookup"
                "Resolves an exposed node, or an identity 'path', in the trace log (TraceJson 'lookup' document)."
                (nodeSchema "path")
                (fun graph request ->
                    let seq = Trace.lastSeq graph

                    match argument request "path" with
                    | Some path when
                        path.ValueKind = JsonValueKind.String
                        && (argument request "node").IsNone
                        ->
                        Result.Ok (TraceJson.lookup seq (Trace.lookup graph (TraceAddress.ByPath (path.GetString ()))))
                    | _ ->
                        nodeOf "trace_lookup" request
                        |> Result.map (fun node -> TraceJson.lookup seq (Trace.lookupNode graph node)))
            traced
                "why"
                "The cause chain of an exposed node's last run, back to the write or creation that started it (TraceJson 'why' document)."
                (nodeSchema "")
                (onNode "why" (fun graph seq node -> TraceJson.why seq (Trace.whyWith graph codecs node)))
            traced
                "why_not"
                "Why an exposed node has not re-run: disposed, queued, unobserved or checked clean; null when it is up to date (TraceJson 'whyNot' document)."
                (nodeSchema "")
                (onNode "why_not" (fun graph seq node -> TraceJson.whyNot seq (Trace.whyNot graph node)))
            traced
                "waiting_on"
                "The pending sources and flights an exposed node waits on (TraceJson 'waiting' document)."
                (nodeSchema "")
                (onNode "waiting_on" (fun graph seq node -> TraceJson.waiting seq (Trace.waitingOn graph node)))
        ]
#endif

    let tools: AdapterTool list =
        [
            snapshotTool
            if not exposure.MessageNames.IsEmpty then
                dispatchTool
            waitVersionTool
#if RANVIER_TRACE
            explainTool
            graphTool
            nodeTool
            if options.RawTraceTools then
                yield! traceTools
#endif
        ]

    let instructions =
        let traced =
#if RANVIER_TRACE
            " When an exposed value is wrong, call explain {node, messages} once, with the messages that reproduce it: it names the node's creation line and its text, what the node reads, the message behind its last run, why it has not re-run, upstream nodes that kept their value (comparer or equality cutoffs), and changed nodes it does not read. Then read and fix the named line. graph lists the exposed nodes; node {node, around} shows a node's neighbours."
#else
            ""
#endif

        "The running application's reactive state. Reproduce a symptom with dispatch: pass 'messages' for a sequence; each step lists the exposed values it changed, then the final state. snapshot reads the state without dispatching."
        + traced

    let resource (uri: string) (name: string) (description: string) =
        Resource (Uri = uri, Name = name, Description = description, MimeType = "application/json")

    let listResources =
        McpRequestHandler<ListResourcesRequestParams, ListResourcesResult>(fun _ _ ->
            let resources =
                resource Resources.State "state" "Every exposed value, from one snapshot, with its version."
                :: [
                    for name in names -> resource (Resources.valueUri name) name $"The exposed value '{name}', with the snapshot's version."
                ]

            ValueTask<ListResourcesResult>(ListResourcesResult (Resources = ResizeArray resources)))

    let readResource =
        McpRequestHandler<ReadResourceRequestParams, ReadResourceResult>(fun request _ ->
            let uri = request.Params.Uri
            let snapshot = exposure.Snapshot

            let text =
                if uri = Resources.State then
                    Resources.state snapshot
                elif known.Contains uri then
                    Resources.value snapshot (uri.Substring (Resources.State.Length + 1))
                else
                    raise (notFound uri)

            let contents =
                TextResourceContents (Uri = uri, MimeType = "application/json", Text = text)

            ValueTask<ReadResourceResult>(ReadResourceResult (Contents = ResizeArray<ResourceContents> [ contents :> ResourceContents ])))

    let subscribe =
        McpRequestHandler<SubscribeRequestParams, EmptyResult>(fun request _ ->
            let uri = request.Params.Uri

            if not (known.Contains uri) then
                raise (notFound uri)

            let subscribed =
                subscriptions.GetOrAdd (request.Server, fun _ -> ConcurrentDictionary<string, byte>())

            subscribed[uri] <- 0uy
            ValueTask<EmptyResult>(EmptyResult ()))

    let unsubscribe =
        McpRequestHandler<UnsubscribeRequestParams, EmptyResult>(fun request _ ->
            match subscriptions.TryGetValue request.Server with
            | true, subscribed -> subscribed.TryRemove request.Params.Uri |> ignore
            | _ -> ()

            ValueTask<EmptyResult>(EmptyResult ()))

    let listTools =
        McpRequestHandler<ListToolsRequestParams, ListToolsResult>(fun _ _ ->
            ValueTask<ListToolsResult>(ListToolsResult (Tools = ResizeArray (tools |> List.map _.Tool))))

    let callTool =
        McpRequestHandler<CallToolRequestParams, CallToolResult>(fun request cancellationToken ->
            match
                tools
                |> List.tryFind (fun tool -> tool.Tool.Name = request.Params.Name)
            with
            | Some tool -> ValueTask<CallToolResult>(tool.Run request.Params cancellationToken)
            | None -> raise (McpProtocolException ($"Unknown tool '{request.Params.Name}'.", McpErrorCode.InvalidParams)))

    /// <summary>Server options carrying the adapter's resources, subscriptions and tools.</summary>
    member val Options =
        McpServerOptions (
            ServerInfo = Implementation (Name = "ranvier", Version = "0.1.0"),
            ServerInstructions = instructions,
            // 2026-07-28 replaces resources/subscribe with subscriptions/listen, which the adapter does not serve.
            ProtocolVersion = "2025-11-25",
            Capabilities =
                ServerCapabilities (
                    Resources = ResourcesCapability (Subscribe = Nullable true, ListChanged = Nullable false),
                    Tools = ToolsCapability (ListChanged = Nullable false)
                ),
            Handlers =
                McpServerHandlers (
                    ListResourcesHandler = listResources,
                    ReadResourceHandler = readResource,
                    SubscribeToResourcesHandler = subscribe,
                    UnsubscribeFromResourcesHandler = unsubscribe,
                    ListToolsHandler = listTools,
                    CallToolHandler = callTool
                )
        )

    /// <summary>An adapter with <c>McpAdapterOptions.Default</c>.</summary>
    new(exposure: Exposure) = new McpAdapter (exposure, McpAdapterOptions.Default)

    /// <summary>Stops notifying subscribers. Idempotent.</summary>
    member _.Dispose() =
        subscription.Dispose ()
        outbox.Writer.TryComplete () |> ignore

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()
