const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { check } = require('./packages.cjs');
const { satisfies, readPackage, parseNuGetConfig, publishedVersions, plan } = require('./feed.cjs');
const registry = 'https://api.nuget.org/v3-flatcontainer/';

function trustedCI(run, repository, sha) {
  return run?.conclusion === 'success' && run.head_branch === 'master' && run.head_sha === sha
    && /^[a-f0-9]{40}$/.test(sha ?? '') && run.head_repository?.full_name === repository
    && ['push', 'workflow_dispatch'].includes(run.event) && run.path === '.github/workflows/ci.yml';
}

function artifactAvailable(artifacts, jobs, automatic) {
  if (artifacts.some(a => a.name === 'nuget-packages' && !a.expired)) return true;
  if (automatic && jobs.some(j => j.name === 'Packages' && j.conclusion === 'skipped')) return false;
  throw new Error('No unexpired nuget-packages artifact from successful CI; run CI first');
}

function orderPackages(packages) {
  const byId = new Map(packages.map(pkg => [pkg.id.toLowerCase(), pkg]));
  const visited = new Set(), active = new Set(), ordered = [];
  function visit(pkg) {
    const id = pkg.id.toLowerCase();
    if (active.has(id)) throw new Error(`Package dependency cycle at ${pkg.id}`);
    if (visited.has(id)) return;
    active.add(id);
    for (const dependency of pkg.dependencies) {
      const sibling = byId.get(dependency.id.toLowerCase());
      if (sibling && satisfies(sibling.version, dependency.range)) visit(sibling);
    }
    active.delete(id);
    visited.add(id);
    ordered.push(pkg);
  }
  packages.forEach(visit);
  return ordered;
}

async function getJson(url) {
  const response = await fetch(url, { signal: AbortSignal.timeout(30000) });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`Package feed returned HTTP ${response.status}: ${url}`);
  return response.json();
}

async function missingPackages(packages, get = getJson) {
  const missing = [];
  for (const pkg of packages) {
    const listing = await get(`${registry}${pkg.id.toLowerCase()}/index.json`);
    if (listing !== null && (!Array.isArray(listing.versions) || listing.versions.some(v => typeof v !== 'string'))) {
      throw new Error(`Invalid version listing for ${pkg.id}`);
    }
    if (!(listing?.versions ?? []).some(version => version.toLowerCase() === pkg.version.toLowerCase())) missing.push(pkg);
  }
  return missing;
}

async function resolveRelease(event, passingCI) {
  if (event.eventName === 'workflow_run') {
    const run = event.run;
    if (!trustedCI(run, event.repository, run?.head_sha)) {
      throw new Error('Automatic publishing requires successful master CI from this repository');
    }
    return { ref: run.head_sha, pack: true, publish: true };
  }
  if (event.eventName !== 'workflow_dispatch') throw new Error('Unsupported release event');
  if (!event.dryRun) {
    if (event.ref !== 'refs/heads/master') throw new Error('Publish from master only');
    if (!await passingCI(event.sha)) throw new Error('CI has not passed for this exact revision');
  }
  return { ref: event.sha, pack: true, publish: !event.dryRun };
}

function adapterProjects(root = '.') {
  const directory = path.join(root, 'adapters');
  return fs.readdirSync(directory).sort().map(name => path.join(directory, name, `${name}.fsproj`))
    .filter(file => fs.existsSync(file));
}

async function releasePlan(directory, get = getJson) {
  const problems = check(directory, 'src', ['tools/Ranvier.Analyzers/Ranvier.Analyzers.fsproj'], adapterProjects());
  if (problems.length) throw new Error(problems.join('\n'));
  const packages = fs.readdirSync(directory).filter(file => file.endsWith('.nupkg')).sort()
    .map(file => ({ file, ...readPackage(fs.readFileSync(path.join(directory, file))) }));
  const sources = parseNuGetConfig(fs.readFileSync('nuget.config', 'utf8'));
  const dependencies = await publishedVersions(packages, registry, sources, get);
  const eligibility = plan(packages, dependencies);
  if (eligibility.refused.length) throw new Error(`Unresolvable package dependencies: ${JSON.stringify(eligibility.refused)}`);
  const pending = await missingPackages(orderPackages(packages), get);
  return { packages, pending };
}

function pushPackage(directory, pkg, key) {
  console.log(`Publishing ${pkg.id} ${pkg.version}`);
  const result = spawnSync('dotnet', ['nuget', 'push', path.join(directory, pkg.file), '--source',
    'https://api.nuget.org/v3/index.json', '--api-key', key, '--skip-duplicate'], { stdio: 'inherit' });
  if (result.error || result.status !== 0) throw new Error(`NuGet push failed for ${pkg.id}`);
}

async function main([command, directory = 'bin']) {
  const release = await releasePlan(directory);
  if (command === 'plan') {
    fs.writeFileSync(path.join(directory, 'release-plan.json'), JSON.stringify(release, null, 2) + '\n');
    console.log(`${release.packages.length} validated packages; ${release.pending.length} versions to publish`);
    for (const pkg of release.pending) console.log(`Would publish ${pkg.id} ${pkg.version}`);
    if (process.env.GITHUB_OUTPUT) fs.appendFileSync(process.env.GITHUB_OUTPUT, `pending=${release.pending.length}\n`);
    if (process.env.GITHUB_STEP_SUMMARY) {
      fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, `## NuGet release\n\n${release.packages.length} validated packages; ${release.pending.length} new versions.\n\n`
        + release.pending.map(p => `- ${p.id} ${p.version}\n`).join(''));
    }
  } else if (command === 'push') {
    if (release.pending.length && !process.env.NUGET_API_KEY) throw new Error('NuGet authentication is missing');
    for (const pkg of release.pending) pushPackage(directory, pkg, process.env.NUGET_API_KEY);
  } else if (command === 'verify') {
    let missing = release.pending;
    const deadline = Date.now() + 10 * 60 * 1000;
    while (missing.length && Date.now() < deadline) {
      await new Promise(resolve => setTimeout(resolve, 15000));
      missing = await missingPackages(missing);
    }
    if (missing.length) throw new Error(`Published versions are not indexed: ${missing.map(p => p.id).join(', ')}`);
    console.log(`Verified all ${release.packages.length} versions on nuget.org`);
  } else throw new Error('Usage: release.cjs plan|push|verify <packages-directory>');
}

module.exports = { trustedCI, artifactAvailable, orderPackages, missingPackages, resolveRelease, adapterProjects, releasePlan, getJson };
if (require.main === module) main(process.argv.slice(2)).catch(error => { console.error(error.message); process.exitCode = 1; });
