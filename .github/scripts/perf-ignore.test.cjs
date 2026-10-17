const test = require('node:test');
const assert = require('node:assert/strict');
const { ignoreReason } = require('./perf-ignore.cjs');

test('footer trailer returns its reason, case-insensitively and beside other trailers', () => {
  assert.equal(ignoreReason('docs: fix typo\n\nPerf-Ignore: docs only\n'), 'docs only');
  assert.equal(ignoreReason('ci: x\r\n\r\nBody.\r\n\r\nCo-Authored-By: A <a@b>\r\nperf-ignore:  workflow change \r\n'), 'workflow change');
});
test('trailer outside the footer, in the subject or absent is not an ignore', () => {
  assert.equal(ignoreReason('docs: x\n\nPerf-Ignore: early\n\nCo-Authored-By: A <a@b>'), null);
  assert.equal(ignoreReason('Perf-Ignore: subject'), null);
  assert.equal(ignoreReason('feat: x\n\nMentions Perf-Ignore: inline in a sentence.'), null);
  assert.equal(ignoreReason(''), null);
  assert.equal(ignoreReason(undefined), null);
});
test('trailer without a reason is an error', () => {
  assert.throws(() => ignoreReason('docs: x\n\nPerf-Ignore:'), /requires a reason/);
});
