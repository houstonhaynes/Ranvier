/// Fable capability probe for arm construction: caller info, union tags, reflection and quotations.
module Caps

open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Reflection

type Load =
    | Idle
    | Loading of percent: int
    | Loaded of text: string * count: int

type Site =
    static member At
        ([<CallerFilePath; Optional; DefaultParameterValue("")>] file: string, [<CallerLineNumber; Optional; DefaultParameterValue(0)>] line: int)
        =
        System.IO.Path.GetFileName file
        + ":"
        + string line

    static member Quoted([<ReflectedDefinition>] f: Expr<Load -> string>) : string =
#if FABLE_COMPILER
        "fable:" + string (box f)
#else
        sprintf "%A" f
        |> fun s -> s.Substring (0, min 60 s.Length)
#endif

#if FABLE_COMPILER
[<Fable.Core.Emit("$0.tag")>]
let fableTag (_: obj) : int =
    Fable.Core.Util.jsNative
#endif

let tagOf (value: Load) =
#if FABLE_COMPILER
    fableTag value
#else
    (FSharpValue.PreComputeUnionTagReader typeof<Load>) (box value)
#endif

#if CAPS_REFLECTION
let reflectedTag = FSharpValue.PreComputeUnionTagReader typeof<Load>
#endif

#if CAPS_QUOTE
let quoted =
    <@
        fun (v: Load) ->
            match v with
            | Loaded (t, _) -> t
            | _ -> ""
    @>
#endif

[<EntryPoint>]
let main _ =
    printfn "site %s %s" (Site.At ()) (Site.At ())
    printfn "tags %d %d %d" (tagOf Idle) (tagOf (Loading 1)) (tagOf (Loaded ("a", 1)))
#if CAPS_REFLECTION
    printfn "reflected %d" (reflectedTag (box (Loaded ("a", 2))))
#endif
#if CAPS_QUOTE
    printfn "quoted %A" quoted

    printfn
        "param %s"
        (Site.Quoted (function
            | Loaded (t, _) -> t
            | _ -> ""))
#endif
    0
