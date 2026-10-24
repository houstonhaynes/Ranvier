module Ranvier.Tests.TraceContractTests

open Expecto
open Ranvier

#if RANVIER_TRACE
type private Point = { X: int; Y: int }

type private Unregistered = { Name: string }

let private pointCodec: TraceCodec =
    {
        Name = "point"
        Version = 2
        Encode =
            fun value ->
                match value with
                | :? Point as p -> Some (string p.X + "," + string p.Y)
                | _ -> None
        Decode =
            fun data ->
                let parts = data.Split ','
                box { X = int parts[0]; Y = int parts[1] }
    }

let private refOf (g: Graph) (node: INode) =
    match TraceContract.nodeRef (Trace.snapshot g) node.Id with
    | Some r -> r
    | None -> failwith "the node must have a ref"

/// <summary>
/// A labelled memo whose body creates a labelled memo, run twice by a write; the refs of both inner memos and the
/// outer memo.
/// </summary>
let private labelledProgram () =
    use g = new Graph ()
    use _ = g.Activate ()
    let count = Trace.named "count" (fun () -> createSignal 1)
    let inners = ResizeArray<INode>()

    let outer =
        Trace.named "outer" (fun () ->
            createMemoWith (fun _ ->
                inners.Add (Trace.named "inner" (fun () -> createMemo (fun _ -> 0)))
                count.Value))

    outer.Value |> ignore
    count.Value <- 2
    outer.Value |> ignore

    [
        yield refOf g count
        yield refOf g outer
        for inner in inners do
            yield refOf g inner
    ]

let private lastSeq (g: Graph) =
    (Array.last (Trace.events g)).Seq

type private Untraced() =
    interface INode with
        member _.Id = 1
        member _.Status = Status.None

/// <summary>A diamond with a dynamic dependency, run, rewritten and partly disposed.</summary>
let private diamond (g: Graph) =
    let a = Trace.named "a" (fun () -> createSignal 1)
    let flag = createSignal true
    let b = Trace.named "b" (fun () -> createMemo (fun _ -> a.Value + 1))
    let c = createMemo (fun _ -> a.Value * 2)
    let d = createMemo (fun _ -> if flag.Value then b.Value else c.Value)
    let e = createEffect (fun () -> d.Value |> ignore)
    a.Value <- 2
    flag.Value <- false
    c.Dispose ()
    e

#if !FABLE_COMPILER
let private flags =
    System.Reflection.BindingFlags.Public
    ||| System.Reflection.BindingFlags.NonPublic

/// <summary>The types reachable from <c>root</c> through record fields, union case fields and generic arguments.</summary>
let private reachable (root: System.Type) =
    let seen = System.Collections.Generic.HashSet<System.Type>()

    let rec walk (t: System.Type) =
        if seen.Add t then
            if t.IsGenericType then
                for arg in t.GetGenericArguments () do
                    walk arg

            if Reflection.FSharpType.IsRecord (t, flags) then
                for p in Reflection.FSharpType.GetRecordFields (t, flags) do
                    walk p.PropertyType
            elif
                Reflection.FSharpType.IsUnion (t, flags)
                && not (
                    t.IsGenericType
                    && t.GetGenericTypeDefinition () = typedefof<option<_>>
                )
                && not (
                    t.IsGenericType
                    && t.GetGenericTypeDefinition () = typedefof<list<_>>
                )
            then
                for case in Reflection.FSharpType.GetUnionCases (t, flags) do
                    for p in case.GetFields () do
                        walk p.PropertyType

    walk root
    seen
#endif
#endif

[<Tests>]
let tests =
    testList
        "TraceContract"
        [
#if RANVIER_TRACE
            test "a scalar payload encodes as Exact" {
                let codecs = TraceCodecs.ofList []
                Expect.equal (TraceCodecs.encode codecs (box 3)) (TracePayload.Exact (TraceScalar.Int 3)) "an int is exact"

                Expect.equal
                    (TraceCodecs.encode codecs (box "a\nb"))
                    (TracePayload.Exact (TraceScalar.String "a\nb"))
                    "a string keeps its line breaks"

                Expect.equal (TraceCodecs.encode codecs (box true)) (TracePayload.Exact (TraceScalar.Bool true)) "a bool is exact"
                Expect.equal (TraceCodecs.encode codecs (box 2.5)) (TracePayload.Exact (TraceScalar.Float 2.5)) "a float is exact"
                Expect.equal (TraceCodecs.encode codecs null) TracePayload.Null "null is Null"
            }

            test "a fractional or non-finite float encodes as Float on every platform" {
                let codecs = TraceCodecs.ofList []
                Expect.equal (TraceCodecs.encode codecs (box -0.75)) (TracePayload.Exact (TraceScalar.Float -0.75)) "-0.75"
                Expect.equal (TraceCodecs.encode codecs (box 1e10)) (TracePayload.Exact (TraceScalar.Float 1e10)) "beyond int range"

                Expect.equal (TraceCodecs.encode codecs (box infinity)) (TracePayload.Exact (TraceScalar.Float infinity)) "infinity"

                let isNaNFloat =
                    match TraceCodecs.encode codecs (box nan) with
                    | TracePayload.Exact (TraceScalar.Float f) -> System.Double.IsNaN f
                    | _ -> false

                Expect.isTrue isNaNFloat "NaN"
                Expect.equal (TraceCodecs.encode codecs (box -5)) (TracePayload.Exact (TraceScalar.Int -5)) "a negative int"
            }

            test "an integral float encodes by the platform's number representation" {
                let codecs = TraceCodecs.ofList []
#if FABLE_COMPILER
                let expected = TracePayload.Exact (TraceScalar.Int 2)
#else
                let expected = TracePayload.Exact (TraceScalar.Float 2.0)
#endif
                Expect.equal (TraceCodecs.encode codecs (box 2.0)) expected "Int on Fable, Float on .NET"
            }

            test "a registered codec encodes as Encoded and decodes back" {
                let codecs = TraceCodecs.ofList [ pointCodec ]
                let payload = TraceCodecs.encode codecs (box { X = 1; Y = 2 })
                Expect.equal payload (TracePayload.Encoded ("point", 2, "1,2")) "the codec's name, version and data"
                Expect.equal (TraceCodecs.decode codecs payload) (box { X = 1; Y = 2 }) "decoding gives the value back"
            }

            test "the first codec that claims a value encodes it" {
                let other =
                    { pointCodec with
                        Name = "other"
                        Encode = fun _ -> Some "x"
                    }

                let codecs = TraceCodecs.ofList [ pointCodec; other ]
                Expect.equal (TraceCodecs.encode codecs (box { X = 1; Y = 2 })) (TracePayload.Encoded ("point", 2, "1,2")) "the earlier codec wins"

                Expect.equal
                    (TraceCodecs.encode codecs (box { Name = "n" }))
                    (TracePayload.Encoded ("other", 2, "x"))
                    "a later codec takes what the earlier declines"
            }

            test "a value with no codec encodes as Lossy of its valueText" {
                let value = box { Name = "n" }
                let payload = TraceCodecs.encode (TraceCodecs.ofList [ pointCodec ]) value
                Expect.equal payload (TracePayload.Lossy (TraceModel.valueText value)) "the diagnostic text, marked lossy"
            }

            test "strict codecs raise for a value with no codec" {
                let codecs = TraceCodecs.strict (TraceCodecs.ofList [ pointCodec ])

                Expect.throwsT<TraceCodecException>
                    (fun () ->
                        TraceCodecs.encode codecs (box { Name = "n" })
                        |> ignore)
                    "strict must raise"

                Expect.equal (TraceCodecs.encode codecs (box 3)) (TracePayload.Exact (TraceScalar.Int 3)) "a scalar stays exact"
            }

            test "decoding raises for an unknown codec, an unknown version and a lossy payload" {
                let codecs = TraceCodecs.ofList [ pointCodec ]

                Expect.throwsT<TraceCodecException>
                    (fun () ->
                        TraceCodecs.decode codecs (TracePayload.Encoded ("other", 2, "1,2"))
                        |> ignore)
                    "an unknown codec must raise"

                Expect.throwsT<TraceCodecException>
                    (fun () ->
                        TraceCodecs.decode codecs (TracePayload.Encoded ("point", 1, "1,2"))
                        |> ignore)
                    "an unknown version must raise"

                Expect.throwsT<TraceCodecException>
                    (fun () ->
                        TraceCodecs.decode codecs (TracePayload.Lossy "text")
                        |> ignore)
                    "a lossy payload must raise"
            }

            test "decoding Exact and Null gives the scalar and null" {
                let codecs = TraceCodecs.ofList []
                Expect.equal (TraceCodecs.decode codecs (TracePayload.Exact (TraceScalar.Int 3))) (box 3) "an int"
                Expect.equal (TraceCodecs.decode codecs (TracePayload.Exact (TraceScalar.String "s"))) (box "s") "a string"
                Expect.isNull (TraceCodecs.decode codecs TracePayload.Null) "null"
            }

            test "a chain of Trace.named labels gives a Named ref" {
                match labelledProgram () with
                | count :: outer :: inner :: _ ->
                    Expect.equal (count.Path, count.Stability, count.Label) ("/count", TracePathStability.Named, Some "count") "a labelled signal"
                    Expect.equal (outer.Path, outer.Stability) ("/outer", TracePathStability.Named) "a labelled memo"
                    Expect.equal (inner.Path, inner.Stability) ("/outer/inner@1", TracePathStability.Named) "a labelled memo under a labelled host"
                | refs -> failtestf "expected three refs, got %A" refs
            }

            test "two graphs built by the same program give equal refs" {
                Expect.equal (labelledProgram ()) (labelledProgram ()) "a ref holds graph-local identity alone"
            }

            test "an @k incarnation of a named path stays Named" {
                let second = List.last (labelledProgram ())
                Expect.equal (second.Path, second.Stability) ("/outer/inner@2", TracePathStability.Named) "the rerun's inner memo"
            }

#if FABLE_COMPILER
            test "an unlabelled node gives a Named ref under Fable, which records no creation sites" {
                use g = new Graph ()
                use _ = g.Activate ()
                let site = createSignal 1
                Expect.equal (refOf g site).Stability TracePathStability.Named "a kind segment"
            }
#else
            test "an unlabelled node, or a labelled node under one, gives a Located ref" {
                use g = new Graph ()
                use _ = g.Activate ()
                let site = createSignal 1
                let mutable inner = Unchecked.defaultof<Memo<int>>

                let host =
                    createMemoWith (fun _ ->
                        inner <- Trace.named "inner" (fun () -> createMemo (fun _ -> 0))
                        site.Value)

                host.Value |> ignore
                Expect.equal (refOf g site).Stability TracePathStability.Located "a site segment"
                Expect.equal (refOf g host).Stability TracePathStability.Located "an unlabelled memo"
                Expect.equal (refOf g inner).Stability TracePathStability.Located "a label under a site segment"
                Expect.equal (refOf g inner).Label (Some "inner") "the node's own label"
            }
#endif

            test "a labelled node under createRoot gives a Named ref" {
                use g = new Graph ()
                use _ = g.Activate ()
                let mutable inner = Unchecked.defaultof<Memo<int>>

                createRoot (fun _ -> inner <- Trace.named "inner" (fun () -> createMemo (fun _ -> 1)))
                |> ignore

                Expect.equal
                    ((refOf g inner).Path, (refOf g inner).Stability)
                    ("/root#0/inner", TracePathStability.Named)
                    "a root scope counts by order"
            }

            test "two runs of the same program give equal refs" {
                let first = labelledProgram ()
                let second = labelledProgram ()
                Expect.equal second first "the refs must not depend on the run"
            }

            test "a node absent from the snapshot has no ref" { Expect.isNone (TraceContract.nodeRef TraceModel.emptySnapshot 7) "no node, no ref" }

            test "lookup finds a live node by id and by path" {
                use g = new Graph ()
                use _ = g.Activate ()
                let count = Trace.named "count" (fun () -> createSignal 1)
                let live = TraceLookup.Live (refOf g count)
                Expect.equal (Trace.lookup g (TraceAddress.ById (count :> INode).Id)) live "by id"
                Expect.equal (Trace.lookup g (TraceAddress.ByPath "/count")) live "by path"
                Expect.equal (Trace.lookupNode g count) live "by node"
            }

            test "lookup reports a node disposed in the retained events with its Dispose seq" {
                use g = new Graph ()
                use _ = g.Activate ()
                let m = Trace.named "total" (fun () -> createMemo (fun _ -> 1))
                m.Value |> ignore
                let r = refOf g m
                m.Dispose ()

                let disposed =
                    Trace.events g
                    |> Array.findBack (fun e -> e.Kind = TraceEventKind.Dispose && e.Node = r.Node)

                Expect.equal (Trace.lookupNode g m) (TraceLookup.Disposed (r, disposed.Seq)) "the ref and the Dispose seq"
            }

            test "lookup reports a node disposed before the checkpoint as Missing once the checkpoint releases it" {
                use g = new Graph ()
                use _ = g.Activate ()
                let m = Trace.named "total" (fun () -> createMemo (fun _ -> 1))
                m.Value |> ignore
                m.Dispose ()
                Trace.checkpoint g (lastSeq g) "cut"
                Expect.equal (Trace.lookupNode g m) TraceLookup.Missing "the pruned checkpoint holds no such node"
                Expect.equal (Trace.lookup g (TraceAddress.ByPath "/total")) TraceLookup.Missing "nor its path"
            }

            test "lookup reports a disposed node held by an unpruned checkpoint with seq 0" {
                use g = new Graph ()
                use _ = g.Activate ()
                let m = createMemo (fun _ -> 1)
                m.Value |> ignore
                m.Dispose ()
                let folded = TraceModel.snapshot (Trace.events g)
                let r = (TraceContract.nodeRef folded (m :> INode).Id).Value

                Expect.equal
                    (TraceContract.lookup folded [||] (TraceAddress.ById r.Node))
                    (TraceLookup.Disposed (r, 0))
                    "the checkpoint holds no Dispose seq"
            }

            test "lookup reports a live node created before the checkpoint as Checkpointed" {
                use g = new Graph ()
                use _ = g.Activate ()
                let count = Trace.named "count" (fun () -> createSignal 1)
                Trace.checkpoint g (lastSeq g) "cut"
                count.Value <- 2
                let r = refOf g count
                Expect.equal (Trace.lookupNode g count) (TraceLookup.Checkpointed r) "by node"
                Expect.equal (Trace.lookup g (TraceAddress.ByPath "/count")) (TraceLookup.Checkpointed r) "by path"
            }

            test "lookup reports an unknown id or path as Missing" {
                use g = new Graph ()
                use _ = g.Activate ()
                createSignal 1 |> ignore
                Expect.equal (Trace.lookup g (TraceAddress.ById 999)) TraceLookup.Missing "an id past every node"
                Expect.equal (Trace.lookup g (TraceAddress.ByPath "/nowhere")) TraceLookup.Missing "a path no node holds"
            }

            test "lookupNode reports a node of another graph or an untraced node as Foreign" {
                use g = new Graph ()
                use other = new Graph ()
                let mine = using (g.Activate ()) (fun _ -> createSignal 1)
                let theirs = using (other.Activate ()) (fun _ -> createSignal 1)
                Expect.equal (theirs :> INode).Id (mine :> INode).Id "both graphs number their first node alike"
                Expect.equal (Trace.lookupNode g theirs) TraceLookup.Foreign "another graph's node"
                Expect.equal (Trace.lookupNode g (Untraced ())) TraceLookup.Foreign "a node no graph traces"
                Expect.equal (Trace.lookupNode g mine) (TraceLookup.Live (refOf g mine)) "the graph's own node"
            }

            test "state matches the snapshot node for node and edge for edge" {
                use g = new Graph ()
                use _ = g.Activate ()
                diamond g |> ignore
                let snap = Trace.snapshot g
                let state = Trace.state g (TraceCodecs.ofList [])

                Expect.equal (state.Seq, state.Root) (snap.Seq, snap.Root) "the read point and the root"
                Expect.equal (Map.keys state.Nodes |> List.ofSeq) (Map.keys snap.Nodes |> List.ofSeq) "the same nodes"

                for KeyValue (id, n) in snap.Nodes do
                    let s = state.Nodes[id]
                    Expect.equal s.Ref (TraceContract.nodeRef snap id).Value "the node's ref"

                    Expect.equal
                        (s.Kind, s.Owner, s.Created, s.Runs, s.LastRun, s.PendingMark, s.Status)
                        (n.Kind, n.Owner, n.Created, n.Runs, n.LastRun, n.PendingMark, n.Status)
                        "the node's fields"

                    Expect.equal s.Value.IsSome n.Value.IsSome "a value exactly where the snapshot has one"

                Expect.equal state.Owners snap.Owners "the same owners"
                Expect.equal state.Sources snap.Sources "the same sources"
                Expect.equal state.Observers snap.Observers "the same observers"
            }

            test "state carries the schema, the checkpoint and the first retained seq" {
                use g = new Graph ()
                use _ = g.Activate ()
                let count = createSignal 1
                let before = Trace.state g (TraceCodecs.ofList [])
                Expect.equal (before.Schema, before.Checkpoint, before.SeqFrom) (3, None, 1) "before a checkpoint"
                let cut = lastSeq g
                Trace.checkpoint g cut "cut"
                count.Value <- 2
                let after = Trace.state g (TraceCodecs.ofList [])
                Expect.equal (after.Schema, after.Checkpoint, after.SeqFrom) (3, Some "cut", cut + 1) "after a checkpoint"
            }

            test "state encodes retained values through the codecs" {
                use g = new Graph ()
                use _ = g.Activate ()
                let point = createSignal { X = 0; Y = 0 }
                let count = createSignal 0
                let other = createSignal { Name = "a" }
                point.Value <- { X = 1; Y = 2 }
                count.Value <- 3
                other.Value <- { Name = "b" }
                let state = Trace.state g (TraceCodecs.ofList [ pointCodec ])

                let valueOf (node: INode) =
                    state.Nodes[node.Id].Value

                Expect.equal (valueOf point) (Some (TracePayload.Encoded ("point", 2, "1,2"))) "a codec's value"
                Expect.equal (valueOf count) (Some (TracePayload.Exact (TraceScalar.Int 3))) "a scalar"
                Expect.equal (valueOf other) (Some (TracePayload.Lossy (TraceModel.valueText (box { Name = "b" })))) "a value with no codec"
            }

            test "lastSeq is the last event's seq, and the checkpoint's once the checkpoint drops every event" {
                use g = new Graph ()
                use _ = g.Activate ()
                let count = createSignal 1
                count.Value <- 2
                let events = Trace.events g
                let last = events[events.Length - 1].Seq
                Expect.equal (Trace.lastSeq g) last "the last event"
                Trace.checkpoint g last "cut"
                Expect.equal (Trace.lastSeq g) last "the checkpoint's seq"
                count.Value <- 3
                Expect.isGreaterThan (Trace.lastSeq g) last "a later write"
                Expect.equal (Trace.lastSeq g) (Trace.state g (TraceCodecs.ofList [])).Seq "the state's read point"
            }

            test "state gives a memo that moved to a null value as Null" {
                use g = new Graph ()
                use _ = g.Activate ()
                let x = createSignal 1
                let text = createMemo (fun _ -> if x.Value = 1 then "a" else null)
                let opt = createMemo (fun _ -> if x.Value = 1 then Some 1 else None)
                text.Value |> ignore
                opt.Value |> ignore

                let valueOf (node: INode) =
                    (Trace.state g (TraceCodecs.ofList [])).Nodes[node.Id].Value

                Expect.equal (valueOf text) (Some (TracePayload.Exact (TraceScalar.String "a"))) "before the move"
                x.Value <- 2
                text.Value |> ignore
                opt.Value |> ignore
                Expect.equal (valueOf text) (Some TracePayload.Null) "a null string"
                Expect.equal (valueOf opt) (Some TracePayload.Null) "None"
            }

            test "state gives a memo that moved to null before a checkpoint as Lossy of null's text" {
                use g = new Graph ()
                use _ = g.Activate ()
                let x = createSignal 1
                let text = createMemo (fun _ -> if x.Value = 1 then "a" else null)
                text.Value |> ignore
                x.Value <- 2
                text.Value |> ignore
                Trace.checkpoint g (Trace.lastSeq g) "cut"
                let state = Trace.state g (TraceCodecs.ofList [])
                Expect.equal state.Nodes[(text :> INode).Id].Value (Some (TracePayload.Lossy (TraceModel.valueText null))) "the folded null"
                Expect.equal (Trace.snapshot g).Nodes[(text :> INode).Id].Value (Some (TraceModel.valueText null)) "the snapshot holds the null"
            }

            test "state gives an async source that settled to null as Null, and as Lossy of null's text after a checkpoint" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createAsyncSource<string>()
                source.Settle "a"
                source.Settle null
                let id = (source :> INode).Id
                Expect.equal (Trace.state g (TraceCodecs.ofList [])).Nodes[id].Value (Some TracePayload.Null) "the live null"
                Expect.equal (Trace.snapshot g).Nodes[id].Value (Some (TraceModel.valueText null)) "the snapshot holds the null"
                Trace.checkpoint g (Trace.lastSeq g) "cut"

                Expect.equal
                    (Trace.state g (TraceCodecs.ofList [])).Nodes[id].Value
                    (Some (TracePayload.Lossy (TraceModel.valueText null)))
                    "the folded null"
            }

            test "state gives a projection that moved without a value as None" {
                use g = new Graph ()
                use _ = g.Activate ()
                let rows = createSignal [ 1; 2 ]
                let p = createProjection id id (fun () -> rows.Value)
                p.Keys |> ignore
                rows.Value <- [ 2; 3 ]
                p.Keys |> ignore
                let state = Trace.state g (TraceCodecs.ofList [])

                let projections =
                    state.Nodes
                    |> Map.filter (fun _ n -> n.Kind = TraceNodeKind.Projection)

                Expect.isNonEmpty projections "the projection is traced"

                for KeyValue (_, n) in projections do
                    Expect.equal n.Value None "a projection records no value"
            }

            test "state gives a value folded into the checkpoint as Lossy of its folded text" {
                use g = new Graph ()
                use _ = g.Activate ()
                let count = createSignal 0
                let point = createSignal { X = 0; Y = 0 }
                count.Value <- 5
                point.Value <- { X = 1; Y = 2 }
                Trace.checkpoint g (lastSeq g) "cut"
                let codecs = TraceCodecs.ofList [ pointCodec ]
                let folded = Trace.state g codecs

                let valueOf (state: TraceGraphState) (node: INode) =
                    state.Nodes[node.Id].Value

                Expect.equal (valueOf folded count) (Some (TracePayload.Lossy "5")) "the checkpoint holds text alone"
                Expect.equal (valueOf folded point) (Some (TracePayload.Lossy (TraceModel.valueText (box { X = 1; Y = 2 })))) "even with a codec"
                count.Value <- 6
                Expect.equal (valueOf (Trace.state g codecs) count) (Some (TracePayload.Exact (TraceScalar.Int 6))) "a retained write is exact again"
            }

            test "strict state raises for a retained value with no codec" {
                use g = new Graph ()
                use _ = g.Activate ()
                let other = createSignal { Name = "a" }
                other.Value <- { Name = "b" }

                Expect.throwsT<TraceCodecException>
                    (fun () ->
                        Trace.state g (TraceCodecs.strict (TraceCodecs.ofList []))
                        |> ignore)
                    "strict must raise"
            }

            test "runs project the history with encoded values" {
                use g = new Graph ()
                use _ = g.Activate ()
                let x = createSignal 1
                let m = createMemo (fun _ -> { X = x.Value; Y = 0 })
                m.Value |> ignore
                x.Value <- 2
                m.Value |> ignore
                let history = Trace.history g m
                let runs = Trace.runs g (TraceCodecs.ofList [ pointCodec ]) m

                Expect.equal
                    (runs
                     |> List.map (fun r -> r.Run, r.Start, r.Status, r.Moved, r.Root, r.Flush))
                    (history.Runs
                     |> List.map (fun r -> r.Run, r.Start, r.Status, r.Moved, r.Root, r.Flush))
                    "the runs' fields"

                Expect.equal
                    (runs |> List.map _.Value)
                    [
                        Some (TracePayload.Encoded ("point", 2, "1,0"))
                        Some (TracePayload.Encoded ("point", 2, "2,0"))
                    ]
                    "each run's value through the codec"
            }

#if !FABLE_COMPILER
            test "no contract type holds an obj, an INode or a mutable field" {
                let roots =
                    [
                        typeof<TraceGraphState>
                        typeof<TraceRunState>
                        typeof<TraceWhy>
                        typeof<TraceLookup>
                        typeof<TraceAddress>
                        typeof<TracePayload>
                    ]

                Expect.isTrue ((reachable typeof<TraceGraphState>).Contains typeof<TracePayload>) "the walk reaches nested fields"
                Expect.isTrue ((reachable typeof<WhyStep>).Contains typeof<obj>) "the walk finds an obj field"

                for t in roots |> Seq.collect reachable |> Seq.distinct do
                    Expect.notEqual t typeof<obj> ("an obj is reachable from the contract")
                    Expect.isFalse (typeof<INode>.IsAssignableFrom t) ("an INode is reachable: " + t.Name)

                    if Reflection.FSharpType.IsRecord (t, flags) then
                        for p in Reflection.FSharpType.GetRecordFields (t, flags) do
                            Expect.isFalse p.CanWrite ("a settable field: " + t.Name + "." + p.Name)
            }
#endif
#endif
        ]
