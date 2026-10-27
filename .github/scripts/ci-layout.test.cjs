const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const workflow = fs.readFileSync(path.join(__dirname, '../workflows/ci.yml'), 'utf8');
const jobs = Object.fromEntries([...workflow.slice(workflow.indexOf('\njobs:')).matchAll(/^  ([a-z][a-z-]*):\r?\n([\s\S]*?)(?=^  [a-z][a-z-]*:\r?$|(?![\s\S]))/gm)].map(([, id, body]) => [id, body]));

test('CSS specification, extraction and browser workloads run independently of core tests', () => {
  for (const id of ['dotnet', 'css-bindings', 'css-assets', 'css-browser']) assert.ok(jobs[id], `Missing ${id}`);
  const gates = {
    'css-bindings': ['Test CSS specification model and bindings', 'Compile generated CSS bindings and check type safety'],
    'css-assets': ['Check CSS extraction, modules and server output', 'Build automatic utility extraction fixture', 'Test automatic composite utility extraction', 'Check the composite utility example', 'Compile and extract the CSS documentation examples', 'Test the CSS source importer', 'Compile imported F# and check CSS round trips'],
    'css-browser': ['Compile CSS reactive browser checks', 'Check reactive CSS mount, updates, disposal and SSR identity', 'Check native CSS behavior in Chromium'],
    dotnet: ['Test Ranvier.Tests', 'Test Ranvier.CSharp.Tests', 'Test Ranvier.Wire.Tests', 'Test Ranvier.Persistence.Tests', 'Check the vendored Thoth.Json', 'Test Ranvier.Tests, traced', 'Test Ranvier.Docs.Maps.Tests, traced', 'Check package compatibility'],
  };
  for (const [id, names] of Object.entries(gates)) {
    assert.match(jobs[id], /needs: changes/);
    for (const name of names) {
      const owners = Object.entries(jobs).filter(([, body]) => body.split(/\r?\n/).some(line => line.trim() === `- name: ${name}`)).map(([owner]) => owner);
      assert.deepEqual(owners, [id], name);
    }
  }
  for (const id of ['css-bindings', 'css-assets', 'css-browser']) {
    assert.match(jobs[id], /actions\/setup-dotnet@/);
    assert.match(jobs[id], /actions\/setup-node@/);
  }
  assert.ok(jobs['css-assets'].indexOf('dotnet build tools/Ranvier.Css.Generate -c Release') < jobs['css-assets'].indexOf('node tools/Ranvier.Css.Import/verify-import.mjs'));
  assert.match(jobs['css-assets'], /dotnet build tools\/Ranvier\.Css\.Generate -c Release/);
  assert.match(jobs.scripts, /node --test .*compiler-rejections\.test\.mjs/);
});

test('service selection outputs are wired into the corresponding job conditions', () => {
  for (const service of ['marten', 'kurrentdb', 'equinox']) {
    assert.ok(jobs.changes.includes(`${service}: \u0024{{ steps.classify.outputs.${service} }}`));
    assert.ok(jobs[service].includes(`if: needs.changes.outputs.${service} == 'true'`));
  }
});

test('required CI result waits for every verification job and rejects failed or cancelled jobs', () => {
  const needed = /needs: \[([^\]]+)\]/.exec(jobs.result)[1].split(',').map(x => x.trim()).sort();
  assert.deepEqual(needed, Object.keys(jobs).filter(id => id !== 'result').sort());
  assert.match(jobs.result, /if: always\(\)/);
  assert.match(jobs.result, /success\|skipped\) ;; \*\) exit 1/);
});
