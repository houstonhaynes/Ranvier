import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { defaultAllow, diffDirectories, expectedChangesFromArgs, parseExpectedChange } from "./generated-diff.mjs";
import { inspectionFailures } from "./inspection-presence.mjs";

const inspectBundle = "//#region ../../src/Ranvier.View.Dom/Debug.js\nconst tag = InstanceProbe;\n";
test("inspection absence and positive control reject opposite bundles", () => {
  assert.equal(inspectionFailures(inspectBundle).length, 2);
  assert.deepEqual(inspectionFailures("export const x = 1;"), []);
  assert.deepEqual(inspectionFailures(inspectBundle, true), []);
  assert.equal(inspectionFailures("export const x = 1;", true).length, 2);
});

test("positive control fails if either detector loses its marker", () => {
  assert.equal(inspectionFailures("const tag = InstanceProbe;", true).length, 1);
  assert.equal(inspectionFailures("//#region src/Ranvier.View.Dom/Inspect.js\n", true).length, 1);
});

const tree = files => {
  const root = mkdtempSync(join(tmpdir(), "gates-"));
  for (const [path, text] of Object.entries(files)) {
    mkdirSync(join(root, path, ".."), { recursive: true });
    writeFileSync(join(root, path), text);
  }
  return root;
};

test("identical trees differ nowhere", () => {
  const files = { "a/Mounting.js": "export const x = 1;\n" };
  assert.deepEqual(diffDirectories(tree(files), tree(files), defaultAllow), { differing: [], disallowed: [] });
});

test("import lines are ignored", () => {
  const base = tree({ "Mounting.js": 'import { a } from "./A.js";\nexport const x = 1;\n' });
  const branch = tree({ "Mounting.js": 'import { a } from "./A.js";\nimport { b } from "./Native.js";\nexport const x = 1;\n' });
  assert.deepEqual(diffDirectories(base, branch, defaultAllow).differing, []);
});

test("a changed body is disallowed and a new Native.js is allowed", () => {
  const base = tree({ "Mounting.js": "export const x = 1;\n" });
  const branch = tree({ "Mounting.js": "export const x = 2;\n", "src/Ranvier.View/Native.js": "export {};\n" });
  const result = diffDirectories(base, branch, defaultAllow);
  assert.deepEqual(result.differing.sort(), ["Mounting.js", "src/Ranvier.View/Native.js"]);
  assert.deepEqual(result.disallowed, ["Mounting.js"]);
});

test("fable_modules is skipped", () => {
  const base = tree({ "Mounting.js": "x\n" });
  const branch = tree({ "Mounting.js": "x\n", "fable_modules/fable-library-js/List.js": "y\n" });
  assert.deepEqual(diffDirectories(base, branch, defaultAllow).differing, []);
});

test("a Native.js outside Ranvier.View is disallowed", () => {
  const result = diffDirectories(tree({}), tree({ "tests/Native.js": "x\n" }), defaultAllow);
  assert.deepEqual(result.disallowed, ["tests/Native.js"]);
});

test("a file deleted on the branch is reported", () => {
  const result = diffDirectories(tree({ "Mounting.js": "x\n" }), tree({}), defaultAllow);
  assert.deepEqual(result.disallowed, ["Mounting.js"]);
});

test("carriage returns are ignored", () => {
  const result = diffDirectories(tree({ "Mounting.js": "a\r\nb\r\n" }), tree({ "Mounting.js": "a\nb\n" }), defaultAllow);
  assert.deepEqual(result.differing, []);
});

test("an expected change permits only its complete replacement", () => {
  const expected = { name: "Mounting.js", hunks: [{ before: "activate(record);", after: "try { activate(record); } catch (e) { fail(e); }" }] };
  const base = tree({ "Mounting.js": "activate(record);\nunchanged();\n" });
  const right = "try { activate(record); } catch (e) { fail(e); }\nunchanged();\n";
  assert.deepEqual(diffDirectories(base, tree({ "Mounting.js": right }), defaultAllow, [expected]).disallowed, []);
  assert.deepEqual(diffDirectories(base, tree({ "Mounting.js": right + "extra();\n" }), /.*/, [expected]).disallowed, ["Mounting.js"]);
});

test("a baseline already holding the expected change is rejected", () => {
  const expected = { name: "Mounting.js", hunks: [{ before: "before();", after: "after();" }] };
  const patched = tree({ "Mounting.js": "after();\n" });
  assert.deepEqual(diffDirectories(patched, patched, /.*/, [expected]).disallowed, ["Mounting.js"]);
});

test("each explicit expected file is checked, even under a broad allow pattern", () => {
  const directory = tree({
    "one.diff": "--- a/One.js\n+++ b/One.js\n@@ -1 +1 @@\n-beforeOne();\n+afterOne();\n",
    "two.diff": "--- a/Two.js\n+++ b/Two.js\n@@ -1 +1 @@\n-beforeTwo();\n+afterTwo();\n",
  });
  const args = ["--expected-hunk", join(directory, "one.diff"), "--expected-hunk", join(directory, "two.diff")];
  const changes = expectedChangesFromArgs(args);
  const base = tree({ "One.js": "beforeOne();\n", "Two.js": "beforeTwo();\n" });
  const branch = tree({ "One.js": "afterOne();\n", "Two.js": "afterTwo();\n" });
  assert.deepEqual(diffDirectories(base, branch, /.*/, changes).disallowed, []);
  writeFileSync(join(branch, "Two.js"), "afterTwo();\nextra();\n");
  assert.deepEqual(diffDirectories(base, branch, /.*/, changes).disallowed, ["Two.js"]);
  assert.throws(() => expectedChangesFromArgs(["--expected-hunk"]), /requires a file/);
  assert.throws(() => expectedChangesFromArgs([...args, "--expected-hunk", join(directory, "one.diff")]), /distinct files/);
});

test("the production manifest restricts every named file to exact hunks", () => {
  const changes = expectedChangesFromArgs([]);
  assert.deepEqual(changes.map(change => change.name).sort(), [
    "src/Ranvier.View.Dom/Mounting.js", "src/Ranvier/Api.js", "src/Ranvier/Bindings.js",
    "src/Ranvier/Core.js", "src/Ranvier/Projections.js", "src/Ranvier/Streams.js", "src/Ranvier/Timed.js",
  ]);
  for (const expected of changes) {
    const before = expected.hunks.map(h => h.before).join("\nseparator\n");
    const after = expected.hunks.map(h => h.after).join("\nseparator\n");
    const base = tree({ [expected.name]: before });
    const branch = tree({ [expected.name]: after });
    assert.deepEqual(diffDirectories(base, branch, defaultAllow, [expected]).disallowed, []);
    writeFileSync(join(branch, expected.name), after + "\nextra();");
    assert.deepEqual(diffDirectories(base, branch, /.*/, [expected]).disallowed, [expected.name]);
  }
});

test("the committed placement exception applies every hunk and rejects an ambiguous base", () => {
  const patch = readFileSync(new URL("../../../../docs/research/2026-10-21-dom-twin/i1-mounting.diff", import.meta.url), "utf8");
  const expected = parseExpectedChange(patch);
  assert.equal(expected.name, "src/Ranvier.View.Dom/Mounting.js");
  assert.equal(expected.hunks.length, 2);
  const before = expected.hunks.map(h => h.before).join("\nseparator\n");
  const after = expected.hunks.map(h => h.after).join("\nseparator\n");
  const branch = tree({ [expected.name]: after });
  assert.deepEqual(diffDirectories(tree({ [expected.name]: before }), branch, defaultAllow, [expected]).disallowed, []);
  assert.deepEqual(diffDirectories(tree({ [expected.name]: before + before }), branch, /.*/, [expected]).disallowed, [expected.name]);
  assert.throws(() => parseExpectedChange(patch.replace("+++ b/", "+++ b/wrong/")), /matching paths/);
});
