/// <summary>
/// <c>Arms.Match</c>: a plain F# match over a union, one arm per match target. Under Fable the <c>CaseArms</c> plugin
/// lowers the call; on .NET the argument arrives as a quotation and <c>Quoted.lower</c> applies the same lowering.
/// </summary>
namespace Ranvier.Arms.Probe

open Ranvier.View.Foundation.Template

#if !FABLE_COMPILER
open System
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns
open Microsoft.FSharp.Linq.RuntimeHelpers

/// <summary>The .NET lowering of a quoted arms function.</summary>
module Quoted =
    /// <summary>A pattern variable read outside a reader of the enclosing instance.</summary>
    exception BuildTimeRead of string

    let private refersTo (control: Set<Var>) (expr: Expr) =
        expr.GetFreeVars () |> Seq.exists control.Contains

    /// <summary>Whether <c>value</c> is a field, tuple item or case payload of a controlled variable.</summary>
    let rec private extracts (control: Set<Var>) (value: Expr) =
        match value with
        | Var var -> control.Contains var
        | PropertyGet (Some inner, _, [])
        | TupleGet (inner, _)
        | Coerce (inner, _) -> extracts control inner
        | _ -> false

    /// <summary>Rebuilds the decision part of <c>expr</c>, replacing leaf <c>i</c> by <c>leaf i bound body</c>.</summary>
    /// <remarks>
    /// A condition that reads the matched value or a pattern variable, and a binding of a payload part, are decision;
    /// anything else is a leaf.
    /// </remarks>
    let mapLeaves (control: Set<Var>) (leaf: int -> Var list -> Expr -> Expr) (expr: Expr) : Expr =
        let mutable next = 0

        let rec walk (control: Set<Var>) (bound: Var list) (expr: Expr) =
            match expr with
            | IfThenElse (condition, whenTrue, whenFalse) when refersTo control condition ->
                let whenTrue = walk control bound whenTrue
                Expr.IfThenElse (condition, whenTrue, walk control bound whenFalse)
            | Let (var, value, body) when extracts control value -> Expr.Let (var, value, walk (Set.add var control) (bound @ [ var ]) body)
            | body ->
                let index = next
                next <- next + 1
                leaf index bound body

        walk control [] expr

    let private unboxMethod =
        match <@ unbox<int> null @> with
        | Call (_, info, _) -> info.GetGenericMethodDefinition ()
        | _ -> failwith "unbox"

    let private getMethod =
        match <@ (null: obj array).[0] @> with
        | Call (_, info, _) -> info
        | _ -> failwith "get"

    let private valuesMethod =
        match <@ Cells.values null null @> with
        | Call (_, info, _) -> info
        | _ -> failwith "values"

    /// <summary>Lowers <c>arms</c> into a target selector and one body per target, for the site's <c>cells</c>.</summary>
    /// <remarks>
    /// The selector returns the target index and its bound values: the pattern variables, then the matched value. A
    /// pattern variable read inside a lambda over <c>'inst</c> becomes a read of the arm's bound values; any other read
    /// raises <c>BuildTimeRead</c>.
    /// </remarks>
    let lower (cells: obj) (arms: Expr<'u -> TemplateView<'inst>>) : ('u -> int * obj array) * TemplateView<'inst> array =
        let matched, tree =
            match arms with
            | Lambda (matched, tree) -> matched, tree
            | other -> failwithf "Arms.Match takes a function literal; got %A" other

        let control = Set.singleton matched
        let leaves = ResizeArray<Var list * Expr>()

        mapLeaves
            control
            (fun _ bound body ->
                leaves.Add (bound, body)
                body)
            tree
        |> ignore

        let boxed (var: Var) =
            Expr.Coerce (Expr.Var var, typeof<obj>)

        let selector =
            Expr.Lambda (
                matched,
                mapLeaves
                    control
                    (fun index bound _ ->
                        Expr.NewTuple
                            [
                                Expr.Value index
                                Expr.NewArray (typeof<obj>, [ for var in bound @ [ matched ] -> boxed var ])
                            ])
                    tree
            )

        let rewrite (bound: Var list, body: Expr) =
            let slots = bound @ [ matched ]
            let slotSet = Set.ofList slots

            let rec visit (expr: Expr) =
                match expr with
                | Lambda (argument, inner) when
                    argument.Type = typeof<'inst>
                    && refersTo slotSet inner
                    ->
                    let values = Var ("values", typeof<obj array>)

                    let substituted =
                        inner.Substitute (fun var ->
                            match List.tryFindIndex ((=) var) slots with
                            | Some field ->
                                Some (
                                    Expr.Call (
                                        unboxMethod.MakeGenericMethod var.Type,
                                        [ Expr.Call (getMethod, [ Expr.Var values; Expr.Value field ]) ]
                                    )
                                )
                            | None -> None)

                    let current =
                        Expr.Call (valuesMethod, [ Expr.Value cells; Expr.Coerce (Expr.Var argument, typeof<obj>) ])

                    Expr.Lambda (argument, Expr.Let (values, current, substituted))
                | ExprShape.ShapeVar _ -> expr
                | ExprShape.ShapeLambda (var, inner) -> Expr.Lambda (var, visit inner)
                | ExprShape.ShapeCombination (shape, args) -> ExprShape.RebuildShapeCombination (shape, List.map visit args)

            let lowered = visit body

            match
                lowered.GetFreeVars ()
                |> Seq.tryFind slotSet.Contains
            with
            | Some var -> raise (BuildTimeRead var.Name)
            | None -> lowered

        let bodies =
            [|
                for leaf in leaves ->
                    rewrite leaf
                    |> LeafExpressionConverter.EvaluateQuotation
                    |> unbox<TemplateView<'inst>>
            |]

        LeafExpressionConverter.EvaluateQuotation selector
        |> unbox<'u -> int * obj array>,
        bodies
#endif

/// <summary>Arm sites written as a plain F# match.</summary>
[<AbstractClass; Sealed>]
type Arms =
#if FABLE_COMPILER
    /// <summary>One arm per match target of <c>arms</c>; the <c>CaseArms</c> plugin supplies <c>lowered</c>.</summary>
    [<Arms.Plugin.CaseArms>]
    static member Match(read: 'inst -> 'u, arms: 'u -> TemplateView<'inst>, ?lowered: obj) : TemplateView<'inst> =
        match lowered with
        | Some packed ->
            let cells, select, bodies =
                unbox<obj * ('u -> int * obj array) * TemplateView<'inst> array> packed

            Lowered.site cells read select bodies
        | None -> failwith "Arms.Match needs the CaseArms plugin on the enclosing member"
#else
    /// <summary>One arm per match target of <c>arms</c>, lowered from its quotation when the site is built.</summary>
    static member Match(read: 'inst -> 'u, [<ReflectedDefinition>] arms: Expr<'u -> TemplateView<'inst>>) : TemplateView<'inst> =
        let cells = Cells.create ()
        let select, bodies = Quoted.lower cells arms
        Lowered.site cells read select bodies
#endif
