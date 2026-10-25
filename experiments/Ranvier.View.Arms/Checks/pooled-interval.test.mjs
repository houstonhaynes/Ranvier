// The per-operation Chromium budgets of chromium-gate.mjs against the committed runs whose sources match the tree (plan
// 2026-10-24-issue-157.md, Decision P9). Usage: node --test experiments/Ranvier.View.Arms/Checks/pooled-interval.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { committed, outOfDate, pairsOf, interval, pooled, runs } from "./pooled-interval.mjs";
import { hashedSources, lineEndingHashes } from "../../../examples/Fable.Ranvier.Playground/benchmarks/frameworks/run-sources.mjs";
import { budgets, judge } from "./chromium-gate.mjs";

const operations = ["armsCreate1k", "armsSwitch1k", "armsPayload1k", "armsClear1k"];

test("a single run's interval equals the one keyed-rows.mjs recorded", () => {
  for (const run of committed.map(r => r.read()))
    for (const recorded of run.pairRatios.filter(r => r.numerator === "ranvier-arms-match" && r.denominator === "ranvier-sugar")) {
      const { median, ci95, pairs } = interval(pairsOf(run, recorded.operation));
      assert.equal(pairs, recorded.pairs);
      assert.equal(median, recorded.medianRatio);
      assert.deepEqual(ci95, recorded.ci95);
    }
});

test("the budgets are fixed", () => {
  assert.deepEqual(budgets, { armsCreate1k: 1.05, armsSwitch1k: 1.04, armsPayload1k: 1.07, armsClear1k: 1.05 });
});

test("every pooled median is within its budget", () => {
  for (const operation of operations) assert.ok(pooled(operation).median <= budgets[operation], operation);
});

test("the gate judges the pooled median and interval", () => {
  for (const operation of operations) {
    const all = pooled(operation);
    const verdict = judge(operation);
    assert.equal(verdict.median, all.median, operation);
    assert.deepEqual(verdict.ci95, all.ci95, operation);
    assert.equal(verdict.ok, all.median <= budgets[operation], operation);
  }
});

test("every committed result with ranvier-arms-match and ranvier-sugar samples is listed", () => {
  const directory = new URL("../../../docs/research/arms-match/", import.meta.url);
  const paired = readdirSync(directory).filter(name => name.endsWith(".json")).filter(name => {
    const samples = JSON.parse(readFileSync(new URL(name, directory), "utf8")).samples ?? [];
    return ["ranvier-arms-match", "ranvier-sugar"].every(mode => samples.some(s => s.mode === mode && s.operation === "armsCreate1k"));
  });
  const listed = committed.map(r => r.path);
  for (const name of paired) assert.ok(listed.includes(`docs/research/arms-match/${name}`), name);
});

test("the pool is the committed runs whose recorded sources match the tree", () => {
  for (const run of committed) assert.equal(runs.includes(run), outOfDate(run).length === 0, run.path);
});

const treeHashes = () => Object.fromEntries(hashedSources.map(path =>
  [path, lineEndingHashes(readFileSync(new URL(`../../../examples/Fable.Ranvier.Playground/${path}`, import.meta.url)))[0]]));
const runOf = sourceHashes => ({ path: "synthetic.json", read: () => ({ sourceHashes }) });

test("a run whose recorded hashes differ from the tree only in src/Ranvier/Core.fs stays in the pool", () => {
  assert.deepEqual(outOfDate(runOf({ ...treeHashes(), "../../src/Ranvier/Core.fs": "0" })), []);
});

test("a run whose recorded hash of experiments/Ranvier.View.Arms/Site.fs differs from the tree leaves the pool", () => {
  assert.deepEqual(outOfDate(runOf({ ...treeHashes(), "../../experiments/Ranvier.View.Arms/Site.fs": "0" })), ["../../experiments/Ranvier.View.Arms/Site.fs"]);
});

test("the runs at 22d86d44, which differ from the tree only in src/Ranvier/Core.fs, are in the pool", () => {
  for (const n of [1, 2, 3, 4, 5, 6]) assert.ok(runs.some(r => r.path === `docs/research/arms-match/arms-match-22d86d44-${n}.json`), String(n));
});

test("the runs recorded before the rebase onto aae54f2e are out of the pool", () => {
  for (const path of ["docs/.ai/benchmarks/arms-157-noisy-probe.json", "docs/research/arms-match/arms-match-299ab276.json", "docs/research/arms-match/arms-match-shape.json",
    "docs/research/arms-match/arms-match-dc8b3d5b.json", "docs/research/arms-match/arms-match-5369d6a0.json", "docs/research/arms-match/arms-match.json"]) {
    const run = committed.find(r => r.path === path);
    assert.ok(run, path);
    assert.ok(outOfDate(run).includes("../../experiments/Ranvier.View.Arms.FablePlugin/Plugin.fs"), path);
    assert.ok(!runs.includes(run), path);
  }
});

test("the runs recorded before keyed-rows.mjs built and hashed its own bundle are out of the pool", () => {
  for (const n of [1, 2, 3, 4, 5]) {
    const run = committed.find(r => r.path === `docs/research/arms-match/arms-match-da6d32e6-${n}.json`);
    assert.ok(run, String(n));
    assert.ok(outOfDate(run).includes("benchmarks/frameworks/keyed-rows.mjs"), String(n));
    assert.ok(!runs.includes(run), String(n));
  }
});

test("every pooled run built the bundle it measured and records the bundle's hash", () => {
  for (const run of runs) {
    const data = run.read();
    assert.equal(data.built, true, run.path);
    assert.match(data.bundleHash ?? "", /^[0-9a-f]{64}$/, run.path);
  }
});

test("the pool holds at least four runs of 45 pairs per operation", () => {
  assert.ok(runs.length >= 4, `${runs.length} runs`);
  for (const operation of operations) assert.equal(pooled(operation).pairs, 45 * runs.length, operation);
});
