// Usage: node --test benchmarks/frameworks/run-sources.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { posix } from "node:path";
import { fileURLToPath } from "node:url";
import { bundleHash, hashedSources, lineEndingHashes, mismatchedSources, pooledRunProblems, staleSources, uncommittedOf, withLock } from "./run-sources.mjs";

const plugin = "../../experiments/Ranvier.View.Arms.FablePlugin/";

test("every file compiled into the Fable plugin, and its project, is hashed", () => {
  const project = readFileSync(new URL(`../../${plugin}Ranvier.View.Arms.FablePlugin.fsproj`, import.meta.url), "utf8");
  const compiled = [...project.matchAll(/<Compile Include="([^"]+)"/g)].map(m => posix.normalize(plugin + m[1]));
  assert.ok(compiled.length >= 4, "the plugin's Compile items");
  for (const path of [...compiled, `${plugin}Ranvier.View.Arms.FablePlugin.fsproj`]) assert.ok(hashedSources.includes(path), path);
});

test("a recorded hash matches its source under LF and CRLF line endings", () => {
  const lf = Buffer.from("a\nb\n"), crlf = Buffer.from("a\r\nb\r\n");
  assert.deepEqual(lineEndingHashes(lf).slice(1), lineEndingHashes(crlf).slice(1));
  assert.ok(lineEndingHashes(crlf).includes(lineEndingHashes(lf)[0]));
});

test("a run matches the tree when it records every hashed source with the source's hash", () => {
  const read = path => Buffer.from(`source of ${path}\n`);
  const recorded = Object.fromEntries(hashedSources.map(path => [path, lineEndingHashes(read(path))[2]]));
  assert.deepEqual(mismatchedSources(recorded, read), []);
  const { [hashedSources.at(-1)]: _, ...missing } = recorded;
  assert.deepEqual(mismatchedSources(missing, read), [hashedSources.at(-1)]);
  assert.deepEqual(mismatchedSources({ ...recorded, [hashedSources[0]]: "0" }, read), [hashedSources[0]]);
});

test("sources unchanged through the run and committed are current", () => {
  assert.deepEqual(staleSources({ before: { a: "1", b: "2" }, after: { a: "1", b: "2" }, uncommitted: [] }), []);
});

test("a source edited during the run is stale", () => {
  assert.deepEqual(staleSources({ before: { a: "1", b: "2" }, after: { a: "1", b: "3" }, uncommitted: [] }), ["b"]);
});

test("an uncommitted source is stale", () => {
  assert.deepEqual(staleSources({ before: { a: "1", b: "2" }, after: { a: "1", b: "2" }, uncommitted: ["a"] }), ["a"]);
});

test("a committed, unmodified file is not uncommitted", () => {
  const cwd = fileURLToPath(new URL("../../", import.meta.url));
  assert.deepEqual(uncommittedOf(cwd, ["benchmarks/frameworks/keyed-page.mjs"]), []);
});

test("a bundle's hash covers every file's path and bytes", () => {
  const dir = mkdtempSync(join(tmpdir(), "bundle-"));
  try {
    mkdirSync(join(dir, "assets"));
    writeFileSync(join(dir, "index.html"), "<p>");
    writeFileSync(join(dir, "assets", "a.js"), "a");
    const first = bundleHash(dir);
    assert.equal(bundleHash(dir), first);
    writeFileSync(join(dir, "assets", "a.js"), "b");
    const edited = bundleHash(dir);
    assert.notEqual(edited, first);
    writeFileSync(join(dir, "assets", "b.js"), "");
    assert.notEqual(bundleHash(dir), edited);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("a pooled run is built by the run and serves the bundle it built", () => {
  assert.deepEqual(pooledRunProblems({ pooled: false, build: false }), []);
  assert.deepEqual(pooledRunProblems({ pooled: true, build: true, bundleBefore: "1", bundleAfter: "1" }), []);
  assert.equal(pooledRunProblems({ pooled: true, build: false }).length, 1);
  assert.equal(pooledRunProblems({ pooled: true, build: true, bundleBefore: "1", bundleAfter: "2" }).length, 1);
});

test("a lock admits one holder at a time", async () => {
  const lock = join(mkdtempSync(join(tmpdir(), "lock-")), "build.lock");
  const order = [];
  const hold = name => withLock(lock, async () => { order.push(`${name} in`); await new Promise(r => setTimeout(r, 50)); order.push(`${name} out`); }, 10);
  await Promise.all([hold("a"), hold("b")]);
  assert.deepEqual(order.map(s => s.split(" ")[1]), ["in", "out", "in", "out"]);
});