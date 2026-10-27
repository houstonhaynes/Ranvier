import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const directory = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(directory, '../..');
const output = path.join(directory, 'out/checks');
await fs.mkdir(output, { recursive: true });
function dotnet(args, expected = true) {
  const result = spawnSync('dotnet', args, { cwd: root, encoding: 'utf8', maxBuffer: 32 * 1024 * 1024 });
  const log = (result.stdout ?? '') + (result.stderr ?? '');
  if ((result.status === 0) !== expected || result.error) throw new Error(log || String(result.error));
  return log;
}
dotnet(['run', '--project', directory, '-c', 'Release', '--', '--specs-dir', path.join(directory, 'out/specs'),
  '--bindings', path.join(output, 'Css.Generated.fs'), '--output', path.join(output, 'model.json'), '--runtime', '--strict']);
const generated = await fs.readFile(path.join(output, 'Css.Generated.fs'), 'utf8');
const committed = await fs.readFile(path.join(root, 'src/Ranvier.Css.Bindings/Generated/Css.fs'), 'utf8');
if (generated !== committed) throw new Error('Generated CSS bindings are stale; regenerate the library source with --runtime.');
const manifestText = await fs.readFile(path.join(output, 'Css.Generated.manifest.json'), 'utf8');
const committedManifest = await fs.readFile(path.join(root, 'src/Ranvier.Css.Bindings/Generated/Css.manifest.json'), 'utf8');
if (manifestText !== committedManifest) throw new Error('Generated CSS binding manifest is stale.');
const corpusManifest = JSON.parse(manifestText);
const propertyCount = corpusManifest.definitions.filter(item => item.kind === 'Property').length;
if ((generated.match(/let reactive \(/g) ?? []).length !== propertyCount) throw new Error('Reactive helpers do not cover every property.');
if (!corpusManifest.cssWideKeywords.includes('revert-rule') || !generated.includes('globalRevertRule')) throw new Error('Latest CSS-wide keywords are missing.');
const runtimeProject = path.join(root, 'src/Ranvier.Css/Ranvier.Css.fsproj');
const project = `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><GenerateProgramFile>false</GenerateProgramFile><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>3390</NoWarn></PropertyGroup><ItemGroup><ProjectReference Include="${runtimeProject}"/><Compile Include="Css.Generated.fs"/><Compile Include="Smoke.fs"/></ItemGroup></Project>`;
await fs.writeFile(path.join(output, 'Checks.fsproj'), project);
await fs.writeFile(path.join(output, 'Smoke.fs'), `module Checks
open Ranvier.Css.Generated
module Short =
    open Ranvier.Css
    let verify () =
        if Declaration.render (Css.width (Css.px 42.0)) <> "width: 42px;" then failwith "Short typed width."
        if Declaration.render (Css.width (Css.percent 50.0)) <> "width: 50%;" then failwith "Short percentage."
        if Declaration.render (Css.width Css.auto) <> "width: auto;" then failwith "Short keyword."
        if Declaration.render (Css.display Css.block) <> "display: block;" then failwith "Short display keyword."
        if Declaration.render (Css.color Css.red) <> "color: red;" then failwith "Short color keyword."
        let style = styled { Css.width (fun (value:float) -> Css.px value) }
        if not ((Style.renderVariables 84.0 style).Contains("84px")) then failwith "Short reader."
[<EntryPoint>]
let main _ =
    Short.verify ()
    let width = Css.TypeLength.px 42.0 |> Css.TypeLengthPercentage.fromLength |> Css.TypeBoxSize.fromLengthPercentage |> Css.PropertyWidth.fromBoxSize |> Css.PropertyWidth.declaration
    if Css.renderDeclaration width <> "width: 42px;" then failwith "Width serialization failed."
    let widthValue px = Css.TypeLength.px px |> Css.TypeLengthPercentage.fromLength |> Css.TypeBoxSize.fromLengthPercentage |> Css.PropertyWidth.fromBoxSize
    let reactive = Ranvier.Css.Style.create [Css.PropertyWidth.reactive widthValue]
    let rules = Ranvier.Css.Stylesheet.render reactive.Definition.Rules
    if not (rules.Contains("width: var(--r-")) then failwith "Reactive lowering failed."
    if not ((Ranvier.Css.Style.renderVariables 42.0 reactive).Contains("42px")) then failwith "Reactive reader failed."
    if not ((Ranvier.Css.Style.renderVariables 84.0 reactive).Contains("84px")) then failwith "Reactive update failed."
    if Ranvier.Css.Stylesheet.render reactive.Definition.Rules <> rules then failwith "Reactive styles changed static rules."
    0
`);
console.log(dotnet(['run', '--project', path.join(output, 'Checks.fsproj'), '-c', 'Release']));
console.log('Complete corpus bindings compile and serialize width correctly.');
const consumer = path.join(output, 'consumer');
await fs.mkdir(consumer, { recursive: true });
await fs.writeFile(path.join(consumer, 'Consumer.fsproj'), `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><WarningsAsErrors>44</WarningsAsErrors></PropertyGroup><ItemGroup><ProjectReference Include="${runtimeProject}"/><Reference Include="Checks"><HintPath>../bin/Release/net10.0/Checks.dll</HintPath></Reference><Compile Include="Consumer.fs"/></ItemGroup></Project>`);
await fs.writeFile(path.join(consumer, 'Consumer.fs'), `module Consumer\nopen Ranvier.Css\nlet width = Css.width (Css.px 42.0)\nlet reactive = Css.width (fun (value:float) -> Css.px value)\n`);
dotnet(['build', path.join(consumer, 'Consumer.fsproj'), '-c', 'Release']);
await fs.writeFile(path.join(consumer, 'Consumer.fs'), `module Consumer
open Ranvier.Css.Generated
let invalidWidth = Css.TypeAngle.deg 90.0 |> Css.PropertyWidth.fromBoxSize
`);
if (!dotnet(['build', path.join(consumer, 'Consumer.fsproj'), '-c', 'Release'], false).includes('FS0001')) throw new Error('Expected a type mismatch for angle as width.');
for (const expression of ['Css.width (Css.deg 90.0)', 'Css.width Css.block', 'Css.width "42px"', 'Css.width (fun (_:float) -> Css.deg 90.0)', 'Css.width (fun (_:float) -> Css.initial)']) {
  await fs.writeFile(path.join(consumer, 'Consumer.fs'), `module Consumer\nopen Ranvier.Css\nlet invalid = ${expression}\n`);
  const log = dotnet(['build', path.join(consumer, 'Consumer.fsproj'), '-c', 'Release'], false);
  if (!log.includes('FS0041')) throw new Error(`Expected an overload rejection for ${expression}: ${log}`);
}
const manifest = JSON.parse(await fs.readFile(path.join(output, 'Css.Generated.manifest.json'), 'utf8'));
const obsolete = manifest.definitions.find(item => item.obsolete && item.moduleName.includes('_Obsolete_'));
if (!obsolete) throw new Error('No archived definition generated.');
await fs.writeFile(path.join(consumer, 'Consumer.fs'), `module Consumer
open Ranvier.Css.Generated
let obsoleteUrl = Css.${obsolete.moduleName}.specUrl
`);
if (!dotnet(['build', path.join(consumer, 'Consumer.fsproj'), '-c', 'Release'], false).includes('FS0044')) throw new Error('Expected an obsolete API warning.');
const fixture = path.join(output, 'fixture');
await fs.mkdir(fixture, { recursive: true });
const feature = (name, syntax) => ({ name, href: `https://example.test/${name}`, ...(syntax ? { syntax } : {}) });
await fs.writeFile(path.join(fixture, 'input.json'), JSON.stringify({ properties: [feature('bounded', '<length [0,100]>'), feature('pair', '<length>{2,3}'), feature('unordered', 'a || b')],
  types: ['length', 'angle', 'string', 'custom-ident', 'ident-token'].map(name => feature(name)), functions: [], selectors: [], atrules: [] }));
dotnet(['run', '--project', directory, '-c', 'Release', '--no-build', '--', '--runtime', '--input', path.join(fixture, 'input.json'), '--bindings', path.join(fixture, 'Css.Generated.fs'), '--output', path.join(fixture, 'model.json')]);
const small = JSON.parse(await fs.readFile(path.join(fixture, 'Css.Generated.manifest.json'), 'utf8'));
const def = name => small.definitions.find(item => item.name === name);
await fs.writeFile(path.join(fixture, 'Checks.fsproj'), project);
await fs.writeFile(path.join(fixture, 'Smoke.fs'), `module Checks
open Ranvier.Css
open Ranvier.Css.Generated
let rejects action =
    try action (); failwith "Expected invalid argument."
    with :? System.ArgumentException -> ()
[<EntryPoint>]
let main _ =
    let length = Css.TypeLength.px 42.0
    if (length |> Css.PropertyBounded.create |> Css.PropertyBounded.declaration |> Css.renderDeclaration) <> "bounded: 42px;" then failwith "Bounded render."
    rejects (fun () -> Css.TypeLength.px -1.0 |> Css.PropertyBounded.create |> ignore)
    rejects (fun () -> Css.TypeLength.px 101.0 |> Css.PropertyBounded.create |> ignore)
    if (global.Ranvier.Css.Css.bounded (global.Ranvier.Css.Css.px 42.0) |> Css.renderDeclaration) <> "bounded: 42px;" then failwith "Short bounded render."
    rejects (fun () -> global.Ranvier.Css.Css.bounded (global.Ranvier.Css.Css.px -1.0) |> ignore)
    rejects (fun () -> global.Ranvier.Css.Css.bounded (global.Ranvier.Css.Css.px 101.0) |> ignore)
    let dynamicBound = global.Ranvier.Css.Style.create [global.Ranvier.Css.Css.bounded (fun (n:float) -> global.Ranvier.Css.Css.px n)]
    rejects (fun () -> global.Ranvier.Css.Style.renderVariables 101.0 dynamicBound |> ignore)
    let pairItem = Css.Syntax.${def('pair').children[0].syntaxFactory} length
    rejects (fun () -> Css.PropertyPair.create [pairItem] |> ignore)
    if (Css.PropertyPair.create [pairItem; pairItem] |> Css.renderValue) <> "42px 42px" then failwith "Repeat render."
    rejects (fun () -> Css.PropertyUnordered.create None None |> ignore)
    if (Css.PropertyUnordered.create (Some Css.Syntax.${def('unordered').children[0].syntaxFactory}) None |> Css.renderValue) <> "a" then failwith "Unordered render."
    for text in ["-"; "-1"; "inherit"; "revert-rule"] do rejects (fun () -> Css.TypeCustomIdent.create text |> ignore)
    if (Css.TypeIdentToken.create "initial" |> Css.renderValue) <> "initial" then failwith "Lexical identifier."
    let formFeed = string (char 12)
    let escaped = Css.TypeString.create formFeed |> Css.renderValue
    if escaped.Contains(formFeed) || not (escaped.Contains("\\c ")) then failwith "Form feed escaping."
    0
`);
console.log(dotnet(['run', '--project', path.join(fixture, 'Checks.fsproj'), '-c', 'Release']));
console.log('Type rejection, obsolete warnings, numeric ranges, cardinality, unordered groups, identifiers, and string escaping pass.');
