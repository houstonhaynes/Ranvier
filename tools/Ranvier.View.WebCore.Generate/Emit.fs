/// <summary>
/// Writes <c>Generated/Objects.fs</c>: one class per interface of the class set, each implementing the interfaces its
/// base class does not, with every member mapped to an <c>rv_dom_*</c> call, a hand-written body, or a throw.
/// </summary>
module Ranvier.View.WebCore.Generate.Emit

open System
open System.Reflection
open System.Text
open Ranvier.View.WebCore.Generate.Types

/// <summary>How a member was emitted.</summary>
type Status =
    | Mapped of functionName: string
    | Hand
    | Unmapped of reason: string

/// <summary>One emitted member: its class, interface, display name and status.</summary>
type Entry =
    {
        Class: string
        Interface: string
        Member: string
        Status: Status
    }

/// <summary>The inputs of a run.</summary>
type Input =
    {
        /// <summary>The <c>Fable.Core.TS+Dom</c> container type.</summary>
        Dom: Type
        Functions: Abi.Function list
        /// <summary>Hand-written bodies by key: <c>Interface.member</c>, <c>.get</c>/<c>.set</c> for accessors, <c>&lt;&gt;</c> for generic methods.</summary>
        Hand: Map<string, string>
        /// <summary>TS interfaces outside the ABI scope that get a class, such as the HTML elements of the tag map.</summary>
        Extra: string list
        /// <summary>The release kind of each root class, by TS interface name.</summary>
        Kinds: Map<string, string>
    }

let private flags =
    BindingFlags.Public
    ||| BindingFlags.Instance
    ||| BindingFlags.DeclaredOnly

let private idl (tsName: string) =
    match tsName with
    | "Selection" -> "DOMSelection"
    | "DOMRect" -> "DOMRectReadOnly"
    | name -> name.Split('`')[0]

/// <summary>The class name of an interface type definition, with its generic parameters.</summary>
let className (t: Type) =
    let baseName = t.Name.Split('`')[0] + "Object"
    baseName + generics (t.GetGenericArguments ())

let private classBase (t: Type) =
    t.Name.Split('`')[0] + "Object"

type private Context =
    {
        Input: Input
        Classes: Map<string, Type>
        Index: Map<string * string * string, Abi.Function list>
    }

let private isInterface (ctx: Context) (t: Type) =
    t.IsInterface || t.IsGenericParameter

/// <summary>The class for a TS type: a class-set interface, or an instance of a generic one.</summary>
let private classFor (ctx: Context) (t: Type) : string option =
    if t.IsGenericType && not t.IsGenericTypeDefinition then
        let definition = t.GetGenericTypeDefinition ()

        if ctx.Classes.ContainsKey definition.Name then
            Some (
                sprintf
                    "%s<%s>"
                    (classBase definition)
                    (t.GetGenericArguments ()
                     |> Array.map name
                     |> String.concat ", ")
            )
        else
            None
    elif ctx.Classes.ContainsKey t.Name then
        Some (classBase t)
    else
        None

let private literal (value: string) =
    match value with
    | "false" -> Some "0"
    | "true" -> Some "1"
    | "\"\"" -> Some "\"\""
    | _ when fst (Double.TryParse value) -> Some value
    | _ -> None

/// <summary>The F# expression passing the TS parameter <c>v</c> of type <c>ts</c> as the C parameter <c>p</c>.</summary>
let private input (ctx: Context) (fn: Abi.Function) (qualified: string) (v: string) (ts: Type) (p: Abi.Parameter) : Result<string, string> =
    let inner, nullable =
        match optionOf ts with
        | Some inner -> inner, true
        | None -> ts, false

    let scalar (convert: string -> string option) =
        match convert "x" with
        | None -> Error $"argument {p.Name}: {name ts} as {p.FSharp}"
        | Some _ when not nullable -> Ok (convert v).Value
        | Some converted ->
            let typed (value: string) =
                match p.FSharp with
                | "uint32" -> value + "u"
                | "uint16" -> value + "us"
                | "float" when not (value.Contains ".") -> value + ".0"
                | "float32" ->
                    (if value.Contains "." then value else value + ".0")
                    + "f"
                | _ -> value

            match fn.Defaults.TryFind p.Name |> Option.bind literal with
            | Some fallback -> Ok $"(match {v} with Some x -> {converted} | None -> {typed fallback})"
            | None -> Error $"argument {p.Name}: optional without an IDL default"

    let numeric (cast: string) (x: string) =
        if inner = typeof<float> then Some $"{cast} {x}" else None

    match p.FSharp with
    | "string" when inner = typeof<string> ->
        Ok (
            if nullable then
                $"Option.toObj {v}"
            else
                $"(if isNull {v} then \"\" else {v})"
        )
    | "int" ->
        scalar (fun x ->
            if inner = typeof<bool> then
                Some $"(if {x} then 1 else 0)"
            else
                numeric "int" x)
    | "uint32" -> scalar (numeric "uint32")
    | "uint16" -> scalar (numeric "uint16")
    | "float" -> scalar (fun x -> if inner = typeof<float> then Some x else None)
    | "float32" -> scalar (numeric "float32")
    | "nativeint" when p.C.StartsWith "Rv" && isInterface ctx inner ->
        Ok (
            if nullable then
                $"Interop.handleOr {v}"
            else
                $"Interop.handle (box {v})"
        )
    | "int[]" when nullable && inner = typeof<bool> -> Ok $"(match {v} with Some x -> [| (if x then 1 else 0) |] | None -> null)"
    | "int[]" when nullable && inner = typeof<float> -> Ok $"(match {v} with Some x -> [| int x |] | None -> null)"
    | "uint32[]" when nullable && inner = typeof<float> -> Ok $"(match {v} with Some x -> [| uint32 x |] | None -> null)"
    | dictionary when
        dictionary.EndsWith "[]"
        && dictionary.StartsWith "RvDom"
        && nullable
        ->
        Ok $"(match {v} with None -> null | Some _ -> Interop.unsupported \"{qualified} with an options argument\")"
    | _ -> Error $"argument {p.Name}: {name ts} as {p.C}"

let private zero (fsharp: string) =
    match fsharp with
    | "int&" -> "0"
    | "nativeint&" -> "0n"
    | "float&" -> "0.0"
    | "uint32&" -> "0u"
    | "int16&" -> "0s"
    | "uint16&" -> "0us"
    | "float32&" -> "0.0f"
    | other -> failwith other

/// <summary>The body of a mapped member, or why it cannot be mapped.</summary>
let private body (ctx: Context) (fn: Abi.Function) (qualified: string) (parameters: (string * Type) list) (returns: Type) =
    let inputs =
        List.zip parameters fn.Inputs
        |> List.map (fun ((v, ts), p) -> input ctx fn qualified v ts p)

    match
        inputs
        |> List.tryPick (function
            | Error e -> Some e
            | Ok _ -> None)
    with
    | Some reason -> Error reason
    | None ->
        let args =
            "this.Handle"
            :: (inputs
                |> List.map (function
                    | Ok e -> e
                    | Error _ -> ""))

        let call (outs: string list) =
            sprintf "NativeDom.%s (%s)" fn.Name (String.concat ", " (args @ outs))

        let checkCall outs =
            sprintf "Interop.check \"%s\" (%s)" qualified (call outs)

        let inner, nullable =
            match optionOf returns with
            | Some inner -> inner, true
            | None -> returns, false

        match fn.Outputs with
        | [] when returns = typeof<Void> || returns = typeof<unit> -> Ok [ checkCall [] ]
        | [] -> Error $"returns {name returns} with no result"
        | [ buffer ] when buffer.Name = "buffer" && inner = typeof<string> ->
            let text =
                sprintf "Interop.text \"%s\" (fun buffer capacity length -> %s)" qualified (call [ "buffer"; "capacity"; "length" ])

            Ok
                [
                    (if nullable then
                         $"Option.ofObj ({text})"
                     else
                         $"Interop.nonNull ({text})")
                ]
        | result :: rest when result.Name = "result" ->
            let present = rest |> List.exists (fun p -> p.Name = "present")
            let declare = [ sprintf "let mutable result = %s" (zero result.FSharp) ]

            let declare =
                if present then
                    declare @ [ "let mutable present = 0" ]
                else
                    declare

            let outs = if present then [ "&result"; "&present" ] else [ "&result" ]

            let finish (value: string) =
                Ok (declare @ [ checkCall outs; value ])

            let scalar (value: string) =
                if present && nullable then
                    finish $"(if present = 0 then None else Some ({value}))"
                elif present || nullable then
                    Error $"returns {name returns} for a nullable {result.C}"
                else
                    finish value

            match result.FSharp with
            | "nativeint&" when result.C = "RvNode**" || result.C = "RvEvent**" ->
                if not (isInterface ctx inner) then
                    Error $"returns {name returns} for {result.C}"
                else
                    let wrapped =
                        if result.C = "RvNode**" then
                            "this.Session.Node result"
                        else
                            "this.Session.Event result"

                    if nullable then
                        finish $"Interop.optionOf<{name inner}> ({wrapped})"
                    else
                        finish $"unbox<{name inner}> ({wrapped})"
            | "nativeint&" ->
                match classFor ctx inner with
                | Some cls ->
                    let created = $"(this.Session.Track ({cls} (this.Session, result)) :> {name inner})"

                    if nullable then
                        finish $"(if result = 0n then None else Some {created})"
                    else
                        finish created
                | None -> Error $"returns {name returns}, which has no class"
            | "int&" when inner = typeof<bool> -> scalar "result <> 0"
            | ("int&" | "uint32&" | "float&" | "int16&" | "uint16&" | "float32&") when inner = typeof<float> -> scalar "float result"
            | _ -> Error $"returns {name returns} for {result.C}"
        | _ -> Error "unrecognised results"

/// <summary>The ABI functions of a member, searched in the declaring interface, then the class and its bases.</summary>
let private candidates (ctx: Context) (chain: Type list) (iface: Type) (memberName: string) (kind: string) =
    (iface :: chain)
    |> List.map (fun t -> idl t.Name)
    |> List.distinct
    |> List.tryPick (fun i ->
        match ctx.Index.TryFind (i, memberName, kind) with
        | Some fns -> Some fns
        | None -> None)
    |> Option.defaultValue []

let private resolve (ctx: Context) chain iface memberName kind (parameters: (string * Type) list) returns qualified =
    match candidates ctx chain iface memberName kind with
    | [] -> Error "no ABI function"
    | fns ->
        match
            fns
            |> List.filter (fun f -> f.Inputs.Length = parameters.Length)
        with
        | [] -> Error "no ABI overload of this arity"
        | matching ->
            let attempts =
                matching
                |> List.map (fun fn -> fn, body ctx fn qualified parameters returns)

            match
                attempts
                |> List.tryFind (fun (_, r) -> Result.isOk r)
            with
            | Some (fn, Ok lines) -> Ok (fn.Name, lines)
            | _ ->
                match attempts with
                | (_, Error reason) :: _ -> Error reason
                | _ -> Error "no ABI function"

type private Emitter(ctx: Context, out: StringBuilder, entries: ResizeArray<Entry>) =
    member _.Line(indent: int, text: string) =
        out.Append(String (' ', indent * 4)).AppendLine text
        |> ignore

    member this.Body(indent: int, lines: string list) =
        for line in lines do
            this.Line (indent, line)

    /// <summary>True while the class being written is a node class, whose members a harness can intercept.</summary>
    member val Intercepts = false with get, set

    /// <summary>
    /// The body run through the session's interceptions of <c>name</c> while it has any: the arguments are rebound from
    /// the array the interceptions pass on.
    /// </summary>
    member this.Intercepted(name: string, parameters: (string * Type) list, returns: Type, lines: string list) : string list =
        let unitResult = returns = typeof<Void> || returns = typeof<unit>

        let boxed =
            parameters
            |> List.map (fun (v, _) -> $"box {v}")
            |> String.concat "; "

        [
            "if this.Session.Intercepting then"
            $"    Interop.intercept this \"{name}\" [| {boxed} |] (fun args ->"
            yield!
                parameters
                |> List.mapi (fun i (v, t) -> $"        let {v}: {Types.name t} = unbox args[{i}]")
            "        let result ="
            yield!
                lines
                |> List.map (fun line -> "            " + line)
            "        box result)"
            (if unitResult then
                 "    |> ignore"
             else
                 $"    |> unbox<{Types.name returns}>")
            "else"
            yield! lines |> List.map (fun line -> "    " + line)
        ]

    /// <summary>A member body: hand-written, mapped or a throw, recorded in the report.</summary>
    member this.Resolve
        (cls: string, chain, iface: Type, memberName, kind, handKey: string, parameters, returns, intercepted: string option)
        : string list =
        let qualified = $"{idl iface.Name}.{memberName}"

        let wrap lines =
            match intercepted with
            | Some name when this.Intercepts -> this.Intercepted (name, parameters, returns, lines)
            | _ -> lines

        let record status =
            entries.Add
                {
                    Class = cls
                    Interface = iface.Name
                    Member =
                        (if kind = "op" then
                             memberName
                         else
                             $"{memberName} ({kind}ter)")
                    Status = status
                }

        match ctx.Input.Hand.TryFind handKey with
        | Some hand ->
            record Hand
            wrap [ hand ]
        | None ->
            match resolve ctx chain iface memberName kind parameters returns qualified with
            | Ok (fn, lines) ->
                record (Mapped fn)
                wrap lines
            | Error reason ->
                record (Unmapped reason)
                [ $"Interop.unsupported \"{idl iface.Name}.{memberName}\"" ]

    member this.Interface(cls: string, chain: Type list, iface: Type) =
        let methods =
            iface.GetMethods flags
            |> Array.filter (fun m -> not m.IsSpecialName)

        let properties = iface.GetProperties flags

        let ifaceName =
            name (
                if iface.IsGenericTypeDefinition then
                    iface.MakeGenericType (iface.GetGenericArguments ())
                else
                    iface
            )

        if methods.Length = 0 && properties.Length = 0 then
            this.Line (1, $"interface {ifaceName}")
        else
            this.Line (1, $"interface {ifaceName} with")
            let prefix = iface.Name.Split('`')[0]

            for p in properties do
                let indexes = p.GetIndexParameters ()
                let t = name p.PropertyType
                this.Line (2, $"member this.{ident p.Name}")

                let indexArgs, indexParams, memberName, getKind =
                    if indexes.Length = 1 then
                        $"(index: {name indexes[0].ParameterType})", [ "index", indexes[0].ParameterType ], "item", "op"
                    else
                        "()", [], p.Name, "get"

                if p.CanRead then
                    this.Line (3, $"with get {indexArgs} : {t} =")

                    let intercepted = if indexes.Length = 1 then None else Some $"get_{p.Name}"

                    this.Body (
                        4,
                        this.Resolve (cls, chain, iface, memberName, getKind, $"{prefix}.{p.Name}.get", indexParams, p.PropertyType, intercepted)
                    )

                if p.CanWrite then
                    let setArgs =
                        if indexes.Length = 1 then
                            $"(index: {name indexes[0].ParameterType}) (value: {t})"
                        else
                            $"(value: {t})"

                    let keyword = if p.CanRead then "and" else "with"
                    this.Line (3, $"{keyword} set {setArgs} =")

                    let lines =
                        if indexes.Length = 1 then
                            entries.Add
                                {
                                    Class = cls
                                    Interface = iface.Name
                                    Member = "Item (setter)"
                                    Status = Unmapped "indexed setter"
                                }

                            [ $"Interop.unsupported \"{prefix}.Item\"" ]
                        else
                            this.Resolve (
                                cls,
                                chain,
                                iface,
                                p.Name,
                                "set",
                                $"{prefix}.{p.Name}.set",
                                [ "value", p.PropertyType ],
                                typeof<Void>,
                                Some $"set_{p.Name}"
                            )

                    this.Body (4, lines)

            for m in methods do
                let parameters = m.GetParameters ()

                let declared =
                    parameters
                    |> Array.mapi (fun i p ->
                        let pname = if String.IsNullOrEmpty p.Name then $"arg{i}" else p.Name

                        if p.IsDefined (typeof<OptionalArgumentAttribute>) then
                            $"?{ident pname}: {name (optionOf p.ParameterType).Value}"
                        elif p.IsDefined (typeof<ParamArrayAttribute>) then
                            $"[<System.ParamArray>] {ident pname}: {name p.ParameterType}"
                        else
                            $"{ident pname}: {name p.ParameterType}")
                    |> String.concat ", "

                let gens =
                    if m.IsGenericMethodDefinition then
                        generics (m.GetGenericArguments ())
                    else
                        ""

                this.Line (2, $"member this.{ident m.Name}{gens}({declared}) : {name m.ReturnType} =")

                let values =
                    parameters
                    |> Array.mapi (fun i p -> ident (if String.IsNullOrEmpty p.Name then $"arg{i}" else p.Name), p.ParameterType)
                    |> List.ofArray

                let key =
                    $"{prefix}.{m.Name}"
                    + (if m.IsGenericMethodDefinition then "<>" else "")

                let lines =
                    if
                        m.IsGenericMethodDefinition
                        && m.ReturnType.IsGenericParameter |> not
                        && m.GetGenericArguments ()
                           |> Array.exists (fun g -> g.GetGenericParameterConstraints().Length = 0)
                        && not (ctx.Input.Hand.ContainsKey key)
                    then
                        entries.Add
                            {
                                Class = cls
                                Interface = iface.Name
                                Member = m.Name + "<>"
                                Status = Unmapped "unconstrained generic overload"
                            }

                        [ $"Interop.unsupported \"{prefix}.{m.Name}\"" ]
                    else
                        this.Resolve (cls, chain, iface, m.Name, "op", key, values, m.ReturnType, Some m.Name)

                this.Body (3, lines)

/// <summary>The class set: in-scope interfaces, their extras, and the class each one inherits.</summary>
let private classSet (input: Input) =
    let lookup (n: string) =
        input.Dom.GetNestedType n |> Option.ofObj

    let names =
        [
            for f in input.Functions ->
                (if f.Interface = "DOMSelection" then
                     "Selection"
                 else
                     f.Interface)
            yield! input.Extra
        ]
        |> List.distinct

    names
    |> List.choose lookup
    |> List.filter (fun t -> t.IsInterface)
    |> List.map (fun t -> t.Name, t)
    |> Map.ofList

let private baseOf (classes: Map<string, Type>) (t: Type) : Type option =
    t.GetInterfaces ()
    |> Array.filter (fun i -> classes.ContainsKey i.Name && not i.IsGenericType)
    |> Array.sortByDescending (fun i -> i.GetInterfaces().Length, i.Name)
    |> Array.tryHead

/// <summary>Writes the generated file and returns it with the report entries.</summary>
let run (input: Input) (tagMap: (string * string) list) : string * Entry list =
    let classes = classSet input

    let ctx =
        {
            Input = input
            Classes = classes
            Index =
                input.Functions
                |> List.groupBy (fun f -> f.Interface, f.Member, f.Kind)
                |> Map.ofList
        }

    let out = StringBuilder ()
    let entries = ResizeArray<Entry>()
    let emitter = Emitter (ctx, out, entries)

    let ordered =
        classes
        |> Map.toList
        |> List.map snd
        |> List.sortBy (fun t -> t.GetInterfaces().Length, t.Name)

    let rec chainOf (t: Type) =
        match baseOf classes t with
        | Some b -> b :: chainOf b
        | None -> []

    let mutable first = true

    for t in ordered do
        let chain = chainOf t
        let cls = className t
        out.AppendLine () |> ignore

        out.AppendLine (sprintf "%s %s(session: Session, handle: nativeint) =" (if first then "type internal" else "and internal") cls)
        |> ignore

        first <- false

        match chain with
        | b :: _ -> emitter.Line (1, $"inherit {classBase b}(session, handle)")
        | [] ->
            match input.Kinds.TryFind (t.Name) with
            | Some kind -> emitter.Line (1, $"inherit NativeObject(session, handle, {kind})")
            | None -> failwith $"{t.Name} is a root class without a release kind"

        let covered =
            match chain with
            | b :: _ -> set [ yield b.Name; for i in b.GetInterfaces () -> i.Name ]
            | [] -> Set.empty

        let explicitly =
            t
            :: [
                for i in t.GetInterfaces () do
                    if not (covered.Contains i.Name) then
                        i
            ]

        // F# requires an interface of the base class to be implemented again when two of the class's own interfaces
        // include it.
        let rec withShared (set: Type list) =
            let shared =
                t.GetInterfaces ()
                |> Array.filter (fun j ->
                    covered.Contains j.Name
                    && not (set |> List.contains j)
                    && (set
                        |> List.filter (fun i -> i.GetInterfaces () |> Array.contains j)
                        |> List.length)
                       >= 2)
                |> List.ofArray

            if shared.IsEmpty then set else withShared (set @ shared)

        let implemented =
            withShared explicitly
            |> List.sortBy (fun i -> i.GetInterfaces().Length, i.Name)

        let root =
            match List.tryLast chain with
            | Some root -> root
            | None -> t

        emitter.Intercepts <- input.Kinds.TryFind root.Name = Some "Kind.Node"

        for iface in implemented do
            emitter.Interface (cls, t :: chain, iface)

    // Element classes by upper-case tag name, for the wrapper of a new element handle.
    out.AppendLine () |> ignore

    out.AppendLine "/// <summary>Wraps a node handle in the class of its node type and HTML tag.</summary>"
    |> ignore

    out.AppendLine "and [<AbstractClass; Sealed>] internal Classes ="
    |> ignore

    emitter.Line (1, "/// <summary>The wrapper class of an HTML element by its upper-case tag name, else <c>ElementObject</c>.</summary>")
    emitter.Line (1, "static member Element(session: Session, handle: nativeint, tag: string) : NativeObject =")
    emitter.Line (2, "match tag with")

    for tag, iface in tagMap do
        if classes.ContainsKey iface then
            emitter.Line (2, $"| \"{tag.ToUpperInvariant ()}\" -> {iface}Object (session, handle)")

    emitter.Line (2, "| _ when tag <> tag.ToLowerInvariant () -> HTMLElementObject (session, handle)")
    emitter.Line (2, "| _ -> ElementObject (session, handle)")

    out.ToString (), List.ofSeq entries
