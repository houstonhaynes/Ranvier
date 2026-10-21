// Paths whose changes affect no build, test or measurement. The view sugar README and the analyzer rule pages are the exceptions: CheckDocs compiles the README's samples, and the analyzer suite compiles each rule page's examples. Formatting still checks the F# sources under docs/.
// docs/maps holds the signal map model and its tests, which the .NET job runs.
const docsPatterns = [
  /^docs\/(?!maps\/|content\/analyzers\/)/,
  /^brand\//,
  /^\.claude\//,
  /^(?!experiments\/Ranvier\.View\.Portable\/README\.md$|docs\/content\/analyzers\/).*\.md$/i,
  /^LICENSE[^/]*$/,
  /^\.github\/FUNDING\.yml$/,
];

// True when every path is documentation; an empty or missing list is false, so unknown diffs run everything.
function docsOnly(paths) {
  const files = (paths ?? []).map(p => p.trim()).filter(Boolean);
  return files.length > 0 && files.every(file => docsPatterns.some(pattern => pattern.test(file)));
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

module.exports = { docsOnly, range };

if (require.main === module) {
  // node changes.cjs <event> <from> <to>: prints docs=true|false for $GITHUB_OUTPUT.
  const { execFileSync } = require('node:child_process');
  const [eventName, from, to] = process.argv.slice(2);
  const r = range(eventName === 'pull_request' ? { eventName, base: from, head: to } : { eventName, before: from, after: to });
  let docs = false;
  if (r) {
    const base = eventName === 'pull_request'
      ? execFileSync('git', ['merge-base', r.from, r.to], { encoding: 'utf8' }).trim()
      : r.from;
    docs = docsOnly(execFileSync('git', ['diff', '--name-only', '--no-renames', base, r.to], { encoding: 'utf8' }).split('\n'));
  }
  console.log(`docs=${docs}`);
}
