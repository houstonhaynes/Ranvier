module Fixtures.Bad.AM001Simple

#if BAD_AM001_SIMPLE
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
                | Loaded (text, _) -> span () { text }
                | _ -> span () { "Other" }
            )
        })
#endif
