module Ranvier.Mcp.Tests.Scaffold

open Expecto
open ModelContextProtocol.Server

[<Tests>]
let tests =
    testList
        "Scaffold"
        [
            test "the MCP SDK resolves" {
                let name = typeof<McpServer>.Assembly.GetName().Name
                Expect.equal name "ModelContextProtocol.Core" "the SDK's server type"
            }

            test "the build's trace setting reaches the adapter" {
                let traced =
#if RANVIER_TRACE
                    true
#else
                    false
#endif
                let metadata =
                    typeof<Ranvier.Graph>.Assembly.GetCustomAttributes(typeof<System.Reflection.AssemblyMetadataAttribute>, false)
                    |> Seq.cast<System.Reflection.AssemblyMetadataAttribute>
                    |> Seq.exists (fun a -> a.Key = "RanvierTrace" && a.Value = "true")

                Expect.equal metadata traced "Ranvier and the tests share one trace setting"
            }
        ]
