namespace Ranvier.Docs.Maps.Authoring

open System
open System.Text
open System.Text.RegularExpressions

/// <summary>The rewritten Markdown, component cells and body-relative diagnostics of a page.</summary>
type FenceScan =
    {
        Body: string
        Cells: PageCell list
        Problems: (int * string) list
    }

/// <summary>Scans component and signal-map fences without a UI framework dependency.</summary>
[<RequireQualifiedAccess>]
module FenceScanner =
    /// <summary>Rewrites executable fences; ordinary Markdown remains unchanged.</summary>
    let scan (key: string) (body: string) : FenceScan =
        let lines = body.Replace("\r\n", "\n").Split '\n'
        let output = StringBuilder ()
        let cells = ResizeArray<PageCell>()
        let problems = ResizeArray<int * string>()
        let ids = Collections.Generic.HashSet<string>()
        let opening = Regex "^(?<indent> {0,3})(?<fence>`{3,}|~{3,})(?<info>[^\r\n]*)$"

        let emit (text: string) =
            output.Append(text).Append '\n' |> ignore

        let mutable index = 0

        while index < lines.Length do
            let matched = opening.Match lines[index]

            if not matched.Success then
                emit lines[index]
                index <- index + 1
            else
                let fence = matched.Groups["fence"].Value

                let words =
                    matched.Groups["info"].Value.Trim().Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
                    |> List.ofArray

                let closing =
                    seq { index + 1 .. lines.Length - 1 }
                    |> Seq.tryFind (fun at ->
                        let raw = lines[at]
                        let line = raw.TrimEnd().TrimStart(' ')
                        let indent = raw.Length - raw.TrimStart(' ').Length

                        indent <= 3
                        && line.Length >= fence.Length
                        && line |> Seq.forall ((=) fence[0]))

                let stop = closing |> Option.defaultValue lines.Length

                let executable =
                    match words with
                    | language :: token :: _ when List.contains (language.ToLowerInvariant ()) [ "fsharp"; "fs"; "f#" ] ->
                        token = "ranvier" || token = "map"
                    | _ -> false

                if not executable then
                    for at in index .. min stop (lines.Length - 1) do
                        emit lines[at]
                else
                    let token = words[1]
                    let flags = words |> List.skip 2

                    let option name =
                        flags
                        |> List.tryPick (fun flag ->
                            if flag.StartsWith (name + "=", StringComparison.Ordinal) then
                                Some (flag.Substring (name.Length + 1))
                            else
                                None)

                    let id =
                        option "id"
                        |> Option.defaultValue ($"c{cells.Count + 1}")

                    let code = lines[index + 1 .. stop - 1] |> String.concat "\n"
                    let line = index + 2

                    let error message =
                        problems.Add (index + 1, message)

                    if closing.IsNone then
                        error "The executable fence has no closing fence."

                    if not (Regex.IsMatch (id, "^[A-Za-z0-9_-]+$")) then
                        error "A fence id must contain only letters, digits, '_' or '-'."

                    if not (ids.Add (MapPage.componentName id)) then
                        error $"The id '{id}' collides with an earlier fence."

                    let setup = List.contains "setup" flags
                    let show = option "show" |> Option.defaultValue "both"

                    if not (List.contains show [ "both"; "code"; "output"; "inline" ]) then
                        error $"Unknown show mode '{show}'."

                    let common flag =
                        flag = "setup"
                        || flag.StartsWith "id="
                        || flag.StartsWith "show="
                        || flag.StartsWith "render="

                    let remaining = flags |> List.filter (common >> not)

                    let kind =
                        if token = "map" then
                            match MapFlags.parse remaining, MapFence.compile id remaining code with
                            | Ok parsed, Ok compiled -> Some (PageCellKind.Map (compiled, parsed))
                            | Error message, _ ->
                                error message
                                None
                            | _, Error found ->
                                for offset, message in found do
                                    problems.Add (line + offset - 1, message)

                                None
                        else
                            for flag in remaining do
                                error $"Unknown component option '{flag}'."

                            if setup then
                                Some PageCellKind.Declarations
                            else
                                match option "render" with
                                | Some name when Regex.IsMatch (name, "^[A-Za-z_][A-Za-z0-9_'.]*$") -> Some (PageCellKind.Render name)
                                | Some _ ->
                                    error "render= requires an F# value name."
                                    None
                                | None when Regex.IsMatch (code, "(?m)^(?:open|let|type|module|namespace|exception)\\b") ->
                                    Some PageCellKind.Declarations
                                | None -> Some PageCellKind.Expression

                    match kind with
                    | None -> ()
                    | Some kind ->
                        cells.Add
                            {
                                Id = id
                                Kind = if show = "code" then PageCellKind.CodeOnly kind else kind
                                Code = code
                                Line = line
                            }

                        let mounts =
                            kind <> PageCellKind.Declarations
                            && show <> "code"

                        let shown = not setup && (show = "both" || show = "code")

                        if token = "map" && mounts then
                            emit "<div class=\"rv-map-card\">\n"

                        if shown then
                            emit (fence + "fsharp")
                            emit code
                            emit fence
                            emit ""

                        if mounts then
                            emit (MapPage.host (show = "inline") key id + "\n")

                        if token = "map" && mounts then
                            emit "</div>\n"

                index <- stop + 1

        {
            Body =
                output.ToString().TrimEnd('\r', '\n')
                + (if body.EndsWith "\n" then "\n" else "")
            Cells = List.ofSeq cells
            Problems = List.ofSeq problems
        }
