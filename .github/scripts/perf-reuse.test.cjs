const test = require('node:test');
const assert = require('node:assert/strict');
const { find, relabel } = require('./perf-reuse.cjs');
const head = 'a'.repeat(40), base = 'b'.repeat(40);
const repo = { owner: 'o', repo: 'r' };
function artifact(leg, h = head, b = base) {
  const run = { Leg: leg, Base: b, Head: h, Rounds: 5, Versions: [], NoBaseline: [], Processes: [], Counters: [] };
  const runtimes = leg === 'arm64-counters' ? ['node', 'node-jitless', 'dotnet'] : ['node', 'dotnet'];
  for (const Runtime of runtimes) for (const Side of ['base', 'head']) {
    const row = { Runtime, Side, Commit: Side === 'head' ? h : b, Scenarios: [{ Gc: false, Metrics: [{ Name: leg === 'arm64-counters' ? 'instructions' : 'cpu_ns', Value: 100 }] }] };
    for (let Process = 0; Process < 5; Process++) run.Processes.push({ ...row, Process });
    if (Runtime !== 'node-jitless') run.Counters.push({ ...row, Process: 0 });
  }
  return run;
}
function api({ same = [], recent = [], artifacts = () => ['arm64-counters', 'x64-timing'] }) {
  const list = Object.assign(async () => ({ data: { workflow_runs: recent } }), { tag: 'runs' });
  return { rest: { actions: { listWorkflowRuns: list, listWorkflowRunArtifacts: 'artifacts' } },
    paginate: async (which, options) => which === list ? same : artifacts(options.run_id).map(leg => ({
      id: options.run_id, name: `ab-${leg}`, expired: false, size_in_bytes: 200, expires_at: '2099-01-01T00:00:00Z'
    })) };
}
const trees = { [head]: 'T1', [base]: 'T0', ['c'.repeat(40)]: 'T1', ['d'.repeat(40)]: 'T0', ['e'.repeat(40)]: 'T9' };
const treeOf = async sha => { if (!(sha in trees)) throw new Error('unknown'); return trees[sha]; };

test('latest completed failed verdict is reusable; incomplete expired malformed and wrong SHA are skipped', async () => {
  const runs = [9, 8, 7, 6, 5, 4].map(id => ({ id, head_sha: head, status: 'completed', conclusion: 'failure' }));
  const github = api({ same: runs, artifacts: id => ['arm64-counters', 'x64-timing'].slice(0, id === 9 ? 1 : 2) });
  const paginate = github.paginate;
  github.paginate = async (which, options) => (await paginate(which, options)).map(a => ({ ...a, expired: options.run_id === 8 }));
  const read = async a => {
    if (a.id === 7) throw new Error('bad zip');
    const data = artifact(a.name.slice(3));
    if (a.id === 6) data.Head = 'e'.repeat(40);
    return data;
  };
  assert.deepEqual(await find({ github, repo, head, base, currentRun: 10, read, treeOf }), { run: '5', base, head });
});
test('no usable pair requires remeasurement', async () => {
  const github = api({ same: [{ id: 1, head_sha: head, status: 'completed' }], artifacts: () => [] });
  assert.equal(await find({ github, repo, head, base, currentRun: 2, treeOf }), null);
});
test('another commit with the same base and head trees is reusable from this repository only', async () => {
  const c = 'c'.repeat(40), d = 'd'.repeat(40);
  const own = { id: 3, head_sha: c, status: 'completed', head_commit: { tree_id: 'T1' }, head_repository: { full_name: 'o/r' } };
  const fork = { ...own, id: 4, head_repository: { full_name: 'x/r' } };
  const other = { ...own, id: 5, head_commit: { tree_id: 'T7' } };
  const github = api({ recent: [fork, other, own] });
  const read = async a => artifact(a.name.slice(3), c, d);
  assert.deepEqual(await find({ github, repo, head, base, currentRun: 9, read, treeOf }), { run: '3', base: d, head: c });
});
test('a matching head tree over a different base tree requires remeasurement', async () => {
  const c = 'c'.repeat(40), e = 'e'.repeat(40);
  const own = { id: 3, head_sha: c, status: 'completed', head_commit: { tree_id: 'T1' }, head_repository: { full_name: 'o/r' } };
  const read = async a => artifact(a.name.slice(3), c, e);
  assert.equal(await find({ github: api({ recent: [own] }), repo, head, base, currentRun: 9, read, treeOf }), null);
});
test('relabel restates commits and records the source run', () => {
  const c = 'c'.repeat(40), d = 'd'.repeat(40);
  const data = relabel(artifact('x64-timing', c, d), { base, head, from: '3' });
  assert.equal(data.Head, head);
  assert.equal(data.Base, base);
  assert.ok([...data.Processes, ...data.Counters].every(p => p.Commit === (p.Side === 'head' ? head : base)));
  assert.deepEqual(data.Versions, [{ Name: 'reused', Value: `run 3: ${c} against ${d}` }]);
  const same = artifact('x64-timing');
  assert.equal(relabel(same, { base, head, from: '3' }), same);
});
test('an arm64 artifact without jitless Node processes is not reusable', async () => {
  const github = api({ same: [{ id: 1, head_sha: head, status: 'completed', conclusion: 'success' }] });
  const read = async a => {
    const data = artifact(a.name.slice(3));
    data.Processes = data.Processes.filter(p => p.Runtime !== 'node-jitless');
    return data;
  };
  assert.equal(await find({ github, repo, head, base, currentRun: 2, read, treeOf }), null);
});
