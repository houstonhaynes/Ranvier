const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { execFileSync } = require('node:child_process');
const { projectPackage, zipEntries, dependencies } = require('./packages.cjs');

const root = path.resolve(__dirname, '../..');
const packages = path.resolve(process.argv[2] || path.join(root, 'bin'));
const version = projectPackage(fs.readFileSync(path.join(root, 'src/Ranvier.Dom/Ranvier.Dom.fsproj'), 'utf8'), 'Ranvier.Dom').version;

const temporaryRoot = fs.realpathSync(os.tmpdir());
const temporary = fs.mkdtempSync(path.join(temporaryRoot, 'ranvier-dom-consumer-'));
const config = path.join(temporary, 'NuGet.Config');
const xml = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
fs.writeFileSync(config, `<configuration>
  <packageSources><clear/><add key="checked-dom" value="${xml(packages)}"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources>
  <packageSourceMapping><clear/>
    <packageSource key="checked-dom"><package pattern="Ranvier.Dom*"/><package pattern="Fable.Ranvier.Dom*"/></packageSource>
    <packageSource key="nuget.org"><package pattern="*"/></packageSource>
  </packageSourceMapping>
</configuration>`);

try {
for (const suffix of ['', '.Traced']) {
  const id = `Ranvier.Dom${suffix}`;
  const entries = zipEntries(fs.readFileSync(path.join(packages, `${id}.${version}.nupkg`)));
  const manifest = [...entries.keys()].find(name => name.endsWith('.nuspec'));
  for (const dependency of dependencies(entries.get(manifest)().toString('utf8')).keys()) {
    if (/fable|xantham/i.test(dependency)) throw new Error(`${id}: native contracts depend on ${dependency}`);
  }
  const project = path.join(root, 'tests/Ranvier.Dom.PackageConsumer/App/App.csproj');
  const properties = [`-p:RanvierDomVersion=${version}`, `-p:RanvierDomPackageSuffix=${suffix}`];
  execFileSync('dotnet', ['restore', project, '--configfile', config, '--packages', path.join(temporary, 'packages'),
    ...properties], { cwd: root, stdio: 'inherit' });
  execFileSync('dotnet', ['run', '--project', project, '--no-restore', '-c', 'Release', ...properties],
    { cwd: root, stdio: 'inherit' });
  const fableProject = path.join(root, 'tests/Ranvier.Dom.PackageConsumer/Fable/Fable.fsproj');
  const output = path.join(temporary, suffix ? 'fable-traced' : 'fable-plain');
  execFileSync('dotnet', ['restore', fableProject, '--configfile', config, '--packages', path.join(temporary, 'packages'),
    ...properties], { cwd: root, stdio: 'inherit' });
  execFileSync('dotnet', ['fickl', fableProject, '--outDir', output], {
    cwd: root, stdio: 'inherit', env: { ...process.env, NUGET_PACKAGES: path.join(temporary, 'packages'),
      RanvierDomVersion: version, RanvierDomPackageSuffix: suffix }
  });
  fs.writeFileSync(path.join(output, 'package.json'), '{"type":"module"}');
  execFileSync('node', [path.join(output, 'Program.js')], { cwd: root, stdio: 'inherit' });
}
} finally {
  const resolved = fs.realpathSync(temporary);
  if (path.dirname(resolved) !== temporaryRoot || !path.basename(resolved).startsWith('ranvier-dom-consumer-')) {
    throw new Error('Consumer scratch directory is outside its allocated temporary root');
  }
  fs.rmSync(temporary, { recursive: true, force: true });
}
