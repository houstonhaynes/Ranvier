import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '../..');
const output = path.join(here, 'generated-types');
await fs.mkdir(output, { recursive: true });
const bindings = path.join(root, 'src/Ranvier.Css.Bindings/bin/Release/net10.0/Ranvier.Css.Bindings.dll');
function dotnet(args, success = true) {
  const result = spawnSync('dotnet', args, { cwd: root, encoding: 'utf8', windowsHide: true, maxBuffer: 16 * 1024 * 1024 });
  const log = (result.stdout ?? '') + (result.stderr ?? '');
  if ((result.status === 0) !== success || result.error) throw new Error(log || String(result.error));
  return log;
}
try { await fs.access(bindings); }
catch { dotnet(['build', 'src/Ranvier.Css.Bindings/Ranvier.Css.Bindings.fsproj', '-c', 'Release', '-f', 'net10.0']); }
const project = path.join(output, 'Types.fsproj');
await fs.writeFile(project, `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include="${path.join(root, 'src/Fable.Ranvier.Anime/Fable.Ranvier.Anime.fsproj')}"/><ProjectReference Include="${path.join(root, 'src/Ranvier.View.Anime/Ranvier.View.Anime.fsproj')}"/><ProjectReference Include="${path.join(root, 'src/Ranvier.Css.View/Ranvier.Css.View.fsproj')}"/><Reference Include="Ranvier.Css.Bindings"><HintPath>${bindings}</HintPath></Reference><Compile Include="${path.join(root, 'docs/samples/css-anime/Example.fs')}"/><Compile Include="Types.fs"/></ItemGroup></Project>`);
const prefix = `module Types
open Ranvier.Css
open Ranvier.Anime
let gap: CustomProperty<CssValue.Length.Value> = CustomProperty.create "--gap"
`;
await fs.writeFile(path.join(output, 'Types.fs'), prefix + `
let literal = Anim().With(gap, Css.px 42.)
let endpoints = Anim().With(gap, Css.px 0., Css.px 42.)
let frames = Anim().With(gap, [| Css.px 0.; Css.px 42.; Css.px 10. |])
let drive (el: Fable.Core.TS.Dom.Element) =
    Reactive.driveCss (fun () -> Css.px 42.) el gap (AnimatableProp(duration = 1000.))
let expression = Css.width (CssValue.Length.calc (CssValue.Length.variable "--gap" * Css.number 2. + Css.px 10.))
`);
dotnet(['build', project, '-c', 'Release']);
const cases = [
  'let invalid = Anim().With(gap, Css.deg 42.)',
  'let invalid = Anim().With(gap, Css.px 0., Css.deg 42.)',
  'let invalid = Anim().With(gap, [| Css.deg 42. |])',
  'let invalid (el: Fable.Core.TS.Dom.Element) = Reactive.driveCss (fun () -> Css.deg 42.) el gap (AnimatableProp())',
  'let invalid = Ranvier.View.Anime.Motion.followCss gap (AnimatableProp()) (fun (_:unit) -> Css.deg 42.)',
];
for (const source of cases) {
  await fs.writeFile(path.join(output, 'Types.fs'), prefix + source + '\n');
  const log = dotnet(['build', project, '-c', 'Release', '--no-restore', '-p:BuildProjectReferences=false'], false);
  if (!/error FS(0001|0041|0193)/.test(log)) throw new Error(`Expected a CSS grammar mismatch: ${source}\n${log}`);
}
console.log('Generated CSS sugar and the complete view example compile; all 5 wrong-dimension endpoint/keyframe/reactive consumers are rejected.');
