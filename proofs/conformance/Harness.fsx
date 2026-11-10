// The harness of a run over real Signal, Memo and Effect objects: the event type, reflection over the core's
// private state, and `Ctx`, the graph with its trace. Loaded by Scenarios.fsx and ../differential/Generate.fsx.
#r "../../src/Ranvier/bin/Release/net10.0/Ranvier.dll"

open System
open System.Collections.Generic
open System.IO
open System.Reflection
open Ranvier

type Outcome =
    | OkV of int
    | FailV of int

/// One Rocq `event`, in trace order.
type Ev =
    | Run of int
    | Log of int * int
    | Cleanup of int
    | FailE of int * int
    | Disposed of int
    | HostRead of int * Outcome

/// An exception whose identity is `f`, as `Fail f` denotes one exception instance.
exception Boom of int

/// The driver of a run gave it up, and the run's trace is void. `Guarded` records nothing for it.
exception Abandoned

let privateField (o: obj) (name: string) =
    o.GetType().GetField(name, BindingFlags.Instance ||| BindingFlags.NonPublic).GetValue o

let internalProp (o: obj) (name: string) =
    o
        .GetType()
        .GetProperty(
            name,
            BindingFlags.Instance
            ||| BindingFlags.NonPublic
            ||| BindingFlags.Public
        )
        .GetValue
        o

let idOf (node: obj) : int =
    let inode = typeof<Graph>.Assembly.GetType "Ranvier.INode"
    inode.GetProperty("Id").GetValue node :?> int

let statusOf (node: obj) : string =
    let inode = typeof<Graph>.Assembly.GetType "Ranvier.INode"
    (inode.GetProperty("Status").GetValue node).ToString()

/// The `freshness` field, which `Status.Stale` reports only as stale or not.
let freshnessOf (node: obj) : string =
    (privateField node "freshness").ToString()

type FreshRecord = { Held: int }

/// A graph with a trace. `Effect`, `Memo` and `Signal` take the label a scenario expects. `Check` records a failure
/// and returns, since a body's exception would end in the node's status, and the run of the script fails at the
/// scenario's end on any recorded failure or any exception other than `Boom` escaping a body.
type Ctx() =
    let g = new Graph ()
    let events = ResizeArray<Ev>()
    let booms = Dictionary<int, exn>()
    let failures = ResizeArray<string>()

    member _.Graph = g
    member _.Events = List.ofSeq events

    member _.Emit(e: Ev) =
        events.Add e

    member _.Throw(f: int) : 'a =
        match booms.TryGetValue f with
        | true, ex -> raise ex
        | _ ->
            let ex = Boom f
            booms[f] <- ex
            raise ex

    member _.Failures = List.ofSeq failures

    member _.Check (condition: bool) (message: string) =
        if not condition then
            failures.Add message

    /// Records an exception other than `Boom` leaving a body, and passes every exception on.
    member _.Guarded(body: unit -> 'a) : 'a =
        try
            body ()
        with
        | Boom _
        | Abandoned -> reraise ()
        | ex ->
            failures.Add $"a body raised {ex.GetType().Name}: {ex.Message}"
            reraise ()

    member this.Labelled (label: int) (node: 'n) : 'n =
        this.Check (idOf (box node) = label) $"node labelled {label} has id {idOf (box node)}"
        node

    member this.Signal (label: int) (v: int) : Signal<int> =
        this.Labelled label (Signal<int>(g, v))

    member this.Memo (label: int) (body: unit -> int) : Memo<int> =
        this.Labelled label (Memo<int>(g, Func<int voption, int>(fun _ -> this.Guarded body)))

    /// A memo through the public constructor and the graph's default options, returning a new record per run. Its value
    /// is `CmpNever`'s model: `JsIdentityPolicy` compares a reference type by reference.
    member this.FreshRecordMemo (label: int) (body: unit -> int) : Memo<FreshRecord> =
        this.Labelled label (Memo<FreshRecord>(g, Func<FreshRecord voption, FreshRecord>(fun _ -> { Held = this.Guarded body })))

    member this.OwningMemo (label: int) (body: unit -> int) : Memo<int> =
        this.Labelled label (Memo<int>(g, Func<int voption, int>(fun _ -> this.Guarded body), true))

    /// `CMemo` with a pure scope and the given comparer.
    member private this.MemoWithComparer (label: int) (comparer: IEqualityComparer<int>) (body: unit -> int) : Memo<int> =
        let create =
            typeof<Memo<int>>
                .GetMethod(
                    "CreateWithComparer",
                    BindingFlags.Static
                    ||| BindingFlags.NonPublic
                    ||| BindingFlags.Public
                )

        let scopeMode =
            Enum.Parse (typeof<Graph>.Assembly.GetType "Ranvier.ScopeMode", "Pure")

        let guarded: int voption -> int = fun _ -> this.Guarded body

        this.Labelled label (create.Invoke (null, [| box g; box guarded; scopeMode; box comparer |]) :?> Memo<int>)

    /// `CMemo` with a pure scope and `CmpThrows f`: a memo whose comparer raises `f` each time it is consulted.
    member this.MemoThrowingComparer (label: int) (f: int) (body: unit -> int) : Memo<int> =
        let comparer =
            { new IEqualityComparer<int> with
                member _.Equals(_, _) =
                    this.Throw f

                member _.GetHashCode v = v
            }

        this.MemoWithComparer label comparer body

    /// `CMemo` with a pure scope and `CmpNever`: a memo whose every run counts as a move.
    member this.MemoNeverEqual (label: int) (body: unit -> int) : Memo<int> =
        let comparer =
            { new IEqualityComparer<int> with
                member _.Equals(_, _) = false
                member _.GetHashCode v = v
            }

        this.MemoWithComparer label comparer body

    member this.Effect (label: int) (body: unit -> unit) : Effect =
        let run () =
            this.Emit (Run label)

            try
                this.Guarded body
            with Boom f as ex ->
                this.Emit (FailE (label, f))
                raise ex

        this.Labelled label (new Effect (g, Action run))

    member this.Cleanup (label: int) (body: unit -> unit) =
        let run () =
            this.Emit (Cleanup label)

            try
                this.Guarded body
            with Boom f as ex ->
                this.Emit (FailE (label, f))
                raise ex

        g.OnCleanup (Action run)

    member this.Log (label: int) (v: int) =
        this.Emit (Log (label, v))

    member this.Batch(body: unit -> unit) =
        g.Batch (Func<unit>(body))

    /// `CDispose`: the event marks a disposal that takes effect.
    member this.Dispose (label: int) (dispose: unit -> unit) (node: obj) =
        let was = privateField node "disposed" :?> bool
        dispose ()

        if not was then
            this.Emit (Disposed label)

    /// `Memo` whose body receives `prev`.
    member this.PrevMemo (label: int) (body: int voption -> int) : Memo<int> =
        this.Labelled label (Memo<int>(g, Func<int voption, int>(fun prev -> this.Guarded (fun () -> body prev))))

    /// The live entries of the graph's effect queue.
    member _.QueueLength =
        (privateField g "queueCount" :?> int)
        - (privateField g "queueHead" :?> int)

    /// Asserts each node's status, an empty queue, and that an explicit flush adds no event and leaves the statuses.
    member this.CheckQuiet(expected: (string * obj * string) list) =
        let check (stage: string) =
            for name, node, status in expected do
                this.Check (statusOf node = status) $"{stage}: {name} has status {statusOf node}, not {status}"

            this.Check (this.QueueLength = 0) $"{stage}: the queue holds {this.QueueLength}"

        check "before the flush"
        let before = events.Count
        g.Flush ()
        this.Check (events.Count = before) "the explicit flush ran something"
        check "after the flush"

    member this.HostRead (label: int) (read: unit -> int) : int =
        try
            let v = read ()
            this.Emit (HostRead (label, OkV v))
            v
        with Boom f as ex ->
            this.Emit (HostRead (label, FailV f))
            raise ex
