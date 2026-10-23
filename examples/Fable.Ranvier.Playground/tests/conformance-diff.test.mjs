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

test("two empty trees are a difference: no artefacts were compared", () => {
  const result = compareTrees(tree({}), tree({}));
  assert.match(result.diff, /no artefacts/);
});

test("bytes that decode alike still differ", () => {
  const root = name => { const r = tree({}); mkdirSync(join(r, "M", "c"), { recursive: true }); return r; };
  const left = root(), right = root();
  writeFileSync(join(left, "M", "c", "01-x.txt"), Buffer.from([0xff]));
  writeFileSync(join(right, "M", "c", "01-x.txt"), Buffer.from([0xfe]));
  assert.equal(compareTrees(left, right).file, "M/c/01-x.txt");
});

test("a platform-only case's artefacts are compared on its own host only", () => {
  assert.equal(compareTrees(tree({ "M/jsdom-a-case-patches-x/01-x.txt": "a", "M/c/01-x.txt": "b" }), tree({ "M/c/01-x.txt": "b" })), null);
});
