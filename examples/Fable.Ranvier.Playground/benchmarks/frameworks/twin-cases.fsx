#I "../../../../tests/Ranvier.View.Twin.Tests/bin/Release/net10.0"
#r "Fable.Core.dll"
#r "Xantham.Fable.Core.dll"
#r "Xantham.Fable.Core.TS.dll"
#r "Ranvier.dll"
#r "Ranvier.View.dll"
#r "Ranvier.View.Dom.dll"
#r "Ranvier.View.Testing.dll"
#r "Ranvier.View.Twin.dll"
#r "Ranvier.View.Twin.Tests.dll"

open System
open System.Diagnostics
open System.IO
open System.Text.Json
open System.Reflection
open System.Collections
open System.Collections.Generic
open Ranvier.View.Twin
open Ranvier.View.Twin.Tests

let now () =
    float (Stopwatch.GetTimestamp ())
    / float Stopwatch.Frequency
    * 1000.0

let samples =
    Array.init 10 (fun _ ->
        let host = TwinHost.Create ()
        use document = host.Document
        let harness = TwinHarness.create host (Path.GetTempPath ())
        let keyed = LoopBench.keyed (document.Native :?> Fable.Core.TS.Dom.Document) now
        let hydration = LoopBench.boardHydration harness now

        {|
            mount = keyed[0]
            reorder = keyed[1]
            hydration = hydration
        |})

let bridgeAssembly = typeof<NodeProxy>.Assembly
let bridgeCore = bridgeAssembly.GetType "Ranvier.View.Twin.BridgeCore"
let entryType = bridgeAssembly.GetType "Ranvier.View.Twin.Entry"
let flags = BindingFlags.NonPublic ||| BindingFlags.Public

let table =
    bridgeCore.GetField("table", flags ||| BindingFlags.Static).GetValue null :?> IDictionary

let runField = entryType.GetField ("Run@", flags ||| BindingFlags.Instance)
let acceptsField = entryType.GetField ("Accepts@", flags ||| BindingFlags.Instance)

let countMountCalls () =
    let counts = Dictionary<string, int>()
    let restores = ResizeArray<unit -> unit>()
    let mutable recording = false

    try
        for item in table do
            let item = unbox<DictionaryEntry> item
            let key = string item.Key

            for entry in item.Value :?> IEnumerable do
                let original =
                    runField.GetValue entry
                    |> unbox<TwinNode -> obj array -> obj>

                let wrap node args =
                    if recording then
                        counts[key] <-
                            (match counts.TryGetValue key with
                             | true, n -> n
                             | _ -> 0)
                            + 1

                    original node args

                restores.Add (fun () -> runField.SetValue (entry, original))
                runField.SetValue (entry, wrap)

        let host = TwinHost.Create ()
        use document = host.Document
        let mutable clockCalls = 0

        LoopBench.keyed (document.Native :?> Fable.Core.TS.Dom.Document) (fun () ->
            clockCalls <- clockCalls + 1
            recording <- clockCalls = 1
            now ())
        |> ignore

        counts
    finally
        for restore in restores do
            restore ()

let calls = countMountCalls ()

let bridgeCosts () =
    let host = TwinHost.Create ()
    use document = host.Document
    let element = document.CreateElement "li"
    let text = document.CreateText "x"
    element.AppendChild text |> ignore
    let native = element.Native
    let nativeText = text.Native :?> Fable.Core.TS.Dom.Text

    let implementation key interfaceType =
        table[key] :?> IEnumerable
        |> Seq.cast<obj>
        |> Seq.find (fun entry -> (acceptsField.GetValue entry |> unbox<Type -> bool>) interfaceType)
        |> runField.GetValue
        |> unbox<TwinNode -> obj array -> obj>

    let pairs =
        [|
            "get_firstChild/0", typeof<Fable.Core.TS.Dom.Node>, element :> TwinNode, [||], (fun () -> box native.firstChild)
            "get_nodeType/0", typeof<Fable.Core.TS.Dom.Node>, element :> TwinNode, [||], (fun () -> box native.nodeType)
            "cloneNode/1", typeof<Fable.Core.TS.Dom.Node>, element :> TwinNode, [| box true |], (fun () -> box (native.cloneNode true))
            "appendChild/1", typeof<Fable.Core.TS.Dom.Node>, element :> TwinNode, [| box text |], (fun () -> box (native.appendChild nativeText))
            "set_data/1",
            typeof<Fable.Core.TS.Dom.CharacterData>,
            text :> TwinNode,
            [| box "x" |],
            (fun () ->
                nativeText.data <- "x"
                null)
        |]

    let iterations = 1000

    let measure action =
        let started = now ()

        for _ in 1..iterations do
            action () |> ignore

        (now () - started) * 1000000.0 / float iterations

    pairs
    |> Array.map (fun (name, interfaceType, node, args, proxy) ->
        let run = implementation name interfaceType

        let direct () =
            run node args

        for _ in 1..100 do
            direct () |> ignore
            proxy () |> ignore

        let measured =
            Array.init 10 (fun round ->
                if round % 2 = 0 then
                    let proxyNs = measure proxy
                    let directNs = measure direct
                    proxyNs, directNs
                else
                    let directNs = measure direct
                    let proxyNs = measure proxy
                    proxyNs, directNs)

        {|
            memberName = name
            calls = calls[name]
            iterations = iterations
            proxyNs = measured |> Array.map fst
            directNs = measured |> Array.map snd
            overheadNs =
                measured
                |> Array.map (fun (proxy, direct) -> proxy - direct)
        |})

let result =
    {|
        host = "twin"
        configuration = "Release"
        unit = "milliseconds"
        samples = samples
        nodeBridgeCalls = calls
        bridgeCosts = bridgeCosts ()
    |}

let json =
    JsonSerializer.Serialize (result, JsonSerializerOptions (WriteIndented = true))

let arguments = fsi.CommandLineArgs |> Array.skip 1

match arguments |> Array.tryFindIndex ((=) "--out") with
| Some index when index + 1 < arguments.Length ->
    let path = Path.GetFullPath arguments[index + 1]

    Directory.CreateDirectory (Path.GetDirectoryName path)
    |> ignore

    File.WriteAllText (path, json + "\n")
| _ -> ()

printfn "%s" json
