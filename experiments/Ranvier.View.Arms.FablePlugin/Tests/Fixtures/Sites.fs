/// <summary>Sites whose emitted JavaScript the plugin tests compare with expected/Sites.js.</summary>
module Fixtures.Sites

#nowarn "25"

open System
open global.Ranvier
open Ranvier.View
open Ranvier.View.Template
open Ranvier.View.Sugar
open Ranvier.View.Arms
open Fixtures.Types

let read (state: Signal<Load>) = state.Value

/// <summary>An or-pattern, an inlined field, a literal pattern and a guard.</summary>
let rich =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                read,
                function
                | Idle
                | Loading 0 -> span () { "Not started" }
                | Loading percent -> b () { fun _ -> string percent }
                | Loaded (_, 0) -> span () { "Empty" }
                | Loaded (text, count) when count > 99 -> b () { fun _ -> text }
                | Loaded (text, count) -> pre () { fun _ -> text + string count }
            )
        })

/// <summary>An incomplete match: the selector raises for Loaded.</summary>
let incomplete =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                read,
                function
                | Idle -> span () { "Not started" }
                | Loading percent -> b () { fun _ -> string percent }
            )
        })

/// <summary>A delegate over the instance reading a pattern variable.</summary>
let held =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                read,
                function
                | Loaded (text, _) ->
                    span (
                        Hooks.exit (
                            Func<_, _, _, _>(fun (_: Signal<Load>) (_: Fable.Core.TS.Dom.HTMLSpanElement) finish ->
                                if text = "" then finish ()
                                ignore)
                        )
                    ) {
                        "held"
                    }
                | _ -> span () { "Other" }
            )
        })

/// <summary>An if and a let at the top of case bodies, and a let of a view.</summary>
let bodies =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                read,
                function
                | Loaded (_, n) when n > 100 -> if n > 1000 then b () { "huge" } else i () { "big" }
                | Loaded (t, 0) ->
                    let label = t + "!"
                    pre () { fun _ -> label }
                | Loaded (t, _) ->
                    let view = pre () { fun _ -> t }
                    view
                | _ -> span () { "Other" }
            )
        })

/// <summary>A site inside a case body reading the outer site's pattern variable.</summary>
let nested =
    Component.template
        (fun _ (panel: Panel) -> panel)
        (div () {
            Arms.Match (
                (fun (panel: Panel) -> panel.State.Value),
                function
                | Loaded (t, _) ->
                    section () {
                        Arms.Match (
                            (fun (panel: Panel) -> panel.Open.Value),
                            function
                            | true -> pre () { fun _ -> t }
                            | false -> span () { "closed" }
                        )
                    }
                | _ -> span () { "Other" }
            )
        })

/// <summary>A failwithf case body: the selector raises it, and no arm is built for it.</summary>
let failing =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                read,
                function
                | Loaded _ -> failwithf "bad %s" "state"
                | _ -> span () { "Other" }
            )
        })

let private badge: TemplateView<Signal<Load>> =
    i () { fun (state: Signal<Load>) -> string state.Value }

/// <summary>A helper view value spliced inside a case body and as a whole case body.</summary>
let spliced =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                read,
                function
                | Loaded (text, _) ->
                    span () {
                        fun _ -> text
                        badge
                    }
                | _ -> badge
            )
        })

/// <summary>A function over the instance bound in a case body, applied inside a reader to the reader's instance.</summary>
let locals =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                read,
                function
                | Loaded (text, _) ->
                    let label (s: Signal<Load>) = text + string s.Value
                    pre () { fun (s: Signal<Load>) -> label s }
                | _ -> span () { "Other" }
            )
        })
