const test = require('node:test');
const assert = require('node:assert/strict');
const { hasProvenance, resolve, shouldDeploy } = require('./docs-perf.cjs');

const repo = { owner: 'owner', repo: 'ranvier' };
const repository = { full_name: 'owner/ranvier' };
const head = 'a'.repeat(40);
const provenance = { repository: repository.full_name, run: '123', head };
const perf = { id: 123, path: '.github/workflows/perf.yml', event: 'push', status: 'completed',
  conclusion: 'failure', head_branch: 'master', repository, head_repository: repository, head_sha: head };
const reporter = { id: 456, path: '.github/workflows/perf-report.yml', event: 'workflow_run',
  conclusion: 'success', repository, head_repository: repository };
function mock(run = perf, artifacts = []) {
  return { rest: { actions: { listWorkflowRunArtifacts: 'artifacts', getWorkflowRun: async options => {
    assert.equal(options.run_id, '123');
    assert.equal(options.owner, repo.owner);
    assert.equal(options.repo, repo.repo);
    return { data: run };
  } } }, paginate: async (method, options) => {
    assert.equal(method, 'artifacts');
    assert.equal(options.run_id, 456);
    return artifacts;
  } };
}

test('PR reporters and successful reporters without publication artifact skip docs', async () => {
  assert.equal(await hasProvenance({ github: mock(), repo, run: reporter }), false);
  assert.equal(await hasProvenance({ github: mock(perf, [{ name: 'perf-report' }]), repo, run: reporter }), false);
  assert.equal(await hasProvenance({ github: mock(perf, [{ name: 'docs-perf-provenance', expired: true }]), repo, run: reporter }), false);
});

test('only successful trusted reporter completions permit the publication artifact', async () => {
  const github = mock(perf, [{ name: 'docs-perf-provenance', expired: false }]);
  assert.equal(await hasProvenance({ github, repo, run: reporter }), true);
  for (const override of [{ conclusion: 'failure' }, { conclusion: 'cancelled' }, { event: 'push' },
    { path: '.github/workflows/other.yml' }, { repository: { full_name: 'attacker/fork' } },
    { head_repository: { full_name: 'attacker/fork' } }]) {
    assert.equal(await hasProvenance({ github, repo, run: { ...reporter, ...override } }), false);
  }
});

test('resolve uses the original measured SHA, including completed regression verdicts', async () => {
  assert.equal(await resolve({ github: mock(), repo, provenance }), head);
});

test('reject malformed and malicious provenance before fetching a run', async () => {
  let calls = 0;
  const github = { rest: { actions: { getWorkflowRun: () => { calls++; throw new Error('unexpected API call'); } } } };
  for (const override of [{ repository: 'attacker/fork' }, { run: '123\nref=master' },
    { run: 123 }, { run: '123\n' }, { head: 'master' }, { head: head + '\n' }]) {
    await assert.rejects(resolve({ github, repo, provenance: { ...provenance, ...override } }));
  }
  assert.equal(calls, 0);
});

test('reject PRs, other branches, repositories, workflows, pending runs and mismatched SHA', async () => {
  for (const override of [{ id: 124 }, { event: 'pull_request' }, { head_branch: 'feature' },
    { status: 'in_progress' }, { path: '.github/workflows/other.yml' }, { head_sha: 'b'.repeat(40) },
    { repository: { full_name: 'attacker/fork' } }, { head_repository: { full_name: 'attacker/fork' } }]) {
    await assert.rejects(resolve({ github: mock({ ...perf, ...override }), repo, provenance }));
  }
});

test('a slow older build cannot replace the newer automatic deployment', async () => {
  const newer = 'b'.repeat(40);
  let calls = 0;
  const github = { rest: { git: { getRef: async options => {
    calls++;
    assert.deepEqual(options, { ...repo, ref: 'heads/master' });
    return { data: { object: { sha: newer } } };
  } } } };
  const deployed = [];
  for (const measured of [newer, head]) {
    if (await shouldDeploy({ github, repo, eventName: 'workflow_run', measured })) deployed.push(measured);
  }
  assert.deepEqual(deployed, [newer]);
  assert.equal(calls, 2);
});

test('manual dispatch intentionally deploys its selected SHA without checking master', async () => {
  assert.equal(await shouldDeploy({ github: {}, repo, eventName: 'workflow_dispatch', measured: head }), true);
});

test('deployment is serialized without cancellation and checks freshness immediately before publication', () => {
  const fs = require('node:fs');
  const workflow = fs.readFileSync(require('node:path').join(__dirname, '../workflows/docs.yml'), 'utf8');
  const deploy = workflow.slice(workflow.indexOf('\n  deploy:'));
  assert.match(deploy, /needs: \[resolve, build\]/);
  assert.match(deploy, /concurrency:\s+group: pages\s+cancel-in-progress: false\s+queue: max/);
  assert.match(deploy, /MEASURED: \$\{\{ needs\.resolve\.outputs\.ref \}\}/);
  assert.match(deploy, /shouldDeploy\(\{github, repo: context\.repo,\s+eventName: context\.eventName, measured: process\.env\.MEASURED\}\)/);
  assert.match(deploy, /- id: deployment\s+if: steps\.current\.outputs\.deploy == 'true'\s+uses: actions\/deploy-pages@v5/);
  assert.equal(deploy.slice(deploy.indexOf('- id: current'), deploy.indexOf('- id: deployment')).includes('\n      - ', 1), false);
});
