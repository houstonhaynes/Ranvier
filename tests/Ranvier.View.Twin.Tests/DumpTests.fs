module Ranvier.View.Twin.Tests.DumpTests

open System
open Expecto
open Ranvier
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Twin
open Ranvier.View.Twin.Tests.Fixtures

type Node' =
    {
        Name: string
        mutable Next: Node' option
    }

type DeepState =
    {
        Cyclic: Node'
        Long: string
        Deep: obj
    }

type ThrowingGetter() =
    member _.Value: int = failwith "The dump invoked a getter"

type OpaqueState =
    {
        Lazy: Lazy<int>
        Pending: System.Threading.Tasks.Task<int>
        Throwing: ThrowingGetter
    }

let private deep =
    Component.define
        (fun _ () ->
            let cyclic = { Name = "a"; Next = None }
            cyclic.Next <- Some cyclic

            let rec nest n : obj =
                if n = 0 then box "bottom" else box [ nest (n - 1) ]

            {
                Cyclic = cyclic
                Long = String ('x', 10_000)
                Deep = nest 200
            })
        (fun t -> t.p [||] [||])

type ProbeState =
    {
        Source: Signal<int>
        Doubled: Memo<int>
    }

let private probe (runs: int ref) =
    Component.define
        (fun _ () ->
            let source = Api.createSignal 1

            {
                Source = source
                Doubled =
                    Api.createMemo (fun _ ->
                        runs.Value <- runs.Value + 1
                        source.Value * 2)
            })
        (fun t -> t.p [||] [| t.text (fun (s: ProbeState) -> string s.Source.Value) |])

let private instanceOf<'T> (session: Session) : 'T =
    let rec find (node: TwinNode) =
        match Inspect.probeOf (unbox node.Native) with
        | Some (Inspect.InstanceProbe (_, instance, _, _)) when (instance :? 'T) -> Some (unbox<'T> instance)
        | _ -> node.Children |> List.tryPick find

    match find (Bridge.nodeOf session.Host) with
    | Some value -> value
    | None -> failwith "no instance"

let private guardedPass commits build =
    let mounted = ref Unchecked.defaultof<Mounting.Mounted>
    let change = ref ignore
    let failures = ResizeArray<string>()

    let capture () =
        if not (isNull (box mounted.Value)) then
            try
                Debug.capture Debug.DumpOptions.defaults mounted.Value
                |> ignore
            with :? InvalidOperationException as error ->
                failures.Add error.Message

    use session =
        Twin.mountView
            { TwinMount.defaults with
                Commits = commits
                Reports = Reports.Collect
            }
            (fun context ->
                let view, update = build capture
                change.Value <- update
                Program.run view context.Backend)

    mounted.Value <- session.Mounted
    session.Act change.Value
    Expect.equal (List.ofSeq failures) [ "Debug.dump ran inside a flush" ] "capture is rejected during the region pass"

[<Tests>]
let tests =
    testList
        "Dump"
        [
            testCase "prepared roots require HTML interfaces even when the local name is svg"
            <| fun () ->
                use session = Twin.mount counter 1
                let document = session.Host.ownerDocument
                let html = document.createElement "svg"
                let supported = Prepared.Template (fun () -> html)
                Expect.equal (supported.Invoke().namespaceURI) (Some "http://www.w3.org/1999/xhtml") "HTML namespace"

                let svg =
                    document.createElementNS (namespaceURI = Some "http://www.w3.org/2000/svg", qualifiedName = "svg")

                let unsupported =
                    Prepared.Template (fun () -> unbox<Fable.Core.TS.Dom.HTMLElement> svg)

                Expect.throwsT<InvalidCastException>
                    (fun () -> unsupported.Invoke () |> ignore)
                    "a foreign SVG element cannot satisfy the prepared HTML root contract"

            for commits in [ Commits.Synchronous; Commits.Queued Scheduling.CollectAndContinue ] do
                testCase $"a keyed row setup rejects capture during {commits} commits"
                <| fun () ->
                    guardedPass commits (fun capture ->
                        let items = Api.createSignal [| 1 |]

                        let child =
                            Component.define
                                (fun _ (_: unit -> int) ->
                                    capture ()
                                    ())
                                (fun t -> t.li [||] [||])

                        let view =
                            Component.define (fun _ () -> ()) (fun t -> t.ul [||] [| t.each id (fun () -> items.Value) child |])

                        Component.render view (), (fun () -> items.Value <- [| 1; 2 |]))

                testCase $"an arm setup rejects capture during {commits} commits"
                <| fun () ->
                    guardedPass commits (fun capture ->
                        let selected = Api.createSignal false

                        let arm =
                            Component.define
                                (fun _ (_: unit -> unit) ->
                                    capture ()
                                    ())
                                (fun t -> t.p [||] [||])

                        let idle =
                            Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.span [||] [||])

                        let view =
                            Component.define (fun _ () -> ()) (fun t ->
                                t.branch (fun () -> selected.Value) (fun active -> if active then t.arm arm () else t.arm idle ()))

                        Component.render view (), (fun () -> selected.Value <- true))

                testCase $"a boundary fallback rejects capture during {commits} commits"
                <| fun () ->
                    guardedPass commits (fun capture ->
                        let source = Api.createAsyncSource<string>()
                        let loading = Component.define (fun _ () -> ()) (fun t -> t.p [||] [||])

                        let failed =
                            Component.define
                                (fun _ (_: BoundaryFailure) ->
                                    capture ()
                                    ())
                                (fun t -> t.p [||] [||])

                        let content =
                            Component.define (fun _ (_: BoundaryStatus) -> ()) (fun t -> t.p [||] [| t.text (fun () -> source.Value) |])

                        let view =
                            Component.define (fun _ () -> ()) (fun t -> t.boundary loading failed content)

                        Component.render view (), (fun () -> source.Fail (exn "failed")))

                testCase $"a hole setup rejects capture during {commits} commits"
                <| fun () ->
                    guardedPass commits (fun capture ->
                        let selected = Api.createSignal false
                        let child = Component.define (fun _ () -> ()) (fun t -> t.p [||] [||])

                        let holed =
                            Component.define (fun _ (_: unit -> unit) -> ()) (fun t ->
                                t.div
                                    [||]
                                    [|
                                        t.view (fun () ->
                                            capture ()
                                            Component.render child ())
                                    |])

                        let idle =
                            Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.span [||] [||])

                        let view =
                            Component.define (fun _ () -> ()) (fun t ->
                                t.branch (fun () -> selected.Value) (fun active -> if active then t.arm holed () else t.arm idle ()))

                        Component.render view (), (fun () -> selected.Value <- true))

            testCase "a nested sibling mount restores the enclosing identity prefix"
            <| fun () ->
                use session = Twin.mount counter 1
                let struct (first, _) = session.Mounted.Range
                let before = first.nextSibling.Value :?> Fable.Core.TS.Dom.Node

                let nested =
                    Mounting.mount session.Graph None ignore session.Host (Some before) (fun context ->
                        Program.run (Component.render counter 2) context.Backend)

                let struct (nestedFirst, _) = nested.Range

                let remove =
                    Inspect.register nestedFirst (Inspect.MountProbe ((fun () -> Attached), false, (fun () -> false), "nested", session.Graph))

                try
                    let captured = Debug.capture Debug.DumpOptions.defaults session.Mounted

                    let paths =
                        captured.Root
                        |> Array.choose (function
                            | Debug.Instance (instance, _) -> Some instance.Path
                            | _ -> None)

                    Expect.equal paths [| "nested-0"; "r-0" |] "only the nested range gets its prefix"
                finally
                    remove ()
                    nested.Dispose ()

            testCase "mounted capture reads each list probe once and ignores sibling mounts"
            <| fun () ->
                use session = Twin.mount counter 1
                let sibling = session.Host.ownerDocument.createComment "["
                session.Host.appendChild sibling |> ignore
                let mutable reads = 0

                let remove =
                    Inspect.register
                        sibling
                        (Inspect.ListProbe (
                            Unchecked.defaultof<_>,
                            (fun () ->
                                reads <- reads + 1
                                [||]),
                            (fun () -> false)
                        ))

                try
                    session.Dump () |> ignore
                    Expect.equal reads 0 "head capture must not traverse a sibling's probes"
                finally
                    remove ()

            testCase "a memo used as the instance is never evaluated by a dump"
            <| fun () ->
                let runs = ref 0
                let source = ref Unchecked.defaultof<Signal<int>>
                let memo = ref Unchecked.defaultof<Memo<int>>

                let definition =
                    Component.define
                        (fun _ () ->
                            source.Value <- Api.createSignal 1

                            memo.Value <-
                                Api.createMemo (fun _ ->
                                    runs.Value <- runs.Value + 1
                                    source.Value.Value * 2)

                            memo.Value)
                        (fun t -> t.p [||] [||])

                use session = Twin.mount definition ()
                session.Graph.Run (fun () -> memo.Value.Value |> ignore)
                session.Graph.Run (fun () -> source.Value.Value <- 5)
                let status = (memo.Value :> INode).Status
                session.Dump () |> ignore
                Expect.equal runs.Value 1 "capture does not recompute the instance"
                Expect.equal (memo.Value :> INode).Status status "status is unchanged"

            testCase "opaque values never run getters, force a lazy or wait for a task"
            <| fun () ->
                let value = lazy (failwith "The dump forced a lazy": int)
                let pending = System.Threading.Tasks.TaskCompletionSource<int>()

                let definition =
                    Component.define
                        (fun _ () ->
                            {
                                Lazy = value
                                Pending = pending.Task
                                Throwing = ThrowingGetter ()
                            })
                        (fun t -> t.p [||] [||])

                use session = Twin.mount definition ()
                let text = session.Dump ()
                Expect.isFalse value.IsValueCreated "lazy remains unevaluated"
                Expect.isFalse pending.Task.IsCompleted "task remains pending"
                Expect.stringContains text "ThrowingGetter" "opaque type remains identifiable"

            testCase "a counter dumps its instance, field and bound text"
            <| fun () ->
                use session = Twin.mount counter 4
                let text = session.Dump ()
                Expect.stringContains text "instance CounterState" "instance"
                Expect.stringContains text "Count  signal  4" "field"
                Expect.stringContains text "=\"4\"" "bound text"

            testCase "dump bounds cyclic, long and deep values"
            <| fun () ->
                use session = Twin.mount deep ()
                let text = session.Dump ()
                Expect.stringContains text "(10000)" "long string cut with its length"
                Expect.stringContains text "<cycle>" "cycle"
                Expect.isLessThan text.Length 65536 "within MaxChars"
                Expect.stringContains text "…" "elided"

            testCase "a 1000-row list dumps 12 rows and one elision line"
            <| fun () ->
                let source = ref Unchecked.defaultof<Signal<int array>>

                use session =
                    Twin.mountView TwinMount.defaults (fun context ->
                        source.Value <- Api.createSignal (Array.init 1000 id)
                        Program.run (Component.render rows source.Value) context.Backend)

                let lines = (session.Dump ()).Split '\n'

                Expect.equal
                    (lines
                     |> Array.filter (fun l -> l.TrimStart().StartsWith "row "))
                        .Length
                    12
                    "rows"

                Expect.equal
                    (lines
                     |> Array.filter (fun l -> l.Contains "… 988 rows (keys 9 … 996)"))
                        .Length
                    1
                    "elision"

            testCase "MaxChars ends the text with the omitted line count"
            <| fun () ->
                use session = Twin.mount counter 1

                let text =
                    session.Dump
                        { Debug.DumpOptions.defaults with
                            MaxChars = 40
                        }

                Expect.stringContains text "lines omitted" "truncated"

            testCase "small MaxRows values retain an elision line"
            <| fun () ->
                let source = ref Unchecked.defaultof<Signal<int array>>

                use session =
                    Twin.mountView TwinMount.defaults (fun context ->
                        source.Value <- Api.createSignal [| 1; 2; 3; 4; 5 |]
                        Program.run (Component.render rows source.Value) context.Backend)

                for maximum in [ 0; 1; 2 ] do
                    let text =
                        session.Dump
                            { Debug.DumpOptions.defaults with
                                MaxRows = maximum
                            }

                    Expect.stringContains text ("… " + string (5 - maximum) + " rows") "all omitted rows are reported"

            testCase "a dump runs no memo and shows a stale memo's previous value"
            <| fun () ->
                let runs = ref 0
                use session = Twin.mount (probe runs) ()

                let state = instanceOf<ProbeState> session

                session.Graph.Run (fun () -> state.Doubled.Value |> ignore)
                Expect.equal runs.Value 1 "one run"
                session.Graph.Run (fun () -> state.Source.Value <- 5)
                let before = runs.Value
                let text = session.Dump ()
                Expect.equal runs.Value before "the dump ran no memo"
                Expect.stringContains text "Doubled  memo  2" "the previous value"
                session.Graph.Run (fun () -> state.Doubled.Value |> ignore)
                Expect.equal runs.Value (before + 1) "the memo was still stale"

            testCase "the JSON form names its schema and parses"
            <| fun () ->
                use session = Twin.mount counter 2
                let json = Debug.json (Debug.capture Debug.DumpOptions.defaults session.Mounted)
                let document = Text.Json.JsonDocument.Parse json
                Expect.equal (document.RootElement.GetProperty("schema").GetString()) "ranvier-view-dump/1" "schema"

            testCase "a dump off the graph's thread raises"
            <| fun () ->
                use session = Twin.mount counter 2
                let mutable failure: exn = null

                let thread =
                    Threading.Thread (fun () ->
                        try
                            session.Dump () |> ignore
                        with e ->
                            failure <- e)

                thread.Start ()
                thread.Join ()
                Expect.isTrue (failure :? InvalidOperationException) "raised"
                Expect.stringContains failure.Message "off the graph's thread" "message"

            testCase "a dump from a row's setup during a synchronous keyed pass raises"
            <| fun () ->
                let source = ref Unchecked.defaultof<Signal<int array>>
                let mounted = ref Unchecked.defaultof<Mounting.Mounted>
                let failures = ResizeArray<string>()

                let dumpingRow =
                    Component.define
                        (fun _ (item: unit -> int) ->
                            if not (isNull (box mounted.Value)) then
                                try
                                    Debug.capture Debug.DumpOptions.defaults mounted.Value
                                    |> ignore
                                with :? InvalidOperationException as e ->
                                    failures.Add e.Message

                            item)
                        (fun t -> t.li [||] [||])

                let dumpingRows =
                    Component.define (fun _ (items: Signal<int array>) -> items) (fun t ->
                        t.ul [||] [| t.each id (fun (items: Signal<int array>) -> items.Value) dumpingRow |])

                use session =
                    Twin.mountView TwinMount.defaults (fun context ->
                        source.Value <- Api.createSignal [| 1 |]
                        Program.run (Component.render dumpingRows source.Value) context.Backend)

                mounted.Value <- session.Mounted
                session.Act (fun () -> source.Value.Value <- [| 1; 2 |])
                Expect.equal (List.ofSeq failures) [ "Debug.dump ran inside a flush" ] "raised in the pass"

            testCase "a list dumped inside the batch that wrote its source shows stale"
            <| fun () ->
                let source = ref Unchecked.defaultof<Signal<int array>>

                use session =
                    Twin.mountView TwinMount.defaults (fun context ->
                        source.Value <- Api.createSignal [| 1; 2 |]
                        Program.run (Component.render rows source.Value) context.Backend)

                Expect.isFalse ((session.Dump ()).Contains "stale") "current before the write"

                let inside =
                    session.Graph.Run (fun () ->
                        Api.batch (fun () ->
                            source.Value.Value <- [| 1; 2; 3 |]
                            session.Dump ()))

                Expect.stringContains inside "list  path=r-0.r0  rows=2  stale" "the last pass, marked stale"
                Expect.stringContains (session.Dump ()) "list  path=r-0.r0  rows=3" "current after the batch"
                Expect.isFalse ((session.Dump ()).Contains "stale") "no longer stale"

            testCase "NodeIds ends each field line with the core node id"
            <| fun () ->
                use session = Twin.mount counter 3

                let text =
                    session.Dump
                        { Debug.DumpOptions.defaults with
                            NodeIds = true
                        }

                Expect.isTrue (Text.RegularExpressions.Regex.IsMatch (text, "Count  signal  3  node=\\d+")) text

            testCase "nodeAt returns the field's core node"
            <| fun () ->
                use session = Twin.mount counter 3
                let dump = Debug.capture Debug.DumpOptions.defaults session.Mounted

                let path =
                    match
                        dump.Root
                        |> Array.tryPick (function
                            | Debug.Instance (instance, _) -> Some instance.Path
                            | _ -> None)
                    with
                    | Some path -> path
                    | None -> failwith "no instance"

                let signal = (instanceOf<CounterState> session).Count

                match Debug.nodeAt (session.Mounted, path + ".Count") with
                | Some node -> Expect.isTrue (obj.ReferenceEquals (node, signal)) "the same signal"
                | None -> failtest "no node"

#if RANVIER_TRACE
            testCase "NodeIds includes the trace snapshot path without recording events"
            <| fun () ->
                use session = Twin.mount counter 3
                let node = (instanceOf<CounterState> session).Count :> INode
                let expected = (Trace.snapshot session.Graph).Nodes[node.Id].Path
                let before = Trace.events session.Graph

                let text =
                    session.Dump
                        { Debug.DumpOptions.defaults with
                            NodeIds = true
                        }

                Expect.stringContains text ("  trace=" + expected) "trace path"
                Expect.equal (Trace.events session.Graph).Length before.Length "no events"

            testCase "a dump records no trace event"
            <| fun () ->
                use session = Twin.mount counter 3
                let before = (Trace.events session.Graph).Length

                Debug.capture Debug.DumpOptions.defaults session.Mounted
                |> ignore

                Expect.equal (Trace.events session.Graph).Length before "trace events"
                Expect.isEmpty (Trace.reconcile session.Graph) "reconcile"
#endif

#if RANVIER_COUNTERS
            testCase "a dump leaves every counter unchanged"
            <| fun () ->
                use session = Twin.mount counter 3
                let before = Counters.Snapshot ()

                Debug.capture Debug.DumpOptions.defaults session.Mounted
                |> ignore

                Expect.equal (Counters.Snapshot ()) before "counters"
#endif
        ]
