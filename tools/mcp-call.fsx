// Calls one tool of a Ranvier MCP application from the command line and prints the result's text.
//
//   dotnet fsi --quiet tools/mcp-call.fsx <app.dll> <tool> ['<json arguments>']
//   dotnet fsi --quiet tools/mcp-call.fsx <app.dll> --list
//
// Each call starts the application from its build output, runs the MCP handshake, makes the call, and stops the
// application, so a rebuild needs no restart and two calls share no state. `explain {node, messages}` and
// `dispatch {messages}` reproduce a sequence in one call. `--list` prints the server's instructions and each tool's
// description and argument names.
// Exit code: 0 for a result, 1 for a tool error, 2 for a usage error or a failed start.

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json.Nodes

let argv = fsi.CommandLineArgs |> Array.skip 1

if argv.Length < 2 then
    eprintfn "usage: dotnet fsi --quiet tools/mcp-call.fsx <app.dll> <tool> ['<json arguments>'] | --list"
    exit 2

let app = Path.GetFullPath argv[0]

if not (File.Exists app) then
    eprintfn $"no build output at {app}; run dotnet build first"
    exit 2

let info =
    ProcessStartInfo (
        "dotnet",
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        UseShellExecute = false,
        StandardOutputEncoding = UTF8Encoding false
    )

info.ArgumentList.Add app
let proc = Process.Start info
proc.StandardInput.AutoFlush <- true

/// Sends a request and returns the response with the same id; notifications from the application are skipped.
let request (id: int) (method: string) (parameters: JsonNode) =
    let message = JsonObject ()
    message["jsonrpc"] <- JsonValue.Create "2.0"
    message["id"] <- JsonValue.Create id
    message["method"] <- JsonValue.Create method
    message["params"] <- parameters
    proc.StandardInput.WriteLine (message.ToJsonString ())
    let mutable response = null

    while isNull response do
        match proc.StandardOutput.ReadLine () with
        | null -> failwith "the application closed its output"
        | line ->
            let node = JsonNode.Parse line

            match node["id"] with
            | null -> ()
            | received when received.ToJsonString () = string id -> response <- node
            | _ -> ()

    response

let initialize =
    JsonNode.Parse """{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"mcp-call","version":"0.1.0"}}"""

let hello = request 1 "initialize" initialize
proc.StandardInput.WriteLine """{"jsonrpc":"2.0","method":"notifications/initialized"}"""

let textOf (body: JsonNode) =
    match body["content"] with
    | :? JsonArray as content ->
        String.Join (
            "\n",
            content
            |> Seq.choose (fun block ->
                match block["text"] with
                | null -> None
                | text -> Some (text.GetValue<string>()))
        )
    | _ -> body.ToJsonString ()

let code =
    if argv[1] = "--list" then
        match hello["result"]["instructions"] with
        | null -> ()
        | text -> printfn "%s\n" (text.GetValue<string>())

        let tools = request 2 "tools/list" (JsonObject ())

        for tool in tools["result"].["tools"].AsArray() do
            let names =
                match tool["inputSchema"]["properties"] with
                | :? JsonObject as properties -> String.Join (", ", properties |> Seq.map _.Key)
                | _ -> ""

            printfn "%s {%s}: %s\n" (tool["name"].GetValue<string>()) names (tool["description"].GetValue<string>())

        0
    else
        let parameters = JsonObject ()
        parameters["name"] <- JsonValue.Create argv[1]

        parameters["arguments"] <-
            (if argv.Length > 2 then
                 JsonNode.Parse argv[2]
             else
                 JsonObject ())

        let response = request 2 "tools/call" parameters

        match response["result"] with
        | null ->
            printfn "%s" (response["error"].ToJsonString())
            1
        | body ->
            printfn "%s" (textOf body)

            match body["isError"] with
            | null -> 0
            | e when e.GetValue<bool>() -> 1
            | _ -> 0

proc.StandardInput.Close ()

if not (proc.WaitForExit 3000) then
    proc.Kill true

exit code
