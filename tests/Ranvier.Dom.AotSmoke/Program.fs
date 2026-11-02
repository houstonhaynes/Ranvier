module Ranvier.Dom.AotSmoke

open System
open System.Runtime.CompilerServices
open Ranvier.Dom

type private Poison() =
    override _.ToString() =
        failwith "unknown payload was formatted"

let private expect name expected actual =
    if actual <> expected then
        raise (
            InvalidOperationException (
                name
                + ": expected "
                + expected
                + ", got "
                + actual
            )
        )

[<EntryPoint>]
let main _ =
    let font: ResourceFont = { Family = "Inter" }

    let response: ResourceResponse =
        {
            ContentType = "text/plain"
            Bytes = [| 1uy; 2uy |]
        }

    let image: ResourceImage =
        {
            Width = 16
            Height = 9
            SourceUrl = "source"
            TransportUrl = "transport"
        }

    let info: ResourceImageInfo =
        {
            Width = 16
            Height = 9
            SourceUrl = "source"
        }

    let snapshot: SnapshotImage =
        {
            PixelWidth = 1
            PixelHeight = 1
            Format = ImageEncoding.Rgba8
            PixelColorSpace = SnapshotColorSpace.Srgb
            Data = [| 1uy; 2uy; 3uy; 4uy |]
        }

    let outcome = ResourceOutcome.Succeeded font
    expect "font" "Succeeded { Family = \"Inter\" }" (outcome.ToString ())
    expect "state" "Settled (Succeeded { Family = \"Inter\" })" ((ResourceState.Settled outcome).ToString())
    expect "response" "Succeeded { ContentType = \"text/plain\"\n            Bytes = [|1uy; 2uy|] }" ((ResourceOutcome.Succeeded response).ToString())

    expect
        "image"
        "Succeeded { Width = 16\n            Height = 9\n            SourceUrl = \"source\"\n            TransportUrl = \"transport\" }"
        ((ResourceOutcome.Succeeded image).ToString())

    expect
        "image info"
        "Succeeded { Width = 16\n            Height = 9\n            SourceUrl = \"source\" }"
        ((ResourceOutcome.Succeeded info).ToString())

    expect "bytes" "Succeeded [|1uy; 2uy|]" ((ResourceOutcome.Succeeded [| 1uy; 2uy |]).ToString())
    expect "null font" "Succeeded null" ((ResourceOutcome.Succeeded (Unchecked.defaultof<ResourceFont>)).ToString())
    expect "null bytes" "Succeeded null" ((ResourceOutcome.Succeeded (Unchecked.defaultof<byte array>)).ToString())
    expect "empty bytes" "Succeeded [||]" ((ResourceOutcome.Succeeded ([||]: byte array)).ToString())

    let longBytes =
        (ResourceOutcome.Succeeded [| for i in 0..149 -> byte i |]).ToString()

    if not (longBytes.Contains "99uy; ...|]") then
        raise (InvalidOperationException ("long bytes: " + longBytes))

    expect
        "snapshot"
        "Succeeded { PixelWidth = 1\n            PixelHeight = 1\n            Format = Rgba8\n            PixelColorSpace = Srgb\n            Data = [|1uy; 2uy; 3uy; 4uy|] }"
        ((ResourceOutcome.Succeeded snapshot).ToString())

    if not RuntimeFeature.IsDynamicCodeSupported then
        expect "unknown" "Succeeded <'T>" ((ResourceOutcome.Succeeded (Poison ())).ToString())

    Console.WriteLine "resource display checks passed"
    0
