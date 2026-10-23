/// <summary>The conformance case format: cases written once, run under jsdom and on the DOM twin.</summary>
module Ranvier.View.Testing.Conformance

open Fable.Core.TS.Dom

/// <summary>
/// The host-specific operations a case uses: the document, the clock, interception and the artefact sink. Both hosts
/// give each case an empty document and the same clock: the view's queued microtasks and <c>Post</c> share one queue
/// that only <c>RunMicrotasks</c> drains, and timers and animation frames run only in <c>AdvanceBy</c>.
/// </summary>
type Harness =
    abstract Platform: string
    abstract Document: Document
    /// <summary>The <c>post</c> of <c>QueuedCommits</c>: appends to the harness's queue.</summary>
    abstract Post: (unit -> unit) -> unit
    /// <summary>Drains the harness's queue, including callbacks queued while it drains.</summary>
    abstract RunMicrotasks: unit -> unit
    /// <summary>
    /// Moves the clock forward: the timers and animation frames (every 16 ms) due within the span run in due order, and
    /// the queue drains after each callback.
    /// </summary>
    abstract AdvanceBy: float -> unit
    /// <summary>Records a named artefact (host HTML, dump text) for the cross-host comparison.</summary>
    abstract Record: name: string * text: string -> unit
    /// <summary>The messages of the listener failures since the last call, which it clears.</summary>
    abstract ListenerErrors: unit -> string list
    /// <summary>
    /// Routes calls of the DOM method <c>memberName</c> on <c>target</c>, or on every node of the document when
    /// <c>target</c> is null, through <c>wrap</c> until the returned function runs. <c>wrap</c> receives the original
    /// method and the call's arguments, and its result is the call's result.
    /// </summary>
    abstract Intercept: target: obj * memberName: string * wrap: System.Func<System.Func<obj array, obj>, obj, obj array, obj> -> (unit -> unit)
    /// <summary>
    /// Records the child list, attribute and character data changes under <c>root</c>, as a MutationObserver over its
    /// subtree does, until the returned function runs; that function returns each change's kind and target in order.
    /// </summary>
    abstract ObserveMutations: root: Node -> (unit -> (string * Node) array)

type Case = string * (Harness -> unit)

/// <summary>
/// The running case's harness. It keeps the listener failures already recorded as artefacts, and its
/// <c>ListenerErrors</c> returns them with the later ones.
/// </summary>
type private Running(inner: Harness) =
    let recorded = ResizeArray<string>()
    let locals = System.Collections.Generic.Dictionary<string, obj>()

    /// <summary>The values <c>Conformance.local</c> created for the case, by key.</summary>
    member _.Locals = locals

    /// <summary>The failures since the last call, kept for <c>ListenerErrors</c>.</summary>
    member _.Fresh() : string list =
        let fresh = inner.ListenerErrors ()
        recorded.AddRange fresh
        fresh

    interface Harness with
        member _.Platform = inner.Platform
        member _.Document = inner.Document

        member _.Post callback =
            inner.Post callback

        member _.RunMicrotasks() =
            inner.RunMicrotasks ()

        member _.AdvanceBy milliseconds =
            inner.AdvanceBy milliseconds

        member _.Record(name, text) =
            inner.Record (name, text)

        member _.ListenerErrors() =
            let all = List.ofSeq recorded @ inner.ListenerErrors ()
            recorded.Clear ()
            all

        member _.Intercept(target, memberName, wrap) =
            inner.Intercept (target, memberName, wrap)

        member _.ObserveMutations root =
            inner.ObserveMutations root

#if FABLE_COMPILER
let mutable private current: Harness = Unchecked.defaultof<_>

let private read () = current

let private write (value: Harness) =
    current <- value
#else
[<Sealed; AbstractClass>]
type private Slot =
    [<System.ThreadStatic; DefaultValue>]
    static val mutable private current: Harness

    static member Current
        with get () = Slot.current
        and set (value: Harness) = Slot.current <- value

let private read () =
    Slot.Current

let private write (value: Harness) =
    Slot.Current <- value
#endif

let harness () : Harness =
    match box (read ()) with
    | null -> raise (System.InvalidOperationException "No conformance case is running on this thread")
    | value -> unbox<Harness> value

let document () : Document =
    (harness ()).Document

/// <summary>Whether a conformance case is running on this thread.</summary>
let isRunning () : bool =
    not (isNull (box (read ())))

/// <summary>
/// The running case's value under <c>key</c>, created by <c>create</c> on its first use in the case: state a check
/// module keeps per case, so cases on different threads or hosts share none.
/// </summary>
let local (key: string) (create: unit -> 'T) : 'T =
    match box (read ()) with
    | null -> raise (System.InvalidOperationException "No conformance case is running on this thread")
    | value ->
        let locals = (unbox<Running> value).Locals

        match locals.TryGetValue key with
        | true, existing -> unbox<'T> existing
        | false, _ ->
            let created = create ()
            locals[key] <- box created
            created

/// <summary>The running case's <c>Harness.Intercept</c>, with <c>wrap</c> taking the original method and the arguments.</summary>
let intercept (target: obj) (name: string) (wrap: (obj array -> obj) -> obj array -> obj) : unit -> unit =
    (harness ()).Intercept(target, name, System.Func<_, _, _, _>(fun original _ args -> wrap original.Invoke args))

/// <summary>The running case's <c>Harness.Intercept</c>, with <c>wrap</c> taking the original method, the call's target and the arguments.</summary>
let interceptOn (target: obj) (name: string) (wrap: (obj array -> obj) -> obj -> obj array -> obj) : unit -> unit =
    (harness ()).Intercept(target, name, System.Func<_, _, _, _>(fun original self args -> wrap original.Invoke self args))

/// <summary>Records a named artefact with the running case's harness; outside a case it does nothing.</summary>
let recordIfRunning (name: string) (text: string) : unit =
    match box (read ()) with
    | null -> ()
    | value -> (unbox<Harness> value).Record(name, text)

/// <summary>
/// Records the listener failures since the last such record as a <c>listener-errors</c> artefact, one message per
/// line, when there are any; the running harness's <c>ListenerErrors</c> still returns them.
/// </summary>
let recordListenerErrors () : unit =
    match box (read ()) with
    | null -> ()
    | value ->
        match (unbox<Running> value).Fresh() with
        | [] -> ()
        | messages -> (unbox<Harness> value).Record("listener-errors", String.concat "\n" messages)

/// <summary>The node's document, or the node itself when it is a document.</summary>
let documentOf (node: Node) : Document =
    match node.ownerDocument with
    | Some document -> document
    | None -> unbox<Document> node

let private run (value: Harness) (check: unit -> unit) =
    let previous = read ()
    write (Running value)

    try
        check ()
    finally
        write previous

let ofChecks (checks: (string * (unit -> unit)) array) : Case array =
    checks
    |> Array.map (fun (name, check) -> name, (fun value -> run value check))

/// <summary>
/// The cases of checks that return a JavaScript promise, which the jsdom runner awaits. A check reads the document before
/// its first await; the harness is current until the check returns its promise. On .NET the check runs and its result
/// is dropped.
/// </summary>
let ofPromiseChecks (checks: (string * (unit -> obj)) array) : Case array =
    checks
    |> Array.map (fun (name, check) ->
#if FABLE_COMPILER
        name,
        unbox<Harness -> unit>(
            box (fun (value: Harness) ->
                let previous = read ()
                write (Running value)

                try
                    check ()
                finally
                    write previous)
        ))
#else
        name, (fun value -> run value (fun () -> check () |> ignore)))
#endif

let platformOnly (platform: string) (reason: string) (name: string, check: 'check) =
    $"[{platform}] {name} ({reason})", check

let skippedOn (platform: string) (name: string) =
    (name.StartsWith "[jsdom]" && platform <> "jsdom")
    || (name.StartsWith "[twin]" && platform <> "twin")
