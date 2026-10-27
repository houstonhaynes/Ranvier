import assert from 'node:assert/strict';
import test from 'node:test';
import { generate } from './index.mjs';

export const manifest = {
  sugar: {
    schemaVersion: 2,
    namespaceName: 'Ranvier.Css',
    properties: [
      { cssName: 'width', memberName: 'width', scopes: [], acceptedMarkers: ['Length'], acceptedKeywords: ['auto'], obsolete: false },
      { cssName: 'opacity', memberName: 'opacity', scopes: [], acceptedMarkers: ['Number'], acceptedKeywords: [], obsolete: false },
      { cssName: 'display', memberName: 'display', scopes: [], acceptedMarkers: [], acceptedKeywords: ['block'], obsolete: false },
      { cssName: 'color', memberName: 'color', scopes: [], acceptedMarkers: [], acceptedKeywords: ['red'], obsolete: false }
    ],
    primitives: [
      { memberName: 'px', marker: 'Length', production: 'length', methodName: 'px', argumentType: 'float' },
      { memberName: 'number', marker: 'Number', production: 'number', methodName: 'create', argumentType: 'float' }
    ],
    keywords: ['auto', 'block', 'red'].map(token => ({ token, memberName: token }))
  }
};

for (const property of manifest.sugar.properties) property.acceptedValues = property.acceptedMarkers.map(marker => ({ marker, ranges: [] }));

test('real CSS declarations become typed sugar, retaining fallbacks and importance', () => {
  const result = generate('.card { width: 240px; display: block; color: red !important; width: auto; }', manifest);
  assert.match(result.source, /Css\.width \(Css\.px 240\.0\)/);
  assert.match(result.source, /Css\.display Css\.block/);
  assert.match(result.source, /Css\.color Css\.red.*Declaration\.important/);
  assert.ok(result.source.indexOf('240.0') < result.source.indexOf('Css.auto'));
  assert.equal(result.report.typedDeclarations, 4);
  assert.equal(result.report.fallbacks.length, 0);
});

test('custom properties and complex values remain explicit and ordered', () => {
  const result = generate('.card { --width: 10px; width: calc(var(--width) * 2); }', manifest);
  assert.match(result.source, /Declaration\.create "--width" "10px"/);
  assert.match(result.source, /calc\(var\(--width\) \* 2\)/);
  assert.equal(result.report.fallbacks.length, 2);
  assert.throws(() => generate('.card { width: calc(10px * 2); }', manifest, { strictTyped: true }), /typed/i);
});

test('nested conditions retain rule order; interleaved declarations are never hoisted', () => {
  const result = generate('@media (width > 1px) { .card { width: 2px; &:hover { width: 3px; } } }', manifest);
  assert.match(result.source, /Rule\.media/);
  assert.match(result.source, /Rule\.nested/);
  const mixed = generate('.a { width: 1px; &:hover { width: 2px; } width: 3px; }', manifest);
  assert.match(mixed.source, /Rule\.unsafeFromText/);
  assert.ok(mixed.report.fallbacks.some(item => item.kind === 'rule'));
});

test('syntax errors fail and cannot produce partial output', () => {
  assert.throws(() => generate('.card { width: 2px;', manifest), /unclosed/i);
});

test('comments, escaped selectors, and unknown at-rule bodies are preserved', () => {
  const result = generate('/*! license */ [data-x=";"] { width: 2px; } @future x { token { x: y } tail: z; }', manifest);
  assert.match(result.source, /license/);
  assert.match(result.source, /Rule\.unsafeFromText/);
  assert.match(result.source, /@future/);
});

test('F# literals escape control characters and names cannot inject source', () => {
  const result = generate('.a { --x: "line\\a break"; }', manifest);
  assert.match(result.source, /\\\\a/);
  assert.throws(() => generate('.a {}', manifest, { moduleName: 'Bad\nlet x = 1' }), /identifier/i);
});

test('numbers that would lose source precision use an explicit fallback', () => {
  const result = generate('.a { width: 9007199254740993px; opacity: .5; }', manifest);
  assert.equal(result.report.typedDeclarations, 1);
  assert.match(result.source, /9007199254740993px/);
});

test('inline comments, escaped CSS tokens and non-ASCII property names survive explicitly', () => {
  const result = generate('.a { width: 2px/*keep*/; --x: "\\n"; --🍏: 1px; }', manifest);
  assert.match(result.source, /Rule\.unsafeFromText/);
  assert.match(result.source, /\\\\n/);
  const valueComments = generate('.a { width: 2px/*keep*/; }', manifest);
  assert.match(valueComments.source, /keep/);
  assert.equal(valueComments.report.typedDeclarations, 0);
});

test('quoted stylesheet delimiters in at-rule heads remain structured', () => {
  const result = generate('@import url("a;b.css"); @import url("b.css");', manifest);
  assert.match(result.source, /Rule\.StatementRule/);
  assert.equal(result.report.fallbacks.length, 0);
});

test('canonical numeric range guards force fallback rather than generate a throwing module', () => {
  const guarded = structuredClone(manifest);
  guarded.sugar.properties[0].acceptedValues[0].ranges = [{ minimum: { kind: 'finite', value: '0', unitName: '' }, maximum: { kind: 'finite', value: '100', unitName: '' } }];
  const result = generate('.a { width: -1px; width: 101px; width: 42px; }', guarded);
  assert.equal(result.report.typedDeclarations, 1);
  assert.equal(result.report.fallbacks.length, 2);
  assert.throws(() => generate('.a { width: -1px; }', guarded, { strictTyped: true }), /Strict typed/);
});

test('escaped at-rule heads retain their tokens and fail strict conversion explicitly', () => {
  for (const css of ['@im\\port url(a.css);', '@m\\65 dia screen { .a { width: 2px; } }']) {
    const result = generate(css, manifest);
    assert.match(result.source, /Rule\.unsafeFromText/);
    assert.equal(result.report.fallbacks[0].kind, 'rule');
    if (css.startsWith('@im')) assert.match(result.source, /url\(a\.css\);/);
    assert.throws(() => generate(css, manifest, { strictTyped: true }), /Strict typed/);
  }
});

test('custom sugar namespaces still import the portable core first', () => {
  const custom = structuredClone(manifest);
  custom.sugar.namespaceName = 'Example';
  assert.match(generate('.a { width: 2px; }', custom).source, /open Ranvier\.Css\nopen Example/);
});

test('legacy declaration hacks cannot silently become ordinary typed declarations', () => {
  const result = generate('.a { *width: 42px; }', manifest);
  assert.match(result.source, /Rule\.unsafeFromText/);
  assert.match(result.source, /\*width/);
  assert.equal(result.report.typedDeclarations, 0);
});
