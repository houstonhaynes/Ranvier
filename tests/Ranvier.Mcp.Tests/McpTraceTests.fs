module Ranvier.Mcp.Tests.McpTraceTests

open System.Text.Json.Nodes
open System.Threading.Tasks
open Expecto
open Ranvier
open Ranvier.Mcp
open Ranvier.Mcp.Tests.McpSession

type private App =
    {
        A: Signal<int>
        Sum: Memo<int>
        Hidden: Memo<int>
        Adapter: McpAdapter
    }

let private traceTools =
    [ "trace_state"; "trace_lookup"; "why"; "why_not"; "waiting_on" ]

/// A host exposing <c>sum</c> (a memo of <c>a + 2</c>) as a value, and <c>a</c> and <c>sum</c> as nodes. <c>hidden</c>
/// (a memo of <c>a * 3</c>) is registered as neither.
let private startHost () =
    GraphHost.Start (fun _ ->
        let a = createSignal 1
        let sum = createMemo (fun _ -> a.Value + 2)
        let hidden = createMemo (fun _ -> a.Value * 3)
        hidden.Value |> ignore
        let exposure = Exposure ()

        exposure
        |> Expose.value "sum" (fun (value: int) -> JsonValue.Create value :> JsonNode) (fun () -> sum.Value)

        exposure |> Expose.node "a" a
        exposure |> Expose.node "sum" sum
        exposure.Start ()

        {
            A = a
            Sum = sum
            Hidden = hidden
            Adapter =
                new McpAdapter (
                    exposure,
                    { McpAdapterOptions.Default with
                        RawTraceTools = true
                    }
                )
        })

let private connect () : Task<Session<App>> =
    McpSession.connect startHost _.Adapter

#if RANVIER_TRACE
[<Tests>]
let tests =
    testList
        "MCP trace tools (traced)"
        [
            testTask "tools/list holds every trace tool" {
                use! session = connect ()
                let! tools = session.Client.ListToolsAsync ()
                let names = tools |> Seq.map _.Name |> Set.ofSeq

                for tool in traceTools do
                    Expect.contains names tool $"{tool} is listed"
            }

            testTask "why names the write that re-ran an exposed memo" {
                use! session = connect ()
                do! session.Host.Invoke (fun app -> app.A.Value <- 5)
                let! sumId = session.Host.Invoke (fun app -> (app.Sum :> INode).Id)

                let! result = call session "why" """{"node":"sum"}"""
                Expect.isFalse (isError result) $"why succeeds: {textOf result}"

                let document = TraceJson.readWhy (textOf result)
                Expect.equal document.Kind "why" "a why document"
                Expect.equal document.Body.Node sumId "the exposed memo's chain"

                match document.Body.Root with
                | Some (WhyRoot.UserWrite _) -> ()
                | other -> failtest $"expected a user write at the root, got {other}"
            }

            testTask "trace_state, trace_lookup, why_not and waiting_on return contract documents" {
                use! session = connect ()
                let! sumId = session.Host.Invoke (fun app -> (app.Sum :> INode).Id)

                let! state = call session "trace_state" "{}"
                Expect.isFalse (isError state) $"trace_state succeeds: {textOf state}"
                let state = TraceJson.readState (textOf state)
                Expect.equal state.Kind "state" "a state document"
                Expect.isTrue (state.Body.Nodes |> Map.containsKey sumId) "the state holds the exposed memo"

                let! lookup = call session "trace_lookup" """{"node":"sum"}"""
                let lookup = TraceJson.readLookup (textOf lookup)

                match lookup.Body with
                | TraceLookup.Live reference -> Expect.equal reference.Node sumId "the exposed memo resolves live"
                | other -> failtest $"expected a live node, got {other}"

                let! whyNot = call session "why_not" """{"node":"a"}"""
                Expect.equal (TraceJson.readWhyNot (textOf whyNot)).Kind "whyNot" "a whyNot document"

                let! waiting = call session "waiting_on" """{"node":"sum"}"""
                let waiting = TraceJson.readWaiting (textOf waiting)
                Expect.equal waiting.Body.Node sumId "the exposed memo's wait"
                Expect.isEmpty waiting.Body.Sources "a synchronous memo waits on nothing"
            }

            testTask "trace_state and trace_lookup by path read a node registered with neither Expose.value nor Expose.node" {
                use! session = connect ()
                let! hiddenId = session.Host.Invoke (fun app -> (app.Hidden :> INode).Id)

                let! state = call session "trace_state" "{}"
                let state = TraceJson.readState (textOf state)

                match state.Body.Nodes |> Map.tryFind hiddenId with
                | None -> failtest "trace_state lists the unexposed memo"
                | Some (hidden: TraceNodeState) ->
                    let args = JsonObject ()
                    args["path"] <- JsonValue.Create hidden.Ref.Path
                    let! lookup = call session "trace_lookup" (args.ToJsonString ())
                    Expect.isFalse (isError lookup) $"trace_lookup by path succeeds: {textOf lookup}"

                    match (TraceJson.readLookup (textOf lookup)).Body with
                    | TraceLookup.Live reference -> Expect.equal reference.Node hiddenId "the unexposed memo resolves live"
                    | other -> failtest $"expected a live node, got {other}"
            }

            testTask "a trace tool's node argument naming an unexposed node is a tool error" {
                use! session = connect ()

                for tool in [ "why"; "why_not"; "waiting_on"; "trace_lookup" ] do
                    let! result = call session tool """{"node":"secret"}"""
                    Expect.isTrue (isError result) $"{tool} rejects an unexposed node"
                    Expect.stringContains (textOf result) "Unknown node 'secret'" $"{tool} names the node"

                let! missing = call session "why" "{}"
                Expect.isTrue (isError missing) "a missing node name is a tool error"
            }
        ]
#else
[<Tests>]
let tests =
    testList
        "MCP trace tools (untraced)"
        [
            testTask "tools/list holds no trace tool, and a call to one is an error" {
                use! session = connect ()
                let! tools = session.Client.ListToolsAsync ()
                let names = tools |> Seq.map _.Name |> Set.ofSeq

                for tool in traceTools do
                    Expect.isFalse (names.Contains tool) $"{tool} is not listed"

                let! refused =
                    task {
                        try
                            let! result = call session "why" """{"node":"sum"}"""
                            return isError result
                        with _ ->
                            return true
                    }

                Expect.isTrue refused "an untraced build refuses a trace tool"
            }
        ]
#endif
