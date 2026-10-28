module Ranvier.View.WebCore.Tests.PaintTests

open Expecto
open Ranvier
open Ranvier.View
open Ranvier.View.WebCore.Tests.Support

let private label =
    Component.define (fun _ (text: Signal<string>) -> text) (fun t ->
        t.div
            [|
                t.Attrs.attribute ("style", "font-size:96px;line-height:120px;color:black")
            |]
            [| t.text (fun (text: Signal<string>) -> text.Value) |])

let private differing (a: byte array) (b: byte array) =
    let mutable count = 0

    for i in 0 .. a.Length - 1 do
        if a[i] <> b[i] then
            count <- count + 1

    count

[<Tests>]
let tests =
    testList
        "Paint"
        [
            hostCase "a mounted component paints and a signal write changes the pixels"
            <| fun host ->
                let blank =
                    let struct (width, height) = host.PaintSize
                    let pixels = Array.zeroCreate<byte>(width * height * 4)
                    host.Paint pixels
                    pixels

                use graph = new Graph ()
                let text = graph.Run (fun () -> Api.createSignal "A")
                use mounted = mount host graph label text
                let struct (width, height) = host.PaintSize
                let first = Array.zeroCreate<byte>(width * height * 4)
                host.Paint first
                Expect.isGreaterThan (differing blank first) 100 "the component painted"

                mounted.Act (host, fun () -> text.Value <- "W")
                Expect.isTrue host.FrameRequested "the write asked for a frame"
                host.Frame ()
                Expect.isTrue host.DirtyRect.IsSome "the frame invalidated an area"
                let second = Array.copy first
                host.PaintDirty second |> ignore
                Expect.isGreaterThan (differing first second) 100 "the dirty repaint changed the pixels"

                let full = Array.zeroCreate<byte>(width * height * 4)
                host.Paint full
                Expect.equal (differing second full) 0 "the dirty repaint matches a full paint"
        ]
