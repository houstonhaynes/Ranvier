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

/// A rule and the assembly whose reference enables it.
[<NoEquality; NoComparison>]
type Rule =
    {
        Code: string
        Title: string
        Gate: string
        Run: Input -> Message list
    }

/// Whether `file` is one of the analyzed project's source files.
let inProject (input: Input) (file: string) : bool =
    let full = System.IO.Path.GetFullPath file

    input.Options.SourceFiles
    |> List.exists (fun source -> System.String.Equals (System.IO.Path.GetFullPath source, full, System.StringComparison.OrdinalIgnoreCase))

/// The source text of `range`.
let text (input: Input) (range: range) : string =
    input.Source.GetSubTextFromRange range

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
