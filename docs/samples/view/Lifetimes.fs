/// The F# samples of docs/content/view/lifetimes.md, in page order; a sample that is an excerpt is wrapped in a function.
module Lifetimes

open Fable.Core.TS
open global.Ranvier
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Sugar
open Counter

let mountSynchronous (graph: Graph) (host: Dom.HTMLElement) (report: exn list -> unit) =
    let mounted =
        Mounting.mount graph None report host None (fun context ->
            Program.run (Component.render counter { Start = 0; Label = "Clicks" }) context.Backend)
    mounted

let mountQueued (graph: Graph) (host: Dom.HTMLElement) (report: exn list -> unit) =
    let mounted =
        Mounting.mountWith (Microtask.mode Scheduling.CollectAndContinue) graph None report host None (fun context ->
            Program.run (Component.render counter { Start = 0; Label = "Clicks" }) context.Backend)
    mounted
