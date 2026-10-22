module Fixtures.Bad.AM002

#if BAD_AM002
open global.Ranvier
open Ranvier.View.Template
open Ranvier.View.Sugar
open Ranvier.View.Arms
open Fixtures.Types

let site =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                (fun (state: Signal<Load>) -> state.Value),
                function
                | Loaded (text, count) when count > 0 -> ul () { forEach id (fun _ -> [| "a" |]) (li () { fun (item: unit -> string) -> item () + text }) }
                | _ -> span () { "Other" }
            )
        })
#endif
