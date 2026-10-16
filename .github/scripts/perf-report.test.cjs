const test = require('node:test');
const assert = require('node:assert/strict');
const { publish, bodyFor } = require('./perf-report.cjs');
const sha = 'a'.repeat(40);
const run = { id: 23, head_sha: sha, event: 'pull_request', html_url: 'https://github.com/owner/repo/actions/runs/23' };
const metadata = { run: '23', head: sha, base: 'b'.repeat(40), pr: '12', event: 'pull_request', repository: 'owner/repo' };
const verdict = { Acceptance: { Unknown: [], Entries: [], All: false }, Verdicts: ['arm64-counters', 'x64-timing'].map(Leg => ({ Leg, Fails: false, Findings: [], Warnings: [] })) };
const report = () => ({ metadata: structuredClone(metadata), verdict: structuredClone(verdict), comment: '<!-- ranvier-perf -->\nPassed' });
function api(head = sha, comments = []) {
  const calls = [];
  return { calls, rest: { pulls: { get: async () => ({ data: { head: { sha: head }, base: { repo: { full_name: 'owner/repo' } } } }) }, issues: {
    listComments: () => {}, updateComment: async x => calls.push(['update', x]), createComment: async x => calls.push(['create', x])
  } }, paginate: async () => comments };
}
test('posts normal report and updates only the owned bot marker', async () => {
  const github = api(sha, [{ id: 1, user: { login: 'attacker', type: 'User' }, body: '<!-- ranvier-perf -->' },
    { id: 2, user: { login: 'github-actions[bot]', type: 'Bot' }, body: '<!-- ranvier-perf -->old' }]);
  await publish({ github, repo: { owner: 'owner', repo: 'repo' }, run, report: report() });
  assert.equal(github.calls[0][0], 'update');
  assert.equal(github.calls[0][1].comment_id, 2);
  assert.equal(github.calls[0][1].body, report().comment);
});
test('stale PR head never posts', async () => {
  const github = api('c'.repeat(40));
  await publish({ github, repo: { owner: 'owner', repo: 'repo' }, run, report: report() });
  assert.equal(github.calls.length, 0);
});
test('malformed PR, SHA, run provenance and verdict are rejected', async () => {
  for (const mutate of [r => r.metadata.pr = '12;echo bad', r => r.metadata.pr = 'x12', r => r.metadata.head = 'c'.repeat(40),
    r => r.metadata.run = '22', r => r.metadata.repository = 'other/repo', r => r.verdict.Verdicts.pop()]) {
    const data = report(); mutate(data);
    const github = api();
    await assert.rejects(publish({ github, repo: { owner: 'owner', repo: 'repo' }, run, report: data }));
    assert.equal(github.calls.length, 0);
  }
});
test('oversize UTF8 report keeps bounded verdict and artifact link', () => {
  const data = report(); data.comment += '\n' + '界'.repeat(24000);
  data.verdict.Verdicts[0].Fails = true;
  data.verdict.Acceptance.Entries = [{ Names: ['all'], Source: 'label', Reason: '界'.repeat(70000) }];
  const body = bodyFor(data, run);
  assert.ok(Buffer.byteLength(body, 'utf8') < 60000);
  assert.match(body, /FAILED/);
  assert.match(body, /arm64-counters/);
  assert.match(body, /Override declarations: 1/);
  assert.ok(body.includes(run.html_url));
  assert.equal(data.comment.length > body.length, true);
});
