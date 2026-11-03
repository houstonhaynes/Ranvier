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

    expect "child-list mutation" "ChildList" (MutationType.ChildList.ToString ())
    expect "attribute mutation" "Attributes" (MutationType.Attributes.ToString ())
    expect "character-data mutation" "CharacterData" (MutationType.CharacterData.ToString ())

    let optionsText = MutationObserverOptions.Default.ToString ()

    if
        not (
            optionsText.Contains "ChildList = false"
            && optionsText.Contains "AttributeFilter = None"
        )
    then
        raise (InvalidOperationException ("mutation options: " + optionsText))

    let mutation: MutationRecord =
        {
            Type = MutationType.Attributes
            Target = Unchecked.defaultof<Node>
            AddedNodes = [||]
            RemovedNodes = null
            PreviousSibling = None
            NextSibling = Some Unchecked.defaultof<Node>
            AttributeName = Some "class"
            AttributeNamespace = None
            OldValue = Some ""
        }

    let mutationText = mutation.ToString ()

    if
        not (
            mutationText.Contains "Type = Attributes"
            && mutationText.Contains "Target = null"
            && mutationText.Contains "OldValue = Some \"\""
        )
    then
        raise (InvalidOperationException ("mutation record: " + mutationText))

    let mutable callbackCalls = 0

    let handle: MutationObserverHandle =
        {
            Observe = fun _ _ -> callbackCalls <- callbackCalls + 1
            Disconnect = fun () -> callbackCalls <- callbackCalls + 1
            TakeRecords =
                fun () ->
                    callbackCalls <- callbackCalls + 1
                    [||]
            Release = fun () -> callbackCalls <- callbackCalls + 1
        }

    let provider: MutationObservationProvider =
        {
            Create =
                fun _ _ ->
                    callbackCalls <- callbackCalls + 1
                    handle
        }

    if
        not (
            handle.ToString().Contains "Observe ="
            && provider.ToString().Contains "Create ="
        )
        || callbackCalls <> 0
    then
        raise (InvalidOperationException "mutation callback diagnostics invoked a callback")

    let boundedOptions =
        { MutationObserverOptions.Default with
            AttributeFilter = Some (Array.create 150 "id")
        }

    if not (boundedOptions.ToString().Contains "...") then
        raise (InvalidOperationException "mutation attribute-filter diagnostics were not bounded")

    expect "implicit intersection root" "Implicit" (IntersectionObserverRoot.Implicit.ToString ())
    expect "document intersection root" "Document" (IntersectionObserverRoot.Document.ToString ())

    expect "null intersection root" "Element null" ((IntersectionObserverRoot.Element Unchecked.defaultof<Element>).ToString())

    let intersectionOptions =
        { IntersectionObserverInit.Default with
            Threshold = Array.create 150 0.5
        }

    let intersectionOptionsText = intersectionOptions.ToString ()

    if
        not (
            intersectionOptionsText.Contains "Threshold ="
            && intersectionOptionsText.Contains "..."
        )
    then
        raise (InvalidOperationException "intersection threshold diagnostics were not bounded")

    let rect: IntersectionObserverRect =
        {
            X = 1.25
            Y = 2.0
            Width = 3.0
            Height = 4.0
        }

    let intersectionEntry: IntersectionObserverEntry =
        {
            Target = Unchecked.defaultof<Element>
            Time = 5.0
            RootBounds = Some rect
            BoundingClientRect = rect
            IntersectionRect = rect
            IsIntersecting = true
            IntersectionRatio = 0.5
        }

    if
        not (
            rect.ToString().Contains "X = 1.25"
            && intersectionEntry.ToString().Contains "Target = null"
            && intersectionEntry.ToString().Contains "IntersectionRatio = 0.5"
        )
    then
        raise (InvalidOperationException "intersection entry diagnostics omitted values")

    let mutable intersectionCalls = 0

    let intersectionHandle: IntersectionObserverHandle =
        {
            Observe = fun _ -> intersectionCalls <- intersectionCalls + 1
            Unobserve = fun _ -> intersectionCalls <- intersectionCalls + 1
            Disconnect = fun () -> intersectionCalls <- intersectionCalls + 1
            TakeRecords =
                fun () ->
                    intersectionCalls <- intersectionCalls + 1
                    [||]
            Release = fun () -> intersectionCalls <- intersectionCalls + 1
        }

    let intersectionProvider: IntersectionObservationProvider =
        {
            Now =
                fun () ->
                    intersectionCalls <- intersectionCalls + 1
                    1.0
            Create =
                fun _ _ ->
                    intersectionCalls <- intersectionCalls + 1
                    intersectionHandle
        }

    if
        not (
            intersectionHandle.ToString().Contains "Observe ="
            && intersectionProvider.ToString().Contains "Create ="
        )
        || intersectionCalls <> 0
    then
        raise (InvalidOperationException "intersection callback diagnostics invoked a callback")

    Console.WriteLine "intersection display checks passed"
    Console.WriteLine "mutation display checks passed"
    Console.WriteLine "resource display checks passed"
    0
