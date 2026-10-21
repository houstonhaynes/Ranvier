/// <summary>Mounting, clock and DOM reads shared by the page checks, which pages.test.mjs runs under jsdom.</summary>
module Harness

open Fable.Core
open global.Ranvier
open Ranvier.Anime
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Scheduling

/// <summary>Advances the mocked <c>Date</c> and timers by the given milliseconds.</summary>
type Clock = float -> unit

type Case = { Name: string; Check: Clock -> unit }

let case (name: string) (check: Clock -> unit) =
    { Name = name; Check = check }

/// <summary>Registers <c>dispose</c> to run at the next <c>release</c>; the runner releases after every check, failed or not.</summary>
[<Emit("(globalThis.motionOpen ??= []).push($0)")>]
let owned (dispose: unit -> unit) : unit = jsNative

/// <summary>Runs every disposal registered since the last release, latest first.</summary>
[<Emit("globalThis.motionRelease()")>]
let release () : unit = jsNative

/// <summary>The same check under synchronous and queued commits.</summary>
let modes (name: string) (check: bool -> Clock -> unit) =
    [
        for queued in [ false; true ] do
            case (sprintf "%s (%s commits)" name (if queued then "queued" else "synchronous")) (check queued)
    ]

[<Emit("$0.style.getPropertyValue($1)")>]
let styleOf (element: obj) (property: string) : string = jsNative

[<Emit("$0.getAttribute($1)")>]
let attributeOf (element: obj) (name: string) : string = jsNative

[<Emit("$0.querySelector($1)")>]
let find (root: obj) (selector: string) : TS.Dom.HTMLElement = jsNative

[<Emit("Array.from($0.querySelectorAll($1), n => n.textContent.trim())")>]
let textsOf (root: obj) (selector: string) : string array = jsNative

[<Emit("Array.from($0.querySelectorAll($1), n => n.getAttribute($2))")>]
let attributesOf (root: obj) (selector: string) (name: string) : string array = jsNative

[<Emit("$0.querySelectorAll($1).length")>]
let countOf (root: obj) (selector: string) : int = jsNative

[<Emit("$0.isConnected")>]
let connected (node: obj) : bool = jsNative

[<Emit("$0.click()")>]
let click (element: obj) : unit = jsNative

[<Emit("$0.textContent.trim()")>]
let textOf (element: obj) : string = jsNative

[<Emit("Object.defineProperty($0, $1, { value: $2, configurable: true })")>]
let fixProperty (target: obj) (name: string) (value: obj) : unit = jsNative

/// <summary>Runs <c>step</c> after the test's microtasks have drained, inside the same test.</summary>
[<Emit("globalThis.motionLater.push($0)")>]
let later (step: unit -> unit) : unit = jsNative

let equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let truth (label: string) (condition: bool) =
    if not condition then
        failwithf "Expected %s" label

/// <summary>Advances the clock and steps anime's engine.</summary>
let advance (clock: Clock) (ms: float) =
    clock ms
    Testing.step ()

/// <summary>Advances in frames of <c>frame</c> milliseconds until <c>total</c> has passed.</summary>
let run (clock: Clock) (frame: float) (total: float) =
    let mutable passed = 0.

    while passed < total do
        advance clock frame
        passed <- passed + frame

let report (errors: exn list) =
    failwithf "Unexpected report %A" [ for error in errors -> error.Message ]

/// <summary>A new element under the document's body.</summary>
let host () : TS.Dom.HTMLElement =
    let target = Page.document.createElement "div"
    Page.document.body.appendChild target |> ignore
    target

/// <summary>Synchronous commits, or commits queued until <c>Flush</c>.</summary>
let modeOf (queued: bool) =
    if queued then
        QueuedCommits (CollectAndContinue, ignore)
    else
        SynchronousCommits

/// <summary>Runs <c>action</c> on <c>graph</c> and flushes the mount's queued commits.</summary>
let write (graph: Graph) (mounted: Mounting.Mounted) (action: unit -> unit) =
    graph.Run action
    mounted.Flush ()

/// <summary>Hands anime's engine to the test clock; called once, after <c>animejs</c> is imported.</summary>
let prepare () =
    Testing.manual ()
