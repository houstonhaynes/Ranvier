module LoopBench

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Testing

let private row =
    Component.define (fun _ (item: unit -> int) -> item) (fun t -> t.li [||] [| t.text (fun item -> string (item ())) |])

let private rows =
    Component.define (fun _ (items: Signal<int array>) -> items) (fun t ->
        t.ul [||] [| t.each id (fun (items: Signal<int array>) -> items.Value) row |])

let keyed (document: Document) (now: unit -> float) =
    use graph = new Graph ()
    let items = graph.Run (fun () -> Api.createSignal (Array.init 1000 id))
    let host = document.createElement "div"
    document.body.appendChild host |> ignore
    let started = now ()

    let mounted =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context ->
            Program.run (Component.render rows items) context.Backend)

    let mountMs = now () - started

    try
        if host.querySelectorAll("li").length <> 1000.0 then
            failwith "Expected 1000 rows"

        let next = Array.rev items.Peek
        let started = now ()
        graph.Run (fun () -> items.Value <- next)
        let reorderMs = now () - started

        if
            host.querySelector("li").Value.textContent
            <> "999"
        then
            failwith "Reorder failed"

        [| mountMs; reorderMs |]
    finally
        mounted.Dispose ()
        document.body.removeChild host |> ignore

#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
let boardHydration (harness: Conformance.Harness) (now: unit -> float) =
    let mutable elapsed = 0.0

    let _, run =
        Conformance.ofChecks
            [|
                "Board hydration timing", (fun () -> elapsed <- DumpChecks.benchmarkBoardHydration now)
            |]
        |> Array.head

    run harness
    elapsed
#endif
