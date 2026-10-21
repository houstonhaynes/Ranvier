/// RAN001 and RAN002: code that a Fable build compiles to different behaviour, or fails to precompile.
module Ranvier.Analyzers.Rules.Fable

open System
open System.IO
open System.Text.RegularExpressions
open FSharp.Analyzers.SDK
open FSharp.Compiler.Symbols
open FSharp.Compiler.Symbols.FSharpExprPatterns
open FSharp.Compiler.Text
open Ranvier.Analyzers
open Ranvier.Analyzers.Index
open Ranvier.Analyzers.Rule

/// The union and case of a compiler-generated `IsCase` getter of a union declared in the analyzed project.
let private generatedTester (input: Input) (m: FSharpMemberOrFunctionOrValue) : (FSharpEntity * FSharpUnionCase) option =
    match m.DeclaringEntity with
    | Some union when
        m.IsPropertyGetterMethod
        && union.IsFSharpUnion
        && inProject input union.DeclarationLocation.FileName
        ->
        let generated =
            m.Attributes
            |> Seq.exists (fun attribute -> attribute.AttributeType.TryFullName = Some Symbols.CompilerGenerated)

        if generated then
            union.UnionCases
            |> Seq.tryFind (fun case -> m.LogicalName = Symbols.TesterPrefix + case.Name)
            |> Option.map (fun case -> union, case)
        else
            None
    | _ -> None

/// The pattern of `case` written where `opened` names paths unqualified: the union's source path relative to the
/// innermost of `opened` that contains it, or the whole source path, then the union and the case. A case with fields
/// takes one wildcard.
let private casePattern (opened: string list) (union: FSharpEntity) (case: FSharpUnionCase) : string =
    let container = Symbols.containerOf union

    let qualifier =
        opened
        |> List.tryPick (fun path ->
            if container = path then
                Some ""
            elif container.StartsWith (path + ".", System.StringComparison.Ordinal) then
                Some (container.Substring (path.Length + 1))
            else
                None)
        |> Option.defaultValue container

    let name =
        String.Join (
            ".",
            [
                if qualifier <> "" then
                    qualifier
                union.DisplayName
                case.Name
            ]
        )

    if case.Fields.Count = 0 then name else name + " _"

/// Whether Fable compiles the project.
let private compilesWithFable (input: Input) =
    (Symbols.Compilers.signal input.Options input.FileName).CompilesWithFable

/// Whether the project's Fable precompile fails on generated union testers: Fable compiles the project, and the
/// declared compiler is a Fable before 5.22 or undeclared.
let private precompileDropsTesters (input: Input) =
    let signal = Symbols.Compilers.signal input.Options input.FileName

    signal.CompilesWithFable
    && (match signal.Declared with
        | Some declared -> Symbols.Compilers.precedesTesterFix declared
        | None -> true)

let private testers (input: Input) : Message list =
    [
        for site in input.Index.Calls do
            match generatedTester input site.Callee, site.Receiver with
            | Some (union, case), Some receiver ->
                let receiverText = text input receiver.Range
                let callText = text input site.Range
                let pattern = casePattern site.Opened union case

                let named =
                    union.DisplayName
                    + "."
                    + Symbols.TesterProperty
                    + case.Name

                // The shorthand `_.IsQueue` or `_.Inner.IsQueue` is replaced with a function.
                let tester, replacement =
                    if receiverText = "_" then
                        named, $"(function {pattern} -> true | _ -> false)"
                    elif receiverText.StartsWith ("_.", System.StringComparison.Ordinal) then
                        named, $"(fun value -> match value{receiverText.Substring 1} with {pattern} -> true | _ -> false)"
                    else
                        callText, $"(match {receiverText} with {pattern} -> true | _ -> false)"

                message
                    "RAN002"
                    Severity.Warning
                    site.Range
                    $"`{tester}` calls a compiler-generated union tester, which a Fable precompiled assembly does not declare. Write `{replacement}`."
                    [ replace site.Range callText replacement ]
            | _ -> ()
    ]

let ran002: Rule =
    {
        Code = "RAN002"
        Title = "Generated union case tester in a Fable library"
        Gate = Symbols.Assemblies.Fable
        Run = fun input -> if precompileDropsTesters input then testers input else []
    }

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
                moduleMutable elements[item]
                |> Option.map (fun source -> source, false)
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
            if
                not site.Value.IsMutable
                && not site.Value.IsCompilerGenerated
            then
                match snapshotOf lets site with
                | Some (source, fixable) when not (sameFile source.DeclarationLocation.FileName site.Value.DeclarationLocation.FileName) ->
                    let at = site.Value.DeclarationLocation

                    let before =
                        (input.Source.GetLineString (at.StartLine - 1)).Substring(0, at.StartColumn)

                    match Index.uses site.Value site.Body with
                    // A `let` on the line, not yet followed by `=`, binds the value; a match case binds it otherwise.
                    | [ single ] when Regex.IsMatch (before, @"\blet\b[^=]*$") ->
                        let name = site.Value.DisplayName

                        let fixes =
                            if fixable && Regex.IsMatch (before, @"\blet\s+$") then
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
        Run = fun input -> if compilesWithFable input then snapshots input else []
    }
