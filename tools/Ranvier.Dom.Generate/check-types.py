"""Compile valid generated usage and reject incompatible/readonly examples."""
from pathlib import Path
import re
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
project = root / "examples/Fable.Ranvier.Playground/tests/DomTests.fsproj"
prefix = """module GeneratedTypeCheck
open Fable.Core.TS
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
module H = Ranvier.View.Foundation.Generated.Html
open Ranvier.View.Foundation.Generated.Html.Authoring
open Ranvier.View.Foundation.Template
"""
sugar_members = re.findall(
    r"member _\.``(\w+)`` ?\(\[<ParamArray>\] parts: Part<[^>]*>[^)]*\) : (ElementBuilder|TemplateView)",
    re.sub(r"\s+", " ", (root / "experiments/Ranvier.View.Portable/Generated/HtmlTemplateSugar.fs").read_text(encoding="utf-8")),
)
assert len(sugar_members) == 112, "the generated sugar file must declare every HTML tag"
# One call per generated tag: a void tag takes no body, any other tag takes an empty body.
all_sugar_tags = "\n".join(
    "            t." + name + ("() { }" if kind == "ElementBuilder" else "()") for name, kind in sugar_members
)
examples = [
    ("valid helpers", "let view = H.div Tree.ops [| H.Properties.HTMLElement.titleStatic \"shared\"; H.Events.onClick (fun (_: Dom.PointerEvent) -> ()) |] [| H.input Tree.ops [| H.Input.checkedStatic true |]; H.select Tree.ops [||] [| H.option Tree.ops [||] [||] |]; H.br Tree.ops [||] |]", None),
    ("generic modifiers reject lists", "let view = H.div Tree.ops [] [||]", "FS0001"),
    ("generic children reject lists", "let view = H.div Tree.ops [||] []", "FS0001"),
    ("bound modifiers reject lists", "let view = H.Program.div [] [||]", "FS0001"),
    ("bound children reject lists", "let view = H.Program.div [||] []", "FS0001"),
    ("input modifier on div", "let view = H.div Tree.ops [| H.Input.checkedStatic true |] [||]", "FS0001"),
    ("wrong property value", 'let modifier = H.Input.checkedStatic "true"', "FS0001"),
    ("readonly native property", "let write (node: Dom.HTMLInputElement) = node.form <- None", "FS0810"),
    ("files setter excluded by policy", "let modifier = H.Input.filesStatic None", "FS0039"),
    ("void element rejects children", "let view = H.br Tree.ops [||] [| Tree.ops.Text \"child\" |]", "FS0001"),
    ("wrong expanded property value", "let modifier = H.Textarea.rowsStatic \"3\"", "FS0001"),
    ("DOM replacement setter excluded", "let modifier = H.Div.innerHTMLStatic \"markup\"", "FS0039"),
    ("outerHTML setter excluded", "let modifier = H.Div.outerHTMLStatic \"markup\"", "FS0039"),
    ("textContent setter excluded", "let modifier = H.Div.textContentStatic \"text\"", "FS0039"),
    ("innerText setter excluded", "let modifier = H.Div.innerTextStatic \"text\"", "FS0039"),
    ("outerText setter excluded", "let modifier = H.Div.outerTextStatic \"text\"", "FS0039"),
    ("srcdoc setter excluded", "let modifier = H.Iframe.srcdocStatic \"markup\"", "FS0039"),
    ("template raw HTML takes UnsafeHtml only", '''
let view = Component.define (fun _ () -> ()) (fun t -> t.div [| t.Unsafe.html "<b>x</b>" |] [||])
''', "FS0041"),
    ("excluded setters' getters and sibling setters exist in their declaring modules", '''
let readers: (Dom.HTMLIFrameElement -> string) list =
    [ H.Properties.Element.innerHTML; H.Properties.Element.outerHTML; H.Properties.Element.textContent; H.Properties.HTMLElement.innerText; H.Properties.HTMLElement.outerText; H.Properties.HTMLIFrameElement.srcdoc; H.Iframe.srcdoc ]
let modifiers: Typed.Modifier<Dom.HTMLIFrameElement> list =
    [ H.Properties.Element.idStatic "x"; H.Properties.HTMLElement.titleStatic "x"; H.Properties.HTMLIFrameElement.srcStatic "x"; H.Iframe.srcStatic "x" ]
''', None),
    ("Element innerHTML setter excluded", "let modifier = H.Properties.Element.innerHTMLStatic \"markup\"", "FS0039"),
    ("Element outerHTML setter excluded", "let modifier = H.Properties.Element.outerHTMLStatic \"markup\"", "FS0039"),
    ("Element textContent setter excluded", "let modifier = H.Properties.Element.textContentStatic \"text\"", "FS0039"),
    ("HTMLElement innerText setter excluded", "let modifier = H.Properties.HTMLElement.innerTextStatic \"text\"", "FS0039"),
    ("HTMLElement outerText setter excluded", "let modifier = H.Properties.HTMLElement.outerTextStatic \"text\"", "FS0039"),
    ("HTMLIFrameElement srcdoc setter excluded", "let modifier = H.Properties.HTMLIFrameElement.srcdocStatic \"markup\"", "FS0039"),
    ("shared input property rejects div", "let view = H.div Tree.ops [| H.Properties.HTMLInputElement.checkedStatic true |] [||]", "FS0001"),
    ("wrong mapped event type", "let modifier = H.Events.onClick (fun (_: Dom.KeyboardEvent) -> ())", "FS0001"),
    ("attribute helper rejects incompatible element", "let view = H.div Tree.ops [| H.Attrs.Input.checkedStatic true |] [||]", "FS0001"),
    ("boolean attribute rejects string value", "let modifier = H.Attrs.Button.disabledStatic \"false\"", "FS0001"),
    ("reactive attribute requires removal option", "let modifier = H.Attrs.classReactive (fun () -> \"panel\")", "FS0001"),
    ("bound Program helpers", "let view = H.Program.article [| H.Attrs.classStatic \"panel\"; H.Events.onClick ignore |] [| H.Program.input [| H.Input.checkedStatic true |]; H.Program.br [||] |]", None),
    ("bound input modifier rejects div", "let view = H.Program.div [| H.Input.checkedStatic true |] [||]", "FS0001"),
    ("bound void element rejects children", "let view = H.Program.br [||] [| Program.ops.Text \"child\" |]", "FS0003"),
    ("bound Program rejects Tree children", "let view = H.Program.div [||] [| Tree.ops.Text \"child\" |]", "FS0001"),
    ("overload source shapes", "let view (signal: global.Ranvier.Signal<string>) (memo: global.Ranvier.Memo<string>) = H.Program.input [| Props.Input.value \"fixed\"; Props.Input.value (fun () -> \"read\"); Props.Input.value signal; Props.Input.value memo; Attrs.title signal; Attrs.Input.value (None: string option) |]", None),
    ("overloaded input property rejects div", "let view = H.Program.div [| Props.Input.value \"input\" |] [||]", "FS0193"),
    ("overloaded property rejects wrong source type", "let modifier (source: global.Ranvier.Signal<int>) = Props.Input.value source", "FS0041"),
    ("overloaded attribute rejects wrong reader result", "let modifier = Attrs.title (fun () -> 123)", "FS0041"),
    ("template catalogue keywords", '''
type R = { Kind: string }
let view = Component.define (fun _ (p: R) -> p) (fun t -> t.div [||] [| t.``base`` [||]; t.button [| t.Attrs.Button.``type`` (fun r -> r.Kind) |] [||] |])
''', None),
    ("template property on another element", '''
type R = { Value: string }
let view = Component.define (fun _ (p: R) -> p) (fun t -> t.div [| t.Props.Input.value (fun r -> r.Value) |] [||])
''', "FS0193"),
    ("template void tag with children", '''
type R = { Value: string }
let view = Component.define (fun _ (p: R) -> p) (fun t -> t.br [||] [||])
''', "FS0003"),
    ("sugar element bodies", '''
open Ranvier.View.Foundation.Sugar
type SugarRow = { Value: global.Ranvier.Signal<string>; Count: global.Ranvier.Signal<int> }
let view =
    Component.define (fun _ (p: SugarRow) -> p) (fun t ->
        t.Div(t.Attrs.className "x", t.Attrs.title (t.from (fun r -> r.Value))) {
            "static"
            t.Input(t.Props.Input.value (t.from (fun r -> r.Value)))
            t.Span() { fun r -> string r.Count.Value }
            t.Span() { t.text (fun (r: SugarRow) -> string r.Count.Value) }
        })
''', None),
    ("sugar input part on div", '''
open Ranvier.View.Foundation.Sugar
type SugarRow = { Value: global.Ranvier.Signal<string> }
let view = Component.define (fun _ (p: SugarRow) -> p) (fun t -> t.Div(t.Props.Input.value (t.from (fun r -> r.Value))) { "x" })
''', "FS0041"),
    ("sugar void tag with children", '''
open Ranvier.View.Foundation.Sugar
let view = Component.define (fun _ () -> ()) (fun t -> t.Div() { t.Input() { "x" } })
''', "FS0708"),
    ("sugar part of another element", '''
open Ranvier.View.Foundation.Sugar
let view =
    Component.define (fun _ () -> ()) (fun t ->
        let part: Part<Dom.HTMLButtonElement, unit> = t.Props.Button.disabled true
        t.Span(t.Attrs.className "x", part) { "y" })
''', "FS0001"),
    ("sugar lambda yield with a label shared by a later record", '''
open Ranvier.View.Foundation.Sugar
type SugarItem = { Id: int }
type SugarOther = { Id: int; Name: string }
let view =
    Component.define (fun _ (items: SugarItem array) -> items) (fun t ->
        t.Div() { t.forEach (fun (item: SugarItem) -> item.Id) id (fun r -> r.Span() { fun row -> string (row ()).Id }) })
''', "FS0001"),
    # Counterpart of the lambda-yield case above: t.text compiles where the bare lambda yield conflicts.
    ("sugar t.text with a label shared by a later record", '''
open Ranvier.View.Foundation.Sugar
type SugarItem = { Id: int }
type SugarOther = { Id: int; Name: string }
let view =
    Component.define (fun _ (items: SugarItem array) -> items) (fun t ->
        t.Div() { t.forEach (fun (item: SugarItem) -> item.Id) id (fun r -> r.Span() { r.text (fun row -> string (row ()).Id) }) })
''', None),
    ("sugar outer instance inside a row", '''
open Ranvier.View.Foundation.Sugar
type SugarItem = { Id: int }
let view =
    Component.define (fun _ (items: SugarItem array) -> items) (fun t ->
        t.Div() { t.forEach (fun (item: SugarItem) -> item.Id) id (fun r -> r.Span() { t.text (fun (items: SugarItem array) -> string items.Length) }) })
''', "FS0041"),
    ("sugar Program view in a body", '''
open Ranvier.View.Foundation.Sugar
let view = Component.define (fun _ () -> ()) (fun t -> t.Div() { Program.text "x" })
''', "FS0041"),
    ("generated sugar tags", '''
open Ranvier.View.Foundation.Sugar
let view =
    Component.define (fun _ () -> ()) (fun t ->
        t.Article(t.Attrs.className "a") {
            t.Base()
            t.Select() { t.Option() { "one" } }
            t.Object() { }
            t.Img()
            t.H1() { "title" }
            t.Tbody() { t.Tr() { t.Td() { "cell" } } }
''' + all_sugar_tags + '''
        })
''', None),
    ("generated sugar void tag with children", '''
open Ranvier.View.Foundation.Sugar
let view = Component.define (fun _ () -> ()) (fun t -> t.Div() { t.Img() { "x" } })
''', "FS0708"),
    ("generated sugar part of another element", '''
open Ranvier.View.Foundation.Sugar
let view = Component.define (fun _ () -> ()) (fun t -> t.Span(t.Props.Input.value "x") { "y" })
''', "FS0041"),
]

with tempfile.TemporaryDirectory(prefix="ranvier-generated-types-") as directory:
    path = Path(directory)
    check = path / "Check.fs"
    fsproj = path / "Check.fsproj"
    fsproj.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><GenerateDocumentationFile>false</GenerateDocumentationFile></PropertyGroup>
  <ItemGroup><Compile Include="Check.fs" /><ProjectReference Include="{project.as_posix()}" /><PackageReference Update="FSharp.Core" Version="10.1.401" /></ItemGroup>
</Project>
''', encoding="utf-8")
    check.write_text(prefix + examples[0][1] + "\n", encoding="utf-8")
    restored = subprocess.run(["rtk", "proxy", "dotnet", "restore", str(fsproj), "--configfile", str(root / "NuGet.Config")], capture_output=True, text=True)
    if restored.returncode:
        raise RuntimeError(restored.stdout + restored.stderr)
    for name, body, diagnostic in examples:
        check.write_text(prefix + body + "\n", encoding="utf-8")
        result = subprocess.run(["rtk", "proxy", "dotnet", "build", str(fsproj), "--no-restore", "--configuration", "Release"], capture_output=True, text=True)
        output = result.stdout + result.stderr
        if diagnostic is None:
            if result.returncode:
                raise AssertionError(f"{name} failed:\n{output}")
        else:
            errors = sorted({match.group(0) for match in re.finditer(r"[^\r\n]*\berror FS\d+:[^\r\n]*", output)})
            codes = [re.search(r"error (FS\d+)", line).group(1) for line in errors]
            if result.returncode == 0 or codes != [diagnostic]:
                raise AssertionError(f"{name} must fail with exactly one error {diagnostic}, got {codes}:\n{output}")
        print(f"PASS {name}")
