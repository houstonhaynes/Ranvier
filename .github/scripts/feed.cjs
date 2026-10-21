// The publish gate of `build.fsx -- publish`: a package pushes only when every dependency of it resolves, either on the
// target feed or among the packages that push with it. A package depending on a refused package is refused too.
const fs = require('node:fs');
const path = require('node:path');
const { zipEntries } = require('./packages.cjs');

// The numeric and prerelease parts of a SemVer 2.0 version; build metadata does not take part in ordering.
function parseVersion(text) {
  const [core, ...pre] = text.split('+')[0].split('-');
  const numbers = core.split('.').map(Number);
  while (numbers.length < 3) numbers.push(0);
  return { numbers, prerelease: pre.length > 0 ? pre.join('-').split('.') : [] };
}

// Negative, zero or positive as `left` orders before, with or after `right`, by SemVer 2.0 precedence.
function compareVersions(left, right) {
  const a = parseVersion(left), b = parseVersion(right);
  for (let i = 0; i < Math.max(a.numbers.length, b.numbers.length); i++) {
    const difference = (a.numbers[i] ?? 0) - (b.numbers[i] ?? 0);
    if (difference !== 0) return difference;
  }
  if (a.prerelease.length === 0 || b.prerelease.length === 0) return b.prerelease.length - a.prerelease.length;
  for (let i = 0; i < Math.min(a.prerelease.length, b.prerelease.length); i++) {
    const x = a.prerelease[i], y = b.prerelease[i];
    const xn = /^\d+$/.test(x), yn = /^\d+$/.test(y);
    if (xn && yn && Number(x) !== Number(y)) return Number(x) - Number(y);
    if (xn !== yn) return xn ? -1 : 1;
    if (x !== y) return x < y ? -1 : 1;
  }
  return a.prerelease.length - b.prerelease.length;
}

// A NuGet version range: a bare version is a minimum, `[x]` is exact, and bracketed bounds are inclusive or exclusive.
function parseRange(text) {
  const range = text.trim();
  if (!/^[[(]/.test(range)) return { min: range, minInclusive: true, max: null, maxInclusive: false };
  const inner = range.slice(1, -1);
  if (!inner.includes(',')) return { min: inner.trim(), minInclusive: true, max: inner.trim(), maxInclusive: true };
  const [min, max] = inner.split(',').map(part => part.trim() || null);
  return { min, minInclusive: range.startsWith('['), max, maxInclusive: range.endsWith(']') };
}

// Whether `version` lies in the range text `range`.
function satisfies(version, range) {
  const { min, minInclusive, max, maxInclusive } = parseRange(range);
  if (min !== null) {
    const order = compareVersions(version, min);
    if (order < 0 || (order === 0 && !minInclusive)) return false;
  }
  if (max !== null) {
    const order = compareVersions(version, max);
    if (order > 0 || (order === 0 && !maxInclusive)) return false;
  }
  return true;
}

// The id, version and dependencies (id and range, across framework groups) of a .nupkg.
function readPackage(buffer) {
  const entries = zipEntries(buffer);
  const nuspec = [...entries.keys()].find(name => !name.includes('/') && name.endsWith('.nuspec'));
  const manifest = entries.get(nuspec)().toString('utf8');
  const value = name => new RegExp(`<${name}>\\s*([^<]+?)\\s*</${name}>`).exec(manifest)[1];
  const dependencies = [];
  for (const [element] of manifest.matchAll(/<dependency\b[^>]*>/g)) {
    const id = /\bid="([^"]+)"/.exec(element)[1];
    const range = (/\bversion="([^"]+)"/.exec(element) ?? [null, '0.0.0'])[1];
    if (!dependencies.some(d => d.id === id && d.range === range)) dependencies.push({ id, range });
  }
  return { id: value('id'), version: value('version'), dependencies };
}

// The packages that push and the ones refused, each refusal with the dependencies it lacks. `published` maps a lower-case
// package id to the versions the target feed lists for it.
function plan(packages, published) {
  const accepted = new Set(packages.map(pkg => pkg.file));
  const lacking = pkg => pkg.dependencies.filter(({ id, range }) => {
    const inPush = packages.some(other => accepted.has(other.file) && other.id.toLowerCase() === id.toLowerCase() && satisfies(other.version, range));
    return !inPush && !(published.get(id.toLowerCase()) ?? []).some(version => satisfies(version, range));
  });
  let changed = true;
  while (changed) {
    changed = false;
    for (const pkg of packages.filter(p => accepted.has(p.file) && lacking(p).length > 0)) {
      accepted.delete(pkg.file);
      changed = true;
    }
  }
  return {
    push: packages.filter(p => accepted.has(p.file)).map(p => p.file),
    refused: packages.filter(p => !accepted.has(p.file)).map(p => ({ file: p.file, missing: lacking(p) })),
  };
}

// The versions `feed` (a NuGet v3 flat-container base URL) lists for each dependency id outside `packages`; an id the
// feed does not know lists none.
async function publishedVersions(packages, feed) {
  const own = new Set(packages.map(p => p.id.toLowerCase()));
  const ids = [...new Set(packages.flatMap(p => p.dependencies.map(d => d.id.toLowerCase())))].filter(id => !own.has(id));
  const published = new Map();
  for (const id of ids) {
    const response = await fetch(`${feed}${id}/index.json`);
    if (response.status === 404) published.set(id, []);
    else if (!response.ok) throw new Error(`${feed}${id}/index.json answered ${response.status}`);
    else published.set(id, (await response.json()).versions);
  }
  return published;
}

module.exports = { compareVersions, parseRange, satisfies, readPackage, plan, publishedVersions };

if (require.main === module) {
  // node feed.cjs <packages directory> [--refuse <directory> | --dry] [--feed <flat-container url>]: prints each package
  // as accepted or refused, a refusal with the dependencies it lacks. With --refuse, moves the refused packages there and
  // exits 0, so a push of <packages directory>/*.nupkg pushes only the accepted ones. With --dry, exits 0 and moves
  // nothing. Otherwise exits 1 when any package is refused.
  const args = process.argv.slice(2);
  const option = name => { const i = args.indexOf(name); return i < 0 ? undefined : args[i + 1]; };
  const directory = args[0] ?? 'bin';
  const refuse = option('--refuse');
  const feed = option('--feed') ?? 'https://api.nuget.org/v3-flatcontainer/';
  const packages = fs.readdirSync(directory).filter(f => f.endsWith('.nupkg')).sort()
    .map(file => ({ file, ...readPackage(fs.readFileSync(path.join(directory, file))) }));
  publishedVersions(packages, feed).then(published => {
    const { push, refused } = plan(packages, published);
    for (const file of push) console.log(`${args.includes('--dry') ? 'Would push' : 'Accepted'} ${file}`);
    for (const { file, missing } of refused) {
      console.error(`${args.includes('--dry') ? 'Would refuse' : 'Refused'} ${file}: no ${missing.map(d => `${d.id} ${d.range}`).join(', ')} on ${feed} or in this push`);
    }
    if (refuse !== undefined && refused.length > 0) {
      fs.mkdirSync(refuse, { recursive: true });
      for (const { file } of refused) fs.renameSync(path.join(directory, file), path.join(refuse, file));
    } else if (refused.length > 0 && !args.includes('--dry')) {
      process.exitCode = 1;
    }
  }).catch(error => {
    console.error(error.message);
    process.exitCode = 1;
  });
}
