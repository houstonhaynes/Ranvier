/// The rule registry, and one CLI and one editor entry per rule.
module Ranvier.Analyzers.Analyzers

open FSharp.Analyzers.SDK
open Ranvier.Analyzers.Rule

/// Every rule, in code order.
let rules: Rule list =
    [
        Rules.Fable.ran001
        Rules.Fable.ran002
        Rules.Core.ran010
        Rules.Core.ran011
        Rules.Core.ran012
        Rules.Core.ran013
        Rules.Core.ran015
        Rules.View.ran020
        Rules.View.ran021
        Rules.View.ran022
        Rules.View.ran023
        Rules.View.ran024
        Rules.View.ran025
    ]

/// Whether `rule` runs on a file of the project: the project references the rule's gate assembly, or, for a view rule,
/// the file calls a member of the view namespace.
let enabled (rule: Rule) (options: AnalyzerProjectOptions) (index: Index.FileIndex) : bool =
    Symbols.references rule.Gate options
    || ((rule.Gate = Symbols.Assemblies.View
         || rule.Gate = Symbols.Assemblies.Dom)
        && index.Calls
           |> Array.exists (fun call -> Symbols.View.isViewMember call.Callee))

/// The findings of `rule` in a CLI context; empty when `enabled` refuses the file.
let runCli (rule: Rule) (context: CliContext) : Message list =
    match context.TypedTree with
    | Some tree when enabled rule context.ProjectOptions (Index.ofFile tree) ->
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
        enabled rule context.ProjectOptions (Index.ofFile tree)
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

[<CliAnalyzer("RAN001", "Fable inlines a snapshot of a module mutable", "https://shayanhabibi.github.io/Ranvier/analyzers/ran001/")>]
let ran001Cli: Analyzer<CliContext> =
    fun context -> async { return runCli Rules.Fable.ran001 context }

[<EditorAnalyzer("RAN001", "Fable inlines a snapshot of a module mutable", "https://shayanhabibi.github.io/Ranvier/analyzers/ran001/")>]
let ran001Editor: Analyzer<EditorContext> =
    fun context -> async { return runEditor Rules.Fable.ran001 context }

[<CliAnalyzer("RAN002", "Generated union case tester in a Fable library", "https://shayanhabibi.github.io/Ranvier/analyzers/ran002/")>]
let ran002Cli: Analyzer<CliContext> =
    fun context -> async { return runCli Rules.Fable.ran002 context }

[<EditorAnalyzer("RAN002", "Generated union case tester in a Fable library", "https://shayanhabibi.github.io/Ranvier/analyzers/ran002/")>]
let ran002Editor: Analyzer<EditorContext> =
    fun context -> async { return runEditor Rules.Fable.ran002 context }

[<CliAnalyzer("RAN010", "Owned node created in a pure derivation", "https://shayanhabibi.github.io/Ranvier/analyzers/ran010/")>]
let ran010Cli: Analyzer<CliContext> =
    fun context -> async { return runCli Rules.Core.ran010 context }

[<EditorAnalyzer("RAN010", "Owned node created in a pure derivation", "https://shayanhabibi.github.io/Ranvier/analyzers/ran010/")>]
let ran010Editor: Analyzer<EditorContext> =
    fun context -> async { return runEditor Rules.Core.ran010 context }

[<CliAnalyzer("RAN011", "Async value created and read in the owner that replaces it", "https://shayanhabibi.github.io/Ranvier/analyzers/ran011/")>]
let ran011Cli: Analyzer<CliContext> =
    fun context -> async { return runCli Rules.Core.ran011 context }

[<EditorAnalyzer("RAN011", "Async value created and read in the owner that replaces it", "https://shayanhabibi.github.io/Ranvier/analyzers/ran011/")>]
let ran011Editor: Analyzer<EditorContext> =
    fun context -> async { return runEditor Rules.Core.ran011 context }

[<CliAnalyzer("RAN012", "Reactive read after the first await of an async memo", "https://shayanhabibi.github.io/Ranvier/analyzers/ran012/")>]
let ran012Cli: Analyzer<CliContext> =
    fun context -> async { return runCli Rules.Core.ran012 context }

[<EditorAnalyzer("RAN012", "Reactive read after the first await of an async memo", "https://shayanhabibi.github.io/Ranvier/analyzers/ran012/")>]
let ran012Editor: Analyzer<EditorContext> =
    fun context -> async { return runEditor Rules.Core.ran012 context }

[<CliAnalyzer("RAN013", "Signal write or Pump on a thread-pool callback", "https://shayanhabibi.github.io/Ranvier/analyzers/ran013/")>]
let ran013Cli: Analyzer<CliContext> =
    fun context -> async { return runCli Rules.Core.ran013 context }

[<EditorAnalyzer("RAN013", "Signal write or Pump on a thread-pool callback", "https://shayanhabibi.github.io/Ranvier/analyzers/ran013/")>]
let ran013Editor: Analyzer<EditorContext> =
    fun context -> async { return runEditor Rules.Core.ran013 context }

[<CliAnalyzer("RAN020", "Reactive read at template build time", "https://shayanhabibi.github.io/Ranvier/analyzers/ran020/")>]
let ran020Cli: Analyzer<CliContext> =
    fun context -> async { return runCli Rules.View.ran020 context }

[<EditorAnalyzer("RAN020", "Reactive read at template build time", "https://shayanhabibi.github.io/Ranvier/analyzers/ran020/")>]
let ran020Editor: Analyzer<EditorContext> =
    fun context -> async { return runEditor Rules.View.ran020 context }

[<CliAnalyzer("RAN021", "Unstable list key", "https://shayanhabibi.github.io/Ranvier/analyzers/ran021/")>]
let ran021Cli: Analyzer<CliContext> =
    fun context -> async { return runCli Rules.View.ran021 context }

[<EditorAnalyzer("RAN021", "Unstable list key", "https://shayanhabibi.github.io/Ranvier/analyzers/ran021/")>]
let ran021Editor: Analyzer<EditorContext> =
    fun context -> async { return runEditor Rules.View.ran021 context }

[<CliAnalyzer("RAN022", "Definition created on every region read", "https://shayanhabibi.github.io/Ranvier/analyzers/ran022/")>]
let ran022Cli: Analyzer<CliContext> =
    fun context -> async { return runCli Rules.View.ran022 context }

[<EditorAnalyzer("RAN022", "Definition created on every region read", "https://shayanhabibi.github.io/Ranvier/analyzers/ran022/")>]
let ran022Editor: Analyzer<EditorContext> =
    fun context -> async { return runEditor Rules.View.ran022 context }

[<CliAnalyzer("RAN023", "Invalid content parts on an element", "https://shayanhabibi.github.io/Ranvier/analyzers/ran023/")>]
let ran023Cli: Analyzer<CliContext> =
    fun context -> async { return runCli Rules.View.ran023 context }

[<EditorAnalyzer("RAN023", "Invalid content parts on an element", "https://shayanhabibi.github.io/Ranvier/analyzers/ran023/")>]
let ran023Editor: Analyzer<EditorContext> =
    fun context -> async { return runEditor Rules.View.ran023 context }

[<CliAnalyzer("RAN024", "Markup the server render refuses", "https://shayanhabibi.github.io/Ranvier/analyzers/ran024/")>]
let ran024Cli: Analyzer<CliContext> =
    fun context -> async { return runCli Rules.View.ran024 context }

[<EditorAnalyzer("RAN024", "Markup the server render refuses", "https://shayanhabibi.github.io/Ranvier/analyzers/ran024/")>]
let ran024Editor: Analyzer<EditorContext> =
    fun context -> async { return runEditor Rules.View.ran024 context }

[<CliAnalyzer("RAN025", "hydrateWith with queued commits", "https://shayanhabibi.github.io/Ranvier/analyzers/ran025/")>]
let ran025Cli: Analyzer<CliContext> =
    fun context -> async { return runCli Rules.View.ran025 context }

[<EditorAnalyzer("RAN025", "hydrateWith with queued commits", "https://shayanhabibi.github.io/Ranvier/analyzers/ran025/")>]
let ran025Editor: Analyzer<EditorContext> =
    fun context -> async { return runEditor Rules.View.ran025 context }

[<CliAnalyzer("RAN015", "CancelPrevious with a body that ignores its token", "https://shayanhabibi.github.io/Ranvier/analyzers/ran015/")>]
let ran015Cli: Analyzer<CliContext> =
    fun context -> async { return runCli Rules.Core.ran015 context }

[<EditorAnalyzer("RAN015", "CancelPrevious with a body that ignores its token", "https://shayanhabibi.github.io/Ranvier/analyzers/ran015/")>]
let ran015Editor: Analyzer<EditorContext> =
    fun context -> async { return runEditor Rules.Core.ran015 context }
