module Fixtures.Bad.AM003

#if BAD_AM003
open global.Ranvier
open Ranvier.View.Template
open Ranvier.View.Sugar
open Ranvier.View.Arms
open Fixtures.Types

let private arms =
    function
    | Idle -> span () { "Idle" }
    | _ -> span () { "Other" }

let site =
    Component.template (fun _ (state: Signal<Load>) -> state) (div () { Arms.Match ((fun (state: Signal<Load>) -> state.Value), arms) })
#endif
