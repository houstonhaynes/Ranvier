#if FABLE_COMPILER
module Fable.Ranvier.Global

open Fable.Core
open Fable.Core.TS.Dom
open Fable.Core.TS

[<Global "globalThis">]
let globalThis: GlobalThis = jsNative

[<Global "window">]
let window: Window = jsNative

[<Global "document">]
let document: Document = jsNative

let isBrowser = lazy (Option.ofObj document |> Option.isSome)
#else
namespace Fable.Ranvier

open Fable.Core.TS
open Fable.Core.TS.Dom

/// <summary>The browser globals of a .NET DOM host, such as Ranvier.View.WebCore's page.</summary>
type IGlobals =
    abstract Document: Document
    abstract QueueMicrotask: (unit -> unit) -> unit

/// <summary>
/// The JavaScript globals on .NET: each member reads the globals installed on the current thread with <c>Install</c>.
/// </summary>
[<Sealed; AbstractClass>]
type Global =
    [<System.ThreadStatic; DefaultValue>]
    static val mutable private current: IGlobals

    static member private Current =
        if isNull (box Global.current) then
            raise (System.InvalidOperationException "No DOM globals are installed on this thread; create a .NET DOM host first")

        Global.current

    /// <summary>
    /// Installs <c>globals</c> for the current thread until the returned value is disposed, which restores the previous
    /// globals.
    /// </summary>
    static member Install(globals: IGlobals) : System.IDisposable =
        let previous = Global.current
        Global.current <- globals

        { new System.IDisposable with
            member _.Dispose() =
                Global.current <- previous
        }

    static member document: Document = Global.Current.Document

    static member queueMicrotask(callback: unit -> unit) : unit =
        Global.Current.QueueMicrotask callback

    static member window: Window =
        raise (System.NotSupportedException "window is not available on .NET")

    static member globalThis: GlobalThis =
        raise (System.NotSupportedException "globalThis is not available on .NET")

    static member isBrowser = lazy (not (isNull (box Global.current)))
#endif
