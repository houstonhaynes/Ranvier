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
                Lower.site helper read arms r
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
