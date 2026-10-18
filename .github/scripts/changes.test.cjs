const test = require('node:test');
const assert = require('node:assert/strict');
const { docsOnly, range } = require('./changes.cjs');
const a = 'a'.repeat(40), b = 'b'.repeat(40), z = '0'.repeat(40);

test('documentation paths alone are docs only', () => {
  assert.equal(docsOnly(['docs/Site.fs', 'docs/guide/x.md', 'README.md', 'src/Ranvier/README.md', 'brand/og/x.svg', '.claude/rules/comments.md', 'LICENSE', '.github/FUNDING.yml', '']), true);
});
test('any other path, or no path, runs everything', () => {
  for (const p of [['docs/x.md', 'src/Ranvier/Core.fs'], ['.github/workflows/ci.yml'], ['build.fsx'], ['package.json'], ['src/docs/x.fs'], ['x.mdx'], [], undefined]) {
    assert.equal(docsOnly(p), false, JSON.stringify(p));
  }
});
test('ranges need two commits; new branches and manual runs have none', () => {
  assert.deepEqual(range({ eventName: 'pull_request', base: a, head: b }), { from: a, to: b });
  assert.deepEqual(range({ eventName: 'push', before: a, after: b }), { from: a, to: b });
  assert.equal(range({ eventName: 'push', before: z, after: b }), null);
  assert.equal(range({ eventName: 'pull_request', base: 'master', head: b }), null);
  assert.equal(range({ eventName: 'workflow_dispatch', before: a, after: b }), null);
});
