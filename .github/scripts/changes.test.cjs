const test = require('node:test');
const assert = require('node:assert/strict');
const { docsOnly, armsAffected, range } = require('./changes.cjs');
const a = 'a'.repeat(40), b = 'b'.repeat(40), z = '0'.repeat(40);

test('documentation paths alone are docs only', () => {
  assert.equal(docsOnly(['docs/Site.fs', 'docs/guide/x.md', 'README.md', 'src/Ranvier/README.md', 'brand/og/x.svg', '.claude/rules/comments.md', 'LICENSE', '.github/FUNDING.yml', '']), true);
});
test('any other path, or no path, runs everything', () => {
  for (const p of [['docs/x.md', 'src/Ranvier/Core.fs'], ['.github/workflows/ci.yml'], ['build.fsx'], ['package.json'], ['src/docs/x.fs'], ['docs/maps/model/MapModel.fs'], ['docs/maps/tests/ModelTests.fs'], ['x.mdx'], ['src/Ranvier.View/README.md'], ['src/Ranvier.View.Anime/README.md'], [], undefined]) {
    assert.equal(docsOnly(p), false, JSON.stringify(p));
  }
});
test('the Motion examples app runs everything, its README aside', () => {
  for (const p of [['examples/Ranvier.Motion/src/Kanban.fs'], ['examples/Ranvier.Motion/tests/kanban.spec.mjs'], ['examples/Ranvier.Motion/package.json'], ['examples/Ranvier.Motion/kanban/kanban.css'], ['examples/Ranvier.Motion/Server/ServerChecks.fsproj']]) {
    assert.equal(docsOnly(p), false, JSON.stringify(p));
  }
  assert.equal(docsOnly(['examples/Ranvier.Motion/README.md']), true);
});
test('the Arms.Match job runs for the view, core, Playground and toolchain, and for an unknown diff', () => {
  for (const p of [['experiments/Ranvier.View.Arms/Cells.fs'], ['src/Ranvier/Core.fs'], ['src/Ranvier.View.Dom/Mounting.fs'], ['examples/Fable.Ranvier.Playground/tests/ArmsChecks.fs'], ['.config/dotnet-tools.json'], ['.github/workflows/ci.yml'], [], undefined]) {
    assert.equal(armsAffected(p), true, JSON.stringify(p));
  }
  for (const p of [['src/Ranvier.Wire/Codec.fs'], ['examples/Ranvier.Motion/src/Kanban.fs'], ['tools/Ranvier.Analyzers/Rules.fs'], ['build.fsx']]) {
    assert.equal(armsAffected(p), false, JSON.stringify(p));
  }
});
test('ranges need two commits; new branches and manual runs have none', () => {
  assert.deepEqual(range({ eventName: 'pull_request', base: a, head: b }), { from: a, to: b });
  assert.deepEqual(range({ eventName: 'push', before: a, after: b }), { from: a, to: b });
  assert.equal(range({ eventName: 'push', before: z, after: b }), null);
  assert.equal(range({ eventName: 'pull_request', base: 'master', head: b }), null);
  assert.equal(range({ eventName: 'workflow_dispatch', before: a, after: b }), null);
});
test('a master push is classified by its diff against the parent', () => {
  const { execFileSync } = require('node:child_process');
  const fs = require('node:fs'), os = require('node:os'), path = require('node:path');
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'changes-'));
  const git = (...args) => execFileSync('git', ['-c', 'user.name=t', '-c', 'user.email=t@t', ...args], { cwd: dir, encoding: 'utf8' }).trim();
  const commit = (file) => {
    fs.mkdirSync(path.dirname(path.join(dir, file)), { recursive: true });
    fs.appendFileSync(path.join(dir, file), 'x\n');
    git('add', '-A');
    git('commit', '-q', '-m', file);
    return git('rev-parse', 'HEAD');
  };
  const classify = (from, to) => execFileSync('node', [path.join(__dirname, 'changes.cjs'), 'push', from, to], { cwd: dir, encoding: 'utf8' }).trim();
  try {
    git('init', '-q');
    const root = commit('src/Core.fs');
    const docs = commit('docs/guide.md');
    const code = commit('src/Core.fs');
    assert.equal(classify(root, docs), 'docs=true\narms=false');
    assert.equal(classify(docs, code), 'docs=false\narms=false');
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
test('an analyzer rule page runs the analyzer suite, which compiles its examples', () => {
  assert.equal(docsOnly(['docs/content/analyzers/ran001.md']), false);
  assert.equal(docsOnly(['docs/content/analyzers/index.md', 'docs/content/guide/memos.md']), false);
});

test('server backend source changes run the hydration check', () => {
  for (const file of ['docs/samples/server-backends/Oxpecker/Program.fs', 'docs/samples/server-backends/Suave/Suave.fsproj']) {
    assert.equal(docsOnly([file]), false, file);
  }
  assert.equal(docsOnly(['docs/content/server/backends.md']), true);
});
