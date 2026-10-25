module Ranvier.View.Twin.Tests.TraceSiteTests

open Expecto
open Ranvier
open Ranvier.View
open Ranvier.View.Twin

#if RANVIER_TRACE
type private TallyState = { Count: Signal<int> }

let private item =
    Component.define (fun _ (item: unit -> int) -> item) (fun t -> t.li [||] [| t.text (fun item -> string (item ())) |])

let private tally =
    Component.define (fun _ (start: int) -> { Count = Api.createSignal start }) (fun t ->
        t.ul
            [||]
            [|
                t.text (fun state -> string state.Count.Value)
                t.each (fun (n: int) -> n) (fun state -> Array.init state.Count.Value id) item
            |])

[<Tests>]
let tests =
    testList
        "View sites (traced)"
        [
            testCase "nodes and owners a Ranvier.View helper creates name the test's file in their sites and identity paths"
            <| fun () ->
                // mountView, so the innermost frame outside Ranvier.View is the test's Program.run line, not Twin's.
                use session =
                    Twin.mountView TwinMount.defaults (fun context -> Program.run (Component.render tally 3) context.Backend)

                let snapshot = Trace.snapshot session.Graph
                let file = System.IO.Path.GetFileName __SOURCE_FILE__

                Expect.isNonEmpty snapshot.Nodes "the mount created nodes"

                for node in snapshot.Nodes.Values do
                    Expect.stringContains node.Site file $"node {node.Id} ({node.Kind})'s site names the test's file"
                    Expect.stringContains node.Path file $"node {node.Id} ({node.Kind})'s path names the test's file"

                let owners =
                    snapshot.Owners.Values
                    |> Seq.filter (fun o -> o.Id <> snapshot.Root && not o.Root)
                    |> List.ofSeq

                Expect.isNonEmpty owners "the mount created owners"

                for owner in owners do
                    Expect.stringContains owner.Path file $"owner {owner.Id}'s path names the test's file"
        ]
#endif
