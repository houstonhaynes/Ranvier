module Ranvier.Mcp.Tests.SampleTests

open System
open System.IO
open System.Text.Json
open System.Threading.Tasks
open Expecto
open ModelContextProtocol.Client
open Ranvier.Mcp
open Ranvier.Mcp.Sample
open Ranvier.Mcp.Tests.McpSession

let private connect (defect: bool) : Task<Session<Cart.App>> =
    McpSession.connect (fun () -> GraphHost.Start (fun _ -> Cart.create defect)) _.Adapter

/// The cart's expectation, checked from a published snapshot alone: the total is the sum of the line prices less the
/// discount percentage, rounded towards zero. Returns the expected and the published total.
let private totals (snapshot: string) =
    use document = JsonDocument.Parse snapshot
    let values = document.RootElement.GetProperty "values"

    let subtotal =
        values.GetProperty("lines").EnumerateArray()
        |> Seq.sumBy (fun line ->
            line.GetProperty("unitPrice").GetInt32()
            * line.GetProperty("quantity").GetInt32())

    let discount = values.GetProperty("discount").GetInt32()
    subtotal - subtotal * discount / 100, values.GetProperty("total").GetInt32()

let private script =
    [
        """{"message":"add_item","args":{"sku":"tea","unitPrice":450,"quantity":2}}"""
        """{"message":"add_item","args":{"sku":"cake","unitPrice":325,"quantity":1}}"""
        """{"message":"set_discount","args":{"percent":10}}"""
        """{"message":"add_item","args":{"sku":"tea","unitPrice":999,"quantity":1}}"""
        """{"message":"set_discount","args":{"percent":25}}"""
        """{"message":"remove_item","args":{"sku":"cake"}}"""
        """{"message":"set_discount","args":{"percent":0}}"""
    ]

/// A seeded random message sequence over a few SKUs.
let private randomMessages (seed: int) (count: int) =
    let random = Random seed
    let skus = [| "tea"; "cake"; "jam"; "bread" |]

    [
        for _ in 1..count do
            match random.Next 3 with
            | 0 -> Cart.AddItem (skus[random.Next skus.Length], random.Next (1, 2000), random.Next (1, 5))
            | 1 -> Cart.RemoveItem skus[random.Next skus.Length]
            | _ -> Cart.SetDiscount (random.Next 101)
    ]

[<Tests>]
let tests =
    testList
        "MCP sample cart"
        [
            testTask "the cart's total meets the expectation after every scripted dispatch" {
                use! session = connect false

                for arguments in script do
                    let! result = call session "dispatch" arguments
                    Expect.isFalse (isError result) $"{arguments} is accepted: {textOf result}"
                    let expected, total = totals (textOf result)
                    Expect.equal total expected $"the total after {arguments}"
            }

            testTask "the cart's total meets the expectation after random message sequences" {
                use! session = connect false

                for message in randomMessages 168 300 do
                    do! session.Host.Invoke (fun (app: Cart.App) -> app.Mvu.Dispatch message)
                    let! snapshot = call session "snapshot" "{}"
                    let expected, total = totals (textOf snapshot)
                    Expect.equal total expected $"the total after {message}"
            }

            testTask "the --defect cart leaves the total stale after a discount change" {
                use! session = connect true
                let! _ = call session "dispatch" script[0]
                let! result = call session "dispatch" script[2]
                let expected, total = totals (textOf result)
                Expect.equal (expected, total) (810, 900) "the total ignores the discount"
            }

            testTask "the decoders reject out-of-range arguments without changing the cart" {
                use! session = connect false

                for arguments in
                    [
                        """{"message":"set_discount","args":{"percent":150}}"""
                        """{"message":"add_item","args":{"sku":"tea","unitPrice":450,"quantity":0}}"""
                        """{"message":"add_item","args":{"sku":" ","unitPrice":450,"quantity":1}}"""
                    ] do
                    let! result = call session "dispatch" arguments
                    Expect.isTrue (isError result) $"{arguments} is rejected"

                let! snapshot = call session "snapshot" "{}"
                Expect.equal (fst (totals (textOf snapshot))) 0 "the cart is empty"

                Expect.stringContains (textOf snapshot) "\"version\":1" "nothing was published"
            }

            testTask "the sample serves MCP over stdio" {
                let sample = Path.Combine (AppContext.BaseDirectory, "Ranvier.Mcp.Sample.dll")

                let transport =
                    StdioClientTransport (
                        StdioClientTransportOptions (Name = "ranvier-sample", Command = "dotnet", Arguments = ResizeArray [ sample ])
                    )

                let! (client: McpClient) = McpClient.CreateAsync transport

                try
                    let! tools = client.ListToolsAsync ()
                    Expect.contains (tools |> Seq.map _.Name) "dispatch" "the sample lists dispatch"

                    let! resources = client.ListResourcesAsync ()

                    Expect.contains (resources |> Seq.map _.Uri) "ranvier://state/total" "the sample exposes the total"
                finally
                    client.DisposeAsync().AsTask().Wait()
            }
        ]

#if RANVIER_TRACE
/// The ids of the cart's <c>discount</c>, <c>subtotal</c> and <c>total</c> nodes.
let private nodeIds (session: Session<Cart.App>) =
    session.Host.Invoke (fun app -> (app.Discount :> Ranvier.INode).Id, (app.Subtotal :> Ranvier.INode).Id, (app.Total :> Ranvier.INode).Id)

/// Adds a line, then sets a 10% discount, through dispatch.
let private addThenDiscount (session: Session<Cart.App>) =
    task {
        let! _ = call session "dispatch" script[0]
        let! _ = call session "dispatch" script[2]
        return ()
    }

[<Tests>]
let diagnosis =
    testList
        "MCP sample cart diagnosis (traced)"
        [
            testTask "why_not and trace_state show the --defect total does not depend on the discount" {
                use! session = connect true
                do! addThenDiscount session
                let! discount, subtotal, total = nodeIds session

                let! whyNot = call session "why_not" """{"node":"total"}"""

                match (Ranvier.TraceJson.readWhyNot (textOf whyNot)).Body with
                | Some (Ranvier.WhyNotReason.CheckedClean (_, upstream)) ->
                    Expect.equal upstream [ subtotal ] "the total was checked clean against the subtotal alone"
                | other -> failtest $"expected CheckedClean, got {other}"

                let! state = call session "trace_state" "{}"
                let state = Ranvier.TraceJson.readState (textOf state)
                Expect.equal state.Body.Sources[total] [ subtotal ] "the total's only source is the subtotal"

                Expect.isFalse
                    (state.Body.Sources[total]
                     |> List.contains discount)
                    "the discount is not a source"
            }

            testTask "why traces the healthy total's last run to the discount write" {
                use! session = connect false
                do! addThenDiscount session
                let! discount, subtotal, total = nodeIds session

                let! state = call session "trace_state" "{}"
                let state = Ranvier.TraceJson.readState (textOf state)

                Expect.equal (List.sort state.Body.Sources[total]) (List.sort [ subtotal; discount ]) "the total reads both"

                let! why = call session "why" """{"node":"total"}"""
                let why = Ranvier.TraceJson.readWhy (textOf why)

                Expect.isTrue
                    (why.Body.Steps
                     |> List.exists (fun step -> step.Node = discount))
                    "the chain passes through the discount"
            }
        ]
#endif
