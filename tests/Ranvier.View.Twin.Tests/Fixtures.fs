module Ranvier.View.Twin.Tests.Fixtures

open Ranvier
open Ranvier.View

type CounterState = { Count: Signal<int> }

let counter =
    Component.define (fun _ (start: int) -> { Count = Api.createSignal start }) (fun t ->
        t.button
            [|
                t.Events.on ("click", fun state _ -> state.Count.Value <- state.Count.Value + 1)
            |]
            [| t.text (fun state -> string state.Count.Value) |])

let row =
    Component.define (fun _ (item: unit -> int) -> item) (fun t -> t.li [||] [| t.text (fun item -> string (item ())) |])

let rows =
    Component.define (fun _ (items: Signal<int array>) -> items) (fun t ->
        t.ul
            [||]
            [|
                t.each (fun (n: int) -> n) (fun (items: Signal<int array>) -> items.Value) row
            |])

let failing =
    Component.define (fun _ () -> ()) (fun t -> t.button [| t.Events.on ("click", fun _ _ -> failwith "handler failed") |] [||])
