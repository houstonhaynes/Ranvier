// Runs each conformance scenario on real Signal, Memo and Effect objects, and writes the event traces as
// theories/Conformance/Expected.v. Each program in theories/Conformance/Check.v states the same scenario in the
// command language, with node ids equal to creation indexes.
//
//   dotnet build src/Ranvier -c Release
//   dotnet fsi proofs/conformance/Scenarios.fsx [output path]
//   dotnet fsi proofs/conformance/Scenarios.fsx --fatal <scenario>
#load "Harness.fsx"

open System
open System.IO
open Ranvier
open Harness

let private lessThan (a: int) (b: int) =
    if a < b then 1 else 0

let scenarios: (string * (Ctx -> unit)) list =
    [
        // Diamond: two memos over one signal, an effect reads both.
        "S01",
        fun c ->
            let s = c.Signal 1 0

            let a =
                c.Memo 2 (fun () ->
                    let v = s.Value
                    c.Log 2 v
                    v + 1)

            let b =
                c.Memo 3 (fun () ->
                    let v = s.Value
                    c.Log 3 v
                    v * 2)

            c.Effect 4 (fun () ->
                let x = a.Value
                let y = b.Value
                c.Log 4 (x + y))
            |> ignore

            s.Value <- 1
            s.Value <- 1
            s.Value <- 2

        // Cutoff: a memo's value is unchanged, and its effect does not run.
        "S02",
        fun c ->
            let s = c.Signal 1 0

            let m =
                c.Memo 2 (fun () ->
                    let v = s.Value
                    c.Log 2 v
                    lessThan v 10)

            c.Effect 3 (fun () -> c.Log 3 m.Value) |> ignore
            s.Value <- 1
            s.Value <- 20
            s.Value <- 30

        // #302, the test markedByInlineSettle at d701e6c5, with a plain memo for its async memo. The middle x reads m, a
        // memo over a, then a itself. The run of a under m marks x while x runs, in place of the inline settle. The value
        // a / 2 is unchanged by odd writes, so those runs end stale, and each even write still reaches the effect.
        "S03",
        fun c ->
            let s = c.Signal 1 0
            let h = c.Signal 2 0
            let k = c.Signal 3 0
            let a = c.Memo 4 (fun () -> s.Value)

            let m =
                c.Memo 5 (fun () ->
                    k.Value |> ignore
                    a.Value)

            let x =
                c.Memo 6 (fun () ->
                    h.Value |> ignore
                    m.Value |> ignore
                    let v = a.Value
                    c.Log 6 v
                    v / 2)

            c.Effect 7 (fun () -> c.Log 7 x.Value) |> ignore

            for i in 1..4 do
                c.Batch (fun () ->
                    h.Value <- i
                    k.Value <- i
                    s.Value <- i)

        // #290, the test lateLinkConverges at 2e8f9690. A memo invalidates itself until it reaches a fixpoint, and an
        // effect reads it for the first time. The effect is told each time the memo ends a run stale.
        "S04",
        fun c ->
            let s = c.Signal 1 0
            let wants = c.Signal 2 0

            let m =
                c.Memo 3 (fun () ->
                    let v = s.Value

                    if v < 3 then
                        s.Value <- v + 1

                    v)

            c.Effect 4 (fun () -> if wants.Value <> 0 then c.Log 4 m.Value else c.Log 4 -1)
            |> ignore

            wants.Value <- 1
            c.HostRead 1 (fun () -> s.Value) |> ignore

        // A conditional branch flips and drops an edge. A write to the old branch wakes nothing.
        "S05",
        fun c ->
            let flag = c.Signal 1 1
            let a = c.Signal 2 10
            let b = c.Signal 3 20

            c.Effect 4 (fun () -> if flag.Value <> 0 then c.Log 4 a.Value else c.Log 4 b.Value)
            |> ignore

            flag.Value <- 0
            a.Value <- 11
            b.Value <- 21

        // Reads A, B, A. The re-run tracks A only, and reads B untracked.
        "S06",
        fun c ->
            let a = c.Signal 1 0
            let b = c.Signal 2 0

            c.Effect 3 (fun () ->
                let v = a.Value

                if v = 0 then
                    let w = b.Value
                    let u = a.Value
                    c.Log 3 (v + w + u)
                else
                    let w = c.Graph.Untrack (Func<int>(fun () -> b.Value))
                    c.Log 3 (v + w))
            |> ignore

            a.Value <- 1
            b.Value <- 5
            a.Value <- 2

        // A batch writes 0, 1, 0 under an effect that reads the signal directly.
        "S07",
        fun c ->
            let s = c.Signal 1 0
            c.Effect 2 (fun () -> c.Log 2 s.Value) |> ignore

            c.Batch (fun () ->
                s.Value <- 1
                s.Value <- 0)

        // An effect's cleanup writes its own source. The cleanup queues no run.
        "S08",
        fun c ->
            let s = c.Signal 1 0

            c.Effect 2 (fun () ->
                let v = s.Value
                c.Log 2 v
                c.Cleanup 2 (fun () -> s.Value <- v + 10))
            |> ignore

            s.Value <- 1
            s.Value <- 11

        // Review Focus 1. An effect body throws in a flush. Its status holds the failure, the effect behind it runs, and
        // the next run clears the status.
        "S09a",
        fun c ->
            let s = c.Signal 1 0
            let a = c.Effect 2 (fun () -> c.Log 2 s.Value)

            let b =
                c.Effect 3 (fun () ->
                    let v = s.Value

                    if lessThan 0 v <> 0 then
                        c.Throw 7

                    c.Log 3 v)

            c.Effect 4 (fun () -> c.Log 4 s.Value) |> ignore
            s.Value <- 1
            c.Check (statusOf b = "Error") $"the status holds the failure: {statusOf b}"
            c.Check (statusOf a = "None") $"the other effect's status is clear: {statusOf a}"
            s.Value <- 0
            c.Check (statusOf b = "None") $"the next run clears the status: {statusOf b}"

        // Review Focus 1. An effect's cleanup throws during its re-run. The failure stays out of the flush: the write
        // returns, the effect's body and the effect behind it run, and Root.Errors holds the exception.
        "S09b",
        fun c ->
            let s = c.Signal 1 0
            c.Effect 2 (fun () -> c.Log 2 s.Value) |> ignore

            let f =
                c.Effect 3 (fun () ->
                    let v = s.Value
                    c.Cleanup 3 (fun () -> c.Throw 8)
                    c.Log 3 v)

            s.Value <- 1
            c.Check (c.Graph.Root.ErrorCount = 1) $"Root.Errors after the first write: {c.Graph.Root.ErrorCount}"
            c.Check (statusOf f = "None") $"a cleanup's failure leaves the status clear: {statusOf f}"
            s.Value <- 2
            c.Check (c.Graph.Root.ErrorCount = 2) $"Root.Errors after the second write: {c.Graph.Root.ErrorCount}"

        // Review Focus 3. A body disposes its own computation and keeps reading. The run's end clears its edges, and
        // later writes run nothing of it.
        "S10",
        fun c ->
            let s = c.Signal 1 0
            let self: Effect ref = ref Unchecked.defaultof<Effect>

            // The constructor runs the first body unless a batch defers it, and the body needs its own reference.
            c.Batch (fun () ->
                self.Value <-
                    c.Effect 2 (fun () ->
                        let v = s.Value
                        c.Log 2 v
                        c.Cleanup 2 (fun () -> c.Log 2 100)
                        c.Dispose 2 (fun () -> self.Value.Dispose ()) (box self.Value)
                        c.Log 2 s.Value))

            c.Effect 3 (fun () -> c.Log 3 s.Value) |> ignore
            c.Check ((internalProp s "ObserverCount" :?> int) = 1) "only the live effect observes the signal"
            s.Value <- 1
            s.Value <- 2

        // Review Focus 2. An owning memo's cleanup writes a source and reads the memo, so a nested run replaces the run
        // the discharge makes way for.
        "S11",
        fun c ->
            let s = c.Signal 1 0
            let self: Memo<int> ref = ref Unchecked.defaultof<Memo<int>>

            let m =
                c.OwningMemo 2 (fun () ->
                    let v = s.Value

                    c.Cleanup 2 (fun () ->
                        if v = 1 then
                            s.Value <- 2

                        c.Log 2 self.Value.Value)

                    v)

            self.Value <- m
            c.Effect 3 (fun () -> c.Log 3 m.Value) |> ignore
            s.Value <- 1
            s.Value <- 5

        // Owner disposal: reverse acquisition order over cleanups and children, each cleanup once, and a registration
        // during the teardown runs at once.
        "S12",
        fun c ->
            let children = ResizeArray<Effect>()

            let p =
                c.Effect 1 (fun () ->
                    c.Cleanup 1 (fun () -> c.Log 1 1)
                    children.Add (c.Effect 2 (fun () -> c.Cleanup 2 (fun () -> c.Log 2 21)))
                    c.Cleanup 1 (fun () -> c.Log 1 2)
                    children.Add (c.Effect 3 (fun () -> c.Cleanup 3 (fun () -> c.Log 3 31)))

                    c.Cleanup 1 (fun () ->
                        c.Log 1 3
                        c.Cleanup 1 (fun () -> c.Log 1 4)))

            c.Dispose 1 (fun () -> p.Dispose ()) (box p)
            c.Dispose 1 (fun () -> p.Dispose ()) (box p)
            c.Dispose 2 (fun () -> children[0].Dispose()) (box children[0])

        // An effect writes a source it reads, climbing to a fixpoint.
        "S13",
        fun c ->
            let s = c.Signal 1 0

            c.Effect 2 (fun () ->
                let v = s.Value
                c.Log 2 v

                if v < 3 then
                    s.Value <- v + 1)
            |> ignore

        // A memo writes its own source, and an effect reads it. A run ends stale with an unchanged value.
        "S14",
        fun c ->
            let s = c.Signal 1 0

            let m =
                c.Memo 2 (fun () ->
                    let v = s.Value
                    c.Log 2 v

                    if v < 3 then
                        s.Value <- v + 1

                    0)

            let e = c.Effect 3 (fun () -> c.Log 3 m.Value)
            c.Check (s.Peek = 2) $"the first flush leaves the signal at {s.Peek}"
            c.CheckQuiet [ "m", box m, "Stale"; "e", box e, "None" ]
            c.HostRead 2 (fun () -> m.Value) |> ignore
            c.HostRead 1 (fun () -> s.Value) |> ignore

        // The effect reads [h, x, y] and x reads [h, y]. A batch writes h and y's source.
        "S15",
        fun c ->
            let h = c.Signal 1 0
            let u = c.Signal 2 0

            let y =
                c.Memo 3 (fun () ->
                    let v = u.Value
                    c.Log 3 v
                    v)

            let x =
                c.Memo 4 (fun () ->
                    let w = h.Value
                    let v = y.Value
                    c.Log 4 (w + v)
                    w + v)

            c.Effect 5 (fun () ->
                let w = h.Value
                let v = x.Value
                let z = y.Value
                c.Log 5 (w + v + z))
            |> ignore

            c.Batch (fun () ->
                h.Value <- 1
                u.Value <- 1)

        // BUGS.md: an effect reads a memo over a signal, and its cleanup writes that signal. The mark stops at the effect
        // in its cleanups, and the body reads the written value.
        "in_cleanup_observer_stays_clean",
        fun c ->
            let s = c.Signal 1 0
            let m = c.Memo 2 (fun () -> s.Value)

            c.Effect 3 (fun () ->
                let v = m.Value
                c.Log 3 v
                c.Cleanup 3 (fun () -> s.Value <- 100))
            |> ignore

            s.Value <- 1
            c.HostRead 1 (fun () -> s.Value) |> ignore
            s.Value <- 2
            c.HostRead 2 (fun () -> m.Value) |> ignore

        // BUGS.md: an effect disposes itself, reads a memo over a signal, and writes that signal. The read links an edge
        // until the run's end, and the mark leaves the effect clean and unqueued.
        "disposed_running_observer_stays_clean",
        fun c ->
            let s = c.Signal 1 0
            let m = c.Memo 2 (fun () -> s.Value)
            let started = ref false

            let observers () =
                internalProp m "ObserverCount" :?> int

            // The constructor runs the first body unless a batch defers it, and the body needs its own reference.
            let self: Effect ref = ref Unchecked.defaultof<Effect>

            c.Batch (fun () ->
                self.Value <-
                    c.Effect 3 (fun () ->
                        started.Value <- true
                        c.Dispose 3 (fun () -> self.Value.Dispose ()) (box self.Value)
                        c.Check (observers () = 0) "the disposal leaves the memo unobserved"
                        let v = m.Value
                        c.Log 3 v
                        c.Check (observers () = 1) "the read after the disposal links an edge"
                        s.Value <- v + 1
                        c.Check (statusOf self.Value = "None") $"the mark leaves the effect clean: {statusOf self.Value}"
                        c.Check (not (privateField self.Value "queued" :?> bool)) "the mark leaves the effect unqueued"
                        c.Check (c.QueueLength = 0) "the mark queues nothing"))

            c.Check started.Value "the effect ran"
            c.Check (observers () = 0) "the run's end cleared the effect's edge"
            s.Value <- 50
            c.HostRead 2 (fun () -> m.Value) |> ignore

        // BUGS.md, self_marking_wake_lost. An owning memo's cleanup writes a source its reader x has read in the same run,
        // so x ends stale with an unchanged value. The effect's check resolves to clean, and it keeps the old value.
        "self_marking_cleanup",
        fun c ->
            let h = c.Signal 1 0
            let k = c.Signal 2 0
            let s = c.Signal 3 0

            let y =
                c.OwningMemo 4 (fun () ->
                    let v = k.Value
                    c.Cleanup 4 (fun () -> s.Value <- 50)
                    v * 0)

            let x =
                c.Memo 5 (fun () ->
                    h.Value |> ignore
                    let v = s.Value
                    y.Value |> ignore
                    c.Log 5 v
                    v)

            let e = c.Effect 6 (fun () -> c.Log 6 x.Value)

            c.Batch (fun () ->
                h.Value <- 1
                k.Value <- 1)

            c.Check (s.Peek = 50) $"the signal holds {s.Peek}"
            c.Check (x.Peek = 0) $"the memo holds {x.Peek}"
            c.Check (freshnessOf x = "Dirty") $"the memo is {freshnessOf x}"
            c.CheckQuiet [ "x", box x, "Stale"; "e", box e, "None" ]
            c.Check (c.HostRead 5 (fun () -> x.Value) = 50) "the memo's body answers 50"

            // BUGS.md, read_move_leaves_effect_queued. The read's move queues the effect and owes no flush.
            c.Check (statusOf e = "Stale") $"after the read the effect has status {statusOf e}"
            c.Check (freshnessOf e = "Dirty") $"after the read the effect is {freshnessOf e}"
            c.Check (privateField e "queued" :?> bool) "after the read the effect is queued"
            c.Check (c.QueueLength = 1) $"after the read the queue holds {c.QueueLength}"
            c.Check (e.Runs = 1) $"after the read the effect has run {e.Runs} times"
            c.Check (not (privateField c.Graph "flushOwed" :?> bool)) "after the read no flush is owed"
            c.Graph.Flush ()
            c.Check (e.Runs = 2) $"the explicit flush leaves {e.Runs} runs"
            c.Check (statusOf e = "None") $"the explicit flush leaves the status {statusOf e}"
            c.Check (c.QueueLength = 0) $"the explicit flush leaves {c.QueueLength} queued"

        // BUGS.md, self_marking_wake_lost. A memo writes its own source. A run ends stale with the value unchanged, and
        // the next run would change it.
        "self_marking_body",
        fun c ->
            let s = c.Signal 1 0

            let m =
                c.Memo 2 (fun () ->
                    let v = s.Value
                    c.Log 2 v

                    if v < 3 then
                        s.Value <- v + 1

                    if v < 2 then 0 else v)

            let e = c.Effect 3 (fun () -> c.Log 3 m.Value)
            c.Check (s.Peek = 2) $"the signal holds {s.Peek}"
            c.Check (m.Peek = 0) $"the memo holds {m.Peek}"
            c.Check (freshnessOf m = "Dirty") $"the memo is {freshnessOf m}"
            c.CheckQuiet [ "m", box m, "Stale"; "e", box e, "None" ]
            c.HostRead 2 (fun () -> m.Value) |> ignore

        // BUGS.md, self_marking_wake_lost. The same memo through a reading memo, whose ResolveCheck goes clean.
        "self_marking_through_memo",
        fun c ->
            let s = c.Signal 1 0

            let m =
                c.Memo 2 (fun () ->
                    let v = s.Value
                    c.Log 2 v

                    if v < 3 then
                        s.Value <- v + 1

                    if v < 2 then 0 else v)

            let r = c.Memo 3 (fun () -> m.Value)
            let e = c.Effect 4 (fun () -> c.Log 4 r.Value)
            c.Check (s.Peek = 2) $"the signal holds {s.Peek}"
            c.Check (r.Peek = 0) $"the reading memo holds {r.Peek}"
            c.Check (freshnessOf m = "Dirty") $"the memo is {freshnessOf m}"
            c.CheckQuiet [ "m", box m, "Stale"; "r", box r, "None"; "e", box e, "None" ]
            c.HostRead 3 (fun () -> r.Value) |> ignore

        // BUGS.md, nested_self_recompute_resets_cursor. A run reads a, marks itself, and reads itself. The nested run takes
        // another branch and trims a's edge. The outer run then stores the value it computed from a, clean.
        "nested_self_run",
        fun c ->
            let flip = c.Signal 1 1
            let a = c.Signal 2 10
            let b = c.Signal 3 20
            let self: Memo<int> ref = ref Unchecked.defaultof<Memo<int>>

            let m =
                c.Memo 4 (fun () ->
                    let f = flip.Value

                    if f = 1 then
                        a.Value
                    elif f = 2 then
                        let v = a.Value
                        flip.Value <- 3
                        self.Value.Value |> ignore
                        v
                    else
                        b.Value)

            self.Value <- m
            let e = c.Effect 5 (fun () -> c.Log 5 m.Value)
            flip.Value <- 2

            let observers (node: obj) =
                internalProp node "ObserverCount" :?> int

            c.Check (flip.Peek = 3) $"flip holds {flip.Peek}"
            c.Check (m.Peek = 10) $"the memo holds {m.Peek}"
            c.Check (observers a = 0) "the memo holds no edge to a"
            c.Check (observers b = 1) "the memo holds an edge to b"
            c.Check (observers m = 2) "the memo observes itself beside the effect"
            c.CheckQuiet [ "m", box m, "None"; "e", box e, "None" ]
            a.Value <- 11
            c.Check (m.Peek = 10) $"after the write to a the memo holds {m.Peek}"
            c.CheckQuiet [ "m", box m, "None"; "e", box e, "None" ]
            c.Check (c.HostRead 4 (fun () -> m.Value) = 10) "a clean read answers the stored value"

        // BUGS.md, check_resolves_over_moved_source. x reads a, writes a's source, and reads b, a memo over a. b's walk runs
        // a under x's body, and the notice of that run skips x. x ends in Check, and its next read resolves it clean.
        "check_over_moved",
        fun c ->
            let s = c.Signal 1 0
            let a = c.Memo 2 (fun () -> s.Value)
            let b = c.Memo 3 (fun () -> a.Value)

            let x =
                c.Memo 4 (fun () ->
                    let v = a.Value
                    s.Value <- 1
                    b.Value |> ignore
                    c.Log 4 v
                    v)

            c.HostRead 3 (fun () -> b.Value) |> ignore
            c.HostRead 4 (fun () -> x.Value) |> ignore
            c.Check (freshnessOf x = "Check") $"the reader is {freshnessOf x}"
            c.Check (freshnessOf a = "Clean") $"the source is {freshnessOf a}"
            c.Check (c.HostRead 4 (fun () -> x.Value) = 0) "the resolved read answers the value of the old read"
            c.Check (freshnessOf x = "Clean") $"after its read the reader is {freshnessOf x}"
            c.Check (a.Peek = 1) $"the source holds {a.Peek}"

        // BUGS.md, check_resolves_over_moved_source. The same skip through a second read of the source.
        "check_two_reads",
        fun c ->
            let s = c.Signal 1 0
            let a = c.Memo 2 (fun () -> s.Value)

            let x =
                c.Memo 3 (fun () ->
                    let v = a.Value
                    s.Value <- 1
                    a.Value |> ignore
                    c.Log 3 v
                    v)

            c.HostRead 2 (fun () -> a.Value) |> ignore
            c.HostRead 3 (fun () -> x.Value) |> ignore
            c.Check (freshnessOf x = "Check") $"the reader is {freshnessOf x}"
            c.Check (freshnessOf a = "Clean") $"the source is {freshnessOf a}"
            c.Check (c.HostRead 3 (fun () -> x.Value) = 0) "the resolved read answers the value of the first read"
            c.Check (freshnessOf x = "Clean") $"after its read the reader is {freshnessOf x}"
            c.Check (a.Peek = 1) $"the source holds {a.Peek}"

        // BUGS.md, self_marking_wake_lost. A reading memo resolves clean over a source that ended its run stale.
        "stale_source_reader",
        fun c ->
            let s = c.Signal 1 0

            let m =
                c.Memo 2 (fun () ->
                    let v = s.Value
                    c.Log 2 v

                    if v < 3 then
                        s.Value <- v + 1

                    if v < 2 then 0 else v)

            let r = c.Memo 3 (fun () -> m.Value)
            c.HostRead 3 (fun () -> r.Value) |> ignore
            c.Check (freshnessOf r = "Check") $"the reader is {freshnessOf r}"
            c.HostRead 3 (fun () -> r.Value) |> ignore
            c.Check (freshnessOf r = "Clean") $"after its read the reader is {freshnessOf r}"
            c.Check (freshnessOf m = "Dirty") $"the source is {freshnessOf m}"
            c.Check (s.Peek = 2) $"the signal holds {s.Peek}"
            c.Check (c.HostRead 3 (fun () -> r.Value) = 0) "the clean reader answers the value of its old read"
            c.Check (c.HostRead 2 (fun () -> m.Value) = 2) "the source's next run moves its value"

        // BUGS.md, disposed_memo_keeps_observers. A disposed memo answers its last value, keeps its observer, and does not
        // wake it. Core.fs 4372-4376.
        "disposed_memo_keeps_observers",
        fun c ->
            let s = c.Signal 1 0
            let m = c.Memo 2 (fun () -> s.Value)
            let e = c.Effect 3 (fun () -> c.Log 3 m.Value)
            c.Dispose 2 (fun () -> m.Dispose ()) (box m)
            s.Value <- 5
            c.Check (privateField m "disposed" :?> bool) "the memo is disposed"
            c.Check (m.Peek = 0) $"the disposed memo holds {m.Peek}"
            c.Check (s.Peek = 5) $"the signal holds {s.Peek}"
            c.Check ((internalProp m "ObserverCount" :?> int) = 1) "the effect still observes the disposed memo"
            c.Check (e.Runs = 1) $"the effect has {e.Runs} runs"
            c.Check (not (privateField e "queued" :?> bool)) "the effect is not queued"
            c.Check ((privateField e "freshness").ToString() = "Clean") "the effect is Clean"

        // BUGS.md, disposed_window_keeps_unannounced. A nested run ends stale and registers a cleanup that disposes the
        // memo. The next untracked self-read discharges it in RecomputeScoped's window, with the nested run's flag set.
        "disposed_self_window",
        fun c ->
            let s = c.Signal 1 0
            let g = c.Signal 2 0
            let m = c.Memo 3 (fun () -> s.Value)
            let self: Memo<int> ref = ref Unchecked.defaultof<Memo<int>>

            let n =
                c.OwningMemo 4 (fun () ->
                    if m.Value = 0 then
                        0
                    elif g.Value = 0 then
                        g.Value <- 1

                        c.Graph.Untrack (Func<int>(fun () -> self.Value.Value))
                        |> ignore

                        c.Graph.Untrack (Func<int>(fun () -> self.Value.Value))
                        |> ignore

                        0
                    else
                        c.Cleanup 4 (fun () -> c.Dispose 4 (fun () -> self.Value.Dispose ()) (box self.Value))
                        g.Value <- 2
                        0)

            self.Value <- n
            c.HostRead 4 (fun () -> n.Value) |> ignore
            s.Value <- 1
            c.Check (freshnessOf n = "Check") $"the memo is {freshnessOf n}"
            c.HostRead 4 (fun () -> n.Value) |> ignore
            c.Check (freshnessOf n = "Clean") $"after its read the memo is {freshnessOf n}"
            c.Check (privateField n "unannounced" :?> bool) "the memo holds its flag"
            c.Check (privateField n "disposed" :?> bool) "the memo is disposed"

        // A memo's previous value. A memo nested in a published memo starts with none, and a later run has its own.
        "previous_value",
        fun c ->
            let s = c.Signal 1 0

            let counter =
                c.PrevMemo 2 (fun prev ->
                    if s.Value = 0 then
                        c.Check prev.IsNone "the first run has no previous value"
                        1
                    else
                        c.Check prev.IsSome "a later run has its previous value"
                        prev.Value + 1)

            let outer =
                c.OwningMemo 3 (fun () ->
                    let v = s.Value

                    if v <> 0 then
                        let inner =
                            c.PrevMemo 4 (fun prev ->
                                c.Check prev.IsNone "the nested memo's first run has no previous value"
                                7)

                        c.Log 3 inner.Value
                        v
                    else
                        42)

            c.HostRead 2 (fun () -> counter.Value) |> ignore
            c.HostRead 3 (fun () -> outer.Value) |> ignore
            s.Value <- 5
            c.HostRead 2 (fun () -> counter.Value) |> ignore
            c.HostRead 3 (fun () -> outer.Value) |> ignore

        // BUGS.md, disposed_stale_source_read. A memo disposed while stale answers its last value and links its reader,
        // with no mark. Core.fs 4372-4376 documents the disposed memo: it stops being woken.
        "disposed_stale_source_read",
        fun c ->
            let s = c.Signal 1 0
            let m = c.Memo 2 (fun () -> s.Value)
            c.HostRead 2 (fun () -> m.Value) |> ignore
            s.Value <- 1
            c.Dispose 2 (fun () -> m.Dispose ()) (box m)
            let e = c.Effect 3 (fun () -> c.Log 3 m.Value)
            c.Check (freshnessOf m = "Dirty") $"the memo is {freshnessOf m}"
            c.Check ((internalProp m "ObserverCount" :?> int) = 1) "the effect observes the disposed memo"
            c.CheckQuiet [ "e", box e, "None" ]
            c.Check (c.HostRead 2 (fun () -> m.Value) = 0) "the disposed memo answers its last value"

        // BUGS.md, walk_passed_source_marked. An effect's walk passes `a`, then a later source's run writes `a`'s source.
        // The notice finds the effect in `Check`, and the walk ends with the effect clean over `a`, now `Dirty`.
        "walk_passed_source_marked",
        fun c ->
            let s = c.Signal 1 0
            let t = c.Signal 2 0
            let a = c.Memo 3 (fun () -> s.Value)

            let b =
                c.Memo 4 (fun () ->
                    s.Value <- t.Value
                    0)

            let e =
                c.Effect 5 (fun () ->
                    let x = a.Value
                    b.Value |> ignore
                    c.Log 5 x)

            t.Value <- 1
            c.Check (s.Peek = 1) $"the signal holds {s.Peek}"
            c.Check (a.Peek = 0) $"the memo holds {a.Peek}"
            c.Check (freshnessOf a = "Dirty") $"the memo is {freshnessOf a}"
            c.Check (not (privateField a "unannounced" :?> bool)) "the memo holds no announcement"
            c.Check (e.Runs = 1) $"the effect has run {e.Runs} times"
            c.CheckQuiet [ "a", box a, "Stale"; "e", box e, "None" ]
            s.Value <- 2
            c.Check (e.Runs = 1) $"after another write the effect has run {e.Runs} times"
            c.Check (c.HostRead 3 (fun () -> a.Value) = 2) "the memo's body answers 2"

        // BUGS.md, walk_passed_source_marked. The same through a cleanup.
        "walk_passed_source_cleanup",
        fun c ->
            let s = c.Signal 1 0
            let t = c.Signal 2 0
            let a = c.Memo 3 (fun () -> s.Value)

            let b =
                c.OwningMemo 4 (fun () ->
                    let v = t.Value
                    c.Cleanup 4 (fun () -> s.Value <- 1)
                    v * 0)

            let e =
                c.Effect 5 (fun () ->
                    let x = a.Value
                    b.Value |> ignore
                    c.Log 5 x)

            t.Value <- 1
            c.Check (s.Peek = 1) $"the signal holds {s.Peek}"
            c.Check (a.Peek = 0) $"the memo holds {a.Peek}"
            c.Check (freshnessOf a = "Dirty") $"the memo is {freshnessOf a}"
            c.Check (not (privateField a "unannounced" :?> bool)) "the memo holds no announcement"
            c.Check (e.Runs = 1) $"the effect has run {e.Runs} times"
            c.CheckQuiet [ "a", box a, "Stale"; "e", box e, "None" ]
            s.Value <- 2
            c.Check (e.Runs = 1) $"after another write the effect has run {e.Runs} times"
            c.Check (c.HostRead 3 (fun () -> a.Value) = 2) "the memo's body answers 2"

        // BUGS.md, effect_resolves_over_moved_source. An effect reads `a`, writes its source and reads a memo over it. The
        // walk of that read runs `a` with the effect as the current computation, and NotifyDirtyExcept skips the effect.
        "effect_resolves_over_moved_source",
        fun c ->
            let s = c.Signal 1 0
            let a = c.Memo 2 (fun () -> s.Value)
            let b = c.Memo 3 (fun () -> a.Value)
            c.HostRead 3 (fun () -> b.Value) |> ignore

            let e =
                c.Effect 4 (fun () ->
                    let v = a.Value
                    s.Value <- 1
                    b.Value |> ignore
                    c.Log 4 v)

            c.Check (a.Peek = 1) $"the source holds {a.Peek}"
            c.Check (freshnessOf a = "Clean") $"the source is {freshnessOf a}"
            c.Check (e.Runs = 1) $"the effect has run {e.Runs} times"
            c.CheckQuiet [ "a", box a, "None"; "e", box e, "None" ]
            s.Value <- 2
            c.Check (e.Runs = 3) $"the next write leaves {e.Runs} runs"

        // A teardown in reverse acquisition order: the later cleanup, the child's cleanup, the earlier cleanup.
        "teardown_order",
        fun c ->
            let s = c.Signal 1 0

            let e =
                c.Effect 2 (fun () ->
                    s.Value |> ignore
                    c.Cleanup 2 (fun () -> c.Log 2 1)

                    c.Effect 3 (fun () -> c.Cleanup 3 (fun () -> c.Log 3 2))
                    |> ignore

                    c.Cleanup 2 (fun () -> c.Log 2 3))

            c.Dispose 2 (fun () -> e.Dispose ()) (box e)

        // A cleanup registered on a disposed scope runs before the body goes on, and a child created there never runs.
        "late_registrations",
        fun c ->
            let self: Effect ref = ref Unchecked.defaultof<Effect>
            let child: Effect ref = ref Unchecked.defaultof<Effect>

            c.Batch (fun () ->
                self.Value <-
                    c.Effect 1 (fun () ->
                        c.Dispose 1 (fun () -> self.Value.Dispose ()) (box self.Value)
                        c.Cleanup 1 (fun () -> c.Log 1 100)
                        child.Value <- c.Effect 2 (fun () -> c.Log 2 5)
                        c.Log 1 7))

            c.Check ((privateField child.Value "disposed" :?> bool)) "the late child is disposed"
            c.Check (child.Value.Runs = 0) $"the late child has {child.Value.Runs} runs"

        // A child's release fails through its cleanup, during its parent's re-run. The effect behind it runs.
        "flush_child_release",
        fun c ->
            let s = c.Signal 1 0
            c.Effect 2 (fun () -> c.Log 2 s.Value) |> ignore
            let next = ref 4

            c.Effect 3 (fun () ->
                s.Value |> ignore
                let label = next.Value
                next.Value <- label + 1

                c.Effect label (fun () -> c.Cleanup label (fun () -> c.Throw 8))
                |> ignore)
            |> ignore

            s.Value <- 1
            c.Check (c.Graph.Root.ErrorCount = 1) $"Root.Errors after the write: {c.Graph.Root.ErrorCount}"
            c.Check (c.QueueLength = 0) $"the queue holds {c.QueueLength}"

        // A memo's run fails under its reader's run. The effect behind the reader runs.
        "flush_memo_failure",
        fun c ->
            let s = c.Signal 1 0

            let m =
                c.Memo 2 (fun () ->
                    let v = s.Value

                    if v <> 0 then c.Throw 9 else v)

            c.Effect 3 (fun () -> c.Log 3 s.Value) |> ignore
            let e = c.Effect 4 (fun () -> c.Log 4 m.Value)
            s.Value <- 1
            c.Check (statusOf e = "Error") $"the reader holds the failure: {statusOf e}"
            c.Check (c.QueueLength = 0) $"the queue holds {c.QueueLength}"

        // An effect ahead in the queue disposes the effect behind it. The entry is dequeued with no run.
        "queued_effect_disposed",
        fun c ->
            let s = c.Signal 1 0
            let behind = c.Effect 2 (fun () -> c.Log 2 s.Value)

            c.Effect 3 (fun () ->
                if s.Value <> 0 then
                    c.Dispose 2 (fun () -> behind.Dispose ()) (box behind))
            |> ignore

            s.Value <- 1
            c.Check (behind.Runs = 1) $"the disposed effect has {behind.Runs} runs"
            c.Check (not (privateField behind "queued" :?> bool)) "the disposed effect is still queued"
            c.Check (c.QueueLength = 0) $"the queue holds {c.QueueLength}"

        // A cleanup registered on a disposed scope fails. The failure is recorded, and the body goes on.
        "late_cleanup_failure",
        fun c ->
            let self: Effect ref = ref Unchecked.defaultof<Effect>

            c.Batch (fun () ->
                self.Value <-
                    c.Effect 1 (fun () ->
                        c.Dispose 1 (fun () -> self.Value.Dispose ()) (box self.Value)
                        c.Cleanup 1 (fun () -> c.Throw 8)
                        c.Log 1 7))

            c.Check (c.Graph.Root.ErrorCount = 1) $"Root.Errors: {c.Graph.Root.ErrorCount}"

        // A pure memo's body breaches its scope. The core raises InvalidOperationException, and the trace reports it as
        // pure_scope_failure, -2. A second read answers from the stored failure, with no second run.
        "memo_scope_violation",
        fun c ->
            c.Signal 1 0 |> ignore

            let m =
                c.Labelled
                    2
                    (Memo<int>(
                        c.Graph,
                        Func<int voption, int>(fun _ ->
                            c.Graph.OnCleanup (Action ignore)
                            0)
                    ))

            let reading () =
                try
                    m.Value |> ignore
                    None
                with :? InvalidOperationException as ex ->
                    Some ex

            let first = reading ()
            c.Check first.IsSome "the read answers InvalidOperationException"
            c.Emit (HostRead (2, FailV -2))
            let second = reading ()
            c.Check (obj.ReferenceEquals (first.Value, second.Value)) "the second read answers another exception"
            c.Check (m.Runs = 1) $"the memo has {m.Runs} runs"

        // A memo fails with 7 on each run. Its throwing comparer is consulted on the second, over the unchanged failure.
        "memo_comparer_failure",
        fun c ->
            let s = c.Signal 1 0

            let m =
                c.MemoThrowingComparer 2 9 (fun () ->
                    s.Value |> ignore
                    c.Throw 7)

            let tryRead () =
                try
                    c.HostRead 2 (fun () -> m.Value) |> ignore
                with Boom _ ->
                    ()

            tryRead ()
            s.Value <- 1
            tryRead ()

        // A throwing comparer fails a memo's reader during a flush. The effect behind the reader runs.
        "flush_comparer",
        fun c ->
            let s = c.Signal 1 0

            let m =
                c.MemoThrowingComparer 2 9 (fun () ->
                    s.Value |> ignore
                    0)

            c.Effect 3 (fun () -> c.Log 3 s.Value) |> ignore
            let e = c.Effect 4 (fun () -> c.Log 4 m.Value)
            s.Value <- 1
            c.Check (statusOf e = "Error") $"the reader holds the failure: {statusOf e}"
            c.Check (c.QueueLength = 0) $"the queue holds {c.QueueLength}"

        // BUGS.md, cmp_never_passes_source. No body writes. The walk of the effect passes `a`, and the runs of the memos
        // behind `b` count as moves and mark `a` `Dirty`. The effect ends clean over `a`, with no announcement on `a`.
        "cmp_never_passes_source",
        fun c ->
            let s = c.Signal 1 0

            let m2 = c.Memo 2 (fun () -> s.Value)

            let m3 =
                c.MemoNeverEqual 3 (fun () ->
                    m2.Value |> ignore
                    0)

            let m4 =
                c.MemoNeverEqual 4 (fun () ->
                    if s.Value <> 0 then
                        m3.Value |> ignore
                    else
                        m2.Value |> ignore

                    0)

            let a =
                c.Memo 5 (fun () ->
                    let v = s.Value
                    m4.Value |> ignore
                    if v < 100 then 1 else 0)

            let m6 =
                c.Memo 6 (fun () ->
                    m4.Value |> ignore
                    0)

            let m7 =
                c.Memo 7 (fun () ->
                    m6.Value |> ignore
                    0)

            let b =
                c.Memo 8 (fun () ->
                    m7.Value |> ignore
                    0)

            let e =
                c.Effect 9 (fun () ->
                    let x = a.Value
                    b.Value |> ignore
                    c.Log 9 x)

            s.Value <- 2
            c.Check (a.Peek = 1) $"the memo holds {a.Peek}"
            c.Check (freshnessOf a = "Dirty") $"the memo is {freshnessOf a}"
            c.Check (not (privateField a "unannounced" :?> bool)) "the memo holds no announcement"
            c.Check (e.Runs = 1) $"the effect has run {e.Runs} times"
            c.CheckQuiet [ "a", box a, "Stale"; "e", box e, "None" ]
            c.Check (c.HostRead 5 (fun () -> a.Value) = 1) "the memo's body answers 1"

        // A batch that fails leaves `a` stale and a writing effect queued. A read of `a` runs it, its batch owes a flush, and
        // the flush runs the effect before the read answers.
        "read_settles_writer",
        fun c ->
            let s = c.Signal 1 0
            let t = c.Signal 2 0

            let a =
                c.Memo 3 (fun () ->
                    c.Batch (fun () -> ())
                    s.Value)

            c.Effect 4 (fun () ->
                let v = t.Value
                let x = a.Value

                if v <> 0 then
                    s.Value <- 9

                c.Log 4 x)
            |> ignore

            try
                c.Batch (fun () ->
                    s.Value <- 1
                    t.Value <- 1
                    c.Throw 5)
            with Boom _ ->
                ()

            c.Check (a.Peek = 0) $"before the read the memo holds {a.Peek}"
            c.Check (c.HostRead 3 (fun () -> a.Value) = 9) "the read answers past the flush"

        // No batch failed and the queue is empty. The walk passes `a`, as in `walk_passed_source_marked`, so the effect
        // sleeps on it. A read of `n` moves `a`, which queues the effect, `n`'s batch owes a flush, and the flush runs
        // the effect before the read answers.
        "quiescent_read_answers_past_flush",
        fun c ->
            let s = c.Signal 1 0
            let t = c.Signal 2 0
            let a = c.Memo 3 (fun () -> s.Value)

            let b =
                c.Memo 4 (fun () ->
                    s.Value <- t.Value
                    0)

            let n =
                c.Memo 5 (fun () ->
                    let v = a.Value
                    c.Batch (fun () -> ())
                    v)

            let e =
                c.Effect 6 (fun () ->
                    let x = a.Value
                    let y = n.Value
                    b.Value |> ignore

                    if x <> 0 then
                        s.Value <- 9

                    c.Log 6 y)

            t.Value <- 1
            c.CheckQuiet [ "a", box a, "Stale"; "e", box e, "None" ]
            c.Check (a.Peek = 0) $"before the read the memo holds {a.Peek}"
            c.Check (s.Peek = 1) $"before the read the signal holds {s.Peek}"
            c.Check (c.HostRead 5 (fun () -> n.Value) = 9) "the read answers past the flush"
            c.Check (s.Peek = 9) $"after the read the signal holds {s.Peek}"

        // The same with no memo body writing: `b`'s cleanup writes.
        "quiescent_read_answers_past_flush_by_cleanup",
        fun c ->
            let s = c.Signal 1 0
            let t = c.Signal 2 0
            let a = c.Memo 3 (fun () -> s.Value)

            let b =
                c.OwningMemo 4 (fun () ->
                    let v = t.Value
                    c.Cleanup 4 (fun () -> s.Value <- 1)
                    v * 0)

            let n =
                c.Memo 5 (fun () ->
                    let v = a.Value
                    c.Batch (fun () -> ())
                    v)

            let e =
                c.Effect 6 (fun () ->
                    let x = a.Value
                    let y = n.Value
                    b.Value |> ignore

                    if x <> 0 then
                        s.Value <- 9

                    c.Log 6 y)

            t.Value <- 1
            c.CheckQuiet [ "a", box a, "Stale"; "e", box e, "None" ]
            c.Check (a.Peek = 0) $"before the read the memo holds {a.Peek}"
            c.Check (s.Peek = 1) $"before the read the signal holds {s.Peek}"
            c.Check (c.HostRead 5 (fun () -> n.Value) = 9) "the read answers past the flush"
            c.Check (s.Peek = 9) $"after the read the signal holds {s.Peek}"

        // BUGS.md, cmp_never_passes_source. `a` is passed and left `Dirty` with no announcement, so writes of a signal only
        // `a` reads stop there. The effect stays clean on 0, and a flush changes nothing, until a host read of `a`.
        "cmp_never_lost_update",
        fun c ->
            let s = c.Signal 1 0
            let t = c.Signal 2 0
            let m3 = c.Memo 3 (fun () -> s.Value)

            let m4 =
                c.MemoNeverEqual 4 (fun () ->
                    m3.Value |> ignore
                    0)

            let m5 =
                c.MemoNeverEqual 5 (fun () ->
                    if s.Value <> 0 then
                        m4.Value |> ignore
                    else
                        m3.Value |> ignore

                    0)

            let a =
                c.Memo 6 (fun () ->
                    s.Value |> ignore
                    m5.Value |> ignore
                    t.Value)

            let m7 =
                c.Memo 7 (fun () ->
                    m5.Value |> ignore
                    0)

            let m8 =
                c.Memo 8 (fun () ->
                    m7.Value |> ignore
                    0)

            let b =
                c.Memo 9 (fun () ->
                    m8.Value |> ignore
                    0)

            let e =
                c.Effect 10 (fun () ->
                    let x = a.Value
                    b.Value |> ignore
                    c.Log 10 x)

            s.Value <- 2
            t.Value <- 7
            c.Check (a.Peek = 0) $"the memo holds {a.Peek}"
            c.CheckQuiet [ "a", box a, "Stale"; "e", box e, "None" ]
            t.Value <- 8
            c.Check (e.Runs = 1) $"the effect has run {e.Runs} times"
            c.CheckQuiet [ "a", box a, "Stale"; "e", box e, "None" ]
            c.Check (c.HostRead 6 (fun () -> a.Value) = 8) "the memo's body answers 8"
            c.Graph.Flush ()
            c.Check (e.Runs = 2) $"after the read and a flush the effect has run {e.Runs} times"

        // BUGS.md, cmp_never_passes_source. The same with default options and no comparer: memos returning a new record.
        "fresh_record_lost_update",
        fun c ->
            let s = c.Signal 1 0
            let t = c.Signal 2 0
            let m3 = c.Memo 3 (fun () -> s.Value)

            let m4 =
                c.FreshRecordMemo 4 (fun () ->
                    m3.Value |> ignore
                    0)

            let m5 =
                c.FreshRecordMemo 5 (fun () ->
                    if s.Value <> 0 then
                        m4.Value |> ignore
                    else
                        m3.Value |> ignore

                    0)

            let a =
                c.Memo 6 (fun () ->
                    s.Value |> ignore
                    m5.Value |> ignore
                    t.Value)

            let m7 =
                c.Memo 7 (fun () ->
                    m5.Value |> ignore
                    0)

            let m8 =
                c.Memo 8 (fun () ->
                    m7.Value |> ignore
                    0)

            let b =
                c.Memo 9 (fun () ->
                    m8.Value |> ignore
                    0)

            let e =
                c.Effect 10 (fun () ->
                    let x = a.Value
                    b.Value |> ignore
                    c.Log 10 x)

            s.Value <- 2
            t.Value <- 7
            c.Check (a.Peek = 0) $"the memo holds {a.Peek}"
            c.CheckQuiet [ "a", box a, "Stale"; "e", box e, "None" ]
            t.Value <- 8
            c.Check (e.Runs = 1) $"the effect has run {e.Runs} times"
            c.CheckQuiet [ "a", box a, "Stale"; "e", box e, "None" ]
            c.Check (c.HostRead 6 (fun () -> a.Value) = 8) "the memo's body answers 8"
            c.Graph.Flush ()
            c.Check (e.Runs = 2) $"after the read and a flush the effect has run {e.Runs} times"

        // A batch whose body fails applies its write and requests no flush. The effect stays queued, nothing is owed,
        // and the next flush runs it.
        "failed_batch",
        fun c ->
            let s = c.Signal 1 0
            let e = c.Effect 2 (fun () -> c.Log 2 s.Value)

            try
                c.Batch (fun () ->
                    s.Value <- 1
                    c.Throw 5)
            with Boom 5 ->
                ()

            c.Check (c.QueueLength = 1) $"the queue holds {c.QueueLength}"
            c.Check (not (privateField c.Graph "flushOwed" :?> bool)) "a flush is owed"
            c.Check (e.Runs = 1) $"the effect has {e.Runs} runs"
            c.Check ((privateField e "freshness").ToString() = "Dirty") "the effect is not Dirty"
            c.Graph.Flush ()
            c.Check (e.Runs = 2) $"the flush leaves {e.Runs} runs"

        // BUGS.md, disposed_unread_memo_value. Nothing has read the memo when it is disposed, and a later read answers the
        // type's default.
        "disposed_unread_memo_value",
        fun c ->
            let s = c.Signal 1 0
            let m = c.Memo 2 (fun () -> s.Value)
            let g = c.Signal 3 0

            c.Effect 4 (fun () ->
                if g.Value <> 0 then
                    c.Log 4 m.Value)
            |> ignore

            s.Value <- 5
            c.Dispose 2 (fun () -> m.Dispose ()) (box m)
            g.Value <- 1
            c.Check (m.Peek = 0) $"the disposed memo holds {m.Peek}"

        // BUGS.md, write_then_dispose_memo_current_value. A batch writes the memo's source and disposes it unread. The memo
        // keeps its value over the old signal, and its reader does not run.
        "write_then_dispose_memo_current_value",
        fun c ->
            let s = c.Signal 1 0
            let m = c.Memo 2 (fun () -> s.Value + 1)
            let g = c.Signal 3 0

            let e =
                c.Effect 4 (fun () ->
                    let x = m.Value
                    g.Value |> ignore
                    c.Log 4 x)

            c.Batch (fun () ->
                s.Value <- 5
                c.Dispose 2 (fun () -> m.Dispose ()) (box m))

            c.Check (m.Peek = 1) $"the disposed memo holds {m.Peek}"
            c.Check (e.Runs = 1) $"the effect has {e.Runs} runs"
            g.Value <- 1

        // BUGS.md, redundant_run_reads_untracked. A restored signal reruns the effect, whose untracked read has moved. The
        // tracked read did not move, so the reference does not run it.
        "redundant_run_reads_untracked",
        fun c ->
            let s = c.Signal 1 0
            let u = c.Signal 2 0

            c.Effect 3 (fun () ->
                s.Value |> ignore
                c.Log 3 u.Peek)
            |> ignore

            c.Batch (fun () ->
                u.Value <- 5
                s.Value <- 1
                s.Value <- 0)

        // BUGS.md, effect_restored_source_loop. An effect reads a source and restores it after a write, which marks the
        // effect. It runs again on the value it read. A budget read untracked ends the runs here. With no budget, as in
        // effect_restored_source_loop, they do not end.
        "effect_restored_source_reruns",
        fun c ->
            let x = c.Signal 1 0
            let b = c.Signal 2 3

            let e =
                c.Effect 3 (fun () ->
                    c.Log 3 x.Value
                    let left = b.Peek

                    if left <> 0 then
                        b.Value <- left - 1
                        x.Value <- 1
                        x.Value <- 0)

            c.Check (e.Runs = 4) $"the effect has {e.Runs} runs"

        // BUGS.md, requeue_runs_after_later_entry. S15's diamond, with a second effect over `h` created first. The walk
        // queues the diamond's effect first. Under its run `y`'s run marks it again, so it runs before and after the other.
        "requeue_runs_after_later_entry",
        fun c ->
            let h = c.Signal 1 0
            let u = c.Signal 2 0
            let y = c.Memo 3 (fun () -> u.Value)

            let x =
                c.Memo 4 (fun () ->
                    let w = h.Value
                    w + y.Value)

            c.Effect 5 (fun () -> c.Log 5 h.Value) |> ignore

            c.Effect 6 (fun () ->
                let w = h.Value
                let v = x.Value
                c.Log 6 (w + v + y.Value))
            |> ignore

            c.Batch (fun () ->
                h.Value <- 1
                u.Value <- 2)

        // Pure/Optimised.v, leave_dirty_cleanup_skip. An effect's cleanup writes the signal the effect reads, and the
        // host writes it twice.
        "S16_cleanup_rewrite",
        fun c ->
            let s = c.Signal 1 0

            c.Effect 2 (fun () ->
                c.Cleanup 2 (fun () -> s.Value <- 100)
                c.Log 2 s.Value)
            |> ignore

            s.Value <- 1
            s.Value <- 2

        // Pure/Optimised.v, leave_dirty_unannounced_skip. A memo writes its own source while it is below 1, and a second
        // memo reads it. The host reads the second memo between writes.
        "S17_self_write_announce",
        fun c ->
            let s = c.Signal 1 0

            let m =
                c.Memo 2 (fun () ->
                    let v = s.Value

                    if v < 1 then
                        s.Value <- v + 1
                        0
                    else
                        v / 10)

            let n = c.Memo 3 (fun () -> m.Value)
            c.HostRead 3 (fun () -> n.Value) |> ignore
            c.HostRead 3 (fun () -> n.Value) |> ignore
            s.Value <- 0
            c.HostRead 3 (fun () -> n.Value) |> ignore
            s.Value <- 20
            c.HostRead 3 (fun () -> n.Value) |> ignore

        // Pure/Optimised.v, cleanup_registered_in_teardown_runs_late. A cleanup registers a cleanup with the scope it is
        // torn down from. The second cleanup outlives the teardown (Core.fs 1130-1132) and runs at the next one.
        "S18_cleanup_in_teardown",
        fun c ->
            let s = c.Signal 1 0

            c.Effect 2 (fun () ->
                c.Cleanup 2 (fun () -> c.Cleanup 2 (fun () -> c.Log 2 7))
                c.Log 2 s.Value)
            |> ignore

            s.Value <- 1
            s.Value <- 2
    ]

/// Scenarios that end the process, run one to a process by `--fatal`. A `Check` failure exits with 2 before the fatal step,
/// and returning from the fatal step exits with 3.
let fatalScenarios: (string * (Ctx -> unit)) list =
    [
        // BUGS.md, self_read_check_overflow. A memo reads itself, then a memo over a signal. The write leaves it in `Check`
        // with a slot naming itself, and the next read's ResolveCheck walks into itself with no body running.
        "self_read_check_overflow",
        fun c ->
            let s = c.Signal 1 0
            let a = c.Memo 2 (fun () -> s.Value)
            let self: Memo<int> ref = ref Unchecked.defaultof<Memo<int>>

            let m =
                c.Memo 3 (fun () ->
                    self.Value.Value |> ignore
                    a.Value)

            self.Value <- m
            c.HostRead 3 (fun () -> m.Value) |> ignore
            c.HostRead 3 (fun () -> m.Value) |> ignore
            c.Check (freshnessOf m = "Clean") $"two reads leave the memo {freshnessOf m}"
            s.Value <- 1
            c.Check (freshnessOf m = "Check") $"the write leaves the memo {freshnessOf m}"
            c.Check ((internalProp m "ObserverCount" :?> int) = 1) "the memo observes itself"

            if not c.Failures.IsEmpty then
                eprintfn "%s" (String.concat "; " c.Failures)
                exit 2

            m.Value |> ignore

        // The same walk through a cycle of two memos. `mutual_read_check_resolves_itself` in Witnesses.v is the model's
        // witness, whose program refers forward by constant id.
        "mutual_read_check_overflow",
        fun c ->
            let s = c.Signal 1 0
            let a = c.Memo 2 (fun () -> s.Value)
            let other: Memo<int> ref = ref Unchecked.defaultof<Memo<int>>

            let x =
                c.Memo 3 (fun () ->
                    if not (isNull (box other.Value)) then
                        other.Value.Value |> ignore

                    a.Value)

            let y = c.Memo 4 (fun () -> x.Value)
            other.Value <- y

            for _ in 1..3 do
                x.Value |> ignore
                y.Value |> ignore

            c.Check (freshnessOf x = "Clean") $"the reads leave the first memo {freshnessOf x}"
            c.Check (freshnessOf y = "Clean") $"the reads leave the second memo {freshnessOf y}"
            s.Value <- 1
            c.Check (freshnessOf x = "Check") $"the write leaves the first memo {freshnessOf x}"
            c.Check (freshnessOf y = "Check") $"the write leaves the second memo {freshnessOf y}"

            if not c.Failures.IsEmpty then
                eprintfn "%s" (String.concat "; " c.Failures)
                exit 2

            x.Value |> ignore
    ]

match fsi.CommandLineArgs |> Array.toList with
| [ _; "--fatal"; name ] ->
    let _, run =
        fatalScenarios
        |> List.find (fun (n, _) -> n = name)

    run (Ctx ())
    eprintfn "%s returned" name
    exit 3
| _ -> ()

let private z (v: int) =
    if v < 0 then $"(({v})%%Z)" else $"{v}%%Z"

let private renderOutcome =
    function
    | OkV v -> $"(Ok {z v})"
    | FailV f -> $"(Fail {z f})"

let private render (e: Ev) =
    match e with
    | Run n -> $"EvRun {n}"
    | Log (n, v) -> $"EvLog {n} {z v}"
    | Cleanup n -> $"EvCleanup {n}"
    | FailE (n, f) -> $"EvFail {n} {z f}"
    | Disposed n -> $"EvDispose {n}"
    | HostRead (n, o) -> $"EvHostRead {n} {renderOutcome o}"

let private definition (name: string, run: Ctx -> unit) =
    let c = Ctx ()
    run c

    if not c.Failures.IsEmpty then
        failwith (name + ": " + String.concat "; " c.Failures)

    let lines =
        c.Events
        |> List.map render
        |> List.chunkBySize 6
        |> List.map (String.concat "; ")
        |> String.concat ";\n  "

    $"Definition exp_{name} : list event := [\n  {lines}\n].\n"

let private body =
    scenarios
    |> List.map definition
    |> String.concat "\n"

let private text =
    "(** Generated by proofs/conformance/Scenarios.fsx from runs of the F# core. *)\n"
    + "From stdpp Require Import prelude.\n"
    + "From Stdlib Require Import ZArith.\n"
    + "From Ranvier.Base Require Import Prelude Syntax.\n\nOpen Scope nat_scope.\n\n"
    + body

let private target =
    match fsi.CommandLineArgs |> Array.toList with
    | _ :: path :: _ -> path
    | _ -> Path.Combine (__SOURCE_DIRECTORY__, "..", "theories", "Conformance", "Expected.v")

Directory.CreateDirectory (Path.GetDirectoryName (Path.GetFullPath target))
|> ignore

File.WriteAllText (target, text.Replace ("\r\n", "\n"))
printfn "Wrote %d scenarios to %s" scenarios.Length target
