/// <summary>The children of a Fable expression and the function that rebuilds it from new children.</summary>
module internal Ranvier.View.Arms.FablePlugin.Shape

open Fable.AST
open Fable.AST.Fable

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
    Option.toList info.ThisArg @ info.Args,
    fun xs ->
        match info.ThisArg with
        | Some _ ->
            { info with
                ThisArg = Some xs.Head
                Args = xs.Tail
            }
        | None -> { info with Args = xs }

/// <summary>The direct subexpressions of an expression and its rebuilder.</summary>
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
    | Set (x, ExprSet key, t, value, r) -> [ x; key; value ], (fun xs -> Set (xs[0], ExprSet xs[1], t, xs[2], r))
    | Set (x, kind, t, value, r) -> [ x; value ], (fun xs -> Set (xs[0], kind, t, xs[1], r))
    | Operation (Unary (op, a), tags, t, r) -> [ a ], (fun xs -> Operation (Unary (op, xs[0]), tags, t, r))
    | Operation (Binary (op, a, b), tags, t, r) -> [ a; b ], (fun xs -> Operation (Binary (op, xs[0], xs[1]), tags, t, r))
    | Operation (Logical (op, a, b), tags, t, r) -> [ a; b ], (fun xs -> Operation (Logical (op, xs[0], xs[1]), tags, t, r))
    | Test (x, kind, r) -> [ x ], (fun xs -> Test (xs.Head, kind, r))
    | TypeCast (x, t) -> [ x ], (fun xs -> TypeCast (xs.Head, t))
    | Value (kind, r) -> valueShape kind r
    | WhileLoop (c, body, r) -> [ c; body ], (fun xs -> WhileLoop (xs[0], xs[1], r))
    | ForLoop (id, a, b, body, up, r) -> [ a; b; body ], (fun xs -> ForLoop (id, xs[0], xs[1], xs[2], up, r))
    | TryCatch (body, catch, finalizer, r) ->
        body
        :: (Option.toList (Option.map snd catch)
            @ Option.toList finalizer),
        (fun xs ->
            let rest = xs.Tail
            TryCatch (xs.Head, Option.map (fun (id, _) -> id, rest.Head) catch, Option.map (fun _ -> List.last rest) finalizer, r))
    | Extended (Throw (Some e, t), r) -> [ e ], (fun xs -> Extended (Throw (Some xs.Head, t), r))
    | Extended (Curry (e, arity), r) -> [ e ], (fun xs -> Extended (Curry (xs.Head, arity), r))
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

/// <summary>Whether the expression refers to one of <c>names</c> outside every lambda and delegate.</summary>
let rec refersOutsideLambdas (names: Set<string>) (expr: Expr) =
    match expr with
    | IdentExpr id -> names.Contains id.Name
    | Lambda _
    | Delegate _ -> false
    | _ ->
        fst (children expr)
        |> List.exists (refersOutsideLambdas names)

let rec firstReference (names: Set<string>) (expr: Expr) : Ident option =
    match expr with
    | IdentExpr id when names.Contains id.Name -> Some id
    | _ ->
        fst (children expr)
        |> List.tryPick (firstReference names)
