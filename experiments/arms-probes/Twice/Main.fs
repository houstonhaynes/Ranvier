/// <summary>Two mounts of one Arms.Match site with one instance; the first mount after the second is disposed.</summary>
module Twice.Main

open global.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Template
open Ranvier.Arms.Probe.Fixtures
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View.Foundation.Mounting
open Ranvier.View.Foundation.Scheduling

[<Emit("document.createElement('div')")>]
let private host () : HTMLElement = jsNative

let mountOn graph (signal: Signal<Load>) =
    let target = host ()
    let reports = ResizeArray<string>()

    let m =
        mountWith
            SynchronousCommits
            graph
            None
            (fun errs ->
                for e in errs do
                    reports.Add e.Message)
            target
            None
            (fun context -> Representation.Program.run (Component.render matchLoader signal) context.Backend)

    target, m, reports

[<EntryPoint>]
let main _ =
    use graph = new Graph ()
    let signal = graph.Run (fun () -> Api.createSignal (Loaded ("a", 1)))
    let a, ma, ra = mountOn graph signal
    let b, mb, rb = mountOn graph signal
    printfn "both|%s|%s" a.innerHTML b.innerHTML
    graph.Run (fun () -> signal.Value <- Loaded ("b", 2))
    printfn "update|%s|%s" a.innerHTML b.innerHTML
    mb.Dispose ()
    graph.Run (fun () -> signal.Value <- Loaded ("c", 3))
    printfn "afterDisposeB|%s" a.innerHTML
    graph.Run (fun () -> signal.Value <- Loading 5)
    graph.Run (fun () -> signal.Value <- Loaded ("d", 4))
    printfn "afterRemount|%s" a.innerHTML
    ma.Dispose ()
    printfn "reports|%A|%A" (List.ofSeq ra) (List.ofSeq rb)
    0
