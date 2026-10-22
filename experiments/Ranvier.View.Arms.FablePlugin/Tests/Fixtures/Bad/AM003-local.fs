module Fixtures.Bad.AM003Local

#if BAD_AM003_LOCAL
open global.Ranvier
open Ranvier.View.Template
open Ranvier.View.Sugar
open Ranvier.View.Arms
open Fixtures.Types

let site =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            let arms =
                function
                | Idle -> span () { "Idle" }
                | _ -> span () { "Other" }

            Arms.Match ((fun (state: Signal<Load>) -> state.Value), arms)
        })
#endif
