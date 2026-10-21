const test = require('node:test');
const assert = require('node:assert/strict');
const { compareVersions, parseRange, satisfies, readPackage, plan } = require('./feed.cjs');

// A zip archive of stored entries.
function zip(entries) {
  const locals = [], centrals = [];
  let offset = 0;
  for (const [name, content] of Object.entries(entries)) {
    const data = Buffer.from(content), fileName = Buffer.from(name);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt32LE(data.length, 18);
    local.writeUInt32LE(data.length, 22);
    local.writeUInt16LE(fileName.length, 26);
    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt32LE(data.length, 20);
    central.writeUInt32LE(data.length, 24);
    central.writeUInt16LE(fileName.length, 28);
    central.writeUInt32LE(offset, 42);
    locals.push(local, fileName, data);
    centrals.push(central, fileName);
    offset += local.length + fileName.length + data.length;
  }
  const directory = Buffer.concat(centrals);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(centrals.length / 2, 8);
  end.writeUInt16LE(centrals.length / 2, 10);
  end.writeUInt32LE(directory.length, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, directory, end]);
}

const pkg = (id, version, dependencies = {}) => ({
  file: `${id}.${version}.nupkg`, id, version,
  dependencies: Object.entries(dependencies).map(([dependency, range]) => ({ id: dependency, range })),
});

test('versions order by SemVer precedence', () => {
  assert.ok(compareVersions('0.1.0-alpha.1', '0.1.0-alpha.2') < 0);
  assert.ok(compareVersions('0.1.0-alpha.10', '0.1.0-alpha.2') > 0);
  assert.ok(compareVersions('0.1.0-preview.6', '0.1.0') < 0);
  assert.ok(compareVersions('0.2.1', '0.2.1') === 0);
  assert.ok(compareVersions('8.0.100', '10.1.401') < 0);
});
test('ranges read as minimum, exact or bounded', () => {
  assert.deepEqual(parseRange('0.2.1'), { min: '0.2.1', minInclusive: true, max: null, maxInclusive: false });
  assert.deepEqual(parseRange('[0.1.0-alpha.1]'), { min: '0.1.0-alpha.1', minInclusive: true, max: '0.1.0-alpha.1', maxInclusive: true });
  assert.deepEqual(parseRange('[1.0, 2.0)'), { min: '1.0', minInclusive: true, max: '2.0', maxInclusive: false });
  assert.ok(satisfies('0.2.3', '0.2.1'));
  assert.ok(!satisfies('0.1.0', '0.2.1'));
  assert.ok(satisfies('0.1.0-alpha.1', '[0.1.0-alpha.1]'));
  assert.ok(!satisfies('0.1.0-alpha.2', '[0.1.0-alpha.1]'));
  assert.ok(!satisfies('2.0', '[1.0, 2.0)'));
});
test('a nuspec yields its id, version and distinct dependencies', () => {
  const group = '<dependency id="Ranvier" version="0.1.0-preview.6" exclude="Build,Analyzers" />';
  const nuspec = `<package><metadata><id>Lib</id><version>0.1.0-alpha.1</version><dependencies><group targetFramework="net10.0">${group}</group><group targetFramework="net8.0">${group}</group></dependencies></metadata></package>`;
  assert.deepEqual(readPackage(zip({ 'Lib.nuspec': nuspec })), {
    id: 'Lib', version: '0.1.0-alpha.1', dependencies: [{ id: 'Ranvier', range: '0.1.0-preview.6' }],
  });
});
test('a dependency on the feed or in the same push is satisfied', () => {
  const published = new Map([['fsharp.core', ['8.0.100', '10.1.401']], ['ranvier', ['0.1.0-preview.6']]]);
  const result = plan([
    pkg('Ranvier.Elmish', '0.1.0-preview.5', { Ranvier: '0.1.0-preview.6', 'FSharp.Core': '8.0.100' }),
    pkg('Ranvier.View', '0.1.0-alpha.1', { Ranvier: '0.1.0-preview.6' }),
    pkg('Ranvier.View.Dom', '0.1.0-alpha.1', { 'Ranvier.View': '[0.1.0-alpha.1]' }),
  ], published);
  assert.deepEqual(result, {
    push: ['Ranvier.Elmish.0.1.0-preview.5.nupkg', 'Ranvier.View.0.1.0-alpha.1.nupkg', 'Ranvier.View.Dom.0.1.0-alpha.1.nupkg'],
    refused: [],
  });
});
test('a dependency missing from the feed refuses its package and every package that depends on it', () => {
  const published = new Map([['ranvier', ['0.1.0-preview.6']], ['xantham.fable.core.ts', ['0.1.0']]]);
  const result = plan([
    pkg('Ranvier', '0.1.0-preview.6'),
    pkg('Ranvier.View', '0.1.0-alpha.1', { Ranvier: '0.1.0-preview.6', 'Xantham.Fable.Core.TS': '0.2.1' }),
    pkg('Ranvier.View.Dom', '0.1.0-alpha.1', { 'Ranvier.View': '[0.1.0-alpha.1]' }),
  ], published);
  assert.deepEqual(result.push, ['Ranvier.0.1.0-preview.6.nupkg']);
  assert.deepEqual(result.refused, [
    { file: 'Ranvier.View.0.1.0-alpha.1.nupkg', missing: [{ id: 'Xantham.Fable.Core.TS', range: '0.2.1' }] },
    { file: 'Ranvier.View.Dom.0.1.0-alpha.1.nupkg', missing: [{ id: 'Ranvier.View', range: '[0.1.0-alpha.1]' }] },
  ]);
});
test('a refusal lists every dependency the final push lacks', () => {
  const result = plan([
    pkg('A.Traced', '1.0.0', { Missing: '1.0.0' }),
    pkg('B.Anime.Traced', '1.0.0', { 'A.Traced': '[1.0.0]', 'C.Traced': '[1.0.0]' }),
    pkg('C.Traced', '1.0.0', { Missing: '1.0.0' }),
  ], new Map());
  assert.deepEqual(result.refused[1], {
    file: 'B.Anime.Traced.1.0.0.nupkg', missing: [{ id: 'A.Traced', range: '[1.0.0]' }, { id: 'C.Traced', range: '[1.0.0]' }],
  });
});
test('a sibling in the push at another version than its pin does not satisfy it', () => {
  const result = plan([
    pkg('Ranvier.View', '0.1.0-alpha.2'),
    pkg('Ranvier.View.Dom', '0.1.0-alpha.1', { 'Ranvier.View': '[0.1.0-alpha.1]' }),
  ], new Map());
  assert.deepEqual(result.refused.map(r => r.file), ['Ranvier.View.Dom.0.1.0-alpha.1.nupkg']);
});
