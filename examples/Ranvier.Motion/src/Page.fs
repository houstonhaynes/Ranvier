/// <summary>
/// The browser interop the pages share: hosts, element queries, the test handle, the commit mode, tweened points and
/// seeded numbers.
/// </summary>
module Page

open Fable.Core
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Dom.Scheduling

[<Global>]
let document: TS.Dom.Document = jsNative

/// <summary>Publishes <c>handle</c> as <c>window.__motion</c>, the object the browser tests read.</summary>
[<Emit("globalThis.__motion = $0")>]
let expose (handle: obj) : unit = jsNative

/// <summary>The page's <c>#app</c> element.</summary>
let host () : TS.Dom.HTMLElement =
    match document.getElementById "app" with
    | Some element -> element
    | None -> invalidOp "The page needs an #app host."

/// <summary>Logs mount and hydration failures to the console.</summary>
let report (errors: exn list) =
    for error in errors do
        JS.console.error ("Ranvier", error)

/// <summary>The commit mode of a browser page: commits queued on a microtask, failures collected.</summary>
let browserMode: CommitMode = Microtask.mode CollectAndContinue

[<Emit("$0.querySelector($1)")>]
let query (root: obj) (selector: string) : TS.Dom.Element = jsNative

[<Emit("Array.from($0.querySelectorAll($1))")>]
let queryAll (root: obj) (selector: string) : TS.Dom.Element array = jsNative

[<Emit("$0.target.value")>]
let targetValue (event: obj) : string = jsNative

[<Emit("$0.target.checked")>]
let targetChecked (event: obj) : bool = jsNative

[<Emit("$0.clientWidth")>]
let widthOf (element: obj) : float = jsNative

/// <summary>A plain JavaScript object with numeric <c>x</c> and <c>y</c>, for anime to tween.</summary>
[<AllowNullLiteral>]
type Point =
    abstract x: float with get, set
    abstract y: float with get, set

[<Emit("({ x: $0, y: $1 })")>]
let point (x: float) (y: float) : Point = jsNative

/// <summary>The next state of the Park–Miller generator, in 1..2147483646 for a seed in that range.</summary>
/// <remarks>The sequence is identical under Fable and on .NET.</remarks>
let nextSeed (seed: int) =
    int ((float seed * 16807.) % 2147483647.)

/// <summary>A number in 0..1 drawn from <c>seed</c>.</summary>
let unitOf (seed: int) =
    float (seed % 10000) / 10000.
