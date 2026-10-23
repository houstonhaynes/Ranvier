module Ranvier.View.Twin.Tests.AsyncTests

open System.Threading.Tasks
open Expecto
open Ranvier
open Ranvier.View
open Ranvier.View.Twin

type private Loaded = { Result: AsyncMemo<string> }

let private loader (source: TaskCompletionSource<string>) =
    Component.define
        (fun _ () ->
            {
                Result = Api.createAsync (fun _ _ -> source.Task)
            })
        (fun t ->
            t.element
                (HtmlTag "p")
                [||]
                [|
                    t.text (fun s ->
                        match s.Result.TryValue with
                        | Ready value -> value
                        | Pending -> "loading"
                        | Failed error -> error.Message)
                |])

/// <summary>The flights of the current test, each with its result source and the token it ran under, by key.</summary>
type private Flights() =
    member val Sources = System.Collections.Generic.Dictionary<int, TaskCompletionSource<string>>()
    member val Tokens = System.Collections.Generic.Dictionary<int, System.Threading.CancellationToken>()

    member this.Start (key: int) (token: System.Threading.CancellationToken) =
        let source = TaskCompletionSource<string>()
        this.Sources[key] <- source
        this.Tokens[key] <- token
        source.Task

let private shown (result: AsyncMemo<string>) =
    match result.TryValue with
    | Ready value -> value
    | Pending -> "loading"
    | Failed error -> error.Message

let private flightRow (flights: Flights) =
    Component.define
        (fun _ (item: unit -> int) ->
            {
                Result = Api.createAsync (fun _ token -> flights.Start (item ()) token)
            })
        (fun t -> t.li [||] [| t.text (fun (s: Loaded) -> shown s.Result) |])

let private flightRows (flights: Flights) =
    Component.define (fun _ (items: Signal<int array>) -> items) (fun t ->
        t.ul
            [||]
            [|
                t.each (fun (n: int) -> n) (fun (items: Signal<int array>) -> items.Value) (flightRow flights)
            |])

let private flightOf (flights: Flights) =
    Component.define
        (fun _ (input: Signal<int>) ->
            {
                Result = Api.createAsync (fun _ token -> flights.Start input.Value token)
            })
        (fun t -> t.p [||] [| t.text (fun (s: Loaded) -> shown s.Result) |])

let private mountRows (flights: Flights) (initial: int array) =
    let items = ref Unchecked.defaultof<Signal<int array>>

    let session =
        Twin.mountView TwinMount.defaults (fun context ->
            items.Value <- Api.createSignal initial
            Program.run (Component.render (flightRows flights) items.Value) context.Backend)

    session, items.Value

let private queued =
    { TwinMount.defaults with
        Commits = Commits.Queued Ranvier.View.Dom.Scheduling.CollectAndContinue
    }

[<Tests>]
let tests =
    testList
        "Async"
        [
            testCase "a result completed off-thread applies when the session settles"
            <| fun () ->
                let source = TaskCompletionSource<string>()
                use session = Twin.mountWith queued (loader source) ()
                session.Act ignore
                Expect.equal (session.Text ()) "loading" "pending"
                Task.Run(fun () -> source.SetResult "done").Wait()
                session.Act ignore
                Expect.equal (session.Text ()) "done" "settled"

            testCase "a result completed on the session's thread applies when the session settles"
            <| fun () ->
                let source = TaskCompletionSource<string>()
                use session = Twin.mountWith TwinMount.defaults (loader source) ()
                Expect.equal (session.Text ()) "loading" "pending"
                session.Act (fun () -> source.SetResult "here")
                Expect.equal (session.Text ()) "here" "settled"

            testCase "removing a row cancels its flight and its later result writes nothing"
            <| fun () ->
                let flights = Flights ()
                let session, items = mountRows flights [| 1; 2 |]
                use _ = session
                session.Act (fun () -> items.Value <- [| 1 |])
                Expect.isTrue flights.Tokens[2].IsCancellationRequested "the removed row's token"
                Expect.isFalse flights.Tokens[1].IsCancellationRequested "the kept row's token"
                session.Act (fun () -> flights.Sources[2].SetResult "late")
                Expect.equal (session.Text ()) "loading" "the late result writes nothing"
                Expect.isEmpty session.Reports "no report"

            testCase "a superseded flight's result is dropped and the current flight's result shows"
            <| fun () ->
                let flights = Flights ()
                let input = ref Unchecked.defaultof<Signal<int>>

                use session =
                    Twin.mountView TwinMount.defaults (fun context ->
                        input.Value <- Api.createSignal 1
                        Program.run (Component.render (flightOf flights) input.Value) context.Backend)

                session.Act (fun () -> input.Value.Value <- 2)
                session.Act (fun () -> flights.Sources[1].SetResult "one")
                Expect.equal (session.Text ()) "loading" "the superseded result is dropped"
                session.Act (fun () -> flights.Sources[2].SetResult "two")
                Expect.equal (session.Text ()) "two" "the current result shows"

            testCase "disposing a mount cancels every row's flight"
            <| fun () ->
                let flights = Flights ()
                let session, _ = mountRows flights [| 1; 2; 3 |]
                session.Mounted.Dispose ()

                Expect.allEqual [ for key in [ 1; 2; 3 ] -> flights.Tokens[key].IsCancellationRequested ] true "every token"

                (session :> System.IDisposable).Dispose()

            testCase "queued commits wait for the clock"
            <| fun () ->
                let count = ref Unchecked.defaultof<Signal<int>>

                let shown =
                    Component.define (fun _ (value: Signal<int>) -> value) (fun t ->
                        t.p [||] [| t.text (fun (value: Signal<int>) -> string value.Value) |])

                use session =
                    Twin.mountView queued (fun context ->
                        count.Value <- Api.createSignal 1
                        Program.run (Component.render shown count.Value) context.Backend)

                session.Graph.Run (fun () -> count.Value.Value <- 2)
                Expect.equal (session.Text ()) "1" "stale until the clock runs"
                Expect.equal session.Clock.Pending.Microtasks 1 "one queued commit"
                session.Clock.RunMicrotasks () |> ignore
                Expect.equal (session.Text ()) "2" "committed"
        ]
