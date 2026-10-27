namespace Ranvier.Css.Spec

open System
open System.Collections.Generic
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

/// <summary>Renders the default unified property surface from canonical, checked conversion paths.</summary>
module internal SugarGeneration =
    let private literal (value: string) =
        JsonSerializer.Serialize value

    let private identifier (value: string) =
        let parts =
            Regex.Matches (value, "[A-Za-z0-9]+")
            |> Seq.cast<Match>
            |> Seq.map _.Value
            |> Seq.toList

        let joined =
            parts
            |> List.map (fun part ->
                Char.ToUpperInvariant(part[0]).ToString()
                + part.Substring (1))
            |> String.concat ""

        let joined =
            if joined = "" then "Value"
            elif Char.IsDigit joined[0] then "N" + joined
            else joined

        Char.ToLowerInvariant(joined[0]).ToString()
        + joined.Substring (1)

    let private quoted (value: string) =
        "``" + value + "``"

    let private suffix (value: string) =
        Convert.ToHexStringLower(SHA256.HashData (Encoding.UTF8.GetBytes value)).Substring(0, 8)

    /// <summary>Appends typed value/reader overloads in the parent of a Generated namespace, keeping the canonical module qualified.</summary>
    let render (namespaceName: string) (manifest: JsonElement) =
        let authoringNamespace =
            if namespaceName.EndsWith (".Generated", StringComparison.Ordinal) then
                namespaceName.Substring (0, namespaceName.Length - 10)
            else
                namespaceName + ".Sugar"

        let qualifiedNamespace =
            namespaceName.Split ('.')
            |> Array.map quoted
            |> String.concat "."

        let source = StringBuilder ()
        let primitiveMetadata = ResizeArray<obj>()

        let line (text: string) =
            source.Append(text).Append('\n') |> ignore

        let definitions =
            manifest.GetProperty("definitions").EnumerateArray()
            |> Seq.toList

        let text (key: string) (item: JsonElement) =
            item.GetProperty(key).GetString()

        let current =
            definitions
            |> List.filter (fun item ->
                (text "moduleName" item).Contains("_Obsolete_")
                |> not)

        let properties =
            current
            |> List.filter (fun item -> text "kind" item = "Property")

        let wide =
            manifest.GetProperty("cssWideKeywords").EnumerateArray()
            |> Seq.map _.GetString()
            |> Set.ofSeq

        let tokens =
            properties
            |> List.collect (fun item ->
                item.GetProperty("ergonomicKeywords").EnumerateArray()
                |> Seq.map (text "token")
                |> Seq.toList)
            |> Set.ofList
            |> Set.union wide
            |> Set.toList

        let names = Dictionary<string, string>()

        let reserve proposed identity =
            match names.TryGetValue proposed with
            | true, existing when existing = identity -> proposed
            | true, _ ->
                let resolved = proposed + "_" + suffix identity
                names.Add (resolved, identity)
                resolved
            | _ ->
                names.Add (proposed, identity)
                proposed

        let propertyNames =
            properties
            |> List.map (fun item ->
                let api = text "moduleName" item
                let raw = text "name" item
                let scopes = item.GetProperty("scopes").GetArrayLength()

                let proposed =
                    identifier raw
                    + (if scopes = 0 then "" else "_" + suffix api)

                api, reserve proposed ("property:" + api))
            |> Map.ofList

        let keywordNames =
            tokens
            |> List.map (fun token -> token, reserve (identifier token) ("keyword:" + token))
            |> Map.ofList

        let keywordTags =
            tokens
            |> List.map (fun token -> token, "Keyword_" + suffix token)
            |> Map.ofList

        line (
            "\nnamespace "
            + (authoringNamespace.Split ('.')
               |> Array.map quoted
               |> String.concat ".")
        )

        line "/// <summary>Nominal keyword markers accepted only by properties whose grammars include the keyword.</summary>"
        line "module CssKeywords ="

        for token in tokens do
            line (
                "    /// <summary>Typed CSS keyword "
                + System.Security.SecurityElement.Escape token
                + ".</summary>"
            )

            line (
                "    type "
                + keywordTags[token]
                + " = private | "
                + keywordTags[token]
                + " of unit"
            )

        line "/// <summary>Default typed CSS helpers; each property accepts a static value or an instance reader.</summary>"
        line "[<AbstractClass; Sealed>]"
        line "type Css private () ="

        let canonical = "global." + qualifiedNamespace + ".Css."

        let valueType marker =
            canonical
            + "Value<"
            + canonical
            + "Markers."
            + marker
            + ">"

        let mutable memberIndent = "    "

        let obsolete (item: JsonElement) =
            let item = item.GetProperty ("obsolete")

            if item.ValueKind = JsonValueKind.String then
                line (
                    memberIndent
                    + "[<System.Obsolete("
                    + literal (item.GetString ())
                    + ")>]"
                )

        let primitives =
            current
            |> List.filter (fun item ->
                text "kind" item = "Type"
                && item.GetProperty("scopes").GetArrayLength() = 0)

        for item in primitives do
            for helper in item.GetProperty("primitiveHelpers").EnumerateArray() do
                let methodName = text "methodName" helper
                let primitive = (text "name" item).Trim('<', '>')

                let proposed =
                    if methodName = "create" || methodName = "value" then
                        if primitive = "string" then
                            "text"
                        else
                            identifier primitive
                    else
                        identifier methodName

                let methodName =
                    reserve
                        proposed
                        ("primitive:"
                         + text "moduleName" item
                         + ":"
                         + methodName)

                let call =
                    canonical
                    + text "moduleName" item
                    + "."
                    + quoted (text "methodName" helper)

                let parameters = text "parameters" helper

                primitiveMetadata.Add (
                    box
                        {|
                            memberName = methodName
                            marker = text "marker" item
                            production = primitive
                            methodName = text "methodName" helper
                            argumentType =
                                if parameters.Contains ("float") then "float"
                                elif parameters.Contains ("int") then "int"
                                elif parameters.Contains ("string") then "string"
                                else "none"
                        |}
                )

                line "    /// <summary>Constructs a typed CSS primitive using the canonical value constructor.</summary>"
                obsolete item

                line (
                    "    static member inline "
                    + quoted methodName
                    + (if parameters = "" then "" else " " + parameters)
                    + " = "
                    + call
                    + (if helper.GetProperty("takesValue").GetBoolean() then
                           " value"
                       else
                           "")
                )

        for token in tokens do
            line (
                "    /// <summary>Typed CSS keyword "
                + System.Security.SecurityElement.Escape token
                + ".</summary>"
            )

            line (
                "    static member inline "
                + quoted keywordNames[token]
                + " : global.Ranvier.Css.Value<CssKeywords."
                + keywordTags[token]
                + "> = global.Ranvier.Css.Generated.value "
                + literal token
            )

        for item in properties do
            line "/// <summary>Unified overloads for one specification property; opened automatically with the CSS namespace.</summary>"
            line "[<AutoOpen>]"
            line ("module Css" + text "moduleName" item + " =")
            line "    type Css with"
            memberIndent <- "        "
            let propertyName = propertyNames[text "moduleName" item]

            let declaration =
                canonical
                + text "moduleName" item
                + ".declaration"

            let emit parameter body reactive =
                line (
                    if reactive then
                        memberIndent
                        + "/// <summary>Binds a typed instance reader through the property's static CSS variable slot.</summary>"
                    else
                        memberIndent
                        + "/// <summary>Creates a declaration through a checked path in this property's specification grammar.</summary>"
                )

                obsolete item

                line (
                    memberIndent
                    + "static member inline "
                    + quoted propertyName
                    + " ("
                    + parameter
                    + ") = "
                    + body
                )

            for conversion in item.GetProperty("ergonomicValues").EnumerateArray() do
                let input = valueType (text "marker" conversion)

                let expression =
                    (text "expression" conversion).Replace("Syntax.", canonical + "Syntax.")

                let body = declaration + " (" + expression + ")"
                emit ("value: " + input) body false

                emit
                    ("read: 'instance -> " + input)
                    ("global.Ranvier.Css.Style.dynamicWithKeywords "
                     + canonical
                     + "cssWideKeywords "
                     + literal (text "name" item)
                     + " (fun instance -> let value = read instance in "
                     + body
                     + ")")
                    true

            let propertyKeywords =
                item.GetProperty("ergonomicKeywords").EnumerateArray()
                |> Seq.map (fun keyword -> text "token" keyword, text "expression" keyword)
                |> Map.ofSeq

            for token in Set.union wide (propertyKeywords |> Map.keys |> Set.ofSeq) do
                let input =
                    "global.Ranvier.Css.Value<CssKeywords."
                    + keywordTags[token]
                    + ">"

                let body =
                    match Map.tryFind token propertyKeywords with
                    | Some expression ->
                        declaration
                        + " ("
                        + expression.Replace ("Syntax.", canonical + "Syntax.")
                        + ")"
                    | None ->
                        "global.Ranvier.Css.RuntimeApi.declaration "
                        + literal (text "name" item)
                        + " "
                        + literal token

                emit ("_: " + input) body false

                if not (Set.contains token wide) then
                    emit
                        ("read: 'instance -> " + input)
                        ("global.Ranvier.Css.Style.dynamicWithKeywords "
                         + canonical
                         + "cssWideKeywords "
                         + literal (text "name" item)
                         + " (fun instance -> read instance |> ignore; "
                         + body
                         + ")")
                        true

        let sugar =
            JsonSerializer.SerializeToElement
                {|
                    schemaVersion = 2
                    namespaceName = authoringNamespace
                    properties =
                        properties
                        |> List.map (fun item ->
                            {|
                                cssName = text "name" item
                                memberName = propertyNames[text "moduleName" item]
                                scopes =
                                    item.GetProperty("scopes").EnumerateArray()
                                    |> Seq.map _.GetString()
                                    |> Seq.toArray
                                acceptedMarkers =
                                    item.GetProperty("ergonomicValues").EnumerateArray()
                                    |> Seq.map (text "marker")
                                    |> Seq.toArray
                                acceptedValues =
                                    item.GetProperty("ergonomicValues").EnumerateArray()
                                    |> Seq.map (fun value ->
                                        {|
                                            marker = text "marker" value
                                            ranges = value.GetProperty("ranges").Clone()
                                        |})
                                    |> Seq.toArray
                                acceptedKeywords =
                                    Set.union
                                        wide
                                        (item.GetProperty("ergonomicKeywords").EnumerateArray()
                                         |> Seq.map (text "token")
                                         |> Set.ofSeq)
                                    |> Set.toArray
                                obsolete = item.GetProperty("obsolete").ValueKind = JsonValueKind.String
                            |})
                    primitives = primitiveMetadata.ToArray ()
                    keywords =
                        tokens
                        |> List.map (fun token ->
                            {|
                                token = token
                                memberName = keywordNames[token]
                            |})
                |}

        source.ToString (), sugar
