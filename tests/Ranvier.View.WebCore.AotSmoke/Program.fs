module Ranvier.View.WebCore.AotSmoke.Program

open System
open Fable.Core.TS.Dom
open Ranvier
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.WebCore

type Counter = { Count: Signal<int> }

let private counter =
    Component.define (fun _ (start: int) -> { Count = Api.createSignal start }) (fun t ->
        t.button
            [|
                t.Attrs.attribute ("style", "font-size:48px;width:200px;height:80px")
                t.Events.on ("click", fun state _ -> state.Count.Value <- state.Count.Value + 1)
            |]
            [| t.text (fun state -> string state.Count.Value) |])

let mutable private failures = 0

let private check (name: string) (ok: bool) (detail: string) =
    if ok then
        Console.WriteLine ("ok   " + name)
    else
        failures <- failures + 1
        Console.WriteLine ("FAIL " + name + ": " + detail)

let private paint (host: WebCoreHost) =
    let struct (width, height) = host.PaintSize
    let pixels = Array.zeroCreate<byte>(width * height * 4)
    host.Paint pixels
    pixels

[<EntryPoint>]
let main _ =
    use host = WebCoreHost.Create (320, 200)
    let blank = paint host
    use graph = new Graph ()
    let container = host.Document.createElement "div"
    host.Document.body.appendChild container |> ignore

    let mounted =
        Mounting.mount graph None (fun errors -> raise (AggregateException (errors))) container None (fun context ->
            Program.run (Component.render counter 1) context.Backend)

    check "the mount rendered" (container.textContent = "1") container.textContent
    let first = paint host
    check "the mount painted" (first <> blank) "the page is blank"

    let button = (container.querySelector<HTMLElement> "button").Value
    let mutable x = 0.0
    let mutable y = 0.0
    let mutable width = 0.0
    let mutable height = 0.0

    RanvierWebCoreAbi.Native.rv_element_rect ((box button :?> NativeObject).Handle, &x, &y, &width, &height)
    |> ignore

    host.Click (int (x + width / 2.0), int (y + height / 2.0))
    |> ignore

    host.Cycle ()
    check "a native click updated the text" (container.textContent = "2") container.textContent
    host.Frame ()
    let second = paint host
    check "the update repainted" (second <> first) "the pixels are unchanged"
    check "no listener errors" (host.Errors.Count = 0) (string host.Errors.Count)
    graph.Run (fun () -> mounted.Dispose ())
    failures
