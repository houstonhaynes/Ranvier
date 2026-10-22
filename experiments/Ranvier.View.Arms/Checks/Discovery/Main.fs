/// <summary>One Arms.Match site rendered on the server for three states, one line per state.</summary>
module Discovery.Main

open global.Ranvier
open Ranvier.View
open Ranvier.View.Representation
open Ranvier.View.Template
open Ranvier.View.Sugar
open Ranvier.View.Arms

type Load =
    | Idle
    | Loading of percent: int
    | Loaded of text: string * count: int

let loader =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div (Attrs.className "loader") {
            Arms.Match (
                (fun (state: Signal<Load>) -> state.Value),
                function
                | Idle -> span () { "Not started" }
                | Loading percent -> progress (Attrs.attribute ("max", "100"), Attrs.attribute ("value", fun _ -> Some (string percent))) { }
                | Loaded (text, count) ->
                    pre () {
                        fun _ -> text
                        " x"
                        fun _ -> string count
                    }
            )
        })

let private omit =
    { Server.Options.defaults with
        Policy = Omit
    }

[<EntryPoint>]
let main _ =
    for label, state in [ "Idle", Idle; "Loading 40", Loading 40; "Loaded done 3", Loaded ("done", 3) ] do
        let graph = new Graph ()
        let signal = graph.Run (fun () -> Api.createSignal state)
        printfn "%s|%s" label (Server.render omit loader signal)

    0
