module Ranvier.Tests.Bindings

open Expecto
open Ranvier
open Ranvier.Tests.Support

let private record (log: ResizeArray<'T>) =
    fun (_: obj) (value: 'T) -> log.Add value

[<Tests>]
let tests =
    testList
        "Bindings"
        [
            test "a signal binding writes the current value on creation" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let log = ResizeArray ()

                Binding<obj, int>.OfSignal(g, s, null, record log)
                |> ignore

                Expect.sequenceEqual log [ 1 ] "initial write"
            }

            test "a signal write writes again" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let log = ResizeArray ()

                Binding<obj, int>.OfSignal(g, s, null, record log)
                |> ignore

                s.Value <- 2
                Expect.sequenceEqual log [ 1; 2 ] "one write per change"
            }

            test "writes in one batch write once with the final value" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let log = ResizeArray ()

                Binding<obj, int>.OfSignal(g, s, null, record log)
                |> ignore

                g.Batch (fun () ->
                    s.Value <- 2
                    s.Value <- 3)

                Expect.sequenceEqual log [ 1; 3 ] "batched"
            }

            test "a memo binding writes when the memo moves" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let m = Make.Memo (g, fun _ -> s.Value * 10)
                let log = ResizeArray ()

                Binding<obj, int>.OfMemo(g, m, null, record log)
                |> ignore

                s.Value <- 2
                Expect.sequenceEqual log [ 10; 20 ] "memo values"
            }

            test "a memo recomputing to an equal value does not write" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let m = Make.Memo (g, fun _ -> s.Value % 2)
                let log = ResizeArray ()
                let b = Binding<obj, int>.OfMemo(g, m, null, record log)
                s.Value <- 3
                Expect.sequenceEqual log [ 1 ] "no second write"
                Expect.equal b.Runs 1 "resolved Clean without running"
            }

            test "a pending memo leaves the target and writes once settled" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)
                let m = Make.Memo (g, fun _ -> a.Value + 1)
                let log = ResizeArray ()
                let b = Binding<obj, int>.OfMemo(g, m, null, record log)
                Expect.isEmpty log "no write while pending"
                Expect.equal b.Status Status.Pending "pending"
                a.Settle 4
                Expect.sequenceEqual log [ 5 ] "written after settle"
                Expect.equal b.Status Status.None "settled"
            }

            test "a failed memo leaves the target and recovers" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let m = Make.Memo (g, fun _ -> if s.Value = 2 then failwith "boom" else s.Value)
                let log = ResizeArray ()
                let b = Binding<obj, int>.OfMemo(g, m, null, record log)
                s.Value <- 2
                Expect.equal b.Status Status.Error "failed"
                s.Value <- 3
                Expect.sequenceEqual log [ 1; 3 ] "the failure wrote nothing"
                Expect.equal b.Status Status.None "recovered"
            }

            test "a throwing write is recorded and does not strand other work" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let log = ResizeArray ()

                let b =
                    Binding<obj, int>.OfSignal(g, s, null, fun _ v -> if v = 2 then failwith "write" else log.Add v)

                let e = new Effect (g, fun () -> log.Add (s.Value * 100))
                s.Value <- 2
                Expect.equal b.Status Status.Error "recorded"
                Expect.equal b.Error.Message "write" "the write's exception"
                Expect.contains log 200 "the effect still ran"
                s.Value <- 3
                Expect.contains log 3 "writes again"
                e.Dispose ()
            }

            test "a binding is disposed with its owner" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let log = ResizeArray ()

                let owner =
                    g.CreateRoot (fun owner ->
                        Binding<obj, int>.OfSignal(g, s, null, record log)
                        |> ignore

                        owner)

                owner.Dispose ()
                s.Value <- 2
                Expect.sequenceEqual log [ 1 ] "no write after disposal"
            }

            test "disposing a binding stops it" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let log = ResizeArray ()
                let b = Binding<obj, int>.OfSignal(g, s, null, record log)
                b.Dispose ()
                b.Dispose ()
                s.Value <- 2
                Expect.sequenceEqual log [ 1 ] "stopped"
            }

            test "a binding disposed while queued does not write" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let log = ResizeArray ()
                let b = Binding<obj, int>.OfSignal(g, s, null, record log)

                g.Batch (fun () ->
                    s.Value <- 2
                    b.Dispose ())

                Expect.sequenceEqual log [ 1 ] "no write after disposal"
            }

            test "a binding under a disposed owner never writes" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = Signal (g, 1)
                let log = ResizeArray ()
                let owner = g.CreateRoot (fun owner -> owner)
                owner.Dispose ()

                runWithOwner owner (fun () ->
                    Binding<obj, int>.OfSignal(g, s, null, record log)
                    |> ignore)

                s.Value <- 2
                Expect.isEmpty log "never written"
                Expect.equal s.ObserverCount 0 "no observer"
#if RANVIER_TRACE
                let snapshot = Trace.snapshot g
                let sid = (s :> INode).Id
                Expect.isFalse (snapshot.Observers.ContainsKey sid) "no traced observer edge on the source"

                Expect.isTrue
                    (snapshot.Sources
                     |> Map.forall (fun _ sources -> not (List.contains sid sources)))
                    "no traced source edge"

                Expect.isEmpty (Trace.reconcile g) "the traced edges fold to the live lists"
#endif
            }

            test "a binding created in a pure memo body never writes and leaves no observer" {
                use g = new Graph ()
                use _ = g.Activate ()
                let trigger = createSignal 0
                let s = createSignal 1
                let log = ResizeArray<int>()

                let m =
                    createMemo (fun _ ->
                        trigger.Value |> ignore
                        bindSignal s log (fun t v -> t.Add v)
                        0)

                Expect.throwsT<System.InvalidOperationException> (fun () -> m.Value |> ignore) "the memo fails"
                trigger.Value <- 1

                Expect.throwsT<System.InvalidOperationException> (fun () -> m.Value |> ignore) "the recompute fails"

                s.Value <- 2
                Expect.isEmpty log "never written"
                Expect.equal s.ObserverCount 0 "no observer left"
            }

            test "a binding created in createEffectOn's compute never writes and leaves no observer" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let log = ResizeArray<int>()

                createEffectOn
                    (fun () ->
                        bindSignal s log (fun t v -> t.Add v)
                        0)
                    ignore

                s.Value <- 2
                Expect.isEmpty log "never written"
                Expect.equal s.ObserverCount 0 "no observer left"
            }

            test "a binding created in an effect body is disposed when the effect re-runs" {
                let g = new Graph ()
                let trigger = Signal (g, 0)
                let s = Signal (g, 1)
                let log = ResizeArray ()
                let mutable effectRuns = 0

                let e =
                    new Effect (
                        g,
                        fun () ->
                            effectRuns <- effectRuns + 1
                            trigger.Value |> ignore

                            Binding<obj, int>.OfSignal(g, s, null, record log)
                            |> ignore
                    )

                s.Value <- 2
                Expect.equal effectRuns 1 "the write is not a dependency of the effect"
                trigger.Value <- 1
                log.Clear ()
                s.Value <- 3
                Expect.sequenceEqual log [ 3 ] "only the binding from the latest run writes"
                e.Dispose ()
            }

            test "the target is passed to the write" {
                let g = new Graph ()
                let s = Signal (g, "a")
                let target = ResizeArray<string>()

                Binding<ResizeArray<string>, string>.OfSignal(g, s, target, fun t v -> t.Add v)
                |> ignore

                s.Value <- "b"
                Expect.sequenceEqual target [ "a"; "b" ] "target received the writes"
            }

            test "bindSignal and bindMemo bind under the current graph and owner" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let m = createMemo (fun _ -> s.Value * 2)
                let log = ResizeArray<int>()

                let owner =
                    createRoot (fun owner ->
                        bindSignal s log (fun t v -> t.Add v)
                        bindMemo m log (fun t v -> t.Add v)
                        owner)

                s.Value <- 2
                owner.Dispose ()
                s.Value <- 3
                Expect.sequenceEqual (Seq.sort log) [ 1; 2; 2; 4 ] "both bound, both disposed with the owner"
            }
        ]
