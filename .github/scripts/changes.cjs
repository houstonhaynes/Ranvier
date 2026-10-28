// Paths whose changes affect no build, test or measurement. The package READMEs of Ranvier.View and Ranvier.View.Anime and the analyzer rule pages are the exceptions: CheckDocs compiles the Ranvier.View README's samples, readme.test.mjs matches the Ranvier.View.Anime README's samples against its fixtures, and the analyzer suite compiles each rule page's examples. Formatting still checks the F# sources under docs/.
// docs/maps holds the signal map model and its tests; docs/samples/server-backends holds compiled server examples.
// docs/research/arms-match and docs/.ai/benchmarks/arms-* hold the committed Arms.Match runs that the Arms.Match job gates.
const docsPatterns = [
  /^docs\/(?!maps\/|content\/analyzers\/|samples\/server-backends\/|research\/arms-match\/|\.ai\/benchmarks\/arms-)/,
  /^brand\//,
  /^\.claude\//,
  /^(?!src\/Ranvier\.View(\.Anime)?\/README\.md$|docs\/content\/analyzers\/).*\.md$/i,
  /^LICENSE[^/]*$/,
  /^\.github\/FUNDING\.yml$/,
];

// True when every path is documentation; an empty or missing list is false, so unknown diffs run everything.
function docsOnly(paths) {
  const files = (paths ?? []).map(p => p.trim()).filter(Boolean);
  return files.length > 0 && files.every(file => !compiledCssDocs.test(file) && docsPatterns.some(pattern => pattern.test(file)));
}

const compiledCssDocs = /^docs\/samples\/css-anime\//;
const servicePaths = {
  marten: /^(?:adapters|tests)\/Ranvier\.Persistence\.Marten(?:\.Tests)?\//,
  kurrentdb: /^(?:adapters|tests)\/Ranvier\.Persistence\.KurrentDB(?:\.Tests)?\//,
  equinox: /^(?:samples|tests)\/Ranvier\.Persistence\.Equinox(?:\.Tests)?\//,
};
const uiPaths = [
  /^src\/(?:Ranvier\.View(?:\.[^/]+)?|Fable\.Ranvier\.Anime)\//,
  /^tools\/Ranvier\.Dom\.Generate\//,
  /^tests\/Ranvier\.View\.[^/]+\//,
  /^fable\//,
  /^examples\/(?:Ranvier\.Motion|Ranvier\.Hydration|Fable\.Ranvier\.Playground)\//,
  compiledCssDocs,
];

function servicesFor(paths, eventName) {
  const files = (paths ?? []).map(p => p.trim()).filter(Boolean);
  const docs = docsOnly(files);
  const shared = files.some(file => !docsOnly([file]) &&
    !Object.values(servicePaths).some(pattern => pattern.test(file)) && !uiPaths.some(pattern => pattern.test(file)));
  const full = eventName !== 'pull_request' || files.length === 0 || shared;
  return Object.fromEntries(Object.entries(servicePaths).map(([name, own]) =>
    [name, !docs && (full || files.some(file => own.test(file)))]));
}

// Paths the Arms.Match job builds or measures: the view and core sources it compiles, the Playground's tests and
// benchmark scripts, the committed runs its budget gate reads, the toolchain pins and the workflow.
const armsPatterns = [
  /^experiments\/Ranvier\.View/,
  /^src\/Ranvier(\.View[^/]*)?\//,
  /^examples\/Fable\.Ranvier\.Playground\//,
  /^docs\/research\/arms-match\//,
  /^docs\/\.ai\/benchmarks\/arms-/,
  /^\.config\/dotnet-tools\.json$/,
  /^global\.json$/,
  /^Directory\.(Build|Packages)\.(props|targets)$/,
  /^\.github\/(workflows\/ci\.yml|scripts\/changes\.cjs)$/,
];

// True when a path touches what the Arms.Match job covers; an empty or missing list is true, as unknown diffs run everything.
function armsAffected(paths) {
  const files = (paths ?? []).map(p => p.trim()).filter(Boolean);
  return files.length === 0 || files.some(file => armsPatterns.some(pattern => pattern.test(file)));
}

// Paths that own the committed Chromium runs of Arms.Match: the lowering and its plugin, the gate scripts, the keyed-rows
// benchmark and its fixtures, the runs themselves and the workflow. They cover every source run-sources.mjs hashes; the
// core, the view libraries and the Playground's lock file are not hashed and leave the pool unchanged.
const armsRunsPatterns = [
  /^experiments\/Ranvier\.View\.Arms/,
  /^examples\/Fable\.Ranvier\.Playground\/benchmarks\/frameworks\//,
  /^examples\/Fable\.Ranvier\.Playground\/tests\/KeyedRows[^/]*Perf\.fs$/,
  /^docs\/research\/arms-match\//,
  /^docs\/\.ai\/benchmarks\/arms-/,
  /^\.github\/(workflows\/ci\.yml|scripts\/changes\.cjs)$/,
];

// True when a path touches what the committed Chromium runs measure; an empty or missing list is true.
function armsRunsAffected(paths) {
  const files = (paths ?? []).map(p => p.trim()).filter(Boolean);
  return files.length === 0 || files.some(file => armsRunsPatterns.some(pattern => pattern.test(file)));
}

// The job outputs for a diff: docs is true when every path is documentation, arms when the Arms.Match job runs, and
// armsRuns when that job checks the committed Chromium runs against their budgets.
function classify(paths) {
  const docs = docsOnly(paths);
  const arms = !docs && armsAffected(paths);
  return { docs, arms, armsRuns: arms && armsRunsAffected(paths) };
}

const zero = /^0{40}$/;
const sha = /^[0-9a-f]{40}$/;

// The diff range for an event: a pull request's merge base to its head, a push's previous tip to its new tip,
// or null when the event has no comparable base (a new branch, a manual run).
function range({ eventName, before, after, base, head }) {
  if (eventName === 'pull_request') return sha.test(base ?? '') && sha.test(head ?? '') ? { from: base, to: head } : null;
  if (eventName === 'push') return sha.test(before ?? '') && !zero.test(before) && sha.test(after ?? '') ? { from: before, to: after } : null;
  return null;
}

module.exports = { docsOnly, armsAffected, armsRunsAffected, classify, range, servicesFor };

if (require.main === module) {
  // node changes.cjs <event> <from> <to>: prints docs, arms and armsRuns, each true or false, for $GITHUB_OUTPUT.
  const { execFileSync } = require('node:child_process');
  const [eventName, from, to] = process.argv.slice(2);
  const r = range(eventName === 'pull_request' ? { eventName, base: from, head: to } : { eventName, before: from, after: to });
  let docs = false, arms = true, armsRuns = true;
  let paths;
  if (r) {
    const base = eventName === 'pull_request'
      ? execFileSync('git', ['merge-base', r.from, r.to], { encoding: 'utf8' }).trim()
      : r.from;
    paths = execFileSync('git', ['diff', '--name-only', '--no-renames', base, r.to], { encoding: 'utf8' }).split('\n');
    ({ docs, arms, armsRuns } = classify(paths));
  }
  console.log(`docs=${docs}`);
  console.log(`arms=${arms}`);
  console.log(`armsRuns=${armsRuns}`);
  for (const [name, selected] of Object.entries(servicesFor(paths, eventName))) console.log(`${name}=${selected}`);
}
