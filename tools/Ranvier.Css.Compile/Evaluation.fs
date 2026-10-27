namespace Ranvier.Css.Compile

open System
open System.Collections.Generic
open FSharp.Compiler.Symbols
open FSharp.Compiler.Symbols.FSharpExprPatterns
open FSharp.Compiler.Text
open Ranvier.Css

module internal Evaluation =
    type Definition =
        {
            Symbol: FSharpMemberOrFunctionOrValue
            Parameters: FSharpMemberOrFunctionOrValue list
            Body: FSharpExpr
        }

    type Data =
        | Scalar of obj
        | Items of Data list
        | Tuple of Data list
        | Record of Map<string, Data>
        | Closure of (string * Data) list * FSharpMemberOrFunctionOrValue * FSharpExpr
        | Function of Definition * (string * Data) list
        | Css of Metadata.CssValue
        | Declaration of Declaration
        | Nodes of StyleNode<unit> list
        | Builder of string * Data list
        | Unknown of string

    let symbolKey (symbol: FSharpMemberOrFunctionOrValue) =
        if not (String.IsNullOrEmpty symbol.XmlDocSig) then
            symbol.Assembly.SimpleName
            + "|"
            + symbol.XmlDocSig
        else
            let location =
                try
                    let location = symbol.DeclarationLocation

                    string location.StartLine
                    + ":"
                    + string location.StartColumn
                with _ ->
                    "binary"

            symbol.Assembly.SimpleName
            + "|"
            + symbol.FullName
            + "|"
            + location

    let private owner (symbol: FSharpMemberOrFunctionOrValue) =
        let name =
            symbol.DeclaringEntity
            |> Option.map _.FullName
            |> Option.defaultValue ""

        if
            List.contains
                name
                [
                    "Ranvier.Css.DeclarationModule"
                    "Ranvier.Css.SelectorModule"
                    "Ranvier.Css.UtilityModule"
                    "Ranvier.Css.ValueModule"
                    "Ranvier.Css.CustomPropertyModule"
                ]
        then
            name.Substring (0, name.Length - 6)
        else
            name

    let private memberName (symbol: FSharpMemberOrFunctionOrValue) =
        let name =
            if symbol.CompiledName.StartsWith "op_" then
                symbol.CompiledName
            else
                symbol.DisplayName

        if name.StartsWith "get_" then name.Substring 4 else name

    let private error (expression: FSharpExpr) message =
        raise (ExtractionFailure (Diagnostic.at expression.Range message))

    let rec private strip expression =
        match expression with
        | Coerce (_, inner)
        | DebugPoint (_, inner) -> strip inner
        | _ -> expression

    let isStyleRoot (expression: FSharpExpr) =
        match expression with
        | Call (_, symbol, _, _, _) ->
            memberName symbol = "Run"
            && (owner symbol = "Ranvier.Css.StylesBuilder"
                || owner symbol = "Ranvier.Css.View.StylesBuilder")
        | _ -> false

    let definitions (projects: Project.CheckedProject list) =
        let output = Dictionary<string, Definition>(StringComparer.Ordinal)

        let rec index declaration =
            match declaration with
            | FSharpImplementationFileDeclaration.Entity (_, children) -> List.iter index children
            | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue (symbol, groups, body) ->
                output[symbolKey symbol] <-
                    {
                        Symbol = symbol
                        Parameters = List.concat groups
                        Body = body
                    }
            | FSharpImplementationFileDeclaration.InitAction _ -> ()

        for project in projects do
            for file in project.Contents do
                file.Declarations |> List.iter index

        output

    type Interpreter(options: ExtractionOptions, metadata: Metadata.Registry, definitions: Dictionary<string, Definition>) =
        let mutable expressions = 0
        let mutable iterations = 0
        let mutable depth = 0
        let cache = Dictionary<string, Data>(StringComparer.Ordinal)
        let active = HashSet<string>(StringComparer.Ordinal)

        let scalar expression =
            function
            | Scalar value -> value
            | Unknown reason ->
                error
                    expression
                    ("A static CSS value depends on live or unsupported input: "
                     + reason)
            | _ -> error expression "Expected a deterministic scalar value."

        let text expression data =
            scalar expression data :?> string

        let selector expression data =
            scalar expression data :?> Selector

        let nodes expression =
            function
            | Nodes values -> values
            | Declaration value -> [ Style.declarations [ value ] ]
            | Items [] -> []
            | Unknown reason ->
                error
                    expression
                    ("Cannot discover styles from "
                     + reason
                     + ". Keep utility definitions static and use whenActive for application conditions.")
            | _ -> error expression "Expected a utility, typed declaration, or style fragment."

        let items expression =
            function
            | Items values -> values
            | _ -> error expression "Expected a finite compile-time list or sequence."

        let css expression =
            function
            | Css value -> value
            | _ -> error expression "Expected a specification-typed CSS value."

        let counted expression values =
            iterations <- iterations + List.length values

            if iterations > options.MaxIterations then
                error expression "Compile-time loop iteration bound exceeded."

            values

        let declarationList expression data =
            items expression data
            |> List.map (function
                | Declaration value -> value
                | _ -> error expression "Expected CSS declarations.")

        let builderOwner ownerName =
            List.contains
                ownerName
                [
                    "Ranvier.Css.UtilityBuilder"
                    "Ranvier.Css.UtilityBuilderBase"
                    "Ranvier.Css.UtilityConditionBuilder"
                    "Ranvier.Css.ApplicationConditionBuilder"
                    "Ranvier.Css.StylesBuilder"
                    "Ranvier.Css.View.StylesBuilder"
                    "Ranvier.Css.StyledBuilder"
                    "Ranvier.Css.DeclarationBuilder"
                    "Ranvier.Css.ActiveUtilityBuilder`1"
                ]

        let coreOwner (ownerName: string) =
            ownerName.StartsWith ("Ranvier.Css.", StringComparison.Ordinal)

        member private this.Global(expression, symbol: FSharpMemberOrFunctionOrValue) =
            if symbol.IsMutable then
                error
                    expression
                    ("Mutable application state is not a static CSS input: "
                     + symbol.FullName)

            let key = symbolKey symbol

            match definitions.TryGetValue key with
            | true, definition when not definition.Parameters.IsEmpty -> Function (definition, [])
            | true, definition ->
                match cache.TryGetValue key with
                | true, value -> value
                | _ ->
                    if not (active.Add key) then
                        error
                            expression
                            ("Cyclic utility or helper evaluation: "
                             + symbol.FullName)

                    try
                        let value = this.Evaluate ([], definition.Body)
                        cache[key] <- value
                        value
                    finally
                        active.Remove key |> ignore
            | _ -> this.Invoke (expression, None, symbol, [])

        member private this.Apply(expression: FSharpExpr, functionValue: Data, arguments: Data list) =
            match arguments with
            | [] -> functionValue
            | first :: remaining ->
                let value =
                    match functionValue with
                    | Closure (environment, parameter, body) -> this.Evaluate ((symbolKey parameter, first) :: environment, body)
                    | Function (definition, bound) ->
                        match definition.Parameters with
                        | parameter :: rest ->
                            let bound = (symbolKey parameter, first) :: bound

                            if rest.IsEmpty then
                                this.Evaluate (bound, definition.Body)
                            else
                                Function ({ definition with Parameters = rest }, bound)
                        | [] -> error expression "Too many helper arguments."
                    | Unknown reason -> error expression ("Cannot invoke compile-time helper: " + reason)
                    | _ -> error expression "Only source-defined deterministic functions can be invoked at build time."

                this.Apply (expression, value, remaining)

        member private this.Condition(expression, kind, arguments, children) =
            match kind, arguments with
            | "media", [ condition ] ->
                let condition = text expression condition
                UtilityAuthoring.media condition |> ignore
                [ Style.media condition children ]
            | "supports", [ condition ] ->
                let condition = text expression condition
                UtilityAuthoring.supports condition |> ignore
                [ Style.supports condition children ]
            | "container", [ condition ] ->
                let condition = text expression condition
                UtilityAuthoring.container condition |> ignore
                [ Style.container condition children ]
            | "minViewportWidth", [ value ] ->
                let value = (css expression value).Value
                UtilityAuthoring.minViewportWidth value |> ignore
                [ Style.media ("(min-width: " + Value.render value + ")") children ]
            | "maxViewportWidth", [ value ] ->
                let value = (css expression value).Value
                UtilityAuthoring.maxViewportWidth value |> ignore
                [ Style.media ("(max-width: " + Value.render value + ")") children ]
            | "hover", [] -> [ Style.nest ":hover" children ]
            | "focus", [] -> [ Style.nest ":focus" children ]
            | "focusWithin", [] -> [ Style.nest ":focus-within" children ]
            | "descendant", [ value ] ->
                let value = selector expression value
                UtilityAuthoring.descendant value |> ignore
                [ Style.nest (" " + Selector.render value) children ]
            | "pseudoElement", [ value ] ->
                let value = text expression value
                UtilityAuthoring.pseudoElement value |> ignore
                [ Style.nest ("::" + value) children ]
            | "selectorScope", [ selector ] -> [ Style.nest (text expression selector) children ]
            | "theme", [ value ] ->
                [
                    Style.nest
                        (":is("
                         + Selector.render (selector expression value)
                         + ")")
                        children
                ]
            | "whenActive", [ _ ] -> [ Style.whenActive (fun () -> false) children ]
            | _ -> error expression ("Unsupported utility condition: " + kind)

        member private this.Build(expression, builder, name, arguments) =
            let kind, conditionArguments =
                match builder with
                | Builder (kind, values) -> kind, values
                | _ -> "utility", []

            match name, arguments with
            | "Yield", [ value ] ->
                if kind = "declarations" then
                    Items [ value ]
                else
                    Nodes (nodes expression value)
            | "YieldFrom", [ Nodes values ] -> Nodes values
            | "YieldFrom", [ Items values ] ->
                if kind = "declarations" then
                    Items values
                elif
                    values
                    |> List.forall (function
                        | Declaration _ -> true
                        | _ -> false)
                then
                    Nodes [ Style.declarations (declarationList expression (Items values)) ]
                else
                    Nodes (List.collect (nodes expression) values)
            | "Zero", _ -> if kind = "declarations" then Items [] else Nodes []
            | "Combine", [ first; second ] ->
                if kind = "declarations" then
                    Items (items expression first @ items expression second)
                else
                    Nodes (nodes expression first @ nodes expression second)
            | "Delay", [ body ] -> this.Apply (expression, body, [ Scalar () ])
            | "For", [ values; body ] ->
                let results =
                    items expression values
                    |> counted expression
                    |> List.map (fun value -> this.Apply (expression, body, [ value ]))

                if kind = "declarations" then
                    Items (List.collect (items expression) results)
                else
                    Nodes (List.collect (nodes expression) results)
            | "Run", [ value ] when
                kind = "utility"
                || kind = "styles"
                || kind = "styled"
                || kind = "declarations"
                ->
                value
            | "Run", [ value ] -> Nodes (this.Condition (expression, kind, conditionArguments, nodes expression value))
            | _ ->
                error
                    expression
                    ("Unsupported computation-expression operation: "
                     + name)

        member private this.Invoke(expression: FSharpExpr, receiver: Data option, symbol: FSharpMemberOrFunctionOrValue, arguments: Data list) =
            let ownerName = owner symbol
            let name = memberName symbol
            let key = symbolKey symbol

            match definitions.TryGetValue key with
            | true, definition ->
                if definition.Parameters.IsEmpty then
                    let value = this.Global (expression, symbol)

                    if arguments.IsEmpty then
                        value
                    else
                        this.Apply (expression, value, arguments)
                else
                    this.Apply (expression, Function (definition, []), arguments)
            | _ when builderOwner ownerName ->
                let builder =
                    receiver
                    |> Option.defaultValue (
                        Builder (
                            (if ownerName.EndsWith "DeclarationBuilder" then
                                 "declarations"
                             elif ownerName.EndsWith "StylesBuilder" then
                                 "styles"
                             else
                                 "utility"),
                            []
                        )
                    )

                this.Build (expression, builder, name, arguments)
            | _ when
                ownerName = metadata.Namespace + ".Css"
                || ownerName.StartsWith (metadata.Namespace + ".CssProperty", StringComparison.Ordinal)
                ->
                match metadata.Primitives.TryFind name, metadata.Keywords.TryFind name, metadata.Properties.TryFind name, arguments with
                | Some primitive, _, _, [] when primitive.Argument = "none" -> Css (Metadata.primitive primitive null)
                | Some primitive, _, _, [ value ] -> Css (Metadata.primitive primitive (scalar expression value))
                | _, Some keyword, _, []
                | _, Some keyword, _, [ Scalar null ] ->
                    Css
                        {
                            Value = Generated.value keyword
                            Marker = "keyword"
                        }
                | _, _, Some property, [ Css value ] -> Declaration (Metadata.declaration property value)
                | _, _, Some property, [ Closure _ ]
                | _, _, Some property, [ Function _ ] ->
                    Nodes
                        [
                            Style.dynamicWithKeywords
                                [ "initial"; "inherit"; "unset"; "revert"; "revert-layer"; "revert-rule" ]
                                property.Name
                                (fun () -> failwith "A build-time reader must never execute.")
                        ]
                | _ ->
                    error
                        expression
                        ("No supported generated sugar metadata matches "
                         + symbol.FullName
                         + " (member "
                         + name
                         + ", arguments "
                         + string arguments.Length
                         + ")"
                         + ". Supply the matching --manifest or use a supported typed primitive.")
            | _ when coreOwner ownerName ->
                match ownerName, name, arguments with
                | _, "utility", [] -> Builder ("utility", [])
                | _, "styles", [] -> Builder ("styles", [])
                | _, "styled", [] -> Builder ("styled", [])
                | _, "declarations", [] when ownerName.EndsWith "Authoring" -> Builder ("declarations", [])
                | _, ("hover" | "focus" | "focusWithin"), [] -> Builder (name, [])
                | _,
                  ("media" | "supports" | "container" | "minViewportWidth" | "maxViewportWidth" | "descendant" | "pseudoElement" | "selectorScope" | "theme" | "whenActive"),
                  [ _ ] when not (ownerName.EndsWith ".Style") -> Builder (name, arguments)
                | "Ranvier.Css.Declaration", "create", [ property; value ] ->
                    Declaration (Declaration.create (text expression property) (text expression value))
                | "Ranvier.Css.Declaration", "important", [ Declaration value ] -> Declaration (Declaration.important value)
                | "Ranvier.Css.Selector", "unsafeFromText", [ value ] -> Scalar (box (Selector.unsafeFromText (text expression value)))
                | "Ranvier.Css.Selector", "className", [ value ] -> Scalar (box (Selector.className (text expression value)))
                | "Ranvier.Css.Selector", "element", [ value ] -> Scalar (box (Selector.element (text expression value)))
                | "Ranvier.Css.Selector", "id", [ value ] -> Scalar (box (Selector.id (text expression value)))
                | "Ranvier.Css.Selector", "descendant", [ first; second ] ->
                    Scalar (box (Selector.descendant (selector expression first) (selector expression second)))
                | "Ranvier.Css.Style", "declarations", [ values ] -> Nodes [ Style.declarations (declarationList expression values) ]
                | "Ranvier.Css.Style", "nest", [ suffix; children ] -> Nodes [ Style.nest (text expression suffix) (nodes expression children) ]
                | "Ranvier.Css.Style", ("media" | "supports" | "container"), [ condition; children ] ->
                    Nodes (this.Condition (expression, name, [ condition ], nodes expression children))
                | "Ranvier.Css.Style", "hover", [ children ] -> Nodes [ Style.hover (nodes expression children) ]
                | "Ranvier.Css.Style", "whenActive", [ _; children ] -> Nodes [ Style.whenActive (fun () -> false) (nodes expression children) ]
                | "Ranvier.Css.Style", ("dynamic" | "dynamicImportant"), [ property; _ ] ->
                    let property = text expression property

                    let create =
                        if name = "dynamic" then
                            Style.dynamic
                        else
                            Style.dynamicImportant

                    Nodes
                        [
                            create property (fun () -> failwith "A build-time reader must never execute.")
                        ]
                | "Ranvier.Css.Style", ("dynamicWithKeywords" | "dynamicImportantWithKeywords"), [ keywords; property; _ ] ->
                    let keywords =
                        items expression keywords
                        |> List.map (text expression)

                    let property = text expression property

                    let create =
                        if name = "dynamicWithKeywords" then
                            Style.dynamicWithKeywords
                        else
                            Style.dynamicImportantWithKeywords

                    Nodes
                        [
                            create keywords property (fun () -> failwith "A build-time reader must never execute.")
                        ]
                | "Ranvier.Css.Style", "create", [ children ] -> Nodes (nodes expression children)
                | "Ranvier.Css.Utility", ("toStyled" | "toStyledReactive"), [ value ] -> value
                | _ -> error expression ("Unsupported CSS operation: " + symbol.FullName)
            | _ when ownerName.StartsWith ("Microsoft.FSharp.", StringComparison.Ordinal) -> this.Pure (expression, ownerName, name, arguments)
            | _ ->
                error
                    expression
                    ("Unsupported build-time call "
                     + symbol.FullName
                     + ". Only deterministic source helpers and documented CSS/pure operations are interpreted; application code is never executed.")

        member private this.Pure(expression, ownerName: string, name, arguments) =
            let numeric operation integerOperation a b =
                let first = scalar expression a
                let second = scalar expression b

                if first :? int && second :? int then
                    Scalar (box (integerOperation (unbox<int> first) (unbox<int> second)))
                elif
                    (first :? float || first :? int)
                    && (second :? float || second :? int)
                then
                    Scalar (box (operation (Convert.ToDouble first) (Convert.ToDouble second)))
                else
                    error expression "Static arithmetic supports int32 and float values; convert other numeric types explicitly."

            let compare operation floatOperation a b =
                let first = scalar expression a
                let second = scalar expression b

                match first, second with
                | (:? float as first), (:? float as second) -> Scalar (box (floatOperation first second))
                | _ -> Scalar (box (operation (Unchecked.compare first second)))

            match name, arguments with
            | "op_Addition", [ Scalar (:? string as first); Scalar (:? string as second) ] -> Scalar (first + second)
            | "op_Addition", [ a; b ] -> numeric (+) (+) a b
            | "op_Subtraction", [ a; b ] -> numeric (-) (-) a b
            | "op_Multiply", [ a; b ] -> numeric (*) (*) a b
            | "op_Division", [ a; b ] -> numeric (/) (/) a b
            | "op_Modulus", [ a; b ] -> numeric (%) (%) a b
            | "op_UnaryNegation", [ value ] -> numeric (-) (-) (Scalar 0) value
            | "op_Equality", [ a; b ] -> Scalar (box (Unchecked.equals (scalar expression a) (scalar expression b)))
            | "op_Inequality", [ a; b ] -> Scalar (box (not (Unchecked.equals (scalar expression a) (scalar expression b))))
            | "op_LessThan", [ a; b ] -> compare (fun c -> c < 0) (<) a b
            | "op_LessThanOrEqual", [ a; b ] -> compare (fun c -> c <= 0) (<=) a b
            | "op_GreaterThan", [ a; b ] -> compare (fun c -> c > 0) (>) a b
            | "op_GreaterThanOrEqual", [ a; b ] -> compare (fun c -> c >= 0) (>=) a b
            | "not", [ value ] -> Scalar (not (unbox<bool>(scalar expression value)))
            | ("float" | "ToDouble"), [ value ] -> Scalar (Convert.ToDouble (scalar expression value, Globalization.CultureInfo.InvariantCulture))
            | ("int" | "ToInt" | "ToInt32"), [ value ] ->
                match scalar expression value with
                | :? float as number -> Scalar (box (int number))
                | :? int as number -> Scalar (box number)
                | :? string as number -> Scalar (box (int number))
                | _ -> error expression "Static int conversion supports float, int32 and integer strings."
            | ("string" | "ToString"), [ value ] -> Scalar (Convert.ToString (scalar expression value, Globalization.CultureInfo.InvariantCulture))
            | "op_Append", [ a; b ] -> Items (items expression a @ items expression b)
            | "op_PipeRight", [ value; body ] -> this.Apply (expression, body, [ value ])
            | "op_PipeLeft", [ body; value ] -> this.Apply (expression, body, [ value ])
            | ("id" | "Identity"), [ value ] -> value
            | ("seq" | "toList" | "ofList" | "ofArray" | "toArray"), [ values ] -> values
            | "delay", [ body ] when ownerName.Contains "Seq" -> this.Apply (expression, body, [ Scalar () ])
            | "singleton", [ value ] -> Items [ value ]
            | "empty", [] -> Items []
            | "map", [ body; values ] ->
                Items (
                    items expression values
                    |> counted expression
                    |> List.map (fun value -> this.Apply (expression, body, [ value ]))
                )
            | "collect", [ body; values ] ->
                Items (
                    items expression values
                    |> counted expression
                    |> List.collect (fun value ->
                        this.Apply (expression, body, [ value ])
                        |> items expression)
                )
            | "append", [ a; b ] -> Items (items expression a @ items expression b)
            | "length", [ values ] -> Scalar (List.length (items expression values))
            | "rev", [ values ] -> Items (List.rev (items expression values))
            | "filter", [ body; values ] ->
                Items (
                    items expression values
                    |> counted expression
                    |> List.filter (fun value ->
                        this.Apply (expression, body, [ value ])
                        |> scalar expression
                        |> unbox<bool>)
                )
            | "fold", [ body; state; values ] ->
                items expression values
                |> counted expression
                |> List.fold (fun state value -> this.Apply (expression, body, [ state; value ])) state
            | "op_Range", [ first; last ] -> this.Range (expression, first, Scalar 1, last)
            | "op_RangeStep", [ first; step; last ] -> this.Range (expression, first, step, last)
            | _ ->
                error
                    expression
                    ("Unsupported pure helper operation: "
                     + ownerName
                     + "."
                     + name)

        member private _.Range(expression, first, step, last) =
            let integer value =
                match scalar expression value with
                | :? int as number -> number
                | _ -> error expression "Static ranges require int32 bounds and steps."

            let first = integer first
            let step = integer step
            let last = integer last

            if step = 0 then
                error expression "Compile-time range step must be nonzero."

            let count =
                if
                    (step > 0 && first > last)
                    || (step < 0 && first < last)
                then
                    0L
                else
                    (int64 last - int64 first) / int64 step + 1L

            if count > int64 options.MaxIterations then
                error expression "Compile-time range exceeds the finite iteration bound."

            Items (
                [
                    for index in 0L .. count - 1L -> Scalar (box (int (int64 first + index * int64 step)))
                ]
            )

        member private this.EvaluateCore(environment: (string * Data) list, expression: FSharpExpr) : Data =
            try
                match strip expression with
                | Const (value, _) -> Scalar value
                | FSharpExprPatterns.Value symbol ->
                    match
                        environment
                        |> List.tryFind (fun (key, _) -> key = symbolKey symbol)
                    with
                    | Some (_, value) -> value
                    | None -> this.Global (expression, symbol)
                | Lambda (parameter, body) -> Closure (environment, parameter, body)
                | Application (body, _, arguments) ->
                    this.Apply (
                        expression,
                        this.Evaluate (environment, body),
                        List.map (fun argument -> this.Evaluate (environment, argument)) arguments
                    )
                | Let ((symbol, value, _), body) ->
                    let value =
                        if symbol.IsMutable then
                            Unknown ("mutable binding " + symbol.DisplayName)
                        else
                            this.Evaluate (environment, value)

                    this.Evaluate ((symbolKey symbol, value) :: environment, body)
                | IfThenElse (condition, first, second) ->
                    let condition =
                        this.Evaluate (environment, condition)
                        |> scalar expression
                        |> unbox<bool>

                    this.Evaluate (environment, if condition then first else second)
                | NewTuple (_, values) -> Tuple (List.map (fun value -> this.Evaluate (environment, value)) values)
                | TupleGet (_, index, value) ->
                    match this.Evaluate (environment, value) with
                    | Tuple values -> List.item index values
                    | _ -> error expression "Expected a deterministic tuple."
                | NewArray (_, values) -> Items (List.map (fun value -> this.Evaluate (environment, value)) values)
                | NewUnionCase (unionType, unionCase, values) when
                    unionType.HasTypeDefinition
                    && unionType.TypeDefinition.FullName.StartsWith "Microsoft.FSharp.Collections.FSharpList"
                    ->
                    match unionCase.Name, List.map (fun value -> this.Evaluate (environment, value)) values with
                    | ("Empty" | "op_Nil"), [] -> Items []
                    | ("Cons" | "op_ColonColon"), [ first; Items rest ] -> Items (first :: rest)
                    | _ -> error expression "Unsupported list representation."
                | NewRecord (recordType, values) ->
                    let fields =
                        recordType.TypeDefinition.FSharpFields
                        |> Seq.toList

                    Record (
                        List.zip fields values
                        |> List.map (fun (field, value) -> field.Name, this.Evaluate (environment, value))
                        |> Map.ofList
                    )
                | FSharpFieldGet (Some target, _, field) ->
                    if field.IsMutable then
                        error
                            expression
                            ("Mutable record state is not a static CSS input: "
                             + field.Name)

                    match this.Evaluate (environment, target) with
                    | Record fields -> fields[field.Name]
                    | Unknown reason -> Unknown reason
                    | _ -> error expression "Only immutable source-record fields can be read during extraction."
                | NewObject (constructor, _, _) when builderOwner (owner constructor) ->
                    Builder (
                        (if (owner constructor).EndsWith "DeclarationBuilder" then
                             "declarations"
                         elif (owner constructor).EndsWith "StylesBuilder" then
                             "styles"
                         else
                             "utility"),
                        []
                    )
                | Call (receiver, symbol, _, _, arguments) ->
                    this.Invoke (
                        expression,
                        receiver
                        |> Option.map (fun receiver -> this.Evaluate (environment, receiver)),
                        symbol,
                        arguments
                        |> List.map (fun argument -> this.Evaluate (environment, argument))
                    )
                | _ ->
                    error
                        expression
                        ("Unsupported static F# expression: "
                         + ((strip expression).ToString().Split('\n')
                            |> Array.head))
            with
            | :? ExtractionFailure -> reraise ()
            | errorValue -> error expression errorValue.Message

        member this.Evaluate(environment: (string * Data) list, expression: FSharpExpr) : Data =
            expressions <- expressions + 1
            depth <- depth + 1

            try
                if expressions > options.MaxExpressions then
                    error expression "Compile-time expression evaluation bound exceeded."

                if depth > options.MaxDepth then
                    error expression "Compile-time evaluation depth exceeded; recursive/cyclic helpers must be finite."

                this.EvaluateCore (environment, expression)
            finally
                depth <- depth - 1

        member this.Extract(expression, environment) =
            this.Evaluate (environment, expression)
            |> nodes expression
            |> Style.create
            |> _.Definition

        member this.Discover(body: FSharpExpr, parameters: FSharpMemberOrFunctionOrValue list) =
            let roots = ResizeArray<StyleDefinition>()

            let containsRoot expression =
                let pending = Stack<FSharpExpr>()
                pending.Push expression
                let mutable count = 0
                let mutable found = false

                while pending.Count > 0 && not found do
                    let next = pending.Pop ()
                    count <- count + 1

                    if count > options.MaxExpressions then
                        error next "Source discovery expression bound exceeded."

                    if isStyleRoot next then
                        found <- true
                    else
                        next.ImmediateSubExpressions
                        |> Seq.iter pending.Push

                found

            let rec walk nesting environment expression =
                if nesting > options.MaxDepth then
                    error expression "Source discovery nesting bound exceeded."

                if isStyleRoot expression then
                    roots.Add (this.Extract (expression, environment))
                else
                    match expression with
                    | Let ((symbol, value, _), rest) ->
                        walk (nesting + 1) environment value

                        let binding =
                            try
                                if symbol.IsMutable then
                                    Unknown ("mutable binding " + symbol.DisplayName)
                                else
                                    this.Evaluate (environment, value)
                            with ExtractionFailure diagnostic ->
                                Unknown diagnostic.Message

                        walk (nesting + 1) ((symbolKey symbol, binding) :: environment) rest
                    | Lambda (parameter, rest) ->
                        walk
                            (nesting + 1)
                            ((symbolKey parameter, Unknown ("runtime parameter " + parameter.DisplayName))
                             :: environment)
                            rest
                    | _ ->
                        expression.ImmediateSubExpressions
                        |> Seq.iter (walk (nesting + 1) environment)

            let environment =
                parameters
                |> List.map (fun parameter -> symbolKey parameter, Unknown ("runtime parameter " + parameter.DisplayName))

            if containsRoot body then
                walk 0 environment body

            List.ofSeq roots
