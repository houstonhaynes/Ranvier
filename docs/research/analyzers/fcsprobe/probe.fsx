#r "nuget: FSharp.Analyzers.SDK.Testing, 0.41.0"

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open FSharp.Compiler.Symbols.FSharpExprPatterns

let opts0 =
    FSharp.Analyzers.SDK.Testing.mkOptionsFromProject "net10.0" []
    |> Async.AwaitTask
    |> Async.RunSynchronously

let dir = __SOURCE_DIRECTORY__

let files =
    [| System.IO.Path.Combine (dir, "A.fs"); System.IO.Path.Combine (dir, "B.fs") |]

let opts =
    { opts0 with
        SourceFiles = files
        OtherOptions =
            Array.append
                (opts0.OtherOptions
                 |> Array.filter (fun o -> not (o.EndsWith ".fs")))
                files
    }

let checker = FSharpChecker.Create (keepAssemblyContents = true)

let res =
    checker.ParseAndCheckProject opts
    |> Async.RunSynchronously

for d in res.Diagnostics do
    printfn "DIAG %s" d.Message

let rec walk depth (e: FSharpExpr) =
    let pad = String.replicate depth " "

    match e with
    | Call (obj, m, _, _, args) ->
        printfn
            "%sCall %s decl=%s cg=%b prop=%b mutable=%b file=%s range=%s attrs=%s"
            pad
            m.CompiledName
            (m.DeclaringEntity
             |> Option.map (fun x -> x.FullName)
             |> Option.defaultValue "-")
            m.IsCompilerGenerated
            m.IsPropertyGetterMethod
            m.IsMutable
            (try
                System.IO.Path.GetFileName m.DeclarationLocation.FileName
             with _ ->
                 "?")
            (try
                string m.DeclarationLocation
             with _ ->
                 "?")
            (m.Attributes
             |> Seq.map (fun a -> a.AttributeType.DisplayName)
             |> String.concat ",")

        obj |> Option.iter (walk (depth + 1))
        args |> List.iter (walk (depth + 1))
    | Value v ->
        printfn
            "%sValue %s mutable=%b moduleVal=%b file=%s"
            pad
            v.DisplayName
            v.IsMutable
            v.IsModuleValueOrMember
            (System.IO.Path.GetFileName v.DeclarationLocation.FileName)
    | UnionCaseTest (x, t, c) ->
        printfn "%sUnionCaseTest %s" pad c.Name
        walk (depth + 1) x
    | Let ((v, b, _), body) ->
        printfn "%sLet %s" pad v.DisplayName
        walk (depth + 1) b
        walk (depth + 1) body
    | Lambda (v, b) ->
        printfn "%sLambda %s" pad v.DisplayName
        walk (depth + 1) b
    | _ ->
        printfn "%s%A" pad (e.Type.Format (FSharpDisplayContext.Empty))

        e.ImmediateSubExpressions
        |> List.iter (walk (depth + 1))

let rec decls (d: FSharpImplementationFileDeclaration) =
    match d with
    | FSharpImplementationFileDeclaration.Entity (_, ds) -> ds |> List.iter decls
    | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue (v, _, body) ->
        printfn "== %s" v.DisplayName
        walk 1 body
    | _ -> ()

for f in res.AssemblyContents.ImplementationFiles do
    if f.FileName.EndsWith "B.fs" then
        f.Declarations |> List.iter decls

for e in res.AssemblySignature.Entities do
    for n in e.NestedEntities do
        if n.IsFSharpUnion then
            for c in n.UnionCases do
                printfn "case %s range=%A" c.Name c.DeclarationLocation

            for m in n.MembersFunctionsAndValues do
                printfn
                    "member %s range=%A cg=%b attrs=%s"
                    m.CompiledName
                    m.DeclarationLocation
                    m.IsCompilerGenerated
                    (m.Attributes
                     |> Seq.map (fun a -> a.AttributeType.DisplayName)
                     |> String.concat ",")
