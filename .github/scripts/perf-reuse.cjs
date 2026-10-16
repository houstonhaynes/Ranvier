const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const legs = ['arm64-counters', 'x64-timing'];

function usable(run, leg, base, head) {
  assert.equal(run.Leg, leg);
  assert.equal(run.Base, base);
  assert.equal(run.Head, head);
  assert.equal(run.Rounds, 5);
  assert.ok(Array.isArray(run.NoBaseline) && Array.isArray(run.Versions));
  for (const runtime of ['node', 'dotnet']) {
    for (const side of ['base', 'head']) {
      const rows = run.Processes.filter(p => p.Runtime === runtime && p.Side === side);
      if (side === 'base' && run.NoBaseline.some(n => n.Name === runtime && typeof n.Value === 'string')) {
        assert.equal(rows.length, 0);
        continue;
      }
      assert.deepEqual(rows.map(p => p.Process).sort(), [0, 1, 2, 3, 4]);
      for (const row of rows) {
        assert.equal(row.Commit, side === 'head' ? head : base);
        assert.ok(row.Scenarios.length > 0);
        for (const scenario of row.Scenarios) {
          assert.equal(typeof scenario.Gc, 'boolean');
          assert.ok(scenario.Metrics.some(m => m.Name === (leg === 'arm64-counters' ? 'instructions' : 'cpu_ns') && Number.isFinite(m.Value)));
          assert.ok(scenario.Metrics.every(m => Number.isFinite(m.Value)));
        }
      }
      const counters = run.Counters.filter(p => p.Side === side && p.Runtime === runtime);
      if (side === 'base' && run.Versions.some(v => v.Name === `base:${runtime}:counters` && v.Value.startsWith('unavailable:'))) continue;
      assert.equal(counters.length, 1);
      assert.equal(counters[0].Commit, side === 'head' ? head : base);
      assert.ok(counters[0].Scenarios.length > 0);
    }
  }
  return true;
}

async function readArchive(github, repo, artifact) {
  const { data } = await github.rest.actions.downloadArtifact({ ...repo, artifact_id: artifact.id, archive_format: 'zip' });
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'perf-reuse-'));
  const archive = path.join(directory, 'artifact.zip');
  try {
    fs.writeFileSync(archive, Buffer.from(data));
    // Read one named member; never extract paths or execute archive contents.
    return JSON.parse(execFileSync('unzip', ['-p', archive, `${artifact.name}.json`], { maxBuffer: 32 * 1024 * 1024, encoding: 'utf8' }));
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
}

async function find({ github, repo, head, base, currentRun, read = a => readArchive(github, repo, a) }) {
  const runs = await github.paginate(github.rest.actions.listWorkflowRuns, { ...repo, workflow_id: 'perf.yml', head_sha: head, status: 'completed', per_page: 100 });
  runs.sort((a, b) => b.id - a.id);
  for (const run of runs) {
    if (run.id === Number(currentRun) || run.head_sha !== head || run.status !== 'completed') continue;
    const artifacts = await github.paginate(github.rest.actions.listWorkflowRunArtifacts, { ...repo, run_id: run.id, per_page: 100 });
    const pair = legs.map(leg => artifacts.find(a => a.name === `ab-${leg}` && !a.expired && a.size_in_bytes > 0 && Date.parse(a.expires_at) > Date.now()));
    if (pair.some(a => !a)) continue;
    try {
      for (let i = 0; i < pair.length; i++) usable(await read(pair[i]), legs[i], base, head);
      return String(run.id);
    } catch (error) {
      console.log(`Run ${run.id} has unusable measurement artifacts: ${error.message}`);
    }
  }
  return '';
}
module.exports = { find, usable };
