module OwnedAsyncChecks

open System.Threading
open System.Threading.Tasks
open global.Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template

[<Global>]
let private globalThis: TS.Es.GlobalThis = jsNative

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

/// <summary>A flight settled by the test, with the token its run received.</summary>
type private Flight =
    {
        Label: string
        Token: CancellationToken
        Task: Task<string>
        Resolve: string -> unit
        Reject: exn -> unit
    }

[<Emit("(() => { let resolve, reject; const task = new Promise((ok, fail) => { resolve = ok; reject = fail; }); return [task, resolve, reject]; })()")>]
let private deferred () : Task<string> * (string -> unit) * (exn -> unit) = jsNative

[<Emit("new Promise(resolve => setTimeout(resolve, 0))")>]
let private nextTask () : JS.Promise<unit> = jsNative

/// <summary>Completes after every pending promise continuation has run.</summary>
let private drain () =
    Async.AwaitPromise (nextTask ())

let private flights = ResizeArray<Flight>()

let private flight (label: string) =
    flights |> Seq.find (fun f -> f.Label = label)

/// <summary>An async value whose runs each start a flight labelled by <c>label</c>, ignoring their token.</summary>
let private loading (label: unit -> string) =
    Api.createAsync (fun _ token ->
        let task, resolve, reject = deferred ()

        flights.Add
            {
                Label = label ()
                Token = token
                Task = task
                Resolve = resolve
                Reject = reject
            }

        task)

let private host () =
    globalThis.document.createElement "main"

let private mount graph (host: HTMLElement) (reported: ResizeArray<exn>) view =
    Mounting.mount graph None reported.AddRange host None (fun context -> Program.run view context.Backend)

type private Item = { Id: int }

let private loadingRow =
    Component.define
        (fun _ (item: unit -> Item) ->
            let id = string (item ()).Id
            loading (fun () -> "row " + id))
        (fun t -> t.element (HtmlTag "li") [||] [| t.text (fun value -> value.Value) |])

let private loadingList =
    Component.define (fun _ (items: Signal<Item array>) -> items) (fun t ->
        t.element (HtmlTag "ul") [||] [| t.each (fun (item: Item) -> item.Id) (fun items -> items.Value) loadingRow |])

let private removedRow () =
    async {
        use graph = new Graph ()
        flights.Clear ()
        let items = graph.Run (fun () -> Api.createSignal [| { Id = 1 }; { Id = 2 } |])
        let host = host ()
        let reported = ResizeArray<exn>()
        let mounted = mount graph host reported (Component.render loadingList items)
        equal "" host.textContent
        (flight "row 1").Resolve "one"
        do! drain ()
        equal "one" host.textContent
        graph.Run (fun () -> items.Value <- [| { Id = 1 } |])
        equal (false, true) ((flight "row 1").Token.IsCancellationRequested, (flight "row 2").Token.IsCancellationRequested)
        (flight "row 2").Resolve "late"
        do! drain ()
        equal ("one", 1., 0) (host.textContent, host.querySelectorAll("li").length, reported.Count)
        mounted.Dispose ()
    }
    |> Async.StartAsPromise

let private removedRowFailure () =
    async {
        use graph = new Graph ()
        flights.Clear ()
        let items = graph.Run (fun () -> Api.createSignal [| { Id = 1 }; { Id = 2 } |])
        let host = host ()
        let reported = ResizeArray<exn>()
        let mounted = mount graph host reported (Component.render loadingList items)
        graph.Run (fun () -> items.Value <- [| { Id = 1 } |])
        (flight "row 2").Reject(exn "late failure")
        do! drain ()
        (flight "row 1").Resolve "one"
        do! drain ()
        equal ("one", 0) (host.textContent, reported.Count)
        mounted.Dispose ()
    }
    |> Async.StartAsPromise

let private cooperativeRow =
    Component.define
        (fun _ (item: unit -> Item) ->
            let id = string (item ()).Id

            Api.createAsync (fun _ token ->
                let task, resolve, reject = deferred ()

                token.Register (fun () -> reject (System.OperationCanceledException token))
                |> ignore

                flights.Add
                    {
                        Label = "row " + id
                        Token = token
                        Task = task
                        Resolve = resolve
                        Reject = reject
                    }

                task))
        (fun t -> t.element (HtmlTag "li") [||] [| t.text (fun value -> value.Value) |])

let private cooperativeList =
    Component.define (fun _ (items: Signal<Item array>) -> items) (fun t ->
        t.element
            (HtmlTag "ul")
            [||]
            [|
                t.each (fun (item: Item) -> item.Id) (fun items -> items.Value) cooperativeRow
            |])

let private cooperativeRemoval () =
    async {
        use graph = new Graph ()
        flights.Clear ()
        let items = graph.Run (fun () -> Api.createSignal [| { Id = 1 }; { Id = 2 } |])
        let host = host ()
        let reported = ResizeArray<exn>()
        let mounted = mount graph host reported (Component.render cooperativeList items)
        graph.Run (fun () -> items.Value <- [| { Id = 1 } |])
        do! drain ()
        (flight "row 1").Resolve "one"
        do! drain ()
        equal ("one", 0) (host.textContent, reported.Count)
        mounted.Dispose ()
        do! drain ()
        equal (0., 0) (host.childNodes.length, reported.Count)
    }
    |> Async.StartAsPromise

let private disposedMount () =
    async {
        use graph = new Graph ()
        flights.Clear ()
        let items = graph.Run (fun () -> Api.createSignal [| { Id = 1 }; { Id = 2 } |])
        let host = host ()
        let reported = ResizeArray<exn>()
        let mounted = mount graph host reported (Component.render loadingList items)
        mounted.Dispose ()

        equal
            [ true; true ]
            (flights
             |> Seq.map _.Token.IsCancellationRequested
             |> List.ofSeq)

        (flight "row 1").Resolve "late"
        (flight "row 2").Reject(exn "late failure")
        do! drain ()
        equal (0., 0) (host.childNodes.length, reported.Count)
    }
    |> Async.StartAsPromise

let private decadeArm =
    Component.define
        (fun _ (decade: unit -> int) ->
            let initial = string (decade ())
            loading (fun () -> "decade " + initial))
        (fun t -> t.span [||] [| t.text (fun value -> value.Value) |])

let private decades =
    Component.define (fun _ (value: Signal<int>) -> value) (fun t ->
        t.div
            [||]
            [|
                t.branch (fun value -> value.Value / 10) (fun decade -> t.arm decadeArm decade)
            |])

let private replacedArm () =
    async {
        use graph = new Graph ()
        flights.Clear ()
        let value = graph.Run (fun () -> Api.createSignal 11)
        let host = host ()
        let reported = ResizeArray<exn>()
        let mounted = mount graph host reported (Component.render decades value)
        graph.Run (fun () -> value.Value <- 21)
        equal (true, false) ((flight "decade 1").Token.IsCancellationRequested, (flight "decade 2").Token.IsCancellationRequested)
        (flight "decade 1").Resolve "late"
        do! drain ()
        equal "" host.textContent
        (flight "decade 2").Resolve "two"
        do! drain ()
        equal ("two", 1., 0) (host.textContent, host.querySelectorAll("span").length, reported.Count)
        mounted.Dispose ()
        equal true (flight "decade 2").Token.IsCancellationRequested
    }
    |> Async.StartAsPromise

let private search =
    Component.define (fun _ (query: Signal<string>) -> loading (fun () -> "query " + query.Value)) (fun t ->
        t.span [||] [| t.text (fun value -> value.Value) |])

let private supersededFlight () =
    async {
        use graph = new Graph ()
        flights.Clear ()
        let query = graph.Run (fun () -> Api.createSignal "a")
        let host = host ()
        let reported = ResizeArray<exn>()
        let mounted = mount graph host reported (Component.render search query)
        graph.Run (fun () -> query.Value <- "b")
        equal (true, false) ((flight "query a").Token.IsCancellationRequested, (flight "query b").Token.IsCancellationRequested)
        (flight "query a").Resolve "stale"
        do! drain ()
        equal "" host.textContent
        (flight "query b").Resolve "fresh"
        do! drain ()
        equal ("fresh", 0) (host.textContent, reported.Count)
        mounted.Dispose ()
    }
    |> Async.StartAsPromise

let cases: (string * (unit -> JS.Promise<unit>)) array =
    [|
        "a removed each row cancels its flight and its later result writes nothing", removedRow
        "a removed each row's later failure reports nothing", removedRowFailure
        "a removed row whose flight rejects on cancellation reports nothing", cooperativeRemoval
        "disposing a mount cancels every row's flight and later completions report nothing", disposedMount
        "a branch key change cancels the replaced arm's flight and its later result writes nothing", replacedArm
        "a superseded flight's later result is dropped and the current flight's result shows", supersededFlight
    |]
