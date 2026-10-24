module Ranvier.Mcp.Sample.Program

open System.Threading
open Microsoft.Extensions.Logging.Abstractions
open ModelContextProtocol.Server
open Ranvier.Mcp

/// <summary>
/// Serves the cart over MCP on stdin and stdout until the client closes stdin. <c>--defect</c> builds the cart with the
/// seeded stale-total defect.
/// </summary>
[<EntryPoint>]
let main argv =
    let defect = argv |> Array.contains "--defect"
    use host = GraphHost.Start (fun _ -> Cart.create defect)
    let adapter = host.Invoke(_.Adapter).Result

    let transport =
        new StdioServerTransport ("ranvier-sample", NullLoggerFactory.Instance)

    let server = McpServer.Create (transport, adapter.Options)
    server.RunAsync(CancellationToken.None).GetAwaiter().GetResult()
    server.DisposeAsync().AsTask().GetAwaiter().GetResult()
    host.Invoke(fun app -> app.Adapter.Dispose ()).Wait()
    0
