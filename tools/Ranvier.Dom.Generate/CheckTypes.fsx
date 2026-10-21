// Compiles valid generated usage and rejects incompatible or readonly examples.
// Usage: dotnet fsi tools/Ranvier.Dom.Generate/CheckTypes.fsx [name-substring ...]
// With arguments, runs only the cases whose name contains one of them.
open System
open System.Diagnostics
open System.IO
open System.Text.RegularExpressions

type Case =
    {
        Name: string
        Body: string
        /// The single FS error code the snippet must fail with; None when it must compile.
        Expected: string option
        /// Opens the free sugar instead of Generated.Html.Authoring.
        Sugar: bool
    }

let root = Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, "..", ".."))

let project =
    Path.Combine(root, "examples/Fable.Ranvier.Playground/tests/DomTests.fsproj").Replace('\\', '/')

let prefix =
    """module GeneratedTypeCheck
open Fable.Core.TS
open Ranvier.View
module H = Ranvier.View.Dom.Generated.Html
open Ranvier.View.Dom.Generated.Html.Authoring
open Ranvier.View.Template
"""

// The free sugar is opened without Generated.Html.Authoring (spec §4 scope rule).
let sugarPrefix =
    """module GeneratedTypeCheck
open Fable.Core.TS
open Ranvier.View
open Ranvier.View.Sugar
"""

let generatedSugar =
    Regex.Replace (File.ReadAllText (Path.Combine (root, "src/Ranvier.View/Generated/HtmlTemplateSugar.fs")), @"\s+", " ")

let pairs pattern =
    [
        for m in Regex.Matches (generatedSugar, pattern) -> m.Groups[1].Value, m.Groups[2].Value
    ]

let fail (message: string) =
    eprintfn "FAIL %s" message
    exit 1

let tagClasses =
    pairs @"type (\w+'?)<'i> private \(ps: Part<[^>]*>[^)]*\) = inherit (Element|VoidElement)"

if tagClasses.Length <> 112 then
    fail "the generated sugar file must declare a class for every HTML tag"

let allTagClasses =
    tagClasses
    |> List.map (fun (name, kind) ->
        "            "
        + name
        + (if kind = "Element" then "() { }" else "()"))
    |> String.concat "\n"

let allTagClassesBody =
    """
let view =
    Component.template (fun _ () -> ()) (
        article (Attrs.className "a") {
"""
    + allTagClasses
    + """
        })
"""

let case name body expected =
    {
        Name = name
        Body = body
        Expected = expected
        Sugar = false
    }

let free name body expected =
    { case name body expected with
        Sugar = true
    }

let cases =
    [
        case
            "valid helpers"
            "let view = H.div Tree.ops [| H.Properties.HTMLElement.titleStatic \"shared\"; H.Events.onClick (fun (_: Dom.PointerEvent) -> ()) |] [| H.input Tree.ops [| H.Input.checkedStatic true |]; H.select Tree.ops [||] [| H.option Tree.ops [||] [||] |]; H.br Tree.ops [||] |]"
            None
        case "generic modifiers reject lists" "let view = H.div Tree.ops [] [||]" (Some "FS0001")
        case "generic children reject lists" "let view = H.div Tree.ops [||] []" (Some "FS0001")
        case "bound modifiers reject lists" "let view = H.Program.div [] [||]" (Some "FS0001")
        case "bound children reject lists" "let view = H.Program.div [||] []" (Some "FS0001")
        case "input modifier on div" "let view = H.div Tree.ops [| H.Input.checkedStatic true |] [||]" (Some "FS0001")
        case "wrong property value" "let modifier = H.Input.checkedStatic \"true\"" (Some "FS0001")
        case "readonly native property" "let write (node: Dom.HTMLInputElement) = node.form <- None" (Some "FS0810")
        case "files setter excluded by policy" "let modifier = H.Input.filesStatic None" (Some "FS0039")
        case "void element rejects children" "let view = H.br Tree.ops [||] [| Tree.ops.Text \"child\" |]" (Some "FS0001")
        case "wrong expanded property value" "let modifier = H.Textarea.rowsStatic \"3\"" (Some "FS0001")
        case "DOM replacement setter excluded" "let modifier = H.Div.innerHTMLStatic \"markup\"" (Some "FS0039")
        case "outerHTML setter excluded" "let modifier = H.Div.outerHTMLStatic \"markup\"" (Some "FS0039")
        case "textContent setter excluded" "let modifier = H.Div.textContentStatic \"text\"" (Some "FS0039")
        case "innerText setter excluded" "let modifier = H.Div.innerTextStatic \"text\"" (Some "FS0039")
        case "outerText setter excluded" "let modifier = H.Div.outerTextStatic \"text\"" (Some "FS0039")
        case "srcdoc setter excluded" "let modifier = H.Iframe.srcdocStatic \"markup\"" (Some "FS0039")
        case
            "template raw HTML takes UnsafeHtml only"
            """
let view = Component.define (fun _ () -> ()) (fun t -> t.div [| t.Unsafe.html "<b>x</b>" |] [||])
"""
            (Some "FS0041")
        case
            "excluded setters' getters and sibling setters exist in their declaring modules"
            """
let readers: (Dom.HTMLIFrameElement -> string) list =
    [ H.Properties.Element.innerHTML; H.Properties.Element.outerHTML; H.Properties.Element.textContent; H.Properties.HTMLElement.innerText; H.Properties.HTMLElement.outerText; H.Properties.HTMLIFrameElement.srcdoc; H.Iframe.srcdoc ]
let modifiers: Typed.Modifier<Dom.HTMLIFrameElement> list =
    [ H.Properties.Element.idStatic "x"; H.Properties.HTMLElement.titleStatic "x"; H.Properties.HTMLIFrameElement.srcStatic "x"; H.Iframe.srcStatic "x" ]
"""
            None
        case "Element innerHTML setter excluded" "let modifier = H.Properties.Element.innerHTMLStatic \"markup\"" (Some "FS0039")
        case "Element outerHTML setter excluded" "let modifier = H.Properties.Element.outerHTMLStatic \"markup\"" (Some "FS0039")
        case "Element textContent setter excluded" "let modifier = H.Properties.Element.textContentStatic \"text\"" (Some "FS0039")
        case "HTMLElement innerText setter excluded" "let modifier = H.Properties.HTMLElement.innerTextStatic \"text\"" (Some "FS0039")
        case "HTMLElement outerText setter excluded" "let modifier = H.Properties.HTMLElement.outerTextStatic \"text\"" (Some "FS0039")
        case "HTMLIFrameElement srcdoc setter excluded" "let modifier = H.Properties.HTMLIFrameElement.srcdocStatic \"markup\"" (Some "FS0039")
        case
            "shared input property rejects div"
            "let view = H.div Tree.ops [| H.Properties.HTMLInputElement.checkedStatic true |] [||]"
            (Some "FS0001")
        case "wrong mapped event type" "let modifier = H.Events.onClick (fun (_: Dom.KeyboardEvent) -> ())" (Some "FS0001")
        case "attribute helper rejects incompatible element" "let view = H.div Tree.ops [| H.Attrs.Input.checkedStatic true |] [||]" (Some "FS0001")
        case "boolean attribute rejects string value" "let modifier = H.Attrs.Button.disabledStatic \"false\"" (Some "FS0001")
        case "reactive attribute requires removal option" "let modifier = H.Attrs.classReactive (fun () -> \"panel\")" (Some "FS0001")
        case
            "bound Program helpers"
            "let view = H.Program.article [| H.Attrs.classStatic \"panel\"; H.Events.onClick ignore |] [| H.Program.input [| H.Input.checkedStatic true |]; H.Program.br [||] |]"
            None
        case "bound input modifier rejects div" "let view = H.Program.div [| H.Input.checkedStatic true |] [||]" (Some "FS0001")
        case "bound void element rejects children" "let view = H.Program.br [||] [| Program.ops.Text \"child\" |]" (Some "FS0003")
        case "bound Program rejects Tree children" "let view = H.Program.div [||] [| Tree.ops.Text \"child\" |]" (Some "FS0001")
        case
            "overload source shapes"
            "let view (signal: global.Ranvier.Signal<string>) (memo: global.Ranvier.Memo<string>) = H.Program.input [| Props.Input.value \"fixed\"; Props.Input.value (fun () -> \"read\"); Props.Input.value signal; Props.Input.value memo; Attrs.title signal; Attrs.Input.value (None: string option) |]"
            None
        case "overloaded input property rejects div" "let view = H.Program.div [| Props.Input.value \"input\" |] [||]" (Some "FS0193")
        case
            "overloaded property rejects wrong source type"
            "let modifier (source: global.Ranvier.Signal<int>) = Props.Input.value source"
            (Some "FS0041")
        case "overloaded attribute rejects wrong reader result" "let modifier = Attrs.title (fun () -> 123)" (Some "FS0041")
        case
            "template catalogue keywords"
            """
type R = { Kind: string }
let view = Component.define (fun _ (p: R) -> p) (fun t -> t.div [||] [| t.``base`` [||]; t.button [| t.Attrs.Button.``type`` (fun r -> r.Kind) |] [||] |])
"""
            None
        case
            "template property on another element"
            """
type R = { Value: string }
let view = Component.define (fun _ (p: R) -> p) (fun t -> t.div [| t.Props.Input.value (fun r -> r.Value) |] [||])
"""
            (Some "FS0193")
        case
            "template void tag with children"
            """
type R = { Value: string }
let view = Component.define (fun _ (p: R) -> p) (fun t -> t.br [||] [||])
"""
            (Some "FS0003")
        free
            "free element bodies"
            """
type FreeRow = { Value: global.Ranvier.Signal<string>; Count: global.Ranvier.Signal<int> }
let view =
    Component.template (fun _ (p: FreeRow) -> p) (
        div (Attrs.className "x", Attrs.title (from (fun r -> r.Value))) {
            "static"
            input (Props.Input.value (from (fun r -> r.Value)))
            span () { fun r -> string r.Count.Value }
        })
"""
            None
        free
            "free input part on div"
            """
type FreeRow = { Value: global.Ranvier.Signal<string> }
let view = Component.template (fun _ (p: FreeRow) -> p) (div (Props.Input.value (from (fun r -> r.Value))) { "x" })
"""
            (Some "FS0041")
        free
            "free generated part of another element"
            """
let view = Component.template (fun _ () -> ()) (span (Props.Input.value "x") { "y" })
"""
            (Some "FS0041")
        free
            "free part of another element"
            """
let part: Part<Dom.HTMLButtonElement, unit> = Props.Button.disabled true
let view = Component.template (fun _ () -> ()) (span (Attrs.className "x", part) { "y" })
"""
            (Some "FS0001")
        free
            "free void tag with children"
            """
let view = Component.template (fun _ () -> ()) (div () { input () { "x" } })
"""
            (Some "FS0708")
        free
            "free outer instance inside a row through text"
            """
type FreeItem = { Id: int }
let view =
    Component.template (fun _ (items: FreeItem array) -> items) (
        div () { forEachFixed (fun (item: FreeItem) -> item.Id) id (span () { text (fun (items: FreeItem array) -> string items.Length) }) })
"""
            (Some "FS0041")
        free
            "free outer instance inside a row through a part"
            """
type FreeItem = { Id: int }
let view =
    Component.template (fun _ (items: FreeItem array) -> items) (
        div () { forEach (fun (item: FreeItem) -> item.Id) id (span (Attrs.title (fun (items: FreeItem array) -> string items.Length)) { "r" }) })
"""
            (Some "FS0193")
        free
            "free outer instance inside a row through a pre-bound outer part"
            """
type FreeItem = { Id: int }
let count: Part<Dom.HTMLSpanElement, FreeItem array> = Attrs.title (fun (items: FreeItem array) -> string items.Length)
let view =
    Component.template (fun _ (items: FreeItem array) -> items) (
        div () { forEach (fun (item: FreeItem) -> item.Id) id (span (count) { "r" }) })
"""
            (Some "FS0193")
        free
            "free Program view in a body"
            """
let view = Component.template (fun _ () -> ()) (div () { Program.text "x" })
"""
            (Some "FS0041")
        free
            "free lambda with a label shared by a later record"
            """
type FreeKeyed = { Id: int; Label: string }
type FreeArm = { Id: int; Mode: int }
let view = Component.template (fun _ (row: FreeKeyed) -> row) (span () { fun r -> string r.Id })
"""
            (Some "FS0001")
        free
            "free annotated lambda with a shared label"
            """
type FreeKeyed = { Id: int; Label: string }
type FreeArm = { Id: int; Mode: int }
let view = Component.template (fun _ (row: FreeKeyed) -> row) (span () { fun (r: FreeKeyed) -> string r.Id })
"""
            None
        free
            "unannotated class member read with an unrelated record label"
            """
type FreeCountRecord = { Count: global.Ranvier.Signal<int> }
type FreeCounter(setup: Setup, start: int) =
    member val Count = global.Ranvier.Api.createSignal start
    static member View = span () { fun c -> string c.Count.Value }
let counter = Component.ofClass FreeCounter
"""
            (Some "FS0001")
        free
            "unannotated class member read in an annotated View with an unrelated record label"
            """
type FreeCountRecord = { Count: global.Ranvier.Signal<int> }
type FreeCounter(setup: Setup, start: int) =
    member val Count = global.Ranvier.Api.createSignal start
    static member View: TemplateView<FreeCounter> = span () { fun c -> string c.Count.Value }
"""
            (Some "FS0001")
        free
            "unannotated class member read with no record label"
            """
type FreeTally(setup: Setup, start: int) =
    member val Tally = global.Ranvier.Api.createSignal start
    static member View = span () { fun c -> string c.Tally.Value }
let tally = Component.ofClass FreeTally
"""
            (Some "FS0072")
        // Inside brackets or a body, parse recovery adds FS0583/FS0604 to the FS0010; a let binding isolates it.
        free
            "base as a tag name"
            """
let tag = base ()
let view = Component.template (fun _ () -> ()) (head () { tag })
"""
            (Some "FS0010")
        free "all tag classes" allTagClassesBody None
        free
            "option' leaves FSharp.Core option usable"
            """
let c: string option = Some "x"
let d = defaultArg (None: int option) 1
let view = Component.template (fun _ () -> ()) (select () { option' () { "one" } })
"""
            None
        free
            "a lambda parameter named like a tag shadows it only in its body"
            """
type FreeTitled = { Title: string }
let view = Component.template (fun _ (t: FreeTitled) -> t) (div () { p () { "note" }; span () { fun p -> p.Title }; p () { "after" } })
"""
            None
        free
            "a local value named like a tag, used as one"
            """
let view =
    let p = "note"
    Component.template (fun _ () -> ()) (div () { p () { "x" } })
"""
            (Some "FS0003")
        free
            "ofMembers on a type without Create"
            """
type NoCreate =
    { N: int }
    static member View: TemplateView<NoCreate> = span () { "x" }
let c = Component.ofMembers<NoCreate, int> ()
"""
            (Some "FS0001")
        free
            "ofClass on a type without View"
            """
type NoView(setup: Setup, n: int) =
    member _.N = n
let c = Component.ofClass NoView
"""
            (Some "FS0001")
        free
            "define, template and ofMembers resolve in one file"
            """
type Members =
    { N: int }
    static member Create(_: Setup, n: int) = { N = n }
    static member View = span () { fun m -> string m.N }
let a = Component.define (fun _ (n: int) -> n) (fun t -> t.span [||] [||])
let b = Component.template (fun _ (n: int) -> n) (span () { })
let c = Component.ofMembers<Members, _> ()
"""
            None
        free
            "a ComponentTag subclass with a private primary constructor"
            """
type Props = { Start: int }
let child = Component.template (fun _ (p: Props) -> p) (span () { fun p -> string p.Start })
type childOf<'i> private (props: 'i -> Props, primary: unit) =
    inherit ComponentTag<Props, 'i>(child, props)
    new(props: 'i -> Props) = childOf<'i>(props, ())
    new(props: Props) = childOf<'i>((fun _ -> props), ())
let view = Component.template (fun _ (p: Props) -> p) (div () { childOf (fun p -> { Start = p.Start + 1 }); childOf { Start = 9 } })
"""
            None
        free
            "void and component tags as views outside a body"
            """
type Shown = { On: global.Ranvier.Signal<bool>; Src: string }
type Props = { Start: int }
let child = Component.template (fun _ (p: Props) -> p) (span () { fun p -> string p.Start })
type childOf<'i> private (props: 'i -> Props, primary: unit) =
    inherit ComponentTag<Props, 'i>(child, props)
    new(props: 'i -> Props) = childOf<'i>(props, ())
    new(props: Props) = childOf<'i>((fun _ -> props), ())
let breakArm = armOf (br ()).View
let childArm = armOf (childOf { Start = 1 }).View
let view =
    Component.template (fun _ (s: Shown) -> s) (
        div () {
            show (fun s -> s.On.Value) (img (Attrs.title (fun s -> s.Src))).View
            show (fun s -> s.On.Value) (childOf { Start = 2 }).View
            matchWith (fun s -> s.On.Value) (fun _ -> arm breakArm ())
            matchWith (fun s -> s.On.Value) (fun _ -> arm childArm ())
            fragment [| (br ()).View; (hr ()).View; (childOf { Start = 3 }).View |]
            forEachFixed (fun (n: int) -> n) (fun _ -> [| 1; 2 |]) (img (Attrs.title (fun (n: int) -> string n))).View
            forEachFixed (fun (n: int) -> n) (fun _ -> [| 1; 2 |]) (childOf (fun n -> { Start = n })).View
        })
let voidRoot = Component.template (fun _ () -> ()) (input (Attrs.className "root")).View
let tagRoot = Component.template (fun _ () -> ()) (childOf { Start = 4 }).View
"""
            None
        free
            "a void tag in a view position without View"
            """
let view = Component.template (fun _ () -> ()) (div () { show (fun _ -> true) (img ()) })
"""
            (Some "FS0193")
        case "open of Generated.Html" "open Ranvier.View.Dom.Generated.Html\nlet view = ()" (Some "FS0892")
        free
            "the sugar, then Generated.Html.Authoring, collides"
            """
open Ranvier.View.Dom.Generated.Html.Authoring
let view = Component.template (fun _ () -> ()) (div (Attrs.className "x") { "y" })
"""
            (Some "FS0041")
        free
            "qualified Generated.Html beside the sugar"
            """
module H = Ranvier.View.Dom.Generated.Html
let tree = H.div Tree.ops [||] [| Tree.ops.Text "x" |]
let view = Component.template (fun _ () -> ()) (div (Attrs.className "x") { "y" })
"""
            None
    ]

let duplicates =
    cases
    |> List.countBy _.Name
    |> List.filter (fun (_, n) -> n > 1)
    |> List.map fst

if not duplicates.IsEmpty then
    fail $"case names must be unique: %A{duplicates}"

let filters = fsi.CommandLineArgs |> Array.skip 1

let selected =
    if filters.Length = 0 then
        cases
    else
        cases
        |> List.filter (fun c -> filters |> Array.exists c.Name.Contains)

let dotnet (args: string list) =
    let info =
        ProcessStartInfo ("dotnet", args, RedirectStandardOutput = true, RedirectStandardError = true)

    use proc = Process.Start info
    let stderr = proc.StandardError.ReadToEndAsync ()
    let stdout = proc.StandardOutput.ReadToEnd ()
    proc.WaitForExit ()
    proc.ExitCode, stdout + stderr.Result

let errorLine = Regex (@"[^\r\n]*\berror FS\d+:[^\r\n]*")
let errorCode = Regex (@"error (FS\d+)")

/// The FS codes of the distinct error lines in a build's output, ordered by line text.
let errorCodes (output: string) =
    [ for m in errorLine.Matches output -> m.Value ]
    |> List.distinct
    |> List.sortWith (fun a b -> String.CompareOrdinal (a, b))
    |> List.map (fun line -> errorCode.Match(line).Groups[1].Value)

let directory =
    Directory.CreateDirectory (
        Path.Combine (
            Path.GetTempPath (),
            "ranvier-generated-types-"
            + Path.GetRandomFileName ()
        )
    )

let check = Path.Combine (directory.FullName, "Check.fs")
let fsproj = Path.Combine (directory.FullName, "Check.fsproj")

let failures =
    try
        File.WriteAllText (
            fsproj,
            $"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><GenerateDocumentationFile>false</GenerateDocumentationFile></PropertyGroup>
  <ItemGroup><Compile Include="Check.fs" /><ProjectReference Include="{project}" /><PackageReference Update="FSharp.Core" Version="10.1.401" /></ItemGroup>
</Project>
"""
        )

        File.WriteAllText (check, prefix + cases.Head.Body + "\n")

        let restored, restoreOutput =
            dotnet [ "restore"; fsproj; "--configfile"; Path.Combine (root, "nuget.config") ]

        if restored <> 0 then
            fail restoreOutput

        [
            for c in selected do
                File.WriteAllText (
                    check,
                    (if c.Sugar then sugarPrefix else prefix)
                    + c.Body
                    + "\n"
                )

                let exitCode, output =
                    dotnet [ "build"; fsproj; "--no-restore"; "--configuration"; "Release" ]

                let failure =
                    match c.Expected with
                    | None when exitCode <> 0 -> Some $"{c.Name} failed:\n{output}"
                    | None -> None
                    | Some code ->
                        let codes = errorCodes output

                        if exitCode = 0 || codes <> [ code ] then
                            Some $"{c.Name} must fail with exactly one error {code}, got %A{codes}:\n{output}"
                        else
                            None

                match failure with
                | Some message ->
                    eprintfn "FAIL %s" message
                    yield c.Name
                | None -> printfn "PASS %s" c.Name
        ]
    finally
        try
            directory.Delete true
        with _ ->
            ()

if failures.IsEmpty then
    printfn "PASS: %d of %d cases" selected.Length cases.Length
else
    eprintfn "FAILED %d of %d cases:" failures.Length selected.Length
    failures |> List.iter (eprintfn "  %s")
    exit 1
