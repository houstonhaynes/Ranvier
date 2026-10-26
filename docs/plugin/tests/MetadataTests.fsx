#r "nuget: Nacara.Core, 2.0.0-beta.6"
#r "nuget: Mono.Cecil, 0.11.6"
#load "../LiveExampleMetadata.fs"

open System
open System.IO
open System.IO.Compression
open Mono.Cecil
open Nacara.Core
open Nacara.Plugins

let check name condition =
    if not condition then
        failwith name

    printfn "PASS %s" name

let raises name work =
    let failed =
        try
            work ()
            false
        with
        | :? InvalidOperationException
        | :? FileNotFoundException -> true

    check name failed

let assetsFile =
    Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, "../../obj/project.assets.json"))

let references =
    [
        LiveExampleMetadata.nuget assetsFile "Xantham.Fable.Core.TS" "0.2.1" "net8.0" "Xantham.Fable.Core.TS"
        LiveExampleMetadata.nuget assetsFile "Xantham.Fable.Core" "0.2.0" "net8.0" "Xantham.Fable.Core"
    ]

let recordedCase =
    LiveExampleMetadata.nuget assetsFile "xantham.fable.core.ts" "0.2.1" "net8.0" "xantham.fable.core.ts"

check "resolved filename uses manifest casing on case-sensitive filesystems" (String.Equals (recordedCase, references.Head, StringComparison.Ordinal))

let undeclared =
    Path.Combine (
        Path.GetTempPath (),
        "ranvier-metadata-"
        + Guid.NewGuid().ToString("N")
        + ".json"
    )

try
    let root = System.Text.Json.Nodes.JsonNode.Parse (File.ReadAllText assetsFile)
    root["libraries"] <- System.Text.Json.Nodes.JsonObject ()
    File.WriteAllText (undeclared, root.ToJsonString ())

    raises "warm cache cannot satisfy a package absent from the restored project" (fun () ->
        LiveExampleMetadata.nuget undeclared "Xantham.Fable.Core.TS" "0.2.1" "net8.0" "Xantham.Fable.Core.TS"
        |> ignore)
finally
    File.Delete undeclared

let liveWithWorker includeWorker =
    Site.plugin
        { new IPlugin with
            member _.Name = "live-example-fixture"

            member _.Configure registry =
                let refs =
                    registry
                    |> Registry.asset (
                        CopyFile (
                            AbsolutePath.create typeof<obj>.Assembly.Location,
                            RelativePath.create "assets/live-example/refs/test-tag/FSharp.Core.dll.txt"
                        )
                    )

                if includeWorker then
                    refs
                    |> Registry.asset (WriteText ("worker", RelativePath.create "assets/live-example/live-example.js"))
                else
                    refs
        }

let live = liveWithWorker true

let registry =
    Site.create "metadata tests"
    |> live
    |> LiveExampleMetadata.register references
    |> fun site -> Registry.ofPlugins site.Plugins

let renamed =
    Path.Combine (
        Path.GetTempPath (),
        "renamed-metadata-"
        + Guid.NewGuid().ToString("N")
        + ".dll"
    )

try
    File.Copy (references.Head, renamed)

    let renamedRegistry =
        Site.create "renamed metadata"
        |> live
        |> LiveExampleMetadata.register [ renamed ]
        |> fun site -> Registry.ofPlugins site.Plugins

    check
        "checker asset uses the DLL assembly identity rather than its filename"
        (renamedRegistry.Assets
         |> List.exists (fun asset -> RelativePath.value asset.Destination = "assets/live-example/refs/test-tag/Xantham.Fable.Core.TS.dll.txt"))
finally
    File.Delete renamed

for source in references do
    let expected =
        "assets/live-example/refs/test-tag/"
        + Path.GetFileName source
        + ".txt"

    check
        ("metadata asset " + Path.GetFileName source)
        (registry.Assets
         |> List.exists (function
             | WriteBytes (bytes, destination) ->
                 (RelativePath.value destination).Replace('\\', '/') = expected
                 && bytes.Length > 0
             | _ -> false))

    let bytes =
        registry.Assets
        |> List.pick (function
            | WriteBytes (bytes, destination) when RelativePath.value destination = expected -> Some bytes
            | _ -> None)

    use original = AssemblyDefinition.ReadAssembly source
    use input = new MemoryStream (bytes, false)
    use prepared = AssemblyDefinition.ReadAssembly input
    check "standalone reference preserves assembly identity" (prepared.Name.FullName = original.Name.FullName)
    check "standalone reference preserves declared types" (prepared.MainModule.Types.Count = original.MainModule.Types.Count)

    for resource in original.MainModule.Resources do
        let name = resource.Name.Replace ("CompressedData", "Data")

        let actual =
            prepared.MainModule.Resources
            |> Seq.find (fun item -> item.Name = name)
            :?> EmbeddedResource

        let original = resource :?> EmbeddedResource

        let expected =
            if resource.Name.Contains "CompressedData" then
                use raw = original.GetResourceStream ()
                use decoder = new DeflateStream (raw, CompressionMode.Decompress)
                use expanded = new MemoryStream ()
                decoder.CopyTo expanded
                expanded.ToArray ()
            else
                original.GetResourceData ()

        check
            ("standalone reference preserves resource data "
             + name)
            (actual.GetResourceData () = expected)

    check
        "standalone reference contains no compressed F# metadata"
        (prepared.MainModule.Resources
         |> Seq.forall (fun item -> not (item.Name.Contains "CompressedData")))

let plainSource = typeof<obj>.Assembly.Location

let plainRegistry =
    Site.create "plain metadata"
    |> live
    |> LiveExampleMetadata.register [ plainSource ]
    |> fun site -> Registry.ofPlugins site.Plugins

check
    "reference without compressed F# metadata is byte-for-byte unchanged"
    (plainRegistry.Assets
     |> List.exists (function
         | WriteBytes (bytes, _) -> bytes = File.ReadAllBytes plainSource
         | _ -> false))

let transform path text =
    registry.AssetTransforms.Head.Transform
        {
            Path = RelativePath.create path
            Content = text
            Diagnostics = DiagnosticSink (DiagnosticBag (), "metadata-tests")
            IsWatch = false
        }

for worker in
    [
        "const extraRefs = [\"Browser.Dom\", \"Browser.Blob\", \"Browser.Event\", \"Browser.WebStorage\"];"
        "var e=[\"Browser.Dom\",\"Browser.Blob\",\"Browser.Event\",\"Browser.WebStorage\"];"
    ] do
    let patched = transform "assets/live-example/live-example.js" worker
    check "worker requests both additional assemblies" (patched.Contains ",\"Xantham.Fable.Core.TS\",\"Xantham.Fable.Core\"]")

check "unrelated JavaScript is unchanged" (transform "other.js" "untouched" = "untouched")

raises "changed upstream worker fails explicitly" (fun () ->
    transform "assets/live-example/live-example.js" "changed worker"
    |> ignore)

raises "missing restored package fails explicitly" (fun () ->
    LiveExampleMetadata.nuget assetsFile "missing-package" "0.0.0" "net8.0" "Missing"
    |> ignore)

raises "registration requires LiveExample references" (fun () ->
    Site.create "missing live plugin"
    |> LiveExampleMetadata.register references
    |> fun site -> Registry.ofPlugins site.Plugins |> ignore)

raises "a missing worker asset fails before transforming" (fun () ->
    Site.create "renamed worker"
    |> liveWithWorker false
    |> LiveExampleMetadata.register references
    |> fun site -> Registry.ofPlugins site.Plugins |> ignore)

check
    "empty registration is opt-in"
    ((Site.create "plain site"
      |> LiveExampleMetadata.register [])
        .Plugins.IsEmpty)
