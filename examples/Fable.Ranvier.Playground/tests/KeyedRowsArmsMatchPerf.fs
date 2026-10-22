/// <summary>The arm benchmark fixture of <c>KeyedRowsSugarPerf</c> with its arm site written as Arms.Match.</summary>
module KeyedRowsArmsMatchPerf

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Representation
open Ranvier.View.Template
open Ranvier.View.Sugar
open Ranvier.View.Arms
open KeyedRowsPerf

let private armList =
    Component.template
        (fun _ (rows: Signal<ArmRow array>) -> rows)
        (table () {
            tbody () {
                forEachFixed
                    (fun row -> row.Id)
                    (fun (rows: Signal<_ array>) -> rows.Value)
                    (tr () {
                        td () { fun row -> string row.Id }

                        td () {
                            Arms.Match (
                                (fun (row: ArmRow) -> row.Mode.Value),
                                function
                                | Plain -> span (Attrs.className "a") { "A" }
                                | Counted count -> b () { fun _ -> string count }
                            )
                        }
                    })
            }
        })

/// <summary>Mounts an empty Arms.Match arm list into the host.</summary>
let mountArms (host: HTMLElement) : ArmOperations =
    let graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal ([||]: ArmRow array))

    let mounted =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context ->
            Program.run (Component.render armList rows) context.Backend)

    let mutable live = true

    let each change =
        graph.Run (fun () ->
            Api.batch (fun () ->
                for row in rows.Value do
                    row.Mode.Value <- change row.Mode.Value))

    {
        Create =
            fun count ->
                graph.Run (fun () ->
                    rows.Value <-
                        Array.init count (fun index ->
                            {
                                Id = index + 1
                                Mode = Api.createSignal (if index % 2 = 0 then Plain else Counted 0)
                            }))
        Switch =
            fun () ->
                each (function
                    | Plain -> Counted 0
                    | Counted _ -> Plain)
        Payload =
            fun () ->
                each (function
                    | Plain -> Plain
                    | Counted count -> Counted (count + 1))
        Clear = fun () -> graph.Run (fun () -> rows.Value <- [||])
        Dispose =
            fun () ->
                if live then
                    live <- false
                    mounted.Dispose ()
                    graph.Dispose ()
    }
