const test = require('node:test');
const assert = require('node:assert/strict');
const { headerProblem, commitProblems } = require('./contrib.cjs');

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
test('merge commits are exempt and the title is checked', () => {
  const problems = commitProblems({
    title: 'Update stuff',
    commits: [
      { sha: '1'.repeat(40), message: 'fix: x', parents: [{}] },
      { sha: '2'.repeat(40), message: "Merge branch 'master' into feat", parents: [{}, {}] },
      { sha: '3'.repeat(40), message: 'wip', parents: [{}] },
    ],
  });
  assert.equal(problems.length, 2);
  assert.match(problems[0], /^Title /);
  assert.match(problems[1], /^Commit 3333333 /);
});
