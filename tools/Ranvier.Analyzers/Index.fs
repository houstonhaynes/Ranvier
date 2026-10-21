/// The calls and `let` bindings of an implementation file, each with the steps that lead from it to the body of its
/// declaration. One walk per file serves every rule.
module Ranvier.Analyzers.Index

open System.Collections.Generic
open System.Runtime.CompilerServices
open FSharp.Compiler.Symbols
open FSharp.Compiler.Symbols.FSharpExprPatterns
open FSharp.Compiler.Text

/// One step from an expression to the expression that contains it.
[<RequireQualifiedAccess; NoEquality; NoComparison>]
type Step =
    /// Argument `index` of `count` arguments of a call of `callee`, counted after the receiver.
    | Argument of callee: FSharpMemberOrFunctionOrValue * index: int * count: int
    /// The receiver of a call of `callee`.
    | Receiver of callee: FSharpMemberOrFunctionOrValue
    /// The body of a lambda, or of an object-expression method (no parameter); `immediate` marks a lambda applied
    /// where it is written.
    | Lambda of parameter: FSharpMemberOrFunctionOrValue option * immediate: bool
    /// The argument of an immediately applied lambda over `parameter`.
    | Applied of parameter: FSharpMemberOrFunctionOrValue
    /// The bound expression of a `let` of `value`.
    | Bound of value: FSharpMemberOrFunctionOrValue

/// A call, a constructor call, a curried member call or a pipe into a call, with its arguments after substitution.
[<NoEquality; NoComparison>]
type CallSite =
    {
        Callee: FSharpMemberOrFunctionOrValue
        Receiver: FSharpExpr option
        Arguments: FSharpExpr list
        Range: range
        /// The steps from the call to the body of its declaration, innermost first.
        Path: Step list
        /// The paths whose contents the call's code names unqualified, innermost first: each enclosing module and the
        /// innermost enclosing namespace.
        Opened: string list
    }

/// A `let` binding; `Path` is shared, by reference, by every expression of `Body`.
[<NoEquality; NoComparison>]
type LetSite =
    {
        Value: FSharpMemberOrFunctionOrValue
        Bound: FSharpExpr
        Body: FSharpExpr
        Path: Step list
    }

/// A module-level value or function of the file.
[<NoEquality; NoComparison>]
type Definition =
    {
        Value: FSharpMemberOrFunctionOrValue
        Parameters: FSharpMemberOrFunctionOrValue list list
        Body: FSharpExpr
    }

[<NoEquality; NoComparison>]
type FileIndex =
    {
        Calls: CallSite array
        Lets: LetSite array
        Definitions: Definition array
    }

let rec private lambdas (expr: FSharpExpr) (parameters: FSharpMemberOrFunctionOrValue list) =
    match expr with
    | Lambda (parameter, body) -> lambdas body (parameter :: parameters)
    | _ -> List.rev parameters, expr

/// The call that `fn` applied to `arguments` makes when `fn` is an eta-expanded call: the typed tree writes
/// `t.div parts children` as `(fun parts children -> t.div (parts, children)) parts children`, and `x |> f` as
/// `(fun value -> f value) x`.
let private redex (fn: FSharpExpr) (arguments: FSharpExpr list) =
    match lambdas fn [] with
    | (_ :: _ as parameters), Call (receiver, callee, _, _, callArguments) when parameters.Length = arguments.Length ->
        let position (argument: FSharpExpr) =
            match argument with
            | Value used -> List.tryFindIndex (fun parameter -> parameter = used) parameters
            | _ -> None

        let covered =
            parameters
            |> List.forall (fun parameter ->
                callArguments
                |> List.exists (fun argument ->
                    match argument with
                    | Value used -> used = parameter
                    | _ -> false))

        if covered then
            let substituted =
                callArguments
                |> List.map (fun argument ->
                    match position argument with
                    | Some index -> arguments[index]
                    | None -> argument)

            Some (receiver, callee, substituted)
        else
            None
    | _ -> None

let private isPipe (name: string) (m: FSharpMemberOrFunctionOrValue) =
    m.LogicalName = name
    && Symbols.enclosing m = Symbols.Operators

/// Excludes the compiler-generated `CompareTo`, `Equals` and `GetHashCode`.
let private generatedComparison (value: FSharpMemberOrFunctionOrValue) =
    value.IsCompilerGenerated
    && Symbols.generatedComparisons.Contains value.LogicalName

let private build (file: FSharpImplementationFileContents) : FileIndex =
    let calls = ResizeArray<CallSite>()
    let lets = ResizeArray<LetSite>()
    let definitions = ResizeArray<Definition>()
    let mutable opened: string list = []

    // Lambdas the compiler binds to a generated local before a partial application: `p |> Projection.fold f 0` is
    // `let folder = f in fun upstream -> fold folder 0 upstream`. Each is walked at the argument that uses it.
    let hoisted = Dictionary<FSharpMemberOrFunctionOrValue, FSharpExpr>()

    let rec call path range receiver (callee: FSharpMemberOrFunctionOrValue) (arguments: FSharpExpr list) =
        calls.Add
            {
                Callee = callee
                Receiver = receiver
                Arguments = arguments
                Range = range
                Path = path
                Opened = opened
            }

        receiver
        |> Option.iter (walk (Step.Receiver callee :: path))

        arguments
        |> List.iteri (fun index argument ->
            let step =
                Step.Argument (callee, index, arguments.Length)
                :: path

            match argument with
            | Value value when hoisted.ContainsKey value ->
                let bound = hoisted[value]
                hoisted.Remove value |> ignore
                walk step bound
            | _ -> walk step argument)

    and apply path (expr: FSharpExpr) (fn: FSharpExpr) (arguments: FSharpExpr list) =
        match redex fn arguments with
        | Some (receiver, callee, substituted) -> call path expr.Range receiver callee substituted
        | None ->
            match fn with
            | Lambda (parameter, body) ->
                arguments
                |> List.iter (walk (Step.Applied parameter :: path))

                walk (Step.Lambda (Some parameter, true) :: path) body
            | _ ->
                walk path fn
                arguments |> List.iter (walk path)

    and walk (path: Step list) (expr: FSharpExpr) =
        match expr with
        | Call (None, pipe, _, _, [ argument; fn ]) when isPipe Symbols.PipeRight pipe -> apply path expr fn [ argument ]
        | Call (None, pipe, _, _, [ fn; argument ]) when isPipe Symbols.PipeLeft pipe -> apply path expr fn [ argument ]
        | Call (receiver, callee, _, _, arguments) -> call path expr.Range receiver callee arguments
        | NewObject (constructor, _, arguments) -> call path expr.Range None constructor arguments
        | Application (fn, _, arguments) -> apply path expr fn arguments
        | Lambda (parameter, body) -> walk (Step.Lambda (Some parameter, false) :: path) body
        | Let ((value, bound, _), body) ->
            lets.Add
                {
                    Value = value
                    Bound = bound
                    Body = body
                    Path = path
                }

            match bound with
            | Lambda _ when value.IsCompilerGenerated ->
                hoisted[value] <- bound
                walk path body

                if hoisted.Remove value then
                    walk (Step.Bound value :: path) bound
            | _ ->
                walk (Step.Bound value :: path) bound
                walk path body
        | ObjectExpr (_, baseCall, overrides, interfaces) ->
            walk path baseCall

            for method in overrides @ List.collect snd interfaces do
                walk (Step.Lambda (None, false) :: path) method.Body
        | _ ->
            for inner in expr.ImmediateSubExpressions do
                walk path inner

    let rec declaration (item: FSharpImplementationFileDeclaration) =
        match item with
        | FSharpImplementationFileDeclaration.Entity (entity, items) when entity.IsFSharpModule || entity.IsNamespace ->
            let outer = opened

            opened <-
                if entity.IsNamespace then
                    [ Symbols.pathOf entity ]
                else
                    Symbols.pathOf entity :: opened

            List.iter declaration items
            opened <- outer
        | FSharpImplementationFileDeclaration.Entity (_, items) -> List.iter declaration items
        | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue (value, parameters, body) ->
            if not (generatedComparison value) then
                definitions.Add
                    {
                        Value = value
                        Parameters = parameters
                        Body = body
                    }

                walk [] body
        | FSharpImplementationFileDeclaration.InitAction body -> walk [] body

    List.iter declaration file.Declarations

    {
        Calls = calls.ToArray ()
        Lets = lets.ToArray ()
        Definitions = definitions.ToArray ()
    }

let private cache =
    ConditionalWeakTable<FSharpImplementationFileContents, FileIndex>()

/// The file's definition of `value`, a module-level value or function.
let definition (index: FileIndex) (value: FSharpMemberOrFunctionOrValue) : Definition option =
    index.Definitions
    |> Array.tryFind (fun site -> site.Value = value)

/// The index of `file`, built on first use and shared by every rule.
let ofFile (file: FSharpImplementationFileContents) : FileIndex =
    cache.GetValue (file, ConditionalWeakTable.CreateValueCallback build)

/// The lambdas that the lambda at the head of `steps` adds to a count, and the steps after it; None when the head is
/// not a lambda. An immediately applied lambda, and the lambda argument of a call that `transparent` accepts, add none.
let lambdaStep (transparent: FSharpMemberOrFunctionOrValue -> bool) (steps: Step list) : (int * Step list) option =
    match steps with
    | Step.Lambda (_, true) :: rest -> Some (0, rest)
    | Step.Lambda (_, false) :: Step.Argument (callee, _, _) :: rest when transparent callee -> Some (0, rest)
    | Step.Lambda (_, false) :: rest -> Some (1, rest)
    | _ -> None

/// The call arguments on `path`, innermost first, each with the number of lambdas between it and the expression.
/// Immediately applied lambdas, and the lambda argument of a call that `transparent` accepts, are not counted.
let arguments (transparent: FSharpMemberOrFunctionOrValue -> bool) (path: Step list) : (FSharpMemberOrFunctionOrValue * int * int * int) list =
    let rec go (lambdaCount: int) (steps: Step list) =
        match lambdaStep transparent steps, steps with
        | Some (added, rest), _ -> go (lambdaCount + added) rest
        | None, [] -> []
        | None, Step.Argument (callee, index, count) :: rest ->
            (callee, index, count, lambdaCount)
            :: go lambdaCount rest
        | None, _ :: rest -> go lambdaCount rest

    go 0 path

/// The lambdas on `steps` that `lambdaStep` counts.
let lambdaCount (transparent: FSharpMemberOrFunctionOrValue -> bool) (steps: Step list) : int =
    let rec go (count: int) (steps: Step list) =
        match lambdaStep transparent steps, steps with
        | Some (added, rest), _ -> go (count + added) rest
        | None, [] -> count
        | None, _ :: rest -> go count rest

    go 0 steps

/// The steps of `path` inside `ancestor`, innermost first, when `ancestor` is a tail of `path` by reference.
let rec within (ancestor: Step list) (path: Step list) : Step list option =
    if obj.ReferenceEquals (path, ancestor) then
        Some []
    else
        match path with
        | [] -> None
        | step :: rest ->
            within ancestor rest
            |> Option.map (fun inner -> step :: inner)

/// The ranges of the uses of `value` in `expr`, lambda bodies included.
let rec uses (value: FSharpMemberOrFunctionOrValue) (expr: FSharpExpr) : range list =
    let own =
        match expr with
        | Value used when used = value -> [ expr.Range ]
        | _ -> []

    own
    @ List.collect (uses value) expr.ImmediateSubExpressions
