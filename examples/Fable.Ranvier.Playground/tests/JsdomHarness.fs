module JsdomHarness

open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View.Testing.Conformance

/// <summary>
/// The JavaScript side of a harness over a jsdom or browser document: <c>conformance-host.mjs</c>'s clock, which
/// replaces the window's microtask, timer and animation-frame functions, and its method interception.
/// </summary>
type HostSide =
    abstract post: (unit -> unit) -> unit
    abstract runMicrotasks: unit -> unit
    abstract advanceBy: float -> unit
    abstract record: string * string -> unit
    abstract errors: unit -> string array
    abstract intercept: obj * string * System.Func<System.Func<obj array, obj>, obj, obj array, obj> -> (unit -> unit)

[<Emit("(() => { const o = new $0.defaultView.MutationObserver(() => {}); o.observe($1, { subtree: true, childList: true, attributes: true, characterData: true }); return () => { const r = o.takeRecords().map(m => [m.type, m.target]); o.disconnect(); return r; }; })()")>]
let private observe (document: Document) (root: Node) : unit -> (string * Node) array = jsNative

/// <summary>A harness over a jsdom or browser document whose clock and interception <c>host</c> provides.</summary>
let create (document: Document) (host: HostSide) : Harness =
    { new Harness with
        member _.Platform = "jsdom"
        member _.Document = document

        member _.Post callback =
            host.post callback

        member _.RunMicrotasks() =
            host.runMicrotasks ()

        member _.AdvanceBy milliseconds =
            host.advanceBy milliseconds

        member _.Record(name, text) =
            host.record (name, text)

        member _.ListenerErrors() =
            List.ofArray (host.errors ())

        member _.Intercept(target, memberName, wrap) =
            host.intercept (target, memberName, wrap)

        member _.ObserveMutations root =
            observe document root
    }
