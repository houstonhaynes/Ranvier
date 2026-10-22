module Fixtures.Bad.AM007

#if BAD_AM007
open System
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
                | Loaded (text, count) when count > 0 -> (Console.WriteLine "x"; if text = "" then span () { "empty" } else span () { "full" })
                | _ -> span () { "Other" }
            )
        })
#endif
