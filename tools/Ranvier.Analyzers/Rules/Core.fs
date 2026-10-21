/// RAN010-RAN015: core graph traps.
module Ranvier.Analyzers.Rules.Core

open FSharp.Analyzers.SDK
open FSharp.Compiler.Symbols
open FSharp.Compiler.Symbols.FSharpExprPatterns
open FSharp.Compiler.Text
open Ranvier.Analyzers
open Ranvier.Analyzers.Index
open Ranvier.Analyzers.Rule
open Ranvier.Analyzers.Symbols.Core

/// Where the node is created and what the failure is, then the fix, for a pure context of `kind`.
let private failure (kind: Pure) (context: string) (owning: string option) : string * string =
    match kind, owning with
    | Pure.Memo, Some owner ->
        $"`{context}`'s body, which fails every read of the memo",
        $"Use `{owner}` for a memo that owns the nodes its body creates, or create the node outside the memo."
    | Pure.Memo, None ->
        $"`{context}`'s body, which fails every read of the memo", "Pass `true` as the owning argument, or create the node outside the memo."
    | Pure.Async, Some owner ->
        $"`{context}`'s compute, which fails every read of the async value",
        $"Use `{owner}` for an async value that owns the nodes its body creates, or create the node outside the async value."
    | Pure.Async, None ->
        $"`{context}`'s compute, which fails every read of the async value",
        "Pass `true` as the owning argument, or create the node outside the async value."
    | Pure.EffectOn, _ -> $"`{context}`'s compute, which fails the effect", "Create the node in act, which owns it."
    | Pure.Map, _ ->
        "a projection's map, which fails the row's reads",
        "Use createProjectionWith, or createIndexProjectionWith, and create the node in the factory, which runs once per key."
    | Pure.Reader, _ -> "a projection row's reader, which fails the row's reads", "Move the creation into the factory body, which runs once per key."
    | Pure.LookupF, _ -> "a lookup's f, which fails the key's reads", "Create the node outside the lookup."
    | Pure.LookupAffected, _ -> "a lookup's affected, which fails every live key", "Create the node outside the lookup."
    | Pure.Seed, _ -> $"`{context}`'s seed, which fails every read of the editable", "Create the node outside the editable."
    | Pure.Aggregate, _ -> "a projection aggregate's add, subtract or folder, which fails the aggregate", "Create the node outside the aggregate."

/// The range of `name` at the start of the call at `range`, when the call's text begins with a qualified `name`.
let private calleeName (input: Input) (range: range) (name: string) : range option =
    let callText = text input range
    let at = callText.IndexOf name

    if
        at >= 0
        && callText.Substring (0, at)
           |> Seq.forall (fun c ->
               System.Char.IsLetterOrDigit c
               || c = '.'
               || c = '_')
    then
        let start = Position.mkPos range.StartLine (range.StartColumn + at)
        Some (Range.mkRange range.FileName start (Position.mkPos range.StartLine (range.StartColumn + at + name.Length)))
    else
        None

/// The pure context whose body holds the expression at `path`, and the context's callee. The search stops at the
/// first argument of another call reached through a lambda.
let private pureContextOf (path: Step list) : (FSharpMemberOrFunctionOrValue * PureContext) option =
    let rec go (lambdas: int) (steps: Step list) =
        match Index.lambdaStep isTransparent steps, steps with
        | Some (added, rest), _ -> go (lambdas + added) rest
        | None, [] -> None
        | None, Step.Argument (callee, index, count) :: rest ->
            match pureContext callee index count with
            | Some purity when purity.Layers = lambdas -> Some (callee, purity)
            | Some _ -> None
            | None when lambdas = 0 -> go lambdas rest
            | None -> None
        | None, _ :: rest -> go lambdas rest

    go 0 path

let private ownedInPure (input: Input) : Message list =
    [
        for site in input.Index.Calls do
            if isCreator site.Callee then
                let context = pureContextOf site.Path

                match context with
                | Some (callee, purity) ->
                    let contextName = Symbols.display callee
                    let where, fix = failure purity.Kind contextName purity.Owning

                    let fixes =
                        match purity.Owning with
                        | Some owner ->
                            // The innermost call of the context's callee around the creator: a memo can nest in a memo.
                            let contextSite =
                                input.Index.Calls
                                |> Array.filter (fun candidate ->
                                    candidate.Callee = callee
                                    && Range.rangeContainsRange candidate.Range site.Range)
                                |> Array.sortBy (fun candidate ->
                                    candidate.Range.EndLine
                                    - candidate.Range.StartLine,
                                    candidate.Range.EndColumn
                                    - candidate.Range.StartColumn)
                                |> Array.tryHead

                            match
                                contextSite
                                |> Option.bind (fun s -> calleeName input s.Range callee.LogicalName)
                            with
                            | Some nameRange -> [ replace nameRange callee.LogicalName owner ]
                            | None -> []
                        | None -> []

                    message "RAN010" Severity.Error site.Range $"`{Symbols.display site.Callee}` creates an owned node inside {where}. {fix}" fixes
                | None -> ()
    ]

let ran010: Rule =
    {
        Code = "RAN010"
        Title = "Owned node created in a pure derivation"
        Gate = Symbols.Assemblies.Ranvier
        Run = ownedInPure
    }

let private recreated (input: Input) : Message list =
    // The paths of the `let`s whose bound expression creates an async value, keyed by the bound value.
    let asyncLets =
        input.Index.Calls
        |> Array.choose (fun call ->
            match call.Path with
            | Step.Bound value :: rest when isAsyncCreator call.Callee -> Some (value, rest)
            | _ -> None)

    let reads =
        input.Index.Calls
        |> Array.filter (fun call -> isReactiveRead call.Callee)

    [
        for site in input.Index.Lets do
            let created =
                asyncLets
                |> Array.exists (fun (value, rest) ->
                    value = site.Value
                    && obj.ReferenceEquals (rest, site.Path))

            let owner =
                if created then
                    Index.arguments isTransparent site.Path
                    |> List.tryHead
                    |> Option.bind (fun (callee, index, count, lambdas) ->
                        match ownerBody callee index count with
                        | Some layers when layers = lambdas -> Some callee
                        | _ -> None)
                else
                    None

            match owner with
            | Some callee ->
                let readInBody =
                    reads
                    |> Array.exists (fun call ->
                        (match call.Receiver with
                         | Some (Value value) -> value = site.Value
                         | _ -> false)
                        && (match Index.within site.Path call.Path with
                            | Some inner -> Index.lambdaCount isTransparent inner = 0
                            | None -> false))

                if readInBody then
                    let name = site.Value.DisplayName
                    let ownerName = Symbols.display callee

                    message
                        "RAN011"
                        Severity.Warning
                        site.Value.DeclarationLocation
                        $"`{name}` is created inside `{ownerName}`'s body and read there; each run of the body disposes `{name}` and starts a new flight. Create `{name}` outside `{ownerName}` and read it inside."
                        []
            | None -> ()
    ]

let ran011: Rule =
    {
        Code = "RAN011"
        Title = "Async value created and read in the owner that replaces it"
        Gate = Symbols.Assemblies.Ranvier
        Run = recreated
    }

/// Whether the computation `expr` awaits: a bind on its own statements, or in the lambdas of its control members.
let rec private containsAwait (expr: FSharpExpr) : bool =
    match expr with
    | Call (_, m, _, _, _) when isBind m -> true
    | Call (_, m, _, _, arguments) when isControl m -> List.exists containsAwait arguments
    | Lambda (_, body)
    | Let (_, body)
    | Sequential (_, body)
    | Coerce (_, body) -> containsAwait body
    | IfThenElse (_, whenTrue, whenFalse) -> containsAwait whenTrue || containsAwait whenFalse
    | DecisionTree (_, targets) -> targets |> List.exists (snd >> containsAwait)
    | TryWith (body, _, _, _, handler, _, _) -> containsAwait body || containsAwait handler
    | TryFinally (body, cleanup, _, _) -> containsAwait body || containsAwait cleanup
    | _ -> false

/// Whether argument `index` of `callee` is a tracked body: a pure context, an owner body, or `untrack`'s body.
let private trackedBody (callee: FSharpMemberOrFunctionOrValue) (index: int) (count: int) : bool =
    (pureContext callee index count).IsSome
    || (ownerBody callee index count).IsSome
    || isUntrack callee

/// Whether the read at `site` runs after an await inside the compute of an async value's creator: in a bind's
/// continuation, or after a `Combine` whose first part awaits. A read in a tracked body that the continuation creates,
/// or under `untrack`, is outside the async value's tracking.
let private afterAwait (input: Input) (site: CallSite) : bool =
    // The `Combine` call at `outer`, the path of the call itself, holds the read in its second argument.
    let awaitsBefore (combine: FSharpMemberOrFunctionOrValue) (outer: Step list) =
        input.Index.Calls
        |> Array.exists (fun call ->
            call.Callee = combine
            && obj.ReferenceEquals (call.Path, outer)
            && call.Arguments.Length = 2
            && containsAwait call.Arguments[0])

    // The loop call at `outer` holds the read, and an await in the loop precedes the read's next run.
    let loopAwaits (loop: FSharpMemberOrFunctionOrValue) (outer: Step list) =
        input.Index.Calls
        |> Array.exists (fun call ->
            call.Callee = loop
            && obj.ReferenceEquals (call.Path, outer)
            && List.exists containsAwait call.Arguments)

    let rec go (awaited: bool) (steps: Step list) =
        match steps with
        | [] -> false
        | Step.Lambda (_, false) :: (Step.Argument (callee, index, count) :: _ as rest) when isBind callee && index = count - 1 -> go true rest
        | Step.Lambda (_, false) :: Step.Argument (callee, index, count) :: _ when
            asyncComputeIndex callee count <> Some index
            && trackedBody callee index count
            ->
            false
        | Step.Argument (callee, 1, 2) :: rest when isCombine callee -> go (awaited || awaitsBefore callee rest) rest
        | Step.Argument (callee, _, _) :: rest when isLoop callee -> go (awaited || loopAwaits callee rest) rest
        | Step.Argument (callee, index, count) :: rest ->
            match asyncComputeIndex callee count with
            | Some compute when compute = index -> awaited
            | _ -> go awaited rest
        | _ :: rest -> go awaited rest

    go false site.Path

let private untrackedReads (input: Input) : Message list =
    [
        for site in input.Index.Calls do
            match site.Receiver with
            | Some receiver when
                isReactiveRead site.Callee
                && afterAwait input site
                ->
                let read = text input site.Range
                let source = text input receiver.Range

                message
                    "RAN012"
                    Severity.Warning
                    site.Range
                    $"`{read}` is read after the first `let!`/`do!` of this async memo; a read after an await that suspends is untracked, and a change to `{source}` starts no flight. Read it before the first await and bind it to a local."
                    []
            | _ -> ()
    ]

let ran012: Rule =
    {
        Code = "RAN012"
        Title = "Reactive read after the first await of an async memo"
        Gate = Symbols.Assemblies.Ranvier
        Run = untrackedReads
    }

/// Whether the expression at `path` runs in a delegate handed to a thread starter, with no `Graph.Dispatch` between.
let private offThread (path: Step list) : bool =
    let rec go (insideLambda: bool) (steps: Step list) =
        match steps with
        | [] -> false
        | Step.Lambda (_, false) :: rest -> go true rest
        | Step.Argument (callee, _, _) :: _ when isDispatch callee -> false
        | Step.Argument (callee, _, _) :: _ when insideLambda && threadStarter callee -> true
        | _ :: rest -> go insideLambda rest

    go false path

/// The value a graph write targets: the signal or graph receiver, or `Signal.update`'s signal.
let private writeTarget (site: CallSite) : FSharpMemberOrFunctionOrValue option =
    match site.Receiver, site.Arguments with
    | Some (Value target), _ -> Some target
    | None, Value target :: _ -> Some target
    | _ -> None

let private offThreadWrites (input: Input) : Message list =
    let starters =
        input.Index.Calls
        |> Array.filter (fun call -> threadStarter call.Callee)

    let within (outer: range) (inner: range) =
        Range.rangeContainsRange outer inner

    let innermostStarter (site: CallSite) =
        starters
        |> Array.filter (fun starter -> within starter.Range site.Range)
        |> Array.sortBy (fun starter ->
            starter.Range.EndLine - starter.Range.StartLine,
            starter.Range.EndColumn
            - starter.Range.StartColumn)
        |> Array.tryHead

    // A graph that the innermost starter's delegate constructs, a node it constructs over that graph, and a write in
    // that graph's `Run`, belong to the delegate's thread.
    let ownedByThread (site: CallSite) =
        match innermostStarter site, writeTarget site with
        | Some starter, Some target ->
            let lets =
                input.Index.Lets
                |> Array.filter (fun binding -> within starter.Range binding.Value.DeclarationLocation)

            let graphs =
                lets
                |> Array.choose (fun binding ->
                    match binding.Bound with
                    | NewObject (constructor, _, _) when isGraphConstructor constructor -> Some binding.Value
                    | _ -> None)

            let overGraph (expr: FSharpExpr) =
                match expr with
                | NewObject (constructor, _, Value graph :: _) when isNodeConstructor constructor -> graphs |> Array.contains graph
                | Call (Some (Value graph), m, _, _, _)
                | Call (None, m, _, _, Value graph :: _) when isGraphRun m -> graphs |> Array.contains graph
                | _ -> false

            let insideRun =
                input.Index.Calls
                |> Array.exists (fun call ->
                    isGraphRun call.Callee
                    && within call.Range site.Range
                    && (match call.Receiver, call.Arguments with
                        | Some (Value graph), _
                        | None, Value graph :: _ -> graphs |> Array.contains graph
                        | _ -> false))

            graphs |> Array.contains target
            || insideRun
            || lets
               |> Array.exists (fun binding -> binding.Value = target && overGraph binding.Bound)
        | _ -> false

    [
        for site in input.Index.Calls do
            if
                isGraphWrite site.Callee
                && offThread site.Path
                && not (ownedByThread site)
            then
                message
                    "RAN013"
                    Severity.Warning
                    site.Range
                    $"`{text input site.Range}` runs on a thread-pool thread; a write off the graph's owning thread raises under `ThreadAffinity.Guarded`. Hand the write to `graph.Dispatch (fun () -> ...)`."
                    []
    ]

let ran013: Rule =
    {
        Code = "RAN013"
        Title = "Signal write or Pump on a thread-pool callback"
        Gate = Symbols.Assemblies.Ranvier
        Run = offThreadWrites
    }

let private ignoredTokens (input: Input) : Message list =
    [
        for site in input.Index.Calls do
            let policyForm = isPolicyCreator site.Callee

            match site.Arguments with
            | [ NewUnionCase (_, case, []); Lambda (_, Lambda (token, body)) ] when
                policyForm
                && isCancelPrevious case
                && (token.DisplayName.StartsWith ("_", System.StringComparison.Ordinal)
                    || (Index.uses token body).IsEmpty)
                ->
                message
                    "RAN015"
                    Severity.Hint
                    token.DeclarationLocation
                    "This async memo's body ignores its CancellationToken, so `CancelPrevious` cancels nothing and acts as `KeepLatest`. Pass the token to the awaited work, or use `KeepLatest`."
                    []
            | _ -> ()
    ]

let ran015: Rule =
    {
        Code = "RAN015"
        Title = "CancelPrevious with a body that ignores its token"
        Gate = Symbols.Assemblies.Ranvier
        Run = ignoredTokens
    }
