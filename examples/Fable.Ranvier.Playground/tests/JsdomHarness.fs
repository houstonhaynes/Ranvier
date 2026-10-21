module JsdomHarness

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
    abstract intercept: obj * string * System.Func<System.Func<obj array, obj>, obj array, obj> -> (unit -> unit)

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
    }
