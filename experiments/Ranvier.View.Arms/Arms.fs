namespace Ranvier.View.Arms

open System
open System.Diagnostics.CodeAnalysis
open Ranvier.View.Template
#if !FABLE_COMPILER
open Microsoft.FSharp.Quotations

/// <summary>The instance type of the site lowered by <c>Arms.Prepare</c>.</summary>
type internal Warmup = { Text: string option }
#endif

/// <summary>Arm sites written as a plain F# match: one arm per match target.</summary>
[<AbstractClass; Sealed>]
type Arms =
#if FABLE_COMPILER && RANVIER_ARMS_PLUGIN
    /// <summary>One arm per match target of <c>arms</c>; the Ranvier.View.Arms Fable plugin supplies <c>lowered</c>.</summary>
    [<Ranvier.View.Arms.FablePlugin.CaseArms>]
    static member Match(read: 'inst -> 'u, arms: 'u -> TemplateView<'inst>, ?lowered: obj) : TemplateView<'inst> =
        let lowered = Site.erased lowered

        // The plugin calls Match with no lowered argument to splice a helper view: arms holds the view and read maps the
        // arm's values thunk to the instance.
        if isNull lowered then
            unbox<TemplateView<'inst>>(Site.splice (unbox read) (unbox arms))
        else
            let select, bodies =
                unbox<Func<'inst, 'u, obj array> * TemplateView<unit -> obj array> array> lowered

            Site.direct read select bodies
#else
#if FABLE_COMPILER
    /// <summary>AM005: the project compiled by Fable does not import Ranvier.View.Arms.targets.</summary>
    [<Obsolete(Diagnostics.noPlugin, true)>]
    static member Match(read: 'inst -> 'u, arms: 'u -> TemplateView<'inst>) : TemplateView<'inst> =
        Errors.noPlugin ()
#else
    /// <summary>One arm per match target of <c>arms</c>, lowered from its quotation when the site is built.</summary>
    [<RequiresUnreferencedCode(Diagnostics.reflection); RequiresDynamicCode(Diagnostics.reflection)>]
    static member Match
        (
            read: 'inst -> 'u,
            [<ReflectedDefinition>] arms: Expr<'u -> TemplateView<'inst>>,
            [<System.Runtime.CompilerServices.CallerFilePath>] ?file: string,
            [<System.Runtime.CompilerServices.CallerLineNumber>] ?line: int
        ) : TemplateView<'inst> =
        let at: Quoted.At =
            {
                File = defaultArg file ""
                Line = defaultArg line 0
            }

        Quoted.checkInstance at typeof<'inst>
        let cells = Cells.create ()
        let select, bodies = Quoted.lower cells at arms
        Site.create cells read select bodies
#endif
#endif

    /// <summary>
    /// Lowers, renders and compiles a built-in site on .NET; the app's first <c>Arms.Match</c> then costs its own generic
    /// instantiations (about 10 ms) instead of the quotation machinery's JIT (about 130 ms). Call it at start-up, on the
    /// calling thread or the thread pool; it returns when done. Under Fable it does nothing.
    /// </summary>
#if !FABLE_COMPILER
    [<RequiresUnreferencedCode(Diagnostics.reflection); RequiresDynamicCode(Diagnostics.reflection)>]
#endif
    static member Prepare() : unit =
#if !FABLE_COMPILER
        let site =
            Component.define (fun _ (warmup: Warmup) -> warmup) (fun _ ->
                Arms.Match (
                    (fun (warmup: Warmup) -> warmup.Text),
                    function
                    | Some text -> Ranvier.View.Sugar.Elements.b () { fun _ -> text }
                    | None -> Ranvier.View.Sugar.Elements.span () { "none" }
                ))

        let omit =
            { Ranvier.View.Server.Options.defaults with
                Policy = Ranvier.View.Representation.Omit
            }

        for text in [ Some "a"; None ] do
            Ranvier.View.Server.render omit site { Text = text }
            |> ignore

        Quoted.warmCompile ()
#else
        ()
#endif
