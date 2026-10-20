const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const marker = '<!-- ranvier-perf -->';
const legs = ['arm64-counters', 'x64-timing'];
const shaPattern = /^[0-9a-f]{40}$/;

function validate(report, run, repo) {
  const m = report.metadata;
  assert.match(m.pr, /^[1-9][0-9]*$/);
  assert.ok(Number.isSafeInteger(Number(m.pr)));
  assert.match(m.head, shaPattern);
  assert.match(m.base, shaPattern);
  assert.equal(m.head, run.head_sha);
  assert.equal(m.run, String(run.id));
  assert.equal(m.event, 'pull_request');
  assert.equal(run.event, 'pull_request');
  assert.equal(m.repository, `${repo.owner}/${repo.repo}`);
  assert.ok(report.comment.startsWith(marker));
  const { Acceptance, Verdicts } = report.verdict;
  assert.ok(Array.isArray(Acceptance.Unknown) && Array.isArray(Acceptance.Entries));
  assert.equal(typeof Acceptance.All, 'boolean');
  assert.deepEqual(Verdicts.map(v => v.Leg).sort(), [...legs].sort());
  for (const v of Verdicts) {
    assert.equal(typeof v.Fails, 'boolean');
    assert.ok(Array.isArray(v.Findings) && Array.isArray(v.Warnings));
    for (const f of v.Findings) {
      assert.ok(['Regressed', 'Improved', 'Unchanged', 'Added', 'Removed', 'CounterChanged', 'NoBaseline', 'StateShift', 'Inconclusive'].includes(f.Kind));
      assert.equal(typeof f.Accepted, 'boolean');
    }
  }
}

function bodyFor(report, run) {
  if (Buffer.byteLength(report.comment, 'utf8') <= 60000) return report.comment;
  const { Acceptance, Verdicts } = report.verdict;
  const failed = Acceptance.Unknown.length > 0 || Verdicts.some(v => v.Fails);
  const rows = Verdicts.map(v => {
    // Findings with Gated false (tiered Node on arm64) are reported only; a verdict.json without the field counts every finding.
    const count = kind => v.Findings.filter(f => f.Kind === kind && f.Gated !== false).length;
    return `| ${v.Leg} | ${v.Fails ? 'FAILED' : 'passed'} | ${count('Regressed')} | ${count('Improved')} | ${v.Warnings.length} |`;
  });
  // Only the API-provided run URL enters the fallback link; artifact text cannot supply it.
  assert.match(run.html_url, /^https:\/\/github\.com\/[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+\/actions\/runs\/[0-9]+$/);
  return [marker, `## Performance: ${failed ? 'FAILED' : 'passed'}`, '',
    '| Leg | Verdict | Regressions | Improvements | Warnings |', '|---|---|---:|---:|---:|', ...rows, '',
    `Override declarations: ${Acceptance.Entries.length}; accept all: ${Acceptance.All}; invalid names: ${Acceptance.Unknown.length}.`,
    '', `The complete report exceeds the comment size limit. [Download perf-report for full tables and override reasons](${run.html_url}).`].join('\n');
}

async function publish({ github, repo, run, report }) {
  validate(report, run, repo);
  const issue_number = Number(report.metadata.pr);
  const { data: pr } = await github.rest.pulls.get({ ...repo, pull_number: issue_number });
  assert.equal(pr.base.repo.full_name, `${repo.owner}/${repo.repo}`);
  if (pr.head.sha !== run.head_sha) return;
  const comments = await github.paginate(github.rest.issues.listComments, { ...repo, issue_number, per_page: 100 });
  const owned = comments.find(c => c.user?.type === 'Bot' && c.user.login === 'github-actions[bot]' && c.body?.startsWith(marker));
  const body = bodyFor(report, run);
  if (owned) await github.rest.issues.updateComment({ ...repo, comment_id: owned.id, body });
  else await github.rest.issues.createComment({ ...repo, issue_number, body });
}

function readReport(directory) {
  const json = name => JSON.parse(fs.readFileSync(path.join(directory, name), 'utf8'));
  return { metadata: json('provenance.json'), verdict: json('verdict.json'), comment: fs.readFileSync(path.join(directory, 'comment.md'), 'utf8') };
}

module.exports = { publish, bodyFor, readReport };
