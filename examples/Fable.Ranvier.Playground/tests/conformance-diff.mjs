// Compares the conformance artefacts of the jsdom and twin runs file by file (spec §6.3); exits 1 at the first
// difference with a unified diff. A file on one side only is a difference.
// Usage: node tests/conformance-diff.mjs [jsdomDir] [twinDir]
import { readdirSync, readFileSync, statSync, existsSync } from "node:fs";
import { join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";

const files = root => {
  if (!existsSync(root)) return [];
  const result = [];
  const walk = d => { for (const n of readdirSync(d)) { const p = join(d, n); if (statSync(p).isDirectory()) walk(p); else result.push(relative(root, p).split(sep).join("/")); } };
  walk(root);
  return result;
};

const unified = (left, right) => {
  const a = left.split("\n"), b = right.split("\n"), out = [];
  let i = 0;
  while (i < a.length && i < b.length && a[i] === b[i]) i++;
  out.push(`@@ line ${i + 1} @@`);
  for (let k = Math.max(0, i - 2); k < i; k++) out.push(` ${a[k]}`);
  for (let k = i; k < Math.min(a.length, i + 5); k++) out.push(`-${a[k]}`);
  for (let k = i; k < Math.min(b.length, i + 5); k++) out.push(`+${b[k]}`);
  return out.join("\n");
};

/** The first file that differs between the two artefact trees, with its diff, or null when they are equal. */
export function compareTrees(left, right) {
  const names = [...new Set([...files(left), ...files(right)])].sort();
  for (const name of names) {
    const l = existsSync(join(left, name)) ? readFileSync(join(left, name), "utf8") : null;
    const r = existsSync(join(right, name)) ? readFileSync(join(right, name), "utf8") : null;
    if (l === null) return { file: name, diff: "missing under jsdom" };
    if (r === null) return { file: name, diff: "missing on the twin" };
    if (l !== r) return { file: name, diff: unified(l, r) };
  }
  return null;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const base = new URL("../", import.meta.url);
  const left = process.argv[2] ?? fileURLToPath(new URL("generated-conformance/jsdom", base));
  const right = process.argv[3] ?? fileURLToPath(new URL("generated-conformance/twin", base));
  const result = compareTrees(left, right);
  if (result) { console.error(`${result.file} differs\n${result.diff}`); process.exitCode = 1; }
  else console.log(`${files(left).length} artefacts equal`);
}
