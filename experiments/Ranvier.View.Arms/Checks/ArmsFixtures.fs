/// <summary>Arms.Match sites over each pattern form of the spec, each beside its explicit armOf twin, with named states.</summary>
module ArmsFixtures

#nowarn "25"

open System
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

type Item = { Id: int; Name: string }

type Score = { Name: string; Points: int }

type Panel =
    {
        State: Signal<Load>
        Open: Signal<bool>
    }

type Editor =
    {
        State: Signal<Load>
        Saved: Signal<string>
    }

[<Struct>]
type Point = { X: int; Y: int }

type Value =
    | Ratio of ratio: float
    | Money of amount: decimal
    | Flag of on: bool
    | Letter of letter: char
    | At of point: Point
    | Maybe of maybe: int voption
    | Count of count: int

/// <summary>A run of a definition over props made by the factory, described as lines.</summary>
type Runner =
    abstract Run<'props> : Component<'props> * (unit -> 'props) -> string list

/// <summary>A fixture state: the site, its twin, their server HTML under given options, and their lines under a runner.</summary>
type Case =
    {
        Fixture: string
        State: string
        Site: TemplateComponent
        Twin: TemplateComponent
        Render: Server.Options -> string * string
        Run: Runner -> string list * string list
    }

let private renderWith (options: Server.Options) (definition: Component<'props>) (make: unit -> 'props) =
    try
        use graph = new Graph ()
        let props = graph.Run make
        Server.render options definition props
    with error ->
        "raised: " + error.Message

let private cases (fixture: string) (site: Component<'props>) (twin: Component<'props>) (states: (string * (unit -> 'props)) list) =
    [
        for state, make in states ->
            {
                Fixture = fixture
                State = state
                Site = site :> TemplateComponent
                Twin = twin :> TemplateComponent
                Render = fun options -> renderWith options site make, renderWith options twin make
                Run = fun runner -> runner.Run (site, make), runner.Run (twin, make)
            }
    ]

let private read (state: Signal<Load>) =
    state.Value

let private loadStates =
    [
        "Idle", Idle
        "Loading 0", Loading 0
        "Loading 40", Loading 40
        "Loaded done 3", Loaded ("done", 3)
        "Loaded none 0", Loaded ("none", 0)
        "Loaded big 120", Loaded ("big", 120)
    ]

let private signals (states: (string * 'v) list) =
    [ for label, value in states -> label, (fun () -> Api.createSignal value) ]

/// <summary>The probe's eight writes after an initial <c>Loading 10</c>.</summary>
let loadSteps =
    [
        Loading 70
        Loaded ("a", 1)
        Loaded ("b", 2)
        Loaded ("c", 0)
        Loaded ("d", 150)
        Loaded ("e", 5)
        Loaded ("f", 3)
        Idle
    ]

// Shared twin arms.
let private idleArm = armOf (span () { "Not started" })

let private loadingArm =
    armOf (progress (Attrs.attribute ("max", "100"), Attrs.attribute ("value", fun (payload: unit -> int) -> Some (string (payload ())))) { })

let private loadedArm =
    armOf (
        pre () {
            fun (payload: unit -> string * int) -> fst (payload ())
            " x"
            fun (payload: unit -> string * int) -> string (snd (payload ()))
        }
    )

let private otherArm = armOf (span () { "Other" })
let private emptyArm = armOf (span () { "Empty" })
let private noneArm = armOf (span () { "none" })
let private textArm = armOf (pre () { fun (payload: unit -> string) -> payload () })
let private boldArm = armOf (b () { fun (payload: unit -> string) -> payload () })
let private italicArm = armOf (i () { fun (payload: unit -> string) -> payload () })

// Union cases with and without payload.
let loader =
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

let loaderTwin =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div (Attrs.className "loader") {
            matchWith read (function
                | Idle -> arm idleArm ()
                | Loading percent -> arm loadingArm percent
                | Loaded (text, count) -> arm loadedArm (text, count))
        })

// Or-pattern, literal nested pattern, guard.
let rich =
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

let richTwin =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div (Attrs.className "loader") {
            matchWith read (function
                | Idle
                | Loading 0 -> arm idleArm ()
                | Loading percent -> arm loadingArm percent
                | Loaded (_, 0) -> arm emptyArm ()
                | Loaded (text, count) when count > 99 -> arm boldArm text
                | Loaded (text, count) -> arm loadedArm (text, count))
        })

// A keyed list inside a case body.
let rows =
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

let private rowsArm =
    armOf (ul () { forEach id (fun (payload: unit -> int) -> Array.init (payload ()) string) (li () { fun (item: unit -> string) -> item () }) })

let rowsTwin =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div (Attrs.className "loader") {
            matchWith read (function
                | Loaded (_, count) -> arm rowsArm count
                | _ -> arm otherArm ())
        })

// Rows reading a pattern variable through the items reader (spec, "Rows").
let rowsTuple =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                read,
                function
                | Loaded (owner, count) ->
                    ul () {
                        forEach
                            snd
                            (fun _ -> Array.init count (fun index -> owner, index))
                            (li () { fun (row: unit -> string * int) -> fst (row ()) + string (snd (row ())) })
                    }
                | _ -> span () { "Other" }
            )
        })

let private rowsTupleArm =
    armOf (
        ul () {
            forEach
                snd
                (fun (payload: unit -> string * int) -> let owner, count = payload () in Array.init count (fun index -> owner, index))
                (li () { fun (row: unit -> string * int) -> fst (row ()) + string (snd (row ())) })
        }
    )

let rowsTupleTwin =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            matchWith read (function
                | Loaded (owner, count) -> arm rowsTupleArm (owner, count)
                | _ -> arm otherArm ())
        })

// Tuple scrutinee.
let tupled =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                (fun (state: Signal<Load>) -> state.Value, state.Value = Idle),
                function
                | Loaded (text, _), _ -> pre () { fun _ -> text }
                | _, true -> span () { "Not started" }
                | _ -> span () { "Other" }
            )
        })

let tupledTwin =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            matchWith (fun (state: Signal<Load>) -> state.Value, state.Value = Idle) (function
                | Loaded (text, _), _ -> arm textArm text
                | _, true -> arm idleArm ()
                | _ -> arm otherArm ())
        })

// Option pattern.
let optional =
    Component.template
        (fun _ (state: Signal<string option>) -> state)
        (div () {
            Arms.Match (
                (fun (state: Signal<string option>) -> state.Value),
                function
                | Some value -> pre () { fun _ -> value }
                | None -> span () { "none" }
            )
        })

let optionalTwin =
    Component.template
        (fun _ (state: Signal<string option>) -> state)
        (div () {
            matchWith (fun (state: Signal<string option>) -> state.Value) (function
                | Some value -> arm textArm value
                | None -> arm noneArm ())
        })

// List pattern.
let private listOneArm =
    armOf (b () { fun (payload: unit -> int) -> string (payload ()) })

let private listManyArm =
    armOf (
        i () {
            fun (payload: unit -> int * int list) ->
                string (fst (payload ()))
                + "+"
                + string (snd (payload ())).Length
        }
    )

let listed =
    Component.template
        (fun _ (state: Signal<int list>) -> state)
        (div () {
            Arms.Match (
                (fun (state: Signal<int list>) -> state.Value),
                function
                | [] -> span () { "none" }
                | [ x ] -> b () { fun _ -> string x }
                | x :: rest -> i () { fun _ -> string x + "+" + string rest.Length }
            )
        })

let listedTwin =
    Component.template
        (fun _ (state: Signal<int list>) -> state)
        (div () {
            matchWith (fun (state: Signal<int list>) -> state.Value) (function
                | [] -> arm noneArm ()
                | [ x ] -> arm listOneArm x
                | x :: rest -> arm listManyArm (x, rest))
        })

// Record pattern.
let private noPointsArm = armOf (span () { "no points" })

let scored =
    Component.template
        (fun _ (state: Signal<Score>) -> state)
        (div () {
            Arms.Match (
                (fun (state: Signal<Score>) -> state.Value),
                function
                | { Points = 0 } -> span () { "no points" }
                | { Name = name; Points = points } -> pre () { fun _ -> name + ":" + string points }
            )
        })

let scoredTwin =
    Component.template
        (fun _ (state: Signal<Score>) -> state)
        (div () {
            matchWith (fun (state: Signal<Score>) -> state.Value) (function
                | { Points = 0 } -> arm noPointsArm ()
                | { Name = name; Points = points } -> arm textArm (name + ":" + string points))
        })

// as pattern.
let whole =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                read,
                function
                | Loaded _ as loaded ->
                    pre () {
                        fun _ ->
                            match loaded with
                            | Loaded (text, count) -> text + string count
                            | _ -> ""
                    }
                | _ -> span () { "Other" }
            )
        })

let wholeTwin =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            matchWith read (function
                | Loaded (text, count) -> arm textArm (text + string count)
                | _ -> arm otherArm ())
        })

// Type test over an obj scrutinee.
let typed =
    Component.template
        (fun _ (state: Signal<obj>) -> state)
        (div () {
            Arms.Match (
                (fun (state: Signal<obj>) -> state.Value),
                function
                | :? Item as item -> b () { fun _ -> item.Name }
                | :? string as name -> i () { fun _ -> name }
                | _ -> span () { "Other" }
            )
        })

let typedTwin =
    Component.template
        (fun _ (state: Signal<obj>) -> state)
        (div () {
            matchWith (fun (state: Signal<obj>) -> state.Value) (function
                | :? Item as item -> arm boldArm item.Name
                | :? string as name -> arm italicArm name
                | _ -> arm otherArm ())
        })

// Total and partial active patterns.
let (|Half|Odd|) (n: int) =
    if n % 2 = 0 then Half (n / 2) else Odd

let (|Number|_|) (text: string) =
    match Int32.TryParse text with
    | true, n -> Some n
    | _ -> None

let private halfArm =
    armOf (b () { fun (payload: unit -> int) -> string (payload ()) })

let private oddArm = armOf (span () { "odd" })

let halves =
    Component.template
        (fun _ (state: Signal<int>) -> state)
        (div () {
            Arms.Match (
                (fun (state: Signal<int>) -> state.Value),
                function
                | Half half -> b () { fun _ -> string half }
                | Odd -> span () { "odd" }
            )
        })

let halvesTwin =
    Component.template
        (fun _ (state: Signal<int>) -> state)
        (div () {
            matchWith (fun (state: Signal<int>) -> state.Value) (function
                | Half half -> arm halfArm half
                | Odd -> arm oddArm ())
        })

let parsed =
    Component.template
        (fun _ (state: Signal<string>) -> state)
        (div () {
            Arms.Match (
                (fun (state: Signal<string>) -> state.Value),
                function
                | Number n -> b () { fun _ -> string (n * 2) }
                | other -> i () { fun _ -> other }
            )
        })

let parsedTwin =
    Component.template
        (fun _ (state: Signal<string>) -> state)
        (div () {
            matchWith (fun (state: Signal<string>) -> state.Value) (function
                | Number n -> arm halfArm (n * 2)
                | other -> arm italicArm other)
        })

// if, let and a let of a view at the top of case bodies.
let private hugeArm = armOf (b () { "huge" })

let private labelArm =
    armOf (pre () { fun (payload: unit -> string) -> payload () })

let private bigArm = armOf (i () { "big" })

let forms =
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

let formsTwin =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            matchWith read (function
                | Loaded (_, n) when n > 1000 -> arm hugeArm ()
                | Loaded (_, n) when n > 100 -> arm bigArm ()
                | Loaded (t, 0) -> arm labelArm (t + "!")
                | Loaded (t, _) -> arm textArm t
                | _ -> arm otherArm ())
        })

// A helper view of the enclosing instance type that takes a reader (Review Focus 3).
let private labelled (label: 'inst -> string) : TemplateView<'inst> =
    b (Attrs.className "label") { label }

let helper =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                read,
                function
                | Loaded (text, _) -> labelled (fun _ -> text)
                | _ -> span () { "Other" }
            )
        })

let private labelledArm =
    armOf (labelled (fun (payload: unit -> string) -> payload ()))

let helperTwin =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            matchWith read (function
                | Loaded (text, _) -> arm labelledArm text
                | _ -> arm otherArm ())
        })

// A site inside a case body reading the outer pattern variable.
let private closedArm = armOf (span () { "closed" })

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

let private openArm =
    armOf (
        section () {
            matchWith (fun (payload: unit -> string * Panel) -> (snd (payload ())).Open.Value, fst (payload ())) (function
                | true, t -> arm textArm t
                | false, _ -> arm closedArm ())
        }
    )

let nestedTwin =
    Component.template
        (fun _ (panel: Panel) -> panel)
        (div () {
            matchWith (fun (panel: Panel) -> panel.State.Value, panel) (function
                | Loaded (t, _), panel -> arm openArm (t, panel)
                | _ -> arm otherArm ())
        })

// An event handler reading a pattern variable.
let editor =
    Component.template
        (fun _ (editor: Editor) -> editor)
        (div () {
            Arms.Match (
                (fun (editor: Editor) -> editor.State.Value),
                function
                | Loaded (text, _) -> button (Events.onClick (fun (editor: Editor) _ -> editor.Saved.Value <- text)) { fun _ -> text }
                | _ -> span () { "Other" }
            )
        })

let private saveArm =
    armOf (
        button (Events.onClick (fun (payload: unit -> string * Signal<string>) _ -> (snd (payload ())).Value <- fst (payload ()))) {
            fun (payload: unit -> string * Signal<string>) -> fst (payload ())
        }
    )

let editorTwin =
    Component.template
        (fun _ (editor: Editor) -> editor)
        (div () {
            matchWith (fun (editor: Editor) -> editor.State.Value, editor.Saved) (function
                | Loaded (text, _), saved -> arm saveArm (text, saved)
                | _ -> arm otherArm ())
        })

// Value-type payload fields.
let values =
    Component.template
        (fun _ (state: Signal<Value>) -> state)
        (div () {
            Arms.Match (
                (fun (state: Signal<Value>) -> state.Value),
                function
                | Ratio ratio -> b () { fun _ -> string ratio }
                | Money amount -> b () { fun _ -> string amount }
                | Flag on -> b () { fun _ -> if on then "on" else "off" }
                | Letter letter -> b () { fun _ -> string letter }
                | At point -> b () { fun _ -> string point.X + "," + string point.Y }
                | Maybe (ValueSome n) -> b () { fun _ -> string n }
                | Maybe ValueNone -> span () { "none" }
                | Count count -> b () { fun _ -> string count }
            )
        })

let private ratioArm =
    armOf (b () { fun (payload: unit -> float) -> string (payload ()) })

let private moneyArm =
    armOf (b () { fun (payload: unit -> decimal) -> string (payload ()) })

let private flagArm =
    armOf (b () { fun (payload: unit -> bool) -> if payload () then "on" else "off" })

let private letterArm =
    armOf (b () { fun (payload: unit -> char) -> string (payload ()) })

let private pointArm =
    armOf (
        b () {
            fun (payload: unit -> Point) ->
                string (payload ()).X
                + ","
                + string (payload ()).Y
        }
    )

let private countArm =
    armOf (b () { fun (payload: unit -> int) -> string (payload ()) })

let valuesTwin =
    Component.template
        (fun _ (state: Signal<Value>) -> state)
        (div () {
            matchWith (fun (state: Signal<Value>) -> state.Value) (function
                | Ratio ratio -> arm ratioArm ratio
                | Money amount -> arm moneyArm amount
                | Flag on -> arm flagArm on
                | Letter letter -> arm letterArm letter
                | At point -> arm pointArm point
                | Maybe (ValueSome n) -> arm countArm n
                | Maybe ValueNone -> arm noneArm ()
                | Count count -> arm countArm count)
        })

// An incomplete match: Loaded raises at the region read.
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

let private percentArm =
    armOf (b () { fun (payload: unit -> int) -> string (payload ()) })

let incompleteTwin =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            matchWith read (function
                | Idle -> arm idleArm ()
                | Loading percent -> arm percentArm percent
                | Loaded _ -> arm otherArm ())
        })

// A failwithf case body: no arm; the selector raises it at the region read.
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

/// <summary>A site whose Loaded body binds a raising <c>let</c>; building it raises.</summary>
let letThrowing () =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                read,
                function
                | Loaded _ ->
                    let text: string = failwith "a"
                    span () { text }
                | _ -> span () { "Other" }
            )
        })

let private panels =
    [
        "Loaded open",
        (fun () ->
            {
                State = Api.createSignal (Loaded ("t", 1))
                Open = Api.createSignal true
            }
            : Panel)
        "Loaded closed",
        (fun () ->
            {
                State = Api.createSignal (Loaded ("t", 1))
                Open = Api.createSignal false
            }
            : Panel)
        "Idle",
        (fun () ->
            {
                State = Api.createSignal Idle
                Open = Api.createSignal true
            }
            : Panel)
    ]

let private editors =
    [
        "Loaded",
        (fun () ->
            {
                State = Api.createSignal (Loaded ("draft", 1))
                Saved = Api.createSignal ""
            }
            : Editor)
        "Idle",
        (fun () ->
            {
                State = Api.createSignal Idle
                Saved = Api.createSignal ""
            }
            : Editor)
    ]

/// <summary>A site whose instance is a plain record, shared by concurrent server renders.</summary>
let plainScore =
    Component.template
        (fun _ (score: Score) -> score)
        (div () {
            Arms.Match (
                id,
                function
                | { Points = 0 } -> span () { "no points" }
                | { Name = name; Points = points } -> pre () { fun _ -> name + ":" + string points }
            )
        })

type Shown =
    {
        Visible: Signal<bool>
        State: Signal<Load>
    }

/// <summary>A site under a <c>show</c>: hiding disposes its arm, showing mounts a new one for the same instance.</summary>
let shownLoader =
    Component.template
        (fun _ (shown: Shown) -> shown)
        (div () {
            show
                (fun (shown: Shown) -> shown.Visible.Value)
                (div () {
                    Arms.Match (
                        (fun (shown: Shown) -> shown.State.Value),
                        function
                        | Loaded (text, count) ->
                            pre () {
                                fun _ -> text
                                " x"
                                fun _ -> string count
                            }
                        | _ -> span () { "Other" }
                    )
                })
        })

/// <summary>Every fixture state; the incomplete fixture's unmatched state is checked on its own.</summary>
let all: Case list =
    List.concat
        [
            cases "loader" loader loaderTwin (signals loadStates)
            cases "rich" rich richTwin (signals loadStates)
            cases "rows" rows rowsTwin (signals loadStates)
            cases "rowsTuple" rowsTuple rowsTupleTwin (signals loadStates)
            cases "tupled" tupled tupledTwin (signals loadStates)
            cases "optional" optional optionalTwin (signals [ "Some", Some "ink"; "None", None ])
            cases "listed" listed listedTwin (signals [ "empty", []; "one", [ 4 ]; "three", [ 1; 2; 3 ] ])
            cases "scored" scored scoredTwin (signals [ "zero", { Name = "ada"; Points = 0 }; "five", { Name = "ada"; Points = 5 } ])
            cases "whole" whole wholeTwin (signals loadStates)
            cases "typed" typed typedTwin (signals [ "Item", box { Id = 1; Name = "pen" }; "string", box "ink"; "other", box 3 ])
            cases "halves" halves halvesTwin (signals [ "four", 4; "five", 5 ])
            cases "parsed" parsed parsedTwin (signals [ "number", "21"; "word", "ink" ])
            cases
                "forms"
                forms
                formsTwin
                (signals
                    [
                        "huge", Loaded ("x", 2000)
                        "big", Loaded ("x", 200)
                        "zero", Loaded ("x", 0)
                        "three", Loaded ("y", 3)
                        "Idle", Idle
                    ])
            cases "helper" helper helperTwin (signals loadStates)
            cases "nested" nested nestedTwin panels
            cases "editor" editor editorTwin editors
            cases
                "values"
                values
                valuesTwin
                (signals
                    [
                        "ratio", Ratio 1.5
                        "money", Money 2.25m
                        "flag", Flag true
                        "letter", Letter 'q'
                        "point", At { X = 3; Y = 4 }
                        "some", Maybe (ValueSome 7)
                        "none", Maybe ValueNone
                        "count", Count 9
                    ])
            cases "incomplete" incomplete incompleteTwin (signals [ "Idle", Idle; "Loading 40", Loading 40 ])
        ]

/// <summary>Whether the incomplete site raises for its unmatched case.</summary>
let incompleteRaises () =
    try
        use graph = new Graph ()
        let props = graph.Run (fun () -> Api.createSignal (Loaded ("x", 1)))

        Server.render Server.Options.defaults incomplete props
        |> ignore

        false
    with
#if FABLE_COMPILER
    | error ->
        error.Message.StartsWith "Match failure"
#else
    | :? MatchFailureException ->
        true
#endif
