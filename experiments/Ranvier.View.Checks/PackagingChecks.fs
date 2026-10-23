/// <summary>Checks that Ranvier.View compiles without a browser.</summary>
module PackagingChecks

open System.IO
open System.Text.RegularExpressions
open System.Xml.Linq

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let private portable =
    Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, "../../src/Ranvier.View"))

let private project () =
    XDocument.Load (Path.Combine (portable, "Ranvier.View.fsproj"))

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
    let expectedProjects = set [ "../Ranvier/Ranvier.fsproj" ]

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
                // Native.fs is checked by nativeStructure.
                if file <> "Native.fs" then
                    let text =
                        File.ReadAllLines (Path.Combine (portable, file))
                        |> Array.filter (fun line -> not (line.TrimStart().StartsWith "//"))
                        |> String.concat "\n"

                    for found in browserAttribute.Matches text do
                        if touchesBrowser found then
                            file + ": " + found.Value
        ]

    equal [] offending

let private fableBlock =
    Regex (
        @"^[ \t]*#if FABLE_COMPILER[ \t]*\r?$(?<fable>.*?)^[ \t]*#else[ \t]*\r?$(?<net>.*?)^[ \t]*#endif",
        RegexOptions.Singleline ||| RegexOptions.Multiline
    )

let private topLevelLet =
    Regex (@"^let (?:inline )?(?:private )?(?:internal )?(?:rec )?(\w+)", RegexOptions.Multiline)

/// <summary>
/// The violations of Native.fs's shape in <c>text</c>: each <c>#if FABLE_COMPILER</c> block has an <c>#else</c> that
/// defines the same top-level names, every attribute is an Emit inside a Fable branch, and every guard naming
/// <c>FABLE_COMPILER</c> is <c>#if FABLE_COMPILER</c> or <c>#if !FABLE_COMPILER</c>.
/// </summary>
let nativeStructure (text: string) : string list =
    let text =
        text.Split '\n'
        |> Array.filter (fun line -> not (line.TrimStart().StartsWith "//"))
        |> String.concat "\n"

    let blocks = [ for found in fableBlock.Matches text -> found ]

    let names (group: Group) =
        set [ for found in topLevelLet.Matches group.Value -> found.Groups[1].Value ]

    let inFable (index: int) =
        blocks
        |> List.exists (fun block ->
            let fable = block.Groups["fable"]

            index >= fable.Index
            && index < fable.Index + fable.Length)

    [
        let guards =
            text.Split '\n'
            |> Array.map _.Trim()
            |> Array.filter (fun line ->
                line.StartsWith "#if"
                && line.Contains "FABLE_COMPILER")

        for guard in guards do
            if
                guard <> "#if FABLE_COMPILER"
                && guard <> "#if !FABLE_COMPILER"
            then
                "unsupported guard: " + guard

        let opened =
            guards
            |> Array.filter ((=) "#if FABLE_COMPILER")
            |> Array.length

        if opened <> blocks.Length then
            $"{opened - blocks.Length} #if FABLE_COMPILER block(s) without an #else"

        for block in blocks do
            let fable, net = block.Groups["fable"], block.Groups["net"]

            if
                fable.Value.Contains "#if"
                || fable.Value.Contains "#endif"
            then
                "a #if FABLE_COMPILER block without an #else before the next directive"
            elif names fable <> names net then
                $"Fable names %A{names fable} differ from .NET names %A{names net}"

        for found in browserAttribute.Matches text do
            if found.Groups[1].Value <> "Emit" then
                "attribute other than Emit: " + found.Value
            elif not (inFable found.Index) then
                "Emit outside a #if FABLE_COMPILER block: "
                + found.Value
    ]

let private nativeBodies () =
    equal [] (nativeStructure (File.ReadAllText (Path.Combine (portable, "Native.fs"))))

let private nativeStructureRejects () =
    let guarded =
        "#if FABLE_COMPILER\n[<Emit(\"document\")>]\nlet document () : Document = jsNative\n#else\nlet document () : Document = host ()\n#endif\n"

    equal [] (nativeStructure guarded)

    let unguarded =
        guarded
        + "[<Emit(\"document.body\")>]\nlet body () : HTMLElement = jsNative\n"

    equal 1 (nativeStructure unguarded).Length

    let missing =
        "#if FABLE_COMPILER\n[<Emit(\"$0.a\")>]\nlet a x = jsNative\n[<Emit(\"$0.b\")>]\nlet b x = jsNative\n#else\nlet a x = x\n#endif\n"

    equal 1 (nativeStructure missing).Length

    let noElse =
        "#if FABLE_COMPILER\n[<Emit(\"$0.a\")>]\nlet a x = jsNative\n#endif\n"
        + guarded

    equal true ((nativeStructure noElse).Length >= 1)

    equal 1 (nativeStructure "#if FABLE_COMPILER && X\nlet a = 1\n#endif\n").Length

/// <summary>
/// The Fable branches of <c>lines</c> through nested directives: for each line, whether it lies in the Fable branch of a
/// <c>#if FABLE_COMPILER</c> block or in the <c>#else</c> of a <c>#if !FABLE_COMPILER</c> block, and, for a line in a
/// <c>#if FABLE_COMPILER</c> block without an <c>#else</c>, that block's first line.
/// </summary>
let private fableBranches (lines: string array) : bool array * int array =
    let inFable = Array.zeroCreate<bool> lines.Length
    let elselessBlock = Array.create lines.Length -1
    // The open directives, innermost first: (kind, start, in the #else part). Kind 1: FABLE_COMPILER, -1: its
    // negation, 0: any other symbol.
    let mutable frames: (int * int * bool) list = []
    let pending = ResizeArray<int * int>()
    let withoutElse = System.Collections.Generic.HashSet<int>()

    for i in 0 .. lines.Length - 1 do
        let trimmed = lines[i].Trim()

        if trimmed.StartsWith "#if" then
            let kind =
                match trimmed with
                | "#if FABLE_COMPILER" -> 1
                | "#if !FABLE_COMPILER" -> -1
                | _ -> 0

            frames <- (kind, i, false) :: frames
        elif trimmed = "#else" then
            match frames with
            | (kind, start, _) :: rest -> frames <- (kind, start, true) :: rest
            | [] -> ()
        elif trimmed = "#endif" then
            match frames with
            | (1, start, false) :: rest ->
                withoutElse.Add start |> ignore
                frames <- rest
            | _ :: rest -> frames <- rest
            | [] -> ()
        else
            let fable =
                frames
                |> List.tryFind (fun (kind, _, inElse) -> (kind = 1 && not inElse) || (kind = -1 && inElse))

            match fable with
            | Some (kind, start, _) ->
                inFable[i] <- true

                if kind = 1 then
                    pending.Add ((i, start))
            | None -> ()

    for line, start in pending do
        if withoutElse.Contains start then
            elselessBlock[line] <- start

    inFable, elselessBlock

let private moduleName (text: string) =
    let found =
        Regex.Match (text, @"^module (?:internal )?(?:rec )?([\w.]+)", RegexOptions.Multiline)

    if found.Success then
        found.Groups[1].Value.Split('.') |> Array.last
    else
        ""

/// <summary>
/// The violations of the Emit rule across <c>files</c> (name and text): an Emit attribute or <c>emitJsExpr</c> call
/// outside a Fable branch, and a name declared in a Fable branch without an <c>#else</c> that is used outside every Fable
/// branch, bare in its own file or qualified by its module in any file, where .NET would reach its Fable-only body.
/// </summary>
let unguardedEmitsAcross (files: (string * string) list) : string list =
    let emit = Regex (@"\[<\s*(?:[\w.]*\.)?Emit\b|emitJsExpr")

    let declared =
        Regex (@"^\s*let (?:inline )?(?:private )?(?:internal )?(?:rec )?(\w+)")

    let nestedModule = Regex (@"^(\s*)module (?:internal |private )?(\w+)\s*=")

    // Each file with its lines, Fable branches and Fable-only names; each name with the modules that own it: the
    // file's module and the nested module it is declared in.
    let parsed =
        [
            for file, text in files ->
                let lines = text.Replace("\r", "").Split '\n'
                let inFable, elseless = fableBranches lines
                let fileModule = moduleName text

                let fableOnly =
                    [
                        for i in 0 .. lines.Length - 1 do
                            if elseless[i] >= 0 then
                                let found = declared.Match lines[i]

                                if found.Success then
                                    let indent = lines[i].Length - lines[i].TrimStart().Length

                                    let nested =
                                        [ 0 .. i - 1 ]
                                        |> List.rev
                                        |> List.tryPick (fun k ->
                                            let m = nestedModule.Match lines[k]

                                            if m.Success && m.Groups[1].Length < indent then
                                                Some m.Groups[2].Value
                                            else
                                                None)

                                    found.Groups[1].Value,
                                    [ fileModule; yield! Option.toList nested ]
                                    |> List.filter ((<>) "")
                    ]

                file, lines, inFable, fableOnly
        ]

    let owned =
        [
            for _, _, _, names in parsed do
                for name, owners in names do
                    for owner in owners do
                        owner, name
        ]

    let violation prefix name (line: string) =
        $"{prefix}{name} is declared in a Fable branch without #else and used here: {line.Trim ()}"

    [
        for file, lines, inFable, fableOnly in parsed do
            let prefix = if files.Length > 1 then file + ": " else ""
            let text = String.concat "\n" lines

            // Modules this file opens: their Fable-only names are reachable bare.
            let opened =
                set
                    [
                        for m in Regex.Matches (text, @"^\s*open (?:global\.)?([\w.]+)", RegexOptions.Multiline) ->
                            m.Groups[1].Value.Split('.') |> Array.last
                    ]

            let bare =
                [
                    for name, _ in fableOnly do
                        yield name
                    for owner, name in owned do
                        if opened.Contains owner then
                            yield name
                ]
                |> List.distinct

            for i in 0 .. lines.Length - 1 do
                let line = lines[i]

                if
                    not (line.TrimStart().StartsWith "//")
                    && not inFable[i]
                then
                    if emit.IsMatch line then
                        prefix + line.Trim ()

                    for name in bare do
                        if Regex.IsMatch (line, @"(?<![\w.])" + Regex.Escape name + @"\b") then
                            violation prefix name line

                    // Qualified by its module, with or without the namespace in front.
                    for owner, name in owned do
                        if
                            Regex.IsMatch (
                                line,
                                @"(?<![\w.])(?:\w+\.)*"
                                + Regex.Escape (owner + "." + name)
                                + @"\b"
                            )
                        then
                            violation prefix (owner + "." + name) line
    ]
    |> List.distinct

/// <summary>The violations of the Emit rule in one file's <c>text</c>; see <c>unguardedEmitsAcross</c>.</summary>
let unguardedEmits (text: string) : string list =
    unguardedEmitsAcross [ "", text ]

let private guardedEmits () =
    let files =
        [
            for file in compiled () do
                if file <> "Native.fs" then
                    file, File.ReadAllText (Path.Combine (portable, file))
        ]

    equal [] (unguardedEmitsAcross files)

let private guardedEmitsRejects () =
    equal [] (unguardedEmits "#if FABLE_COMPILER\n[<Emit(\"$0.a\")>]\nlet a x = jsNative\n#else\nlet a x = x\n#endif\n")
    equal 1 (unguardedEmits "let seam: Seam = Fable.Core.JsInterop.emitJsExpr () \"{}\"\n").Length
    equal 1 (unguardedEmits "[<Emit(\"setTimeout($0, $1)\")>]\nlet startTimer a b = jsNative\n").Length
    equal 1 (unguardedEmits "#if FABLE_COMPILER\n[<Emit(\"$0.a\")>]\nlet a x = jsNative\n#endif\nlet b = a 1\n").Length

    equal
        []
        (unguardedEmits
            "#if FABLE_COMPILER\n[<Emit(\"$0.a\")>]\nlet a x = jsNative\n#endif\n#if FABLE_COMPILER\nlet b = a 1\n#else\nlet b = 1\n#endif\n")

    // A nested block inside the Fable branch does not end it.
    equal [] (unguardedEmits "#if FABLE_COMPILER\n#if X\nlet y = 1\n#endif\n[<Emit(\"$0.a\")>]\nlet a x = jsNative\n#else\nlet a x = x\n#endif\n")

    // The #else of #if !FABLE_COMPILER is a Fable branch.
    equal [] (unguardedEmits "#if !FABLE_COMPILER\nlet a x = x\n#else\n[<Emit(\"$0.a\")>]\nlet a x = jsNative\n#endif\n")
    equal 1 (unguardedEmits "#if !FABLE_COMPILER\n[<Emit(\"$0.a\")>]\nlet a x = jsNative\n#endif\n").Length

    // A Fable-only helper used qualified from another file.
    let helper =
        "module Helpers\n#if FABLE_COMPILER\n[<Emit(\"$0.a\")>]\nlet a x = jsNative\n#endif\n"

    equal 1 (unguardedEmitsAcross [ "Helpers.fs", helper; "Uses.fs", "module Uses\nlet b = Helpers.a 1\n" ]).Length

    equal
        []
        (unguardedEmitsAcross
            [
                "Helpers.fs", helper
                "Uses.fs", "module Uses\n#if FABLE_COMPILER\nlet b = Helpers.a 1\n#else\nlet b = 1\n#endif\n"
            ])

let private guardedEmitsReach () =
    let helper =
        "module Helpers\n#if FABLE_COMPILER\n[<Emit(\"$0.a\")>]\nlet a x = jsNative\n#endif\n"

    // Through an open, then a bare name.
    equal 1 (unguardedEmitsAcross [ "Helpers.fs", helper; "Uses.fs", "module Uses\nopen Helpers\nlet b = a 1\n" ]).Length

    // Fully qualified with the namespace.
    equal
        1
        (unguardedEmitsAcross
            [
                "Helpers.fs", helper
                "Uses.fs", "module Uses\nlet b = Ranvier.View.Portable.Helpers.a 1\n"
            ])
            .Length

    // Declared in a nested module and qualified by it.
    let nested =
        "module Outer\nmodule Inner =\n#if FABLE_COMPILER\n    [<Emit(\"$0.a\")>]\n    let a x = jsNative\n#endif\n    let z = 1\n"

    equal 1 (unguardedEmitsAcross [ "Outer.fs", nested; "Uses.fs", "module Uses\nlet b = Inner.a 1\n" ]).Length
    equal [] (unguardedEmitsAcross [ "Outer.fs", nested; "Uses.fs", "module Uses\nlet b = Inner.z\n" ])

let cases: (string * (unit -> unit)) array =
    [|
        "the portable project references only src/Ranvier and the Xantham type bindings", browserReferences
        "the portable project's attributes use no Import or Global, and no Emit outside Native.fs touches document, window or addEventListener",
        browserEmits
        "every Emit of Native.fs sits in a #if FABLE_COMPILER block whose #else defines the same names", nativeBodies
        "the Native.fs shape check rejects an unguarded Emit, a missing .NET body, a block without #else and other guards", nativeStructureRejects
        "every Emit and emitJsExpr of the portable files other than Native.fs sits in a Fable branch, and Fable-only helpers stay in Fable branches",
        guardedEmits
        "the guarded-Emit check rejects an unguarded emitJsExpr, an unguarded Emit and a Fable-only helper used on .NET", guardedEmitsRejects
        "the guarded-Emit check follows a Fable-only helper through an open, a namespace and a nested module", guardedEmitsReach
    |]
