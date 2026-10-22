/// <summary>
/// A Fable plugin that lowers <c>Arms.Match (read, function ...)</c> inside a <c>[&lt;CaseArms&gt;]</c> member into one
/// arm per match target, built once, whose readers re-derive the pattern variables from the instance.
/// </summary>
namespace Arms.Plugin

open Fable
open Fable.AST
open Fable.AST.Fable

[<assembly: ScanForPlugins>]
do ()

/// <summary>The children of a Fable expression and the function that rebuilds it from new children.</summary>
module internal Shape =
    let private valueShape (kind: ValueKind) (r: SourceLocation option) : Expr list * (Expr list -> Expr) =
        match kind with
        | NewArray (ArrayValues values, t, k) -> values, (fun xs -> Value (NewArray (ArrayValues xs, t, k), r))
        | NewArray (ArrayAlloc size, t, k) -> [ size ], (fun xs -> Value (NewArray (ArrayAlloc xs.Head, t, k), r))
        | NewArray (ArrayFrom source, t, k) -> [ source ], (fun xs -> Value (NewArray (ArrayFrom xs.Head, t, k), r))
        | NewTuple (values, s) -> values, (fun xs -> Value (NewTuple (xs, s), r))
        | NewRecord (values, e, g) -> values, (fun xs -> Value (NewRecord (xs, e, g), r))
        | NewAnonymousRecord (values, names, g, s) -> values, (fun xs -> Value (NewAnonymousRecord (xs, names, g, s), r))
        | NewUnion (values, tag, e, g) -> values, (fun xs -> Value (NewUnion (xs, tag, e, g), r))
        | NewOption (Some value, t, s) -> [ value ], (fun xs -> Value (NewOption (Some xs.Head, t, s), r))
        | NewList (Some (head, tail), t) -> [ head; tail ], (fun xs -> Value (NewList (Some (xs[0], xs[1]), t), r))
        | StringTemplate (tag, parts, values) -> values, (fun xs -> Value (StringTemplate (tag, parts, xs), r))
        | _ -> [], (fun _ -> Value (kind, r))

    let private callShape (info: CallInfo) : Expr list * (Expr list -> CallInfo) =
        let this = Option.toList info.ThisArg

        this @ info.Args,
        fun xs ->
            match info.ThisArg with
            | Some _ ->
                { info with
                    ThisArg = Some xs.Head
                    Args = xs.Tail
                }
            | None -> { info with Args = xs }

    /// <summary>The direct children of <c>expr</c> and its rebuilder; a lambda's binder is not a child.</summary>
    let children (expr: Expr) : Expr list * (Expr list -> Expr) =
        match expr with
        | Lambda (arg, body, n) -> [ body ], (fun xs -> Lambda (arg, xs.Head, n))
        | Delegate (args, body, n, tags) -> [ body ], (fun xs -> Delegate (args, xs.Head, n, tags))
        | Let (id, value, body) -> [ value; body ], (fun xs -> Let (id, xs[0], xs[1]))
        | LetRec (bindings, body) ->
            (List.map snd bindings) @ [ body ],
            (fun xs -> LetRec (List.map2 (fun (id, _) x -> id, x) bindings (List.take bindings.Length xs), List.last xs))
        | Call (callee, info, t, r) ->
            let args, rebuild = callShape info
            callee :: args, (fun xs -> Call (xs.Head, rebuild xs.Tail, t, r))
        | Emit (info, t, r) ->
            let args, rebuild = callShape info.CallInfo
            args, (fun xs -> Emit ({ info with CallInfo = rebuild xs }, t, r))
        | CurriedApply (f, args, t, r) -> f :: args, (fun xs -> CurriedApply (xs.Head, xs.Tail, t, r))
        | IfThenElse (c, a, b, r) -> [ c; a; b ], (fun xs -> IfThenElse (xs[0], xs[1], xs[2], r))
        | DecisionTree (tree, targets) ->
            tree :: List.map snd targets, (fun xs -> DecisionTree (xs.Head, List.map2 (fun (ids, _) x -> ids, x) targets xs.Tail))
        | DecisionTreeSuccess (index, values, t) -> values, (fun xs -> DecisionTreeSuccess (index, xs, t))
        | Sequential xs -> xs, Sequential
        | Get (x, ExprGet key, t, r) -> [ x; key ], (fun xs -> Get (xs[0], ExprGet xs[1], t, r))
        | Get (x, kind, t, r) -> [ x ], (fun xs -> Get (xs.Head, kind, t, r))
        | Set (x, kind, t, value, r) -> [ x; value ], (fun xs -> Set (xs[0], kind, t, xs[1], r))
        | Operation (Unary (op, a), tags, t, r) -> [ a ], (fun xs -> Operation (Unary (op, xs[0]), tags, t, r))
        | Operation (Binary (op, a, b), tags, t, r) -> [ a; b ], (fun xs -> Operation (Binary (op, xs[0], xs[1]), tags, t, r))
        | Operation (Logical (op, a, b), tags, t, r) -> [ a; b ], (fun xs -> Operation (Logical (op, xs[0], xs[1]), tags, t, r))
        | Test (x, kind, r) -> [ x ], (fun xs -> Test (xs.Head, kind, r))
        | TypeCast (x, t) -> [ x ], (fun xs -> TypeCast (xs.Head, t))
        | Value (kind, r) -> valueShape kind r
        | WhileLoop (c, body, r) -> [ c; body ], (fun xs -> WhileLoop (xs[0], xs[1], r))
        | ForLoop (id, a, b, body, up, r) -> [ a; b; body ], (fun xs -> ForLoop (id, xs[0], xs[1], xs[2], up, r))
        | ObjectExpr (members, t, baseCall) ->
            (List.map (fun (m: ObjectExprMember) -> m.Body) members)
            @ Option.toList baseCall,
            (fun xs ->
                let bodies = List.take members.Length xs
                ObjectExpr (List.map2 (fun (m: ObjectExprMember) b -> { m with Body = b }) members bodies, t, List.tryItem members.Length xs))
        | _ -> [], (fun _ -> expr)

    /// <summary>Rewrites top-down: <c>f</c> returns a replacement, or None to descend into the children.</summary>
    let rec map (f: Expr -> Expr option) (expr: Expr) : Expr =
        match f expr with
        | Some replaced -> replaced
        | None ->
            match children expr with
            | [], _ -> expr
            | xs, rebuild -> rebuild (List.map (map f) xs)

    let rec exists (f: Expr -> bool) (expr: Expr) : bool =
        f expr
        || (fst (children expr) |> List.exists (exists f))

    let refersTo (names: Set<string>) (expr: Expr) =
        exists
            (function
            | IdentExpr id -> names.Contains id.Name
            | _ -> false)
            expr

    let rec firstReference (names: Set<string>) (expr: Expr) : Ident option =
        match expr with
        | IdentExpr id when names.Contains id.Name -> Some id
        | _ ->
            fst (children expr)
            |> List.tryPick (firstReference names)

module internal Lower =
    let ident name t : Ident =
        {
            Name = name
            Type = t
            IsMutable = false
            IsThisArgument = false
            IsCompilerGenerated = true
            Range = None
            IsInlineIfLambda = false
        }

    let int32 (value: int) =
        Value (NumberConstant (NumberValue.Int32 value, NumberInfo.Empty), None)

    /// <summary>Whether <c>value</c> is a field, tuple item or case payload of a controlled identifier.</summary>
    let rec extracts (control: Set<string>) (value: Expr) =
        match value with
        | IdentExpr id -> control.Contains id.Name
        | Get (inner, (UnionField _ | TupleIndex _ | FieldGet _ | OptionValue | ListHead | ListTail), _, _)
        | TypeCast (inner, _) -> extracts control inner
        | _ -> false

    /// <summary>Rebuilds the decision part of <c>tree</c>, replacing leaf <c>i</c> by <c>leaf i bound body</c>.</summary>
    /// <remarks>
    /// A condition that reads the matched value or a pattern variable, and a binding of a payload part, are decision;
    /// anything else is a leaf.
    /// </remarks>
    let mapLeaves (control: Set<string>) (leaf: int -> Ident list -> Expr -> Expr) (tree: Expr) : Expr =
        let mutable next = 0

        let rec walk (control: Set<string>) (bound: Ident list) (expr: Expr) =
            match expr with
            | DecisionTree (decision, targets) ->
                DecisionTree (
                    decision,
                    [
                        for ids, body in targets -> ids, walk (control + set [ for id in ids -> id.Name ]) (bound @ ids) body
                    ]
                )
            | IfThenElse (condition, whenTrue, whenFalse, r) when Shape.refersTo control condition ->
                let whenTrue = walk control bound whenTrue
                IfThenElse (condition, whenTrue, walk control bound whenFalse, r)
            | Let (id, value, body) when extracts control value -> Let (id, value, walk (control.Add id.Name) (bound @ [ id ]) body)
            | body ->
                let index = next
                next <- next + 1
                leaf index bound body

        walk control [] tree

    let mutable private counter = 0

    /// <summary>The read binding and the packed selector and bodies of one site, or None when <c>arms</c> is not a function literal.</summary>
    let site (helper: PluginHelper) (read: Expr) (arms: Expr) (r: SourceLocation option) : (Ident * (Ident * Expr) * Expr) option =
        match arms with
        | Lambda (matched, tree, _) ->
            counter <- counter + 1
            let site = counter
            let readId = ident (sprintf "armsRead_%d" site) read.Type

            let instanceType =
                match read.Type with
                | LambdaType (instance, _) -> instance
                | other -> other

            let control = Set.singleton matched.Name
            let leaves = System.Collections.Generic.List<Ident list * Expr>()

            mapLeaves
                control
                (fun _ bound body ->
                    leaves.Add (bound, body)
                    body)
                tree
            |> ignore

            let cellsId = ident (sprintf "armsCells_%d" site) Any

            let emit macro args =
                Emit (
                    {
                        Macro = macro
                        IsStatement = false
                        CallInfo = CallInfo.Create (args = args)
                    },
                    Any,
                    None
                )

            let selector =
                let pick index (bound: Ident list) _ =
                    let values = [ for id in bound @ [ matched ] -> IdentExpr id ]
                    Value (NewTuple ([ int32 index; Value (NewArray (ArrayValues values, Any, MutableArray), None) ], false), None)

                Lambda (matched, mapLeaves control pick tree, None)

            let rewrite (bound: Ident list, body: Expr) =
                let names = control + set [ for id in bound -> id.Name ]
                let slots = bound @ [ matched ]

                let lowered =
                    body
                    |> Shape.map (fun expr ->
                        match expr with
                        | Lambda (argument, inner, n) when
                            argument.Type = instanceType
                            && Shape.refersTo names inner
                            ->
                            let values = ident (sprintf "armsValues_%d" site) Any

                            let fresh =
                                [ for id in slots -> id, ident (sprintf "%s_arm%d" id.Name site) id.Type ]

                            let inner =
                                inner
                                |> Shape.map (fun e ->
                                    match e with
                                    | IdentExpr id ->
                                        fresh
                                        |> List.tryFind (fun (b, _) -> b.Name = id.Name)
                                        |> Option.map (fun (_, renamed) -> IdentExpr renamed)
                                    | _ -> None)

                            let body =
                                List.foldBack
                                    (fun (position, (_, renamed: Ident)) acc ->
                                        Let (renamed, Get (IdentExpr values, ExprGet (int32 position), renamed.Type, None), acc))
                                    (List.indexed fresh)
                                    inner

                            let current = emit "$0.get($1)()" [ IdentExpr cellsId; IdentExpr argument ]
                            Some (Lambda (argument, Let (values, current, body), n))
                        | _ -> None)

                let outsideReaders =
                    body
                    |> Shape.map (fun expr ->
                        match expr with
                        | Lambda (argument, _, _) when argument.Type = instanceType -> Some (Value (UnitConstant, None))
                        | _ -> None)

                match Shape.firstReference names outsideReaders with
                | Some id ->
                    let shown =
                        if id.Name = matched.Name then
                            "a pattern variable"
                        else
                            "'" + id.DisplayName + "'"

                    let at =
                        match id.Range with
                        | Some _ -> id.Range
                        | None -> r

                    helper.LogError (
                        sprintf
                            "Arms.Match: %s is read outside a reader of the instance; read it inside a lambda over the instance, such as (fun _ -> ...)"
                            shown,
                        ?range = at
                    )
                | None -> ()

                lowered

            let bodies = [ for leaf in leaves -> rewrite leaf ]

            let packed =
                Value (
                    NewTuple (
                        [
                            IdentExpr cellsId
                            selector
                            Value (NewArray (ArrayValues bodies, tree.Type, MutableArray), None)
                        ],
                        false
                    ),
                    None
                )

            Some (readId, (cellsId, emit "new WeakMap()" []), packed)
        | _ -> None


module internal Calls =
    /// <summary>The lowered form of <c>expr</c> when it is a call of <c>Arms.Match</c>.</summary>
    let lower (helper: PluginHelper) (expr: Expr) : Expr option =
        match expr with
        | Call (callee, info, t, r) ->
            let isMatch =
                match info.MemberRef with
                | Some ref -> (helper.GetMember ref).FullName = "Ranvier.Arms.Probe.Arms.Match"
                | None -> false

            match info.Args with
            | [ read; arms ] when isMatch ->
                match Lower.site helper read arms r with
                | Some (readId, (cellsId, cellsInit), packed) ->
                    let call =
                        Call (
                            callee,
                            { info with
                                Args = [ IdentExpr readId; Value (Null arms.Type, None); packed ]
                            },
                            t,
                            r
                        )

                    Some (Let (readId, read, Let (cellsId, cellsInit, call)))
                | None ->
                    helper.LogError ("Arms.Match takes a function literal as its second argument", ?range = r)
                    None
            | _ -> None
        | _ -> None

/// <summary>
/// On a member, lowers every <c>Arms.Match</c> call in its body. On <c>Arms.Match</c> itself, lowers every call of it,
/// so call sites need no attribute.
/// </summary>
type CaseArmsAttribute() =
    inherit MemberDeclarationPluginAttribute()
    override _.FableMinimumVersion = "4.0"

    override _.Transform(helper: PluginHelper, _: File, decl: MemberDecl) =
        { decl with
            Body = decl.Body |> Shape.map (Calls.lower helper)
        }

    override _.TransformCall(helper: PluginHelper, _: MemberFunctionOrValue, expr: Expr) =
        Calls.lower helper expr
        |> Option.defaultValue expr
