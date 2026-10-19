const assert = require('node:assert/strict');

const artifactName = 'docs-perf-provenance';

async function hasProvenance({ github, repo, run }) {
  if (run.conclusion !== 'success' || run.event !== 'workflow_run'
      || run.path !== '.github/workflows/perf-report.yml'
      || run.repository?.full_name !== `${repo.owner}/${repo.repo}`
      || run.head_repository?.full_name !== `${repo.owner}/${repo.repo}`) return false;
  const artifacts = await github.paginate(github.rest.actions.listWorkflowRunArtifacts, { ...repo, run_id: run.id });
  return artifacts.some(a => a.name === artifactName && !a.expired);
}

async function resolve({ github, repo, provenance }) {
  assert.equal(provenance.repository, `${repo.owner}/${repo.repo}`);
  assert.match(provenance.head, /^[0-9a-f]{40}$/);
  assert.equal(provenance.head.length, 40);
  assert.match(provenance.run, /^[1-9][0-9]*$/);
  assert.equal(provenance.run.trim(), provenance.run);
  assert.equal(typeof provenance.recorded, 'boolean');
  const { data: run } = await github.rest.actions.getWorkflowRun({ ...repo, run_id: provenance.run });
  assert.equal(String(run.id), provenance.run);
  assert.equal(run.path, '.github/workflows/perf.yml');
  assert.equal(run.event, 'push');
  assert.equal(run.status, 'completed');
  assert.equal(run.head_branch, 'master');
  assert.equal(run.repository.full_name, provenance.repository);
  assert.equal(run.head_repository.full_name, provenance.repository);
  assert.equal(run.head_sha, provenance.head);
  // A run without a history record skipped measurement and succeeded.
  if (!provenance.recorded) assert.equal(run.conclusion, 'success');
  return run.head_sha;
}

async function shouldDeploy({ github, repo, eventName, measured }) {
  if (eventName === 'workflow_dispatch') return true;
  assert.equal(eventName, 'workflow_run');
  assert.match(measured, /^[0-9a-f]{40}$/);
  assert.equal(measured.length, 40);
  const { data } = await github.rest.git.getRef({ ...repo, ref: 'heads/master' });
  return data.object.sha === measured;
}

module.exports = { hasProvenance, resolve, shouldDeploy };
