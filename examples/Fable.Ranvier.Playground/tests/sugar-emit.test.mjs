// Criterion 4: the emitted sugar modules contain no curried-call helpers.
import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const curry = /\bcurry[0-9]*\(|\buncurry[0-9]*\(|\bpartialApply\(/g;
for (const file of ["TemplateSugar.js", "Generated/HtmlTemplateSugar.js"])
  test(`${file} has no curry, uncurry or partialApply`, () => {
    const source = readFileSync(new URL(`../generated-tests/src/Ranvier.View/${file}`, import.meta.url), "utf8");
    assert.deepEqual(source.match(curry) ?? [], []);
  });
