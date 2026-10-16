const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { publishHistory } = require('./perf-history.cjs');
function fixture(fn) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'perf-history-test-'));
  const file = path.join(directory, 'record.json');
  fs.writeFileSync(file, JSON.stringify({ Commit: 'a'.repeat(40) }));
  try { fn(file); } finally { fs.rmSync(directory, { recursive: true, force: true }); }
}
test('retry fetches competing records and regenerates timeline without replacing history', () => fixture(file => {
  let pushes = 0, timelines = 0;
  const execute = (exe, args) => {
    const ok = stdout => ({ status: 0, stdout, stderr: '' });
    if (exe === 'dotnet') {
      timelines++;
      const runs = args[args.indexOf('--runs') + 1];
      assert.equal(fs.existsSync(path.join(runs, `${'b'.repeat(40)}.json`)), true);
      if (pushes > 0) assert.equal(fs.existsSync(path.join(runs, `${'c'.repeat(40)}.json`)), true);
      fs.writeFileSync(args[args.indexOf('--out') + 1], 'timeline');
      return ok('');
    }
    if (args[0] === 'rev-parse') return ok('d'.repeat(40));
    if (args[0] === 'ls-tree') return ok(`runs/${'b'.repeat(40)}.json\n` + (pushes ? `runs/${'c'.repeat(40)}.json\n` : ''));
    if (args[0] === 'show') return ok('{}');
    if (args[0] === 'push') return { status: ++pushes === 1 ? 1 : 0, stdout: '', stderr: 'non-fast-forward' };
    return ok('e'.repeat(40));
  };
  publishHistory(file, 'tool.dll', execute);
  assert.equal(pushes, 2); assert.equal(timelines, 2);
}));
test('network failure is never treated as absent history', () => fixture(file => {
  let calls = 0;
  assert.throws(() => publishHistory(file, 'tool.dll', () => { calls++; return { status: 128, stderr: 'network failure' }; }), /Cannot inspect/);
  assert.equal(calls, 1);
}));
test('absent branch initializes an orphan index and adds only record and timeline', () => fixture(file => {
  const staged = [];
  let empty = false;
  publishHistory(file, 'tool.dll', (exe, args) => {
    if (exe === 'dotnet') fs.writeFileSync(args[args.indexOf('--out') + 1], 'timeline');
    if (args[0] === 'ls-remote') return { status: 2, stdout: '', stderr: '' };
    if (args[0] === 'read-tree') empty = args[1] === '--empty';
    if (args[0] === 'update-index') staged.push(args.at(-1).split(',').at(-1));
    return { status: 0, stdout: 'f'.repeat(40), stderr: '' };
  });
  assert.equal(empty, true);
  assert.deepEqual(staged, [`runs/${'a'.repeat(40)}.json`, 'timeline.md']);
}));
