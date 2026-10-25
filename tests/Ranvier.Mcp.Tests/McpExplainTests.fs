module Ranvier.Mcp.Tests.McpExplainTests

open System.Collections.Generic
open System.Text.Json.Nodes
open System.Threading.Tasks
open Expecto
open Ranvier
open Ranvier.Elmish
open Ranvier.Mcp
open Ranvier.Mcp.Sample
open Ranvier.Mcp.Tests.McpSession

type private Offer = { Label: string; Percent: int }

/// Calls two offers equal when their labels match: a discount change inside one band keeps the old offer.
let private byLabel =
    { new IEqualityComparer<Offer> with
        member _.Equals(a, b) =
            a.Label = b.Label

        member _.GetHashCode a =
            a.Label.GetHashCode ()
    }

type private PricedApp = { Adapter: McpAdapter }

/// A priced total over an <c>offer</c> memo whose comparer compares labels alone: the defect of #168's round 2.
let private startPriced () =
    GraphHost.Start (fun _ ->
        let mvu =
            Mvu.create (900, 0) (fun (percent: int) (subtotal, _) -> subtotal, percent)

        let discount = mvu.Select snd

        let offer =
            createMemoWithComparer byLabel (fun _ ->
                let percent = discount.Value

                {
                    Label =
                        (if percent = 0 then "none"
                         elif percent < 20 then "standard"
                         else "promo")
                    Percent = percent
                })

        let total = createMemo (fun _ -> 900 - 900 * offer.Value.Percent / 100)
        let exposure = Exposure ()

        exposure
        |> Expose.value "total" (fun (value: int) -> JsonValue.Create value :> JsonNode) (fun () -> total.Value)

        exposure
        |> Expose.messages
            mvu
            [
                "set_discount", fun args -> Result.Ok (args.GetProperty("percent").GetInt32())
            ]

        exposure |> Expose.node "total" total
        exposure.Start ()
        { Adapter = new McpAdapter (exposure) })

type private WideApp =
    {
        Input: Signal<int>
        Adapter: McpAdapter
    }

/// <c>width</c> chains of <c>depth</c> memos over one input, summed by <c>leaf</c>: <c>width * depth + 3</c> nodes.
let private startWide (width: int) (depth: int) =
    GraphHost.Start (fun _ ->
        let input = createSignal 0

        let ends =
            [
                for w in 1..width ->
                    let mutable memo = createMemo (fun _ -> input.Value + w)

                    for _ in 2..depth do
                        let previous = memo
                        memo <- createMemo (fun _ -> previous.Value + 1)

                    memo
            ]

        let leaf = createMemo (fun _ -> ends |> List.sumBy _.Value)
        let exposure = Exposure ()

        exposure
        |> Expose.value "leaf" (fun (value: int) -> JsonValue.Create value :> JsonNode) (fun () -> leaf.Value)

        exposure |> Expose.node "leaf" leaf
        exposure |> Expose.node "input" input
        exposure.Start ()

        {
            Input = input
            Adapter = new McpAdapter (exposure)
        })

let private cartScript =
    """{"messages":[{"message":"add_item","args":{"sku":"tea","unitPrice":450,"quantity":2}},{"message":"add_item","args":{"sku":"cake","unitPrice":300,"quantity":1}},{"message":"set_discount","args":{"percent":10}}]}"""

[<Tests>]
let tests =
    testList
        "MCP explain, graph, node and dispatch sequences"
        [
            testTask "dispatch runs a sequence and lists each step's changed values in at most 200 characters" {
                use! session =
                    McpSession.connect (fun () -> GraphHost.Start (fun _ -> Cart.create false)) _.Adapter

                let! result = call session "dispatch" cartScript
                Expect.isFalse (isError result) $"the sequence is accepted: {textOf result}"
                let lines = (textOf result).Split '\n'
                Expect.equal lines.Length 4 "three steps and the final state"

                for step in lines[0..2] do
                    Expect.isLessThanOrEqual step.Length 200 $"a step is short: {step}"

                Expect.stringContains lines[2] "discount 0→10" "the discount step names its change"
                Expect.stringContains lines[2] "total 1200→1080" "the healthy total follows the discount"
                Expect.stringContains lines[3] "v4 " "the final state carries the version"
                Expect.stringContains (result.StructuredContent.Value.GetRawText ()) "\"version\":4" "the structured content is the final snapshot"
            }

            testTask "dispatch stops at a rejected message and names its position" {
                use! session =
                    McpSession.connect (fun () -> GraphHost.Start (fun _ -> Cart.create false)) _.Adapter

                let! result =
                    call
                        session
                        "dispatch"
                        """{"messages":[{"message":"set_discount","args":{"percent":5}},{"message":"set_discount","args":{"percent":500}},{"message":"set_discount","args":{"percent":7}}]}"""

                Expect.isTrue (isError result) "the rejection is a tool error"
                Expect.stringContains (textOf result) "Message 2 (set_discount) was rejected" "the error names the position"
                Expect.stringContains (textOf result) "discount 0→5" "the first message stays dispatched"
                let! snapshot = call session "snapshot" "{}"
                Expect.stringContains (textOf snapshot) "\"discount\":5" "the third message did not run"
            }

#if RANVIER_TRACE
            testTask "tools/list holds at most 6 tools, loads dispatch and explain always, and the server carries instructions" {
                use! session =
                    McpSession.connect (fun () -> GraphHost.Start (fun _ -> Cart.create true)) _.Adapter

                let! tools = session.Client.ListToolsAsync ()
                Expect.isLessThanOrEqual (Seq.length tools) 6 "at most 6 tools"

                let definitions =
                    System.Text.Json.JsonSerializer.Serialize (
                        tools |> Seq.map _.ProtocolTool |> Array.ofSeq,
                        ModelContextProtocol.McpJsonUtilities.DefaultOptions
                    )

                let shapes =
                    Cart.shapes
                    |> List.map (fun (name, shape) -> $"{name} {shape}")
                    |> fun parts -> System.Text.Json.JsonSerializer.Serialize (" Args: " + System.String.Join ("; ", parts) + ".")

                Expect.isLessThanOrEqual (definitions.Length - shapes.Length) 2500 "the definitions, less the sample's argument shapes, are small"

                let dispatch =
                    tools
                    |> Seq.find (fun tool -> tool.Name = "dispatch")

                Expect.stringContains dispatch.Description "add_item {\"sku\": string" "dispatch describes each message's arguments"

                for name in [ "dispatch"; "explain" ] do
                    let tool = tools |> Seq.find (fun tool -> tool.Name = name)
                    let meta = tool.ProtocolTool.Meta
                    Expect.isNotNull meta $"{name} carries _meta"
                    Expect.equal (meta["anthropic/alwaysLoad"].GetValue<bool>()) true $"{name} is always loaded"

                let instructions = session.Client.ServerInstructions
                Expect.isNotNull instructions "the server carries instructions"
                Expect.isLessThanOrEqual instructions.Length 1000 "the instructions are short"
                Expect.stringContains instructions "explain" "the instructions name explain"
            }

            testTask "explain names the --defect total's line and the discount change it does not read, in at most 1,000 characters" {
                use! session =
                    McpSession.connect (fun () -> GraphHost.Start (fun _ -> Cart.create true)) _.Adapter

                let! _ = call session "dispatch" cartScript
                let! result = call session "explain" """{"node":"total"}"""
                Expect.isFalse (isError result) $"explain succeeds: {textOf result}"
                let text = textOf result
                Expect.isLessThanOrEqual text.Length 1000 $"the answer is short: {text}"
                Expect.stringContains text "total#" "the node by name"
                Expect.stringContains text "Cart.fs:" "the node's creation site"
                Expect.stringContains text "discount.Peek" "the creation line's text"
                Expect.stringContains text "unread: discount#" "the changed discount"
                Expect.stringContains text "total does not read it" "the missing dependency"
                Expect.stringContains text "set_discount" "the message behind the change"
            }

            testTask "explain names an upstream comparer cutoff that keeps a stale offer" {
                use! session = McpSession.connect startPriced _.Adapter

                let! _ =
                    call
                        session
                        "dispatch"
                        """{"messages":[{"message":"set_discount","args":{"percent":10}},{"message":"set_discount","args":{"percent":15}}]}"""

                let! result = call session "explain" """{"node":"total"}"""
                let text = textOf result
                Expect.isFalse (isError result) $"explain succeeds: {text}"
                Expect.isLessThanOrEqual text.Length 1000 $"the answer is short: {text}"
                Expect.stringContains text "cutoff: offer#" "the cutoff names the offer memo"
                Expect.stringContains text "McpExplainTests.fs:" "the cutoff names the offer's site"
                Expect.stringContains text "Percent = 10" "the offer kept the old percent"
                Expect.stringContains text "comparer" "the cutoff names the comparer"
            }

            testTask "graph, node and explain stay bounded on a 1,000-node graph, and node pages with a cursor" {
                use! session = McpSession.connect (fun () -> startWide 50 20) _.Adapter
                do! session.Host.Invoke (fun app -> app.Input.Value <- 1)
                let! graph = call session "graph" "{}"
                Expect.isLessThanOrEqual (textOf graph).Length 2000 $"graph is short: {textOf graph}"
                Expect.stringContains (textOf graph) "1003 live nodes" "graph counts every node"

                let! page = call session "node" """{"node":"leaf","around":1}"""
                let text = textOf page
                Expect.isLessThanOrEqual text.Length 4000 "a node page is short"
                Expect.stringContains text "Continue with cursor" "an over-limit page says how to continue"

                let! full =
                    call session "node" """{"node":"leaf","around":2,"detail":"full","limit":100}"""

                Expect.isLessThanOrEqual (textOf full).Length 4000 "a full page is short"

                let! explain = call session "explain" """{"node":"leaf"}"""
                Expect.isLessThanOrEqual (textOf explain).Length 20000 "explain is bounded"
            }

            testTask "node and explain accept an id and a binding name, and name the exposed nodes on a miss" {
                use! session =
                    McpSession.connect (fun () -> GraphHost.Start (fun _ -> Cart.create true)) _.Adapter

                let! byName = call session "node" """{"node":"lines","around":0}"""
                Expect.stringContains (textOf byName) "lines#" $"a binding name resolves: {textOf byName}"
                let! byId = call session "explain" """{"node":"#5"}"""
                Expect.stringContains (textOf byId) "total#5" "an id resolves"
                let! missing = call session "explain" """{"node":"nope"}"""
                Expect.isTrue (isError missing) "a miss is a tool error"
                Expect.stringContains (textOf missing) "subtotal, discount, total" "the miss lists the exposed nodes"
            }
#endif
        ]
