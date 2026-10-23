module Ranvier.View.Twin.Tests.HeadTests

open System.Threading.Tasks
open Expecto
open Ranvier
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Twin

let private described =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [||]
            [|
                t.Document.meta
                    [|
                        t.Attrs.attribute ("name", "description")
                        t.Attrs.attribute ("content", "shown")
                    |]
            |])

let private metaCount (session: Session) =
    (Html.inner session.Document.Head).Split("<meta").Length
    - 1

[<Tests>]
let tests =
    testList
        "Document head"
        [
            testCase "head entries mounted on parallel threads against separate documents stay in their own documents"
            <| fun () ->
                let counts =
                    Array.init 16 (fun _ ->
                        Task.Run (fun () ->
                            [
                                for _ in 1..50 do
                                    use session = Twin.mount described ()
                                    let mounted = metaCount session
                                    session.Mounted.Dispose ()
                                    mounted, metaCount session
                            ]))
                    |> Array.collect (fun task -> List.toArray task.Result)

                Expect.allEqual counts (1, 0) "one meta while mounted, none after disposal, in every document"
        ]
