module Ranvier.View.Twin.Tests.DepartureTests

open Expecto
open Ranvier
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Twin

// A row root whose exit never calls finish: the row leaves at the safety timeout.
let private row =
    Component.define (fun _ (item: unit -> int) -> item) (fun t ->
        t.li
            [|
                t.Attrs.className "leaving-row"
                t.Hooks.exit (System.Func<_, _, _, _>(fun (_: unit -> int) (_: Fable.Core.TS.Dom.HTMLLIElement) _ -> ignore))
            |]
            [| t.text (fun item -> string (item ())) |])

let private rows =
    Component.define (fun _ (items: Signal<int array>) -> items) (fun t ->
        t.ul [||] [| t.each id (fun (items: Signal<int array>) -> items.Value) row |])

[<Tests>]
let tests =
    testList
        "Departures"
        [
            testCase "a held exit ends at the safety timeout on the twin clock"
            <| fun () ->
                let items = ref Unchecked.defaultof<Signal<int array>>

                use session =
                    Twin.mountView TwinMount.defaults (fun context ->
                        items.Value <- Api.createSignal [| 1 |]
                        Program.run (Component.render rows items.Value) context.Backend)

                session.Act (fun () -> items.Value.Value <- [||])
                Expect.stringContains (session.Html ()) "leaving-row" "held after removal"
                let pending = session.Clock.Pending
                Expect.equal (pending.Timers, pending.NextTimerAt) (1, Some 10000.0) "the safety timer"
                session.AdvanceBy 9999.0
                Expect.stringContains (session.Html ()) "leaving-row" "held before the safety timeout"
                session.AdvanceBy 1.0
                Expect.isFalse ((session.Html ()).Contains "leaving-row") "finished at 10 s"
        ]
