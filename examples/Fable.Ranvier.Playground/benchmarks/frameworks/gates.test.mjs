import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { defaultAllow, diffDirectories } from "./generated-diff.mjs";

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
  const branch = tree({ "Mounting.js": "export const x = 2;\n", "experiments/Ranvier.View.Portable/Native.js": "export {};\n" });
  const result = diffDirectories(base, branch, defaultAllow);
  assert.deepEqual(result.differing.sort(), ["Mounting.js", "experiments/Ranvier.View.Portable/Native.js"]);
  assert.deepEqual(result.disallowed, ["Mounting.js"]);
});

test("fable_modules is skipped", () => {
  const base = tree({ "Mounting.js": "x\n" });
  const branch = tree({ "Mounting.js": "x\n", "fable_modules/fable-library-js/List.js": "y\n" });
  assert.deepEqual(diffDirectories(base, branch, defaultAllow).differing, []);
});

test("a Native.js outside Portable is disallowed", () => {
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
