module Probe.Rules

open FSharp.Analyzers.SDK
open FSharp.Compiler.Symbols
open FSharp.Compiler.Symbols.FSharpExprPatterns

let private isGeneratedTester (m: FSharpMemberOrFunctionOrValue) =
    m.IsPropertyGetterMethod
    && m.CompiledName.StartsWith "get_Is"
    && (match m.DeclaringEntity with
        | Some e when e.IsFSharpUnion ->
            m.Attributes
            |> Seq.exists (fun a -> a.AttributeType.FullName = "System.Runtime.CompilerServices.CompilerGeneratedAttribute")
        | _ -> false)

let rec private walk (acc: ResizeArray<Message>) (e: FSharpExpr) =
    match e with
    | Call (_, m, _, _, _) when isGeneratedTester m ->
        acc.Add
            {
                Type = "Union case tester"
                Message = $"RAN002 probe: generated tester {m.DisplayName}"
                Code = "RAN002"
                Severity = Severity.Warning
                Range = e.Range
                Fixes = []
            }
    | _ -> ()

    for s in e.ImmediateSubExpressions do
        walk acc s

let rec private decl acc d =
    match d with
    | FSharpImplementationFileDeclaration.Entity (_, ds) -> ds |> List.iter (decl acc)
    | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue (_, _, body) -> walk acc body
    | FSharpImplementationFileDeclaration.InitAction e -> walk acc e

[<CliAnalyzer("UnionTesterProbe", "probe", "https://example.invalid/RAN002")>]
let analyzer: Analyzer<CliContext> =
    fun ctx ->
        async {
            let acc = ResizeArray ()

            ctx.TypedTree
            |> Option.iter (fun t -> t.Declarations |> List.iter (decl acc))

            return List.ofSeq acc
        }
