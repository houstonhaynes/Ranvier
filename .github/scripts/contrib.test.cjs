const test = require('node:test');
const assert = require('node:assert/strict');
const { headerProblem, check } = require('./contrib.cjs');

test('conventional headers pass', () => {
  for (const s of ['feat: x', 'fix(core): x', 'chore!: drop net8', 'perf(fable/core): x\n\nbody', 'bench(counters): add a variant', 'ci(perf-report): x']) {
    assert.equal(headerProblem(s), null, s);
  }
});
test('other headers fail', () => {
  for (const s of ['Add collapsible maps', 'feat:x', 'feat : x', 'Feat: x', 'feature: x', 'feat(): x', 'feat(Core): x', 'fix(core):  ', '', undefined, '[skip ci]']) {
    assert.notEqual(headerProblem(s), null, String(s));
  }
});
test('only the title is checked, and bots are exempt', async () => {
  const run = async (title, type = 'User') => {
    const failed = [];
    const core = { setFailed: m => failed.push(m), notice: () => {} };
    await check({ context: { payload: { pull_request: { title, user: { type, login: 'x' } } } }, core });
    return failed;
  };
  assert.deepEqual(await run('fix(ci): x'), []);
  const failed = await run('Update stuff');
  assert.equal(failed.length, 1);
  assert.match(failed[0], /^Title "Update stuff"/);
  assert.deepEqual(await run('Update stuff', 'Bot'), []);
});
