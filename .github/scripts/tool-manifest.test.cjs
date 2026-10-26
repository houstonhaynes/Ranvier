const test = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');

test('local tools restore the RID-specific Fickl package last on SDK 10.0.402', () => {
  const manifest = JSON.parse(readFileSync(join(__dirname, '../../.config/dotnet-tools.json'), 'utf8'));
  // Fresh-cache restores on SDK 10.0.402 resolve later tools as Fickl; --disable-parallel also fails.
  assert.equal(Object.keys(manifest.tools).at(-1), 'fickl', 'Keep RID-specific Fickl after portable tools');
  assert.equal(manifest.tools.fickl.version, '0.6.9');
  for (const name of ['fantomas', 'fsharp-analyzers', 'fickl']) {
    assert.deepEqual(manifest.tools[name].commands, [name]);
    assert.equal(manifest.tools[name].rollForward, false);
  }
});
