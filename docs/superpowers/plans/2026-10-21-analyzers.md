# Ranvier Analyzers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An opt-in `Ranvier.Analyzers` package that reports the Fable, core and view traps of the spec as `RANnnn`
diagnostics in `dotnet build`, the `fsharp-analyzers` CLI and a SARIF report, with a CI gate over the repository.

**Architecture:** One analyzer assembly (`tools/Ranvier.Analyzers`) walks each file's typed tree once into a memoised
index of calls and `let` bindings, each with the path of steps (argument, receiver, lambda, binding) that leads to it.
Every rule is a pure function from that index to messages, gated on the project's references, and is exposed as one
`CliAnalyzer` and one `EditorAnalyzer`. An Expecto suite compiles fixtures against the real Ranvier assemblies; a
truth script checks the external facts the rules rely on (Fable inlining, Fable precompile, MSBuild integration).

**Tech Stack:** F# on .NET 10, FSharp.Analyzers.SDK 0.41.0, FSharp.Analyzers.SDK.Testing 0.41.0,
FSharp.Analyzers.Build 0.5.0, the `fsharp-analyzers` 0.41.0 CLI, Expecto 11.0.0-alpha8, Fable 5.18.0, Partas.Build
(`build.fsx`), GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-10-21-analyzers-design.md`

## Global Constraints

- FSharp.Analyzers.SDK 0.41.0, CLI `fsharp-analyzers` 0.41.0: FSharp.Compiler.Service `[43.12.402]`, FSharp.Core
  `[10.1.402]`, `net10.0`.
- `tools/Ranvier.Analyzers` holds every rule; `tools/Ranvier.Analyzers.Tests` holds the Expecto suite.
- One `[<CliAnalyzer>]` and one `[<EditorAnalyzer>]` entry per rule. `Message` carries no help link; the `HelpUri` of
  the analyzer attribute does: `https://ranvier-org.github.io/Ranvier/analyzers/ran001/` and so on.
- One shared traversal per file: a memoised index (a `ConditionalWeakTable` keyed on the
  `FSharpImplementationFileContents`) records, in one walk, each call with its lambda-context stack.
- A rule set runs only when the project's `-r:` options name `Ranvier.dll`, `Ranvier.View.Portable.dll` or
  `Fable.Core.dll`.
- The package's `build/` targets set `RunAnalyzersDuringBuild` and `FSharpAnalyzersContinueOnError=false` through
  FSharp.Analyzers.Build 0.5.0.
- `.config/dotnet-tools.json` gains `fsharp-analyzers` 0.41.0 with `rollForward: false`.
- The analyzer project overrides the repo's FSharp.Core 10.1.401 with 10.1.402.
- **Message.** One or two sentences: what the program does, then the fix, with the flagged symbol's own name
  substituted. Messages carry no "here"/"this line" deixis: the range locates them.
- **Range.** The smallest expression that is the cause: the read, the creator call, the tester property, the binding
  identifier.
- **Budget:** all rules together add at most 5 % to the CLI wall time of each gate project, and the `analyzers` CI job
  finishes in under 6 minutes including the solution build. The test suite times each rule over `Template.fs` and
  `Core.fs` and fails above 250 ms per file on the CI runner.
- **Suppression:** the SDK's comments, `// fsharpanalyzer: ignore-line RAN013`, `ignore-line-next`,
  `ignore-region-start`/`ignore-region-end`, `ignore-file`. The repository requires the reason on the preceding line.
- Each rule has at least three positive and three negative cases, the cases named in its section, and an exact-message
  assertion for one positive case. A fix is applied to the source and the result re-checked: it type-checks and the
  rule no longer fires.
- `Ranvier.Analyzers` has its own version, starting `0.1.0-preview.1`. Each release note names the SDK: "Built against
  FSharp.Analyzers.SDK 0.41.0".
- Symbol names come from `Symbols.fs` only.
- RAN030-RAN032 are out of this plan: they land with `Arms.Match` on `feat/arms-match` (spec Decision 17).
- Repository hazards: Fable 5.18 Release inlines a single-use immutable `let` snapshot of a module `mutable` (write
  `let mutable` snapshots); a union `.IsCase` tester breaks the Fable precompile (write `match`); never create
  `node_modules` junctions; serialise shared machine resources (timing runs, browser runs) with a `mkdir`/`rmdir`
  lock at `C:\Users\shaya\.claude\jobs\7fee9ca9\tmp\bench.lock`.
- Parallel efforts `feat/dom-twin` and `feat/arms-match` change `experiments/Ranvier.View.Portable` and
  `experiments/Ranvier.View.Foundation`. Each phase starts with `git fetch origin && git merge origin/master` (merge,
  never rebase), and Task 17 (the `ContentModel` extraction from `Template.fs`) merges master again immediately
  before its first edit.
- Shell commands carry the `rtk` prefix except `node`, `npm`, `npx`, `dotnet`, `gh` and `git commit`. Commit messages
  are written to a file under `C:\Users\shaya\.claude\jobs\7fee9ca9\tmp\` and committed with `git commit -F`, ending
  with the `Co-Authored-By` and `Claude-Session` trailers; `git diff --cached --check` is empty before each commit.
- Comments follow `.claude/rules/comments.md`; no `FOR-REVIEW` comment remains at a commit.

## Review Focus

1. A creator or context reached through a pipe (`(fun _ -> ...) |> createMemo`, `createMemo <| fun _ -> ...`) is
   reported exactly as the direct call is. Pinned by Task 1 ("a pipe is indexed as the call it applies") and Task 8
   ("a pipelined createMemo body is a pure context").
2. A user function or member named like a Ranvier API (a `createMemo` in the user's own module, a `Value` property on a
   user type) is never reported. Pinned by Task 8 ("a user function named createMemo is not a context") and Task 10
   ("a user Value property after an await is not a reactive read").
3. Code shapes the rules were not written for (object expressions, quotations, SRTP inline functions, active patterns,
   units of measure, `seq`/`async`/`task` expressions, records with generated members) never make a rule throw.
   Pinned by Task 1 ("the index and every rule survive unusual shapes") and Task 6 (the budget test runs every rule
   over `Core.fs` and `Template.fs`).
4. A suppression comment or `MSBuildWarningsAsMessages` silences a finding in `dotnet build` output, and a finding
   promoted to an error fails the build. Pinned by Task 5 (`Truth.fsx` build checks).
5. A change confined to `docs/content/analyzers/*.md` still runs the analyzer suite, which compiles each page's
   examples. Pinned by Task 7 (`changes.test.cjs`, "an analyzer rule page runs the analyzer suite").

---

## File structure

```text
tools/Ranvier.Analyzers/
  Ranvier.Analyzers.fsproj     net10.0 analyzer assembly (packable from Task 12)
  Symbols.fs                   symbol names, reference gates, the context tables of the core and view rules
  Index.fs                     the shared per-file walk: CallSite, LetSite, Step, ofFile, arguments, within, uses
  Rule.fs                      Input, Rule, message and fix helpers
  Rules/Fable.fs               RAN001, RAN002
  Rules/Core.fs                RAN010, RAN011, RAN012, RAN013, RAN015
  Rules/View.fs                RAN020-RAN025 (links experiments/Ranvier.View.Portable/ContentModel.fs from Task 17)
  Analyzers.fs                 the rule registry and one CliAnalyzer and one EditorAnalyzer per rule
  build/Ranvier.Analyzers.props, build/Ranvier.Analyzers.targets   (Task 12)
  README.md                    package readme (Task 12)
tools/Ranvier.Analyzers.Tests/
  Ranvier.Analyzers.Tests.fsproj   Expecto; FSharp.Analyzers.SDK.Testing 0.41.0; references the view assemblies
  Options.fs                   fixture options, contexts, fix application, repository paths
  IndexTests.fs                the index
  Ran001.fs ... Ran025.fs      one module per rule
  DocsExamples.fs              compiles each docs page's examples through its rule
  Budget.fs                    per-rule timing over Core.fs and Template.fs
  Main.fs                      Expecto entry point
tools/Ranvier.Analyzers.Truth/
  Truth.fsx                    external facts: Fable inlining, Fable precompile, MSBuild integration
  Inlining/, Testers/, GenericTesters/, Build/   fixture projects
docs/content/analyzers/index.md, ran001.md ... ran025.md
docs/research/analyzers/      the three probes of the spec
```

---

## Phase 0: scaffolding and the Fable rules

### Task 1: Analyzer and test projects with the shared index

**Files:**
- Create: `tools/Ranvier.Analyzers/Ranvier.Analyzers.fsproj`, `tools/Ranvier.Analyzers/Symbols.fs`,
  `tools/Ranvier.Analyzers/Index.fs`, `tools/Ranvier.Analyzers/Rule.fs`, `tools/Ranvier.Analyzers/Analyzers.fs`
- Create: `tools/Ranvier.Analyzers.Tests/Ranvier.Analyzers.Tests.fsproj`, `tools/Ranvier.Analyzers.Tests/Options.fs`,
  `tools/Ranvier.Analyzers.Tests/IndexTests.fs`, `tools/Ranvier.Analyzers.Tests/Main.fs`
- Create: `docs/research/analyzers/` (copies of the three probes)
- Modify: `Ranvier.slnx` (the `/tools/` folder)

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `Symbols.enclosing: FSharpMemberOrFunctionOrValue -> string`, `Symbols.isMember: string -> string ->
    FSharpMemberOrFunctionOrValue -> bool`, `Symbols.display: FSharpMemberOrFunctionOrValue -> string`,
    `Symbols.references: string -> AnalyzerProjectOptions -> bool`, `Symbols.Assemblies.{Ranvier, View, Foundation,
    Fable}`, `Symbols.Operators`.
  - `Index.Step` (`Argument of callee * index * count | Receiver of callee | Lambda of parameter option * immediate |
    Applied of parameter | Bound of value`), `Index.CallSite`, `Index.LetSite`, `Index.FileIndex`, `Index.ofFile:
    FSharpImplementationFileContents -> FileIndex`, `Index.arguments: (FSharpMemberOrFunctionOrValue -> bool) -> Step
    list -> (FSharpMemberOrFunctionOrValue * int * int * int) list`, `Index.within: Step list -> Step list -> Step list
    option`, `Index.uses: FSharpMemberOrFunctionOrValue -> FSharpExpr -> range list`.
  - `Rule.Input`, `Rule.Rule`, `Rule.text`, `Rule.message`, `Rule.replace`.
  - `Analyzers.rules: Rule list` (empty here), `Analyzers.runCli`, `Analyzers.runEditor`.
  - Test helpers `Options.ranvier`, `Options.plain`, `Options.withReferences`, `Options.context`, `Options.contextOf`,
    `Options.run`, `Options.applyFix`, `Options.repository`.

- [ ] **Step 1: Copy the probes and create the analyzer project**

```bash
rtk mkdir -p docs/research/analyzers
rtk cp -r C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/fableprobe docs/research/analyzers/fableprobe
rtk cp -r C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/fcsprobe docs/research/analyzers/fcsprobe
rtk cp -r C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/anaprobe docs/research/analyzers/anaprobe
rtk find docs/research/analyzers -type d \( -name bin -o -name obj -o -name out -o -name outd -o -name fable_modules \) -prune -exec rm -rf {} +
```

`tools/Ranvier.Analyzers/Ranvier.Analyzers.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <IsPackable>false</IsPackable>
    </PropertyGroup>
    <ItemGroup>
        <Compile Include="Symbols.fs" />
        <Compile Include="Index.fs" />
        <Compile Include="Rule.fs" />
        <Compile Include="Analyzers.fs" />
    </ItemGroup>
    <ItemGroup>
        <PackageReference Include="FSharp.Analyzers.SDK" Version="0.41.0" PrivateAssets="all" />
        <PackageReference Update="FSharp.Core" Version="10.1.402" />
    </ItemGroup>
</Project>
```

`tools/Ranvier.Analyzers/Symbols.fs`:

```fsharp
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
    let View = "Ranvier.View.Portable.dll"

    [<Literal>]
    let Foundation = "Ranvier.View.Foundation.dll"

    [<Literal>]
    let Fable = "Fable.Core.dll"
```

`tools/Ranvier.Analyzers/Index.fs`:

```fsharp
/// The calls and `let` bindings of an implementation file, each with the steps that lead from it to the body of its
/// declaration. One walk per file serves every rule.
module Ranvier.Analyzers.Index

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

[<NoEquality; NoComparison>]
type FileIndex =
    {
        Calls: CallSite array
        Lets: LetSite array
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
    m.LogicalName = name && Symbols.enclosing m = Symbols.Operators

let private build (file: FSharpImplementationFileContents) : FileIndex =
    let calls = ResizeArray<CallSite> ()
    let lets = ResizeArray<LetSite> ()

    let rec call path range receiver (callee: FSharpMemberOrFunctionOrValue) (arguments: FSharpExpr list) =
        calls.Add
            {
                Callee = callee
                Receiver = receiver
                Arguments = arguments
                Range = range
                Path = path
            }

        receiver |> Option.iter (walk (Step.Receiver callee :: path))

        arguments
        |> List.iteri (fun index argument -> walk (Step.Argument (callee, index, arguments.Length) :: path) argument)

    and apply path (expr: FSharpExpr) (fn: FSharpExpr) (arguments: FSharpExpr list) =
        match redex fn arguments with
        | Some (receiver, callee, substituted) -> call path expr.Range receiver callee substituted
        | None ->
            match fn with
            | Lambda (parameter, body) ->
                arguments |> List.iter (walk (Step.Applied parameter :: path))
                walk (Step.Lambda (Some parameter, true) :: path) body
            | _ ->
                walk path fn
                arguments |> List.iter (walk path)

    and walk (path: Step list) (expr: FSharpExpr) =
        match expr with
        | Call (None, pipe, _, _, [ argument; fn ]) when isPipe "op_PipeRight" pipe -> apply path expr fn [ argument ]
        | Call (None, pipe, _, _, [ fn; argument ]) when isPipe "op_PipeLeft" pipe -> apply path expr fn [ argument ]
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
        | FSharpImplementationFileDeclaration.Entity (_, items) -> List.iter declaration items
        | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue (value, _, body) ->
            if not value.IsCompilerGenerated then
                walk [] body
        | FSharpImplementationFileDeclaration.InitAction body -> walk [] body

    List.iter declaration file.Declarations

    {
        Calls = calls.ToArray ()
        Lets = lets.ToArray ()
    }

let private cache = ConditionalWeakTable<FSharpImplementationFileContents, FileIndex> ()

/// The index of `file`, built on first use and shared by every rule.
let ofFile (file: FSharpImplementationFileContents) : FileIndex =
    cache.GetValue (file, ConditionalWeakTable.CreateValueCallback build)

/// The call arguments on `path`, innermost first, each with the number of lambdas between it and the expression.
/// Immediately applied lambdas, and the lambda argument of a call that `transparent` accepts, are not counted.
let arguments
    (transparent: FSharpMemberOrFunctionOrValue -> bool)
    (path: Step list)
    : (FSharpMemberOrFunctionOrValue * int * int * int) list =
    let rec go (lambdaCount: int) (steps: Step list) =
        match steps with
        | [] -> []
        | Step.Lambda (_, true) :: rest -> go lambdaCount rest
        | Step.Lambda (_, false) :: Step.Argument (callee, _, _) :: rest when transparent callee -> go lambdaCount rest
        | Step.Lambda (_, false) :: rest -> go (lambdaCount + 1) rest
        | Step.Argument (callee, index, count) :: rest -> (callee, index, count, lambdaCount) :: go lambdaCount rest
        | _ :: rest -> go lambdaCount rest

    go 0 path

/// The steps of `path` inside `ancestor`, innermost first, when `ancestor` is a tail of `path` by reference.
let rec within (ancestor: Step list) (path: Step list) : Step list option =
    if obj.ReferenceEquals (path, ancestor) then
        Some []
    else
        match path with
        | [] -> None
        | step :: rest -> within ancestor rest |> Option.map (fun inner -> step :: inner)

/// The ranges of the uses of `value` in `expr`, lambda bodies included.
let rec uses (value: FSharpMemberOrFunctionOrValue) (expr: FSharpExpr) : range list =
    let own =
        match expr with
        | Value used when used = value -> [ expr.Range ]
        | _ -> []

    own @ List.collect (uses value) expr.ImmediateSubExpressions
```

`tools/Ranvier.Analyzers/Rule.fs`:

```fsharp
/// The input every rule reads, and the helpers that build its messages.
module Ranvier.Analyzers.Rule

open FSharp.Analyzers.SDK
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text

/// A file's typed tree and source, with the shared index of the tree.
[<NoEquality; NoComparison>]
type Input =
    {
        FileName: string
        Source: ISourceText
        Tree: FSharpImplementationFileContents
        Index: Index.FileIndex
        Options: AnalyzerProjectOptions
        /// The project's check results; the CLI always has them, an editor context may not.
        Project: FSharpCheckProjectResults option
    }

/// A rule: its code and title, the assembly whose reference enables it, and its findings in a file.
[<NoEquality; NoComparison>]
type Rule =
    {
        Code: string
        Title: string
        Gate: string
        Run: Input -> Message list
    }

/// The source text of `range`.
let text (input: Input) (range: range) : string = input.Source.GetSubTextFromRange range

/// A message of `code` at `range`.
let message (code: string) (severity: Severity) (range: range) (text: string) (fixes: Fix list) : Message =
    {
        Type = code
        Message = text
        Code = code
        Severity = severity
        Range = range
        Fixes = fixes
    }

/// A fix that replaces `original`, the text of `range`, with `replacement`.
let replace (range: range) (original: string) (replacement: string) : Fix =
    {
        FromRange = range
        FromText = original
        ToText = replacement
    }
```

`tools/Ranvier.Analyzers/Analyzers.fs`:

```fsharp
/// The rule registry, and one CLI and one editor entry per rule.
module Ranvier.Analyzers.Analyzers

open FSharp.Analyzers.SDK
open Ranvier.Analyzers.Rule

/// Every rule, in code order.
let rules: Rule list = []

/// The findings of `rule` in a CLI context; empty when the project lacks the rule's gate reference.
let runCli (rule: Rule) (context: CliContext) : Message list =
    match context.TypedTree with
    | Some tree when Symbols.references rule.Gate context.ProjectOptions ->
        rule.Run
            {
                FileName = context.FileName
                Source = context.SourceText
                Tree = tree
                Index = Index.ofFile tree
                Options = context.ProjectOptions
                Project = Some context.CheckProjectResults
            }
    | _ -> []

let private ignoresFile (code: string) (ranges: Map<string, AnalyzerIgnoreRange list>) =
    ranges
    |> Map.tryFind code
    |> Option.exists (
        List.exists (fun range ->
            match range with
            | AnalyzerIgnoreRange.File -> true
            | _ -> false)
    )

/// The findings of `rule` in an editor context; empty for a file that an `ignore-file` comment exempts.
let runEditor (rule: Rule) (context: EditorContext) : Message list =
    match context.TypedTree with
    | Some tree when
        Symbols.references rule.Gate context.ProjectOptions
        && not (ignoresFile rule.Code context.AnalyzerIgnoreRanges)
        ->
        rule.Run
            {
                FileName = context.FileName
                Source = context.SourceText
                Tree = tree
                Index = Index.ofFile tree
                Options = context.ProjectOptions
                Project = context.CheckProjectResults
            }
    | _ -> []
```

- [ ] **Step 2: Create the test project and helpers**

`tools/Ranvier.Analyzers.Tests/Ranvier.Analyzers.Tests.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <IsPackable>false</IsPackable>
        <GenerateProgramFile>false</GenerateProgramFile>
        <GenerateDocumentationFile>false</GenerateDocumentationFile>
    </PropertyGroup>
    <ItemGroup>
        <Compile Include="Options.fs" />
        <Compile Include="IndexTests.fs" />
        <Compile Include="Main.fs" />
    </ItemGroup>
    <ItemGroup>
        <PackageReference Include="Expecto" Version="11.0.0-alpha8" />
        <PackageReference Include="YoloDev.Expecto.TestSdk" Version="0.15.5" />
        <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.4.0" />
        <PackageReference Include="FSharp.Analyzers.SDK.Testing" Version="0.41.0" />
        <PackageReference Update="FSharp.Core" Version="10.1.402" />
    </ItemGroup>
    <ItemGroup>
        <ProjectReference Include="../Ranvier.Analyzers/Ranvier.Analyzers.fsproj" />
        <!-- Copies Ranvier, the view assemblies, Fable.Core and Xantham into the output directory, where the fixtures
             reference them. -->
        <ProjectReference Include="../../experiments/Ranvier.View.Foundation/Ranvier.View.Foundation.fsproj" />
    </ItemGroup>
</Project>
```

`tools/Ranvier.Analyzers.Tests/Options.fs`:

```fsharp
/// Project options, contexts and source edits for the rule fixtures.
module Ranvier.Analyzers.Tests.Options

open System
open System.IO
open FSharp.Analyzers.SDK
open FSharp.Analyzers.SDK.Testing
open FSharp.Compiler.CodeAnalysis

// getContext and getContextFor load the analyzers of the working directory, which the output directory holds.
Environment.CurrentDirectory <- AppContext.BaseDirectory

/// The repository root: the nearest ancestor of the output directory that holds Ranvier.slnx.
let repository: string =
    let rec up (directory: DirectoryInfo) =
        if File.Exists (Path.Combine (directory.FullName, "Ranvier.slnx")) then
            directory.FullName
        else
            up directory.Parent

    up (DirectoryInfo AppContext.BaseDirectory)

let private baseOptions =
    lazy (mkOptionsFromProject "net10.0" [] |> Async.AwaitTask |> Async.RunSynchronously)

/// The base project options with a `-r:` option for each of `assemblies`, files of the output directory.
let withReferences (assemblies: string list) : FSharpProjectOptions =
    let options = baseOptions.Force ()

    {
        options with
            OtherOptions =
                Array.append
                    options.OtherOptions
                    [| for assembly in assemblies -> "-r:" + Path.Combine (AppContext.BaseDirectory, assembly) |]
    }

/// Options that reference Ranvier, the view assemblies and Fable.Core.
let ranvier: Lazy<FSharpProjectOptions> =
    lazy
        (withReferences
            [
                "Ranvier.dll"
                "Ranvier.View.Portable.dll"
                "Ranvier.View.Foundation.dll"
                "Fable.Ranvier.dll"
                "Fable.Core.dll"
                "Xantham.Fable.Core.dll"
                "Xantham.Fable.Core.TS.dll"
            ])

/// Options that reference no Ranvier or Fable assembly.
let plain: Lazy<FSharpProjectOptions> = lazy (withReferences [])

/// The context of `source` compiled as the single file of a project; raises on a type error.
let context (options: FSharpProjectOptions) (source: string) : CliContext = getContext options source

/// The context of the last of `files`, each a file name and its source, compiled in order.
let contextOf (options: FSharpProjectOptions) (files: (string * string) list) : CliContext =
    let sources = [ for name, source in files -> { FileName = name; Source = source } ]

    getContextFor (AnalyzerProjectOptions.BackgroundCompilerOptions options) sources (List.last sources)
    |> Async.AwaitTask
    |> Async.RunSynchronously

/// The findings of `analyzer` in `context`.
let run (analyzer: Analyzer<CliContext>) (context: CliContext) : Message list =
    analyzer context |> Async.RunSynchronously

/// `source` with `fix` applied; line ends are normalised to `\n`.
let applyFix (source: string) (fix: Fix) : string =
    let normal = source.Replace ("\r\n", "\n")
    let lines = normal.Split '\n'

    let offset (line: int) (column: int) =
        (lines |> Array.take (line - 1) |> Array.sumBy (fun text -> text.Length + 1)) + column

    let start = offset fix.FromRange.StartLine fix.FromRange.StartColumn
    let finish = offset fix.FromRange.EndLine fix.FromRange.EndColumn
    normal.Substring (0, start) + fix.ToText + normal.Substring finish
```

`tools/Ranvier.Analyzers.Tests/Main.fs`:

```fsharp
module Ranvier.Analyzers.Tests.Main

open Expecto

[<EntryPoint>]
let main argv = runTestsInAssemblyWithCLIArgs [] argv
```

Add both projects to the `/tools/` folder of `Ranvier.slnx`:

```xml
  <Folder Name="/tools/">
    <Project Path="tools/Ranvier.Dom.Generate/Ranvier.Dom.Generate.fsproj" />
    <Project Path="tools/Ranvier.Analyzers/Ranvier.Analyzers.fsproj" />
    <Project Path="tools/Ranvier.Analyzers.Tests/Ranvier.Analyzers.Tests.fsproj" />
  </Folder>
```

- [ ] **Step 3: Write the failing index tests**

`tools/Ranvier.Analyzers.Tests/IndexTests.fs`:

```fsharp
module Ranvier.Analyzers.Tests.IndexTests

open Expecto
open FSharp.Compiler.Symbols.FSharpExprPatterns
open Ranvier.Analyzers
open Ranvier.Analyzers.Index
open Ranvier.Analyzers.Tests.Options

let private index (source: string) =
    let context = context (ranvier.Force ()) source
    context.TypedTree.Value |> Index.ofFile

let private callsOf (name: string) (file: FileIndex) =
    file.Calls |> Array.filter (fun site -> site.Callee.LogicalName = name)

/// Shapes the rules were not written for; every rule must return without raising.
let unusual =
    """
module Fixture

open System
open Ranvier

[<Measure>]
type ms

type Point = { X: int; Y: int }

type Shape =
    | Circle of float
    | Square of float

type Alias = int list

let (|Even|Odd|) n = if n % 2 = 0 then Even else Odd
let inline twice (x: ^T) = x + x

let disposable () =
    { new IDisposable with
        member _.Dispose() = () }

let quoted = <@ fun (x: int) -> x + 1 @>
let delay = 5<ms>
let numbers = seq { for i in 1..3 -> i * 2 }
let work = async { return 1 }

let running () =
    task {
        let! x = Threading.Tasks.Task.FromResult 1
        return x
    }

let classify n =
    match n with
    | Even -> "even"
    | Odd -> "odd"

let area shape =
    match shape with
    | Circle r -> r * r
    | Square s -> s * s

let memo () = createMemo (fun _ -> twice 2)
"""

[<Tests>]
let tests =
    testList
        "Index"
        [
            test "a curried member call keeps its literal arguments" {
                let file =
                    index
                        """
module Fixture
open Ranvier.View.Foundation.Template
let view = Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.text "x" |])
"""

                let div = callsOf "div" file |> Array.exactlyOne
                Expect.equal div.Arguments.Length 2 "parts and children"

                match div.Arguments[1] with
                | NewArray (_, [ _ ]) -> ()
                | other -> failtestf "the children were %A" other
            }

            test "a pipe is indexed as the call it applies" {
                let file =
                    index
                        """
module Fixture
open Ranvier
let memo () = (fun _ -> 1) |> createMemo
let back () = createMemo <| fun _ -> 2
"""

                let memos = callsOf "createMemo" file
                Expect.equal memos.Length 2 "both pipes"

                for site in memos do
                    match site.Arguments with
                    | [ Lambda _ ] -> ()
                    | other -> failtestf "the arguments were %A" other
            }

            test "a call's path names each enclosing argument and lambda" {
                let file =
                    index
                        """
module Fixture
open Ranvier
let memo () = createMemo (fun _ -> (untrack (fun () -> createSignal 1)).Value)
"""

                let signal = callsOf "createSignal" file |> Array.exactlyOne

                match signal.Path with
                | [ Step.Lambda (_, false)
                    Step.Argument (untrack, 0, 1)
                    Step.Receiver value
                    Step.Lambda (_, false)
                    Step.Argument (memo, 0, 1) ] ->
                    Expect.equal untrack.LogicalName "untrack" "untrack's lambda"
                    Expect.equal value.LogicalName "get_Value" "the Value receiver"
                    Expect.equal memo.LogicalName "createMemo" "createMemo's compute"
                | other -> failtestf "the path was %A" other
            }

            test "arguments skips transparent lambdas and counts the others" {
                let file =
                    index
                        """
module Fixture
open Ranvier
let memo () = createMemo (fun _ -> (untrack (fun () -> createSignal 1)).Value)
"""

                let signal = callsOf "createSignal" file |> Array.exactlyOne
                let transparent (m: FSharp.Compiler.Symbols.FSharpMemberOrFunctionOrValue) = m.LogicalName = "untrack"

                let names =
                    Index.arguments transparent signal.Path
                    |> List.map (fun (callee, index, _, lambdas) -> callee.LogicalName, index, lambdas)

                Expect.equal names [ "get_Value", 0, 0; "createMemo", 0, 1 ] "untrack is skipped"
            }

            test "a let body shares the let's path by reference" {
                let file =
                    index
                        """
module Fixture
open Ranvier
let memo () =
    createMemo (fun _ ->
        let s = createSignal 1
        s.Value)
"""

                let binding = file.Lets |> Array.find (fun site -> site.Value.DisplayName = "s")
                let read = callsOf "get_Value" file |> Array.exactlyOne
                Expect.equal (Index.within binding.Path read.Path |> Option.map List.length) (Some 1) "the receiver step"
            }

            test "the index is built once per file" {
                let tree =
                    (context (ranvier.Force ()) "module Fixture\nlet x = 1").TypedTree.Value

                Expect.isTrue (obj.ReferenceEquals (Index.ofFile tree, Index.ofFile tree)) "the cached index"
            }

            test "the index and every rule survive unusual shapes" {
                let context = context (ranvier.Force ()) unusual
                Index.ofFile context.TypedTree.Value |> ignore

                for rule in Analyzers.rules do
                    Analyzers.runCli rule context |> ignore
            }
        ]
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`
Expected: FAIL to build until Step 1-2 files exist; once they compile, all 7 cases run. If a case fails, the index has a
defect: fix `Index.fs`, never the expectation, unless the typed tree's shape differs from the comment in `redex`, in
which case record the observed shape in the ledger as a ruling and adjust both.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`
Expected: `Passed!` with 7 passed, 0 failed.

- [ ] **Step 6: Format, check and commit**

```bash
dotnet fantomas tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/research/analyzers
rtk git add tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/research/analyzers Ranvier.slnx
rtk git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-1-commit.txt
```

Commit subject: `feat(analyzers): analyzer and test projects with the shared typed-tree index`.

### Task 2: RAN002, generated union case testers

**Files:**
- Create: `tools/Ranvier.Analyzers/Rules/Fable.fs`
- Modify: `tools/Ranvier.Analyzers/Ranvier.Analyzers.fsproj` (compile `Rules/Fable.fs` before `Analyzers.fs`),
  `tools/Ranvier.Analyzers/Analyzers.fs`
- Create: `tools/Ranvier.Analyzers.Tests/Ran002.fs`, `tools/Ranvier.Analyzers.Tests/DocsExamples.fs`
- Create: `docs/content/analyzers/index.md`, `docs/content/analyzers/ran002.md`
- Modify: `docs/Site.fs` (an `analyzers` menu after the `fable` menu)

**Interfaces:**
- Consumes: `Index.ofFile`, `Rule.*`, `Analyzers.runCli`, `Options.*` (Task 1).
- Produces: `Rules.Fable.ran002: Rule`; `Analyzers.ran002Cli: Analyzer<CliContext>`, `Analyzers.ran002Editor:
  Analyzer<EditorContext>`; `DocsExamples.examples: string -> (string * string) list` (the `Triggers` and `Fixed`
  fenced blocks of a page) and `DocsExamples.shared: string -> (string * string) option` (a `### Shared: <file>` block
  compiled before each example), used by every later rule task.

- [ ] **Step 1: Write the failing tests**

`tools/Ranvier.Analyzers.Tests/Ran002.fs`:

```fsharp
module Ranvier.Analyzers.Tests.Ran002

open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private findings (source: string) =
    run Analyzers.ran002Cli (context (ranvier.Force ()) source)

let private policy =
    """
module Fixture
type Policy =
    | Queue
    | Drop of int
    member this.IsUserDefined = true
"""

[<Tests>]
let tests =
    testList
        "RAN002"
        [
            test "a tester of a case without fields, with its exact message" {
                let messages = findings (policy + "let isQueue (policy: Policy) = policy.IsQueue\n")

                Expect.equal
                    (messages |> List.map _.Message)
                    [
                        "`policy.IsQueue` calls a compiler-generated union tester, which a Fable precompiled assembly does not declare. Write `(match policy with Fixture.Policy.Queue -> true | _ -> false)`."
                    ]
                    "one message"
            }

            test "a tester of a case with fields" {
                let messages = findings (policy + "let isDrop (policy: Policy) = policy.IsDrop\n")
                Expect.equal (messages |> List.map _.Code) [ "RAN002" ] "one finding"
                Expect.stringContains messages.Head.Message "Fixture.Policy.Drop _ -> true" "the field wildcard"
            }

            test "a tester of a struct union" {
                let source =
                    "module Fixture\n[<Struct>]\ntype Flag =\n    | On\n    | Off\nlet on (flag: Flag) = flag.IsOn\n"

                Expect.equal (findings source |> List.map _.Code) [ "RAN002" ] "one finding"
            }

            test "a tester of a generic union" {
                let source =
                    "module Fixture\ntype Outcome<'T> =\n    | Done of 'T\n    | Failed of exn\nlet finished (o: Outcome<int>) = o.IsDone\n"

                Expect.equal (findings source |> List.map _.Code) [ "RAN002" ] "one finding"
            }

            test "a tester of an internal union" {
                let source =
                    "module Fixture\ntype internal Mode =\n    | Fast\n    | Slow\nlet internal fast (mode: Mode) = mode.IsFast\n"

                Expect.equal (findings source |> List.map _.Code) [ "RAN002" ] "one finding"
            }

            test "the regression seed of 2cf84be fires" {
                let source =
                    """
module Fixture
type FlightPolicy =
    | CancelPrevious
    | FinishCurrent
let trace (graph: obj, id: int, gen: int, owed: bool, finish: bool) = ()
let drop (policy: FlightPolicy) graph id gen owed = trace (graph, id, gen, owed, policy.IsFinishCurrent)
"""

                Expect.equal (findings source |> List.map _.Code) [ "RAN002" ] "Core.fs:4545 before 2cf84be"
            }

            test "a user-written IsX member is not a generated tester" {
                Expect.isEmpty (findings (policy + "let custom (policy: Policy) = policy.IsUserDefined\n")) "no finding"
            }

            test "Option.IsSome is not a generated tester" {
                Expect.isEmpty (findings "module Fixture\nlet some = (Some 1).IsSome\n") "no finding"
            }

            test "a tester of a union from a referenced assembly is out of scope" {
                Expect.isEmpty
                    (findings "module Fixture\nlet queued (policy: Ranvier.FlightPolicy) = policy.IsQueue\n")
                    "Ranvier's FlightPolicy"
            }

            test "a project without Fable.Core is not analyzed" {
                let source = policy + "let isQueue (policy: Policy) = policy.IsQueue\n"
                Expect.isEmpty (run Analyzers.ran002Cli (context (plain.Force ()) source)) "no Fable.Core reference"
            }

            test "the fix type-checks and clears the finding" {
                let source = policy + "let isDrop (policy: Policy) = policy.IsDrop\n"
                let fix = (findings source).Head.Fixes |> List.exactlyOne
                let fixed = applyFix source fix
                Expect.stringContains fixed "(match policy with Fixture.Policy.Drop _ -> true | _ -> false)" "the match"
                Expect.isEmpty (findings fixed) "no finding after the fix"
            }
        ]
```

`tools/Ranvier.Analyzers.Tests/DocsExamples.fs`:

```fsharp
/// Compiles the examples of each rule page through its rule: a `Triggers` example reports the page's code, a `Fixed`
/// example reports nothing.
module Ranvier.Analyzers.Tests.DocsExamples

open System.IO
open System.Text.RegularExpressions
open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private block (text: string) (heading: string) =
    let section =
        Regex.Match (text, $"### {Regex.Escape heading}\n+```fsharp\n(.*?)\n```", RegexOptions.Singleline)

    if section.Success then
        Some (section.Groups[1].Value + "\n")
    else
        None

/// The fenced F# blocks under the page's `### Triggers` and `### Fixed` headings, each with its heading.
let examples (page: string) : (string * string) list =
    let text = File.ReadAllText(page).Replace ("\r\n", "\n")

    [
        for heading in [ "Triggers"; "Fixed" ] do
            match block text heading with
            | Some source -> heading, source
            | None -> ()
    ]

/// The block under a `### Shared: <file>` heading, compiled before each example: the file name and its source.
let shared (page: string) : (string * string) option =
    let text = File.ReadAllText(page).Replace ("\r\n", "\n")
    let heading = Regex.Match (text, "### Shared: (\\S+)")

    if heading.Success then
        let file = heading.Groups[1].Value
        block text $"Shared: {file}" |> Option.map (fun source -> file, source)
    else
        None

let private pages () =
    Directory.GetFiles (Path.Combine (repository, "docs/content/analyzers"), "ran*.md") |> Array.sort

[<Tests>]
let tests =
    testList
        "Docs examples"
        [
            for page in pages () do
                let code = (Path.GetFileNameWithoutExtension page).ToUpperInvariant ()

                test code {
                    let rule = Analyzers.rules |> List.find (fun rule -> rule.Code = code)
                    let blocks = examples page
                    Expect.equal (blocks |> List.map fst) [ "Triggers"; "Fixed" ] "both examples"

                    for heading, source in blocks do
                        let compiled =
                            match shared page with
                            | Some file -> contextOf (ranvier.Force ()) [ file; "Example.fs", source ]
                            | None -> context (ranvier.Force ()) source

                        let codes = Analyzers.runCli rule compiled |> List.map _.Code

                        if heading = "Triggers" then
                            Expect.contains codes code "the example triggers the rule"
                        else
                            Expect.isEmpty codes "the fixed example is clean"
                }
        ]
```

Add `Ran002.fs` and `DocsExamples.fs` to the test project's `Compile` items, after `IndexTests.fs` and before
`Main.fs`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`
Expected: FAIL to compile: `The value, constructor, namespace or type 'ran002Cli' is not defined`.

- [ ] **Step 3: Implement RAN002**

`tools/Ranvier.Analyzers/Rules/Fable.fs`:

```fsharp
/// RAN001 and RAN002: code that a Fable build compiles to different behaviour, or fails to precompile.
module Ranvier.Analyzers.Rules.Fable

open FSharp.Analyzers.SDK
open FSharp.Compiler.Symbols
open Ranvier.Analyzers
open Ranvier.Analyzers.Rule

[<Literal>]
let private CompilerGenerated = "System.Runtime.CompilerServices.CompilerGeneratedAttribute"

/// The union and case of a compiler-generated `IsCase` getter of a union declared in the analyzed assembly.
let private generatedTester (m: FSharpMemberOrFunctionOrValue) : (FSharpEntity * FSharpUnionCase) option =
    match m.DeclaringEntity with
    | Some union when m.IsPropertyGetterMethod && union.IsFSharpUnion && union.Assembly.FileName.IsNone ->
        let generated =
            m.Attributes
            |> Seq.exists (fun attribute -> attribute.AttributeType.TryFullName = Some CompilerGenerated)

        if generated then
            union.UnionCases
            |> Seq.tryFind (fun case -> m.LogicalName = "get_Is" + case.Name)
            |> Option.map (fun case -> union, case)
        else
            None
    | _ -> None

/// The pattern of `case`, qualified by the union's access path; a case with fields takes one wildcard.
let private casePattern (union: FSharpEntity) (case: FSharpUnionCase) : string =
    let name = union.AccessPath + "." + union.DisplayName + "." + case.Name
    if case.Fields.Count = 0 then name else name + " _"

let private testers (input: Input) : Message list =
    [
        for site in input.Index.Calls do
            match generatedTester site.Callee, site.Receiver with
            | Some (union, case), Some receiver ->
                let receiverText = text input receiver.Range
                let replacement = $"(match {receiverText} with {casePattern union case} -> true | _ -> false)"

                message
                    "RAN002"
                    Severity.Warning
                    site.Range
                    $"`{receiverText}.Is{case.Name}` calls a compiler-generated union tester, which a Fable precompiled assembly does not declare. Write `{replacement}`."
                    [ replace site.Range (text input site.Range) replacement ]
            | _ -> ()
    ]

let ran002: Rule =
    {
        Code = "RAN002"
        Title = "Generated union case tester in a Fable library"
        Gate = Symbols.Assemblies.Fable
        Run = testers
    }
```

In `Ranvier.Analyzers.fsproj`, add `<Compile Include="Rules/Fable.fs" />` after `Rule.fs`. In `Analyzers.fs`, set
`let rules: Rule list = [ Rules.Fable.ran002 ]` and append:

```fsharp
[<CliAnalyzer("RAN002",
              "Generated union case tester in a Fable library",
              "https://ranvier-org.github.io/Ranvier/analyzers/ran002/")>]
let ran002Cli: Analyzer<CliContext> =
    fun context -> async { return runCli Rules.Fable.ran002 context }

[<EditorAnalyzer("RAN002",
                 "Generated union case tester in a Fable library",
                 "https://ranvier-org.github.io/Ranvier/analyzers/ran002/")>]
let ran002Editor: Analyzer<EditorContext> =
    fun context -> async { return runEditor Rules.Fable.ran002 context }
```

- [ ] **Step 4: Write the docs pages**

`docs/content/analyzers/index.md`:

````markdown
---
title: Analyzers
order: 1
---

:::info
Preview: the analyzers run in this repository and ship in `Ranvier.Analyzers` after phase 1.
:::

`Ranvier.Analyzers` reports Ranvier traps at build time, at the source range of the cause. Each code below links to its
cause, its fix and an example.

| Code | Title | Severity |
|---|---|---|
| [RAN002](ran002.md) | Generated union case tester in a Fable library | Warning |

## Running the analyzers

In this repository, after `dotnet build Ranvier.slnx -c Release`:

```bash
dotnet fsi build.fsx -- analyze
```

The command builds the analyzers, runs them over the repository's projects with every code promoted to an error, and
writes `artifacts/analyzers/ranvier.sarif`. A project that sets `<RanvierAnalyze>true</RanvierAnalyze>` reports them in
its own `dotnet build` output after each compile.

## Suppression

Write the reason on the line before the SDK's comment:

```fsharp
// The snapshot is read before any write, so Fable's inlining leaves the value unchanged.
// fsharpanalyzer: ignore-line-next RAN001
let setups = Loops.childSetups
```

`ignore-line`, `ignore-region-start`/`ignore-region-end` and `ignore-file` take the same codes.
````

`docs/content/analyzers/ran002.md`:

````markdown
---
title: RAN002 Generated union case tester
order: 3
---

`warning RAN002`: *`policy.IsQueue` calls a compiler-generated union tester, which a Fable precompiled assembly does
not declare.*

**Cause.** F# generates an `IsCase` property for each union case. A Fable precompile of the library that calls one
fails with `MethodDefNotFound` for the getter, for public, internal and private unions alike (Fable 5.18.0). The rule
reports every tester of a union declared in the project, generic unions included.

**Fix.** Match on the case. The rule's fix writes the `match`.

### Triggers

```fsharp
module Example

type Policy =
    | Queue
    | Drop of int

let queued (policy: Policy) = policy.IsQueue
```

### Fixed

```fsharp
module Example

type Policy =
    | Queue
    | Drop of int

let queued (policy: Policy) =
    match policy with
    | Queue -> true
    | _ -> false
```

**Suppression.** `// fsharpanalyzer: ignore-line RAN002`, with the reason on the line before.
````

In `docs/Site.fs`, after `>> Theme.menu "fable" [ Menu.section "Fable" [ Menu.page "fable/index.md" ] ]`, add:

```fsharp
        >> Theme.menu
            "analyzers"
            [
                Menu.section "Analyzers" [ Menu.page "analyzers/index.md"; Menu.page "analyzers/ran002.md" ]
            ]
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`
Expected: `Passed!`, 0 failed; the RAN002 list has 11 cases and `Docs examples` has `RAN002`.

- [ ] **Step 6: Build the docs site**

Run: `dotnet fsi build.fsx -- docs`
Expected: the build finishes; the link validator reports no broken link under `analyzers/`.

- [ ] **Step 7: Format, check and commit**

```bash
dotnet fantomas tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/Site.fs
rtk git add tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/content/analyzers docs/Site.fs
rtk git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-2-commit.txt
```

Commit subject: `feat(analyzers): RAN002 reports generated union case testers`.

### Task 3: RAN001, Fable snapshot inlining of module mutables

**Files:**
- Modify: `tools/Ranvier.Analyzers/Rules/Fable.fs`, `tools/Ranvier.Analyzers/Analyzers.fs`
- Create: `tools/Ranvier.Analyzers.Tests/Ran001.fs` (compiled before `DocsExamples.fs`)
- Create: `docs/content/analyzers/ran001.md`
- Modify: `docs/content/analyzers/index.md` (table row), `docs/Site.fs` (menu page)

**Interfaces:**
- Consumes: `Index.FileIndex.Lets`, `Index.uses`, `Options.contextOf` (Task 1).
- Produces: `Rules.Fable.ran001: Rule`; `Analyzers.ran001Cli`, `Analyzers.ran001Editor`.

- [ ] **Step 1: Write the failing tests**

`tools/Ranvier.Analyzers.Tests/Ran001.fs`:

```fsharp
module Ranvier.Analyzers.Tests.Ran001

open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private loops =
    "module Loops\nlet mutable childSetups = 0\nlet setup () = childSetups <- childSetups + 1\n"

let private findingsWith options (probe: string) =
    run Analyzers.ran001Cli (contextOf options [ "Loops.fs", loops; "Probe.fs", probe ])

let private findings (probe: string) = findingsWith (ranvier.Force ()) probe

let private snapshot =
    "module Probe\nlet snapshot () =\n    let setups = Loops.childSetups\n    Loops.setup ()\n    Loops.childSetups - setups\n"

[<Tests>]
let tests =
    testList
        "RAN001"
        [
            test "a single-use snapshot in another file, with its exact message" {
                Expect.equal
                    (findings snapshot |> List.map _.Message)
                    [
                        "Fable Release compiles `setups` as a second read of `Loops.childSetups` at its single use (line 5), so writes between the two lines change it. Declare it `let mutable setups` to keep the snapshot."
                    ]
                    "one message"
            }

            test "the use inside a closure" {
                let probe =
                    "module Probe\nlet closure () =\n    let setups = Loops.childSetups\n    Loops.setup ()\n    fun () -> Loops.childSetups - setups\n"

                Expect.equal (findings probe |> List.map _.Code) [ "RAN001" ] "one finding"
            }

            test "a tuple pattern, without a fix" {
                let probe =
                    "module Probe\nlet tupled () =\n    let a, b = Loops.childSetups, 1\n    Loops.setup ()\n    Loops.childSetups - a + b\n"

                let messages = findings probe
                Expect.equal (messages |> List.map _.Code) [ "RAN001" ] "one finding"
                Expect.isEmpty messages.Head.Fixes "no fix for a tuple pattern"
            }

            test "the regression seed of ElmishNestedChecks.fs:29-30 fires" {
                let probe =
                    "module Probe\nlet loops () =\n    let setups = Loops.childSetups\n    let target = obj ()\n    Loops.setup ()\n    target, Loops.childSetups - setups\n"

                Expect.equal (findings probe |> List.map _.Code) [ "RAN001" ] "let mutable changed to let"
            }

            test "a mutable of the same file is not inlined" {
                let probe =
                    "module Probe\nlet mutable count = 0\nlet snapshot () =\n    let seen = count\n    count <- 1\n    count - seen\n"

                Expect.isEmpty (findings probe) "same file"
            }

            test "two uses are not inlined" {
                let probe =
                    "module Probe\nlet twice () =\n    let setups = Loops.childSetups\n    Loops.setup ()\n    setups + (Loops.childSetups - setups)\n"

                Expect.isEmpty (findings probe) "two uses"
            }

            test "a let mutable snapshot is kept" {
                Expect.isEmpty (findings (snapshot.Replace ("let setups", "let mutable setups"))) "let mutable"
            }

            test "a wrapped read is kept" {
                Expect.isEmpty (findings (snapshot.Replace ("= Loops.childSetups", "= Loops.childSetups + 0"))) "+ 0"
            }

            test "a mutable record field is kept" {
                let records =
                    "module Loops\ntype Counter = { mutable Count: int }\nlet state = { Count = 0 }\nlet setup () = state.Count <- state.Count + 1\n"

                let probe =
                    "module Probe\nlet field () =\n    let seen = Loops.state.Count\n    Loops.setup ()\n    Loops.state.Count - seen\n"

                Expect.isEmpty
                    (run Analyzers.ran001Cli (contextOf (ranvier.Force ()) [ "Loops.fs", records; "Probe.fs", probe ]))
                    "record field"
            }

            test "a project without Fable.Core is not analyzed" {
                Expect.isEmpty (findingsWith (plain.Force ()) snapshot) "no Fable.Core reference"
            }

            test "the fix type-checks and clears the finding" {
                let fix = (findings snapshot).Head.Fixes |> List.exactlyOne
                let fixed = applyFix snapshot fix
                Expect.stringContains fixed "let mutable setups = Loops.childSetups" "mutable inserted"
                Expect.isEmpty (findings fixed) "no finding after the fix"
            }
        ]
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`
Expected: FAIL to compile: `ran001Cli` is not defined.

- [ ] **Step 3: Implement RAN001**

Add to `tools/Ranvier.Analyzers/Rules/Fable.fs`, after the opens (add `open System`, `open System.IO`,
`open FSharp.Compiler.Symbols.FSharpExprPatterns`, `open FSharp.Compiler.Text` and `open Ranvier.Analyzers.Index`):

```fsharp
/// The module-level `let mutable` that `expr` reads bare.
let private moduleMutable (expr: FSharpExpr) : FSharpMemberOrFunctionOrValue option =
    match expr with
    | Call (None, value, [], [], []) when value.IsMutable && value.IsModuleValueOrMember -> Some value
    | _ -> None

let private sameFile (left: string) (right: string) =
    String.Equals (Path.GetFullPath left, Path.GetFullPath right, StringComparison.OrdinalIgnoreCase)

/// The mutable a binding snapshots, and whether a fix can make the binding mutable: false for a tuple pattern.
let private snapshotOf (lets: LetSite array) (site: LetSite) : (FSharpMemberOrFunctionOrValue * bool) option =
    match moduleMutable site.Bound, site.Bound with
    | Some source, _ -> Some (source, true)
    | None, TupleGet (_, item, Value tuple) ->
        lets
        |> Array.tryPick (fun outer ->
            match outer.Bound with
            | NewTuple (_, elements) when outer.Value = tuple && item < elements.Length ->
                moduleMutable elements[item] |> Option.map (fun source -> source, false)
            | _ -> None)
    | None, _ -> None

let private qualified (source: FSharpMemberOrFunctionOrValue) =
    match source.ApparentEnclosingEntity with
    | Some entity -> entity.DisplayName + "." + source.DisplayName
    | None -> source.DisplayName

let private snapshots (input: Input) : Message list =
    let lets = input.Index.Lets

    [
        for site in lets do
            if not site.Value.IsMutable && not site.Value.IsCompilerGenerated then
                match snapshotOf lets site with
                | Some (source, fixable) when
                    not (sameFile source.DeclarationLocation.FileName site.Value.DeclarationLocation.FileName)
                    ->
                    match Index.uses site.Value site.Body with
                    | [ single ] ->
                        let name = site.Value.DisplayName
                        let at = site.Value.DeclarationLocation

                        let fixes =
                            if fixable then
                                [ replace (Range.mkRange at.FileName at.Start at.Start) "" "mutable " ]
                            else
                                []

                        message
                            "RAN001"
                            Severity.Warning
                            at
                            $"Fable Release compiles `{name}` as a second read of `{qualified source}` at its single use (line {single.StartLine}), so writes between the two lines change it. Declare it `let mutable {name}` to keep the snapshot."
                            fixes
                    | _ -> ()
                | _ -> ()
    ]

let ran001: Rule =
    {
        Code = "RAN001"
        Title = "Fable inlines a snapshot of a module mutable"
        Gate = Symbols.Assemblies.Fable
        Run = snapshots
    }
```

In `Analyzers.fs`, set `let rules: Rule list = [ Rules.Fable.ran001; Rules.Fable.ran002 ]` and add the pair:

```fsharp
[<CliAnalyzer("RAN001",
              "Fable inlines a snapshot of a module mutable",
              "https://ranvier-org.github.io/Ranvier/analyzers/ran001/")>]
let ran001Cli: Analyzer<CliContext> =
    fun context -> async { return runCli Rules.Fable.ran001 context }

[<EditorAnalyzer("RAN001",
                 "Fable inlines a snapshot of a module mutable",
                 "https://ranvier-org.github.io/Ranvier/analyzers/ran001/")>]
let ran001Editor: Analyzer<EditorContext> =
    fun context -> async { return runEditor Rules.Fable.ran001 context }
```

- [ ] **Step 4: Write the docs page**

`docs/content/analyzers/ran001.md`:

````markdown
---
title: RAN001 Fable inlines a snapshot of a module mutable
order: 2
---

`warning RAN001`: *Fable Release compiles `setups` as a second read of `Loops.childSetups` at its single use (line 5),
so writes between the two lines change it.*

**Cause.** Fable 5.18 Release replaces a single-use immutable `let` whose right-hand side is a bare read of a
module-level `let mutable` declared in another file with a second read at the use. The snapshot then sees the writes
made between the binding and the use. A use inside a closure and a tuple pattern are inlined the same way.

**Fix.** Declare the snapshot `let mutable`. The rule's fix inserts `mutable`; a tuple pattern has no fix.

The mutable lives in another file:

### Shared: Loops.fs

```fsharp
module Loops

let mutable childSetups = 0

let setup () = childSetups <- childSetups + 1
```

### Triggers

```fsharp
module Example

let snapshot () =
    let setups = Loops.childSetups
    Loops.setup ()
    Loops.childSetups - setups
```

### Fixed

```fsharp
module Example

let snapshot () =
    let mutable setups = Loops.childSetups
    Loops.setup ()
    Loops.childSetups - setups
```

**Suppression.** `// fsharpanalyzer: ignore-line-next RAN001`, with the reason on the line before.
````

Add `| [RAN001](ran001.md) | Fable inlines a snapshot of a module mutable | Warning |` above the RAN002 row of
`docs/content/analyzers/index.md`, and `Menu.page "analyzers/ran001.md"` before `ran002.md` in `docs/Site.fs`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`
Expected: `Passed!`, 0 failed; RAN001 has 11 cases; `Docs examples` has `RAN001` and `RAN002`.

- [ ] **Step 6: Format, check and commit**

```bash
dotnet fantomas tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/Site.fs
rtk git add tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/content/analyzers docs/Site.fs
rtk git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-3-commit.txt
```

Commit subject: `feat(analyzers): RAN001 reports single-use snapshots of module mutables`.

### Task 4: Truth fixtures for Fable inlining and the precompile

**Files:**
- Create: `tools/Ranvier.Analyzers.Truth/Truth.fsx`
- Create: `tools/Ranvier.Analyzers.Truth/Inlining/Inlining.fsproj`, `Inlining/Loops.fs`, `Inlining/Probe.fs`
- Create: `tools/Ranvier.Analyzers.Truth/Testers/Testers.fsproj`, `Testers/Unions.fs`
- Create: `tools/Ranvier.Analyzers.Truth/GenericTesters/GenericTesters.fsproj`, `GenericTesters/Unions.fs`

**Interfaces:**
- Consumes: nothing from the analyzer assembly; it checks the facts RAN001 and RAN002 describe.
- Produces: `Truth.fsx` with `check: string -> bool -> string -> unit` and `dotnet: string list -> int * string`,
  which Task 5 extends with the build checks.

- [ ] **Step 1: Write the fixtures**

`tools/Ranvier.Analyzers.Truth/Inlining/Inlining.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <IsPackable>false</IsPackable>
    </PropertyGroup>
    <ItemGroup>
        <Compile Include="Loops.fs" />
        <Compile Include="Probe.fs" />
    </ItemGroup>
    <ItemGroup>
        <PackageReference Include="Fable.Core" Version="5.3.0" />
        <PackageReference Update="FSharp.Core" Version="10.1.401" />
    </ItemGroup>
</Project>
```

`Inlining/Loops.fs`:

```fsharp
module Loops

let mutable childSetups = 0

let setup () = childSetups <- childSetups + 1
```

`Inlining/Probe.fs` (the RAN001 positive fixtures, and one `let mutable` control):

```fsharp
module Probe

let snapshot () =
    let setups = Loops.childSetups
    Loops.setup ()
    Loops.childSetups - setups

let closure () =
    let setups = Loops.childSetups
    Loops.setup ()
    fun () -> Loops.childSetups - setups

let tupled () =
    let a, b = Loops.childSetups, 1
    Loops.setup ()
    Loops.childSetups - a + b

let kept () =
    let mutable kept = Loops.childSetups
    Loops.setup ()
    Loops.childSetups - kept
```

`tools/Ranvier.Analyzers.Truth/Testers/Testers.fsproj` and `GenericTesters/GenericTesters.fsproj` are the
`Inlining.fsproj` file with `<Compile Include="Unions.fs" />` as the only `Compile` item.

`Testers/Unions.fs`:

```fsharp
module Truth.Testers

type Policy =
    | Queue
    | Drop of int

let queued (policy: Policy) = policy.IsQueue
```

`GenericTesters/Unions.fs`:

```fsharp
module Truth.GenericTesters

type Outcome<'T> =
    | Completed of value: 'T
    | Canceled of error: exn

let finished (outcome: Outcome<int>) = outcome.IsCompleted
```

- [ ] **Step 2: Write the truth script**

`tools/Ranvier.Analyzers.Truth/Truth.fsx`:

```fsharp
// The external facts the analyzers rely on: Fable's inlining of module-mutable snapshots (RAN001) and the Fable
// precompile failure on generated union testers (RAN002). Exits non-zero when a fact no longer holds: retire or rescope
// the rule that describes it. Run from any directory: dotnet fsi tools/Ranvier.Analyzers.Truth/Truth.fsx
open System
open System.Diagnostics
open System.IO
open System.Text.RegularExpressions

let root = Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, "../.."))
let output = Path.Combine (root, "artifacts/analyzers/truth")

/// Runs `dotnet` with `arguments` in the repository root; the exit code and the combined output.
let dotnet (arguments: string list) : int * string =
    let info =
        ProcessStartInfo (
            "dotnet",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        )

    for argument in arguments do
        info.ArgumentList.Add argument

    use proc = Process.Start info
    let out = proc.StandardOutput.ReadToEndAsync ()
    let err = proc.StandardError.ReadToEndAsync ()
    proc.WaitForExit ()
    proc.ExitCode, out.Result + err.Result

let failures = ResizeArray<string> ()

let check (name: string) (holds: bool) (detail: string) =
    if holds then
        printfn "ok   %s" name
    else
        failures.Add name
        printfn "FAIL %s\n%s" name detail

let inlining () =
    let target = Path.Combine (output, "inlining")

    let code, log =
        dotnet [ "fable"; "tools/Ranvier.Analyzers.Truth/Inlining"; "-c"; "Release"; "--noCache"; "-e"; ".js"; "-o"; target ]

    let js =
        if code = 0 then
            File.ReadAllText (Path.Combine (target, "Probe.js"))
        else
            ""

    check "Fable compiles the inlining fixture" (code = 0) log

    check
        "RAN001: Fable Release inlines each single-use snapshot (snapshot, closure, tuple)"
        (Regex.Matches(js, Regex.Escape "childSetups() - childSetups()").Count >= 3)
        js

    check "RAN001: Fable keeps a let mutable snapshot" (js.Contains "kept = childSetups()") js

let precompile () =
    let testers, testersLog =
        dotnet [ "fable"; "precompile"; "tools/Ranvier.Analyzers.Truth/Testers"; "-o"; Path.Combine (output, "testers") ]

    check
        "RAN002: the precompile fails on a non-generic union tester"
        (testers <> 0 && testersLog.Contains "MethodDefNotFound")
        testersLog

    let generic, genericLog =
        dotnet [
            "fable"
            "precompile"
            "tools/Ranvier.Analyzers.Truth/GenericTesters"
            "-o"
            Path.Combine (output, "generic-testers")
        ]

    check "RAN002: the precompile accepts a generic union tester" (generic = 0) genericLog

dotnet [ "tool"; "restore" ] |> ignore
inlining ()
precompile ()

if failures.Count > 0 then
    eprintfn "%d truth checks failed: %s" failures.Count (String.Join (", ", failures))
    exit 1
```

- [ ] **Step 3: Run the script and read each check**

Run: `dotnet fsi tools/Ranvier.Analyzers.Truth/Truth.fsx`
Expected: five `ok` lines, exit 0. A `FAIL` on the `kept` check with the inlining checks passing means the emitted form
of a mutable differs: read `artifacts/analyzers/truth/inlining/Probe.js`, change the expected text to the emitted
form, and ledger the change as a ruling. A failing inlining or precompile check means a fact of the spec no longer
holds under the installed Fable: stop and report it.

- [ ] **Step 4: Format, check and commit**

```bash
dotnet fantomas tools/Ranvier.Analyzers.Truth
rtk git add tools/Ranvier.Analyzers.Truth
rtk git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-4-commit.txt
```

Commit subject: `test(analyzers): truth fixtures for Fable inlining and the union tester precompile failure`.

### Task 5: Build integration: tool manifest, the RanvierAnalyze opt-in and `build.fsx analyze`

**Files:**
- Modify: `.config/dotnet-tools.json`, `Directory.Build.props`, `Directory.Build.targets`, `build.fsx`
- Create: `tools/Ranvier.Analyzers.Truth/Build/Build.fsproj`, `Build/A.fs`, `Build/B.fs`
- Modify: `tools/Ranvier.Analyzers.Truth/Truth.fsx` (the build checks)

**Interfaces:**
- Consumes: `Truth.fsx`'s `dotnet` and `check` (Task 4); the built analyzer assembly at
  `tools/Ranvier.Analyzers/bin/Release/net10.0`.
- Produces: the MSBuild properties `RanvierAnalyze` (opt-in) and `RanvierAnalyzersFlags` (extra CLI flags); the
  `build.fsx` command `analyze` and the list `Spec.analyzedProjects`.

- [ ] **Step 1: Write the failing build checks**

`tools/Ranvier.Analyzers.Truth/Build/Build.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <IsPackable>false</IsPackable>
        <RanvierAnalyze>true</RanvierAnalyze>
    </PropertyGroup>
    <ItemGroup>
        <Compile Include="A.fs" />
        <Compile Include="B.fs" />
    </ItemGroup>
    <ItemGroup>
        <PackageReference Include="Fable.Core" Version="5.3.0" />
        <PackageReference Update="FSharp.Core" Version="10.1.401" />
    </ItemGroup>
</Project>
```

`Build/A.fs`:

```fsharp
module A

let mutable counter = 0

let bump () = counter <- counter + 1
```

`Build/B.fs`:

```fsharp
module B

let counted () =
    let seen = A.counter
    A.bump ()
    A.counter - seen

let suppressed () =
    // No write separates the snapshot from its use, so Fable's inlining leaves the value unchanged.
    // fsharpanalyzer: ignore-line-next RAN001
    let seen = A.counter
    A.counter - seen
```

Append to `Truth.fsx`, before the final `if failures.Count > 0`, and call it after `precompile ()`:

```fsharp
let build () =
    let analyzerCode, analyzerLog =
        dotnet [ "build"; "tools/Ranvier.Analyzers/Ranvier.Analyzers.fsproj"; "-c"; "Release"; "-v"; "q" ]

    check "the analyzers build" (analyzerCode = 0) analyzerLog
    let project = "tools/Ranvier.Analyzers.Truth/Build/Build.fsproj"

    let code, log = dotnet [ "build"; project; "-c"; "Release"; "--no-incremental" ]
    check "RanvierAnalyze reports RAN001 as an MSBuild warning" (code = 0 && log.Contains "B.fs(4,9): warning RAN001") log
    check "an ignore-line-next comment silences its line" (not (log.Contains "B.fs(11,")) log

    let quiet, quietLog =
        dotnet [ "build"; project; "-c"; "Release"; "--no-incremental"; "-p:MSBuildWarningsAsMessages=RAN001" ]

    check "MSBuildWarningsAsMessages demotes RAN001" (quiet = 0 && not (quietLog.Contains "warning RAN001")) quietLog

    let strict, strictLog =
        dotnet [
            "build"
            project
            "-c"
            "Release"
            "--no-incremental"
            "-p:RanvierAnalyzersFlags=--treat-as-error RAN001"
        ]

    check "an error-promoted RAN001 fails the build" (strict <> 0 && strictLog.Contains "error RAN001") strictLog
```

- [ ] **Step 2: Run the script to verify the build checks fail**

Run: `dotnet fsi tools/Ranvier.Analyzers.Truth/Truth.fsx`
Expected: the five Task 4 checks pass; the four RAN001 build checks FAIL (no analyzers run during the build).

- [ ] **Step 3: Add the tool, the opt-in and the analyze command**

`.config/dotnet-tools.json` gains, after `fable`:

```json
    ,
    "fsharp-analyzers": {
      "version": "0.41.0",
      "commands": [ "fsharp-analyzers" ],
      "rollForward": false
    }
```

`Directory.Build.props`, before `</Project>`:

```xml
    <!--
    `RanvierAnalyze=true` runs the Ranvier analyzers after each compile; Directory.Build.targets sets the flags. The
    item condition is evaluated after every property, so a project file can set RanvierAnalyze itself.
    -->
    <ItemGroup Condition="'$(RanvierAnalyze)' == 'true'">
        <PackageReference Include="FSharp.Analyzers.Build" Version="0.5.0" PrivateAssets="all" IncludeAssets="build" />
    </ItemGroup>
```

`Directory.Build.targets`, before `</Project>`:

```xml
    <!--
    `RanvierAnalyze=true` runs the analyzers built at tools/Ranvier.Analyzers/bin/Release/net10.0 after CoreCompile, and
    a finding promoted to an error fails the build. `RanvierAnalyzersFlags` passes further CLI flags, such as
    `treat-as-error RAN001`; the analyzers run only when CoreCompile does.
    -->
    <PropertyGroup Condition="'$(RanvierAnalyze)' == 'true'">
        <RunAnalyzersDuringBuild>true</RunAnalyzersDuringBuild>
        <FSharpAnalyzersContinueOnError>false</FSharpAnalyzersContinueOnError>
        <FSharpAnalyzersOtherFlags>$(FSharpAnalyzersOtherFlags) $(RanvierAnalyzersFlags) --analyzers-path &quot;$(MSBuildThisFileDirectory)tools/Ranvier.Analyzers/bin/Release/net10.0&quot; --code-root &quot;$(MSBuildThisFileDirectory).&quot;</FSharpAnalyzersOtherFlags>
    </PropertyGroup>
```

`build.fsx`: in `module Spec`, after `mapsTests`:

```fsharp
    /// The projects `analyze` checks; each type-checks under the analyzers CLI. Fable-only projects are left out.
    let analyzedProjects =
        [
            "src/Ranvier/Ranvier.fsproj"
            "src/Ranvier.Query/Ranvier.Query.fsproj"
            "src/Ranvier.Elmish/Ranvier.Elmish.fsproj"
            "src/Ranvier.Wire/Ranvier.Wire.fsproj"
            "src/Fable.Ranvier/Fable.Ranvier.fsproj"
            "src/Fable.Ranvier.Anime/Fable.Ranvier.Anime.fsproj"
            "experiments/Ranvier.View.Portable/Ranvier.View.Portable.fsproj"
            "experiments/Ranvier.View.Foundation/Ranvier.View.Foundation.fsproj"
            "experiments/Ranvier.View.Anime/Ranvier.View.Anime.fsproj"
            "examples/Fable.Ranvier.Playground/Playground.fsproj"
            "examples/Fable.Ranvier.Playground/tests/DomTests.fsproj"
            "examples/Ranvier.Query.Dictionary/Ranvier.Query.Dictionary.fsproj"
            "examples/Ranvier.FuncUI.Workload/Ranvier.FuncUI.Workload.fsproj"
            "examples/Ranvier.FuncUI.Workload/tests/Ranvier.FuncUI.Workload.Tests.fsproj"
            "docs/samples/view/Ranvier.Docs.ViewSamples.fsproj"
            "tests/Ranvier.Tests/Ranvier.Tests.fsproj"
            "tests/Ranvier.Wire.Tests/Ranvier.Wire.Tests.fsproj"
        ]
```

Before writing the list, confirm each path with `rtk ls` (the two FuncUI projects in particular); a path that differs
is corrected in the list and in the ledger.

In `module Stage`, after `generateDocs`:

```fsharp
    /// Builds the analyzers, then runs them over `Spec.analyzedProjects` with every RAN code an error, writing a SARIF
    /// report to artifacts/analyzers. Under GitHub Actions the findings also print as annotations. The analyzed
    /// projects' references must be built first.
    let analyze =
        input {
            let projects =
                Spec.analyzedProjects
                |> List.map (sprintf "--project %s")
                |> String.concat " "

            let github =
                if System.Environment.GetEnvironmentVariable "GITHUB_ACTIONS" = "true" then
                    " --output-format github"
                else
                    ""

            return
                stage "analyze" {
                    stage "build analyzers" {
                        run (cmd "dotnet build tools/Ranvier.Analyzers/Ranvier.Analyzers.fsproj -c Release -v q")
                    }

                    stage "run analyzers" {
                        run (fun _ -> System.IO.Directory.CreateDirectory "artifacts/analyzers" |> ignore)

                        run (
                            cmd
                                $"dotnet fsharp-analyzers {projects} --analyzers-path tools/Ranvier.Analyzers/bin/Release/net10.0 -c Release --fail-on-type-errors --treat-as-error RAN* --exclude-files **/Generated/** --code-root . --report artifacts/analyzers/ranvier.sarif{github}"
                        )
                    }
                }
        }
```

In `rootCommandOfScript`, after the `docs` command:

```fsharp
    command "analyze" {
        description "Runs the Ranvier analyzers over the repository's projects, every finding an error; build first"
        Stage.restore
        Stage.analyze
    }
```

Check the CLI accepts each flag before relying on it: `dotnet tool restore && dotnet fsharp-analyzers --help`. A
flag the CLI lacks (`-c` in particular) is replaced by its listed equivalent, and the replacement is ledgered.

- [ ] **Step 4: Run the script to verify every check passes**

Run: `dotnet fsi tools/Ranvier.Analyzers.Truth/Truth.fsx`
Expected: nine `ok` lines, exit 0.

- [ ] **Step 5: Run the analyze command**

Run: `dotnet build Ranvier.slnx -c Release` then `dotnet fsi build.fsx -- analyze`
Expected: the command runs every project. It fails with the two RAN002 findings at `src/Ranvier/Core.fs(4506,...)` and
`(4563,...)`, which Task 6 fixes, and with nothing else. A project that fails to type-check is recorded for Task 6.

- [ ] **Step 6: Format, check and commit**

```bash
dotnet fantomas tools/Ranvier.Analyzers.Truth build.fsx
rtk git add .config/dotnet-tools.json Directory.Build.props Directory.Build.targets build.fsx tools/Ranvier.Analyzers.Truth
rtk git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-5-commit.txt
```

Commit subject: `build(analyzers): RanvierAnalyze opt-in, the fsharp-analyzers tool and build.fsx analyze`.

### Task 6: The corpus gate: FlightOutcome matches, the gate list and the timing budget

**Files:**
- Modify: `src/Ranvier/Core.fs:4506` and `:4563`
- Modify: `build.fsx` (`Spec.analyzedProjects`, only for a project that does not type-check)
- Create: `tools/Ranvier.Analyzers.Tests/Budget.fs` (compiled after `DocsExamples.fs`)

**Interfaces:**
- Consumes: `Analyzers.rules`, `Index.ofFile`, `Options.withReferences`, `Options.contextOf`, `Options.repository`.
- Produces: `Budget.sourcesOf: string -> (string * string) list` (the unconditional `Compile` items of a project, each
  a path and its text), and the budget test that every later rule joins through `Analyzers.rules`.

- [ ] **Step 1: Write the failing budget test**

`tools/Ranvier.Analyzers.Tests/Budget.fs`:

```fsharp
/// Each rule's time over Core.fs and Template.fs, the two largest files of the gate; the budget is 250 ms per rule
/// per file. The rules run ungated, so the view rules are timed over Template.fs as well.
module Ranvier.Analyzers.Tests.Budget

open System.Diagnostics
open System.IO
open System.Xml.Linq
open Expecto
open FSharp.Analyzers.SDK
open Ranvier.Analyzers
open Ranvier.Analyzers.Rule
open Ranvier.Analyzers.Tests.Options

/// The unconditional `Compile` items of `project`, a path relative to the repository, each with its text.
let sourcesOf (project: string) : (string * string) list =
    let path = Path.Combine (repository, project)
    let directory = Path.GetDirectoryName path

    [
        for item in XDocument.Load(path).Descendants (XName.Get "Compile") do
            if isNull (item.Attribute (XName.Get "Condition")) then
                let file = Path.GetFullPath (Path.Combine (directory, item.Attribute(XName.Get "Include").Value))
                file, File.ReadAllText file
    ]

let private timed (label: string) (budget: int64) (action: unit -> unit) =
    let clock = Stopwatch.StartNew ()
    action ()
    clock.Stop ()
    Expect.isLessThanOrEqual clock.ElapsedMilliseconds budget $"{label}: {clock.ElapsedMilliseconds} ms"

let private budgetOver (project: string) (file: string) (references: string list) =
    let sources = sourcesOf project
    let upTo = sources |> List.findIndex (fun (path, _) -> path.EndsWith file)
    let context = contextOf (withReferences references) (List.take (upTo + 1) sources)
    let tree = context.TypedTree.Value
    timed $"index of {file}" 250L (fun () -> Index.ofFile tree |> ignore)

    let input =
        {
            FileName = context.FileName
            Source = context.SourceText
            Tree = tree
            Index = Index.ofFile tree
            Options = context.ProjectOptions
            Project = Some context.CheckProjectResults
        }

    for rule in Analyzers.rules do
        timed $"{rule.Code} over {file}" 250L (fun () -> rule.Run input |> ignore)

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Budget"
        [
            test "every rule over Core.fs" {
                budgetOver "src/Ranvier/Ranvier.fsproj" "Core.fs" [ "Fable.Core.dll" ]
            }

            test "every rule over Template.fs" {
                budgetOver
                    "experiments/Ranvier.View.Portable/Ranvier.View.Portable.fsproj"
                    "Template.fs"
                    [ "Ranvier.dll"; "Fable.Core.dll"; "Xantham.Fable.Core.dll"; "Xantham.Fable.Core.TS.dll" ]
            }
        ]
```

`Budget.fs` refers to `Severity` through `Message` only; drop the `FSharp.Analyzers.SDK` open if Fantomas or the
compiler reports it unused.

- [ ] **Step 2: Run the budget test**

Run, holding the bench lock:

```bash
T=C:/Users/shaya/.claude/jobs/7fee9ca9/tmp
until mkdir $T/bench.lock 2>/dev/null; do sleep 30; done
dotnet test tools/Ranvier.Analyzers.Tests -c Release --filter "FullyQualifiedName~Budget"
rmdir $T/bench.lock
```

Expected: both cases pass, the index and each of RAN001-RAN002 under 250 ms per file. A type error in either context
means the project needs a define or a reference the helper lacks: add it to the `budgetOver` call and ledger it.
Then run the unfiltered suite (`dotnet test tools/Ranvier.Analyzers.Tests -c Release`) as the acceptance check.

- [ ] **Step 3: Replace the two FlightOutcome testers with matches**

`src/Ranvier/Core.fs:4506`:

```fsharp
                    if
                        (match outcome with
                         | Platform.FlightOutcome.Completed _ -> true
                         | _ -> false)
                    then
                        discardAtSettle gen
```

`src/Ranvier/Core.fs:4563` (the `Tracer.FlightSettled` argument):

```fsharp
                Tracer.FlightSettled (
                    graph,
                    id,
                    gen,
                    (match outcome with
                     | Platform.FlightOutcome.Canceled _ -> 2
                     | _ -> 1),
                    0,
                    ex
                )
```

- [ ] **Step 4: Run the core tests and the corpus gate**

Run: `dotnet test tests/Ranvier.Tests -c Release` then `dotnet build Ranvier.slnx -c Release` then
`dotnet fsi build.fsx -- analyze`
Expected: the core suite passes with the counts it had before the edit; `analyze` exits 0 with zero findings. A gate
project that fails `--fail-on-type-errors` leaves the list, with the error in the ledger as a ruling.

- [ ] **Step 5: Measure the budget against the spec's baseline**

```bash
T=C:/Users/shaya/.claude/jobs/7fee9ca9/tmp
until mkdir $T/bench.lock 2>/dev/null; do sleep 30; done
time dotnet fsharp-analyzers --project src/Ranvier/Ranvier.fsproj --analyzers-path tools/Ranvier.Analyzers/bin/Release/net10.0
time dotnet fsharp-analyzers --project experiments/Ranvier.View.Portable/Ranvier.View.Portable.fsproj --analyzers-path tools/Ranvier.Analyzers/bin/Release/net10.0
rmdir $T/bench.lock
```

Expected: at most 8.4 s and 14.6 s (the spec's 8.0 s and 13.9 s plus 5 %). Record both times in the ledger.

- [ ] **Step 6: Format, check and commit**

```bash
dotnet fantomas src/Ranvier/Core.fs tools/Ranvier.Analyzers.Tests build.fsx
rtk git add src/Ranvier/Core.fs tools/Ranvier.Analyzers.Tests build.fsx
rtk git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-6-commit.txt
```

Commit subject: `fix(core): match on flight outcomes, and gate the repository on the analyzers`.

### Task 7: The CI `analyzers` job and the docs-change classification

**Files:**
- Modify: `.github/workflows/ci.yml` (new job; `result` needs it)
- Modify: `.github/scripts/changes.cjs`, `.github/scripts/changes.test.cjs`

**Interfaces:**
- Consumes: `build.fsx analyze` (Task 5), `Truth.fsx` (Tasks 4-5), the test project.
- Produces: the `analyzers` job.

- [ ] **Step 1: Write the failing classification test**

Append to `.github/scripts/changes.test.cjs`:

```js
test('an analyzer rule page runs the analyzer suite, which compiles its examples', () => {
  assert.equal(docsOnly(['docs/content/analyzers/ran001.md']), false);
  assert.equal(docsOnly(['docs/content/analyzers/index.md', 'docs/content/guide/memos.md']), false);
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `node --test .github/scripts/changes.test.cjs`
Expected: FAIL on the new test (`true !== false`).

- [ ] **Step 3: Exclude the rule pages from documentation-only changes**

In `.github/scripts/changes.cjs`, extend the header comment with "and the analyzer rule pages, whose examples the
analyzer suite compiles", then change the first and fourth patterns:

```js
  /^docs\/(?!maps\/|content\/analyzers\/)/,
```

```js
  /^(?!experiments\/Ranvier\.View\.Portable\/README\.md$|docs\/content\/analyzers\/).*\.md$/i,
```

- [ ] **Step 4: Run it to verify it passes**

Run: `node --test .github/scripts/*.test.cjs`
Expected: every test passes.

- [ ] **Step 5: Add the job**

In `.github/workflows/ci.yml`, after the `motion-view` job:

```yaml
  # Runs the Ranvier analyzers' unit tests and truth fixtures, then the analyzers over the repository's projects with
  # every RAN code promoted to an error: any finding fails the job.
  analyzers:
    name: Analyzers
    needs: changes
    if: needs.changes.outputs.docs != 'true'
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json
      - name: Restore tools
        run: dotnet tool restore
      - name: Test the analyzers
        run: dotnet test tools/Ranvier.Analyzers.Tests -c Release
      # Fable's inlining, the Fable precompile and the MSBuild integration that the rules describe.
      - name: Check the analyzers' truth fixtures
        run: dotnet fsi tools/Ranvier.Analyzers.Truth/Truth.fsx
      - name: Build the analyzed projects
        run: dotnet build Ranvier.slnx -c Release
      - name: Run the analyzers over the repository
        run: dotnet fsi build.fsx -- analyze
      - name: Upload the SARIF report
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: analyzers-sarif
          path: artifacts/analyzers/
```

Change the `result` job's `needs` to
`[changes, scripts, dotnet, aot, packages, format, fable, playground, sugar, anime, motion-view, analyzers]`.

- [ ] **Step 6: Check the workflow locally and commit**

Run each job step from the repository root in order: `dotnet tool restore`, `dotnet test tools/Ranvier.Analyzers.Tests
-c Release`, `dotnet fsi tools/Ranvier.Analyzers.Truth/Truth.fsx`, `dotnet build Ranvier.slnx -c Release`, `dotnet fsi
build.fsx -- analyze`; time the sequence with `time` and record it (budget: under 6 minutes on the runner).
Expected: each exits 0.

```bash
rtk git add .github/workflows/ci.yml .github/scripts/changes.cjs .github/scripts/changes.test.cjs
rtk git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-7-commit.txt
```

Commit subject: `ci: run the Ranvier analyzers over the repository`.

---

## Phase 1: core rules

The phase starts with `git fetch origin && git merge origin/master` (merge commit message file with trailers), a clean
`dotnet test tools/Ranvier.Analyzers.Tests -c Release` and a clean `dotnet fsi build.fsx -- analyze`.

### Task 8: Core symbols and RAN010, owned nodes in pure derivations

**Files:**
- Modify: `tools/Ranvier.Analyzers/Symbols.fs` (module `Core`)
- Create: `tools/Ranvier.Analyzers/Rules/Core.fs` (compiled after `Rules/Fable.fs`)
- Modify: `tools/Ranvier.Analyzers/Analyzers.fs`
- Create: `tools/Ranvier.Analyzers.Tests/Ran010.fs`, `docs/content/analyzers/ran010.md`
- Modify: `docs/content/analyzers/index.md`, `docs/Site.fs`, `docs/content/guide/troubleshooting.md`

**Interfaces:**
- Consumes: `Index.arguments`, `Index.CallSite`, `Symbols.enclosing`, `Symbols.display`.
- Produces: `Symbols.Core.Api`, `Symbols.Core.isReactiveRead`, `Symbols.Core.isCreator`, `Symbols.Core.isTransparent`,
  `Symbols.Core.Pure` (failure kinds), `Symbols.Core.PureContext`, `Symbols.Core.pureContext: FSharpMemberOrFunctionOrValue
  -> int -> int -> PureContext option`, `Symbols.Core.asyncComputeIndex: FSharpMemberOrFunctionOrValue -> int -> int
  option`; `Rules.Core.ran010: Rule`; `Analyzers.ran010Cli`, `Analyzers.ran010Editor`.

- [ ] **Step 1: Write the failing tests**

`tools/Ranvier.Analyzers.Tests/Ran010.fs`:

```fsharp
module Ranvier.Analyzers.Tests.Ran010

open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private findings (body: string) =
    let source = "module Fixture\nopen Ranvier\nopen System.Threading.Tasks\n" + body
    run Analyzers.ran010Cli (context (ranvier.Force ()) source)

let private codes body = findings body |> List.map _.Code

[<Tests>]
let tests =
    testList
        "RAN010"
        [
            test "an effect in createMemo's body, with its exact message" {
                Expect.equal
                    (findings "let m () = createMemo (fun _ -> createEffect ignore; 1)\n" |> List.map _.Message)
                    [
                        "`createEffect` creates an owned node inside `createMemo`'s body, which fails every read of the memo. Use `createMemoWith` for a memo that owns the nodes its body creates, or create the node outside the memo."
                    ]
                    "one message"
            }

            test "a memo in createAsync's compute" {
                Expect.equal
                    (codes "let a () = createAsync (fun _ _ -> createMemo (fun _ -> 1) |> ignore; Task.FromResult 1)\n")
                    [ "RAN010" ]
                    "createAsync"
            }

            test "a creator in createEffectOn's compute" {
                Expect.equal
                    (codes "let e () = createEffectOn (fun () -> onCleanup ignore; 1) ignore\n")
                    [ "RAN010" ]
                    "createEffectOn"
            }

            test "a creator in a projection's map" {
                Expect.equal
                    (codes "let p () = createProjection id (fun x -> createSignal 1 |> ignore; createEffect ignore; x) (fun () -> [ 1 ])\n")
                    [ "RAN010" ]
                    "createProjection map"
            }

            test "a creator in a projection row's reader" {
                Expect.equal
                    (codes "let p () = createProjectionWith id (fun item -> fun () -> createEffect ignore; item ()) (fun () -> [ 1 ])\n")
                    [ "RAN010" ]
                    "the reader"
            }

            test "a creator in a lookup's f and affected" {
                Expect.equal
                    (codes "let l () = createLookup (fun s k -> onCleanup ignore; s + k) (fun _ _ -> onCleanup ignore; Seq.empty) (fun () -> 1)\n")
                    [ "RAN010"; "RAN010" ]
                    "f and affected"
            }

            test "a creator in an editable's seed" {
                Expect.equal (codes "let e () = createEditable (fun _ -> onCleanup ignore; 1)\n") [ "RAN010" ] "seed"
            }

            test "a creator in a projection aggregate's folder" {
                Expect.equal
                    (codes "let f (p: Projection<int, int>) = p |> Projection.fold (fun s v -> onCleanup ignore; s + v) 0\n")
                    [ "RAN010" ]
                    "fold"
            }

            test "a creator under untrack" {
                Expect.equal
                    (codes "let m () = createMemo (fun _ -> untrack (fun () -> createEffect ignore); 1)\n")
                    [ "RAN010" ]
                    "untrack is the same run"
            }

            test "a creator inside try ... with" {
                Expect.equal
                    (codes "let m () = createMemo (fun _ -> try createEffect ignore; 1 with _ -> 0)\n")
                    [ "RAN010" ]
                    "caught creator"
            }

            test "the Memo constructor without the owning argument" {
                Expect.equal
                    (codes "let m (g: Graph) = Memo (g, System.Func<int voption, int>(fun _ -> createEffect ignore; 1))\n")
                    [ "RAN010" ]
                    "Memo (graph, compute)"
            }

            test "a pipelined createMemo body is a pure context" {
                Expect.equal (codes "let m () = (fun _ -> createEffect ignore; 1) |> createMemo\n") [ "RAN010" ] "pipe"
            }

            test "createMemoWith owns its nodes" {
                Expect.isEmpty (codes "let m () = createMemoWith (fun _ -> createEffect ignore; 1)\n") "owning memo"
            }

            test "a creator in the projection factory body" {
                Expect.isEmpty
                    (codes "let p () = createProjectionWith id (fun item -> createEffect ignore; fun () -> item ()) (fun () -> [ 1 ])\n")
                    "the factory runs once per key"
            }

            test "a creator in a stored callback" {
                Expect.isEmpty
                    (codes "let m (handlers: ResizeArray<unit -> unit>) = createMemo (fun _ -> handlers.Add (fun () -> createEffect ignore); 1)\n")
                    "the callback runs later"
            }

            test "createSignal in a memo" {
                Expect.isEmpty (codes "let m () = createMemo (fun _ -> (createSignal 1).Value)\n") "a signal is not owned"
            }

            test "a user function named createMemo is not a context" {
                Expect.isEmpty
                    (codes "let createMemo (f: unit -> int) = f ()\nlet m () = createMemo (fun () -> createEffect ignore; 1)\n")
                    "own createMemo"
            }

            test "a project without Ranvier is not analyzed" {
                let source = "module Fixture\nlet x = 1\n"
                Expect.isEmpty (run Analyzers.ran010Cli (context (plain.Force ()) source)) "no Ranvier reference"
            }

            test "the fix renames createMemo to createMemoWith" {
                let source = "module Fixture\nopen Ranvier\nlet m () = createMemo (fun _ -> createEffect ignore; 1)\n"
                let fix = (run Analyzers.ran010Cli (context (ranvier.Force ()) source)).Head.Fixes |> List.exactlyOne
                let fixed = applyFix source fix
                Expect.stringContains fixed "createMemoWith (fun _" "renamed"
                Expect.isEmpty (run Analyzers.ran010Cli (context (ranvier.Force ()) fixed)) "no finding after the fix"
            }
        ]
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`
Expected: FAIL to compile: `ran010Cli` is not defined.

- [ ] **Step 3: Add the core symbols**

Append to `tools/Ranvier.Analyzers/Symbols.fs`:

```fsharp
/// The core node types, their creators and their pure contexts.
module Core =
    [<Literal>]
    let Api = "Ranvier.Api"

    let private valueTypes =
        set [
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
            && (m.LogicalName = "Get" || m.LogicalName = "TryGet" || m.LogicalName = "get_Keys"))

    let private creatorFunctions =
        set [
            "createMemo"; "createMemoWithComparer"; "createMemoWith"; "createOwningMemoWithComparer"
            "createEffect"; "createEffectOn"; "createEffectOnWithComparer"
            "createAsync"; "createAsyncWith"; "createAsyncWithPolicy"; "createOwningAsyncWithPolicy"; "createAsyncSource"
            "createStream"; "createStreamFold"
            "createSuspense"; "createSuspenseWithComparer"; "createErrorBoundary"; "createErrorBoundaryWithComparer"
            "createBoundary"; "createBoundaryWithComparer"; "createRoot"
            "createProjection"; "createProjectionWith"; "createProjectionWithOptions"; "createProjectionOnce"
            "createIndexProjection"; "createIndexProjectionWith"; "createLookup"; "createSelector"; "createOptionMemo"
            "onCleanup"
        ]

    let private creatorTypes = set [ "Ranvier.Memo`1"; "Ranvier.Effect"; "Ranvier.AsyncMemo`1" ]

    /// Whether a call of `m` creates an owned node.
    let isCreator (m: FSharpMemberOrFunctionOrValue) : bool =
        (enclosing m = Api && creatorFunctions.Contains m.LogicalName)
        || (m.IsConstructor && creatorTypes.Contains (enclosing m))

    /// Whether the lambda argument of a call of `m` runs in the caller's run: `untrack`, `batch`, and the `Delay` of a
    /// task expression.
    let isTransparent (m: FSharpMemberOrFunctionOrValue) : bool =
        (enclosing m = Api && (m.LogicalName = "untrack" || m.LogicalName = "batch"))
        || (m.LogicalName = "Delay" && enclosing m = "Microsoft.FSharp.Control.TaskBuilderBase")

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

        Map.ofList [
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
                Some { Kind = Pure.Memo; Layers = 1; Owning = None }
            | "Ranvier.AsyncMemo`1" when index = count - 1 && not (owningConstructor m) ->
                Some { Kind = Pure.Async; Layers = 2; Owning = None }
            | _ -> None
        else
            pureFunctions |> Map.tryFind (enclosing m, m.LogicalName, index)

    /// The index of the compute argument of an async value's creator, when `m` is one; `count` is the call's
    /// argument count.
    let asyncComputeIndex (m: FSharpMemberOrFunctionOrValue) (count: int) : int option =
        if m.IsConstructor && enclosing m = "Ranvier.AsyncMemo`1" then
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
```

- [ ] **Step 4: Implement RAN010**

`tools/Ranvier.Analyzers/Rules/Core.fs`:

```fsharp
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
        $"`{context}`'s body, which fails every read of the memo",
        "Pass `true` as the owning argument, or create the node outside the memo."
    | Pure.Async, Some owner ->
        $"`{context}`'s compute, which fails every read of the async value",
        $"Use `{owner}` for an async value that owns the nodes its body creates, or create the node outside the async value."
    | Pure.Async, None ->
        $"`{context}`'s compute, which fails every read of the async value",
        "Pass `true` as the owning argument, or create the node outside the async value."
    | Pure.EffectOn, _ ->
        $"`{context}`'s compute, which fails the effect", "Create the node in act, which owns it."
    | Pure.Map, _ ->
        "a projection's map, which fails the row's reads",
        "Use createProjectionWith, or createIndexProjectionWith, and create the node in the factory, which runs once per key."
    | Pure.Reader, _ ->
        "a projection row's reader, which fails the row's reads",
        "Move the creation into the factory body, which runs once per key."
    | Pure.LookupF, _ -> "a lookup's f, which fails the key's reads", "Create the node outside the lookup."
    | Pure.LookupAffected, _ ->
        "a lookup's affected, which fails every live key", "Create the node outside the lookup."
    | Pure.Seed, _ ->
        $"`{context}`'s seed, which fails every read of the editable", "Create the node outside the editable."
    | Pure.Aggregate, _ ->
        "a projection aggregate's add, subtract or folder, which fails the aggregate",
        "Create the node outside the aggregate."

/// The range of `name` at the start of the call at `range`, when the call's text begins with a qualified `name`.
let private calleeName (input: Input) (range: range) (name: string) : range option =
    let callText = text input range
    let at = callText.IndexOf name

    if
        at >= 0
        && callText.Substring (0, at)
           |> Seq.forall (fun c -> System.Char.IsLetterOrDigit c || c = '.' || c = '_')
    then
        let start = Position.mkPos range.StartLine (range.StartColumn + at)
        Some (Range.mkRange range.FileName start (Position.mkPos range.StartLine (range.StartColumn + at + name.Length)))
    else
        None

let private ownedInPure (input: Input) : Message list =
    [
        for site in input.Index.Calls do
            if isCreator site.Callee then
                let context =
                    Index.arguments isTransparent site.Path
                    |> List.tryPick (fun (callee, index, count, lambdas) ->
                        match pureContext callee index count with
                        | Some pure when pure.Layers = lambdas -> Some (callee, pure)
                        | _ -> None)

                match context with
                | Some (callee, pure) ->
                    let contextName = Symbols.display callee
                    let where, fix = failure pure.Kind contextName pure.Owning

                    let fixes =
                        match pure.Owning with
                        | Some owner ->
                            // The innermost call of the context's callee around the creator: a memo can nest in a memo.
                            let contextSite =
                                input.Index.Calls
                                |> Array.filter (fun candidate ->
                                    candidate.Callee = callee
                                    && Range.rangeContainsRange candidate.Range site.Range)
                                |> Array.sortBy (fun candidate ->
                                    candidate.Range.EndLine - candidate.Range.StartLine,
                                    candidate.Range.EndColumn - candidate.Range.StartColumn)
                                |> Array.tryHead

                            match contextSite |> Option.bind (fun s -> calleeName input s.Range callee.LogicalName) with
                            | Some nameRange -> [ replace nameRange callee.LogicalName owner ]
                            | None -> []
                        | None -> []

                    message
                        "RAN010"
                        Severity.Error
                        site.Range
                        $"`{Symbols.display site.Callee}` creates an owned node inside {where}. {fix}"
                        fixes
                | None -> ()
    ]

let ran010: Rule =
    {
        Code = "RAN010"
        Title = "Owned node created in a pure derivation"
        Gate = Symbols.Assemblies.Ranvier
        Run = ownedInPure
    }
```

Add `<Compile Include="Rules/Core.fs" />` after `Rules/Fable.fs`; add `Rules.Core.ran010` to `Analyzers.rules`; add the
`ran010Cli`/`ran010Editor` pair with title "Owned node created in a pure derivation" and HelpUri
`https://ranvier-org.github.io/Ranvier/analyzers/ran010/`, in the form of the RAN002 pair.

- [ ] **Step 5: Write the docs page and the troubleshooting link**

`docs/content/analyzers/ran010.md`:

````markdown
---
title: RAN010 Owned node created in a pure derivation
order: 10
---

`error RAN010`: *`createEffect` creates an owned node inside `createMemo`'s body, which fails every read of the memo.*

**Cause.** A memo, effect, async value, boundary, root, projection, lookup, selector or `onCleanup` created in a pure
derivation fails the derivation's run with `InvalidOperationException`: the body of `createMemo` or `Memo (graph,
compute)`, the compute of `createAsync` and `createEffectOn`, a projection's map or row reader, a lookup's `f` and
`affected`, an editable's seed, and a projection aggregate's functions. A creator under `untrack` or inside `try ...
with` fails the same way. A creator in a stored callback is not reported.

**Fix.** Use the owning form (`createMemoWith`, `createAsyncWith`), or create the node outside. The rule's fix renames
the memo and async creators.

### Triggers

```fsharp
module Example

open Ranvier

let label (topic: Signal<string>) =
    createMemo (fun _ ->
        onCleanup ignore
        topic.Value)
```

### Fixed

```fsharp
module Example

open Ranvier

let label (topic: Signal<string>) =
    createMemoWith (fun _ ->
        onCleanup ignore
        topic.Value)
```

**Suppression.** `// fsharpanalyzer: ignore-line RAN010`, with the reason on the line before.
````

In `docs/content/guide/troubleshooting.md`, after the paragraph that begins `**Fix.** Use \`createMemoWith\`, or
\`Memo (graph, compute, true)\`` in the entry "A memo created by createMemo created an owned node in its body", add:

```markdown
The analyzers report this at build time: [RAN010](../analyzers/ran010.md).
```

Add the RAN010 row to `docs/content/analyzers/index.md` and `Menu.page "analyzers/ran010.md"` to `docs/Site.fs`.

- [ ] **Step 6: Run the tests, the gate, and commit**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`, then `dotnet build Ranvier.slnx -c Release`, then
`dotnet fsi build.fsx -- analyze`
Expected: `Passed!`, 0 failed (RAN010: 19 cases; `Docs examples` has `RAN010`; `Budget` includes RAN010); `analyze`
exits 0. A finding in the repository is a defect of the rule or of the code: fix the rule when the code is correct,
fix the code when it is not, and ledger which.

```bash
dotnet fantomas tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/Site.fs
rtk git add tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/content docs/Site.fs
rtk git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-8-commit.txt
```

Commit subject: `feat(analyzers): RAN010 reports owned nodes created in pure derivations`.

### Task 9: RAN011, an async value created and read in the owner that replaces it

**Files:**
- Modify: `tools/Ranvier.Analyzers/Symbols.fs` (`Core.ownerBody`, `Core.isAsyncCreator`),
  `tools/Ranvier.Analyzers/Rules/Core.fs`, `tools/Ranvier.Analyzers/Analyzers.fs`
- Create: `tools/Ranvier.Analyzers.Tests/Ran011.fs`, `docs/content/analyzers/ran011.md`
- Modify: `docs/content/analyzers/index.md`, `docs/Site.fs`, `docs/content/guide/troubleshooting.md`

**Interfaces:**
- Consumes: `Index.arguments`, `Index.within`, `Symbols.Core.isReactiveRead`, `Symbols.Core.isTransparent`.
- Produces: `Symbols.Core.ownerBody: FSharpMemberOrFunctionOrValue -> int -> int -> int option` (the layers of an owner
  body argument), `Symbols.Core.isAsyncCreator`; `Rules.Core.ran011`; `Analyzers.ran011Cli`, `Analyzers.ran011Editor`.

- [ ] **Step 1: Write the failing tests**

`tools/Ranvier.Analyzers.Tests/Ran011.fs`:

```fsharp
module Ranvier.Analyzers.Tests.Ran011

open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private findings (body: string) =
    let source = "module Fixture\nopen Ranvier\nopen System.Threading.Tasks\n" + body
    run Analyzers.ran011Cli (context (ranvier.Force ()) source)

let private codes body = findings body |> List.map _.Code

let private fetch = "let fetch () = createAsync (fun _ _ -> Task.FromResult 1)\n"

[<Tests>]
let tests =
    testList
        "RAN011"
        [
            test "created and read in a suspense body, with its exact message" {
                let body =
                    fetch
                    + "let view () =\n    createSuspense (fun _ -> 0) (fun () ->\n        let report = fetch ()\n        report.Value)\n"

                Expect.equal
                    (findings body |> List.map _.Message)
                    [
                        "`report` is created inside `createSuspense`'s body and read there; each run of the body disposes `report` and starts a new flight. Create `report` outside `createSuspense` and read it inside."
                    ]
                    "one message"
            }

            test "an owning memo" {
                Expect.equal
                    (codes "let m () =\n    createMemoWith (fun _ ->\n        let a = createAsync (fun _ _ -> Task.FromResult 1)\n        a.Value)\n")
                    [ "RAN011" ]
                    "createMemoWith"
            }

            test "an effect" {
                Expect.equal
                    (codes "let e () =\n    createEffect (fun () ->\n        let a = createAsync (fun _ _ -> Task.FromResult 1)\n        ignore a.Status)\n")
                    [ "RAN011" ]
                    "createEffect"
            }

            test "an error boundary and a boundary" {
                let body =
                    "let b () =\n    createErrorBoundary (fun _ _ -> 0) (fun () ->\n        let a = createAsync (fun _ _ -> Task.FromResult 1)\n        a.Value)\n"
                    + "let c () =\n    createBoundary (fun _ -> 0) (fun _ _ -> 0) (fun () ->\n        let a = createAsync (fun _ _ -> Task.FromResult 1)\n        a.Value)\n"

                Expect.equal (codes body) [ "RAN011"; "RAN011" ] "both boundaries"
            }

            test "createAsyncWith" {
                Expect.equal
                    (codes "let w () =\n    createAsyncWith (fun _ _ ->\n        let inner = createAsync (fun _ _ -> Task.FromResult 1)\n        Task.FromResult inner.Value)\n")
                    [ "RAN011" ]
                    "owning async"
            }

            test "created outside and read inside" {
                let body =
                    fetch + "let view () =\n    let report = fetch ()\n    createSuspense (fun _ -> 0) (fun () -> report.Value)\n"

                Expect.isEmpty (codes body) "the right shape"
            }

            test "created inside and never read there" {
                let body =
                    "let view () =\n    createSuspense (fun _ -> 0) (fun () ->\n        let report = createAsync (fun _ _ -> Task.FromResult 1)\n        ignore report\n        0)\n"

                Expect.isEmpty (codes body) "not read in the body"
            }

            test "created inside a pure createMemo is RAN010's" {
                Expect.isEmpty
                    (codes "let m () =\n    createMemo (fun _ ->\n        let a = createAsync (fun _ _ -> Task.FromResult 1)\n        a.Value)\n")
                    "pure memo"
            }

            test "read inside a nested stored callback" {
                let body =
                    "let view (handlers: ResizeArray<unit -> int>) =\n    createSuspense (fun _ -> 0) (fun () ->\n        let a = createAsync (fun _ _ -> Task.FromResult 1)\n        handlers.Add (fun () -> a.Value)\n        0)\n"

                Expect.isEmpty (codes body) "the read is not in the body's run"
            }
        ]
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`
Expected: FAIL to compile: `ran011Cli` is not defined.

- [ ] **Step 3: Implement RAN011**

Append to `module Core` in `Symbols.fs`:

```fsharp
    let private ownerFunctions: Map<string * int, int> =
        Map.ofList [
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
        if m.IsConstructor && enclosing m = "Ranvier.Effect" && index = count - 1 then
            Some 1
        elif enclosing m = Api then
            ownerFunctions |> Map.tryFind (m.LogicalName, index)
        else
            None

    /// Whether a call of `m` creates an async value: an async memo, an async source or a stream.
    let isAsyncCreator (m: FSharpMemberOrFunctionOrValue) : bool =
        (m.IsConstructor && enclosing m = "Ranvier.AsyncMemo`1")
        || (enclosing m = Api
            && (m.LogicalName.StartsWith "createAsync" || m.LogicalName.StartsWith "createStream"
                || m.LogicalName = "createOwningAsyncWithPolicy"))
```

Append to `Rules/Core.fs`:

```fsharp
/// The lambdas on `steps` that run outside the enclosing run: immediate and transparent lambdas do not count.
let private lambdaCount (steps: Step list) =
    let rec go count steps =
        match steps with
        | [] -> count
        | Step.Lambda (_, true) :: rest -> go count rest
        | Step.Lambda (_, false) :: Step.Argument (callee, _, _) :: rest when isTransparent callee -> go count rest
        | Step.Lambda (_, false) :: rest -> go (count + 1) rest
        | _ :: rest -> go count rest

    go 0 steps

let private recreated (input: Input) : Message list =
    [
        for site in input.Index.Lets do
            let created =
                input.Index.Calls
                |> Array.exists (fun call ->
                    isAsyncCreator call.Callee
                    && (match call.Path with
                        | Step.Bound value :: rest -> value = site.Value && obj.ReferenceEquals (rest, site.Path)
                        | _ -> false))

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
                    input.Index.Calls
                    |> Array.exists (fun call ->
                        isReactiveRead call.Callee
                        && (match call.Receiver with
                            | Some (Value value) -> value = site.Value
                            | _ -> false)
                        && (match Index.within site.Path call.Path with
                            | Some inner -> lambdaCount inner = 0
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
```

Register `Rules.Core.ran011` and the `ran011Cli`/`ran011Editor` pair (title "Async value created and read in the owner
that replaces it", HelpUri `https://ranvier-org.github.io/Ranvier/analyzers/ran011/`).

- [ ] **Step 4: Write the docs page and the troubleshooting link**

`docs/content/analyzers/ran011.md` follows the RAN010 page's layout, with this text and examples:

````markdown
---
title: RAN011 Async value created and read in the owner that replaces it
order: 11
---

`warning RAN011`: *`report` is created inside `createSuspense`'s body and read there; each run of the body disposes
`report` and starts a new flight.*

**Cause.** An owner body (a boundary, an owning memo, an effect, `createAsyncWith`) disposes the nodes it created
before each re-run. An async value created and read in the same body is replaced on every settle: the boundary shows
its fallback forever and starts a flight per settle.

**Fix.** Create the async value outside the owner and read it inside.

### Triggers

```fsharp
module Example

open System.Threading.Tasks
open Ranvier

let view () =
    createSuspense (fun _ -> 0) (fun () ->
        let report = createAsync (fun _ _ -> Task.FromResult 1)
        report.Value)
```

### Fixed

```fsharp
module Example

open System.Threading.Tasks
open Ranvier

let view () =
    let report = createAsync (fun _ _ -> Task.FromResult 1)
    createSuspense (fun _ -> 0) (fun () -> report.Value)
```

**Suppression.** `// fsharpanalyzer: ignore-line RAN011`, with the reason on the line before.
````

Link it from the `docs/content/guide/troubleshooting.md` entry that `docs/content/guide/troubleshooting.md:550-582`
holds (its `**Fix.**` paragraph), with `The analyzers report this at build time: [RAN011](../analyzers/ran011.md).`;
add the index row and the menu page.

- [ ] **Step 5: Run the tests, the gate, and commit**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`, then `dotnet fsi build.fsx -- analyze`
Expected: `Passed!` (RAN011: 9 cases), `analyze` exits 0.

```bash
dotnet fantomas tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/Site.fs
rtk git add tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/content docs/Site.fs
rtk git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-9-commit.txt
```

Commit subject: `feat(analyzers): RAN011 reports async values recreated by their owner`.

### Task 10: RAN012, a reactive read after the first await of an async memo

**Files:**
- Modify: `tools/Ranvier.Analyzers/Rules/Core.fs`, `tools/Ranvier.Analyzers/Analyzers.fs`
- Create: `tools/Ranvier.Analyzers.Tests/Ran012.fs`, `docs/content/analyzers/ran012.md`
- Modify: `docs/content/analyzers/index.md`, `docs/Site.fs`, `docs/content/guide/troubleshooting.md`

**Interfaces:**
- Consumes: `Symbols.Core.asyncComputeIndex`, `Symbols.Core.isReactiveRead`, `Index.CallSite.Path`.
- Produces: `Rules.Core.ran012`; `Analyzers.ran012Cli`, `Analyzers.ran012Editor`.

- [ ] **Step 1: Write the failing tests**

`tools/Ranvier.Analyzers.Tests/Ran012.fs`:

```fsharp
module Ranvier.Analyzers.Tests.Ran012

open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private findings (body: string) =
    let source = "module Fixture\nopen Ranvier\nopen System.Threading.Tasks\n" + body
    run Analyzers.ran012Cli (context (ranvier.Force ()) source)

let private codes body = findings body |> List.map _.Code

let private asyncMemo (inner: string) =
    "let load (userId: Signal<int>) (previous: Task<int>) =\n    createAsync (fun _ _ ->\n        task {\n"
    + inner
    + "        })\n"

[<Tests>]
let tests =
    testList
        "RAN012"
        [
            test "a read after do! Task.Yield, with its exact message" {
                Expect.equal
                    (findings (asyncMemo "            do! Task.Yield ()\n            return userId.Value\n")
                     |> List.map _.Message)
                    [
                        "`userId.Value` is read after the first `let!`/`do!` of this async memo; a read after an await that suspends is untracked, and a change to `userId` starts no flight. Read it before the first await and bind it to a local."
                    ]
                    "one message"
            }

            test "a read after let!" {
                Expect.equal
                    (codes (asyncMemo "            let! _ = previous\n            return userId.Value\n"))
                    [ "RAN012" ]
                    "let!"
            }

            test "a read in an and! continuation" {
                Expect.equal
                    (codes (asyncMemo "            let! a = previous\n            and! b = previous\n            return a + b + userId.Value\n"))
                    [ "RAN012" ]
                    "and!"
            }

            test "a read after the await of createAsyncWithPolicy" {
                let body =
                    "let load (userId: Signal<int>) =\n    createAsyncWithPolicy FlightPolicy.KeepLatest (fun _ _ ->\n        task {\n            do! Task.Yield ()\n            return userId.Value\n        })\n"

                Expect.equal (codes body) [ "RAN012" ] "policy form"
            }

            test "a read before the first await" {
                Expect.isEmpty
                    (codes (asyncMemo "            let id = userId.Value\n            do! Task.Yield ()\n            return id\n"))
                    "tracked"
            }

            test "Peek after the await" {
                Expect.isEmpty
                    (codes (asyncMemo "            do! Task.Yield ()\n            return userId.Peek\n"))
                    "untracked by design"
            }

            test "an await outside an async memo" {
                Expect.isEmpty
                    (codes "let t (s: Signal<int>) =\n    task {\n        do! Task.Yield ()\n        return s.Value\n    }\n")
                    "no async memo"
            }

            test "a user Value property after an await is not a reactive read" {
                let body =
                    "type Box() =\n    member _.Value = 1\nlet load (box: Box) =\n    createAsync (fun _ _ ->\n        task {\n            do! Task.Yield ()\n            return box.Value\n        })\n"

                Expect.isEmpty (codes body) "user type"
            }
        ]
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`
Expected: FAIL to compile: `ran012Cli` is not defined.

- [ ] **Step 3: Implement RAN012**

Append to `Rules/Core.fs`:

```fsharp
/// Whether the expression at `path` sits in the continuation of a computation-expression bind inside the compute of
/// an async value's creator.
let private afterAwait (path: Step list) : bool =
    let rec go (awaited: bool) (steps: Step list) =
        match steps with
        | [] -> false
        | Step.Lambda (_, false) :: (Step.Argument (callee, index, count) :: _ as rest) when
            callee.LogicalName.StartsWith "Bind" && index = count - 1
            ->
            go true rest
        | Step.Argument (callee, index, count) :: rest ->
            match asyncComputeIndex callee count with
            | Some compute when compute = index -> awaited
            | _ -> go awaited rest
        | _ :: rest -> go awaited rest

    go false path

let private untrackedReads (input: Input) : Message list =
    [
        for site in input.Index.Calls do
            match site.Receiver with
            | Some receiver when isReactiveRead site.Callee && afterAwait site.Path ->
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
```

Register `Rules.Core.ran012` and the pair (title "Reactive read after the first await of an async memo", HelpUri
`https://ranvier-org.github.io/Ranvier/analyzers/ran012/`).

- [ ] **Step 4: Write the docs page and the troubleshooting link**

`docs/content/analyzers/ran012.md`, in the RAN010 page's layout: the message `*`userId.Value` is read after the first
`let!`/`do!` of this async memo...*`; **Cause.** "An async memo tracks the reads its body makes while it runs
synchronously, up to the first await that suspends. A read in a continuation is untracked, so a change to that source
starts no flight. A `let!` on a task that has already completed does not suspend; the rule reports it too."; **Fix.**
"Read the source before the first await and bind it to a local."; `### Triggers`:

```fsharp
module Example

open System.Threading.Tasks
open Ranvier

let profile (userId: Signal<int>) =
    createAsync (fun _ _ ->
        task {
            do! Task.Yield ()
            return userId.Value
        })
```

`### Fixed`:

```fsharp
module Example

open System.Threading.Tasks
open Ranvier

let profile (userId: Signal<int>) =
    createAsync (fun _ _ ->
        task {
            let id = userId.Value
            do! Task.Yield ()
            return id
        })
```

**Suppression.** as on the RAN010 page with `RAN012`. Link it from the entry at
`docs/content/guide/troubleshooting.md:506-548`; add the index row and the menu page.

- [ ] **Step 5: Run the tests, the gate, and commit**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`, then `dotnet fsi build.fsx -- analyze`
Expected: `Passed!` (RAN012: 8 cases), `analyze` exits 0.

```bash
dotnet fantomas tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/Site.fs
rtk git add tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/content docs/Site.fs
rtk git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-10-commit.txt
```

Commit subject: `feat(analyzers): RAN012 reports reactive reads after an async memo's first await`.

### Task 11: RAN013, a signal write or `Pump` on a thread-pool callback

**Files:**
- Modify: `tools/Ranvier.Analyzers/Symbols.fs` (`Core.isGraphWrite`, `Core.threadStarter`, `Core.isDispatch`),
  `tools/Ranvier.Analyzers/Rules/Core.fs`, `tools/Ranvier.Analyzers/Analyzers.fs`
- Create: `tools/Ranvier.Analyzers.Tests/Ran013.fs`, `docs/content/analyzers/ran013.md`
- Modify: `docs/content/analyzers/index.md`, `docs/Site.fs`, `docs/content/guide/troubleshooting.md`

**Interfaces:**
- Consumes: `Index.CallSite.Path`.
- Produces: `Rules.Core.ran013`; `Analyzers.ran013Cli`, `Analyzers.ran013Editor`.

- [ ] **Step 1: Write the failing tests**

`tools/Ranvier.Analyzers.Tests/Ran013.fs`:

```fsharp
module Ranvier.Analyzers.Tests.Ran013

open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private findings (body: string) =
    let source =
        "module Fixture\nopen System.Threading\nopen System.Threading.Tasks\nopen Ranvier\n" + body

    run Analyzers.ran013Cli (context (ranvier.Force ()) source)

let private codes body = findings body |> List.map _.Code

[<Tests>]
let tests =
    testList
        "RAN013"
        [
            test "a write in Task.Run, with its exact message" {
                Expect.equal
                    (findings "let f (temperature: Signal<int>) = Task.Run (fun () -> temperature.Value <- 25)\n"
                     |> List.map _.Message)
                    [
                        "`temperature.Value <- 25` runs on a thread-pool thread; a write off the graph's owning thread raises under `ThreadAffinity.Guarded`. Hand the write to `graph.Dispatch (fun () -> ...)`."
                    ]
                    "one message"
            }

            test "Pump in ThreadPool.QueueUserWorkItem" {
                Expect.equal
                    (codes "let f (graph: Graph) = ThreadPool.QueueUserWorkItem (fun _ -> graph.Pump ())\n")
                    [ "RAN013" ]
                    "Pump"
            }

            test "Signal.update in Parallel.For" {
                Expect.equal
                    (codes "let f (s: Signal<int>) = Parallel.For (0, 4, fun _ -> Signal.update s ((+) 1)) |> ignore\n")
                    [ "RAN013" ]
                    "update"
            }

            test "a write in a new Thread" {
                Expect.equal
                    (codes "let f (s: Signal<int>) = Thread (fun () -> s.Value <- 1)\n")
                    [ "RAN013" ]
                    "Thread"
            }

            test "a write inside graph.Dispatch inside Task.Run" {
                Expect.isEmpty
                    (codes "let f (graph: Graph) (s: Signal<int>) = Task.Run (fun () -> graph.Dispatch (fun () -> s.Value <- 1))\n")
                    "dispatched"
            }

            test "Settle inside Task.Run" {
                Expect.isEmpty
                    (codes "let f (source: AsyncSource<int>) = Task.Run (fun () -> source.Settle 1)\n")
                    "settle waits in the inbox"
            }

            test "a write in an ordinary lambda" {
                Expect.isEmpty (codes "let f (s: Signal<int>) = List.iter (fun x -> s.Value <- x) [ 1 ]\n") "same thread"
            }
        ]
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`
Expected: FAIL to compile: `ran013Cli` is not defined.

- [ ] **Step 3: Implement RAN013**

Append to `module Core` in `Symbols.fs`:

```fsharp
    /// Whether a call of `m` writes a signal or pumps the graph: work the graph's owning thread must do.
    let isGraphWrite (m: FSharpMemberOrFunctionOrValue) : bool =
        isMember "Ranvier.Signal`1" "set_Value" m
        || isMember "Ranvier.SignalModule" "update" m
        || isMember "Ranvier.Graph" "Pump" m

    /// Whether `m` is `Graph.Dispatch`, which runs its work on the owning thread.
    let isDispatch (m: FSharpMemberOrFunctionOrValue) : bool = isMember "Ranvier.Graph" "Dispatch" m

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
        | "System.Threading.Timer", ".ctor" -> true
        | _ -> false
```

Append to `Rules/Core.fs`:

```fsharp
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

let private offThreadWrites (input: Input) : Message list =
    [
        for site in input.Index.Calls do
            if isGraphWrite site.Callee && offThread site.Path then
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
```

Register `Rules.Core.ran013` and the pair (title "Signal write or Pump on a thread-pool callback", HelpUri
`https://ranvier-org.github.io/Ranvier/analyzers/ran013/`).

- [ ] **Step 4: Write the docs page and the troubleshooting link**

`docs/content/analyzers/ran013.md`, in the RAN010 page's layout: the message; **Cause.** "Under the default `Guarded`
affinity a signal write or `Graph.Pump` on a thread other than the graph's owner raises *... ran on thread N, but this
graph is owned by thread M*. `AsyncSource.Settle` and `Fail` are not reported: an off-thread settle waits in the
graph's inbox."; **Fix.** "Hand the write to `graph.Dispatch`. A graph built with `ThreadAffinity.Unchecked`, or a
host that guarantees one thread, suppresses the finding with its reason."; `### Triggers`:

```fsharp
module Example

open System.Threading.Tasks
open Ranvier

let report (temperature: Signal<int>) = Task.Run (fun () -> temperature.Value <- 25)
```

`### Fixed`:

```fsharp
module Example

open System.Threading.Tasks
open Ranvier

let report (graph: Graph) (temperature: Signal<int>) =
    Task.Run (fun () -> graph.Dispatch (fun () -> temperature.Value <- 25))
```

Link it from the entry at `docs/content/guide/troubleshooting.md:81-118`; add the index row and the menu page.

- [ ] **Step 5: Run the tests, the gate, and commit**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`, then `dotnet fsi build.fsx -- analyze`
Expected: `Passed!` (RAN013: 7 cases), `analyze` exits 0. A finding in `tests/Ranvier.Tests` that exercises an
off-thread write on purpose carries a suppression comment with its reason; ledger each.

```bash
dotnet fantomas tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/Site.fs
rtk git add tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/content docs/Site.fs tests
rtk git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-11-commit.txt
```

Commit subject: `feat(analyzers): RAN013 reports graph writes on thread-pool callbacks`.

### Task 12: Package Ranvier.Analyzers

**Files:**
- Modify: `tools/Ranvier.Analyzers/Ranvier.Analyzers.fsproj`
- Create: `tools/Ranvier.Analyzers/build/Ranvier.Analyzers.props`, `tools/Ranvier.Analyzers/build/Ranvier.Analyzers.targets`,
  `tools/Ranvier.Analyzers/README.md`
- Modify: `.github/scripts/packages.cjs`, `.github/scripts/packages.test.cjs`, `.github/workflows/ci.yml` (the
  `packages` job's check step), `build.fsx` (`Stage.packAnalyzers` in `publish`), `tests/check-package-compatibility.ps1`

**Interfaces:**
- Consumes: the built analyzer assembly.
- Produces: `bin/Ranvier.Analyzers.0.1.0-preview.1.nupkg` from `build.fsx publish`; `packages.cjs` `check(packages,
  src, analyzerProjects)`.

- [ ] **Step 1: Write the failing package checks**

Append to `.github/scripts/packages.test.cjs`:

```js
test('an analyzer package holds its assembly under analyzers/dotnet/fs and its build assets', () => {
  const expected = { id: 'Ranvier.Analyzers', version: '0.1.0-preview.1', assembly: 'Ranvier.Analyzers', analyzer: true };
  const good = zip({
    'Ranvier.Analyzers.nuspec': '<package><metadata><id>Ranvier.Analyzers</id><version>0.1.0-preview.1</version></metadata></package>',
    'analyzers/dotnet/fs/Ranvier.Analyzers.dll': 'x',
    'build/Ranvier.Analyzers.props': '<Project />',
    'build/Ranvier.Analyzers.targets': '<Project />',
  });
  assert.deepEqual(checkAnalyzerPackage(good, expected), []);
  const withLib = zip({
    'Ranvier.Analyzers.nuspec': '<package><metadata><id>Ranvier.Analyzers</id><version>0.1.0-preview.1</version></metadata></package>',
    'lib/net10.0/Ranvier.Analyzers.dll': 'x',
  });
  assert.deepEqual(checkAnalyzerPackage(withLib, expected), [
    'Ranvier.Analyzers: has lib/ entries',
    'Ranvier.Analyzers: missing analyzers/dotnet/fs/Ranvier.Analyzers.dll',
    'Ranvier.Analyzers: missing build/Ranvier.Analyzers.props',
    'Ranvier.Analyzers: missing build/Ranvier.Analyzers.targets',
  ]);
});
```

Add `checkAnalyzerPackage` to the test file's `require('./packages.cjs')` destructuring.

- [ ] **Step 2: Run it to verify it fails**

Run: `node --test .github/scripts/packages.test.cjs`
Expected: FAIL: `checkAnalyzerPackage is not a function`.

- [ ] **Step 3: Implement the package layout and its checks**

`tools/Ranvier.Analyzers/Ranvier.Analyzers.fsproj`, replacing `<IsPackable>false</IsPackable>`:

```xml
        <IsPackable>true</IsPackable>
        <PackageId>Ranvier.Analyzers</PackageId>
        <Version>0.1.0-preview.1</Version>
        <Title>Ranvier.Analyzers</Title>
        <Description>F# analyzers that report Ranvier traps at build time: Fable snapshot inlining and union testers, owned nodes in pure derivations, async values recreated by their owner, untracked reads after an await, and graph writes off the owning thread.</Description>
        <PackageTags>ranvier;fsharp;analyzers;fsharp-analyzers</PackageTags>
        <PackageReadmeFile>README.md</PackageReadmeFile>
        <PackageReleaseNotes>Preview release. Built against FSharp.Analyzers.SDK 0.41.0.</PackageReleaseNotes>
        <DevelopmentDependency>true</DevelopmentDependency>
        <IncludeBuildOutput>false</IncludeBuildOutput>
        <IncludeSymbols>false</IncludeSymbols>
        <TargetsForTfmSpecificContentInPackage>$(TargetsForTfmSpecificContentInPackage);_RanvierAnalyzerInPackage</TargetsForTfmSpecificContentInPackage>
```

and, after the `ItemGroup`s:

```xml
    <ItemGroup>
        <PackageReference Include="FSharp.Analyzers.Build" Version="0.5.0" PrivateAssets="none" />
        <None Include="build/Ranvier.Analyzers.props" Pack="true" PackagePath="build" />
        <None Include="build/Ranvier.Analyzers.targets" Pack="true" PackagePath="build" />
        <None Include="README.md" Pack="true" PackagePath="\" />
        <None Include="../../brand/icon/ranvier-icon.png" Pack="true" PackagePath="\" Visible="false" />
    </ItemGroup>

    <!-- The analyzer assembly packs under analyzers/dotnet/fs, the FSharp.Analyzers.SDK convention; the SDK and the
         compiler service are the host's and do not pack. -->
    <Target Name="_RanvierAnalyzerInPackage">
        <ItemGroup>
            <TfmSpecificPackageFile Include="$(TargetPath)" PackagePath="analyzers/dotnet/fs" />
        </ItemGroup>
    </Target>
```

`tools/Ranvier.Analyzers/build/Ranvier.Analyzers.props`:

```xml
<Project>
    <!-- Evaluated before the project body, so a project's own value wins. An error-promoted finding fails the build. -->
    <PropertyGroup>
        <FSharpAnalyzersContinueOnError Condition="'$(FSharpAnalyzersContinueOnError)' == ''">false</FSharpAnalyzersContinueOnError>
    </PropertyGroup>
</Project>
```

`tools/Ranvier.Analyzers/build/Ranvier.Analyzers.targets`:

```xml
<Project>
    <!-- RanvierAnalyze=true runs the analyzers after each compile; `dotnet msbuild -t:AnalyzeFSharpProject` runs them on
         demand. -->
    <PropertyGroup>
        <FSharpAnalyzersOtherFlags>$(FSharpAnalyzersOtherFlags) --analyzers-path &quot;$(MSBuildThisFileDirectory)../analyzers/dotnet/fs&quot;</FSharpAnalyzersOtherFlags>
        <RunAnalyzersDuringBuild Condition="'$(RanvierAnalyze)' == 'true'">true</RunAnalyzersDuringBuild>
    </PropertyGroup>
</Project>
```

`tools/Ranvier.Analyzers/README.md`:

````markdown
# Ranvier.Analyzers

F# analyzers that report Ranvier traps at build time. Built against FSharp.Analyzers.SDK 0.41.0.

```xml
<PackageReference Include="Ranvier.Analyzers" Version="0.1.0-preview.1" PrivateAssets="all" />
<PropertyGroup>
  <RanvierAnalyze>true</RanvierAnalyze>
</PropertyGroup>
```

Install the CLI in the tool manifest: `dotnet tool install fsharp-analyzers --version 0.41.0`. `dotnet build` then
prints `Foo.fs(12,9): warning RAN001: ...` after each build that compiles. The rules and their fixes are listed at
https://ranvier-org.github.io/Ranvier/analyzers/.
````

In `.github/scripts/packages.cjs`, add after `checkPackage`:

```js
// The problems of an analyzer .nupkg: no lib/ entries, the assembly under analyzers/dotnet/fs, and the build assets.
function checkAnalyzerPackage(buffer, expected) {
  const problems = [];
  const entries = zipEntries(buffer);
  const nuspec = [...entries.keys()].find(name => !name.includes('/') && name.endsWith('.nuspec'));
  if (!nuspec) return [`${expected.id}: no .nuspec`];
  const manifest = entries.get(nuspec)().toString('utf8');
  if (property(manifest, 'id') !== expected.id) problems.push(`${expected.id}: nuspec id is ${property(manifest, 'id')}`);
  if (property(manifest, 'version') !== expected.version) problems.push(`${expected.id}: nuspec version is ${property(manifest, 'version')}`);
  if ([...entries.keys()].some(name => name.startsWith('lib/'))) problems.push(`${expected.id}: has lib/ entries`);
  for (const file of [`analyzers/dotnet/fs/${expected.assembly}.dll`, `build/${expected.id}.props`, `build/${expected.id}.targets`]) {
    if (!entries.has(file)) problems.push(`${expected.id}: missing ${file}`);
  }
  return problems;
}
```

Replace `check` with a version that also takes `analyzerProjects` (paths of analyzer `.fsproj` files) and expects
each one's single package, without a traced twin or a symbol package:

```js
// The problems of a package directory against the packable projects under src and the analyzer projects; empty when
// it holds exactly the expected packages, each library package with its symbol package.
function check(packagesDirectory, sourceDirectory, analyzerProjects = []) {
  const projects = fs.readdirSync(sourceDirectory, { withFileTypes: true })
    .filter(entry => entry.isDirectory() && fs.existsSync(path.join(sourceDirectory, entry.name, `${entry.name}.fsproj`)))
    .map(entry => ({ name: entry.name, xml: fs.readFileSync(path.join(sourceDirectory, entry.name, `${entry.name}.fsproj`), 'utf8') }))
    .filter(({ xml }) => property(xml, 'IsPackable') !== 'false')
    .map(({ name, xml }) => projectPackage(xml, name));
  if (projects.length === 0) return [`no project under ${sourceDirectory}`];
  const expected = expectedPackages(projects);
  const analyzers = new Map(analyzerProjects.map(file => {
    const pkg = { ...projectPackage(fs.readFileSync(file, 'utf8'), path.basename(file, '.fsproj')), analyzer: true };
    return [`${pkg.id}.${pkg.version}.nupkg`, pkg];
  }));
  const files = fs.existsSync(packagesDirectory) ? fs.readdirSync(packagesDirectory) : [];
  const problems = [];
  for (const file of files.filter(f => f.endsWith('.nupkg') && !expected.has(f) && !analyzers.has(f))) problems.push(`unexpected package ${file}`);
  for (const [file, pkg] of expected) {
    if (!files.includes(file)) {
      problems.push(`missing package ${file}`);
      continue;
    }
    if (!files.includes(file.replace(/\.nupkg$/, '.snupkg'))) problems.push(`missing symbol package for ${file}`);
    problems.push(...checkPackage(fs.readFileSync(path.join(packagesDirectory, file)), pkg));
  }
  for (const [file, pkg] of analyzers) {
    if (!files.includes(file)) problems.push(`missing package ${file}`);
    else problems.push(...checkAnalyzerPackage(fs.readFileSync(path.join(packagesDirectory, file)), pkg));
  }
  return problems;
}
```

Export `checkAnalyzerPackage`; the CLI entry reads `const [packagesDirectory = 'bin', sourceDirectory = 'src',
...analyzerProjects] = process.argv.slice(2);` and passes them to `check`.

In `.github/workflows/ci.yml`, the `packages` job's check step runs
`node .github/scripts/packages.cjs bin src tools/Ranvier.Analyzers/Ranvier.Analyzers.fsproj`.

`build.fsx`, in `module Stage` after `packTraced`:

```fsharp
    /// Packs the analyzers into the output directory; publish pushes them with the libraries.
    let packAnalyzers =
        input {
            let! config = Options.config

            return
                stage "pack analyzers" {
                    quiet

                    run (
                        cmd
                            $"dotnet pack tools/Ranvier.Analyzers/Ranvier.Analyzers.fsproj -c {config} -v q -o {Repo.VirtualFileSystem.bin.ToString ()}"
                    )
                }
        }
```

and `Stage.packAnalyzers` after `Stage.packTraced` in the `publish` command.

- [ ] **Step 4: Add the consumer case to the compatibility script**

In `tests/check-package-compatibility.ps1`, inside `try`, after the `foreach ($traced ...)` loop and before
`if ($CheckFable)`, add a block that runs when `-CheckFable` is not set:

```powershell
    if (-not $CheckFable) {
        # An F# consumer of Ranvier.Analyzers: RanvierAnalyze reports RAN002 as a build warning through the package's
        # build/ targets and the fsharp-analyzers tool.
        Invoke-DotNet pack (Join-Path $repo 'tools/Ranvier.Analyzers/Ranvier.Analyzers.fsproj') -c Release -o $feed
        $analyzerConsumer = Join-Path $temp 'analyzer-consumer'
        New-Item -ItemType Directory -Path (Join-Path $analyzerConsumer '.config') | Out-Null
        Copy-Item (Join-Path $consumer 'NuGet.Config') $analyzerConsumer
        Copy-Item (Join-Path $repo 'global.json') $analyzerConsumer
        @'
{ "version": 1, "isRoot": true, "tools": { "fsharp-analyzers": { "version": "0.41.0", "commands": [ "fsharp-analyzers" ], "rollForward": false } } }
'@ | Set-Content -LiteralPath (Join-Path $analyzerConsumer '.config/dotnet-tools.json')
        $analyzerVersion = ([xml](Get-Content -LiteralPath (Join-Path $repo 'tools/Ranvier.Analyzers/Ranvier.Analyzers.fsproj') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ }
        @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RanvierAnalyze>true</RanvierAnalyze>
    <RestorePackagesPath>$temp/packages</RestorePackagesPath>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Library.fs" />
    <PackageReference Include="Fable.Core" Version="5.3.0" />
    <PackageReference Include="Ranvier.Analyzers" Version="$analyzerVersion" PrivateAssets="all" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $analyzerConsumer 'AnalyzerConsumer.fsproj')
        @'
module Library

type Policy =
    | Queue
    | Drop of int

let queued (policy: Policy) = policy.IsQueue
'@ | Set-Content -LiteralPath (Join-Path $analyzerConsumer 'Library.fs')
        Push-Location $analyzerConsumer
        try {
            Invoke-DotNet tool restore
            $build = & dotnet build AnalyzerConsumer.fsproj -c Release 2>&1 | Out-String
            if ($LASTEXITCODE -ne 0) { throw "The analyzer consumer failed to build:`n$build" }
            if ($build -notmatch 'warning RAN002') { throw "The analyzer consumer's build reported no RAN002:`n$build" }
        } finally { Pop-Location }
        Write-Host 'Analyzer package consumer reports RAN002'
    }
```

- [ ] **Step 5: Run the checks**

Run: `node --test .github/scripts/*.test.cjs`, then `dotnet fsi build.fsx -- publish --dry-publish --skip-tests`, then
`node .github/scripts/packages.cjs bin src tools/Ranvier.Analyzers/Ranvier.Analyzers.fsproj`, then
`pwsh ./tests/check-package-compatibility.ps1`
Expected: every test passes; the dry publish lists `Ranvier.Analyzers.0.1.0-preview.1.nupkg`; `packages.cjs` reports
the packages match; the compatibility script prints `Analyzer package consumer reports RAN002`.

- [ ] **Step 6: Commit**

```bash
dotnet fantomas build.fsx
rtk git add tools/Ranvier.Analyzers .github build.fsx tests/check-package-compatibility.ps1
rtk git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-12-commit.txt
```

Commit subject: `build(analyzers): pack Ranvier.Analyzers with its build assets and check a consumer`. The package
reaches NuGet with the next `build.fsx publish` (spec Decision 13); this plan pushes nothing.

---

## Phase 2: view rules

The phase starts with `git fetch origin && git merge origin/master` and clean runs of the suite and `analyze`. The
view symbols bind to `Ranvier.View.Foundation.*` names; after each merge, re-run the suite before editing, since
`feat/dom-twin` and `feat/arms-match` change the view.

### Task 13: View symbols and RAN020, reactive reads at template build time

**Files:**
- Modify: `tools/Ranvier.Analyzers/Symbols.fs` (module `View`)
- Create: `tools/Ranvier.Analyzers/Rules/View.fs` (compiled after `Rules/Core.fs`)
- Modify: `tools/Ranvier.Analyzers/Analyzers.fs`
- Create: `tools/Ranvier.Analyzers.Tests/Ran020.fs`, `docs/content/analyzers/ran020.md`
- Modify: `docs/content/analyzers/index.md`, `docs/Site.fs`, `docs/content/view/troubleshooting.md`

**Interfaces:**
- Consumes: `Symbols.Core.isReactiveRead`, `Index.*`.
- Produces: `Symbols.View.{Builder, Attributes, Properties, Hooks, Unsafe, Component, ComponentType, Sugar,
  SugarComponent, Element, Elements, Source, Hydration, Microtask}` (entity full names),
  `Symbols.View.isRegionLambda: FSharpMemberOrFunctionOrValue -> int -> bool`; `Rules.View.inRegion: Step list -> bool`
  (private); `Rules.View.ran020`; `Analyzers.ran020Cli`, `Analyzers.ran020Editor`.

- [ ] **Step 1: Write the failing tests**

`tools/Ranvier.Analyzers.Tests/Ran020.fs`:

```fsharp
module Ranvier.Analyzers.Tests.Ran020

open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private header =
    "module Fixture\nopen Ranvier\nopen Ranvier.View.Foundation\nopen Ranvier.View.Foundation.Representation\nopen Ranvier.View.Foundation.Template\nopen Ranvier.View.Foundation.Sugar\nlet count = createSignal 0\n"

let private findings (body: string) =
    run Analyzers.ran020Cli (context (ranvier.Force ()) (header + body))

let private codes body = findings body |> List.map _.Code

[<Tests>]
let tests =
    testList
        "RAN020"
        [
            test "a static text yield, with its exact message" {
                Expect.equal
                    (findings "let c = Component.template (fun _ () -> ()) (div () { string count.Value })\n"
                     |> List.map _.Message)
                    [
                        "`count.Value` is read once, when the template is built, so this text never updates. Yield a reader: `fun _ -> string count.Value`."
                    ]
                    "one message"
            }

            test "t.text with a string" {
                Expect.equal
                    (codes "let c = Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.text (string count.Value) |])\n")
                    [ "RAN020" ]
                    "explicit text"
            }

            test "a static attribute" {
                Expect.equal
                    (codes "let c = Component.template (fun _ () -> ()) (div (Attrs.className (string count.Value)) { \"x\" })\n")
                    [ "RAN020" ]
                    "className value"
            }

            test "renderOf's props" {
                let body =
                    "let child = Component.template (fun _ (n: int) -> n) (span () { \"x\" })\nlet c = Component.template (fun _ () -> ()) (div () { renderOf child count.Value })\n"

                Expect.equal (codes body) [ "RAN020" ] "renderOf"
            }

            test "a setup that returns a read" {
                Expect.equal
                    (codes "let c = Component.template (fun _ () -> count.Value) (div () { \"x\" })\n")
                    [ "RAN020" ]
                    "setup return"
            }

            test "render's props reader that returns a read" {
                let body =
                    "type S = { Count: Signal<int> }\nlet child = Component.template (fun _ (n: int) -> n) (span () { \"x\" })\nlet c = Component.template (fun _ (s: S) -> s) (div () { render child (fun s -> s.Count.Value) })\n"

                Expect.equal (codes body) [ "RAN020" ] "render props"
            }

            test "a reader yield" {
                Expect.isEmpty
                    (codes "let c = Component.template (fun _ () -> count) (div () { fun (c: Signal<int>) -> string c.Value })\n")
                    "reader"
            }

            test "a Source part" {
                Expect.isEmpty
                    (codes "let c = Component.template (fun _ () -> count) (div () { from (fun (c: Signal<int>) -> c) |> ignore; \"x\" })\n")
                    "from"
            }

            test "Peek in a static yield" {
                Expect.isEmpty
                    (codes "let c = Component.template (fun _ () -> ()) (div () { string count.Peek })\n")
                    "untracked"
            }

            test "a read in an event handler" {
                Expect.isEmpty
                    (codes "let c = Component.template (fun _ () -> count) (button (Events.onClick (fun (c: Signal<int>) _ -> c.Value <- c.Value + 1)) { \"+\" })\n")
                    "handler"
            }

            test "setup reading a value to seed createSignal" {
                Expect.isEmpty
                    (codes "type P = { Start: int }\nlet c = Component.template (fun _ (p: P) -> createSignal (count.Value + p.Start)) (div () { \"x\" })\n")
                    "seed"
            }

            test "the text fix type-checks and clears the finding" {
                let source =
                    header + "let c = Component.template (fun _ () -> ()) (div () { string count.Value })\n"

                let fix = (run Analyzers.ran020Cli (context (ranvier.Force ()) source)).Head.Fixes |> List.exactlyOne
                let fixed = applyFix source fix
                Expect.isEmpty (run Analyzers.ran020Cli (context (ranvier.Force ()) fixed)) "no finding after the fix"
            }
        ]
```

The `Source part` case writes `from ... |> ignore` inside a CE, which may not type-check; if it does not, use the
docs' form `span () { from (fun (c: Signal<int>) -> c) }` with an `int`-to-string source and ledger the change.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`
Expected: FAIL to compile: `ran020Cli` is not defined.

- [ ] **Step 3: Add the view symbols**

Append to `Symbols.fs`:

```fsharp
/// The view layer's builders, sugar and definitions.
module View =
    [<Literal>]
    let Builder = "Ranvier.View.Foundation.Template.TemplateBuilder`1"

    [<Literal>]
    let Attributes = "Ranvier.View.Foundation.Template.AttributeBuilder`1"

    [<Literal>]
    let Properties = "Ranvier.View.Foundation.Template.PropertyBuilder`1"

    [<Literal>]
    let Hooks = "Ranvier.View.Foundation.Template.HookBuilder`1"

    [<Literal>]
    let Unsafe = "Ranvier.View.Foundation.Template.UnsafeBuilder`1"

    [<Literal>]
    let Component = "Ranvier.View.Foundation.Template.Component"

    [<Literal>]
    let ComponentType = "Ranvier.View.Foundation.Template.Component`1"

    [<Literal>]
    let Sugar = "Ranvier.View.Foundation.Sugar.Builders"

    [<Literal>]
    let SugarComponent = "Ranvier.View.Foundation.Sugar.Builders.Component"

    [<Literal>]
    let Element = "Ranvier.View.Foundation.Sugar.Builders.Element`2"

    [<Literal>]
    let Elements = "Ranvier.View.Foundation.Sugar.Elements"

    [<Literal>]
    let Source = "Ranvier.View.Foundation.Template.Source`2"

    [<Literal>]
    let Hydration = "Ranvier.View.Foundation.Hydration"

    [<Literal>]
    let Microtask = "Ranvier.View.Foundation.Microtask"

    /// Whether argument `index` of a call of `m` is read per region read: the arms of `matchWith` and `branch`, the
    /// key and items of `each`, `forEach` and `forEachFixed`.
    let isRegionLambda (m: FSharpMemberOrFunctionOrValue) (index: int) : bool =
        let entity = enclosing m

        (entity = Builder || entity = Sugar)
        && (match m.LogicalName, index with
            | ("matchWith" | "branch"), 1 -> true
            | ("each" | "forEach" | "forEachFixed"), (0 | 1) -> true
            | _ -> false)
```

`Symbols.fs` is compiled before `Index.fs`, so the path test `inRegion` lives in `Rules/View.fs`. Check `Source`'s full
name against the compiled assembly before relying on it (`Template.fs:2114` declares `type Source<'inst, 'value>` in module
`Ranvier.View.Foundation.Template`).

- [ ] **Step 4: Implement RAN020**

`tools/Ranvier.Analyzers/Rules/View.fs`:

```fsharp
/// RAN020-RAN025: view layer traps.
module Ranvier.Analyzers.Rules.View

open FSharp.Analyzers.SDK
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

let private parameterAt (m: FSharpMemberOrFunctionOrValue) (index: int) : FSharpParameter option =
    m.CurriedParameterGroups |> Seq.concat |> Seq.tryItem index

let private isString (t: FSharpType) =
    t.HasTypeDefinition && t.TypeDefinition.TryFullName = Some "System.String"

let private isStatic (t: FSharpType) =
    not t.IsFunctionType
    && not (t.HasTypeDefinition && t.TypeDefinition.TryFullName = Some Source)

/// The build-time site that argument `index` of a call of `m` is.
let private buildSite (m: FSharpMemberOrFunctionOrValue) (index: int) : BuildSite option =
    let entity = Symbols.enclosing m

    match parameterAt m index with
    | Some parameter when entity = Element && m.LogicalName = "Yield" && isString parameter.Type -> Some BuildSite.Text
    | Some parameter when entity = Builder && m.LogicalName = "text" && isString parameter.Type -> Some BuildSite.Text
    | Some parameter when
        (entity = Attributes || entity = Properties)
        && parameter.Name = Some "value"
        && isStatic parameter.Type
        ->
        Some (BuildSite.Part (if entity = Attributes then "attribute" else "property"))
    | Some _ when entity = Sugar && m.LogicalName = "renderOf" && index = 1 -> Some BuildSite.RenderOf
    | _ -> None

/// The build-time site whose argument holds the expression at `path` outside any lambda, with the callee and index.
let private buildSiteOf (path: Step list) =
    let rec go (steps: Step list) =
        match steps with
        | [] -> None
        | Step.Lambda (_, true) :: rest -> go rest
        | Step.Lambda (_, false) :: _ -> None
        | Step.Argument (callee, index, _) :: rest ->
            match buildSite callee index with
            | Some site -> Some (callee, index, site)
            | None -> go rest
        | _ :: rest -> go rest

    go path

/// Whether `m` has an overload of the same name that takes a reader (a parameter named `read`).
let private hasReaderOverload (m: FSharpMemberOrFunctionOrValue) =
    match m.DeclaringEntity with
    | Some entity ->
        entity.MembersFunctionsAndValues
        |> Seq.exists (fun candidate ->
            candidate.LogicalName = m.LogicalName
            && candidate.CurriedParameterGroups |> Seq.concat |> Seq.exists (fun p -> p.Name = Some "read"))
    | None -> false

/// The expressions in the return position of `expr`: through `let`, sequencing, records and tuples.
let rec private returned (expr: FSharpExpr) : FSharpExpr list =
    match expr with
    | Let (_, body) -> returned body
    | Sequential (_, last) -> returned last
    | NewRecord (_, fields) -> fields
    | NewTuple (_, items) -> items
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

    let staticSites =
        [
            for read in calls do
                if Symbols.Core.isReactiveRead read.Callee && not (inRegion read.Path) then
                    match buildSiteOf read.Path with
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
                    if hasReaderOverload call.Callee then
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

        for call in calls do
            let entity = Symbols.enclosing call.Callee

            let setup =
                (entity = Component && call.Callee.LogicalName = "define")
                || (entity = SugarComponent && call.Callee.LogicalName = "template")

            let renderProps =
                (entity = Sugar || entity = Builder) && call.Callee.LogicalName = "render"

            if setup && not call.Arguments.IsEmpty then
                for expr in returned (bodyAfter 2 call.Arguments[0]) do
                    if readCall expr then
                        message
                            "RAN020"
                            Severity.Warning
                            expr.Range
                            $"`{text input expr.Range}` is read once per instance, when setup runs untracked, so the instance holds the value at mount. Return the signal or memo itself and read it in the template."
                            []

            if renderProps && call.Arguments.Length = 2 then
                for expr in returned (bodyAfter 1 call.Arguments[1]) do
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
                            $"`render`'s props reader runs once per instance, so the child receives the value of `{readText}` at mount. Pass the signal: `fun s -> {signal}`."
                            []
    ]

let ran020: Rule =
    {
        Code = "RAN020"
        Title = "Reactive read at template build time"
        Gate = Symbols.Assemblies.View
        Run = buildTimeReads
    }
```

The site-5 message writes `fun s -> {signal}` with the receiver's text, which names the lambda's own parameter only
when the fixture uses `s`; use the lambda parameter's display name (the `Lambda` of `call.Arguments[1]`) in place of
`s` if the reviewer flags it.

Register `Rules.View.ran020` and the pair (title "Reactive read at template build time", HelpUri
`https://ranvier-org.github.io/Ranvier/analyzers/ran020/`); add `<Compile Include="Rules/View.fs" />` after
`Rules/Core.fs`.

- [ ] **Step 5: Write the docs page and the troubleshooting link**

`docs/content/analyzers/ran020.md`, in the RAN010 page's layout: the site-1 message; **Cause.** "`Component.define`
builds a template once, when the definition is created, and `setup` and `render`'s props reader run once per
instance, untracked. A reactive read in a static text or attribute, in `renderOf`'s props, in setup's returned value
or in `render`'s props reader is a snapshot that never updates. A site inside an `arms` function or an items reader is
RAN022's."; **Fix.** "Yield or pass a reader (`fun _ -> ...`), or pass the signal and read it where it is used. The
rule's fix wraps a static text, and an attribute that has a reader overload."; `### Triggers`:

```fsharp
module Example

open Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar

let count = createSignal 0

let counter = Component.template (fun _ () -> ()) (span () { string count.Value })
```

`### Fixed`:

```fsharp
module Example

open Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar

let count = createSignal 0

let counter =
    Component.template (fun _ () -> count) (span () { fun (c: Signal<int>) -> string c.Value })
```

Link it from the entry "A child does not follow a parent value" at `docs/content/view/troubleshooting.md:75-76`; add
the index row and the menu page.

- [ ] **Step 6: Run the tests, the gate, and commit**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`, then `dotnet fsi build.fsx -- analyze`
Expected: `Passed!` (RAN020: 12 cases), `analyze` exits 0. Findings in `docs/samples/view` or the playground that
document an intended snapshot take a suppression comment with the reason (spec Decision 15); ledger each.

```bash
dotnet fantomas tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/Site.fs
rtk git add tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/content docs/Site.fs
rtk git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-13-commit.txt
```

Commit subject: `feat(analyzers): RAN020 reports reactive reads at template build time`.

### Task 14: RAN021, unstable list keys

**Files:**
- Modify: `tools/Ranvier.Analyzers/Rules/View.fs`, `tools/Ranvier.Analyzers/Analyzers.fs`
- Create: `tools/Ranvier.Analyzers.Tests/Ran021.fs`, `docs/content/analyzers/ran021.md`
- Modify: `docs/content/analyzers/index.md`, `docs/Site.fs`, `docs/content/view/collections.md`

**Interfaces:**
- Consumes: `Index.CallSite`, `Symbols.Core.isReactiveRead`, `Symbols.View.{Builder, Sugar}`.
- Produces: `Rules.View.ran021`; `Analyzers.ran021Cli`, `Analyzers.ran021Editor`.

- [ ] **Step 1: Write the failing tests**

`tools/Ranvier.Analyzers.Tests/Ran021.fs`:

```fsharp
module Ranvier.Analyzers.Tests.Ran021

open Expecto
open FSharp.Analyzers.SDK
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private header =
    "module Fixture\nopen System\nopen Ranvier\nopen Ranvier.View.Foundation\nopen Ranvier.View.Foundation.Representation\nopen Ranvier.View.Foundation.Template\nopen Ranvier.View.Foundation.Sugar\ntype Todo = { Id: int; Label: string }\ntype Plain(id: int) =\n    member _.Id = id\nlet todos = createSignal [| { Id = 1; Label = \"a\" } |]\nlet row = li () { fun (row: unit -> Todo) -> (row ()).Label }\n"

let private findings (key: string) =
    let body =
        $"let view = Component.template (fun _ () -> todos) (ul () {{ forEach ({key}) (fun (s: Signal<Todo array>) -> s.Value) row }})\n"

    run Analyzers.ran021Cli (context (ranvier.Force ()) (header + body))

let private severities key = findings key |> List.map _.Severity

[<Tests>]
let tests =
    testList
        "RAN021"
        [
            test "a positional key, with its exact message" {
                Expect.equal
                    (findings "fun (todo: Todo) -> Array.IndexOf (todos.Peek, todo)" |> List.map _.Message)
                    [
                        "This key is the item's position (`Array.IndexOf`), so moving or inserting an item remounts every row after it. Key by a value unique to the item, such as an id."
                    ]
                    "one message"
            }

            test "Array.findIndex is positional" {
                Expect.equal
                    (severities "fun (todo: Todo) -> Array.findIndex ((=) todo) todos.Peek")
                    [ Severity.Warning ]
                    "findIndex"
            }

            test "Guid.NewGuid makes a key per pass" {
                Expect.equal (severities "fun (_: Todo) -> Guid.NewGuid ()") [ Severity.Warning ] "NewGuid"
            }

            test "a module mutable written by the key makes a key per pass" {
                let source =
                    header
                    + "let mutable next = 0\nlet view = Component.template (fun _ () -> todos) (ul () { forEach (fun (_: Todo) -> next <- next + 1; next) (fun (s: Signal<Todo array>) -> s.Value) row })\n"

                Expect.equal
                    (run Analyzers.ran021Cli (context (ranvier.Force ()) source) |> List.map _.Severity)
                    [ Severity.Warning ]
                    "captured mutable"
            }

            test "a reactive read in the key" {
                let source = header + "let offset = createSignal 0\n"

                let body =
                    "let view = Component.template (fun _ () -> todos) (ul () { forEach (fun (todo: Todo) -> todo.Id + offset.Value) (fun (s: Signal<Todo array>) -> s.Value) row })\n"

                Expect.equal
                    (run Analyzers.ran021Cli (context (ranvier.Force ()) (source + body)) |> List.map _.Severity)
                    [ Severity.Warning ]
                    "tracked key"
            }

            test "forEach keyed by the record item itself is Info" {
                Expect.equal (severities "fun (todo: Todo) -> todo") [ Severity.Info ] "identity"
            }

            test "a key compared by reference is Info" {
                let source = header + "let plainItems = createSignal [| Plain 1 |]\nlet plainRow = li () { \"x\" }\n"

                let body =
                    "let view = Component.template (fun _ () -> plainItems) (ul () { forEach (fun (item: Plain) -> item) (fun (s: Signal<Plain array>) -> s.Value) plainRow })\n"

                Expect.equal
                    (run Analyzers.ran021Cli (context (ranvier.Force ()) (source + body)) |> List.map _.Severity)
                    [ Severity.Info ]
                    "reference key"
            }

            test "an id key" { Expect.isEmpty (findings "fun (todo: Todo) -> todo.Id") "stable" }

            test "a string list keyed by id is correct" {
                let source = header + "let names = createSignal [| \"a\" |]\nlet nameRow = li () { \"x\" }\n"

                let body =
                    "let view = Component.template (fun _ () -> names) (ul () { forEach id (fun (s: Signal<string array>) -> s.Value) nameRow })\n"

                Expect.isEmpty (run Analyzers.ran021Cli (context (ranvier.Force ()) (source + body))) "strings"
            }

            test "a ref cell written by the key makes a key per pass" {
                let source =
                    header
                    + "let counter = ref 0\nlet view = Component.template (fun _ () -> todos) (ul () { forEach (fun (_: Todo) -> counter.Value <- counter.Value + 1; counter.Value) (fun (s: Signal<Todo array>) -> s.Value) row })\n"

                Expect.equal
                    (run Analyzers.ran021Cli (context (ranvier.Force ()) source) |> List.map _.Severity)
                    [ Severity.Warning ]
                    "ref cell"
            }

            test "a tuple key is stable" {
                Expect.isEmpty (findings "fun (todo: Todo) -> todo.Id, todo.Label") "structural"
            }

            test "t.each with a positional key" {
                let body =
                    "let rowDef = Component.define (fun _ (row: unit -> Todo) -> row) (fun t -> t.li [||] [||])\nlet view = Component.define (fun _ () -> todos) (fun t -> t.ul [||] [| t.each (fun (todo: Todo) -> Array.IndexOf (todos.Peek, todo)) (fun (s: Signal<Todo array>) -> s.Value) rowDef |])\n"

                Expect.equal
                    (run Analyzers.ran021Cli (context (ranvier.Force ()) (header + body)) |> List.map _.Code)
                    [ "RAN021" ]
                    "builder member"
            }
        ]
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`
Expected: FAIL to compile: `ran021Cli` is not defined.

- [ ] **Step 3: Implement RAN021**

Append to `Rules/View.fs`:

```fsharp
let private isKeyed (m: FSharpMemberOrFunctionOrValue) =
    let entity = Symbols.enclosing m

    (entity = Builder && m.LogicalName = "each")
    || (entity = Sugar
        && (m.LogicalName = "each" || m.LogicalName = "forEach" || m.LogicalName = "forEachFixed"))

let rec private subExpressions (expr: FSharpExpr) : FSharpExpr list =
    expr :: List.collect subExpressions expr.ImmediateSubExpressions

let private impureCall (m: FSharpMemberOrFunctionOrValue) =
    match Symbols.enclosing m, m.LogicalName with
    | "System.Guid", "NewGuid"
    | "System.DateTime", ("get_Now" | "get_UtcNow")
    | "System.Object", ".ctor"
    | "Microsoft.FSharp.Core.FSharpRef`1", ("set_Value" | "set_contents")
    | Symbols.Operators, (":=" | "op_ColonEquals" | "incr" | "decr") -> true
    | ("System.Random" | "System.Diagnostics.Stopwatch"), _ -> true
    | _ -> false

let private positionalCall (m: FSharpMemberOrFunctionOrValue) =
    match Symbols.enclosing m, m.LogicalName with
    | ("Microsoft.FSharp.Collections.ArrayModule"
      | "Microsoft.FSharp.Collections.ListModule"
      | "Microsoft.FSharp.Collections.SeqModule"),
      ("indexOf" | "findIndex" | "tryFindIndex") -> true
    | _, "IndexOf" -> true
    | _ -> false

let private isPrimitive (t: FSharpType) =
    t.HasTypeDefinition
    && (t.TypeDefinition.IsValueType
        || t.TypeDefinition.TryFullName = Some "System.String")

let private comparedByReference (t: FSharpType) =
    t.HasTypeDefinition
    && (let entity = t.TypeDefinition

        entity.IsClass
        && not entity.IsFSharpRecord
        && not entity.IsFSharpUnion
        && entity.TryFullName <> Some "System.String"
        && not (
            entity.MembersFunctionsAndValues
            |> Seq.exists (fun m -> m.LogicalName = "Equals" && m.IsOverrideOrExplicitInterfaceImplementation)
        ))

let private unstableKeys (input: Input) : Message list =
    [
        for site in input.Index.Calls do
            if isKeyed site.Callee && not site.Arguments.IsEmpty then
                let key = site.Arguments[0]

                match key with
                | Lambda (item, body) ->
                    let inner = subExpressions body

                    let impure =
                        inner
                        |> List.tryPick (fun expr ->
                            match expr with
                            | Call (_, m, _, _, _)
                            | NewObject (m, _, _) when impureCall m -> Some (text input expr.Range)
                            | ValueSet (v, _) when not (Range.rangeContainsRange key.Range v.DeclarationLocation) ->
                                Some (text input expr.Range)
                            | _ -> None)

                    let positional =
                        inner
                        |> List.tryPick (fun expr ->
                            match expr with
                            | Call (_, m, _, _, _) when positionalCall m ->
                                // The call as written, up to its arguments: `Array.IndexOf`, `List.findIndex`.
                                let written = text input expr.Range
                                let stop = written.IndexOfAny [| ' '; '(' |]
                                Some (if stop > 0 then written.Substring (0, stop) else written)
                            | _ -> None)

                    let tracked =
                        inner
                        |> List.tryPick (fun expr ->
                            match expr with
                            | Call (Some _, m, _, _, _) when Symbols.Core.isReactiveRead m -> Some (text input expr.Range)
                            | _ -> None)

                    let structuredItem =
                        item.FullType.HasTypeDefinition
                        && (item.FullType.TypeDefinition.IsFSharpRecord || item.FullType.TypeDefinition.IsFSharpUnion)

                    let identity =
                        site.Callee.LogicalName = "forEach"
                        && structuredItem
                        && (match body with
                            | Value v -> v = item
                            | Call (None, m, _, _, [ Value v ]) -> m.LogicalName = "id" && v = item
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
                    | None, None, Some read ->
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
                    | None, None, None when comparedByReference body.Type && not (isPrimitive body.Type) ->
                        message
                            "RAN021"
                            Severity.Info
                            key.Range
                            $"The key `{body.Type.Format FSharpDisplayContext.Empty}` is compared by reference, so a rebuilt item gets a new key and its row remounts. Key by a value with structural equality, such as an id."
                            []
                    | _ -> ()
                | _ -> ()
    ]

let ran021: Rule =
    {
        Code = "RAN021"
        Title = "Unstable list key"
        Gate = Symbols.Assemblies.View
        Run = unstableKeys
    }
```

Register `Rules.View.ran021` and the pair (title "Unstable list key", HelpUri
`https://ranvier-org.github.io/Ranvier/analyzers/ran021/`).

- [ ] **Step 4: Write the docs page and the collections link**

`docs/content/analyzers/ran021.md`, in the RAN010 page's layout, with the five rows of the spec's RAN021 table as the
**Cause**, **Fix.** "Key by a value unique to the item, such as an id field.", `### Triggers`:

```fsharp
module Example

open System
open Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar

type Todo = { Id: int; Label: string }

let todos = createSignal [| { Id = 1; Label = "a" } |]
let row = li () { fun (row: unit -> Todo) -> (row ()).Label }

let list =
    Component.template (fun _ () -> todos) (
        ul () { forEach (fun (todo: Todo) -> Array.IndexOf (todos.Peek, todo)) (fun (s: Signal<Todo array>) -> s.Value) row }
    )
```

and `### Fixed` the same module with the key `(fun (todo: Todo) -> todo.Id)`. Link it from the "a key function must
be pure" paragraph at `docs/content/view/collections.md:58-63`; add the index row and the menu page.

- [ ] **Step 5: Run the tests, the gate, and commit**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`, then `dotnet fsi build.fsx -- analyze`
Expected: `Passed!` (RAN021: 12 cases), `analyze` exits 0 (Info findings do not fail the gate unless promoted:
`--treat-as-error RAN*` promotes them; an Info finding on intended code takes a suppression with its reason).

```bash
dotnet fantomas tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/Site.fs
rtk git add tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/content docs/Site.fs
rtk git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-14-commit.txt
```

Commit subject: `feat(analyzers): RAN021 reports unstable list keys`.

### Task 15: RAN022, a definition created on every region read

**Files:**
- Modify: `tools/Ranvier.Analyzers/Rules/View.fs`, `tools/Ranvier.Analyzers/Analyzers.fs`
- Create: `tools/Ranvier.Analyzers.Tests/Ran022.fs`, `docs/content/analyzers/ran022.md`
- Modify: `docs/content/analyzers/index.md`, `docs/Site.fs`, `docs/content/view/troubleshooting.md`

**Interfaces:**
- Consumes: `Symbols.View.isRegionLambda`, `Input.Project`.
- Produces: `Rules.View.ran022`; `Analyzers.ran022Cli`, `Analyzers.ran022Editor`.

- [ ] **Step 1: Write the failing tests**

`tools/Ranvier.Analyzers.Tests/Ran022.fs`:

```fsharp
module Ranvier.Analyzers.Tests.Ran022

open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private header =
    "module Fixture\nopen Ranvier\nopen Ranvier.View.Foundation\nopen Ranvier.View.Foundation.Representation\nopen Ranvier.View.Foundation.Template\nopen Ranvier.View.Foundation.Sugar\ntype Page =\n    | Home\n    | About\nlet page = createSignal Home\n"

let private findings (body: string) =
    run Analyzers.ran022Cli (context (ranvier.Force ()) (header + body))

let private codes body = findings body |> List.map _.Code

let private viewOf (arms: string) =
    $"let view = Component.template (fun _ () -> page) (div () {{ matchWith (fun (p: Signal<Page>) -> p.Value) ({arms}) }})\n"

[<Tests>]
let tests =
    testList
        "RAN022"
        [
            test "armOf in function arms, with its exact message" {
                Expect.equal
                    (findings (viewOf "function Home -> arm (armOf (span () { \"home\" })) () | About -> arm (armOf (span () { \"about\" })) ()")
                     |> List.map _.Message
                     |> List.distinct)
                    [
                        "`armOf` runs on every read of this region, so the arm is a new definition each time and remounts. Bind the definition with `let` outside the template."
                    ]
                    "the message"
            }

            test "show inside arms" {
                Expect.contains
                    (codes (viewOf "function Home -> arm (armOf (show (fun _ -> true) (span () { \"x\" }))) () | About -> arm (armOf (span () { \"y\" })) ()"))
                    "RAN022"
                    "show"
            }

            test "a definition in an items reader" {
                let body =
                    "let row = li () { \"x\" }\nlet view = Component.template (fun _ () -> page) (ul () { forEach id (fun (_: Signal<Page>) -> (Component.template (fun _ () -> ()) (span () { \"x\" }) |> ignore; [| 1 |])) row })\n"

                Expect.equal (codes body) [ "RAN022" ] "items reader"
            }

            test "a static property read in arms" {
                let body =
                    "type Arms =\n    static member Home: Component<unit -> unit> = armOf (span () { \"home\" })\n"
                    + viewOf "function Home -> arm Arms.Home () | About -> arm Arms.Home ()"

                Expect.contains (codes body) "RAN022" "non-auto static property"
            }

            test "hoisted let definitions" {
                let body =
                    "let home = armOf (span () { \"home\" })\nlet about = armOf (span () { \"about\" })\n"
                    + viewOf "function Home -> arm home () | About -> arm about ()"

                Expect.isEmpty (codes body) "hoisted"
            }

            test "static member val" {
                let body =
                    "type Arms =\n    static member val Home: Component<unit -> unit> = armOf (span () { \"home\" })\n"
                    + viewOf "function Home -> arm Arms.Home () | About -> arm Arms.Home ()"

                Expect.isEmpty (codes body) "auto property"
            }

            test "a definition in a template build function outside regions" {
                Expect.isEmpty
                    (codes "let view = Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.render (Component.template (fun _ () -> ()) (span () { \"x\" })) (fun _ -> ()) |])\n")
                    "build function"
            }
        ]
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`
Expected: FAIL to compile: `ran022Cli` is not defined.

- [ ] **Step 3: Implement RAN022**

Append to `Rules/View.fs`:

```fsharp
let private isDefinitionCreator (m: FSharpMemberOrFunctionOrValue) =
    match Symbols.enclosing m, m.LogicalName with
    | Component, "define"
    | SugarComponent, ("template" | "ofMembers" | "ofClass")
    | Sugar, ("armOf" | "show" | "showElse") -> true
    | _ -> false

let rec private createsDefinition (expr: FSharpExpr) =
    match expr with
    | Call (_, m, _, _, _) when isDefinitionCreator m -> true
    | _ -> List.exists createsDefinition expr.ImmediateSubExpressions

/// Whether the getter `m` is a static `Component` property whose implementation creates a definition.
let private recreatingProperty (project: FSharpCheckProjectResults option) (m: FSharpMemberOrFunctionOrValue) =
    m.IsPropertyGetterMethod
    && not m.IsInstanceMember
    && m.ReturnParameter.Type.HasTypeDefinition
    && m.ReturnParameter.Type.TypeDefinition.TryFullName = Some ComponentType
    && (match project with
        | Some results ->
            let rec find (item: FSharpImplementationFileDeclaration) =
                match item with
                | FSharpImplementationFileDeclaration.Entity (_, items) -> List.exists find items
                | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue (value, _, body) ->
                    value.IsEffectivelySameAs m && createsDefinition body
                | FSharpImplementationFileDeclaration.InitAction _ -> false

            results.AssemblyContents.ImplementationFiles
            |> List.exists (fun file -> List.exists find file.Declarations)
        | None -> false)

let private regionDefinitions (input: Input) : Message list =
    [
        for site in input.Index.Calls do
            let creator = isDefinitionCreator site.Callee
            let property = not creator && recreatingProperty input.Project site.Callee

            if (creator || property) && inRegion site.Path then
                let name =
                    if creator then
                        site.Callee.DisplayName
                    else
                        text input site.Range

                message
                    "RAN022"
                    Severity.Warning
                    site.Range
                    $"`{name}` runs on every read of this region, so the arm is a new definition each time and remounts. Bind the definition with `let` outside the template."
                    []
    ]

let ran022: Rule =
    {
        Code = "RAN022"
        Title = "Definition created on every region read"
        Gate = Symbols.Assemblies.View
        Run = regionDefinitions
    }
```

Add `open FSharp.Compiler.CodeAnalysis` to `Rules/View.fs`. `show` inside arms reports both `show` and the `armOf`
around it; the tests assert `contains`, and the docs page says the outermost definition is the one to hoist.

Register `Rules.View.ran022` and the pair (title "Definition created on every region read", HelpUri
`https://ranvier-org.github.io/Ranvier/analyzers/ran022/`). `runEditor` passes the editor's project results when it
has them; a missing project disables the property check only.

- [ ] **Step 4: Write the docs page and the troubleshooting links**

`docs/content/analyzers/ran022.md`, in the RAN010 page's layout: **Cause.** "A definition (`armOf`,
`Component.define`, `Component.template`, `Component.ofMembers`, `Component.ofClass`, and `show`/`showElse`, which
define two components per call) created inside an `arms` function or a list's key or items reader runs on every region
read. The arm remounts on every update; Fable Debug builds raise *a template definition was created while a region was
read*. A non-auto `static member` property that creates a definition does the same on every read."; **Fix.** "Bind
the definition with `let` outside the template, or use `static member val`."; `### Triggers` (the `viewOf` fixture
with `armOf` arms, as a whole module with the opens of Task 13's page) and `### Fixed` (the hoisted `let home`/`let
about` form). Link it from the entries at `docs/content/view/troubleshooting.md:52-54` and `:68-69`; add the index row
and the menu page.

- [ ] **Step 5: Run the tests, the gate, and commit**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`, then `dotnet fsi build.fsx -- analyze`
Expected: `Passed!` (RAN022: 7 cases), `analyze` exits 0.

```bash
dotnet fantomas tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/Site.fs
rtk git add tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/content docs/Site.fs
rtk git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-15-commit.txt
```

Commit subject: `feat(analyzers): RAN022 reports definitions created on every region read`.

### Task 16: RAN023, invalid content parts on an element

**Files:**
- Modify: `tools/Ranvier.Analyzers/Rules/View.fs`, `tools/Ranvier.Analyzers/Analyzers.fs`
- Create: `tools/Ranvier.Analyzers.Tests/Ran023.fs`, `docs/content/analyzers/ran023.md`
- Modify: `docs/content/analyzers/index.md`, `docs/Site.fs`, `docs/content/view/troubleshooting.md`

**Interfaces:**
- Consumes: `Index.Step.Applied`, `Symbols.View.{Builder, Elements, Element, Unsafe, Hooks}`.
- Produces: `Rules.View.ElementSite` (`{ Tag: string; Parts: FSharpExpr list; HasChildren: bool; Range: range;
  Ancestors: string list }`), `Rules.View.elements: Input -> ElementSite list` (used again by RAN024);
  `Rules.View.ran023`; `Analyzers.ran023Cli`, `Analyzers.ran023Editor`.

- [ ] **Step 1: Write the failing tests**

`tools/Ranvier.Analyzers.Tests/Ran023.fs`:

```fsharp
module Ranvier.Analyzers.Tests.Ran023

open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private header =
    "module Fixture\nopen Ranvier\nopen Ranvier.View.Foundation\nopen Ranvier.View.Foundation.Representation\nopen Ranvier.View.Foundation.Template\nopen Ranvier.View.Foundation.Sugar\nlet markup = UnsafeHtml.Trust \"<b>x</b>\"\nlet attach (_: unit) (_: 'native) = fun () -> ()\n"

let private findings (body: string) =
    run Analyzers.ran023Cli (context (ranvier.Force ()) (header + body))

let private codes body = findings body |> List.map _.Code

[<Tests>]
let tests =
    testList
        "RAN023"
        [
            test "two content parts, with its exact message" {
                Expect.equal
                    (findings "let c = Component.define (fun _ () -> ()) (fun t -> t.div [| t.Unsafe.html markup; t.Hooks.widget attach |] [||])\n"
                     |> List.map _.Message)
                    [
                        "This element has two content parts (`Unsafe.html` and `Hooks.widget`), which fails when the definition is created. Give each content part its own element."
                    ]
                    "one message"
            }

            test "a content part and template children" {
                Expect.equal
                    (codes "let c = Component.define (fun _ () -> ()) (fun t -> t.div [| t.Unsafe.html markup |] [| t.text \"x\" |])\n")
                    [ "RAN023" ]
                    "children"
            }

            test "a content part and sugar children" {
                Expect.equal
                    (codes "let c = Component.define (fun _ () -> ()) (fun t -> div (t.Unsafe.html markup) { \"x\" })\n")
                    [ "RAN023" ]
                    "sugar children"
            }

            test "raw HTML on a void element" {
                Expect.equal
                    (codes "let c = Component.define (fun _ () -> ()) (fun t -> t.br [| t.Unsafe.html markup |] [||])\n")
                    [ "RAN023" ]
                    "void"
            }

            test "raw HTML on textarea" {
                Expect.equal
                    (codes "let c = Component.define (fun _ () -> ()) (fun t -> t.textarea [| t.Unsafe.html markup |] [||])\n")
                    [ "RAN023" ]
                    "textarea"
            }

            test "one content part" {
                Expect.isEmpty
                    (codes "let c = Component.define (fun _ () -> ()) (fun t -> t.div [| t.Unsafe.html markup |] [||])\n")
                    "valid"
            }

            test "content parts on separate elements" {
                Expect.isEmpty
                    (codes "let c = Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.div [| t.Unsafe.html markup |] [||]; t.div [| t.Hooks.widget attach |] [||] |])\n")
                    "separate"
            }

            test "parts built by a helper are not inspected" {
                Expect.isEmpty
                    (codes "let parts (t: TemplateBuilder<unit>) = [| t.Unsafe.html markup; t.Hooks.widget attach |]\nlet c = Component.define (fun _ () -> ()) (fun t -> t.div (parts t) [||])\n")
                    "false negative by design"
            }
        ]
```

`Unsafe` is reached through the builder (`t.Unsafe.html`, `Template.fs:2643`), `Hooks` through the builder or the
sugar's `Hooks` value; `widget` takes `'inst -> 'native -> (unit -> unit)` (`Template.fs:2390`).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`
Expected: FAIL to compile: `ran023Cli` is not defined.

- [ ] **Step 3: Implement the element sites and RAN023**

Append to `Rules/View.fs`:

```fsharp
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

let private isTagMember (m: FSharpMemberOrFunctionOrValue) (arguments: FSharpExpr list) =
    m.IsExtensionMember
    && Symbols.enclosing m = Builder
    && arguments.Length = 2

let private sugarTag (m: FSharpMemberOrFunctionOrValue) =
    match m.ApparentEnclosingEntity with
    | Some entity when m.IsConstructor && (Symbols.enclosing m).StartsWith (Elements + ".") ->
        Some (entity.DisplayName.Trim '`')
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
        builders |> Array.tryPick (fun (b, tag) -> if b = parameter then Some tag else None)

    let ancestors (path: Step list) =
        path
        |> List.choose (fun step ->
            match step with
            | Step.Argument (callee, 1, 2) when callee.IsExtensionMember && Symbols.enclosing callee = Builder ->
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
                    HasChildren = not (items site.Arguments[1]).IsEmpty
                    Range = site.Range
                    Ancestors = ancestors site.Path
                }
            else
                match sugarTag site.Callee, site.Path with
                | Some tag, Step.Applied parameter :: outer ->
                    let children =
                        calls
                        |> Array.exists (fun call ->
                            (call.Callee.LogicalName = "Yield" || call.Callee.LogicalName = "YieldFrom")
                            && Symbols.enclosing call.Callee = Element
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

let rec private contentPart (expr: FSharpExpr) : string option =
    match expr with
    | Coerce (_, inner) -> contentPart inner
    | Call (_, m, _, _, _) when Symbols.enclosing m = Unsafe && m.LogicalName = "html" -> Some "Unsafe.html"
    | Call (_, m, _, _, _) when Symbols.enclosing m = Hooks && m.LogicalName = "widget" -> Some "Hooks.widget"
    | _ -> None

let private voidTags =
    set [ "area"; "base"; "br"; "col"; "embed"; "hr"; "img"; "input"; "link"; "meta"; "source"; "track"; "wbr" ]

let private rawTextTags =
    set [ "script"; "style"; "iframe"; "noscript"; "xmp"; "noembed"; "noframes"; "plaintext" ]

let private invalidContent (input: Input) : Message list =
    [
        for element in elements input do
            let contents = element.Parts |> List.choose (fun part -> contentPart part |> Option.map (fun name -> name, part))

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
            | [ "Unsafe.html", part ] when
                voidTags.Contains element.Tag
                || rawTextTags.Contains element.Tag
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
```

Register `Rules.View.ran023` and the pair (title "Invalid content parts on an element", HelpUri
`https://ranvier-org.github.io/Ranvier/analyzers/ran023/`).

- [ ] **Step 4: Write the docs page and the troubleshooting link**

`docs/content/analyzers/ran023.md`, in the RAN010 page's layout: **Cause.** "`TemplateParts.element` raises
`ArgumentException` when an element has two content parts (`Unsafe.html`, `Hooks.widget`), a content part and
template children, or raw HTML on a void, raw-text, `template`, `textarea` or `title` element. The exception fires when
the definition is created, on every target. The rule reads literal parts only."; **Fix.** "Give each content part its
own element."; `### Triggers` (the two-content-part fixture as a module) and `### Fixed` (the two separate `t.div`s).
Link it from the entry at `docs/content/view/troubleshooting.md:62-64`; add the index row and the menu page.

- [ ] **Step 5: Run the tests, the gate, and commit**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`, then `dotnet fsi build.fsx -- analyze`
Expected: `Passed!` (RAN023: 8 cases), `analyze` exits 0.

```bash
dotnet fantomas tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/Site.fs
rtk git add tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/content docs/Site.fs
rtk git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-16-commit.txt
```

Commit subject: `feat(analyzers): RAN023 reports invalid content parts`.

### Task 17: Extract ContentModel into its own file

**Files:**
- Create: `experiments/Ranvier.View.Portable/ContentModel.fs`
- Modify: `experiments/Ranvier.View.Portable/Template.fs` (remove `isRcdata`, `RootSummary` and the `ContentModel`
  module; keep `summarise` as a private function), `experiments/Ranvier.View.Portable/Ranvier.View.Portable.fsproj`,
  `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj` (both compile `ContentModel.fs` before `Template.fs`)

**Interfaces:**
- Consumes: nothing new.
- Produces: `module internal Ranvier.View.Foundation.ContentModel` with `isRcdata`, `RootSummary`, `path`, `check`,
  `checkText`, `checkRegion`, `isWhitespace`, `contextKey`, `checkSummary`, `scopes` (the existing members, unchanged);
  Template.fs's private `summariseRoots: TemplateNode -> ContentModel.RootSummary`.

- [ ] **Step 1: Merge master immediately before editing**

```bash
rtk git fetch origin
git merge origin/master -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-17-merge.txt
```

The merge message file holds `Merge origin/master into feat/analyzers` and the trailers. Then re-locate the module:
`rtk grep -n "module internal ContentModel\|type internal RootSummary\|let private isRcdata\|let summarise" experiments/Ranvier.View.Portable/Template.fs`.
Every line number below is from `5afc604`; use the located ones.

- [ ] **Step 2: Record the baseline**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release`, then
`dotnet run --project docs/samples/view/Ranvier.Docs.ViewSamples.fsproj -c Release`
Expected: both pass; record their counts in the ledger. The extraction is a move: every later run must match them.

- [ ] **Step 3: Move the module**

Create `experiments/Ranvier.View.Portable/ContentModel.fs` from, in order: a header, `isRcdata` (`Template.fs:311-313`,
made public within the module), `RootSummary` (`Template.fs:603-617`, without `internal`), and the body of `module
internal ContentModel` (`Template.fs:666-1040`, through `checkSummary`), outdented by four spaces:

```fsharp
/// <summary>
/// Nesting rules from the HTML tree-construction algorithm. Markup matched by a rule parses to a different tree than
/// the client builds.
/// </summary>
/// <remarks>
/// <para>
/// <c>ancestors</c> lists the tags of the enclosing elements, nearest first. Where two rules match, the first rule in
/// <c>check</c>'s order is reported.
/// </para>
/// <para>
/// Conservative rejections, parsed as written: <c>input type="hidden"</c> and an empty <c>form</c> in a table part,
/// <c>table</c> in <c>p</c> in quirks mode, and HTML elements inside <c>svg</c> or <c>math</c> (foreign content is
/// checked as HTML).
/// </para>
/// <para>The Ranvier analyzers compile this file into their assembly (RAN024), so it references System only.</para>
/// </remarks>
module internal Ranvier.View.Foundation.ContentModel

open System
open System.Text

/// <summary>Elements whose content is parsed as a single text node.</summary>
let isRcdata (tagName: string) =
    let tagName = tagName.ToLowerInvariant ()
    tagName = "textarea" || tagName = "title"

// ... RootSummary, then path ... checkSummary, moved verbatim ...
```

In `Template.fs`: delete the moved lines; replace `isRcdata` uses with `ContentModel.isRcdata`; change the
`ServerPlan.Summary` field type to `ContentModel.RootSummary`; turn `ContentModel.summarise` into a top-level
`let private summariseRoots (root: TemplateNode) : ContentModel.RootSummary` placed where the module stood, with its
body unchanged except `scopes` and `isWhitespace` qualified as `ContentModel.scopes` and `ContentModel.isWhitespace`;
replace its call `ContentModel.summarise root` with `summariseRoots root`. Add `<Compile Include="ContentModel.fs" />`
before `Template.fs` in `Ranvier.View.Portable.fsproj`, and the matching linked item before `Template.fs` in
`DomTests.fsproj` (it compiles the Portable sources by path; copy the form of its `Template.fs` item).

- [ ] **Step 4: Run every view gate**

Run, in order:

```bash
dotnet build experiments/Ranvier.View.Foundation -c Release
dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release
dotnet run --project docs/samples/view/Ranvier.Docs.ViewSamples.fsproj -c Release
dotnet fsi tools/Ranvier.Dom.Generate/CheckTypes.fsx
```

and the playground's jsdom suite:
`cd examples/Fable.Ranvier.Playground && npm ci && npm run test:dom` (run `npm ci` only when `node_modules` is absent;
never link it from another worktree).
Expected: each passes with the Step 2 counts.

- [ ] **Step 5: Commit**

```bash
dotnet fantomas experiments/Ranvier.View.Portable/ContentModel.fs experiments/Ranvier.View.Portable/Template.fs
rtk git add experiments/Ranvier.View.Portable examples/Fable.Ranvier.Playground/tests/DomTests.fsproj
rtk git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-17-commit.txt
```

Commit subject: `refactor(view): move the HTML content model into its own file`.

### Task 18: RAN024, markup the server render refuses

**Files:**
- Modify: `tools/Ranvier.Analyzers/Ranvier.Analyzers.fsproj` (link `ContentModel.fs` before `Rules/View.fs`),
  `tools/Ranvier.Analyzers/Rules/View.fs`, `tools/Ranvier.Analyzers/Analyzers.fs`
- Create: `tools/Ranvier.Analyzers.Tests/Ran024.fs`, `docs/content/analyzers/ran024.md`
- Modify: `docs/content/analyzers/index.md`, `docs/Site.fs`, `docs/content/view/troubleshooting.md`

**Interfaces:**
- Consumes: `Rules.View.elements` (Task 16), `ContentModel.check` (Task 17).
- Produces: `Rules.View.ran024`; `Analyzers.ran024Cli`, `Analyzers.ran024Editor`.

- [ ] **Step 1: Write the failing tests**

`tools/Ranvier.Analyzers.Tests/Ran024.fs`:

```fsharp
module Ranvier.Analyzers.Tests.Ran024

open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private header =
    "module Fixture\nopen Ranvier\nopen Ranvier.View.Foundation\nopen Ranvier.View.Foundation.Representation\nopen Ranvier.View.Foundation.Template\nopen Ranvier.View.Foundation.Sugar\n"

let private findings (body: string) =
    run Analyzers.ran024Cli (context (ranvier.Force ()) (header + body))

let private codes body = findings body |> List.map _.Code

[<Tests>]
let tests =
    testList
        "RAN024"
        [
            test "a script element, with its exact message" {
                Expect.equal
                    (findings "let c = Component.define (fun _ () -> ()) (fun t -> t.script [||] [||])\n" |> List.map _.Message)
                    [
                        "`script` is refused by every render (\"Script elements are not created by templates or typed views\"). Load scripts through the server document shell, for example `Server.Page` scripts."
                    ]
                    "one message"
            }

            test "a style element" {
                Expect.equal (codes "let c = Component.define (fun _ () -> ()) (fun t -> t.style [||] [||])\n") [ "RAN024" ] "style"
            }

            test "a template element" {
                Expect.equal
                    (codes "let c = Component.define (fun _ () -> ()) (fun t -> t.template [||] [||])\n")
                    [ "RAN024" ]
                    "template"
            }

            test "tr directly in table" {
                Expect.equal
                    (codes "let c = Component.template (fun _ () -> ()) (table () { tr () { td () { \"x\" } } })\n")
                    [ "RAN024" ]
                    "table-row"
            }

            test "tr in tbody in table" {
                Expect.isEmpty
                    (codes "let c = Component.template (fun _ () -> ()) (table () { tbody () { tr () { td () { \"x\" } } } })\n")
                    "parsed as written"
            }

            test "a div in a div" {
                Expect.isEmpty
                    (codes "let c = Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.div [||] [||] |])\n")
                    "valid nesting"
            }

            test "nesting through a child component is not inspected" {
                let body =
                    "let row = Component.template (fun _ () -> ()) (tr () { td () { \"x\" } })\nlet c = Component.template (fun _ () -> ()) (table () { renderOf row () })\n"

                Expect.isEmpty (codes body) "false negative by design"
            }
        ]
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`
Expected: FAIL to compile: `ran024Cli` is not defined.

- [ ] **Step 3: Link the content model and implement RAN024**

In `Ranvier.Analyzers.fsproj`, before `Rules/View.fs`:

```xml
        <!-- The view's HTML nesting rules, shared so the diagnostic and the server refusal agree (spec Decision 14). -->
        <Compile Include="../../experiments/Ranvier.View.Portable/ContentModel.fs" Link="Rules/ContentModel.fs" />
```

Append to `Rules/View.fs`:

```fsharp
let private refused (element: ElementSite) : string option =
    match element.Tag with
    | "script" ->
        Some
            "`script` is refused by every render (\"Script elements are not created by templates or typed views\"). Load scripts through the server document shell, for example `Server.Page` scripts."
    | "template" ->
        Some
            "`template` is refused by every render (\"Template elements are not supported in templates\"). Build the content with a component instead."
    | tag when rawTextTags.Contains tag ->
        Some
            $"`{tag}` is a raw-text element, which the server render refuses (\"Raw-text elements require an explicit HTML serialization policy\"). Write it outside the template."
    | _ -> None

let private restructured (element: ElementSite) : string option =
    try
        Ranvier.View.Foundation.ContentModel.check element.Ancestors element.Tag
        None
    with :? System.InvalidOperationException as failure ->
        Some
            $"{failure.Message}, which the server render refuses at its first render. Nest the elements as the HTML parser builds them."

let private serverRefusals (input: Input) : Message list =
    [
        for element in elements input do
            match refused element, restructured element with
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
```

`ContentModel.check` expects ancestors nearest first and lower-case tags, which `elements` provides; read its
signature in the extracted file and adapt the call if Task 17's merge changed it. If the generated builders omit a
refused tag (`t.script`, `t.template`), the fixture uses the sugar tag type of the same name, and when neither exists
the tag cannot be written literally, so its case leaves the test list with a ledger entry. Register `Rules.View.ran024` and the
pair (title "Markup the server render refuses", HelpUri `https://ranvier-org.github.io/Ranvier/analyzers/ran024/`).

- [ ] **Step 4: Write the docs page and the troubleshooting link**

`docs/content/analyzers/ran024.md`, in the RAN010 page's layout: **Cause.** the spec's RAN024 trap text (raw-text and
`template` refusals, `ContentModel` restructuring, the client refusals since #140); **Fix.** "Move scripts to the
server document shell, write raw-text elements outside templates, and nest table parts as the parser builds them
(`tbody` between `table` and `tr`)."; `### Triggers` (the `table`/`tr` fixture as a module) and `### Fixed` (with
`tbody`). Link it from the entry at `docs/content/view/troubleshooting.md:43-45`; add the index row and the menu page.

- [ ] **Step 5: Run the tests, the gate, and commit**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`, then `dotnet fsi build.fsx -- analyze`
Expected: `Passed!` (RAN024: 7 cases), `analyze` exits 0.

```bash
dotnet fantomas tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/Site.fs
rtk git add tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/content docs/Site.fs
rtk git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-18-commit.txt
```

Commit subject: `feat(analyzers): RAN024 reports markup the server render refuses`.

### Task 19: RAN025, `hydrateWith` with queued commits

**Files:**
- Modify: `tools/Ranvier.Analyzers/Rules/View.fs`, `tools/Ranvier.Analyzers/Analyzers.fs`
- Create: `tools/Ranvier.Analyzers.Tests/Ran025.fs`, `docs/content/analyzers/ran025.md`
- Modify: `docs/content/analyzers/index.md`, `docs/Site.fs`, `docs/content/view/troubleshooting.md`

**Interfaces:**
- Consumes: `Symbols.View.{Hydration, Microtask}`, `Index.FileIndex.Lets`.
- Produces: `Rules.View.ran025`; `Analyzers.ran025Cli`, `Analyzers.ran025Editor`.

- [ ] **Step 1: Write the failing tests**

`tools/Ranvier.Analyzers.Tests/Ran025.fs`:

```fsharp
module Ranvier.Analyzers.Tests.Ran025

open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private header =
    "module Fixture\nopen Ranvier\nopen Ranvier.View.Foundation\nopen Ranvier.View.Foundation.Scheduling\nopen Ranvier.View.Foundation.Hydration\nlet hydrated graph host definition (failure: CommitFailure) (post: (unit -> unit) -> unit) =\n"

let private findings (body: string) =
    run Analyzers.ran025Cli (context (ranvier.Force ()) (header + body))

let private codes body = findings body |> List.map _.Code

let private call (mode: string) =
    $"    hydrateWith ({mode}) graph None ignore HydrationOptions.defaults host definition ()\n"

[<Tests>]
let tests =
    testList
        "RAN025"
        [
            test "a QueuedCommits construction, with its exact message" {
                Expect.equal
                    (findings (call "QueuedCommits (failure, post)") |> List.map _.Message)
                    [ "Hydration supports synchronous commits only, so this call raises. Pass `SynchronousCommits`, or call `hydrate`." ]
                    "one message"
            }

            test "Microtask.mode" {
                Expect.equal (codes (call "Microtask.mode failure")) [ "RAN025" ] "Microtask.fs:16"
            }

            test "a let-bound queued mode" {
                Expect.equal
                    (codes ("    let mode = QueuedCommits (failure, post)\n" + call "mode"))
                    [ "RAN025" ]
                    "let-bound"
            }

            test "SynchronousCommits" { Expect.isEmpty (codes (call "SynchronousCommits")) "supported" }

            test "a mode parameter" {
                let body =
                    "    ()\nlet other (mode: CommitMode) graph host definition =\n    hydrateWith mode graph None ignore HydrationOptions.defaults host definition ()\n"

                Expect.isEmpty (codes body) "unknown mode"
            }

            test "hydrate" {
                Expect.isEmpty
                    (codes "    hydrate graph None ignore HydrationOptions.defaults host definition ()\n")
                    "synchronous by definition"
            }
        ]
```

Read `Hydration.fs:22` for `HydrationOptions`'s default value name and `hydrateWith`'s parameter list
(`Hydration.fs:265-273`) before running; correct the fixture's call to the compiled signature and ledger it.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`
Expected: FAIL to compile: `ran025Cli` is not defined.

- [ ] **Step 3: Implement RAN025**

Append to `Rules/View.fs`:

```fsharp
let rec private queuedMode (lets: LetSite array) (expr: FSharpExpr) : bool =
    match expr with
    | NewUnionCase (_, case, _) -> case.Name = "QueuedCommits"
    | Call (None, m, _, _, _) when Symbols.isMember Microtask "mode" m -> true
    | Value value ->
        lets |> Array.exists (fun site -> site.Value = value && queuedMode lets site.Bound)
    | Coerce (_, inner) -> queuedMode lets inner
    | _ -> false

let private queuedHydration (input: Input) : Message list =
    [
        for site in input.Index.Calls do
            match site.Arguments with
            | mode :: _ when Symbols.isMember Hydration "hydrateWith" site.Callee && queuedMode input.Index.Lets mode ->
                message
                    "RAN025"
                    Severity.Error
                    mode.Range
                    "Hydration supports synchronous commits only, so this call raises. Pass `SynchronousCommits`, or call `hydrate`."
                    []
            | _ -> ()
    ]

let ran025: Rule =
    {
        Code = "RAN025"
        Title = "hydrateWith with queued commits"
        Gate = Symbols.Assemblies.Foundation
        Run = queuedHydration
    }
```

Register `Rules.View.ran025` and the pair (title "hydrateWith with queued commits", HelpUri
`https://ranvier-org.github.io/Ranvier/analyzers/ran025/`).

- [ ] **Step 4: Write the docs page and the troubleshooting link**

`docs/content/analyzers/ran025.md`, in the RAN010 page's layout: **Cause.** "`hydrateWith` raises
`NotSupportedException \"Hydration supports synchronous commits only\"` for `QueuedCommits`."; **Fix.** "Pass
`SynchronousCommits`, or call `hydrate`."; `### Triggers` (the `QueuedCommits` fixture as a module) and `### Fixed`
(with `SynchronousCommits`). Link it from the hydration entry of `docs/content/view/troubleshooting.md`; add the index
row and the menu page.

- [ ] **Step 5: Run the tests, the gate, the docs build, and commit**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`, then `dotnet fsi build.fsx -- analyze`, then
`dotnet fsi build.fsx -- docs`
Expected: `Passed!` (RAN025: 6 cases), `analyze` exits 0, the docs build has no broken link under `analyzers/`.

```bash
dotnet fantomas tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/Site.fs
rtk git add tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/content docs/Site.fs
rtk git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-19-commit.txt
```

Commit subject: `feat(analyzers): RAN025 reports hydrateWith with queued commits`.

---

## Phase 3: the remaining core rule

The phase starts with `git fetch origin && git merge origin/master` and clean runs of the suite and `analyze`.
RAN030-RAN032 are not part of this plan (spec Decision 17). The Ionide-loadable build waits for FsAutoComplete to move
to SDK 0.41 (spec Decision 8).

### Task 20: RAN015, `CancelPrevious` with a body that ignores its token

**Files:**
- Modify: `tools/Ranvier.Analyzers/Rules/Core.fs`, `tools/Ranvier.Analyzers/Analyzers.fs`
- Create: `tools/Ranvier.Analyzers.Tests/Ran015.fs`, `docs/content/analyzers/ran015.md`
- Modify: `docs/content/analyzers/index.md`, `docs/Site.fs`

**Interfaces:**
- Consumes: `Index.uses`, `Symbols.Core.Api`.
- Produces: `Rules.Core.ran015`; `Analyzers.ran015Cli`, `Analyzers.ran015Editor`.

- [ ] **Step 1: Write the failing tests**

`tools/Ranvier.Analyzers.Tests/Ran015.fs`:

```fsharp
module Ranvier.Analyzers.Tests.Ran015

open Expecto
open FSharp.Analyzers.SDK
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private findings (body: string) =
    let source = "module Fixture\nopen System.Threading.Tasks\nopen Ranvier\n" + body
    run Analyzers.ran015Cli (context (ranvier.Force ()) source)

[<Tests>]
let tests =
    testList
        "RAN015"
        [
            test "a wildcard token, with its exact message and Hint severity" {
                let messages =
                    findings "let a () = createAsyncWithPolicy FlightPolicy.CancelPrevious (fun _ _ -> Task.FromResult 1)\n"

                Expect.equal
                    (messages |> List.map (fun m -> m.Severity, m.Message))
                    [
                        Severity.Hint,
                        "This async memo's body ignores its CancellationToken, so `CancelPrevious` cancels nothing and acts as `KeepLatest`. Pass the token to the awaited work, or use `KeepLatest`."
                    ]
                    "one hint"
            }

            test "a named token that is never used" {
                Expect.equal
                    (findings "let a () = createAsyncWithPolicy FlightPolicy.CancelPrevious (fun _ token -> Task.FromResult 1)\n"
                     |> List.map _.Code)
                    [ "RAN015" ]
                    "unused"
            }

            test "the owning form" {
                Expect.equal
                    (findings "let a () = createOwningAsyncWithPolicy FlightPolicy.CancelPrevious (fun _ _ -> Task.FromResult 1)\n"
                     |> List.map _.Code)
                    [ "RAN015" ]
                    "owning"
            }

            test "a token passed to the work" {
                Expect.isEmpty
                    (findings "let a () = createAsyncWithPolicy FlightPolicy.CancelPrevious (fun _ token -> Task.Delay (10, token).ContinueWith (fun _ -> 1))\n")
                    "used"
            }

            test "KeepLatest" {
                Expect.isEmpty
                    (findings "let a () = createAsyncWithPolicy FlightPolicy.KeepLatest (fun _ _ -> Task.FromResult 1)\n")
                    "no cancellation promised"
            }

            test "a policy parameter" {
                Expect.isEmpty
                    (findings "let a (policy: FlightPolicy) = createAsyncWithPolicy policy (fun _ _ -> Task.FromResult 1)\n")
                    "unknown policy"
            }
        ]
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`
Expected: FAIL to compile: `ran015Cli` is not defined.

- [ ] **Step 3: Implement RAN015**

Append to `Rules/Core.fs`:

```fsharp
let private ignoredTokens (input: Input) : Message list =
    [
        for site in input.Index.Calls do
            let policyForm =
                Symbols.isMember Api "createAsyncWithPolicy" site.Callee
                || Symbols.isMember Api "createOwningAsyncWithPolicy" site.Callee

            match site.Arguments with
            | [ NewUnionCase (_, case, []); Lambda (_, Lambda (token, body)) ] when
                policyForm
                && case.Name = "CancelPrevious"
                && (token.DisplayName.StartsWith "_" || (Index.uses token body).IsEmpty)
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
```

Register `Rules.Core.ran015` after `ran013` and the pair (title "CancelPrevious with a body that ignores its token",
HelpUri `https://ranvier-org.github.io/Ranvier/analyzers/ran015/`).

- [ ] **Step 4: Write the docs page**

`docs/content/analyzers/ran015.md`, in the RAN010 page's layout: **Cause.** "`createAsyncWithPolicy CancelPrevious`
cancels the token of a superseded flight. A body that never reads the token runs every flight to the end, so the
policy acts as `KeepLatest`."; **Fix.** "Pass the token to the awaited work, or use `KeepLatest`."; `### Triggers`:

```fsharp
module Example

open System.Threading.Tasks
open Ranvier

let search () =
    createAsyncWithPolicy FlightPolicy.CancelPrevious (fun _ _ -> Task.Delay(100).ContinueWith (fun _ -> 1))
```

`### Fixed`:

```fsharp
module Example

open System.Threading.Tasks
open Ranvier

let search () =
    createAsyncWithPolicy FlightPolicy.CancelPrevious (fun _ token -> Task.Delay(100, token).ContinueWith (fun _ -> 1))
```

Add the index row and the menu page.

- [ ] **Step 5: Run the tests, the gate, and commit**

Run: `dotnet test tools/Ranvier.Analyzers.Tests -c Release`, then `dotnet fsi build.fsx -- analyze`
Expected: `Passed!` (RAN015: 6 cases), `analyze` exits 0 (a Hint promoted by `RAN*` fails the gate, so a repository
finding is fixed or suppressed with its reason).

```bash
dotnet fantomas tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/Site.fs
rtk git add tools/Ranvier.Analyzers tools/Ranvier.Analyzers.Tests docs/content docs/Site.fs
rtk git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/analyzers-task-20-commit.txt
```

Commit subject: `feat(analyzers): RAN015 hints at CancelPrevious bodies that ignore their token`.
