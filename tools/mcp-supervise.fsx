// Serves a Ranvier MCP application over stdio so that a rebuild never needs a server restart.
//
//   dotnet fsi --quiet tools/mcp-supervise.fsx <app.dll> [app arguments...]
//
// The application runs from a shadow copy of its output directory, so `dotnet build` can overwrite the output while
// the server runs. On each tools/call, when the output's <app.dll> is newer than the running copy, the supervisor
// copies the new build, starts it, repeats the client's initialize, and dispatches again every message accepted so far,
// in one `dispatch {messages}` call. The tool result that follows a reload starts with a line naming the reload and the
// replay's final state. Every other message passes through unchanged, one request at a time.
// Exit code: the supervisor exits when the client closes stdin.

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open System.Collections.Concurrent

let argv = fsi.CommandLineArgs |> Array.skip 1

if argv.Length < 1 then
    eprintfn "usage: dotnet fsi --quiet tools/mcp-supervise.fsx <app.dll> [app arguments...]"
    exit 2

let app = Path.GetFullPath argv[0]
let appArgs = argv[1..]
let output = Path.GetDirectoryName app

let shadowRoot =
    let key = string (abs (app.ToLowerInvariant().GetHashCode()))
    Path.Combine (Path.GetTempPath (), "ranvier-mcp", key, string Environment.ProcessId)

let log (text: string) =
    eprintfn "[mcp-supervise] %s" text

let stamp () =
    File.GetLastWriteTimeUtc app

/// Waits until the build output's app.dll has kept its stamp for half a second, up to 60 seconds.
let settled () =
    let deadline = DateTime.UtcNow.AddSeconds 60.0
    let mutable last = stamp ()
    let mutable quiet = DateTime.UtcNow

    while DateTime.UtcNow - quiet < TimeSpan.FromMilliseconds 500.0
          && DateTime.UtcNow < deadline do
        Thread.Sleep 100
        let now = stamp ()

        if now <> last then
            last <- now
            quiet <- DateTime.UtcNow

    last

let rec copyDirectory (source: string) (target: string) =
    Directory.CreateDirectory target |> ignore

    for file in Directory.GetFiles source do
        File.Copy (file, Path.Combine (target, Path.GetFileName file), true)

    for directory in Directory.GetDirectories source do
        copyDirectory directory (Path.Combine (target, Path.GetFileName directory))

type Child =
    {
        Process: Process
        Lines: BlockingCollection<string>
        Built: DateTime
        Shadow: string
    }

let mutable generation = 0

let start () =
    let built = settled ()
    generation <- generation + 1
    let shadow = Path.Combine (shadowRoot, string generation)

    if Directory.Exists shadow then
        Directory.Delete (shadow, true)

    copyDirectory output shadow

    let info =
        ProcessStartInfo (
            "dotnet",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            StandardOutputEncoding = UTF8Encoding false
        )

    info.ArgumentList.Add (Path.Combine (shadow, Path.GetFileName app))

    for argument in appArgs do
        info.ArgumentList.Add argument

    let proc = Process.Start info
    proc.StandardInput.AutoFlush <- true
    let lines = new BlockingCollection<string> ()

    let reader =
        Thread (
            (fun () ->
                try
                    let mutable line = proc.StandardOutput.ReadLine ()

                    while not (isNull line) do
                        lines.Add line
                        line <- proc.StandardOutput.ReadLine ()
                with _ ->
                    ()

                lines.CompleteAdding ()),
            IsBackground = true
        )

    reader.Start ()
    log $"started build of {built:u} from {shadow}"

    {
        Process = proc
        Lines = lines
        Built = built
        Shadow = shadow
    }

let stop (child: Child) =
    try
        child.Process.StandardInput.Close ()

        if not (child.Process.WaitForExit 3000) then
            child.Process.Kill true
    with _ ->
        ()

    try
        Directory.Delete (child.Shadow, true)
    with _ ->
        ()

let stdout' = Console.OpenStandardOutput ()
let toClient = new StreamWriter (stdout', UTF8Encoding false, AutoFlush = true)
let fromClient = new StreamReader (Console.OpenStandardInput (), UTF8Encoding false)
let mutable child = start ()
let mutable initialize: string = null
let mutable initialized: string = null
let mutable nextId = 0

/// The accepted messages, oldest first, as {"message", "args"} objects.
let journal = ResizeArray<JsonNode>()

let idOf (node: JsonNode) =
    match node["id"] with
    | null -> None
    | id -> Some (id.ToJsonString ())

/// Sends a line to the child and returns the response line with the same id; other lines go to the client.
let exchange (line: string) (id: string) =
    child.Process.StandardInput.WriteLine line
    let mutable response = None

    while response.IsNone do
        let mutable received = null

        match child.Lines.TryTake (&received, Timeout.Infinite) with
        | true ->
            let node = JsonNode.Parse received

            if idOf node = Some id && isNull node["method"] then
                response <- Some received
            else
                toClient.WriteLine received
        | _ -> failwith "the application closed its output"

    response.Value

/// Sends a request of the supervisor's own, with a fresh id, and returns its response.
let request (method: string) (parameters: JsonNode) =
    nextId <- nextId + 1
    let id = $"\"supervise-{nextId}\""
    let message = JsonObject ()
    message["jsonrpc"] <- JsonValue.Create "2.0"
    message["id"] <- JsonNode.Parse id
    message["method"] <- JsonValue.Create method
    message["params"] <- parameters
    JsonNode.Parse (exchange (message.ToJsonString ()) id)

let textOf (result: JsonNode) =
    match result["result"] with
    | null -> result.ToJsonString ()
    | body ->
        match body["content"] with
        | :? JsonArray as content ->
            String.Join (
                "",
                content
                |> Seq.choose (fun block ->
                    match block["text"] with
                    | null -> None
                    | text -> Some (text.GetValue<string>()))
            )
        | _ -> ""

/// Starts the new build, repeats the handshake, replays the journal, and returns the line that reports it.
let reload () =
    stop child
    child <- start ()
    let built = child.Built.ToLocalTime().ToString "HH:mm:ss"
    let mutable note = $"[reloaded the build of {built}"

    if not (isNull initialize) then
        let node = JsonNode.Parse initialize
        exchange initialize (idOf node).Value |> ignore

        if not (isNull initialized) then
            child.Process.StandardInput.WriteLine initialized

    if journal.Count > 0 then
        let arguments = JsonObject ()
        let messages = JsonArray ()

        for message in journal do
            messages.Add (message.DeepClone ())

        arguments["messages"] <- messages
        let parameters = JsonObject ()
        parameters["name"] <- JsonValue.Create "dispatch"
        parameters["arguments"] <- arguments
        let result = request "tools/call" parameters
        let text = textOf result
        let final = text.Split ('\n') |> Array.last

        let failed =
            (match result["result"] with
             | null -> true
             | body ->
                 (match body["isError"] with
                  | null -> false
                  | e -> e.GetValue<bool>()))

        if failed then
            note <-
                note
                + $"; the replay of {journal.Count} messages failed: {text}"

            journal.Clear ()
        else
            note <-
                note
                + $"; replayed {journal.Count} messages: {final}"

    note + "]"

/// Records the messages of an accepted dispatch call.
let record (call: JsonNode) (response: JsonNode) =
    let accepted =
        match response["result"] with
        | null -> false
        | body ->
            match body["isError"] with
            | null -> true
            | e -> not (e.GetValue<bool>())

    if accepted then
        match call["params"]["arguments"] with
        | null -> ()
        | arguments ->
            match arguments["messages"] with
            | :? JsonArray as messages ->
                for message in messages do
                    journal.Add (message.DeepClone ())
            | _ ->
                let message = JsonObject ()
                message["message"] <- arguments["message"].DeepClone()

                match arguments["args"] with
                | null -> ()
                | args -> message["args"] <- args.DeepClone ()

                journal.Add message

let prefix (note: string) (response: string) =
    let node = JsonNode.Parse response

    match node["result"] with
    | null -> response
    | body ->
        match body["content"] with
        | :? JsonArray as content when content.Count > 0 ->
            let first = content.[0]

            match first.["text"] with
            | null -> response
            | text ->
                first.["text"] <- JsonValue.Create (note + "\n" + text.GetValue<string>())
                // A client may show structured content in place of the text, which would hide the note.
                body.AsObject().Remove "structuredContent"
                |> ignore

                node.ToJsonString ()
        | _ -> response

/// The initialize response with the reload behaviour added to the server's instructions.
let announce (response: string) =
    let node = JsonNode.Parse response

    match node["result"] with
    | null -> response
    | body ->
        let given =
            match body["instructions"] with
            | null -> ""
            | text -> text.GetValue<string>() + " "

        body["instructions"] <-
            JsonValue.Create (
                given
                + "The server reloads the application after a rebuild: run dotnet build or dotnet test as usual, and the next tool call serves the new build with every dispatched message replayed, so a fix can be checked live without a restart."
            )

        node.ToJsonString ()

let mutable line = fromClient.ReadLine ()

while not (isNull line) do
    try
        let node = JsonNode.Parse line

        let method =
            match node["method"] with
            | null -> ""
            | m -> m.GetValue<string>()

        match idOf node with
        | None ->
            if method = "notifications/initialized" then
                initialized <- line

            child.Process.StandardInput.WriteLine line
        | Some id ->
            if method = "initialize" then
                initialize <- line

            let note =
                if
                    method = "tools/call"
                    && (child.Process.HasExited || stamp () > child.Built)
                then
                    Some (reload ())
                else
                    None

            let response =
                let response = exchange line id

                if method = "initialize" then
                    announce response
                else
                    response

            if method = "tools/call" then
                match node["params"]["name"] with
                | null -> ()
                | name when name.GetValue<string>() = "dispatch" -> record node (JsonNode.Parse response)
                | _ -> ()

            toClient.WriteLine (
                match note with
                | Some note -> prefix note response
                | None -> response
            )
    with ex ->
        log $"error: {ex.Message}"

        match
            (try
                idOf (JsonNode.Parse line)
             with _ ->
                 None)
        with
        | Some id ->
            let error = JsonObject ()
            error["code"] <- JsonValue.Create -32603
            error["message"] <- JsonValue.Create ex.Message
            let reply = JsonObject ()
            reply["jsonrpc"] <- JsonValue.Create "2.0"
            reply["id"] <- JsonNode.Parse id
            reply["error"] <- error
            toClient.WriteLine (reply.ToJsonString ())
        | None -> ()

    line <- fromClient.ReadLine ()

stop child
