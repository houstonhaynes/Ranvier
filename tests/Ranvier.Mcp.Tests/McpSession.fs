module Ranvier.Mcp.Tests.McpSession

open System
open System.Collections.Generic
open System.IO.Pipelines
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Expecto
open ModelContextProtocol.Client
open ModelContextProtocol.Protocol
open ModelContextProtocol.Server
open Ranvier.Mcp

/// An in-process MCP client connected to a host's adapter over a pair of pipes.
type Session<'App> =
    {
        Host: GraphHost<'App>
        Adapter: McpAdapter
        Client: McpClient
        Cancel: CancellationTokenSource
        Server: Task
    }

    interface IAsyncDisposable with
        member this.DisposeAsync() =
            ValueTask (
                task {
                    do! this.Client.DisposeAsync ()
                    this.Cancel.Cancel ()

                    try
                        do! this.Server
                    with _ ->
                        ()

                    let adapter = this.Adapter
                    do! this.Host.Invoke (fun _ -> (adapter :> IDisposable).Dispose())
                    this.Host.Dispose ()
                }
            )

/// Starts a host and connects a client to the adapter <c>adapterOf</c> returns.
let connect (start: unit -> GraphHost<'App>) (adapterOf: 'App -> McpAdapter) =
    task {
        let host = start ()
        let! adapter = host.Invoke adapterOf
        let toServer = Pipe ()
        let toClient = Pipe ()
        let cancel = new CancellationTokenSource ()

        let transport =
            new StreamServerTransport (toServer.Reader.AsStream (), toClient.Writer.AsStream ())

        let server = McpServer.Create (transport, adapter.Options)
        let running = server.RunAsync cancel.Token

        let! client =
            McpClient.CreateAsync (new StreamClientTransport (toServer.Writer.AsStream (), toClient.Reader.AsStream ()))

        return
            {
                Host = host
                Adapter = adapter
                Client = client
                Cancel = cancel
                Server = running
            }
    }

/// Calls <c>name</c> with arguments given as a JSON object's text.
let call (session: Session<'App>) (name: string) (arguments: string) =
    use document = JsonDocument.Parse arguments

    let values = Dictionary<string, JsonElement>()

    for property in document.RootElement.EnumerateObject () do
        values[property.Name] <- property.Value.Clone ()

    session.Client.CallToolAsync (CallToolRequestParams (Name = name, Arguments = values))

/// The text of a tool result's single text block.
let textOf (result: CallToolResult) =
    match Seq.exactlyOne result.Content with
    | :? TextContentBlock as block -> block.Text
    | other -> failtest $"expected text, got {other}"

/// True when the result is a tool error.
let isError (result: CallToolResult) =
    result.IsError.GetValueOrDefault false
