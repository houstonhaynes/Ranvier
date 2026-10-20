/// <summary>Checks that the portable project compiles without a browser.</summary>
module PackagingChecks

open System.IO
open System.Text.RegularExpressions
open System.Xml.Linq

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let private portable = Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, ".."))

let private project () =
    XDocument.Load (Path.Combine (portable, "Ranvier.View.Portable.fsproj"))

let private includes (element: string) (attribute: string) =
    [
        for node in (project ()).Descendants(XName.Get element) do
            match node.Attribute (XName.Get attribute) with
            | null -> ()
            | value -> value.Value.Replace ("\\", "/")
    ]

let private compiled () =
    includes "Compile" "Include"

let private browserReferences () =
    let expectedProjects = set [ "../../src/Ranvier/Ranvier.fsproj" ]

    let expectedPackages = set [ "Xantham.Fable.Core.TS" ]

    let extra (expected: Set<string>) (actual: string list) =
        actual
        |> List.filter (expected.Contains >> not)
        |> List.sort

    equal [] (extra expectedProjects (includes "ProjectReference" "Include"))

    equal [] (extra (expectedPackages.Add "FSharp.Core") (includes "PackageReference" "Include"))

let private browserAttribute =
    Regex (@"\[<\s*(?:[\w.]*\.)?(Emit|Import|ImportAll|ImportDefault|ImportMember|Global)\b.*?>\]", RegexOptions.Singleline)

let private browserEmits () =
    let touchesBrowser (found: Match) =
        let attribute = found.Value

        found.Groups[1].Value <> "Emit"
        || attribute.Contains "document"
        || attribute.Contains "window"
        || attribute.Contains "addEventListener"

    let offending =
        [
            for file in compiled () do
                let text =
                    File.ReadAllLines (Path.Combine (portable, file))
                    |> Array.filter (fun line -> not (line.TrimStart().StartsWith "//"))
                    |> String.concat "\n"

                for found in browserAttribute.Matches text do
                    if touchesBrowser found then
                        file + ": " + found.Value
        ]

    equal [] offending

let cases: (string * (unit -> unit)) array =
    [|
        "the portable project references only src/Ranvier and the Xantham type bindings", browserReferences
        "the portable project's attributes use no Import or Global, and no Emit touches document, window or addEventListener", browserEmits
    |]
