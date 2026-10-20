/// <summary>Placement fixture: a keyed template list whose rows each run one mounted hook, or none.</summary>
module PlacementPerf

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template

let private hookedRow =
    Component.define (fun _ (index: unit -> int) -> index) (fun t ->
        t.div
            [|
                t.Attrs.className "row"
                t.Hooks.mounted (fun _ (node: HTMLDivElement) ->
                    node.setAttribute ("data-placed", "true")
                    ignore)
            |]
            [| t.text (fun index -> string (index ())) |])

let private plainRow =
    Component.define (fun _ (index: unit -> int) -> index) (fun t ->
        t.div [| t.Attrs.className "row" |] [| t.text (fun index -> string (index ())) |])

let private hookedRows =
    Component.define (fun _ (items: int array) -> items) (fun t -> t.div [||] [| t.each id (fun (items: int array) -> items) hookedRow |])

let private plainRows =
    Component.define (fun _ (items: int array) -> items) (fun t -> t.div [||] [| t.each id (fun (items: int array) -> items) plainRow |])

let private mountRows (rows: Component<int array>) (host: HTMLElement) (width: int) : unit -> unit =
    let graph = new Graph ()

    let mounted =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context ->
            Program.run (Component.render rows (Array.init width id)) context.Backend)

    fun () ->
        mounted.Dispose ()
        graph.Dispose ()

/// <summary>Mounts <c>width</c> rows that each set <c>data-placed</c> from a mounted hook; returns the disposal.</summary>
let mountHooked (host: HTMLElement) (width: int) =
    mountRows hookedRows host width

/// <summary>Mounts the same rows without hooks; returns the disposal.</summary>
let mountPlain (host: HTMLElement) (width: int) =
    mountRows plainRows host width
