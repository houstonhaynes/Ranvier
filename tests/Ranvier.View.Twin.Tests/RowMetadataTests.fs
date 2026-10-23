module Ranvier.View.Twin.Tests.RowMetadataTests

open Expecto
open Ranvier
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Twin

let private row =
    Component.define (fun _ (item: unit -> int) -> item) (fun t ->
        t.li
            [|
                t.Hooks.exit (System.Func<_, _, _, _>(fun (_: unit -> int) (_: Fable.Core.TS.Dom.HTMLLIElement) _ -> ignore))
            |]
            [| t.text (fun item -> string (item ())) |])

let private rows =
    Component.define (fun _ (items: Signal<int array>) -> items) (fun t ->
        t.ul [||] [| t.each id (fun (items: Signal<int array>) -> items.Value) row |])

let private fragment =
    Component.define (fun _ () -> ()) (fun t -> t.fragment [| t.p [||] [| t.text "first" |]; t.button [||] [| t.text "second" |] |])

let private fragmentPage =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [||]
            [|
                t.span [||] [| t.text "before" |]
                t.render fragment id
                t.span [||] [| t.text "after" |]
            |])

[<Tests>]
let tests =
    testList
        "RowMetadata"
        [
            testCase "fragment ranges select the inner instance and disappear on disposal"
            <| fun () ->
                for hydrate in [ false; true ] do
                    let session =
                        if hydrate then
                            Twin.hydrate
                                Server.Options.defaults
                                { Hydration.HydrationOptions.defaults with
                                    Strict = true
                                }
                                fragmentPage
                                ()
                        else
                            Twin.mount fragmentPage ()

                    let button = Ranvier.View.Testing.Query.get session.Host "button"
                    let first, finish = Inspect.enclosingInstanceOf button |> Option.get
                    Expect.equal first.nodeName "P" "the child fragment starts at its first root"
                    Expect.equal finish.textContent (Some "after") "the child fragment ends before its following sibling"
                    (session :> System.IDisposable).Dispose()
                    Expect.isNone (Inspect.enclosingInstanceOf button) "disposed ranges are removed"

            testCase "held row keys survive retirement and disappear after physical removal"
            <| fun () ->
                let items = ref Unchecked.defaultof<Signal<int array>>

                use session =
                    Twin.mountView TwinMount.defaults (fun context ->
                        items.Value <- Api.createSignal [| 17 |]
                        Program.run (Component.render rows items.Value) context.Backend)

                let rec firstRow (node: TwinNode) =
                    match Inspect.probeOf (unbox node.Native) with
                    | Some (Inspect.ListProbe (_, readRows, _)) -> readRows () |> Array.tryHead |> Option.map snd
                    | _ -> node.Children |> Seq.tryPick firstRow

                let first =
                    firstRow (Bridge.nodeOf session.Host)
                    |> Option.get

                Expect.equal (Inspect.rowKeyOf first) (Some (box 17)) "active row key"
                session.Act (fun () -> items.Value.Value <- [||])
                Expect.equal (Inspect.rowKeyOf first) (Some (box 17)) "retired row retains its key during exit"
                session.AdvanceBy 10000.0
                Expect.isNone (Inspect.rowKeyOf first) "finished row metadata clears"
                session.Host.appendChild first |> ignore
                Expect.isNone (Inspect.rowKeyOf first) "cleared metadata stays absent after reattachment"
        ]
