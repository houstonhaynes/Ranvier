/// <summary>Boundary reveal fixture: rows under one boundary that reveals once one async source settles.</summary>
module BoundaryPerf

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation

/// <summary>Mounts <c>width</c> rows reading one pending source under a boundary, settles the source, and returns the disposal.</summary>
let mountReveal (host: HTMLElement) (width: int) : unit -> unit =
    let graph = new Graph ()
    let source = graph.Run (fun () -> Api.createAsyncSource<string>())

    let row (index: int) =
        Program.element Div [| Attribute ("class", "row") |] [| Program.bind (=) (fun () -> source.Value + string index) |]

    let view =
        Program.ops.Boundary (
            Program.text "loading",
            (fun failure -> Program.text failure.Error.Message),
            (fun _ -> Program.fragment (Array.init width row))
        )

    let mounted =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context -> Program.run view context.Backend)

    graph.Run (fun () -> source.Settle "ready")

    fun () ->
        mounted.Dispose ()
        graph.Dispose ()

let private mountSwitch (bounded: bool) (host: HTMLElement) (width: int) : unit -> unit =
    let graph = new Graph ()
    let source = graph.Run (fun () -> Api.createAsyncSource<string>())
    let arm = graph.Run (fun () -> Api.createSignal 0)
    graph.Run (fun () -> source.Settle "ready")

    let row (index: int) =
        Program.element Div [| Attribute ("class", "row") |] [| Program.bind (=) (fun () -> source.Value + string index) |]

    let rows () =
        Program.fragment (Array.init width row)

    let arm1 () =
        if bounded then
            Program.ops.Boundary (Program.text "loading", (fun failure -> Program.text failure.Error.Message), (fun _ -> rows ()))
        else
            rows ()

    let view =
        Branch.select Program.ops id (fun () -> arm.Value) (fun current -> if current () = 0 then Program.ops.Empty else arm1 ())

    let mounted =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context -> Program.run view context.Backend)

    graph.Run (fun () -> arm.Value <- 1)

    fun () ->
        mounted.Dispose ()
        graph.Dispose ()

/// <summary>Mounts an empty branch over an already settled source, then switches to a branch holding a boundary of <c>width</c> rows, so the boundary mounts inside a flush; returns the disposal.</summary>
let mountSwitchReveal (host: HTMLElement) (width: int) : unit -> unit =
    mountSwitch true host width

/// <summary>The same switch as <c>mountSwitchReveal</c> with the <c>width</c> rows mounted without a boundary.</summary>
let mountSwitchPlain (host: HTMLElement) (width: int) : unit -> unit =
    mountSwitch false host width
