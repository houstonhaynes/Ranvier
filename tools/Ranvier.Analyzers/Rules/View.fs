/// RAN020-RAN025: view layer traps.
module Ranvier.Analyzers.Rules.View

open FSharp.Analyzers.SDK
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open FSharp.Compiler.Symbols.FSharpExprPatterns
open FSharp.Compiler.Text
open Ranvier.Analyzers
open Ranvier.Analyzers.Index
open Ranvier.Analyzers.Rule
open Ranvier.Analyzers.Symbols.View

/// Whether the expression at `path` is lexically inside a region lambda.
let private inRegion (path: Step list) : bool =
    path
    |> List.exists (fun step ->
        match step with
        | Step.Argument (callee, index, _) -> isRegionLambda callee index
        | _ -> false)

[<RequireQualifiedAccess>]
type private BuildSite =
    | Text
    | Part of kind: string
    | RenderOf
    | Structure

let private parameterAt (m: FSharpMemberOrFunctionOrValue) (index: int) : FSharpParameter option =
    m.CurriedParameterGroups
    |> Seq.concat
    |> Seq.tryItem index

/// The full name of `t`'s type definition, through abbreviations such as `string`.
let private typeName (t: FSharpType) : string option =
    let t = t.StripAbbreviations ()

    if t.HasTypeDefinition then
        t.TypeDefinition.TryFullName
    else
        None

let private isString (t: FSharpType) =
    typeName t = Some Symbols.StringType

let private isStatic (t: FSharpType) =
    not (t.StripAbbreviations ()).IsFunctionType
    && typeName t <> Some Source

/// The build-time site that argument `index` of a call of `m` is.
let private buildSite (m: FSharpMemberOrFunctionOrValue) (index: int) : BuildSite option =
    let entity = Symbols.enclosing m

    match parameterAt m index with
    | Some parameter when isTextMember m && isString parameter.Type -> Some BuildSite.Text
    | Some parameter when
        (entity = Attributes || entity = Properties)
        && parameter.Name = Some ValueParameter
        && isStatic parameter.Type
        ->
        Some (BuildSite.Part (if entity = Attributes then "attribute" else "property"))
    | Some _ when isRenderOf m && index = 1 -> Some BuildSite.RenderOf
    | Some parameter when
        isComponentTagConstructor m
        && isStatic parameter.Type
        ->
        Some BuildSite.RenderOf
    | _ when isElementStructure m index -> Some BuildSite.Structure
    | _ -> None

/// The build-time site whose argument holds the expression at `path` outside any lambda, with the callee and index.
let private buildSiteOf (path: Step list) =
    let rec go (steps: Step list) =
        match steps with
        | [] -> None
        | Step.Lambda (_, true) :: rest -> go rest
        | Step.Lambda (_, false) :: (Step.Argument (callee, index, _) :: _ as rest) when isElementBuildLambda callee index -> go rest
        | Step.Lambda (_, false) :: _ -> None
        | Step.Argument (callee, index, _) :: rest ->
            match buildSite callee index with
            | Some site -> Some (callee, index, site)
            | None -> go rest
        | _ :: rest -> go rest

    go path

let private shape (t: FSharpType) =
    (t.StripAbbreviations ()).Format FSharpDisplayContext.Empty

/// Whether `m` has an overload of the same name whose reader (a parameter named `read`) returns `value`'s type.
let private hasReaderOverload (m: FSharpMemberOrFunctionOrValue) (value: FSharpType) =
    match m.DeclaringEntity with
    | Some entity ->
        entity.MembersFunctionsAndValues
        |> Seq.exists (fun candidate ->
            candidate.LogicalName = m.LogicalName
            && candidate.CurriedParameterGroups
               |> Seq.concat
               |> Seq.exists (fun p ->
                   let t = p.Type.StripAbbreviations ()

                   p.Name = Some ReaderParameter
                   && t.IsFunctionType
                   && t.GenericArguments.Count = 2
                   && shape t.GenericArguments[1] = shape value))
    | None -> false

/// The expressions in the return position of `expr`: through `let`, sequencing, records and tuples.
let rec private returned (expr: FSharpExpr) : FSharpExpr list =
    match expr with
    | Let (_, body) -> returned body
    | Sequential (_, last) -> returned last
    | NewRecord (_, fields) -> fields
    | NewAnonRecord (_, fields) -> fields
    | NewTuple (_, items) -> items
    | IfThenElse (_, whenTrue, whenFalse) -> returned whenTrue @ returned whenFalse
    | DecisionTree (_, targets) -> targets |> List.collect (snd >> returned)
    | _ -> [ expr ]

let rec private bodyAfter (layers: int) (expr: FSharpExpr) =
    match expr with
    | Lambda (_, body) when layers > 0 -> bodyAfter (layers - 1) body
    | _ -> expr

let private readCall (expr: FSharpExpr) =
    match expr with
    | Call (Some _, m, _, _, _) when Symbols.Core.isReactiveRead m -> true
    | _ -> false

let private buildTimeReads (input: Input) : Message list =
    let calls = input.Index.Calls

    let structureReads = ResizeArray<CallSite>()

    let staticSites =
        [
            for read in calls do
                if
                    Symbols.Core.isReactiveRead read.Callee
                    && not (inRegion read.Path)
                then
                    match buildSiteOf read.Path with
                    | Some (_, _, BuildSite.Structure) -> structureReads.Add read
                    | Some (callee, index, site) ->
                        let owner =
                            calls
                            |> Array.tryFind (fun call ->
                                call.Callee = callee
                                && index < call.Arguments.Length
                                && Range.rangeContainsRange call.Arguments[index].Range read.Range)

                        match owner with
                        | Some call -> call, index, site, read
                        | None -> ()
                    | None -> ()
        ]
        |> List.distinctBy (fun (call, index, _, _) -> call.Range, index)

    [
        // A read that the element expression's own code makes: a guard, a match or a loop's sequence.
        for read in structureReads do
            message
                "RAN020"
                Severity.Warning
                read.Range
                $"`{text input read.Range}` is read once, when the template is built, so the element's structure never follows it. Read it in a reader, or choose the views with matchWith or forEach."
                []

        for call, index, site, read in staticSites do
            let readText = text input read.Range
            let argument = call.Arguments[index]
            let argumentText = text input argument.Range

            match site with
            | BuildSite.Text ->
                message
                    "RAN020"
                    Severity.Warning
                    read.Range
                    $"`{readText}` is read once, when the template is built, so this text never updates. Yield a reader: `fun _ -> {argumentText}`."
                    [ replace argument.Range argumentText $"fun _ -> {argumentText}" ]
            | BuildSite.Part kind ->
                let fixes =
                    let value = parameterAt call.Callee index |> Option.map _.Type

                    if
                        value
                        |> Option.exists (hasReaderOverload call.Callee)
                    then
                        [ replace argument.Range argumentText $"fun _ -> {argumentText}" ]
                    else
                        []

                message
                    "RAN020"
                    Severity.Warning
                    read.Range
                    $"`{readText}` is read once, when the template is built, so the {kind} never updates. Pass a reader: `fun _ -> {argumentText}`."
                    fixes
            | BuildSite.RenderOf ->
                message
                    "RAN020"
                    Severity.Warning
                    read.Range
                    $"`{readText}` is read once, when the template is built, so the child's props never update. Pass the signal or memo in the props and read it in the child."
                    []
            | BuildSite.Structure -> ()

        for call in calls do
            let entity = Symbols.enclosing call.Callee

            let setup = isSetupDefinition call.Callee
            let renderProps = isRender call.Callee

            if setup && not call.Arguments.IsEmpty then
                for expr in returned (bodyAfter 2 call.Arguments[0]) do
                    if readCall expr then
                        message
                            "RAN020"
                            Severity.Warning
                            expr.Range
                            $"`{text input expr.Range}` is read once per instance, when setup runs untracked, so the instance holds the value at mount. Return the signal or memo itself and read it in the template."
                            []

            let propsReader =
                match call.Arguments with
                | [ _; reader ] when renderProps -> Some reader
                | [ reader ] when isComponentTagConstructor call.Callee -> Some reader
                | _ -> None

            match propsReader with
            | Some (Lambda (parameter, _) as reader) ->
                for expr in returned (bodyAfter 1 reader) do
                    if readCall expr then
                        let readText = text input expr.Range

                        let signal =
                            match expr with
                            | Call (Some receiver, _, _, _, _) -> text input receiver.Range
                            | _ -> readText

                        message
                            "RAN020"
                            Severity.Warning
                            expr.Range
                            $"`render`'s props reader runs once per instance, so the child receives the value of `{readText}` at mount. Pass the signal: `fun {parameter.DisplayName} -> {signal}`."
                            []
            | _ -> ()
    ]

let ran020: Rule =
    {
        Code = "RAN020"
        Title = "Reactive read at template build time"
        Gate = Symbols.Assemblies.View
        Run = buildTimeReads
    }

let rec private subExpressions (expr: FSharpExpr) : FSharpExpr list =
    expr
    :: List.collect subExpressions expr.ImmediateSubExpressions

let private isPrimitive (t: FSharpType) =
    t.HasTypeDefinition
    && (t.TypeDefinition.IsValueType
        || t.TypeDefinition.TryFullName = Some Symbols.StringType)

let private comparedByReference (t: FSharpType) =
    t.HasTypeDefinition
    && (let entity = t.TypeDefinition

        entity.IsClass
        && not entity.IsFSharpRecord
        && not entity.IsFSharpUnion
        && entity.TryFullName <> Some Symbols.StringType
        && not (
            entity.MembersFunctionsAndValues
            |> Seq.exists Symbols.Keys.isEqualsOverride
        ))

/// The key function's body with the bodies of the same-file functions it calls, and the ranges of those functions.
let private keyCode (index: FileIndex) (body: FSharpExpr) : FSharpExpr list * range list =
    let rec go (visited: FSharpMemberOrFunctionOrValue list) (expr: FSharpExpr) =
        let inner = subExpressions expr

        let called =
            inner
            |> List.choose (fun expr ->
                match expr with
                | Call (None, m, _, _, _) when not (List.contains m visited) ->
                    Index.definition index m
                    |> Option.filter (fun definition -> not definition.Parameters.IsEmpty)
                | _ -> None)
            |> List.distinctBy _.Value.DeclarationLocation

        let visited = visited @ (called |> List.map _.Value)

        called
        |> List.fold
            (fun (exprs, ranges) definition ->
                let more, scopes = go visited definition.Body

                exprs @ more,
                definition.Value.DeclarationLocation
                :: definition.Body.Range
                :: ranges
                @ scopes)
            (inner, [])

    go [] body

/// Whether `expr` is a member chain rooted at `value`: `value.A.B`.
let rec private rootedAt (value: FSharpMemberOrFunctionOrValue) (expr: FSharpExpr) : bool =
    match expr with
    | Value v -> v = value
    | Call (Some receiver, _, _, _, _)
    | FSharpFieldGet (Some receiver, _, _) -> rootedAt value receiver
    | Coerce (_, inner) -> rootedAt value inner
    | _ -> false

/// The key function's parameter and body: a lambda, or a same-file function passed by name.
let private keyFunction (index: FileIndex) (key: FSharpExpr) : (FSharpMemberOrFunctionOrValue * FSharpExpr) option =
    match key with
    | Lambda (item, body) -> Some (item, body)
    | Value value ->
        match Index.definition index value with
        | Some {
                   Parameters = [ [ item ] ]
                   Body = body
               } -> Some (item, body)
        | _ -> None
    | _ -> None

let private unstableKeys (input: Input) : Message list =
    [
        for site in input.Index.Calls do
            if isKeyed site.Callee && not site.Arguments.IsEmpty then
                let key = site.Arguments[0]

                match keyFunction input.Index key with
                | Some (item, body) ->
                    let inner, scopes = keyCode input.Index body

                    let local (location: range) =
                        (key.Range :: body.Range :: scopes)
                        |> List.exists (fun scope -> Range.rangeContainsRange scope location)

                    let impure =
                        inner
                        |> List.tryPick (fun expr ->
                            match expr with
                            | Call (_, m, _, _, _)
                            | NewObject (m, _, _) when Symbols.Keys.impureCall m -> Some (text input expr.Range)
                            | ValueSet (v, _) when not (local v.DeclarationLocation) -> Some (text input expr.Range)
                            | _ -> None)

                    let positional =
                        inner
                        |> List.tryPick (fun expr ->
                            match expr with
                            | Call (_, m, _, _, _) when Symbols.Keys.positionalCall m ->
                                // The call as written, up to its arguments: `Array.IndexOf`, `List.findIndex`.
                                let written = text input expr.Range
                                let stop = written.IndexOfAny [| ' '; '(' |]
                                Some (if stop > 0 then written.Substring (0, stop) else written)
                            | _ -> None)

                    let tracked =
                        inner
                        |> List.tryPick (fun expr ->
                            match expr with
                            | Call (Some receiver, m, _, _, _) when Symbols.Core.isReactiveRead m ->
                                Some (text input expr.Range, rootedAt item receiver)
                            | _ -> None)

                    let structuredItem =
                        item.FullType.HasTypeDefinition
                        && (item.FullType.TypeDefinition.IsFSharpRecord
                            || item.FullType.TypeDefinition.IsFSharpUnion)

                    let identity =
                        isForEach site.Callee
                        && structuredItem
                        && (match body with
                            | Value v -> v = item
                            | Call (None, m, _, _, [ Value v ]) -> Symbols.Keys.isIdentity m && v = item
                            | _ -> false)

                    match impure, positional, tracked with
                    | Some call, _, _ ->
                        message
                            "RAN021"
                            Severity.Warning
                            key.Range
                            $"This key changes on every pass (`{call}`), so every row remounts. Key by a value unique to the item, such as an id."
                            []
                    | None, Some call, _ ->
                        message
                            "RAN021"
                            Severity.Warning
                            key.Range
                            $"This key is the item's position (`{call}`), so moving or inserting an item remounts every row after it. Key by a value unique to the item, such as an id."
                            []
                    | None, None, Some (read, true) ->
                        message
                            "RAN021"
                            Severity.Warning
                            key.Range
                            $"This key reads `{read}`, a tracked value of the item, so a change to it re-keys the row. Key by a stable value of the item, such as an id."
                            []
                    | None, None, Some (read, false) ->
                        message
                            "RAN021"
                            Severity.Warning
                            key.Range
                            $"This key reads `{read}`, a tracked value outside the item, so a change to it re-keys the rows. Key by a value of the item."
                            []
                    | None, None, None when identity ->
                        message
                            "RAN021"
                            Severity.Info
                            key.Range
                            "The key is the item itself, so a replaced item gets a new key and its row remounts instead of following the item. Key by an id field."
                            []
                    | None, None, None when
                        comparedByReference body.Type
                        && not (isPrimitive body.Type)
                        ->
                        message
                            "RAN021"
                            Severity.Info
                            key.Range
                            $"The key `{body.Type.Format FSharpDisplayContext.Empty}` is compared by reference, so a rebuilt item gets a new key and its row remounts. Key by a value with structural equality, such as an id."
                            []
                    | _ -> ()
                | None -> ()
    ]

let ran021: Rule =
    {
        Code = "RAN021"
        Title = "Unstable list key"
        Gate = Symbols.Assemblies.View
        Run = unstableKeys
    }

let rec private createsDefinition (expr: FSharpExpr) =
    match expr with
    | Call (_, m, _, _, _) when isDefinitionCreator m -> true
    | _ -> List.exists createsDefinition expr.ImmediateSubExpressions

/// The project's static `Component` getters whose implementations create a definition.
let private isComponentGetter (m: FSharpMemberOrFunctionOrValue) =
    m.IsPropertyGetterMethod
    && not m.IsInstanceMember
    && m.ReturnParameter.Type.HasTypeDefinition
    && m.ReturnParameter.Type.TypeDefinition.TryFullName = Some ComponentType

let private recreatingProperties (project: FSharpCheckProjectResults option) : FSharpMemberOrFunctionOrValue list =

    let rec find (item: FSharpImplementationFileDeclaration) =
        match item with
        | FSharpImplementationFileDeclaration.Entity (_, items) -> List.collect find items
        | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue (value, _, body) when isComponentGetter value && createsDefinition body ->
            [ value ]
        | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue _
        | FSharpImplementationFileDeclaration.InitAction _ -> []

    match project with
    | Some results ->
        results.AssemblyContents.ImplementationFiles
        |> List.collect (fun file -> List.collect find file.Declarations)
    | None -> []

// Each project's recreating getters, computed once per check results.
let private propertiesByProject =
    System.Runtime.CompilerServices.ConditionalWeakTable<FSharpCheckProjectResults, FSharpMemberOrFunctionOrValue list>()

let private projectProperties (project: FSharpCheckProjectResults option) : FSharpMemberOrFunctionOrValue list =
    match project with
    | Some results ->
        propertiesByProject.GetValue (
            results,
            System.Runtime.CompilerServices.ConditionalWeakTable.CreateValueCallback (Some >> recreatingProperties)
        )
    | None -> []

/// Whether `definition` is a function: it takes parameters, or its body is a lambda.
let private isFunction (definition: Definition) =
    not definition.Parameters.IsEmpty
    || (match definition.Body with
        | Lambda _ -> true
        | _ -> false)

/// Whether `expr` creates a definition, directly or through the same-file functions it calls.
let rec private createsThrough (index: FileIndex) (visited: FSharpMemberOrFunctionOrValue list) (expr: FSharpExpr) =
    subExpressions expr
    |> List.exists (fun expr ->
        match expr with
        | Call (_, m, _, _, _) when isDefinitionCreator m -> true
        | Call (None, m, _, _, _) when not (List.contains m visited) ->
            Index.definition index m
            |> Option.exists (fun definition ->
                isFunction definition
                && createsThrough index (m :: visited) definition.Body)
        | _ -> false)

/// The callee as written at `range`, up to its arguments: `Component.template`, `armOf`.
let private writtenCallee (input: Input) (range: range) =
    let written = text input range
    let stop = written.IndexOfAny [| ' '; '('; '\n' |]
    if stop > 0 then written.Substring (0, stop) else written

/// The range of the callee as written at the start of `range`.
let private calleeRange (input: Input) (range: range) : range =
    let callee = writtenCallee input range
    Range.mkRange range.FileName range.Start (Position.mkPos range.StartLine (range.StartColumn + callee.Length))

let private regionDefinitions (input: Input) : Message list =
    let properties = lazy (projectProperties input.Project)

    // The bodies of same-file functions passed by name as a region's lambda, `matchWith reader arms`, which the
    // compiler may pass as `fun x -> arms x`.
    let hoisted =
        input.Index.Calls
        |> Array.choose (fun site ->
            site.Arguments
            |> List.indexed
            |> List.tryPick (fun (index, argument) ->
                match argument with
                | Value value
                | Lambda (_, Call (None, value, _, _, _)) when isRegionLambda site.Callee index ->
                    Index.definition input.Index value
                    |> Option.filter isFunction
                    |> Option.map _.Body.Range
                | _ -> None))

    let perRead (site: CallSite) =
        inRegion site.Path
        || hoisted
           |> Array.exists (fun body -> Range.rangeContainsRange body site.Range)

    [
        for site in input.Index.Calls do
            let cause =
                if not (perRead site) then
                    None
                elif isDefinitionCreator site.Callee then
                    Some (writtenCallee input site.Range)
                elif
                    isComponentGetter site.Callee
                    && properties.Value
                       |> List.exists site.Callee.IsEffectivelySameAs
                then
                    Some (text input site.Range)
                elif
                    site.Receiver.IsNone
                    && Index.definition input.Index site.Callee
                       |> Option.exists (fun definition ->
                           isFunction definition
                           && not (Array.contains definition.Body.Range hoisted)
                           && createsThrough input.Index [ site.Callee ] definition.Body)
                then
                    Some (writtenCallee input site.Range)
                else
                    None

            match cause with
            | Some name ->
                message
                    "RAN022"
                    Severity.Warning
                    site.Range
                    $"`{name}` runs on every read of this region, so the definition is new each time and remounts. Bind the definition with `let` outside the template."
                    []
            | None -> ()
    ]

let ran022: Rule =
    {
        Code = "RAN022"
        Title = "Definition created on every region read"
        Gate = Symbols.Assemblies.View
        Run = regionDefinitions
    }

/// An element written with literal parts: an explicit `t.tag parts children` call or a sugar tag.
[<NoEquality; NoComparison>]
type ElementSite =
    {
        Tag: string
        Parts: FSharpExpr list
        HasChildren: bool
        Range: range
        /// The tags of the literal enclosing elements, nearest first, lower case.
        Ancestors: string list
    }

let rec private items (expr: FSharpExpr) : FSharpExpr list =
    match expr with
    | NewArray (_, elements) -> List.collect items elements
    | Coerce (_, inner) -> items inner
    | _ -> [ expr ]

/// Whether `expr` is a literal array with at least one element.
let rec private literalNonEmpty (expr: FSharpExpr) : bool =
    match expr with
    | NewArray (_, elements) -> not elements.IsEmpty
    | Coerce (_, inner) -> literalNonEmpty inner
    | _ -> false

/// Whether a call of `m` with `arguments` writes an element: a generated tag member of the builder, `t.div parts
/// children`, or `t.br parts` for a void element.
let private isTagMember (m: FSharpMemberOrFunctionOrValue) (arguments: FSharpExpr list) =
    m.IsExtensionMember
    && Symbols.enclosing m = Builder
    && (arguments.Length = 1 || arguments.Length = 2)
    && arguments.Head.Type.IsArrayType

let private sugarTag (m: FSharpMemberOrFunctionOrValue) =
    match m.ApparentEnclosingEntity with
    | Some entity when
        m.IsConstructor
        && (Symbols.enclosing m).StartsWith(Elements + ".", System.StringComparison.Ordinal)
        ->
        Some (entity.DisplayName.Trim('`').TrimEnd ''')
    | _ -> None

/// The literal elements of the file, each with its literal ancestors.
let elements (input: Input) : ElementSite list =
    let calls = input.Index.Calls

    let builders =
        calls
        |> Array.choose (fun site ->
            match sugarTag site.Callee, site.Path with
            | Some tag, Step.Applied parameter :: _ -> Some (parameter, tag)
            | _ -> None)

    let builderTag (parameter: FSharpMemberOrFunctionOrValue) =
        builders
        |> Array.tryPick (fun (b, tag) -> if b = parameter then Some tag else None)

    let ancestors (path: Step list) =
        path
        |> List.choose (fun step ->
            match step with
            | Step.Argument (callee, 1, 2) when
                callee.IsExtensionMember
                && Symbols.enclosing callee = Builder
                ->
                Some callee.LogicalName
            | Step.Lambda (Some parameter, true) -> builderTag parameter
            | _ -> None)
        |> List.map (fun tag -> tag.ToLowerInvariant ())

    [
        for site in calls do
            if isTagMember site.Callee site.Arguments then
                {
                    Tag = site.Callee.LogicalName.ToLowerInvariant ()
                    Parts = items site.Arguments[0]
                    HasChildren =
                        site.Arguments.Length = 2
                        && literalNonEmpty site.Arguments[1]
                    Range = calleeRange input site.Range
                    Ancestors = ancestors site.Path
                }
            else
                match sugarTag site.Callee, site.Path with
                | Some tag, Step.Applied parameter :: outer ->
                    let children =
                        calls
                        |> Array.exists (fun call ->
                            isChildYield call.Callee
                            && (match call.Receiver with
                                | Some (Value receiver) -> receiver = parameter
                                | _ -> false))

                    {
                        Tag = tag.ToLowerInvariant ()
                        Parts = List.collect items site.Arguments
                        HasChildren = children
                        Range = site.Range
                        Ancestors = ancestors outer
                    }
                | _ -> ()
    ]

let private invalidContent (input: Input) : Message list =
    [
        for element in elements input do
            let contents =
                element.Parts
                |> List.choose (fun part ->
                    Symbols.View.contentPart part
                    |> Option.map (fun name -> name, part))

            match contents with
            | (first, _) :: (second, part) :: _ ->
                message
                    "RAN023"
                    Severity.Error
                    part.Range
                    $"This element has two content parts (`{first}` and `{second}`), which fails when the definition is created. Give each content part its own element."
                    []
            | [ name, part ] when element.HasChildren ->
                message
                    "RAN023"
                    Severity.Error
                    part.Range
                    $"This element has a content part (`{name}`) and template children, which fails when the definition is created. Move the children into another element."
                    []
            | [ UnsafeHtmlPart, part ] when
                Symbols.View.voidTags.Contains element.Tag
                || Symbols.View.rawTextTags.Contains element.Tag
                || element.Tag = "template"
                || element.Tag = "textarea"
                || element.Tag = "title"
                ->
                message
                    "RAN023"
                    Severity.Error
                    part.Range
                    $"`Unsafe.html` sets the content of `{element.Tag}`, whose children the HTML parser does not build from markup, which fails when the definition is created. Put the markup in an element such as `div`."
                    []
            | _ -> ()
    ]

let ran023: Rule =
    {
        Code = "RAN023"
        Title = "Invalid content parts on an element"
        Gate = Symbols.Assemblies.View
        Run = invalidContent
    }

let private refused (element: ElementSite) : string option =
    match element.Tag with
    | "script" ->
        Some
            "`script` is refused by every render (\"Script elements are not created by templates or typed views\"). Load scripts through the server document shell, for example `Server.Page` scripts."
    | "template" ->
        Some
            "`template` is refused by every render (\"Template elements are not supported in templates\"). Build the content with a component instead."
    | tag when Symbols.View.rawTextTags.Contains tag ->
        Some
            $"`{tag}` is a raw-text element, which the server render refuses (\"Raw-text elements require an explicit HTML serialization policy\"). Write it outside the template."
    | _ -> None

let private restructured (element: ElementSite) : string option =
    try
        Symbols.View.checkNesting element.Ancestors element.Tag
        None
    with :? System.InvalidOperationException as failure ->
        Some $"{failure.Message}, which the server render refuses at its first render. Nest the elements as the HTML parser builds them."

/// Whether an enclosing literal element is itself restructured.
let private withinRestructured (element: ElementSite) : bool =
    let rec go (ancestors: string list) =
        match ancestors with
        | tag :: outer ->
            restructured
                { element with
                    Tag = tag
                    Ancestors = outer
                }
            |> Option.isSome
            || go outer
        | [] -> false

    go element.Ancestors

let private serverRefusals (input: Input) : Message list =
    [
        for element in elements input do
            match
                refused element,
                (if withinRestructured element then
                     None
                 else
                     restructured element)
            with
            | Some text, _
            | None, Some text -> message "RAN024" Severity.Warning element.Range text []
            | None, None -> ()
    ]

let ran024: Rule =
    {
        Code = "RAN024"
        Title = "Markup the server render refuses"
        Gate = Symbols.Assemblies.View
        Run = serverRefusals
    }

/// Whether `expr` evaluates to a queued commit mode, following the file's lets and its module values and functions.
/// With `every`, each branch must be queued: a function's arguments choose its branch. Without it, one branch
/// suffices.
let rec private queuedMode (index: FileIndex) (depth: int) (every: bool) (expr: FSharpExpr) : bool =
    let follow = queuedMode index (depth + 1) every

    let branches (exprs: FSharpExpr list) =
        if every then
            List.forall follow exprs
        else
            List.exists follow exprs

    depth < 8
    && match expr with
       | NewUnionCase (_, case, _) -> Symbols.View.isQueuedCommits case
       | Call (None, m, _, _, _) when Symbols.View.isQueuedModeFunction m -> true
       | Call (None, m, _, _, _) ->
           Index.definition index m
           |> Option.exists (fun definition -> queuedMode index (depth + 1) (every || not definition.Parameters.IsEmpty) definition.Body)
       | Value value ->
           index.Lets
           |> Array.exists (fun site -> site.Value = value && follow site.Bound)
           || Index.definition index value
              |> Option.exists (fun definition ->
                  definition.Parameters.IsEmpty
                  && follow definition.Body)
       | Coerce (_, inner)
       | Let (_, inner)
       | Sequential (_, inner) -> follow inner
       | IfThenElse (_, whenTrue, whenFalse) -> branches [ whenTrue; whenFalse ]
       | DecisionTree (_, targets) -> branches (List.map snd targets)
       | _ -> false

/// The range of `mode` as written, through the compiler-generated local that a piped call binds it to.
let private written (index: FileIndex) (mode: FSharpExpr) : range =
    match mode with
    | Value value when value.IsCompilerGenerated ->
        index.Lets
        |> Array.tryFind (fun site -> site.Value = value)
        |> Option.map _.Bound.Range
        |> Option.defaultValue mode.Range
    | _ -> mode.Range

let private queuedHydration (input: Input) : Message list =
    [
        for site in input.Index.Calls do
            match site.Arguments with
            | mode :: _ when
                Symbols.View.isHydrateWith site.Callee
                && queuedMode input.Index 0 false mode
                ->
                message
                    "RAN025"
                    Severity.Error
                    (written input.Index mode)
                    "Hydration supports synchronous commits only, so this call raises. Pass `SynchronousCommits`, or call `hydrate`."
                    []
            | _ -> ()
    ]

let ran025: Rule =
    {
        Code = "RAN025"
        Title = "hydrateWith with queued commits"
        Gate = Symbols.Assemblies.Dom
        Run = queuedHydration
    }
