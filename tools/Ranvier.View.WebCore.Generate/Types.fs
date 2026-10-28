/// <summary>F# names of the <c>Fable.Core.TS</c> types, as source text.</summary>
module Ranvier.View.WebCore.Generate.Types

open System

let private keywords =
    set
        [
            "abstract"
            "and"
            "as"
            "assert"
            "base"
            "begin"
            "class"
            "default"
            "delegate"
            "do"
            "done"
            "downcast"
            "downto"
            "elif"
            "else"
            "end"
            "exception"
            "extern"
            "false"
            "finally"
            "fixed"
            "for"
            "fun"
            "function"
            "global"
            "if"
            "in"
            "inherit"
            "inline"
            "interface"
            "internal"
            "lazy"
            "let"
            "match"
            "member"
            "module"
            "mutable"
            "namespace"
            "new"
            "not"
            "null"
            "of"
            "open"
            "or"
            "override"
            "private"
            "public"
            "rec"
            "return"
            "select"
            "sig"
            "static"
            "struct"
            "then"
            "to"
            "true"
            "try"
            "type"
            "upcast"
            "use"
            "val"
            "void"
            "when"
            "while"
            "with"
            "yield"
            "const"
            "checked"
            "component"
            "constraint"
            "continue"
            "event"
            "external"
            "include"
            "mixin"
            "parallel"
            "process"
            "protected"
            "pure"
            "sealed"
            "tailcall"
            "trait"
            "virtual"
            "break"
            "params"
            "base"
        ]

/// <summary>The identifier as F# source, in double backticks when it is a keyword or not a plain identifier.</summary>
let ident (name: string) =
    let plain =
        name.Length > 0
        && (Char.IsLetter name[0] || name[0] = '_')
        && name
           |> Seq.forall (fun c -> Char.IsLetterOrDigit c || c = '_')

    if plain && not (keywords.Contains name) then
        name
    else
        "``" + name + "``"

let private strip (name: string) =
    match name.IndexOf '`' with
    | -1 -> name
    | at -> name.Substring (0, at)

/// <summary>The type's path without generic arguments, such as <c>Fable.Core.TS.Dom.NodeListOf</c>.</summary>
let rec path (t: Type) : string =
    // A module compiled with the Module suffix keeps its source name.
    let own =
        if
            Microsoft.FSharp.Reflection.FSharpType.IsModule t
            && t.Name.EndsWith "Module"
        then
            t.Name.Substring (0, t.Name.Length - "Module".Length)
        else
            strip t.Name

    if isNull t.DeclaringType then
        t.Namespace + "." + own
    else
        path t.DeclaringType + "." + own

/// <summary>The type as F# source.</summary>
let rec name (t: Type) : string =
    if t.IsGenericParameter then
        "'" + t.Name
    elif t.IsArray then
        sprintf "%s array" (atom (t.GetElementType ()))
    else
        match t.FullName with
        | "System.String" -> "string"
        | "System.Boolean" -> "bool"
        | "System.Double" -> "float"
        | "System.Single" -> "float32"
        | "System.Int32" -> "int"
        | "System.Int64" -> "int64"
        | "System.Void" -> "unit"
        | "System.Object" -> "obj"
        | "Microsoft.FSharp.Core.Unit" -> "unit"
        | _ when t.IsGenericType ->
            let definition = t.GetGenericTypeDefinition ()
            let args = t.GetGenericArguments ()

            if definition = typedefof<option<_>> then
                sprintf "%s option" (atom args[0])
            elif definition = typedefof<_ -> _> then
                sprintf "(%s -> %s)" (atom args[0]) (name args[1])
            elif definition.FullName.StartsWith "System.Tuple`" then
                args
                |> Array.map atom
                |> String.concat " * "
                |> sprintf "(%s)"
            else
                sprintf "%s<%s>" (path definition) (args |> Array.map name |> String.concat ", ")
        | _ -> path t

/// <summary>The type as F# source that can stand as a type argument or before a postfix type.</summary>
and atom (t: Type) : string =
    let text = name t

    if text.Contains " " && not (text.StartsWith "(") then
        "(" + text + ")"
    else
        text

/// <summary>The value type of an option, or None for another type.</summary>
let optionOf (t: Type) =
    if
        t.IsGenericType
        && t.GetGenericTypeDefinition () = typedefof<option<_>>
    then
        Some (t.GetGenericArguments()[0])
    else
        None

/// <summary>The generic parameters of a type or method with their type constraints, as F# source.</summary>
let generics (parameters: Type array) : string =
    if parameters.Length = 0 then
        ""
    else
        let names =
            parameters
            |> Array.map (fun p -> "'" + p.Name)
            |> String.concat ", "

        let constraints =
            [
                for p in parameters do
                    for c in p.GetGenericParameterConstraints () do
                        if c <> typeof<ValueType> then
                            sprintf "'%s :> %s" p.Name (name c)
            ]

        match constraints with
        | [] -> sprintf "<%s>" names
        | _ -> sprintf "<%s when %s>" names (String.concat " and " constraints)
