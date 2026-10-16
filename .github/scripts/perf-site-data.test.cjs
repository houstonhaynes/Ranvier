const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { buildIndex } = require('./perf-site-data.cjs');

function fixture(fn) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'perf-site-data-test-'));
  const runs = path.join(root, 'runs');
  fs.mkdirSync(runs);
  try { fn(root, runs); } finally { fs.rmSync(root, { recursive: true, force: true }); }
}

function record(commit, date, side = 'head') {
  return {
    Commit: commit,
    Date: date,
    Subject: `commit ${commit.slice(0, 7)}`,
    Medians: [
      { Leg: 'arm64-counters', Side: side, Runtime: 'dotnet', Scenario: 'chain', Engine: 'Ranvier', Metric: 'instructions', Value: 2470 },
      { Leg: 'arm64-counters', Side: 'base', Runtime: 'dotnet', Scenario: 'chain', Engine: 'Ranvier', Metric: 'instructions', Value: 2500 },
    ],
  };
}

test('builds an ordered index containing head measurements for every recorded commit', () => fixture((root, runs) => {
  const later = 'b'.repeat(40);
  const earlier = 'a'.repeat(40);
  fs.writeFileSync(path.join(runs, `${later}.json`), JSON.stringify(record(later, '2026-10-16T10:00:00Z')));
  fs.writeFileSync(path.join(runs, `${earlier}.json`), JSON.stringify(record(earlier, '2026-10-15T10:00:00Z')));
  const output = path.join(root, 'static', 'perf-data', 'index.json');

  buildIndex(runs, output);

  const index = JSON.parse(fs.readFileSync(output, 'utf8'));
  assert.deepEqual(index.commits.map(commit => commit.Commit), [earlier, later]);
  assert.equal(index.commits[0].Medians.length, 1);
  assert.equal(index.commits[0].Medians[0].Value, 2470);
}));

test('fails when the history contains no records', () => fixture((_root, runs) => {
  assert.throws(() => buildIndex(runs, path.join(runs, 'index.json')), /No performance records found/);
}));

test('fails when a record has no measurements for its commit', () => fixture((root, runs) => {
  const sha = 'a'.repeat(40);
  fs.writeFileSync(path.join(runs, `${sha}.json`), JSON.stringify(record(sha, '2026-10-16T10:00:00Z', 'base')));
  assert.throws(() => buildIndex(runs, path.join(root, 'index.json')), /No head measurements/);
}));
