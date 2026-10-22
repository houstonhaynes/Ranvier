/// <summary>Two views mounted on graphs of their own, driven by the same steps, and compared by markup, reports, counters, listeners and native writes.</summary>
module TwinTrace

open System
open global.Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View
open Ranvier.View.Dom
open CheckSupport

type Step =
    | Write of (unit -> unit)
    | Each of (HTMLElement -> unit)

/// <summary>Native writes recorded under a root since the previous drain.</summary>
type WriteWatch =
    abstract drain: unit -> string array
    abstract stop: unit -> unit

[<Emit("(() => { const o = new MutationObserver(() => {}); o.observe($0, { attributes: true, childList: true, characterData: true, subtree: true }); return { drain: () => o.takeRecords().map(r => r.type + ':' + (r.attributeName ?? '') + ':' + r.addedNodes.length + ':' + r.removedNodes.length), stop: () => o.disconnect() }; })()")>]
let watchWrites (root: HTMLElement) : WriteWatch = jsNative

/// <summary>Listener registrations made through <c>addEventListener</c> since the wrap was installed.</summary>
type ListenerWatch =
    abstract under: HTMLElement -> int
    abstract stop: unit -> unit

[<Emit("(() => { const log = []; const proto = window.EventTarget.prototype; const original = proto.addEventListener; proto.addEventListener = function (type, ...rest) { log.push([this, type]); return original.call(this, type, ...rest); }; return { under: (root) => log.filter(([target]) => target instanceof Node && root.contains(target)).length, stop: () => { proto.addEventListener = original; } }; })()")>]
let watchListeners () : ListenerWatch = jsNative

[<Emit("globalThis.ranvierExpectCounters === true")>]
let countersExpected () : bool = jsNative

/// <summary>The graph counters as name-value pairs; fails when counters are expected but not compiled.</summary>
let tally () : (string * float) list =
#if RANVIER_COUNTERS
    [ for name, value in Counters.Snapshot () -> name, float value ]
#else
    if countersExpected () then
        failwith "The run expects counters; compile with RanvierCounters=true"

    []
#endif

let delta (before: (string * float) list) (after: (string * float) list) =
    List.map2 (fun (name, was) (_, now) -> name, now - was) before after

let click (selector: string) (root: HTMLElement) =
    (root.querySelector selector |> unbox<HTMLElement>).click()

/// <summary>
/// One twin's run: markup, reports, counter deltas, listener registrations under the host and native writes after the
/// mount and each step, plus the counter delta and writes of the disposal.
/// </summary>
type Trace =
    {
        Shots: string list
        Reports: string list list
        Counts: (string * float) list list
        Listeners: int list
        Writes: string array list
    }

/// <summary>
/// Mounts <c>view</c> on a graph and state of its own, runs <c>steps</c> and disposes. Counters are read around the
/// mount, each step and the disposal.
/// </summary>
let run queued (make: Graph -> 'state) (view: 'state -> Program.View) (steps: 'state -> Step list) =
    use graph = new Graph ()
    let state = make graph
    let target = host ()
    let reports = ResizeArray<exn list>()
    let writes = watchWrites target
    let listeners = watchListeners ()

    try
        let shots, counts, registrations, native =
            ResizeArray (), ResizeArray (), ResizeArray (), ResizeArray ()

        let measured (act: unit -> unit) =
            let before = tally ()
            act ()
            counts.Add (delta before (tally ()))

        let mounted = ref Unchecked.defaultof<_>

        measured (fun () ->
            mounted.Value <- mountTemplate queued graph reports.Add target (view state)
            mounted.Value.Flush ())

        let record () =
            shots.Add target.innerHTML
            registrations.Add (listeners.under target)
            native.Add (writes.drain ())

        record ()

        for step in steps state do
            measured (fun () ->
                match step with
                | Write write -> graph.Run write
                | Each act -> act target

                mounted.Value.Flush ())

            record ()

        measured mounted.Value.Dispose
        shots.Add target.innerHTML
        native.Add (writes.drain ())

        {
            Shots = List.ofSeq shots
            Reports = messages reports
            Counts = List.ofSeq counts
            Listeners = List.ofSeq registrations
            Writes = List.ofSeq native
        },
        state
    finally
        writes.stop ()
        listeners.stop ()

/// <summary>Runs the explicit twin, then the sugared twin, each on its own graph and state.</summary>
let twins queued make explicitView sugarView steps =
    let explicitTrace, explicitState = run queued make explicitView steps
    let sugarTrace, sugarState = run queued make sugarView steps
    (explicitTrace, sugarTrace), (explicitState, sugarState)

let same (explicitTrace: Trace, sugarTrace: Trace) =
    equal explicitTrace.Shots sugarTrace.Shots
    equal explicitTrace.Reports sugarTrace.Reports

    for index, (left, right) in List.indexed (List.zip explicitTrace.Counts sugarTrace.Counts) do
        equal (index, left) (index, right)

    equal explicitTrace.Listeners sugarTrace.Listeners

    for index, (left, right) in List.indexed (List.zip explicitTrace.Writes sugarTrace.Writes) do
        equal (index, left) (index, right)

let pair queued make explicitView sugarView steps =
    same (fst (twins queued make explicitView sugarView steps))
