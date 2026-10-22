/// <summary>One loader written four ways: explicit arms, active-pattern lenses, and a plain match (plugin or quotation).</summary>
module Ranvier.Arms.Probe.Fixtures

open global.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar
open Arms.Plugin

type Load =
    | Idle
    | Loading of percent: int
    | Loaded of text: string * count: int

let read (state: Signal<Load>) =
    state.Value

#if !ENTRY_LENS && !ENTRY_PLUGIN
// Today's form: hoisted definitions whose instance reads the payload.
let private idleArm = armOf (span () { "Not started" })

let private loadingArm =
    armOf (progress (Attrs.attribute ("max", "100"), Attrs.attribute ("value", fun (p: unit -> int) -> Some (string (p ())))) { })

let private loadedArm =
    armOf (
        pre () {
            fun (p: unit -> string * int) -> fst (p ())
            " x"
            fun (p: unit -> string * int) -> string (snd (p ()))
        }
    )

let explicitLoader =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div (Attrs.className "loader") {
            matchWith read (function
                | Idle -> arm idleArm ()
                | Loading percent -> arm loadingArm percent
                | Loaded (text, count) -> arm loadedArm (text, count))
        })

#if !ENTRY_EXPLICIT
let private emptyArm = armOf (span () { "Empty" })
let private bigArm = armOf (b () { fun (p: unit -> string) -> p () })

let explicitRich =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div (Attrs.className "loader") {
            matchWith read (function
                | Idle
                | Loading 0 -> arm idleArm ()
                | Loading percent -> arm loadingArm percent
                | Loaded (_, 0) -> arm emptyArm ()
                | Loaded (text, count) when count > 99 -> arm bigArm text
                | Loaded (text, count) -> arm loadedArm (text, count))
        })
#endif
#endif

#if !ENTRY_EXPLICIT && !ENTRY_PLUGIN
/// <summary>The cases of <c>Load</c> as readers of the instance's current payload.</summary>
module LoadCase =
    let tag =
        function
        | Idle -> 0
        | Loading _ -> 1
        | Loaded _ -> 2

    let (|Idle|Loading|Loaded|) (case: Case<'inst, Load>) =
        match case.Tag with
        | 0 -> Choice1Of3 ()
        | 1 ->
            Choice2Of3 (
                case.Field (function
                    | Load.Loading percent -> percent
                    | _ -> 0)
            )
        | _ ->
            Choice3Of3 (
                case.Field (function
                    | Load.Loaded (text, _) -> text
                    | _ -> ""),
                case.Field (function
                    | Load.Loaded (_, count) -> count
                    | _ -> 0)
            )

let lensLoader =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div (Attrs.className "loader") {
            Lowered.cases read LoadCase.tag 3 (function
                | LoadCase.Idle -> span () { "Not started" }
                | LoadCase.Loading percent ->
                    progress (Attrs.attribute ("max", "100"), Attrs.attribute ("value", fun state -> Some (string (percent state)))) { }
                | LoadCase.Loaded (text, count) ->
                    pre () {
                        text
                        " x"
                        fun state -> string (count state)
                    })
        })
#endif

#if !ENTRY_EXPLICIT && !ENTRY_LENS
[<CaseArms>]
let matchLoader =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div (Attrs.className "loader") {
            Arms.Match (
                read,
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

#if !ENTRY_PLUGIN
[<CaseArms>]
let matchRich =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div (Attrs.className "loader") {
            Arms.Match (
                read,
                function
                | Idle
                | Loading 0 -> span () { "Not started" }
                | Loading percent -> progress (Attrs.attribute ("max", "100"), Attrs.attribute ("value", fun _ -> Some (string percent))) { }
                | Loaded (_, 0) -> span () { "Empty" }
                | Loaded (text, count) when count > 99 -> b () { fun _ -> text }
                | Loaded (text, count) ->
                    pre () {
                        fun _ -> text
                        " x"
                        fun _ -> string count
                    }
            )
        })
#endif
#endif

#if !ENTRY_EXPLICIT && !ENTRY_LENS && !ENTRY_PLUGIN
let private rowsArm =
    armOf (ul () { forEach id (fun (p: unit -> int) -> Array.init (p ()) string) (li () { fun (item: unit -> string) -> item () }) })

let private otherArm = armOf (span () { "Other" })

/// <summary>A keyed list inside an arm: row count follows the payload.</summary>
let explicitRows =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div (Attrs.className "loader") {
            matchWith read (function
                | Loaded (_, count) -> arm rowsArm count
                | _ -> arm otherArm ())
        })

[<CaseArms>]
let matchRows =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div (Attrs.className "loader") {
            Arms.Match (
                read,
                function
                | Loaded (_, count) -> ul () { forEach id (fun _ -> Array.init count string) (li () { fun (item: unit -> string) -> item () }) }
                | _ -> span () { "Other" }
            )
        })

/// <summary><c>matchLoader</c> without the member attribute: lowered by the attribute on <c>Arms.Match</c>.</summary>
let matchUnmarked =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div (Attrs.className "loader") {
            Arms.Match (
                read,
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

/// <summary>A match over a tuple of the union and a flag.</summary>
[<CaseArms>]
let matchPair =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div (Attrs.className "loader") {
            Arms.Match (
                (fun (state: Signal<Load>) -> state.Value, state.Value = Idle),
                function
                | Loaded (text, _), _ -> pre () { fun _ -> text }
                | _, true -> span () { "Not started" }
                | _ -> span () { "Other" }
            )
        })
#endif

#if PROBE_BAD
/// <summary>A pattern variable read as static text: rejected by the plugin at compile time.</summary>
[<CaseArms>]
let matchBad =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                read,
                function
                | Loaded (text, _) -> span () { text }
                | _ -> span () { "Other" }
            )
        })
#endif
