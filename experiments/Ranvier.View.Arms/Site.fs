/// <summary>The runtime shape every Arms.Match site lowers to, on both targets.</summary>
module internal Ranvier.View.Arms.Site

open Ranvier.View.Template
open Ranvier.View.Sugar

/// <summary>
/// One arm per body, built when the site is built. The same target keeps its arm and replaces its bound values;
/// another target remounts. A body's instance is the enclosing instance.
/// </summary>
let inline create (cells: Cells.Cells) (read: 'inst -> 'u) (select: 'u -> int * obj array) (bodies: TemplateView<'inst> array) : TemplateView<'inst> =
    let definitions =
        bodies
        |> Array.mapi (fun target body ->
            Component.define
                (fun setup (current: unit -> 'inst * obj array) ->
                    let props = current ()
                    let instance = fst props
#if FABLE_COMPILER
                    Cells.enter cells setup (box instance) target (unbox<unit -> obj * obj array>(box current)) (snd props)
#else
                    Cells.enter cells setup (box instance) target (fun () -> snd (current ())) (snd props)
#endif
                    instance)
                (fun _ -> body))

    matchWith id (fun instance ->
        let target, values = select (read instance)
#if FABLE_COMPILER
        arm (Fable.Core.JsInterop.emitJsExpr (definitions, target) "$0[$1]") (instance, values))
#else
        arm definitions[target] (instance, values))
#endif
