module Ranvier.View.Dom.Microtask

open Ranvier.View
open Fable.Core
open Ranvier.View.Dom.Scheduling

#if FABLE_COMPILER
[<Global>]
let private globalThis: TS.Es.GlobalThis = jsNative
#endif

let private post callback =
#if FABLE_COMPILER
    globalThis.queueMicrotask callback
#else
    Native.queueMicrotask callback
#endif

let create graph failure =
    Queue (graph, failure, post)

let mode failure =
    QueuedCommits (failure, post)
