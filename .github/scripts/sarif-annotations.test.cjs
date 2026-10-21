const test = require('node:test');
const assert = require('node:assert/strict');
const { annotations } = require('./sarif-annotations.cjs');

const result = (extra) => ({
  ruleId: 'RAN001',
  message: { text: 'Fable Release compiles `seen` as a second read, 100%\nnext' },
  locations: [{ physicalLocation: { artifactLocation: { uri: 'tools/B.fs' }, region: { startLine: 4, startColumn: 9, endLine: 4, endColumn: 13 } } }],
  ...extra,
});

test('a result becomes an annotation at its 1-based SARIF columns', () => {
  assert.deepEqual(annotations({ runs: [{ results: [result({})] }] }), [
    '::warning file=tools/B.fs,line=4,col=9,endLine=4,endColumn=12,title=RAN001::Fable Release compiles `seen` as a second read, 100%25%0Anext',
  ]);
});

test('the SARIF level selects the annotation command', () => {
  assert.match(annotations({ runs: [{ results: [result({ level: 'error' })] }] })[0], /^::error /);
  assert.match(annotations({ runs: [{ results: [result({ level: 'note' })] }] })[0], /^::notice /);
});

test('a report without results has no annotations', () => {
  assert.deepEqual(annotations({ runs: [{ results: [] }] }), []);
  assert.deepEqual(annotations({}), []);
});
