const test = require('node:test');
const assert = require('node:assert/strict');
const { trustedCI, artifactAvailable, orderPackages, missingPackages, resolveRelease, getJson } = require('./release.cjs');

test('publish dependencies before their dependants even when filenames sort the other way', () => {
  const packages = [
    { id: 'Adapter', version: '1.0.0', dependencies: [{ id: 'Core', range: '[1.0.0]' }] },
    { id: 'Core', version: '1.0.0', dependencies: [] },
  ];
  assert.deepEqual(orderPackages(packages).map(p => p.id), ['Core', 'Adapter']);
  assert.throws(() => orderPackages([
    { id: 'A', version: '1.0.0', dependencies: [{ id: 'B', range: '1.0.0' }] },
    { id: 'B', version: '1.0.0', dependencies: [{ id: 'A', range: '1.0.0' }] },
  ]), /cycle/i);
});

test('missing versions include new IDs and new versions, and omit existing versions case-insensitively', async () => {
  const packages = [{ id: 'Core', version: '1.0.0' }, { id: 'Core.Traced', version: '1.0.0-PREVIEW.1' }, { id: 'New', version: '0.1.0' }];
  const get = async url => url.includes('core.traced') ? { versions: ['1.0.0-preview.1'] }
    : url.includes('/core/') ? { versions: ['0.9.0'] } : null;
  assert.deepEqual((await missingPackages(packages, get)).map(p => p.id), ['Core', 'New']);
  await assert.rejects(missingPackages(packages, async () => { throw new Error('feed unavailable'); }), /feed unavailable/);
});

const own = 'ranvier-org/Ranvier';
const sha = 'a'.repeat(40);
const successful = { conclusion: 'success', event: 'push', path: '.github/workflows/ci.yml', head_branch: 'master', head_sha: sha, head_repository: { full_name: own } };

test('automatic publishing plans all successful master artifacts even when the bump precedes the final commit', async () => {
  assert.deepEqual(await resolveRelease({ eventName: 'workflow_run', repository: own, run: successful }, async () => true,
    ['src/Ranvier/Ranvier.fsproj']), { ref: sha, pack: true, publish: true });
  assert.equal((await resolveRelease({ eventName: 'workflow_run', repository: own, run: successful }, async () => true,
    ['docs/content/guide/index.md'])).pack, true);
  for (const run of [{ ...successful, conclusion: 'failure' }, { ...successful, head_branch: 'feature' },
    { ...successful, head_repository: { full_name: 'other/Ranvier' } }, { ...successful, event: 'pull_request' },
    { ...successful, path: '.github/workflows/other.yml' }]) {
    await assert.rejects(resolveRelease({ eventName: 'workflow_run', repository: own, run }, async () => true, []));
  }
});

test('only exact tested master revisions from CI push or manual runs supply publish artifacts', () => {
  assert.equal(trustedCI(successful, own, sha), true);
  assert.equal(trustedCI({ ...successful, event: 'workflow_dispatch' }, own, sha), true);
  assert.equal(trustedCI({ ...successful, event: 'pull_request' }, own, sha), false);
  assert.equal(trustedCI(successful, own, 'b'.repeat(40)), false);
  assert.equal(trustedCI({ ...successful, path: '.github/workflows/other.yml' }, own, sha), false);
});

test('docs-only CI skips publication; an absent or expired package artifact from a pack run fails', () => {
  assert.equal(artifactAvailable([{ name: 'nuget-packages', expired: false }], [], true), true);
  assert.equal(artifactAvailable([], [{ name: 'Packages', conclusion: 'skipped' }], true), false);
  assert.throws(() => artifactAvailable([], [{ name: 'Packages', conclusion: 'success' }], true), /artifact/);
  assert.throws(() => artifactAvailable([{ name: 'nuget-packages', expired: true }], [], false), /artifact/);
});

test('feed errors fail closed; only HTTP 404 means missing', async () => {
  const original = global.fetch;
  try {
    global.fetch = async () => new Response('', { status: 404 });
    assert.equal(await getJson('https://example.invalid'), null);
    global.fetch = async () => new Response('', { status: 503 });
    await assert.rejects(getJson('https://example.invalid'), /HTTP 503/);
    await assert.rejects(missingPackages([{ id: 'A', version: '1.0.0' }], async () => ({})), /Invalid version listing/);
  } finally { global.fetch = original; }
});

test('manual publication requires master and passing CI at the exact revision; dry runs carry no publishing permission', async () => {
  const manual = { eventName: 'workflow_dispatch', repository: own, ref: 'refs/heads/master', sha, dryRun: false };
  assert.deepEqual(await resolveRelease(manual, async requested => requested === sha, []), { ref: sha, pack: true, publish: true });
  await assert.rejects(resolveRelease(manual, async () => false, []), /CI/);
  await assert.rejects(resolveRelease({ ...manual, ref: 'refs/heads/feature' }, async () => true, []), /master/);
  assert.deepEqual(await resolveRelease({ ...manual, ref: 'refs/heads/feature', dryRun: true }, async () => false, []),
    { ref: sha, pack: true, publish: false });
});
