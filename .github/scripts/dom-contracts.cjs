const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');

function normalized(file) {
  return fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n');
}

function checkContracts(root, nativeRoot, webkitRoot, python = 'python3') {
  execFileSync(python, ['-I', path.join(nativeRoot, 'gen/domgen.py'), 'check', '--source', webkitRoot],
    { cwd: nativeRoot, stdio: 'inherit' });
  const generated = path.join(nativeRoot, 'gen/Ranvier.Dom.fs');
  const committed = path.join(root, 'src/Ranvier.Dom/Generated/Dom.fs');
  if (normalized(generated) !== normalized(committed)) {
    throw new Error('Ranvier.Dom sources differ from the pinned WebCore generator output');
  }
  console.log('Shared DOM sources match the pinned IDL generator output.');
}

module.exports = { normalized, checkContracts };

if (require.main === module) {
  const [nativeRoot, webkitRoot, python = 'python3'] = process.argv.slice(2);
  if (!nativeRoot || !webkitRoot) throw new Error('Usage: dom-contracts.cjs <native checkout> <WebKit checkout> [python]');
  checkContracts(path.resolve(__dirname, '../..'), path.resolve(nativeRoot), path.resolve(webkitRoot), python);
}
