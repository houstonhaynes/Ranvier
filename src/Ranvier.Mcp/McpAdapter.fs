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
/// <c>dispatch</c> (<c>{"message": name, "args": json}</c>, listed when the exposure has messages) runs
/// <c>Exposure.Dispatch</c> and returns the snapshot after the flush. An unknown message, a decoder <c>Error</c>, a
/// decoder that raises, or a missing <c>message</c> is a tool error that dispatches nothing. <c>wait_version</c>
/// (<c>{"version": n, "timeoutMs": ms}</c>) returns the first snapshot at or past <c>n</c>, or a tool error on timeout.
/// </para>
/// <para>
/// A traced build (<c>RANVIER_TRACE</c>) also lists <c>trace_state</c>, <c>trace_lookup</c>, <c>why</c>,
/// <c>why_not</c> and <c>waiting_on</c>, each returning the <c>TraceJson</c> document of the matching <c>Trace</c>
/// query, run on the graph thread. Their <c>node</c> argument names a node registered with <c>Expose.node</c>, but
/// their results read the whole graph's trace log: <c>trace_state</c> lists every node, <c>trace_lookup</c> resolves any
/// identity <c>path</c>, and a <c>why</c> chain names each node it passes through. An untraced build lists none of them.
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
type McpAdapter(exposure: Exposure) =
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

    /// A tool error: the client sees the reason, and the call has changed nothing.
    let failure (reason: string) =
        CallToolResult (Content = blocks reason, IsError = Nullable true)

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
                    Description = "The latest published snapshot of the exposed graph state: {\"version\": n, \"values\": {name: value}}.",
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
                                KeyValuePair<string, JsonNode>("description", JsonValue.Create "The message's arguments, read by its decoder.")
                            ]
                        )
                    )
                ]
            )

        schema["required"] <- JsonArray (JsonValue.Create "message")
        json (schema.ToJsonString ())

    let dispatchTool =
        {
            Tool =
                Tool (
                    Name = "dispatch",
                    Description =
                        "Dispatches an exposed message, decoded from 'args', and returns the snapshot published once it and its effects have run. A rejected message is a tool error and changes nothing.",
                    InputSchema = dispatchSchema
                )
            Run =
                fun request _ ->
                    task {
                        match argument request "message" with
                        | Some message when message.ValueKind = JsonValueKind.String ->
                            let args =
                                argument request "args"
                                |> Option.defaultValue nullElement

                            try
                                match! exposure.Dispatch (message.GetString (), args) with
                                | Result.Ok snapshot -> return success (Resources.state snapshot)
                                | Result.Error reason -> return failure reason
                            with ex ->
                                return failure $"The dispatch failed: {ex.Message}"
                        | _ -> return failure "dispatch takes a string 'message' naming an exposed message."
                    }
        }

    let maximumWait = 60000

    let waitVersionTool =
        {
            Tool =
                Tool (
                    Name = "wait_version",
                    Description =
                        $"Waits until the published snapshot reaches 'version', and returns it; a tool error after 'timeoutMs' (default 5000, at most {maximumWait}).",
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

    /// A tool whose query runs on the graph thread and returns a TraceJson document.
    let traced (name: string) (description: string) (schema: JsonElement) (query: Graph -> CallToolRequestParams -> Result<string, string>) =
        {
            Tool = Tool (Name = name, Description = description, InputSchema = schema)
            Run =
                fun request _ ->
                    task {
                        try
                            match! exposure.Enter (fun graph -> query graph request) with
                            | Result.Ok document -> return success document
                            | Result.Error reason -> return failure reason
                        with ex ->
                            return failure $"{name} failed: {ex.Message}"
                    }
        }

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
            yield! traceTools
#endif
        ]

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

    /// <summary>Stops notifying subscribers. Idempotent.</summary>
    member _.Dispose() =
        subscription.Dispose ()
        outbox.Writer.TryComplete () |> ignore

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()
