/// Full names of the symbols the rules recognise, and the reference gates of the rule sets.
module Ranvier.Analyzers.Symbols

open System
open System.IO
open FSharp.Analyzers.SDK
open FSharp.Compiler.Symbols

/// The full name of the entity that declares a member or, for an extension member, the entity it extends; empty for a
/// local value.
let enclosing (m: FSharpMemberOrFunctionOrValue) : string =
    match m.ApparentEnclosingEntity with
    | Some entity -> defaultArg entity.TryFullName ""
    | None -> ""

/// Whether `m` is the member or function `name` of `entity`.
let isMember (entity: string) (name: string) (m: FSharpMemberOrFunctionOrValue) : bool =
    m.LogicalName = name && enclosing m = entity

/// The name a message gives `m`: the type's name for a constructor, the display name otherwise.
let display (m: FSharpMemberOrFunctionOrValue) : string =
    match m.ApparentEnclosingEntity with
    | Some entity when m.IsConstructor -> entity.DisplayName
    | _ -> m.DisplayName

[<Literal>]
let Operators = "Microsoft.FSharp.Core.Operators"

[<Literal>]
let StringType = "System.String"

[<Literal>]
let PipeRight = "op_PipeRight"

[<Literal>]
let PipeLeft = "op_PipeLeft"

[<Literal>]
let CompilerGenerated = "System.Runtime.CompilerServices.CompilerGeneratedAttribute"

/// The case-name prefix of a tester property: `IsQueue` tests `Queue`.
[<Literal>]
let TesterProperty = "Is"

/// The prefix of a generated union case tester's getter: `get_IsQueue` tests `Queue`.
[<Literal>]
let TesterPrefix = "get_" + TesterProperty

/// The names of the equality, hashing and comparison members the compiler generates for a type.
let generatedComparisons = set [ "CompareTo"; "Equals"; "GetHashCode" ]

/// The path that source code names a module, namespace or type by: its namespace, then the display names of its
/// enclosing modules and types. A module compiled with the `Module` suffix keeps its source name.
let rec pathOf (entity: FSharpEntity) : string =
    if entity.IsNamespace then
        match entity.AccessPath with
        | null
        | ""
        | "global" -> entity.DisplayName
        | access -> access + "." + entity.DisplayName
    else
        match entity.DeclaringEntity with
        | Some parent -> pathOf parent + "." + entity.DisplayName
        | None ->
            match entity.Namespace with
            | Some space when space <> "" -> space + "." + entity.DisplayName
            | _ -> entity.DisplayName

/// The source path of the module, namespace or type that declares `entity`; empty at the global level.
let containerOf (entity: FSharpEntity) : string =
    match entity.DeclaringEntity with
    | Some parent -> pathOf parent
    | None -> defaultArg entity.Namespace ""

let private referencedFiles (options: AnalyzerProjectOptions) : string seq =
    options.OtherOptions
    |> Seq.choose (fun option ->
        if option.StartsWith ("-r:", StringComparison.Ordinal) then
            Some (Path.GetFileName (option.Substring 3))
        elif option.StartsWith ("--reference:", StringComparison.Ordinal) then
            Some (Path.GetFileName (option.Substring 12))
        else
            None)

/// Whether the project's `-r:` options name the assembly file `assembly`.
let references (assembly: string) (options: AnalyzerProjectOptions) : bool =
    referencedFiles options
    |> Seq.exists (fun file -> String.Equals (file, assembly, StringComparison.OrdinalIgnoreCase))

/// The assembly files whose reference enables a rule set.
module Assemblies =
    [<Literal>]
    let Ranvier = "Ranvier.dll"

    [<Literal>]
    let View = "Ranvier.View.dll"

    [<Literal>]
    let Dom = "Ranvier.View.Dom.dll"

    [<Literal>]
    let Fable = "Fable.Core.dll"

/// The F#-to-JavaScript compiler a project declares in its tool manifest.
module Compilers =
    open System.Collections.Concurrent
    open System.Text.Json

    [<Literal>]
    let Fable = "fable"

    [<Literal>]
    let Fickl = "fickl"

    /// A declared compiler: its command, its version without a prerelease label, and whether it has one.
    type Declared =
        {
            Command: string
            Version: Version
            Prerelease: bool
        }

    /// The first Fable release whose precompile declares the generated union testers.
    let testersPrecompileFrom = Version (5, 22, 0)

    /// Whether `declared` is a Fable release before `testersPrecompileFrom`; a prerelease of it is before it.
    let precedesTesterFix (declared: Declared) : bool =
        declared.Command = Fable
        && (declared.Version < testersPrecompileFrom
            || (declared.Version = testersPrecompileFrom
                && declared.Prerelease))

    let private parse (text: string) : (Version * bool) option =
        let core = text.Split('-', '+').[0]

        match Version.TryParse core with
        | true, version -> Some (version, text.Contains '-')
        | _ -> None

    let private options =
        JsonDocumentOptions (CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)

    /// A manifest's declared compiler, and whether the manifest is a root.
    let private read (file: string) : Declared option * bool =
        try
            use document = JsonDocument.Parse (File.ReadAllText file, options)
            let root = document.RootElement

            let isRoot =
                root.EnumerateObject ()
                |> Seq.exists (fun property ->
                    String.Equals (property.Name, "isRoot", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind = JsonValueKind.True)

            let tools =
                root.EnumerateObject ()
                |> Seq.tryFind (fun property -> String.Equals (property.Name, "tools", StringComparison.OrdinalIgnoreCase))

            let declared =
                match tools with
                | Some tools when tools.Value.ValueKind = JsonValueKind.Object ->
                    [ Fickl; Fable ]
                    |> List.tryPick (fun command ->
                        tools.Value.EnumerateObject ()
                        |> Seq.tryFind (fun tool -> String.Equals (tool.Name, command, StringComparison.OrdinalIgnoreCase))
                        |> Option.bind (fun tool ->
                            tool.Value.EnumerateObject ()
                            |> Seq.tryFind (fun property -> String.Equals (property.Name, "version", StringComparison.OrdinalIgnoreCase))
                            |> Option.bind (fun version -> parse (version.Value.GetString ())))
                        |> Option.map (fun (version, prerelease) ->
                            {
                                Command = command
                                Version = version
                                Prerelease = prerelease
                            }))
                | _ -> None

            declared, isRoot
        with _ ->
            None, false

    // Each manifest's reading, keyed by its path and valid while its last write time is unchanged.
    let private readings =
        ConcurrentDictionary<string, DateTime * (Declared option * bool)>()

    let private manifestOf (file: string) : Declared option * bool =
        let written = File.GetLastWriteTimeUtc file

        match readings.TryGetValue file with
        | true, (stamp, reading) when stamp = written -> reading
        | _ ->
            let reading = read file
            readings[file] <- (written, reading)
            reading

    /// The compiler, `fable` or `fickl`, that the tool manifests above `projectFile` declare: the nearest manifest that
    /// declares one, searching up to the first root manifest. An edited or added manifest takes effect on the next call.
    let declared (projectFile: string) : Declared option =
        let rec up (directory: string) =
            if String.IsNullOrEmpty directory then
                None
            else
                let manifest =
                    [
                        Path.Combine (directory, ".config", "dotnet-tools.json")
                        Path.Combine (directory, "dotnet-tools.json")
                    ]
                    |> List.tryFind File.Exists

                match manifest |> Option.map manifestOf with
                | Some (Some found, _) -> Some found
                | Some (None, true) -> None
                | _ -> up (Path.GetDirectoryName directory)

        if String.IsNullOrEmpty projectFile then
            None
        else
            up (Path.GetDirectoryName (Path.GetFullPath projectFile))

    /// The compile symbol that opts a project into the Fable rules: `RanvierFableAnalyzers=true` defines it.
    [<Literal>]
    let OptInDefine = "RANVIER_FABLE_ANALYZERS"

    /// Whether the project's compiler options define `OptInDefine`.
    let optedIn (options: AnalyzerProjectOptions) : bool =
        options.OtherOptions
        |> Seq.exists (fun option ->
            option = "--define:" + OptInDefine
            || option = "-d:" + OptInDefine)

    let private readsFable (file: string) : bool =
        try
            Xml.Linq.XDocument.Load(file).Descendants()
            |> Seq.exists (fun element ->
                element.Name.LocalName = "PackageReference"
                && (match element.Attribute (Xml.Linq.XName.Get "Include") with
                    | null -> false
                    | reference -> reference.Value.StartsWith ("Fable.", StringComparison.OrdinalIgnoreCase)))
        with _ ->
            false

    // Each project file's reading, keyed by its path and valid while its last write time is unchanged.
    let private projectReadings = ConcurrentDictionary<string, DateTime * bool>()

    let private referencesFable (file: string) : bool =
        let written = File.GetLastWriteTimeUtc file

        match projectReadings.TryGetValue file with
        | true, (stamp, reading) when stamp = written -> reading
        | _ ->
            let reading = readsFable file
            projectReadings[file] <- (written, reading)
            reading

    /// The project files of the project that compiles `source`: the options' project file when it exists, otherwise
    /// the project files of the nearest directory above `source` that holds one.
    let private projectFiles (options: AnalyzerProjectOptions) (source: string) : string list =
        if File.Exists options.ProjectFileName then
            [ options.ProjectFileName ]
        else
            let rec up (directory: string) =
                if String.IsNullOrEmpty directory then
                    []
                else
                    match
                        Directory.GetFiles (directory, "*.fsproj")
                        |> List.ofArray
                    with
                    | [] -> up (Path.GetDirectoryName directory)
                    | found -> found

            try
                up (Path.GetDirectoryName (Path.GetFullPath source))
            with _ ->
                []

    /// The Fable signals of a project: the compiler its tool manifests declare, and whether anything says that Fable
    /// compiles it.
    type Signal =
        {
            Declared: Declared option
            CompilesWithFable: bool
        }

    /// The Fable signals of the project that compiles `source`. Fable compiles the project when a tool manifest
    /// declares `fable` or `fickl`, the project file references a `Fable.*` package, or the options define
    /// `OptInDefine`.
    let signal (options: AnalyzerProjectOptions) (source: string) : Signal =
        let anchor =
            if File.Exists options.ProjectFileName then
                options.ProjectFileName
            else
                source

        let declared = declared anchor

        {
            Declared = declared
            CompilesWithFable =
                declared.IsSome
                || optedIn options
                || projectFiles options source
                   |> List.exists referencesFable
        }

/// The core node types, their creators and their pure contexts.
module Core =
    [<Literal>]
    let Api = "Ranvier.Api"

    let private valueTypes =
        set
            [
                "Ranvier.Signal`1"
                "Ranvier.AsyncSource`1"
                "Ranvier.Memo`1"
                "Ranvier.AsyncMemo`1"
                "Ranvier.Boundary`1"
                "Ranvier.Editable`1"
            ]

    let private keyedTypes = set [ "Ranvier.Projection`2"; "Ranvier.Lookup`2" ]

    /// Whether `m` is a tracked read: `Value`, `TryValue` or `Status` of a node, `Get`, `TryGet` or `Keys` of a
    /// projection or lookup. `Peek` is untracked.
    let isReactiveRead (m: FSharpMemberOrFunctionOrValue) : bool =
        let entity = enclosing m

        (valueTypes.Contains entity
         && (m.LogicalName = "get_Value"
             || m.LogicalName = "get_TryValue"
             || m.LogicalName = "get_Status"))
        || (keyedTypes.Contains entity
            && (m.LogicalName = "Get"
                || m.LogicalName = "TryGet"
                || m.LogicalName = "get_Keys"))

    let private creatorFunctions =
        set
            [
                "createMemo"
                "createMemoWithComparer"
                "createMemoWith"
                "createOwningMemoWithComparer"
                "createEffect"
                "createEffectOn"
                "createEffectOnWithComparer"
                "createAsync"
                "createAsyncWith"
                "createAsyncWithPolicy"
                "createOwningAsyncWithPolicy"
                "createAsyncSource"
                "createStream"
                "createStreamFold"
                "createSuspense"
                "createSuspenseWithComparer"
                "createErrorBoundary"
                "createErrorBoundaryWithComparer"
                "createBoundary"
                "createBoundaryWithComparer"
                "createRoot"
                "createProjection"
                "createProjectionWith"
                "createProjectionWithOptions"
                "createProjectionOnce"
                "createIndexProjection"
                "createIndexProjectionWith"
                "createLookup"
                "createSelector"
                "createOptionMemo"
                "onCleanup"
            ]

    let private creatorTypes =
        set [ "Ranvier.Memo`1"; "Ranvier.Effect"; "Ranvier.AsyncMemo`1" ]

    /// Whether a call of `m` creates an owned node.
    let isCreator (m: FSharpMemberOrFunctionOrValue) : bool =
        (enclosing m = Api
         && creatorFunctions.Contains m.LogicalName)
        || (m.IsConstructor
            && creatorTypes.Contains (enclosing m))

    /// Whether the lambda argument of a call of `m` runs in the caller's run: `untrack`, `batch`, and the `Delay` of a
    /// task expression.
    let isTransparent (m: FSharpMemberOrFunctionOrValue) : bool =
        (enclosing m = Api
         && (m.LogicalName = "untrack"
             || m.LogicalName = "batch"))
        || (m.LogicalName = "Delay"
            && enclosing m = "Microsoft.FSharp.Control.TaskBuilderBase")

    /// How a pure context fails when its body creates an owned node.
    [<RequireQualifiedAccess>]
    type Pure =
        | Memo
        | Async
        | EffectOn
        | Map
        | Reader
        | LookupF
        | LookupAffected
        | Seed
        | Aggregate

    /// A lambda argument that runs as a pure derivation: its kind, the lambdas before its body, and the owning
    /// function that replaces the callee.
    type PureContext =
        {
            Kind: Pure
            Layers: int
            Owning: string option
        }

    let private pureFunctions: Map<string * string * int, PureContext> =
        let context kind layers owning =
            {
                Kind = kind
                Layers = layers
                Owning = owning
            }

        Map.ofList
            [
                (Api, "createMemo", 0), context Pure.Memo 1 (Some "createMemoWith")
                (Api, "createMemoWithComparer", 1), context Pure.Memo 1 (Some "createOwningMemoWithComparer")
                (Api, "createAsync", 0), context Pure.Async 2 (Some "createAsyncWith")
                (Api, "createAsyncWithPolicy", 1), context Pure.Async 2 (Some "createOwningAsyncWithPolicy")
                (Api, "createEffectOn", 0), context Pure.EffectOn 1 None
                (Api, "createEffectOnWithComparer", 1), context Pure.EffectOn 1 None
                (Api, "createProjection", 1), context Pure.Map 1 None
                (Api, "createIndexProjection", 0), context Pure.Map 1 None
                (Api, "createProjectionWith", 1), context Pure.Reader 2 None
                (Api, "createIndexProjectionWith", 0), context Pure.Reader 2 None
                (Api, "createLookup", 0), context Pure.LookupF 2 None
                (Api, "createLookup", 1), context Pure.LookupAffected 2 None
                (Api, "createEditable", 0), context Pure.Seed 1 None
                (Api, "createDraft", 0), context Pure.Seed 1 None
                ("Ranvier.ProjectionModule", "foldGroup", 0), context Pure.Aggregate 2 None
                ("Ranvier.ProjectionModule", "foldGroup", 1), context Pure.Aggregate 2 None
                ("Ranvier.ProjectionModule", "fold", 0), context Pure.Aggregate 2 None
                ("Ranvier.ProjectionModule", "foldGroupBy", 1), context Pure.Aggregate 2 None
                ("Ranvier.ProjectionModule", "foldGroupBy", 2), context Pure.Aggregate 2 None
            ]

    let private owningConstructor (m: FSharpMemberOrFunctionOrValue) =
        m.CurriedParameterGroups
        |> Seq.concat
        |> Seq.exists (fun parameter ->
            parameter.Type.HasTypeDefinition
            && parameter.Type.TypeDefinition.TryFullName = Some "System.Boolean")

    /// The pure context that argument `index` of `count` arguments of a call of `m` is.
    let pureContext (m: FSharpMemberOrFunctionOrValue) (index: int) (count: int) : PureContext option =
        if m.IsConstructor then
            match enclosing m with
            | "Ranvier.Memo`1" when index = count - 1 && not (owningConstructor m) ->
                Some
                    {
                        Kind = Pure.Memo
                        Layers = 1
                        Owning = None
                    }
            | "Ranvier.AsyncMemo`1" when index = count - 1 && not (owningConstructor m) ->
                Some
                    {
                        Kind = Pure.Async
                        Layers = 2
                        Owning = None
                    }
            | _ -> None
        else
            pureFunctions
            |> Map.tryFind (enclosing m, m.LogicalName, index)

    /// The index of the compute argument of an async value's creator, when `m` is one; `count` is the call's
    /// argument count.
    let asyncComputeIndex (m: FSharpMemberOrFunctionOrValue) (count: int) : int option =
        if
            m.IsConstructor
            && enclosing m = "Ranvier.AsyncMemo`1"
        then
            Some (count - 1)
        elif enclosing m = Api then
            match m.LogicalName with
            | "createAsync"
            | "createAsyncWith" -> Some 0
            | "createAsyncWithPolicy"
            | "createOwningAsyncWithPolicy" -> Some 1
            | _ -> None
        else
            None

    let private ownerFunctions: Map<string * int, int> =
        Map.ofList
            [
                ("createSuspense", 1), 1
                ("createSuspenseWithComparer", 2), 1
                ("createErrorBoundary", 1), 1
                ("createErrorBoundaryWithComparer", 2), 1
                ("createBoundary", 2), 1
                ("createBoundaryWithComparer", 3), 1
                ("createMemoWith", 0), 1
                ("createOwningMemoWithComparer", 1), 1
                ("createEffect", 0), 1
                ("createAsyncWith", 0), 2
                ("createOwningAsyncWithPolicy", 1), 2
            ]

    /// The lambdas before the body when argument `index` of `count` arguments of a call of `m` is an owner body: a
    /// body that disposes the nodes it created before each re-run.
    let ownerBody (m: FSharpMemberOrFunctionOrValue) (index: int) (count: int) : int option =
        if
            m.IsConstructor
            && enclosing m = "Ranvier.Effect"
            && index = count - 1
        then
            Some 1
        elif enclosing m = Api then
            ownerFunctions
            |> Map.tryFind (m.LogicalName, index)
        else
            None

    /// Whether a call of `m` creates an async value: an async memo, an async source or a stream.
    let isAsyncCreator (m: FSharpMemberOrFunctionOrValue) : bool =
        (m.IsConstructor
         && enclosing m = "Ranvier.AsyncMemo`1")
        || (enclosing m = Api
            && (m.LogicalName.StartsWith ("createAsync", StringComparison.Ordinal)
                || m.LogicalName.StartsWith ("createStream", StringComparison.Ordinal)
                || m.LogicalName = "createOwningAsyncWithPolicy"))

    /// Whether a call of `m` writes a signal or pumps the graph: work the graph's owning thread must do.
    let isGraphWrite (m: FSharpMemberOrFunctionOrValue) : bool =
        isMember "Ranvier.Signal`1" "set_Value" m
        || isMember "Ranvier.SignalModule" "update" m
        || isMember "Ranvier.Graph" "Pump" m

    /// Whether `m` is `Graph.Dispatch`, which runs its work on the owning thread.
    let isDispatch (m: FSharpMemberOrFunctionOrValue) : bool =
        isMember "Ranvier.Graph" "Dispatch" m

    /// Whether a call of `m` runs its delegate argument on a thread-pool or new thread.
    let threadStarter (m: FSharpMemberOrFunctionOrValue) : bool =
        match enclosing m, m.LogicalName with
        | "System.Threading.Tasks.Task", "Run"
        | "System.Threading.Tasks.TaskFactory", "StartNew"
        | "System.Threading.ThreadPool", "QueueUserWorkItem"
        | "System.Threading.ThreadPool", "UnsafeQueueUserWorkItem"
        | "System.Threading.Thread", ".ctor"
        | "System.Threading.Tasks.Parallel", "For"
        | "System.Threading.Tasks.Parallel", "ForEach"
        | "System.Threading.Tasks.Parallel", "Invoke"
        | "System.Threading.Tasks.Task", "ContinueWith"
        | "System.Threading.Tasks.Task`1", "ContinueWith"
        | "System.Threading.Timer", ".ctor" -> true
        | _ -> false

    /// Whether `m` is a computation-expression bind, whose last argument is the continuation after an await:
    /// `Bind`, `BindReturn`, `Bind2` and the like.
    let isBind (m: FSharpMemberOrFunctionOrValue) : bool =
        m.LogicalName.StartsWith ("Bind", StringComparison.Ordinal)

    /// Whether `m` is a computation-expression `Combine`, whose second argument runs after its first.
    let isCombine (m: FSharpMemberOrFunctionOrValue) : bool =
        m.LogicalName = "Combine"

    /// Whether `m` is a computation-expression loop, `For` or `While`, whose body runs again after its own awaits.
    let isLoop (m: FSharpMemberOrFunctionOrValue) : bool =
        m.LogicalName = "For" || m.LogicalName = "While"

    /// Whether `m` is a computation-expression member that runs its lambda arguments as part of the computation:
    /// `Delay`, `Combine`, a loop, `TryWith`, `TryFinally` or `Using`.
    let isControl (m: FSharpMemberOrFunctionOrValue) : bool =
        match m.LogicalName with
        | "Delay"
        | "Combine"
        | "For"
        | "While"
        | "TryWith"
        | "TryFinally"
        | "Using" -> true
        | _ -> false

    /// Whether `m` is `Graph.Run`, which runs its body with the graph active.
    let isGraphRun (m: FSharpMemberOrFunctionOrValue) : bool =
        m.DisplayName = "Run"
        && (m.ApparentEnclosingEntity
            |> Option.bind _.TryFullName)
            =
            Some "Ranvier.Graph"

    /// Whether `m` creates an async value with a flight policy: `createAsyncWithPolicy` or its owning form.
    let isPolicyCreator (m: FSharpMemberOrFunctionOrValue) : bool =
        isMember Api "createAsyncWithPolicy" m
        || isMember Api "createOwningAsyncWithPolicy" m

    /// Whether `case` is `FlightPolicy.CancelPrevious`, which cancels a superseded flight's token.
    let isCancelPrevious (case: FSharpUnionCase) : bool =
        case.Name = "CancelPrevious"
        && case.ReturnType.HasTypeDefinition
        && case.ReturnType.TypeDefinition.TryFullName = Some "Ranvier.FlightPolicy"

    /// Whether `m` constructs a graph.
    let isGraphConstructor (m: FSharpMemberOrFunctionOrValue) : bool =
        m.IsConstructor && enclosing m = "Ranvier.Graph"

    /// Whether `m` constructs a node over the graph it takes first: a signal, an async source or a memo.
    let isNodeConstructor (m: FSharpMemberOrFunctionOrValue) : bool =
        m.IsConstructor
        && (match enclosing m with
            | "Ranvier.Signal`1"
            | "Ranvier.AsyncSource`1"
            | "Ranvier.Memo`1"
            | "Ranvier.AsyncMemo`1" -> true
            | _ -> false)

    /// Whether `m` is `untrack`.
    let isUntrack (m: FSharpMemberOrFunctionOrValue) : bool =
        isMember Api "untrack" m

/// The view layer's builders, sugar and definitions.
module View =
    /// The namespace of the view layer's templates, sugar and server rendering.
    [<Literal>]
    let Root = "Ranvier.View"

    /// The namespace of the DOM renderer: mounting, hydration and scheduling.
    [<Literal>]
    let Dom = Root + ".Dom"

    /// The namespaces under `Root` outside the view layer's rule surface.
    let private outside =
        [ Root + ".Anime."; Root + ".Twin."; Root + ".Testing."; Root + ".Checks." ]

    open FSharp.Compiler.Symbols.FSharpExprPatterns

    /// Raises `InvalidOperationException` when the HTML parser restructures `tag` under `ancestors` (nearest first, lower
    /// case).
    let checkNesting (ancestors: string list) (tag: string) : unit =
        Ranvier.View.ContentModel.check ancestors tag

    [<Literal>]
    let Builder = Root + ".Template.TemplateBuilder`1"

    [<Literal>]
    let Attributes = Root + ".Template.AttributeBuilder`1"

    [<Literal>]
    let Properties = Root + ".Template.PropertyBuilder`1"

    [<Literal>]
    let Hooks = Root + ".Template.HookBuilder`1"

    [<Literal>]
    let Unsafe = Root + ".Template.UnsafeBuilder`1"

    [<Literal>]
    let Component = Root + ".Template.Component"

    [<Literal>]
    let ComponentType = Root + ".Template.Component`1"

    [<Literal>]
    let Sugar = Root + ".Sugar.Builders"

    [<Literal>]
    let SugarComponent = Root + ".Sugar.Builders.Component"

    [<Literal>]
    let Element = Root + ".Sugar.Builders.Element`2"

    [<Literal>]
    let Elements = Root + ".Sugar.Elements"

    [<Literal>]
    let Source = Root + ".Template.Source`2"

    [<Literal>]
    let Hydration = Dom + ".Hydration"

    [<Literal>]
    let Microtask = Dom + ".Microtask"

    [<Literal>]
    let CommitMode = Dom + ".Scheduling.CommitMode"

    [<Literal>]
    let ComponentTag = Root + ".Sugar.Builders.ComponentTag`2"

    /// Whether `m` belongs to the view layer, by its enclosing entity's namespace.
    let isViewMember (m: FSharpMemberOrFunctionOrValue) : bool =
        let entity = enclosing m

        entity.StartsWith (Root + ".", StringComparison.Ordinal)
        && not (
            outside
            |> List.exists (fun prefix -> entity.StartsWith (prefix, StringComparison.Ordinal))
        )

    /// Whether argument `index` of a call of `m` is read per region read: the arms of `matchWith` and `branch`, the
    /// key and items of `each`, `forEach` and `forEachFixed`.
    let isRegionLambda (m: FSharpMemberOrFunctionOrValue) (index: int) : bool =
        let entity = enclosing m

        (entity = Builder || entity = Sugar)
        && (match m.LogicalName, index with
            | ("matchWith" | "branch"), 1 -> true
            | ("each" | "forEach" | "forEachFixed"), (0 | 1) -> true
            | _ -> false)

    /// Whether a call of `m` creates a component definition.
    let isDefinitionCreator (m: FSharpMemberOrFunctionOrValue) =
        match enclosing m, m.LogicalName with
        | Component, "define"
        | SugarComponent, ("template" | "ofMembers" | "ofClass")
        | Sugar, ("armOf" | "show" | "showElse") -> true
        | _ -> false

    [<Literal>]
    let UnsafeHtmlPart = "Unsafe.html"

    [<Literal>]
    let WidgetPart = "Hooks.widget"

    /// The name of the content part that `expr` is: `UnsafeHtmlPart` or `WidgetPart`.
    let rec contentPart (expr: FSharpExpr) : string option =
        match expr with
        | Coerce (_, inner) -> contentPart inner
        | Call (_, m, _, _, _) when enclosing m = Unsafe && m.LogicalName = "html" -> Some UnsafeHtmlPart
        | Call (_, m, _, _, _) when enclosing m = Hooks && m.LogicalName = "widget" -> Some WidgetPart
        | _ -> None

    /// The void HTML elements.
    let voidTags =
        set
            [
                "area"
                "base"
                "br"
                "col"
                "embed"
                "hr"
                "img"
                "input"
                "link"
                "meta"
                "source"
                "track"
                "wbr"
            ]

    /// The raw-text HTML elements.
    let rawTextTags =
        set
            [
                "script"
                "style"
                "iframe"
                "noscript"
                "xmp"
                "noembed"
                "noframes"
                "plaintext"
            ]

    /// Whether `case` is the queued commit mode, which hydration refuses.
    let isQueuedCommits (case: FSharpUnionCase) : bool =
        case.Name = "QueuedCommits"
        && case.ReturnType.HasTypeDefinition
        && case.ReturnType.TypeDefinition.TryFullName = Some CommitMode

    /// Whether `m` returns a queued commit mode: `Microtask.mode`.
    let isQueuedModeFunction (m: FSharpMemberOrFunctionOrValue) : bool =
        isMember Microtask "mode" m

    /// Whether `m` is `Hydration.hydrateWith`, whose first argument is the commit mode.
    let isHydrateWith (m: FSharpMemberOrFunctionOrValue) : bool =
        isMember Hydration "hydrateWith" m

    /// The parameter name of a builder member's static value overload, beside its `read` overload.
    [<Literal>]
    let ValueParameter = "value"

    /// The parameter name of a builder member's reader overload.
    [<Literal>]
    let ReaderParameter = "read"

    /// Whether `m` yields or builds a text node: the element CE's `Yield` or the builder's `text`.
    let isTextMember (m: FSharpMemberOrFunctionOrValue) : bool =
        isMember Element "Yield" m
        || isMember Builder "text" m

    /// Whether `m` is a child of an element CE: its `Yield` or `YieldFrom`.
    let isChildYield (m: FSharpMemberOrFunctionOrValue) : bool =
        isMember Element "Yield" m
        || isMember Element "YieldFrom" m

    /// Whether `m` is the sugar's `renderOf`, whose props are fixed when the template is built.
    let isRenderOf (m: FSharpMemberOrFunctionOrValue) : bool =
        isMember Sugar "renderOf" m

    /// Whether `m` defines a component from a setup function: `Component.define` or the sugar's `Component.template`.
    let isSetupDefinition (m: FSharpMemberOrFunctionOrValue) : bool =
        isMember Component "define" m
        || isMember SugarComponent "template" m

    /// Whether `m` inlines a child template with a props reader: the sugar's or the builder's `render`.
    let isRender (m: FSharpMemberOrFunctionOrValue) : bool =
        isMember Sugar "render" m
        || isMember Builder "render" m

    /// Whether `m` renders a keyed list: the builder's `each`, the sugar's `each`, `forEach` and `forEachFixed`.
    let isKeyed (m: FSharpMemberOrFunctionOrValue) : bool =
        isMember Builder "each" m
        || isMember Sugar "each" m
        || isMember Sugar "forEach" m
        || isMember Sugar "forEachFixed" m

    /// Whether `m` is the element CE's `Delay`, or the body argument of its `For`: code that runs when the template is
    /// built, with the CE.
    let isElementBuildLambda (m: FSharpMemberOrFunctionOrValue) (index: int) : bool =
        isMember Element "Delay" m
        || (isMember Element "For" m && index = 1)

    /// Whether argument `index` of the element CE's member `m` is CE structure built once: `Run`, `Combine`, or the
    /// sequence of `For`.
    let isElementStructure (m: FSharpMemberOrFunctionOrValue) (index: int) : bool =
        isMember Element "Run" m
        || isMember Element "Combine" m
        || (isMember Element "For" m && index = 0)

    /// Whether `m` constructs a component tag: a type whose base types include `ComponentTag`.
    let isComponentTagConstructor (m: FSharpMemberOrFunctionOrValue) : bool =
        let rec inherits (t: FSharpType option) (depth: int) =
            match t with
            | Some t when depth < 8 && t.HasTypeDefinition ->
                t.TypeDefinition.TryFullName = Some ComponentTag
                || inherits t.TypeDefinition.BaseType (depth + 1)
            | _ -> false

        m.IsConstructor
        && (match m.ApparentEnclosingEntity with
            | Some entity -> inherits entity.BaseType 0
            | None -> false)

    /// Whether `m` is the sugar's `forEach`, whose rows follow the current item.
    let isForEach (m: FSharpMemberOrFunctionOrValue) : bool =
        isMember Sugar "forEach" m

/// The calls that make a list key unstable.
module Keys =
    /// Whether a call of `m` in a key function gives a new key on every pass.
    let impureCall (m: FSharpMemberOrFunctionOrValue) =
        match enclosing m, m.LogicalName with
        | "System.Guid", "NewGuid"
        | "System.DateTime", ("get_Now" | "get_UtcNow")
        | "System.Environment", ("get_TickCount" | "get_TickCount64")
        | "System.Object", ".ctor"
        | "Microsoft.FSharp.Core.FSharpRef`1", ("set_Value" | "set_contents")
        | Operators, (":=" | "op_ColonEquals" | "incr" | "decr") -> true
        | ("System.Random" | "System.Diagnostics.Stopwatch"), _ -> true
        | _ -> false

    /// Whether a call of `m` in a key function computes the item's position.
    let positionalCall (m: FSharpMemberOrFunctionOrValue) =
        match enclosing m, m.LogicalName with
        | ("Microsoft.FSharp.Collections.ArrayModule" | "Microsoft.FSharp.Collections.ListModule" | "Microsoft.FSharp.Collections.SeqModule"),
          ("findIndex" | "tryFindIndex")
        | ("System.Array" | "System.Collections.Generic.List`1" | "System.Collections.Generic.IList`1"), "IndexOf" -> true
        | _ -> false

    /// Whether `m` is FSharp.Core's `id`.
    let isIdentity (m: FSharpMemberOrFunctionOrValue) =
        isMember Operators "id" m

    /// Whether `m` overrides `Object.Equals` or implements an equality interface's `Equals`.
    let isEqualsOverride (m: FSharpMemberOrFunctionOrValue) =
        m.LogicalName = "Equals"
        && m.IsOverrideOrExplicitInterfaceImplementation
