/// <summary>Keyed-list fixture for the js-framework-benchmark operations: rows of a template list mounted through <c>t.each</c>.</summary>
module KeyedRowsPerf

open global.Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View
open Ranvier.View.Dom

type KeyedRow = { Id: int; Label: Signal<string> }

/// <summary>A copy of the rows without the row at <c>index</c>, built with <c>slice</c> and <c>splice</c> as the Solid fixture builds it.</summary>
#if FABLE_COMPILER
[<Emit("(() => { const next = $1.slice(); next.splice($0, 1); return next; })()")>]
let private without (index: int) (rows: KeyedRow array) : KeyedRow array = jsNative
#else
let private without (index: int) (rows: KeyedRow array) : KeyedRow array =
    Array.removeAt index rows
#endif

/// <summary>Counts row clicks; the handlers exist to match the benchmark's row shape.</summary>
let mutable private clicks = 0

let private keyedRow =
    Component.define (fun _ (item: unit -> KeyedRow) -> item ()) (fun t ->
        t.element
            (HtmlTag "tr")
            [||]
            [|
                t.element (HtmlTag "td") [| t.Attrs.className "col-md-1" |] [| t.text (fun (row: KeyedRow) -> string row.Id) |]
                t.element
                    (HtmlTag "td")
                    [| t.Attrs.className "col-md-4" |]
                    [|
                        t.a [| t.Events.onClick (fun _ _ -> clicks <- clicks + 1) |] [| t.text (t.from (fun (row: KeyedRow) -> row.Label)) |]
                    |]
                t.element
                    (HtmlTag "td")
                    [| t.Attrs.className "col-md-1" |]
                    [|
                        t.a
                            [| t.Events.onClick (fun _ _ -> clicks <- clicks - 1) |]
                            [|
                                t.span
                                    [|
                                        t.Attrs.className "glyphicon glyphicon-remove"
                                        t.Attrs.attribute ("aria-hidden", "true")
                                    |]
                                    [||]
                            |]
                    |]
                t.element (HtmlTag "td") [| t.Attrs.className "col-md-6" |] [||]
            |])

let private keyedList =
    Component.define (fun _ (rows: Signal<KeyedRow array>) -> rows) (fun t ->
        t.element
            (HtmlTag "table")
            [| t.Attrs.className "table" |]
            [|
                t.element (HtmlTag "tbody") [||] [| t.each (fun (row: KeyedRow) -> row.Id) (fun rows -> rows.Value) keyedRow |]
            |])

/// <summary>The operations of a mounted keyed list; each runs synchronously and commits its DOM work before returning.</summary>
type KeyedOperations =
    {
        Create: int -> unit
        Append: int -> unit
        UpdateEvery10th: unit -> unit
        Swap: unit -> unit
        Remove: int -> unit
        Clear: unit -> unit
        Dispose: unit -> unit
    }

/// <summary>Mounts an empty keyed list into the host.</summary>
let mountKeyed (host: HTMLElement) : KeyedOperations =
    let graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal ([||]: KeyedRow array))
    let mutable nextId = 1

    let build count =
        Array.init count (fun _ ->
            let id = nextId
            nextId <- nextId + 1

            {
                Id = id
                Label = Api.createSignal ("row " + string id)
            })

    let mounted =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context ->
            Program.run (Component.render keyedList rows) context.Backend)

    let mutable live = true

    {
        Create = fun count -> graph.Run (fun () -> rows.Value <- build count)
        Append = fun count -> graph.Run (fun () -> rows.Value <- Array.append rows.Value (build count))
        UpdateEvery10th =
            fun () ->
                graph.Run (fun () ->
                    Api.batch (fun () ->
                        let current = rows.Value

                        let mutable index = 0

                        while index < current.Length do
                            let label = current[index].Label
                            label.Value <- label.Value + " !!!"
                            index <- index + 10))
        Swap =
            fun () ->
                graph.Run (fun () ->
                    let next = Array.copy rows.Value

                    if next.Length > 998 then
                        let first = next[1]
                        next[1] <- next[998]
                        next[998] <- first

                    rows.Value <- next)
        Remove = fun index -> graph.Run (fun () -> rows.Value <- without index rows.Value)
        Clear = fun () -> graph.Run (fun () -> rows.Value <- [||])
        Dispose =
            fun () ->
                if live then
                    live <- false
                    mounted.Dispose ()
                    graph.Dispose ()
    }

/// <summary>The arm a row of the arm fixture selects.</summary>
type ArmMode =
    | Plain
    | Counted of int

type ArmRow = { Id: int; Mode: Signal<ArmMode> }

let private plainArm =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.span [| t.Attrs.className "a" |] [| t.text "A" |])

let private countedArm =
    Component.define (fun _ (payload: unit -> int) -> payload) (fun t -> t.b [||] [| t.text (fun (payload: unit -> int) -> string (payload ())) |])

let private armRow =
    Component.define (fun _ (item: unit -> ArmRow) -> item ()) (fun t ->
        t.element
            (HtmlTag "tr")
            [||]
            [|
                t.element (HtmlTag "td") [||] [| t.text (fun (row: ArmRow) -> string row.Id) |]
                t.element
                    (HtmlTag "td")
                    [||]
                    [|
                        t.matchWith (fun (row: ArmRow) -> row.Mode.Value) (function
                            | Plain -> t.arm plainArm ()
                            | Counted count -> t.arm countedArm count)
                    |]
            |])

let private armList =
    Component.define (fun _ (rows: Signal<ArmRow array>) -> rows) (fun t ->
        t.element
            (HtmlTag "table")
            [||]
            [|
                t.element (HtmlTag "tbody") [||] [| t.each (fun (row: ArmRow) -> row.Id) (fun rows -> rows.Value) armRow |]
            |])

/// <summary>The operations of a mounted list whose rows each hold one arm region.</summary>
type ArmOperations =
    {
        /// <summary>Replaces the rows with <c>count</c> rows; even rows select <c>Plain</c>, odd rows <c>Counted 0</c>.</summary>
        Create: int -> unit
        /// <summary>Selects the other arm in every row.</summary>
        Switch: unit -> unit
        /// <summary>Increments the payload of every <c>Counted</c> row, retaining its arm.</summary>
        Payload: unit -> unit
        Clear: unit -> unit
        Dispose: unit -> unit
    }

/// <summary>Mounts an empty arm list into the host.</summary>
let mountArms (host: HTMLElement) : ArmOperations =
    let graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal ([||]: ArmRow array))

    let mounted =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context ->
            Program.run (Component.render armList rows) context.Backend)

    let mutable live = true

    let each change =
        graph.Run (fun () ->
            Api.batch (fun () ->
                for row in rows.Value do
                    row.Mode.Value <- change row.Mode.Value))

    {
        Create =
            fun count ->
                graph.Run (fun () ->
                    rows.Value <-
                        Array.init count (fun index ->
                            {
                                Id = index + 1
                                Mode = Api.createSignal (if index % 2 = 0 then Plain else Counted 0)
                            }))
        Switch =
            fun () ->
                each (function
                    | Plain -> Counted 0
                    | Counted _ -> Plain)
        Payload =
            fun () ->
                each (function
                    | Plain -> Plain
                    | Counted count -> Counted (count + 1))
        Clear = fun () -> graph.Run (fun () -> rows.Value <- [||])
        Dispose =
            fun () ->
                if live then
                    live <- false
                    mounted.Dispose ()
                    graph.Dispose ()
    }
