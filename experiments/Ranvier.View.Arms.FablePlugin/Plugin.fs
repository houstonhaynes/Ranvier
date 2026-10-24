namespace Ranvier.View.Arms.FablePlugin

open Fable
open Fable.AST.Fable

[<assembly: ScanForPlugins>]
do ()

module internal Calls =
    let private isMatch (helper: PluginHelper) (info: CallInfo) =
        match info.MemberRef with
        | Some ref -> (helper.GetMember ref).FullName = "Ranvier.View.Arms.Arms.Match"
        | None -> false

    /// <summary>The lowered form of a call of <c>Arms.Match</c>, or None to keep the call.</summary>
    let lower (helper: PluginHelper) (expr: Expr) : Expr option =
        match expr with
        | Call (callee, info, t, r) when isMatch helper info ->
            match info.Args with
            | [ read; arms ] ->
                // A splice is a call of Match with the instance mapper and the view, and no lowered argument.
                let splice (mapper: Expr) (view: Expr) =
                    Call (
                        callee,
                        { info with
                            Args = [ mapper; view; Value (Null Any, None) ]
                        },
                        view.Type,
                        view.Range
                    )

                // A call of a function of Site.fs, beside Arms.fs, of the given result type; None when Arms.Match is not
                // imported.
                let siteCall =
                    match callee with
                    | Import (import, _, _) ->
                        let site =
                            import.Path.Substring (0, import.Path.LastIndexOf '/' + 1)
                            + "Site.fs"

                        Some (fun (selector: string) (args: Expr list) (t: Type) (range: Fable.AST.SourceLocation option) ->
                            Call (
                                Import (
                                    {
                                        Selector = selector
                                        Path = site
                                        Kind = UserImport false
                                    },
                                    Any,
                                    None
                                ),
                                CallInfo.Create (args = args),
                                t,
                                range
                            ))
                    | _ -> None

                // The values array of a thunk: a call of Site.values with the site's tag (AM006).
                let values (thunk: Expr) (tag: Expr) (range: Fable.AST.SourceLocation option) =
                    match siteCall with
                    | Some call -> call "values" [ thunk; tag ] Any range
                    | None ->
                        Emit (
                            {
                                Macro = "$0()"
                                IsStatement = false
                                CallInfo = CallInfo.Create (args = [ thunk ])
                            },
                            Any,
                            range
                        )

                Lower.site helper splice values siteCall read arms r
                |> Option.map (fun packed ->
                    Call (
                        callee,
                        { info with
                            Args = [ read; Value (Null Any, None); packed ]
                        },
                        t,
                        r
                    ))
            | _ -> None
        | _ -> None

/// <summary>Placed on <c>Arms.Match</c>: Fable lowers every call of it; the attribute is required only on the member.</summary>
type CaseArmsAttribute() =
    inherit MemberDeclarationPluginAttribute()
    override _.FableMinimumVersion = "5.0"

    override _.Transform(_: PluginHelper, _: File, decl: MemberDecl) = decl

    override _.TransformCall(helper: PluginHelper, _: MemberFunctionOrValue, expr: Expr) =
        Calls.lower helper expr
        |> Option.defaultValue expr
