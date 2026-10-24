module Ranvier.Mcp.Tests.McpReadTests

open System
open System.Collections.Concurrent
open System.IO.Pipelines
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks
open Expecto
open ModelContextProtocol.Client
open ModelContextProtocol.Protocol
open ModelContextProtocol.Server
open Ranvier
open Ranvier.Mcp
open Ranvier.Mcp.Tests.McpSession

type private App =
    {
        A: Signal<int>
        B: Signal<int>
        Adapter: McpAdapter
    }

let private number (value: int) : JsonNode =
    JsonValue.Create value

/// A host whose graph exposes <c>b</c>, <c>parity</c> (a memo of <c>a % 2</c>) and <c>sum</c> (a memo of <c>a + b</c>).
let private startHost () =
    GraphHost.Start (fun _ ->
        let a = createSignal 1
        let b = createSignal 2
        let sum = createMemo (fun _ -> a.Value + b.Value)
        let parity = createMemo (fun _ -> a.Value % 2)
        let exposure = Exposure ()

        exposure
        |> Expose.value "b" number (fun () -> b.Value)

        exposure
        |> Expose.value "parity" number (fun () -> parity.Value)

        exposure
        |> Expose.value "sum" number (fun () -> sum.Value)

        exposure.Start ()

        {
            A = a
            B = b
            Adapter = new McpAdapter (exposure)
        })

let private connect () : Task<McpSession.Session<App>> =
    McpSession.connect startHost _.Adapter

let private textOf (result: ReadResourceResult) =
    match Seq.exactlyOne result.Contents with
    | :? TextResourceContents as text -> JsonNode.Parse text.Text
    | other -> failtest $"expected text contents, got {other}"

[<Tests>]
let tests =
    testList
        "MCP read side"
        [
            testTask "resources/list names the whole state and each exposed value" {
                use! session = connect ()
                let! resources = session.Client.ListResourcesAsync ()

                Expect.equal
                    (resources |> Seq.map _.Uri |> List.ofSeq)
                    [
                        "ranvier://state"
                        "ranvier://state/b"
                        "ranvier://state/parity"
                        "ranvier://state/sum"
                    ]
                    "the state, then each value in registration order"

                for resource in resources do
                    Expect.equal resource.MimeType "application/json" $"{resource.Uri} is JSON"
            }

            testTask "resources/read returns the whole snapshot and each value at its version" {
                use! session = connect ()
                let! state = session.Client.ReadResourceAsync "ranvier://state"
                let state = textOf state
                Expect.equal (state["version"].GetValue<int64>()) 1L "the first publication"

                Expect.equal (state["values"].ToJsonString()) """{"b":2,"parity":1,"sum":3}""" "every value from one snapshot"

                let! sum = session.Client.ReadResourceAsync "ranvier://state/sum"
                Expect.equal ((textOf sum).ToJsonString()) """{"version":1,"value":3}""" "one value and its version"

                let! unknown =
                    task {
                        try
                            let! _ = session.Client.ReadResourceAsync "ranvier://state/missing"
                            return None
                        with ex ->
                            return Some ex
                    }

                Expect.isSome unknown "an unknown resource is an error"
            }

            testTask "the snapshot tool returns the published snapshot, and follows a change" {
                use! session = connect ()
                let! tools = session.Client.ListToolsAsync ()
                Expect.contains (tools |> Seq.map _.Name) "snapshot" "the snapshot tool is listed"

                do! session.Host.Invoke (fun app -> app.A.Value <- 2)
                let! result = session.Client.CallToolAsync "snapshot"
                Expect.isFalse (result.IsError.GetValueOrDefault false) "the call succeeds"

                let text =
                    match Seq.exactlyOne result.Content with
                    | :? TextContentBlock as block -> block.Text
                    | other -> failtest $"expected text, got {other}"

                Expect.equal text """{"version":2,"values":{"b":2,"parity":0,"sum":4}}""" "the snapshot after the write"

                Expect.equal (result.StructuredContent.Value.GetRawText ()) text "the structured content holds the same snapshot"
            }

            testTask "resources/updated is sent only for values whose encoding changed" {
                use! session = connect ()
                let received = ConcurrentQueue<string>()

                let uris =
                    [
                        "ranvier://state"
                        "ranvier://state/b"
                        "ranvier://state/parity"
                        "ranvier://state/sum"
                    ]

                for uri in uris do
                    let! _ =
                        session.Client.SubscribeToResourceAsync (
                            uri,
                            Func<ResourceUpdatedNotificationParams, CancellationToken, ValueTask>(fun update _ ->
                                received.Enqueue update.Uri
                                ValueTask.CompletedTask)
                        )

                    ()

                // a: 1 -> 3. parity is cut off at 1; sum moves to 5.
                do! session.Host.Invoke (fun app -> app.A.Value <- 3)
                // A batch: a 3 -> 4, b 2 -> 1. sum re-encodes 5; parity moves to 0, b to 1.
                do!
                    session.Host.Invoke (fun app ->
                        batch (fun () ->
                            app.A.Value <- 4
                            app.B.Value <- 1))
                // An equal write: no flush at all.
                do! session.Host.Invoke (fun app -> app.B.Value <- 1)

                let expected =
                    [
                        "ranvier://state"
                        "ranvier://state/sum"
                        "ranvier://state"
                        "ranvier://state/b"
                        "ranvier://state/parity"
                    ]

                SpinWait.SpinUntil ((fun () -> received.Count >= expected.Length), TimeSpan.FromSeconds 10.)
                |> ignore

                do! Task.Delay 200

                Expect.equal (received |> List.ofSeq |> List.sort) (List.sort expected) "two publications, changed values only"
            }
        ]
