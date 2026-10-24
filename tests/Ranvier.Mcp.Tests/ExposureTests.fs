module Ranvier.Mcp.Tests.ExposureTests

open System
open System.Text.Json.Nodes
open System.Threading.Tasks
open Expecto
open Ranvier
open Ranvier.Mcp

let private number (value: int) : JsonNode =
    JsonValue.Create value

/// The snapshot's values as JSON text, by name.
let private texts (snapshot: Snapshot) =
    snapshot.Values
    |> Map.map (fun _ value -> value.GetRawText ())
    |> Map.toList

/// A graph active on the calling thread, an exposure over two signals, and the publications it raises.
let private fixture (expose: Signal<int> -> Signal<int> -> Exposure -> unit) =
    let graph = new Graph ()
    let activation = graph.Activate ()
    let a = createSignal 1
    let b = createSignal 2
    let exposure = Exposure ()
    let publications = ResizeArray<Publication>()
    exposure.Published.Add publications.Add
    expose a b exposure
    exposure.Start ()

    let dispose () =
        activation.Dispose ()
        graph.Dispose ()

    a,
    b,
    exposure,
    publications,
    { new IDisposable with
        member _.Dispose() =
            dispose ()
    }

[<Tests>]
let tests =
    testList
        "Exposure"
        [
            test "Start publishes version 1 with every exposed value" {
                let _, _, exposure, publications, scope =
                    fixture (fun a b exposure ->
                        let sum = createMemo (fun _ -> a.Value + b.Value)

                        exposure
                        |> Expose.value "a" number (fun () -> a.Value)

                        exposure
                        |> Expose.value "sum" number (fun () -> sum.Value))

                use _ = scope
                Expect.equal exposure.Snapshot.Version 1L "the first publication"
                Expect.equal (texts exposure.Snapshot) [ "a", "1"; "sum", "3" ] "every value"
                Expect.equal (publications |> Seq.map _.Changed |> List.ofSeq) [ [ "a"; "sum" ] ] "every name changed"
            }

            test "a batch of writes publishes one consistent snapshot" {
                let a, b, exposure, publications, scope =
                    fixture (fun a b exposure ->
                        let sum = createMemo (fun _ -> a.Value + b.Value)

                        exposure
                        |> Expose.value "a" number (fun () -> a.Value)

                        exposure
                        |> Expose.value "b" number (fun () -> b.Value)

                        exposure
                        |> Expose.value "sum" number (fun () -> sum.Value))

                use _ = scope

                batch (fun () ->
                    a.Value <- 10
                    b.Value <- 20
                    a.Value <- 11)

                Expect.equal publications.Count 2 "Start and one publication for the batch"
                Expect.equal exposure.Snapshot.Version 2L "one version for the batch"
                Expect.equal (texts exposure.Snapshot) [ "a", "11"; "b", "20"; "sum", "31" ] "the values agree"
                Expect.equal publications[1].Changed [ "a"; "b"; "sum" ] "the changed names, in registration order"
            }

            test "each real change increments the version and names only the changed values" {
                let a, _, exposure, publications, scope =
                    fixture (fun a b exposure ->
                        exposure
                        |> Expose.value "a" number (fun () -> a.Value)

                        exposure
                        |> Expose.value "b" number (fun () -> b.Value))

                use _ = scope
                a.Value <- 5
                a.Value <- 6

                Expect.equal exposure.Snapshot.Version 3L "two changes, two versions"

                Expect.equal
                    (publications
                     |> Seq.skip 1
                     |> Seq.map _.Changed
                     |> List.ofSeq)
                    [ [ "a" ]; [ "a" ] ]
                    "only a"

                Expect.equal
                    (publications
                     |> Seq.map _.Snapshot.Version
                     |> List.ofSeq)
                    [ 1L; 2L; 3L ]
                    "versions in order"
            }

            test "a memo cutoff publishes nothing" {
                let a, _, exposure, publications, scope =
                    fixture (fun a _ exposure ->
                        let parity = createMemo (fun _ -> a.Value % 2)

                        exposure
                        |> Expose.value "parity" number (fun () -> parity.Value))

                use _ = scope
                a.Value <- 3
                a.Value <- 5

                Expect.equal publications.Count 1 "only Start published"
                Expect.equal exposure.Snapshot.Version 1L "the version stays"
            }

            test "a re-run that encodes identical JSON publishes nothing" {
                let a, b, exposure, publications, scope =
                    fixture (fun a b exposure ->
                        exposure
                        |> Expose.value "sum" number (fun () -> a.Value + b.Value))

                use _ = scope

                batch (fun () ->
                    a.Value <- 2
                    b.Value <- 1)

                Expect.equal publications.Count 1 "only Start published"
                Expect.equal (texts exposure.Snapshot) [ "sum", "3" ] "the value stays"
            }

            test "registration closes at Start, and names are unique" {
                let a, _, exposure, _, scope =
                    fixture (fun a _ exposure ->
                        exposure
                        |> Expose.value "a" number (fun () -> a.Value))

                use _ = scope

                Expect.throwsT<InvalidOperationException>
                    (fun () ->
                        exposure
                        |> Expose.value "late" number (fun () -> a.Value))
                    "a value exposed after Start"

                let fresh = Exposure ()
                fresh |> Expose.value "x" number (fun () -> 1)
                Expect.throwsT<ArgumentException> (fun () -> fresh |> Expose.value "x" number (fun () -> 2)) "a repeated name"
                Expect.throwsT<ArgumentException> (fun () -> fresh |> Expose.value "a b" number (fun () -> 2)) "a name unfit for a URI"
            }

            test "the snapshot is readable from a pool thread of a Guarded graph" {
                let a, _, exposure, _, scope =
                    fixture (fun a _ exposure ->
                        exposure
                        |> Expose.value "a" number (fun () -> a.Value))

                use _ = scope
                a.Value <- 9

                let read = Task.Run (fun () -> exposure.Snapshot)
                Expect.equal (texts read.Result) [ "a", "9" ] "the published snapshot, read off the graph thread"
            }
        ]
