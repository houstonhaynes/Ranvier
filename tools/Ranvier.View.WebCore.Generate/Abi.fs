/// <summary>The generated <c>rv_dom_*</c> functions, read from <c>RanvierWebCoreDom.h</c> and <c>NativeDom.fs</c>.</summary>
module Ranvier.View.WebCore.Generate.Abi

open System.Text.RegularExpressions

/// <summary>A parameter: its C type from the header, its name, and its F# type in <c>NativeDom.fs</c>.</summary>
type Parameter =
    {
        C: string
        Name: string
        FSharp: string
    }

/// <summary>
/// An <c>rv_dom_*</c> function. <c>Kind</c> is <c>get</c>, <c>set</c> or <c>op</c>; <c>Interface</c> and <c>Member</c> are
/// the IDL names from the header comment, and <c>Defaults</c> maps a parameter to its IDL default.
/// </summary>
type Function =
    {
        Name: string
        Interface: string
        Member: string
        Kind: string
        Parameters: Parameter list
        Defaults: Map<string, string>
    }

    /// <summary>The parameters after <c>target</c> that carry IDL arguments.</summary>
    member this.Inputs =
        this.Parameters
        |> List.tail
        |> List.filter (fun p ->
            match p.Name with
            | "result"
            | "present"
            | "buffer"
            | "capacity"
            | "length" -> false
            | _ -> true)

    /// <summary>The result parameters: <c>result</c>, <c>present</c>, or the string triple.</summary>
    member this.Outputs =
        this.Parameters
        |> List.filter (fun p ->
            match p.Name with
            | "result"
            | "present"
            | "buffer" -> true
            | _ -> false)

let private externs (nativeDom: string) : Map<string, (string * string) list> =
    let rx = Regex @"extern \S+ (rv_dom_\w+)\((.*)\)"

    [
        for m in rx.Matches nativeDom do
            let parameters =
                m.Groups[2].Value.Split ','
                |> Array.toList
                |> List.map (fun part ->
                    let part = Regex.Replace(part, @"\[<[^>]*>\]", "").Trim()
                    let at = part.LastIndexOf ' '
                    part.Substring(0, at).Trim(), part.Substring(at + 1).Trim('`'))

            m.Groups[1].Value, parameters
    ]
    |> Map.ofList

/// <summary>Every commented <c>rv_dom_*</c> function of the header, joined with its F# declaration.</summary>
let read (header: string) (nativeDom: string) : Function list =
    let declared = externs nativeDom

    let rx =
        Regex (@"/\*\s*(?<c>(?:(?!\*/).)*?)\*/\s*RV_EXPORT\s+int32_t\s+(?<n>rv_dom_\w+)\((?<p>[^)]*)\);", RegexOptions.Singleline)

    [
        for m in rx.Matches header do
            let comment = Regex.Replace(m.Groups["c"].Value, @"\s*\n\s*\*?\s*", " ").Trim()

            let head = Regex.Match (comment, @"^(?<i>\w+)\.(?<m>\w+)(?<rest>.*)$")
            let name = m.Groups["n"].Value

            if head.Success && declared.ContainsKey name then
                let rest = head.Groups["rest"].Value

                let kind =
                    if rest.StartsWith " getter" then "get"
                    elif rest.StartsWith " setter" then "set"
                    else "op"

                let fsharp = declared[name]

                let parameters =
                    m.Groups["p"].Value.Split ','
                    |> Array.toList
                    |> List.mapi (fun i part ->
                        let mm = Regex.Match (part.Trim (), @"^(?<t>.*?[\s\*])(?<n>\w+)$")

                        {
                            C = Regex.Replace (mm.Groups["t"].Value.Trim(), @"\s+\*", "*")
                            Name = mm.Groups["n"].Value
                            FSharp = fst fsharp[i]
                        })

                let defaults =
                    [
                        for d in Regex.Matches (comment, @"(\w+) defaults to (.+?) in IDL") -> d.Groups[1].Value, d.Groups[2].Value
                    ]
                    |> Map.ofList

                {
                    Name = name
                    Interface = head.Groups["i"].Value
                    Member = head.Groups["m"].Value
                    Kind = kind
                    Parameters = parameters
                    Defaults = defaults
                }
    ]
