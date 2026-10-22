module Fixtures.Bad.AM002WildcardSimple

#if BAD_AM002_WILDCARD_SIMPLE
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
                | Loaded (text, _) -> ul () { forEach id (fun _ -> [| "a" |]) (li () { fun _ -> text }) }
                | _ -> span () { "Other" }
            )
        })
#endif
