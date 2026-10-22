/// <summary>The lowering of an Arms.Match call into a target selector and one case body per target.</summary>
module internal Ranvier.View.Arms.FablePlugin.Lower

open Fable
open Fable.AST
open Fable.AST.Fable
open Ranvier.View.Arms

let ident (name: string) (t: Type) : Ident =
    {
        Name = name
        Type = t
        IsMutable = false
        IsThisArgument = false
        IsCompilerGenerated = true
        Range = None
        IsInlineIfLambda = false
    }

let private int32 (value: int) =
    Value (NumberConstant (NumberValue.Int32 value, NumberInfo.Empty), None)

let private emit (macro: string) (args: Expr list) (r: SourceLocation option) =
    Emit (
        {
            Macro = macro
            IsStatement = false
            CallInfo = CallInfo.Create (args = args)
        },
        Any,
        r
    )

/// <summary>An F# rendering of a type, for diagnostics.</summary>
let rec show (helper: PluginHelper) (t: Type) : string =
    match t with
    | Type.Unit -> "unit"
    | Type.Boolean -> "bool"
    | Type.Char -> "char"
    | Type.String -> "string"
    | Type.Number (kind, _) ->
        Diagnostics.numberName (
            match kind with
            | NumberKind.Int8 -> "SByte"
            | NumberKind.UInt8 -> "Byte"
            | NumberKind.BigInt -> "BigInteger"
            | NumberKind.NativeInt -> "IntPtr"
            | NumberKind.UNativeInt -> "UIntPtr"
            | NumberKind.Float32 -> "Single"
            | NumberKind.Float64 -> "Double"
            | other -> string other
        )
    | Type.Option (arg, false) -> atom helper arg + " option"
    | Type.Option (arg, true) -> atom helper arg + " voption"
    | Type.Array (arg, _) -> atom helper arg + " array"
    | Type.List arg -> atom helper arg + " list"
    | Type.Tuple (args, _) ->
        args
        |> List.map (atom helper)
        |> String.concat " * "
    | Type.LambdaType (domain, range) -> atom helper domain + " -> " + show helper range
    | Type.DeclaredType (ref, []) -> (helper.GetEntity ref).DisplayName
    | Type.DeclaredType (ref, args) ->
        (helper.GetEntity ref).DisplayName
        + "<"
        + (args
           |> List.map (show helper)
           |> String.concat ", ")
        + ">"
    | Type.GenericParam (name, _, _) -> "'" + name
    | _ -> "obj"

and private atom (helper: PluginHelper) (t: Type) =
    match t with
    | Type.LambdaType _
    | Type.Tuple _ -> "(" + show helper t + ")"
    | _ -> show helper t

/// <summary>Whether a value of the type is a JavaScript primitive or a .NET value type (AM004).</summary>
let private notObject (helper: PluginHelper) (t: Type) =
    match t with
    | Type.Unit
    | Type.Boolean
    | Type.Char
    | Type.String
    | Type.Number _
    | Type.Option _
    | Type.Tuple (_, true)
    | Type.AnonymousRecordType (_, _, true) -> true
    | Type.DeclaredType (ref, _) ->
        let entity = helper.GetEntity ref
        entity.IsValueType || entity.IsEnum
    | _ -> false

let private raising =
    set
        [
            "Microsoft.FSharp.Core.Operators.raise"
            "Microsoft.FSharp.Core.Operators.failwith"
            "Microsoft.FSharp.Core.Operators.invalidOp"
            "Microsoft.FSharp.Core.Operators.invalidArg"
            "Microsoft.FSharp.Core.Operators.nullArg"
            "Microsoft.FSharp.Core.ExtraTopLevelOperators.failwithf"
        ]

/// <summary>Whether a case body is a raised exception, which builds no definition.</summary>
let rec private isThrow (helper: PluginHelper) (expr: Expr) =
    match expr with
    | Extended (Throw _, _) -> true
    | Call (_, { MemberRef = Some ref }, _, _) -> raising.Contains (helper.GetMember ref).FullName
    | Import (info, _, _) -> info.Selector = "toFail"
    | Let (id, value, _) when id.IsCompilerGenerated -> isThrow helper value
    | Call (f, _, _, _)
    | CurriedApply (f, _, _, _)
    | TypeCast (f, _) -> isThrow helper f
    | _ -> false

/// <summary>The raising body where the selector returns a target: a throw retyped, any other raise followed by null.</summary>
let private raiseAs (t: Type) (body: Expr) =
    match body with
    | Extended (Throw (e, _), r) -> Extended (Throw (e, t), r)
    | _ -> Sequential [ body; Value (Null t, None) ]

/// <summary>Whether <c>value</c> is a field, tuple item or case payload of a controlled identifier.</summary>
let rec private extracts (control: Set<string>) (value: Expr) =
    match value with
    | IdentExpr id -> control.Contains id.Name
    | Get (inner, (UnionField _ | TupleIndex _ | FieldGet _ | OptionValue | ListHead | ListTail), _, _)
    | TypeCast (inner, _) -> extracts control inner
    | _ -> false

/// <summary>
/// Whether a <c>let</c> belongs to the selection: its value refers to a controlled identifier outside every lambda, or,
/// for a compiler-generated identifier, extracts a part of one.
/// </summary>
let private decides (control: Set<string>) (id: Ident) (value: Expr) =
    if id.IsCompilerGenerated then
        extracts control value
    else
        Shape.refersOutsideLambdas control value

/// <summary>Rebuilds the decision part of <c>tree</c>, replacing each leaf by <c>leaf bound body</c> in walk order.</summary>
let mapLeaves (control: Set<string>) (leaf: Ident list -> Expr -> Expr) (tree: Expr) : Expr =
    let rec walk (control: Set<string>) (bound: Ident list) (expr: Expr) =
        match expr with
        | DecisionTree (decision, targets) ->
            DecisionTree (
                decision,
                [
                    for ids, body in targets -> ids, walk (control + set [ for id in ids -> id.Name ]) (bound @ ids) body
                ]
            )
        | IfThenElse (condition, whenTrue, whenFalse, r) when Shape.refersOutsideLambdas control condition ->
            let whenTrue = walk control bound whenTrue
            IfThenElse (condition, whenTrue, walk control bound whenFalse, r)
        | Let (id, value, body) when decides control id value -> Let (id, value, walk (control.Add id.Name) (bound @ [ id ]) body)
        | body -> leaf bound body

    walk control [] tree

/// <summary>The range of a decision node that a wrapper at the root of a leaf hides from the walk (AM007).</summary>
let rec private hidden (control: Set<string>) (expr: Expr) : SourceLocation option option =
    match expr with
    | IfThenElse (condition, _, _, r) when Shape.refersOutsideLambdas control condition -> Some r
    | Let (id, value, _) when decides control id value -> Some id.Range
    | DecisionTree (decision, _) when Shape.refersOutsideLambdas control decision -> Some None
    | TypeCast (inner, _)
    | TryCatch (inner, _, _, _)
    | Let (_, _, inner) -> hidden control inner
    | Sequential xs when not xs.IsEmpty -> hidden control (List.last xs)
    | _ -> None

/// <summary>
/// The suffix of a site's emitted names: the line and column of the first available range, in the order given (the
/// call, its arms, its read).
/// </summary>
let siteName (ranges: SourceLocation option list) : string option =
    ranges
    |> List.tryPick id
    |> Option.map (fun r -> sprintf "L%dC%d" r.start.line r.start.column)

/// <summary>
/// Whether a lambda is the compiler's eta-expansion of a function value, <c>fun x -> f x</c>, which Fable passes for a
/// named arms function (AM003).
/// </summary>
let private isEtaExpansion (expr: Expr) =
    match expr with
    | Lambda (argument, CurriedApply (f, [ IdentExpr passed ], _, _), _)
    | Lambda (argument, Call (f, { Args = [ IdentExpr passed ] }, _, _), _) ->
        argument.IsCompilerGenerated
        && passed.Name = argument.Name
        && not (Shape.refersTo (Set.singleton argument.Name) f)
    | _ -> false

/// <summary>The parameter of a lambda or delegate over the instance type.</summary>
let private readerArgument (instanceType: Type) (expr: Expr) : Ident option =
    match expr with
    | Lambda (argument, _, _) when argument.Type = instanceType -> Some argument
    | Delegate (argument :: _, _, _, _) when argument.Type = instanceType -> Some argument
    | _ -> None

let private bodyOf (expr: Expr) =
    match expr with
    | Lambda (_, body, _)
    | Delegate (_, body, _, _) -> body
    | other -> other

let private withBody (expr: Expr) (body: Expr) =
    match expr with
    | Lambda (argument, _, n) -> Lambda (argument, body, n)
    | Delegate (args, _, n, tags) -> Delegate (args, body, n, tags)
    | other -> other

/// <summary>Whether an identifier is a computation expression's builder binding (<c>builder@</c>).</summary>
let private isBuilder (id: Ident) =
    id.Name.Contains "@" || id.Name.Contains "$0040"

/// <summary>A pattern-variable reference left outside every reader, with the lambda it sits in.</summary>
type private Found =
    {
        Ident: Ident
        Field: (UnionFieldInfo * SourceLocation option) option
        Over: Type option
    }

let rec private find (names: Set<string>) (over: Type option) (expr: Expr) : Found option =
    match expr with
    | Get (IdentExpr id, UnionField info, _, r) when names.Contains id.Name ->
        Some
            {
                Ident = id
                Field = Some (info, r)
                Over = over
            }
    | IdentExpr id when names.Contains id.Name ->
        Some
            {
                Ident = id
                Field = None
                Over = over
            }
    | Lambda (argument, body, _) when argument.Type = Type.Unit || isBuilder argument -> find names over body
    | Lambda (argument, body, _) -> find names (Some argument.Type) body
    | Delegate (argument :: _, body, _, _) -> find names (Some argument.Type) body
    | _ ->
        fst (Shape.children expr)
        |> List.tryPick (find names over)

/// <summary>Logs AM001, AM001b or AM002 for a reference outside every reader.</summary>
let private report (helper: PluginHelper) (matched: Ident) (instanceType: Type) (fallback: SourceLocation option) (found: Found) =
    let at (r: SourceLocation option) =
        match r with
        | Some _ -> r
        | None -> fallback

    let field =
        match found.Field with
        | Some (info, r) when found.Ident.Name = matched.Name ->
            let case =
                (helper.GetEntity info.Entity).UnionCases
                |> List.item info.CaseIndex

            Some ((case.UnionCaseFields |> List.item info.FieldIndex).Name, case.Name, r)
        | _ -> None

    match found.Over, field with
    | Some over, Some (name, _, r) -> helper.LogError (Diagnostics.otherLambda name (show helper over) (show helper instanceType), ?range = at r)
    | Some over, None ->
        helper.LogError (Diagnostics.otherLambda found.Ident.DisplayName (show helper over) (show helper instanceType), ?range = at found.Ident.Range)
    | None, Some (name, case, r) -> helper.LogError (Diagnostics.fieldOutsideReader name case, ?range = at r)
    | None, None -> helper.LogError (Diagnostics.outsideReader found.Ident.DisplayName, ?range = at found.Ident.Range)

/// <summary>The packed function of a call, or None after reporting AM003 or AM004.</summary>
let site (helper: PluginHelper) (read: Expr) (arms: Expr) (r: SourceLocation option) : Expr option =
    let instanceType =
        match read.Type with
        | LambdaType (instance, _) -> instance
        | other -> other

    match arms, siteName [ r; arms.Range; read.Range ] with
    | _ when notObject helper instanceType ->
        helper.LogError (Diagnostics.notObject (show helper instanceType), ?range = r)
        None
    | _ when isEtaExpansion arms ->
        helper.LogError (Diagnostics.notLiteral, ?range = r)
        None
    | Lambda _, None ->
        helper.LogError Diagnostics.noRange
        None
    | Lambda (matched, tree, _), Some name ->
        let control = Set.singleton matched.Name
        let leaves = System.Collections.Generic.List<Ident list * Expr>()

        mapLeaves
            control
            (fun bound body ->
                leaves.Add (bound, body)
                body)
            tree
        |> ignore

        // The target of each leaf; a raising leaf has none.
        let targets =
            leaves
            |> Seq.fold
                (fun (acc, next) (_, body) ->
                    if isThrow helper body then
                        None :: acc, next
                    else
                        Some next :: acc, next + 1)
                ([], 0)
            |> fst
            |> List.rev
            |> Array.ofList

        let valuesFn = ident ("armsValues_" + name) Any

        let selectType =
            Type.Tuple (
                [
                    Type.Number (NumberKind.Int32, NumberInfo.Empty)
                    Type.Array (Any, MutableArray)
                ],
                false
            )

        let selector =
            let position = ref 0

            let pick (bound: Ident list) (body: Expr) =
                let leaf = position.Value
                position.Value <- leaf + 1

                match targets[leaf] with
                | None -> raiseAs selectType body
                | Some target ->
                    let values = [ for id in bound @ [ matched ] -> IdentExpr id ]
                    Value (NewTuple ([ int32 target; Value (NewArray (ArrayValues values, Any, MutableArray), None) ], false), None)

            Lambda (matched, mapLeaves control pick tree, None)

        let rewrite (target: int) (bound: Ident list) (body: Expr) =
            let slots = bound @ [ matched ]
            let names = set [ for id in slots -> id.Name ]

            let lowered =
                body
                |> Shape.map (fun expr ->
                    match readerArgument instanceType expr with
                    | Some argument when Shape.refersTo names (bodyOf expr) ->
                        let inner = bodyOf expr

                        let used =
                            slots
                            |> List.filter (fun id -> Shape.refersTo (Set.singleton id.Name) inner)

                        let fresh = [ for id in used -> id, ident (sprintf "%s_%s" id.Name name) id.Type ]

                        let renamed =
                            inner
                            |> Shape.map (function
                                | IdentExpr id ->
                                    fresh
                                    |> List.tryFind (fun (slot, _) -> slot.Name = id.Name)
                                    |> Option.map (snd >> IdentExpr)
                                | _ -> None)

                        let values = ident ("armsRead_" + name) Any

                        let prologue =
                            List.foldBack
                                (fun (slot: Ident, local: Ident) acc ->
                                    let position =
                                        slots
                                        |> List.findIndex (fun s -> s.Name = slot.Name)

                                    Let (local, Get (IdentExpr values, ExprGet (int32 position), local.Type, None), acc))
                                fresh
                                renamed

                        let range =
                            Shape.firstReference names inner
                            |> Option.bind _.Range

                        let current =
                            emit "$0($1, $2)" [ IdentExpr valuesFn; IdentExpr argument; int32 target ] range

                        Some (withBody expr (Let (values, current, prologue)))
                    | _ -> None)

            match hidden names body with
            | Some range ->
                helper.LogError (
                    Diagnostics.unsupported,
                    ?range =
                        (match range with
                         | Some _ -> range
                         | None -> r)
                )
            | None ->
                let outsideReaders =
                    body
                    |> Shape.map (fun expr ->
                        match readerArgument instanceType expr with
                        | Some _ -> Some (Value (UnitConstant, None))
                        | None -> None)

                match find names None outsideReaders with
                | Some found -> report helper matched instanceType r found
                | None -> ()

            lowered

        let bodies =
            [
                for leaf, (bound, body) in Seq.indexed leaves do
                    match targets[leaf] with
                    | Some target -> yield rewrite target bound body
                    | None -> ()
            ]

        Some (
            Lambda (
                valuesFn,
                Value (
                    NewTuple (
                        [
                            selector
                            Value (NewArray (ArrayValues bodies, tree.Type, MutableArray), None)
                        ],
                        false
                    ),
                    None
                ),
                None
            )
        )
    | _ ->
        helper.LogError (Diagnostics.notLiteral, ?range = r)
        None
