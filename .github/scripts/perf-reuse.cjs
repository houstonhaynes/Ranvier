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
  // The runtimes Compare.validate requires; node-jitless (the gated Node runtime) has no library-counter run.
  const runtimes = leg === 'arm64-counters' ? ['node', 'node-jitless', 'dotnet'] : ['node', 'dotnet'];
  for (const runtime of runtimes) {
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
      if (runtime === 'node-jitless') continue;
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

async function commitTree(github, repo, sha) {
  const { data } = await github.rest.git.getCommit({ ...repo, commit_sha: sha });
  return data.tree.sha;
}

// The newest completed Perf run whose two measurement artifacts compare the same base and head trees, as
// { run, base, head } with the commits that run measured, or null. A run of the head commit qualifies from any
// source; a run of another commit qualifies only from this repository, among the 100 newest Perf runs.
async function find({ github, repo, head, base, currentRun, read = a => readArchive(github, repo, a), treeOf = sha => commitTree(github, repo, sha) }) {
  const same = await github.paginate(github.rest.actions.listWorkflowRuns, { ...repo, workflow_id: 'perf.yml', head_sha: head, status: 'completed', per_page: 100 });
  const trees = new Map([[head, await treeOf(head)], [base, await treeOf(base)]]);
  const tree = async sha => {
    if (!trees.has(sha)) trees.set(sha, await treeOf(sha).catch(() => null));
    return trees.get(sha);
  };
  const { data: recent } = await github.rest.actions.listWorkflowRuns({ ...repo, workflow_id: 'perf.yml', status: 'completed', per_page: 100 });
  const full = `${repo.owner}/${repo.repo}`;
  const moved = (recent.workflow_runs ?? []).filter(r =>
    r.head_sha !== head && r.head_commit?.tree_id === trees.get(head) && r.head_repository?.full_name === full);
  const runs = [...same.filter(r => r.head_sha === head), ...moved].sort((a, b) => b.id - a.id);
  for (const run of runs) {
    if (run.id === Number(currentRun) || run.status !== 'completed') continue;
    const artifacts = await github.paginate(github.rest.actions.listWorkflowRunArtifacts, { ...repo, run_id: run.id, per_page: 100 });
    const pair = legs.map(leg => artifacts.find(a => a.name === `ab-${leg}` && !a.expired && a.size_in_bytes > 0 && Date.parse(a.expires_at) > Date.now()));
    if (pair.some(a => !a)) continue;
    try {
      const data = [];
      for (const artifact of pair) data.push(await read(artifact));
      const measured = { base: data[0].Base, head: data[0].Head };
      assert.equal(await tree(measured.head), trees.get(head), 'head tree differs');
      assert.equal(await tree(measured.base), trees.get(base), 'base tree differs');
      for (let i = 0; i < data.length; i++) usable(data[i], legs[i], measured.base, measured.head);
      return { run: String(run.id), ...measured };
    } catch (error) {
      console.log(`Run ${run.id} has unusable measurement artifacts: ${error.message}`);
    }
  }
  return null;
}

// The measurement restated for base and head. A "reused" label names the run and commits that produced it.
function relabel(data, { base, head, from }) {
  if (data.Base === base && data.Head === head) return data;
  const side = p => ({ ...p, Commit: p.Side === 'head' ? head : base });
  return {
    ...data,
    Base: base,
    Head: head,
    Processes: data.Processes.map(side),
    Counters: data.Counters.map(side),
    Versions: [...data.Versions, { Name: 'reused', Value: `run ${from}: ${data.Head} against ${data.Base}` }],
  };
}

module.exports = { find, usable, relabel };

if (require.main === module) {
  // node perf-reuse.cjs <dir> <base> <head> <run> relabels <dir>/ab-<leg>.json in place.
  const [dir, base, head, from] = process.argv.slice(2);
  for (const leg of legs) {
    const file = path.join(dir, `ab-${leg}.json`);
    fs.writeFileSync(file, JSON.stringify(relabel(JSON.parse(fs.readFileSync(file, 'utf8')), { base, head, from })));
  }
}
