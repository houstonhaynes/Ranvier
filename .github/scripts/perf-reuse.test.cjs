const test = require('node:test');
const assert = require('node:assert/strict');
const { find } = require('./perf-reuse.cjs');
const head = 'a'.repeat(40), base = 'b'.repeat(40);
function artifact(leg) {
  const run = { Leg: leg, Base: base, Head: head, Rounds: 5, Versions: [], NoBaseline: [], Processes: [], Counters: [] };
  for (const Runtime of ['node', 'dotnet']) for (const Side of ['base', 'head']) {
    const row = { Runtime, Side, Commit: Side === 'head' ? head : base, Scenarios: [{ Gc: false, Metrics: [{ Name: leg === 'arm64-counters' ? 'instructions' : 'cpu_ns', Value: 100 }] }] };
    for (let Process = 0; Process < 5; Process++) run.Processes.push({ ...row, Process });
    run.Counters.push({ ...row, Process: 0 });
  }
  return run;
}
test('latest completed failed verdict is reusable; incomplete expired malformed and wrong SHA are skipped', async () => {
  const runs = [9, 8, 7, 6, 5, 4].map(id => ({ id, head_sha: head, status: 'completed', conclusion: 'failure' }));
  const github = { rest: { actions: { listWorkflowRuns: 'runs', listWorkflowRunArtifacts: 'artifacts' } },
    paginate: async (which, options) => which === 'runs' ? runs : ['arm64-counters', 'x64-timing'].slice(0, options.run_id === 9 ? 1 : 2).map(leg => ({
      id: options.run_id, name: `ab-${leg}`, expired: options.run_id === 8, size_in_bytes: 200, expires_at: '2099-01-01T00:00:00Z'
    })) };
  const read = async a => {
    if (a.id === 7) throw new Error('bad zip');
    const data = artifact(a.name.slice(3));
    if (a.id === 6) data.Head = 'c'.repeat(40);
    return data;
  };
  assert.equal(await find({ github, repo: {}, head, base, currentRun: 10, read }), '5');
});
test('no usable pair requires remeasurement', async () => {
  const github = { rest: { actions: { listWorkflowRuns: 'runs', listWorkflowRunArtifacts: 'artifacts' } },
    paginate: async which => which === 'runs' ? [{ id: 1, head_sha: head, status: 'completed' }] : [] };
  assert.equal(await find({ github, repo: {}, head, base, currentRun: 2 }), '');
});
