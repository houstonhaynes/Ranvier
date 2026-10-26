module Ranvier.Mcp.Tests.McpDispatchTests

open System
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading.Tasks
open Expecto
open Ranvier
open Ranvier.Elmish
open Ranvier.Mcp
open Ranvier.Mcp.Tests.McpSession

type private Model = { Count: int; Label: string }

type private Msg =
    | Add of int
    | Rename of string
    | Reset

type private App =
    {
        Mvu: Mvu<Model, Msg>
        Adapter: McpAdapter
        /// The number of updates applied.
        Updates: int ref
        /// The thread the last decoder ran on.
        DecoderThread: int ref
    }

/// A host exposing <c>count</c> and <c>label</c> of an Mvu, and the messages <c>add</c>, <c>rename</c> and
/// <c>reset</c>. <c>add</c> rejects an amount below 1.
let private startHost () =
    GraphHost.Start (fun _ ->
        let updates = ref 0
        let decoderThread = ref 0

        let update msg model =
            updates.Value <- updates.Value + 1

            match msg with
            | Add amount ->
                { model with
                    Count = model.Count + amount
                }
            | Rename label -> { model with Label = label }
            | Reset -> { model with Count = 0 }

        let mvu = Mvu.create { Count = 0; Label = "cart" } update
        let count = mvu.Select _.Count
        let label = mvu.Select _.Label

        let decoder (decode: JsonElement -> Result<Msg, string>) : MessageDecoder<Msg> =
            fun args ->
                decoderThread.Value <- Environment.CurrentManagedThreadId
                decode args

        let exposure = Exposure ()

        exposure
        |> Expose.value "count" (fun (value: int) -> JsonValue.Create value :> JsonNode) (fun () -> count.Value)

        exposure
        |> Expose.value "label" (fun (value: string) -> JsonValue.Create value :> JsonNode) (fun () -> label.Value)

        exposure
        |> Expose.messages
            mvu
            [
                "add",
                decoder (fun args ->
                    let amount = args.GetProperty("amount").GetInt32()

                    if amount < 1 then
                        Result.Error "amount must be positive"
                    else
                        Result.Ok (Add amount))
                "rename", decoder (fun args -> Result.Ok (Rename (args.GetProperty("label").GetString())))
                "reset", decoder (fun _ -> Result.Ok Reset)
            ]

        exposure.Start ()

        {
            Mvu = mvu
            Adapter = new McpAdapter (exposure)
            Updates = updates
            DecoderThread = decoderThread
        })

let private connect () : Task<McpSession.Session<App>> =
    McpSession.connect startHost _.Adapter

let private initial = """{"version":1,"values":{"count":0,"label":"cart"}}"""

[<Tests>]
let tests =
    testList
        "MCP dispatch"
        [
            testTask "tools/list lists dispatch and wait_version, and dispatch's schema names every exposed message" {
                use! session = connect ()
                let! tools = session.Client.ListToolsAsync ()
                let names = tools |> Seq.map _.Name |> List.ofSeq
                Expect.contains names "dispatch" "dispatch is listed"
                Expect.contains names "wait_version" "wait_version is listed"

                let dispatch =
                    tools
                    |> Seq.find (fun tool -> tool.Name = "dispatch")

                let messages =
                    dispatch.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("message").GetProperty("enum").EnumerateArray()
                    |> Seq.map _.GetString()
                    |> List.ofSeq

                Expect.equal messages [ "add"; "rename"; "reset" ] "the schema enumerates the exposed messages"
            }

            testTask "dispatch applies an authorised message on the graph thread and returns the snapshot after its flush" {
                use! session = connect ()
                let! result = call session "dispatch" """{"message":"add","args":{"amount":3}}"""
                Expect.isFalse (isError result) $"the dispatch succeeds: {textOf result}"

                Expect.stringContains (textOf result) "v2 count=3 label=\"cart\"" "the state after the flush"

                Expect.stringContains (textOf result) "count 0→3" "the text names the changed value"

                let! count, decoderThread =
                    session.Host.Invoke (fun app -> app.Mvu.Model.Count, app.DecoderThread.Value)

                Expect.equal count 3 "the model holds the message's effect"
                Expect.equal decoderThread session.Host.GraphThreadId "the decoder ran on the graph thread"

                let! unchanged =
                    call session "dispatch" """{"message":"rename","args":{"label":"cart"}}"""

                Expect.stringContains (textOf unchanged) "no exposed value changed" "an update leaving the model equal changes nothing"
                Expect.stringContains (textOf unchanged) "v2 " "an update leaving the model equal publishes nothing"
            }

            testTask "an unknown message, a decoder Error and malformed arguments are tool errors that leave the state unchanged" {
                use! session = connect ()

                let cases =
                    [
                        """{"message":"explode"}""", "Unknown message 'explode'"
                        """{"message":"add","args":{"amount":-1}}""", "amount must be positive"
                        """{"message":"add","args":"three"}""", "malformed"
                        """{"message":"add"}""", "malformed"
                        """{"message":"add","args":{"amount":"3"}}""", "malformed"
                        """{}""", "'message'"
                        """{"message":7}""", "'message'"
                    ]

                for arguments, reason in cases do
                    let! result = call session "dispatch" arguments
                    Expect.isTrue (isError result) $"{arguments} is a tool error"
                    Expect.stringContains (textOf result) reason $"{arguments} names the reason"

                let! snapshot = call session "snapshot" "{}"
                Expect.equal (textOf snapshot) initial "the snapshot is unchanged"

                let! updates, model =
                    session.Host.Invoke (fun app -> app.Updates.Value, app.Mvu.Model)

                Expect.equal updates 0 "no update ran"
                Expect.equal model { Count = 0; Label = "cart" } "the model is unchanged"
            }

            testTask "wait_version completes once the published version reaches its target" {
                use! session = connect ()
                let waiting = call session "wait_version" """{"version":3,"timeoutMs":10000}"""
                do! Task.Delay 100
                Expect.isFalse waiting.IsCompleted "the wait is pending at version 1"

                do! session.Host.Invoke (fun app -> app.Mvu.Dispatch (Add 1))
                do! session.Host.Invoke (fun app -> app.Mvu.Dispatch (Add 1))

                let! result = waiting
                Expect.isFalse (isError result) $"the wait succeeds: {textOf result}"
                Expect.equal (textOf result) """{"version":3,"values":{"count":2,"label":"cart"}}""" "the snapshot at the target"
            }

            testTask "wait_version returns a reached version at once, and a tool error on timeout or a bad version" {
                use! session = connect ()
                let! reached = call session "wait_version" """{"version":1}"""
                Expect.equal (textOf reached) initial "a reached version returns the current snapshot"

                let! late = call session "wait_version" """{"version":50,"timeoutMs":50}"""
                Expect.isTrue (isError late) "a timeout is a tool error"
                Expect.stringContains (textOf late) "timed out" "the error names the timeout"

                let! bad = call session "wait_version" """{"version":"soon"}"""
                Expect.isTrue (isError bad) "a non-integer version is a tool error"
                Expect.equal (textOf bad) "wait_version takes an integer 'version'." "a string version names the version"

                let! fractional = call session "wait_version" """{"version":1.5}"""
                Expect.equal (textOf fractional) "wait_version takes an integer 'version'." "a fractional version names the version"

                let! badTimeout = call session "wait_version" """{"version":1,"timeoutMs":"x"}"""
                Expect.isTrue (isError badTimeout) "a non-integer timeout is a tool error"
                Expect.equal (textOf badTimeout) "'timeoutMs' must be a non-negative integer." "a string timeout names the timeout"
            }
        ]
