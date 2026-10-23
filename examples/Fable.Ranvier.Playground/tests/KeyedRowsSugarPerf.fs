/// <summary>The keyed-list and arm benchmark fixtures of <c>KeyedRowsPerf</c>, written with the template sugar. The operations duplicate those of <c>KeyedRowsPerf</c>.</summary>
module KeyedRowsSugarPerf

open global.Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Sugar
open KeyedRowsPerf

#if FABLE_COMPILER
[<Emit("(() => { const next = $1.slice(); next.splice($0, 1); return next; })()")>]
let private without (index: int) (rows: KeyedRow array) : KeyedRow array = jsNative
#else
let private without (index: int) (rows: KeyedRow array) : KeyedRow array =
    Array.removeAt index rows
#endif

let mutable private clicks = 0

let private keyedList =
    Component.template
        (fun _ (rows: Signal<KeyedRow array>) -> rows)
        (table (Attrs.className "table") {
            tbody () {
                forEachFixed
                    (fun (row: KeyedRow) -> row.Id)
                    (fun (rows: Signal<_ array>) -> rows.Value)
                    (tr () {
                        td (Attrs.className "col-md-1") { fun (row: KeyedRow) -> string row.Id }
                        td (Attrs.className "col-md-4") { a (Events.onClick (fun _ _ -> clicks <- clicks + 1)) { from (fun row -> row.Label) } }

                        td (Attrs.className "col-md-1") {
                            a (Events.onClick (fun _ _ -> clicks <- clicks - 1)) {
                                span (Attrs.className "glyphicon glyphicon-remove", Attrs.attribute ("aria-hidden", "true")) { }
                            }
                        }

                        td (Attrs.className "col-md-6") { }
                    })
            }
        })

/// <summary>Mounts an empty sugared keyed list into the host.</summary>
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

let private plain = armOf (span (Attrs.className "a") { "A" })
let private counted = armOf (b () { fun payload -> string (payload ()) })

let private armList =
    Component.template
        (fun _ (rows: Signal<ArmRow array>) -> rows)
        (table () {
            tbody () {
                forEachFixed
                    (fun row -> row.Id)
                    (fun (rows: Signal<_ array>) -> rows.Value)
                    (tr () {
                        td () { fun row -> string row.Id }

                        td () {
                            matchWith (fun row -> row.Mode.Value) (function
                                | Plain -> arm plain ()
                                | Counted count -> arm counted count)
                        }
                    })
            }
        })

/// <summary>Mounts an empty sugared arm list into the host.</summary>
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
