import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { compareTrees } from "./conformance-diff.mjs";

const tree = files => {
  const root = mkdtempSync(join(tmpdir(), "conf-"));
  for (const [path, text] of Object.entries(files)) { mkdirSync(join(root, path, ".."), { recursive: true }); writeFileSync(join(root, path), text); }
  return root;
};

test("equal trees compare equal", () => {
  const files = { "M/case/01-click.txt": "<b>1</b>" };
  assert.equal(compareTrees(tree(files), tree(files)), null);
});

test("a differing byte reports the file and a diff", () => {
  const result = compareTrees(tree({ "M/c/01-x.txt": "a\nb\n" }), tree({ "M/c/01-x.txt": "a\nc\n" }));
  assert.equal(result.file, "M/c/01-x.txt");
  assert.match(result.diff, /^-b$/m);
  assert.match(result.diff, /^\+c$/m);
});

test("a file present on one host only is a difference", () => {
  const result = compareTrees(tree({ "M/c/01-x.txt": "a" }), tree({}));
  assert.equal(result.file, "M/c/01-x.txt");
  assert.match(result.diff, /missing on the twin/);
});
