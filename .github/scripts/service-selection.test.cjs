const test = require('node:test');
const assert = require('node:assert/strict');
const { servicesFor, docsOnly } = require('./changes.cjs');
const all = { marten: true, kurrentdb: true, equinox: true };
const none = { marten: false, kurrentdb: false, equinox: false };

test('UI and CSS pull requests do not start unrelated database containers', () => {
  for (const file of ['src/Ranvier.Css/Values.fs', 'src/Ranvier.Css.View/Bindings.fs', 'tools/Ranvier.Css.Generate/Generation.fs', 'tools/Ranvier.Css.Import/import.test.mjs', 'tests/Ranvier.Css.Compile.Tests/Tests.fs', 'src/Ranvier.View/Template.fs', 'src/Ranvier.View.Anime/Motion.fs', 'src/Fable.Ranvier.Anime/Css.fs', 'fable/Ranvier.Anime.Tests/CssChecks.fs', 'examples/Ranvier.Motion/src/App.fs', 'docs/samples/css-anime/Example.fs']) {
    assert.deepEqual(servicesFor([file], 'pull_request'), none, file);
  }
});

test('each database adapter change runs its owning service check', () => {
  for (const [backend, files] of Object.entries({
    marten: ['adapters/Ranvier.Persistence.Marten/MartenStateStore.fs', 'tests/Ranvier.Persistence.Marten.Tests/Tests.fs'],
    kurrentdb: ['adapters/Ranvier.Persistence.KurrentDB/KurrentDBStateStore.fs', 'tests/Ranvier.Persistence.KurrentDB.Tests/Tests.fs'],
    equinox: ['samples/Ranvier.Persistence.Equinox/State.fs', 'tests/Ranvier.Persistence.Equinox.Tests/Tests.fs'],
  })) for (const file of files) assert.deepEqual(servicesFor([file], 'pull_request'), { ...none, [backend]: true }, file);
  assert.deepEqual(servicesFor(['adapters/Ranvier.Persistence.Marten/MartenStateStore.fs', 'tests/Ranvier.Persistence.KurrentDB.Tests/Tests.fs'], 'pull_request'), { ...all, equinox: false });
});

test('core, shared infrastructure, unknown paths and unavailable diffs run every service', () => {
  for (const paths of [['src/Ranvier/Graph.fs'], ['src/Ranvier.Persistence/State.fs'], ['src/Ranvier.Wire/Codec.fs'], ['Directory.Build.props'], ['src/Directory.Build.targets'], ['.config/dotnet-tools.json'], ['global.json'], ['build.fsx'], ['.github/workflows/ci.yml'], ['.github/scripts/changes.cjs'], ['new-library/Thing.fs'], [], undefined, ['src/Ranvier.Css/Values.fs', 'src/Ranvier/Graph.fs']]) {
    assert.deepEqual(servicesFor(paths, 'pull_request'), all, JSON.stringify(paths));
  }
});

test('master and manual source runs retain full database coverage', () => {
  for (const event of ['push', 'workflow_dispatch']) {
    assert.deepEqual(servicesFor(['src/Ranvier.Css/Values.fs'], event), all, event);
    assert.deepEqual(servicesFor([], event), all, event);
    assert.deepEqual(servicesFor(['docs/content/guide/memos.md'], event), none, event);
  }
});

test('compiled CSS documentation is checked rather than classified as prose', () => {
  for (const file of ['docs/content/view/css.md', 'docs/content/view/css-build.md', 'docs/content/view/css-utilities.md', 'docs/samples/css/verify.mjs', 'docs/samples/css-anime/Example.fs']) {
    assert.equal(docsOnly([file]), false, file);
    assert.deepEqual(servicesFor([file], 'pull_request'), none, file);
  }
  assert.equal(docsOnly(['docs/content/guide/memos.md']), true);
});
