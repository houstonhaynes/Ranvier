module Ranvier.Tests.Observers

open Expecto
open Ranvier

/// <summary>
/// A source's observer list is an array with swap-remove. A wide list has failure
/// modes a narrow test never reaches: a swap that misplaces an entry silently
/// stops waking an observer, and a missed dedupe silently appends the same
/// observer on every run until the array is the size of the run count.
/// </summary>
/// <remarks>
/// So every case here is run at fan-outs on both sides of 8.
/// </remarks>
let private widths = [ 3; 8; 9; 20 ]

[<Tests>]
let tests =
    testList
        "Observers"
        [
            for width in widths do
                test $"every one of {width} observers is woken" {
                    let g = new Graph ()
                    let s = Signal (g, 0)
                    let seen = ResizeArray ()

                    for i in 1..width do
                        new Effect (g, (fun () -> seen.Add (i, s.Value)))
                        |> ignore

                    seen.Clear ()
                    s.Value <- 1

                    Expect.equal seen.Count width "one run each"

                    Expect.sequenceEqual (seen |> Seq.map fst |> Seq.sort) (Seq.sort [ 1..width ]) "and no observer was skipped"
                }

                test $"a body reading its source twice does not grow the list of {width}" {
                    let g = new Graph ()
                    let s = Signal (g, 0)
                    let memos = ResizeArray<Memo<int>>()

                    // Reading the same source twice in one body is where a
                    // missing dedupe actually accumulates: the source set that
                    // drives detachment holds one entry, so a run removes one
                    // observer entry and adds two. Reading once per run is
                    // self-correcting and hides the bug entirely.
                    for _ in 1..width do
                        memos.Add (Make.Memo (g, (fun _ -> s.Value + s.Value)))

                    for v in 1..50 do
                        s.Value <- v

                        for m in memos do
                            m.Value |> ignore

                    // Counted rather than inferred from behaviour: the
                    // duplication changes nothing the graph computes, because a
                    // second dirty mark on a dirty node is a no-op. It shows up
                    // only as a list that is 50 times longer than it should be.
                    Expect.equal s.ObserverCount width "one entry per reader, however many times each read"
                }

                test $"a disposed observer among {width} stops being woken" {
                    let g = new Graph ()
                    let s = Signal (g, 0)
                    let seen = ResizeArray ()
                    let effects = ResizeArray<Effect>()

                    for i in 1..width do
                        effects.Add (
                            new Effect (
                                g,
                                fun () ->
                                    s.Value |> ignore
                                    seen.Add i
                            )
                        )

                    // The first, the middle and the last: a swap-remove fills
                    // the hole from the end, so removing the last entry and
                    // removing an interior one are different paths.
                    let dropped = [ 0; width / 2; width - 1 ] |> List.distinct

                    for i in dropped do
                        effects[i].Dispose()

                    seen.Clear ()
                    s.Value <- 1

                    let survivors =
                        [ 1..width ]
                        |> List.filter (fun i -> not (List.contains (i - 1) dropped))

                    Expect.sequenceEqual (seen |> Seq.sort) survivors "the survivors, and only the survivors"
                }

                test $"observers among {width} can be dropped in any order" {
                    let g = new Graph ()
                    let s = Signal (g, 0)
                    let seen = ResizeArray ()

                    let effects =
                        [|
                            for i in 1..width ->
                                new Effect (
                                    g,
                                    fun () ->
                                        s.Value |> ignore
                                        seen.Add i
                                )
                        |]

                    // Dropped from alternating ends rather than in order. A
                    // swap-remove moves the last entry into the hole, and above
                    // the index threshold that move has to be written back to
                    // the position index — a failure only observable when
                    // something is removed *after* it has been moved, which
                    // dropping in order never reaches.
                    let order =
                        [ for k in 0 .. width - 1 -> if k % 2 = 0 then k / 2 else width - 1 - k / 2 ]

                    let mutable written = 0
                    let mutable expected = width

                    for i in order do
                        effects[i].Dispose()
                        expected <- expected - 1

                        Expect.equal s.ObserverCount expected $"one fewer observer after dropping effect {i + 1}"

                        seen.Clear ()
                        written <- written + 1
                        s.Value <- written

                        Expect.equal seen.Count expected "and exactly the survivors ran"
                }

                test $"a memo among {width} that drops the branch stops being woken" {
                    let g = new Graph ()
                    let useLeft = Signal (g, true)
                    let left = Signal (g, 1)
                    let memos = ResizeArray<Memo<int>>()

                    for _ in 1..width do
                        memos.Add (Make.Memo (g, (fun _ -> if useLeft.Value then left.Value else 0)))

                    for m in memos do
                        m.TryValue |> ignore

                    useLeft.Value <- false

                    for m in memos do
                        m.TryValue |> ignore

                    let before = memos |> Seq.map (fun m -> m.Runs) |> Seq.toList
                    left.Value <- 99
                    let after = memos |> Seq.map (fun m -> m.Runs) |> Seq.toList

                    Expect.sequenceEqual after before "a source nobody reads wakes nobody"
                }
        ]

/// <summary>
/// The other half of the edge: what a computation records about the sources it
/// read. That list is matched positionally against the next run's reads, so it
/// has failure modes of its own — a stale entry keeps waking a computation that
/// no longer reads it, and a missing duplicate check makes the list as long as
/// the read count.
/// </summary>
[<Tests>]
let sourceTests =
    testList
        "Sources"
        [
            test "a memo that stops reading a source stops recomputing for it" {
                let g = new Graph ()
                let useLeft = Signal (g, true)
                let left = Signal (g, 1)
                let m = Make.Memo (g, (fun _ -> if useLeft.Value then left.Value else 0))

                m.Value |> ignore
                useLeft.Value <- false
                m.Value |> ignore

                let before = m.Runs
                left.Value <- 99
                m.Value |> ignore

                // Read *after* the write, unlike the observer-side test: a stale
                // edge marks the memo dirty, and only a read turns that into a
                // recomputation the count can see.
                Expect.equal m.Runs before "the branch it no longer takes cannot wake it"
            }

            test "a memo that reads one source in a loop records one edge" {
                let g = new Graph ()
                let s = Signal (g, 1)

                let m =
                    Make.Memo (
                        g,
                        fun _ ->
                            let mutable total = 0

                            for _ in 1..20 do
                                total <- total + s.Value

                            total
                    )

                for v in 1..5 do
                    s.Value <- v
                    m.Value |> ignore

                Expect.equal m.SourceCount 1 "one edge, however many times it was read"
                Expect.equal s.ObserverCount 1 "and one observer entry on the other side"
            }

            test "a memo that reorders its reads ends up with both edges" {
                let g = new Graph ()
                let flip = Signal (g, false)
                let a = Signal (g, 1)
                let b = Signal (g, 10)

                // Reversing the read order is the case positional matching
                // cannot reuse: the first read mismatches its slot and the whole
                // list is rebuilt. Both edges still have to be live afterwards.
                let m =
                    Make.Memo (
                        g,
                        (fun _ ->
                            if flip.Value then
                                b.Value * 100 + a.Value
                            else
                                a.Value * 100 + b.Value)
                    )

                Expect.equal m.Value 110 "a then b"
                flip.Value <- true
                Expect.equal m.Value 1001 "b then a"
                Expect.equal m.SourceCount 3 "flip, b, a"

                a.Value <- 2
                Expect.equal m.Value 1002 "the source read last still wakes it"
                b.Value <- 20
                Expect.equal m.Value 2002 "and so does the one read first"
            }

            test "a source read twice keeps its edge when the later read is trimmed" {
                let g = new Graph ()
                let second = Signal (g, false)
                let a = Signal (g, 1)
                let b = Signal (g, 10)
                let c = Signal (g, 100)

                // The first run records [second; a; b; a]. The second run
                // matches [second; a], then trims b and the repeated a.
                let m =
                    Make.Memo (
                        g,
                        (fun _ ->
                            if second.Value then
                                a.Value + c.Value
                            else
                                a.Value + b.Value + a.Value)
                    )

                Expect.equal m.Value 12 "a, b, a"
                second.Value <- true
                Expect.equal m.Value 101 "a, c"
                Expect.equal a.ObserverCount 1 "a still has the memo as an observer"

                a.Value <- 2
                Expect.equal m.Value 102 "a write to a still wakes the memo"
            }

            // 10 leading sources take the list past the scan limit before the repeated read.
            for lead in [ 0; 10 ] do
                test $"a, b, a after {lead} other reads links a once while b also observes a" {
                    let g = new Graph ()
                    let pads = Array.init lead (fun _ -> Signal (g, 0))
                    let a = Signal (g, 1)
                    // b links to a between the memo's two reads of a, so a's newest observer is b.
                    let b = Make.Memo (g, (fun _ -> a.Value * 10))

                    let m =
                        Make.Memo (
                            g,
                            fun _ ->
                                for p in pads do
                                    p.Value |> ignore

                                a.Value + b.Value + a.Value
                        )

                    Expect.equal m.Value 12 "a, b, a"
                    Expect.equal m.SourceCount (lead + 3) "a slot per read"
                    Expect.equal a.ObserverCount 2 "one entry for the memo and one for b"

                    let before = m.Runs
                    a.Value <- 2
                    Expect.equal m.Value 24 "a write to a wakes the memo"
                    Expect.equal m.Runs (before + 1) "once"
                    Expect.equal a.ObserverCount 2 "and still one entry each after the re-run"
                }

                test $"truncation after {lead} other reads drops both slots of a repeated source" {
                    let g = new Graph ()
                    let pads = Array.init lead (fun _ -> Signal (g, 0))
                    let useA = Signal (g, true)
                    let a = Signal (g, 1)
                    let b = Signal (g, 10)
                    let c = Signal (g, 100)

                    let m =
                        Make.Memo (
                            g,
                            fun _ ->
                                for p in pads do
                                    p.Value |> ignore

                                if useA.Value then a.Value + b.Value + a.Value else c.Value
                        )

                    Expect.equal m.Value 12 "a, b, a"
                    useA.Value <- false
                    Expect.equal m.Value 100 "c"
                    Expect.equal a.ObserverCount 0 "a lost its only entry"
                    Expect.equal b.ObserverCount 0 "b too"

                    let before = m.Runs
                    a.Value <- 5
                    m.Value |> ignore
                    Expect.equal m.Runs before "a no longer wakes the memo"

                    useA.Value <- true
                    Expect.equal m.Value 20 "a, b, a again"
                    Expect.equal a.ObserverCount 1 "a linked once more"
                    a.Value <- 6
                    Expect.equal m.Value 22 "and a wakes the memo again"
                }

                test $"a, b, a after {lead} other reads of a source with 10 other readers links a once" {
                    let g = new Graph ()
                    let pads = Array.init lead (fun _ -> Signal (g, 0))
                    let a = Signal (g, 1)

                    for _ in 1..10 do
                        new Effect (g, (fun () -> a.Value |> ignore))
                        |> ignore

                    // b links to a between the memo's two reads of a, and a has more observers than a scan covers:
                    // the memo's own slots answer the repeated read.
                    let b = Make.Memo (g, (fun _ -> a.Value * 10))

                    let m =
                        Make.Memo (
                            g,
                            fun _ ->
                                for p in pads do
                                    p.Value |> ignore

                                a.Value + b.Value + a.Value
                        )

                    Expect.equal m.Value 12 "a, b, a"
                    Expect.equal a.ObserverCount 12 "one entry each for the memo, b and the effects"

                    let before = m.Runs
                    a.Value <- 2
                    Expect.equal m.Value 24 "a write to a wakes the memo"
                    Expect.equal m.Runs (before + 1) "once"
                    Expect.equal a.ObserverCount 12 "and still one entry each after the re-run"
                }

            for width in [ 3; 12 ] do
                test $"a reader of {width} sources in a changing order keeps one edge to each" {
                    let g = new Graph ()
                    let shift = Signal (g, 0)
                    let sources = Array.init width (fun i -> Signal (g, i))

                    let m =
                        Make.Memo (
                            g,
                            fun _ ->
                                let k = shift.Value
                                let mutable total = 0

                                for i in 0 .. width - 1 do
                                    total <- total + sources[(i + k) % width].Value

                                total
                        )

                    for k in 0..4 do
                        shift.Value <- k
                        m.Value |> ignore

                    Expect.equal m.SourceCount (width + 1) "shift and every source"

                    for s in sources do
                        Expect.equal s.ObserverCount 1 "one entry per source"

                    for i in 0 .. width - 1 do
                        let before = m.Runs
                        sources[i].Value <- sources[i].Value + 1
                        m.Value |> ignore
                        Expect.equal m.Runs (before + 1) $"source {i} wakes the memo once"
                }
        ]

/// <summary>
/// A computation that counts its dirty marks, runs <c>OnDirty</c> on each one, and holds its edge handle in the set it
/// is driven against.
/// </summary>
type private Probe(id: int) =
    member val Dirty = 0 with get, set
    member val OnDirty: unit -> unit = ignore with get, set
    member val Handle = EdgeHandle.Unlinked with get, set

    interface INode with
        member _.Id = id
        member _.Status = Status.None

    interface IComputation with
        member this.MarkDirty() =
            this.Dirty <- this.Dirty + 1
            this.OnDirty ()

        member _.MarkCheck() = ()
        member _.AddSource _ = ()

        member this.MovedIn(_, handle) =
            this.Handle <- handle

let private link (set: ObserverSet) (p: Probe) =
    p.Handle <- set.Add (p, 0)

let private unlink (set: ObserverSet) (p: Probe) =
    set.RemoveAt (p, p.Handle)
    p.Handle <- EdgeHandle.Unlinked

/// <summary>
/// Links the probe unless the set or the probe's handle shows it linked, as <c>SourceList</c> does.
/// </summary>
let private relink (set: ObserverSet) (p: Probe) =
    match set.TryAdd (p, 0) with
    | EdgeHandle.Unknown ->
        if p.Handle < 0 then
            link set p
    | EdgeHandle.Unlinked -> ()
    | handle -> p.Handle <- handle

let private probesOn (set: ObserverSet) width =
    let probes = Array.init width Probe

    for p in probes do
        link set p

    probes

/// <summary>
/// Whether the handles of the linked probes are exactly the positions <c>0 .. Count - 1</c>.
/// </summary>
let private handlesExact (set: ObserverSet) (probes: Probe seq) =
    let handles =
        probes
        |> Seq.map (fun p -> p.Handle)
        |> Seq.filter (fun h -> h >= 0)
        |> Seq.sort
        |> Seq.toList

    handles = List.init set.Count id

/// <summary>
/// The observer list driven directly, so removal can happen from inside a
/// notification walk and in orders the graph does not produce on its own.
/// </summary>
[<Tests>]
let observerSetTests =
    testList
        "ObserverSet"
        [
            for width in [ 3; 8; 9; 20; 100 ] do
                test $"removal during notification among {width} skips no survivor" {
                    let set = ObserverSet ()
                    let probes = probesOn set width
                    let first = probes[width - 1]

                    // Visited first: removes itself, every unvisited even probe,
                    // so more than half the list leaves mid-walk.
                    first.OnDirty <-
                        fun () ->
                            unlink set first

                            for i in 0..2 .. width - 2 do
                                unlink set probes[i]

                    set.NotifyDirty ()

                    for i in 0 .. width - 2 do
                        if i % 2 = 0 then
                            Expect.equal probes[i].Dirty 0 $"removed probe {i} is not marked"
                        else
                            Expect.isGreaterThan probes[i].Dirty 0 $"surviving probe {i} is marked"

                    Expect.equal first.Dirty 1 "the remover is marked once"
                    Expect.equal set.Count ((width - 1) / 2) "only the odd probes remain"
                    Expect.isTrue (handlesExact set probes) "every survivor's handle is its position"
                }

                test $"an observer added during notification among {width} is not marked" {
                    let set = ObserverSet ()
                    let probes = probesOn set width
                    let late = Probe 1000
                    probes[width - 1].OnDirty <- fun () -> link set late

                    set.NotifyDirty ()

                    Expect.equal late.Dirty 0 "added behind the walk"

                    for p in probes do
                        Expect.equal p.Dirty 1 "every original observer marked once"
                }

                test $"the count of {width} is exact after removals in any order" {
                    let set = ObserverSet ()
                    let probes = probesOn set width
                    // 7 is coprime with every width, so this removes each probe once, out of order.
                    let order = Array.init width (fun k -> (k * 7 + 3) % width)
                    let removed = Array.create width false

                    for step in 0 .. width - 1 do
                        let i = order[step]
                        unlink set probes[i]
                        unlink set probes[i]
                        removed[i] <- true

                        Expect.equal set.Count (width - step - 1) "one fewer per removal, however often repeated"
                        Expect.isTrue (handlesExact set probes) "every survivor's handle is its position"

                        for q in probes do
                            q.Dirty <- 0

                        set.NotifyDirty ()

                        for j in 0 .. width - 1 do
                            Expect.equal probes[j].Dirty (if removed[j] then 0 else 1) "exactly the survivors are marked"
                }

                test $"an observer re-added after removal among {width} is counted and marked once" {
                    let set = ObserverSet ()
                    let probes = probesOn set width

                    for round in 1..3 do
                        for i in 0..2 .. width - 1 do
                            unlink set probes[i]

                        for i in 0..2 .. width - 1 do
                            relink set probes[i]
                            relink set probes[i]

                        Expect.equal set.Count width $"every probe once after round {round}"

                    unlink set probes[width / 2]
                    relink set probes[width / 2]
                    relink set probes[width / 2]

                    for p in probes do
                        p.Dirty <- 0

                    set.NotifyDirty ()

                    for p in probes do
                        Expect.equal p.Dirty 1 "marked once"

                    Expect.isTrue (handlesExact set probes) "every handle is its position"
                }

                for walker in List.distinct [ 0; width / 2; width - 1 ] do
                    test $"the observer at {walker} of {width} removed by its own notification skips no survivor" {
                        let set = ObserverSet ()
                        let probes = probesOn set width
                        let self = probes[walker]

                        // The walk runs from the last position down, so every probe below the walker is unvisited.
                        let victims =
                            List.distinct [ 0; walker - 1 ]
                            |> List.filter (fun i -> i >= 0 && i < walker)

                        self.OnDirty <-
                            fun () ->
                                unlink set self

                                for i in victims do
                                    unlink set probes[i]

                        set.NotifyDirty ()

                        Expect.equal self.Dirty 1 "the walker is marked once"

                        for i in 0 .. width - 1 do
                            if List.contains i victims then
                                Expect.equal probes[i].Dirty 0 $"removed probe {i} is not marked"
                            elif i <> walker then
                                Expect.isGreaterThan probes[i].Dirty 0 $"surviving probe {i} is marked"

                        Expect.equal set.Count (width - 1 - victims.Length) "the walker and its victims left"

                        // A stale handle raises here.
                        for p in probes do
                            unlink set p

                        Expect.equal set.Count 0 "every survivor unlinked by its handle"
                    }

                test $"removing an absent observer from {width} changes nothing" {
                    let set = ObserverSet ()
                    let probes = probesOn set width
                    let stranger = Probe 1000

                    unlink set stranger
                    unlink set stranger
                    unlink set probes[1]
                    unlink set probes[1]

                    Expect.equal set.Count (width - 1) "only the one removal counts"

                    set.NotifyDirty ()

                    Expect.equal stranger.Dirty 0 "the stranger is not marked"

                    for i in 0 .. width - 1 do
                        Expect.equal probes[i].Dirty (if i = 1 then 0 else 1) "every remaining probe marked once"
                }

                test $"a handle holding another observer among {width} raises and removes nothing" {
                    let set = ObserverSet ()
                    let probes = probesOn set width

                    Expect.throws (fun () -> set.RemoveAt (probes[0], probes[1].Handle)) "another observer's handle"
                    Expect.throws (fun () -> set.RemoveAt (probes[0], width)) "a handle past the count"
                    Expect.equal set.Count width "nothing removed"

                    set.NotifyDirty ()

                    for p in probes do
                        Expect.equal p.Dirty 1 "every probe still marked"
                }

                test $"TryAdd among {width} finds the latest add and leaves wide misses to the caller" {
                    let set = ObserverSet ()
                    let probes = probesOn set width
                    let latest = probes[width - 1]

                    Expect.equal (set.TryAdd (latest, 0)) EdgeHandle.Unlinked "the latest add is held"

                    Expect.equal
                        (set.TryAdd (probes[0], 0))
                        (if width <= EdgeHandle.ScanLimit then
                             EdgeHandle.Unlinked
                         else
                             EdgeHandle.Unknown)
                        "an older observer is found by a scan of a narrow list only"

                    Expect.equal set.Count width "nothing added"

                    // The record names a handle, not the observer: after the latest observer leaves, it misses.
                    unlink set latest
                    let again = set.TryAdd (latest, 0)

                    if width - 1 <= EdgeHandle.ScanLimit then
                        Expect.isTrue (again >= 0) "a narrow list links the removed observer again"
                    else
                        Expect.equal again EdgeHandle.Unknown "a wide list leaves the removed observer to the caller"
                }

                test $"the latest add's record among {width} follows its observer through a swap" {
                    let set = ObserverSet ()
                    let probes = probesOn set width
                    let late = Probe 1000
                    link set late
                    unlink set probes[0]

                    Expect.equal late.Handle 0 "the last observer moved into the hole"
                    Expect.equal (set.TryAdd (late, 0)) EdgeHandle.Unlinked "and is still the latest add"
                }

            test "positions copied into a larger array read back as written" {
                let small = Platform.positions 3

                for i in 0..2 do
                    small[i] <- i - 1

                let large = Platform.positions 8
                Platform.copyPositions small large 3
                Expect.equal large.Length 8 "eight positions"
                Expect.equal [ for i in 0..2 -> large[i] ] [ -1; 0; 1 ] "the copied positions"
            }


            test "a wide list survives repeated relinking through the graph" {
                let g = new Graph ()
                let wide = Signal (g, 0)
                let gates = Array.init 40 (fun _ -> Signal (g, true))
                let runs = Array.zeroCreate 40

                for i in 0..39 do
                    new Effect (
                        g,
                        fun () ->
                            if gates[i].Value then
                                runs[i] <- runs[i] + wide.Value
                    )
                    |> ignore

                for round in 1..5 do
                    for i in 0..39 do
                        if (i + round) % 3 = 0 then
                            gates[i].Value <- false
                            gates[i].Value <- true

                Expect.equal wide.ObserverCount 40 "every effect linked once"

                Array.fill runs 0 40 0
                wide.Value <- 1
                Expect.sequenceEqual runs (Array.create 40 1) "every effect ran once on the write"
            }
        ]

/// <summary>
/// Disposal orders for readers created at positions <c>0 .. width - 1</c>: the newest first, and the oldest first.
/// </summary>
let private disposalOrders width =
    [ "reverse", [ width - 1 .. -1 .. 0 ]; "creation", [ 0 .. width - 1 ] ]

/// <summary>
/// The readers that hold an edge in a field rather than in a <c>SourceList</c>, each sharing its source with other
/// readers and disposed in both orders. A stale handle raises on removal or stops a survivor updating.
/// </summary>
[<Tests>]
let directEdgeTests =
    testList
        "Direct edges"
        [
            for width in [ 3; 12 ] do
                for name, order in disposalOrders width do
                    test $"{width} bindings of one signal disposed in {name} order" {
                        let g = new Graph ()
                        let s = Signal (g, 0)
                        let writes = Array.zeroCreate width
                        let mutable effectRuns = 0

                        let bind i =
                            Binding<obj, int>.OfSignal(g, s, null, (fun _ _ -> writes[i] <- writes[i] + 1))

                        let early = Array.init (width / 2) bind

                        // An ordinary reader between the bindings, so swaps also move a source list's handle.
                        new Effect (
                            g,
                            fun () ->
                                s.Value |> ignore
                                effectRuns <- effectRuns + 1
                        )
                        |> ignore

                        let bindings =
                            Array.append early (Array.init (width - width / 2) (fun i -> bind (width / 2 + i)))

                        let live = Array.create width true

                        for step, i in List.indexed order do
                            bindings[i].Dispose()
                            live[i] <- false
                            Expect.equal s.ObserverCount (width - step) "one edge fewer per disposal"

                            Array.fill writes 0 width 0
                            let before = effectRuns
                            s.Value <- s.Value + 1

                            for j in 0 .. width - 1 do
                                Expect.equal writes[j] (if live[j] then 1 else 0) $"binding {j} writes only while live"

                            Expect.equal effectRuns (before + 1) "the effect still runs"
                    }

                    test $"{width} folds of one projection disposed in {name} order" {
                        use g = new Graph ()
                        use _ = g.Activate ()
                        let source = createSignal [ 1, 1; 2, 2; 3, 3 ]
                        let weight = createSignal 1
                        let view = createProjection fst snd (fun () -> source.Value)
                        let mutable seen = 0
                        createEffect (fun () -> seen <- view.Get 1)

                        let folds =
                            Array.init width (fun _ ->
                                createRoot (fun owner ->
                                    let total =
                                        view
                                        |> Projection.foldGroup (fun s n -> s + n * weight.Value) (fun s n -> s - n * weight.Value) 0

                                    total.Value |> ignore
                                    owner, total))

                        let live = Array.create width true

                        let expectTotals label =
                            let expected = (source.Value |> List.sumBy snd) * weight.Value

                            for j in 0 .. width - 1 do
                                if live[j] then
                                    Expect.equal (snd folds[j]).Value expected $"{label}: fold {j}"

                        for step, i in List.indexed order do
                            (fst folds[i]).Dispose()
                            live[i] <- false
                            Expect.equal weight.ObserverCount (width - step - 1) "one edge to weight fewer per disposal"

                            weight.Value <- weight.Value + 1
                            expectTotals "a weight change"
                            source.Value <- source.Value |> List.map (fun (k, n) -> k, n + k)
                            expectTotals "a row change"
                            Expect.equal seen (snd source.Value[0]) "the effect reading the row still runs"
                    }

                    test $"pending rows settled and {width} folds disposed in {name} order" {
                        use g = new Graph ()
                        use _ = g.Activate ()
                        let keys = [ 0..3 ]
                        let flights = [| for _ in keys -> createAsyncSource<int>() |]
                        let view = createProjection id (fun k -> flights[k].Value) (fun () -> keys)
                        let mutable pending = false
                        createEffect (fun () -> pending <- view.AnyPending)

                        let folds =
                            Array.init width (fun _ ->
                                createRoot (fun owner ->
                                    let total = view |> Projection.foldGroup (+) (-) 0
                                    total.Value |> ignore
                                    owner, total))

                        use values = view.NewValueReader ()
                        values.Read () |> ignore
                        Expect.isTrue pending "every row is pending"

                        let settled = Array.create flights.Length 0
                        let live = Array.create width true

                        let expectTotals label =
                            for j in 0 .. width - 1 do
                                if live[j] then
                                    Expect.equal (snd folds[j]).Value (Array.sum settled) $"{label}: fold {j}"

                        // Each settle removes the row's edge to the summary watch while folds and the value reader
                        // still observe the row.
                        let settleOrder = if name = "reverse" then List.rev keys else keys

                        for step, k in List.indexed settleOrder do
                            settled[k] <- 10 * (k + 1)
                            flights[k].Settle settled[k]
                            Expect.equal pending (step < keys.Length - 1) "pending until the last row settles"
                            expectTotals "a settle"

                        values.Read () |> ignore

                        for i in order do
                            (fst folds[i]).Dispose()
                            live[i] <- false
                            settled[1] <- settled[1] + 1
                            flights[1].Settle settled[1]
                            expectTotals "a row change"

                            let changed =
                                (values.Read ()).Changes
                                |> Seq.map (fun change -> change.Key)
                                |> List.ofSeq

                            Expect.equal changed [ 1 ] "the value reader sees the row change"
                    }

            test "a pending row's edge to the summary watch survives a swap" {
                use g = new Graph ()
                use _ = g.Activate ()
                let gate = createSignal false
                let flight = createAsyncSource<int>()

                let view =
                    createProjection id (fun k -> if gate.Value then flight.Value else k) (fun () -> [ 1 ])

                let mutable pending = false
                createEffect (fun () -> pending <- view.AnyPending)

                let folds =
                    Array.init 3 (fun _ ->
                        createRoot (fun owner ->
                            let total = view |> Projection.foldGroup (+) (-) 0
                            total.Value |> ignore
                            owner, total))

                // The row turns pending after the folds observe it, so the watch's edge is the row's last.
                gate.Value <- true

                for _, total in folds do
                    total.Value |> ignore

                Expect.isTrue pending "the row is pending"

                // Moves the watch's edge into the disposed fold's position.
                (fst folds[0]).Dispose()
                flight.Settle 5
                Expect.isFalse pending "the settle reaches the summary"

                for j in 1..2 do
                    Expect.equal (snd folds[j]).Value 5 $"fold {j}"
            }
        ]
