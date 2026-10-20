// The packages `build.fsx -- publish` pushes: for each src/<Name>/<Name>.fsproj, `<PackageId>` and `<PackageId>.Traced`
// at the project's `<Version>`, each with a lib/<tfm>/<AssemblyName>.dll per `<TargetFrameworks>` entry and a .snupkg.
const fs = require('node:fs');
const path = require('node:path');
const zlib = require('node:zlib');

// The AssemblyMetadata("RanvierTrace", "true") blob: two length-prefixed UTF-8 strings. Only a traced build carries it.
const traceMarker = Buffer.concat([Buffer.from([12]), Buffer.from('RanvierTrace'), Buffer.from([4]), Buffer.from('true')]);

function property(xml, name) {
  const match = new RegExp(`<${name}>\\s*([^<]+?)\\s*</${name}>`).exec(xml);
  return match ? match[1] : null;
}

// The untraced package of a project file; the unconditional `<PackageId>` is the untraced id.
function projectPackage(xml, name) {
  const frameworks = property(xml, 'TargetFrameworks') ?? property(xml, 'TargetFramework');
  return {
    id: property(xml, 'PackageId') ?? name,
    version: property(xml, 'Version'),
    assembly: property(xml, 'AssemblyName') ?? name,
    frameworks: (frameworks ?? '').split(';').map(f => f.trim()).filter(Boolean).sort(),
  };
}

// Every package publish pushes, untraced and traced, keyed by its .nupkg file name.
function expectedPackages(projects) {
  const packages = new Map();
  for (const project of projects) {
    for (const traced of [false, true]) {
      const id = traced ? `${project.id}.Traced` : project.id;
      packages.set(`${id}.${project.version}.nupkg`, { ...project, id, traced });
    }
  }
  return packages;
}

// The entries of a zip archive, by name, each read on demand. Stored and deflated entries only, as NuGet writes.
function zipEntries(buffer) {
  let end = buffer.length - 22;
  while (end >= 0 && buffer.readUInt32LE(end) !== 0x06054b50) end--;
  if (end < 0) throw new Error('not a zip archive');
  const entries = new Map();
  let at = buffer.readUInt32LE(end + 16);
  for (let i = buffer.readUInt16LE(end + 10); i > 0; i--) {
    const method = buffer.readUInt16LE(at + 10);
    const size = buffer.readUInt32LE(at + 20);
    const nameLength = buffer.readUInt16LE(at + 28);
    const local = buffer.readUInt32LE(at + 42);
    const name = buffer.toString('utf8', at + 46, at + 46 + nameLength);
    entries.set(name, () => {
      const start = local + 30 + buffer.readUInt16LE(local + 26) + buffer.readUInt16LE(local + 28);
      const data = buffer.subarray(start, start + size);
      return method === 8 ? zlib.inflateRawSync(data) : data;
    });
    at += 46 + nameLength + buffer.readUInt16LE(at + 30) + buffer.readUInt16LE(at + 32);
  }
  return entries;
}

// The problems of one .nupkg against its expected package; empty when it matches.
function checkPackage(buffer, expected) {
  const problems = [];
  const entries = zipEntries(buffer);
  const nuspec = [...entries.keys()].find(name => !name.includes('/') && name.endsWith('.nuspec'));
  if (!nuspec) return [`${expected.id}: no .nuspec`];
  const manifest = entries.get(nuspec)().toString('utf8');
  if (property(manifest, 'id') !== expected.id) problems.push(`${expected.id}: nuspec id is ${property(manifest, 'id')}`);
  if (property(manifest, 'version') !== expected.version) problems.push(`${expected.id}: nuspec version is ${property(manifest, 'version')}`);
  const libs = [...entries.keys()].filter(name => name.startsWith('lib/'));
  const frameworks = [...new Set(libs.map(name => name.split('/')[1]))].sort();
  if (frameworks.join(';') !== expected.frameworks.join(';')) {
    problems.push(`${expected.id}: lib frameworks are [${frameworks}], expected [${expected.frameworks}]`);
  }
  for (const framework of expected.frameworks) {
    const dll = `lib/${framework}/${expected.assembly}.dll`;
    if (!entries.has(dll)) {
      problems.push(`${expected.id}: missing ${dll}`);
    } else if (entries.get(dll)().includes(traceMarker) !== expected.traced) {
      problems.push(`${expected.id}: ${dll} is ${expected.traced ? 'untraced' : 'traced'}`);
    }
  }
  return problems;
}

// The problems of a package directory against the packable projects under src; empty when it holds exactly the expected
// packages, each with its symbol package.
function check(packagesDirectory, sourceDirectory) {
  const projects = fs.readdirSync(sourceDirectory, { withFileTypes: true })
    .filter(entry => entry.isDirectory() && fs.existsSync(path.join(sourceDirectory, entry.name, `${entry.name}.fsproj`)))
    .map(entry => ({ name: entry.name, xml: fs.readFileSync(path.join(sourceDirectory, entry.name, `${entry.name}.fsproj`), 'utf8') }))
    .filter(({ xml }) => property(xml, 'IsPackable') !== 'false')
    .map(({ name, xml }) => projectPackage(xml, name));
  if (projects.length === 0) return [`no project under ${sourceDirectory}`];
  const expected = expectedPackages(projects);
  const files = fs.existsSync(packagesDirectory) ? fs.readdirSync(packagesDirectory) : [];
  const problems = [];
  for (const file of files.filter(f => f.endsWith('.nupkg') && !expected.has(f))) problems.push(`unexpected package ${file}`);
  for (const [file, pkg] of expected) {
    if (!files.includes(file)) {
      problems.push(`missing package ${file}`);
      continue;
    }
    if (!files.includes(file.replace(/\.nupkg$/, '.snupkg'))) problems.push(`missing symbol package for ${file}`);
    problems.push(...checkPackage(fs.readFileSync(path.join(packagesDirectory, file)), pkg));
  }
  return problems;
}

module.exports = { traceMarker, projectPackage, expectedPackages, zipEntries, checkPackage, check };

if (require.main === module) {
  // node packages.cjs <packages directory> <src directory>: exits non-zero listing each problem.
  const [packagesDirectory = 'bin', sourceDirectory = 'src'] = process.argv.slice(2);
  const problems = check(packagesDirectory, sourceDirectory);
  for (const problem of problems) console.error(problem);
  if (problems.length > 0) process.exit(1);
  console.log(`Packages in ${packagesDirectory} match the projects in ${sourceDirectory}.`);
}
