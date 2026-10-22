// Spike probe: does a helper TemplateView<'inst> spliced through an adapter child (the payload-direct redesign's
// treatment) keep the server HTML and structural hash of today's direct splice?
// Build experiments/Ranvier.View.Arms in Debug first: dotnet build experiments/Ranvier.View.Arms -c Debug
// Run: dotnet fsi docs/research/arms-match/splice-probe.fsx
#r "nuget: Fable.Core, 5.3.0"
#r "nuget: Xantham.Fable.Core, 0.2.0"
#r "nuget: Xantham.Fable.Core.TS, 0.2.1"
#r "../../../experiments/Ranvier.View.Arms/bin/Debug/net10.0/Ranvier.dll"
#r "../../../experiments/Ranvier.View.Arms/bin/Debug/net10.0/Ranvier.View.dll"

open Ranvier
open Ranvier.View
open Ranvier.View.Sugar

type Load =
    | Idle
    | Loaded of string

let helperView: TemplateView<Signal<Load>> =
    b (Attrs.className "label") {
        fun (s: Signal<Load>) ->
            match s.Value with
            | Loaded t -> t
            | Idle -> ""
    }

// Today: the arm's instance is the enclosing instance, so the helper is the arm's whole view.
let direct =
    Component.define (fun _ (current: unit -> Signal<Load> * obj array) -> fst (current ())) (fun _ -> helperView)

// Redesign: the arm's instance is its values thunk; an adapter child maps it back to the enclosing instance.
let adapter = Component.define (fun _ (s: Signal<Load>) -> s) (fun _ -> helperView)

let adapted =
    Component.define (fun _ (current: unit -> Signal<Load> * obj array) -> fun () -> snd (current ())) (fun _ ->
        render adapter (fun (values: unit -> obj array) -> unbox<Signal<Load>> (values ()).[1]))

let site (definition: Component<unit -> Signal<Load> * obj array>) (redesign: bool) =
    Component.template
        (fun _ (s: Signal<Load>) -> s)
        (div () { matchWith id (fun (s: Signal<Load>) -> arm definition (s, [| box s.Value; box s |])) })

let omit =
    { Server.Options.defaults with
        Policy = Omit
    }

let html (definition: Component<unit -> Signal<Load> * obj array>) =
    use graph = new Graph ()
    let state = graph.Run (fun () -> Api.createSignal (Loaded "ink"))
    Server.render omit (site definition false) state

printfn "hash direct=%s adapted=%s" (structuralHash (direct :> TemplateComponent)) (structuralHash (adapted :> TemplateComponent))
printfn "direct  %s" (html direct)
printfn "adapted %s" (html adapted)
