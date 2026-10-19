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

test('keeps package versions, head-process ranges and nonzero library counters', () => fixture((root, runs) => {
  const sha = 'c'.repeat(40);
  const process = (side, value) => ({
    Side: side, Runtime: 'dotnet', Scenarios: [{ Scenario: 'chain', Engine: 'Ranvier', Metrics: [{ Name: 'instructions', Value: value }] }],
  });
  const counter = (side, value) => ({
    Side: side, Runtime: 'dotnet',
    Scenarios: [{ Scenario: 'chain', Engine: 'Ranvier', Metrics: [{ Name: 'lib:MemoRecomputes', Value: value }, { Name: 'lib:Flushes', Value: 0 }] }],
  });
  fs.writeFileSync(path.join(runs, `${sha}.json`), JSON.stringify({
    ...record(sha, '2026-10-16T10:00:00Z'),
    Packages: [{ Name: 'Ranvier', Version: '0.1.0-preview.5' }],
    Runs: [{
      Leg: 'arm64-counters',
      Processes: [process('head', 2460), process('head', 2470), process('head', 2490), process('base', 2400)],
      Counters: [counter('head', 4), counter('base', 5)],
    }],
  }));
  const output = path.join(root, 'index.json');

  buildIndex(runs, output);

  const [commit] = JSON.parse(fs.readFileSync(output, 'utf8')).commits;
  assert.deepEqual(commit.Packages, [{ Name: 'Ranvier', Version: '0.1.0-preview.5' }]);
  assert.deepEqual(commit.Medians, [
    { Leg: 'arm64-counters', Runtime: 'dotnet', Scenario: 'chain', Engine: 'Ranvier', Metric: 'instructions', Value: 2470, Min: 2460, Max: 2490 },
  ]);
  assert.deepEqual(commit.Counters, [{ Runtime: 'dotnet', Scenario: 'chain', Engine: 'Ranvier', Name: 'MemoRecomputes', Value: 4 }]);
}));

test('writes each scenario group beside the index, keeping every commit', () => fixture((root, runs) => {
  const sha = 'd'.repeat(40);
  const other = 'e'.repeat(40);
  const row = scenario => ({ Leg: 'arm64-counters', Side: 'head', Runtime: 'dotnet', Scenario: scenario, Engine: 'Ranvier', Metric: 'instructions', Value: 1 });
  fs.writeFileSync(path.join(runs, `${sha}.json`), JSON.stringify({
    Commit: sha, Date: '2026-10-16T10:00:00Z', Subject: 's', Medians: [row('chain'), row('chain-affinity'), row('create')],
    Runs: [{ Leg: 'arm64-counters', Counters: [{ Side: 'head', Runtime: 'dotnet', Scenarios: [
      { Scenario: 'chain', Engine: 'Ranvier', Metrics: [{ Name: 'lib:Flushes', Value: 2 }] },
      { Scenario: 'create', Engine: 'Ranvier', Metrics: [{ Name: 'lib:Flushes', Value: 3 }] },
    ] }] }],
  }));
  fs.writeFileSync(path.join(runs, `${other}.json`), JSON.stringify({ Commit: other, Date: '2026-10-17T10:00:00Z', Subject: 't', Medians: [row('create')] }));
  const output = path.join(root, 'out', 'index.json');

  buildIndex(runs, output, { signals: ['chain'] });

  const read = name => JSON.parse(fs.readFileSync(path.join(root, 'out', name), 'utf8')).commits;
  assert.equal(read('index.json')[0].Medians.length, 3);
  const [first, second] = read('signals.json');
  assert.deepEqual(first.Medians.map(median => median.Scenario), ['chain', 'chain-affinity']);
  assert.deepEqual(first.Counters.map(counter => counter.Scenario), ['chain']);
  assert.equal(second.Commit, other);
  assert.deepEqual(second.Medians, []);
}));

test('rejects a group that would overwrite the index or names no prefixes', () => fixture((root, runs) => {
  const sha = 'f'.repeat(40);
  fs.writeFileSync(path.join(runs, `${sha}.json`), JSON.stringify(record(sha, '2026-10-16T10:00:00Z')));
  assert.throws(() => buildIndex(runs, path.join(root, 'index.json'), { index: ['chain'] }), /would overwrite/);
  assert.throws(() => buildIndex(runs, path.join(root, 'index.json'), { signals: [] }), /requires scenario prefixes/);
}));

test('the committed group map is valid', () => {
  const groups = require('./perf-groups.json');
  assert.ok(Object.keys(groups).length > 0);
  for (const [name, prefixes] of Object.entries(groups)) {
    assert.match(name, /^[a-z0-9-]+$/);
    assert.ok(prefixes.length > 0 && prefixes.every(prefix => typeof prefix === 'string' && prefix));
  }
});

test('fails when the history contains no records', () => fixture((_root, runs) => {
  assert.throws(() => buildIndex(runs, path.join(runs, 'index.json')), /No performance records found/);
}));

test('fails when a record has no measurements for its commit', () => fixture((root, runs) => {
  const sha = 'a'.repeat(40);
  fs.writeFileSync(path.join(runs, `${sha}.json`), JSON.stringify(record(sha, '2026-10-16T10:00:00Z', 'base')));
  assert.throws(() => buildIndex(runs, path.join(root, 'index.json')), /No head measurements/);
}));
