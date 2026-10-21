// Asserts deterministic full HTML output and coverage, typed native members and events, policy exclusions
// and corrupt-catalogue rejection over the generator's output.
// Usage: dotnet fsi tools/Ranvier.Dom.Generate/GeneratorTests.fsx
open System
open System.Diagnostics
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions

exception CheckFailed of string

let check (condition: bool) (message: string) =
    if not condition then
        raise (CheckFailed message)

let root = __SOURCE_DIRECTORY__
let repositoryRoot = Path.GetFullPath (Path.Combine (root, "..", ".."))
let generatorProject = Path.Combine (root, "Ranvier.Dom.Generate.fsproj")

/// Reads a file as text with LF line endings.
let readText (path: string) =
    File.ReadAllText(path).Replace("\r\n", "\n")

let flatten (text: string) =
    Regex.Replace (text, @"\s+", " ")

/// The number of non-overlapping occurrences of the substring.
let count (part: string) (text: string) =
    let rec go (from: int) acc =
        match text.IndexOf (part, from, StringComparison.Ordinal) with
        | -1 -> acc
        | at -> go (at + part.Length) (acc + 1)

    go 0 0

let matches (pattern: string) (text: string) =
    [ for m in Regex.Matches (text, pattern) -> m ]

let group (index: int) (m: Match) =
    m.Groups[index].Value

let sameBytes (a: string) (b: string) =
    File.ReadAllBytes a = File.ReadAllBytes b

/// Runs the generator; returns its exit code, standard output and standard error.
let generate (args: string list) =
    let info =
        ProcessStartInfo (
            "dotnet",
            [ "run"; "--project"; generatorProject; "-c"; "Release" ]
            @ args,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        )

    use proc = Process.Start info
    let stderr = proc.StandardError.ReadToEndAsync ()
    let stdout = proc.StandardOutput.ReadToEnd ()
    proc.WaitForExit ()
    proc.ExitCode, stdout, stderr.Result

let rec copyDirectory (source: string) (target: string) =
    Directory.CreateDirectory target |> ignore

    for file in Directory.GetFiles source do
        File.Copy (file, Path.Combine (target, Path.GetFileName file))

    for directory in Directory.GetDirectories source do
        copyDirectory directory (Path.Combine (target, Path.GetFileName directory))

let jsonArray (node: JsonNode) =
    node.AsArray () |> Seq.toList

let run (folder: string) =
    let first = Path.Combine (folder, "first.fs")
    let second = Path.Combine (folder, "second.fs")

    for target in [ first; second ] do
        let code, stdout, stderr = generate [ "--"; target ]
        check (code = 0) (stdout + stderr)

    check (sameBytes first second) "regeneration must be byte-identical"

    for output, committed in
        [
            first, "experiments/Ranvier.View.Foundation/Generated/Html.fs"
            Path.Combine (folder, "firstTemplate.fs"), "experiments/Ranvier.View.Portable/Generated/HtmlTemplate.fs"
            Path.Combine (folder, "firstTemplateSugar.fs"), "experiments/Ranvier.View.Portable/Generated/HtmlTemplateSugar.fs"
        ] do
        check (readText output = readText (Path.Combine (repositoryRoot, committed))) (committed + " must equal the generator's output")

    let text = readText first
    check (text.Contains "[<RequireQualifiedAccess>]\nmodule Ranvier.View.Foundation.Generated.Html\n") "Generated.Html requires qualified access"
    check (text.Contains "module Authoring =") "generate overloaded authoring helpers"
    check (text.Contains "static member inline") "static member inline in text"
    check (text.Contains "module Program =") "generate the bound Program element authoring surface"

    let program =
        text.Substring (
            text.IndexOf "module Program ="
            + "module Program =".Length
        )

    check (count "let inline " program = 112) "every HTML tag needs one inline Program constructor"
    check (count "Typed.programElement " program = 112) "bound constructors must inline the typed Program path"
    check (text.Contains "Typed.Modifier<Dom.HTMLDivElement> array") "Typed.Modifier<Dom.HTMLDivElement> array in text"
    check (program.Contains "Representation.Program.View array") "Representation.Program.View array in program"

    check (not (text.Contains "Typed.Modifier<Dom.HTMLDivElement> list")) "Typed.Modifier<Dom.HTMLDivElement> list not in text"

    check (not (program.Contains "Representation.Program.View list")) "Representation.Program.View list not in program"

    check (program.Contains "Typed.programElement (HtmlTag \"br\") modifiers [||]") "Typed.programElement (HtmlTag \"br\") modifiers [||] in program"

    check (text.Contains "Dom.HTMLInputElement") "Dom.HTMLInputElement in text"
    check (text.Contains "Dom.InputEvent -> unit") "Dom.InputEvent -> unit in text"
    check (text.Contains "Dom.FileList option") "Dom.FileList option in text"
    check (text.Contains "let ``files`` (") "let ``files`` ( in text"
    check (not (text.Contains "let filesStatic")) "let filesStatic not in text"
    check (not (text.Contains "let formStatic")) "let formStatic not in text"
    let coverageFile = Path.ChangeExtension (first, ".coverage.json")
    use coverageDocument = JsonDocument.Parse (readText coverageFile)
    let coverage = coverageDocument.RootElement

    let tags =
        coverage.GetProperty("tags").EnumerateArray()
        |> Seq.toList

    check (tags.Length > 100) "full HTML tag map must be generated"

    check
        (tags
         |> List.exists (fun tag ->
             tag.GetProperty("tag").GetString() = "br"
             && tag.GetProperty("isVoid").GetBoolean()))
        "br is a void tag in coverage"

    check
        ([
            "div"
            "button"
            "input"
            "select"
            "option"
            "textarea"
            "img"
            "br"
            "article"
         ]
         |> List.forall (fun tag -> text.Contains ("let ``" + tag + "`` ")))
        "every listed tag has a constructor"

    check
        (text.Contains "let onClick "
         && text.Contains "Dom.MouseEvent -> unit")
        "let onClick and Dom.MouseEvent -> unit in text"

    check (sameBytes coverageFile (Path.ChangeExtension (second, ".coverage.json"))) "coverage must be byte-identical"

    check
        (coverage.GetProperty("omissions").EnumerateArray()
         |> Seq.exists (fun item -> item.GetProperty("reason").GetString().Contains "unsupported type"))
        "an omission names an unsupported type"

    check (coverage.GetProperty("attributeCount").GetInt32() = 34) "attributeCount = 34"
    check (coverage.GetProperty("authoringHelperCount").GetInt32() = 14) "authoringHelperCount = 14"

    check (not (String.IsNullOrEmpty (coverage.GetProperty("authoringPolicySha256").GetString()))) "authoringPolicySha256 is set"

    check (not (String.IsNullOrEmpty (coverage.GetProperty("attributePolicySha256").GetString()))) "attributePolicySha256 is set"

    check
        (text.Contains "module Attrs ="
         && text.Contains "let classReactive")
        "module Attrs and let classReactive in text"

    check (text.Contains "Typed.reactiveBooleanAttribute \"disabled\"") "Typed.reactiveBooleanAttribute \"disabled\" in text"

    check (text.Contains "Typed.reactiveAriaBooleanAttribute \"aria-expanded\"") "Typed.reactiveAriaBooleanAttribute \"aria-expanded\" in text"

    check
        (not (text.Contains "let innerHTMLStatic")
         && not (text.Contains "let outerHTMLStatic"))
        "innerHTMLStatic and outerHTMLStatic not in text"

    let tampered = Path.Combine (folder, "tampered")
    copyDirectory (Path.Combine (root, "bin/Release/net10.0/xantham")) (Path.Combine (tampered, "xantham"))
    let payload = Path.Combine (tampered, "xantham/declarations.json.br")
    let data = File.ReadAllBytes payload
    data[data.Length - 1] <- data[data.Length - 1] ^^^ 1uy
    File.WriteAllBytes (payload, data)
    let code, stdout, stderr = generate [ "--no-build"; "--"; first; tampered ]

    check
        (code <> 0
         && stderr.Contains "Catalogue hash mismatch")
        (stdout + stderr)

    check (sameBytes first second) "rejection must preserve existing output"
    let policyPath = Path.Combine (root, "attributes.json")
    let originalPolicy = File.ReadAllBytes policyPath

    try
        for invalidName in [ ""; "1title"; "-title"; "title١" ] do
            let policy = JsonNode.Parse (Text.Encoding.UTF8.GetString originalPolicy)
            policy.["groups"].[0].["entries"].[0].["name"] <- JsonValue.Create invalidName
            File.WriteAllText (policyPath, policy.ToJsonString ())
            let code, stdout, stderr = generate [ "--no-build"; "--"; first ]

            check
                (code <> 0
                 && stderr.Contains "Invalid attribute policy name")
                (stdout + stderr)

            check (sameBytes first second) "invalid policy must preserve existing output"
    finally
        File.WriteAllBytes (policyPath, originalPolicy)

    let authoringPath = Path.Combine (root, "authoring.json")
    let originalAuthoring = File.ReadAllBytes authoringPath

    try
        for name in [ "files"; "form"; "missingMember" ] do
            let policy = JsonNode.Parse (Text.Encoding.UTF8.GetString originalAuthoring)
            policy.["properties"].[1].["members"] <- JsonArray (JsonValue.Create name)
            File.WriteAllText (authoringPath, policy.ToJsonString ())
            let code, _, _ = generate [ "--no-build"; "--"; first ]
            check (code <> 0) "unsafe, readonly and missing properties must be rejected"
            check (sameBytes first second) "invalid authoring policy must preserve existing output"
    finally
        File.WriteAllBytes (authoringPath, originalAuthoring)

    let template = Path.Combine (folder, "firstTemplate.fs")
    let templateSecond = Path.Combine (folder, "secondTemplate.fs")
    check (sameBytes template templateSecond) "template catalogue must be byte-identical"
    let ttext = readText template
    check (ttext.StartsWith "// <auto-generated>") "template output starts with // <auto-generated>"

    check (ttext.Contains "[<AutoOpen>]\nmodule Ranvier.View.Foundation.HtmlTemplate\n") "template output declares the AutoOpen HtmlTemplate module"

    check (coverage.GetProperty("templateTagCount").GetInt32() = 112) "templateTagCount = 112"

    let entries =
        jsonArray (JsonNode.Parse(readText policyPath).["groups"])
        |> List.collect (fun group -> jsonArray group["entries"])

    check (coverage.GetProperty("templateAttributeCount").GetInt32() = entries.Length) "templateAttributeCount = policy entries"
    let overloads = Map [ "string", 6; "boolean", 3; "ariaBoolean", 6 ]
    let dataAria = 2 * 3

    let attributeParts =
        (matches @"TemplateParts\.(?:attribute|optionalAttribute|readAttribute|sourceAttribute)\b" ttext).Length

    check
        (attributeParts =
            (entries
             |> List.sumBy (fun entry -> overloads[entry["kind"].GetValue<string>()]))
            + dataAria)
        "string and ARIA attributes need six overloads, booleans three"

    let childReplacing =
        set
            [
                "HTMLAnchorElement", "text"
                "HTMLOptionElement", "text"
                "HTMLTitleElement", "text"
                "HTMLScriptElement", "text"
                "HTMLOutputElement", "value"
                "HTMLOutputElement", "defaultValue"
                "HTMLTextAreaElement", "defaultValue"
            ]

    let templateOmissions =
        coverage.GetProperty("templateOmissions").EnumerateArray()
        |> Seq.filter (fun item -> item.GetProperty("reason").GetString() = "setter replaces child nodes")
        |> Seq.map (fun item -> item.GetProperty("owner").GetString(), item.GetProperty("memberName").GetString())
        |> set

    check (templateOmissions = childReplacing) "child-replacing setters are template coverage omissions"

    for owner, name in childReplacing do
        check
            (not (
                ttext.Contains (
                    "member _.``"
                    + name
                    + "``<'native when 'native :> Dom."
                    + owner
                    + ">"
                )
            ))
            ("template omits child-replacing setter "
             + owner
             + "."
             + name)

        check
            (text.Contains (
                "(node :> Dom."
                + owner
                + ").``"
                + name
                + "`` <- value"
            ))
            ("Html.fs keeps setter " + owner + "." + name)

    check (not (ttext.Contains "type Groups.TitleProperties<'inst> with")) "no TitleProperties group"

    check
        (ttext.Contains "member _.``value``<'native when 'native :> Dom.HTMLTextAreaElement>(value: string)")
        "value stays on owners whose setter keeps child nodes"

    let templatePropertyCount = coverage.GetProperty("templatePropertyCount").GetInt32()

    check
        (templatePropertyCount =
            count "Typed.staticProperty (fun (node: 'native)" text
            - childReplacing.Count)
        "every shared settable property, except child-replacing setters, needs a template part"

    for fn in [ "property"; "readProperty"; "sourceProperty" ] do
        check
            ((matches (@"TemplateParts\." + fn + @"\b") ttext).Length = templatePropertyCount)
            "every property needs value, reader and source overloads"

    check
        (coverage.GetProperty("templateEventCount").GetInt32() = (matches @"TemplateParts\.event\b" ttext).Length)
        "templateEventCount = TemplateParts.event calls"

    check
        (ttext.Contains "type Groups.TextAreaAttributes<'inst> with"
         && ttext.Contains "member _.TextArea: Groups.TextAreaAttributes<'inst>")
        "TextAreaAttributes group and its member"

    check (not (ttext.Contains "Textarea")) "template attribute groups take the native type's group name"
    check (text.Contains "    module Textarea =") "Html.Attrs keeps the policy module names"

    check
        (ttext.Contains "member _.``div`` (parts: Part<Dom.HTMLDivElement, 'inst> array) (children: TemplateView<'inst> array)")
        "template div member"

    check (ttext.Contains "member _.``br``(parts: Part<Dom.HTMLBRElement, 'inst> array) : TemplateView<'inst>") "template br member"

    check (ttext.Contains "type Groups.InputProperties<'inst> with") "InputProperties group"

    check
        (ttext.Contains "member _.onClick<'native when 'native :> Dom.Element>(handler: 'inst -> Dom.PointerEvent -> unit)")
        "template onClick member"

    check
        (ttext.Contains "TemplateParts.readAttribute \"aria-expanded\" (fun instance -> Some (TemplateParts.ariaText (read instance)))")
        "template aria-expanded reader"

    // The formatter wraps a property call over 150 columns onto one line per argument.
    let flat = flatten ttext

    check
        (flat.Contains "TemplateParts.readProperty (PropertyHtml.text \"class\")")
        "TemplateParts.readProperty (PropertyHtml.text \"class\") in flat"

    check
        (flat.Contains
            "TemplateParts.readProperty PropertyHtml.childText (fun (node: 'native) (value: string) -> (node :> Dom.HTMLTextAreaElement).``value`` <- value) read")
        "textarea value childText reader in flat"

    let sugar = Path.Combine (folder, "firstTemplateSugar.fs")
    let sugarSecond = Path.Combine (folder, "secondTemplateSugar.fs")
    check (sameBytes sugar sugarSecond) "template sugar must be byte-identical"
    let stext = readText sugar
    check (stext.StartsWith "// <auto-generated>") "sugar output starts with // <auto-generated>"

    check
        (stext.Contains "namespace Ranvier.View.Foundation.Sugar\n"
         && stext.Contains "module Elements =")
        "sugar namespace and Elements module"

    let sflat = flatten stext

    let explicitElements =
        matches
            @"member _\.``([a-z0-9]+)`` ?\(parts: Part<(Dom\.\w+), 'inst> array\)( \(children: TemplateView<'inst> array\))? : TemplateView<'inst>"
            flat
        |> List.map (fun m -> group 1 m, (group 2 m, group 3 m = ""))
        |> Map.ofList

    check (coverage.GetProperty("templateTagClassCount").GetInt32() = 112) "templateTagClassCount = 112"

    let classes =
        matches
            @"type (\w+'?)<'i> private \(ps: Part<(Dom\.\w+), 'i> array, primary: unit\) = inherit (Element<(Dom\.\w+), 'i>|VoidElement<'i>)"
            sflat
        |> List.map (fun m -> group 1 m, group 2 m, group 3 m, group 4 m)

    check (classes.Length = 112) "every HTML tag needs one tag class"
    check (count "new([<ParamArray>] ps: Part<" sflat = 112) "every tag class needs a public ParamArray constructor"

    check
        ((classes
          |> List.map (fun (name, _, _, _) -> name)
          |> List.filter _.EndsWith("'")
          |> set)
            =
            set [ "base'"; "option'" ])
        "exactly base and option take an apostrophe"

    let sugaredElements =
        classes
        |> List.map (fun (name, native, baseClass, _) -> name.TrimEnd '\'', (native, baseClass = "VoidElement<'i>"))
        |> Map.ofList

    check
        (sugaredElements = explicitElements)
        $"tag classes must match HtmlTemplate.fs in native type and void form: %A{Set.difference
                                                                                      (set (Map.toList sugaredElements)
                                                                                       + set (Map.toList explicitElements))
                                                                                      (Set.intersect (set (Map.toList sugaredElements)) (set (Map.toList explicitElements)))}"

    check
        (explicitElements.Count = 112
         && sugaredElements.Count = 112)
        "112 explicit elements and 112 tag classes"

    for name, native, _, elementNative in classes do
        if elementNative <> "" then
            check
                (elementNative = native)
                (name
                 + ": Element's native type must equal the parts'")

    // Compares with whitespace removed.
    let squeeze (text: string) =
        Regex.Replace (text, @"\s+", "")

    let squeezed = squeeze stext

    check
        (squeezed.Contains (
            squeeze "type div<'i> private (ps: Part<Dom.HTMLDivElement, 'i> array, primary: unit) = inherit Element<Dom.HTMLDivElement, 'i>(Div, ps)"
        ))
        "div tag class"

    check
        (squeezed.Contains (
            squeeze
                "type br<'i> private (ps: Part<Dom.HTMLBRElement, 'i> array, primary: unit) = inherit VoidElement<'i>(TemplateParts.element (HtmlTag \"br\") ps [||])"
        ))
        "br tag class"

    let handwrittenSugar =
        readText (Path.Combine (root, "../../experiments/Ranvier.View.Portable/TemplateSugar.fs"))

    let names pattern =
        [
            for m in Regex.Matches (handwrittenSugar, pattern, RegexOptions.Multiline) -> m.Groups[1].Value
        ]
        |> set

    let freeNames =
        names @"^    let (?:inline )?(?:internal )?(\w+)"
        + names @"^    type (?:private )?(\w+)"
        + names @"^    module (\w+)"

    let collidingClasses =
        Set.intersect
            (classes
             |> List.map (fun (name, _, _, _) -> name)
             |> set)
            freeNames

    check collidingClasses.IsEmpty $"a tag class would collide with a Builders name %A{Set.toList collidingClasses}"

    let handwritten =
        readText (Path.Combine (root, "../../experiments/Ranvier.View.Portable/Template.fs"))

    let builderMembers =
        [
            for builder in [ "TemplateBuilder"; "AttributeBuilder"; "PropertyBuilder"; "EventBuilder" ] do
                let body =
                    Regex.Match (handwritten, "^type " + builder + @"<'inst>.*?(?=^[^\s#])", RegexOptions.Multiline ||| RegexOptions.Singleline)

                check body.Success (builder + " must be declared in Template.fs")

                builder,
                [
                    for m in Regex.Matches (body.Value, @"^    member (?:_|this)\.(\w+)", RegexOptions.Multiline) -> m.Groups[1].Value
                ]
                |> set
        ]
        |> Map.ofList

    let generator = readText (Path.Combine (root, "Program.fs"))

    let reservedBlock =
        let after =
            generator.Substring (
                generator.IndexOf "    let reserved ="
                + "    let reserved =".Length
            )

        let stop = after.IndexOf "    let authoringRenames"
        if stop < 0 then after else after.Substring (0, stop)

    let reserved =
        [
            for m in Regex.Matches (reservedBlock, @"""(\w+Builder)"",\s*set\s*\[(.*?)\]", RegexOptions.Singleline) ->
                m.Groups[1].Value,
                matches @"""(\w+)""" m.Groups[2].Value
                |> List.map (group 1)
                |> set
        ]
        |> Map.ofList

    let exempt =
        Map.empty
        |> Map.add "AttributeBuilder" (set [ "data"; "aria" ])

    let keys (map: Map<string, _>) =
        map |> Map.keys |> set

    check (keys reserved = keys builderMembers) $"%A{reserved}"

    for KeyValue (builder, members) in builderMembers do
        check (not members.IsEmpty) (builder + " must declare handwritten members")

        let expected =
            reserved[builder]
            - (exempt
               |> Map.tryFind builder
               |> Option.defaultValue Set.empty)

        check
            (expected = members)
            $"{builder}: reserved names must equal handwritten members %A{Set.toList (Set.difference (members + expected) (Set.intersect members expected))}"

    let policyRoot = Path.Combine (folder, "policy")
    Directory.CreateDirectory policyRoot |> ignore
    File.Copy (Path.Combine (root, "authoring.json"), Path.Combine (policyRoot, "authoring.json"))

    let entry (helper: string) (name: string) (kind: string) (memberName: string) =
        JsonObject (
            [
                Collections.Generic.KeyValuePair<string, JsonNode>("helper", JsonValue.Create helper)
                Collections.Generic.KeyValuePair<string, JsonNode>("name", JsonValue.Create name)
                Collections.Generic.KeyValuePair<string, JsonNode>("kind", JsonValue.Create kind)
                Collections.Generic.KeyValuePair<string, JsonNode>("member", JsonValue.Create memberName)
            ]
        )

    let firstEntries (groups: JsonArray) =
        groups.[0].["entries"].AsArray()

    let collisions: (string * (JsonArray -> unit) * string) list =
        [
            "reserved flat member",
            (fun groups -> (firstEntries groups).Add(entry "attribute" "data" "string" "id")),
            "Template member takes a reserved name: AttributeBuilder.attribute"
            "reserved prefix member",
            (fun groups -> (firstEntries groups).Add(entry "data" "data" "string" "id")),
            "Template member takes a reserved name: AttributeBuilder.data"
            "merged group member",
            (fun (groups: JsonArray) ->
                let extra = JsonObject ()
                extra["module"] <- JsonValue.Create "TextAreaExtra"
                extra["native"] <- JsonValue.Create "HTMLTextAreaElement"
                extra["entries"] <- JsonArray (entry "disabled" "disabled" "boolean" "disabled")
                groups.Add extra),
            "Duplicate template member: Groups.TextAreaAttributes.disabled"
        ]

    for name, mutate, message in collisions do
        let policy = JsonNode.Parse (readText policyPath)
        mutate (policy["groups"].AsArray())
        File.WriteAllText (Path.Combine (policyRoot, "attributes.json"), policy.ToJsonString ())

        let code, stdout, stderr =
            generate
                [
                    "--no-build"
                    "--"
                    first
                    Path.Combine (root, "bin/Release/net10.0")
                    policyRoot
                ]

        check (code <> 0 && stderr.Contains message) (name + ": " + stdout + stderr)

        check
            (sameBytes first second
             && sameBytes template templateSecond)
            (name + " must preserve existing output")

let folder =
    Directory.CreateDirectory (
        Path.Combine (
            Path.GetTempPath (),
            "ranvier-generator-tests-"
            + Path.GetRandomFileName ()
        )
    )

let outcome =
    try
        try
            run folder.FullName
            None
        with
        | CheckFailed message -> Some message
        | error -> Some (string error)
    finally
        try
            folder.Delete true
        with _ ->
            ()

match outcome with
| None -> printfn "PASS: deterministic full HTML output and coverage, typed native members/events, policy exclusions, corrupt catalogue rejection"
| Some message ->
    eprintfn "FAIL %s" message
    exit 1
