module Ranvier.View.Twin.Tests.ConformanceTests

open System.IO
open Expecto
open Ranvier.View.Twin
open Ranvier.View.Testing

let output () =
    Path.Combine (TwinHarness.repository (), "examples", "Fable.Ranvier.Playground", "generated-conformance", "twin")

/// <summary>
/// What a case left on its document and the document's html, head and body: live listeners and the view's expandos.
/// The jsdom runner fails a case for the same leftovers, since its cases share one document.
/// </summary>
let leftovers (document: TwinDocument) : string list =
    [
        for label, node in
            [
                "document", document :> TwinNode
                "html", document.Html
                "head", document.Head
                "body", document.Body
            ] do
            for eventType in Events.listenerTypesOn node do
                $"{label} '{eventType}' listener"

            for name in Ranvier.View.Foundation.Native.expandoNames (box node.Native) do
                if name.StartsWith "$$ranvier" then
                    $"{label} expando {name}"
    ]

/// <summary>Runs a case on a fresh twin host whose artefacts go to <c>directory</c>; a case that leaves state fails.</summary>
let runIn (directory: string) (run: Conformance.Harness -> unit) =
    let host = TwinHost.Create ()

    try
        run (TwinHarness.create host directory)

        match leftovers host.Document with
        | [] -> ()
        | left -> failwithf "The case left state on the shared document: %s" (String.concat "; " left)
    finally
        (host.Document :> System.IDisposable).Dispose()

// The artefacts of an earlier run go before the first case writes, so a renamed case leaves none behind.
let private cleared =
    lazy
        (let root = output ()

         if Directory.Exists root then
             Directory.Delete (root, true))

let private runCase (moduleName: string) (name: string) (run: Conformance.Harness -> unit) () =
    cleared.Force ()
    runIn (Path.Combine (output (), moduleName, TwinHarness.slug name)) run

[<Tests>]
let tests =
    testList
        "Conformance"
        [
            for moduleName, cases in ConformanceCases.modules do
                testList
                    moduleName
                    [
                        for name, run in cases do
                            if Conformance.skippedOn "twin" name then
                                ptestCase name ignore
                            else
                                testCase name (runCase moduleName name run)
                    ]
        ]
