/// <summary>Event dispatch fixture: clickable rows mounted without a boundary, as authored elements and as a template list.</summary>
module DispatchPerf

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template

module H = Ranvier.View.Foundation.Generated.Html

let mutable private clicks = 0

/// <summary>The number of handler invocations since the module loaded.</summary>
let clickCount () : int = clicks

let private mountView (host: HTMLElement) (graph: Graph) view : unit -> unit =
    let mounted =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context -> Program.run view context.Backend)

    fun () ->
        mounted.Dispose ()
        graph.Dispose ()

/// <summary>Mounts <c>count</c> authored buttons whose click handlers increment a counter, and returns the disposal.</summary>
let mountAuthored (host: HTMLElement) (count: int) : unit -> unit =
    let graph = new Graph ()

    let button (_: int) =
        H.Program.button [| H.Events.onClick (fun _ -> clicks <- clicks + 1) |] [||]

    mountView host graph (Program.fragment (Array.init count button))

let private clickRow =
    Component.define (fun _ (item: unit -> int) -> item ()) (fun t ->
        t.a [| t.Events.onClick (fun _ _ -> clicks <- clicks + 1) |] [| t.text (fun (id: int) -> string id) |])

let private clickList =
    Component.define (fun _ (rows: Signal<int array>) -> rows) (fun t ->
        t.element (HtmlTag "div") [||] [| t.each (fun (id: int) -> id) (fun rows -> rows.Value) clickRow |])

/// <summary>Mounts a template list of <c>count</c> anchors whose click handlers increment a counter, and returns the disposal.</summary>
let mountTemplate (host: HTMLElement) (count: int) : unit -> unit =
    let graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal (Array.init count id))
    mountView host graph (Component.render clickList rows)
